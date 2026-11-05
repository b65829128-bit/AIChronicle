using System;

namespace AIChronicle
{
    /// <summary>v1 固定定义：冻结事实输入，无随机、控制者或语言模型参与。改系数须升级版本。</summary>
    public static class DuelRewardMath
    {
        public const string Version = "duel-rewards-v1";
        private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(value, max));
        private static double Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 1e12)
                throw new ArgumentOutOfRangeException(nameof(value), "决斗奖励输入须为有效非负数。");
            return value;
        }
        public static double Power(DuelFighterSnapshot hero)
        {
            if (hero == null || string.IsNullOrWhiteSpace(hero.ClanId) || !Enum.IsDefined(typeof(DuelRole), hero.Role))
                throw new ArgumentException("决斗者身份不完整。");
            Number(hero.Renown); Number(hero.RenownMultiplier); Number(hero.InfluenceMultiplier);
            return (50 + Number(hero.WeaponSkill) + 0.5 * Number(hero.MobilitySkill)) * (1 + Number(hero.ArmorMean) / 100);
        }
        private static int Rounded(double value)
        {
            var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
            if (double.IsInfinity(rounded) || double.IsNaN(rounded) || rounded > int.MaxValue || rounded < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "奖励超出游戏范围。");
            return (int)rounded;
        }

        public static DuelReward Calculate(DuelFighterSnapshot winner, DuelFighterSnapshot loser, int priorWins, bool normalVictory = true)
        {
            if (priorWins < 0) throw new ArgumentOutOfRangeException(nameof(priorWins));
            var winnerPower = Power(winner); var loserPower = Power(loser);
            var level = Math.Log(1 + loser.Renown / 50, 2);
            var fame = Clamp(Math.Pow((loser.Renown + 50) / (winner.Renown + 50), 0.12), 0.6, 1.8);
            var difficulty = Clamp(Math.Pow(loserPower / winnerPower, 0.35), 0.75, 1.4);
            var roleR = loser.Role == DuelRole.Ruler ? 1.7 : loser.Role == DuelRole.ClanLeader ? 1.35 : 1;
            var roleI = loser.Role == DuelRole.Ruler ? 2 : loser.Role == DuelRole.ClanLeader ? 1.5 : 1;
            var firstR = (12 + 5 * level) * fame * difficulty * roleR;
            var firstI = (10 + 4 * level) * Math.Sqrt(fame * difficulty) * roleI;
            var repeat = !normalVictory || winner.ClanId == loser.ClanId ? 0 : priorWins == 0 ? 1 : priorWins == 1 ? 0.25 : 0;
            return new DuelReward
            {
                Winner = winner.Copy(), Loser = loser.Copy(), PriorWins = priorWins,
                WinnerPower = winnerPower, LoserPower = loserPower, FameLevel = level, FameFactor = fame,
                DifficultyFactor = difficulty, RoleRenown = roleR, RoleInfluence = roleI,
                FirstRenown = firstR, FirstInfluence = firstI, RepeatFactor = repeat,
                Renown = Rounded(firstR * repeat * winner.RenownMultiplier),
                Influence = winner.CanGainInfluence ? Rounded(firstI * repeat * winner.InfluenceMultiplier) : 0
            };
        }
    }
}
