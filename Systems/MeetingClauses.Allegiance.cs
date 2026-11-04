using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace AIChronicle
{
    public sealed class MeetingAllegianceClause : IMeetingClauseHandler
    {
        public string Id => "change_allegiance";
        public string Title => "家族加入／叛逃";
        public string Description => "由家族领袖承诺让整个家族加入指定王国；已有君主时按原版叛逃执行。";
        public string? Unavailable(MeetingClauseContext context)
        {
            var clan = context.Actor.Clan;
            if (clan == null || clan.Leader != context.Actor || !context.Actor.IsLord) return "只有家族领袖能决定整个家族的归属。";
            if (clan.IsEliminated || clan.IsBanditFaction || clan.IsRebelClan) return "该家族当前不能加入王国。";
            if (clan.Kingdom?.RulingClan == clan) return "王室须先处理王位，不能直接作为封臣家族叛逃。";
            if (context.Actor.PartyBelongedTo?.MapEvent != null) return "家族领袖正参与战斗，暂时不能转换阵营。";
            var tier = Campaign.Current.Models.ClanTierModel.VassalEligibleTier;
            if (clan.Tier < tier) return "家族等级不足，成为封臣需要 " + tier + " 级。";
            return null;
        }
        public IReadOnlyList<MeetingField> Fields(MeetingClauseContext context) => new[]
        {
            new MeetingField { Key = "kingdomId", Label = "整个家族加入的王国", Choices = Kingdom.All
                .Where(k => !k.IsEliminated && k != context.Actor.Clan?.Kingdom)
                .Select(k => new MeetingChoice { Id = k.StringId, Label = k.Name.ToString() }).ToList() }
        };
        public string? Validate(MeetingClauseContext context, MeetingClause clause)
        {
            var unavailable = Unavailable(context);
            if (unavailable != null) return unavailable;
            var target = Kingdom.All.FirstOrDefault(k => k.StringId == clause.Parameters["kingdomId"]?.ToString() && !k.IsEliminated);
            return target == null || target == context.Actor.Clan!.Kingdom ? "目标王国已失效或家族已属于该国。" : null;
        }
        public MeetingEffect Prepare(MeetingClauseContext context, MeetingClause clause)
        {
            var clan = context.Actor.Clan!;
            var old = clan.Kingdom;
            var oldMercenary = clan.IsUnderMercenaryService;
            var oldInfluence = clan.Influence;
            var target = Kingdom.All.First(k => k.StringId == clause.Parameters["kingdomId"]!.ToString());
            return new MeetingEffect
            {
                Name = clan.Name + "家族" + (old == null ? "加入" : "脱离" + old.Name + "并加入") + target.Name,
                Order = 30, Resources = new[] { "clan:" + clan.StringId },
                Validate = () => context.Session.Epoch != MainThreadExecutor.CampaignEpoch || context.Actor.Clan != clan
                    || clan.Leader != context.Actor || clan.Kingdom != old ? "家族领袖或原阵营已变化。" : Validate(context, clause),
                Apply = () =>
                {
                    if (old == null) ChangeKingdomAction.ApplyByJoinToKingdom(clan, target);
                    else ChangeKingdomAction.ApplyByJoinToKingdomByDefection(clan, old, target);
                    EntityManager.RefreshEntity(context.Actor);
                },
                IsApplied = () => clan.Kingdom == target && !clan.IsUnderMercenaryService,
                HasChanged = () => clan.Kingdom != old || clan.IsUnderMercenaryService != oldMercenary || clan.Influence != oldInfluence
            };
        }
    }
}
