using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;

namespace MyFirstMod
{
    public class SubModule : MBSubModuleBase
    {
        private static Hero? _pendingChatHero;
        private static Hero? _pendingLetterHero;
        private static LordChatBehavior? _chatBehavior;
        private static bool _prevLetterO;
        private static bool _prevChanceryP;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            var modulePath = ModuleHelper.GetModuleFullPath("MyFirstMod");
            PromptManager.Initialize(modulePath);

            var harmony = new Harmony("MyFirstMod");
            harmony.PatchAll();
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();

            InformationManager.DisplayMessage(new InformationMessage(
                "[MyFirstMod] AI 聊天模组已加载！",
                Colors.Green));
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarter)
        {
            base.OnGameStart(game, gameStarter);

            if (game.GameType is Campaign && gameStarter is CampaignGameStarter starter)
            {
                _chatBehavior = new LordChatBehavior();
                starter.AddBehavior(_chatBehavior);

                var kdpbType = Type.GetType("TaleWorlds.CampaignSystem.CampaignBehaviors.KingdomDecisionProposalBehavior, TaleWorlds.CampaignSystem");
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[MyFirstMod] KDPB type: {(kdpbType != null ? kdpbType.FullName : "NOT FOUND")}", kdpbType != null ? Colors.Green : Colors.Red));

                if (kdpbType != null)
                {
                    var harmony = new Harmony("MyFirstMod.Diplomacy");
                    var regMethod = kdpbType.GetMethod("RegisterEvents", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (regMethod != null)
                        harmony.Patch(regMethod,
                            postfix: new HarmonyMethod(typeof(SubModule).GetMethod(nameof(KdpbRegisterPatched), BindingFlags.Static | BindingFlags.NonPublic)));

                    var prefix = new HarmonyMethod(typeof(SubModule).GetMethod(nameof(BlockDiplomacyDecision), BindingFlags.Static | BindingFlags.NonPublic));

                    var warM = kdpbType.GetMethod("GetRandomWarDecision", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (warM != null) harmony.Patch(warM, prefix: prefix);

                    var peaceM = kdpbType.GetMethod("GetRandomPeaceDecision", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (peaceM != null) harmony.Patch(peaceM, prefix: prefix);

                    var allianceM = kdpbType.GetMethod("GetRandomStartingAllianceDecision", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (allianceM != null) harmony.Patch(allianceM, prefix: prefix);

                    var tradeM = kdpbType.GetMethod("GetRandomTradeAgreementDecision", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (tradeM != null) harmony.Patch(tradeM, prefix: prefix);
                }

                var diplomVmType = Type.GetType("TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy.KingdomDiplomacyVM, TaleWorlds.CampaignSystem.ViewModelCollection");
                if (diplomVmType != null)
                {
                    var uiMethod = diplomVmType.GetMethod("GetAreProposalActionsEnabledWithReason", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (uiMethod != null)
                    {
                        var harmony = new Harmony("MyFirstMod.DiplomacyUI");
                        harmony.Patch(uiMethod,
                            prefix: new HarmonyMethod(typeof(SubModule).GetMethod(nameof(BlockDiplomacyUI), BindingFlags.Static | BindingFlags.NonPublic)));
                    }
                }
            }
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);

            PartyBehaviorManager.Tick();
            AIChatClient.CheckPendingInquiry();
            AgentScheduler.Tick();
            AgentScheduler.CheckPlayerProposal();

            var oDown = Input.IsKeyDown(InputKey.O);
            if (oDown && !_prevLetterO && Campaign.Current != null)
            {
                if (!AIChatScreen.IsOpen && !LetterListScreen.IsOpen)
                    LetterListScreen.Open();
                else if (LetterListScreen.IsOpen)
                    LetterListScreen.Close();
            }
            _prevLetterO = oDown;

            var mDown = Input.IsKeyDown(InputKey.M);
            if (mDown && !_prevChanceryP && Campaign.Current != null)
            {
                if (!AIChatScreen.IsOpen && !LetterListScreen.IsOpen)
                    AIChatScreen.OpenChancery();
            }
            _prevChanceryP = mDown;

            if (_pendingChatHero != null)
            {
                var hero = _pendingChatHero;
                _pendingChatHero = null;
                try
                {
                    AIChatScreen.DoOpen(hero);
                }
                catch (Exception ex)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"[MyFirstMod] 打开聊天窗口异常：{ex.Message}",
                        Colors.Red));
                }
            }

            if (_pendingLetterHero != null)
            {
                var hero = _pendingLetterHero;
                _pendingLetterHero = null;
                try
                {
                    AIChatScreen.DoOpenLetter(hero);
                }
                catch (Exception ex)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"[MyFirstMod] 打开写信窗口异常：{ex.Message}",
                        Colors.Red));
                }
            }
        }

        public static void RequestChatOpen(Hero hero)
        {
            _pendingChatHero = hero;
        }

        public static void RequestLetterOpen(Hero hero)
        {
            _pendingLetterHero = hero;
        }

        private static void KdpbRegisterPatched()
        {
            InformationManager.DisplayMessage(new InformationMessage(
                "[MyFirstMod] KDPB.RegisterEvents was called (manual patch works!)",
                Colors.Green));
        }

        private static bool BlockDiplomacyDecision(ref object __result)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true)
            {
                __result = null;
                return false;
            }
            return true;
        }

        private static bool BlockDiplomacyUI(ref object disabledReason, ref bool __result)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true)
            {
                disabledReason = new TextObject("外交已被接管，请使用 M 键秘书处处理外交事务。");
                __result = false;
                return false;
            }
            return true;
        }

        public static List<string> GetKnownNpcIds()
        {
            return _chatBehavior?.KnownNpcIds ?? new List<string>();
        }
    }

    [HarmonyPatch(typeof(TaleWorlds.CampaignSystem.GameComponents.DefaultBattleRewardModel),
                  "CalculateRenownGain")]
    public static class DoubleRenownPatch
    {
        public static void Postfix(ref float __result)
        {
            if (MySettings.Instance?.DoubleRenownEnabled == true)
            {
                __result *= 2f;
            }
        }
    }
}
