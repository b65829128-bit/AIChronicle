using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;

namespace AIChronicle
{
    public static partial class CuriaService
    {
        private static List<CuriaVote> SelectAttendance(List<CuriaVote> voters, CuriaAgenda agenda, float quorum, int cap)
        {
            var selected = new List<CuriaVote>();
            var remaining = new List<CuriaVote>(voters);

            void Add(CuriaVote v, string reason)
            {
                if (v == null || selected.Contains(v)) return;
                v.SummonReason = reason;
                selected.Add(v);
                remaining.Remove(v);
            }

            // 固定席位：国王/玩家/提议者
            Add(remaining.FirstOrDefault(v => v.IsKing), "固定：国王");
            Add(remaining.FirstOrDefault(v => v.IsPlayer), "固定：玩家");
            Add(remaining.FirstOrDefault(v => v.IsProposer), "固定：提议者");

            // 利害席位：前 3
            var stake = remaining
                .Select(v => new { Voter = v, Score = ComputeStake(v, agenda) })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Voter.Weight)
                .Take(3)
                .ToList();
            foreach (var s in stake) Add(s.Voter, "利害相关");

            // 权重席位：补到 quorum 或上限
            foreach (var v in remaining.OrderByDescending(v => v.Weight).ToList())
            {
                if (selected.Count >= cap && selected.Sum(x => x.Weight) >= quorum) break;
                if (selected.Sum(x => x.Weight) >= quorum && selected.Count >= Math.Min(3, cap)) break;
                Add(v, "权重席位");
            }

            // 轮换席位：再补 1–2 个最久未应召者
            var rotationSlots = Math.Min(2, Math.Max(0, cap - selected.Count));
            foreach (var v in remaining
                         .OrderBy(v => GetLastSummonedDay(agenda.KingdomName, v.ClanId))
                         .ThenByDescending(v => v.Weight)
                         .Take(rotationSlots))
                Add(v, "轮换席位");

            // 软上限：quorum 优先，补满
            foreach (var v in remaining.OrderByDescending(v => v.Weight).ToList())
            {
                if (selected.Sum(x => x.Weight) >= quorum) break;
                Add(v, "权重席位（补足 quorum）");
            }

            return selected;
        }

        private static int GetLastSummonedDay(string kingdomName, string clanId)
        {
            try
            {
                var state = CuriaStore.LoadState(Kingdom.All.FirstOrDefault(k => k.Name?.ToString() == kingdomName));
                return state.LastSummonedDay.TryGetValue(clanId, out var day) ? day : -1;
            }
            catch { return -1; }
        }

        private static float ComputeStake(CuriaVote voter, CuriaAgenda agenda)
        {
            var score = 0f;
            try
            {
                var clan = Clan.All.FirstOrDefault(c => c.StringId == voter.ClanId);
                if (clan == null) return score;
                var kingdom = clan.Kingdom;
                if (kingdom == null) return score;

                if (agenda.Type == CuriaAgendaType.DeclareWar || agenda.Type == CuriaAgendaType.ProposePeace)
                {
                    var target = CuriaStore.FindKingdom(agenda.TargetKingdomName);
                    if (target != null)
                    {
                        if (kingdom.IsAtWarWith(target)) score += 60;
                        var trade = Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
                        if (trade != null && trade.HasTradeAgreement(kingdom, target, out _)) score += 25;
                        score += BorderScore(clan, target);
                    }
                }
                else if (agenda.Type == CuriaAgendaType.ProposeTrade)
                {
                    var target = CuriaStore.FindKingdom(agenda.TargetKingdomName);
                    if (target != null) score += BorderScore(clan, target) + 20;
                }
                else if (agenda.Type == CuriaAgendaType.TransferFief)
                {
                    var settlement = CuriaStore.FindSettlement(agenda.TargetSettlementName);
                    if (settlement != null)
                    {
                        var dist = MinClanDistanceToSettlement(clan, settlement);
                        if (dist < 600f) score += 50;
                        else if (dist < 1200f) score += 25;
                    }
                    var targetClan = Clan.All.FirstOrDefault(c => c.StringId == agenda.TargetClanId);
                    if (targetClan?.Leader != null && clan.Leader != null)
                    {
                        var rel = clan.Leader.GetRelation(targetClan.Leader);
                        if (Math.Abs(rel) >= 20) score += 35;
                    }
                    var newOwner = Clan.All.FirstOrDefault(c => c.StringId == agenda.NewOwnerClanId);
                    if (newOwner == clan) score += 60;
                }
                else if (agenda.Type == CuriaAgendaType.ExpelClan)
                {
                    var targetClan = Clan.All.FirstOrDefault(c => c.StringId == agenda.TargetClanId);
                    if (targetClan?.Leader != null && clan.Leader != null)
                    {
                        var rel = clan.Leader.GetRelation(targetClan.Leader);
                        if (Math.Abs(rel) >= 20) score += 40;
                    }
                    if (targetClan != null)
                    {
                        var dist = MinClanDistanceToClan(clan, targetClan);
                        if (dist < 600f) score += 25;
                    }
                }

                if (clan.Leader != null && agenda.ProposerEntityId.Length > 0)
                {
                    var proposer = CuriaStore.FindHeroByEntityId(agenda.ProposerEntityId);
                    if (proposer != null)
                    {
                        var rel = clan.Leader.GetRelation(proposer);
                        if (Math.Abs(rel) >= 15) score += 15;
                    }
                }
            }
            catch { }
            return score;
        }

        private static float BorderScore(Clan clan, Kingdom target)
        {
            var min = 99999f;
            foreach (var s in clan.Settlements)
            {
                foreach (var t in target.Settlements)
                {
                    try
                    {
                        var d = s.GetPosition2D.Distance(t.GetPosition2D);
                        if (d < min) min = d;
                    }
                    catch { }
                }
            }
            return min < 500f ? 45f : min < 1000f ? 25f : 5f;
        }

        private static float MinClanDistanceToSettlement(Clan clan, Settlement target)
        {
            var min = 99999f;
            try
            {
                var tp = target.GetPosition2D;
                foreach (var s in clan.Settlements)
                {
                    var d = s.GetPosition2D.Distance(tp);
                    if (d < min) min = d;
                }
            }
            catch { }
            return min;
        }

        private static float MinClanDistanceToClan(Clan clan, Clan target)
        {
            var min = 99999f;
            try
            {
                foreach (var s in clan.Settlements)
                foreach (var t in target.Settlements)
                {
                    var d = s.GetPosition2D.Distance(t.GetPosition2D);
                    if (d < min) min = d;
                }
            }
            catch { }
            return min;
        }
    }
}
