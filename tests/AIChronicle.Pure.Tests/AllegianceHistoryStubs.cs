// 直接链接归属记录 partial；仅提供原版事件签名，不模拟游戏的换国算法。
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Actions;

namespace TaleWorlds.CampaignSystem
{
    public sealed class CampaignGameStarter { }
    public sealed class StoryTestEvent<T1, T2, T3, T4, T5>
    {
        private readonly List<Action<T1, T2, T3, T4, T5>> _listeners = new();
        public void AddNonSerializedListener(object owner, Action<T1, T2, T3, T4, T5> action) => _listeners.Insert(0, action);
        public void Raise(T1 a, T2 b, T3 c, T4 d, T5 e)
        { foreach (var action in _listeners.ToArray()) action(a, b, c, d, e); }
        public void Clear() => _listeners.Clear();
    }
    public static partial class CampaignEvents
    {
        public static StoryTestEvent<CampaignGameStarter> OnSessionLaunchedEvent { get; } = new();
        public static StoryTestEvent<Clan, Kingdom, Kingdom, ChangeKingdomAction.ChangeKingdomActionDetail, bool> OnClanChangedKingdomEvent { get; } = new();
        public static StoryTestEvent<Clan, StartMercenaryServiceAction.StartMercenaryServiceActionDetails> OnMercenaryServiceStartedEvent { get; } = new();
        public static StoryTestEvent<Clan, EndMercenaryServiceAction.EndMercenaryServiceActionDetails> OnMercenaryServiceEndedEvent { get; } = new();
    }
    public static class AllegianceTestEvents
    {
        public static void Reset()
        {
            CampaignEvents.OnSessionLaunchedEvent.Clear(); CampaignEvents.OnClanChangedKingdomEvent.Clear();
            CampaignEvents.OnMercenaryServiceStartedEvent.Clear(); CampaignEvents.OnMercenaryServiceEndedEvent.Clear();
        }
    }
}
namespace TaleWorlds.CampaignSystem.Actions
{
    public static partial class ChangeKingdomAction
    {
        public enum ChangeKingdomActionDetail
        { JoinAsMercenary, JoinKingdom, JoinKingdomByDefection, LeaveKingdom, LeaveWithRebellion,
          LeaveAsMercenary, LeaveByClanDestruction, CreateKingdom, LeaveByKingdomDestruction }
    }
    public static class StartMercenaryServiceAction
    {
        public enum StartMercenaryServiceActionDetails { ApplyByDefault }
    }
    public static class EndMercenaryServiceAction
    {
        public enum EndMercenaryServiceActionDetails { ApplyByDefault, ApplyByLeavingKingdom, ApplyByBecomingVassal }
    }
}
