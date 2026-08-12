using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace AIChronicle
{
    public static partial class AgentScheduler
    {
        /// <summary>
        /// 自省激活（由谏言槽位泛化而来）：激活就是激活，激活后可做写信之外的任何事，谏言只是其中一个选项。
        /// 总量不变（每王国每天一次检定，概率沿用 MCM「谏言概率」），选池放宽为封臣 + 雇佣兵 + 独立氏族领袖，
        /// 选择方式改为公平轮转（距上次自省最久者优先），不再按权重抽签。
        /// </summary>
        private static void CheckSelfReviewActivations()
        {
            if (Campaign.Current == null) return;
            if (_warmupFrames > 0) return;
            if (MySettings.Instance?.AdvisoryEnabled != true) return;

            var probability = MySettings.Instance?.AdvisoryProbability ?? 0.1f;

            // 每王国每天一次检定：选该王国最久未自省的家族领袖（封臣+佣兵）
            foreach (var kingdom in Kingdom.All)
            {
                if (kingdom.IsEliminated) continue;
                if (kingdom.RulingClan?.Leader == null || !kingdom.RulingClan.Leader.IsAlive)
                    continue;

                if (!_lastAdvisoryCheck.TryGetValue(kingdom, out var lastCheck))
                {
                    _lastAdvisoryCheck[kingdom] = CampaignTime.Now;
                    continue;
                }

                if ((CampaignTime.Now - lastCheck).ToDays < 1)
                    continue;

                _lastAdvisoryCheck[kingdom] = CampaignTime.Now;

                if (_rng.NextDouble() > probability) continue;

                var leader = SelectSelfReviewLeader(kingdom);
                if (leader != null)
                {
                    QueueSelfReview(leader);
                    return; // 每天只激活一个
                }
            }

            // 独立氏族领袖：无王可依，最需要自省。各自低频检定（概率取一半，防止独立领袖多了把总量顶高）。
            foreach (var entity in IndependentClanLeaders())
            {
                if (!_lastIndependentReviewCheck.TryGetValue(entity.Id, out var lastCheck))
                {
                    _lastIndependentReviewCheck[entity.Id] = CampaignTime.Now;
                    continue;
                }

                if ((CampaignTime.Now - lastCheck).ToDays < 1)
                    continue;

                _lastIndependentReviewCheck[entity.Id] = CampaignTime.Now;

                if (_rng.NextDouble() > probability * 0.5f) continue;

                QueueSelfReview(entity);
                return;
            }
        }

        /// <summary>公平轮转：选该王国里距离上次自省最久（或从未自省）的家族领袖。国王本人不走自省（走 KingDiplomacy）。</summary>
        private static Entity? SelectSelfReviewLeader(Kingdom kingdom)
        {
            var candidates = new List<Entity>();

            foreach (var clan in kingdom.Clans)
            {
                var leader = clan.Leader;
                if (leader == null || !leader.IsAlive) continue;
                if (leader.IsPrisoner || leader.IsFugitive) continue;
                if (leader == kingdom.RulingClan?.Leader) continue; // 国王本人排除

                var entity = EntityManager.GetOrCreateEntity(leader);
                if (entity == null || entity.Controller != EntityController.Agent) continue;

                candidates.Add(entity);
            }

            if (candidates.Count == 0) return null;

            // 距上次自省最久优先；从未自省的（-1）最先轮值
            return candidates
                .OrderBy(e => _lastSelfReviewDay.TryGetValue(e.Id, out var d) ? d : -1)
                .First();
        }

        /// <summary>所有在世独立氏族领袖（无王国、非佣兵势力、非玩家、未被俘/逃亡）的实体列表。</summary>
        private static List<Entity> IndependentClanLeaders()
        {
            var result = new List<Entity>();
            try
            {
                foreach (var clan in Clan.All)
                {
                    if (clan.IsMinorFaction || clan.IsBanditFaction) continue;
                    if (clan.Kingdom != null) continue;
                    var leader = clan.Leader;
                    if (leader == null || !leader.IsAlive) continue;
                    if (leader.IsPrisoner || leader.IsFugitive) continue;
                    var entity = EntityManager.GetOrCreateEntity(leader);
                    if (entity == null || entity.Controller != EntityController.Agent) continue;
                    result.Add(entity);
                }
            }
            catch { }
            return result;
        }

        private static void QueueSelfReview(Entity leader)
        {
            _lastSelfReviewDay[leader.Id] = (int)CampaignTime.Now.ToDays;

            QueueEvent(new ActivationEvent
            {
                Type = ActivationEventType.SelfReview,
                AgentId = leader.Id,
                TargetId = leader.Id, // 自省：对方是自己
                Content = "你正处在一个独处的时刻。审视你自己的处境、家族与将来，然后依你自己的判断行事。",
                Depth = 0
            });
        }
    }
}
