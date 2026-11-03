using System;
using System.Text;

namespace AIChronicle
{
    /// <summary>
    /// 静默见闻的文本渲染：人物统一为「{国}·{族}家族的{名}」，不用代词。
    /// 纯逻辑，不依赖 TaleWorlds，可进纯逻辑测试。
    ///
    /// 为什么不用代词：日记靠 grep 检索，「他/她/其」会让关键词命中率归零；
    /// 也不符合项目的人称约定（docs/architecture.md §8）。
    /// </summary>
    public static class SilentDiaryText
    {
        private const int MaxTextLength = 120;

        /// <summary>人物：库赛特·乌尔克汗家族的巴图 / 德赫家族的炎萌 / 瓦兰迪亚的德瑟特。</summary>
        public static string Hero(string? kingdom, string? clan, string name, string? role = null)
        {
            var k = CleanPlain(kingdom);
            var c = NormalizeClan(CleanPlain(clan));
            var n = CleanPlain(name);
            if (n.Length == 0) n = "某人";

            var sb = new StringBuilder();
            if (k.Length > 0) sb.Append(k).Append(c.Length > 0 ? '·' : '的');
            if (c.Length > 0) sb.Append(c).Append('的');
            sb.Append(n);
            if (!string.IsNullOrWhiteSpace(role)) sb.Append('（').Append(CleanPlain(role)).Append('）');
            return sb.ToString();
        }

        /// <summary>定居点：乌尔克汗堡（南帝国）。</summary>
        public static string Settlement(string? name, string? kingdom)
        {
            var n = CleanPlain(name);
            if (n.Length == 0) n = "某地";
            var k = CleanPlain(kingdom);
            return k.Length == 0 ? n : n + "（" + k + "）";
        }

        /// <summary>去掉换行、首尾空白与句末标点，压缩连续空白，限长。</summary>
        public static string Clean(string? text)
        {
            var s = CleanPlain(text);
            s = s.TrimEnd('。', '．', '.', '；', ';', '，', ',', '、', ' ', '\t');
            if (s.Length > MaxTextLength) s = s.Substring(0, MaxTextLength - 1) + "…";
            return s;
        }

        private static string CleanPlain(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text!.Length);
            var lastSpace = false;
            foreach (var ch in text!)
            {
                if (ch == '\r' || ch == '\n' || ch == '\t')
                {
                    if (!lastSpace) { sb.Append(' '); lastSpace = true; }
                    continue;
                }
                sb.Append(ch);
                lastSpace = ch == ' ';
            }
            return sb.ToString().Trim();
        }

        private static string NormalizeClan(string clan)
        {
            if (clan.Length == 0) return "";
            if (clan.EndsWith("家族", StringComparison.Ordinal)
                || clan.EndsWith("氏族", StringComparison.Ordinal)
                || clan.EndsWith("团", StringComparison.Ordinal))
                return clan;
            return clan + "家族";
        }
    }
}
