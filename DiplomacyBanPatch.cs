using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;

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

    [HarmonyPatch(typeof(DeclareWarDecision), "ApplyChosenOutcome")]
    public static class BanDeclareWarApplyPatch
    {
        public static bool Prefix(DeclareWarDecision __instance)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true
                && !AIChatClient.IsAgentDiplomacyInProgress
                && __instance.ProposerClan == Clan.PlayerClan)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(MakePeaceKingdomDecision), "ApplyChosenOutcome")]
    public static class BanPeaceApplyPatch
    {
        public static bool Prefix(MakePeaceKingdomDecision __instance)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true
                && !AIChatClient.IsAgentDiplomacyInProgress
                && __instance.ProposerClan == Clan.PlayerClan)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(StartAllianceDecision), "ApplyChosenOutcome")]
    public static class BanAllianceApplyPatch
    {
        public static bool Prefix(StartAllianceDecision __instance)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true
                && !AIChatClient.IsAgentDiplomacyInProgress
                && __instance.ProposerClan == Clan.PlayerClan)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(TradeAgreementDecision), "ApplyChosenOutcome")]
    public static class BanTradeApplyPatch
    {
        public static bool Prefix(TradeAgreementDecision __instance)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true
                && !AIChatClient.IsAgentDiplomacyInProgress
                && __instance.ProposerClan == Clan.PlayerClan)
                return false;
            return true;
        }
    }
}
