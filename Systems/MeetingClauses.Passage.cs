using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;

namespace AIChronicle
{
    /// <summary>限本次决斗：只有参与遭遇的领队可承诺结束该次遭遇，不替第三方军队承诺停战。</summary>
    public sealed class MeetingPassageClause : IMeetingClauseHandler, IMeetingScopedClause
    {
        public string Id => "safe_passage";
        public string Title => "结束本次遭遇并放行";
        public string Description => "由遭遇双方领队约定战后结束本次遭遇并准许对方离开；不代表整个王国停战。";
        public IReadOnlyList<string> ActionTypes => new[] { "duel" };
        public string? Unavailable(MeetingClauseContext context)
        {
            var session = context.Session;
            if (session.Encounter == null || PlayerEncounter.InsideSettlement) return "只适用于当前地图遭遇的决斗。";
            var own = context.Actor.PartyBelongedTo;
            var other = context.Other.PartyBelongedTo;
            if (own == null || own.LeaderHero != context.Actor || other == null || own == other)
                return "只有各自带领部队的遭遇双方能约定放行。";
            if (own != MobileParty.MainParty && own != PlayerEncounter.EncounteredMobileParty) return "无法代表当前遭遇的领队。";
            return null;
        }
        public IReadOnlyList<MeetingField> Fields(MeetingClauseContext context) => Array.Empty<MeetingField>();
        public string? Validate(MeetingClauseContext context, MeetingClause clause) => Unavailable(context);
        public MeetingEffect Prepare(MeetingClauseContext context, MeetingClause clause)
        {
            var party = context.Actor.PartyBelongedTo!;
            var applied = false;
            return new MeetingEffect
            {
                Name = context.Actor.Name + "准许" + context.Other.Name + "及其部队结束本次遭遇并离开",
                Order = 100, Resources = new[] { "encounter:passage:" + context.Session.Id },
                Validate = () => context.Session.Epoch != MainThreadExecutor.CampaignEpoch ? "原战役已经结束。"
                    : context.Actor.PartyBelongedTo != party || party.LeaderHero != context.Actor ? "原领队或部队已变化。"
                    : DuelService.IsFatal(context.Session, context.Other) ? "对方战斗判定致命，本次未执行放行。"
                    : PlayerEncounter.Current != null && !ReferenceEquals(PlayerEncounter.Current, context.Session.Encounter) ? "已经进入另一场遭遇。" : null,
                Apply = () =>
                {
                    DuelService.ArrangePassage(context.Session);
                    applied = true;
                    if (party != MobileParty.MainParty)
                    {
                        party.Ai.SetDoNotAttackMainParty(12);
                        MobileParty.MainParty.IgnoreForHours(6);
                    }
                },
                IsApplied = () => applied,
                HasChanged = () => applied || PlayerEncounter.LeaveEncounter
            };
        }
    }
}
