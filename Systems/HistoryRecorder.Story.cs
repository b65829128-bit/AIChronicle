using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StoryMode;
using StoryMode.Quests.FirstPhase;
using StoryMode.Quests.SecondPhase;
using StoryMode.Quests.ThirdPhase;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using static TaleWorlds.CampaignSystem.QuestBase;

namespace AIChronicle
{
    public partial class HistoryRecorder
    {
        // 原版事件不会在读档时重放；只在本次战役行为实例内去重，不新增存档字段或回填旧事。
        private readonly HashSet<string> _storyRecorded = new(StringComparer.Ordinal);
        private static readonly FieldInfo? StoryOppositionField = typeof(DefeatTheConspiracyQuestBehavior.DefeatTheConspiracyQuest)
            .GetField("_oppositionKingdom", BindingFlags.Instance | BindingFlags.NonPublic);

        private sealed class StoryOccurrence
        {
            public MainStoryLine Story = null!;
            public CampaignTime Time;
            public string Date = "";
            public string Actor = "";
            public string Kingdom = "";
            public string Ruler = "";
            public string[] RulerIds = Array.Empty<string>();
        }

        private void RegisterStoryEvents()
        {
            var story = StoryModeManager.Current?.MainStoryLine;
            if (story == null) return; // 沙盒没有 StoryModeEvents 实例。

            if (story.FirstPhase?.AllPiecesCollected == true) _storyRecorded.Add("dragon_banner_assembled");
            if (story.MainStoryLineSide != MainStoryLineSide.None)
            {
                _storyRecorded.Add("dragon_banner_claimed");
                if (story.PlayerSupportedKingdom != null && Hero.MainHero?.Clan?.Kingdom != story.PlayerSupportedKingdom)
                    _storyRecorded.Add("story_withdrawn");
            }
            if (story.ThirdPhase != null)
            {
                _storyRecorded.Add("story_conspiracy_war");
                if (story.ThirdPhase.OppositionKingdoms.Count == 0 || story.PlayerSupportedKingdom?.IsEliminated == true)
                    _storyRecorded.Add("story_outcome");
            }

            CampaignEvents.OnQuestCompletedEvent.AddNonSerializedListener(this,
                new Action<QuestBase, QuestCompleteDetails>((quest, detail) => ObserveStory(() => OnStoryQuestCompleted(quest, detail))));
            CampaignEvents.KingdomDestroyedEvent.AddNonSerializedListener(this,
                new Action<Kingdom>(realm => ObserveStory(() => OnStoryKingdomDestroyed(realm))));
            StoryModeEvents.OnBannerPieceCollectedEvent.AddNonSerializedListener(this,
                new Action(() => ObserveStory(OnStoryBannerCollected)));
            StoryModeEvents.OnMainStoryLineSideChosenEvent.AddNonSerializedListener(this,
                new Action<MainStoryLineSide>(side => ObserveStory(() => OnStoryBannerClaimed(side))));
            StoryModeEvents.OnConspiracyActivatedEvent.AddNonSerializedListener(this,
                new Action(() => ObserveStory(OnStoryConspiracyWar)));
        }

        private static void ObserveStory(Action action)
        {
            if (Campaign.Current == null || StoryModeManager.Current?.MainStoryLine == null) return;
            try { action(); }
            catch (Exception e) { DebugLogger.Log("龙旗史料记录失败：" + e.Message); }
        }

        private static string StoryHero(Hero? hero) => hero == null ? "姓名不详者" : SilentDiaryText.Hero(
            hero.Clan?.Kingdom?.Name?.ToString(), hero.Clan?.Name?.ToString(), hero.Name?.ToString() ?? "");

        private static StoryOccurrence CaptureStory(bool notifyRulers = false)
        {
            var story = StoryModeManager.Current!.MainStoryLine;
            var kingdom = story.PlayerSupportedKingdom;
            var recipients = new HashSet<string>(StringComparer.Ordinal);
            if (notifyRulers && SilentDiary.Enabled)
            {
                foreach (var realm in Kingdom.All)
                {
                    var ruler = realm.RulingClan?.Leader;
                    if (realm.IsEliminated || ruler == null || !ruler.IsAlive || ruler == Hero.MainHero) continue;
                    var entity = EntityManager.GetOrCreateEntity(ruler);
                    if (entity.Controller == EntityController.Agent) recipients.Add(entity.Id);
                }
            }
            return new StoryOccurrence
            {
                Story = story,
                Time = CampaignTime.Now,
                Date = AgentManager.GetDiaryDatePrefix(),
                Actor = StoryHero(Hero.MainHero),
                Kingdom = kingdom?.Name?.ToString() ?? "国名不详的王国",
                Ruler = StoryHero(kingdom?.RulingClan?.Leader),
                RulerIds = recipients.ToArray()
            };
        }

        private void RecordStory(StoryOccurrence at, string type, string summary, string? key = null)
        {
            if (!ReferenceEquals(at.Story, StoryModeManager.Current?.MainStoryLine) || !_storyRecorded.Add(key ?? type)) return;
            RecordEvent(type, summary, at.Time);
            // 重大公共消息立即落盘，不被普通见闻的每日上限吞掉；只写情报，不排 Agent 事件。
            foreach (var id in at.RulerIds)
                AgentManager.AppendSystemDiary(id, "情报", summary, at.Date);
        }

        private void DeferStory(StoryOccurrence at, Action action)
        {
            // 等原版本轮事件的其余监听器完成；队列自带 campaign epoch，切档后旧动作会被丢弃。
            MainThreadExecutor.Post(() =>
            {
                if (ReferenceEquals(at.Story, StoryModeManager.Current?.MainStoryLine)) ObserveStory(action);
            });
        }

        private void OnStoryBannerCollected()
        {
            var story = StoryModeManager.Current!.MainStoryLine;
            if (story.FirstPhase?.AllPiecesCollected != true || _storyRecorded.Contains("dragon_banner_assembled")) return;
            var at = CaptureStory(notifyRulers: true);
            DeferStory(at, () =>
            {
                // 集齐碎片事件先于 AssembleTheBannerQuest 的 MergeDragonBanner；确认实物已重组再入史。
                if (story.DragonBanner == null || (MobileParty.MainParty?.ItemRoster.GetItemNumber(story.DragonBanner) ?? 0) <= 0) return;
                RecordStory(at, "dragon_banner_assembled", $"{at.Actor}集齐散失的龙旗碎片，重新拼合卡拉狄乌斯的龙旗。古老的龙旗再度现世。");
            });
        }

        private void OnStoryBannerClaimed(MainStoryLineSide side)
        {
            if (side == MainStoryLineSide.None) return;
            var at = CaptureStory(notifyRulers: true);
            if (at.Story.PlayerSupportedKingdom == null) return;
            var intent = at.Story.IsOnImperialQuestLine ? "重建卡拉德帝国" : "终结卡拉德帝国的统治";
            var summary = side == MainStoryLineSide.CreateImperialKingdom || side == MainStoryLineSide.CreateAntiImperialKingdom
                ? $"{at.Actor}为{at.Kingdom}举起龙旗，宣示{intent}。龙旗由{at.Ruler}执掌。"
                : $"{at.Actor}将龙旗奉予{at.Ruler}，支持{at.Kingdom}{intent}。龙旗归于{at.Kingdom}。";
            RecordStory(at, "dragon_banner_claimed", summary);
        }

        private void OnStoryConspiracyWar()
        {
            var at = CaptureStory(notifyRulers: true);
            DeferStory(at, () =>
            {
                // 原版在同一事件中创建敌国目标、组织战争；名单须等初始化完成后读取。
                var enemies = at.Story.ThirdPhase?.OppositionKingdoms;
                if (enemies == null || enemies.Count == 0) return;
                var names = string.Join("、", enemies.Select(k => k.Name.ToString()));
                RecordStory(at, "story_conspiracy_war", $"{names}围绕龙旗结成反对{at.Kingdom}的阵线，龙旗之争转入列国战争。");
            });
        }

        private void OnStoryQuestCompleted(QuestBase quest, QuestCompleteDetails detail)
        {
            if (quest is BannerInvestigationQuest)
            {
                var at = CaptureStory();
                if (detail == QuestCompleteDetails.Success)
                    RecordStory(at, "story_banner_investigated", $"{at.Actor}查明涅雷采斯之愚及龙旗的来历，得知龙旗碎片仍散落各地。");
                else if (detail == QuestCompleteDetails.Timeout || detail == QuestCompleteDetails.Fail)
                    RecordStory(at, "story_banner_search_ended", $"{at.Actor}追寻龙旗来历的事业中止，未能完成寻访。", "story_search_ended");
                return;
            }

            if (quest is AssembleEmpireQuestBehavior.AssembleEmpireQuest || quest is WeakenEmpireQuestBehavior.WeakenEmpireQuest)
            {
                var at = CaptureStory();
                if (detail == QuestCompleteDetails.Success)
                {
                    var progress = quest is AssembleEmpireQuestBehavior.AssembleEmpireQuest
                        ? "已掌握约三分之二的帝国文化城镇，重建帝国的势力初成"
                        : "已将帝国三国合计控制的城镇削减至不足四座，帝国势力大为衰弱";
                    RecordStory(at, "story_empire_progress", $"{at.Kingdom}{progress}；龙旗之争尚未结束。");
                }
                else RecordStoryWithdrawal(at, detail);
                return;
            }

            if (quest is DefeatTheConspiracyQuestBehavior.DefeatTheConspiracyQuest)
            {
                var at = CaptureStory();
                if (detail == QuestCompleteDetails.Success)
                {
                    // 原版未提供公开属性；只读该任务自己的绑定对象，不从本地化标题猜国家。
                    var opponent = StoryOppositionField?.GetValue(quest) as Kingdom;
                    if (opponent != null)
                        RecordStory(at, "story_kingdom_defeated", $"{opponent.Name}在龙旗之争中败于{at.Kingdom}。",
                            "story_kingdom_defeated|" + opponent.StringId);

                    // IsCompleted 可能在击败第一国时就为 true；必须核对全部原版敌国目标已清空。
                    if (at.Story.ThirdPhase?.OppositionKingdoms.Count == 0 && at.Story.PlayerSupportedKingdom?.IsEliminated == false)
                    {
                        var outcome = CaptureStory(notifyRulers: true);
                        var result = at.Story.IsOnImperialQuestLine
                            ? "卡拉德帝国重新统一"
                            : "帝国诸势力覆灭，卡拉德帝国的统治告终";
                        RecordStory(outcome, "story_victory", $"{at.Kingdom}击败全部反龙旗势力，赢得龙旗之争；{result}。", "story_outcome");
                    }
                }
                else if (detail == QuestCompleteDetails.Fail && at.Story.PlayerSupportedKingdom?.IsEliminated == true)
                    RecordStoryDefeat();
                else RecordStoryWithdrawal(at, detail);
                return;
            }

            // 两条举旗/献旗的备选分支会被原版自动取消，不能当成龙旗事业失败。
            if ((quest is AssembleTheBannerQuest || quest is CreateKingdomQuest || quest is SupportKingdomQuest)
                && (detail == QuestCompleteDetails.Fail || detail == QuestCompleteDetails.Timeout)
                && StoryModeManager.Current?.MainStoryLine.MainStoryLineSide == MainStoryLineSide.None)
            {
                var at = CaptureStory();
                RecordStory(at, "story_banner_search_ended", $"{at.Actor}重聚龙旗并择定归属的事业中止，龙旗未能树立于任何王国。", "story_search_ended");
            }
        }

        private void RecordStoryWithdrawal(StoryOccurrence at, QuestCompleteDetails detail)
        {
            if (detail != QuestCompleteDetails.Cancel && detail != QuestCompleteDetails.FailWithBetrayal) return;
            var realm = at.Story.PlayerSupportedKingdom;
            if (realm == null || realm.IsEliminated || Hero.MainHero?.Clan?.Kingdom == realm) return;
            RecordStory(at, "story_withdrawn", $"{at.Actor}离开{at.Kingdom}，不再为{at.Kingdom}承担龙旗之争。");
        }

        private void OnStoryKingdomDestroyed(Kingdom realm)
        {
            var story = StoryModeManager.Current!.MainStoryLine;
            if (realm != story.PlayerSupportedKingdom || story.MainStoryLineSide == MainStoryLineSide.None) return;
            // 灭国会先毁族/退国，原版可能先取消全部目标，使后续任务失败事件不再发出。
            RecordStoryDefeat();
        }

        private void RecordStoryDefeat()
        {
            if (_storyRecorded.Contains("story_withdrawn") || _storyRecorded.Contains("story_outcome")) return;
            var at = CaptureStory(notifyRulers: true);
            if (at.Story.PlayerSupportedKingdom?.IsEliminated != true) return;
            var aim = at.Story.IsOnImperialQuestLine ? "重建帝国" : "终结帝国统治";
            RecordStory(at, "story_defeat", $"奉龙旗的{at.Kingdom}覆亡，{at.Kingdom}以龙旗{aim}的事业失败。", "story_outcome");
        }
    }
}
