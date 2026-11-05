using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AIChronicle
{
    internal sealed class DuelMatch
    {
        public MeetingSession Session = null!;
        public MeetingRequest Request = null!;
        public Campaign CampaignInstance = null!;
        public string Id => Request.Id;
        public bool IsCurrent => Session.Epoch == MainThreadExecutor.CampaignEpoch && ReferenceEquals(Campaign.Current, CampaignInstance);
        public bool Mounted;
        public double Day;
        public string Date = "", Place = "", Scene = "", Conditions = "", RecordRoot = "", OtherEntityId = "", YourEntityId = "";
        public Clan OtherClan = null!, YourClan = null!;
        public Equipment OtherEquipment = null!, YourEquipment = null!;
        public DuelReward OtherReward = null!, YourReward = null!;
        public DuelReward? AppliedReward;
        public MeetingEscrow Escrow = null!;
        public DuelEncounter Encounter = null!;
        public readonly Dictionary<string, List<MeetingEffect>> Effects = new(StringComparer.Ordinal);
        public readonly DuelResolution Resolution = new();
        public DuelAgentOrigin? OtherOrigin, YourOrigin;
        public Mission? OriginalMission, Mission;
        public bool ConversationEnded, OriginalMissionEnded, SceneOpened, CombatStarted, Finished, RecordsPublished, RewardsAttempted;
        public bool PassageRequested;
        public double ActualRenown, ActualInfluence;
        public int WinnerClanTierBefore = -1, WinnerClanTierAfter = -1;
        public DuelFulfillment? Fulfillment;
        public readonly List<string> RewardNotes = new();
        public string Summary = "", FailureReason = "";
        public DateTime QueuedAt = DateTime.UtcNow;
    }

    /// <summary>主线程跨场景执行；开始后独立持有约定，不依赖已关闭的会晤 UI 或 LLM 上下文。</summary>
    public static class DuelService
    {
        internal static DuelMatch? Active { get; private set; }
        public static bool IsBusy => Active != null;
        public static void ResetForNewCampaign() => Active = null;

        public static string? Unavailable(MeetingSession session)
        {
            if (Active != null) return "当前已经有一场决斗正在准备或结算。";
            if (Campaign.Current == null || MobileParty.MainParty == null) return "当前没有可用的战役场景。";
            if (DuelStore.Current == null || DuelStore.Current.Error != null) return DuelStore.Current?.Error ?? "决斗奖励账本尚未就绪。";
            foreach (var hero in new[] { session.Player, session.Npc })
            {
                if (!hero.IsAlive || hero.Clan == null || hero.Clan.IsEliminated) return hero.Name + "当前无法参加决斗。";
                if (hero.IsChild) return "未成年人不能参加决斗。";
                if (hero.IsWounded || hero.DeathMark != TaleWorlds.CampaignSystem.Actions.KillCharacterAction.KillCharacterActionDetail.None)
                    return hero.Name + "当前伤势过重或死亡状态尚未处理，无法开战。";
            }
            if (!MobileParty.MainParty.Position.IsOnLand) return "当前身处海上，无法布置陆地决斗。";
            var mission = Mission.Current;
            if (mission != null && (mission.Mode == MissionMode.Battle || mission.Mode == MissionMode.Duel || mission.Mode == MissionMode.Tournament
                || mission.GetMissionBehavior<SandBox.Missions.MissionLogics.BattleAgentLogic>() != null))
                return "当前正在实战或比武，须结束当前战斗后再决斗。";
            return null;
        }

        internal static void Queue(MeetingSession session, MeetingRequest request)
        {
            var invalid = Unavailable(session);
            if (invalid != null) throw new InvalidOperationException(invalid);
            if (!MeetingService.IsActive(session.Id) || session.Player != Hero.MainHero || request.Status != MeetingStatus.Running)
                throw new InvalidOperationException("原会晤或已接受的约定已失效。");
            var mounted = request.Terms.Parameters["mounts"]?.ToString() == "mounted";
            var otherEquipment = DuelRewardService.Loadout(session.Player, mounted);
            var yourEquipment = DuelRewardService.Loadout(session.Npc, mounted);
            var day = CampaignTime.Now.ToDays;
            var other = DuelRewardService.Snapshot(session.Player, otherEquipment, mounted);
            var yours = DuelRewardService.Snapshot(session.Npc, yourEquipment, mounted);
            var settlement = session.Player.CurrentSettlement;
            var nearest = settlement ?? Settlement.All.Where(s => s.IsTown).OrderBy(s =>
                (s.Position.ToVec2() - MobileParty.MainParty.Position.ToVec2()).LengthSquared).FirstOrDefault();
            var match = new DuelMatch
            {
                Session = session, Request = request.Copy(), CampaignInstance = Campaign.Current, Day = day, Mounted = mounted,
                OtherEquipment = otherEquipment, YourEquipment = yourEquipment,
                OtherClan = session.Player.Clan, YourClan = session.Npc.Clan,
                OtherReward = DuelRewardService.Forecast(other, yours, day), YourReward = DuelRewardService.Forecast(yours, other, day),
                Escrow = new MeetingEscrow(session, request.Terms), Encounter = new DuelEncounter(session.Encounter, session.Epoch), OriginalMission = Mission.Current,
                Conditions = MeetingService.Describe(session, request.Terms), Date = PromptManager.GetCurrentTimeString(),
                Place = settlement?.IsTown == true ? settlement.Name + "竞技场" : (nearest == null ? "野外" : nearest.Name + "附近"),
                RecordRoot = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "duels"),
                OtherEntityId = EntityManager.GetOrCreateEntity(session.Player).Id, YourEntityId = EntityManager.GetOrCreateEntity(session.Npc).Id
            };
            foreach (var branch in request.Terms.Outcomes)
            {
                var effects = new List<MeetingEffect>();
                foreach (var side in new[] { MeetingSide.Other, MeetingSide.Self })
                {
                    var context = new MeetingClauseContext(session, side);
                    var offer = side == MeetingSide.Other ? branch.OtherOffer : branch.YourOffer;
                    foreach (var clause in offer.Actions)
                    {
                        var effect = MeetingClauses.Find(clause.TypeId)!.Prepare(context, clause.Copy());
                        var originalValidate = effect.Validate;
                        effect.Validate = () => !match.IsCurrent ? "原战役已经结束。"
                            : !context.Actor.IsAlive || (side == MeetingSide.Other ? match.Resolution.OtherKilled : match.Resolution.YourKilled)
                                ? context.Actor.Name + "战斗判定致命或已经死亡，无法由本人履行此行动。" : originalValidate();
                        effects.Add(effect);
                    }
                }
                match.Effects.Add(branch.Id, effects);
            }
            Active = match;
            try { DuelRecords.Save(match, "queued"); }
            catch { Active = null; throw; }
            Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
        }

        public static void Tick()
        {
            var match = Active;
            if (match == null) return;
            if (!match.IsCurrent) { Active = null; return; }
            try
            {
                if (match.Finished)
                {
                    if (!match.SceneOpened && Mission.Current != null && ReferenceEquals(Mission.Current, match.OriginalMission)) { Active = null; return; }
                    if (Mission.Current != null || !(Game.Current?.GameStateManager?.ActiveState is MapState)) return;
                    if (!match.Encounter.Finish()) return;
                    FinishPassage(match);
                    Active = null;
                    if (Hero.MainHero == match.Session.Player && match.Session.Player.IsAlive)
                    {
                        var original = match.Summary.IndexOf(" 原约定：", StringComparison.Ordinal);
                        var concise = original < 0 ? match.Summary : match.Summary.Substring(0, original);
                        InformationManager.ShowInquiry(new InquiryData("决斗结果", concise, true, false, "知晓", "", () => { }, () => { }), true);
                    }
                    return;
                }
                if (match.SceneOpened)
                {
                    if (Mission.Current == null && Game.Current?.GameStateManager?.ActiveState is MapState)
                        AbortPreparation(match, "战场已关闭，未收到有效完成事件。");
                    return;
                }
                if (!match.ConversationEnded)
                {
                    match.ConversationEnded = true;
                    match.Encounter.EndConversation(() => Campaign.Current.ConversationManager.EndConversation());
                }
                if (Mission.Current != null)
                {
                    if (ReferenceEquals(Mission.Current, match.OriginalMission) && !match.OriginalMissionEnded)
                    { match.OriginalMissionEnded = true; Mission.Current.EndMission(); }
                    if ((DateTime.UtcNow - match.QueuedAt).TotalSeconds > 60) AbortPreparation(match, "原场景未能结束。");
                    return;
                }
                if (!(Game.Current?.GameStateManager?.ActiveState is MapState)) return;
                if (PlayerEncounter.Current != null && !ReferenceEquals(PlayerEncounter.Current, match.Session.Encounter))
                { AbortPreparation(match, "已经进入另一场遭遇。"); return; }
                if (Hero.MainHero != match.Session.Player || !match.Session.Npc.IsAlive)
                { AbortPreparation(match, "参战者已经变化。"); return; }
                if (!match.Encounter.Finish())
                {
                    if ((DateTime.UtcNow - match.QueuedAt).TotalSeconds > 60) AbortPreparation(match, "原地图会面未能结束。");
                    return;
                }
                match.SceneOpened = true;
                match.Mission = DuelMission.Open(match);
            }
            catch (Exception ex)
            {
                UiTrace.Log("Duel transition failed: " + ex);
                if (match.Finished)
                {
                    var note = "返回流程：" + ex.Message;
                    match.RewardNotes.Add(note); SaveAudit(match, "return_failed");
                    DuelRecords.PublishNote(match, note); Active = null;
                }
                else AbortPreparation(match, ex.Message);
            }
        }

        private static void AbortPreparation(DuelMatch match, string reason)
        {
            if (match.Finished) return;
            match.FailureReason = reason;
            match.Resolution.Seal(true);
            MissionEnded(match);
        }

        internal static void CombatReady(DuelMatch match)
        {
            if (!ReferenceEquals(Active, match) || !match.IsCurrent || Hero.MainHero != match.Session.Player)
                throw new OperationCanceledException("这场决斗已结束。");
            foreach (var hero in new[] { match.Session.Player, match.Session.Npc })
                if (!hero.IsAlive || hero.IsWounded) throw new InvalidOperationException("参战者身体状态已变化。");
            if (match.Session.Player.Clan != match.OtherClan || match.Session.Npc.Clan != match.YourClan)
                throw new InvalidOperationException("参战者家族已变化。");
            foreach (var branch in match.Request.Terms.Outcomes)
            {
                var invalid = MeetingAssets.Validate(match.Session, branch.AsTerms(match.Request.Terms.TypeId))
                    ?? MeetingBatch.Validate(match.Effects[branch.Id]);
                if (invalid != null) throw new InvalidOperationException(invalid);
            }
            match.Escrow.Take();
            SaveAudit(match, "fighting");
            match.CombatStarted = true;
        }

        internal static void MissionEnded(DuelMatch match)
        {
            if (!match.IsCurrent || !match.Resolution.TryBeginSettlement()) return;
            SaveAudit(match, "settling");
            try
            {
                if (match.CombatStarted) { match.OtherOrigin?.ApplyHealth(); match.YourOrigin?.ApplyHealth(); }
                var outcome = match.Resolution.Outcome;
                var winner = outcome == DuelOutcome.OtherVictory ? match.OtherReward.Winner : match.YourReward.Winner;
                var loser = outcome == DuelOutcome.OtherVictory ? match.OtherReward.Loser : match.YourReward.Loser;
                var recorded = DuelStore.Current?.Complete(match.Id, match.Day, match.Resolution.HasWinner ? winner.ClanId : "",
                    match.Resolution.HasWinner ? loser.ClanId : "", match.Resolution.RewardEligible) == true;
                if (match.Resolution.HasWinner && recorded)
                {
                    var forecast = outcome == DuelOutcome.OtherVictory ? match.OtherReward : match.YourReward;
                    match.AppliedReward = DuelRewardMath.Calculate(forecast.Winner, forecast.Loser, forecast.PriorWins, match.Resolution.RewardEligible);
                    DuelRewardService.Grant(match, match.AppliedReward);
                    var branch = match.Request.Terms.Outcomes.First(o => o.Id == (outcome == DuelOutcome.OtherVictory ? "otherVictory" : "yourVictory"));
                    match.Fulfillment = DuelSettlement.Apply(() => match.Escrow.Deliver(branch), match.Effects[branch.Id]);
                }
                else
                {
                    match.Escrow.Refund();
                    if (!recorded) match.RewardNotes.Add("结果编号已经结算或账本不可用，未重复交付奖励或条件。");
                }
            }
            catch (Exception ex)
            {
                UiTrace.Log("Duel settlement failed: " + ex);
                match.RewardNotes.Add("结算异常：" + ex.Message);
                if (match.Escrow.Held && !match.Escrow.Finished)
                {
                    try { match.Escrow.Refund(); }
                    catch (Exception refund) { match.RewardNotes.Add("退还财物未完成：" + refund.Message); }
                }
            }
            finally
            {
                if (match.CombatStarted)
                {
                    // NPC 先处理，主角最后处理，确保进入原版继承界面前财物已经结算。
                    try { match.YourOrigin?.ApplyNativeDeath(match.Session.Player); }
                    catch (Exception ex) { match.RewardNotes.Add("原版伤亡处理异常：" + ex.Message); }
                    // 主角死亡可能直接进入继承/游戏结束，先持久记录已知结果，不能因此丢掉整场决斗。
                    match.Summary = Summary(match);
                    SaveAudit(match, "settled");
                    DuelRecords.Publish(match);
                    var before = DuelRecords.Condition(match.Session.Player, match.OtherOrigin);
                    try { match.OtherOrigin?.ApplyNativeDeath(match.Session.Npc); }
                    catch (Exception ex) { match.RewardNotes.Add("原版伤亡处理异常：" + ex.Message); }
                    var after = DuelRecords.Condition(match.Session.Player, match.OtherOrigin);
                    if (before != after) DuelRecords.PublishNote(match, match.Session.Player.Name + "伤亡处理确认：" + after);
                }
                match.Summary = Summary(match);
                match.Finished = true;
                match.Session.Negotiation.CompleteDeferred(match.Id, match.Summary,
                    partial: match.Fulfillment?.Complete == false || match.RewardNotes.Count > 0,
                    failed: match.Resolution.Outcome == DuelOutcome.Aborted);
                SaveAudit(match, "completed");
                DuelRecords.Publish(match);
            }
        }

        private static string Summary(DuelMatch match)
        {
            var result = match.Resolution.Outcome == DuelOutcome.OtherVictory ? match.Session.Player.Name + "获胜"
                : match.Resolution.Outcome == DuelOutcome.YourVictory ? match.Session.Npc.Name + "获胜"
                : match.Resolution.Outcome == DuelOutcome.Draw ? "双方倒下或退场，未分胜负"
                : match.CombatStarted ? "决斗中断，未确定胜者" : "决斗未能开场";
            var forfeits = new List<string>();
            if (match.Resolution.OtherForfeit) forfeits.Add(match.Session.Player.Name + "认输或退场");
            if (match.Resolution.YourForfeit) forfeits.Add(match.Session.Npc.Name + "认输或退场");
            return match.Date + "，" + match.Place + "，" + match.Session.Player.Name + "与" + match.Session.Npc.Name + "决斗：" + result
                + (forfeits.Count > 0 ? "（" + string.Join("、", forfeits) + "）" : "") + "。"
                + match.Session.Player.Name + "：" + DuelRecords.Condition(match.Session.Player, match.OtherOrigin) + "；"
                + match.Session.Npc.Name + "：" + DuelRecords.Condition(match.Session.Npc, match.YourOrigin) + "。"
                + (match.AppliedReward == null ? "未发成长奖励。" : "胜者家族实际获得声望 " + match.ActualRenown.ToString("0.##") + "、影响力 " + match.ActualInfluence.ToString("0.##") + "。")
                + (match.WinnerClanTierAfter > match.WinnerClanTierBefore && match.WinnerClanTierBefore >= 0
                    ? "家族等级由 " + match.WinnerClanTierBefore + " 级升至 " + match.WinnerClanTierAfter + " 级。" : "")
                + (match.Fulfillment?.ToString() ?? match.Escrow.Result)
                + (string.IsNullOrWhiteSpace(match.FailureReason) ? "" : " 原因：" + match.FailureReason)
                + (match.RewardNotes.Count == 0 ? "" : " 处理记录：" + string.Join("；", match.RewardNotes))
                + " 原约定：" + match.Conditions;
        }

        private static void SaveAudit(DuelMatch match, string phase)
        {
            try { DuelRecords.Save(match, phase); }
            catch (Exception ex)
            {
                UiTrace.Log("Duel audit write failed: " + ex);
                var note = "详细结果记录写入失败：" + ex.Message;
                if (!match.RewardNotes.Contains(note)) match.RewardNotes.Add(note);
            }
        }

        internal static bool IsFatal(MeetingSession session, Hero hero)
        {
            var match = Active;
            return match?.Session == session && (hero == session.Player ? match.Resolution.OtherKilled : match.Resolution.YourKilled);
        }

        internal static void ArrangePassage(MeetingSession session)
        {
            var match = Active;
            if (match?.Session != session || !match.IsCurrent) throw new InvalidOperationException("当前决斗已经结束。");
            match.PassageRequested = true;
            if (ReferenceEquals(PlayerEncounter.Current, session.Encounter)) PlayerEncounter.LeaveEncounter = true;
        }

        private static void FinishPassage(DuelMatch match)
        {
            if (!match.PassageRequested) return;
            string note;
            try
            {
                if (PlayerEncounter.Current != null)
                {
                    if (!ReferenceEquals(PlayerEncounter.Current, match.Session.Encounter))
                        throw new InvalidOperationException("遭遇已经更换，未结束新的遭遇。");
                    PlayerEncounter.LeaveEncounter = true;
                    PlayerEncounter.Finish(false);
                    if (ReferenceEquals(PlayerEncounter.Current, match.Session.Encounter)) throw new InvalidOperationException("原遭遇仍未结束。");
                }
                note = "放行已完成，原遭遇已结束。";
            }
            catch (Exception ex)
            {
                note = "放行未完成：" + ex.Message;
                match.RewardNotes.Add(note);
            }
            match.Summary = Summary(match);
            var original = match.Summary.IndexOf(" 原约定：", StringComparison.Ordinal);
            match.Summary = original < 0 ? match.Summary + " " + note : match.Summary.Insert(original, " " + note);
            SaveAudit(match, "returned");
            DuelRecords.PublishNote(match, note);
        }
    }
}
