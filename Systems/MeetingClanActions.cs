using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;

namespace AIChronicle
{
    /// <summary>两种客观去向共用家庭、财物和执行器；主观意愿由双方同版接受表达。</summary>
    public sealed class MeetingClanAction : IMeetingAction
    {
        private readonly bool _found;
        private readonly ConditionalWeakTable<MeetingSession, ClanDeparture> _completed = new();
        public MeetingClanAction(bool found) { _found = found; }
        public string Id => _found ? "found_clan" : "join_clan";
        public string Title => _found ? "自立家族" : "加入家族";
        public string Description => "由非家族领袖本人决定" + (_found ? "建立新家族并选择王国或保持独立" : "加入指定家族")
            + "；配偶及同族未成年子女随迁，保留本人金钱与部队。可无偿商议或附双方财物；被俘时也可商议，转族本身不代表获释。";

        private static IEnumerable<Hero> Candidates(MeetingSession session) => new[] { session.Player, session.Npc }
            .Where(h => ClanDeparture.Family(h, out _) == null);

        public string? Unavailable(MeetingSession session) => Candidates(session).Any() ? null
            : ClanDeparture.Family(session.Npc, out _) ?? "当前双方都没有可以离族的贵族成员。";

        public IReadOnlyList<MeetingField> Fields(MeetingSession session)
        {
            var fields = new List<MeetingField>
            {
                new() { Key = "familyId", Label = _found ? "自立人选及随迁家庭" : "加入人选及随迁家庭", Choices = Candidates(session).Select(h =>
                {
                    ClanDeparture.Family(h, out var members);
                    return new MeetingChoice { Id = ClanDeparture.FamilyId(h, members),
                        Label = h.Name + (members.Count == 1 ? "（本人）" : "（随迁：" + string.Join("、", members.Skip(1).Select(m => m.Name.ToString())) + "）") };
                }).ToList() }
            };
            if (_found)
            {
                fields.Add(new MeetingField { Key = "clanName", Label = "新家族名称", Kind = "text" });
                fields.Add(new MeetingField { Key = "kingdomId", Label = "新家族归属", Choices = new[]
                    { new MeetingChoice { Id = ClanDeparture.Independent, Label = "保持独立" } }.Concat(Kingdom.All.Where(k => !k.IsEliminated)
                        .Select(k => new MeetingChoice { Id = k.StringId, Label = k.Name.ToString() })).ToList() });
            }
            else fields.Add(new MeetingField { Key = "clanId", Label = "加入的家族", Choices = Clan.All.Where(ClanDeparture.ValidClan)
                .Where(c => Candidates(session).Any(h => h.Clan != c)).Select(c => new MeetingChoice
                { Id = c.StringId, Label = c.Name + " · " + (c.Kingdom?.Name.ToString() ?? "独立") }).ToList() });
            return fields;
        }

        private ClanDeparture Plan(MeetingSession session, MeetingTerms terms)
        {
            if (session.Epoch != MainThreadExecutor.CampaignEpoch) throw new InvalidOperationException("原会晤所属战役已经结束。");
            var invalid = Unavailable(session) ?? MeetingFieldRules.Validate(Fields(session), terms.Parameters);
            if (invalid != null) throw new InvalidOperationException(invalid);
            var founder = Candidates(session).FirstOrDefault(h =>
            {
                ClanDeparture.Family(h, out var members);
                return ClanDeparture.FamilyId(h, members) == terms.Parameters["familyId"]?.ToString();
            }) ?? throw new InvalidOperationException("人选或随迁家庭已经变化，请重新选择。");
            if (_found)
            {
                var id = terms.Parameters["kingdomId"]!.ToString();
                return new ClanDeparture(founder, null, terms.Parameters["clanName"]!.ToString(),
                    id == ClanDeparture.Independent ? null : Kingdom.All.First(k => k.StringId == id));
            }
            return new ClanDeparture(founder, Clan.All.First(c => c.StringId == terms.Parameters["clanId"]!.ToString()), "", null);
        }

        public string? Validate(MeetingSession session, MeetingTerms terms)
        {
            try { return Plan(session, terms).Validate(); }
            catch (InvalidOperationException ex) { return ex.Message; }
        }
        public string Describe(MeetingSession session, MeetingTerms terms) => Plan(session, terms).Description;
        public void Apply(MeetingSession session, MeetingTerms terms) => PrepareEffect(session, terms).Apply();
        public bool IsApplied(MeetingSession session, MeetingTerms terms) => _completed.TryGetValue(session, out var plan) && plan.IsApplied();
        public MeetingEffect PrepareEffect(MeetingSession session, MeetingTerms terms)
        {
            var plan = Plan(session, terms);
            var effect = plan.Effect();
            effect.Apply = () =>
            {
                try { plan.Apply(); }
                finally
                {
                    if (plan.HasChanged()) { _completed.Remove(session); _completed.Add(session, plan); }
                }
            };
            return effect;
        }
        public void RecordApplied(MeetingSession session, MeetingTerms terms, string result)
        {
            if (_completed.TryGetValue(session, out var plan) && plan.IsApplied())
            { _completed.Remove(session); plan.Record(); }
        }
    }
}
