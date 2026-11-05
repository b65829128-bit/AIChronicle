using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;

namespace AIChronicle
{
    public static class DuelRewardService
    {
        public static Equipment Loadout(Hero hero, bool mounted)
        {
            var equipment = new Equipment(hero.BattleEquipment);
            if (!mounted)
            {
                equipment[EquipmentIndex.Horse] = default;
                equipment[EquipmentIndex.HorseHarness] = default;
            }
            return equipment;
        }

        public static DuelFighterSnapshot Snapshot(Hero hero, Equipment equipment, bool mounted)
        {
            var clan = hero.Clan ?? throw new InvalidOperationException("参战者须属于家族。");
            var weaponSkill = 0;
            for (var i = 0; i < (int)EquipmentIndex.NumAllWeaponSlots; i++)
            {
                var item = equipment[(EquipmentIndex)i].Item;
                if (item == null || !item.HasWeaponComponent || item.Type == ItemObject.ItemTypeEnum.Arrows || item.Type == ItemObject.ItemTypeEnum.Bolts) continue;
                foreach (var weapon in item.Weapons)
                    if (weapon.RelevantSkill != null) weaponSkill = Math.Max(weaponSkill, hero.GetSkillValue(weapon.RelevantSkill));
            }
            return new DuelFighterSnapshot
            {
                HeroId = hero.StringId, Name = hero.Name.ToString(), ClanId = clan.StringId, ClanName = clan.Name.ToString(), Renown = clan.Renown,
                Role = clan.Kingdom?.Leader == hero ? DuelRole.Ruler : clan.Leader == hero ? DuelRole.ClanLeader : DuelRole.Member,
                WeaponSkill = weaponSkill, MobilitySkill = hero.GetSkillValue(mounted ? DefaultSkills.Riding : DefaultSkills.Athletics),
                ArmorMean = (equipment.GetHeadArmorSum() + equipment.GetHumanBodyArmorSum() + equipment.GetArmArmorSum() + equipment.GetLegArmorSum()) / 4.0,
                CanGainInfluence = clan.Kingdom != null,
                RenownMultiplier = MySettings.Instance?.DoubleRenownEnabled == true ? 2 : 1
            };
        }

        public static DuelReward Forecast(DuelFighterSnapshot winner, DuelFighterSnapshot loser, double day)
            => DuelRewardMath.Calculate(winner, loser, DuelStore.Current?.CountWins(winner.ClanId, loser.ClanId, day)
                ?? throw new InvalidOperationException("决斗奖励账本尚未就绪。"));

        public static string Preview(MeetingSession session, MeetingTerms terms)
        {
            var mounted = terms.Parameters["mounts"]?.ToString() == "mounted";
            var other = Snapshot(session.Player, Loadout(session.Player, mounted), mounted);
            var yours = Snapshot(session.Npc, Loadout(session.Npc, mounted), mounted);
            var day = CampaignTime.Now.ToDays;
            return "获胜奖励预估：" + Describe(Forecast(other, yours, day)) + "；" + Describe(Forecast(yours, other, day))
                + "。认输或异常中断不发战斗奖励。";
        }

        public static string Describe(DuelReward reward)
        {
            var note = reward.Winner.ClanId == reward.Loser.ClanId ? "（同家族切磋）"
                : reward.PriorWins >= 2 ? "（七日内已重复获胜，不再追加奖励）"
                : reward.PriorWins == 1 ? "（七日内第二次获胜，按四分之一）" : "";
            return reward.Winner.Name + "胜：声望 +" + reward.Renown + "、影响力 +" + reward.Influence
                + (!reward.Winner.CanGainInfluence ? "（无王国影响力资格）" : "") + note;
        }

        internal static void Grant(DuelMatch match, DuelReward reward)
        {
            if (match.RewardsAttempted) return;
            match.RewardsAttempted = true;
            var hero = match.Resolution.Outcome == DuelOutcome.OtherVictory ? match.Session.Player : match.Session.Npc;
            var clan = match.Resolution.Outcome == DuelOutcome.OtherVictory ? match.OtherClan : match.YourClan;
            if (!match.IsCurrent || !hero.IsAlive || hero.Clan != clan || clan.IsEliminated)
            {
                match.RewardNotes.Add("胜者身份已变化，未自动发放成长奖励。");
                return;
            }
            var renownBefore = clan.Renown;
            var influenceBefore = clan.Influence;
            match.WinnerClanTierBefore = clan.Tier;
            if (reward.Renown > 0)
            {
                try { GainRenownAction.Apply(hero, reward.Renown); }
                catch (Exception ex) { match.RewardNotes.Add("声望回调异常，不重复发放：" + ex.Message); }
            }
            if (reward.Influence > 0)
            {
                try { ChangeClanInfluenceAction.Apply(clan, reward.Influence); }
                catch (Exception ex) { match.RewardNotes.Add("影响力回调异常，不重复发放：" + ex.Message); }
            }
            match.ActualRenown = Math.Max(0, clan.Renown - renownBefore);
            match.ActualInfluence = Math.Max(0, clan.Influence - influenceBefore);
            match.WinnerClanTierAfter = clan.Tier;
            if (Math.Abs(match.ActualRenown - reward.Renown) > 0.1 || Math.Abs(match.ActualInfluence - reward.Influence) > 0.1)
                match.RewardNotes.Add("成长奖励实际到账与预估不同，按实际到账记录，不自动重试。");
        }
    }
}
