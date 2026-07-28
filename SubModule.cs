using HarmonyLib;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
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
        private static readonly List<string> _patchLog = new();

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            var modulePath = ModuleHelper.GetModuleFullPath("MyFirstMod");
            PromptManager.Initialize(modulePath);

            var harmony = new Harmony("MyFirstMod");
            harmony.PatchAll();

            try
            {
                var m = AccessTools.Method(typeof(TaleWorlds.CampaignSystem.Election.StartAllianceDecision), "CanMakeDecision");
                if (m != null) { harmony.Patch(m, prefix: new HarmonyMethod(typeof(DiplomacyBanPatch), nameof(DiplomacyBanPatch.BanAllianceCanMakeDecision))); _patchLog.Add("[PASS] StartAllianceDecision.CanMakeDecision"); }
                else _patchLog.Add("[FAIL] StartAllianceDecision.CanMakeDecision not found");
            }
            catch (Exception ex) { _patchLog.Add($"[FAIL] StartAllianceDecision.CanMakeDecision: {ex.Message}"); }

            try
            {
                var m = AccessTools.Method(typeof(TaleWorlds.CampaignSystem.Election.TradeAgreementDecision), "CanMakeDecision");
                if (m != null) { harmony.Patch(m, prefix: new HarmonyMethod(typeof(DiplomacyBanPatch), nameof(DiplomacyBanPatch.BanTradeCanMakeDecision))); _patchLog.Add("[PASS] TradeAgreementDecision.CanMakeDecision"); }
                else _patchLog.Add("[FAIL] TradeAgreementDecision.CanMakeDecision not found");
            }
            catch (Exception ex) { _patchLog.Add($"[FAIL] TradeAgreementDecision.CanMakeDecision: {ex.Message}"); }

            try
            {
                var m = AccessTools.Method(typeof(TaleWorlds.CampaignSystem.GameComponents.DefaultAllianceModel), "CanMakeAlliance");
                if (m != null) { harmony.Patch(m, prefix: new HarmonyMethod(typeof(DiplomacyBanPatch), nameof(DiplomacyBanPatch.BanAllianceModel))); _patchLog.Add("[PASS] DefaultAllianceModel.CanMakeAlliance"); }
                else _patchLog.Add("[FAIL] DefaultAllianceModel.CanMakeAlliance not found");
            }
            catch (Exception ex) { _patchLog.Add($"[FAIL] DefaultAllianceModel.CanMakeAlliance: {ex.Message}"); }

            try
            {
                var m = AccessTools.Method(typeof(TaleWorlds.CampaignSystem.GameComponents.DefaultTradeAgreementModel), "CanMakeTradeAgreement");
                if (m != null) { harmony.Patch(m, prefix: new HarmonyMethod(typeof(DiplomacyBanPatch), nameof(DiplomacyBanPatch.BanTradeModel))); _patchLog.Add("[PASS] DefaultTradeAgreementModel.CanMakeTradeAgreement"); }
                else _patchLog.Add("[FAIL] DefaultTradeAgreementModel.CanMakeTradeAgreement not found");
            }
            catch (Exception ex) { _patchLog.Add($"[FAIL] DefaultTradeAgreementModel.CanMakeTradeAgreement: {ex.Message}"); }

            try
            {
                var m = AccessTools.Method(typeof(TaleWorlds.CampaignSystem.Kingdom), "AddDecision", new[] { typeof(TaleWorlds.CampaignSystem.Election.KingdomDecision), typeof(bool) });
                if (m != null) { harmony.Patch(m, prefix: new HarmonyMethod(typeof(DiplomacyBanPatch), nameof(DiplomacyBanPatch.BanAddDecision))); _patchLog.Add("[PASS] Kingdom.AddDecision"); }
                else _patchLog.Add("[FAIL] Kingdom.AddDecision not found");
            }
            catch (Exception ex) { _patchLog.Add($"[FAIL] Kingdom.AddDecision: {ex.Message}"); }

            try
            {
                var m = AccessTools.Method(typeof(TaleWorlds.CampaignSystem.Actions.DeclareWarAction), "ApplyByKingdomDecision");
                if (m != null) { harmony.Patch(m, prefix: new HarmonyMethod(typeof(DiplomacyBanPatch), nameof(DiplomacyBanPatch.BanDeclareWar))); _patchLog.Add("[PASS] DeclareWarAction.ApplyByKingdomDecision"); }
                else _patchLog.Add("[FAIL] DeclareWarAction.ApplyByKingdomDecision not found");
            }
            catch (Exception ex) { _patchLog.Add($"[FAIL] DeclareWarAction.ApplyByKingdomDecision: {ex.Message}"); }

            try
            {
                var m = AccessTools.Method(typeof(TaleWorlds.CampaignSystem.Actions.MakePeaceAction), "ApplyByKingdomDecision");
                if (m != null) { harmony.Patch(m, prefix: new HarmonyMethod(typeof(DiplomacyBanPatch), nameof(DiplomacyBanPatch.BanMakePeace))); _patchLog.Add("[PASS] MakePeaceAction.ApplyByKingdomDecision"); }
                else _patchLog.Add("[FAIL] MakePeaceAction.ApplyByKingdomDecision not found");
            }
            catch (Exception ex) { _patchLog.Add($"[FAIL] MakePeaceAction.ApplyByKingdomDecision: {ex.Message}"); }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();

            InformationManager.DisplayMessage(new InformationMessage(
                "[MyFirstMod] AI 聊天模组已加载！",
                Colors.Green));

            foreach (var log in _patchLog)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[MyFirstMod] {log}",
                    log.StartsWith("[PASS]") ? Colors.Green : Colors.Red));
            }
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarter)
        {
            base.OnGameStart(game, gameStarter);

            if (game.GameType is Campaign && gameStarter is CampaignGameStarter starter)
            {
                _chatBehavior = new LordChatBehavior();
                starter.AddBehavior(_chatBehavior);
            }
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);

            AIChatClient.Tick();
            AIChatClient.CheckPendingInquiry();
            AgentScheduler.Tick();

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
