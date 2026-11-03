namespace AIChronicle
{
    /// <summary>当前游戏公开身份的快照，只包含事实；与 persona、认知和实体头衔缓存独立。</summary>
    internal sealed class IdentityFacts
    {
        public string Name { get; init; } = "";
        public bool IsFemale { get; init; }
        public string ClanName { get; init; } = "";
        public bool IsClanLeader { get; init; }
        public bool IsLord { get; init; }
        public string KingdomName { get; init; } = "";
        public bool IsRuler { get; init; }
        public bool IsUnderMercenaryService { get; init; }
        public string RulerName { get; init; } = "";
        public bool? RulerIsFemale { get; init; }

        public const string UnfamiliarKnowledge = "暂无关于对方的私人认知记录。对方的公开身份以当前系统事实为准。";

        private string PoliticalStatus()
        {
            if (KingdomName.Length > 0)
            {
                if (IsRuler) return "王国统治者";
                if (IsUnderMercenaryService) return IsClanLeader ? "雇佣兵首领" : "雇佣兵成员";
                if (IsLord || IsClanLeader) return "封臣";
                return ClanName.Length > 0 ? "王国家族成员" : "王国居民";
            }
            if (ClanName.Length > 0) return IsClanLeader ? "独立家族领袖" : "独立家族成员";
            return "无王国身份";
        }

        public string Describe(string subject)
        {
            var clan = ClanName.Length == 0 ? "无" : ClanName + (IsClanLeader ? "（领袖）" : "（成员）");
            var kingdom = KingdomName.Length == 0 ? "无" : KingdomName;
            var text = $"{subject}：{(Name.Length == 0 ? "姓名未知" : Name)}（{(IsFemale ? "女" : "男")}）；家族：{clan}；所属王国：{kingdom}；政治身份：{PoliticalStatus()}。";
            if (KingdomName.Length == 0)
                return text + $"\n{subject}所属王国的统治者：无（当前未属于任何王国）。";
            var ruler = RulerName.Length == 0 ? "未知（需要时查询，不凭旧记忆猜测）"
                : RulerName + "（" + (RulerIsFemale.HasValue ? RulerIsFemale.Value ? "女" : "男" : "性别未知") + "）";
            return text + $"\n{subject}所属王国的统治者：{ruler}。";
        }

        public static string DescribePair(IdentityFacts agent, IdentityFacts? target)
        {
            return "当前身份事实（身份与君主称谓以此为准，优先于旧对话、persona 和记忆）：\n"
                + agent.Describe("你") + (target == null ? "" : "\n" + target.Describe("对方"));
        }
    }
}
