using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;

namespace AIChronicle
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

    [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "RegisterEvents")]
    public static class DiplomacyBanVerifyPatch
    {
        public static void Postfix()
        {
            InformationManager.DisplayMessage(new InformationMessage(
                "[AI编年史] DiplomacyBanPatch: KingdomDecisionProposalBehavior.RegisterEvents patched successfully, BanVanillaDiplomacy="
                + (MySettings.Instance?.BanVanillaDiplomacy == true),
                Colors.Green));
        }
    }

    [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "DailyTickClan")]
    public static class DiplomacyBanDailyTickPatch
    {
        private static bool _once;

        public static void Prefix()
        {
            if (_once) return;
            _once = true;
            InformationManager.DisplayMessage(new InformationMessage(
                "[AI编年史] DiplomacyBanPatch: first DailyTickClan fired",
                Colors.Green));
        }
    }
}
