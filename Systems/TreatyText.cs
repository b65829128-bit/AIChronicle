using System;
using System.Collections.Generic;

namespace AIChronicle
{
    /// <summary>
    /// 条约文案的纯逻辑部分：不依赖 TaleWorlds，供游戏内与纯逻辑测试共用。
    /// 所有描述都从“谁提出、谁接受”的视角生成，避免把割让/赔款方向说反。
    /// </summary>
    public static class TreatyText
    {
        public static string NormalizeCedeFrom(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            switch (raw!.Trim().ToLowerInvariant())
            {
                case "proposer":
                case "us":
                case "our":
                case "ours":
                case "self":
                case "我方":
                case "我国":
                case "我们":
                    return "proposer";
                case "target":
                case "them":
                case "their":
                case "enemy":
                case "对方":
                case "敌方":
                case "他国":
                    return "target";
                default:
                    return "";
            }
        }

        public static string CedeFromLabel(string cedeFrom)
        {
            return cedeFrom == "proposer" ? "我方割让" : cedeFrom == "target" ? "对方割让" : "不割地";
        }

        /// <summary>提议者视角（朝议议程、投票提示）。</summary>
        public static string Describe(TreatyTerms terms, string proposerName, string targetName)
        {
            return Build(terms, proposerName, targetName, perspective: "proposer");
        }

        /// <summary>接受方视角（对方提案激活、M 页外国提案卡片）。</summary>
        public static string DescribeForTarget(TreatyTerms terms, string proposerName, string targetName)
        {
            return Build(terms, proposerName, targetName, perspective: "target");
        }

        /// <summary>中立视角（史料、双方家族领袖日记；双方看到同一段话不会把“我方”理解反）。</summary>
        public static string DescribeNeutral(TreatyTerms terms, string proposerName, string targetName)
        {
            return Build(terms, proposerName, targetName, perspective: "neutral");
        }

        private static string Build(TreatyTerms? terms, string proposerName, string targetName, string perspective)
        {
            if (terms == null) return "";
            var parts = new List<string>();
            var cessions = terms.GetCessions();
            if (cessions.Count > 0)
            {
                var proposerCessions = new List<TreatyCession>();
                var targetCessions = new List<TreatyCession>();
                foreach (var c in cessions)
                {
                    // 非法方向在校验阶段会被拒绝；文案阶段按对方割让处理，避免吞掉条款。
                    if (NormalizeCedeFrom(c.CedeFrom) == "proposer") proposerCessions.Add(c);
                    else targetCessions.Add(c);
                }
                if (proposerCessions.Count > 0)
                    parts.Add(CessionClause(proposerCessions, true, proposerName, targetName, perspective));
                if (targetCessions.Count > 0)
                    parts.Add(CessionClause(targetCessions, false, proposerName, targetName, perspective));
            }

            if (terms.HasTribute())
            {
                var amount = Math.Abs(terms.TributeAmount);
                var total = (long)amount * terms.TributeDays;
                var payerIsProposer = terms.TributeAmount > 0;
                if (perspective == "proposer")
                    parts.Add(payerIsProposer
                        ? $"我方每日向{targetName}支付 {amount} 金，持续 {terms.TributeDays} 天（共 {total} 金）"
                        : $"对方每日向我方支付 {amount} 金，持续 {terms.TributeDays} 天（共 {total} 金）");
                else if (perspective == "target")
                    parts.Add(payerIsProposer
                        ? $"对方每日向你方支付 {amount} 金，持续 {terms.TributeDays} 天（共 {total} 金）"
                        : $"你方每日向{proposerName}支付 {amount} 金，持续 {terms.TributeDays} 天（共 {total} 金）");
                else
                    parts.Add(payerIsProposer
                        ? $"{proposerName}每日向{targetName}支付 {amount} 金，持续 {terms.TributeDays} 天（共 {total} 金）"
                        : $"{targetName}每日向{proposerName}支付 {amount} 金，持续 {terms.TributeDays} 天（共 {total} 金）");
            }

            if (terms.TruceDays > 0)
                parts.Add($"签约后双方休战 {terms.TruceDays} 天");

            if (parts.Count == 0) parts.Add("不附带割地或赔款条件");

            if (terms.WarOnReject)
            {
                if (perspective == "proposer") parts.Add("若对方拒绝，我方将宣战");
                else if (perspective == "target") parts.Add("若你拒绝，对方将宣战");
                else parts.Add($"若{targetName}拒绝，{proposerName}将宣战");
            }

            return string.Join("；", parts);
        }

        private static string CessionClause(List<TreatyCession> cessions, bool proposerCedes,
            string proposerName, string targetName, string perspective)
        {
            var names = CessionNames(cessions);
            if (perspective == "proposer")
                return proposerCedes
                    ? $"我方将{names}割让给{targetName}"
                    : $"对方将{names}割让给我方";
            if (perspective == "target")
                return proposerCedes
                    ? $"对方将{names}割让给你方"
                    : $"你方将{names}割让给{proposerName}";
            return proposerCedes
                ? $"{proposerName}将{names}割让给{targetName}"
                : $"{targetName}将{names}割让给{proposerName}";
        }

        /// <summary>解析 AI 工具的多割地简写：side:城名;side:城名（side = proposer/target，支持我方/对方）。</summary>
        public static List<TreatyCession> ParseCessionSpec(string? text)
        {
            var result = new List<TreatyCession>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            var segments = text!.Split(new[] { ';', '；', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in segments)
            {
                var segment = raw.Trim();
                if (segment.Length == 0) continue;
                var idx = segment.IndexOfAny(new[] { ':', '：' });
                string side;
                string name;
                if (idx >= 0)
                {
                    side = NormalizeCedeFrom(segment.Substring(0, idx));
                    name = segment.Substring(idx + 1).Trim();
                }
                else
                {
                    side = "";
                    name = segment;
                }
                if (string.IsNullOrWhiteSpace(name)) continue;
                result.Add(new TreatyCession { CedeFrom = side, SettlementName = name });
            }
            return result;
        }

        public static string CessionNames(IReadOnlyList<TreatyCession> cessions)
        {
            var names = new List<string>();
            foreach (var c in cessions)
            {
                var name = !string.IsNullOrWhiteSpace(c.SettlementName)
                    ? c.SettlementName.Trim()
                    : (c.SettlementId ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(name)) names.Add($"「{name}」");
            }
            return names.Count == 0 ? "某处封地" : string.Join("、", names);
        }

        public static string SignedSummary(string proposerName, string targetName, TreatyTerms terms)
        {
            return $"{proposerName}与{targetName}签订条约：{DescribeNeutral(terms, proposerName, targetName)}";
        }

        public static string RejectedSummary(string proposerName, string targetName, TreatyTerms? terms)
        {
            if (terms == null || !terms.HasAnyTerm())
                return $"{targetName}拒绝了{proposerName}的条约提议";
            return $"{targetName}拒绝了{proposerName}的条约：{DescribeNeutral(terms, proposerName, targetName)}";
        }
    }
}
