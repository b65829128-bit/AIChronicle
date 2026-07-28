using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;

namespace MyFirstMod
{
    [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomWarDecision")]
    public static class BanWarDecisionPatch
    {
        public static bool Prefix(ref KingdomDecision? __result)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true) { __result = null; return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomPeaceDecision")]
    public static class BanPeaceDecisionPatch
    {
        public static bool Prefix(ref KingdomDecision? __result)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true) { __result = null; return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomStartingAllianceDecision")]
    public static class BanAllianceDecisionPatch
    {
        public static bool Prefix(ref KingdomDecision? __result)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true) { __result = null; return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomTradeAgreementDecision")]
    public static class BanTradeDecisionPatch
    {
        public static bool Prefix(ref KingdomDecision? __result)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true) { __result = null; return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(DefaultAllianceModel), "CanMakeAlliance")]
    public static class BanAllianceModelPatch
    {
        public static bool Prefix(IFaction evaluatingFaction, ref bool __result, ref TextObject reason)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true
                && !AIChatClient.IsAgentDiplomacyInProgress
                && evaluatingFaction == Clan.PlayerClan)
            {
                __result = false;
                reason = new TextObject("{=MFM_diplo_ban}原版外交已禁用。请按 M 键打开秘书处，通过 AI 执行外交决策。", null);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(DefaultTradeAgreementModel), "CanMakeTradeAgreement")]
    public static class BanTradeModelPatch
    {
        public static bool Prefix(Kingdom kingdom, ref bool __result, ref TextObject reason)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true
                && !AIChatClient.IsAgentDiplomacyInProgress
                && kingdom == Hero.MainHero.MapFaction)
            {
                __result = false;
                reason = new TextObject("{=MFM_diplo_ban}原版外交已禁用。请按 M 键打开秘书处，通过 AI 执行外交决策。", null);
                return false;
            }
            return true;
        }
    }
}
