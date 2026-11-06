// 史事回归直接链接生产 partial；只提供原版签名与只读状态，不模拟婚姻、选王、战斗或 AI。
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace TaleWorlds.CampaignSystem
{
    public sealed class StoryTestEvent<T1, T2, T3>
    {
        private readonly List<Action<T1, T2, T3>> _listeners = new();
        public void AddNonSerializedListener(object owner, Action<T1, T2, T3> action) => _listeners.Insert(0, action);
        public void Raise(T1 a, T2 b, T3 c) { foreach (var action in _listeners.ToArray()) action(a, b, c); }
        public void Clear() => _listeners.Clear();
    }
    public static class Romance { public enum RomanceLevelEnum { Untested, Marriage } }
    public sealed partial class Kingdom
    {
        public IEnumerable<Clan> Clans => Clan.All.Where(c => c.Kingdom == this);
        public List<KingdomDecision> UnresolvedDecisions { get; } = new();
    }
    public static partial class CampaignEvents
    {
        public static StoryTestEvent<Kingdom, Clan> RulingClanChanged { get; } = new();
        public static StoryTestEvent<Hero, Hero> OnClanLeaderChangedEvent { get; } = new();
        public static StoryTestEvent<KingdomDecision, DecisionOutcome, bool> KingdomDecisionConcluded { get; } = new();
        public static StoryTestEvent<KingdomDecision, bool> KingdomDecisionCancelled { get; } = new();
        public static StoryTestEvent<Hero, Hero, bool> BeforeHeroesMarried { get; } = new();
        public static StoryTestEvent<Hero, Hero, Romance.RomanceLevelEnum> RomanticStateChanged { get; } = new();
        public static StoryTestEvent<Hero, Hero> OnMarriageOfferedToPlayerEvent { get; } = new();
        public static StoryTestEvent<PartyBase, Hero> HeroPrisonerTaken { get; } = new();
        public static StoryTestEvent<Hero, PartyBase, IFaction, EndCaptivityDetail, bool> HeroPrisonerReleased { get; } = new();
        public static StoryTestEvent<Settlement, Clan> RebellionFinished { get; } = new();
        public static StoryTestEvent<Settlement, Clan> RebelliousClanDisbandedAtSettlement { get; } = new();
        public static StoryTestEvent<Clan> OnClanDestroyedEvent { get; } = new();
        public static StoryTestEvent<Village> VillageBeingRaided { get; } = new();
        public static StoryTestEvent<Village> VillageLooted { get; } = new();
        public static StoryTestEvent<BattleSideEnum, RaidEventComponent> RaidCompletedEvent { get; } = new();
        public static StoryTestEvent<MobileParty, Settlement, SiegeAftermathAction.SiegeAftermath, Clan, Dictionary<MobileParty, float>> OnSiegeAftermathAppliedEvent { get; } = new();
    }
    public static class WorldHistoryTestEvents
    {
        public static void Reset()
        {
            CampaignEvents.RulingClanChanged.Clear(); CampaignEvents.OnClanLeaderChangedEvent.Clear();
            CampaignEvents.KingdomDecisionConcluded.Clear(); CampaignEvents.KingdomDecisionCancelled.Clear(); CampaignEvents.BeforeHeroesMarried.Clear();
            CampaignEvents.RomanticStateChanged.Clear(); CampaignEvents.OnMarriageOfferedToPlayerEvent.Clear();
            CampaignEvents.HeroPrisonerTaken.Clear(); CampaignEvents.HeroPrisonerReleased.Clear();
            CampaignEvents.RebellionFinished.Clear(); CampaignEvents.RebelliousClanDisbandedAtSettlement.Clear();
            CampaignEvents.OnClanDestroyedEvent.Clear(); CampaignEvents.VillageBeingRaided.Clear();
            CampaignEvents.VillageLooted.Clear(); CampaignEvents.RaidCompletedEvent.Clear();
            CampaignEvents.OnSiegeAftermathAppliedEvent.Clear(); Village.All.Clear(); AIChronicle.SilentDiary.WorldBuffered.Clear();
        }
    }
}
namespace TaleWorlds.CampaignSystem.Election
{
    public class KingdomDecision { public Kingdom Kingdom { get; set; } = null!; }
    public sealed class KingSelectionKingdomDecision : KingdomDecision { }
    public sealed class DecisionOutcome { }
}
namespace TaleWorlds.CampaignSystem.Actions
{
    public enum EndCaptivityDetail { Ransom, ReleasedAfterPeace, ReleasedAfterBattle, ReleasedAfterEscape, ReleasedByChoice, Death, ReleasedByCompensation }
    public static class SiegeAftermathAction { public enum SiegeAftermath { Devastate, Pillage, ShowMercy } }
}
namespace TaleWorlds.CampaignSystem.Settlements
{
    public sealed partial class Settlement
    {
        public Clan? OwnerClan { get; set; }
        public bool IsTown { get; set; } = true;
        public bool IsCastle { get; set; }
        public PartyBase Party { get; } = new();
        public Village? Village { get; set; }
    }
    public sealed class Village
    {
        public enum VillageStates { Normal, BeingRaided, Looted }
        public static List<Village> All { get; } = new();
        public Village() { All.Add(this); }
        public Settlement Settlement { get; set; } = null!;
        public VillageStates VillageState { get; set; }
    }
}
namespace TaleWorlds.CampaignSystem.Party
{
    public sealed partial class PartyBase
    {
        public Hero? Owner { get; set; }
        public Hero? LeaderHero { get; set; }
        public Settlement? Settlement { get; set; }
        public bool IsSettlement => Settlement != null;
        public IFaction? MapFaction { get; set; }
        public MapEvent? MapEvent { get; set; }
    }
}
namespace TaleWorlds.CampaignSystem.MapEvents
{
    public sealed class MapEvent { public MapEventSide? AttackerSide { get; set; } }
    public sealed class MapEventSide { public PartyBase? LeaderParty { get; set; } }
    public sealed class RaidEventComponent { public Settlement? MapEventSettlement { get; set; } }
}
namespace TaleWorlds.Core { public enum BattleSideEnum { None, Attacker, Defender } }
namespace AIChronicle
{
    public static partial class SilentDiary
    {
        public static readonly List<(string Type, string Key, string Id, string Text)> WorldBuffered = new();
        public static WorldTestDiaryBatch Begin(string type, string key) => new(type, key);
    }
    // 输出接缝：只捕获交给普通 SilentDiary 缓冲的通知，不模拟配额或激活。
    public sealed class WorldTestDiaryBatch
    {
        private readonly string _type, _key;
        private readonly Dictionary<string, string> _items = new();
        public WorldTestDiaryBatch(string type, string key) { _type = type; _key = key; }
        public WorldTestDiaryBatch Add(Hero hero, string text)
        { if (hero.IsAlive && hero != Hero.MainHero) _items[hero.StringId] = text; return this; }
        public void Publish()
        { if (SilentDiary.Enabled) foreach (var item in _items) SilentDiary.WorldBuffered.Add((_type, _key, item.Key, item.Value)); }
    }
}
