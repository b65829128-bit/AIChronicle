using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

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
            if (kingdom == null || kingdom.Clans.Count <= 1) return; // 单家族王国无需再分配

            // 取消投票标记：攻城后默认归国王氏族，避免 unassigned 的忠诚惩罚；国王 Agent 决定是否再分配
            settlement.Town.IsOwnerUnassigned = false;

            var ruler = kingdom.RulingClan?.Leader;
            if (ruler == null || ruler == Hero.MainHero) return; // 国王是人（玩家）：由玩家经秘书处处理
            var rulerEntity = EntityManager.GetEntityByHero(ruler);
            if (rulerEntity == null) return;

            var content = $"王国新攻下{settlement.Name}，需要你决定其归属。"
                + "请审视「内政审视」报告中的封地分配，用 gift_fief 将这座城赐予合适的家族（可考虑战功、无地而失意的家族、王国的平衡）。"
                + "若你判断维持现状更妥，也可以不调整。";
            AgentScheduler.QueueEvent(new ActivationEvent
            {
                Type = ActivationEventType.KingDiplomacy,
                AgentId = rulerEntity.Id,
                TargetId = rulerEntity.Id,
                Content = content,
                Depth = 0
            });
        }
    }
}
