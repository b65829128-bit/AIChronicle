using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

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
    /// 2. 当"有残留遭遇、但既没有菜单/对话、也没有 MapEvent"持续 8 秒时，调用 Finish 清理。
    /// 这是恢复闸门，不替代正常的遭遇流程。
    /// </summary>
    internal static class EncounterDiagnostics
    {
        private static DateTime? _noUiSince;
        private static bool _hadEncounter;
        private static string _lastSignature = "";

        public static void OnSessionLaunched()
        {
            _noUiSince = null;
            _hadEncounter = false;
            _lastSignature = "";
            LogState("session launched");
        }

        /// <summary>每帧主线程调用。任何诊断异常都不能影响游戏主循环。</summary>
        public static void Tick()
        {
            try
            {
                TickCore();
            }
            catch (Exception ex)
            {
                UiTrace.Log("[Encounter] Tick failed: " + ex);
            }
        }

        private static void TickCore()
        {
            if (Campaign.Current == null)
                return;

            var encounter = PlayerEncounter.Current;
            if (encounter == null)
            {
                if (_hadEncounter)
                {
                    UiTrace.Log("[Encounter] current=null (encounter ended)");
                    _hadEncounter = false;
                }
                _noUiSince = null;
                _lastSignature = "";
                return;
            }

            if (!_hadEncounter)
            {
                _hadEncounter = true;
                LogState("encounter active");
            }

            bool atMenu = IsAtMenu();
            bool mapConversation = IsMapConversationActive();
            bool conversation = IsConversationActive();
            bool mapEvent = HasMapEvent();
            bool modUi = HasModUi();
            string signature = BuildSignature(encounter, atMenu, mapConversation, conversation, mapEvent, modUi);
            if (signature != _lastSignature)
            {
                _lastSignature = signature;
                LogState("state changed");
            }

            if (atMenu || conversation || mapEvent || modUi)
            {
                _noUiSince = null;
                return;
            }

            if (_noUiSince == null)
            {
                _noUiSince = DateTime.UtcNow;
                UiTrace.Log("[Encounter] no menu/conversation/map event; waiting 8s before recovery");
                return;
            }

            if ((DateTime.UtcNow - _noUiSince.Value).TotalSeconds < 8.0)
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
                _noUiSince = null;
                _lastSignature = "";
            }
        }

        /// <summary>let_go 结束对话后，下一帧显式收尾遭遇，避免只置 LeaveEncounter 而留下 Current。</summary>
        public static void FinishAfterLetGo()
        {
            MainThreadExecutor.Post(() =>
            {
                try
                {
                    var encounter = PlayerEncounter.Current;
                    if (encounter == null || !PlayerEncounter.LeaveEncounter)
                        return;

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

            if (IsConversationActive())
            {
                Show("当前正在对话中，未修复。请先结束对话。");
                return;
            }

            if (HasMapEvent())
            {
                Show("当前正在战斗/地图事件中，未修复。");
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
            return MobileParty.MainParty?.MapEvent != null;
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

        private static string BuildSignature(PlayerEncounter encounter, bool atMenu, bool mapConversation,
            bool conversation, bool mapEvent, bool modUi)
        {
            return $"{encounter.EncounterState}|{PlayerEncounter.LeaveEncounter}|{atMenu}|{mapConversation}|" +
                   $"{conversation}|{mapEvent}|{modUi}|" +
                   $"{PlayerEncounter.EncounteredMobileParty?.StringId ?? "-"}|{PlayerEncounter.EncounterSettlement?.StringId ?? "-"}";
        }

        private static void LogState(string reason)
        {
            try
            {
                var encounter = PlayerEncounter.Current;
                bool atMenu = IsAtMenu();
                bool mapConversation = IsMapConversationActive();
                bool conversation = IsConversationActive();
                bool mapEvent = HasMapEvent();
                bool modUi = HasModUi();
                string encountered = PlayerEncounter.EncounteredMobileParty?.Name?.ToString() ?? "-";
                string settlement = PlayerEncounter.EncounterSettlement?.Name?.ToString() ?? "-";
                string state = encounter?.EncounterState.ToString() ?? "-";
                bool leave = encounter != null && PlayerEncounter.LeaveEncounter;
                UiTrace.Log($"[Encounter] {reason} current={encounter != null} state={state} leave={leave} " +
                            $"atMenu={atMenu} mapConversation={mapConversation} conversation={conversation} " +
                            $"mapEvent={mapEvent} modUi={modUi} " +
                            $"encountered={encountered} settlement={settlement} time={Campaign.Current?.TimeControlMode}");
            }
            catch (Exception ex)
            {
                UiTrace.Log("[Encounter] LogState failed: " + ex);
            }
        }
    }
}
