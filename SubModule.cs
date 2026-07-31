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
        private static bool _prevHistoryH;

        private static bool IsPlayerFreeOnMap()
        {
            if (Campaign.Current == null) return false;
            try
            {
                var state = Game.Current?.GameStateManager?.ActiveState;
                if (state == null) return false;
                return state.GetType().Name == "MapState";
            }
            catch { return true; }
        }

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            var modulePath = ModuleHelper.GetModuleFullPath("MyFirstMod");
            PromptManager.Initialize(modulePath);

            // OnSubModuleLoad 在游戏主线程执行——绑定主线程 ID，供工具主线程分发判断。
            MainThreadExecutor.Initialize();

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

            InformationManager.DisplayMessage(new InformationMessage(
                "[MyFirstMod] O键=信箱 | H键=史书 | M键=秘书处",
                Colors.Green));

            if (game.GameType is Campaign && gameStarter is CampaignGameStarter starter)
            {
                _chatBehavior = new LordChatBehavior();
                starter.AddBehavior(_chatBehavior);

                starter.AddBehavior(new HistoryRecorder());

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

                    var prefix = new HarmonyMethod(typeof(SubModule).GetMethod(nameof(BlockDiplomacyDecisionLogged), BindingFlags.Static | BindingFlags.NonPublic));

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

        /// <summary>战役结束（切档/退回主菜单/关游戏）时清空跨档残留状态，避免新档用到旧档的实体/计时器。</summary>
        public override void OnGameEnd(Game game)
        {
            base.OnGameEnd(game);
            EntityManager.ResetForNewCampaign();
            PartyBehaviorManager.ResetForNewCampaign();
            AgentScheduler.ResetForNewCampaign();
            DebugLogger.Reset();
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);

            MainThreadExecutor.Tick();
            PartyBehaviorManager.Tick();
            AIChatClient.CheckPendingInquiry();
            AgentScheduler.Tick();
            AgentScheduler.CheckPlayerProposal();

            var oDown = Input.IsKeyDown(InputKey.O);
            if (oDown && !_prevLetterO && IsPlayerFreeOnMap())
            {
                if (!AIChatScreen.IsOpen && !LetterListScreen.IsOpen && !HistoryScreen.IsOpen)
                    LetterListScreen.Open();
                else if (LetterListScreen.IsOpen)
                    LetterListScreen.Close();
            }
            _prevLetterO = oDown;

            var mDown = Input.IsKeyDown(InputKey.M);
            if (mDown && !_prevChanceryP && IsPlayerFreeOnMap())
            {
                if (!AIChatScreen.IsOpen && !LetterListScreen.IsOpen && !HistoryScreen.IsOpen)
                    AIChatScreen.OpenChancery();
            }
            _prevChanceryP = mDown;

            var hDown = Input.IsKeyDown(InputKey.H);
            if (hDown && !_prevHistoryH && IsPlayerFreeOnMap())
            {
                if (!AIChatScreen.IsOpen && !LetterListScreen.IsOpen && !HistoryScreen.IsOpen)
                    HistoryScreen.Open();
                else if (HistoryScreen.IsOpen)
                    HistoryScreen.Close();
            }
            _prevHistoryH = hDown;

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
            var enabled = MySettings.Instance?.BanVanillaDiplomacy == true;
            if (enabled)
            {
                __result = null;
                return false;
            }
            return true;
        }

        private static readonly System.Collections.Generic.Dictionary<string, int> _blockLogCounter = new();
        private static bool BlockDiplomacyDecisionLogged(ref object __result)
        {
            var stack = new System.Diagnostics.StackTrace();
            foreach (var frame in stack.GetFrames())
            {
                var method = frame.GetMethod();
                var name = method?.Name ?? "";
                if (name.Contains("Peace") || name.Contains("War") || name.Contains("Alliance") || name.Contains("Trade"))
                {
                    if (!_blockLogCounter.ContainsKey(name))
                        _blockLogCounter[name] = 0;
                    if (_blockLogCounter[name] < 3)
                    {
                        _blockLogCounter[name]++;
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"[MyFirstMod] 拦截原版外交：{name}（第{_blockLogCounter[name]}次）",
                            Colors.Cyan));
                    }
                    break;
                }
            }

            var enabled = MySettings.Instance?.BanVanillaDiplomacy == true;
            if (enabled)
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
        // 修复：CalculateRenownGain 返回 ExplainedNumber 而非 float，
        // 原 `ref float __result` 类型不匹配会让 PatchAll 抛 HarmonyException 并中止后续补丁注册。
        public static void Postfix(ref TaleWorlds.CampaignSystem.ExplainedNumber __result)
        {
            if (MySettings.Instance?.DoubleRenownEnabled == true)
            {
                __result.Add(__result.ResultNumber);
            }
        }
    }
}
