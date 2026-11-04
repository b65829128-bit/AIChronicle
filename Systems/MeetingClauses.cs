using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace AIChronicle
{
    public sealed class MeetingClauseContext
    {
        public MeetingSession Session { get; }
        public MeetingSide Side { get; }
        public Hero Actor => Side == MeetingSide.Other ? Session.Player : Session.Npc;
        public Hero Other => Side == MeetingSide.Other ? Session.Npc : Session.Player;
        public MeetingClauseContext(MeetingSession session, MeetingSide side) { Session = session; Side = side; }
    }

    /// <summary>报价内可组合的行动。新增 handler 并注册即可复用双方表单、AI schema 与批量执行器。</summary>
    public interface IMeetingClauseHandler
    {
        string Id { get; }
        string Title { get; }
        string Description { get; }
        string? Unavailable(MeetingClauseContext context);
        IReadOnlyList<MeetingField> Fields(MeetingClauseContext context);
        string? Validate(MeetingClauseContext context, MeetingClause clause);
        MeetingEffect Prepare(MeetingClauseContext context, MeetingClause clause);
    }

    public static class MeetingClauses
    {
        private static readonly Dictionary<string, IMeetingClauseHandler> Handlers = new(StringComparer.Ordinal);
        static MeetingClauses()
        {
            Register(new MeetingReleasePrisonerClause());
            Register(new MeetingAllegianceClause());
        }
        public static void Register(IMeetingClauseHandler handler)
        {
            if (handler == null || string.IsNullOrWhiteSpace(handler.Id)) throw new ArgumentException("行动须有唯一编号。");
            if (Handlers.ContainsKey(handler.Id)) throw new InvalidOperationException("重复行动类型：" + handler.Id);
            Handlers.Add(handler.Id, handler);
        }
        public static IReadOnlyList<IMeetingClauseHandler> All => Handlers.Values.ToList();
        public static IMeetingClauseHandler? Find(string id) => Handlers.TryGetValue(id, out var handler) ? handler : null;

        public static string? Validate(MeetingSession session, MeetingTerms terms)
        {
            foreach (var side in new[] { MeetingSide.Other, MeetingSide.Self })
            {
                var context = new MeetingClauseContext(session, side);
                foreach (var clause in Offer(terms, side).Actions)
                {
                    var handler = Find(clause.TypeId);
                    if (handler == null) return "这类行动尚未开放。";
                    var invalid = handler.Unavailable(context) ?? MeetingFieldRules.Validate(handler.Fields(context), clause.Parameters)
                        ?? handler.Validate(context, clause);
                    if (invalid != null) return context.Actor.Name + "：" + invalid;
                }
            }
            return MeetingBatch.Validate(Prepare(session, terms));
        }

        public static List<MeetingEffect> Prepare(MeetingSession session, MeetingTerms terms)
        {
            var effects = new List<MeetingEffect>();
            foreach (var side in new[] { MeetingSide.Other, MeetingSide.Self })
            {
                var context = new MeetingClauseContext(session, side);
                effects.AddRange(Offer(terms, side).Actions.Select(c => Find(c.TypeId)!.Prepare(context, c)));
            }
            var primary = MeetingRegistry.Find(terms.TypeId)!.PrepareEffect(session, terms);
            if (primary != null) effects.Add(primary);
            return effects;
        }

        public static string Describe(MeetingSession session, MeetingSide side, MeetingOffer offer)
        {
            var context = new MeetingClauseContext(session, side);
            var actions = offer.Actions.Select(c => Find(c.TypeId)?.Prepare(context, c).Name ?? "未开放行动").ToList();
            var assets = MeetingAssets.DescribeOffer(context.Actor, offer);
            return actions.Count == 0 ? assets : assets + "；承诺：" + string.Join("、", actions);
        }

        private static MeetingOffer Offer(MeetingTerms terms, MeetingSide side) => side == MeetingSide.Other ? terms.OtherOffer : terms.YourOffer;
    }
}
