// 家族迁移回归直接链接生产 handler/service。以下仅提供对象容器、索引与可注入异常，
// 不模拟原版外交、战斗、出生、继承、保存或 AI 决策。
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;

namespace TaleWorlds.Localization
{
    public sealed class TextObject
    {
        private readonly string _text;
        public TextObject(string text) { _text = text; }
        public override string ToString() => _text;
    }
}
namespace TaleWorlds.Core
{
    public sealed class Banner
    {
        public static Banner CreateRandomClanBanner() => new();
        public uint GetPrimaryColor() => 1;
        public uint GetFirstIconColor() => 2;
    }
}
namespace TaleWorlds.CampaignSystem
{
    public sealed class CultureObject { }
    public sealed partial class Hero
    {
        public static Hero? MainHero { get; set; }
        public string StringId { get; set; } = Guid.NewGuid().ToString("N");
        public float Age { get; set; } = 30;
        public bool IsChild => Age < 18;
        public bool IsTemplate { get; set; }
        public bool IsDisabled { get; set; }
        public bool IsNotSpawned { get; set; }
        public bool IsFugitive { get; set; }
        public bool IsTraveling { get; set; }
        public bool IsPregnant { get; set; }
        public CultureObject Culture { get; set; } = new();
        public List<Hero> Children { get; } = new();
        public Settlements.Settlement? HomeSettlement { get; set; } = new();
        public Settlements.Settlement? BornSettlement { get; set; }
        public Settlements.Settlement? CurrentSettlement { get; set; }
        public PartyBase? PartyBelongedToAsPrisoner { get; set; }
        public object? GovernorOf { get; set; }
        public Action? AfterClanChange { get; set; }
        public CharacterObject CharacterObject { get; }
        public Hero() { CharacterObject = new CharacterObject { HeroObject = this }; }
        public void UpdateHomeSettlement() { }
    }
    public sealed class CharacterObject
    {
        public bool IsHero => HeroObject != null;
        public Hero HeroObject { get; set; } = null!;
    }
    public sealed partial class Clan
    {
        public static List<Clan> All { get; } = new();
        public static Clan? PlayerClan => Hero.MainHero?.Clan;
        public Clan() { All.Add(this); }
        public string StringId { get; set; } = Guid.NewGuid().ToString("N");
        public bool IsNoble { get; set; } = true;
        public bool IsEliminated { get; set; }
        public bool IsBanditFaction { get; set; }
        public bool IsRebelClan { get; set; }
        public CultureObject Culture { get; set; } = new();
        public Kingdom? Kingdom { get; set; }
        public List<Hero> Heroes { get; } = new();
        public Settlements.Settlement? HomeSettlement { get; set; } = new();
        public TaleWorlds.Core.Banner Banner { get; set; } = new();
        public uint Color { get; set; }
        public uint Color2 { get; set; }
        public float Renown { get; private set; }
        public int Tier { get; private set; }
        public static Clan CreateClan(string id) => new() { StringId = id + All.Count };
        public void ChangeClanName(TextObject name, TextObject informal) { Name = name.ToString(); }
        public void SetLeader(Hero hero) { Leader = hero; hero.Clan = this; }
        public void SetInitialHomeSettlement(Settlements.Settlement settlement) { HomeSettlement = settlement; }
        public void CalculateMidSettlement() { }
        public void AddRenown(float value, bool notify) { Renown += value; Tier = (int)Renown / 100; }
    }
    public sealed partial class Kingdom
    {
        public static List<Kingdom> All { get; } = new();
        public Kingdom() { All.Add(this); }
        public string StringId { get; set; } = Guid.NewGuid().ToString("N");
        public bool IsEliminated { get; set; }
    }
    public sealed partial class Campaign { public Models Models { get; } = new(); }
    public sealed class Models { public ClanTierModel ClanTierModel { get; } = new(); }
    public sealed class ClanTierModel
    {
        public int CompanionToLordClanStartingTier { get; set; } = 2;
        public int VassalEligibleTier { get; set; } = 2;
        public int GetRequiredRenownForTier(int tier) => tier * 100;
    }
    public sealed class Army { public MobileParty? LeaderParty { get; set; } }
    public sealed class CampaignEventDispatcher
    {
        public static CampaignEventDispatcher Instance { get; } = new();
        public int Created;
        public bool ThrowOnCreated;
        public void OnClanCreated(Clan clan, bool companion)
        { Created++; if (ThrowOnCreated) throw new InvalidOperationException("建族回调异常"); }
    }
}
namespace TaleWorlds.CampaignSystem.Settlements
{
    public sealed partial class Settlement
    {
        public object? SiegeEvent { get; set; }
        public IFaction MapFaction { get; set; } = null!;
    }
}
namespace TaleWorlds.CampaignSystem.Roster
{
    public sealed class TroopRoster
    {
        public List<TroopRosterElement> Entries { get; } = new();
        public List<TroopRosterElement> GetTroopRoster() => Entries.ToList();
    }
    public sealed class TroopRosterElement { public CharacterObject Character { get; set; } = null!; }
}
namespace TaleWorlds.CampaignSystem.Party
{
    public sealed partial class PartyBase
    {
        public Roster.TroopRoster PrisonRoster { get; } = new();
        public void SetVisualAsDirty() { }
    }
    public sealed partial class MobileParty
    {
        public Clan ActualClan { get; set; } = null!;
        public IFaction MapFaction => (IFaction?)ActualClan.Kingdom ?? ActualClan;
        public Army? Army { get; set; }
        public object? SiegeEvent { get; set; }
        public bool IsLordParty { get; set; } = true;
        public bool IsDisbanding { get; set; }
        public bool IsTransitionInProgress { get; set; }
        public PartyBase Party { get; } = new();
        public Roster.TroopRoster MemberRoster { get; } = new();
        public int Holds;
        public void SetMoveModeHold() { Holds++; }
        public void CancelNavigationTransition() { IsTransitionInProgress = false; }
    }
}
namespace TaleWorlds.CampaignSystem.Actions
{
    public static class ChangeGovernorAction { public static void RemoveGovernorOf(Hero hero) { hero.GovernorOf = null; } }
    public static class DisbandArmyAction
    {
        public static Action? AfterDisband;
        public static void ApplyByUnknownReason(Army army)
        { if (army.LeaderParty != null) army.LeaderParty.Army = null; AfterDisband?.Invoke(); }
    }
    public static class MakeHeroFugitiveAction
    {
        public static void Apply(Hero hero)
        {
            if (hero.PartyBelongedTo?.LeaderHero == hero) throw new InvalidOperationException("测试禁止销毁本人部队");
            hero.PartyBelongedTo?.MemberRoster.Entries.RemoveAll(e => e.Character.HeroObject == hero);
            hero.PartyBelongedTo = null; hero.CurrentSettlement = null; hero.IsPrisoner = false; hero.IsFugitive = true;
        }
    }
    public static partial class ChangeKingdomAction
    {
        public static Action<Clan, Kingdom>? AfterJoin;
        public static void ApplyByJoinToKingdom(Clan clan, Kingdom kingdom) { clan.Kingdom = kingdom; AfterJoin?.Invoke(clan, kingdom); }
    }
    public static class LeaveSettlementAction { public static void ApplyForParty(MobileParty party) { party.CurrentSettlement = null; } }
}
namespace Helpers
{
    public static class FactionHelper
    {
        public static Tuple<bool, string> IsClanNameApplicable(string name) => Tuple.Create(!string.IsNullOrWhiteSpace(name)
            && !Clan.All.Any(c => c.Name == name), "家族名称为空或重复。");
    }
}
namespace AIChronicle
{
    public static class PartyBehaviorManager { public static void RemoveAction(Hero hero) { } }
    public sealed class DepartureEntity { public string Id { get; set; } = ""; public EntityController Controller { get; set; } }
    public static class EntityManager
    {
        public static void RefreshEntity(Hero hero) { }
        public static DepartureEntity GetOrCreateEntity(Hero hero) => new()
        { Id = hero.StringId, Controller = hero == Hero.MainHero ? EntityController.Human : EntityController.Agent };
    }
    public static partial class AgentManager
    {
        public static readonly List<(string Hero, string Kind, string Text)> DepartureDiaries = new();
        public static void AppendSystemDiary(string id, string kind, string text) { DepartureDiaries.Add((id, kind, text)); }
    }
}
