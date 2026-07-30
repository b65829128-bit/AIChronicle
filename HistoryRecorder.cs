using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace MyFirstMod
{
    public class HistoryRecorder : CampaignBehaviorBase
    {
        private string? _historyDir;

        public override void RegisterEvents()
        {
            CampaignEvents.WarDeclared.AddNonSerializedListener(this,
                new Action<IFaction, IFaction, DeclareWarAction.DeclareWarDetail>(OnWarDeclared));
            CampaignEvents.MakePeace.AddNonSerializedListener(this,
                new Action<IFaction, IFaction, MakePeaceAction.MakePeaceDetail>(OnMakePeace));
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this,
                new Action<Settlement, bool, Hero, Hero, Hero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail>(OnSettlementOwnerChanged));
            CampaignEvents.KingdomDestroyedEvent.AddNonSerializedListener(this,
                new Action<Kingdom>(OnKingdomDestroyed));
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this,
                new Action<Hero, Hero, KillCharacterAction.KillCharacterActionDetail, bool>(OnHeroKilled));
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this,
                new Action<Clan, Kingdom, Kingdom, ChangeKingdomAction.ChangeKingdomActionDetail, bool>(OnClanChangedKingdom));

            try
            {
                var clanLeaderEvent = typeof(CampaignEvents).GetEvent("OnClanLeaderChangedEvent");
                if (clanLeaderEvent != null)
                {
                    var actionType = typeof(Action<,>).MakeGenericType(typeof(Hero), typeof(Hero));
                    var handler = Delegate.CreateDelegate(actionType, this, nameof(OnClanLeaderChanged));
                    clanLeaderEvent.AddEventHandler(null, handler);
                }
            }
            catch { }

            try
            {
                var kingdomCreatedEvent = typeof(CampaignEvents).GetEvent("OnKingdomCreatedEvent");
                if (kingdomCreatedEvent != null)
                {
                    var actionType = typeof(Action<>).MakeGenericType(typeof(Kingdom));
                    var handler = Delegate.CreateDelegate(actionType, this, nameof(OnKingdomCreated));
                    kingdomCreatedEvent.AddEventHandler(null, handler);
                }
            }
            catch { }

            try
            {
                var marriageEvent = typeof(CampaignEvents).GetEvent("MarriageOfferedToPlayerEvent");
                if (marriageEvent == null) marriageEvent = typeof(CampaignEvents).GetEvent("OnMarriageOfferedToPlayerEvent");
                if (marriageEvent != null)
                {
                    var actionType = typeof(Action<,>).MakeGenericType(typeof(Hero), typeof(Hero));
                    var handler = Delegate.CreateDelegate(actionType, this, nameof(OnMarriage));
                    marriageEvent.AddEventHandler(null, handler);
                }
                else
                {
                    var onMarriageEvent = typeof(CampaignEvents).GetEvent("OnMarriageEvent");
                    if (onMarriageEvent != null)
                    {
                        var actionType = typeof(Action<,>).MakeGenericType(typeof(Hero), typeof(Hero));
                        var handler = Delegate.CreateDelegate(actionType, this, nameof(OnMarriage));
                        onMarriageEvent.AddEventHandler(null, handler);
                    }
                }
            }
            catch { }
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private string GetHistoryDir()
        {
            if (_historyDir == null)
            {
                var baseDir = PromptManager.CampaignDir;
                if (string.IsNullOrEmpty(baseDir))
                    baseDir = PromptManager.PromptsBaseDir;
                _historyDir = Path.Combine(baseDir, "NPCs", "World", "history");
                Directory.CreateDirectory(_historyDir);
                Directory.CreateDirectory(Path.Combine(_historyDir, "chronicles"));
            }
            return _historyDir;
        }

        private void RecordEvent(string eventType, string summary)
        {
            if (Campaign.Current == null) return;

            var now = CampaignTime.Now;
            var year = now.GetYear;
            var season = GetSeasonName(now.GetSeasonOfYear);
            var day = now.GetDayOfSeason + 1;

            var escapedSummary = summary.Replace("\"", "'").Replace("\\", "/");
            var line = $"{{\"year\":{year},\"season\":\"{season}\",\"day\":{day},\"type\":\"{eventType}\",\"summary\":\"{escapedSummary}\"}}";

            var dir = GetHistoryDir();
            if (string.IsNullOrEmpty(dir)) return;

            var filePath = Path.Combine(dir, $"events_{year}.txt");
            File.AppendAllText(filePath, line + Environment.NewLine, Encoding.UTF8);
        }

        private static string GetSeasonName(CampaignTime.Seasons s) => s switch
        {
            CampaignTime.Seasons.Spring => "春",
            CampaignTime.Seasons.Summer => "夏",
            CampaignTime.Seasons.Autumn => "秋",
            CampaignTime.Seasons.Winter => "冬",
            _ => "?"
        };

        private static string FactionName(IFaction? f)
        {
            if (f == null) return "未知";
            return f.Name?.ToString() ?? "未知势力";
        }

        private void OnWarDeclared(IFaction faction1, IFaction faction2, DeclareWarAction.DeclareWarDetail detail)
        {
            var attacker = FactionName(faction1);
            var defender = FactionName(faction2);
            RecordEvent("war_declared", $"{attacker}向{defender}宣战");
        }

        private void OnMakePeace(IFaction faction1, IFaction faction2, MakePeaceAction.MakePeaceDetail detail)
        {
            var side1 = FactionName(faction1);
            var side2 = FactionName(faction2);
            RecordEvent("peace_made", $"{side1}与{side2}议和");
        }

        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturer, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (!settlement.IsTown && !settlement.IsCastle) return;

            var name = settlement.Name?.ToString() ?? "未知";
            var oldClan = oldOwner?.Clan?.Name?.ToString() ?? "未知";
            var newClan = newOwner?.Clan?.Name?.ToString() ?? "未知";
            var oldKingdom = oldOwner?.MapFaction?.Name?.ToString() ?? "";
            var newKingdom = newOwner?.MapFaction?.Name?.ToString() ?? "";

            var summary = $"{name}易主：从{oldKingdom}的{oldClan}转归{newKingdom}的{newClan}";
            RecordEvent("settlement_captured", summary);
        }

        private void OnKingdomDestroyed(Kingdom kingdom)
        {
            var name = kingdom.Name?.ToString() ?? "未知王国";
            RecordEvent("kingdom_destroyed", $"{name}灭亡");

            AgentScheduler.QueueSpecialChronicle(name);
        }

        private void OnKingdomCreated(Kingdom kingdom)
        {
            var name = kingdom.Name?.ToString() ?? "未知王国";
            var founder = kingdom.RulingClan?.Leader?.Name?.ToString() ?? "未知";
            RecordEvent("kingdom_created", $"{name}建立，创始人为{founder}");

            AgentScheduler.QueueSpecialChronicle(name);
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotifications = true)
        {
            if (victim == null) return;
            if (victim.Clan == null && !victim.IsLord) return;

            var name = victim.Name?.ToString() ?? "未知";
            var clan = victim.Clan?.Name?.ToString() ?? "";
            var title = "";
            if (victim.Clan?.Kingdom?.RulingClan?.Leader == victim)
                title = victim.Clan.Kingdom.Name + "统治者";
            else if (victim.Clan?.Leader == victim)
                title = $"{clan}族长";
            else if (!string.IsNullOrEmpty(clan))
                title = $"{clan}成员";

            var cause = detail switch
            {
                KillCharacterAction.KillCharacterActionDetail.DiedInBattle => "阵亡",
                KillCharacterAction.KillCharacterActionDetail.Executed => "被处决",
                KillCharacterAction.KillCharacterActionDetail.DiedOfOldAge => "寿终",
                KillCharacterAction.KillCharacterActionDetail.Murdered => "遭谋杀",
                KillCharacterAction.KillCharacterActionDetail.Lost => "失踪",
                _ => "去世"
            };

            var summary = $"{title} {name}{cause}";
            if (killer != null && killer != victim)
                summary += $"，凶手为{killer.Name}";

            RecordEvent("hero_killed", summary);

            if (victim.Clan?.Kingdom?.RulingClan?.Leader == victim)
            {
                AgentScheduler.QueueSpecialChronicle($"重要人物之死：{victim.Clan.Kingdom.Name}统治者 {name}{cause}。");
            }
            else if (victim.Clan?.Leader == victim)
            {
                AgentScheduler.QueueSpecialChronicle($"重要人物之死：{clan}族长 {name}{cause}。");
            }
            else if (victim.Clan != null && MySettings.Instance?.BiographyAllNobles != false)
            {
                AgentScheduler.QueueSpecialChronicle($"重要人物之死：{clan}成员 {name}{cause}。");
            }
            else if (victim == Hero.MainHero)
            {
                AgentScheduler.QueueSpecialChronicle($"重要人物之死：冒险者 {name}{cause}，一段传奇就此落幕。");
            }
        }

        private void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom, ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotifications = true)
        {
            var clanName = clan.Name?.ToString() ?? "未知氏族";
            var oldName = oldKingdom?.Name?.ToString() ?? "无";
            var newName = newKingdom?.Name?.ToString() ?? "独立";

            var summary = $"{clanName}脱离{oldName}，加入{newName}";
            RecordEvent("clan_changed_kingdom", summary);
        }

        private void OnClanLeaderChanged(Hero oldLeader, Hero newLeader)
        {
            if (oldLeader == null || newLeader == null) return;
            var clanName = oldLeader.Clan?.Name?.ToString() ?? newLeader.Clan?.Name?.ToString() ?? "未知氏族";
            var oldName = oldLeader.Name?.ToString() ?? "?";
            var newName = newLeader.Name?.ToString() ?? "?";

            RecordEvent("clan_leader_changed", $"{clanName}领袖由{oldName}变更为{newName}");
        }

        private void OnMarriage(Hero hero1, Hero hero2)
        {
            if (hero1 == null || hero2 == null) return;
            if (hero1.Clan == null && hero2.Clan == null) return;

            var name1 = hero1.Name?.ToString() ?? "?";
            var name2 = hero2.Name?.ToString() ?? "?";
            var clan1 = hero1.Clan?.Name?.ToString() ?? "";
            var clan2 = hero2.Clan?.Name?.ToString() ?? "";

            var summary = $"{name1}({clan1})与{name2}({clan2})成婚";
            RecordEvent("marriage", summary);
        }
    }
}
