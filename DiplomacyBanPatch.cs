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
            if (MySettings.Instance?.BanVanillaDiplomacy == true)
            {
                __result = null;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomPeaceDecision")]
    public static class BanPeaceDecisionPatch
    {
        public static bool Prefix(ref KingdomDecision? __result)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true)
            {
                __result = null;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomStartingAllianceDecision")]
    public static class BanAllianceDecisionPatch
    {
        public static bool Prefix(ref KingdomDecision? __result)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true)
            {
                __result = null;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomTradeAgreementDecision")]
    public static class BanTradeDecisionPatch
    {
        public static bool Prefix(ref KingdomDecision? __result)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true)
            {
                __result = null;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(DeclareWarAction), "ApplyByKingdomDecision")]
    public static class BanDeclareWarPatch
    {
        public static bool Prefix(IFaction faction1)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true
                && !AIChatClient.IsAgentDiplomacyInProgress
                && faction1 == Hero.MainHero.MapFaction)
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(MakePeaceAction), "ApplyByKingdomDecision")]
    public static class BanMakePeacePatch
    {
        public static bool Prefix(IFaction faction1)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true
                && !AIChatClient.IsAgentDiplomacyInProgress
                && faction1 == Hero.MainHero.MapFaction)
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(DefaultAllianceModel), "CanMakeAlliance")]
    public static class BanAllianceModelPatch
    {
        public static void Postfix(Kingdom kingdom, Kingdom targetKingdom, IFaction evaluatingFaction, ref bool __result)
        {
            if (__result && MySettings.Instance?.BanVanillaDiplomacy == true
                && !AIChatClient.IsAgentDiplomacyInProgress
                && evaluatingFaction == Hero.MainHero.Clan)
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(DefaultTradeAgreementModel), "CanMakeTradeAgreement")]
    public static class BanTradeModelPatch
    {
        public static void Postfix(Kingdom kingdom, Kingdom other, ref bool __result)
        {
            if (__result && MySettings.Instance?.BanVanillaDiplomacy == true
                && !AIChatClient.IsAgentDiplomacyInProgress
                && kingdom == Hero.MainHero.MapFaction)
            {
                __result = false;
            }
        }
    }
}
