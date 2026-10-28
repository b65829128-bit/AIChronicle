using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AIChronicle
{
    /// <summary>只读内容页的数据模型；C# 只负责填数据，模板负责排版。</summary>
    public sealed class WebPageModel
    {
        public string Template { get; set; } = "chronicle";
        public string Eyebrow { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public List<WebPageSection> Sections { get; set; } = new List<WebPageSection>();
        public string Note { get; set; } = "";
        public string Seal { get; set; } = "";
        public string Footer { get; set; } = "";
    }

    public sealed class WebPageSection
    {
        public string Heading { get; set; } = "";
        public List<string> Paragraphs { get; set; } = new List<string>();
    }

    public sealed class WebPageRequest
    {
        public string Html { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public int Width { get; set; } = 1200;
        public int Height { get; set; } = 1600;
        public int Scale { get; set; } = 1;
    }

    /// <summary>离屏 Web 渲染器。CEF/Ultralight 实现此接口；未安装时用 Null 实现回退。</summary>
    public interface IWebPageRenderer
    {
        bool IsAvailable { get; }
        Task<byte[]?> RenderPngAsync(WebPageRequest request, CancellationToken cancellationToken);
    }

    public sealed class NullWebPageRenderer : IWebPageRenderer
    {
        public bool IsAvailable => false;

        public Task<byte[]?> RenderPngAsync(WebPageRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult<byte[]?>(null);
        }
    }
}
