using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace AIChronicle
{
    internal static class DuelRecords
    {
        public static void Save(DuelMatch match, string phase)
        {
            if (!match.IsCurrent) return;
            var audit = new DuelAudit
            {
                Id = match.Id, Phase = phase, Day = match.Day, Date = match.Date, Place = match.Place,
                OtherEntityId = match.OtherEntityId, YourEntityId = match.YourEntityId,
                Terms = match.Request.Terms.Copy(), Conditions = match.Conditions,
                OtherVictoryReward = match.OtherReward, YourVictoryReward = match.YourReward,
                AppliedReward = match.AppliedReward, ActualRenown = match.ActualRenown, ActualInfluence = match.ActualInfluence,
                WinnerClanTierBefore = match.WinnerClanTierBefore, WinnerClanTierAfter = match.WinnerClanTierAfter,
                RewardNotes = new List<string>(match.RewardNotes), Outcome = match.Resolution.Outcome, RewardEligible = match.Resolution.RewardEligible,
                OtherKilledInMission = match.Resolution.OtherKilled, YourKilledInMission = match.Resolution.YourKilled,
                OtherForfeit = match.Resolution.OtherForfeit, YourForfeit = match.Resolution.YourForfeit,
                OtherCondition = Condition(match.Session.Player, match.OtherOrigin), YourCondition = Condition(match.Session.Npc, match.YourOrigin),
                Escrow = match.Escrow.Result, Fulfillment = match.Fulfillment, Summary = match.Summary
            };
            var path = Path.Combine(match.RecordRoot, match.Id + ".json");
            SafeFileIO.WriteAllTextAtomic(path, JsonConvert.SerializeObject(audit, Formatting.Indented));
        }

        public static string Condition(TaleWorlds.CampaignSystem.Hero hero, DuelAgentOrigin? origin)
            => !hero.IsAlive ? "已战死" : hero.DeathMark != TaleWorlds.CampaignSystem.Actions.KillCharacterAction.KillCharacterActionDetail.None
                ? "原版已标记死亡，等待战役处理" : origin?.DeathBlocked == true ? "原版规则保留性命，受伤，生命 " + hero.HitPoints
                : origin?.Killed == true ? "战斗判定致命，原版死亡流程待处理"
                : origin != null && (origin.Removed || origin.LastHealth < origin.InitialHealth) ? "受伤，生命 " + hero.HitPoints : "存活，生命 " + hero.HitPoints;

        public static void Publish(DuelMatch match)
        {
            if (!match.IsCurrent || match.RecordsPublished) return;
            match.RecordsPublished = true;
            try { HistoryRecorder.RecordDuel("决斗 " + match.Id + "：" + match.Summary); }
            catch (Exception ex) { UiTrace.Log("Duel history write failed: " + ex); }
            try
            {
                AgentManager.AppendSystemDiary(match.YourEntityId, "决斗", match.Summary);
                AgentManager.AppendSystemDiary(match.OtherEntityId, "决斗", match.Summary);
                PromptManager.AppendChatLogFor(match.YourEntityId, match.OtherEntityId, "system", "[决斗结果] " + match.Summary);
            }
            catch (Exception ex) { UiTrace.Log("Duel diary/thread write failed: " + ex); }
        }
        public static void PublishNote(DuelMatch match, string text)
        {
            if (!match.IsCurrent) return;
            try
            {
                HistoryRecorder.RecordDuelAftermath("决斗 " + match.Id + " 履约/伤亡补记：" + text);
                AgentManager.AppendSystemDiary(match.YourEntityId, "决斗", text);
                AgentManager.AppendSystemDiary(match.OtherEntityId, "决斗", text);
                PromptManager.AppendChatLogFor(match.YourEntityId, match.OtherEntityId, "system", "[决斗补记] " + text);
            }
            catch (Exception ex) { UiTrace.Log("Duel aftermath write failed: " + ex); }
        }
    }
}
