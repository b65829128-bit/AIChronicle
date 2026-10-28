using System;
using System.IO;
using System.Numerics;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.Library;
using TaleWorlds.TwoDimension;
using EngineTextureRaw = TaleWorlds.Engine.Texture;
using GauntletEngineTexture = TaleWorlds.Engine.GauntletUI.EngineTexture;
using TwoDimTexture = TaleWorlds.TwoDimension.Texture;

namespace AIChronicle
{
    /// <summary>用动态纹理构造的 Sprite；交给引擎的 BrushRenderer 绘制，避免自己拼 SimpleMaterial。</summary>
    internal sealed class KitDynamicSprite : Sprite
    {
        private readonly TwoDimTexture _texture;

        public KitDynamicSprite(TwoDimTexture texture, int width, int height)
            : base("KitDynamicSprite", width, height, SpriteNinePatchParameters.Empty)
        {
            _texture = texture;
        }

        public override TwoDimTexture Texture => _texture;

        public override Vec2 GetMinUvs() => Vec2.Zero;

        public override Vec2 GetMaxUvs() => Vec2.One;
    }

    /// <summary>
    /// 显示动态纹理的 Widget：不自己 OnRender，而是把纹理包成 Sprite 塞进 Brush，
    /// 由 Gauntlet 的 ImageWidget/BrushRenderer 走引擎标准绘制路径。
    /// </summary>
    public sealed class KitTextureWidget : ImageWidget
    {
        private TwoDimTexture? _pageTexture;
        private bool _loggedBrush;
        private int _viewWidth = 1;
        private int _viewHeight = 1;

        /// <summary>指针移动（已换算到 CEF view 坐标）。</summary>
        public event Action<int, int>? PointerMoved;
        /// <summary>鼠标按下/抬起；bool 为 true 表示按下。</summary>
        public event Action<int, int, bool>? PointerButton;
        /// <summary>滚轮；最后一个参数为 CEF wheel delta。</summary>
        public event Action<int, int, int>? PointerWheel;

        public KitTextureWidget(UIContext context) : base(context)
        {
            Brush = context.DefaultBrush ?? context.GetBrush("ButtonSimpleBrush") ?? new Brush();
            WidthSizePolicy = SizePolicy.StretchToParent;
            HeightSizePolicy = SizePolicy.Fixed;
            SuggestedHeight = 120f;
        }

        public void SetPageTexture(TwoDimTexture? texture, int width, int height)
        {
            _pageTexture = texture;
            _viewWidth = Math.Max(1, width);
            _viewHeight = Math.Max(1, height);
            bool sizeChanged = Math.Abs(SuggestedWidth - width) > 0.5f || Math.Abs(SuggestedHeight - height) > 0.5f;
            SuggestedWidth = width;
            SuggestedHeight = height;

            if (texture != null)
            {
                var brush = (Brush ?? Context.DefaultBrush ?? new Brush()).Clone();
                brush.Sprite = new KitDynamicSprite(texture, width, height);
                Brush = brush;
            }

            // 动画帧尺寸不变时不要每帧 dirty 整个布局，只重绘 brush。
            if (sizeChanged)
                SetMeasureAndLayoutDirty();
        }

        private EngineTextureRaw? _pendingTexture;
        private string _pendingKind = "";
        private DateTime _pendingDeadline;
        private bool _animate;
        private DateTime _lastFrameTime;
        private int _frameIndex;
        private int _rawFrameCount;
        private EngineTextureRaw? _activeEngineTexture;
        private const int MaxAnimationFrames = 150;

        public bool SetPng(byte[] png)
        {
            try
            {
                UiTrace.Log($"TextureWidget.SetPng bytes={png.Length}");
                var engineTexture = EngineTextureRaw.CreateFromMemory(png);
                if (engineTexture == null)
                {
                    UiTrace.Log("TextureWidget.SetPng failed: null texture");
                    return false;
                }

                return QueueEngineTexture(engineTexture, "png");
            }
            catch (Exception ex)
            {
                UiTrace.Log("TextureWidget.SetPng failed: " + ex);
                return false;
            }
        }

        public bool SetPngFile(string filePath)
        {
            try
            {
                string folder = Path.GetDirectoryName(filePath) ?? "";
                string fileName = Path.GetFileName(filePath);
                UiTrace.Log($"TextureWidget.SetPngFile file={fileName} folder={folder}");

                var engineTexture = EngineTextureRaw.LoadTextureFromPath(fileName, folder);
                if (engineTexture == null)
                {
                    UiTrace.Log("TextureWidget.SetPngFile failed: null texture");
                    return false;
                }

                return QueueEngineTexture(engineTexture, "file");
            }
            catch (Exception ex)
            {
                UiTrace.Log("TextureWidget.SetPngFile failed: " + ex);
                return false;
            }
        }

        public void SetRawBgra(byte[] data, int width, int height)
        {
            if (data == null || data.Length < width * height * 4)
                return;

            try
            {
                _rawFrameCount++;
                if (ShouldLogRawFrame())
                    UiTrace.Log($"TextureWidget.SetRawBgra start {width}x{height} bytes={data.Length}");
                var engineTexture = EngineTextureRaw.CreateFromByteArray(data, width, height);
                if (engineTexture != null)
                    QueueEngineTexture(engineTexture, "raw");
            }
            catch (Exception ex)
            {
                UiTrace.Log("TextureWidget.SetRawBgra failed: " + ex);
            }
        }

        /// <summary>
        /// 不在 UI 线程阻塞 PreloadTexture；若纹理尚未 loaded，挂起并逐帧轮询，
        /// 加载成功后才交给 BrushRenderer，避免把未上传纹理送去 GPU 采样。
        /// </summary>
        private bool QueueEngineTexture(EngineTextureRaw texture, string kind)
        {
            texture.Name = "AIChronicle_KitPage_" + Guid.NewGuid().ToString("N");
            bool valid = texture.IsValid;
            bool loaded = texture.IsLoaded();
            if (kind != "raw" || ShouldLogRawFrame())
                UiTrace.Log($"TextureWidget.Queue kind={kind} size={texture.Width}x{texture.Height} valid={valid} loaded={loaded}");

            if (valid && loaded)
            {
                Activate(texture, kind);
                return true;
            }

            _pendingTexture = texture;
            _pendingKind = kind;
            _pendingDeadline = DateTime.UtcNow.AddSeconds(5);
            UiTrace.Log("TextureWidget.Queue pending (not loaded/valid)");
            return true;
        }

        public void SetAnimate(bool enabled)
        {
            _animate = enabled;
            _lastFrameTime = DateTime.MinValue;
            _frameIndex = 0;
            UiTrace.Log($"TextureWidget.SetAnimate {enabled}");
        }

        protected override void OnUpdate(float dt)
        {
            base.OnUpdate(dt);

            if (_pendingTexture != null)
            {
                var pending = _pendingTexture;
                bool pendingValid = pending.IsValid;
                bool pendingLoaded = pending.IsLoaded();
                if (pendingValid && pendingLoaded)
                {
                    _pendingTexture = null;
                    Activate(pending, _pendingKind);
                }
                else if (!pendingValid || DateTime.UtcNow > _pendingDeadline)
                {
                    UiTrace.Log($"TextureWidget.Queue timeout kind={_pendingKind} valid={pendingValid} loaded={pendingLoaded}");
                    _pendingTexture = null;
                }

                return;
            }

            if (!_animate || DateTime.UtcNow - _lastFrameTime < TimeSpan.FromMilliseconds(66))
                return;

            if (_frameIndex >= MaxAnimationFrames)
            {
                _animate = false;
                UiTrace.Log("TextureWidget.Animation finished");
                return;
            }

            _lastFrameTime = DateTime.UtcNow;
            _frameIndex++;
            if (_frameIndex % 15 == 0)
                UiTrace.Log($"TextureWidget.Animation frame={_frameIndex}");

            SetRawBgra(CreateAnimatedFrame(480, 160, _frameIndex), 480, 160);
        }

        private static byte[] CreateAnimatedFrame(int width, int height, int frame)
        {
            var bytes = new byte[width * height * 4];
            int band = (frame * 8) % width;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 4;
                    bool border = x < 2 || y < 2 || x >= width - 2 || y >= height - 2;
                    int distance = Math.Abs(x - band);
                    if (distance > width / 2)
                        distance = width - distance;
                    float glow = distance < 70 ? 1f - distance / 70f : 0f;
                    byte b = border ? (byte)0x27 : (byte)(0x2A + y * 0x18 / height + glow * 0x60);
                    byte g = border ? (byte)0x4A : (byte)(0x3A + y * 0x20 / height + glow * 0x50);
                    byte r = border ? (byte)0x8C : (byte)(0x6A + y * 0x24 / height + glow * 0x40);
                    bytes[i] = b;
                    bytes[i + 1] = g;
                    bytes[i + 2] = r;
                    bytes[i + 3] = 0xFF;
                }
            }

            return bytes;
        }

        private void Activate(EngineTextureRaw texture, string kind)
        {
            var previous = _activeEngineTexture;
            _activeEngineTexture = texture;

            var adapter = new GauntletEngineTexture(texture);
            SetPageTexture(new TwoDimTexture(adapter), texture.Width, texture.Height);

            // 动画帧不能立即释放旧纹理，延后两帧交给 GPU 完成引用。
            if (previous != null && !ReferenceEquals(previous, texture))
            {
                try { previous.ReleaseAfterNumberOfFrames(2); } catch { }
            }

            if (kind != "raw" || ShouldLogRawFrame())
                UiTrace.Log($"TextureWidget.Activate kind={kind} size={texture.Width}x{texture.Height}");
        }

        private bool ShouldLogRawFrame()
        {
            if (_animate)
                return _frameIndex % 15 == 0;
            return _rawFrameCount <= 1 || _rawFrameCount % 30 == 0;
        }

        public override void OnBrushChanged()
        {
            base.OnBrushChanged();
            if (!_loggedBrush)
            {
                _loggedBrush = true;
                UiTrace.Log("TextureWidget.OnBrushChanged first");
            }
        }

        protected override void OnMousePressed()
        {
            base.OnMousePressed();
            var point = ToView(EventManager.MousePosition);
            PointerButton?.Invoke(point.X, point.Y, true);
        }

        protected override void OnMouseReleased(bool isFromInput)
        {
            base.OnMouseReleased(isFromInput);
            var point = ToView(EventManager.MousePosition);
            PointerButton?.Invoke(point.X, point.Y, false);
        }

        protected override void OnMouseMove()
        {
            base.OnMouseMove();
            var point = ToView(EventManager.MousePosition);
            PointerMoved?.Invoke(point.X, point.Y);
        }

        protected override bool OnPreviewMouseScroll()
        {
            // Gauntlet 默认不把滚轮事件交给普通 Widget；声明消费后才会派发到 OnMouseScroll。
            return true;
        }

        protected override void OnMouseScroll()
        {
            base.OnMouseScroll();
            var point = ToView(EventManager.MousePosition);
            // 传递 Gauntlet 原始滚轮量（通常 ±120），由 CEF 层做速度/惯性换算。
            int delta = (int)Math.Round(EventManager.DeltaMouseScroll);
            PointerWheel?.Invoke(point.X, point.Y, delta);
        }

        private (int X, int Y) ToView(Vector2 screenPosition)
        {
            var local = AreaRect.TransformScreenPositionToLocal(in screenPosition);
            float sizeX = Math.Max(1f, Size.X);
            float sizeY = Math.Max(1f, Size.Y);
            int x = (int)Math.Round(local.X / sizeX * _viewWidth);
            int y = (int)Math.Round(local.Y / sizeY * _viewHeight);
            x = Math.Max(0, Math.Min(_viewWidth - 1, x));
            y = Math.Max(0, Math.Min(_viewHeight - 1, y));
            return (x, y);
        }
    }

    /// <summary>带原生滚动条的动态纹理面板；Web 页面/长图的宿主。</summary>
    public sealed class KitTexturePanel
    {
        public Widget Root { get; }
        private readonly KitTextureWidget _widget;

        public KitTexturePanel(UIContext context)
        {
            var scroll = Kit.Scroll(context);
            Root = scroll.Root;
            _widget = new KitTextureWidget(context);
            scroll.Content.AddChild(_widget);
        }

        public bool SetPng(byte[] png)
        {
            return _widget.SetPng(png);
        }

        public void SetRawBgra(byte[] data, int width, int height)
        {
            _widget.SetRawBgra(data, width, height);
        }

        public void Clear()
        {
            _widget.SetPageTexture(null, 0, 0);
        }
    }
}
