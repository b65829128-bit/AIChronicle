using System;
using System.Linq;
using AIChronicle;
using StoryMode;
using StoryMode.Quests.FirstPhase;
using StoryMode.Quests.SecondPhase;
using StoryMode.StoryModePhases;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using static TaleWorlds.CampaignSystem.QuestBase;
using OppositionQuest = StoryMode.Quests.ThirdPhase.DefeatTheConspiracyQuestBehavior.DefeatTheConspiracyQuest;

namespace AIChronicle.Tests
{
    internal static partial class Program
    {
        private static void RunStoryHistoryTests()
        {
            Run("story history: sandbox and ordinary/alternate quests stay silent", TestStoryFiltering);
            Run("story history: investigation and stage success are facts only", TestStoryMilestones);
            Run("story history: reassembly validates the banner and snapshots rulers/date", TestStoryReassembly);
            Run("story history: missing banner/party and campaign changes cannot publish", TestStoryReassemblyGuards);
            Run("story history: four allegiances name people and realms", TestStoryAllegiances);
            Run("story history: conspiracy war waits for native opponents", TestStoryWar);
            Run("story history: diary switch only disables diaries", TestStoryDiarySwitch);
            Run("story history: first enemy defeat cannot be final victory", TestStoryVictory);
            Run("story history: terminal defeat dedupes and differs from withdrawal", TestStoryFailureAndWithdrawal);
            Run("story history: kingdom destruction is terminal even when native quests cancel first", TestStoryDestroyedRealm);
            Run("story history: loading progress cannot fabricate history", TestStoryLoadBaseline);
        }

        private static Kingdom StoryRealm(string name, string rulerName)
        {
            var realm = new Kingdom { Name = name };
            var clan = new Clan { Name = name + "王族", Kingdom = realm };
            clan.SetLeader(new Hero { Name = rulerName }); realm.RulingClan = clan;
            return realm;
        }

        private static (HistoryRecorder Recorder, MainStoryLine Story, Kingdom South, Kingdom West) StoryScene()
        {
            MainThreadExecutor.BeginCampaign(); Campaign.Current = new Campaign();
            Kingdom.All.Clear(); Clan.All.Clear();
            CampaignEvents.OnQuestCompletedEvent.Clear(); StoryModeEvents.Reset();
            CampaignEvents.KingdomDestroyedEvent.Clear();
            AllegianceTestEvents.Reset();
            WorldHistoryTestEvents.Reset();
            SilentDiary.Enabled = true; AgentManager.StoryDiaries.Clear();
            CampaignTime.Now = new CampaignTime("1085春7");
            Hero.MainHero = new Hero { Name = "炎萌", Clan = new Clan { Name = "西岭" } };
            var south = StoryRealm("南帝国", "拉盖娅"); var west = StoryRealm("西帝国", "加里俄斯");
            StoryModeManager.Current = new StoryModeManager();
            var story = StoryModeManager.Current.MainStoryLine;
            story.PlayerSupportedKingdom = south;
            story.DragonBanner = new ItemObject { StringId = "dragon_banner", Name = "龙旗" };
            MobileParty.MainParty = new MobileParty();
            var recorder = new HistoryRecorder(); recorder.RegisterEvents();
            return (recorder, story, south, west);
        }

        private static void CompleteStory(QuestBase quest, QuestCompleteDetails detail = QuestCompleteDetails.Success) =>
            CampaignEvents.OnQuestCompletedEvent.Raise(quest, detail);

        private static void AssertStoryLanguage(HistoryRecorder recorder)
        {
            var text = string.Join("\n", recorder.StoryEntries.Select(e => e.Text).Concat(AgentManager.StoryDiaries.Select(d => d.Text)));
            foreach (var forbidden in new[] { "玩家", "主角", "NPC", "任务", "剧情" })
                AssertFalse(text.Contains(forbidden, StringComparison.Ordinal), "no out-of-world label: " + forbidden);
            AssertTrue(AgentManager.StoryDiaries.All(d => d.Kind == "情报"), "only objective intelligence");
        }

        private static void TestStoryFiltering()
        {
            var (recorder, _, _, _) = StoryScene();
            CompleteStory(new QuestBase()); CompleteStory(new QuestBase(), QuestCompleteDetails.Fail);
            CompleteStory(new CreateKingdomQuest(), QuestCompleteDetails.Cancel);
            CompleteStory(new SupportKingdomQuest(), QuestCompleteDetails.Cancel);
            CompleteStory(new AssembleTheBannerQuest());
            AssertEqual(0, recorder.StoryEntries.Count); AssertEqual(0, AgentManager.StoryDiaries.Count);
            CampaignEvents.OnQuestCompletedEvent.Clear(); StoryModeEvents.Reset(); StoryModeManager.Current = null;
            recorder = new HistoryRecorder(); recorder.RegisterEvents();
            AssertEqual(0, CampaignEvents.OnQuestCompletedEvent.ListenerCount, "sandbox has no story listeners");
            StoryModeEvents.OnBannerPieceCollectedEvent.Raise(); MainThreadExecutor.Tick();
            AssertEqual(0, recorder.StoryEntries.Count);
        }

        private static void TestStoryMilestones()
        {
            var (recorder, story, _, _) = StoryScene();
            CompleteStory(new BannerInvestigationQuest()); CompleteStory(new BannerInvestigationQuest());
            AssertEqual(1, recorder.StoryEntries.Count, "same milestone dedupes");
            AssertTrue(recorder.StoryEntries[0].Text.Contains("西岭家族的炎萌"), "named investigator");
            AssertEqual("story_banner_investigated", recorder.StoryEntries[0].Kind);
            story.MainStoryLineSide = MainStoryLineSide.SupportImperialKingdom;
            CompleteStory(new AssembleEmpireQuestBehavior.AssembleEmpireQuest());
            AssertEqual("story_empire_progress", recorder.StoryEntries[1].Kind);
            AssertTrue(recorder.StoryEntries[1].Text.Contains("三分之二"), "native unification threshold");
            AssertTrue(recorder.StoryEntries[1].Text.Contains("尚未结束"), "phase goal is not final victory");
            AssertEqual(0, AgentManager.StoryDiaries.Count, "private investigation and phase goals do not inform all kings");
            AssertStoryLanguage(recorder);
            (recorder, story, _, _) = StoryScene();
            story.MainStoryLineSide = MainStoryLineSide.SupportAntiImperialKingdom;
            CompleteStory(new WeakenEmpireQuestBehavior.WeakenEmpireQuest());
            AssertTrue(recorder.StoryEntries.Single().Text.Contains("不足四座"), "native weakening threshold");
            CompleteStory(new WeakenEmpireQuestBehavior.WeakenEmpireQuest(), QuestCompleteDetails.Fail);
            AssertEqual(1, recorder.StoryEntries.Count, "phase failure when war begins is not final defeat");
            AssertStoryLanguage(recorder);
        }

        private static void TestStoryReassembly()
        {
            var (recorder, story, south, west) = StoryScene();
            var oldRuler = south.Leader!; var westRuler = west.Leader!;
            StoryRealm("旧国", "故王").IsEliminated = true;
            StoryRealm("绝嗣国", "死王").Leader!.IsAlive = false;
            StoryRealm("无王之国", "无王").RulingClan = null;
            new Kingdom { Name = "共主领土", RulingClan = south.RulingClan };
            var humanRealm = new Kingdom { Name = "炎国", RulingClan = Hero.MainHero!.Clan };
            humanRealm.RulingClan!.Leader = Hero.MainHero;
            story.FirstPhase!.CollectedBannerPieceCount = 1; StoryModeEvents.OnBannerPieceCollectedEvent.Raise();
            story.FirstPhase.CollectedBannerPieceCount = 2; StoryModeEvents.OnBannerPieceCollectedEvent.Raise();
            AssertEqual(0, MainThreadExecutor.PendingCount, "individual pieces are not milestones");
            story.FirstPhase.CollectedBannerPieceCount = 3;
            StoryModeEvents.OnBannerPieceCollectedEvent.Raise(); StoryModeEvents.OnBannerPieceCollectedEvent.Raise();
            AssertEqual(0, recorder.StoryEntries.Count, "native merge has not run yet");
            MobileParty.MainParty!.ItemRoster.AddToCounts(new EquipmentElement(story.DragonBanner!), 1);
            south.RulingClan!.SetLeader(new Hero { Name = "继任女皇" }); StoryRealm("后来建立的王国", "新王");
            Hero.MainHero.Name = "后来的姓名"; CampaignTime.Now = new CampaignTime("1085春8");
            MainThreadExecutor.Tick();
            AssertEqual(1, recorder.StoryEntries.Count); AssertEqual("dragon_banner_assembled", recorder.StoryEntries[0].Kind);
            AssertEqual("1085春7", recorder.StoryEntries[0].Time.Stamp, "raw history keeps occurrence date");
            AssertTrue(recorder.StoryEntries[0].Text.Contains("炎萌"), "event-time actor name");
            AssertFalse(recorder.StoryEntries[0].Text.Contains("后来的姓名"), "no later rename");
            AssertEqual(2, AgentManager.StoryDiaries.Count, "unique living AI kings of extant realms only");
            AssertTrue(AgentManager.StoryDiaries.Select(d => d.Id).ToHashSet().SetEquals(new[] { oldRuler.StringId, westRuler.StringId }), "event-time recipients");
            AssertTrue(AgentManager.StoryDiaries.All(d => d.Date == "[1085春7]"), "event-time diary date");
            StoryModeEvents.OnBannerPieceCollectedEvent.Raise(); MainThreadExecutor.Tick();
            AssertEqual(1, recorder.StoryEntries.Count); AssertStoryLanguage(recorder);
        }

        private static void TestStoryReassemblyGuards()
        {
            var (recorder, story, _, _) = StoryScene();
            story.FirstPhase!.CollectedBannerPieceCount = 3;
            StoryModeEvents.OnBannerPieceCollectedEvent.Raise(); MainThreadExecutor.Tick();
            AssertEqual(0, recorder.StoryEntries.Count, "failed merge cannot create history");
            MobileParty.MainParty = null; StoryModeEvents.OnBannerPieceCollectedEvent.Raise(); MainThreadExecutor.Tick();
            AssertEqual(0, recorder.StoryEntries.Count, "missing party cannot pass inventory validation");
            (recorder, story, _, _) = StoryScene();
            story.FirstPhase!.CollectedBannerPieceCount = 3; StoryModeEvents.OnBannerPieceCollectedEvent.Raise();
            MobileParty.MainParty!.ItemRoster.AddToCounts(new EquipmentElement(story.DragonBanner!), 1);
            MainThreadExecutor.EndCampaign(); MainThreadExecutor.BeginCampaign(); MainThreadExecutor.Tick();
            AssertEqual(0, recorder.StoryEntries.Count); AssertEqual(0, AgentManager.StoryDiaries.Count);
            (recorder, story, _, _) = StoryScene();
            story.FirstPhase!.CollectedBannerPieceCount = 3; StoryModeEvents.OnBannerPieceCollectedEvent.Raise();
            StoryModeManager.Current = new StoryModeManager(); MainThreadExecutor.Tick();
            AssertEqual(0, recorder.StoryEntries.Count, "replaced story is discarded");
        }

        private static void TestStoryAllegiances()
        {
            foreach (var side in new[] { MainStoryLineSide.CreateImperialKingdom, MainStoryLineSide.SupportImperialKingdom,
                MainStoryLineSide.CreateAntiImperialKingdom, MainStoryLineSide.SupportAntiImperialKingdom })
            {
                var (recorder, story, south, _) = StoryScene(); story.MainStoryLineSide = side;
                var create = side == MainStoryLineSide.CreateImperialKingdom || side == MainStoryLineSide.CreateAntiImperialKingdom;
                if (create) { south.RulingClan = Hero.MainHero!.Clan; south.RulingClan!.Kingdom = south; south.RulingClan.Leader = Hero.MainHero; }
                StoryModeEvents.OnMainStoryLineSideChosenEvent.Raise(side); StoryModeEvents.OnMainStoryLineSideChosenEvent.Raise(side);
                CompleteStory(new CreateKingdomQuest(), QuestCompleteDetails.Cancel);
                CompleteStory(new SupportKingdomQuest(), QuestCompleteDetails.Cancel); CompleteStory(new AssembleTheBannerQuest());
                var entry = recorder.StoryEntries.Single(); AssertEqual("dragon_banner_claimed", entry.Kind);
                AssertTrue(entry.Text.Contains("炎萌"), "named banner bearer"); AssertTrue(entry.Text.Contains("南帝国"), "named realm");
                AssertTrue(entry.Text.Contains(create ? "举起龙旗" : "将龙旗奉予"), "claim versus pledge");
                AssertTrue(entry.Text.Contains(story.IsOnImperialQuestLine ? "重建卡拉德帝国" : "终结卡拉德帝国的统治"), "actual public claim");
                if (!create) AssertTrue(entry.Text.Contains("拉盖娅"), "actual receiving king named");
                AssertEqual(create ? 1 : 2, AgentManager.StoryDiaries.Count, "instant diary entries, no Human recipient");
                AssertStoryLanguage(recorder);
            }
        }

        private static void TestStoryWar()
        {
            var (recorder, story, _, west) = StoryScene(); story.ThirdPhase = new ThirdPhase();
            StoryModeEvents.OnConspiracyActivatedEvent.Raise(); AssertEqual(0, recorder.StoryEntries.Count);
            story.ThirdPhase.OppositionKingdoms.Add(west); MainThreadExecutor.Tick();
            AssertEqual("story_conspiracy_war", recorder.StoryEntries.Single().Kind);
            AssertTrue(recorder.StoryEntries[0].Text.Contains("西帝国"), "actual opponent"); AssertTrue(recorder.StoryEntries[0].Text.Contains("南帝国"), "actual banner realm");
            AssertEqual(2, AgentManager.StoryDiaries.Count);
            StoryModeEvents.OnConspiracyActivatedEvent.Raise(); MainThreadExecutor.Tick();
            AssertEqual(1, recorder.StoryEntries.Count); AssertStoryLanguage(recorder);
        }

        private static void TestStoryDiarySwitch()
        {
            var (recorder, story, _, _) = StoryScene(); SilentDiary.Enabled = false;
            story.MainStoryLineSide = MainStoryLineSide.SupportImperialKingdom;
            StoryModeEvents.OnMainStoryLineSideChosenEvent.Raise(story.MainStoryLineSide);
            AssertEqual(1, recorder.StoryEntries.Count, "raw history still enabled"); AssertEqual(0, AgentManager.StoryDiaries.Count);
        }

        private static void TestStoryVictory()
        {
            foreach (var imperial in new[] { true, false })
            {
                var (recorder, story, _, west) = StoryScene(); var north = StoryRealm("北帝国", "卢孔");
                story.MainStoryLineSide = imperial ? MainStoryLineSide.SupportImperialKingdom : MainStoryLineSide.SupportAntiImperialKingdom;
                story.ThirdPhase = new ThirdPhase(); story.ThirdPhase.OppositionKingdoms.AddRange(new[] { west, north });
                story.ThirdPhase.OppositionKingdoms.Remove(west); story.ThirdPhase.IsCompleted = true;
                var westQuest = new OppositionQuest(west); CompleteStory(westQuest); CompleteStory(westQuest);
                AssertEqual(1, recorder.StoryEntries.Count, "early IsCompleted cannot end the war");
                AssertEqual("story_kingdom_defeated", recorder.StoryEntries[0].Kind);
                AssertTrue(recorder.StoryEntries[0].Text.Contains("西帝国"), "bound opponent, not a localized title");
                AssertFalse(recorder.StoryEntries[0].Text.Contains("议和"), "declined peace is not peace");
                AssertEqual(0, AgentManager.StoryDiaries.Count);
                story.ThirdPhase.OppositionKingdoms.Remove(north);
                var finalQuest = new OppositionQuest(north); CompleteStory(finalQuest); CompleteStory(finalQuest);
                AssertEqual(3, recorder.StoryEntries.Count, "two enemy defeats and one final victory");
                var final = recorder.StoryEntries.Last(); AssertEqual("story_victory", final.Kind);
                AssertTrue(final.Text.Contains(imperial ? "帝国重新统一" : "帝国的统治告终"), "actual imperial versus anti-imperial ending");
                AssertEqual(3, AgentManager.StoryDiaries.Count); AssertStoryLanguage(recorder);
            }
        }

        private static void TestStoryFailureAndWithdrawal()
        {
            var (recorder, story, south, west) = StoryScene(); story.MainStoryLineSide = MainStoryLineSide.SupportImperialKingdom;
            story.ThirdPhase = new ThirdPhase(); story.ThirdPhase.OppositionKingdoms.Add(west);
            CompleteStory(new OppositionQuest(west), QuestCompleteDetails.Fail);
            AssertEqual(0, recorder.StoryEntries.Count, "one failure alone cannot prove the realm lost");
            south.IsEliminated = true;
            CompleteStory(new OppositionQuest(west), QuestCompleteDetails.Fail); CompleteStory(new OppositionQuest(west), QuestCompleteDetails.Fail);
            AssertEqual("story_defeat", recorder.StoryEntries.Single().Kind);
            AssertEqual(1, AgentManager.StoryDiaries.Count, "destroyed realm's former king excluded"); AssertStoryLanguage(recorder);
            (recorder, story, _, west) = StoryScene();
            CompleteStory(new AssembleEmpireQuestBehavior.AssembleEmpireQuest(), QuestCompleteDetails.Cancel);
            CompleteStory(new OppositionQuest(west), QuestCompleteDetails.Cancel);
            AssertEqual("story_withdrawn", recorder.StoryEntries.Single().Kind);
            AssertFalse(recorder.StoryEntries[0].Text.Contains("覆亡"), "withdrawal is not destruction"); AssertEqual(0, AgentManager.StoryDiaries.Count);
            (recorder, _, _, _) = StoryScene();
            CompleteStory(new BannerInvestigationQuest(), QuestCompleteDetails.Timeout);
            CompleteStory(new AssembleTheBannerQuest(), QuestCompleteDetails.Timeout); CompleteStory(new CreateKingdomQuest(), QuestCompleteDetails.Fail);
            AssertEqual("story_banner_search_ended", recorder.StoryEntries.Single().Kind);
            AssertEqual(0, AgentManager.StoryDiaries.Count); AssertStoryLanguage(recorder);
        }

        private static void TestStoryLoadBaseline()
        {
            var (_, story, south, west) = StoryScene(); story.FirstPhase!.CollectedBannerPieceCount = 3;
            Hero.MainHero!.Clan!.Kingdom = south;
            story.MainStoryLineSide = MainStoryLineSide.SupportImperialKingdom; story.ThirdPhase = new ThirdPhase { IsCompleted = true };
            CampaignEvents.OnQuestCompletedEvent.Clear(); CampaignEvents.KingdomDestroyedEvent.Clear(); StoryModeEvents.Reset();
            var loaded = new HistoryRecorder(); loaded.RegisterEvents();
            AssertEqual(0, loaded.StoryEntries.Count, "loading cannot fabricate dates for past milestones");
            StoryModeEvents.OnBannerPieceCollectedEvent.Raise(); StoryModeEvents.OnMainStoryLineSideChosenEvent.Raise(story.MainStoryLineSide);
            StoryModeEvents.OnConspiracyActivatedEvent.Raise(); CompleteStory(new OppositionQuest(west)); MainThreadExecutor.Tick();
            AssertFalse(loaded.StoryEntries.Any(e => e.Kind == "dragon_banner_assembled" || e.Kind == "dragon_banner_claimed" || e.Kind == "story_victory"), "completed milestones are not republished");
            AssertEqual(0, AgentManager.StoryDiaries.Count);
            (_, story, south, west) = StoryScene();
            Hero.MainHero!.Clan!.Kingdom = south; story.MainStoryLineSide = MainStoryLineSide.SupportImperialKingdom;
            story.ThirdPhase = new ThirdPhase { IsCompleted = true }; story.ThirdPhase.OppositionKingdoms.Add(west);
            CampaignEvents.OnQuestCompletedEvent.Clear(); CampaignEvents.KingdomDestroyedEvent.Clear(); StoryModeEvents.Reset();
            loaded = new HistoryRecorder(); loaded.RegisterEvents();
            story.ThirdPhase.OppositionKingdoms.Remove(west); CompleteStory(new OppositionQuest(west));
            AssertTrue(loaded.StoryEntries.Any(e => e.Kind == "story_victory"), "new final victory after loading partial progress is recorded");
        }

        private static void TestStoryDestroyedRealm()
        {
            var (recorder, story, south, west) = StoryScene();
            story.MainStoryLineSide = MainStoryLineSide.SupportImperialKingdom;
            CampaignEvents.KingdomDestroyedEvent.Raise(west);
            CampaignEvents.KingdomDestroyedEvent.Raise(south);
            AssertEqual(0, recorder.StoryEntries.Count, "unrelated or still-active realm cannot prove failure");
            south.IsEliminated = true;
            CompleteStory(new AssembleEmpireQuestBehavior.AssembleEmpireQuest(), QuestCompleteDetails.Cancel);
            CampaignEvents.KingdomDestroyedEvent.Raise(south);
            CompleteStory(new OppositionQuest(west), QuestCompleteDetails.Fail);
            AssertEqual("story_defeat", recorder.StoryEntries.Single().Kind);
            AssertEqual(1, AgentManager.StoryDiaries.Count, "destruction fallback and native fail share the outcome key");
            AssertStoryLanguage(recorder);

            (recorder, story, south, west) = StoryScene();
            story.MainStoryLineSide = MainStoryLineSide.SupportImperialKingdom;
            CompleteStory(new OppositionQuest(west), QuestCompleteDetails.Cancel);
            south.IsEliminated = true; CampaignEvents.KingdomDestroyedEvent.Raise(south);
            AssertEqual("story_withdrawn", recorder.StoryEntries.Single().Kind, "earlier withdrawal cannot become later personal defeat");
            AssertEqual(0, AgentManager.StoryDiaries.Count);

            // 旧档已退出路线时，不能因为后来奉旗王国灭亡而新增当前结局。
            (_, story, south, _) = StoryScene(); story.MainStoryLineSide = MainStoryLineSide.SupportImperialKingdom;
            CampaignEvents.OnQuestCompletedEvent.Clear(); CampaignEvents.KingdomDestroyedEvent.Clear(); StoryModeEvents.Reset();
            var loaded = new HistoryRecorder(); loaded.RegisterEvents();
            south.IsEliminated = true; CampaignEvents.KingdomDestroyedEvent.Raise(south);
            AssertEqual(0, loaded.StoryEntries.Count, "loaded prior withdrawal has no later terminal announcement");
            AssertEqual(0, AgentManager.StoryDiaries.Count);
        }
    }
}
