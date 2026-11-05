using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;

namespace AIChronicle
{
    public sealed class MeetingSession
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public long Epoch { get; } = MainThreadExecutor.CampaignEpoch;
        public Hero Player { get; }
        public Hero Npc { get; }
        public object? Encounter { get; } = PlayerEncounter.Current;
        public MeetingNegotiation Negotiation { get; } = new();
        public string Summary { get; set; } = "";
        public MeetingSession(Hero player, Hero npc) { Player = player; Npc = npc; }
    }

    /// <summary>新事项实现此接口并 Register；菜单、标准字段、报价、AI 工具和状态机无需改动。</summary>
    public interface IMeetingAction
    {
        string Id { get; }
        string Title { get; }
        string Description { get; }
        string? Unavailable(MeetingSession session);
        IReadOnlyList<MeetingField> Fields(MeetingSession session);
        string? Validate(MeetingSession session, MeetingTerms terms);
        void Apply(MeetingSession session, MeetingTerms terms);
        bool IsApplied(MeetingSession session, MeetingTerms terms);
        string Describe(MeetingSession session, MeetingTerms terms);
        void RecordApplied(MeetingSession session, MeetingTerms terms, string result);
        MeetingEffect? PrepareEffect(MeetingSession session, MeetingTerms terms);
    }

    public interface IMeetingDeferredAction : IMeetingAction
    {
        string StartLabel { get; }
        IReadOnlyList<MeetingOutcomeSlot> OutcomeSlots(MeetingSession session);
        string Confirm(MeetingSession session, MeetingTerms terms);
        void Start(MeetingSession session, MeetingRequest request);
    }

    public static class MeetingRegistry
    {
        private static readonly Dictionary<string, IMeetingAction> Actions = new(StringComparer.Ordinal);
        static MeetingRegistry()
        {
            Register(new MeetingTradeAction());
            Register(new MeetingMarriageAction());
            Register(new MeetingDuelAction());
        }

        public static void Register(IMeetingAction action)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.Id)) throw new ArgumentException("事项必须有唯一 ID。");
            if (Actions.ContainsKey(action.Id)) throw new InvalidOperationException("重复会晤事项：" + action.Id);
            Actions.Add(action.Id, action);
        }

        public static IReadOnlyList<IMeetingAction> All => Actions.Values.ToList();
        public static IMeetingAction? Find(string id) => Actions.TryGetValue(id, out var action) ? action : null;
    }

    public sealed class MeetingUnavailableAction : IMeetingAction
    {
        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        private readonly string _reason;
        public MeetingUnavailableAction(string id, string title, string description, string reason)
        { Id = id; Title = title; Description = description; _reason = reason; }
        public string? Unavailable(MeetingSession session) => _reason;
        public IReadOnlyList<MeetingField> Fields(MeetingSession session) => Array.Empty<MeetingField>();
        public string? Validate(MeetingSession session, MeetingTerms terms) => _reason;
        public void Apply(MeetingSession session, MeetingTerms terms) => throw new InvalidOperationException(_reason);
        public bool IsApplied(MeetingSession session, MeetingTerms terms) => false;
        public string Describe(MeetingSession session, MeetingTerms terms) => _reason;
        public void RecordApplied(MeetingSession session, MeetingTerms terms, string result) { }
        public MeetingEffect? PrepareEffect(MeetingSession session, MeetingTerms terms) => null;
    }
}
