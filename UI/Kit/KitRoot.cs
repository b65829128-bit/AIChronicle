using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.Library;

namespace AIChronicle
{
    /// <summary>
    /// Kit 屏幕的根节点：包住 KitHost 的根 Widget，负责清空与添加。
    /// </summary>
    public sealed class KitRoot
    {
        public UIContext Context { get; }
        public Widget Host { get; }

        internal KitRoot(UIContext context, Widget host)
        {
            Context = context;
            Host = host;
        }

        public void Clear()
        {
            Host.RemoveAllChildren();
        }

        public void Add(Widget widget)
        {
            Host.AddChild(widget);
        }

        public void Add(KitContainer container)
        {
            Add(container.Root);
        }

        public Widget Scrim()
        {
            var scrim = Kit.Surface(Context, KitTheme.Scrim);
            scrim.WidthSizePolicy = SizePolicy.StretchToParent;
            scrim.HeightSizePolicy = SizePolicy.StretchToParent;
            Add(scrim);
            return scrim;
        }
    }
}
