using System;
using System.Collections.Generic;
using System.Linq;

namespace AIChronicle
{
    public enum DuelRole { Member, ClanLeader, Ruler }
    public enum DuelOutcome { Unresolved, OtherVictory, YourVictory, Draw, Aborted }

    public sealed class DuelVictoryStamp
    {
        public string Id { get; set; } = "";
        public double Day { get; set; }
        public string WinnerClanId { get; set; } = "";
        public string LoserClanId { get; set; } = "";
        public bool NormalVictory { get; set; }
    }

    public sealed class DuelRewardLedger
    {
        public int Version { get; set; } = 1;
        public List<DuelVictoryStamp> Entries { get; set; } = new();
        public int Count(string winnerClan, string loserClan, double now) => Entries.Count(e => e.NormalVictory
            && e.WinnerClanId == winnerClan && e.LoserClanId == loserClan && now - e.Day >= 0 && now - e.Day < 7);
        public bool Complete(DuelVictoryStamp stamp)
        {
            if (Entries.Any(e => e.Id == stamp.Id)) return false;
            if (string.IsNullOrWhiteSpace(stamp.Id) || double.IsNaN(stamp.Day) || double.IsInfinity(stamp.Day))
                throw new ArgumentException("决斗记录缺少有效编号或时间。");
            Entries.RemoveAll(e => stamp.Day - e.Day >= 7 || e.Day > stamp.Day);
            Entries.Add(stamp);
            return true;
        }
    }

    public sealed class DuelAudit
    {
        public int Version { get; set; } = 1;
        public string Id { get; set; } = "";
        public string Phase { get; set; } = "";
        public double Day { get; set; }
        public string Date { get; set; } = "";
        public string Place { get; set; } = "";
        public string OtherEntityId { get; set; } = "";
        public string YourEntityId { get; set; } = "";
        public MeetingTerms Terms { get; set; } = new();
        public string Conditions { get; set; } = "";
        public DuelReward? OtherVictoryReward { get; set; }
        public DuelReward? YourVictoryReward { get; set; }
        public DuelReward? AppliedReward { get; set; }
        public double ActualRenown { get; set; }
        public double ActualInfluence { get; set; }
        public int WinnerClanTierBefore { get; set; } = -1;
        public int WinnerClanTierAfter { get; set; } = -1;
        public List<string> RewardNotes { get; set; } = new();
        public DuelOutcome Outcome { get; set; }
        public bool RewardEligible { get; set; }
        public bool OtherKilledInMission { get; set; }
        public bool YourKilledInMission { get; set; }
        public bool OtherForfeit { get; set; }
        public bool YourForfeit { get; set; }
        public string OtherCondition { get; set; } = "";
        public string YourCondition { get; set; } = "";
        public string Escrow { get; set; } = "";
        public DuelFulfillment? Fulfillment { get; set; }
        public string Summary { get; set; } = "";
    }

    public sealed class DuelFighterSnapshot
    {
        public string HeroId { get; set; } = "";
        public string Name { get; set; } = "";
        public string ClanId { get; set; } = "";
        public string ClanName { get; set; } = "";
        public double Renown { get; set; }
        public DuelRole Role { get; set; }
        public double WeaponSkill { get; set; }
        public double MobilitySkill { get; set; }
        public double ArmorMean { get; set; }
        public bool CanGainInfluence { get; set; }
        public double RenownMultiplier { get; set; } = 1;
        public double InfluenceMultiplier { get; set; } = 1;
        public DuelFighterSnapshot Copy() => (DuelFighterSnapshot)MemberwiseClone();
    }

    public sealed class DuelReward
    {
        public string AlgorithmVersion { get; set; } = DuelRewardMath.Version;
        public int Renown { get; set; }
        public int Influence { get; set; }
        public double FirstRenown { get; set; }
        public double FirstInfluence { get; set; }
        public double FameLevel { get; set; }
        public double FameFactor { get; set; }
        public double DifficultyFactor { get; set; }
        public double WinnerPower { get; set; }
        public double LoserPower { get; set; }
        public double RoleRenown { get; set; }
        public double RoleInfluence { get; set; }
        public double RepeatFactor { get; set; }
        public int PriorWins { get; set; }
        public DuelFighterSnapshot Winner { get; set; } = new();
        public DuelFighterSnapshot Loser { get; set; } = new();
    }

    /// <summary>只接受已识别的两名决斗者事件。每帧收齐移除事件后判定，避免互相击倒误判胜者。</summary>
    public sealed class DuelResolution
    {
        public bool OtherLost { get; private set; }
        public bool YourLost { get; private set; }
        public bool OtherKilled { get; private set; }
        public bool YourKilled { get; private set; }
        public bool Forfeit { get; private set; }
        public bool OtherForfeit { get; private set; }
        public bool YourForfeit { get; private set; }
        public DuelOutcome Outcome { get; private set; }
        public bool Sealed { get; private set; }
        public bool Settling { get; private set; }
        public bool HasWinner => Outcome == DuelOutcome.OtherVictory || Outcome == DuelOutcome.YourVictory;
        public bool RewardEligible => HasWinner && !Forfeit;

        public void ReportLoss(MeetingSide side, bool killed, bool forfeit = false)
        {
            if (Sealed) return;
            if (side == MeetingSide.Other) { OtherLost = true; OtherKilled |= killed; OtherForfeit |= forfeit; }
            else { YourLost = true; YourKilled |= killed; YourForfeit |= forfeit; }
            Forfeit |= forfeit;
        }

        public DuelOutcome Observe() => OtherLost && YourLost ? DuelOutcome.Draw
            : OtherLost ? DuelOutcome.YourVictory : YourLost ? DuelOutcome.OtherVictory : DuelOutcome.Unresolved;

        public void Seal(bool aborted = false)
        {
            if (Sealed) return;
            Outcome = aborted ? DuelOutcome.Aborted : Observe();
            if (Outcome == DuelOutcome.Unresolved) Outcome = DuelOutcome.Aborted;
            Sealed = true;
        }

        public bool TryBeginSettlement()
        {
            if (!Sealed || Settling) return false;
            Settling = true;
            return true;
        }
    }

    public sealed class DuelFulfillment
    {
        public bool AssetsDelivered { get; set; }
        public List<string> Done { get; } = new();
        public List<string> Missing { get; } = new();
        public List<string> Warnings { get; } = new();
        public bool Complete => AssetsDelivered && Missing.Count == 0;
        public override string ToString() => (AssetsDelivered ? "保管财物已按胜负交付，未使用部分已退回。" : "财物交付未完成。")
            + (Done.Count == 0 ? "" : " 已履行：" + string.Join("；", Done))
            + (Missing.Count == 0 ? "" : " 未履行：" + string.Join("；", Missing))
            + (Warnings.Count == 0 ? "" : " 核验提示：" + string.Join("；", Warnings));
    }

    /// <summary>战斗已经发生，财物按约定胜负交付；失效行动如实记账，不能回滚战斗或替继承人作承诺。</summary>
    public static class DuelSettlement
    {
        public static DuelFulfillment Apply(Action transfer, IReadOnlyList<MeetingEffect> effects)
        {
            var result = new DuelFulfillment();
            try { transfer(); result.AssetsDelivered = true; }
            catch (Exception ex) { result.Missing.Add("财物：" + ex.Message); result.Missing.AddRange(effects.Select(e => e.Name)); return result; }
            var ordered = effects.OrderBy(e => e.Order).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var effect = ordered[i];
                try
                {
                    var invalid = effect.Validate();
                    if (invalid != null) { result.Missing.Add(effect.Name + "（" + invalid + "）"); continue; }
                    try { effect.Apply(); }
                    catch (Exception ex)
                    {
                        if (!effect.IsApplied()) throw;
                        result.Warnings.Add(effect.Name + "已生效，但回调异常：" + ex.Message);
                    }
                    if (!effect.IsApplied()) throw new InvalidOperationException(effect.HasChanged() ? "仅部分生效" : "未核实生效");
                    result.Done.Add(effect.Name);
                }
                catch (Exception ex)
                {
                    result.Missing.Add(effect.Name + "（" + ex.Message + "；不自动重试）");
                    result.Missing.AddRange(ordered.Skip(i + 1).Select(e => e.Name));
                    break;
                }
            }
            return result;
        }
    }
}
