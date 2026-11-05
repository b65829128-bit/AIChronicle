using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace AIChronicle
{
    /// <summary>战场决定伤亡；先保存结果，等保管财物交割后调用原版战死流程，避免财物随继承丢失。</summary>
    internal sealed class DuelAgentOrigin : IAgentOriginBase
    {
        private readonly IAgentOriginBase _native;
        private readonly long _epoch;
        private readonly Hero _hero;
        private readonly MeetingSide _side;
        private bool _healthApplied;
        private bool _deathApplied;
        private readonly uint _color, _color2;
        private Banner _banner;
        public bool Removed { get; private set; }
        public bool Killed { get; private set; }
        public bool DeathBlocked { get; private set; }
        public float InitialHealth { get; }
        public float LastHealth { get; private set; }
        public PartyBase MedicalParty { get; }

        public DuelAgentOrigin(Hero hero, MeetingSide side, long epoch)
        {
            _hero = hero; _side = side; _epoch = epoch;
            InitialHealth = LastHealth = hero.HitPoints;
            // 无部队的在场领主仍须有原版战斗医疗上下文，否则 PartyHealingModel(null) 会变成必死。
            // 优先所属/拘押部队，其次当前城防，最后现场主队；不创建或迁移任何战役部队。
            MedicalParty = hero.PartyBelongedTo?.Party ?? hero.PartyBelongedToAsPrisoner
                ?? hero.CurrentSettlement?.Town?.GarrisonParty?.Party ?? MobileParty.MainParty.Party;
            _native = new PartyAgentOrigin(MedicalParty, hero.CharacterObject, 0);
            _color = hero.MapFaction.Color; _color2 = hero.MapFaction.Color2;
            _banner = hero.Clan?.Banner ?? _native.Banner;
        }
        public bool IsUnderPlayersCommand => _side == MeetingSide.Other;
        public bool IsInSameArmyAsPlayer => _side == MeetingSide.Other && _native.IsInSameArmyAsPlayer;
        public uint FactionColor => _color;
        public uint FactionColor2 => _color2;
        public IBattleCombatant BattleCombatant => MedicalParty;
        public int UniqueSeed => _native.UniqueSeed;
        public int Seed => _native.Seed;
        public Banner Banner => _banner;
        public BasicCharacterObject Troop => _hero.CharacterObject;
        public bool HasThrownWeapon => _native.HasThrownWeapon;
        public bool HasHeavyArmor => _native.HasHeavyArmor;
        public bool HasShield => _native.HasShield;
        public bool HasSpear => _native.HasSpear;
        public void SetWounded() { Removed = true; LastHealth = 1; }
        public void SetKilled() { Removed = true; Killed = true; LastHealth = 1; }
        public void SetRouted(bool isOrderRetreat) { Removed = true; }
        public void OnAgentRemoved(float agentHealth) { LastHealth = Math.Max(1, agentHealth); }
        public void SetBanner(Banner banner) { _banner = banner; _native.SetBanner(banner); }
        public TroopTraitsMask GetTraitsMask() => _native.GetTraitsMask();
        public void OnScoreHit(BasicCharacterObject victim, BasicCharacterObject captain, int damage, bool isFatal, bool isTeamKill, WeaponComponentData weapon)
        {
            if (_epoch != MainThreadExecutor.CampaignEpoch || Campaign.Current == null || !_hero.IsAlive || weapon == null || !(victim is CharacterObject target)) return;
            var model = Campaign.Current.Models.CombatXpModel;
            var skill = model.GetSkillForWeapon(weapon, false);
            if (skill != null) _hero.AddSkillXp(skill, model.GetXpFromHit(_hero.CharacterObject, captain as CharacterObject, target,
                _hero.PartyBelongedTo?.Party, Math.Max(0, damage), isFatal, CombatXpModel.MissionTypeEnum.Battle).ResultNumber);
        }

        public void ApplyHealth()
        {
            if (_healthApplied || _epoch != MainThreadExecutor.CampaignEpoch || !_hero.IsAlive) return;
            _healthApplied = true;
            _hero.HitPoints = Math.Max(1, (int)Math.Round(LastHealth));
        }
        public void ApplyNativeDeath(Hero opponent)
        {
            if (_deathApplied || !Killed || _epoch != MainThreadExecutor.CampaignEpoch || !_hero.IsAlive) return;
            _deathApplied = true;
            DeathBlocked = !_hero.CanDie(KillCharacterAction.KillCharacterActionDetail.DiedInBattle);
            // 不传 isForced、不自掷死亡概率；原版剧情保护、死亡标记和继任流程照常生效。
            if (!DeathBlocked) KillCharacterAction.ApplyByBattle(_hero, opponent);
        }
    }
}
