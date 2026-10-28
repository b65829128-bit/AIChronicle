using System.Threading;
using System.Threading.Tasks;

namespace AIChronicle
{
    /// <summary>Web 内容页的原生宿主：KitTexturePanel + WebPageService。</summary>
    public sealed class WebPageView
    {
        public KitTexturePanel Panel { get; }

        public WebPageView(TaleWorlds.GauntletUI.UIContext context)
        {
            Panel = new KitTexturePanel(context);
        }

        public async Task<bool> ShowAsync(WebPageModel model, int width, int height, int scale,
            CancellationToken cancellationToken = default)
        {
            // 调用方必须在游戏主线程上 await 之后继续执行；引擎纹理创建只能在主线程。
            byte[]? png = await WebPageService.RenderPngAsync(model, width, height, scale, cancellationToken);
            if (png == null)
                return false;

            return Panel.SetPng(png);
        }
    }
}
