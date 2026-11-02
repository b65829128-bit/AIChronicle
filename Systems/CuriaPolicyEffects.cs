using System;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace AIChronicle
{
    /// <summary>
    /// 朝议接管原版影响力投票后，4 条政策中依赖“影响力成本/推翻决策”的条款失效。
    /// 这里把它们翻译成朝议原生修正，并统一给 UI / AI / 门槛计算读取。
    ///
    /// 只处理真正受影响的条款：
    /// - Precarial Land Tenure：原本降低封地归并影响力成本 → 封地转让/夺封门槛 -8%
    /// - Feudal Inheritance：原本提高剥夺封地影响力成本 → 封地转让/夺封门槛 +8%
    /// - War Tax：原本提高宣战影响力成本 → 宣战门槛 +10%
    /// - Royal Privilege：原本降低推翻决策影响力成本 → 国王家族票权 ×1.2
    ///
    /// 其余政策效果（税收/忠诚/军团/影响力增减等）仍由原版模型读取 ActivePolicies 生效。
    /// </summary>
    public static class CuriaPolicyEffects
    {
        public const string PrecarialLandTenure = "policy_precarial_land_tenure";
        public const string FeudalInheritance = "policy_feudal_inheritance";
        public const string WarTax = "policy_war_tax";
        public const string RoyalPrivilege = "policy_royal_privilege";

        public const float FiefThresholdStep = 0.08f;
        public const float WarThresholdStep = 0.10f;
        public const float KingWeightMultiplier = 1.20f;

        public static bool Has(Kingdom? kingdom, string policyId)
        {
            if (kingdom == null || string.IsNullOrEmpty(policyId)) return false;
            return kingdom.ActivePolicies.Any(p => string.Equals(p.StringId, policyId, StringComparison.OrdinalIgnoreCase));
        }

        public static float FiefThresholdDelta(Kingdom? kingdom)
        {
            var delta = 0f;
            if (Has(kingdom, PrecarialLandTenure)) delta -= FiefThresholdStep;
            if (Has(kingdom, FeudalInheritance)) delta += FiefThresholdStep;
            return delta;
        }

        public static float WarThresholdDelta(Kingdom? kingdom)
        {
            return Has(kingdom, WarTax) ? WarThresholdStep : 0f;
        }

        public static float KingVoteWeightMultiplier(Kingdom? kingdom)
        {
            return Has(kingdom, RoyalPrivilege) ? KingWeightMultiplier : 1f;
        }

        public static float AdjustThreshold(Kingdom? kingdom, CuriaAgendaType type, float baseThreshold, bool hasCession = false)
        {
            var value = baseThreshold;
            if (type == CuriaAgendaType.TransferFief || hasCession) value += FiefThresholdDelta(kingdom);
            if (type == CuriaAgendaType.DeclareWar || (type == CuriaAgendaType.ProposeTreaty && !hasCession))
                value += WarThresholdDelta(kingdom);
            return Math.Max(0.05f, Math.Min(0.95f, value));
        }

        /// <summary>某条政策的朝议原生效果；无替换则空串。</summary>
        public static string DescribePolicy(PolicyObject? policy)
        {
            if (policy == null) return "";
            return policy.StringId switch
            {
                PrecarialLandTenure => "封地转让/割地条约门槛 −8%",
                FeudalInheritance => "封地转让/割地条约门槛 +8%",
                WarTax => "宣战/战前条约门槛 +10%",
                RoyalPrivilege => "国王家族票权 ×1.2",
                _ => ""
            };
        }

        /// <summary>当前王国所有生效的朝议修正，用于 UI 提醒与 AI 投票提示。</summary>
        public static string DescribeKingdom(Kingdom? kingdom)
        {
            if (kingdom == null) return "";
            var parts = new System.Collections.Generic.List<string>();
            if (Has(kingdom, PrecarialLandTenure)) parts.Add("临时土地占有：封地/割地条约门槛 −8%");
            if (Has(kingdom, FeudalInheritance)) parts.Add("封建继承：封地/割地条约门槛 +8%");
            if (Has(kingdom, WarTax)) parts.Add("战争税：宣战/战前条约门槛 +10%");
            if (Has(kingdom, RoyalPrivilege)) parts.Add("王室特权：国王票权 ×1.2");
            return string.Join("；", parts);
        }
    }
}
