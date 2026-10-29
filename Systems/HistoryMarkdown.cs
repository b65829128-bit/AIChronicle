using System;
using System.Text;
using System.Text.RegularExpressions;

namespace AIChronicle
{
    /// <summary>
    /// 把史官成文的 Markdown 转成原生 RichText 可显示的纯文本。
    /// 原生史书只是 CEF 失败/关闭时的 fallback，不追求完整 Markdown 语义，
    /// 只去掉标题、强调、列表、引用、代码等标记，保留正文与行间空行。
    /// </summary>
    public static class HistoryMarkdown
    {
        private static readonly Regex Heading = new Regex(@"^\s{0,3}#{1,6}\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex Fence = new Regex(@"^\s*```", RegexOptions.Compiled);
        private static readonly Regex HorizontalRule = new Regex(@"^\s*(?:-{3,}|\*{3,}|_{3,})\s*$", RegexOptions.Compiled);
        private static readonly Regex BlockQuote = new Regex(@"^\s*>\s?", RegexOptions.Compiled);
        private static readonly Regex ListMarker = new Regex(@"^\s*(?:[-*+]|\d+[.)])\s+", RegexOptions.Compiled);
        private static readonly Regex Image = new Regex(@"!\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
        private static readonly Regex Link = new Regex(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
        private static readonly Regex HtmlTag = new Regex(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex ItalicAsterisk = new Regex(@"\*([^*\n]+)\*", RegexOptions.Compiled);
        private static readonly Regex ItalicUnderscore = new Regex(@"_([^_\n]+)_", RegexOptions.Compiled);

        public static string ToPlainText(string? markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return "";

            var normalized = markdown!.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = normalized.Split('\n');
            var output = new StringBuilder();
            bool inFence = false;

            foreach (var rawLine in lines)
            {
                var line = rawLine;

                if (Fence.IsMatch(line))
                {
                    inFence = !inFence;
                    continue;
                }

                if (inFence)
                {
                    output.AppendLine(line);
                    continue;
                }

                if (HorizontalRule.IsMatch(line))
                {
                    output.AppendLine();
                    continue;
                }

                var heading = Heading.Match(line);
                if (heading.Success)
                {
                    output.AppendLine(StripInline(heading.Groups[1].Value.Trim()));
                    continue;
                }

                line = BlockQuote.Replace(line, "");
                line = ListMarker.Replace(line, "");
                line = StripInline(line);
                output.AppendLine(line.TrimEnd());
            }

            var result = output.ToString();
            result = Regex.Replace(result, @"\n{3,}", "\n\n");
            return result.Trim('\n');
        }

        private static string StripInline(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            var value = Image.Replace(text, "$1");
            value = Link.Replace(value, "$1");
            value = HtmlTag.Replace(value, "");
            // 只去掉成对的强调标记，保留 CharacterObject_1664 这类带下划线的标识。
            value = value.Replace("**", "").Replace("__", "");
            value = value.Replace("`", "");
            value = ItalicAsterisk.Replace(value, "$1");
            value = ItalicUnderscore.Replace(value, "$1");
            return value;
        }
    }
}
