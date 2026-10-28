using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.GauntletUI.Layout;
using TaleWorlds.Library;
using TaleWorlds.TwoDimension;

namespace AIChronicle
{
    /// <summary>
    /// 带内容面板的容器。屏幕通过 Add 添加子控件，不直接操作底层 Widget 的尺寸。
    /// </summary>
    public sealed class KitContainer
    {
        public Widget Root { get; }
        public ListPanel Content { get; }

        private readonly bool _horizontal;
        private readonly float _gap;

        internal KitContainer(Widget root, ListPanel content, bool horizontal = false, float gap = 0f)
        {
            Root = root;
            Content = content;
            _horizontal = horizontal;
            _gap = gap;
        }

        public KitContainer Add(Widget child)
        {
            if (_gap > 0f)
            {
                if (_horizontal)
                    child.MarginRight = _gap;
                else
                    child.MarginBottom = _gap;
            }

            Content.AddChild(child);
            return this;
        }

        public KitContainer Add(KitContainer child)
        {
            return Add(child.Root);
        }
    }

    public sealed class KitScroll
    {
        public Widget Root { get; }
        public ScrollablePanel Scroll { get; }
        public ListPanel Content { get; }

        internal KitScroll(Widget root, ScrollablePanel scroll, ListPanel content)
        {
            Root = root;
            Scroll = scroll;
            Content = content;
        }

        public KitScroll Add(Widget child)
        {
            Content.AddChild(child);
            return this;
        }

        public KitScroll Add(KitContainer child)
        {
            return Add(child.Root);
        }
    }

    public static partial class Kit
    {
        public static KitContainer Panel(UIContext context, Color color, float padding = KitTheme.PanelPadding,
            bool stretchContent = false)
        {
            var root = Surface(context, color);
            root.WidthSizePolicy = SizePolicy.StretchToParent;
            root.HeightSizePolicy = SizePolicy.StretchToParent;

            var content = new ListPanel(context)
            {
                WidthSizePolicy = SizePolicy.StretchToParent,
                HeightSizePolicy = stretchContent ? SizePolicy.StretchToParent : SizePolicy.CoverChildren,
                StackLayout = { LayoutMethod = LayoutMethod.VerticalTopToBottom }
            };
            content.MarginLeft = padding;
            content.MarginRight = padding;
            content.MarginTop = padding;
            content.MarginBottom = padding;

            root.AddChild(content);
            return new KitContainer(root, content);
        }

        public static KitContainer Card(UIContext context, float width = KitTheme.WideWidth, float padding = KitTheme.CardPadding)
        {
            var outer = Surface(context, KitTheme.Gold);
            outer.WidthSizePolicy = SizePolicy.Fixed;
            outer.SuggestedWidth = width;
            outer.HeightSizePolicy = SizePolicy.CoverChildren;
            outer.HorizontalAlignment = HorizontalAlignment.Center;
            outer.VerticalAlignment = VerticalAlignment.Center;

            var inner = Surface(context, KitTheme.Card);
            inner.WidthSizePolicy = SizePolicy.StretchToParent;
            inner.HeightSizePolicy = SizePolicy.CoverChildren;
            inner.MarginLeft = 1f;
            inner.MarginRight = 1f;
            inner.MarginTop = 1f;
            inner.MarginBottom = 1f;
            outer.AddChild(inner);

            var content = new ListPanel(context)
            {
                WidthSizePolicy = SizePolicy.StretchToParent,
                HeightSizePolicy = SizePolicy.CoverChildren,
                StackLayout = { LayoutMethod = LayoutMethod.VerticalTopToBottom }
            };
            content.MarginLeft = padding;
            content.MarginRight = padding;
            content.MarginTop = padding;
            content.MarginBottom = padding;
            inner.AddChild(content);

            return new KitContainer(outer, content);
        }

        public static KitContainer Column(UIContext context, float gap = KitTheme.S2)
        {
            var root = new ListPanel(context)
            {
                WidthSizePolicy = SizePolicy.StretchToParent,
                HeightSizePolicy = SizePolicy.CoverChildren,
                StackLayout = { LayoutMethod = LayoutMethod.VerticalTopToBottom }
            };
            return new KitContainer(root, root, horizontal: false, gap: gap);
        }

        public static KitContainer Row(UIContext context, float gap = KitTheme.S2)
        {
            var root = new ListPanel(context)
            {
                WidthSizePolicy = SizePolicy.StretchToParent,
                HeightSizePolicy = SizePolicy.CoverChildren,
                StackLayout = { LayoutMethod = LayoutMethod.HorizontalLeftToRight }
            };
            return new KitContainer(root, root, horizontal: true, gap: gap);
        }

        public static KitScroll Scroll(UIContext context)
        {
            // Scrollbar 与 ScrollablePanel 平级：对齐原版 prefab 的 "sibling scrollbar" 结构，
            // 避免把 scrollbar 放进滚动面板自身导致布局/滚动互相影响。
            var wrapper = new Widget(context)
            {
                WidthSizePolicy = SizePolicy.StretchToParent,
                HeightSizePolicy = SizePolicy.StretchToParent
            };

            var scroll = new KitScrollablePanel(context)
            {
                WidthSizePolicy = SizePolicy.StretchToParent,
                HeightSizePolicy = SizePolicy.StretchToParent,
                AutoHideScrollBars = false,
                MouseScrollAxis = AlignmentAxis.Vertical
            };
            scroll.MarginRight = KitTheme.S3;

            var clip = new Widget(context)
            {
                WidthSizePolicy = SizePolicy.StretchToParent,
                HeightSizePolicy = SizePolicy.StretchToParent,
                ClipContents = true
            };
            clip.MarginRight = KitTheme.S3;

            var content = new ListPanel(context)
            {
                WidthSizePolicy = SizePolicy.StretchToParent,
                HeightSizePolicy = SizePolicy.CoverChildren,
                VerticalAlignment = VerticalAlignment.Top,
                StackLayout = { LayoutMethod = LayoutMethod.VerticalTopToBottom }
            };

            clip.AddChild(content);
            scroll.AddChild(clip);
            wrapper.AddChild(scroll);

            var bar = new ScrollbarWidget(context)
            {
                WidthSizePolicy = SizePolicy.Fixed,
                HeightSizePolicy = SizePolicy.StretchToParent,
                SuggestedWidth = KitTheme.S2,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                AlignmentAxis = AlignmentAxis.Vertical,
                MinValue = 0,
                MaxValue = 100
            };
            var handle = new ImageWidget(context)
            {
                Brush = context.GetBrush("FaceGen.Scrollbar.Handle") ?? context.DefaultBrush,
                WidthSizePolicy = SizePolicy.Fixed,
                HeightSizePolicy = SizePolicy.Fixed,
                SuggestedWidth = KitTheme.S2,
                SuggestedHeight = 24f,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            bar.AddChild(handle);
            bar.Handle = handle;
            wrapper.AddChild(bar);

            scroll.ClipRect = clip;
            scroll.InnerPanel = content;
            scroll.VerticalScrollbar = bar;

            return new KitScroll(wrapper, scroll, content);
        }

        public static KitContainer FormRow(UIContext context, string label, Widget control, string? hint = null)
        {
            var column = Column(context, KitTheme.S1);
            column.Add(Text(context, label ?? "", KitTextStyle.Heading, wrap: false));
            column.Add(control);
            if (!string.IsNullOrEmpty(hint))
                column.Add(Text(context, hint!, KitTextStyle.Caption));
            column.Root.MarginBottom = KitTheme.S3;
            return column;
        }

        public static TextWidget SectionTitle(UIContext context, string text)
        {
            var title = Text(context, text, KitTextStyle.Heading, wrap: false);
            title.MarginTop = KitTheme.S4;
            title.MarginBottom = KitTheme.S2;
            return title;
        }
    }


    /// <summary>带首帧跟踪的 ScrollablePanel，用于定位布局/滚动初始化卡死。</summary>
    internal sealed class KitScrollablePanel : ScrollablePanel
    {
        private bool _loggedUpdate;
        private bool _loggedUpdateDone;
        private bool _loggedLateUpdate;
        private bool _loggedLateUpdateDone;

        public KitScrollablePanel(UIContext context) : base(context)
        {
        }

        protected override void OnUpdate(float dt)
        {
            if (!_loggedUpdate)
            {
                _loggedUpdate = true;
                UiTrace.Log("ScrollPanel.OnUpdate first start");
            }

            base.OnUpdate(dt);

            if (!_loggedUpdateDone)
            {
                _loggedUpdateDone = true;
                UiTrace.Log("ScrollPanel.OnUpdate first done");
            }
        }

        protected override void OnLateUpdate(float dt)
        {
            if (!_loggedLateUpdate)
            {
                _loggedLateUpdate = true;
                UiTrace.Log("ScrollPanel.OnLateUpdate first start");
            }

            base.OnLateUpdate(dt);

            if (!_loggedLateUpdateDone)
            {
                _loggedLateUpdateDone = true;
                UiTrace.Log("ScrollPanel.OnLateUpdate first done");
            }
        }
    }
}
