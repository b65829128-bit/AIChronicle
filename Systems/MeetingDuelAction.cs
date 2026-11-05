using System;
using System.Collections.Generic;
using TaleWorlds.Core;

namespace AIChronicle
{
    public sealed class MeetingDuelAction : IMeetingDeferredAction
    {
        public string Id => "duel";
        public string Title => "请求决斗";
        public string StartLabel => "开始决斗";
        public string Description => "双方亲自一战，分别约定各自获胜后的财物与行动。战死遵循当前战役设置。";
        public string? Unavailable(MeetingSession session) => DuelService.Unavailable(session);
        public IReadOnlyList<MeetingOutcomeSlot> OutcomeSlots(MeetingSession session) => new[]
        {
            new MeetingOutcomeSlot { Id = "otherVictory", Title = session.Player.Name + "获胜" },
            new MeetingOutcomeSlot { Id = "yourVictory", Title = session.Npc.Name + "获胜" }
        };
        public IReadOnlyList<MeetingField> Fields(MeetingSession session)
        {
            var choices = new List<MeetingChoice> { new() { Id = "foot", Label = "步战 · 使用各自战斗装备" } };
            if (session.Player.BattleEquipment[EquipmentIndex.Horse].Item != null && session.Npc.BattleEquipment[EquipmentIndex.Horse].Item != null)
                choices.Add(new MeetingChoice { Id = "mounted", Label = "骑战 · 使用各自战斗装备与坐骑" });
            return new[] { new MeetingField { Key = "mounts", Label = "决斗方式", Choices = choices } };
        }
        public string? Validate(MeetingSession session, MeetingTerms terms) => Unavailable(session);
        public string Confirm(MeetingSession session, MeetingTerms terms) => "决斗约定已确认，等待准备开始；胜负条款尚未交付。";
        public void Start(MeetingSession session, MeetingRequest request) => DuelService.Queue(session, request);
        public void Apply(MeetingSession session, MeetingTerms terms) => throw new InvalidOperationException("决斗须等待实战结果。");
        public bool IsApplied(MeetingSession session, MeetingTerms terms) => false;
        public MeetingEffect? PrepareEffect(MeetingSession session, MeetingTerms terms) => null;
        public void RecordApplied(MeetingSession session, MeetingTerms terms, string result) { }
        public string Describe(MeetingSession session, MeetingTerms terms)
            => (terms.Parameters["mounts"]?.ToString() == "mounted" ? "骑战" : "步战")
                + "；伤亡遵循原版战役设置；财物在开战前保管，战后按实际胜负交付。即使因死亡等情况无法履行本人行动，财物仍按胜负交付，未履行的行动如实记账。"
                + " " + DuelRewardService.Preview(session, terms);
    }
}
