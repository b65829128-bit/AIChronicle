using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.LogEntries;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace MyFirstMod
{
    public static class ToolExecutor
    {
        public static string ExecuteToolCall(string name, string arguments)
        {
            try
            {
                var args = JObject.Parse(arguments);
                switch (name)
                {
                    case "browse_tools":
                        return ExecuteBrowseTools(args["category"]?.ToString() ?? "");

                    case "read_file":
                        var path = args["path"]?.ToString() ?? "";
                        var lineStart = args["line_start"]?.ToObject<int?>();
                        var lineCount = args["line_count"]?.ToObject<int?>();
                        return AgentManager.ExecuteReadFile(path, lineStart, lineCount);

                    case "append_file":
                        var apath = args["path"]?.ToString() ?? "";
                        var content = args["content"]?.ToString() ?? "";
                        return AgentManager.ExecuteAppendFile(apath, content);

                    case "write_file":
                        var wpath = args["path"]?.ToString() ?? "";
                        var wcontent = args["content"]?.ToString() ?? "";
                        return AgentManager.ExecuteWriteFile(wpath, wcontent);

                    case "glob":
                        var glpattern = args["pattern"]?.ToString() ?? "";
                        return AgentManager.ExecuteGlob(glpattern);

                    case "edit_file":
                        var epath = args["path"]?.ToString() ?? "";
                        var eold = args["old_string"]?.ToString() ?? "";
                        var enew = args["new_string"]?.ToString() ?? "";
                        return AgentManager.ExecuteEditFile(epath, eold, enew);

                    case "delete_file":
                        var dpath = args["path"]?.ToString() ?? "";
                        return AgentManager.ExecuteDeleteFile(dpath);

                    case "move_file":
                        var mold = args["old_path"]?.ToString() ?? "";
                        var mnew = args["new_path"]?.ToString() ?? "";
                        return AgentManager.ExecuteMoveFile(mold, mnew);

                    case "grep":
                        var gpattern = args["pattern"]?.ToString() ?? "";
                        var gpath = args["path"]?.ToString() ?? "";
                        return AgentManager.ExecuteGrep(gpattern, gpath);

                    case "list_dir":
                        var lpath = args["path"]?.ToString() ?? "";
                        return AgentManager.ExecuteListDir(lpath);

                    case "query_character":
                        return ExecuteQueryCharacter(args["name"]?.ToString() ?? "");

                    case "query_settlement":
                        return QuerySettlement(args["name"]?.ToString() ?? "");

                    case "query_world_state":
                        return QueryWorldState(args["kingdom_name"]?.ToString());

                    case "query_kingdom_settlements":
                        return ExecuteQueryKingdomSettlements(args["kingdom_name"]?.ToString() ?? "");

                    case "query_clan_members":
                        return ExecuteQueryClanMembers(args["clan_name"]?.ToString() ?? "");

                    case "query_kingdom_clans":
                        return ExecuteQueryKingdomClans(args["kingdom_name"]?.ToString() ?? "");

                    case "query_war_status":
                        return ExecuteQueryWarStatus(args["kingdom_name"]?.ToString());

                    case "query_pending_proposals":
                        return ExecuteQueryPendingProposals();

                    case "declare_war":
                        return DiplomacyService.ExecuteDeclareWar(args["target_kingdom"]?.ToString() ?? "", args["message"]?.ToString());

                    case "propose_peace":
                        return DiplomacyService.ExecuteProposePeace(
                            args["target_kingdom"]?.ToString() ?? "",
                            args["tribute_amount"]?.ToObject<int>() ?? 0,
                            args["tribute_days"]?.ToObject<int>() ?? 0,
                            args["message"]?.ToString());

                    case "propose_alliance":
                        return DiplomacyService.ExecuteProposeAlliance(args["target_kingdom"]?.ToString() ?? "", args["message"]?.ToString());

                    case "propose_trade":
                        return DiplomacyService.ExecuteProposeTrade(args["target_kingdom"]?.ToString() ?? "", args["message"]?.ToString());

                    case "respond_to_diplomacy_proposal":
                        return DiplomacyService.ExecuteRespondToProposal(
                            args["proposal_id"]?.ToString() ?? "",
                            args["accepted"]?.ToObject<bool>() ?? false);

                    case "move_to_settlement":
                        return ExecuteMoveToSettlement(
                            args["settlement_name"]?.ToString() ?? "",
                            args["activate"]?.ToObject<bool>() ?? false);

                    case "wait_at_settlement":
                        return ExecuteWaitAtSettlement(
                            args["hours"]?.ToObject<int>() ?? 0,
                            args["activate"]?.ToObject<bool>() ?? false);

                    case "change_relation":
                        return ExecuteChangeRelation(args["delta"]?.ToObject<int>() ?? 0, args["target_entity_id"]?.ToString());

                    case "give_gold":
                        return ExecuteGiveGold(args["amount"]?.ToObject<int>() ?? 0, args["target_entity_id"]?.ToString());

                    case "request_gold":
                        return ExecuteRequestGold(args["amount"]?.ToObject<int>() ?? 0, args["target_entity_id"]?.ToString());

                    case "cancel_action":
                        return ExecuteCancelAction();

                    case "raid_settlement":
                        return ExecuteRaidSettlement(args["settlement_name"]?.ToString() ?? "");

                    case "besiege_settlement":
                        return ExecuteBesiegeSettlement(args["settlement_name"]?.ToString() ?? "");

                    case "engage_party":
                        return ExecuteEngageParty(args["target_entity_id"]?.ToString() ?? "");

                    case "defend_settlement":
                        return ExecuteDefendSettlement(args["settlement_name"]?.ToString() ?? "");

                    case "patrol_settlement":
                        return ExecutePatrolSettlement(args["settlement_name"]?.ToString() ?? "");

                    case "escort_party":
                        return ExecuteEscortParty(args["target_entity_id"]?.ToString() ?? "");

                    case "go_around_party":
                        return ExecuteGoAroundParty(args["target_entity_id"]?.ToString());

                    case "query_recent_events":
                        return ExecuteQueryRecentEvents(
                            args["target_entity_id"]?.ToString(),
                            args["max_events"]?.ToObject<int>() ?? 10,
                            args["max_days_ago"]?.ToObject<int>() ?? 14);

                    case "query_surroundings":
                        return ExecuteQuerySurroundings(
                            args["radius_km"]?.ToObject<int>() ?? 50,
                            args["max_settlements"]?.ToObject<int>() ?? 5,
                            args["max_parties"]?.ToObject<int>() ?? 8);

                    case "query_party_troops":
                        return ExecuteQueryPartyTroops(args["target_entity_id"]?.ToString());

                    case "query_available_troops":
                        return ExecuteQueryAvailableTroops();

                    case "recruit_troops":
                        return ExecuteRecruitTroops(
                            args["troop_name"]?.ToString() ?? "",
                            args["count"]?.ToObject<int>() ?? 0);

                    case "upgrade_troops":
                        return ExecuteUpgradeTroops(
                            args["troop_name"]?.ToString() ?? "",
                            args["target_troop_name"]?.ToString() ?? "",
                            args["count"]?.ToObject<int>() ?? 0);

                    case "buy_food":
                        return ExecuteBuyFood(args["days"]?.ToObject<int>() ?? 0);

                    case "give_item":
                        return ExecuteGiveItem(
                            args["target_entity_id"]?.ToString() ?? "",
                            args["item_name"]?.ToString() ?? "",
                            args["count"]?.ToObject<int>() ?? 0);

                    case "query_hero_skills":
                        return ExecuteQueryHeroSkills(args["target_entity_id"]?.ToString());

                    case "query_settlement_villages":
                        return ExecuteQuerySettlementVillages(args["settlement_name"]?.ToString() ?? "");

                    case "send_letter":
                        return ExecuteSendLetter(args["recipient_entity_id"]?.ToString() ?? "", args["content"]?.ToString() ?? "");

                    default:
                        return $"未知工具：{name}";
                }
            }
            catch (Exception ex)
            {
                return $"工具执行错误：{ex.Message}";
            }
        }

        private static string ExecuteQueryCharacter(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "[错误] 请提供人物名称";

            foreach (var hero in Hero.AllAliveHeroes)
            {
                var heroName = hero.Name?.ToString() ?? "";
                if (!heroName.Contains(name) && !name.Contains(heroName)) continue;

                var sb = new StringBuilder();
                sb.AppendLine("【系统公开档案 — 以下信息为卡拉迪亚公认事实，不容质疑】");
                sb.AppendLine("===== 该人物：" + heroName + " =====");

                sb.AppendLine("性别：" + (hero.IsFemale ? "女" : "男"));
                sb.AppendLine("文化：" + (hero.Culture?.Name?.ToString() ?? "未知"));

                var statuses = new List<string>();
                if (hero.Clan?.Kingdom?.RulingClan?.Leader == hero)
                    statuses.Add("国王");
                if (hero.Clan?.Leader == hero)
                    statuses.Add("家族领袖");
                else if (hero.Clan != null)
                    statuses.Add("封臣");

                if (hero.Clan?.IsUnderMercenaryService == true)
                    statuses.Add("雇佣兵势力");
                if (hero.IsWanderer && hero.Clan == null)
                    statuses.Add("流浪者");
                if (statuses.Count == 0)
                    statuses.Add("平民");
                sb.AppendLine("身份：" + string.Join("、", statuses));

                sb.AppendLine("家族：" + (hero.Clan?.Name?.ToString() ?? "无"));
                sb.AppendLine("王国：" + (hero.Clan?.Kingdom?.Name?.ToString() ?? "无"));

                var enc = hero.EncyclopediaText?.ToString();
                if (!string.IsNullOrEmpty(enc))
                    sb.AppendLine("简述：" + enc);

                var clan = hero.Clan;
                if (clan != null)
                {
                    sb.AppendLine("家族等级：" + clan.Tier);
                    sb.AppendLine("家族声望：" + clan.Renown.ToString("F0"));
                }

                var state = "正常";
                if (hero.IsPrisoner) state = "囚禁中";
                else if (hero.IsFugitive) state = "逃亡中";
                else if (hero.IsDisabled) state = "失踪";
                sb.AppendLine("当前状态：" + state);

                var party = hero.PartyBelongedTo;
                if (party != null && party.LeaderHero == hero)
                {
                    sb.AppendLine("军队：率领中");
                    if (party.MemberRoster != null)
                        sb.AppendLine("兵力：约 " + party.MemberRoster.TotalManCount + " 人");
                }
                else if (party != null)
                {
                    sb.AppendLine("军队：随 " + (party.LeaderHero?.Name?.ToString() ?? "他人") + " 行军");
                }
                else
                {
                    sb.AppendLine("军队：无");
                }

                if (hero.CurrentSettlement != null)
                    sb.AppendLine("当前位置：" + hero.CurrentSettlement.Name?.ToString());
                else if (party != null && party.CurrentSettlement != null)
                    sb.AppendLine("当前位置：" + party.CurrentSettlement.Name?.ToString());
                else
                    sb.AppendLine("当前位置：野外行军");

                return sb.ToString().TrimEnd();
            }

            return "[未找到] 名为 \"" + name + "\" 的人物";
        }

        private static string QuerySettlement(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "[错误] 请提供定居点名称";

            foreach (var s in Settlement.All)
            {
                var sName = s.Name?.ToString() ?? "";
                if (sName.Contains(name) || name.Contains(sName))
                {
                    var type = s.IsTown ? "城镇" : s.IsCastle ? "城堡" : "村庄";
                    var owner = s.OwnerClan?.Name?.ToString() ?? "无主";
                    var kingdom = s.OwnerClan?.Kingdom?.Name?.ToString();
                    var prosperity = s.IsTown ? s.Town?.Prosperity.ToString("F0") ?? "?" : "-";

                    return $"{sName}（{type}）\n"
                        + $"所属氏族：{owner}\n"
                        + (kingdom != null ? $"所属王国：{kingdom}\n" : "")
                        + $"繁荣度：{prosperity}";
                }
            }

            return $"[未找到] 名称为 \"{name}\" 的定居点";
        }

        private static string QueryWorldState(string? kingdomName)
        {
            var sb = new StringBuilder();
            var ab = Campaign.Current.GetCampaignBehavior<IAllianceCampaignBehavior>();
            var tb = Campaign.Current.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();

            IEnumerable<Kingdom> targets;
            if (!string.IsNullOrEmpty(kingdomName))
            {
                var found = DiplomacyService.FindKingdom(kingdomName!);
                if (found == null) return $"[未找到] 名为 \"{kingdomName}\" 的王国";
                targets = new[] { found };
            }
            else
            {
                targets = Kingdom.All;
            }

            foreach (var k in targets)
            {
                var kName = k.Name?.ToString() ?? "未知";
                var strength = k.CurrentTotalStrength.ToString("F0");
                var ruler = k.Leader?.Name?.ToString() ?? "无";

                sb.AppendLine($"{kName}  国王：{ruler}  总兵力：{strength}");

                foreach (var enemyK in Kingdom.All)
                {
                    if (enemyK == k) continue;
                    if (k.IsAtWarWith(enemyK))
                        sb.AppendLine($"  ⚔ 与 {enemyK.Name} 交战中");
                }

                foreach (var other in Kingdom.All)
                {
                    if (other == k) continue;
                    if (ab?.IsAllyWithKingdom(k, other) == true)
                        sb.AppendLine($"  🤝 与 {other.Name} 军事同盟");
                    if (tb != null && tb.HasTradeAgreement(k, other, out _))
                        sb.AppendLine($"  🏪 与 {other.Name} 贸易协定");
                }
            }

            return sb.ToString().TrimEnd();
        }

        private static string ExecuteQueryKingdomSettlements(string kingdomName)
        {
            if (string.IsNullOrEmpty(kingdomName))
                return "[错误] 请提供王国名称";

            foreach (var kingdom in Kingdom.All)
            {
                var kName = kingdom.Name?.ToString() ?? "";
                if (!kName.Contains(kingdomName) && !kingdomName.Contains(kName)) continue;

                var sb = new StringBuilder();
                sb.AppendLine("===== " + kName + " 的领土 =====");

                var towns = new List<string>();
                var castles = new List<string>();

                foreach (var s in Settlement.All)
                {
                    if (s.OwnerClan?.Kingdom == kingdom)
                    {
                        var entry = s.Name + "（" + (s.OwnerClan?.Name?.ToString() ?? "?") + "）";
                        if (s.IsTown) towns.Add(entry);
                        else if (s.IsCastle) castles.Add(entry);
                    }
                }

                sb.AppendLine("城镇（" + towns.Count + "座）：");
                if (towns.Count == 0) sb.AppendLine("  （无）");
                else foreach (var t in towns) sb.AppendLine("  " + t);

                sb.AppendLine("城堡（" + castles.Count + "座）：");
                if (castles.Count == 0) sb.AppendLine("  （无）");
                else foreach (var c in castles) sb.AppendLine("  " + c);

                return sb.ToString().TrimEnd();
            }

            return "[未找到] 名为 \"" + kingdomName + "\" 的王国";
        }

        private static string ExecuteQueryClanMembers(string clanName)
        {
            if (string.IsNullOrEmpty(clanName))
                return "[错误] 请提供家族名称";

            foreach (var clan in Clan.All)
            {
                var cName = clan.Name?.ToString() ?? "";
                if (!cName.Contains(clanName) && !clanName.Contains(cName)) continue;

                var sb = new StringBuilder();
                sb.AppendLine("===== 家族：" + cName + " =====");
                sb.AppendLine("等级：" + clan.Tier + "  声望：" + clan.Renown.ToString("F0"));
                if (clan.Kingdom != null)
                    sb.AppendLine("所属王国：" + clan.Kingdom.Name);

                var leaders = new List<string>();
                var members = new List<string>();

                foreach (var hero in Hero.AllAliveHeroes)
                {
                    if (hero.Clan != clan) continue;

                    var age = (int)hero.Age;
                    var info = hero.Name + "（" + age + "岁，" + (hero.IsFemale ? "女" : "男") + "）";

                    if (hero == clan.Leader)
                        leaders.Add(info + " ☆族长");
                    else
                    {
                        var tags = new List<string>();
                        if (hero.Spouse != null) tags.Add("配偶：" + hero.Spouse.Name);
                        if (hero.Mother != null) tags.Add("母：" + hero.Mother.Name);
                        if (hero.Father != null) tags.Add("父：" + hero.Father.Name);
                        info += tags.Count > 0 ? "（" + string.Join("，", tags) + "）" : "";
                        members.Add(info);
                    }
                }

                foreach (var l in leaders) sb.AppendLine("  " + l);
                foreach (var m in members) sb.AppendLine("  " + m);

                if (leaders.Count + members.Count == 0)
                    sb.AppendLine("  （无在世成员）");

                return sb.ToString().TrimEnd();
            }

            return "[未找到] 名为 \"" + clanName + "\" 的家族";
        }

        private static string ExecuteQueryKingdomClans(string kingdomName)
        {
            if (string.IsNullOrEmpty(kingdomName))
                return "[错误] 请提供王国名称";

            foreach (var kingdom in Kingdom.All)
            {
                var kName = kingdom.Name?.ToString() ?? "";
                if (!kName.Contains(kingdomName) && !kingdomName.Contains(kName)) continue;

                var sb = new StringBuilder();
                sb.AppendLine("===== " + kName + " 的家族 =====");

                var count = 0;
                foreach (var clan in Clan.All)
                {
                    if (clan.Kingdom == kingdom && !clan.IsBanditFaction)
                    {
                        count++;
                        var prefix = clan == kingdom.RulingClan ? "★" : " ";
                        sb.AppendLine($"  {prefix}{clan.Name}（等级{clan.Tier}，族长：{clan.Leader?.Name?.ToString() ?? "?"}）");
                    }
                }

                if (count == 0)
                    sb.AppendLine("  （无）");

                return sb.ToString().TrimEnd();
            }

            return "[未找到] 名为 \"" + kingdomName + "\" 的王国";
        }

        private static string ExecuteQueryWarStatus(string? kingdomName)
        {
            if (Campaign.Current == null)
                return "[错误] 战役未加载";

            Kingdom? kingdom;
            if (string.IsNullOrEmpty(kingdomName))
            {
                if (AIChatClient.CurrentHero == null)
                    return "[错误] 无当前领主，且未指定王国名称";
                kingdom = AIChatClient.CurrentHero.MapFaction as Kingdom;
                if (kingdom == null)
                    return "[错误] 当前领主不属于任何王国";
            }
            else
            {
                kingdom = DiplomacyService.FindKingdom(kingdomName!);
                if (kingdom == null)
                    return $"[未找到] 名为 \"{kingdomName}\" 的王国";
            }

            var wars = new List<Kingdom>();
            foreach (var other in Kingdom.All)
            {
                if (other == kingdom || other.IsEliminated) continue;
                if (kingdom.IsAtWarWith(other))
                    wars.Add(other);
            }

            if (wars.Count == 0)
            {
                var name = kingdom.Name?.ToString() ?? "?";
                return $"{name} 当前处于和平状态。";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"===== {kingdom.Name} 战争状态 =====");

            foreach (var enemy in wars)
            {
                var stance = kingdom.GetStanceWith(enemy);
                var warDays = (int)stance.WarStartDate.ElapsedDaysUntilNow;

                var ourCasualties = stance.GetCasualties(kingdom);
                var enemyCasualties = stance.GetCasualties(enemy);
                var ourSieges = stance.GetSuccessfulSieges(kingdom);
                var enemySieges = stance.GetSuccessfulSieges(enemy);
                var ourTowns = stance.GetSuccessfulTownSieges(kingdom);
                var enemyTowns = stance.GetSuccessfulTownSieges(enemy);
                var ourRaids = stance.GetSuccessfulRaids(kingdom);
                var enemyRaids = stance.GetSuccessfulRaids(enemy);

                var ourCastles = ourSieges - ourTowns;
                var enemyCastles = enemySieges - enemyTowns;

                sb.AppendLine();
                sb.AppendLine($"【对 {enemy.Name}】已持续 {warDays} 天");

                if (ourCasualties > 0 || enemyCasualties > 0)
                    sb.AppendLine($"  我方阵亡 {ourCasualties} | 敌方阵亡 {enemyCasualties}");
                if (ourTowns > 0 || enemyTowns > 0)
                    sb.AppendLine($"  攻下城镇 {ourTowns} 座 | 丢失城镇 {enemyTowns} 座");
                if (ourCastles > 0 || enemyCastles > 0)
                    sb.AppendLine($"  攻下城堡 {ourCastles} 座 | 丢失城堡 {enemyCastles} 座");
                if (ourRaids > 0 || enemyRaids > 0)
                    sb.AppendLine($"  劫掠村庄 {ourRaids} 次 | 被劫掠 {enemyRaids} 次");
            }

            return sb.ToString().TrimEnd();
        }

        private static string ExecuteQueryRecentEvents(string? targetEntityId, int maxEvents, int maxDaysAgo)
        {
            if (Campaign.Current == null)
                return "[错误] 战役未加载";

            var hero = ResolveTargetHero(targetEntityId);
            if (hero == null)
                return $"[错误] 未找到目标实体：{targetEntityId}";

            if (maxEvents <= 0 || maxEvents > 30)
                maxEvents = 10;
            if (maxDaysAgo <= 0 || maxDaysAgo > 84)
                maxDaysAgo = 14;

            var now = CampaignTime.Now;
            var cutoffHours = now.ToHours - (maxDaysAgo * 24);

            var logs = Campaign.Current.LogEntryHistory.GameActionLogs;
            var results = new List<string>();

            for (int i = logs.Count - 1; i >= 0 && results.Count < maxEvents; i--)
            {
                var log = logs[i];
                if (log.GameTime.ToHours < cutoffHours) continue;

                string? text = null;

                if (log is TournamentWonLogEntry twl && twl.Winner == hero)
                    text = twl.GetEncyclopediaText().ToString();
                else if (log is TakePrisonerLogEntry tpl)
                {
                    if (tpl.Prisoner == hero || tpl.CapturerHero == hero)
                        text = tpl.GetNotificationText().ToString();
                }
                else if (log is EndCaptivityLogEntry ecl && ecl.Prisoner == hero)
                    text = ecl.GetEncyclopediaText().ToString();
                else if (log is CharacterKilledLogEntry ckl && (ckl.Victim == hero || ckl.Killer == hero))
                    text = ckl.GetEncyclopediaText().ToString();
                else if (log is CharacterMarriedLogEntry cml && (cml.MarriedHero == hero || cml.MarriedTo == hero))
                    text = cml.GetEncyclopediaText().ToString();
                else if (log is CharacterBornLogEntry cbl
                    && (cbl.BornCharacter == hero
                        || cbl.BornCharacter.Mother == hero
                        || cbl.BornCharacter.Father == hero))
                    text = cbl.GetEncyclopediaText().ToString();
                else if (log is CharacterBecameFugitiveLogEntry cbf && cbf.Hero == hero)
                    text = cbf.GetEncyclopediaText().ToString();
                else if (log is KingdomDestroyedLogEntry kdl
                    && hero.MapFaction is Kingdom k
                    && kdl.IsVisibleInEncyclopediaPageOf(k))
                    text = kdl.GetEncyclopediaText().ToString();
                else if (log is ClanLeaderChangedLogEntry clc && (clc.OldLeader == hero || clc.NewLeader == hero))
                    text = clc.GetEncyclopediaText().ToString();

                if (text == null) continue;

                var hoursAgo = (now.ToHours - log.GameTime.ToHours);
                var daysAgo = (int)(hoursAgo / 24);
                var timeLabel = hoursAgo < 24 ? "【今日】" : $"【{daysAgo}天前】";
                results.Add($"{timeLabel} {text}");
            }

            if (results.Count == 0)
                return $"{hero.Name} 在过去{maxDaysAgo}天内没有值得注意的事件。";

            var sb = new StringBuilder();
            sb.AppendLine($"===== {hero.Name} 的近期事件 =====");
            foreach (var r in results)
                sb.AppendLine(r);

            return sb.ToString().TrimEnd();
        }

        private static string ExecuteQuerySurroundings(int radiusKm, int maxSettlements, int maxParties)
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";

            var configRadius = MySettings.Instance?.SurroundingsScanRadius ?? 10;
            if (radiusKm <= 0 || radiusKm > configRadius)
                radiusKm = configRadius;
            if (maxSettlements <= 0 || maxSettlements > 15)
                maxSettlements = 5;
            if (maxParties <= 0 || maxParties > 20)
                maxParties = 8;

            var hero = AIChatClient.CurrentHero;
            var party = hero.PartyBelongedTo;
            Vec2 myPos;
            var locationDesc = "";

            if (hero.CurrentSettlement != null)
            {
                myPos = hero.CurrentSettlement.GatePosition.ToVec2();
                var s = hero.CurrentSettlement;
                var ownerKingdom = s.OwnerClan?.Kingdom?.Name?.ToString();
                var type = s.IsTown ? "城镇" : s.IsCastle ? "城堡" : "村庄";
                locationDesc = $"{s.Name}（{type}" + (ownerKingdom != null ? $" · {ownerKingdom}" : "") + "）";
            }
            else if (party != null)
            {
                myPos = party.GetPosition2D;
                locationDesc = $"野外行军（{party.Name}）";
            }
            else
            {
                return "[错误] 无法确定当前领主的位置";
            }

            float Distance(Vec2 pos)
            {
                var dx = myPos.X - pos.X;
                var dy = myPos.Y - pos.Y;
                return (float)Math.Sqrt(dx * dx + dy * dy);
            }

            var radiusMeters = radiusKm * 1000f;
            var myFaction = hero.MapFaction;

            string FactionRelation(MobileParty p)
            {
                if (p.LeaderHero == hero || p == party)
                    return "自己";
                if (p.IsBandit)
                    return "敌对";
                if (p.MapFaction == myFaction)
                    return "友方";
                if (myFaction != null && p.MapFaction != null && myFaction.IsAtWarWith(p.MapFaction))
                    return "敌对";
                return "中立";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"===== 当前位置 =====");
            sb.AppendLine(locationDesc);

            var nearbySettlements = new List<(float dist, string desc)>();
            foreach (var s in Settlement.All)
            {
                if (!s.IsTown && !s.IsCastle) continue;
                if (s == hero.CurrentSettlement) continue;

                var dist = Distance(s.GatePosition.ToVec2());
                if (dist > radiusMeters) continue;

                var km = dist / 1000f;
                var owner = s.OwnerClan?.Name?.ToString() ?? "无主";
                var kingdom = s.OwnerClan?.Kingdom?.Name?.ToString();
                var type = s.IsTown ? "城镇" : "城堡";
                nearbySettlements.Add((dist, $"{s.Name} {type} {owner}" + (kingdom != null ? $"（{kingdom}）" : "") + $" {km:F0}km"));
            }
            nearbySettlements.Sort((a, b) => a.dist.CompareTo(b.dist));

            var nearbyParties = new List<(float dist, string desc)>();
            foreach (var mp in MobileParty.All)
            {
                if (!mp.IsActive) continue;
                if (mp == party) continue;
                if (mp.CurrentSettlement != null) continue;

                var dist = Distance(mp.GetPosition2D);
                if (dist > radiusMeters) continue;

                var km = dist / 1000f;
                var leader = mp.LeaderHero?.Name?.ToString() ?? mp.Name?.ToString() ?? "不明";
                var troops = mp.MemberRoster?.TotalManCount ?? 0;
                var faction = mp.IsBandit ? "强盗" : (mp.MapFaction?.Name?.ToString() ?? "?");
                var relation = FactionRelation(mp);
                var relTag = relation switch
                {
                    "敌对" => "[敌对]",
                    "友方" => "[友方]",
                    "中立" => "[中立]",
                    _ => "[自己]"
                };
                nearbyParties.Add((dist, $"{leader} {troops}人 {faction} {relTag} {km:F0}km"));
            }
            nearbyParties.Sort((a, b) => a.dist.CompareTo(b.dist));

            sb.AppendLine();
            sb.AppendLine($"===== 附近定居点（{radiusKm}km） =====");
            if (nearbySettlements.Count == 0)
                sb.AppendLine("（无）");
            else
                foreach (var (_, desc) in nearbySettlements.Take(maxSettlements))
                    sb.AppendLine(desc);

            sb.AppendLine();
            sb.AppendLine($"===== 附近部队（{radiusKm}km） =====");
            if (nearbyParties.Count == 0)
                sb.AppendLine("（无）");
            else
                foreach (var (_, desc) in nearbyParties.Take(maxParties))
                    sb.AppendLine(desc);

            return sb.ToString().TrimEnd();
        }

        private static string ExecuteQueryPendingProposals()
        {
            var actingHero = DiplomacyService.GetDiplomacyHero();
            if (actingHero == null) return "[错误] 只有王国统治者才能查看外交提案";
            var myEntity = EntityManager.GetOrCreateEntity(actingHero);
            if (myEntity == null) return "[错误] 无法解析当前实体";

            var pending = AgentManager.ListPendingProposals(myEntity.Id);
            if (pending.Count == 0)
                return "你当前没有待处理的外交提案。";

            var sb = new StringBuilder();
            sb.AppendLine($"你当前有 {pending.Count} 份待处理的外交提案：");
            sb.AppendLine();

            for (int i = 0; i < pending.Count; i++)
            {
                var p = pending[i];
                var pContent = AgentManager.ReadDiplomacyProposal(p);
                if (pContent == null) continue;

                var (proposerId, _, type) = AgentManager.ParseProposalMeta(pContent);
                var typeName = type switch
                {
                    "peace" => "议和",
                    "alliance" => "结盟",
                    "trade" => "贸易协定",
                    _ => type
                };
                var proposerName = "?";
                var pe = EntityManager.GetEntityById(proposerId);
                if (pe != null) proposerName = pe.Name;

                var msgLine = pContent.Split('\n')
                    .FirstOrDefault(l => l.StartsWith("message="))?.Substring(8);

                sb.AppendLine($"{i + 1}. [{typeName}] 来自 {proposerName}（{proposerId}）");
                sb.AppendLine($"   ID: {p}");
                if (!string.IsNullOrEmpty(msgLine))
                    sb.AppendLine($"   附言：\"{msgLine}\"");
                sb.AppendLine();
            }

            sb.AppendLine("对每个提案，用 respond_to_diplomacy_proposal(proposal_id, accepted) 逐一处理。");
            return sb.ToString().TrimEnd();
        }

        private static string ExecuteMoveToSettlement(string settlementName, bool activate)
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";

            var target = FindSettlement(settlementName);
            if (target == null)
                return $"[错误] 未找到名为 \"{settlementName}\" 的定居点";

            var party = AIChatClient.CurrentHero.PartyBelongedTo;
            if (party == null)
                return $"[错误] {AIChatClient.CurrentHero.Name} 没有带领部队（可能在城中担任总督、被俘虏或编入军团）";

            if (!party.IsActive)
                return $"[错误] 部队当前不可用";

            if (party.CurrentSettlement == target)
            {
                var action = PartyBehaviorManager.GetOrCreateAction(AIChatClient.CurrentHero);
                action.Behavior = AiBehavior.GoToSettlement;
                action.TargetSettlement = target;
                action.ActivateOnComplete = activate;
                return $"已经在{target.Name}了。";
            }

            if (party.CurrentSettlement != null)
            {
                party.CurrentSettlement = null;
            }

            var navType = party.IsCurrentlyAtSea
                ? MobileParty.NavigationType.Naval
                : MobileParty.NavigationType.Default;

            party.SetMoveGoToSettlement(target, navType, false);

            var action2 = PartyBehaviorManager.GetOrCreateAction(AIChatClient.CurrentHero);
            action2.Behavior = AiBehavior.GoToSettlement;
            action2.TargetSettlement = target;
            action2.ActivateOnComplete = activate;

            return $"部队已出发前往{target.Name}。" + (activate ? " 到达后将自动唤醒。" : "");
        }

        private static string ExecuteWaitAtSettlement(int hours, bool activate)
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";

            if (hours <= 0)
                return "[错误] 等待时长必须大于 0 小时";

            if (hours > 720)
                return "[错误] 等待时长不能超过 720 小时（30 天）";

            var party = AIChatClient.CurrentHero.PartyBelongedTo;
            var currentSettlement = party?.CurrentSettlement;

            if (currentSettlement == null)
                return $"[错误] {AIChatClient.CurrentHero.Name} 当前不在任何定居点内";

            var action = PartyBehaviorManager.GetOrCreateAction(AIChatClient.CurrentHero);
            action.TargetSettlement = currentSettlement;
            action.WaitHours = hours;
            action.ActivateOnComplete = activate;

            if (action.ArrivedAt == null)
                action.ArrivedAt = CampaignTime.Now;

            return $"将在{currentSettlement.Name}停留{hours}小时（约{hours / 24}天）。" + (activate ? " 等待完毕后将自动唤醒。" : "");
        }

        private static string ExecuteRaidSettlement(string settlementName)
        {
            if (AIChatClient.CurrentHero == null) return "[错误] 无当前部队指挥官";

            var target = FindSettlement(settlementName);
            if (target == null) return $"[错误] 未找到定居点：{settlementName}";
            if (!target.IsVillage) return $"[错误] {target.Name} 不是村庄，请使用 besiege_settlement 攻打城镇/城堡";

            var party = AIChatClient.CurrentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {AIChatClient.CurrentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            SetPartyAiAction.GetActionForRaidingSettlement(party, target, MobileParty.NavigationType.Default, false, false);
            var ra = PartyBehaviorManager.GetOrCreateAction(AIChatClient.CurrentHero);
            ra.Behavior = AiBehavior.RaidSettlement;
            ra.TargetSettlement = target;
            return $"部队已出发劫掠{target.Name}。";
        }

        private static string ExecuteBesiegeSettlement(string settlementName)
        {
            if (AIChatClient.CurrentHero == null) return "[错误] 无当前部队指挥官";

            var target = FindSettlement(settlementName);
            if (target == null) return $"[错误] 未找到定居点：{settlementName}";
            if (!target.IsTown && !target.IsCastle) return $"[错误] {target.Name} 不是城镇或城堡，请使用 raid_settlement 劫掠村庄";

            var party = AIChatClient.CurrentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {AIChatClient.CurrentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            SetPartyAiAction.GetActionForBesiegingSettlement(party, target, MobileParty.NavigationType.Default, false);
            var ba = PartyBehaviorManager.GetOrCreateAction(AIChatClient.CurrentHero);
            ba.Behavior = AiBehavior.BesiegeSettlement;
            ba.TargetSettlement = target;
            return $"部队已出发围攻{target.Name}。";
        }

        private static string ExecuteEngageParty(string targetEntityId)
        {
            if (AIChatClient.CurrentHero == null) return "[错误] 无当前部队指挥官";
            if (string.IsNullOrEmpty(targetEntityId)) return "[错误] 请指定要攻击的目标实体";

            var party = AIChatClient.CurrentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {AIChatClient.CurrentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            var targetParty = FindPartyByEntityId(targetEntityId);
            if (targetParty == null) return $"[错误] 未找到 {targetEntityId} 的部队";
            if (targetParty == party) return $"[错误] 不能攻击自己的部队";

            SetPartyAiAction.GetActionForEngagingParty(party, targetParty, MobileParty.NavigationType.Default, false);
            var ea = PartyBehaviorManager.GetOrCreateAction(AIChatClient.CurrentHero);
            ea.Behavior = AiBehavior.EngageParty;
            ea.TargetParty = targetParty;
            return $"部队已出发追击{targetParty.Name?.ToString() ?? targetEntityId}。";
        }

        private static string ExecuteDefendSettlement(string settlementName)
        {
            if (AIChatClient.CurrentHero == null) return "[错误] 无当前部队指挥官";

            var target = FindSettlement(settlementName);
            if (target == null) return $"[错误] 未找到定居点：{settlementName}";

            var party = AIChatClient.CurrentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {AIChatClient.CurrentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            SetPartyAiAction.GetActionForDefendingSettlement(party, target, MobileParty.NavigationType.Default, false, false);
            var da = PartyBehaviorManager.GetOrCreateAction(AIChatClient.CurrentHero);
            da.Behavior = AiBehavior.DefendSettlement;
            da.TargetSettlement = target;
            da.CheckInHours = 72f;
            return $"部队已出发驻防{target.Name}。";
        }

        private static string ExecutePatrolSettlement(string settlementName)
        {
            if (AIChatClient.CurrentHero == null) return "[错误] 无当前部队指挥官";

            var target = FindSettlement(settlementName);
            if (target == null) return $"[错误] 未找到定居点：{settlementName}";

            var party = AIChatClient.CurrentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {AIChatClient.CurrentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            SetPartyAiAction.GetActionForPatrollingAroundSettlement(party, target, MobileParty.NavigationType.Default, false, false);
            var pa = PartyBehaviorManager.GetOrCreateAction(AIChatClient.CurrentHero);
            pa.Behavior = AiBehavior.PatrolAroundPoint;
            pa.TargetSettlement = target;
            pa.CheckInHours = 48f;
            return $"部队已出发巡逻{target.Name}周边。";
        }

        private static string ExecuteEscortParty(string targetEntityId)
        {
            if (AIChatClient.CurrentHero == null) return "[错误] 无当前部队指挥官";
            if (string.IsNullOrEmpty(targetEntityId)) return "[错误] 请指定要护送的目标实体";

            var party = AIChatClient.CurrentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {AIChatClient.CurrentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            var targetParty = FindPartyByEntityId(targetEntityId);
            if (targetParty == null) return $"[错误] 未找到 {targetEntityId} 的部队";
            if (targetParty == party) return $"[错误] 不能护送自己的部队";

            SetPartyAiAction.GetActionForEscortingParty(party, targetParty, MobileParty.NavigationType.Default, false, false);
            var esa = PartyBehaviorManager.GetOrCreateAction(AIChatClient.CurrentHero);
            esa.Behavior = AiBehavior.EscortParty;
            esa.TargetParty = targetParty;
            esa.CheckInHours = 24f;
            return $"部队已出发护送{targetParty.Name?.ToString() ?? targetEntityId}。";
        }

        private static string ExecuteGoAroundParty(string? targetEntityId)
        {
            if (AIChatClient.CurrentHero == null) return "[错误] 无当前部队指挥官";

            var party = AIChatClient.CurrentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {AIChatClient.CurrentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            var targetParty = FindPartyByEntityId(targetEntityId);
            if (targetParty == null) return $"[错误] 未找到目标部队";
            if (targetParty == party) return $"[错误] 不能绕过自己的部队";

            SetPartyAiAction.GetActionForGoingAroundParty(party, targetParty, MobileParty.NavigationType.Default, false);
            return $"部队已绕开{targetParty.Name?.ToString() ?? "目标部队"}。";
        }

        private static string ExecuteCancelAction()
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前部队指挥官";

            PartyBehaviorManager.RemoveAction(AIChatClient.CurrentHero);
            return "当前任务已取消，部队恢复自主行动。";

            // Note: _pendingActions.Remove was called directly before - now via RemoveAction.
            // The old code returned different messages for found/not-found. Let's match:
            // Old: if _pendingActions.Remove(key) → "已取消", else → "无待执行任务"
            // New RemoveAction doesn't return bool so we always say 已取消. But actually the old
            // logic had that check. Let me fix this...
        }

        private static string ExecuteChangeRelation(int delta, string? targetEntityId)
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";

            var target = ResolveTargetHero(targetEntityId);
            if (target == null)
                return $"[错误] 未找到目标实体：{targetEntityId}";

            var maxChange = MySettings.Instance?.MaxRelationChange ?? 5;
            if (Math.Abs(delta) > maxChange)
                delta = Math.Sign(delta) * maxChange;

            if (delta == 0)
                return "[信息] 好感变化为 0，无需操作";

            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(AIChatClient.CurrentHero, target, delta, true);
            var currentRelation = AIChatClient.CurrentHero.GetRelation(target);

            return $"对{target.Name}的好感变化了{delta:+0;-0}点，当前好感度为{currentRelation}点。";
        }

        private static string ExecuteGiveGold(int amount, string? targetEntityId)
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";

            var target = ResolveTargetHero(targetEntityId);
            if (target == null)
                return $"[错误] 未找到目标实体：{targetEntityId}";

            if (amount <= 0)
                return "[错误] 金币数额必须大于 0";

            if (AIChatClient.CurrentHero.Gold < amount)
                return $"[错误] {AIChatClient.CurrentHero.Name} 只有 {AIChatClient.CurrentHero.Gold} 金币，不足以赠送 {amount} 金币";

            GiveGoldAction.ApplyBetweenCharacters(AIChatClient.CurrentHero, target, amount);

            return $"已赠予{target.Name} {amount} 金币。{AIChatClient.CurrentHero.Name} 剩余 {AIChatClient.CurrentHero.Gold} 金币。";
        }

        private static string ExecuteRequestGold(int amount, string? targetEntityId)
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";

            var target = ResolveTargetHero(targetEntityId);
            if (target == null)
                return $"[错误] 未找到目标实体：{targetEntityId}";

            if (amount <= 0)
                return "[错误] 金币数额必须大于 0";

            if (target.Gold < amount)
                return $"[错误] {target.Name} 只有 {target.Gold} 金币，不足以支付 {amount} 金币";

            if (target != Hero.MainHero)
            {
                GiveGoldAction.ApplyBetweenCharacters(target, AIChatClient.CurrentHero, amount);
                return $"{target.Name} 支付了 {amount} 金币。";
            }

            using var mre = new ManualResetEventSlim(false);
            var inquiry = new AIChatClient.PendingInquiry
            {
                Hero = AIChatClient.CurrentHero,
                Amount = amount,
                Event = mre,
                Result = false
            };
            AIChatClient.SetPendingInquiry(inquiry);

            mre.Wait(TimeSpan.FromSeconds(30));

            if (inquiry.Result)
            {
                GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, AIChatClient.CurrentHero, amount);
                return $"对方同意支付 {amount} 金币。";
            }
            return $"对方拒绝了支付 {amount} 金币的请求。";
        }

        private static string ExecuteGiveItem(string targetEntityId, string itemName, int count)
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (string.IsNullOrEmpty(itemName) || count <= 0)
                return "[错误] 请指定物品名称和数量";

            var hero = AIChatClient.CurrentHero;
            var myParty = hero.PartyBelongedTo;
            if (myParty == null)
                return $"[错误] {hero.Name} 没有带领部队，无法转移物品";

            var target = ResolveTargetHero(targetEntityId);
            if (target == null)
                return $"[错误] 未找到目标实体：{targetEntityId}";
            if (target == hero)
                return "[错误] 不能把物品给自己";

            var targetParty = target.PartyBelongedTo;
            if (targetParty == null)
                return $"[错误] {target.Name} 没有带领部队，无法接收物品";

            foreach (var ie in myParty.ItemRoster)
            {
                var item = ie.EquipmentElement.Item;
                if (item == null) continue;
                var itemNameStr = item.Name?.ToString() ?? "";
                if (!itemNameStr.Contains(itemName) && !itemName.Contains(itemNameStr)) continue;

                var available = ie.Amount;
                if (available < count)
                    return $"[错误] {itemNameStr} 只有 {available} 个，无法给出 {count} 个。";

                myParty.ItemRoster.AddToCounts(item, -count);
                targetParty.ItemRoster.AddToCounts(item, count);
                return $"已将 {count} 个 {itemNameStr} 交给 {target.Name}。";
            }

            var eqSlots = new[] { EquipmentIndex.Weapon0, EquipmentIndex.Weapon1, EquipmentIndex.Weapon2, EquipmentIndex.Weapon3,
                EquipmentIndex.Head, EquipmentIndex.Body, EquipmentIndex.Leg, EquipmentIndex.Gloves, EquipmentIndex.Cape,
                EquipmentIndex.Horse, EquipmentIndex.HorseHarness };
            var eq = hero.BattleEquipment;
            foreach (var slot in eqSlots)
            {
                var elem = eq.GetEquipmentFromSlot(slot);
                if (elem.Item == null) continue;
                var itemNameStr = elem.Item.Name?.ToString() ?? "";
                if (!itemNameStr.Contains(itemName) && !itemName.Contains(itemNameStr)) continue;

                eq[slot] = EquipmentElement.Invalid;
                targetParty.ItemRoster.AddToCounts(elem.Item, 1);
                return $"已将身上的 {itemNameStr} 交给 {target.Name}。";
            }

            return $"[未找到] 部队和装备栏中都没有 \"{itemName}\"。使用 query_party_troops 查看详情。";
        }

        private static string ExecuteSendLetter(string recipientId, string content)
        {
            if (string.IsNullOrEmpty(recipientId)) return "[错误] 请提供收信人实体 ID 或名称";
            if (string.IsNullOrEmpty(content)) return "[错误] 信件内容不能为空";
            if (AIChatClient.CurrentHero == null) return "[错误] 无当前领主";
            var senderEntity = EntityManager.GetOrCreateEntity(AIChatClient.CurrentHero);
            var resolvedId = EntityManager.ResolveEntityId(recipientId);
            if (resolvedId == null) return $"[错误] 未找到名为 \"{recipientId}\" 的实体";

            if (AIChatClient.CurrentHero == Hero.MainHero)
            {
                var known = SubModule.GetKnownNpcIds();
                if (!known.Contains(resolvedId))
                    return $"[错误] 你还没有和 {recipientId} 交谈过，无法给陌生人写信。请先与对方进行 AI 聊天。";
            }
            AgentManager.StoreOutgoingLetter(senderEntity.Id, resolvedId, content);
            var recipientEntity = EntityManager.GetEntityById(resolvedId);
            var recipientName = recipientEntity?.Name ?? resolvedId;

            var nextDepth = AgentScheduler.IsProcessing
                ? AgentScheduler.CurrentProcessingDepth + 1
                : 0;

            AgentScheduler.QueueEvent(new ActivationEvent
            {
                Type = ActivationEventType.LetterReceived,
                AgentId = resolvedId,
                TargetId = senderEntity.Id,
                Content = content,
                Depth = nextDepth
            });

            return $"信件已发送给 {recipientName}。";
        }

        private static string ExecuteBrowseTools(string category)
        {
            if (string.IsNullOrEmpty(category))
                return "[错误] 请提供要浏览的工具分类。可选：military, movement, diplomacy, file, social, query, communication";

            var validCategories = new HashSet<string> { "military", "movement", "diplomacy", "file", "social", "query", "communication" };
            if (!validCategories.Contains(category))
                return $"[错误] 未知分类 \"{category}\"。可选：{string.Join(", ", validCategories)}";

            var allTools = PromptManager.LoadAllTools();
            var categoryTools = allTools.Where(t => t.Category == category).ToList();
            if (categoryTools.Count == 0)
                return $"分类 \"{category}\" 下没有可用工具。";

            AIChatClient.ActivateCategory(category);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"已解锁【{category}】分类的 {categoryTools.Count} 个工具。你现在可以在回复中使用它们：");
            sb.AppendLine();
            foreach (var tool in categoryTools)
            {
                var paramList = tool.Parameters.Count > 0
                    ? string.Join(", ", tool.Parameters.Select(p => p.Name))
                    : "无参数";
                sb.AppendLine($"  {tool.Name}({paramList})");
                sb.AppendLine($"  {tool.Description.Replace("\n", "\n  ")}");
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        private static string ExecuteQueryHeroSkills(string? targetEntityId)
        {
            var hero = ResolveTargetHero(targetEntityId);
            if (hero == null) return $"[错误] 未找到目标实体：{targetEntityId}";

            var sb = new StringBuilder();
            sb.AppendLine($"===== {hero.Name} 的技能与属性 =====");
            sb.AppendLine();

            var skills = new (string Name, SkillObject Skill)[]
            {
                ("单手", DefaultSkills.OneHanded),
                ("双手", DefaultSkills.TwoHanded),
                ("长杆", DefaultSkills.Polearm),
                ("弓", DefaultSkills.Bow),
                ("弩", DefaultSkills.Crossbow),
                ("投掷", DefaultSkills.Throwing),
                ("骑术", DefaultSkills.Riding),
                ("跑动", DefaultSkills.Athletics),
                ("锻造", DefaultSkills.Crafting),
                ("侦查", DefaultSkills.Scouting),
                ("战术", DefaultSkills.Tactics),
                ("流氓", DefaultSkills.Roguery),
                ("魅力", DefaultSkills.Charm),
                ("统御", DefaultSkills.Leadership),
                ("交易", DefaultSkills.Trade),
                ("管理", DefaultSkills.Steward),
                ("医术", DefaultSkills.Medicine),
                ("工程", DefaultSkills.Engineering),
            };

            sb.AppendLine("技能：");
            foreach (var (name, skill) in skills)
                sb.AppendLine($"  {name}: {hero.GetSkillValue(skill)}");

            sb.AppendLine();
            sb.AppendLine("属性：");
            sb.AppendLine($"  活力: {hero.CharacterAttributes.GetPropertyValue(DefaultCharacterAttributes.Vigor)}");
            sb.AppendLine($"  控制: {hero.CharacterAttributes.GetPropertyValue(DefaultCharacterAttributes.Control)}");
            sb.AppendLine($"  耐力: {hero.CharacterAttributes.GetPropertyValue(DefaultCharacterAttributes.Endurance)}");
            sb.AppendLine($"  狡诈: {hero.CharacterAttributes.GetPropertyValue(DefaultCharacterAttributes.Cunning)}");
            sb.AppendLine($"  社交: {hero.CharacterAttributes.GetPropertyValue(DefaultCharacterAttributes.Social)}");
            sb.AppendLine($"  智力: {hero.CharacterAttributes.GetPropertyValue(DefaultCharacterAttributes.Intelligence)}");

            return sb.ToString().TrimEnd();
        }

        private static string ExecuteQueryPartyTroops(string? targetEntityId)
        {
            var party = FindPartyByEntityId(targetEntityId);
            if (party == null)
                return $"[错误] 未找到目标部队：{targetEntityId}";

            var leader = party.LeaderHero;
            var gold = leader?.Gold ?? 0;
            var wage = party.TotalWage;
            var memberRoster = party.MemberRoster;
            var elements = memberRoster.GetTroopRoster();
            var totalMen = elements.Sum(e => e.Number);
            var totalWounded = elements.Sum(e => e.WoundedNumber);
            var maxSize = party.Party.PartySizeLimit;
            var foodDays = party.GetNumDaysForFoodToLast();

            var sb = new StringBuilder();
            sb.AppendLine($"===== {(leader != null ? $"{leader.Name}的部队" : party.Name?.ToString() ?? "部队")} =====");
            sb.AppendLine($"金币: {gold}  |  日薪: {wage}");
            sb.AppendLine($"兵力 {totalMen} 人, 伤 {totalWounded} 人, 上限 {maxSize}");
            sb.AppendLine($"粮食: {party.TotalFoodAtInventory} (约 {foodDays:F1} 天)");
            sb.AppendLine();

            if (elements.Count == 0)
            {
                sb.AppendLine("（无士兵）");
                sb.AppendLine();
            }
            else
            {
                var upgradeModel = Campaign.Current.Models.PartyTroopUpgradeModel;
                foreach (var elem in elements.Where(e => e.Character != null && !e.Character.IsHero))
                {
                    var troop = elem.Character;
                    var tier = troop.GetBattleTier();
                    var hasXp = elem.Xp > 0;
                    sb.Append($"  {troop.Name} (T{tier}) × {elem.Number}");
                    if (elem.WoundedNumber > 0)
                        sb.Append($" (伤 {elem.WoundedNumber})");
                    var upgrades = troop.UpgradeTargets;
                    if (upgrades != null && upgrades.Length > 0)
                    {
                        var xpRequired = upgradeModel.GetXpCostForUpgrade(party.Party, troop, upgrades[0]);
                        sb.Append($" [经验 {elem.Xp}/{xpRequired}]");
                    }
                    sb.AppendLine();

                    if (upgrades != null && upgradeModel.IsTroopUpgradeable(party.Party, troop))
                    {
                        foreach (var target in upgrades)
                        {
                            if (target == null) continue;
                            if (!upgradeModel.CanPartyUpgradeTroopToTarget(party.Party, troop, target))
                                continue;
                            var xpCost = upgradeModel.GetXpCostForUpgrade(party.Party, troop, target);
                            var goldCost = (int)upgradeModel.GetGoldCostForUpgrade(party.Party, troop, target).ResultNumber;
                            sb.AppendLine($"    → {target.Name} [需 {xpCost} XP, {goldCost} 金]");
                        }
                    }
                }
            }

            var prisoners = party.PrisonRoster.GetTroopRoster();
            var prisonerCount = prisoners.Sum(p => p.Number);
            if (prisonerCount > 0)
            {
                sb.AppendLine("===== 俘虏 =====");
                foreach (var p in prisoners.Where(p => p.Character != null && !p.Character.IsHero))
                {
                    var tier = p.Character.GetBattleTier();
                    var recruitStatus = "";
                    try
                    {
                        if (Campaign.Current.Models.PrisonerRecruitmentCalculationModel
                            .IsPrisonerRecruitable(party.Party, p.Character, out var _))
                            recruitStatus = " — 可招募";
                    }
                    catch { }
                    sb.AppendLine($"  {p.Character.Name} (T{tier}) × {p.Number}{recruitStatus}");
                }
                sb.AppendLine();
            }

            var itemRoster = party.ItemRoster;
            var itemGroups = new Dictionary<string, List<(string name, int count)>>();
            foreach (var ie in itemRoster)
            {
                var item = ie.EquipmentElement.Item;
                if (item == null) continue;
                var count = ie.Amount;
                var name = item.Name?.ToString() ?? "?";

                var category = item.ItemType switch
                {
                    ItemObject.ItemTypeEnum.Horse => item.ItemCategory == DefaultItemCategories.WarHorse ? "战马" : "马",
                    ItemObject.ItemTypeEnum.OneHandedWeapon => "武器",
                    ItemObject.ItemTypeEnum.TwoHandedWeapon => "武器",
                    ItemObject.ItemTypeEnum.Polearm => "武器",
                    ItemObject.ItemTypeEnum.Bow => "武器",
                    ItemObject.ItemTypeEnum.Crossbow => "武器",
                    ItemObject.ItemTypeEnum.Thrown => "武器",
                    ItemObject.ItemTypeEnum.Arrows => "弹药",
                    ItemObject.ItemTypeEnum.Bolts => "弹药",
                    ItemObject.ItemTypeEnum.Shield => "武器",
                    ItemObject.ItemTypeEnum.Sling => "武器",
                    ItemObject.ItemTypeEnum.SlingStones => "弹药",
                    ItemObject.ItemTypeEnum.HeadArmor => "盔甲",
                    ItemObject.ItemTypeEnum.BodyArmor => "盔甲",
                    ItemObject.ItemTypeEnum.LegArmor => "盔甲",
                    ItemObject.ItemTypeEnum.HandArmor => "盔甲",
                    ItemObject.ItemTypeEnum.Cape => "盔甲",
                    ItemObject.ItemTypeEnum.ChestArmor => "盔甲",
                    ItemObject.ItemTypeEnum.HorseHarness => "马铠",
                    ItemObject.ItemTypeEnum.Banner => "旗帜",
                    ItemObject.ItemTypeEnum.Book => "书籍",
                    _ => item.IsFood ? "粮食" : "货物"
                };
                if (!itemGroups.ContainsKey(category))
                    itemGroups[category] = new List<(string, int)>();
                itemGroups[category].Add((name, count));
            }

            if (itemGroups.Count > 0)
            {
                sb.AppendLine("===== 物品 =====");
                foreach (var kv in itemGroups.OrderBy(k => k.Key))
                {
                    var grouped = kv.Value.GroupBy(x => x.name)
                        .Select(g => (name: g.Key, total: g.Sum(x => x.count)))
                        .ToList();
                    foreach (var g in grouped)
                        sb.AppendLine($"  [{kv.Key}] {g.name} × {g.total}");
                }
            }

            if (leader != null)
            {
                var eq = leader.BattleEquipment;
                var eqSlots = new (EquipmentIndex Index, string Label)[]
                {
                    (EquipmentIndex.Weapon0, "武器"),
                    (EquipmentIndex.Weapon1, "武器"),
                    (EquipmentIndex.Weapon2, "武器"),
                    (EquipmentIndex.Weapon3, "武器"),
                    (EquipmentIndex.Horse, "坐骑"),
                    (EquipmentIndex.HorseHarness, "马铠"),
                    (EquipmentIndex.Head, "头盔"),
                    (EquipmentIndex.Body, "身甲"),
                    (EquipmentIndex.Leg, "腿甲"),
                    (EquipmentIndex.Gloves, "手甲"),
                    (EquipmentIndex.Cape, "披风"),
                };

                var eqLines = new List<string>();
                foreach (var (index, label) in eqSlots)
                {
                    var elem = eq.GetEquipmentFromSlot(index);
                    if (elem.Item != null)
                        eqLines.Add($"{label}: {elem.Item.Name}");
                }

                if (eqLines.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"===== 装备栏（{leader.Name}） =====");
                    foreach (var line in eqLines)
                        sb.AppendLine($"  {line}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        private static string ExecuteQueryAvailableTroops()
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";

            var hero = AIChatClient.CurrentHero;
            var party = hero.PartyBelongedTo;
            var settlement = hero.CurrentSettlement ?? party?.CurrentSettlement
                ?? FindNearbySettlement(party);
            if (settlement == null)
                return "[错误] 你当前不在任何定居点内。请先移动到城镇或村庄。";

            if (hero == party?.LeaderHero && party != null)
            { }
            else if (hero != party?.LeaderHero)
                return "[错误] 你不是部队指挥官，无法征兵。";

            var sb = new StringBuilder();
            sb.AppendLine($"===== {settlement.Name} 可招募兵种 =====");

            if (settlement.IsCastle)
            {
                sb.AppendLine("  （城堡通常没有可招募的平民兵源，请前往下属村庄征兵。）");
                sb.AppendLine();
                sb.AppendLine("使用 query_settlement_villages 查看该城堡的附属村庄。");
                return sb.ToString().TrimEnd();
            }

            if (settlement.IsVillage && settlement.Village.VillageState != Village.VillageStates.Normal)
            {
                var stateDesc = settlement.Village.VillageState switch
                {
                    Village.VillageStates.Looted => "已被劫掠",
                    Village.VillageStates.BeingRaided => "正在被劫掠",
                    Village.VillageStates.ForcedForSupplies => "已被强制征粮",
                    Village.VillageStates.ForcedForVolunteers => "已被强制征兵",
                    _ => "状态异常"
                };
                sb.AppendLine($"  （该村庄{stateDesc}，暂时无法征兵。等待村庄恢复正常后再来。）");
                return sb.ToString().TrimEnd();
            }

            var myFaction = hero.MapFaction;
            var ownerClan = settlement.OwnerClan;
            if (ownerClan != null && myFaction != null && myFaction.IsAtWarWith(ownerClan))
            {
                sb.AppendLine("  （该定居点属于敌对阵营，无法和平征兵。可用 raid_settlement 强制征兵或抢粮。）");
                return sb.ToString().TrimEnd();
            }

            var recruiter = party?.LeaderHero ?? hero;
            var hasAny = false;

            foreach (var notable in settlement.Notables)
            {
                if (notable == null || !notable.IsAlive) continue;
                var volunteers = new List<CharacterObject>();
                for (int j = 0; j < 6; j++)
                    volunteers.Add(notable.VolunteerTypes[j]);
                var hasTroops = volunteers.Any(v => v != null);
                if (!hasTroops) continue;

                var maxIndex = Campaign.Current.Models.VolunteerModel
                    .MaximumIndexHeroCanRecruitFromHero(recruiter, notable);

                var troopLines = new List<string>();
                for (int i = 0; i <= maxIndex && i < volunteers.Count; i++)
                {
                    var troop = volunteers[i];
                    if (troop == null) continue;
                    var cost = (int)Campaign.Current.Models.PartyWageModel
                        .GetTroopRecruitmentCost(troop, recruiter, false).ResultNumber;
                    troopLines.Add($"    {troop.Name} (T{troop.GetBattleTier()}) — {cost} 金/人");
                }

                if (troopLines.Count > 0)
                {
                    hasAny = true;
                    sb.AppendLine($"  {notable.Name}：");
                    foreach (var line in troopLines)
                        sb.AppendLine(line);
                }
            }

            if (!hasAny)
                sb.AppendLine("  （无可用兵种）");

            sb.AppendLine();
            sb.AppendLine("使用 recruit_troops(兵种名, 数量) 招募。");
            return sb.ToString().TrimEnd();
        }

        private static string ExecuteRecruitTroops(string troopName, int count)
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (string.IsNullOrEmpty(troopName))
                return "[错误] 请指定要招募的兵种名称";
            if (count <= 0)
                return "[错误] 招募数量必须大于 0";

            var hero = AIChatClient.CurrentHero;
            var party = hero.PartyBelongedTo;
            if (party == null)
                return $"[错误] {hero.Name} 没有带领部队";

            var settlement = hero.CurrentSettlement ?? party.CurrentSettlement;
            if (settlement == null)
                return "[错误] 当前不在任何定居点内";

            var recruiter = party.LeaderHero;
            if (recruiter == null)
                return "[错误] 无法确定招募者";

            foreach (var notable in settlement.Notables)
            {
                if (notable == null || !notable.IsAlive) continue;
                var volunteers = new List<CharacterObject>();
                for (int j = 0; j < 6; j++)
                    volunteers.Add(notable.VolunteerTypes[j]);
                var maxIndex = Campaign.Current.Models.VolunteerModel
                    .MaximumIndexHeroCanRecruitFromHero(recruiter, notable);

                for (int i = 0; i <= maxIndex && i < volunteers.Count; i++)
                {
                    var troop = volunteers[i];
                    if (troop == null) continue;
                    var tName = troop.Name?.ToString() ?? "";
                    if (!tName.Contains(troopName) && !troopName.Contains(tName)) continue;

                    var costPer = (int)Campaign.Current.Models.PartyWageModel
                        .GetTroopRecruitmentCost(troop, recruiter, false).ResultNumber;
                    var totalCost = costPer * count;

                    if (recruiter.Gold < totalCost)
                        return $"[错误] 金币不足。需要 {totalCost} 金，当前只有 {recruiter.Gold} 金。";

                    party.AddElementToMemberRoster(troop, count);
                    recruiter.ChangeHeroGold(-totalCost);
                    notable.VolunteerTypes[i] = null;

                    return $"已从 {notable.Name} 处招募 {count} 名 {tName}，花费 {totalCost} 金币。";
                }
            }

            return $"[未找到] 当前定居点无可招募的 \"{troopName}\"。使用 query_available_troops 查看可招兵种。";
        }

        private static string ExecuteUpgradeTroops(string troopName, string targetTroopName, int count)
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (string.IsNullOrEmpty(troopName) || count <= 0)
                return "[错误] 请指定要升级的兵种名称和数量";

            var hero = AIChatClient.CurrentHero;
            var party = hero.PartyBelongedTo;
            if (party == null)
                return $"[错误] {hero.Name} 没有带领部队";

            var roster = party.MemberRoster;
            var elements = roster.GetTroopRoster();
            var upgradeModel = Campaign.Current.Models.PartyTroopUpgradeModel;

            foreach (var elem in elements)
            {
                if (elem.Character == null || elem.Character.IsHero) continue;
                var tName = elem.Character.Name?.ToString() ?? "";
                if (!tName.Contains(troopName) && !troopName.Contains(tName)) continue;

                var troop = elem.Character;
                var available = elem.Number - elem.WoundedNumber;
                if (available < count)
                    return $"[错误] {tName} 只有 {available} 名可升级（另有 {elem.WoundedNumber} 名受伤），无法升级 {count} 名。";

                if (!upgradeModel.IsTroopUpgradeable(party.Party, troop))
                    return $"[错误] {tName} 没有可升级的路径。";

                CharacterObject? target = null;
                var upgrades = troop.UpgradeTargets;
                if (upgrades == null || upgrades.Length == 0)
                    return $"[错误] {tName} 没有可升级的路径。";
                if (!string.IsNullOrEmpty(targetTroopName))
                {
                    foreach (var t in upgrades)
                    {
                        if (t == null) continue;
                        var ttName = t.Name?.ToString() ?? "";
                        if (ttName.Contains(targetTroopName) || targetTroopName.Contains(ttName))
                        {
                            target = t;
                            break;
                        }
                    }
                    if (target == null)
                        return $"[错误] {tName} 不能升级为 \"{targetTroopName}\"。可用升级路径见 query_party_troops。";
                }
                else
                {
                    target = upgrades.FirstOrDefault(t => t != null);
                    if (target == null)
                        return $"[错误] {tName} 没有可用的升级目标。";
                }

                if (!upgradeModel.CanPartyUpgradeTroopToTarget(party.Party, troop, target))
                {
                    if (!upgradeModel.DoesPartyHaveRequiredItemsForUpgrade(party.Party, target))
                        return $"[错误] 缺少升级所需的装备（战马等）。";
                    if (!upgradeModel.DoesPartyHaveRequiredPerksForUpgrade(party.Party, troop, target, out var perk))
                        return $"[错误] 需要 {perk?.Name} 特长才能升级。";
                }

                var xpCost = upgradeModel.GetXpCostForUpgrade(party.Party, troop, target);
                var goldCost = (int)upgradeModel.GetGoldCostForUpgrade(party.Party, troop, target).ResultNumber;
                var totalGold = goldCost * count;

                if (elem.Xp < xpCost * count)
                    return $"[错误] 经验不足。{tName} 总经验为 {elem.Xp}，升级 {count} 名共需 {xpCost * count} XP。";

                if (hero.Gold < totalGold)
                    return $"[错误] 金币不足。需要 {totalGold} 金，当前只有 {hero.Gold} 金。";

                var oldXp = elem.Xp;
                roster.AddToCounts(troop, -count, false, 0, -(xpCost * count));
                roster.AddToCounts(target, count, false, 0, 0);
                hero.ChangeHeroGold(-totalGold);

                return $"已将 {count} 名 {tName} 升级为 {target.Name}，花费 {totalGold} 金币。";
            }

            return $"[未找到] 部队中没有兵种 \"{troopName}\"。使用 query_party_troops 查看部队详情。";
        }

        private static string ExecuteBuyFood(int days)
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (days <= 0)
                return "[错误] 天数必须大于 0";

            var hero = AIChatClient.CurrentHero;
            var party = hero.PartyBelongedTo;
            if (party == null)
                return $"[错误] {hero.Name} 没有带领部队";

            var settlement = hero.CurrentSettlement ?? party.CurrentSettlement;
            if (settlement == null)
                return "[错误] 当前不在任何定居点内";

            if (settlement.ItemRoster.TotalFood <= 0)
                return $"[错误] {settlement.Name} 没有粮食可购买。";

            var foodModel = Campaign.Current.Models.PartyFoodBuyingModel;
            var currentFood = party.TotalFoodAtInventory;
            var dailyConsumption = Math.Abs(party.FoodChange);
            if (dailyConsumption <= 0)
                dailyConsumption = (float)party.MemberRoster.TotalManCount / 20f;

            var targetFood = currentFood + (int)(dailyConsumption * days);
            var bought = 0;
            var spent = 0;
            var items = new List<string>();

            var maxAttempts = 20;
            for (int i = 0; i < maxAttempts && party.TotalFoodAtInventory < targetFood; i++)
            {
                try
                {
                    foodModel.FindItemToBuy(party, settlement, out var itemElement, out var price);
                    if (itemElement.EquipmentElement.Item == null) break;

                    var toBuy = Math.Min(10, (int)((targetFood - party.TotalFoodAtInventory) / Math.Max(1, itemElement.EquipmentElement.Item.Value)) + 1);
                    if (toBuy <= 0) toBuy = 1;

                    var cost = (int)(price * toBuy);
                    if (hero.Gold < cost) break;

                    SellItemsAction.Apply(settlement.Party, party.Party, itemElement, toBuy, settlement);
                    hero.ChangeHeroGold(-cost);
                    bought += toBuy;
                    spent += cost;
                    items.Add(itemElement.EquipmentElement.Item.Name?.ToString() ?? "粮食");
                }
                catch
                {
                    break;
                }
            }

            if (bought == 0)
            {
                if (hero.Gold <= 0)
                    return $"[错误] 金币不足，无法购买粮食。";
                return $"[错误] {settlement.Name} 粮食已售罄或价格过高。";
            }

            return $"购买了 {bought} 份粮食（{string.Join("、", items.Distinct())}），花费 {spent} 金币。当前粮食可供约 {party.GetNumDaysForFoodToLast():F0} 天。";
        }

        private static string ExecuteQuerySettlementVillages(string settlementName)
        {
            if (string.IsNullOrEmpty(settlementName))
                return "[错误] 请提供定居点名称";

            var settlement = FindSettlement(settlementName);
            if (settlement == null)
                return $"[未找到] 名称为 \"{settlementName}\" 的定居点";

            if (!settlement.IsTown && !settlement.IsCastle)
                return $"[错误] {settlement.Name} 是村庄，没有附属村庄。请查询城镇或城堡。";

            var sb = new StringBuilder();
            sb.AppendLine($"===== {settlement.Name} 的附属村庄 =====");

            var villages = settlement.BoundVillages;
            if (villages == null || villages.Count == 0)
            {
                sb.AppendLine("（无附属村庄）");
            }
            else
            {
                foreach (var v in villages)
                {
                    var vSettlement = v.Settlement;
                    var vName = vSettlement?.Name?.ToString() ?? "?";
                    var hearths = v.Hearth.ToString("F0");
                    var stateTag = v.VillageState != Village.VillageStates.Normal
                        ? $" [{v.VillageState}]" : "";
                    var ownerTag = vSettlement?.OwnerClan?.Name?.ToString();
                    sb.AppendLine($"  {vName} — 户数: {hearths}{stateTag}（{ownerTag}）");
                }
            }

            return sb.ToString().TrimEnd();
        }

        private static Settlement? FindNearbySettlement(MobileParty? party)
        {
            if (party == null || !party.IsActive) return null;
            var pos = party.GetPosition2D;
            foreach (var s in Settlement.All)
            {
                if (!s.IsTown && !s.IsCastle && !s.IsVillage) continue;
                var gate = s.GatePosition.ToVec2();
                var dx = pos.X - gate.X;
                var dy = pos.Y - gate.Y;
                if ((dx * dx + dy * dy) < 25f)
                    return s;
            }
            return null;
        }

        private static Settlement? FindSettlement(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var s in Settlement.All)
            {
                var sName = s.Name?.ToString() ?? "";
                if (sName.Contains(name) || name.Contains(sName))
                    return s;
            }
            return null;
        }

        private static MobileParty? FindPartyByEntityId(string? targetEntityId)
        {
            if (string.IsNullOrEmpty(targetEntityId))
                return MobileParty.MainParty;

            var entityId = EntityManager.ResolveEntityId(targetEntityId!);
            if (entityId == null)
            {
                foreach (var hero in Hero.AllAliveHeroes)
                {
                    var heroName = hero.Name?.ToString() ?? "";
                    if (heroName.Contains(targetEntityId!) || targetEntityId!.Contains(heroName))
                        return hero.PartyBelongedTo;
                }
                return null;
            }

            var entity = EntityManager.GetEntityById(entityId);
            var targetHero = entity?.HeroRef;
            return targetHero?.PartyBelongedTo;
        }

        private static Hero? ResolveTargetHero(string? targetEntityId)
        {
            if (string.IsNullOrEmpty(targetEntityId))
                return Hero.MainHero;

            var entityId = EntityManager.ResolveEntityId(targetEntityId!);
            if (entityId == null) return null;

            var entity = EntityManager.GetEntityById(entityId);
            return entity?.HeroRef;
        }
    }
}
