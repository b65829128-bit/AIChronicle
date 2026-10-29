#if NET472
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CefSharp;
using CefSharp.Handler;
using CefSharp.OffScreen;

namespace AIChronicle
{
    /// <summary>
    /// CefSharp 生命周期与路径配置。CEF 的 Initialize/Shutdown 必须与游戏主线程一致；
    /// 这里只 Initialize 一次，浏览器实例由 CefWebSession 管理。
    /// </summary>
    public static class CefWebHost
    {
        private static bool _initialized;
        private static bool _failed;
        private static string _lastError = "";

        public static bool IsInitialized => _initialized;
        public static string LastError => _lastError;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        public static bool EnsureInitialized()
        {
            if (_initialized)
                return true;
            if (_failed)
                return false;

            try
            {
                string baseDir = Path.GetFullPath(Path.GetDirectoryName(typeof(CefWebHost).Assembly.Location)
                                 ?? AppDomain.CurrentDomain.BaseDirectory);
                UiTrace.Log("CefWebHost baseDir=" + baseDir);
                try { SetDllDirectory(baseDir); } catch { }

                string subprocess = Path.Combine(baseDir, "CefSharp.BrowserSubprocess.exe");
                UiTrace.Log("CefWebHost subprocess exists=" + File.Exists(subprocess) + " path=" + subprocess);
                if (!File.Exists(subprocess))
                {
                    _failed = true;
                    _lastError = "找不到 CefSharp.BrowserSubprocess.exe";
                    return false;
                }

                string campaign = PromptManager.CampaignDir;
                if (string.IsNullOrEmpty(campaign))
                    campaign = Path.Combine(Path.GetTempPath(), "AIChronicleCef");
                // CEF 的 RootCachePath 必须是绝对路径。
                string campaignFull = Path.GetFullPath(campaign);
                string cache = Path.Combine(campaignFull, "WebCache", "cef");
                string logs = Path.Combine(campaignFull, "debug_logs");
                Directory.CreateDirectory(cache);
                Directory.CreateDirectory(logs);

                var settings = new CefSettings
                {
                    BrowserSubprocessPath = subprocess,
                    ResourcesDirPath = baseDir,
                    LocalesDirPath = Path.Combine(baseDir, "locales"),
                    CachePath = cache,
                    RootCachePath = cache,
                    LogFile = Path.Combine(logs, "cef.log"),
                    LogSeverity = LogSeverity.Info,
                    WindowlessRenderingEnabled = true,
                    MultiThreadedMessageLoop = true,
                    Locale = "zh-CN"
                };

                // Proton/DXVK 下优先软件渲染；OffScreen 的推荐开关。
                settings.SetOffScreenRenderingBestPerformanceArgs();
                settings.CefCommandLineArgs.Add("no-sandbox", "1");

                UiTrace.Log("Cef.Initialize start");
                _initialized = Cef.Initialize(settings, performDependencyCheck: true);
                UiTrace.Log("Cef.Initialize result=" + _initialized);
                if (!_initialized)
                {
                    _failed = true;
                    _lastError = "Cef.Initialize 返回 false";
                }

                return _initialized;
            }
            catch (Exception ex)
            {
                _failed = true;
                _lastError = ex.Message;
                UiTrace.Log("CefWebHost failed: " + ex);
                return false;
            }
        }
    }

    /// <summary>一个 OffScreen CEF 浏览器；Paint 回调把 BGRA 帧交给调用方。</summary>
    public sealed class CefWebSession : IDisposable
    {
        private ChromiumWebBrowser? _browser;
        private BrowserSettings? _browserSettings;
        private bool _disposed;
        private string _url = "";
        private bool _firstFrameLogged;
        private int _viewportHeight;
        private DateTime _lastWheelAt;
        private int _lastWheelDirection;
        private float _wheelVelocity;

        public event Action<byte[], int, int>? FrameReady;
        public event Action<string>? Status;
        /// <summary>主 frame 加载完成（CEF UI 线程）。此时才能可靠执行 JS。</summary>
        public event Action? PageLoaded;

        public bool Start(string url, int width, int height, int fps, object? bridge = null)
        {
            if (!CefWebHost.EnsureInitialized())
            {
                Status?.Invoke("CEF 初始化失败：" + CefWebHost.LastError);
                return false;
            }

            try
            {
                _url = url;
                _viewportHeight = height;
                _browserSettings = new BrowserSettings
                {
                    WindowlessFrameRate = fps,
                    BackgroundColor = 0xFF1C1814
                };

                _browser = new ChromiumWebBrowser(string.Empty, _browserSettings, null,
                    automaticallyCreateBrowser: false);
                _browser.Size = new System.Drawing.Size(width, height);
                _browser.DeviceScaleFactor = 1f;
                _browser.RequestHandler = new LocalOnlyRequestHandler();
                _browser.Paint += OnPaint;
                _browser.BrowserInitialized += OnBrowserInitialized;
                _browser.LoadError += OnLoadError;
                _browser.FrameLoadEnd += OnFrameLoadEnd;

                if (bridge != null)
                {
                    _browser.JavascriptObjectRepository.Register(
                        "aiChronicle", bridge, isAsync: true, options: BindingOptions.DefaultBinder);
                    UiTrace.Log("CefWebSession bridge registered: aiChronicle");
                }

                _browser.CreateBrowser(null, _browserSettings);

                UiTrace.Log($"CefWebSession.Start size={width}x{height} fps={fps} url={url}");
                Status?.Invoke("CEF 浏览器启动中…");
                return true;
            }
            catch (Exception ex)
            {
                UiTrace.Log("CefWebSession.Start failed: " + ex);
                Status?.Invoke("CEF 启动失败：" + ex.Message);
                Dispose();
                return false;
            }
        }

        private void OnBrowserInitialized(object? sender, EventArgs e)
        {
            try
            {
                UiTrace.Log("CefWebSession.BrowserInitialized");
                Status?.Invoke("CEF 页面加载中…");
                _browser?.LoadUrl(_url);
            }
            catch (Exception ex)
            {
                UiTrace.Log("CefWebSession.LoadUrl failed: " + ex);
                Status?.Invoke("CEF 加载失败：" + ex.Message);
            }
        }

        private void OnLoadError(object? sender, LoadErrorEventArgs e)
        {
            UiTrace.Log($"CefWebSession.LoadError {e.ErrorCode} {e.ErrorText} url={e.FailedUrl}");
            Status?.Invoke("CEF 加载错误：" + e.ErrorText);
        }

        private void OnFrameLoadEnd(object? sender, FrameLoadEndEventArgs e)
        {
            try
            {
                if (e.Frame == null || !e.Frame.IsMain)
                    return;

                UiTrace.Log("CefWebSession main frame loaded url=" + e.Url + " status=" + e.HttpStatusCode);
                Status?.Invoke("CEF 页面加载完成");
                PageLoaded?.Invoke();
            }
            catch (Exception ex)
            {
                UiTrace.Log("CefWebSession.OnFrameLoadEnd failed: " + ex);
            }
        }

        private void OnPaint(object? sender, OnPaintEventArgs e)
        {
            if (_disposed || e.IsPopup || e.Width <= 0 || e.Height <= 0)
                return;

            try
            {
                int width = e.Width;
                int height = e.Height;
                var buffer = new byte[width * height * 4];
                Marshal.Copy(e.BufferHandle, buffer, 0, buffer.Length);

                // CEF OnPaint 是 BGRA；引擎的 CreateFromByteArray 按 RGBA 解释，必须交换 R/B。
                for (int i = 0; i < buffer.Length; i += 4)
                {
                    byte b = buffer[i];
                    buffer[i] = buffer[i + 2];
                    buffer[i + 2] = b;
                }

                if (!_firstFrameLogged)
                {
                    _firstFrameLogged = true;
                    UiTrace.Log($"CefWebSession first frame {width}x{height}");
                    Status?.Invoke("CEF 首帧已到达");
                }

                FrameReady?.Invoke(buffer, width, height);
            }
            catch (Exception ex)
            {
                UiTrace.Log("CefWebSession.OnPaint failed: " + ex);
            }
        }

        public Task<JavascriptResponse> EvaluateScriptAsync(string script)
        {
            if (_browser == null)
                return Task.FromResult(new JavascriptResponse { Success = false, Message = "browser null" });
            return _browser.EvaluateScriptAsync(script);
        }

        public void SendMouseMove(int x, int y)
        {
            try
            {
                var host = GetHost();
                host?.SendMouseMoveEvent(new MouseEvent(x, y, CefEventFlags.None), false);
            }
            catch (Exception ex) { UiTrace.Log("CefWebSession.SendMouseMove failed: " + ex.Message); }
        }

        public void SendMouseDown(int x, int y)
        {
            try
            {
                var host = GetHost();
                if (host == null) return;
                host.SetFocus(true);
                host.SendMouseClickEvent(new MouseEvent(x, y, CefEventFlags.None), MouseButtonType.Left, false, 1);
            }
            catch (Exception ex) { UiTrace.Log("CefWebSession.SendMouseDown failed: " + ex.Message); }
        }

        public void SendMouseUp(int x, int y)
        {
            try
            {
                var host = GetHost();
                host?.SendMouseClickEvent(new MouseEvent(x, y, CefEventFlags.None), MouseButtonType.Left, true, 1);
            }
            catch (Exception ex) { UiTrace.Log("CefWebSession.SendMouseUp failed: " + ex.Message); }
        }

        public void SendMouseWheel(int x, int y, int delta)
        {
            if (delta == 0 || _viewportHeight <= 0)
                return;

            try
            {
                var host = GetHost();
                if (host == null)
                    return;
                host.SetFocus(true);

                var now = DateTime.UtcNow;
                int direction = -Math.Sign(delta); // 页面滚动方向与 Gauntlet 原始 delta 相反
                double gap = _lastWheelAt == default
                    ? 0.05
                    : Math.Max(0.0, Math.Min(0.20, (now - _lastWheelAt).TotalSeconds));
                bool newBurst = _lastWheelAt == default
                    || direction != _lastWheelDirection
                    || (now - _lastWheelAt).TotalSeconds > 0.18;

                // 一次滚动量按 viewport 高度归一，而不是写死像素：
                // 原始 ±120 视为一格；触控板的小 delta 按比例缩小。
                float notches = Math.Max(0.1f, Math.Min(3f, Math.Abs(delta) / 120f));
                float targetVelocity = direction * _viewportHeight * 1.6f * notches;

                if (newBurst)
                    _wheelVelocity = 0f;

                // 指数逼近目标速度：起手有冲量，连续滚动时积累速度，不会一格直接到底。
                _wheelVelocity += (targetVelocity - _wheelVelocity) * 0.45f;
                float dt = (float)Math.Max(0.008, Math.Min(0.05, gap));
                float step = _wheelVelocity * dt;
                if (newBurst)
                    step += direction * _viewportHeight * 0.08f * notches;

                _lastWheelAt = now;
                _lastWheelDirection = direction;

                int pixels = (int)Math.Round(step);
                if (pixels == 0)
                    pixels = direction;

                _browser?.EvaluateScriptAsync(
                    "if(window.__scrollBySmooth){window.__scrollBySmooth(" + pixels + "," + x + "," + y + ");}" +
                    "else{window.scrollBy({top:" + pixels + ",behavior:'smooth'});}");
                UiTrace.Log($"CefWebSession wheel delta={delta} notches={notches:F2} step={pixels} velocity={_wheelVelocity:F0}");
            }
            catch (Exception ex) { UiTrace.Log("CefWebSession.SendMouseWheel failed: " + ex.Message); }
        }

        private IBrowserHost? GetHost()
        {
            if (_browser == null || !_browser.IsBrowserInitialized)
                return null;
            return _browser.GetBrowser()?.GetHost();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            try
            {
                if (_browser != null)
                {
                    _browser.Paint -= OnPaint;
                    _browser.BrowserInitialized -= OnBrowserInitialized;
                    _browser.LoadError -= OnLoadError;
                    _browser.FrameLoadEnd -= OnFrameLoadEnd;
                    _browser.Dispose();
                }
            }
            catch (Exception ex)
            {
                UiTrace.Log("CefWebSession.Dispose failed: " + ex);
            }

            _browser = null;
            _browserSettings?.Dispose();
            _browserSettings = null;
            FrameReady = null;
            Status = null;
        }
    }

    /// <summary>只允许本地 file/about/data 页面；远程导航直接取消。</summary>
    internal sealed class LocalOnlyRequestHandler : RequestHandler
    {
        protected override bool OnBeforeBrowse(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame,
            IRequest request, bool userGesture, bool isRedirect)
        {
            return !IsLocal(request.Url);
        }

        protected override bool OnOpenUrlFromTab(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame,
            string targetUrl, WindowOpenDisposition targetDisposition, bool userGesture)
        {
            return !IsLocal(targetUrl);
        }

        private static bool IsLocal(string? url)
        {
            if (string.IsNullOrEmpty(url))
                return false;
            return url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>暴露给网页的 JS 对象。所有会碰游戏对象的方法都必须回主线程。</summary>
    public sealed class CefBridge
    {
        private readonly CefWebScreen _screen;

        public event Action<string, string>? ActionReceived;

        public CefBridge(CefWebScreen screen)
        {
            _screen = screen;
        }

        public string Ping(string message)
        {
            UiTrace.Log("CefBridge.Ping: " + message);
            return "pong: " + message;
        }

        public string GetPageInfo()
        {
            return "AIChronicle / CefSharp 140 / CEF";
        }

        public void Log(string message)
        {
            UiTrace.Log("CefBridge.Log: " + message);
        }

        /// <summary>网页动作入口：id + payloadJson；由 CefWebScreen.PageAction 转发。</summary>
        public void Dispatch(string id, string payload)
        {
            UiTrace.Log($"CefBridge.Dispatch id={id} payload={payload}");
            ActionReceived?.Invoke(id, payload ?? "");
        }

        public void Close()
        {
            MainThreadExecutor.Post(() =>
            {
                try { _screen.Close(); }
                catch (Exception ex) { UiTrace.Log("CefBridge.Close failed: " + ex); }
            });
        }
    }
}
#endif
