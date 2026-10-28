using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TaleWorlds.ModuleManager;

namespace AIChronicle
{
    /// <summary>
    /// Web 内容页服务：模型 → HTML → 渲染器 → PNG → 磁盘缓存。
    /// 未安装渲染器时返回 null，由调用方回退原生界面。
    /// </summary>
    public static class WebPageService
    {
        private const string TemplateFile = "WebUI/templates/page.html";

        public static IWebPageRenderer Renderer { get; set; } = new NullWebPageRenderer();
        public static bool IsAvailable => Renderer.IsAvailable;

        public static string CacheDirectory
        {
            get
            {
                var campaign = PromptManager.CampaignDir;
                if (string.IsNullOrEmpty(campaign))
                    campaign = Path.Combine(Path.GetTempPath(), "AIChronicleWebCache");
                return Path.Combine(campaign, "WebCache");
            }
        }

        public static async Task<byte[]?> RenderPngAsync(WebPageModel model, int width, int height,
            int scale, CancellationToken cancellationToken = default)
        {
            if (!Renderer.IsAvailable)
                return null;

            try
            {
                string json = JsonConvert.SerializeObject(model);
                string html = WebPageTemplateBuilder.Build(model, json);
                string hash = Hash(html + "|" + width + "x" + height + "@" + scale);
                string directory = CacheDirectory;
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, hash + ".png");

                if (File.Exists(path))
                    return File.ReadAllBytes(path);

                var request = new WebPageRequest
                {
                    Html = html,
                    BaseUrl = WebPageTemplateBuilder.BaseUrl,
                    Width = width,
                    Height = height,
                    Scale = scale
                };

                byte[]? png = await Renderer.RenderPngAsync(request, cancellationToken).ConfigureAwait(false);
                if (png == null || png.Length == 0)
                    return null;

                File.WriteAllBytes(path, png);
                return png;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Hash(string text)
        {
            using (var sha = SHA1.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes)
                    builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }

    internal static class WebPageTemplateBuilder
    {
        private const string TemplateFile = "WebUI/templates/page.html";
        private const string PagePlaceholder = "__PAGE_DATA__";
        private const string TitlePlaceholder = "__PAGE_TITLE__";
        private const string BasePlaceholder = "__BASE_URL__";

        public static string BaseUrl
        {
            get
            {
                try
                {
                    string module = ModuleHelper.GetModuleFullPath("AIChronicle");
                    string webUi = Path.Combine(module, "WebUI") + Path.DirectorySeparatorChar;
                    return new Uri(webUi).AbsoluteUri;
                }
                catch
                {
                    return "";
                }
            }
        }

        public static string Build(WebPageModel model, string json)
        {
            string template = LoadTemplate();
            string safeJson = json.Replace("</", "<\\/");
            string title = System.Net.WebUtility.HtmlEncode(model.Title ?? "");

            if (!template.Contains(PagePlaceholder))
            {
                template = template.Replace("</body>",
                    "<script>window.__PAGE__ = " + safeJson + ";</script></body>");
            }

            template = template
                .Replace(PagePlaceholder, safeJson)
                .Replace(TitlePlaceholder, title)
                .Replace(BasePlaceholder, BaseUrl);

            return template;
        }

        private static string LoadTemplate()
        {
            try
            {
                string module = ModuleHelper.GetModuleFullPath("AIChronicle");
                string path = Path.Combine(module, TemplateFile.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path))
                    return File.ReadAllText(path, Encoding.UTF8);
            }
            catch
            {
                // 回退到内嵌最小页面
            }

            return "<!doctype html><html><head><meta charset=\"utf-8\"><title>__PAGE_TITLE__</title></head>" +
                   "<body><div id=\"app\"></div><script>window.__PAGE__ = __PAGE_DATA__;</script></body></html>";
        }
    }
}
