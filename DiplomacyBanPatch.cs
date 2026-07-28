using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Library;
using Kdiplomacy = TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;

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

    [HarmonyPatch(typeof(Kdiplomacy.KingdomDiplomacyVM), "OnDeclareWar")]
    public static class BanPlayerDeclareWarPatch
    {
        public static bool Prefix()
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[MyFirstMod] 原版外交已禁用。作为国王，请通过 AI 聊天执行外交决策。",
                    Colors.Yellow));
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Kdiplomacy.KingdomDiplomacyVM), "OnDeclarePeace")]
    public static class BanPlayerDeclarePeacePatch
    {
        public static bool Prefix()
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[MyFirstMod] 原版外交已禁用。作为国王，请通过 AI 聊天执行外交决策。",
                    Colors.Yellow));
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Kdiplomacy.KingdomDiplomacyVM), "OnStartAlliance")]
    public static class BanPlayerStartAlliancePatch
    {
        public static bool Prefix()
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[MyFirstMod] 原版外交已禁用。作为国王，请通过 AI 聊天执行外交决策。",
                    Colors.Yellow));
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Kdiplomacy.KingdomDiplomacyVM), "OnStartTradeAgreement")]
    public static class BanPlayerStartTradeAgreementPatch
    {
        public static bool Prefix()
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[MyFirstMod] 原版外交已禁用。作为国王，请通过 AI 聊天执行外交决策。",
                    Colors.Yellow));
                return false;
            }
            return true;
        }
    }
}
