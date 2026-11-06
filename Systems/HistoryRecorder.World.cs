using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Party;

namespace AIChronicle
{
    public partial class HistoryRecorder
    {
        private sealed class WorldFact
        {
            public readonly CampaignTime Time = CampaignTime.Now;
            public readonly string Date = AgentManager.GetDiaryDatePrefix();
            public readonly bool DiaryEnabled = SilentDiary.Enabled;
            public string Text;
            public readonly Dictionary<string, Hero> Recipients = new(StringComparer.Ordinal);
            public WorldFact(string text) { Text = text; }
            public WorldFact Add(Hero? hero)
            {
                if (!DiaryEnabled || hero == null || !hero.IsAlive || hero == Hero.MainHero) return this;
                var entity = EntityManager.GetOrCreateEntity(hero);
                if (entity.Controller == EntityController.Agent) Recipients[entity.Id] = hero;
                return this;
            }
            public WorldFact Clan(Clan? clan) => Add(clan?.IsEliminated == false ? clan.Leader : null);
            public WorldFact Realm(Kingdom? realm) => Add(realm?.IsEliminated == false ? realm.RulingClan?.Leader : null);
        }

        private sealed class RulerState
        {
            public readonly Clan? Clan;
            public readonly Hero? Hero;
            public RulerState(Clan? clan, Hero? hero) { Clan = clan; Hero = hero; }
            public bool Matches(RulerState other) => Clan == other.Clan && Hero == other.Hero;
            public static RulerState Read(Kingdom realm) => new(realm.RulingClan, realm.RulingClan?.Leader);
        }
        private sealed class CaptivityState
        {
            public PartyBase? Party;
            public bool Public;
        }
        private readonly Dictionary<Kingdom, RulerState> _worldRulers = new();
        private readonly Dictionary<string, WorldFact> _pendingWeddings = new(StringComparer.Ordinal);
        private readonly HashSet<string> _recordedWeddings = new(StringComparer.Ordinal);
        private readonly Dictionary<Hero, CaptivityState> _worldCaptivities = new();
        private readonly HashSet<Hero> _worldReleased = new();
        private long _worldDiarySequence;
        private int _worldHistoryGeneration;

        private void RegisterWorldHistoryEvents()
        {
            ObserveWorldHistory(CaptureWorldHistoryBaseline);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this,
                new Action<CampaignGameStarter>(_ => ObserveWorldHistory(CaptureWorldHistoryBaseline)));
            CampaignEvents.RulingClanChanged.AddNonSerializedListener(this,
                new Action<Kingdom, Clan>((realm, oldClan) => ObserveWorldHistory(() => QueueRulerHistory(realm, oldClan?.Leader))));
            CampaignEvents.OnClanLeaderChangedEvent.AddNonSerializedListener(this,
                new Action<Hero, Hero>((oldHero, newHero) => ObserveWorldHistory(() => OnWorldClanLeaderChanged(oldHero, newHero))));
            CampaignEvents.KingdomDecisionConcluded.AddNonSerializedListener(this,
                new Action<KingdomDecision, DecisionOutcome, bool>((decision, _, __) => ObserveWorldHistory(() =>
                { if (decision is KingSelectionKingdomDecision) QueueRulerHistory(decision.Kingdom, null); })));
            CampaignEvents.KingdomDecisionCancelled.AddNonSerializedListener(this,
                new Action<KingdomDecision, bool>((decision, _) => ObserveWorldHistory(() =>
                { if (decision is KingSelectionKingdomDecision) QueueRulerHistory(decision.Kingdom, null); })));
            CampaignEvents.BeforeHeroesMarried.AddNonSerializedListener(this,
                new Action<Hero, Hero, bool>((first, second, _) => ObserveWorldHistory(() => CaptureWedding(first, second))));
            CampaignEvents.RomanticStateChanged.AddNonSerializedListener(this,
                new Action<Hero, Hero, Romance.RomanceLevelEnum>((first, second, level) => ObserveWorldHistory(() => CompleteWedding(first, second, level))));
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this,
                new Action<PartyBase, Hero>((party, captive) => ObserveWorldHistory(() => OnWorldPrisonerTaken(party, captive))));
            CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this,
                new Action<Hero, PartyBase, IFaction, EndCaptivityDetail, bool>((hero, party, faction, detail, _) =>
                    ObserveWorldHistory(() => OnWorldPrisonerReleased(hero, party, faction, detail))));
            RegisterWarfareHistoryEvents();
        }

        private void CaptureWorldHistoryBaseline()
        {
            _worldHistoryGeneration++;
            _worldRulers.Clear(); _pendingWeddings.Clear(); _recordedWeddings.Clear();
            _worldCaptivities.Clear(); _worldReleased.Clear();
            if (Campaign.Current == null) return;
            foreach (var realm in Kingdom.All) _worldRulers[realm] = RulerState.Read(realm);
            foreach (var clan in Clan.All)
                foreach (var hero in clan.Heroes)
                {
                    if (hero.Spouse != null) _recordedWeddings.Add(WeddingKey(hero, hero.Spouse));
                    if (hero.IsPrisoner) _worldCaptivities[hero] = new CaptivityState
                    { Party = hero.PartyBelongedToAsPrisoner, Public = IsPublicCaptive(hero) };
                }
            CaptureWarfareHistoryBaseline();
        }

        private static void ObserveWorldHistory(Action action)
        {
            if (Campaign.Current == null) return;
            try { action(); }
            catch (Exception e) { DebugLogger.Log("世事史料记录失败：" + e.Message); }
        }

        private static string WorldHero(Hero? hero) => hero == null ? "姓名不详者" : SilentDiaryText.Hero(
            hero.Clan?.Kingdom?.Name?.ToString(), hero.Clan?.Name?.ToString(), hero.Name?.ToString() ?? "");

        private void PublishWorldFact(string type, WorldFact fact, bool history = true, bool immediate = true)
        {
            if (history) RecordEvent(type, fact.Text, fact.Time);
            if (immediate)
                foreach (var id in fact.Recipients.Keys) AgentManager.AppendSystemDiary(id, "情报", fact.Text, fact.Date);
            else
            {
                var batch = SilentDiary.Begin(type, "world|" + ++_worldDiarySequence);
                foreach (var hero in fact.Recipients.Values) batch.Add(hero, fact.Text);
                batch.Publish();
            }
        }

        private void QueueRulerHistory(Kingdom realm, Hero? fallbackPrevious)
        {
            if (realm == null || realm.IsEliminated) return;
            var next = RulerState.Read(realm);
            if (next.Hero == null || !next.Hero.IsAlive) return;
            if (!_worldRulers.TryGetValue(realm, out var previous))
            {
                if (fallbackPrevious == null) { _worldRulers[realm] = next; return; } // 新建王国，另有建国史料。
                previous = new RulerState(fallbackPrevious.Clan, fallbackPrevious);
            }
            if (previous.Hero == null) { _worldRulers[realm] = next; return; } // 首次立国不虚构前任。
            if (previous.Matches(next)) return;
            var priorName = previous.Hero == null ? "姓名不详者" : SilentDiaryText.Hero(realm.Name?.ToString(),
                previous.Clan?.Name?.ToString(), previous.Hero.Name?.ToString() ?? "");
            var fact = new WorldFact($"{WorldHero(next.Hero)}成为{realm.Name}的新君，原统治者为{priorName}。").Add(next.Hero);
            foreach (var clan in realm.Clans) fact.Clan(clan);
            foreach (var kingdom in Kingdom.All) fact.Realm(kingdom);
            var campaign = Campaign.Current;
            var generation = _worldHistoryGeneration;
            // 同轮死亡/退位会先安排临时统治者，正式选王结束后才记录登基。
            MainThreadExecutor.Post(() => ObserveWorldHistory(() =>
            {
                if (Campaign.Current != campaign || generation != _worldHistoryGeneration || realm.IsEliminated || !next.Matches(RulerState.Read(realm))
                    || realm.UnresolvedDecisions.Any(d => d is KingSelectionKingdomDecision)) return;
                if (_worldRulers.TryGetValue(realm, out var known) && known.Matches(next)) return;
                _worldRulers[realm] = next;
                PublishWorldFact("ruler_changed", fact);
            }));
        }

        private void OnWorldClanLeaderChanged(Hero oldHero, Hero newHero)
        {
            var clan = newHero?.Clan;
            if (oldHero == null || clan == null || clan.IsEliminated || clan.Leader != newHero || oldHero == newHero) return;
            var fact = new WorldFact($"{clan.Name}的族长由{WorldHero(oldHero)}变更为{WorldHero(newHero)}。").Realm(clan.Kingdom);
            foreach (var member in clan.Heroes) fact.Add(member);
            PublishWorldFact("clan_leader_changed", fact, history: false); // 主文件已记同一条史料。
            if (clan.Kingdom?.RulingClan == clan) QueueRulerHistory(clan.Kingdom, oldHero);
        }

        private static string WeddingKey(Hero first, Hero second) => string.CompareOrdinal(first.StringId, second.StringId) < 0
            ? first.StringId + "|" + second.StringId : second.StringId + "|" + first.StringId;

        private void CaptureWedding(Hero first, Hero second)
        {
            if (first == null || second == null || first == second || (first.Clan == null && second.Clan == null)) return;
            var key = WeddingKey(first, second);
            if (_recordedWeddings.Contains(key)) return;
            var fact = new WorldFact($"{WorldHero(first)}与{WorldHero(second)}成婚").Add(first).Add(second).Clan(first.Clan).Clan(second.Clan);
            if (IsRoyalClan(first.Clan) || IsRoyalClan(second.Clan))
                fact.Realm(first.Clan?.Kingdom).Realm(second.Clan?.Kingdom);
            _pendingWeddings[key] = fact;
        }

        private static bool IsRoyalClan(Clan? clan) => clan != null && clan.Kingdom?.RulingClan == clan;

        private void CompleteWedding(Hero first, Hero second, Romance.RomanceLevelEnum level)
        {
            if (first == null || second == null || level != Romance.RomanceLevelEnum.Marriage
                || first.Spouse != second || second.Spouse != first || first.Clan == null || first.Clan != second.Clan) return;
            var key = WeddingKey(first, second);
            if (_recordedWeddings.Contains(key)) return;
            if (!_pendingWeddings.TryGetValue(key, out var fact))
            { CaptureWedding(first, second); if (!_pendingWeddings.TryGetValue(key, out fact)) return; }
            _pendingWeddings.Remove(key); _recordedWeddings.Add(key);
            fact.Text += $"，婚后同属{first.Clan.Name}。";
            PublishWorldFact("marriage", fact);
        }

        private static bool IsPublicCaptive(Hero hero) => hero.Clan?.Leader == hero || hero.Clan?.Kingdom?.RulingClan?.Leader == hero;

        private static string CaptorName(PartyBase? party, IFaction? faction = null)
        {
            var prison = party?.Settlement;
            if (prison != null) return prison.Name?.ToString() + "的守军";
            var hero = party?.LeaderHero ?? party?.Owner;
            return hero != null ? WorldHero(hero) + "的部队" : (faction?.Name?.ToString() ?? "不明势力");
        }

        private static WorldFact CaptivityFact(Hero hero, PartyBase? party, IFaction? faction, string text)
        {
            var captor = party?.LeaderHero ?? party?.Owner;
            return new WorldFact(text).Add(hero).Clan(hero.Clan).Realm(hero.Clan?.Kingdom)
                .Add(captor).Clan(captor?.Clan).Realm((faction ?? party?.MapFaction) as Kingdom);
        }

        private void OnWorldPrisonerTaken(PartyBase party, Hero hero)
        {
            if (hero == null || hero.Clan == null || !hero.IsAlive || !hero.IsPrisoner) return;
            var transfer = _worldCaptivities.TryGetValue(hero, out var known);
            _worldCaptivities[hero] = new CaptivityState { Party = party, Public = known?.Public == true || IsPublicCaptive(hero) };
            if (transfer) return; // 已在拘押中转移牢所，不另记一次被俘。
            _worldReleased.Remove(hero);
            var fact = CaptivityFact(hero, party, null, $"{WorldHero(hero)}被{CaptorName(party)}俘获。");
            PublishWorldFact("hero_captured", fact, history: IsPublicCaptive(hero));
        }

        private void OnWorldPrisonerReleased(Hero hero, PartyBase? party, IFaction? faction, EndCaptivityDetail detail)
        {
            if (hero == null || hero.Clan == null || !hero.IsAlive || hero.IsPrisoner) return;
            _worldCaptivities.TryGetValue(hero, out var known);
            _worldCaptivities.Remove(hero);
            if (detail == EndCaptivityDetail.Death || !_worldReleased.Add(hero)) return;
            var reason = detail switch
            {
                EndCaptivityDetail.Ransom => "经赎金获释",
                EndCaptivityDetail.ReleasedAfterPeace => "随议和获释",
                EndCaptivityDetail.ReleasedAfterBattle => "在战后获释",
                EndCaptivityDetail.ReleasedAfterEscape => "逃脱拘押",
                EndCaptivityDetail.ReleasedByChoice => "被主动释放",
                EndCaptivityDetail.ReleasedByCompensation => "经补偿获释",
                _ => "获释"
            };
            party ??= known?.Party;
            // 原版释放事件不提供付款人/营救者，不能把原拘押者猜成施恩者。
            var fact = CaptivityFact(hero, party, faction, $"{WorldHero(hero)}结束了被{CaptorName(party, faction)}拘押的处境，{reason}。");
            PublishWorldFact("hero_released", fact, history: known?.Public == true || IsPublicCaptive(hero));
        }
    }
}
