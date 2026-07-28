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
            if (entityId == null) return null;

            var entity = EntityManager.GetEntityById(entityId);
            var hero = entity?.HeroRef;
            return hero?.PartyBelongedTo;
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

            var response = await _client.SendAsync(request);
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

            var response = await _client.SendAsync(request);
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
