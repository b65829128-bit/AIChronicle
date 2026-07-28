using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.LogEntries;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace MyFirstMod
{
    public class ChatResponse
    {
        public string Content { get; set; } = "";
        public string? LearnedKnowledge { get; set; }
        public List<ToolCallData> ToolCalls { get; set; } = new();
        public Dictionary<string, string> ToolResults { get; set; } = new();
    }

    public static class AIChatClient
    {
        private static readonly HttpClient _client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private static Hero? _currentHero;
        private static string _currentIntent = "conversation";

        private sealed class PendingAction
        {
            public Hero Hero = null!;
            public AiBehavior Behavior;
            public Settlement? TargetSettlement;
            public MobileParty? TargetParty;
            public int WaitHours;
            public float CheckInHours;
            public CampaignTime? ArrivedAt;
            public bool TargetReached;
            public bool CheckInQueued;
        }

        private static readonly Dictionary<string, PendingAction> _pendingActions = new();

        private sealed class PendingInquiry
        {
            public Hero Hero = null!;
            public int Amount;
            public ManualResetEventSlim Event = new(false);
            public bool Result;
        }

        private static PendingInquiry? _pendingInquiry;

        private static object BuildTools()
        {
            var activeAgent = EntityManager.ActiveAgent;
            List<ToolDef> toolDefs;
            if (activeAgent != null)
            {
                toolDefs = ContextBuilder.GetFilteredTools(activeAgent, _currentIntent);
            }
            else
            {
                toolDefs = PromptManager.LoadAllTools();
            }
            return BuildTools(toolDefs);
        }

        private static object BuildTools(List<ToolDef> toolDefs)
        {
            return toolDefs.Select(d => new
            {
                type = "function",
                function = new
                {
                    name = d.Name,
                    description = d.Description,
                    parameters = new
                    {
                        type = "object",
                        properties = d.Parameters.ToDictionary(
                            p => p.Name,
                            p => (object)new { type = p.Type, description = p.Description }
                        ),
                        required = d.Parameters.Select(p => p.Name).ToArray()
                    }
                }
            }).ToArray();
        }

        public static string ExecuteToolCall(string name, string arguments)
        {
            try
            {
                var args = JObject.Parse(arguments);
                switch (name)
                {
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
                        return QueryWorldState();

                    case "query_kingdom_settlements":
                        return ExecuteQueryKingdomSettlements(args["kingdom_name"]?.ToString() ?? "");

                    case "query_clan_members":
                        return ExecuteQueryClanMembers(args["clan_name"]?.ToString() ?? "");

                    case "query_kingdom_clans":
                        return ExecuteQueryKingdomClans(args["kingdom_name"]?.ToString() ?? "");

                    case "query_war_status":
                        return ExecuteQueryWarStatus(args["kingdom_name"]?.ToString());

                    case "declare_war":
                        return ExecuteDeclareWar(args["target_kingdom"]?.ToString() ?? "");

                    case "propose_peace":
                        return ExecuteProposePeace(
                            args["target_kingdom"]?.ToString() ?? "",
                            args["tribute_amount"]?.ToObject<int>() ?? 0,
                            args["tribute_days"]?.ToObject<int>() ?? 0);

                    case "propose_alliance":
                        return ExecuteProposeAlliance(args["target_kingdom"]?.ToString() ?? "");

                    case "propose_trade":
                        return ExecuteProposeTrade(args["target_kingdom"]?.ToString() ?? "");

                    case "respond_to_diplomacy_proposal":
                        return ExecuteRespondToProposal(
                            args["proposal_id"]?.ToString() ?? "",
                            args["accepted"]?.ToObject<bool>() ?? false);

                    case "move_to_settlement":
                        return ExecuteMoveToSettlement(args["settlement_name"]?.ToString() ?? "");

                    case "wait_at_settlement":
                        return ExecuteWaitAtSettlement(args["hours"]?.ToObject<int>() ?? 0);

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

                var sb = new System.Text.StringBuilder();
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

        private static string QueryWorldState()
        {
            var sb = new System.Text.StringBuilder();

            foreach (var k in Kingdom.All)
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

                var sb = new System.Text.StringBuilder();
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

                var sb = new System.Text.StringBuilder();
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

                var sb = new System.Text.StringBuilder();
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

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"===== {hero.Name} 的近期事件 =====");
            foreach (var r in results)
                sb.AppendLine(r);

            return sb.ToString().TrimEnd();
        }

        private static string ExecuteQuerySurroundings(int radiusKm, int maxSettlements, int maxParties)
        {
            if (_currentHero == null)
                return "[错误] 无当前领主";

            var configRadius = MySettings.Instance?.SurroundingsScanRadius ?? 10;
            if (radiusKm <= 0 || radiusKm > configRadius)
                radiusKm = configRadius;
            if (maxSettlements <= 0 || maxSettlements > 15)
                maxSettlements = 5;
            if (maxParties <= 0 || maxParties > 20)
                maxParties = 8;

            var hero = _currentHero;
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
                return (float)System.Math.Sqrt(dx * dx + dy * dy);
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

            var sb = new System.Text.StringBuilder();
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

        private static string ExecuteQueryWarStatus(string? kingdomName)
        {
            if (Campaign.Current == null)
                return "[错误] 战役未加载";

            Kingdom? kingdom;
            if (string.IsNullOrEmpty(kingdomName))
            {
                if (_currentHero == null)
                    return "[错误] 无当前领主，且未指定王国名称";
                kingdom = _currentHero.MapFaction as Kingdom;
                if (kingdom == null)
                    return "[错误] 当前领主不属于任何王国";
            }
            else
            {
                kingdom = null;
                foreach (var k in Kingdom.All)
                {
                    var kName = k.Name?.ToString() ?? "";
                    if (kName.Contains(kingdomName!) || kingdomName!.Contains(kName))
                    {
                        kingdom = k;
                        break;
                    }
                }
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

            var sb = new System.Text.StringBuilder();
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

        private static Kingdom? FindKingdom(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var k in Kingdom.All)
            {
                var kName = k.Name?.ToString() ?? "";
                if (kName.Contains(name) || name.Contains(kName))
                    return k;
            }
            return null;
        }

        private static string ExecuteDeclareWar(string targetKingdomName)
        {
            if (_currentHero == null) return "[错误] 无当前领主";
            var myKingdom = _currentHero.MapFaction as Kingdom;
            if (myKingdom == null) return "[错误] 你当前不属于任何王国";
            if (myKingdom.RulingClan?.Leader != _currentHero) return "[错误] 只有王国统治者才能宣战";
            var target = FindKingdom(targetKingdomName);
            if (target == null) return $"[错误] 未找到王国：{targetKingdomName}";
            if (target == myKingdom) return "[错误] 不能对自己宣战";
            if (myKingdom.IsAtWarWith(target)) return $"已经与{target.Name}处于交战状态。";
            DeclareWarAction.ApplyByKingdomDecision(myKingdom, target);
            return $"已向{target.Name}宣战。";
        }

        private static string ExecuteProposePeace(string targetKingdomName, int tributeAmount, int tributeDays)
        {
            if (_currentHero == null) return "[错误] 无当前领主";
            var myKingdom = _currentHero.MapFaction as Kingdom;
            if (myKingdom == null) return "[错误] 你当前不属于任何王国";
            if (myKingdom.RulingClan?.Leader != _currentHero) return "[错误] 只有王国统治者才能提出议和";
            var target = FindKingdom(targetKingdomName);
            if (target == null) return $"[错误] 未找到王国：{targetKingdomName}";
            if (!myKingdom.IsAtWarWith(target)) return $"[错误] 当前并未与{target.Name}交战";
            var myEntity = EntityManager.GetEntityByHero(_currentHero);
            var targetRuler = target.RulingClan?.Leader;
            if (targetRuler == null) return $"[错误] {target.Name} 没有统治者";
            var targetEntity = EntityManager.GetEntityByHero(targetRuler);
            if (myEntity == null || targetEntity == null) return "[错误] 实体解析失败";
            var tributeArg = $"{tributeAmount}_{tributeDays}";
            AgentManager.StoreDiplomacyProposal(myEntity.Id, targetEntity.Id, "peace", tributeArg);
            QueueKingActivation(targetEntity.Id, myEntity.Id,
                $"来自 {myEntity.Name}（{myKingdom.Name} 统治者）的议和提案：愿意支付每日 {tributeAmount} 金币、持续 {tributeDays} 天作为赔款。你接受这个议和条件吗？请用 respond_to_diplomacy_proposal 回复。");
            return $"议和提案已发送给{target.Name}的统治者{targetRuler.Name}，等待回复。";
        }

        private static string ExecuteProposeAlliance(string targetKingdomName)
        {
            if (_currentHero == null) return "[错误] 无当前领主";
            var myKingdom = _currentHero.MapFaction as Kingdom;
            if (myKingdom == null) return "[错误] 你当前不属于任何王国";
            if (myKingdom.RulingClan?.Leader != _currentHero) return "[错误] 只有王国统治者才能提议结盟";
            var target = FindKingdom(targetKingdomName);
            if (target == null) return $"[错误] 未找到王国：{targetKingdomName}";
            if (target == myKingdom) return "[错误] 不能和自己结盟";
            if (myKingdom.IsAtWarWith(target)) return $"[错误] 无法与交战中的王国结盟";
            var targetRuler = target.RulingClan?.Leader;
            if (targetRuler == null) return $"[错误] {target.Name} 没有统治者";
            var myEntity = EntityManager.GetEntityByHero(_currentHero);
            var targetEntity = EntityManager.GetEntityByHero(targetRuler);
            if (myEntity == null || targetEntity == null) return "[错误] 实体解析失败";
            AgentManager.StoreDiplomacyProposal(myEntity.Id, targetEntity.Id, "alliance");
            QueueKingActivation(targetEntity.Id, myEntity.Id,
                $"来自 {myEntity.Name}（{myKingdom.Name} 统治者）的结盟提案。你接受这个结盟邀请吗？请用 respond_to_diplomacy_proposal 回复。");
            return $"结盟提案已发送给{target.Name}的统治者{targetRuler.Name}，等待回复。";
        }

        private static string ExecuteProposeTrade(string targetKingdomName)
        {
            if (_currentHero == null) return "[错误] 无当前领主";
            var myKingdom = _currentHero.MapFaction as Kingdom;
            if (myKingdom == null) return "[错误] 你当前不属于任何王国";
            if (myKingdom.RulingClan?.Leader != _currentHero) return "[错误] 只有王国统治者才能提议贸易协定";
            var target = FindKingdom(targetKingdomName);
            if (target == null) return $"[错误] 未找到王国：{targetKingdomName}";
            if (target == myKingdom) return "[错误] 不能和自己签订贸易协定";
            if (myKingdom.IsAtWarWith(target)) return $"[错误] 无法与交战中的王国签订贸易协定";
            var targetRuler = target.RulingClan?.Leader;
            if (targetRuler == null) return $"[错误] {target.Name} 没有统治者";
            var myEntity = EntityManager.GetEntityByHero(_currentHero);
            var targetEntity = EntityManager.GetEntityByHero(targetRuler);
            if (myEntity == null || targetEntity == null) return "[错误] 实体解析失败";
            AgentManager.StoreDiplomacyProposal(myEntity.Id, targetEntity.Id, "trade");
            QueueKingActivation(targetEntity.Id, myEntity.Id,
                $"来自 {myEntity.Name}（{myKingdom.Name} 统治者）的贸易协定提案。你接受这个贸易协定吗？请用 respond_to_diplomacy_proposal 回复。");
            return $"贸易协定提案已发送给{target.Name}的统治者{targetRuler.Name}，等待回复。";
        }

        private static string ExecuteRespondToProposal(string proposalId, bool accepted)
        {
            if (_currentHero == null) return "[错误] 无当前领主";
            var myKingdom = _currentHero.MapFaction as Kingdom;
            if (myKingdom == null) return "[错误] 你当前不属于任何王国";
            if (myKingdom.RulingClan?.Leader != _currentHero) return "[错误] 只有王国统治者才能处理外交提案";

            var content = AgentManager.ReadDiplomacyProposal(proposalId);
            if (content == null) return $"[错误] 未找到提案：{proposalId}";

            var parts = proposalId.Split('_');
            if (parts.Length < 3) return $"[错误] 无效的提案ID格式";

            var proposerId = "";
            var targetId = "";
            var type = "";
            var toIdx = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "to")
                {
                    proposerId = string.Join("_", parts.Take(i));
                    toIdx = i;
                    break;
                }
            }
            if (toIdx < 0) return $"[错误] 无法解析提案ID";
            for (int i = toIdx + 1; i < parts.Length; i++)
            {
                if (parts[i] == "peace" || parts[i] == "alliance" || parts[i] == "trade")
                {
                    targetId = string.Join("_", parts.Skip(toIdx + 1).Take(i - toIdx - 1));
                    type = parts[i];
                    break;
                }
            }

            var myEntity = EntityManager.GetEntityByHero(_currentHero);
            if (myEntity == null || myEntity.Id != targetId)
                return "[错误] 该提案不是发给你的";

            var proposerEntity = EntityManager.GetOrCreateEntityById(proposerId);
            if (proposerEntity?.HeroRef == null) return "[错误] 无法找到提案发起人";

            if (!accepted)
            {
                AgentManager.DeleteDiplomacyProposal(proposalId);
                return "已拒绝该提案。";
            }

            var proposerKingdom = proposerEntity.HeroRef.MapFaction as Kingdom;
            if (proposerKingdom == null) return "[错误] 提案发起人已不属于任何王国";

            switch (type)
            {
                case "peace":
                {
                    if (!myKingdom.IsAtWarWith(proposerKingdom)) return "[错误] 当前并未与该王国交战";
                    var lines = content.Split('\n');
                    var tribute = "0_0";
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("tribute="))
                        {
                            tribute = line.Substring(8);
                            break;
                        }
                    }
                    var tributeParts = tribute.Split('_');
                    var tributeAmount = int.TryParse(tributeParts[0], out var a) ? a : 0;
                    var tributeDays = int.TryParse(tributeParts[1], out var d) ? d : 0;
                    MakePeaceAction.ApplyByKingdomDecision(proposerKingdom, myKingdom, tributeAmount, tributeDays);
                    AgentManager.DeleteDiplomacyProposal(proposalId);
                    return $"已接受议和，与{proposerKingdom.Name}达成和平。";
                }
                case "alliance":
                {
                    var ab = Campaign.Current.GetCampaignBehavior<IAllianceCampaignBehavior>();
                    ab.StartAlliance(proposerKingdom, myKingdom);
                    AgentManager.DeleteDiplomacyProposal(proposalId);
                    return $"已接受结盟，与{proposerKingdom.Name}组成军事同盟。";
                }
                case "trade":
                {
                    var tb = Campaign.Current.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
                    tb.MakeTradeAgreement(proposerKingdom, myKingdom, CampaignTime.Years(1f));
                    AgentManager.DeleteDiplomacyProposal(proposalId);
                    return $"已接受贸易协定，与{proposerKingdom.Name}建立贸易关系。";
                }
                default:
                    return $"[错误] 未知的提案类型：{type}";
            }
        }

        private static void QueueKingActivation(string agentId, string targetId, string message)
        {
            AgentScheduler.QueueEvent(new ActivationEvent
            {
                Type = ActivationEventType.KingDiplomacy,
                AgentId = agentId,
                TargetId = targetId,
                Content = message,
                Depth = 0
            });
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

        private static string ExecuteMoveToSettlement(string settlementName)
        {
            if (_currentHero == null)
                return "[错误] 无当前领主";

            var target = FindSettlement(settlementName);
            if (target == null)
                return $"[错误] 未找到名为 \"{settlementName}\" 的定居点";

            if (!target.IsTown && !target.IsCastle)
                return $"[错误] {target.Name} 是村庄，只能移动到城镇或城堡";

            var party = _currentHero.PartyBelongedTo;
            if (party == null)
                return $"[错误] {_currentHero.Name} 没有带领部队（可能在城中担任总督、被俘虏或编入军团）";

            if (!party.IsActive)
                return $"[错误] 部队当前不可用";

            if (party.CurrentSettlement == target)
            {
                var action = GetOrCreateAction(_currentHero);
                action.Behavior = AiBehavior.GoToSettlement;
                action.TargetSettlement = target;
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

            var action2 = GetOrCreateAction(_currentHero);
            action2.Behavior = AiBehavior.GoToSettlement;
            action2.TargetSettlement = target;

            return $"部队已出发前往{target.Name}。";
        }

        private static string ExecuteWaitAtSettlement(int hours)
        {
            if (_currentHero == null)
                return "[错误] 无当前领主";

            if (hours <= 0)
                return "[错误] 等待时长必须大于 0 小时";

            if (hours > 720)
                return "[错误] 等待时长不能超过 720 小时（30 天）";

            var party = _currentHero.PartyBelongedTo;
            var currentSettlement = party?.CurrentSettlement;

            if (currentSettlement == null)
                return "[错误] {NPC} 当前不在任何定居点内".Replace("{NPC}", _currentHero.Name?.ToString() ?? "领主");

            var key = _currentHero.Id.ToString();
            if (!_pendingActions.TryGetValue(key, out var action))
            {
                action = new PendingAction { Hero = _currentHero, TargetSettlement = currentSettlement };
                _pendingActions[key] = action;
            }

            action.WaitHours = hours;

            if (action.ArrivedAt == null)
                action.ArrivedAt = CampaignTime.Now;

            return $"将在{currentSettlement.Name}停留{hours}小时（约{hours / 24}天）。";
        }

        private static PendingAction GetOrCreateAction(Hero hero)
        {
            var key = hero.Id.ToString();
            if (!_pendingActions.TryGetValue(key, out var action))
            {
                action = new PendingAction { Hero = hero };
                _pendingActions[key] = action;
            }
            return action;
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

        private static string ExecuteRaidSettlement(string settlementName)
        {
            if (_currentHero == null) return "[错误] 无当前部队指挥官";

            var target = FindSettlement(settlementName);
            if (target == null) return $"[错误] 未找到定居点：{settlementName}";
            if (!target.IsVillage) return $"[错误] {target.Name} 不是村庄，请使用 besiege_settlement 攻打城镇/城堡";

            var party = _currentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {_currentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            SetPartyAiAction.GetActionForRaidingSettlement(party, target, MobileParty.NavigationType.Default, false, false);
            var ra = GetOrCreateAction(_currentHero);
            ra.Behavior = AiBehavior.RaidSettlement;
            ra.TargetSettlement = target;
            return $"部队已出发劫掠{target.Name}。";
        }

        private static string ExecuteBesiegeSettlement(string settlementName)
        {
            if (_currentHero == null) return "[错误] 无当前部队指挥官";

            var target = FindSettlement(settlementName);
            if (target == null) return $"[错误] 未找到定居点：{settlementName}";
            if (!target.IsTown && !target.IsCastle) return $"[错误] {target.Name} 不是城镇或城堡，请使用 raid_settlement 劫掠村庄";

            var party = _currentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {_currentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            SetPartyAiAction.GetActionForBesiegingSettlement(party, target, MobileParty.NavigationType.Default, false);
            var ba = GetOrCreateAction(_currentHero);
            ba.Behavior = AiBehavior.BesiegeSettlement;
            ba.TargetSettlement = target;
            return $"部队已出发围攻{target.Name}。";
        }

        private static string ExecuteEngageParty(string targetEntityId)
        {
            if (_currentHero == null) return "[错误] 无当前部队指挥官";
            if (string.IsNullOrEmpty(targetEntityId)) return "[错误] 请指定要攻击的目标实体";

            var party = _currentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {_currentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            var targetParty = FindPartyByEntityId(targetEntityId);
            if (targetParty == null) return $"[错误] 未找到 {targetEntityId} 的部队";
            if (targetParty == party) return $"[错误] 不能攻击自己的部队";

            SetPartyAiAction.GetActionForEngagingParty(party, targetParty, MobileParty.NavigationType.Default, false);
            var ea = GetOrCreateAction(_currentHero);
            ea.Behavior = AiBehavior.EngageParty;
            ea.TargetParty = targetParty;
            return $"部队已出发追击{targetParty.Name?.ToString() ?? targetEntityId}。";
        }

        private static string ExecuteDefendSettlement(string settlementName)
        {
            if (_currentHero == null) return "[错误] 无当前部队指挥官";

            var target = FindSettlement(settlementName);
            if (target == null) return $"[错误] 未找到定居点：{settlementName}";

            var party = _currentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {_currentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            SetPartyAiAction.GetActionForDefendingSettlement(party, target, MobileParty.NavigationType.Default, false, false);
            var da = GetOrCreateAction(_currentHero);
            da.Behavior = AiBehavior.DefendSettlement;
            da.TargetSettlement = target;
            da.CheckInHours = 72f;
            return $"部队已出发驻防{target.Name}。";
        }

        private static string ExecutePatrolSettlement(string settlementName)
        {
            if (_currentHero == null) return "[错误] 无当前部队指挥官";

            var target = FindSettlement(settlementName);
            if (target == null) return $"[错误] 未找到定居点：{settlementName}";

            var party = _currentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {_currentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            SetPartyAiAction.GetActionForPatrollingAroundSettlement(party, target, MobileParty.NavigationType.Default, false, false);
            var pa = GetOrCreateAction(_currentHero);
            pa.Behavior = AiBehavior.PatrolAroundPoint;
            pa.TargetSettlement = target;
            pa.CheckInHours = 48f;
            return $"部队已出发巡逻{target.Name}周边。";
        }

        private static string ExecuteEscortParty(string targetEntityId)
        {
            if (_currentHero == null) return "[错误] 无当前部队指挥官";
            if (string.IsNullOrEmpty(targetEntityId)) return "[错误] 请指定要护送的目标实体";

            var party = _currentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {_currentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            var targetParty = FindPartyByEntityId(targetEntityId);
            if (targetParty == null) return $"[错误] 未找到 {targetEntityId} 的部队";
            if (targetParty == party) return $"[错误] 不能护送自己的部队";

            SetPartyAiAction.GetActionForEscortingParty(party, targetParty, MobileParty.NavigationType.Default, false, false);
            var esa = GetOrCreateAction(_currentHero);
            esa.Behavior = AiBehavior.EscortParty;
            esa.TargetParty = targetParty;
            esa.CheckInHours = 24f;
            return $"部队已出发护送{targetParty.Name?.ToString() ?? targetEntityId}。";
        }

        private static string ExecuteGoAroundParty(string? targetEntityId)
        {
            if (_currentHero == null) return "[错误] 无当前部队指挥官";

            var party = _currentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {_currentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            var targetParty = FindPartyByEntityId(targetEntityId);
            if (targetParty == null) return $"[错误] 未找到目标部队";
            if (targetParty == party) return $"[错误] 不能绕过自己的部队";

            SetPartyAiAction.GetActionForGoingAroundParty(party, targetParty, MobileParty.NavigationType.Default, false);
            return $"部队已绕开{targetParty.Name?.ToString() ?? "目标部队"}。";
        }

        private static string ExecuteCancelAction()
        {
            if (_currentHero == null)
                return "[错误] 无当前部队指挥官";

            var key = _currentHero.Id.ToString();
            if (_pendingActions.Remove(key))
                return "当前任务已取消，部队恢复自主行动。";

            return "当前没有待执行的任务。";
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

        private static string ExecuteChangeRelation(int delta, string? targetEntityId)
        {
            if (_currentHero == null)
                return "[错误] 无当前领主";

            var target = ResolveTargetHero(targetEntityId);
            if (target == null)
                return $"[错误] 未找到目标实体：{targetEntityId}";

            var maxChange = MySettings.Instance?.MaxRelationChange ?? 5;
            if (Math.Abs(delta) > maxChange)
                delta = Math.Sign(delta) * maxChange;

            if (delta == 0)
                return "[信息] 好感变化为 0，无需操作";

            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(_currentHero, target, delta, true);
            var currentRelation = _currentHero.GetRelation(target);

            return $"对{target.Name}的好感变化了{delta:+0;-0}点，当前好感度为{currentRelation}点。";
        }

        private static string ExecuteGiveGold(int amount, string? targetEntityId)
        {
            if (_currentHero == null)
                return "[错误] 无当前领主";

            var target = ResolveTargetHero(targetEntityId);
            if (target == null)
                return $"[错误] 未找到目标实体：{targetEntityId}";

            if (amount <= 0)
                return "[错误] 金币数额必须大于 0";

            if (_currentHero.Gold < amount)
                return $"[错误] {_currentHero.Name} 只有 {_currentHero.Gold} 金币，不足以赠送 {amount} 金币";

            GiveGoldAction.ApplyBetweenCharacters(_currentHero, target, amount);

            return $"已赠予{target.Name} {amount} 金币。{_currentHero.Name} 剩余 {_currentHero.Gold} 金币。";
        }

        private static string ExecuteRequestGold(int amount, string? targetEntityId)
        {
            if (_currentHero == null)
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
                GiveGoldAction.ApplyBetweenCharacters(target, _currentHero, amount);
                return $"{target.Name} 支付了 {amount} 金币。";
            }

            using var mre = new ManualResetEventSlim(false);
            var inquiry = new PendingInquiry
            {
                Hero = _currentHero,
                Amount = amount,
                Event = mre,
                Result = false
            };
            _pendingInquiry = inquiry;

            mre.Wait(TimeSpan.FromSeconds(30));

            if (inquiry.Result)
            {
                GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, _currentHero, amount);
                return $"对方同意支付 {amount} 金币。";
            }
            return $"对方拒绝了支付 {amount} 金币的请求。";
        }

        private static string ExecuteSendLetter(string recipientId, string content)
        {
            if (string.IsNullOrEmpty(recipientId)) return "[错误] 请提供收信人实体 ID 或名称";
            if (string.IsNullOrEmpty(content)) return "[错误] 信件内容不能为空";
            if (_currentHero == null) return "[错误] 无当前领主";
            var senderEntity = EntityManager.GetEntityByHero(_currentHero);
            if (senderEntity == null) return "[错误] 发信人实体不存在";
            var resolvedId = EntityManager.ResolveEntityId(recipientId);
            if (resolvedId == null) return $"[错误] 未找到名为 \"{recipientId}\" 的实体";
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

        public static void CheckPendingInquiry()
        {
            var inquiry = _pendingInquiry;
            if (inquiry == null) return;

            _pendingInquiry = null;

            var hero = inquiry.Hero;
            var amount = inquiry.Amount;

            InformationManager.ShowInquiry(new InquiryData(
                $"{hero.Name} 向你索要金币",
                $"{hero.Name} 向你要 {amount} 金币。\n你当前拥有 {Hero.MainHero.Gold} 金币。",
                true, true, "同意", "拒绝",
                () => { inquiry.Result = true; inquiry.Event.Set(); },
                () => { inquiry.Result = false; inquiry.Event.Set(); }),
                pauseGameActiveState: true,
                prioritize: true);
        }

        public static void Tick()
        {
            if (_pendingActions.Count == 0 || Campaign.Current == null)
                return;

            var keysToRemove = new List<string>();

            foreach (var kv in _pendingActions)
            {
                try
                {
                    var action = kv.Value;
                    var hero = action.Hero;
                    if (hero == null)
                    {
                        keysToRemove.Add(kv.Key);
                        continue;
                    }

                    var party = hero.PartyBelongedTo;
                    if (party == null || !party.IsActive)
                    {
                        keysToRemove.Add(kv.Key);
                        continue;
                    }

                    if (action.Behavior == AiBehavior.EngageParty
                        || action.Behavior == AiBehavior.EscortParty)
                    {
                        if (action.TargetParty == null || !action.TargetParty.IsActive)
                        {
                            keysToRemove.Add(kv.Key);
                            continue;
                        }
                    }

                    if (action.Behavior == AiBehavior.GoToSettlement
                        && action.TargetSettlement != null
                        && party.CurrentSettlement == action.TargetSettlement
                        && action.ArrivedAt == null)
                    {
                        action.ArrivedAt = CampaignTime.Now;
                    }

                    if (action.ArrivedAt != null)
                    {
                        var elapsed = (CampaignTime.Now - action.ArrivedAt.Value).ToHours;
                        if (action.WaitHours <= 0 || elapsed >= action.WaitHours)
                        {
                            keysToRemove.Add(kv.Key);
                            if (action.TargetSettlement != null)
                            {
                                InformationManager.DisplayMessage(new InformationMessage(
                                    $"[MyFirstMod] {hero.Name} 结束了在{action.TargetSettlement.Name}的停留。",
                                    Colors.Cyan));
                            }
                            continue;
                        }
                        continue;
                    }

                    if (action.TargetSettlement == null && action.TargetParty == null)
                        continue;

                    bool isOneShot = action.Behavior == AiBehavior.RaidSettlement
                        || action.Behavior == AiBehavior.BesiegeSettlement
                        || action.Behavior == AiBehavior.EngageParty;

                    if (!action.TargetReached && action.TargetSettlement != null)
                    {
                        if (action.Behavior == AiBehavior.BesiegeSettlement)
                            action.TargetReached = party.BesiegedSettlement == action.TargetSettlement;
                        else if (action.Behavior == AiBehavior.DefendSettlement
                            || action.Behavior == AiBehavior.PatrolAroundPoint)
                            action.TargetReached = party.DefaultBehavior == action.Behavior
                                && party.TargetSettlement == action.TargetSettlement;
                        else
                            action.TargetReached = party.CurrentSettlement == action.TargetSettlement;

                        if (action.TargetReached && action.ArrivedAt == null)
                            action.ArrivedAt = CampaignTime.Now;
                    }

                    if (!action.TargetReached && action.TargetParty != null)
                    {
                        if (action.Behavior == AiBehavior.EngageParty)
                            action.TargetReached = party.MapEvent != null;
                        else if (action.Behavior == AiBehavior.EscortParty)
                            action.TargetReached = party.DefaultBehavior == AiBehavior.EscortParty
                                && party.TargetParty == action.TargetParty;

                        if (action.TargetReached && action.ArrivedAt == null)
                            action.ArrivedAt = CampaignTime.Now;
                    }

                    if (action.CheckInHours > 0f && action.TargetReached
                        && !action.CheckInQueued && action.ArrivedAt != null
                        && (CampaignTime.Now - action.ArrivedAt.Value).ToHours >= action.CheckInHours)
                    {
                        action.CheckInQueued = true;

                        var agentEntity = EntityManager.GetEntityByHero(hero);
                        if (agentEntity != null)
                        {
                            var locDesc = action.TargetSettlement?.Name?.ToString()
                                ?? action.TargetParty?.Name?.ToString()
                                ?? "当前位置";
                            var behaviorDesc = action.Behavior switch
                            {
                                AiBehavior.DefendSettlement => "驻防",
                                AiBehavior.PatrolAroundPoint => "巡逻",
                                AiBehavior.EscortParty => "护送",
                                _ => "执行任务"
                            };

                            var checkInContent =
                                $"你已在{locDesc}{behaviorDesc}了{(int)action.CheckInHours}小时以上。\n" +
                                "是否需要：1) 继续当前任务 2) 前往别处 3) 向阵营领袖汇报情况。";

                            AgentScheduler.QueueEvent(new ActivationEvent
                            {
                                Type = ActivationEventType.BehaviorCheckIn,
                                AgentId = agentEntity.Id,
                                TargetId = agentEntity.Id,
                                Content = checkInContent,
                                Depth = 0
                            });
                        }

                        keysToRemove.Add(kv.Key);
                        continue;
                    }

                    var shortTerm = party.ShortTermBehavior;
                    bool isFleeing = shortTerm == AiBehavior.FleeToPoint
                        || shortTerm == AiBehavior.FleeToGate
                        || shortTerm == AiBehavior.FleeToParty;
                    bool isFighting = party.MapEvent != null;

                    if (!isFleeing && !isFighting)
                    {
                        if (isOneShot && action.TargetReached && party.DefaultBehavior != action.Behavior)
                        {
                            keysToRemove.Add(kv.Key);
                            continue;
                        }

                        bool needsReissue = party.DefaultBehavior != action.Behavior;
                        if (!needsReissue && action.TargetSettlement != null)
                            needsReissue = party.TargetSettlement != action.TargetSettlement;
                        if (!needsReissue && action.TargetParty != null)
                            needsReissue = party.TargetParty != action.TargetParty;

                        if (needsReissue)
                        {
                            var navType = party.IsCurrentlyAtSea
                                ? MobileParty.NavigationType.Naval
                                : MobileParty.NavigationType.Default;

                            switch (action.Behavior)
                            {
                                case AiBehavior.GoToSettlement:
                                    party.SetMoveGoToSettlement(action.TargetSettlement!, navType, false);
                                    break;
                                case AiBehavior.RaidSettlement:
                                    SetPartyAiAction.GetActionForRaidingSettlement(party, action.TargetSettlement!, navType, false, false);
                                    break;
                                case AiBehavior.BesiegeSettlement:
                                    SetPartyAiAction.GetActionForBesiegingSettlement(party, action.TargetSettlement!, navType, false);
                                    break;
                                case AiBehavior.EngageParty:
                                    SetPartyAiAction.GetActionForEngagingParty(party, action.TargetParty!, navType, false);
                                    break;
                                case AiBehavior.DefendSettlement:
                                    SetPartyAiAction.GetActionForDefendingSettlement(party, action.TargetSettlement!, navType, false, false);
                                    break;
                                case AiBehavior.PatrolAroundPoint:
                                    SetPartyAiAction.GetActionForPatrollingAroundSettlement(party, action.TargetSettlement!, navType, false, false);
                                    break;
                                case AiBehavior.EscortParty:
                                    SetPartyAiAction.GetActionForEscortingParty(party, action.TargetParty!, navType, false, false);
                                    break;
                            }
                        }
                    }
                }
                catch
                {
                    keysToRemove.Add(kv.Key);
                }
            }

            foreach (var key in keysToRemove)
                _pendingActions.Remove(key);
        }

        public static async Task<ChatResponse> EvaluateToolCalls(CharacterPrompt charPrompt, string roleplayResponse)
        {
            var settings = MySettings.Instance!;
            var systemPrompt = PromptManager.LoadToolCallPrompt();

            var messageList = new List<object> { new { role = "system", content = systemPrompt } };

            foreach (var entry in charPrompt.ChatHistory)
            {
                if (entry.Role == "tool")
                {
                    messageList.Add(new
                    {
                        role = "tool",
                        tool_call_id = entry.ToolCallId ?? "",
                        content = entry.Content
                    });
                }
                else if (entry.ToolCalls != null && entry.ToolCalls.Count > 0)
                {
                    messageList.Add(new
                    {
                        role = entry.Role,
                        content = entry.Content,
                        tool_calls = entry.ToolCalls.Select(tc => new
                        {
                            id = tc.Id,
                            type = "function",
                            function = new { name = tc.Name, arguments = tc.Arguments }
                        })
                    });
                }
                else
                {
                    messageList.Add(new { role = entry.Role, content = entry.Content });
                }
            }

            if (!string.IsNullOrEmpty(roleplayResponse))
            {
                messageList.Add(new { role = "assistant", content = roleplayResponse });
            }

            var payload = new
            {
                model = settings.Model,
                messages = messageList,
                tools = BuildTools(),
                tool_choice = "auto",
                max_tokens = 200,
                temperature = 0.1f
            };

            var json = JsonConvert.SerializeObject(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            var response = await _client.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(responseBody);
            var message = result["choices"]?[0]?["message"];

            string? learnedKnowledge = null;
            var responseToolCalls = new List<ToolCallData>();

            var toolCalls = message?["tool_calls"];
            if (toolCalls != null)
            {
                foreach (var call in toolCalls)
                {
                    var callId = call["id"]?.ToString() ?? Guid.NewGuid().ToString("N");
                    var funcName = call["function"]?["name"]?.ToString() ?? "";
                    var argsStr = call["function"]?["arguments"]?.ToString() ?? "{}";

                    responseToolCalls.Add(new ToolCallData
                    {
                        Id = callId,
                        Name = funcName,
                        Arguments = argsStr
                    });

                    if (funcName == "update_knowledge")
                    {
                        try
                        {
                            var args = JObject.Parse(argsStr);
                            learnedKnowledge = args["knowledge"]?.ToString();
                        }
                        catch { }
                    }
                }
            }

            return new ChatResponse
            {
                Content = "",
                LearnedKnowledge = learnedKnowledge,
                ToolCalls = responseToolCalls
            };
        }

        public static async Task<ChatResponse> SendMessage(CharacterPrompt charPrompt, Hero? hero = null, bool includeTools = true, string intent = "conversation")
        {
            _currentHero = hero;
            _currentIntent = intent;
            var settings = MySettings.Instance!;
            var systemPrompt = hero != null
                ? PromptManager.BuildAgentSystemPrompt(hero, charPrompt, intent)
                : PromptManager.BuildSystemPrompt(charPrompt.HeroName, charPrompt);

            var historyLimit = settings.ChatHistoryLimit;
            var trimmedHistory = charPrompt.ChatHistory;
            if (trimmedHistory.Count > historyLimit)
            {
                trimmedHistory = trimmedHistory.Skip(trimmedHistory.Count - historyLimit).ToList();
            }

            var messageList = new List<object> { new { role = "system", content = systemPrompt } };

            foreach (var entry in trimmedHistory)
            {
                if (entry.Role == "tool")
                {
                    if (includeTools)
                    {
                        messageList.Add(new
                        {
                            role = "tool",
                            tool_call_id = entry.ToolCallId ?? "",
                            content = entry.Content
                        });
                    }
                }
                else if (entry.ToolCalls != null && entry.ToolCalls.Count > 0)
                {
                    if (includeTools)
                    {
                        messageList.Add(new
                        {
                            role = entry.Role,
                            content = entry.Content,
                            tool_calls = entry.ToolCalls.Select(tc => new
                            {
                                id = tc.Id,
                                type = "function",
                                function = new
                                {
                                    name = tc.Name,
                                    arguments = tc.Arguments
                                }
                            })
                        });
                    }
                    else
                    {
                        messageList.Add(new { role = entry.Role, content = entry.Content });
                    }
                }
                else
                {
                    messageList.Add(new { role = entry.Role, content = entry.Content });
                }
            }

            string? learnedKnowledge = null;
            var allToolCalls = new List<ToolCallData>();
            var toolResults = new Dictionary<string, string>();
            var accumulatedText = "";

            var maxRounds = settings.UnlimitedAgentRounds ? int.MaxValue : settings.MaxAgentRounds;

            for (int round = 0; round < maxRounds; round++)
            {
                object payload;
                if (includeTools)
                {
                    payload = new
                    {
                        model = settings.Model,
                        messages = messageList,
                        tools = BuildTools(),
                        tool_choice = "auto",
                        max_tokens = settings.MaxTokens,
                        temperature = settings.Temperature,
                        stream = true
                    };
                }
                else
                {
                    payload = new
                    {
                        model = settings.Model,
                        messages = messageList,
                        max_tokens = settings.MaxTokens,
                        temperature = settings.Temperature,
                        stream = true
                    };
                }

                var json = JsonConvert.SerializeObject(payload);
                var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiUrl)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
                var httpResponse = await _client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cts.Token);
                httpResponse.EnsureSuccessStatusCode();

                var roundToolCalls = new List<JToken>();
                var roundText = "";

                using var stream = await httpResponse.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream, Encoding.UTF8);

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(line)) continue;
                    if (!line.StartsWith("data: ")) continue;

                    var data = line.Substring(6);
                    if (data == "[DONE]") break;

                    JObject chunk;
                    try { chunk = JObject.Parse(data); }
                    catch { continue; }

                    var choices = chunk["choices"]?[0];
                    if (choices == null) continue;

                    var delta = choices["delta"];
                    if (delta == null) continue;

                    var deltaContent = delta["content"]?.ToString();
                    if (deltaContent != null)
                    {
                        roundText += deltaContent;
                        accumulatedText += deltaContent;
                    }

                    var deltaToolCalls = delta["tool_calls"];
                    if (deltaToolCalls != null)
                    {
                        foreach (var dtc in deltaToolCalls)
                        {
                            var idx = dtc["index"]?.ToObject<int>() ?? 0;
                            while (roundToolCalls.Count <= idx)
                                roundToolCalls.Add(new JObject());

                            var existing = (JObject)roundToolCalls[idx];
                            var funcDelta = dtc["function"];
                            if (funcDelta?["name"] != null)
                            {
                                existing["id"] = dtc["id"];
                                existing["type"] = "function";
                                existing["function"] = new JObject
                                {
                                    ["name"] = funcDelta["name"].ToString(),
                                    ["arguments"] = funcDelta["arguments"]?.ToString() ?? ""
                                };
                            }
                            else if (funcDelta?["arguments"] != null)
                            {
                                var func = existing["function"] as JObject;
                                if (func != null)
                                    func["arguments"] = (func["arguments"]?.ToString() ?? "") + funcDelta["arguments"].ToString();
                            }
                            if (dtc["index"] == null)
                            {
                                existing["id"] = dtc["id"];
                                existing["type"] = "function";
                            }
                        }
                    }
                }

                if (roundToolCalls.Count == 0)
                {
                    return new ChatResponse
                    {
                        Content = string.IsNullOrEmpty(accumulatedText) ? "（领主沉默不语）" : accumulatedText,
                        LearnedKnowledge = learnedKnowledge,
                        ToolCalls = allToolCalls,
                        ToolResults = toolResults
                    };
                }

                var roundToolCallsObj = new JArray(roundToolCalls);
                messageList.Add(new { role = "assistant", content = roundText, tool_calls = roundToolCallsObj });

                foreach (var rt in roundToolCalls)
                {
                    var callId = rt["id"]?.ToString() ?? Guid.NewGuid().ToString("N");
                    var func = rt["function"];
                    var funcName = func?["name"]?.ToString() ?? "";
                    var argsStr = func?["arguments"]?.ToString() ?? "{}";

                    allToolCalls.Add(new ToolCallData { Id = callId, Name = funcName, Arguments = argsStr });

                    if (funcName == "update_knowledge")
                    {
                        try { var args = JObject.Parse(argsStr); learnedKnowledge = args["knowledge"]?.ToString(); }
                        catch { }
                        toolResults[callId] = "已记录。";
                        messageList.Add(new { role = "tool", tool_call_id = callId, content = "已记录。" });
                    }
                    else
                    {
                        var toolResult = ExecuteToolCall(funcName, argsStr);
                        toolResults[callId] = toolResult;
                        messageList.Add(new { role = "tool", tool_call_id = callId, content = toolResult });
                    }
                }
            }

            return new ChatResponse
            {
                Content = string.IsNullOrEmpty(accumulatedText) ? "（领主沉默不语）" : accumulatedText,
                LearnedKnowledge = learnedKnowledge,
                ToolCalls = allToolCalls,
                ToolResults = toolResults
            };
        }

        public static async Task<string> TestFunctionCalling()
        {
            var settings = MySettings.Instance!;
            var payload = new
            {
                model = settings.Model,
                messages = new[]
                {
                    new { role = "user", content = "我叫炎瑰。现在请用一句话跟我打招呼并介绍你自己，同时调用 update_knowledge 函数记录你对我的认知。" }
                },
                tools = BuildTools(),
                temperature = 0.7f,
                max_tokens = 300
            };

            var json = JsonConvert.SerializeObject(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            var response = await _client.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(responseBody);
            var message = result["choices"]?[0]?["message"];

            var toolCalls = message?["tool_calls"];
            if (toolCalls == null || !toolCalls.Any())
                return "模型不支持 function calling，或该模型未启用此功能。";

            var func = toolCalls[0]?["function"];
            var name = func?["name"]?.ToString();
            var args = func?["arguments"]?.ToString() ?? "{}";
            var parsed = JObject.Parse(args);

            return $"支持 function calling。\n函数名: {name}\n参数: knowledge={parsed["knowledge"]}";
        }

        public static async void TestConnection()
        {
            var settings = MySettings.Instance!;
            InformationManager.DisplayMessage(new InformationMessage(
                "[MyFirstMod] 正在测试 API 连接...",
                Colors.Cyan));

            try
            {
                var normalTest = await SendMessage(new CharacterPrompt
                {
                    HeroName = "测试领主",
                    ChatHistory = new List<ChatHistoryEntry>
                    {
                        new() { Role = "user", Content = "你好，请用一句话介绍卡拉迪亚大陆。" }
                    }
                });

                InformationManager.DisplayMessage(new InformationMessage(
                    $"[MyFirstMod] 连接成功！回复：{normalTest.Content}",
                    Colors.Green));

                InformationManager.DisplayMessage(new InformationMessage(
                    "[MyFirstMod] 正在检测 function calling 支持...",
                    Colors.Cyan));

                var fcResult = await TestFunctionCalling();
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[MyFirstMod] {fcResult}",
                    Colors.Green));
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[MyFirstMod] 连接失败：{ex.Message}",
                    Colors.Red));
            }
        }
    }
}
