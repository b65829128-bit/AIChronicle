#if NET472
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.Library;
using TaleWorlds.ModuleManager;

namespace AIChronicle
{
    /// <summary>
    /// CEF OffScreen 实时页面宿主。默认加载 live.html 演示；chronicle 模式由 C# 注入页面数据。
    /// 页面动作通过 CefBridge.dispatch 回传，宿主只暴露 PageAction 事件，不直接碰游戏状态。
    /// </summary>
    public sealed class CefWebScreen : KitScreen
    {
        private readonly string _pageFile;
        private readonly string _title;
        private readonly string _subtitle;
        private readonly object? _pageData;

        private KitTextureWidget? _texture;
        private TextWidget? _status;
        private CefWebSession? _session;
        private bool _closed;
        private int _framePostPending;

        /// <summary>网页动作（id, payloadJson）；在 CEF 线程触发，处理游戏状态需自行回主线程。</summary>
        public event Action<string, string>? PageAction;

        public CefWebScreen(
            string pageFile = "live.html",
            string title = "CEF 实时页面",
            string subtitle = "800×600 · 15fps · 本地 HTML",
            object? pageData = null)
        {
            _pageFile = pageFile;
            _title = title;
            _subtitle = subtitle;
            _pageData = pageData;
        }

        /// <summary>打开真实史书页面：内容由 C# 数据注入，页面本身只负责渲染。</summary>
        public static void OpenChronicle()
        {
            new CefWebScreen(
                "chronicle.html",
                "编年史 · 实时页面",
                "800×600 · CEF · 数据由 C# 注入 · 可滚动",
                BuildChronicleData()).Open();
        }

        protected override void Build(KitRoot root)
        {
            UiTrace.Log("CefWebScreen.Build start page=" + _pageFile);
            var ctx = Context!;
            root.Scrim();

            var panel = Kit.Panel(ctx, KitTheme.Window, KitTheme.S5, stretchContent: true);
            panel.Add(Kit.Header(ctx, _title,
                right: Kit.Button(ctx, "关闭", Close, KitButtonStyle.Ghost),
                subtitle: _subtitle));
            panel.Add(Kit.Divider(ctx));

            _status = Kit.Text(ctx, "正在启动 CEF…", KitTextStyle.Muted, wrap: false);
            panel.Add(_status);

            _texture = new KitTextureWidget(ctx)
            {
                WidthSizePolicy = SizePolicy.Fixed,
                HeightSizePolicy = SizePolicy.Fixed,
                SuggestedWidth = 800f,
                SuggestedHeight = 600f,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _texture.PointerMoved += OnPointerMoved;
            _texture.PointerButton += OnPointerButton;
            _texture.PointerWheel += OnPointerWheel;
            panel.Add(_texture);
            root.Add(panel);

            if (!CefWebHost.EnsureInitialized())
            {
                SetStatus("CEF 初始化失败：" + CefWebHost.LastError);
                return;
            }

            string page = Path.Combine(ModuleHelper.GetModuleFullPath("AIChronicle"), "WebUI", _pageFile);
            UiTrace.Log("CefWebScreen page=" + page + " exists=" + File.Exists(page));
            if (!File.Exists(page))
            {
                SetStatus("页面不存在：" + page);
                return;
            }

            _session = new CefWebSession();
            _session.Status += OnStatus;
            _session.FrameReady += OnFrame;
            _session.PageLoaded += OnPageLoaded;

            var bridge = new CefBridge(this);
            bridge.ActionReceived += OnBridgeAction;
            string pageFull = Path.GetFullPath(page);
            if (!_session.Start(new Uri(pageFull).AbsoluteUri, 800, 600, 15, bridge))
            {
                SetStatus("CEF 启动失败：" + CefWebHost.LastError);
            }
        }

        private void OnStatus(string text)
        {
            MainThreadExecutor.Post(() => SetStatus(text));
        }

        private void OnFrame(byte[] bytes, int width, int height)
        {
            // 主线程忙时丢弃中间帧，只保留最新帧，避免队列越积越多。
            if (Interlocked.Exchange(ref _framePostPending, 1) == 1)
                return;

            MainThreadExecutor.Post(() =>
            {
                Interlocked.Exchange(ref _framePostPending, 0);
                if (_closed)
                    return;
                try
                {
                    _texture?.SetRawBgra(bytes, width, height);
                }
                catch (Exception ex)
                {
                    UiTrace.Log("CefWebScreen.OnFrame main failed: " + ex);
                }
            });
        }

        private void OnPageLoaded()
        {
            if (_closed)
                return;
            try
            {
                string script;
                if (_pageData != null)
                {
                    string json = JsonConvert.SerializeObject(_pageData);
                    UiTrace.Log("CefWebScreen inject page data bytes=" + json.Length);
                    script = "window.__game && window.__game.render(" + json + ")";
                }
                else
                {
                    UiTrace.Log("CefWebScreen page loaded -> setStatus");
                    script = "window.__game && window.__game.setStatus('AI Chronicle 已接入 CEF 游戏桥')";
                }

                _session?.EvaluateScriptAsync(script);
            }
            catch (Exception ex)
            {
                UiTrace.Log("CefWebScreen.OnPageLoaded failed: " + ex);
            }
        }

        private void OnBridgeAction(string id, string payload)
        {
            try
            {
                PageAction?.Invoke(id, payload);
            }
            catch (Exception ex)
            {
                UiTrace.Log("CefWebScreen.PageAction failed: " + ex);
            }
        }

        private void OnPointerMoved(int x, int y)
        {
            _session?.SendMouseMove(x, y);
        }

        private void OnPointerButton(int x, int y, bool down)
        {
            if (down)
                _session?.SendMouseDown(x, y);
            else
                _session?.SendMouseUp(x, y);
        }

        private void OnPointerWheel(int x, int y, int delta)
        {
            if (delta == 0)
                return;
            UiTrace.Log($"CefWebScreen wheel x={x} y={y} delta={delta}");
            _session?.SendMouseWheel(x, y, delta);
        }

        private void SetStatus(string text)
        {
            if (_closed)
                return;
            try
            {
                if (_status != null)
                    _status.Text = text;
                UiTrace.Log("CefWebScreen status: " + text);
            }
            catch (Exception ex)
            {
                UiTrace.Log("CefWebScreen.SetStatus failed: " + ex);
            }
        }

        protected override void OnClosed()
        {
            _closed = true;
            _session?.Dispose();
            _session = null;
            _texture = null;
            _status = null;
            UiTrace.Log("CefWebScreen closed");
        }

        private static object BuildChronicleData()
        {
            return new Dictionary<string, object>
            {
                ["template"] = "chronicle",
                ["eyebrow"] = "卡拉迪亚 · 编年史",
                ["title"] = "第一〇八九年编年史",
                ["subtitle"] = "史官谨记：岁在己巳，天下未定。",
                ["sections"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["heading"] = "北境",
                        ["paragraphs"] = new[]
                        {
                            "春正月，斯特吉亚大公朗瓦德聚兵于巴尔加德，声言复其故地。瓦兰迪亚王德泰尔遣使责之，使者不得见而还。",
                            "二月，库赛特可汗蒙楚格率轻骑掠瓦兰迪亚西境，焚村三，掠牛马无算。德泰尔怒，召集封臣，议出兵。"
                        }
                    },
                    new Dictionary<string, object>
                    {
                        ["heading"] = "夏 · 朝议",
                        ["paragraphs"] = new[]
                        {
                            "夏四月，南帝国女皇拉盖娅大会群臣于吕卡隆。元老卢孔不至，遣其子奉书，言「帝位当由元老院议定，非一妇人所可私授」。拉盖娅览书，默然良久，命藏之。",
                            "五月，西帝国将军加里俄斯攻取阿塞莱边城一，阿塞莱苏丹温吉德遣使求和，许岁贡。加里俄斯不受，进兵不止。"
                        }
                    },
                    new Dictionary<string, object>
                    {
                        ["heading"] = "秋 · 盟变",
                        ["paragraphs"] = new[]
                        {
                            "秋七月，巴旦尼亚至高王卡拉多格与斯特吉亚盟，约共击瓦兰迪亚。盟成之日，斯特吉亚背之，反与瓦兰迪亚通好。卡拉多格大怒，斩其使。",
                            "八月，帝国三雄各遣使至巴旦尼亚，许以金币、粮秣、边地。卡拉多格闭门三日，终不允。诸使归，各言其国将兴兵。"
                        }
                    },
                    new Dictionary<string, object>
                    {
                        ["heading"] = "冬 · 岁末",
                        ["paragraphs"] = new[]
                        {
                            "冬十月，瓦兰迪亚与斯特吉亚会盟于西境，约以婚媾，共御库赛特。盟书未干，库赛特游骑已越山隘，焚仓廪二。",
                            "十一月，阿塞莱商队自南来，载盐、枣、香料，易铁与毛皮。吕卡隆市价稍平，然城中流言不止，皆言女皇将迁都。",
                            "十二月，大雪。诸国罢兵，各归其境。史官录是岁战事三十七起，盟约十一，背盟者五。"
                        }
                    },
                    new Dictionary<string, object>
                    {
                        ["heading"] = "诸国纪事",
                        ["paragraphs"] = new[]
                        {
                            "斯特吉亚：朗瓦德虽得巴尔加德，然诸部离心，冬粮不足，遣使借粮于巴旦尼亚，卡拉多格许之，索马三百。",
                            "瓦兰迪亚：德泰尔整饬边军，置斥候于山口，然国库空虚，封臣多怨。王乃减赋一年，以安人心。",
                            "帝国：拉盖娅与卢孔各拥一军，隔河相望。元老院议和三次，皆不欢而散。加里俄斯坐观成败。"
                        }
                    },
                    new Dictionary<string, object>
                    {
                        ["heading"] = "史官附记",
                        ["paragraphs"] = new[]
                        {
                            "是岁也，天时无常，人事反复。诸国以盟约相欺，以兵戈相试，而百姓流离，仓廪空虚。史官不敢言天命，唯录其事，以待后人。"
                        }
                    }
                },
                ["note"] = "史官曰：观是岁之事，盟约如朝露，兵戈似长河。诸国皆言天命，而天命未尝一顾；所顾者，粮秣、人心与刀剑耳。",
                ["noteLabel"] = "史官曰",
                ["seal"] = "编年之印",
                ["footer"] = "AI Chronicle · 史官手记",
                ["page"] = "第一页"
            };
        }
    }
}
#endif
