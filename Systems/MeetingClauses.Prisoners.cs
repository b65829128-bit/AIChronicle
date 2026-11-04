using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace AIChronicle
{
    public sealed class MeetingReleasePrisonerClause : IMeetingClauseHandler
    {
        public string Id => "release_prisoner";
        public string Title => "释放俘虏";
        public string Description => "释放自己部队中的指定贵族或指定数量的士兵俘虏。";
        public string? Unavailable(MeetingClauseContext context)
        {
            var party = context.Actor.PartyBelongedTo;
            if (party == null || party.LeaderHero != context.Actor) return "没有可自行处置的部队俘虏。";
            if (party.MapEvent != null) return "正参与战斗，暂时不能处置俘虏。";
            return party.PrisonRoster.TotalManCount == 0 ? "部队中没有俘虏。" : null;
        }
        public IReadOnlyList<MeetingField> Fields(MeetingClauseContext context)
        {
            var prisoners = context.Actor.PartyBelongedTo?.PrisonRoster.GetTroopRoster();
            return new[]
            {
                new MeetingField { Key = "prisonerId", Label = "释放谁", Choices = prisoners == null ? new() : prisoners
                    .Where(p => p.Character != null && p.Number > 0).Select(p => new MeetingChoice
                    { Id = p.Character.StringId, Label = p.Character.Name + " ×" + p.Number }).ToList() },
                new MeetingField { Key = "count", Label = "释放人数（贵族为 1）", Kind = "number", Minimum = 1,
                    Maximum = prisoners == null || prisoners.Count == 0 ? 1 : prisoners.Max(p => p.Number) }
            };
        }
        public string? Validate(MeetingClauseContext context, MeetingClause clause)
        {
            var unavailable = Unavailable(context);
            if (unavailable != null) return unavailable;
            var id = clause.Parameters["prisonerId"]?.ToString();
            var entry = context.Actor.PartyBelongedTo!.PrisonRoster.GetTroopRoster().FirstOrDefault(p => p.Character?.StringId == id);
            if (entry.Character == null || !int.TryParse(clause.Parameters["count"]?.ToString(), out var count) || count <= 0 || count > entry.Number)
                return "俘虏已不在部队中或数量不足。";
            if (entry.Character.IsHero && (count != 1 || entry.Character.HeroObject?.IsPrisoner != true)) return "贵族释放人数须为 1，且当前仍被俘。";
            if (entry.Character.IsHero && entry.Character.HeroObject.PartyBelongedToAsPrisoner != context.Actor.PartyBelongedTo.Party)
                return "这位贵族已经不由该部队拘押。";
            return null;
        }
        public MeetingEffect Prepare(MeetingClauseContext context, MeetingClause clause)
        {
            var party = context.Actor.PartyBelongedTo!;
            var roster = party.PrisonRoster;
            var victim = roster.GetTroopRoster().First(p => p.Character?.StringId == clause.Parameters["prisonerId"]?.ToString()).Character;
            var count = int.Parse(clause.Parameters["count"]!.ToString());
            var before = roster.GetTroopCount(victim);
            return new MeetingEffect
            {
                Name = context.Actor.Name + "释放" + victim.Name + "（" + count + "人）", Order = 10,
                Resources = new[] { "prisoner:" + party.StringId + ":" + victim.StringId },
                Validate = () => context.Session.Epoch != MainThreadExecutor.CampaignEpoch || context.Actor.PartyBelongedTo != party
                    ? "释放者所在部队已变化。" : Validate(context, clause),
                Apply = () =>
                {
                    if (victim.IsHero) EndCaptivityAction.ApplyByReleasedByChoice(victim.HeroObject, context.Actor);
                    else
                    {
                        var current = roster.GetTroopRoster().First(p => p.Character == victim);
                        var wounded = Math.Max(0, count - (current.Number - current.WoundedNumber));
                        roster.AddToCounts(victim, -count, woundedCount: -wounded);
                    }
                },
                IsApplied = () => roster.GetTroopCount(victim) <= before - count && (!victim.IsHero || !victim.HeroObject.IsPrisoner),
                HasChanged = () => roster.GetTroopCount(victim) != before || (victim.IsHero && !victim.HeroObject.IsPrisoner)
            };
        }
    }
}
