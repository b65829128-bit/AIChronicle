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

namespace AIChronicle
{
    public class HistoryRecorder : CampaignBehaviorBase
    {
        private string? _historyDir;
        /// <summary>册封宣言：由 ExecuteTransferFief 在转让前设置（「{国王}以「{reason}」册封」），OnSettlementOwnerChanged 读取后清空。</summary>
        public static string? PendingFiefGrantText;
        /// <summary>宣战宣言：由 ExecuteDeclareWar 在宣战前设置（国王的宣战声明），OnWarDeclared 读取后清空——历史与现实形成对照的素材。</summary>
        public static string? PendingWarDeclaration;
        private readonly HashSet<Settlement> _recentSiegeCaptures = new();
        // 围城开始时即记录攻城方名号——结束时 LeaderParty 可能已被击败/解散而取不到名字（史料 "?" 的根源）
        private readonly Dictionary<Settlement, (int Attackers, int Defenders, string Leader, string Kingdom)> _siegeStartTroops = new();
        // 攻城方名号的独立缓存（不随结束事件消耗）——OnSiegeEnded 与 OnSiegeCompleted 两个处理器都可能触发，
        // 若只读共享的 _siegeStartTroops，先触发者 Remove 后，后触发者就拿不到名号（史料 "?" 残留的根源）。
        private readonly Dictionary<Settlement, (string Leader, string Kingdom)> _siegeActorCache = new();
        /// <summary>大会战入史门槛：双方总兵力达到此数才记 battle_fought 史料，避免小规模遭遇战刷屏。</summary>
        private const int FieldBattleMinTroops = 600;
        private List<string>? _serializedSiegeIds;
        private List<int>? _serializedSiegeAttackers;
        private List<int>? _serializedSiegeDefenders;
        private List<string>? _serializedSiegeLeaders;
        private List<string>? _serializedSiegeKingdoms;

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

            // 野战/解围野战入史：战场决胜不伴随攻城时原史料缺失（战争叙事"围攻单边"），此事件补上大会战
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this,
                new Action<MapEvent>(OnMapEventEnded));

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
                _serializedSiegeLeaders = new List<string>();
                _serializedSiegeKingdoms = new List<string>();
                foreach (var kv in _siegeStartTroops)
                {
                    _serializedSiegeIds.Add(kv.Key.StringId);
                    _serializedSiegeAttackers.Add(kv.Value.Attackers);
                    _serializedSiegeDefenders.Add(kv.Value.Defenders);
                    _serializedSiegeLeaders.Add(kv.Value.Leader);
                    _serializedSiegeKingdoms.Add(kv.Value.Kingdom);
                }
            }

            if (dataStore.SyncData("mfm_siege_ids", ref _serializedSiegeIds)
                && dataStore.SyncData("mfm_siege_attackers", ref _serializedSiegeAttackers)
                && dataStore.SyncData("mfm_siege_defenders", ref _serializedSiegeDefenders))
            {
                // 攻城方名号是新增字段，旧存档没有 → 可选同步，缺失则兜底 "?"
                dataStore.SyncData("mfm_siege_leaders", ref _serializedSiegeLeaders);
                dataStore.SyncData("mfm_siege_kingdoms", ref _serializedSiegeKingdoms);

                _siegeStartTroops.Clear();
                if (_serializedSiegeIds != null && _serializedSiegeAttackers != null && _serializedSiegeDefenders != null)
                {
                    for (int i = 0; i < _serializedSiegeIds.Count
                        && i < _serializedSiegeAttackers.Count
                        && i < _serializedSiegeDefenders.Count; i++)
                    {
                        var id = _serializedSiegeIds[i];
                        if (string.IsNullOrEmpty(id)) continue;
                        var leader = _serializedSiegeLeaders != null && i < _serializedSiegeLeaders.Count ? _serializedSiegeLeaders[i] : "?";
                        var kingdom = _serializedSiegeKingdoms != null && i < _serializedSiegeKingdoms.Count ? _serializedSiegeKingdoms[i] : "?";
                        foreach (var s in Settlement.All)
                        {
                            if (s.StringId == id)
                            {
                                _siegeStartTroops[s] = (_serializedSiegeAttackers[i], _serializedSiegeDefenders[i], leader, kingdom);
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

        private string GetCourtDir()
        {
            var baseDir = PromptManager.CampaignDir;
            if (string.IsNullOrEmpty(baseDir))
                baseDir = PromptManager.PromptsBaseDir;
            var dir = Path.Combine(baseDir, "NPCs", "World", "court");
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// 战功记录：围攻/攻克/失利 写入 World/court/{王国}_merit.txt。
        /// 供国王内政审视读取——「近期战事中谁在出力」是朝堂风声的真实来源，不是编的。
        /// </summary>
        private void RecordMerit(string kingdomName, string leader, string action, string settlementName)
        {
            if (string.IsNullOrEmpty(kingdomName) || kingdomName == "?"
                || string.IsNullOrEmpty(leader) || leader == "?"
                || string.IsNullOrEmpty(settlementName))
                return;
            try
            {
                var filePath = Path.Combine(GetCourtDir(), $"{kingdomName}_merit.txt");
                var now = CampaignTime.Now;
                var season = GetSeasonName(now.GetSeasonOfYear);
                var day = now.GetDayOfSeason + 1;
                SafeFileIO.AppendAllText(filePath, $"{season}{day}: {leader} {action} {settlementName}" + Environment.NewLine);
            }
            catch (Exception e)
            {
                DebugLogger.Log($"战功记录失败：{e.Message}");
            }
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

        /// <summary>静态入口：供 create_clan（天意建族）等外部模块记录家族建立事件。</summary>
        public static void RecordClanCreated(string summary)
        {
            RecordExternalEvent("clan_created", summary);
        }

        /// <summary>静态入口：供 DiplomacyService 记录结盟/背盟/贸易协定等外交史料（alliance_made 等）。</summary>
        public static void RecordDiplomacyEvent(string eventType, string summary)
        {
            RecordExternalEvent(eventType, summary);
        }

        private static void RecordExternalEvent(string eventType, string summary)
        {
            if (Campaign.Current == null) return;
            var behavior = Campaign.Current.GetCampaignBehavior<HistoryRecorder>();
            behavior?.RecordEvent(eventType, summary);
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
            // 开战即毁约：两国若在盟约/贸易协定期内，原版会随宣战自动终止协定。
            // 清掉到期日志与自然到期观察项，避免该对协定之后被误判为"期满而罢"（战争毁约由 war_declared 叙事）。
            if (faction1 is Kingdom warK1 && faction2 is Kingdom warK2)
            {
                DiplomacyService.ClearAgreementTracking("盟约", warK1, warK2);
                DiplomacyService.ClearAgreementTracking("贸易", warK1, warK2);
            }

            var attacker = FactionName(faction1);
            var defender = FactionName(faction2);
            var declaration = PendingWarDeclaration;
            PendingWarDeclaration = null; // 用完即清
            var summary = string.IsNullOrEmpty(declaration)
                ? $"{attacker}向{defender}宣战"
                : $"{attacker}向{defender}宣战。宣战宣言：「{declaration}」";
            RecordEvent("war_declared", summary);
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

            // 国王册封/转让：记 fief_granted（附册封宣言），而非 settlement_captured——转让不是攻城
            if (detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByKingDecision
                || detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByGift)
            {
                var name = settlement.Name?.ToString() ?? "未知";
                var oldClan = oldOwner?.Clan?.Name?.ToString() ?? "未知";
                var newClan = newOwner?.Clan?.Name?.ToString() ?? "未知";
                var oldLeader = oldOwner?.Name?.ToString() ?? "";
                var newLeader = newOwner?.Name?.ToString() ?? "";
                var grantNote = PendingFiefGrantText;
                PendingFiefGrantText = null; // 用完即清

                var summary = string.IsNullOrEmpty(grantNote)
                    ? $"册封：将{name}从{oldClan}（{oldLeader}）转予{newClan}（{newLeader}）"
                    : $"{grantNote}：将{name}从{oldClan}（{oldLeader}）转予{newClan}（{newLeader}）";
                RecordEvent("fief_granted", summary);
                return;
            }

            var sName = settlement.Name?.ToString() ?? "未知";
            var oldClanName = oldOwner?.Clan?.Name?.ToString() ?? "未知";
            var newClanName = newOwner?.Clan?.Name?.ToString() ?? "未知";
            var oldKingdom = oldOwner?.MapFaction?.Name?.ToString() ?? "";
            var newKingdom = newOwner?.MapFaction?.Name?.ToString() ?? "";

            var summary2 = $"{sName}易主：从{oldKingdom}的{oldClanName}转归{newKingdom}的{newClanName}";
            RecordEvent("settlement_captured", summary2);
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

            _siegeStartTroops[settlement] = (attackers, defenders, attackerLeader, attackerKingdom);
            _siegeActorCache[settlement] = (attackerLeader, attackerKingdom);
            if (attackers > 0)
                RecordEvent("siege_started", $"{attackerKingdom}的{attackerLeader}率{attackers}人围攻{settlement.Name}，守军{defenders}人");
            else
                RecordEvent("siege_started", $"{attackerKingdom}的{attackerLeader}围攻{settlement.Name}");
            RecordMerit(attackerKingdom, attackerLeader, "围攻", settlement.Name?.ToString() ?? "?");
        }

        private void OnSiegeCompleted(Settlement settlement, MobileParty attackerParty, bool isWin, MapEvent.BattleTypes battleType)
        {
            if (!settlement.IsTown && !settlement.IsCastle) return;

            var attackerLeader = attackerParty?.LeaderHero?.Name?.ToString();
            var attackerKingdom = attackerParty?.MapFaction?.Name?.ToString();

            var attackers = 0;
            var defenders = 0;
            if (_siegeStartTroops.TryGetValue(settlement, out var counts))
            {
                attackers = counts.Attackers;
                defenders = counts.Defenders;
                // 攻城部队已被击败/解散时 LeaderParty 为空，用开始时的名号兜底（修复史料中的 "?"）
                if (string.IsNullOrEmpty(attackerLeader)) attackerLeader = counts.Leader;
                if (string.IsNullOrEmpty(attackerKingdom)) attackerKingdom = counts.Kingdom;
            }
            _siegeStartTroops.Remove(settlement);

            // 名号兜底（不消耗缓存）：即使 _siegeStartTroops 已被 OnSiegeEnded 先消耗，仍能取到攻城名号
            if (string.IsNullOrEmpty(attackerLeader) || string.IsNullOrEmpty(attackerKingdom))
            {
                if (_siegeActorCache.TryGetValue(settlement, out var actor))
                {
                    attackerLeader ??= actor.Leader;
                    attackerKingdom ??= actor.Kingdom;
                }
            }

            attackerLeader ??= "?";
            attackerKingdom ??= "?";

            if (isWin)
            {
                var summary = attackers > 0
                    ? $"{attackerKingdom}的{attackerLeader}率{attackers}人攻克{settlement.Name}，守军{defenders}人"
                    : $"{attackerKingdom}的{attackerLeader}攻克{settlement.Name}";
                _recentSiegeCaptures.Add(settlement);
                RecordEvent("settlement_captured", summary);
                RecordMerit(attackerKingdom, attackerLeader, "攻克", settlement.Name?.ToString() ?? "?");
            }
            else
            {
                RecordEvent("siege_failed", $"{attackerKingdom}的{attackerLeader}围攻{settlement.Name}失败，攻城部队被击败");
                RecordMerit(attackerKingdom, attackerLeader, "攻城失利", settlement.Name?.ToString() ?? "?");
            }
        }

        /// <summary>
        /// 野战/解围野战入史：围攻全程由 siege_* 事件记录，这里补上"野外会战"——战场决胜不伴随攻城时
        /// 原史料缺失（战争叙事"围攻单边"）。仅记够得上"大会战"门槛的野战（双方总兵力≥FieldBattleMinTroops），
        /// 小规模遭遇战不刷屏。解围野战被击败时与 siege_abandoned 互补：先战而后围解。
        /// </summary>
        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (mapEvent.EventType != MapEvent.BattleTypes.FieldBattle
                && mapEvent.EventType != MapEvent.BattleTypes.SiegeOutside) return;

            var attacker = mapEvent.AttackerSide;
            var defender = mapEvent.DefenderSide;
            if (attacker == null || defender == null) return;

            var attackerTroops = attacker.HealthyTroopCountAtMapEventStart;
            var defenderTroops = defender.HealthyTroopCountAtMapEventStart;
            if (attackerTroops + defenderTroops < FieldBattleMinTroops) return;

            var attackerLeader = attacker.LeaderParty?.LeaderHero?.Name?.ToString();
            var defenderLeader = defender.LeaderParty?.LeaderHero?.Name?.ToString();
            if (string.IsNullOrEmpty(attackerLeader) || string.IsNullOrEmpty(defenderLeader)) return;

            var attackerKingdom = attacker.MapFaction?.Name?.ToString() ?? "未知势力";
            var defenderKingdom = defender.MapFaction?.Name?.ToString() ?? "未知势力";

            var winner = mapEvent.Winner;
            string resultPart;
            if (winner == null)
                resultPart = "，胜负未分"; // 撤退/中途结束（BattleState=None）不可强行分胜负
            else if (winner.MissionSide == BattleSideEnum.Attacker)
                resultPart = $"，{attackerKingdom}的{attackerLeader}获胜";
            else
                resultPart = $"，{defenderKingdom}的{defenderLeader}获胜";

            var battleKind = mapEvent.EventType == MapEvent.BattleTypes.SiegeOutside ? "解围野战" : "野战";
            var location = mapEvent.MapEventSettlement?.Name?.ToString();
            var where = string.IsNullOrEmpty(location) ? "" : $"于{location}附近";
            var casualties = attacker.TroopCasualties + defender.TroopCasualties;

            var summary = $"{attackerKingdom}的{attackerLeader}率{attackerTroops}人与{defenderKingdom}的{defenderLeader}所部{defenderTroops}人{where}展开{battleKind}{resultPart}，双方共损失约{casualties}人";
            RecordEvent("battle_fought", summary);
        }

        private void OnSiegeEnded(SiegeEvent siegeEvent)
        {
            var settlement = siegeEvent.BesiegedSettlement;
            if (settlement == null || (!settlement.IsTown && !settlement.IsCastle)) return;

            if (!_siegeStartTroops.TryGetValue(settlement, out var info)) return;
            _siegeStartTroops.Remove(settlement);

            var attackerParty = siegeEvent.BesiegerCamp.LeaderParty;
            var attackerLeader = attackerParty?.LeaderHero?.Name?.ToString();
            var attackerKingdom = attackerParty?.MapFaction?.Name?.ToString();
            if (string.IsNullOrEmpty(attackerLeader)) attackerLeader = info.Leader;
            if (string.IsNullOrEmpty(attackerKingdom)) attackerKingdom = info.Kingdom;
            // 名号兜底（不消耗缓存）：即使 _siegeStartTroops 已被 OnSiegeCompleted 先消耗，仍能取到攻城名号
            if (string.IsNullOrEmpty(attackerLeader) || string.IsNullOrEmpty(attackerKingdom))
            {
                if (_siegeActorCache.TryGetValue(settlement, out var actor))
                {
                    attackerLeader ??= actor.Leader;
                    attackerKingdom ??= actor.Kingdom;
                }
            }
            attackerLeader ??= "?";
            attackerKingdom ??= "?";
            RecordEvent("siege_abandoned", $"{attackerKingdom}的{attackerLeader}放弃了对{settlement.Name}的围攻");
        }

        private void OnKingdomDestroyed(Kingdom kingdom)
        {
            // 灭国即尽废其盟约/贸易协定：清掉该国参与的到期日志与观察项，防止之后被误判为"期满而罢"（灭国由本事件叙事）。
            foreach (var other in Kingdom.All)
            {
                if (other == kingdom || other.IsEliminated) continue;
                DiplomacyService.ClearAgreementTracking("盟约", kingdom, other);
                DiplomacyService.ClearAgreementTracking("贸易", kingdom, other);
            }

            var name = kingdom.Name?.ToString() ?? "未知王国";
            RecordEvent("kingdom_destroyed", $"{name}灭亡");

            AgentScheduler.QueueSpecialChronicle($"王国灭亡：{name}。");
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

            // 传记摘要带上受害者编号（StringId），供史官用 query_character 精确查询——重名者多，仅凭姓名会张冠李戴
            var idTag = string.IsNullOrEmpty(victim.StringId) ? "" : $"（{victim.StringId}）";

            if (victim.Clan?.Kingdom?.RulingClan?.Leader == victim)
            {
                AgentScheduler.QueueSpecialChronicle($"重要人物之死：{victim.Clan.Kingdom.Name}统治者 {name}{idTag}{cause}。");
            }
            else if (victim.Clan?.Leader == victim)
            {
                AgentScheduler.QueueSpecialChronicle($"重要人物之死：{clan}族长 {name}{idTag}{cause}。");
            }
            else if (victim.Clan != null && MySettings.Instance?.BiographyAllNobles != false)
            {
                AgentScheduler.QueueSpecialChronicle($"重要人物之死：{clan}成员 {name}{idTag}{cause}。");
            }
            else if (victim == Hero.MainHero)
            {
                AgentScheduler.QueueSpecialChronicle($"重要人物之死：冒险者 {name}{idTag}{cause}，一段传奇就此落幕。");
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
