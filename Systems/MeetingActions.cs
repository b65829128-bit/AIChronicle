using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace AIChronicle
{
    public sealed class MeetingTradeAction : IMeetingAction
    {
        public string Id => "trade";
        public string Title => "交易与赠礼";
        public string Description => "组合金币、物资与行动条件：交换财物、释放俘虏，或商议家族转换阵营。";
        public string? Unavailable(MeetingSession session) => null;
        public IReadOnlyList<MeetingField> Fields(MeetingSession session) => Array.Empty<MeetingField>();
        public string? Validate(MeetingSession session, MeetingTerms terms)
            => terms.OtherOffer.Gold == 0 && terms.YourOffer.Gold == 0
                && terms.OtherOffer.Items.Count == 0 && terms.YourOffer.Items.Count == 0
                && terms.OtherOffer.Actions.Count == 0 && terms.YourOffer.Actions.Count == 0 ? "请至少提出一项财物或行动条件。" : null;
        public void Apply(MeetingSession session, MeetingTerms terms) { }
        public bool IsApplied(MeetingSession session, MeetingTerms terms) => false;
        public string Describe(MeetingSession session, MeetingTerms terms) => "双方履行约定的交换条件。";
        public void RecordApplied(MeetingSession session, MeetingTerms terms, string result) { }
        public MeetingEffect? PrepareEffect(MeetingSession session, MeetingTerms terms) => null;
    }

    public sealed class MeetingMarriageAction : IMeetingAction
    {
        public string Id => "marriage";
        public string Title => "家族婚配";
        public string Description => "指定双方人选，提出无条件婚配，或附上双向金币、物资条件，与家族领袖协商。";

        public string? Unavailable(MeetingSession session)
        {
            if (session.Player.Clan == null || session.Npc.Clan == null) return "双方须属于家族。";
            if (session.Player.Clan == session.Npc.Clan) return "请与其他家族的领袖议婚。";
            if (session.Npc.Clan.Leader != session.Npc) return "对方不是家族领袖，无权代表家族议婚。";
            if (FactionManager.IsAtWarAgainstFaction(session.Player.MapFaction, session.Npc.MapFaction)) return "交战期间不能安排家族婚配。";
            return null;
        }

        private static IEnumerable<Hero> Candidates(MeetingSession session, bool playerSide)
        {
            var representative = playerSide ? session.Player : session.Npc;
            if (representative.Clan == null) return Array.Empty<Hero>();
            var model = Campaign.Current.Models.MarriageModel;
            return representative.Clan.AliveLords.Where(h => h.IsAlive && !h.IsPrisoner && h.Age >= 18
                && model.IsSuitableForMarriage(h)
                && (!playerSide || representative.Clan.Leader == representative || h == representative));
        }

        public IReadOnlyList<MeetingField> Fields(MeetingSession session)
        {
            var player = Candidates(session, true).ToList();
            var npc = Candidates(session, false).ToList();
            var model = Campaign.Current.Models.MarriageModel;
            return new[]
            {
                Field("otherHeroId", session.Player.Name + "家族人选", player.Where(p => npc.Any(n => model.IsCoupleSuitableForMarriage(p, n)))),
                Field("yourHeroId", session.Npc.Name + "家族人选", npc.Where(n => player.Any(p => model.IsCoupleSuitableForMarriage(p, n))))
            };
        }

        private static MeetingField Field(string key, string label, IEnumerable<Hero> heroes) => new()
        {
            Key = key, Label = label,
            Choices = heroes.Select(h => new MeetingChoice { Id = h.StringId, Label = $"{h.Name} · {(int)h.Age}岁" }).ToList()
        };

        private static (Hero? player, Hero? npc) Couple(MeetingSession session, MeetingTerms terms)
        {
            var p = terms.Parameters["otherHeroId"]?.ToString();
            var n = terms.Parameters["yourHeroId"]?.ToString();
            return (session.Player.Clan?.AliveLords.FirstOrDefault(h => h.StringId == p),
                session.Npc.Clan?.AliveLords.FirstOrDefault(h => h.StringId == n));
        }

        public string? Validate(MeetingSession session, MeetingTerms terms)
        {
            var unavailable = Unavailable(session);
            if (unavailable != null) return unavailable;
            var (player, npc) = Couple(session, terms);
            if (player == null || npc == null) return "请选择双方家族中的真实成员。";
            if (!Candidates(session, true).Contains(player) || !Candidates(session, false).Contains(npc))
                return "人选当前不符合婚配条件，或你无权代表该成员。";
            if (!Campaign.Current.Models.MarriageModel.IsCoupleSuitableForMarriage(player, npc))
                return "这两位成员不符合游戏当前的婚配规则。";
            return null;
        }

        public void Apply(MeetingSession session, MeetingTerms terms)
        {
            var (player, npc) = Couple(session, terms);
            if (player == null || npc == null) throw new InvalidOperationException("婚配人选已变化。");
            MarriageAction.Apply(player, npc, true);
            if (!IsApplied(session, terms)) throw new InvalidOperationException("游戏没有完成这桩婚配。");
        }

        public bool IsApplied(MeetingSession session, MeetingTerms terms)
        {
            // 成婚会改变 Clan，不能再从原家族查人。
            var p = terms.Parameters["otherHeroId"]?.ToString();
            var n = terms.Parameters["yourHeroId"]?.ToString();
            return Hero.AllAliveHeroes.Any(h => h.StringId == p && h.Spouse != null && h.Spouse.StringId == n
                && h.Spouse.Spouse == h && h.Clan == h.Spouse.Clan);
        }

        public string Describe(MeetingSession session, MeetingTerms terms)
        {
            var p = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == terms.Parameters["otherHeroId"]?.ToString());
            var n = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == terms.Parameters["yourHeroId"]?.ToString());
            if (p == null || n == null) return "双方指定成员成婚。";
            var clan = p.Spouse == n ? p.Clan : Campaign.Current.Models.MarriageModel.GetClanAfterMarriage(p, n);
            return $"{p.Name}与{n.Name}成婚；婚后归属：{clan?.Name}家族。";
        }

        public void RecordApplied(MeetingSession session, MeetingTerms terms, string result)
            => HistoryRecorder.RecordMeetingMarriage(result);

        public MeetingEffect PrepareEffect(MeetingSession session, MeetingTerms terms)
        {
            var (first, second) = Couple(session, terms);
            if (first == null || second == null) throw new InvalidOperationException("婚配人选已变化。");
            var firstClan = first.Clan;
            var secondClan = second.Clan;
            return new MeetingEffect
            {
                Name = Describe(session, terms), Order = 20,
                Resources = new[] { "clan:" + firstClan.StringId, "clan:" + secondClan.StringId },
                Validate = () => session.Epoch == MainThreadExecutor.CampaignEpoch ? Validate(session, terms) : "会晤所属战役已结束。",
                Apply = () => Apply(session, terms), IsApplied = () => IsApplied(session, terms),
                HasChanged = () => first.Spouse == second || second.Spouse == first || first.Clan != firstClan || second.Clan != secondClan
            };
        }
    }
}
