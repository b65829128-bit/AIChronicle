using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace AIChronicle
{
    /// <summary>结束个人会面再进入决斗；不让原版会面收尾自动创建整队战斗。</summary>
    public sealed class DuelEncounter
    {
        private readonly object? _encounter;
        private readonly Campaign _campaign;
        private readonly long _epoch;
        private readonly bool _inSettlement;
        public bool Detached { get; private set; }

        public DuelEncounter(object? encounter, long epoch)
        {
            _encounter = encounter;
            _campaign = Campaign.Current ?? throw new InvalidOperationException("当前没有可用的战役。");
            _epoch = epoch;
            _inSettlement = InSettlement();
        }

        private static bool InSettlement() => MobileParty.MainParty?.CurrentSettlement != null
            || PlayerEncounter.EncounterSettlement != null || PlayerEncounter.LocationEncounter != null;

        private void CheckContext()
        {
            if (_epoch != MainThreadExecutor.CampaignEpoch || !ReferenceEquals(Campaign.Current, _campaign))
                throw new OperationCanceledException("原决斗所属战役已结束。");
            if (PlayerEncounter.Current != null && !ReferenceEquals(PlayerEncounter.Current, _encounter))
                throw new InvalidOperationException("已经进入另一场遭遇，未结束新的遭遇。");
        }

        public void EndConversation(Action endConversation)
        {
            CheckContext();
            if (!_inSettlement && PlayerEncounter.Current != null)
            {
                if (InSettlement()) throw new InvalidOperationException("会面已经转入定居点，未退出新的场景。");
                // 必须先设置：原版 encounter_meeting 的对话结束回调否则会 StartBattle。
                PlayerEncounter.LeaveEncounter = true;
                UiTrace.Log("[Duel] original map meeting marked to leave before ending conversation");
            }
            endConversation();
        }

        /// <summary>等待原场景真正退出后才清理本次地图会面；城镇和替换后的遭遇保留。</summary>
        public bool Finish()
        {
            CheckContext();
            if (_inSettlement || _encounter == null) return true;
            if (PlayerEncounter.Current == null) { Detached = true; return true; }
            if (Mission.Current != null || !(Game.Current?.GameStateManager?.ActiveState is MapState map)
                || MobileParty.MainParty?.IsActive != true || InSettlement() || map.MapConversationActive
                || _campaign.ConversationManager?.IsConversationInProgress == true)
                return false;
            PlayerEncounter.LeaveEncounter = true;
            PlayerEncounter.Finish(false);
            if (ReferenceEquals(PlayerEncounter.Current, _encounter)) throw new InvalidOperationException("原地图会面仍未结束。");
            Detached = true;
            UiTrace.Log("[Duel] original map meeting finished without resuming army battle");
            return true;
        }
    }
}
