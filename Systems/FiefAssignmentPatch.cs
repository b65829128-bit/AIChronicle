using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace AIChronicle
{
    /// <summary>
    /// 册封由 Agent 主导：拦截原版攻城后的封地影响力投票（SettlementClaimantDecision），
    /// 改由国王 Agent 决定归属（用 gift_fief 赐予合适的家族）。
    ///
    /// ⚠️ 注意：CampaignBehaviors 类在 OnSubModuleLoad 时未完成运行时初始化，
    /// [HarmonyPatch] 属性会被静默跳过——必须在 OnGameStart 中用 Type.GetType + harmony.Patch
    /// 手动注册（见 SubModule），不要依赖 PatchAll。
    /// </summary>
    public static class FiefAssignmentPatch
    {
        /// <summary>拦截 DailyTickSettlement：设置开启时跳过原版投票（SettlementClaimantDecision）。</summary>
        public static bool PrefixDailyTickSettlement()
        {
            return MySettings.Instance?.FiefAssignmentByAgent != true;
        }

        /// <summary>
        /// OnSettlementOwnerChanged 后处理：取消 unassigned 标记（防忠诚惩罚），并激活国王 Agent
        /// （P1 级，与外交提案同级）决定该城归属。攻城后的默认归属是王国（国王氏族），不区分攻城者
        /// 是玩家还是 AI——统一由国王决定。
        /// </summary>
        public static void PostfixOnSettlementOwnerChanged(
            Settlement settlement, bool openToClaim, Hero newOwner)
        {
            if (MySettings.Instance?.FiefAssignmentByAgent != true) return;
            if (settlement?.Town == null) return;           // 只处理城镇/城堡
            if (!openToClaim) return;                       // 非"开放归属"（如国王转让）不干预

            var kingdom = newOwner?.MapFaction as Kingdom;
            if (kingdom == null) return;

            // 取消投票标记：攻城后默认归国王氏族，避免 unassigned 的忠诚惩罚；国王 Agent 决定是否再分配
            settlement.Town.IsOwnerUnassigned = false;
            QueueKingFiefDecision(kingdom, new[] { settlement }, $"王国新攻下{settlement.Name}。");
        }

        /// <summary>
        /// 让受地国王先决定分封：条约割地/攻城后，封地暂归统治家族，国王可 gift_fief 赐予封臣，
        /// 也可保留王室直属。玩家国王收到 M 页提示，由玩家在「封地」页自行转让。
        /// 同一批封地只激活一次，避免多块割地刷屏。
        /// </summary>
        public static void QueueKingFiefDecision(Kingdom? kingdom, IReadOnlyList<Settlement>? settlements, string intro)
        {
            if (kingdom == null || settlements == null || settlements.Count == 0) return;
            if (kingdom.Clans.Count <= 1) return; // 单家族王国无需再分配

            var ruler = kingdom.RulingClan?.Leader;
            if (ruler == null || !ruler.IsAlive) return;

            var valid = settlements.Where(s => s != null).ToList();
            if (valid.Count == 0) return;
            var names = string.Join("、", valid.Select(s => s.Name?.ToString() ?? "?"));
            var lead = string.IsNullOrWhiteSpace(intro) ? $"王国获得{names}。" : intro;

            if (ruler == Hero.MainHero)
            {
                CuriaStore.Notify(
                    $"{lead}这些封地暂归统治家族。按 M 键参政页 → 封地，用「转让自己的封地」分封；建议至少分封一块，若确实没有合适家族才保留王室。",
                    Colors.Cyan);
                return;
            }

            var entityId = CuriaStore.EntityIdOf(ruler);
            if (string.IsNullOrEmpty(entityId)) return;
            var content = $"{lead}这些封地暂时归统治家族。请审视「内政审视」报告中的封地分配，"
                + "优先用 gift_fief 将合适的城赐予有功/无地的家族（可考虑战功、无地而失意的家族、王国的平衡）。"
                + "只有确实没有合适家族时，才保留王室直属。";
            AgentScheduler.QueueEvent(new ActivationEvent
            {
                Type = ActivationEventType.KingDiplomacy,
                AgentId = entityId,
                TargetId = entityId,
                Content = content,
                Depth = 0
            });
            CuriaStore.AppendDiary(entityId, "结果", $"{lead}这些封地暂归统治家族，需要决定分封。");
        }
    }
}
