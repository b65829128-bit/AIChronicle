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

namespace AIChronicle
{
    public class SubModule : MBSubModuleBase
    {
        private static Hero? _pendingChatHero;
        private static Hero? _pendingLetterHero;
        private static Hero? _pendingEnvoyHero;
        private static LordChatBehavior? _chatBehavior;
        private static bool _prevLetterO;
        private static bool _prevChanceryP;
        private static bool _prevHistoryH;

        /// <summary>玩家是否正位于战役大地图（非捏脸/菜单/战斗等界面）。天意等大地图系统的激活以此为准。</summary>
        internal static bool IsPlayerFreeOnMap()
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

            var modulePath = ModuleHelper.GetModuleFullPath("AIChronicle");
            PromptManager.Initialize(modulePath);

            // OnSubModuleLoad 在游戏主线程执行——绑定主线程 ID，供工具主线程分发判断。
            MainThreadExecutor.Initialize();

            var harmony = new Harmony("AIChronicle");
            harmony.PatchAll();
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();

            InformationManager.DisplayMessage(new InformationMessage(
                "[AI编年史] AI 聊天模组已加载！",
                Colors.Green));
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarter)
        {
            base.OnGameStart(game, gameStarter);

            InformationManager.DisplayMessage(new InformationMessage(
                "[AI编年史] O键=书信往来 | H键=史书 | M键=秘书处",
                Colors.Green));

            if (game.GameType is Campaign && gameStarter is CampaignGameStarter starter)
            {
                _chatBehavior = new LordChatBehavior();
                starter.AddBehavior(_chatBehavior);

                starter.AddBehavior(new HistoryRecorder());

                var kdpbType = Type.GetType("TaleWorlds.CampaignSystem.CampaignBehaviors.KingdomDecisionProposalBehavior, TaleWorlds.CampaignSystem");
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[AI编年史] KDPB type: {(kdpbType != null ? kdpbType.FullName : "NOT FOUND")}", kdpbType != null ? Colors.Green : Colors.Red));

                if (kdpbType != null)
                {
                    var harmony = new Harmony("AIChronicle.Diplomacy");
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

                // 号召盟友宣战投票（盟约 CallToWar 机制）：拦截原版"号召盟友向敌国宣战"的国内投票，
                // 让军事同盟只保留名义作用——是否号召盟友/宣战由国王 Agent 激活时自行决定（或玩家经秘书处）。
                // ProposeCallToWarAgreementDecision / AcceptCallToWarAgreementDecision 是 Election 类，
                // PatchAll 在 OnSubModuleLoad 会静默跳过，必须这里手动注册。
                // 原理：KingdomDecision.ShouldBeCancelled() 在投票触发前检查 IsAllowed()，强制其返回 false 即取消投票。
                foreach (var callToWarTypeName in new[]
                {
                    "TaleWorlds.CampaignSystem.Election.ProposeCallToWarAgreementDecision, TaleWorlds.CampaignSystem",
                    "TaleWorlds.CampaignSystem.Election.AcceptCallToWarAgreementDecision, TaleWorlds.CampaignSystem"
                })
                {
                    var callToWarType = Type.GetType(callToWarTypeName);
                    if (callToWarType == null) continue;
                    var isAllowedM = callToWarType.GetMethod("IsAllowed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (isAllowedM == null) continue;
                    var ctHarmony = new Harmony("AIChronicle.CallToWar");
                    ctHarmony.Patch(isAllowedM,
                        prefix: new HarmonyMethod(typeof(SubModule).GetMethod(nameof(BlockCallToWarVote), BindingFlags.Static | BindingFlags.NonPublic)));
                }

                // 玩家对话里的原版"号召盟友宣战"选项也一并隐藏——其投票已被拦截，若选项仍显示，
                // 玩家会花影响力/金币却无声失效。隐藏主选项后，其下的列王国/拒绝子流程都不可达。
                // LordConversationsCampaignBehavior 是 CampaignBehaviors 类，PatchAll 会静默跳过，须手动注册。
                var lordConversationsType = Type.GetType("TaleWorlds.CampaignSystem.CampaignBehaviors.LordConversationsCampaignBehavior, TaleWorlds.CampaignSystem");
                if (lordConversationsType != null)
                {
                    var callToWarDialogM = lordConversationsType.GetMethod("conversation_player_wants_to_sponsor_call_to_war_on_condition", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (callToWarDialogM != null)
                    {
                        var dialogHarmony = new Harmony("AIChronicle.CallToWarDialog");
                        dialogHarmony.Patch(callToWarDialogM,
                            prefix: new HarmonyMethod(typeof(SubModule).GetMethod(nameof(BlockCallToWarDialog), BindingFlags.Static | BindingFlags.NonPublic)));
                    }
                }

                // 册封由 Agent 主导：拦截攻城后投票（SettlementClaimantCampaignBehavior 是 CampaignBehaviors 类，
                // PatchAll 在 OnSubModuleLoad 会静默跳过，必须这里手动注册）
                var scbType = Type.GetType("TaleWorlds.CampaignSystem.CampaignBehaviors.SettlementClaimantCampaignBehavior, TaleWorlds.CampaignSystem");
                if (scbType != null)
                {
                    var fiefHarmony = new Harmony("AIChronicle.FiefAssignment");
                    var dailyTickM = scbType.GetMethod("DailyTickSettlement", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (dailyTickM != null)
                        fiefHarmony.Patch(dailyTickM,
                            prefix: new HarmonyMethod(typeof(FiefAssignmentPatch).GetMethod(nameof(FiefAssignmentPatch.PrefixDailyTickSettlement), BindingFlags.Static | BindingFlags.Public)));

                    var ownerChangedM = scbType.GetMethod("OnSettlementOwnerChanged", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (ownerChangedM != null)
                        fiefHarmony.Patch(ownerChangedM,
                            postfix: new HarmonyMethod(typeof(FiefAssignmentPatch).GetMethod(nameof(FiefAssignmentPatch.PostfixOnSettlementOwnerChanged), BindingFlags.Static | BindingFlags.Public)));
                }

                var diplomVmType = Type.GetType("TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy.KingdomDiplomacyVM, TaleWorlds.CampaignSystem.ViewModelCollection");
                if (diplomVmType != null)
                {
                    var uiMethod = diplomVmType.GetMethod("GetAreProposalActionsEnabledWithReason", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (uiMethod != null)
                    {
                        var harmony = new Harmony("AIChronicle.DiplomacyUI");
                        harmony.Patch(uiMethod,
                            prefix: new HarmonyMethod(typeof(SubModule).GetMethod(nameof(BlockDiplomacyUI), BindingFlags.Static | BindingFlags.NonPublic)));
                    }
                }

                // 处决无惩罚（MCM「处决无惩罚」默认开）：禁用处决的荣誉与好感代价。手动注册防 PatchAll 静默跳过。
                var execHarmony = new Harmony("AIChronicle.ExecutionNoPenalty");
                var traitType = Type.GetType("TaleWorlds.CampaignSystem.CharacterDevelopment.TraitLevelingHelper, TaleWorlds.CampaignSystem");
                if (traitType != null)
                {
                    var traitM = traitType.GetMethod("OnLordExecuted", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (traitM != null)
                        execHarmony.Patch(traitM,
                            prefix: new HarmonyMethod(typeof(ExecutionNoPenaltyPatch).GetMethod(nameof(ExecutionNoPenaltyPatch.OnLordExecutedPrefix), BindingFlags.Static | BindingFlags.Public)));
                }
                var execRelType = Type.GetType("TaleWorlds.CampaignSystem.GameComponents.DefaultExecutionRelationModel, TaleWorlds.CampaignSystem");
                if (execRelType != null)
                {
                    var relM = execRelType.GetMethod("GetRelationChangeForExecutingHero", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (relM != null)
                        execHarmony.Patch(relM,
                            prefix: new HarmonyMethod(typeof(ExecutionNoPenaltyPatch).GetMethod(nameof(ExecutionNoPenaltyPatch.GetRelationChangeForExecutingHeroPrefix), BindingFlags.Static | BindingFlags.Public)));
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
            AIChatClient.ResetForNewCampaign();
            TtsService.Stop(); // 切档/退出时停止朗读，防止跨档播放残留
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
                        $"[AI编年史] 打开聊天窗口异常：{ex.Message}",
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
                        $"[AI编年史] 打开写信窗口异常：{ex.Message}",
                        Colors.Red));
                }
            }

            if (_pendingEnvoyHero != null)
            {
                var hero = _pendingEnvoyHero;
                _pendingEnvoyHero = null;
                try
                {
                    AIChatScreen.DoOpenEnvoy(hero);
                }
                catch (Exception ex)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"[AI编年史] 打开密使往来窗口异常：{ex.Message}",
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

        public static void RequestEnvoyOpen(Hero hero)
        {
            _pendingEnvoyHero = hero;
        }

        private static void KdpbRegisterPatched()
        {
            InformationManager.DisplayMessage(new InformationMessage(
                "[AI编年史] KDPB.RegisterEvents was called (manual patch works!)",
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
                            $"[AI编年史] 拦截原版外交：{name}（第{_blockLogCounter[name]}次）",
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

        /// <summary>
        /// 拦截盟约"号召盟友宣战"投票（CallToWar）：强制 IsAllowed() 返回 false，使决策被 ShouldBeCancelled() 取消。
        /// 军事同盟只保留名义作用——是否号召盟友/宣战由国王 Agent 激活时自行决定（或玩家经秘书处）。
        /// </summary>
        private static bool BlockCallToWarVote(ref bool __result)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true)
            {
                __result = false;
                return false; // 跳过原 IsAllowed → 决策在触发前被取消，不再出现国内投票
            }
            return true;
        }

        /// <summary>隐藏玩家对话里的原版"号召盟友宣战"选项（其投票已被拦截，避免玩家花影响力/金币却无声失效）。</summary>
        private static bool BlockCallToWarDialog(ref bool __result)
        {
            if (MySettings.Instance?.BanVanillaDiplomacy == true)
            {
                __result = false;
                return false; // 跳过原条件 → 对话选项不再出现
            }
            return true;
        }

        public static List<string> GetKnownNpcIds()
        {
            return _chatBehavior?.KnownNpcIds ?? new List<string>();
        }

        /// <summary>登记一个已知 NPC（联系人）。NPC 主动给玩家写信时调用，使对方出现在 O 键书信面板。</summary>
        public static void MarkNpcKnown(string entityId)
        {
            _chatBehavior?.AddKnownNpc(entityId);
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
