using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace MyFirstMod
{
    public class HistoryRecorder : CampaignBehaviorBase
    {
        private string? _historyDir;
        private readonly HashSet<Settlement> _recentSiegeCaptures = new();
        private readonly Dictionary<Settlement, (int Attackers, int Defenders)> _siegeStartTroops = new();
        private List<string>? _serializedSiegeIds;
        private List<int>? _serializedSiegeAttackers;
        private List<int>? _serializedSiegeDefenders;

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

            CampaignEvents.SiegeCompletedEvent.AddNonSerializedListener(this,
                new Action<Settlement, MobileParty, bool, MapEvent.BattleTypes>(OnSiegeCompleted));

            CampaignEvents.OnSiegeEventStartedEvent.AddNonSerializedListener(this,
                new Action<SiegeEvent>(OnSiegeStarted));

            // 修复：直接用 AddNonSerializedListener 注册——CampaignEvents 成员是 CampaignEvent<T> 静态属性而非 CLR event，
            // 原反射 GetEvent 恒返回 null，导致 siege_abandoned/clan_leader_changed/kingdom_created/marriage 四种史料从不记录。
            CampaignEvents.OnSiegeEventEndedEvent.AddNonSerializedListener(this,
                new Action<SiegeEvent>(OnSiegeEnded));
            CampaignEvents.OnClanLeaderChangedEvent.AddNonSerializedListener(this,
                new Action<Hero, Hero>(OnClanLeaderChanged));
            CampaignEvents.OnMarriageOfferedToPlayerEvent.AddNonSerializedListener(this,
                new Action<Hero, Hero>(OnMarriage));
            // kingdom_created 游戏无独立事件：在 OnClanChangedKingdom 中按 CreateKingdom 详情补记
        }

        public override void SyncData(IDataStore dataStore)
        {
            // 修复：保存前先重建序列化列表——原实现在 SyncData 之后才重建，导致围城兵力数据滞后一次存档
            if (dataStore.IsSaving)
            {
                _serializedSiegeIds = new List<string>();
                _serializedSiegeAttackers = new List<int>();
                _serializedSiegeDefenders = new List<int>();
                foreach (var kv in _siegeStartTroops)
                {
                    _serializedSiegeIds.Add(kv.Key.StringId);
                    _serializedSiegeAttackers.Add(kv.Value.Attackers);
                    _serializedSiegeDefenders.Add(kv.Value.Defenders);
                }
            }

            if (dataStore.SyncData("mfm_siege_ids", ref _serializedSiegeIds)
                && dataStore.SyncData("mfm_siege_attackers", ref _serializedSiegeAttackers)
                && dataStore.SyncData("mfm_siege_defenders", ref _serializedSiegeDefenders))
            {
                _siegeStartTroops.Clear();
                if (_serializedSiegeIds != null && _serializedSiegeAttackers != null && _serializedSiegeDefenders != null)
                {
                    for (int i = 0; i < _serializedSiegeIds.Count
                        && i < _serializedSiegeAttackers.Count
                        && i < _serializedSiegeDefenders.Count; i++)
                    {
                        var id = _serializedSiegeIds[i];
                        if (string.IsNullOrEmpty(id)) continue;
                        foreach (var s in Settlement.All)
                        {
                            if (s.StringId == id)
                            {
                                _siegeStartTroops[s] = (_serializedSiegeAttackers[i], _serializedSiegeDefenders[i]);
                                break;
                            }
                        }
                    }
                }
                return;
            }
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
            try
            {
                // 带重试写入：史官后台读史料时若撞上"文件正被使用"，重试而非崩游戏；仍失败则丢弃该事件（记日志）
                SafeFileIO.AppendAllText(filePath, line + Environment.NewLine);
            }
            catch (Exception e)
            {
                DebugLogger.Log($"史料写入失败（可能文件被占用）：{filePath} → {e.Message}");
            }
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
            if (_recentSiegeCaptures.Remove(settlement)) return;

            var name = settlement.Name?.ToString() ?? "未知";
            var oldClan = oldOwner?.Clan?.Name?.ToString() ?? "未知";
            var newClan = newOwner?.Clan?.Name?.ToString() ?? "未知";
            var oldKingdom = oldOwner?.MapFaction?.Name?.ToString() ?? "";
            var newKingdom = newOwner?.MapFaction?.Name?.ToString() ?? "";

            var summary = $"{name}易主：从{oldKingdom}的{oldClan}转归{newKingdom}的{newClan}";
            RecordEvent("settlement_captured", summary);
        }

        private void OnSiegeStarted(SiegeEvent siegeEvent)
        {
            var settlement = siegeEvent.BesiegedSettlement;
            if (settlement == null || (!settlement.IsTown && !settlement.IsCastle)) return;

            var attackerParty = siegeEvent.BesiegerCamp.LeaderParty;
            var attackerLeader = attackerParty?.LeaderHero?.Name?.ToString() ?? "?";
            var attackerKingdom = attackerParty?.MapFaction?.Name?.ToString() ?? "?";

            var attackers = 0;
            foreach (var party in siegeEvent.BesiegerCamp.GetInvolvedPartiesForEventType(MapEvent.BattleTypes.Siege))
                attackers += party.NumberOfHealthyMembers;

            var defenders = 0;
            if (settlement.Town?.GarrisonParty != null)
                defenders += settlement.Town.GarrisonParty.MemberRoster.TotalHealthyCount;
            defenders += settlement.MilitiaPartyComponent?.MobileParty?.MemberRoster?.TotalHealthyCount ?? 0;

            _siegeStartTroops[settlement] = (attackers, defenders);
            if (attackers > 0)
                RecordEvent("siege_started", $"{attackerKingdom}的{attackerLeader}率{attackers}人围攻{settlement.Name}，守军{defenders}人");
            else
                RecordEvent("siege_started", $"{attackerKingdom}的{attackerLeader}围攻{settlement.Name}");
        }

        private void OnSiegeCompleted(Settlement settlement, MobileParty attackerParty, bool isWin, MapEvent.BattleTypes battleType)
        {
            if (!settlement.IsTown && !settlement.IsCastle) return;

            var attackerLeader = attackerParty?.LeaderHero?.Name?.ToString() ?? "?";
            var attackerKingdom = attackerParty?.MapFaction?.Name?.ToString() ?? "?";

            var attackers = 0;
            var defenders = 0;
            if (_siegeStartTroops.TryGetValue(settlement, out var counts))
            {
                attackers = counts.Attackers;
                defenders = counts.Defenders;
            }
            _siegeStartTroops.Remove(settlement);

            if (isWin)
            {
                var summary = attackers > 0
                    ? $"{attackerKingdom}的{attackerLeader}率{attackers}人攻克{settlement.Name}，守军{defenders}人"
                    : $"{attackerKingdom}的{attackerLeader}攻克{settlement.Name}";
                _recentSiegeCaptures.Add(settlement);
                RecordEvent("settlement_captured", summary);
            }
            else
            {
                RecordEvent("siege_failed", $"{attackerKingdom}的{attackerLeader}围攻{settlement.Name}失败，攻城部队被击败");
            }
        }

        private void OnSiegeEnded(SiegeEvent siegeEvent)
        {
            var settlement = siegeEvent.BesiegedSettlement;
            if (settlement == null || (!settlement.IsTown && !settlement.IsCastle)) return;

            if (!_siegeStartTroops.Remove(settlement)) return;

            var attackerParty = siegeEvent.BesiegerCamp.LeaderParty;
            var attackerLeader = attackerParty?.LeaderHero?.Name?.ToString() ?? "?";
            var attackerKingdom = attackerParty?.MapFaction?.Name?.ToString() ?? "?";
            RecordEvent("siege_abandoned", $"{attackerKingdom}的{attackerLeader}放弃了对{settlement.Name}的围攻");
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
            // 修复：主角（无氏族冒险者）放行——原守卫把无氏族非领主全部拦下，导致主角死亡列传分支永不可达
            if (victim.Clan == null && !victim.IsLord && victim != Hero.MainHero) return;

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

            // 修复：游戏无 kingdom_created 独立事件——王国创建走 ChangeKingdomAction.CreateKingdom 详情，在此补记"新王国建立"
            if (detail == ChangeKingdomAction.ChangeKingdomActionDetail.CreateKingdom && newKingdom != null)
            {
                RecordEvent("kingdom_created", $"{clanName}建立了新王国 {newKingdom.Name}");
            }
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
