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
