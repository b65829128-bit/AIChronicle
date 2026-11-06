// 直接链接生产史料 partial；事件沿原版后注册先调用的顺序，只提供只读进度与输出接缝。
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace TaleWorlds.CampaignSystem
{
    public readonly struct CampaignTime
    {
        public static CampaignTime Now { get; set; }
        public readonly string Stamp;
        public CampaignTime(string stamp) { Stamp = stamp; }
    }
    public class QuestBase
    {
        public enum QuestCompleteDetails { Invalid, Success, Cancel, Fail, Timeout, FailWithBetrayal }
    }
    public sealed class StoryTestEvent
    {
        private readonly List<Action> _listeners = new();
        public void AddNonSerializedListener(object owner, Action action) => _listeners.Insert(0, action);
        public void Raise() { foreach (var action in _listeners.ToArray()) action(); }
        public void Clear() => _listeners.Clear();
    }
    public sealed class StoryTestEvent<T>
    {
        private readonly List<Action<T>> _listeners = new();
        public void AddNonSerializedListener(object owner, Action<T> action) => _listeners.Insert(0, action);
        public void Raise(T value) { foreach (var action in _listeners.ToArray()) action(value); }
        public void Clear() => _listeners.Clear();
    }
    public sealed class StoryTestEvent<T, U>
    {
        private readonly List<Action<T, U>> _listeners = new();
        public int ListenerCount => _listeners.Count;
        public void AddNonSerializedListener(object owner, Action<T, U> action) => _listeners.Insert(0, action);
        public void Raise(T first, U second) { foreach (var action in _listeners.ToArray()) action(first, second); }
        public void Clear() => _listeners.Clear();
    }
    public static partial class CampaignEvents
    {
        public static StoryTestEvent<QuestBase, QuestBase.QuestCompleteDetails> OnQuestCompletedEvent { get; } = new();
        public static StoryTestEvent<Kingdom> KingdomDestroyedEvent { get; } = new();
    }
}
namespace StoryMode
{
    public enum MainStoryLineSide { None, CreateImperialKingdom, SupportImperialKingdom, CreateAntiImperialKingdom, SupportAntiImperialKingdom }
    public sealed class StoryModeManager
    {
        public static StoryModeManager? Current { get; set; }
        public MainStoryLine MainStoryLine { get; set; } = new();
    }
    public sealed class MainStoryLine
    {
        public MainStoryLineSide MainStoryLineSide;
        public StoryModePhases.FirstPhase? FirstPhase { get; set; } = new();
        public StoryModePhases.ThirdPhase? ThirdPhase { get; set; }
        public Kingdom? PlayerSupportedKingdom { get; set; }
        public TaleWorlds.Core.ItemObject? DragonBanner { get; set; }
        public bool IsOnImperialQuestLine => MainStoryLineSide == MainStoryLineSide.CreateImperialKingdom
            || MainStoryLineSide == MainStoryLineSide.SupportImperialKingdom;
    }
    public static class StoryModeEvents
    {
        public static StoryTestEvent OnBannerPieceCollectedEvent { get; } = new();
        public static StoryTestEvent<MainStoryLineSide> OnMainStoryLineSideChosenEvent { get; } = new();
        public static StoryTestEvent OnConspiracyActivatedEvent { get; } = new();
        public static void Reset()
        { OnBannerPieceCollectedEvent.Clear(); OnMainStoryLineSideChosenEvent.Clear(); OnConspiracyActivatedEvent.Clear(); }
    }
}
namespace StoryMode.StoryModePhases
{
    public sealed class FirstPhase
    {
        public int CollectedBannerPieceCount { get; set; }
        public bool AllPiecesCollected => CollectedBannerPieceCount == 3;
    }
    public sealed class ThirdPhase
    {
        public readonly List<Kingdom> OppositionKingdoms = new();
        public bool IsCompleted { get; set; }
    }
}
namespace StoryMode.Quests.FirstPhase
{
    public sealed class BannerInvestigationQuest : QuestBase { }
    public sealed class AssembleTheBannerQuest : QuestBase { }
    public sealed class CreateKingdomQuest : QuestBase { }
    public sealed class SupportKingdomQuest : QuestBase { }
}
namespace StoryMode.Quests.SecondPhase
{
    public static class AssembleEmpireQuestBehavior { public sealed class AssembleEmpireQuest : QuestBase { } }
    public static class WeakenEmpireQuestBehavior { public sealed class WeakenEmpireQuest : QuestBase { } }
}
namespace StoryMode.Quests.ThirdPhase
{
    public static class DefeatTheConspiracyQuestBehavior
    {
        public sealed class DefeatTheConspiracyQuest : QuestBase
        {
            // 原版没有公开属性；保持字段名/可见性以覆盖生产的绑定对象读取。
            private readonly Kingdom _oppositionKingdom;
            public DefeatTheConspiracyQuest(Kingdom kingdom) { _oppositionKingdom = kingdom; }
        }
    }
}
namespace AIChronicle
{
    public enum EntityController { Human, Agent }
    public static partial class SilentDiary { public static bool Enabled { get; set; } = true; }
    public static partial class AgentManager
    {
        public static readonly List<(string Id, string Kind, string Text, string Date)> StoryDiaries = new();
        public static string GetDiaryDatePrefix() => "[" + CampaignTime.Now.Stamp + "]";
        public static void AppendSystemDiary(string id, string kind, string text, string date) => StoryDiaries.Add((id, kind, text, date));
    }
    public partial class HistoryRecorder : CampaignBehaviorBase
    {
        public readonly List<(string Kind, string Text, CampaignTime Time)> StoryEntries = new();
        public override void RegisterEvents() { RegisterClanAllegianceEvents(); RegisterWorldHistoryEvents(); RegisterStoryEvents(); }
        public override void SyncData(IDataStore store) { }
        private void RecordEvent(string kind, string text, CampaignTime? time = null) => StoryEntries.Add((kind, text, time ?? CampaignTime.Now));
        public static void RecordDiplomacyEvent(string kind, string text) { }
    }
}
