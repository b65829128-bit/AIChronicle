using System;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.GauntletUI.Layout;
using TaleWorlds.Library;
using TaleWorlds.TwoDimension;

namespace AIChronicle
{
    /// <summary>
    /// 原生 UI Kit 的叶子组件工厂。屏幕只调用这些方法，不直接设置 SuggestedWidth/Margin/颜色。
    /// </summary>
    public static partial class Kit
    {
        public static Widget Surface(UIContext context, Color color)
        {
            var widget = new Widget(context)
            {
                Sprite = KitTheme.SquareSprite(context),
                Color = color,
                WidthSizePolicy = SizePolicy.StretchToParent,
                HeightSizePolicy = SizePolicy.StretchToParent
            };
            return widget;
        }

        public static TextWidget Text(UIContext context, string text, KitTextStyle style = KitTextStyle.Body,
            bool wrap = true, bool center = false)
        {
            var widget = new TextWidget(context)
            {
                Text = text,
                CanBreakWords = wrap,
                WidthSizePolicy = wrap || center ? SizePolicy.StretchToParent : SizePolicy.CoverChildren,
                HeightSizePolicy = SizePolicy.CoverChildren,
                HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                Brush = KitTheme.TextBrush(context, style, center)
            };
            return widget;
        }

        public static ButtonWidget Button(UIContext context, string label, Action? onClick,
            KitButtonStyle style = KitButtonStyle.Primary, float? width = null)
        {
            var button = new ButtonWidget(context)
            {
                Brush = KitTheme.ButtonBrush(context, style),
                WidthSizePolicy = SizePolicy.Fixed,
                HeightSizePolicy = SizePolicy.Fixed,
                SuggestedWidth = width ?? EstimateButtonWidth(label),
                SuggestedHeight = KitTheme.ButtonHeight,
                DoNotPassEventsToChildren = true,
                UpdateChildrenStates = true
            };

            var text = new TextWidget(context)
            {
                Text = label,
                Brush = KitTheme.ButtonTextBrush(context),
                WidthSizePolicy = SizePolicy.StretchToParent,
                HeightSizePolicy = SizePolicy.StretchToParent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            button.AddChild(text);

            if (onClick != null)
                button.ClickEventHandlers.Add(_ => onClick());

            return button;
        }

        public static ButtonWidget IconButton(UIContext context, string label, Action? onClick,
            KitButtonStyle style = KitButtonStyle.Ghost)
        {
            return Button(context, label, onClick, style, KitTheme.IconButtonSize);
        }

        public static Widget Badge(UIContext context, string label, Color color)
        {
            var root = Surface(context, color);
            root.WidthSizePolicy = SizePolicy.CoverChildren;
            root.HeightSizePolicy = SizePolicy.Fixed;
            root.SuggestedHeight = 22f;
            root.HorizontalAlignment = HorizontalAlignment.Left;

            var text = Text(context, label, KitTextStyle.Caption, wrap: false);
            text.Brush.TextHorizontalAlignment = TextHorizontalAlignment.Center;
            text.MarginLeft = KitTheme.S2;
            text.MarginRight = KitTheme.S2;
            text.VerticalAlignment = VerticalAlignment.Center;
            root.AddChild(text);
            return root;
        }

        public static Widget Divider(UIContext context)
        {
            var line = Surface(context, KitTheme.Line);
            line.WidthSizePolicy = SizePolicy.StretchToParent;
            line.HeightSizePolicy = SizePolicy.Fixed;
            line.SuggestedHeight = 1f;
            line.DoNotAcceptEvents = true;
            return line;
        }

        public static Widget Spacer(UIContext context, float width = 0f, float height = 0f)
        {
            var spacer = new Widget(context)
            {
                DoNotAcceptEvents = true,
                WidthSizePolicy = width > 0f ? SizePolicy.Fixed : SizePolicy.CoverChildren,
                HeightSizePolicy = height > 0f ? SizePolicy.Fixed : SizePolicy.CoverChildren,
                SuggestedWidth = width > 0f ? width : KitTheme.S2,
                SuggestedHeight = height > 0f ? height : 0f
            };
            return spacer;
        }

        public static ButtonWidget Toggle(UIContext context, string label, bool selected, Action<bool>? onChanged)
        {
            var button = new ButtonWidget(context)
            {
                ButtonType = ButtonType.Toggle,
                IsSelected = selected,
                Brush = KitTheme.ButtonBrush(context, KitButtonStyle.Ghost),
                WidthSizePolicy = SizePolicy.CoverChildren,
                HeightSizePolicy = SizePolicy.Fixed,
                SuggestedHeight = KitTheme.ButtonHeight,
                DoNotPassEventsToChildren = true,
                UpdateChildrenStates = true
            };

            var text = new TextWidget(context)
            {
                Text = label,
                Brush = KitTheme.ButtonTextBrush(context),
                WidthSizePolicy = SizePolicy.CoverChildren,
                HeightSizePolicy = SizePolicy.StretchToParent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MarginLeft = KitTheme.S4,
                MarginRight = KitTheme.S4
            };
            button.AddChild(text);

            button.ClickEventHandlers.Add(_ => onChanged?.Invoke(button.IsSelected));
            return button;
        }

        public static SliderWidget Slider(UIContext context, float min, float max, float value,
            Action<float>? onChanged)
        {
            var slider = new KitSliderWidget(context, onChanged)
            {
                WidthSizePolicy = SizePolicy.StretchToParent,
                HeightSizePolicy = SizePolicy.Fixed,
                SuggestedHeight = 28f,
                AlignmentAxis = AlignmentAxis.Horizontal,
                DoNotPassEventsToChildren = true,
                MinValueFloat = min,
                MaxValueFloat = max
            };

            var track = Surface(context, KitTheme.Line);
            track.WidthSizePolicy = SizePolicy.StretchToParent;
            track.HeightSizePolicy = SizePolicy.Fixed;
            track.SuggestedHeight = 4f;
            track.VerticalAlignment = VerticalAlignment.Center;
            track.DoNotAcceptEvents = true;

            var filler = Surface(context, KitTheme.Gold);
            filler.WidthSizePolicy = SizePolicy.Fixed;
            filler.HeightSizePolicy = SizePolicy.Fixed;
            filler.SuggestedWidth = 120f;
            filler.SuggestedHeight = 4f;
            filler.VerticalAlignment = VerticalAlignment.Center;
            filler.DoNotAcceptEvents = true;

            var handle = new ImageWidget(context)
            {
                Brush = context.GetBrush("FaceGen.Scrollbar.Handle") ?? context.DefaultBrush,
                WidthSizePolicy = SizePolicy.Fixed,
                HeightSizePolicy = SizePolicy.Fixed,
                SuggestedWidth = 12f,
                SuggestedHeight = 26f,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                DoNotAcceptEvents = true
            };

            slider.AddChild(track);
            slider.AddChild(filler);
            slider.AddChild(handle);
            slider.Filler = filler;
            slider.Handle = handle;
            slider.ValueFloat = value;
            return slider;
        }

        public static Widget Header(UIContext context, string title, Widget? right = null, string? subtitle = null)
        {
            var header = new Widget(context)
            {
                WidthSizePolicy = SizePolicy.StretchToParent,
                HeightSizePolicy = SizePolicy.Fixed,
                SuggestedHeight = 64f
            };

            var titleColumn = new ListPanel(context)
            {
                WidthSizePolicy = SizePolicy.CoverChildren,
                HeightSizePolicy = SizePolicy.CoverChildren,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                StackLayout = { LayoutMethod = LayoutMethod.VerticalTopToBottom }
            };
            titleColumn.AddChild(Text(context, title, KitTextStyle.Title, wrap: false));
            if (!string.IsNullOrEmpty(subtitle))
                titleColumn.AddChild(Text(context, subtitle!, KitTextStyle.Caption, wrap: false));
            header.AddChild(titleColumn);

            if (right != null)
            {
                right.HorizontalAlignment = HorizontalAlignment.Right;
                right.VerticalAlignment = VerticalAlignment.Center;
                header.AddChild(right);
            }

            return header;
        }

        public static float EstimateButtonWidth(string label)
        {
            float width = label.Length * 17f + KitTheme.ButtonPaddingX * 2f;
            return Math.Max(KitTheme.ButtonMinWidth, width);
        }
    }

    internal sealed class KitSliderWidget : SliderWidget
    {
        private readonly Action<float>? _onChanged;

        public KitSliderWidget(UIContext context, Action<float>? onChanged) : base(context)
        {
            _onChanged = onChanged;
        }

        protected override void OnValueFloatChanged(float value)
        {
            base.OnValueFloatChanged(value);
            _onChanged?.Invoke(value);
        }
    }
}
