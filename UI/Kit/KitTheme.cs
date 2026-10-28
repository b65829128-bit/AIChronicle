using TaleWorlds.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.TwoDimension;

namespace AIChronicle
{
    public enum KitTextStyle
    {
        Title,
        Heading,
        Body,
        Muted,
        Caption
    }

    public enum KitButtonStyle
    {
        Primary,
        Secondary,
        Ghost,
        Danger
    }

    public enum KitSurfaceStyle
    {
        Scrim,
        Window,
        Card,
        Sidebar,
        Header,
        Line,
        Gold,
        Green,
        Blue,
        Blood
    }

    /// <summary>
    /// 原生 UI Kit 的主题 token。所有屏幕只能引用这里的语义名，不要直接写颜色/字号/间距。
    /// </summary>
    public static class KitTheme
    {
        public const float S1 = 4f;
        public const float S2 = 8f;
        public const float S3 = 12f;
        public const float S4 = 16f;
        public const float S5 = 24f;
        public const float S6 = 32f;

        public const int FontTitle = 30;
        public const int FontHeading = 22;
        public const int FontBody = 17;
        public const int FontCaption = 14;

        public const float ButtonHeight = 42f;
        public const float ButtonMinWidth = 92f;
        public const float ButtonPaddingX = 20f;
        public const float IconButtonSize = 34f;

        public const float PanelPadding = 24f;
        public const float CardPadding = 20f;
        public const float WideWidth = 980f;
        public const float DialogWidth = 760f;
        public const float NarrowWidth = 420f;

        public static readonly Color Ink = Color.ConvertStringToColor("#F2E6CFFF");
        public static readonly Color Muted = Color.ConvertStringToColor("#B89B72FF");
        public static readonly Color Gold = Color.ConvertStringToColor("#C9A227FF");
        public static readonly Color Blood = Color.ConvertStringToColor("#8C3A3AFF");
        public static readonly Color Green = Color.ConvertStringToColor("#7FA86AFF");
        public static readonly Color Blue = Color.ConvertStringToColor("#7C9FB8FF");

        public static readonly Color Window = Color.ConvertStringToColor("#1C1814F2");
        public static readonly Color Card = Color.ConvertStringToColor("#241F19FF");
        public static readonly Color Sidebar = Color.ConvertStringToColor("#17130FFF");
        public static readonly Color Header = Color.ConvertStringToColor("#2A231DFF");
        public static readonly Color Line = Color.ConvertStringToColor("#3D3228FF");
        public static readonly Color Scrim = Color.ConvertStringToColor("#000000AA");
        public static readonly Color SecondaryButton = Color.ConvertStringToColor("#8A7A66FF");

        public static Color SurfaceColor(KitSurfaceStyle style)
        {
            switch (style)
            {
                case KitSurfaceStyle.Window: return Window;
                case KitSurfaceStyle.Card: return Card;
                case KitSurfaceStyle.Sidebar: return Sidebar;
                case KitSurfaceStyle.Header: return Header;
                case KitSurfaceStyle.Line: return Line;
                case KitSurfaceStyle.Gold: return Gold;
                case KitSurfaceStyle.Green: return Green;
                case KitSurfaceStyle.Blue: return Blue;
                case KitSurfaceStyle.Blood: return Blood;
                default: return Scrim;
            }
        }

        public static Sprite SquareSprite(UIContext context)
        {
            return context.SpriteData.GetSprite("BlankWhiteSquare_9");
        }

        public static Brush TextBrush(UIContext context, KitTextStyle style, bool center = false)
        {
            string themeName = ThemeBrushName(style);
            var themeBrush = context.GetBrush(themeName);
            var baseBrush = themeBrush ?? context.GetBrush("ChatLog.Text") ?? context.DefaultBrush ?? new Brush();
            var brush = baseBrush.Clone();
            brush.TextHorizontalAlignment = center ? TextHorizontalAlignment.Center : TextHorizontalAlignment.Left;
            brush.TextVerticalAlignment = TextVerticalAlignment.Center;

            // Kit.xml 存在时字号/颜色由热重载的主题文件决定；缺失时回退到 C# token。
            if (themeBrush == null)
            {
                switch (style)
                {
                    case KitTextStyle.Title:
                        brush.FontSize = FontTitle;
                        brush.FontColor = Ink;
                        break;
                    case KitTextStyle.Heading:
                        brush.FontSize = FontHeading;
                        brush.FontColor = Gold;
                        break;
                    case KitTextStyle.Muted:
                        brush.FontSize = FontBody;
                        brush.FontColor = Muted;
                        break;
                    case KitTextStyle.Caption:
                        brush.FontSize = FontCaption;
                        brush.FontColor = Muted;
                        break;
                    default:
                        brush.FontSize = FontBody;
                        brush.FontColor = Ink;
                        break;
                }
            }

            return brush;
        }

        public static string ThemeBrushName(KitTextStyle style)
        {
            switch (style)
            {
                case KitTextStyle.Title: return "Kit.Text.Title";
                case KitTextStyle.Heading: return "Kit.Text.Heading";
                case KitTextStyle.Muted: return "Kit.Text.Muted";
                case KitTextStyle.Caption: return "Kit.Text.Caption";
                default: return "Kit.Text.Body";
            }
        }

        public static Brush ButtonBrush(UIContext context, KitButtonStyle style)
        {
            string baseName = style == KitButtonStyle.Ghost ? "ButtonSimpleBrush" : "ButtonBrush2";
            var baseBrush = context.GetBrush(baseName) ?? context.DefaultBrush ?? new Brush();
            var brush = baseBrush.Clone();

            if (style == KitButtonStyle.Secondary)
                brush.Color = SecondaryButton;
            else if (style == KitButtonStyle.Danger)
                brush.Color = Blood;

            return brush;
        }

        public static Brush ButtonTextBrush(UIContext context)
        {
            var themeBrush = context.GetBrush("Kit.Button.Text");
            if (themeBrush != null)
            {
                var clone = themeBrush.Clone();
                clone.TextHorizontalAlignment = TextHorizontalAlignment.Center;
                clone.TextVerticalAlignment = TextVerticalAlignment.Center;
                return clone;
            }

            var brush = TextBrush(context, KitTextStyle.Body, center: true);
            brush.FontSize = 16;
            return brush;
        }
    }
}
