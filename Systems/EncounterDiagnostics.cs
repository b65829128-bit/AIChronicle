using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AIChronicle
{
    /// <summary>
    /// 遭遇状态诊断与恢复。
    ///
    /// 背景：Bannerlord 的 EncounterManager 在 PlayerEncounter.Current != null 时会跳过主队伍的
    /// 新遭遇；如果 UI 在遭遇菜单/对话之上叠加、或代码直接结束对话却没有 Finish 遭遇，
    /// PlayerEncounter 会作为存档状态残留，导致读档后"部队相遇没反应"。
    ///
    /// 本类只做两件事：
    /// 1. 把 PlayerEncounter 的关键状态写入 ui_trace.log，便于定位；
    /// 2. 仅在确认空闲的大地图上，同一残留遭遇持续 8 秒时，调用 Finish 清理。
    /// Mission/定居点流程仍依赖 PlayerEncounter，不能因没有地图菜单就判为残留。
    /// 这是恢复闸门，不替代正常的遭遇流程。
    /// </summary>
    internal static class EncounterDiagnostics
    {
        private static DateTime? _noUiSince;
        private static PlayerEncounter? _recoveryEncounter;
        private static bool _hadEncounter;
        private static string _lastSignature = "";
        internal static Func<bool>? TransitionInProgress { get; set; }

        public static void OnSessionLaunched()
        {
            ResetRecovery();
            _hadEncounter = false;
            _lastSignature = "";
            LogState("session launched");
        }

        /// <summary>每帧主线程调用。任何诊断异常都不能影响游戏主循环。</summary>
        public static void Tick()
        {
            try
            {
                TickCore(DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                ResetRecovery();
                UiTrace.Log("[Encounter] Tick failed: " + ex);
            }
        }

        internal static void TickCore(DateTime now)
        {
            if (Campaign.Current == null)
            {
                ResetRecovery();
                return;
            }

            var encounter = PlayerEncounter.Current;
            if (encounter == null)
            {
                if (_hadEncounter)
                {
                    UiTrace.Log("[Encounter] current=null (encounter ended)");
                    _hadEncounter = false;
                }
                ResetRecovery();
                _lastSignature = "";
                return;
            }

            if (!_hadEncounter)
            {
                _hadEncounter = true;
                LogState("encounter active");
            }

            string signature = BuildSignature(encounter);
            if (signature != _lastSignature)
            {
                _lastSignature = signature;
                LogState("state changed");
            }

            if (GetRecoveryBlockReason() != null)
            {
                ResetRecovery();
                return;
            }

            if (_noUiSince == null || !ReferenceEquals(_recoveryEncounter, encounter))
            {
                _noUiSince = now;
                _recoveryEncounter = encounter;
                UiTrace.Log("[Encounter] idle world map; waiting 8s before recovery of this encounter");
                return;
            }

            if ((now - _noUiSince.Value).TotalSeconds < 8.0)
                return;

            UiTrace.Log("[Encounter] stale for 8s -> PlayerEncounter.Finish()");
            try
            {
                PlayerEncounter.Finish();
                MainThreadExecutor.DisplayMessage(new InformationMessage(
                    "[AI编年史] 检测到残留的遭遇状态，已自动清理（若时间暂停请按空格恢复）。", Colors.Yellow));
            }
            catch (Exception ex)
            {
                UiTrace.Log("[Encounter] Finish failed: " + ex);
            }
            finally
            {
                ResetRecovery();
                _lastSignature = "";
            }
        }

        /// <summary>let_go 结束对话后，下一帧显式收尾遭遇，避免只置 LeaveEncounter 而留下 Current。</summary>
        public static void FinishAfterLetGo()
        {
            var expectedEncounter = Campaign.Current == null ? null : PlayerEncounter.Current;
            if (expectedEncounter == null || !PlayerEncounter.LeaveEncounter)
                return;

            MainThreadExecutor.Post(() =>
            {
                try
                {
                    if (Campaign.Current == null || !ReferenceEquals(PlayerEncounter.Current, expectedEncounter)
                        || !PlayerEncounter.LeaveEncounter)
                        return;

                    // let_go 可以主动收尾其遭遇菜单，但不能跨到新遭遇、Mission 或定居点流程。
                    string? blocked = GetRecoveryBlockReason(allowEncounterMenu: true);
                    if (blocked != null)
                    {
                        LogState("let_go Finish deferred: " + blocked);
                        return;
                    }

                    LogState("let_go -> Finish");
                    PlayerEncounter.Finish();
                }
                catch (Exception ex)
                {
                    UiTrace.Log("[Encounter] let_go Finish failed: " + ex);
                }
            });
        }

        /// <summary>MCM「修复遭遇状态」按钮：仅在玩家明确点击时调用。</summary>
        public static void RepairFromSettings()
        {
            if (Campaign.Current == null)
            {
                Show("请先进入战役。");
                return;
            }

            var encounter = PlayerEncounter.Current;
            LogState("manual repair requested");
            if (encounter == null)
            {
                Show("当前没有残留的遭遇状态。");
                return;
            }

            string? blocked = GetRecoveryBlockReason();
            if (blocked != null)
            {
                Show(blocked);
                return;
            }

            try
            {
                PlayerEncounter.Finish();
                UiTrace.Log("[Encounter] manual repair -> Finish");
                Show("已清理残留遭遇状态（若时间暂停请按空格恢复）。请尝试再次与部队相遇。");
            }
            catch (Exception ex)
            {
                UiTrace.Log("[Encounter] manual repair failed: " + ex);
                Show("修复失败：" + ex.Message);
            }
        }

        private static void Show(string text)
        {
            MainThreadExecutor.DisplayMessage(new InformationMessage("[AI编年史] " + text, Colors.Yellow));
        }

        private static void ResetRecovery()
        {
            _noUiSince = null;
            _recoveryEncounter = null;
        }

        /// <summary>所有恢复入口共用的安全闸门；未知状态一律不调用 Finish。</summary>
        private static string? GetRecoveryBlockReason(bool allowEncounterMenu = false)
        {
            if (TransitionInProgress?.Invoke() == true) return "正在准备或结算决斗，未修复。";
            if (Mission.Current != null || !(Game.Current?.GameStateManager?.ActiveState is MapState mapState))
                return "当前不在大地图或正在场景中，未修复。请退出场景后再试。";

            var party = MobileParty.MainParty;
            if (party == null || !party.IsActive)
                return "当前主队伍不可用，未修复。";

            if (party.CurrentSettlement != null || PlayerEncounter.EncounterSettlement != null
                || PlayerEncounter.LocationEncounter != null)
                return "当前仍在城镇/村庄等定居点流程中，未修复。请先返回大地图。";

            if (mapState.MapConversationActive || IsConversationActive())
                return "当前正在对话中，未修复。请先结束对话。";

            if (HasMapEvent())
                return "当前正在战斗/地图事件中，未修复。";

            if ((!allowEncounterMenu && mapState.AtMenu) || HasModUi())
                return "当前仍有菜单/模组界面打开，未修复。请先关闭界面。";

            return null;
        }

        private static bool IsAtMenu()
        {
            return Game.Current?.GameStateManager?.ActiveState is MapState mapState && mapState.AtMenu;
        }

        private static bool IsMapConversationActive()
        {
            return Game.Current?.GameStateManager?.ActiveState is MapState mapState && mapState.MapConversationActive;
        }

        private static bool IsConversationActive()
        {
            return Campaign.Current?.ConversationManager?.IsConversationInProgress == true;
        }

        private static bool HasMapEvent()
        {
            return MobileParty.MainParty?.MapEvent != null || PlayerEncounter.Battle != null
                || (Game.Current?.GameStateManager?.ActiveState is MapState mapState && mapState.IsSimulationActive);
        }

        /// <summary>模组自己的全屏 UI 打开时不要自动收尾（尤其 AI 聊天/史书/秘书处）。</summary>
        private static bool HasModUi()
        {
            if (AIChatScreen.IsOpen || LetterListScreen.IsOpen || HistoryScreen.IsOpen || KitScreen.AnyOpen)
                return true;
#if NET472
            if (HistoryCefScreen.IsOpen)
                return true;
#endif
            return false;
        }

        private static string BuildSignature(PlayerEncounter encounter)
        {
            return $"{encounter.EncounterState}|{PlayerEncounter.LeaveEncounter}|{IsAtMenu()}|{IsMapConversationActive()}|" +
                   $"{IsConversationActive()}|{HasMapEvent()}|{HasModUi()}|" +
                   $"{Game.Current?.GameStateManager?.ActiveState?.GetType().Name}|{Mission.Current != null}|" +
                   $"{MobileParty.MainParty?.IsActive}|{MobileParty.MainParty?.CurrentSettlement?.StringId}|" +
                   $"{PlayerEncounter.LocationEncounter != null}|" +
                   $"{PlayerEncounter.EncounteredMobileParty?.StringId ?? "-"}|{PlayerEncounter.EncounterSettlement?.StringId ?? "-"}";
        }

        private static void LogState(string reason)
        {
            try
            {
                var encounter = Campaign.Current == null ? null : PlayerEncounter.Current;
                bool atMenu = IsAtMenu();
                bool mapConversation = IsMapConversationActive();
                bool conversation = IsConversationActive();
                bool mapEvent = HasMapEvent();
                bool modUi = HasModUi();
                string activeState = Game.Current?.GameStateManager?.ActiveState?.GetType().Name ?? "-";
                bool mission = Mission.Current != null;
                bool insideSettlement = MobileParty.MainParty?.CurrentSettlement != null;
                bool locationEncounter = Campaign.Current != null && PlayerEncounter.LocationEncounter != null;
                string encountered = PlayerEncounter.EncounteredMobileParty?.Name?.ToString() ?? "-";
                string settlement = PlayerEncounter.EncounterSettlement?.Name?.ToString() ?? "-";
                string state = encounter?.EncounterState.ToString() ?? "-";
                bool leave = encounter != null && PlayerEncounter.LeaveEncounter;
                UiTrace.Log($"[Encounter] {reason} current={encounter != null} state={state} leave={leave} " +
                            $"atMenu={atMenu} mapConversation={mapConversation} conversation={conversation} " +
                            $"mapEvent={mapEvent} modUi={modUi} activeState={activeState} mission={mission} " +
                            $"insideSettlement={insideSettlement} locationEncounter={locationEncounter} " +
                            $"encountered={encountered} settlement={settlement} time={Campaign.Current?.TimeControlMode}");
            }
            catch (Exception ex)
            {
                UiTrace.Log("[Encounter] LogState failed: " + ex);
            }
        }
    }
}
