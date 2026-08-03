using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AIChronicle
{
    internal sealed class PendingAction
    {
        public Hero Hero = null!;
        public AiBehavior Behavior;
        public Settlement? TargetSettlement;
        public MobileParty? TargetParty;
        public int WaitHours;
        public float CheckInHours;
        public CampaignTime? CreatedAt;   // 持久行为下发时间（卡死保险：到不了目标点时超时释放用）
        public CampaignTime? ArrivedAt;
        public bool TargetReached;
        public bool CheckInQueued;
        public bool ActivateOnComplete;
    }

    public static class PartyBehaviorManager
    {
        // 并发修复：工具在后台线程注册/移除动作，Tick 在主线程遍历——
        // 普通 Dictionary 枚举中被修改会抛 "Collection was modified"。加锁 + 快照遍历。
        private static readonly object _pendingLock = new();
        private static readonly Dictionary<string, PendingAction> _pendingActions = new();

        internal static PendingAction GetOrCreateAction(Hero hero)
        {
            var key = hero.Id.ToString();
            lock (_pendingLock)
            {
                if (!_pendingActions.TryGetValue(key, out var action))
                {
                    action = new PendingAction { Hero = hero };
                    _pendingActions[key] = action;
                }
                return action;
            }
        }

        internal static void RemoveAction(Hero hero)
        {
            lock (_pendingLock)
            {
                _pendingActions.Remove(hero.Id.ToString());
            }
        }

        /// <summary>战役结束/切档时清空待执行动作与检查站冷却。</summary>
        public static void ResetForNewCampaign()
        {
            lock (_pendingLock)
            {
                _pendingActions.Clear();
            }
            _lastCheckInByAgent.Clear();
        }

        // 检查站冷却：防止 move/wait activate 到达后立刻签到 → agent 再发新指令 → 死循环（蒙楚格/加里俄斯反复激活的根因）。
        // 用真实时间（而非游戏时间）——游戏时间加速时 12 游戏小时可能只是几十真实秒，根本挡不住循环。
        private static readonly Dictionary<string, DateTime> _lastCheckInByAgent = new();
        private const double MinCheckInIntervalRealMinutes = 15f;

        private static bool CheckInCooldownPassed(string agentId)
        {
            if (!_lastCheckInByAgent.TryGetValue(agentId, out var last)) return true;
            return (DateTime.Now - last).TotalMinutes >= MinCheckInIntervalRealMinutes;
        }

        private static void MarkCheckIn(string agentId)
        {
            _lastCheckInByAgent[agentId] = DateTime.Now;
        }

        public static void Tick()
        {
            if (Campaign.Current == null)
                return;

            List<KeyValuePair<string, PendingAction>> snapshot;
            lock (_pendingLock)
            {
                if (_pendingActions.Count == 0) return;
                snapshot = _pendingActions.ToList();
            }

            var keysToRemove = new List<string>();

            foreach (var kv in snapshot)
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

                    // 持久行为下发时间兜底（工具侧已设置；此处防遗漏，保证卡死保险有时间基准）
                    if (action.CheckInHours > 0f && action.CreatedAt == null)
                        action.CreatedAt = CampaignTime.Now;

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
                        // 修复：持续性行为（驻防/巡逻/护送，CheckInHours>0）到达岗位后保留动作，
                        // 让下方 CheckInHours 分支到期触发签到——原逻辑 WaitHours<=0 立即删除，
                        // 使定时签到成为死代码、中断后也不再重发。
                        var isPersistent = action.CheckInHours > 0f;
                        if (!isPersistent && (action.WaitHours <= 0 || elapsed >= action.WaitHours))
                        {
                            keysToRemove.Add(kv.Key);
                            if (action.TargetSettlement != null)
                            {
                                InformationManager.DisplayMessage(new InformationMessage(
                                    $"[AI编年史] {hero.Name} 结束了在{action.TargetSettlement.Name}的停留。",
                                    Colors.Cyan));
                            }
                            QueuePlanCheckIn(action);
                            continue;
                        }
                        if (!isPersistent)
                            continue; // 非持续性等待未到期：保持等待
                        // 持续性行为：继续往下走，CheckInHours 到期时由下方分支触发签到
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
                        {
                            // 修复：用距离判定真实到达——原 DefaultBehavior 判定在发令瞬间即真，
                            // 导致签到从"下令时刻"而非"实际到达"开始计时
                            var myPos = party.GetPosition2D;
                            var targetPos = action.TargetSettlement!.GatePosition.ToVec2();
                            var dx = myPos.X - targetPos.X;
                            var dy = myPos.Y - targetPos.Y;
                            action.TargetReached = (dx * dx + dy * dy) < 25f;
                        }
                        else if (action.Behavior == AiBehavior.GoToSettlement)
                        {
                            if (party.CurrentSettlement == action.TargetSettlement)
                                action.TargetReached = true;
                            else if (party.DefaultBehavior != AiBehavior.GoToSettlement)
                            {
                                var myPos = party.GetPosition2D;
                                var targetPos = action.TargetSettlement.GatePosition.ToVec2();
                                var dx = myPos.X - targetPos.X;
                                var dy = myPos.Y - targetPos.Y;
                                action.TargetReached = (dx * dx + dy * dy) < 25f;
                            }
                        }
                        else
                            action.TargetReached = party.CurrentSettlement == action.TargetSettlement;

                        if (action.TargetReached && action.ArrivedAt == null)
                            action.ArrivedAt = CampaignTime.Now;
                    }

                    if (!action.TargetReached && action.TargetParty != null)
                    {
                        if (action.Behavior == AiBehavior.EngageParty)
                        {
                            // 修复：只有"卷入的战斗包含追击目标"才算到达——原 `MapEvent != null` 会因卷入无关战斗（被伏击/救援）而误终结追击
                            action.TargetReached = party.MapEvent != null && IsPartyInMapEvent(party.MapEvent, action.TargetParty);
                        }
                        else if (action.Behavior == AiBehavior.EscortParty)
                            action.TargetReached = party.DefaultBehavior == AiBehavior.EscortParty
                                && party.TargetParty == action.TargetParty;

                        if (action.TargetReached && action.ArrivedAt == null)
                            action.ArrivedAt = CampaignTime.Now;
                    }

                    // 卡死保险：持久行为（驻防/巡逻/护送）下发后长时间未能到达目标点
                    //（被拦截/目标遥不可及/巡逻绕圈不进判定圈），强制触发签到释放——
                    // 否则 PendingAction 永不移除、mod 每帧重发覆盖原版 AI、agent 永不激活而静默卡死。
                    // 超时阈值 = 2× 签到周期（驻防 6 天/巡逻 4 天/护送 2 天）。正常到达时此分支永不触发。
                    if (action.CheckInHours > 0f && !action.TargetReached
                        && action.CreatedAt != null
                        && (CampaignTime.Now - action.CreatedAt.Value).ToDays >= action.CheckInHours / 24f * 2f
                        && CheckInCooldownPassed(hero.Id.ToString()))
                    {
                        MarkCheckIn(hero.Id.ToString());
                        var agentEntity = EntityManager.GetEntityByHero(hero);
                        if (agentEntity != null)
                        {
                            var locDesc = action.TargetSettlement?.Name?.ToString()
                                ?? action.TargetParty?.Name?.ToString()
                                ?? "目标";
                            var behaviorDesc = action.Behavior switch
                            {
                                AiBehavior.DefendSettlement => "驻防",
                                AiBehavior.PatrolAroundPoint => "巡逻",
                                AiBehavior.EscortParty => "护送",
                                _ => "执行任务"
                            };
                            var stuckContent =
                                $"你奉命{behaviorDesc} {locDesc}，但已过去{(int)(action.CheckInHours * 2)}小时仍未到达目标（可能被拦截或目标遥不可及）。\n" +
                                $"请决定：1) 放弃该任务，转往他处 2) 继续尝试 3) 向阵营领袖汇报。";
                            AgentScheduler.QueueEvent(new ActivationEvent
                            {
                                Type = ActivationEventType.BehaviorCheckIn,
                                AgentId = agentEntity.Id,
                                TargetId = agentEntity.Id,
                                Content = stuckContent,
                                Depth = 0
                            });
                        }
                        keysToRemove.Add(kv.Key);
                        continue;
                    }

                    if (action.CheckInHours > 0f && action.TargetReached
                        && !action.CheckInQueued && action.ArrivedAt != null
                        && (CampaignTime.Now - action.ArrivedAt.Value).ToHours >= action.CheckInHours
                        && CheckInCooldownPassed(hero.Id.ToString()))
                    {
                        action.CheckInQueued = true;
                        MarkCheckIn(hero.Id.ToString());

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

            lock (_pendingLock)
            {
                foreach (var key in keysToRemove)
                    _pendingActions.Remove(key);
            }
        }

        private static bool IsPartyInMapEvent(MapEvent mapEvent, MobileParty? targetParty)
        {
            if (mapEvent == null || targetParty == null) return false;
            foreach (var side in new[] { BattleSideEnum.Attacker, BattleSideEnum.Defender })
            {
                foreach (var mep in mapEvent.PartiesOnSide(side))
                {
                    if (mep.Party?.MobileParty == targetParty)
                        return true;
                }
            }
            return false;
        }

        private static void QueuePlanCheckIn(PendingAction action)
        {
            if (!action.ActivateOnComplete) return;
            var agentEntity = EntityManager.GetEntityByHero(action.Hero);
            if (agentEntity == null) return;
            // 冷却中：跳过本次签到——打断「move 到达→签到→再 move」的快速循环（蒙楚格反复激活根因）
            if (!CheckInCooldownPassed(action.Hero.Id.ToString())) return;
            MarkCheckIn(action.Hero.Id.ToString());

            var party = action.Hero.PartyBelongedTo;
            if (party != null && party.IsActive)
                party.SetMoveModeHold();

            var locName = action.TargetSettlement?.Name?.ToString() ?? "目的地";
            var behaviorDesc = action.Behavior switch
            {
                AiBehavior.GoToSettlement => $"到达{locName}",
                _ => $"在{locName}完成等待"
            };

            var content = $"你{behaviorDesc}。你现在必须：\n"
                + "1. 用 glob(\"goals/plan_*\") 找你的活跃计划\n"
                + "2. 用 read_file 读计划文件，查看当前进度\n"
                + "3. 执行计划的下一步。如果计划已全部完成，用 move_file 将计划移到 goals/done_名称";

            AgentScheduler.QueueEvent(new ActivationEvent
            {
                Type = ActivationEventType.PlanCheckIn,
                AgentId = agentEntity.Id,
                TargetId = agentEntity.Id,
                Content = content,
                Depth = 0
            });
        }
    }
}
