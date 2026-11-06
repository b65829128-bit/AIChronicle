using System;
using System.Linq;
using AIChronicle;
using TaleWorlds.CampaignSystem;
using Change = TaleWorlds.CampaignSystem.Actions.ChangeKingdomAction.ChangeKingdomActionDetail;
using Start = TaleWorlds.CampaignSystem.Actions.StartMercenaryServiceAction.StartMercenaryServiceActionDetails;
using End = TaleWorlds.CampaignSystem.Actions.EndMercenaryServiceAction.EndMercenaryServiceActionDetails;

namespace AIChronicle.Tests
{
    internal static partial class Program
    {
        private static void RunAllegianceHistoryTests()
        {
            Run("allegiance: defection immediately informs leader and both current rulers", TestAllegianceDefection);
            Run("allegiance: joining/leaving/rebellion/founding and destruction stay factual", TestAllegianceKinds);
            Run("allegiance: real same-day returns survive duplicate notification filtering", TestAllegianceReturns);
            Run("allegiance: mercenary start and outer country change deliver once", TestAllegianceMercenaryStart);
            Run("allegiance: mercenary switch and promotion hide intermediate states", TestAllegianceMercenarySwitch);
            Run("allegiance: mercenary departure waits for final native country change", TestAllegianceMercenaryLeave);
            Run("allegiance: standalone service end snapshots date, names and recipients", TestAllegianceMercenaryEnd);
            Run("allegiance: switch disables diaries without losing the observed baseline", TestAllegianceDiarySwitch);
            Run("allegiance: recipients dedupe roles, exclude human/dead and missing realms", TestAllegianceRecipients);
            Run("allegiance: session baseline never backfills and stale callbacks cannot publish", TestAllegianceLoadAndEpoch);
        }

        private static (HistoryRecorder Recorder, Clan Clan, Kingdom Old, Kingdom Next) AllegianceScene(bool mercenary = false)
        {
            var (recorder, _, oldRealm, nextRealm) = StoryScene();
            var clan = new Clan { Name = "德赫", Kingdom = oldRealm, IsUnderMercenaryService = mercenary };
            clan.SetLeader(new Hero { Name = "阿尔德里克" });
            CampaignEvents.OnSessionLaunchedEvent.Raise(new CampaignGameStarter());
            return (recorder, clan, oldRealm, nextRealm);
        }

        private static void ChangeAllegiance(Clan clan, Kingdom? oldRealm, Kingdom? nextRealm, Change detail,
            bool mercenary = false, bool show = true)
        {
            clan.Kingdom = nextRealm; clan.IsUnderMercenaryService = mercenary;
            CampaignEvents.OnClanChangedKingdomEvent.Raise(clan, oldRealm!, nextRealm!, detail, show);
        }

        private static void StartAllegianceService(Clan clan, Kingdom realm)
        {
            clan.Kingdom = realm; clan.IsUnderMercenaryService = true;
            CampaignEvents.OnMercenaryServiceStartedEvent.Raise(clan, Start.ApplyByDefault);
        }

        private static void EndAllegianceService(Clan clan, End detail)
        {
            clan.IsUnderMercenaryService = false;
            CampaignEvents.OnMercenaryServiceEndedEvent.Raise(clan, detail);
        }

        private static void AssertAllegianceRecipients(params Hero[] heroes)
        {
            AssertEqual(string.Join(",", heroes.Select(h => h.StringId).OrderBy(x => x)),
                string.Join(",", AgentManager.StoryDiaries.Select(d => d.Id).OrderBy(x => x)), "one entry for each recipient");
            AssertTrue(AgentManager.StoryDiaries.All(d => d.Kind == "情报"), "no opinions, promises or results");
        }

        private static void TestAllegianceDefection()
        {
            var (recorder, clan, oldRealm, next) = AllegianceScene();
            ChangeAllegiance(clan, oldRealm, next, Change.JoinKingdomByDefection, show: false);
            AssertAllegianceRecipients(clan.Leader!, oldRealm.Leader!, next.Leader!);
            AssertEqual(0, MainThreadExecutor.PendingCount, "ordinary change immediately writes without waking an agent");
            var text = AgentManager.StoryDiaries[0].Text;
            foreach (var name in new[] { clan.Name, clan.Leader!.Name, oldRealm.Name, next.Name, "转投" })
                AssertTrue(text.Contains(name), "fact names both parties: " + name);
            AssertEqual("clan_changed_kingdom", recorder.StoryEntries.Single().Kind);
            AssertStoryLanguage(recorder);
        }

        private static void TestAllegianceKinds()
        {
            var (recorder, clan, oldRealm, next) = AllegianceScene();
            ChangeAllegiance(clan, oldRealm, null, Change.LeaveKingdom);
            AssertAllegianceRecipients(clan.Leader!, oldRealm.Leader!);
            AssertTrue(AgentManager.StoryDiaries[0].Text.Contains("不再隶属任何王国"), "leaving becomes independent");
            AgentManager.StoryDiaries.Clear();
            ChangeAllegiance(clan, null, next, Change.JoinKingdom);
            AssertAllegianceRecipients(clan.Leader!, next.Leader!);
            AssertFalse(AgentManager.StoryDiaries[0].Text.Contains("脱离"), "independent join has no invented former realm");
            AgentManager.StoryDiaries.Clear();
            ChangeAllegiance(clan, next, null, Change.LeaveWithRebellion);
            AssertTrue(AgentManager.StoryDiaries[0].Text.Contains("反叛并脱离"), "native rebellion detail is explicit");
            AgentManager.StoryDiaries.Clear(); next.RulingClan = clan;
            ChangeAllegiance(clan, null, next, Change.CreateKingdom);
            AssertAllegianceRecipients(clan.Leader!);
            AssertTrue(AgentManager.StoryDiaries[0].Text.Contains("建立" + next.Name), "founding is not vassal service");
            AssertEqual("kingdom_created", recorder.StoryEntries.Last().Kind);
            (recorder, clan, oldRealm, _) = AllegianceScene(); oldRealm.IsEliminated = true;
            ChangeAllegiance(clan, oldRealm, null, Change.LeaveByKingdomDestruction);
            AssertAllegianceRecipients(clan.Leader!);
            AssertTrue(AgentManager.StoryDiaries[0].Text.Contains("覆亡"), "kingdom destruction is not voluntary desertion");
            (_, clan, oldRealm, _) = AllegianceScene();
            ChangeAllegiance(clan, oldRealm, null, Change.LeaveByClanDestruction);
            AssertEqual(0, AgentManager.StoryDiaries.Count, "clan destruction cannot fabricate a departure decision");
        }

        private static void TestAllegianceReturns()
        {
            var (_, clan, oldRealm, next) = AllegianceScene();
            ChangeAllegiance(clan, oldRealm, next, Change.JoinKingdomByDefection);
            ChangeAllegiance(clan, oldRealm, next, Change.JoinKingdomByDefection);
            AssertEqual(3, AgentManager.StoryDiaries.Count, "repeat notification is not another change");
            ChangeAllegiance(clan, next, oldRealm, Change.JoinKingdomByDefection);
            ChangeAllegiance(clan, oldRealm, next, Change.JoinKingdomByDefection);
            AssertEqual(9, AgentManager.StoryDiaries.Count, "same day and same destination can recur after a genuine return");
        }

        private static void TestAllegianceMercenaryStart()
        {
            var (_, clan, oldRealm, next) = AllegianceScene();
            ChangeAllegiance(clan, oldRealm, null, Change.LeaveKingdom); AgentManager.StoryDiaries.Clear();
            StartAllegianceService(clan, next);
            ChangeAllegiance(clan, null, next, Change.JoinAsMercenary, mercenary: true);
            AssertAllegianceRecipients(clan.Leader!, next.Leader!);
            AssertTrue(AgentManager.StoryDiaries[0].Text.Contains("以佣兵身份"), "mercenary service is not an oath as vassal");
        }

        private static void TestAllegianceMercenarySwitch()
        {
            var (_, clan, oldRealm, next) = AllegianceScene(mercenary: true);
            EndAllegianceService(clan, End.ApplyByLeavingKingdom);
            AssertEqual(0, AgentManager.StoryDiaries.Count, "intermediate end cannot invent vassal service");
            StartAllegianceService(clan, next);
            ChangeAllegiance(clan, oldRealm, next, Change.JoinAsMercenary, mercenary: true);
            MainThreadExecutor.Tick(); AssertAllegianceRecipients(clan.Leader!, oldRealm.Leader!, next.Leader!);
            AssertFalse(AgentManager.StoryDiaries.Any(d => d.Text.Contains("封臣")), "switch is between employers");
            AgentManager.StoryDiaries.Clear();
            EndAllegianceService(clan, End.ApplyByBecomingVassal);
            ChangeAllegiance(clan, next, next, Change.JoinKingdom); MainThreadExecutor.Tick();
            AssertAllegianceRecipients(clan.Leader!, next.Leader!);
            AssertTrue(AgentManager.StoryDiaries[0].Text.Contains("成为" + next.Name + "的封臣"), "same-realm promotion has its own event");

            (_, clan, oldRealm, next) = AllegianceScene(mercenary: true);
            EndAllegianceService(clan, End.ApplyByDefault);
            ChangeAllegiance(clan, oldRealm, next, Change.JoinKingdomByDefection); MainThreadExecutor.Tick();
            AssertAllegianceRecipients(clan.Leader!, oldRealm.Leader!, next.Leader!);
            AssertTrue(AgentManager.StoryDiaries.All(d => d.Text.Contains("成为" + next.Name + "的封臣")), "no transient promotion in former realm");
        }

        private static void TestAllegianceMercenaryLeave()
        {
            var (_, clan, oldRealm, _) = AllegianceScene(mercenary: true);
            clan.Kingdom = null; // 原版退国先清 Kingdom，再触发佣兵结束，最后才发换国。
            EndAllegianceService(clan, End.ApplyByLeavingKingdom);
            ChangeAllegiance(clan, oldRealm, null, Change.LeaveAsMercenary); MainThreadExecutor.Tick();
            AssertAllegianceRecipients(clan.Leader!, oldRealm.Leader!);
            AssertTrue(AgentManager.StoryDiaries.All(d => d.Text.Contains("结束为" + oldRealm.Name + "的佣兵效力")), "old employer survives intermediate null kingdom");
        }

        private static void TestAllegianceMercenaryEnd()
        {
            var (_, clan, oldRealm, _) = AllegianceScene(mercenary: true);
            var leader = clan.Leader!; var ruler = oldRealm.Leader!;
            EndAllegianceService(clan, End.ApplyByDefault);
            AssertEqual(0, AgentManager.StoryDiaries.Count, "unresolved end waits for native stack to finish");
            oldRealm.RulingClan!.SetLeader(new Hero { Name = "继位君主" }); clan.SetLeader(new Hero { Name = "新族长" });
            CampaignTime.Now = new CampaignTime("1085春8"); MainThreadExecutor.Tick();
            AssertAllegianceRecipients(leader, ruler);
            AssertTrue(AgentManager.StoryDiaries.All(d => d.Date == "[1085春7]" && d.Text.Contains(leader.Name)
                && !d.Text.Contains("新族长")), "snapshot belongs to event day and people");
        }

        private static void TestAllegianceDiarySwitch()
        {
            var (recorder, clan, oldRealm, next) = AllegianceScene(mercenary: true);
            SilentDiary.Enabled = false;
            ChangeAllegiance(clan, oldRealm, next, Change.JoinAsMercenary, mercenary: true);
            EndAllegianceService(clan, End.ApplyByDefault);
            SilentDiary.Enabled = true; MainThreadExecutor.Tick();
            AssertEqual(0, AgentManager.StoryDiaries.Count, "events disabled at occurrence are not backfilled");
            AssertEqual(1, recorder.StoryEntries.Count, "diary setting leaves raw history intact");
            ChangeAllegiance(clan, next, null, Change.LeaveKingdom);
            AssertAllegianceRecipients(clan.Leader!, next.Leader!);
            AssertFalse(AgentManager.StoryDiaries[0].Text.Contains("佣兵"), "disabled promotion still advanced baseline");
        }

        private static void TestAllegianceRecipients()
        {
            var (_, clan, oldRealm, next) = AllegianceScene();
            oldRealm.RulingClan = clan; next.RulingClan = clan;
            ChangeAllegiance(clan, oldRealm, next, Change.JoinKingdomByDefection);
            AssertAllegianceRecipients(clan.Leader!);
            (_, clan, oldRealm, next) = AllegianceScene(); Hero.MainHero = clan.Leader;
            ChangeAllegiance(clan, oldRealm, next, Change.JoinKingdomByDefection);
            AssertAllegianceRecipients(oldRealm.Leader!, next.Leader!);
            (_, clan, oldRealm, next) = AllegianceScene(); clan.Leader!.IsAlive = false; next.Leader!.IsAlive = false;
            ChangeAllegiance(clan, oldRealm, next, Change.JoinKingdomByDefection);
            AssertAllegianceRecipients(oldRealm.Leader!);
            (_, clan, oldRealm, next) = AllegianceScene(); oldRealm.IsEliminated = true; next.RulingClan = null;
            ChangeAllegiance(clan, oldRealm, next, Change.JoinKingdomByDefection);
            AssertAllegianceRecipients(clan.Leader!);
            (_, clan, oldRealm, next) = AllegianceScene(); clan.IsEliminated = true;
            ChangeAllegiance(clan, oldRealm, next, Change.JoinKingdomByDefection);
            AssertEqual(0, AgentManager.StoryDiaries.Count, "eliminated clan has no living affiliation change");
            (_, clan, oldRealm, next) = AllegianceScene(); clan.Leader = null;
            ChangeAllegiance(clan, oldRealm, next, Change.JoinKingdomByDefection);
            AssertAllegianceRecipients(oldRealm.Leader!, next.Leader!);
        }

        private static void TestAllegianceLoadAndEpoch()
        {
            var (recorder, clan, oldRealm, next) = AllegianceScene();
            clan.Kingdom = next; clan.IsUnderMercenaryService = true;
            CampaignEvents.OnSessionLaunchedEvent.Raise(new CampaignGameStarter());
            AssertEqual(0, recorder.StoryEntries.Count); AssertEqual(0, AgentManager.StoryDiaries.Count);
            EndAllegianceService(clan, End.ApplyByBecomingVassal);
            AssertAllegianceRecipients(clan.Leader!, next.Leader!);
            (_, clan, oldRealm, _) = AllegianceScene(mercenary: true);
            EndAllegianceService(clan, End.ApplyByDefault);
            MainThreadExecutor.EndCampaign(); MainThreadExecutor.BeginCampaign(); MainThreadExecutor.Tick();
            AssertEqual(0, AgentManager.StoryDiaries.Count, "old campaign callback cannot write");
            (_, clan, oldRealm, _) = AllegianceScene(mercenary: true);
            EndAllegianceService(clan, End.ApplyByDefault);
            CampaignEvents.OnSessionLaunchedEvent.Raise(new CampaignGameStarter()); MainThreadExecutor.Tick();
            AssertEqual(0, AgentManager.StoryDiaries.Count, "session baseline invalidates earlier pending observations");
        }
    }
}
