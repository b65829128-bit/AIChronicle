using System;
using System.IO;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.Library;
using TaleWorlds.ModuleManager;

namespace AIChronicle
{
    /// <summary>
    /// 组件画廊：所有 Kit 组件与状态在一屏可见。F8 开关，用于视觉回归与主题调整。
    /// 默认“安全模式”不加载 Scroll/动态纹理；可分别测试滚动区、纹理，再组合，便于实机定位卡死点。
    /// </summary>
    public sealed class KitGalleryScreen : KitScreen
    {
        private static KitGalleryScreen? _current;
        private TextWidget? _status;
        private bool _useScroll;
        private bool _useTexture;

        public static bool IsGalleryOpen => _current != null && _current.IsOpen;

        public static void Toggle()
        {
            UiTrace.Log("Gallery.Toggle currentOpen=" + (_current != null && _current.IsOpen));
            if (_current != null && _current.IsOpen)
            {
                _current.Close();
                return;
            }

            _current = new KitGalleryScreen();
            _current.Open();
            if (!_current.IsOpen)
                _current = null;
        }

        public static void CloseIfOpen()
        {
            if (_current != null && _current.IsOpen)
                _current.Close();
            _current = null;
        }

        protected override void Build(KitRoot root)
        {
            UiTrace.Log($"Gallery.Build start scroll={_useScroll} texture={_useTexture}");
            var ctx = Context!;
            root.Scrim();
            UiTrace.Log("Gallery.Build scrim");

            var panel = Kit.Panel(ctx, KitTheme.Window, KitTheme.S5, stretchContent: true);
            panel.Add(Kit.Header(ctx, "AI Chronicle · UI Kit",
                right: Kit.Button(ctx, "关闭", Close, KitButtonStyle.Ghost),
                subtitle: ModeLabel()));
            UiTrace.Log("Gallery.Build header");
            panel.Add(Kit.Divider(ctx));

            // Gallery 始终使用滚动区：内容长度随模式变化，避免无滚动模式把顶部标题/说明挤出屏幕。
            var scroll = Kit.Scroll(ctx);
            UiTrace.Log("Gallery.Build scroll created");
            scroll.Root.HeightSizePolicy = SizePolicy.StretchToParent;
            panel.Add(scroll.Root);
            Action<Widget> add = widget => scroll.Add(widget);

            add(Kit.Text(ctx, "文字层级", KitTextStyle.Title, wrap: false));
            add(Kit.Text(ctx, "标题 Heading：用于分区与卡片标题。", KitTextStyle.Heading));
            add(Kit.Text(ctx, "正文 Body：这是一段用于检查行高、字距、换行与中文标点的示例文字。朝堂之上，众臣各执一词；史官退而记之，曰：「名分未定，兵戈不息。」", KitTextStyle.Body));
            add(Kit.Text(ctx, "注释 Caption：次要信息、时间、来源。", KitTextStyle.Caption));
            add(Kit.Spacer(ctx, height: KitTheme.S4));

            add(Kit.SectionTitle(ctx, "按钮"));
            var buttonRow = Kit.Row(ctx);
            buttonRow.Add(Kit.Button(ctx, "主要", () => SetStatus("主要按钮"), KitButtonStyle.Primary));
            buttonRow.Add(Kit.Button(ctx, "次要", () => SetStatus("次要按钮"), KitButtonStyle.Secondary));
            buttonRow.Add(Kit.Button(ctx, "幽灵", () => SetStatus("幽灵按钮"), KitButtonStyle.Ghost));
            buttonRow.Add(Kit.Button(ctx, "危险", () => SetStatus("危险按钮"), KitButtonStyle.Danger));
            add(buttonRow.Root);
            add(Kit.Spacer(ctx, height: KitTheme.S3));
            UiTrace.Log("Gallery.Build buttons");

            add(Kit.SectionTitle(ctx, "标签"));
            var badgeRow = Kit.Row(ctx);
            badgeRow.Add(Kit.Badge(ctx, "战争", KitTheme.Blood));
            badgeRow.Add(Kit.Badge(ctx, "盟约", KitTheme.Blue));
            badgeRow.Add(Kit.Badge(ctx, "丰收", KitTheme.Green));
            badgeRow.Add(Kit.Badge(ctx, "中立", KitTheme.Line));
            add(badgeRow.Root);
            add(Kit.Spacer(ctx, height: KitTheme.S3));
            UiTrace.Log("Gallery.Build badges");

            add(Kit.SectionTitle(ctx, "表单"));
            add(Kit.FormRow(ctx, "开关", Kit.Toggle(ctx, "启用示例", true, v => SetStatus($"开关：{(v ? "开" : "关")}")), "Toggle 的选中状态由原生 ButtonWidget 维护。").Root);
            add(Kit.FormRow(ctx, "滑块", Kit.Slider(ctx, 0f, 100f, 40f, v => SetStatus($"滑块：{v:F0}")), "Slider 的 min/max/value/回调由 Kit 统一处理。").Root);
            UiTrace.Log("Gallery.Build form");

            if (!_useScroll && !_useTexture)
            {
                BuildSafeExtras(ctx, add);
            }
            else
            {
                BuildModeSwitch(ctx, add);
                if (_useScroll)
                    BuildScrollRows(ctx, add);
                if (_useTexture)
                    BuildTexture(ctx, add);
            }

            add(Kit.Spacer(ctx, height: KitTheme.S4));
            _status = Kit.Text(ctx, "就绪", KitTextStyle.Muted, wrap: false);
            add(_status);

            root.Add(panel);
            UiTrace.Log($"Gallery.Build done scroll={_useScroll} texture={_useTexture}");
        }

        private string ModeLabel()
        {
            if (_useScroll && _useTexture) return "滚动 + 纹理 · F8 关闭";
            if (_useScroll) return "仅滚动区 · F8 关闭";
            if (_useTexture) return "仅动态纹理 · F8 关闭";
            return "安全模式 · F8 关闭";
        }

        private void BuildSafeExtras(UIContext ctx, Action<Widget> add)
        {
            add(Kit.SectionTitle(ctx, "列表（安全模式 6 行）"));
            for (int i = 0; i < 6; i++)
            {
                int index = i;
                var row = Kit.Row(ctx);
                row.Add(Kit.Text(ctx, $"第 {index + 1} 条朝议", KitTextStyle.Body, wrap: false));
                row.Add(Kit.Badge(ctx, index % 2 == 0 ? "公开" : "密陈", index % 2 == 0 ? KitTheme.Blue : KitTheme.Line));
                row.Add(Kit.Button(ctx, "查看", () => SetStatus($"查看第 {index + 1} 条"), KitButtonStyle.Ghost, 80f));
                row.Root.MarginBottom = KitTheme.S2;
                add(row.Root);
            }
            UiTrace.Log("Gallery.Build safe list");

            add(Kit.SectionTitle(ctx, "诊断：分项加载"));
            var scrollRow = Kit.Row(ctx);
            scrollRow.Add(Kit.Text(ctx, "滚动区（24 行）", KitTextStyle.Body, wrap: false));
            scrollRow.Add(Kit.Button(ctx, "仅滚动区", () =>
            {
                _useScroll = true;
                _useTexture = false;
                Rebuild();
            }, KitButtonStyle.Secondary));
            add(scrollRow.Root);

            var textureRow = Kit.Row(ctx);
            textureRow.Add(Kit.Text(ctx, "动态纹理", KitTextStyle.Body, wrap: false));
            textureRow.Add(Kit.Button(ctx, "仅纹理", () =>
            {
                _useScroll = false;
                _useTexture = true;
                Rebuild();
            }, KitButtonStyle.Secondary));
            add(textureRow.Root);

            var bothRow = Kit.Row(ctx);
            bothRow.Add(Kit.Text(ctx, "滚动区 + 纹理", KitTextStyle.Body, wrap: false));
            bothRow.Add(Kit.Button(ctx, "完整模式", () =>
            {
                _useScroll = true;
                _useTexture = true;
                Rebuild();
            }, KitButtonStyle.Primary));
            add(bothRow.Root);
            UiTrace.Log("Gallery.Build safe switch");

#if NET472
            add(Kit.SectionTitle(ctx, "CEF 实时页面"));
            var cefRow = Kit.Row(ctx);
            cefRow.Add(Kit.Text(ctx, "真实 CefSharp OffScreen 页面（1280×720 / 30fps）", KitTextStyle.Body, wrap: false));
            cefRow.Add(Kit.Button(ctx, "打开 CEF", () =>
            {
                Close();
                new CefWebScreen().Open();
            }, KitButtonStyle.Primary));
            cefRow.Add(Kit.Button(ctx, "打开 CEF 史书", () =>
            {
                Close();
                CefWebScreen.OpenChronicle();
            }, KitButtonStyle.Secondary));
            add(cefRow.Root);
            UiTrace.Log("Gallery.Build cef button");
#endif
        }

        private void BuildScrollRows(UIContext ctx, Action<Widget> add)
        {
            add(Kit.SectionTitle(ctx, "滚动列表（24 行）"));
            for (int i = 0; i < 24; i++)
            {
                int index = i;
                var row = Kit.Row(ctx);
                row.Add(Kit.Text(ctx, $"第 {index + 1} 条朝议", KitTextStyle.Body, wrap: false));
                row.Add(Kit.Badge(ctx, index % 2 == 0 ? "公开" : "密陈", index % 2 == 0 ? KitTheme.Blue : KitTheme.Line));
                row.Add(Kit.Button(ctx, "查看", () => SetStatus($"查看第 {index + 1} 条"), KitButtonStyle.Ghost, 80f));
                row.Root.MarginBottom = KitTheme.S2;
                add(row.Root);
            }
            UiTrace.Log("Gallery.Build scroll rows");
        }

        private void BuildTexture(UIContext ctx, Action<Widget> add)
        {
            add(Kit.SectionTitle(ctx, "动态纹理"));
            var texture = new KitTextureWidget(ctx)
            {
                WidthSizePolicy = SizePolicy.Fixed,
                HeightSizePolicy = SizePolicy.Fixed,
                SuggestedWidth = 480f,
                SuggestedHeight = 160f
            };
            UiTrace.Log("Gallery.Build texture before png");
            try
            {
                string module = ModuleHelper.GetModuleFullPath("AIChronicle");
                string path = Path.Combine(module, "WebUI", "assets", "texture-test.png");
                UiTrace.Log("Gallery.Build texture path=" + path);
                if (File.Exists(path))
                {
                    // 生产路径：Web 缓存也是磁盘 PNG，优先验证引擎资源加载。
                    if (!texture.SetPngFile(path))
                    {
                        UiTrace.Log("Gallery.Build file texture failed, trying memory");
                        texture.SetPng(File.ReadAllBytes(path));
                    }
                }
                else
                {
                    UiTrace.Log("Gallery.Build texture png missing");
                }
            }
            catch (Exception ex)
            {
                UiTrace.Log("Gallery.Build texture failed: " + ex);
            }
            UiTrace.Log("Gallery.Build texture added");
            add(texture);

            var animRow = Kit.Row(ctx);
            animRow.Add(Kit.Text(ctx, "帧流测试（15fps / 10 秒）", KitTextStyle.Body, wrap: false));
            animRow.Add(Kit.Button(ctx, "开始动画", () => texture.SetAnimate(true), KitButtonStyle.Secondary));
            animRow.Add(Kit.Button(ctx, "停止", () => texture.SetAnimate(false), KitButtonStyle.Ghost));
            add(animRow.Root);
            UiTrace.Log("Gallery.Build animation controls");
        }

        private void BuildModeSwitch(UIContext ctx, Action<Widget> add)
        {
            add(Kit.SectionTitle(ctx, "切换诊断模式"));
            var row = Kit.Row(ctx);
            row.Add(Kit.Text(ctx, ModeLabel(), KitTextStyle.Muted, wrap: false));

            if (!_useScroll)
            {
                row.Add(Kit.Button(ctx, "改测仅滚动区", () =>
                {
                    _useScroll = true;
                    _useTexture = false;
                    Rebuild();
                }, KitButtonStyle.Secondary));
            }
            else if (!_useTexture)
            {
                row.Add(Kit.Button(ctx, "改测仅纹理", () =>
                {
                    _useScroll = false;
                    _useTexture = true;
                    Rebuild();
                }, KitButtonStyle.Secondary));
            }
            else
            {
                row.Add(Kit.Button(ctx, "改测仅滚动区", () =>
                {
                    _useScroll = true;
                    _useTexture = false;
                    Rebuild();
                }, KitButtonStyle.Secondary));
                row.Add(Kit.Button(ctx, "改测仅纹理", () =>
                {
                    _useScroll = false;
                    _useTexture = true;
                    Rebuild();
                }, KitButtonStyle.Secondary));
            }

            row.Add(Kit.Button(ctx, "返回安全模式", () =>
            {
                _useScroll = false;
                _useTexture = false;
                Rebuild();
            }, KitButtonStyle.Ghost));
            add(row.Root);
            UiTrace.Log("Gallery.Build mode switch");
        }

        protected override void OnClosed()
        {
            _status = null;
        }

        private void SetStatus(string text)
        {
            if (_status != null)
                _status.Text = text;
        }

        private static byte[] CreateGradient(int width, int height)
        {
            var bytes = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 4;
                    bool border = x < 2 || y < 2 || x >= width - 2 || y >= height - 2;
                    byte b = border ? (byte)0x27 : (byte)(0x2A + y * 0x18 / height);
                    byte g = border ? (byte)0x4A : (byte)(0x3A + y * 0x20 / height);
                    byte r = border ? (byte)0x8C : (byte)(0x6A + y * 0x24 / height);
                    bytes[i] = b;
                    bytes[i + 1] = g;
                    bytes[i + 2] = r;
                    bytes[i + 3] = 0xFF;
                }
            }

            return bytes;
        }
    }
}
