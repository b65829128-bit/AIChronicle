using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace MyFirstMod
{
    internal sealed class PendingAction
    {
        public Hero Hero = null!;
        public AiBehavior Behavior;
        public Settlement? TargetSettlement;
        public MobileParty? TargetParty;
        public int WaitHours;
        public float CheckInHours;
        public CampaignTime? ArrivedAt;
        public bool TargetReached;
        public bool CheckInQueued;
        public bool ActivateOnComplete;
    }

    public static class PartyBehaviorManager
    {
        private static readonly Dictionary<string, PendingAction> _pendingActions = new();

        internal static PendingAction GetOrCreateAction(Hero hero)
        {
            var key = hero.Id.ToString();
            if (!_pendingActions.TryGetValue(key, out var action))
            {
                action = new PendingAction { Hero = hero };
                _pendingActions[key] = action;
            }
            return action;
        }

        internal static void RemoveAction(Hero hero)
        {
            _pendingActions.Remove(hero.Id.ToString());
        }

        public static void Tick()
        {
            if (_pendingActions.Count == 0 || Campaign.Current == null)
                return;

            var keysToRemove = new List<string>();

            foreach (var kv in _pendingActions)
            {
                try
                {
                    var action = kv.Value;
                    var hero = action.Hero;
                    if (hero == null)
                    {
                        keysToRemove.Add(kv.Key);
                        continue;
                    }

                    var party = hero.PartyBelongedTo;
                    if (party == null || !party.IsActive)
                    {
                        keysToRemove.Add(kv.Key);
                        continue;
                    }

                    if (action.Behavior == AiBehavior.EngageParty
                        || action.Behavior == AiBehavior.EscortParty)
                    {
                        if (action.TargetParty == null || !action.TargetParty.IsActive)
                        {
                            keysToRemove.Add(kv.Key);
                            continue;
                        }
                    }

                    if (action.Behavior == AiBehavior.GoToSettlement
                        && action.TargetSettlement != null
                        && party.CurrentSettlement == action.TargetSettlement
                        && action.ArrivedAt == null)
                    {
                        action.ArrivedAt = CampaignTime.Now;
                    }

                    if (action.ArrivedAt != null)
                    {
                        var elapsed = (CampaignTime.Now - action.ArrivedAt.Value).ToHours;
                        if (action.WaitHours <= 0 || elapsed >= action.WaitHours)
                        {
                            keysToRemove.Add(kv.Key);
                            if (action.TargetSettlement != null)
                            {
                                InformationManager.DisplayMessage(new InformationMessage(
                                    $"[MyFirstMod] {hero.Name} 结束了在{action.TargetSettlement.Name}的停留。",
                                    Colors.Cyan));
                            }
                            QueuePlanCheckIn(action);
                            continue;
                        }
                        continue;
                    }

                    if (action.TargetSettlement == null && action.TargetParty == null)
                        continue;

                    bool isOneShot = action.Behavior == AiBehavior.RaidSettlement
                        || action.Behavior == AiBehavior.BesiegeSettlement
                        || action.Behavior == AiBehavior.EngageParty;

                    if (!action.TargetReached && action.TargetSettlement != null)
                    {
                        if (action.Behavior == AiBehavior.BesiegeSettlement)
                            action.TargetReached = party.BesiegedSettlement == action.TargetSettlement;
                        else if (action.Behavior == AiBehavior.DefendSettlement
                            || action.Behavior == AiBehavior.PatrolAroundPoint)
                            action.TargetReached = party.DefaultBehavior == action.Behavior
                                && party.TargetSettlement == action.TargetSettlement;
                        else
                            action.TargetReached = party.CurrentSettlement == action.TargetSettlement;

                        if (action.TargetReached && action.ArrivedAt == null)
                            action.ArrivedAt = CampaignTime.Now;
                    }

                    if (!action.TargetReached && action.TargetParty != null)
                    {
                        if (action.Behavior == AiBehavior.EngageParty)
                            action.TargetReached = party.MapEvent != null;
                        else if (action.Behavior == AiBehavior.EscortParty)
                            action.TargetReached = party.DefaultBehavior == AiBehavior.EscortParty
                                && party.TargetParty == action.TargetParty;

                        if (action.TargetReached && action.ArrivedAt == null)
                            action.ArrivedAt = CampaignTime.Now;
                    }

                    if (action.CheckInHours > 0f && action.TargetReached
                        && !action.CheckInQueued && action.ArrivedAt != null
                        && (CampaignTime.Now - action.ArrivedAt.Value).ToHours >= action.CheckInHours)
                    {
                        action.CheckInQueued = true;

                        var agentEntity = EntityManager.GetEntityByHero(hero);
                        if (agentEntity != null)
                        {
                            var locDesc = action.TargetSettlement?.Name?.ToString()
                                ?? action.TargetParty?.Name?.ToString()
                                ?? "当前位置";
                            var behaviorDesc = action.Behavior switch
                            {
                                AiBehavior.DefendSettlement => "驻防",
                                AiBehavior.PatrolAroundPoint => "巡逻",
                                AiBehavior.EscortParty => "护送",
                                _ => "执行任务"
                            };

                            var checkInContent =
                                $"你已在{locDesc}{behaviorDesc}了{(int)action.CheckInHours}小时以上。\n" +
                                "是否需要：1) 继续当前任务 2) 前往别处 3) 向阵营领袖汇报情况。";

                            AgentScheduler.QueueEvent(new ActivationEvent
                            {
                                Type = ActivationEventType.BehaviorCheckIn,
                                AgentId = agentEntity.Id,
                                TargetId = agentEntity.Id,
                                Content = checkInContent,
                                Depth = 0
                            });
                        }

                        keysToRemove.Add(kv.Key);
                        continue;
                    }

                    var shortTerm = party.ShortTermBehavior;
                    bool isFleeing = shortTerm == AiBehavior.FleeToPoint
                        || shortTerm == AiBehavior.FleeToGate
                        || shortTerm == AiBehavior.FleeToParty;
                    bool isFighting = party.MapEvent != null;

                    if (!isFleeing && !isFighting)
                    {
                        if (isOneShot && action.TargetReached && party.DefaultBehavior != action.Behavior)
                        {
                            keysToRemove.Add(kv.Key);
                            continue;
                        }

                        bool needsReissue = party.DefaultBehavior != action.Behavior;
                        if (!needsReissue && action.TargetSettlement != null)
                            needsReissue = party.TargetSettlement != action.TargetSettlement;
                        if (!needsReissue && action.TargetParty != null)
                            needsReissue = party.TargetParty != action.TargetParty;

                        if (needsReissue)
                        {
                            var navType = party.IsCurrentlyAtSea
                                ? MobileParty.NavigationType.Naval
                                : MobileParty.NavigationType.Default;

                            switch (action.Behavior)
                            {
                                case AiBehavior.GoToSettlement:
                                    party.SetMoveGoToSettlement(action.TargetSettlement!, navType, false);
                                    break;
                                case AiBehavior.RaidSettlement:
                                    SetPartyAiAction.GetActionForRaidingSettlement(party, action.TargetSettlement!, navType, false, false);
                                    break;
                                case AiBehavior.BesiegeSettlement:
                                    SetPartyAiAction.GetActionForBesiegingSettlement(party, action.TargetSettlement!, navType, false);
                                    break;
                                case AiBehavior.EngageParty:
                                    SetPartyAiAction.GetActionForEngagingParty(party, action.TargetParty!, navType, false);
                                    break;
                                case AiBehavior.DefendSettlement:
                                    SetPartyAiAction.GetActionForDefendingSettlement(party, action.TargetSettlement!, navType, false, false);
                                    break;
                                case AiBehavior.PatrolAroundPoint:
                                    SetPartyAiAction.GetActionForPatrollingAroundSettlement(party, action.TargetSettlement!, navType, false, false);
                                    break;
                                case AiBehavior.EscortParty:
                                    SetPartyAiAction.GetActionForEscortingParty(party, action.TargetParty!, navType, false, false);
                                    break;
                            }
                        }
                    }
                }
                catch
                {
                    keysToRemove.Add(kv.Key);
                }
            }

            foreach (var key in keysToRemove)
                _pendingActions.Remove(key);
        }

        private static void QueuePlanCheckIn(PendingAction action)
        {
            if (!action.ActivateOnComplete) return;
            var agentEntity = EntityManager.GetEntityByHero(action.Hero);
            if (agentEntity == null) return;

            var locName = action.TargetSettlement?.Name?.ToString() ?? "目的地";
            var behaviorDesc = action.Behavior switch
            {
                AiBehavior.GoToSettlement => $"到达{locName}",
                _ => $"在{locName}完成等待"
            };

            AgentScheduler.QueueEvent(new ActivationEvent
            {
                Type = ActivationEventType.PlanCheckIn,
                AgentId = agentEntity.Id,
                TargetId = agentEntity.Id,
                Content = behaviorDesc,
                Depth = 0
            });
        }
    }
}
