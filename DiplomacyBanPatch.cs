using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Library;
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

    public static class DiplomacyBanPatch
    {
        public static bool BanAllianceCanMakeDecision(StartAllianceDecision __instance, ref bool __result, ref TextObject reason)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true
                && !AIChatClient.IsAgentDiplomacyInProgress
                && __instance.ProposerClan == Clan.PlayerClan)
            {
                __result = false;
                reason = new TextObject("{=MFM_diplo_ban}原版外交已禁用。请按 M 键打开秘书处，通过 AI 执行外交决策。", null);
                return false;
            }
            return true;
        }

        public static bool BanTradeCanMakeDecision(TradeAgreementDecision __instance, ref bool __result, ref TextObject reason)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true
                && !AIChatClient.IsAgentDiplomacyInProgress
                && __instance.ProposerClan == Clan.PlayerClan)
            {
                __result = false;
                reason = new TextObject("{=MFM_diplo_ban}原版外交已禁用。请按 M 键打开秘书处，通过 AI 执行外交决策。", null);
                return false;
            }
            return true;
        }

        public static bool BanAllianceModel(IFaction evaluatingFaction, ref bool __result, ref TextObject reason)
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

        public static bool BanTradeModel(Kingdom kingdom, ref bool __result, ref TextObject reason)
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

        public static bool BanAddDecision(KingdomDecision decision)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true
                && !AIChatClient.IsAgentDiplomacyInProgress
                && decision.ProposerClan == Clan.PlayerClan)
                return false;
            return true;
        }

        public static bool BanDeclareWar(IFaction faction1)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true
                && !AIChatClient.IsAgentDiplomacyInProgress
                && faction1 == Hero.MainHero.MapFaction)
                return false;
            return true;
        }

        public static bool BanMakePeace(IFaction faction1)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true
                && !AIChatClient.IsAgentDiplomacyInProgress
                && faction1 == Hero.MainHero.MapFaction)
                return false;
            return true;
        }
    }
}
