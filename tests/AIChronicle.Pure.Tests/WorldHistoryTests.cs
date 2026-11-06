using System;
using System.Collections.Generic;
using System.Linq;
using AIChronicle;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace AIChronicle.Tests
{
    internal static partial class Program
    {
        private static void RunWorldHistoryTests()
        {
            Run("world history: accession snapshots event-day rulers and domestic clan leaders", TestWorldAccession);
            Run("world history: royal clan succession and election hooks publish one accession", TestWorldRoyalSuccession);
            Run("world history: provisional ruler waits for completed election", TestWorldElection);
            Run("world history: initial crown has no invented predecessor and canceled election can settle", TestWorldInitialAndCanceledCrown);
            Run("world history: proposal is not a wedding, marriage waits for final membership", TestWorldWedding);
            Run("world history: royal marriage informs related realms only", TestWorldRoyalWedding);
            Run("world history: captivity, transfers, release and recapture are distinct", TestWorldCaptivity);
            Run("world history: ordinary captive has personal intelligence without public history", TestWorldOrdinaryCaptive);
            Run("world history: six release causes stay factual without guessed benefactors", TestWorldReleaseCauses);
            Run("world history: rebellion starts at native finished event and establishment is survival", TestWorldRebellion);
            Run("world history: rebel destruction waits for elimination and keeps event snapshot", TestWorldRebellionFailure);
            Run("world history: actual raid and completed looting share hooks without duplicate results", TestWorldRaids);
            Run("world history: interrupted raid and loaded raid cannot fabricate completed looting", TestWorldRaidInterruptedAndLoaded);
            Run("world history: three siege dispositions are facts, without invented casualties", TestWorldAftermath);
            Run("world history: disabled diaries preserve history and human/dead recipients are excluded", TestWorldDiarySwitch);
            Run("world history: loaded baselines and earlier campaign callbacks remain silent", TestWorldBaselines);
        }

        private static Clan WorldClan(string name, Kingdom? realm, string leader)
        {
            var clan = new Clan { Name = name, Kingdom = realm };
            clan.SetLeader(new Hero { Name = leader, IsLord = true }); return clan;
        }

        private static (HistoryRecorder Recorder, Clan Family, Clan Other, Kingdom South, Kingdom West) WorldScene()
        {
            var (recorder, _, south, west) = StoryScene();
            var family = WorldClan("德赫", south, "阿尔德里克"); var other = WorldClan("瓦尔", west, "泰斯" );
            CampaignEvents.OnSessionLaunchedEvent.Raise(new CampaignGameStarter());
            return (recorder, family, other, south, west);
        }

        private static void WorldRecipients(params Hero[] heroes)
        {
            AssertAllegianceRecipients(heroes);
            var text = string.Join("\n", AgentManager.StoryDiaries.Select(d => d.Text));
            foreach (var label in new[] { "玩家", "主角", "NPC", "任务", "剧情" })
                AssertFalse(text.Contains(label), "only in-world names: " + label);
        }

        private static void TestWorldAccession()
        {
            var (recorder, family, _, south, west) = WorldScene();
            var oldClan = south.RulingClan!; oldClan.Leader!.IsAlive = false;
            var westRuler = west.Leader!;
            south.RulingClan = family; CampaignEvents.RulingClanChanged.Raise(south, oldClan);
            AssertEqual(0, recorder.StoryEntries.Count, "same-stack royal state is not final yet");
            west.RulingClan!.SetLeader(new Hero { Name = "后来继位者" }); CampaignTime.Now = new CampaignTime("1085春8");
            MainThreadExecutor.Tick();
            AssertEqual("ruler_changed", recorder.StoryEntries.Single().Kind);
            AssertEqual("1085春7", recorder.StoryEntries[0].Time.Stamp);
            AssertTrue(recorder.StoryEntries[0].Text.Contains("阿尔德里克") && recorder.StoryEntries[0].Text.Contains("拉盖娅"), "old and new rulers both named");
            WorldRecipients(family.Leader!, westRuler);
            AssertTrue(AgentManager.StoryDiaries.All(d => d.Date == "[1085春7]"), "original event date survives delayed verification");
        }

        private static void TestWorldRoyalSuccession()
        {
            var (recorder, family, _, south, west) = WorldScene();
            var royalClan = south.RulingClan!; var deceased = royalClan.Leader!; deceased.IsAlive = false;
            var heir = new Hero { Name = "伊拉", Clan = royalClan }; royalClan.SetLeader(heir);
            CampaignEvents.OnClanLeaderChangedEvent.Raise(deceased, heir);
            AssertTrue(AgentManager.StoryDiaries.Any(d => d.Id == heir.StringId && d.Text.Contains("族长由")), "heir receives family succession immediately");
            AgentManager.StoryDiaries.Clear();
            CampaignEvents.RulingClanChanged.Raise(south, royalClan);
            CampaignEvents.KingdomDecisionConcluded.Raise(new KingSelectionKingdomDecision { Kingdom = south }, new DecisionOutcome(), false);
            MainThreadExecutor.Tick();
            AssertEqual("ruler_changed", recorder.StoryEntries.Single().Kind);
            WorldRecipients(heir, family.Leader!, west.Leader!);
        }

        private static void TestWorldElection()
        {
            var (recorder, family, other, south, _) = WorldScene();
            var oldClan = south.RulingClan!; oldClan.Leader!.IsAlive = false;
            var election = new KingSelectionKingdomDecision { Kingdom = south };
            south.UnresolvedDecisions.Add(election);
            south.RulingClan = family; CampaignEvents.RulingClanChanged.Raise(south, oldClan); MainThreadExecutor.Tick();
            AssertEqual(0, recorder.StoryEntries.Count, "temporary ruler cannot be crowned while election is unresolved");
            other.Kingdom = south; south.RulingClan = other;
            CampaignEvents.RulingClanChanged.Raise(south, family); south.UnresolvedDecisions.Clear();
            CampaignEvents.KingdomDecisionConcluded.Raise(election, new DecisionOutcome(), false); MainThreadExecutor.Tick();
            AssertEqual(1, recorder.StoryEntries.Count);
            AssertTrue(recorder.StoryEntries[0].Text.Contains("泰斯") && recorder.StoryEntries[0].Text.Contains("拉盖娅"), "final ruler succeeds actual predecessor");
            AssertFalse(recorder.StoryEntries[0].Text.Contains("阿尔德里克"), "temporary ruler is not a separate reign");
        }

        private static void TestWorldWedding()
        {
            var (recorder, family, other, _, _) = WorldScene();
            var first = new Hero { Name = "莉娜", Clan = family }; var second = new Hero { Name = "恩德", Clan = other };
            var firstLeader = family.Leader!; var secondLeader = other.Leader!;
            CampaignEvents.OnMarriageOfferedToPlayerEvent.Raise(first, second);
            AssertEqual(0, recorder.StoryEntries.Count); AssertEqual(0, AgentManager.StoryDiaries.Count);
            first.Spouse = second; second.Spouse = first;
            CampaignEvents.BeforeHeroesMarried.Raise(first, second, false);
            CampaignEvents.RomanticStateChanged.Raise(first, second, Romance.RomanceLevelEnum.Untested);
            CampaignEvents.RomanticStateChanged.Raise(first, second, Romance.RomanceLevelEnum.Marriage);
            AssertEqual(0, recorder.StoryEntries.Count, "marriage cannot publish before final clan migration");
            second.Clan = family; other.SetLeader(new Hero { Name = "新族长" });
            CampaignEvents.RomanticStateChanged.Raise(second, first, Romance.RomanceLevelEnum.Marriage);
            CampaignEvents.RomanticStateChanged.Raise(first, second, Romance.RomanceLevelEnum.Marriage);
            AssertEqual("marriage", recorder.StoryEntries.Single().Kind);
            AssertTrue(recorder.StoryEntries[0].Text.Contains("西帝国·瓦尔家族的恩德")
                && recorder.StoryEntries[0].Text.Contains("婚后同属德赫"), "original identity and final clan remain distinct");
            WorldRecipients(first, second, firstLeader, secondLeader);
        }

        private static void TestWorldInitialAndCanceledCrown()
        {
            var (recorder, family, _, _, _) = WorldScene();
            var realm = new Kingdom { Name = "新立王国" };
            CampaignEvents.OnSessionLaunchedEvent.Raise(new CampaignGameStarter());
            family.Kingdom = realm; realm.RulingClan = family;
            CampaignEvents.RulingClanChanged.Raise(realm, null!); MainThreadExecutor.Tick();
            AssertEqual(0, recorder.StoryEntries.Count, "first ruler belongs to founding history, not succession");
            (recorder, family, _, realm, _) = WorldScene(); var previous = realm.RulingClan!;
            var election = new KingSelectionKingdomDecision { Kingdom = realm }; realm.UnresolvedDecisions.Add(election);
            realm.RulingClan = family; CampaignEvents.RulingClanChanged.Raise(realm, previous); MainThreadExecutor.Tick();
            AssertEqual(0, recorder.StoryEntries.Count);
            realm.UnresolvedDecisions.Clear(); CampaignEvents.KingdomDecisionCancelled.Raise(election, false); MainThreadExecutor.Tick();
            AssertEqual("ruler_changed", recorder.StoryEntries.Single().Kind, "settled actual ruler is observed when election is canceled");
        }

        private static void TestWorldRoyalWedding()
        {
            var (recorder, _, other, south, west) = WorldScene(); StoryRealm("库赛特", "蒙楚格");
            var first = new Hero { Name = "伊拉", Clan = south.RulingClan }; var second = new Hero { Name = "恩德", Clan = other };
            first.Spouse = second; second.Spouse = first; CampaignEvents.BeforeHeroesMarried.Raise(first, second, true);
            second.Clan = first.Clan; CampaignEvents.RomanticStateChanged.Raise(first, second, Romance.RomanceLevelEnum.Marriage);
            WorldRecipients(first, second, south.Leader!, other.Leader!, west.Leader!);
            AssertStoryLanguage(recorder);
        }

        private static PartyBase WorldCaptor(Clan captor) => new() { LeaderHero = captor.Leader, Owner = captor.Leader, MapFaction = captor.Kingdom };
        private static void TakeWorldCaptive(PartyBase party, Hero hero)
        {
            hero.IsPrisoner = true; hero.PartyBelongedToAsPrisoner = party;
            CampaignEvents.HeroPrisonerTaken.Raise(party, hero);
        }
        private static void ReleaseWorldCaptive(PartyBase party, Hero hero, EndCaptivityDetail detail)
        {
            hero.IsPrisoner = false; hero.PartyBelongedToAsPrisoner = null;
            CampaignEvents.HeroPrisonerReleased.Raise(hero, party, party.MapFaction!, detail, false);
        }

        private static void TestWorldCaptivity()
        {
            var (recorder, family, other, south, west) = WorldScene(); var captive = family.Leader!; var party = WorldCaptor(other);
            TakeWorldCaptive(party, captive); TakeWorldCaptive(party, captive);
            var prison = new PartyBase { Settlement = new Settlement { Name = "帕拉汶" }, Owner = other.Leader, MapFaction = west };
            TakeWorldCaptive(prison, captive);
            AssertEqual("hero_captured", recorder.StoryEntries.Single().Kind, "transfer does not become another capture");
            WorldRecipients(captive, other.Leader!, south.Leader!, west.Leader!);
            AgentManager.StoryDiaries.Clear();
            ReleaseWorldCaptive(prison, captive, EndCaptivityDetail.ReleasedAfterEscape);
            ReleaseWorldCaptive(prison, captive, EndCaptivityDetail.ReleasedAfterEscape);
            AssertEqual(2, recorder.StoryEntries.Count);
            AssertTrue(recorder.StoryEntries[1].Text.Contains("帕拉汶") && recorder.StoryEntries[1].Text.Contains("逃脱拘押"), "release preserves last prison and actual cause");
            WorldRecipients(captive, other.Leader!, south.Leader!, west.Leader!);
            TakeWorldCaptive(party, captive); AssertEqual(3, recorder.StoryEntries.Count, "same-day recapture is a new occurrence");
        }

        private static void TestWorldOrdinaryCaptive()
        {
            var (recorder, family, other, south, west) = WorldScene();
            var member = new Hero { Name = "阿娜", Clan = family }; var party = WorldCaptor(other);
            TakeWorldCaptive(party, member); AssertEqual(0, recorder.StoryEntries.Count);
            WorldRecipients(member, family.Leader!, other.Leader!, south.Leader!, west.Leader!);
            AgentManager.StoryDiaries.Clear(); ReleaseWorldCaptive(party, member, EndCaptivityDetail.ReleasedByChoice);
            AssertEqual(0, recorder.StoryEntries.Count); WorldRecipients(member, family.Leader!, other.Leader!, south.Leader!, west.Leader!);
        }

        private static void TestWorldReleaseCauses()
        {
            var causes = new[] { (EndCaptivityDetail.Ransom, "经赎金获释"), (EndCaptivityDetail.ReleasedAfterPeace, "随议和获释"),
                (EndCaptivityDetail.ReleasedAfterBattle, "在战后获释"), (EndCaptivityDetail.ReleasedAfterEscape, "逃脱拘押"),
                (EndCaptivityDetail.ReleasedByChoice, "被主动释放"), (EndCaptivityDetail.ReleasedByCompensation, "经补偿获释") };
            foreach (var (detail, expected) in causes)
            {
                var (recorder, family, other, _, _) = WorldScene(); var party = WorldCaptor(other); TakeWorldCaptive(party, family.Leader!);
                ReleaseWorldCaptive(party, family.Leader!, detail);
                AssertTrue(recorder.StoryEntries.Last().Text.Contains(expected), "specific release cause: " + detail);
                AssertFalse(recorder.StoryEntries.Last().Text.Contains("营救"), "native signature provides no rescuer");
            }
            var (r, c, o, _, _) = WorldScene(); var p = WorldCaptor(o); TakeWorldCaptive(p, c.Leader!);
            ReleaseWorldCaptive(p, c.Leader!, EndCaptivityDetail.Death);
            AssertEqual(1, r.StoryEntries.Count, "death is not freedom");
        }

        private static (Settlement Town, Clan Rebels) StartWorldRebellion(Clan owner)
        {
            var town = new Settlement { Name = "厄庇克洛忒亚", OwnerClan = owner };
            var rebels = WorldClan("厄城起义军", null, "卡洛斯"); rebels.IsRebelClan = true; rebels.HomeSettlement = town;
            town.OwnerClan = rebels; CampaignEvents.RebellionFinished.Raise(town, owner); return (town, rebels);
        }

        private static void TestWorldRebellion()
        {
            var (recorder, family, _, south, _) = WorldScene(); var (town, rebels) = StartWorldRebellion(family);
            CampaignEvents.RebellionFinished.Raise(town, family);
            AssertEqual("rebellion_started", recorder.StoryEntries.Single().Kind); WorldRecipients(family.Leader!, south.Leader!, rebels.Leader!);
            AgentManager.StoryDiaries.Clear(); rebels.IsRebelClan = false; rebels.Name = "卡洛斯家族";
            CampaignEvents.RebelliousClanDisbandedAtSettlement.Raise(town, rebels);
            AssertEqual("rebellion_established", recorder.StoryEntries.Last().Kind);
            AssertFalse(recorder.StoryEntries.Last().Text.Contains("覆亡"), "native disbanded event means surviving establishment");
            WorldRecipients(family.Leader!, south.Leader!, rebels.Leader!);
        }

        private static void TestWorldRebellionFailure()
        {
            var (recorder, family, _, south, _) = WorldScene(); var (town, rebels) = StartWorldRebellion(family);
            town.OwnerClan = family; var ruler = south.Leader!; AgentManager.StoryDiaries.Clear();
            CampaignEvents.OnClanDestroyedEvent.Raise(rebels);
            AssertEqual(1, recorder.StoryEntries.Count, "pre-destruction notification cannot prove elimination");
            rebels.IsEliminated = true; south.RulingClan!.SetLeader(new Hero { Name = "后来君主" });
            CampaignTime.Now = new CampaignTime("1085春8"); MainThreadExecutor.Tick();
            AssertEqual("rebellion_failed", recorder.StoryEntries.Last().Kind);
            AssertEqual("1085春7", recorder.StoryEntries.Last().Time.Stamp); WorldRecipients(family.Leader!, ruler);
            CampaignEvents.OnClanDestroyedEvent.Raise(rebels); MainThreadExecutor.Tick(); AssertEqual(2, recorder.StoryEntries.Count);
            (recorder, family, _, _, _) = WorldScene(); (_, rebels) = StartWorldRebellion(family);
            CampaignEvents.OnClanDestroyedEvent.Raise(rebels); MainThreadExecutor.Tick();
            AssertEqual(1, recorder.StoryEntries.Count, "failed destruction leaves no invented outcome");
        }

        private static (Village Village, RaidEventComponent Raid) WorldRaid(Clan owner, Clan attacker)
        {
            var settlement = new Settlement { Name = "泽翁尼卡村", OwnerClan = owner, IsTown = false };
            var village = new Village { Settlement = settlement }; settlement.Village = village;
            settlement.Party.MapEvent = new MapEvent { AttackerSide = new MapEventSide { LeaderParty = WorldCaptor(attacker) } };
            return (village, new RaidEventComponent { MapEventSettlement = settlement });
        }

        private static void TestWorldRaids()
        {
            var (recorder, family, other, _, _) = WorldScene(); var (village, raid) = WorldRaid(family, other);
            village.VillageState = Village.VillageStates.BeingRaided;
            CampaignEvents.VillageBeingRaided.Raise(village); CampaignEvents.VillageBeingRaided.Raise(village);
            AssertEqual("village_raided", recorder.StoryEntries.Single().Kind);
            AssertEqual(0, AgentManager.StoryDiaries.Count, "ordinary raids use the existing capped buffer");
            AssertEqual(4, SilentDiary.WorldBuffered.Count);
            village.VillageState = Village.VillageStates.Looted; CampaignEvents.VillageLooted.Raise(village);
            CampaignEvents.RaidCompletedEvent.Raise(BattleSideEnum.Attacker, raid);
            AssertEqual(2, recorder.StoryEntries.Count); AssertEqual(8, SilentDiary.WorldBuffered.Count, "loot and completion callbacks report the same result once");
            village.VillageState = Village.VillageStates.BeingRaided; CampaignEvents.VillageBeingRaided.Raise(village);
            village.VillageState = Village.VillageStates.Looted; CampaignEvents.VillageLooted.Raise(village);
            CampaignEvents.RaidCompletedEvent.Raise(BattleSideEnum.Attacker, new RaidEventComponent { MapEventSettlement = village.Settlement });
            AssertEqual(4, recorder.StoryEntries.Count, "genuine same-day second raid is retained");
            AssertEqual(4, SilentDiary.WorldBuffered.Select(d => d.Key).Distinct().Count(), "separate occurrences reach ordinary cap independently");
        }

        private static void TestWorldRaidInterruptedAndLoaded()
        {
            var (recorder, family, other, _, _) = WorldScene(); var (village, raid) = WorldRaid(family, other);
            village.VillageState = Village.VillageStates.BeingRaided; CampaignEvents.OnSessionLaunchedEvent.Raise(new CampaignGameStarter());
            CampaignEvents.VillageBeingRaided.Raise(village); AssertEqual(0, recorder.StoryEntries.Count, "loaded ongoing raid has no invented start date");
            village.VillageState = Village.VillageStates.Normal;
            CampaignEvents.RaidCompletedEvent.Raise(BattleSideEnum.Defender, raid); CampaignEvents.RaidCompletedEvent.Raise(BattleSideEnum.Defender, raid);
            AssertEqual("raid_ended", recorder.StoryEntries.Single().Kind);
            AssertTrue(recorder.StoryEntries[0].Text.Contains("未被洗劫一空"), "interruption is not proof of no damage");
            (recorder, family, other, _, _) = WorldScene(); (village, raid) = WorldRaid(family, other);
            village.VillageState = Village.VillageStates.Looted; CampaignEvents.OnSessionLaunchedEvent.Raise(new CampaignGameStarter());
            CampaignEvents.VillageLooted.Raise(village); CampaignEvents.RaidCompletedEvent.Raise(BattleSideEnum.Attacker, raid);
            AssertEqual(0, recorder.StoryEntries.Count, "loaded past looting is not new history");
        }

        private static void TestWorldAftermath()
        {
            foreach (var (choice, word) in new[] { (SiegeAftermathAction.SiegeAftermath.ShowMercy, "宽赦"),
                (SiegeAftermathAction.SiegeAftermath.Pillage, "洗劫"), (SiegeAftermathAction.SiegeAftermath.Devastate, "毁坏") })
            {
                var (recorder, family, other, south, west) = WorldScene();
                var town = new Settlement { Name = "奥尼拉", OwnerClan = other }; var army = new MobileParty { LeaderHero = other.Leader };
                CampaignEvents.OnSiegeAftermathAppliedEvent.Raise(army, town, choice, family, new Dictionary<MobileParty, float>());
                AssertEqual("siege_aftermath", recorder.StoryEntries.Single().Kind);
                AssertTrue(recorder.StoryEntries[0].Text.Contains(word), "specific applied treatment");
                AssertFalse(recorder.StoryEntries[0].Text.Contains("屠杀"), "destruction does not establish civilian deaths");
                WorldRecipients(family.Leader!, other.Leader!, south.Leader!, west.Leader!);
            }
        }

        private static void TestWorldDiarySwitch()
        {
            var (recorder, family, other, _, _) = WorldScene(); SilentDiary.Enabled = false;
            var party = WorldCaptor(other); TakeWorldCaptive(party, family.Leader!); ReleaseWorldCaptive(party, family.Leader!, EndCaptivityDetail.Ransom);
            AssertEqual(2, recorder.StoryEntries.Count); AssertEqual(0, AgentManager.StoryDiaries.Count);
            SilentDiary.Enabled = true; Hero.MainHero = family.Leader; other.Leader!.IsAlive = false;
            TakeWorldCaptive(party, family.Leader!);
            AssertTrue(AgentManager.StoryDiaries.All(d => d.Id != Hero.MainHero!.StringId && d.Id != other.Leader!.StringId), "only alive AI recipients");
            AssertEqual(2, AgentManager.StoryDiaries.Count, "both realm rulers still informed");
        }

        private static void TestWorldBaselines()
        {
            var (recorder, family, other, south, _) = WorldScene(); var oldClan = south.RulingClan!;
            south.RulingClan = family; CampaignEvents.RulingClanChanged.Raise(south, oldClan);
            CampaignEvents.OnSessionLaunchedEvent.Raise(new CampaignGameStarter()); MainThreadExecutor.Tick();
            AssertEqual(0, recorder.StoryEntries.Count, "session baseline invalidates pending succession");
            var first = new Hero { Name = "莉娜", Clan = family }; var second = new Hero { Name = "恩德", Clan = family };
            first.Spouse = second; second.Spouse = first;
            var party = WorldCaptor(other); first.IsPrisoner = true; first.PartyBelongedToAsPrisoner = party;
            CampaignEvents.OnSessionLaunchedEvent.Raise(new CampaignGameStarter());
            CampaignEvents.RomanticStateChanged.Raise(first, second, Romance.RomanceLevelEnum.Marriage);
            AssertEqual(0, recorder.StoryEntries.Count, "old marriage cannot fabricate a date");
            ReleaseWorldCaptive(party, first, EndCaptivityDetail.ReleasedByChoice);
            AssertTrue(AgentManager.StoryDiaries.Any(d => d.Id == first.StringId), "loaded captive can still receive a new release");
            (recorder, family, _, south, _) = WorldScene(); oldClan = south.RulingClan!;
            south.RulingClan = family; CampaignEvents.RulingClanChanged.Raise(south, oldClan);
            MainThreadExecutor.EndCampaign(); MainThreadExecutor.BeginCampaign(); MainThreadExecutor.Tick();
            AssertEqual(0, recorder.StoryEntries.Count); AssertEqual(0, AgentManager.StoryDiaries.Count);
        }
    }
}
