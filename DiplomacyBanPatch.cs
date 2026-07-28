using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;
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

    [HarmonyPatch(typeof(DeclareWarAction), "ApplyByKingdomDecision")]
    public static class BanDeclareWarPatch
    {
        public static bool Prefix(IFaction faction1)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true
                && !AIChatClient.IsAgentDiplomacyInProgress
                && faction1 == Hero.MainHero.MapFaction)
                return false;
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
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(StartAllianceDecision), "CanMakeDecision")]
    public static class BanAllianceCanMakeDecisionPatch
    {
        public static bool Prefix(StartAllianceDecision __instance, ref bool __result, ref TextObject reason)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true
                && __instance.ProposerClan == Clan.PlayerClan)
            {
                __result = false;
                reason = new TextObject("{=MFM_ban}原版外交已禁用，请使用秘书处（M 键）执行外交决策。", null);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(TradeAgreementDecision), "CanMakeDecision")]
    public static class BanTradeCanMakeDecisionPatch
    {
        public static bool Prefix(TradeAgreementDecision __instance, ref bool __result, ref TextObject reason)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true
                && __instance.ProposerClan == Clan.PlayerClan)
            {
                __result = false;
                reason = new TextObject("{=MFM_ban}原版外交已禁用，请使用秘书处（M 键）执行外交决策。", null);
                return false;
            }
            return true;
        }
    }
}
