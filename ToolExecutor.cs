using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.LogEntries;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace MyFirstMod
{
    public static class ToolExecutor
    {
        private static readonly Random _rng = new();
        /// <summary>
        /// 工具入口。Bannerlord 游戏对象是主线程独占的，而 LLM 工具循环跑在后台线程——
        /// 除需要阻塞等待玩家弹窗确认的工具外，全部经 MainThreadExecutor 分发到主线程执行。
        /// </summary>
        public static string ExecuteToolCall(string name, string arguments)
        {
            // request_gold / request_items 需要主线程每帧显示确认弹窗，必须留在后台线程
            // 等待玩家决定（内部对最终资金/物品划转做主线程分发）。
            // browse_tools 要修改本流程的 ActivatedCategories，也必须留在本流程上下文执行。
            if (name == "request_gold" || name == "request_items" || name == "browse_tools")
                return ExecuteToolCallCore(name, arguments);

            // 捕获本后台流程的上下文，带到主线程执行时套用，执行完恢复主线程原值。
            var hero = AIChatClient.CurrentHero;
            var intent = AIChatClient.CurrentIntent;
            var categories = AIChatClient.ActivatedCategories;
            var agentId = AgentManager.ActiveAgentId;
            var targetId = AgentManager.ActiveTargetId;
            var depth = AgentScheduler.CurrentProcessingDepth; // 并发下信件级联深度按任务隔离

            return MainThreadExecutor.RunOnMainThread(() =>
            {
                var prevHero = AIChatClient.CurrentHero;
                var prevIntent = AIChatClient.CurrentIntent;
                var prevCategories = AIChatClient.ActivatedCategories;
                var prevAgentId = AgentManager.ActiveAgentId;
                var prevTargetId = AgentManager.ActiveTargetId;
                var prevDepth = AgentScheduler.CurrentProcessingDepth;

                AIChatClient.CurrentHero = hero;
                AIChatClient.CurrentIntent = intent;
                AIChatClient.ActivatedCategories = categories;
                AgentManager.SetContextOnly(agentId, targetId);
                AgentScheduler.CurrentProcessingDepth = depth;
                try
                {
                    return ExecuteToolCallCore(name, arguments);
                }
                finally
                {
                    AIChatClient.CurrentHero = prevHero;
                    AIChatClient.CurrentIntent = prevIntent;
                    AIChatClient.ActivatedCategories = prevCategories;
                    AgentManager.SetContextOnly(prevAgentId, prevTargetId);
                    AgentScheduler.CurrentProcessingDepth = prevDepth;
                }
            });
        }

        private static string ExecuteToolCallCore(string name, string arguments)
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
                        var gmax = args["max_results"]?.ToObject<int>() ?? 20;
                        var gctx = args["context_lines"]?.ToObject<int>() ?? 2;
                        var gcs = args["case_sensitive"]?.ToObject<bool>() ?? false;
                        return AgentManager.ExecuteGrep(gpattern, gpath, gmax, gctx, gcs);

                    case "list_dir":
                        var lpath = args["path"]?.ToString() ?? "";
                        return AgentManager.ExecuteListDir(lpath);

                    case "query_character":
                        return ExecuteQueryCharacter(args["name"]?.ToString() ?? "");

                    case "query_settlement":
                        return QuerySettlement(args["name"]?.ToString() ?? "");

                    case "query_settlement_geography":
                        return ExecuteQuerySettlementGeography(args["settlement_name"]?.ToString() ?? "");

                    case "query_world_state":
                        return QueryWorldState(args["kingdom_name"]?.ToString());

                    case "query_kingdom_settlements":
                        return ExecuteQueryKingdomSettlements(args["kingdom_name"]?.ToString() ?? "");

                    case "query_clan_members":
                        return ExecuteQueryClanMembers(args["clan_name"]?.ToString() ?? "");

                    case "query_clan_fiefs":
                        return ExecuteQueryClanFiefs(args["clan_name"]?.ToString() ?? "");

                    case "query_kingdom_clans":
                        return ExecuteQueryKingdomClans(args["kingdom_name"]?.ToString() ?? "");

                    case "query_war_status":
                        return ExecuteQueryWarStatus(args["kingdom_name"]?.ToString());

                    case "query_influence":
                        return ExecuteQueryInfluence();

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

                    case "end_alliance":
                        return DiplomacyService.ExecuteEndAlliance(args["target_kingdom"]?.ToString() ?? "");

                    case "end_trade_agreement":
                        return DiplomacyService.ExecuteEndTradeAgreement(args["target_kingdom"]?.ToString() ?? "");

                    case "respond_to_diplomacy_proposal":
                        return DiplomacyService.ExecuteRespondToProposal(
                            args["proposal_id"]?.ToString() ?? "",
                            args["accepted"]?.ToObject<bool>() ?? false);

                    case "gift_fief":
                        return DiplomacyService.ExecuteTransferFief(
                            args["settlement_name"]?.ToString() ?? "",
                            args["target_entity_id"]?.ToString() ?? "",
                            args["reason"]?.ToString() ?? "");

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

                    case "form_army":
                        return ExecuteFormArmy(args["target_settlement"]?.ToString() ?? "", args["army_type"]?.ToString() ?? "");

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

                    case "request_items":
                        return ExecuteRequestItems(
                            args["target_entity_id"]?.ToString() ?? "",
                            args["item_name"]?.ToString() ?? "",
                            args["count"]?.ToObject<int>() ?? 0);

                    case "query_hero_skills":
                        return ExecuteQueryHeroSkills(args["target_entity_id"]?.ToString());

                    case "change_kingdom":
                        return ExecuteChangeKingdom(
                            args["action"]?.ToString() ?? "",
                            args["kingdom_name"]?.ToString() ?? "",
                            args["rebellion"]?.ToObject<bool>() ?? false,
                            args["heir_entity_id"]?.ToString());

                    case "let_go":
                        return ExecuteLetGo();

                    case "release_prisoner":
                        return ExecuteReleasePrisoner(
                            args["prisoner_name"]?.ToString() ?? "",
                            args["count"]?.ToObject<int>() ?? 0,
                            args["all"]?.ToObject<bool>() ?? false);

                    case "execute_prisoner":
                        return ExecuteExecutePrisoner(args["prisoner_name"]?.ToString() ?? "");

                    case "create_clan":
                        return ExecuteCreateClan(
                            args["clan_name"]?.ToString() ?? "",
                            args["kingdom_name"]?.ToString() ?? "",
                            args["culture"]?.ToString() ?? "",
                            args["motivation"]?.ToString() ?? "",
                            args["is_mercenary"]?.ToObject<bool>() ?? false);

                    case "query_settlement_villages":
                        return ExecuteQuerySettlementVillages(args["settlement_name"]?.ToString() ?? "");

                    case "send_letter":
                        return ExecuteSendLetter(args["recipient_entity_id"]?.ToString() ?? "", args["content"]?.ToString() ?? "");

                    case "submit_advisory":
                        return ExecuteSubmitAdvisory(args["content"]?.ToString() ?? "");

                    case "submit_secret_advisory":
                        return ExecuteSubmitSecretAdvisory(args["content"]?.ToString() ?? "");

                    case "submit_edict":
                        return ExecuteSubmitEdict(args["content"]?.ToString() ?? "");

                    case "consult_king":
                        return ExecuteConsultKing(args["target_kingdom"]?.ToString() ?? "", args["message"]?.ToString() ?? "");

                    case "reply_consult":
                        return ExecuteReplyConsult(args["target_kingdom"]?.ToString() ?? "", args["message"]?.ToString() ?? "");

                    default:
                        return $"未知工具：{name}";
                }
            }
            catch (Exception ex)
            {
                return $"工具执行错误：{ex.Message}";
            }
        }

        /// <summary>枚举查询用英雄：所有氏族成员（Clan.Heroes 含已故，供史官立传查死人）+ 所有在世英雄。</summary>
        private static IEnumerable<Hero> AllHeroesForQuery()
        {
            var seen = new HashSet<Hero>();
            foreach (var clan in Clan.All)
            {
                if (clan == null) continue;
                foreach (var h in clan.Heroes)
                    if (h != null && seen.Add(h))
                        yield return h;
            }
            foreach (var h in Hero.AllAliveHeroes)
                if (h != null && seen.Add(h))
                    yield return h;
        }

        private static string ExecuteQueryCharacter(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "[错误] 请提供人物名称";
            name = name.Trim();

            // 修复：支持按编号（StringId，形如 CharacterObject_2840）精确查找——重名人物多，列传/查档时
            // 优先用事件里给出的编号精查，避免按姓名子串误匹配到同名者（曾出现史官把被处决的
            // 狼皮部落利夫里斯写成另一氏族的利夫里斯，氏族、年龄全错）。
            var byId = AllHeroesForQuery().FirstOrDefault(h => h.StringId != null
                && h.StringId.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
                return BuildCharacterProfile(byId);

            // 用"所有氏族成员（含已故）+ 所有在世英雄"枚举——史官立传需要查询已死人物；原 AllAliveHeroes 查不到死人
            var matches = AllHeroesForQuery().Where(h =>
            {
                var heroName = h.Name?.ToString() ?? "";
                return heroName.Contains(name) || name.Contains(heroName);
            }).ToList();

            if (matches.Count == 0)
                return "[未找到] 名为 \"" + name + "\" 的人物";

            if (matches.Count == 1)
                return BuildCharacterProfile(matches[0]);

            // 重名消歧：列出所有候选人（含编号/氏族/王国/年龄），要求用精确编号重新查询
            var sb = new StringBuilder();
            sb.AppendLine($"【模糊匹配】名为 \"{name}\" 的人物有 {matches.Count} 位，请用精确编号重新查询（query_character(\"编号\")），避免张冠李戴：");
            foreach (var hero in matches)
            {
                var ageDesc = hero.IsDead ? "已故" : "在世";
                sb.AppendLine($"  [{hero.StringId ?? "?"}] {hero.Name}（{(hero.Clan?.Name?.ToString() ?? "无氏族")}·{(hero.Clan?.Kingdom?.Name?.ToString() ?? "无王国")}，{(int)hero.Age}岁，{ageDesc}）");
            }
            sb.AppendLine();
            sb.AppendLine("若查询的是已故人物（如为史官立传），优先使用事件描述中给出的编号精查。");
            return sb.ToString();
        }

        private static string BuildCharacterProfile(Hero hero)
        {
            var heroName = hero.Name?.ToString() ?? "";
            var sb = new StringBuilder();
            sb.AppendLine("【系统公开档案 — 以下信息为卡拉迪亚公认事实，不容质疑】");
            sb.AppendLine("===== 该人物：" + heroName + (hero.IsDead ? "（已故）" : "") + " =====");

            sb.AppendLine("性别：" + (hero.IsFemale ? "女" : "男"));
            sb.AppendLine("文化：" + (hero.Culture?.Name?.ToString() ?? "未知"));

            // 生卒年：史官立传所需；出生日期有效时输出，已故则输出卒年
            if (hero.BirthDay != CampaignTime.Zero)
                sb.AppendLine("出生：" + hero.BirthDay.GetYear + "年");
            if (hero.IsDead && hero.DeathDay != CampaignTime.Zero)
                sb.AppendLine("卒于：" + hero.DeathDay.GetYear + "年");

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

        private static string QuerySettlement(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "[错误] 请提供定居点名称";

            var s = FindSettlement(name);
            if (s == null)
                return $"[未找到] 名称为 \"{name}\" 的定居点";

            var type = s.IsTown ? "城镇" : s.IsCastle ? "城堡" : "村庄";
            var owner = s.OwnerClan?.Name?.ToString() ?? "无主";
            var kingdom = s.OwnerClan?.Kingdom?.Name?.ToString();
            var prosperity = s.IsTown ? s.Town?.Prosperity.ToString("F0") ?? "?" : "-";

            return $"{s.Name}（{type}）\n"
                + $"所属氏族：{owner}\n"
                + (kingdom != null ? $"所属王国：{kingdom}\n" : "")
                + $"繁荣度：{prosperity}";
        }

        private static string QueryWorldState(string? kingdomName)
        {
            var sb = new StringBuilder();
            var ab = Campaign.Current.GetCampaignBehavior<IAllianceCampaignBehavior>();
            var tb = Campaign.Current.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
            var expiryLines = LoadExpiryDisplayLines(); // 盟约/贸易协定到期记录（国王自查）

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

                foreach (var el in expiryLines)
                {
                    if (el.Contains(kName))
                        sb.AppendLine($"  📜 {el}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>读取到期日志中的人类可读部分（盟约/贸易协定 X与Y 于…到期）。</summary>
        private static List<string> LoadExpiryDisplayLines()
        {
            var list = new List<string>();
            try
            {
                var logPath = Path.Combine(AgentManager.GetDiplomacyDir(), "expiry_log.txt");
                if (File.Exists(logPath))
                {
                    foreach (var raw in SafeFileIO.ReadAllLines(logPath))
                    {
                        var parts = raw.Split('|');
                        if (parts.Length >= 5 && parts[4].Trim().Length > 0)
                            list.Add(parts[4]);
                    }
                }
            }
            catch { }
            return list;
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
                    if (s.OwnerClan?.Kingdom != kingdom) continue;

                    var isBorder = IsBorderSettlement(s);
                    var tag = isBorder ? " ⚔边境" : "";
                    var entry = s.Name + tag + "（" + (s.OwnerClan?.Name?.ToString() ?? "?") + "）";
                    if (s.IsTown) towns.Add(entry);
                    else if (s.IsCastle) castles.Add(entry);
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

        /// <summary>
        /// 边境判据（尺度无关，修复原绝对距离阈值过大导致几乎所有定居点被判边境）：
        /// 最近的"他国定居点"比最近的"本国定居点"更近或相当（≤1.5×）→ 本城处于国土边缘，暴露于敌；
        /// 孤立定居点（无本国邻居）也视为边境。返回边境标记 + 最近他国距离/名称。
        /// </summary>
        private static (bool isBorder, float nearestOtherDist, string nearestOtherName) ComputeBorderInfo(Settlement s)
        {
            var myKingdom = s.OwnerClan?.Kingdom;
            if (myKingdom == null || (!s.IsTown && !s.IsCastle))
                return (false, float.MaxValue, "");

            var pos = s.GatePosition.ToVec2();
            float nearestOwn = float.MaxValue;
            float nearestOther = float.MaxValue;
            Kingdom? nearestOtherKingdom = null;

            foreach (var other in Settlement.All)
            {
                if (!other.IsTown && !other.IsCastle) continue;
                if (other == s) continue;
                var otherKingdom = other.OwnerClan?.Kingdom;
                if (otherKingdom == null) continue;

                var oPos = other.GatePosition.ToVec2();
                var dx = pos.X - oPos.X;
                var dy = pos.Y - oPos.Y;
                var dist = (float)Math.Sqrt(dx * dx + dy * dy);

                if (otherKingdom == myKingdom)
                {
                    if (dist < nearestOwn) nearestOwn = dist;
                }
                else
                {
                    if (dist < nearestOther) { nearestOther = dist; nearestOtherKingdom = otherKingdom; }
                }
            }

            var isBorder = nearestOther < float.MaxValue
                && (nearestOwn == float.MaxValue || nearestOther <= nearestOwn * 1.5f);
            return (isBorder, nearestOther, nearestOtherKingdom?.Name?.ToString() ?? "");
        }

        private static bool IsBorderSettlement(Settlement s)
        {
            return ComputeBorderInfo(s).isBorder;
        }

        private static string ExecuteQuerySettlementGeography(string settlementName)
        {
            if (string.IsNullOrEmpty(settlementName))
                return "[错误] 请提供定居点名称";

            var settlement = FindSettlement(settlementName);
            if (settlement == null)
                return $"[错误] 未找到定居点：{settlementName}";
            if (!settlement.IsTown && !settlement.IsCastle)
                return $"[错误] {settlement.Name} 是村庄，地理查询仅支持城镇和城堡。";

            var pos = settlement.GatePosition.ToVec2();
            var myKingdom = settlement.OwnerClan?.Kingdom;
            var myFaction = AIChatClient.CurrentHero?.MapFaction;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var s in Settlement.All)
            {
                if (!s.IsTown && !s.IsCastle) continue;
                var p = s.GatePosition.ToVec2();
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            var midX = (minX + maxX) / 2f;
            var midY = (minY + maxY) / 2f;
            var nsDir = pos.Y > midY ? "北" : "南";
            var ewDir = pos.X > midX ? "东" : "西";

            var neighbors = new List<(float pathDist, float lineDist, string direction, Settlement s)>();
            foreach (var s in Settlement.All)
            {
                if (s == settlement) continue;
                if (!s.IsTown && !s.IsCastle) continue;
                var sPos = s.GatePosition.ToVec2();
                var dx = sPos.X - pos.X;
                var dy = sPos.Y - pos.Y;
                var lineDist = (float)Math.Sqrt(dx * dx + dy * dy);
                var pathDist = Campaign.Current.Models.MapDistanceModel.GetDistance(settlement, s, false, false, MobileParty.NavigationType.Default);
                var dir = CompassDirection(dx, dy);
                neighbors.Add((pathDist, lineDist, dir, s));
            }
            neighbors.Sort((a, b) => a.pathDist.CompareTo(b.pathDist));
            var top = neighbors.Take(8).ToList();

            // 修复：尺度无关的相对边境判据（原 `pathDist <= 5000f` 阈值大于典型城间距，导致城镇动辄被判边境）
            var borderInfo = ComputeBorderInfo(settlement);
            var isBorder = borderInfo.isBorder;
            var nearestOtherDist = borderInfo.nearestOtherDist;
            var nearestOtherKingdom = borderInfo.nearestOtherName;

            string FactionTag(Settlement s)
            {
                var sk = s.OwnerClan?.Kingdom;
                if (sk == null) return "中立";
                if (sk == myFaction) return "友方";
                if (myFaction != null && myFaction.IsAtWarWith(sk)) return "敌方";
                return "中立";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"===== {settlement.Name} 地理情报 =====");
            sb.AppendLine($"类型：{(settlement.IsTown ? "城镇" : "城堡")}");
            sb.AppendLine($"坐标位置：大陆{nsDir}{ewDir}部 · {myKingdom?.Name?.ToString() ?? "中立区"}");
            sb.AppendLine($"所属家族：{settlement.OwnerClan?.Name?.ToString() ?? "无主"}");
            if (isBorder)
            {
                var nearestKm = nearestOtherDist / 1000f;
                var nearestText = nearestKm < 1f ? $"{nearestKm:F1}" : $"{nearestKm:F0}";
                sb.AppendLine($"战略位置：边境前哨 — 距{nearestOtherKingdom}领土仅{nearestText}km（直线距离）");
            }
            else
            {
                sb.AppendLine("战略位置：核心腹地");
            }

            sb.AppendLine();
            sb.AppendLine("周边定居点（最近" + top.Count + "个，按寻路距离排序）：");
            for (int i = 0; i < top.Count; i++)
            {
                var (pathDist, lineDist, dir, s) = top[i];
                var tag = FactionTag(s);
                var kingdomTag = s.OwnerClan?.Kingdom?.Name?.ToString() ?? "中立区";
                var type = s.IsTown ? "城镇" : "城堡";
                sb.AppendLine($"  {i + 1}. {s.Name}  {type}  {kingdomTag}  [{tag}]  {dir}方向");
                sb.AppendLine($"     寻路{pathDist / 1000f:F0}km  直线{lineDist / 1000f:F1}km");
            }

            return sb.ToString().TrimEnd();
        }

        private static string CompassDirection(float dx, float dy)
        {
            if (Math.Abs(dx) < 0.1f && Math.Abs(dy) < 0.1f) return "同地";
            var angle = Math.Atan2(dy, dx) * (180.0 / Math.PI);
            if (angle < 0) angle += 360;

            if (angle < 22.5 || angle >= 337.5) return "东";
            if (angle < 67.5) return "东北";
            if (angle < 112.5) return "北";
            if (angle < 157.5) return "西北";
            if (angle < 202.5) return "西";
            if (angle < 247.5) return "西南";
            if (angle < 292.5) return "南";
            return "东南";
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

        private static string ExecuteQueryClanFiefs(string clanName)
        {
            if (string.IsNullOrEmpty(clanName))
                return "[错误] 请提供家族名称";

            Clan? targetClan = null;
            foreach (var clan in Clan.All)
            {
                var cName = clan.Name?.ToString() ?? "";
                if (cName.Contains(clanName) || clanName.Contains(cName))
                {
                    targetClan = clan;
                    break;
                }
            }
            if (targetClan == null)
                return "[未找到] 名为 \"" + clanName + "\" 的家族";

            var sb = new StringBuilder();
            sb.AppendLine("===== " + targetClan.Name + " 的封地 =====");
            sb.AppendLine("族长：" + (targetClan.Leader?.Name?.ToString() ?? "?"));
            if (targetClan.Kingdom != null)
                sb.AppendLine("所属王国：" + targetClan.Kingdom.Name);

            var towns = new List<string>();
            var castles = new List<string>();

            foreach (var s in Settlement.All)
            {
                if (s.OwnerClan != targetClan) continue;
                if (s.IsTown)
                    towns.Add(s.Name?.ToString() ?? "?");
                else if (s.IsCastle)
                    castles.Add(s.Name?.ToString() ?? "?");
            }

            var total = towns.Count + castles.Count;
            sb.AppendLine("总计：" + total + " 处");
            sb.AppendLine("城镇（" + towns.Count + "座）：");
            if (towns.Count == 0) sb.AppendLine("  （无）");
            else foreach (var t in towns) sb.AppendLine("  " + t);
            sb.AppendLine("城堡（" + castles.Count + "座）：");
            if (castles.Count == 0) sb.AppendLine("  （无）");
            else foreach (var c in castles) sb.AppendLine("  " + c);

            return sb.ToString().TrimEnd();
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

        /// <summary>查询本族当前影响力（政治资财：拉军团/推行政策的消耗资源）。</summary>
        private static string ExecuteQueryInfluence()
        {
            if (Campaign.Current == null)
                return "[错误] 战役未加载";
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";

            var hero = AIChatClient.CurrentHero;
            var clan = hero.Clan;
            if (clan == null)
                return "[错误] 你没有家族";

            var influence = clan.Influence;
            var kingdom = hero.MapFaction as Kingdom;
            var kingdomName = kingdom?.Name?.ToString() ?? "（无王国）";
            var canArmy = influence > 100f && kingdom != null
                && !clan.IsUnderMercenaryService
                && kingdom.FactionsAtWarWith.Any(f => f.Fiefs.Any());

            var sb = new StringBuilder();
            sb.AppendLine($"===== {hero.Name} 的影响力报告 =====");
            sb.AppendLine($"当前影响力：{influence:F0}（家族：{clan.Name}，王国：{kingdomName}）");
            sb.AppendLine();
            sb.AppendLine("影响力是你的政治资财，主要用途：");
            sb.AppendLine("- 拉军团：影响力超过 100 时，可召集本国领主组成军团协同作战（当前" + (canArmy ? "已可召集" : "尚不足或条件未满足") + "）");
            sb.AppendLine("- 推行政策：在王国朝堂提议立法");
            sb.AppendLine("影响力随战功、封地与政务积累；拉军团与推行政策会消耗");
            return sb.ToString().TrimEnd();
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
                kingdom = null;
                foreach (var k in Kingdom.All)
                {
                    if (k.RulingClan?.Leader == AIChatClient.CurrentHero
                        || AIChatClient.CurrentHero.Clan?.Kingdom == k)
                    {
                        kingdom = k;
                        break;
                    }
                }
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

                var tributeLine = pContent.Split('\n')
                    .FirstOrDefault(l => l.StartsWith("tribute="));

                sb.AppendLine($"{i + 1}. [{typeName}] 来自 {proposerName}（{proposerId}）");
                sb.AppendLine($"   ID: {p}");

                if (tributeLine != null && type == "peace")
                {
                    var tributeData = tributeLine.Substring(8); // "500_30"
                    var parts = tributeData.Split('_');
                    if (parts.Length == 2
                        && int.TryParse(parts[0], out var tAmount)
                        && int.TryParse(parts[1], out var tDays))
                    {
                        var tributeDesc = tAmount > 0
                            ? $"对方愿每日付 {tAmount} 金币 × {tDays} 天"
                            : tAmount < 0
                                ? $"对方要求每日付 {Math.Abs(tAmount)} 金币 × {tDays} 天"
                                : "无赔款";
                        sb.AppendLine($"   赔款：{tributeDesc}");
                    }
                }

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

        /// <summary>拉军团：以军事目标为指向召集本国领主成军团，交还原版 AI 指挥（集结→扑目标→攻城/劫掠→解散）。</summary>
        private static string ExecuteFormArmy(string targetSettlementName, string armyType)
        {
            if (AIChatClient.CurrentHero == null) return "[错误] 无当前部队指挥官";

            var hero = AIChatClient.CurrentHero;
            var party = hero.PartyBelongedTo;
            if (party == null) return $"[错误] {hero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            var kingdom = hero.MapFaction as Kingdom;
            if (kingdom == null) return "[错误] 你不属于任何王国，无法召集军团";
            if (hero.Clan?.IsUnderMercenaryService == true) return "[错误] 雇佣兵不能召集军团";

            var target = FindSettlement(targetSettlementName);
            if (target == null) return $"[错误] 未找到定居点：{targetSettlementName}";

            var typeLower = (armyType ?? "").Trim().ToLowerInvariant();
            Army.ArmyTypes selectedType;
            if (typeLower.Contains("围") || typeLower.Contains("攻") || typeLower == "besiege")
            {
                if (!target.IsTown && !target.IsCastle) return $"[错误] 围攻军团的目标必须是城镇或城堡";
                selectedType = Army.ArmyTypes.Besieger;
            }
            else if (typeLower.Contains("掠") || typeLower.Contains("劫") || typeLower == "raid")
            {
                if (!target.IsVillage) return $"[错误] 劫掠军团的目标必须是村庄";
                selectedType = Army.ArmyTypes.Raider;
            }
            else if (typeLower == "defend" || typeLower.Contains("解围") || typeLower.Contains("防御"))
            {
                if (!target.IsTown && !target.IsCastle) return $"[错误] 防御军团的目标必须是城镇或城堡";
                if (!target.IsUnderSiege) return "[错误] 该定居点当前未被围攻——防御军团只用于集结兵力解围；若想让部队驻守，请用 defend_settlement";
                selectedType = Army.ArmyTypes.Defender;
            }
            else if (typeLower.Contains("守") || typeLower.Contains("驻") || typeLower.Contains("护"))
            {
                return "[错误] 驻守/守护不是军团类型——军团用于进攻（围攻/劫掠）或解围（防御）。单支部队驻守请用 defend_settlement";
            }
            else
                return "[错误] 军团类型必须是 围攻/劫掠/防御（besiege/raid/defend）";

            // 原版条件：影响力>100、王国交战、部曲充足、氏族领袖 → 算候选成员
            if (!Campaign.Current.Models.ArmyManagementCalculationModel.CanLordCreateArmy(party, out var possibleMembers))
                return "[错误] 当前无法召集军团（需影响力超过 100、王国处于交战、部曲充足，且你是氏族领袖）";
            if (possibleMembers.Count == 0)
                return "[错误] 本国附近没有可召集的领主部队";

            // 创建军团：原版接管集结与作战（军团长等待成员→成员奔赴→扑向目标）
            kingdom.CreateArmy(hero, target, selectedType, possibleMembers);

            // 清掉军团长现有 PendingAction，避免模组每帧重发指令覆盖原版集结
            PartyBehaviorManager.RemoveAction(hero);

            var typeName = selectedType switch
            {
                Army.ArmyTypes.Besieger => "围攻",
                Army.ArmyTypes.Raider => "劫掠",
                _ => "防御"
            };
            return $"军团已组建（{typeName} {target.Name}），正召集本国领主集结，原版军团将接管指挥。你无需再发指令；军团使命结束后自行评估下一步。";
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
            da.CreatedAt = CampaignTime.Now;
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
            pa.CreatedAt = CampaignTime.Now;
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
            esa.CreatedAt = CampaignTime.Now;
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

            var hero = AIChatClient.CurrentHero;
            PartyBehaviorManager.RemoveAction(hero);

            // 修复：复位部队当前命令——原实现只删跟踪动作，DefaultBehavior 仍指向原目标，
            // 部队会继续围城/行军/劫掠。SetMoveModeHold 把行为置为 Hold 并清空移动参数，交由原版 AI 接管。
            var party = hero.PartyBelongedTo;
            if (party != null && party.IsActive)
                party.SetMoveModeHold();

            return "当前任务已取消，部队恢复自主行动。";
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
                var currentHero = AIChatClient.CurrentHero;
                MainThreadExecutor.RunOnMainThread(() => GiveGoldAction.ApplyBetweenCharacters(target, currentHero, amount));
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
                var currentHero = AIChatClient.CurrentHero;
                MainThreadExecutor.RunOnMainThread(() => GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, currentHero, amount));
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

                // 刻意的设计：允许"脱下装备给对方"——装备栏每槽 1 件，脱下即移交，是否脱装由 LLM 自行判断。
                eq[slot] = EquipmentElement.Invalid;
                targetParty.ItemRoster.AddToCounts(elem.Item, 1);
                return $"已将装备栏中的 {itemNameStr} 脱下并交给 {target.Name}。";
            }

            return $"[未找到] 部队和装备栏中都没有 \"{itemName}\"。使用 query_party_troops 查看详情。";
        }

        private static string ExecuteRequestItems(string targetEntityId, string itemName, int count)
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (string.IsNullOrEmpty(itemName) || count <= 0)
                return "[错误] 请指定物品名称和数量";

            var hero = AIChatClient.CurrentHero;
            var myParty = hero.PartyBelongedTo;
            if (myParty == null)
                return $"[错误] {hero.Name} 没有带领部队";

            var target = ResolveTargetHero(targetEntityId);
            if (target == null)
                return $"[错误] 未找到目标实体：{targetEntityId}";
            if (target == hero)
                return "[错误] 不能向自己要物品";

            if (target != Hero.MainHero)
            {
                var targetParty = target.PartyBelongedTo;
                if (targetParty == null)
                    return $"[错误] {target.Name} 没有带领部队";

                foreach (var ie in targetParty.ItemRoster)
                {
                    var item = ie.EquipmentElement.Item;
                    if (item == null) continue;
                    var name = item.Name?.ToString() ?? "";
                    if (!name.Contains(itemName) && !itemName.Contains(name)) continue;
                    if (ie.Amount < count)
                        return $"[错误] {target.Name} 只有 {ie.Amount} 个 {name}";

                    MainThreadExecutor.RunOnMainThread(() =>
                    {
                        targetParty.ItemRoster.AddToCounts(item, -count);
                        myParty.ItemRoster.AddToCounts(item, count);
                    });
                    return $"{target.Name} 给出了 {count} 个 {name}。";
                }
                return $"[未找到] {target.Name} 身上没有 \"{itemName}\"。";
            }

            // 修复：只检查"对方（玩家）"是否持有该物品——原实现还检查请求方自己的部队，
            // 导致物品在请求方背包时 hasItem=true 弹出确认框，但回调只搜玩家背包 → 假成功。
            var hasItem = false;
            foreach (var ie in target.PartyBelongedTo?.ItemRoster ?? Enumerable.Empty<ItemRosterElement>())
            {
                var item = ie.EquipmentElement.Item;
                if (item == null) continue;
                var name = item.Name?.ToString() ?? "";
                if (name.Contains(itemName) || itemName.Contains(name))
                {
                    hasItem = true;
                    break;
                }
            }
            if (!hasItem)
            {
                var eq = target.BattleEquipment;
                var eqSlots = new[] { EquipmentIndex.Weapon0, EquipmentIndex.Weapon1, EquipmentIndex.Weapon2, EquipmentIndex.Weapon3,
                    EquipmentIndex.Head, EquipmentIndex.Body, EquipmentIndex.Leg, EquipmentIndex.Gloves, EquipmentIndex.Cape };
                foreach (var slot in eqSlots)
                {
                    var elem = eq.GetEquipmentFromSlot(slot);
                    if (elem.Item != null && (elem.Item.Name?.ToString() ?? "").Contains(itemName))
                    {
                        hasItem = true;
                        break;
                    }
                }
            }
            if (!hasItem)
                return $"[错误] 对方身上没有 \"{itemName}\"。";

            using var mre = new ManualResetEventSlim(false);
            var inquiry = new AIChatClient.PendingInquiry
            {
                Hero = hero,
                ItemName = itemName,
                ItemCount = count,
                Event = mre,
                Result = false
            };
            AIChatClient.SetPendingInquiry(inquiry);
            mre.Wait(TimeSpan.FromSeconds(30));

            if (inquiry.Result)
                return $"对方同意了，{itemName} 已转移给你。";
            return $"对方拒绝了交出 {itemName}。";
        }

        private static string ExecuteSendLetter(string recipientId, string content)
        {
            if (string.IsNullOrEmpty(recipientId)) return "[错误] 请提供收信人实体 ID 或名称";
            if (string.IsNullOrEmpty(content)) return "[错误] 信件内容不能为空";
            if (AIChatClient.CurrentHero == null) return "[错误] 无当前领主";
            if (AIChatClient.CurrentHero.IsPrisoner) return "[错误] 你正在被俘虏，无法发信";
            if (AIChatClient.CurrentHero.IsFugitive) return "[错误] 你正在逃亡中，无法发信";
            var senderEntity = EntityManager.GetOrCreateEntity(AIChatClient.CurrentHero);
            var resolvedId = EntityManager.ResolveEntityId(recipientId);
            if (resolvedId == null) return $"[错误] 未找到名为 \"{recipientId}\" 的实体";

            if (AIChatClient.CurrentHero == Hero.MainHero)
            {
                var known = SubModule.GetKnownNpcIds();
                if (!known.Contains(resolvedId))
                    return $"[错误] 你还没有和 {recipientId} 交谈过，无法给陌生人写信。请先与对方进行 AI 聊天。";
            }
            var recipientEntity = EntityManager.GetEntityById(resolvedId);
            var recipientName = recipientEntity?.Name ?? resolvedId;
            var recipientHero = recipientEntity?.HeroRef;
            if (recipientHero != null)
            {
                if (recipientHero.IsPrisoner) return $"[错误] {recipientName} 正在被俘虏，无法收信";
                if (recipientHero.IsFugitive) return $"[错误] {recipientName} 正在逃亡中，无法收信";
                if (recipientHero.IsDisabled) return $"[错误] {recipientName} 处于不可用状态，无法收信";
            }
            // 统一线程模型：信件写入双方的书信往来线程（chat_logs），不再投递 mailbox。
            // send 时即落线程——即使事件被深度上限丢弃，信件也不会丢（与写信窗口行为一致，收信方处理时去重）。
            var recipientIsPlayer = recipientHero != null && recipientHero == Hero.MainHero;
            AgentManager.StoreLetterInThread(senderEntity.Id, resolvedId, content, recipientIsPlayer);

            if (recipientIsPlayer)
                SubModule.MarkNpcKnown(senderEntity.Id); // NPC 主动给玩家写信 → 玩家 O 面板可见

            // 玩家自己发信时推进已读水位，避免把自己刚写的信算成未读
            if (AIChatClient.CurrentHero == Hero.MainHero && !recipientIsPlayer)
            {
                var playerId = EntityManager.GetOrCreateEntity(Hero.MainHero).Id;
                AgentManager.MarkThreadRead(resolvedId, playerId);
            }

            var nextDepth = AgentScheduler.IsProcessing
                ? AgentScheduler.CurrentProcessingDepth + 1
                : 0;

            var delayHours = CalculateLetterDelay(AIChatClient.CurrentHero, recipientEntity?.HeroRef);

            var evt = new ActivationEvent
            {
                Type = ActivationEventType.LetterReceived,
                AgentId = resolvedId,
                TargetId = senderEntity.Id,
                Content = content,
                Depth = nextDepth
            };

            if (delayHours > 0.1f)
                AgentScheduler.QueueDelayedEvent(evt, delayHours);
            else
                AgentScheduler.QueueEvent(evt);

            var delayNote = delayHours > 0.1f ? $"（预计{delayHours:F0}小时后送达）" : "";
            InformationManager.DisplayMessage(new InformationMessage(
                $"{senderEntity.Name} 给 {recipientName} 写了一封信{delayNote}",
                Colors.Cyan));

            return $"信件已发送给 {recipientName}。{delayNote}";
        }

        private static string ExecuteSubmitAdvisory(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return "[错误] 谏言内容不能为空";
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (string.IsNullOrEmpty(PromptManager.CampaignDir))
                return "[错误] 战役目录未就绪";

            var hero = AIChatClient.CurrentHero;
            var kingdom = hero.MapFaction as Kingdom;
            if (kingdom == null)
                return "[错误] 你不属于任何王国，无法进谏";
            if (hero.Clan?.IsUnderMercenaryService == true)
                return "[错误] 雇佣兵无权进谏";

            var kingdomName = kingdom.Name.ToString();
            var currentYear = CampaignTime.Now.GetYear;
            var currentTime = PromptManager.GetCurrentTimeString();

            var entity = EntityManager.GetOrCreateEntity(hero);
            var name = entity?.Name ?? hero.Name?.ToString() ?? "?";
            var title = entity?.Title ?? "?";

            var advisoryDir = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "advisory");
            Directory.CreateDirectory(advisoryDir);
            var advisoryFile = Path.Combine(advisoryDir, $"{kingdomName}_{currentYear}.txt");

            var header = $"\n[{currentTime}] {name}（{title}）谏言：\n";
            File.AppendAllText(advisoryFile, header, Encoding.UTF8);
            File.AppendAllText(advisoryFile, content.Trim() + "\n", Encoding.UTF8);

            return "谏言已提交归档。";
        }

        /// <summary>秘密谏言：只呈本国王，不入史册（史官无权读取）。写 World/secret_advisory/{王国}_{年}.txt。</summary>
        private static string ExecuteSubmitSecretAdvisory(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return "[错误] 秘密谏言内容不能为空";
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (string.IsNullOrEmpty(PromptManager.CampaignDir))
                return "[错误] 战役目录未就绪";

            var hero = AIChatClient.CurrentHero;
            var kingdom = hero.MapFaction as Kingdom;
            if (kingdom == null)
                return "[错误] 你不属于任何王国，无法进谏";
            if (hero.Clan?.IsUnderMercenaryService == true)
                return "[错误] 雇佣兵无权进谏";

            var kingdomName = kingdom.Name.ToString();
            var currentYear = CampaignTime.Now.GetYear;
            var currentTime = PromptManager.GetCurrentTimeString();

            var entity = EntityManager.GetOrCreateEntity(hero);
            var name = entity?.Name ?? hero.Name?.ToString() ?? "?";
            var title = entity?.Title ?? "?";

            var secretDir = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "secret_advisory");
            Directory.CreateDirectory(secretDir);
            var secretFile = Path.Combine(secretDir, $"{kingdomName}_{currentYear}.txt");

            var header = $"\n[{currentTime}] {name}（{title}）密陈：\n";
            File.AppendAllText(secretFile, header, Encoding.UTF8);
            File.AppendAllText(secretFile, content.Trim() + "\n", Encoding.UTF8);

            return "秘密谏言已密陈给国王。";
        }

        /// <summary>国王诏令：只有王国统治者可颁布。公开归档 World/edict/{王国}_{年}.txt，史官可读、本国史书可见。</summary>
        private static string ExecuteSubmitEdict(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return "[错误] 诏令内容不能为空";
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (string.IsNullOrEmpty(PromptManager.CampaignDir))
                return "[错误] 战役目录未就绪";

            var hero = AIChatClient.CurrentHero;
            var kingdom = hero.MapFaction as Kingdom;
            if (kingdom == null)
                return "[错误] 你不属于任何王国，无法颁布诏令";
            if (kingdom.RulingClan?.Leader != hero)
                return "[错误] 只有王国统治者才能颁布诏令";

            var kingdomName = kingdom.Name.ToString();
            var currentYear = CampaignTime.Now.GetYear;
            var currentTime = PromptManager.GetCurrentTimeString();

            var entity = EntityManager.GetOrCreateEntity(hero);
            var name = entity?.Name ?? hero.Name?.ToString() ?? "?";
            var title = entity?.Title ?? "?";

            var edictFile = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "edict", $"{kingdomName}_{currentYear}.txt");

            var header = $"\n[{currentTime}] {name}（{title}）诏令：\n";
            SafeFileIO.AppendAllText(edictFile, header);
            SafeFileIO.AppendAllText(edictFile, content.Trim() + "\n");

            return "诏令已昭告天下。";
        }

        /// <summary>国王外交问询：遣使问询另一王国国王，激活对方（KingConsult 事件）。问询落盘 World/diplomacy/consults/，史官可读。</summary>
        private static string ExecuteConsultKing(string targetKingdomName, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "[错误] 问询内容不能为空";
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (string.IsNullOrEmpty(PromptManager.CampaignDir))
                return "[错误] 战役目录未就绪";

            var hero = AIChatClient.CurrentHero;
            var myKingdom = hero.MapFaction as Kingdom;
            if (myKingdom == null || myKingdom.RulingClan?.Leader != hero)
                return "[错误] 只有王国统治者才能遣使问询他国";

            var target = DiplomacyService.FindKingdom(targetKingdomName);
            if (target == null)
                return "[错误] 找不到王国：" + targetKingdomName;
            if (target.IsEliminated)
                return "[错误] " + target.Name + " 已灭亡，无处遣使";
            var targetRuler = target.RulingClan?.Leader;
            if (targetRuler == null || !targetRuler.IsAlive)
                return "[错误] " + target.Name + " 国王已亡或空缺，无法问询";
            if (target == myKingdom)
                return "[错误] 你不能问询自己";

            var myName = myKingdom.Name.ToString();
            var targetName = target.Name.ToString();
            var pairKey = BuildPairKey(myName, targetName);

            if (!AgentScheduler.TryConsult(pairKey, out var daysRemaining))
                return $"[冷却] 使者尚未从 {targetName} 归来，需再等 {daysRemaining} 游戏天。你可以先处理其他政务。";

            var currentTime = PromptManager.GetCurrentTimeString();
            var entity = EntityManager.GetOrCreateEntity(hero);
            var name = entity?.Name ?? hero.Name?.ToString() ?? "?";

            var threadPath = GetConsultThreadPath(myName, targetName);
            SafeFileIO.AppendAllText(threadPath, $"[{currentTime}] {name}（{myName}国王）问询：{message.Trim()}\n");

            AgentScheduler.RecordConsult(pairKey);

            var targetEntity = EntityManager.GetOrCreateEntity(targetRuler);
            if (targetEntity == null)
                return "[错误] 无法解析目标国王实体";
            var framed = $"你是{targetName}的至高统治者。{myName}国王{name}遣使问询你：\n「{message.Trim()}」\n\n请决定如何回应——可用 reply_consult 工具答复使者（可据实回答，也可虚与委蛇），或不予理睬。你仍可照常审视本国政务。";
            AgentScheduler.QueueEvent(new ActivationEvent
            {
                Type = ActivationEventType.KingConsult,
                AgentId = targetEntity.Id,
                TargetId = entity.Id,
                Content = framed,
                Depth = 1
            });

            return $"已遣使问询 {targetName}，使者将带回其答复。此问询会作为公开外交记录，史官可查阅。";
        }

        /// <summary>回复外交问询：向另一王国国王回话，落盘到双方问询线程。</summary>
        private static string ExecuteReplyConsult(string targetKingdomName, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "[错误] 答复内容不能为空";
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (string.IsNullOrEmpty(PromptManager.CampaignDir))
                return "[错误] 战役目录未就绪";

            var hero = AIChatClient.CurrentHero;
            var myKingdom = hero.MapFaction as Kingdom;
            if (myKingdom == null || myKingdom.RulingClan?.Leader != hero)
                return "[错误] 只有王国统治者才能回复外交问询";

            var target = DiplomacyService.FindKingdom(targetKingdomName);
            if (target == null)
                return "[错误] 找不到王国：" + targetKingdomName;
            if (target == myKingdom)
                return "[错误] 不能回复自己";

            var myName = myKingdom.Name.ToString();
            var targetName = target.Name.ToString();
            var currentTime = PromptManager.GetCurrentTimeString();
            var entity = EntityManager.GetOrCreateEntity(hero);
            var name = entity?.Name ?? hero.Name?.ToString() ?? "?";

            var threadPath = GetConsultThreadPath(myName, targetName);
            SafeFileIO.AppendAllText(threadPath, $"[{currentTime}] {name}（{myName}国王）答复：{message.Trim()}\n");

            return $"答复已送达 {targetName}。";
        }

        private static string GetConsultThreadPath(string kingdomA, string kingdomB)
        {
            var pair = BuildPairKey(kingdomA, kingdomB);
            var dir = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "diplomacy", "consults");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, pair + ".txt");
        }

        private static string BuildPairKey(string kingdomA, string kingdomB)
        {
            if (string.CompareOrdinal(kingdomA, kingdomB) <= 0)
                return kingdomA + "_and_" + kingdomB;
            return kingdomB + "_and_" + kingdomA;
        }

        internal static float CalculateLetterDelay(Hero sender, Hero? recipient)
        {
            if (sender == null || recipient == null) return 0f;
            var senderParty = sender.PartyBelongedTo;
            var recipientParty = recipient.PartyBelongedTo;
            Vec2 senderPos, recipientPos;

            if (senderParty != null)
                senderPos = senderParty.GetPosition2D;
            else if (sender.CurrentSettlement != null)
                senderPos = sender.CurrentSettlement.GetPosition2D;
            else
                return 0f;

            if (recipientParty != null)
                recipientPos = recipientParty.GetPosition2D;
            else if (recipient.CurrentSettlement != null)
                recipientPos = recipient.CurrentSettlement.GetPosition2D;
            else
                return 0f;

            var dist = senderPos.Distance(recipientPos);
            var km = dist / 1000f;
            // 信使送信延时：地图单位尺度偏小（全图约 2-3"公里"），原公式 km/4 几乎恒 <1 被钳到 1h，等于无延时。
            // 改为按距离线性放大（km*4h）+ 最低 3h，让信件往返有可感知的时间差（跨图约半天）。
            return Math.Max(3f, km * 4f);
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

            // 军情迷雾：自己/同阵营全量精确；异国按距离与可达性分级（近距/远距/传闻）。
            // 背景：原实现无视距离与阵营，任何部队的金币/经验/装备等机密一览无余，导致「完美信息 → 和平均衡」。
            var intelTier = DetermineIntelTier(party, leader);
            if (intelTier != IntelTier.Full)
                return BuildFuzzyPartyReport(party, leader, intelTier);

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
                foreach (var p in prisoners.Where(p => p.Character != null && p.Character.IsHero))
                {
                    var hero = p.Character.HeroObject;
                    if (hero == null) continue;
                    var desc = hero.Clan != null ? hero.Clan.Name?.ToString() ?? "无氏族" : "无氏族";
                    sb.AppendLine($"  [贵族] {hero.Name}（{desc}）");
                }
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

        /// <summary>军情情报等级：Full=全量精确，NearScout=近距侦察，FarScout=远方模糊，Rumor=传闻。</summary>
        private enum IntelTier { Full, NearScout, FarScout, Rumor }

        /// <summary>
        /// 决定查看某支部队的情报等级。
        /// 自己/自己的部队、同阵营（王国/同盟/同一服役王国）→ 全量精确；
        /// 异国 → 按「距离 + 能否直接观察（跨海/不可通行地形）」分级。
        /// </summary>
        private static IntelTier DetermineIntelTier(MobileParty party, Hero? leader)
        {
            var actor = AIChatClient.CurrentHero;
            // 无当前代理上下文（如后台事件）时保守给全量，避免误伤合法调用
            if (actor == null || leader == null)
                return IntelTier.Full;

            if (leader == actor) return IntelTier.Full;
            if (actor.PartyBelongedTo == party) return IntelTier.Full;

            // 同阵营：军情共享（含雇佣兵服役于同一王国）
            if (actor.MapFaction != null && actor.MapFaction == party.MapFaction)
                return IntelTier.Full;

            var actorPos = GetHeroPosition(actor);
            var targetPos = GetPartyPosition(party, leader);
            if (actorPos == null || targetPos == null)
                return IntelTier.Rumor; // 位置不明，只有传闻

            // 跨海 / 中间隔着不可通行地形：无法实地侦察，降为传闻
            if (!IsDirectlyObservable(actor, party, actorPos.Value, targetPos.Value))
                return IntelTier.Rumor;

            var dist = actorPos.Value.Distance(targetPos.Value);
            var (scoutRadius, midRadius) = GetIntelRadii();
            if (dist <= scoutRadius) return IntelTier.NearScout;
            if (dist <= midRadius) return IntelTier.FarScout;
            return IntelTier.Rumor;
        }

        private static Vec2? GetHeroPosition(Hero? hero)
        {
            if (hero == null) return null;
            var p = hero.PartyBelongedTo;
            if (p != null) return p.GetPosition2D;
            if (hero.CurrentSettlement != null) return hero.CurrentSettlement.GatePosition.ToVec2();
            return null;
        }

        private static Vec2? GetPartyPosition(MobileParty party, Hero? leader)
        {
            if (party != null) return party.GetPosition2D;
            if (leader?.CurrentSettlement != null) return leader.CurrentSettlement.GatePosition.ToVec2();
            return null;
        }

        /// <summary>
        /// 判断能否「直接观察」目标：双方是否在海中，以及两点连线是否被不可通行地形（海域/山脉）阻断。
        /// 出错时回退为可达，避免误伤。
        /// </summary>
        private static bool IsDirectlyObservable(Hero actor, MobileParty party, Vec2 fromPos, Vec2 toPos)
        {
            try
            {
                if (actor.PartyBelongedTo?.IsCurrentlyAtSea == true) return false;
                if (party?.IsCurrentlyAtSea == true) return false;

                PathFaceRecord? fromFace = null;
                if (actor.PartyBelongedTo != null)
                    fromFace = actor.PartyBelongedTo.CurrentNavigationFace;
                else if (actor.CurrentSettlement != null)
                    fromFace = actor.CurrentSettlement.GatePosition.Face;
                if (fromFace == null) return true;

                return Campaign.Current.MapSceneWrapper.IsLineToPointClear(fromFace.Value, fromPos, toPos, 0.5f);
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// 情报分级半径。用「地图实际尺度 × 比例」作相对锚，不依赖绝对 km——
        /// 骑砍全图仅约 2000-3000 地图单位（1 单位 ≈ 1 米），凭直觉定绝对值必然踩坑。
        /// </summary>
        private static (float scoutRadius, float midRadius) GetIntelRadii()
        {
            var extent = ComputeMapExtent();
            var frac = MySettings.Instance?.IntelligenceScoutRadiusFraction ?? 0.2f;
            var scout = Math.Max(100f, extent * frac);
            var mid = Math.Max(scout * 1.5f, extent * 0.45f);
            return (scout, mid);
        }

        /// <summary>全图尺度：城镇/城堡包围盒的最大边。兜底 2500（原版地图实测量级）。</summary>
        private static float ComputeMapExtent()
        {
            try
            {
                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;
                int count = 0;
                foreach (var s in Settlement.All)
                {
                    if (!s.IsTown && !s.IsCastle) continue;
                    var p = s.GatePosition.ToVec2();
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                    count++;
                }
                if (count == 0) return 2500f;
                var extent = Math.Max(maxX - minX, maxY - minY);
                return extent > 0 ? extent : 2500f;
            }
            catch
            {
                return 2500f;
            }
        }

        /// <summary>异国部队的模糊化报告（不含金币/军饷/经验/升级/俘虏/物品/装备等机密）。</summary>
        private static string BuildFuzzyPartyReport(MobileParty party, Hero? leader, IntelTier tier)
        {
            var name = leader != null ? leader.Name?.ToString() ?? "?" : party.Name?.ToString() ?? "?";
            var totalMen = SafeTotalMen(party);
            var sb = new StringBuilder();

            if (tier == IntelTier.NearScout)
            {
                var (lo, hi) = FuzzBand(totalMen, 0.2f);
                sb.AppendLine($"===== {name}的部队（近距侦察） =====");
                sb.AppendLine($"兵力：约 {hi} 人（{lo}-{hi}，情报误差 ±20%）");
                var (loMax, hiMax) = FuzzBand(SafePartySizeLimit(party), 0.2f);
                if (hiMax > 0)
                    sb.AppendLine($"规模上限：约 {hiMax} 人（{loMax}-{hiMax}，情报误差 ±20%）");
                var wounded = SafeWounded(party);
                if (wounded > 0)
                    sb.AppendLine($"伤兵：约 {FuzzRound(wounded, 10)} 人");
                sb.AppendLine($"兵种构成：{SafeComposition(party, totalMen)}");
                sb.AppendLine($"当前位置：{SafeLocationDesc(party, leader)}");
                sb.AppendLine("【侦察情报】近距观察所得，兵力、规模上限与兵种构成基本可信；金币、军饷、兵种经验、升级路线、俘虏与物资详情无法从外部探知。");
                return sb.ToString().TrimEnd();
            }

            if (tier == IntelTier.FarScout)
            {
                var (lo, hi) = FuzzBand(totalMen, 0.4f);
                sb.AppendLine($"===== {name}的部队（远方情报） =====");
                sb.AppendLine($"兵力：约 {lo}-{hi} 人（情报不确定）");
                var (loMax, hiMax) = FuzzBand(SafePartySizeLimit(party), 0.4f);
                if (hiMax > 0)
                    sb.AppendLine($"规模上限：约 {loMax}-{hiMax} 人（情报不确定）");
                sb.AppendLine($"兵种构成：{SafeComposition(party, totalMen)}");
                sb.AppendLine($"装备水平：{SafeGearQuality(party, totalMen)}");
                sb.AppendLine($"当前位置：{SafeLocationDesc(party, leader)}");
                sb.AppendLine("【情报不确定】远处得来的消息，仅有大致兵力、规模上限与构成印象；具体军情（金币、军饷、兵种经验、升级路线、俘虏、物资、装备）无从得知。");
                return sb.ToString().TrimEnd();
            }

            // Rumor：纯定性传闻
            sb.AppendLine($"===== {name}的部队（传闻） =====");
            sb.AppendLine($"据说：{StrengthDesc(totalMen)}");
            sb.AppendLine($"位置：{SafeLocationDesc(party, leader)}");
            sb.AppendLine("【传闻】未经证实，可能严重失真。不可作为决策依据。");
            return sb.ToString().TrimEnd();
        }

        private static int SafeTotalMen(MobileParty party)
        {
            try { return party.MemberRoster?.TotalManCount ?? 0; } catch { return 0; }
        }

        private static int SafePartySizeLimit(MobileParty party)
        {
            try { return party.Party?.PartySizeLimit ?? 0; } catch { return 0; }
        }

        private static int SafeWounded(MobileParty party)
        {
            try { return party.MemberRoster?.GetTroopRoster().Sum(e => e.WoundedNumber) ?? 0; } catch { return 0; }
        }

        private static (int lo, int hi) FuzzBand(int n, float ratio)
        {
            var lo = FuzzRound(Math.Max(0, n) * (1f - ratio), 10);
            var hi = FuzzRound(Math.Max(0, n) * (1f + ratio), 10);
            return (lo, hi);
        }

        private static int FuzzRound(float v, int step)
        {
            return (int)Math.Round(v / step) * step;
        }

        private static string SafeComposition(MobileParty party, int totalMen)
        {
            try
            {
                if (totalMen <= 0) return "不详";
                var elements = party.MemberRoster?.GetTroopRoster();
                if (elements == null) return "不详";
                int inf = 0, ranged = 0, cav = 0, horse = 0;
                foreach (var e in elements)
                {
                    var t = e.Character;
                    if (t == null || t.IsHero) continue;
                    var n = e.Number;
                    switch (t.DefaultFormationClass)
                    {
                        case FormationClass.Infantry: inf += n; break;
                        case FormationClass.Ranged: ranged += n; break;
                        case FormationClass.Cavalry: cav += n; break;
                        case FormationClass.HorseArcher: horse += n; break;
                    }
                }
                var cats = new (string name, int count)[]
                {
                    ("步兵", inf), ("射手", ranged), ("骑兵", cav), ("弓骑兵", horse)
                };
                var top = cats.OrderByDescending(c => c.count).Where(c => c.count > 0).Take(2).ToList();
                if (top.Count == 0) return "不详";
                var parts = top.Select(c => $"{c.name}约{Math.Round(c.count * 100f / totalMen)}%");
                return string.Join("、", parts) + "为主";
            }
            catch { return "不详"; }
        }

        private static string SafeGearQuality(MobileParty party, int totalMen)
        {
            try
            {
                if (totalMen <= 0) return "不详";
                var elements = party.MemberRoster?.GetTroopRoster();
                if (elements == null) return "不详";
                double totalTier = 0;
                int count = 0;
                foreach (var e in elements)
                {
                    var t = e.Character;
                    if (t == null || t.IsHero) continue;
                    totalTier += t.GetBattleTier() * e.Number;
                    count += e.Number;
                }
                if (count == 0) return "不详";
                var avg = totalTier / count;
                if (avg >= 3.5) return "精良";
                if (avg >= 2.0) return "普通";
                return "简陋";
            }
            catch { return "不详"; }
        }

        private static string StrengthDesc(int totalMen)
        {
            if (totalMen <= 0) return "兵力微弱";
            if (totalMen < 100) return "兵力薄弱（不足百人）";
            if (totalMen < 300) return "兵力一般（数百人规模）";
            if (totalMen < 800) return "兵力较强（近千人之众）";
            return "兵力雄厚（千军之众）";
        }

        private static string SafeLocationDesc(MobileParty party, Hero? leader)
        {
            try
            {
                var s = party?.CurrentSettlement ?? leader?.CurrentSettlement;
                if (s != null) return s.Name?.ToString() ?? "某定居点";
                if (party != null && !party.IsActive) return "已解散";
                return "野外行军";
            }
            catch { return "不明"; }
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

                    // 修复：int 溢出防护（原 costPer*count 溢出为负可绕过金币检查并反向加钱）+ 部队上限检查
                    if (count > 10000)
                        return "[错误] 单次招募数量过大（上限 10000）";
                    long totalCostL = (long)costPer * count;
                    if (totalCostL > int.MaxValue)
                        return "[错误] 招募总花费超出范围";
                    var totalCost = (int)totalCostL;

                    if (recruiter.Gold < totalCost)
                        return $"[错误] 金币不足。需要 {totalCost} 金，当前只有 {recruiter.Gold} 金。";

                    var newSize = party.Party.NumberOfAllMembers + count;
                    var sizeLimit = party.Party.PartySizeLimit;
                    if (newSize > sizeLimit)
                        return $"[错误] 招募后会超过部队上限（当前 {party.Party.NumberOfAllMembers}/{sizeLimit}，需 {newSize}）。";

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

                // 修复：扣除升级所需物品（战马等）——原版 PartyScreenLogic 会真正移除，此前只检查不扣除
                var requiredCategory = target.UpgradeRequiresItemFromCategory;
                if (requiredCategory != null)
                {
                    var itemsRemoved = 0;
                    for (int i = 0; i < party.ItemRoster.Count && itemsRemoved < count; i++)
                    {
                        var ie = party.ItemRoster[i];
                        var item = ie.EquipmentElement.Item;
                        if (item == null) continue;
                        if (item.ItemCategory == requiredCategory)
                        {
                            var toRemove = Math.Min(ie.Amount, count - itemsRemoved);
                            party.ItemRoster.AddToCounts(item, -toRemove);
                            itemsRemoved += toRemove;
                        }
                    }
                    if (itemsRemoved < count)
                        return $"[错误] 缺少升级所需的 {requiredCategory.GetName()}（需要 {count}，实际可扣除 {itemsRemoved}）。";
                }

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
            var trimmed = name.Trim();
            var lower = trimmed.ToLowerInvariant();

            foreach (var s in Settlement.All)
            {
                var sName = s.Name?.ToString() ?? "";
                if (sName.Trim().Equals(trimmed, StringComparison.OrdinalIgnoreCase))
                    return s;
            }

            var candidates = new List<Settlement>();
            foreach (var s in Settlement.All)
            {
                var sName = s.Name?.ToString() ?? "";
                if (sName.Contains(trimmed) || trimmed.Contains(sName))
                    candidates.Add(s);
            }

            if (candidates.Count > 0)
            {
                var fort = candidates.FirstOrDefault(c => c.IsTown || c.IsCastle);
                if (fort != null) return fort;
                return candidates[0];
            }

            foreach (var s in Settlement.All)
            {
                var sName = (s.Name?.ToString() ?? "").ToLowerInvariant();
                if (sName.Contains(lower) || lower.Contains(sName))
                    candidates.Add(s);
            }

            if (candidates.Count > 0)
            {
                var fort = candidates.FirstOrDefault(c => c.IsTown || c.IsCastle);
                if (fort != null) return fort;
                return candidates[0];
            }

            return null;
        }

        private static MobileParty? FindPartyByEntityId(string? targetEntityId)
        {
            if (string.IsNullOrEmpty(targetEntityId))
            {
                // 修复：空参 = 查自己部队（tools.json "Omit to inspect your own party"），不再默认返回玩家部队
                return AIChatClient.CurrentHero?.PartyBelongedTo;
            }

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
            {
                // 修复：空参优先取当前对话对方；无对方或对方是自己时取自己。
                // 原实现固定返回玩家（MainHero），导致 NPC 省略参数时误作用于玩家。
                var target = EntityManager.ActiveTarget?.HeroRef;
                if (target != null && target != AIChatClient.CurrentHero)
                    return target;
                return AIChatClient.CurrentHero;
            }

            var entityId = EntityManager.ResolveEntityId(targetEntityId!);
            if (entityId == null)
            {
                // 修复：支持按 StringId 精确查找（重名人物多，且已故人物不参与 ResolveEntityId 的名称匹配）。
                // 例如列传流程 query_recent_events("CharacterObject_2840") 可直接命中被处决者。
                var byStringId = AllHeroesForQuery().FirstOrDefault(h => h.StringId != null
                    && h.StringId.Equals(targetEntityId, StringComparison.OrdinalIgnoreCase));
                if (byStringId != null)
                    return byStringId;
                return null;
            }

            var entity = EntityManager.GetEntityById(entityId);
            return entity?.HeroRef;
        }

        private static string ExecuteChangeKingdom(string action, string kingdomName, bool rebellion, string? heirEntityId)
        {
            var hero = AIChatClient.CurrentHero;
            if (hero == null) return "[错误] 无当前领主";
            if (hero.Clan == null) return "[错误] 你不属于任何氏族";
            if (hero.Clan.Leader != hero) return "[错误] 只有氏族领袖才能改变阵营";
            var clan = hero.Clan;

            switch (action)
            {
                case "abdicate":
                {
                    var k = clan.Kingdom;
                    if (k == null) return "[错误] 你当前不属于任何王国，无法禅位";
                    if (k.RulingClan != clan) return "[错误] 只有国王才能禅位";
                    if (string.IsNullOrEmpty(heirEntityId))
                        return "[错误] 必须指定继承人（heir_entity_id），继承人需与你同属一个氏族";

                    var resolvedId = EntityManager.ResolveEntityId(heirEntityId) ?? heirEntityId;
                    var heirEntity = EntityManager.GetOrCreateEntityById(resolvedId);
                    if (heirEntity?.HeroRef == null)
                        return $"[错误] 未找到继承人：{heirEntityId}";
                    var heir = heirEntity.HeroRef;
                    if (heir.Clan != clan && heir.Clan?.Kingdom != k)
                        return $"[错误] 继承人需要与你同氏族，或与你同王国的其他氏族领袖";
                    if (heir == hero)
                        return "[错误] 不能禅位给自己";

                    if (heir.Clan == clan)
                    {
                        ChangeClanLeaderAction.ApplyWithSelectedNewLeader(clan, heir);
                        return $"你已将王位禅让给同氏族的{heir.Name}。{heir.Name}现在是{k.Name}的新统治者。";
                    }
                    else
                    {
                        ChangeRulingClanAction.Apply(k, heir.Clan);
                        return $"你已将王位禅让给{heir.Clan.Name}的{heir.Name}。统治氏族已变更，{heir.Name}现在是{k.Name}的新统治者。";
                    }
                }

                case "leave_kingdom":
                {
                    if (clan.Kingdom == null) return "[错误] 你当前不属于任何王国";
                    var oldName = clan.Kingdom.Name?.ToString() ?? "?";
                    if (rebellion)
                    {
                        ChangeKingdomAction.ApplyByLeaveWithRebellionAgainstKingdom(clan);
                        return $"已以叛乱方式脱离{oldName}。你对旧王国宣战，保留了封地。";
                    }
                    else
                    {
                        ChangeKingdomAction.ApplyByLeaveKingdom(clan);
                        return $"已和平脱离{oldName}。你失去了在旧王国的封地。";
                    }
                }

                case "join_kingdom":
                case "defect_to_kingdom":
                case "join_as_mercenary":
                {
                    var target = DiplomacyService.FindKingdom(kingdomName);
                    if (target == null) return $"[错误] 未找到王国：{kingdomName}";

                    if (action == "join_kingdom")
                    {
                        if (clan.Kingdom == target) return "[错误] 你已经属于该王国";
                        if (clan.Kingdom != null) return "[错误] 你必须先脱离当前王国才能加入新王国。先用 change_kingdom(action=\"leave_kingdom\")";
                        ChangeKingdomAction.ApplyByJoinToKingdom(clan, target);
                        return $"已加入{target.Name}，成为其封臣。";
                    }

                    if (action == "defect_to_kingdom")
                    {
                        if (clan.Kingdom == null) return "[错误] 你当前不属于任何王国，请用 join_kingdom";
                        if (clan.Kingdom == target) return "[错误] 你已经属于该王国";
                        ChangeKingdomAction.ApplyByJoinToKingdomByDefection(clan, clan.Kingdom, target);
                        return $"已叛逃至{target.Name}。";
                    }

                    if (action == "join_as_mercenary")
                    {
                        if (clan.Kingdom == target) return "[错误] 你已经属于该王国";
                        if (clan.Kingdom != null) return "[错误] 你必须先脱离当前王国才能成为雇佣兵。先用 change_kingdom(action=\"leave_kingdom\")";
                        ChangeKingdomAction.ApplyByJoinFactionAsMercenary(clan, target);
                        return $"已成为{target.Name}的雇佣兵。";
                    }

                    return "[错误] 未知操作";
                }

                default:
                    return $"[错误] 未知 action：{action}。可用：abdicate, leave_kingdom, join_kingdom, defect_to_kingdom, join_as_mercenary";
            }
        }

        private static string ExecuteLetGo()
        {
            var hero = AIChatClient.CurrentHero;
            if (hero == null) return "[错误] 无当前领主";

            var encounter = PlayerEncounter.Current;
            if (encounter == null) return "[错误] 当前没有遭遇战，无法放行";

            var encounteredParty = PlayerEncounter.EncounteredMobileParty;
            if (encounteredParty == null) return "[错误] 没有遭遇方";

            var myParty = hero.PartyBelongedTo;
            if (myParty == null) return "[错误] 你没有带领部队";

            // 修复：PlayerEncounter.EncounteredMobileParty 恒为"非玩家一侧"（即 NPC 自己的部队），
            // 原判断 `encounteredParty.LeaderHero != MainHero` 恒真导致永远报"对方不是玩家"。
            // 正确语义：遭遇对象是自己的部队即代表对方是玩家，允许放行。
            if (encounteredParty != myParty)
                return "[错误] 遭遇对象不是你的部队，此工具仅供对玩家放行使用";

            PlayerEncounter.LeaveEncounter = true;

            foreach (var party in MobileParty.All)
            {
                if (party != encounteredParty && party.MapFaction == myParty.MapFaction)
                {
                    if (party.Ai != null)
                        party.Ai.SetDoNotAttackMainParty(12);
                }
            }

            if (myParty.Ai != null)
                myParty.Ai.SetDoNotAttackMainParty(12);

            MobileParty.MainParty.IgnoreForHours(6f);

            return $"你决定放{encounteredParty.LeaderHero?.Name?.ToString() ?? "玩家"}一马。对方已安全离开，你的部队短时间内不会再次追击。";
        }

        /// <summary>按名称在部队俘虏名册中匹配俘虏（支持中英文名，双向包含）。</summary>
        private static bool PrisonerNameMatches(CharacterObject? troop, string name)
        {
            if (troop?.Name == null || string.IsNullOrEmpty(name)) return false;
            var n = troop.Name.ToString();
            return n.Contains(name) || name.Contains(n);
        }

        /// <summary>释放自己部队中的俘虏（贵族英雄或普通士兵）。释放英雄 → EndCaptivityAction（对方成为逃亡者，事件入史）；
        /// 释放普通士兵 → 直接从俘虏名册移除。</summary>
        private static string ExecuteReleasePrisoner(string prisonerName, int count, bool all)
        {
            var hero = AIChatClient.CurrentHero;
            if (hero == null) return "[错误] 无当前领主";

            var party = hero.PartyBelongedTo;
            if (party == null)
                return $"[错误] {hero.Name} 没有带领部队，无法释放俘虏";

            var prisoners = party.PrisonRoster.GetTroopRoster();

            // 释放全部
            if (all || string.IsNullOrEmpty(prisonerName))
            {
                var releasedHeroes = 0;
                var releasedTroops = 0;
                foreach (var p in prisoners.Where(p => p.Character != null).ToList())
                {
                    if (p.Character!.IsHero)
                    {
                        var h = p.Character.HeroObject;
                        if (h == null) continue;
                        EndCaptivityAction.ApplyByReleasedByChoice(h, hero);
                        releasedHeroes++;
                    }
                    else
                    {
                        party.PrisonRoster.RemoveTroop(p.Character, p.Number);
                        releasedTroops += p.Number;
                    }
                }
                return $"已释放全部俘虏：贵族 {releasedHeroes} 人，士兵 {releasedTroops} 人。";
            }

            var match = prisoners.FirstOrDefault(p => p.Character != null && PrisonerNameMatches(p.Character, prisonerName));
            if (match.Character == null)
                return $"[未找到] 部队中没有名为 \"{prisonerName}\" 的俘虏。使用 query_party_troops 查看俘虏列表。";

            if (match.Character.IsHero)
            {
                var h = match.Character.HeroObject;
                if (h == null) return "[错误] 无法解析该俘虏";
                EndCaptivityAction.ApplyByReleasedByChoice(h, hero);
                return $"已释放贵族 {h.Name}。对方成为逃亡者，返回领地休整。";
            }

            var n = count > 0 ? Math.Min(count, match.Number) : match.Number;
            party.PrisonRoster.RemoveTroop(match.Character, n);
            return $"已释放 {n} 名 {match.Character.Name} 俘虏。";
        }

        /// <summary>处决自己部队中的贵族俘虏（仅限贵族英雄）。处决是不可逆的重罪：
        /// 斩首者名誉下降，受害者氏族/亲友/同阵营贵族对斩首者的好感大幅下降（移植原版玩家处决惩罚给 NPC）。</summary>
        private static string ExecuteExecutePrisoner(string prisonerName)
        {
            var hero = AIChatClient.CurrentHero;
            if (hero == null) return "[错误] 无当前领主";

            var party = hero.PartyBelongedTo;
            if (party == null)
                return $"[错误] {hero.Name} 没有带领部队，无法处决俘虏";

            var prisoners = party.PrisonRoster.GetTroopRoster();
            var match = prisoners.FirstOrDefault(p =>
                p.Character != null && p.Character.IsHero && PrisonerNameMatches(p.Character, prisonerName));
            if (match.Character == null)
                return $"[未找到] 部队中没有名为 \"{prisonerName}\" 的贵族俘虏（处决仅限贵族）。使用 query_party_troops 查看俘虏列表。";

            var victim = match.Character.HeroObject;
            if (victim == null) return "[错误] 无法解析该贵族";
            if (victim == Hero.MainHero)
                return "[错误] 不能处决玩家本人。";
            if (victim.IsPlayerCompanion)
                return $"[错误] {victim.Name} 是玩家的同伴，不能处决。";
            if (!victim.IsLord)
                return $"[错误] {victim.Name} 不是贵族，不能处以斩首之刑。";

            // isForced=true：无论"死亡模式"是否开启都强制执行（参考原版叛乱清除成员的用法）。
            KillCharacterAction.ApplyByExecution(victim, hero, true, true);

            // 政治代价——受 MCM「处决无惩罚」控制（默认开启 = 无惩罚，玩家与 NPC 都可随便处决）：
            // 仅 NPC 执行者手动施加（玩家执行者的处决代价由原版行为处理，避免叠加；原版惩罚由 Harmony 补丁禁用）。
            if (MySettings.Instance?.ExecutionNoPenalty != true && !hero.IsHumanPlayerCharacter)
            {
                // 名誉下降：移植玩家处决的 -1000 名誉经验（约降 1 级），下限 -2。
                var honorLevel = hero.GetTraitLevel(DefaultTraits.Honor);
                if (honorLevel > -2)
                    hero.SetTraitLevel(DefaultTraits.Honor, honorLevel - 1);

                // 关系惩罚：走游戏自带的 DefaultExecutionRelationModel（已内置 NPC 执行者分支），
                // 受害者氏族成员/亲友/同阵营贵族/名誉高尚的贵族对斩首者好感下降。
                var affected = new List<string>();
                foreach (var other in Hero.AllAliveHeroes)
                {
                    if (other == hero || other == victim) continue;
                    var penalty = Campaign.Current.Models.ExecutionRelationModel
                        .GetRelationChangeForExecutingHero(victim, other, out var showQuickNotification);
                    if (penalty != 0)
                    {
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(hero, other, penalty, showQuickNotification);
                        affected.Add($"{other.Name}{penalty:+0;-0}");
                    }
                }

                var result = $"已处决贵族 {victim.Name}。{hero.Name} 名誉受损，人神共愤，卡拉迪亚贵族间对其恶名昭彰。";
                if (affected.Count > 0)
                    result += " 受此影响的关系：" + string.Join("，", affected.Take(8)) + (affected.Count > 8 ? " 等" : "");
                return result;
            }

            return $"已处决贵族 {victim.Name}。";
        }

        /// <summary>
        /// 天意建族（家族补充系统）：创建一个新的贵族家族。成员 3-6 人由程序随机生成（偏向年轻），
        /// 家族等级 2（恰好够当封臣又看得出是新族），投效指定王国（封臣或雇佣兵），族长自动获得部队。
        /// 仅「天意」（__fate__）实体可调用。
        /// </summary>
        /// <summary>
        /// 天意单次激活的建族预算：一次只建一族。
        /// 背景：天意提示词虽写明"一次只创建一个家族"，但 LLM 可能连建多族——
        /// 曾出现一次激活连建 5 族（每族 3-6 英雄 + 族长部队），与游戏其他状态变更撞车导致原生层崩溃。
        /// 这里用代码强制限流：每次天意激活（ProcessClanReplenishmentEvent 开头）重置预算，超限即拒绝。
        /// </summary>
        private static int _fateClanBudget;
        private static readonly object _fateClanBudgetLock = new();

        public static void ResetFateClanBudget()
        {
            lock (_fateClanBudgetLock) _fateClanBudget = 0;
        }

        private static bool TryConsumeFateClanBudget()
        {
            lock (_fateClanBudgetLock)
            {
                if (_fateClanBudget >= 1) return false;
                _fateClanBudget++;
                return true;
            }
        }

        private static string ExecuteCreateClan(string clanName, string kingdomName, string culture, string motivation, bool isMercenary)
        {
            if (AgentManager.ActiveAgentId != "__fate__")
                return "[拒绝] 只有天意（家族补充系统）能创建家族。";
            if (!TryConsumeFateClanBudget())
                return "[提示] 你本次唤醒已经降下过一支贵族血脉。世家兴衰非一日之功——请静待日后再次被唤起，届时再观天下之势补充。不要连续创建。";
            if (string.IsNullOrEmpty(clanName))
                return "[错误] 请提供家族名称。";
            if (Campaign.Current == null)
                return "[错误] 战役未加载。";

            var kingdom = DiplomacyService.FindKingdom(kingdomName);
            if (kingdom == null)
                return $"[错误] 未找到王国：{kingdomName}。";

            CultureObject cultureObj = null;
            if (!string.IsNullOrEmpty(culture))
            {
                try
                {
                    cultureObj = Game.Current.ObjectManager.GetObjectTypeList<CultureObject>()
                        .FirstOrDefault(c => (c.Name?.ToString() ?? "").Contains(culture) || culture.Contains(c.Name?.ToString() ?? ""));
                }
                catch { }
            }
            cultureObj ??= kingdom.Culture;
            if (cultureObj == null)
                return "[错误] 无法确定家族文化。";

            var homeSettlement = kingdom.Settlements.FirstOrDefault(s => s.IsTown)
                ?? Settlement.All.FirstOrDefault(s => s.IsTown && s.Culture == cultureObj)
                ?? Settlement.All.FirstOrDefault(s => s.IsTown);
            if (homeSettlement == null)
                return "[错误] 找不到合适的定居点作为家族根基。";

            try
            {
                // 1. 建族
                var clan = Clan.CreateClan("fate_clan_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                var nameObj = new TextObject(clanName);
                clan.ChangeClanName(nameObj, nameObj);
                clan.Culture = cultureObj;
                clan.IsNoble = true;
                // 家族等级 2（恰好够当封臣又看得出是新族）：Tier 私有 setter，改从声望抬升
                clan.AddRenown(Campaign.Current.Models.ClanTierModel.GetRequiredRenownForTier(2), false);
                try
                {
                    var donor = Clan.All.FirstOrDefault(c => c.Banner != null);
                    if (donor?.Banner != null)
                        clan.Banner = new Banner(donor.Banner);
                }
                catch { }
                clan.SetInitialHomeSettlement(homeSettlement);

                // 2. 生成成员（3-6，偏向年轻：族长 25-40，其余 14-35）
                // 对齐游戏叛乱建族的模式：先建英雄（clan=null，暂不挂族），
                // 全部创建成功后再统一 hero.Clan = clan 注册进族 + ChangeState(Active) 激活。
                // 原实现直接把 clan 传进 CreateSpecialHero 且不激活，英雄与族关联可能不完整，
                // 游戏后续迭代该族/英雄时可能原生崩溃。
                var memberCount = 3 + _rng.Next(4);
                var heroes = new List<Hero>();
                for (var i = 0; i < memberCount; i++)
                {
                    try
                    {
                        var age = i == 0 ? _rng.Next(25, 41) : _rng.Next(14, 36);
                        var template = Campaign.Current.Models.HeroCreationModel.GetRandomTemplateByOccupation(Occupation.Lord, homeSettlement);
                        if (template == null) continue;
                        var h = HeroCreator.CreateSpecialHero(template, homeSettlement, null, null, age);
                        if (h != null) heroes.Add(h);
                    }
                    catch { }
                }
                if (heroes.Count == 0)
                    return "[错误] 未能生成家族成员。";
                foreach (var h in heroes)
                {
                    h.Clan = clan;
                    h.ChangeState(Hero.CharacterStates.Active);
                }
                clan.SetLeader(heroes[0]);

                // 3. 投效（封臣或雇佣兵）
                if (isMercenary)
                    ChangeKingdomAction.ApplyByJoinFactionAsMercenary(clan, kingdom, CampaignTime.Zero, 50, true);
                else
                    ChangeKingdomAction.ApplyByJoinToKingdom(clan, kingdom, CampaignTime.Zero, true);

                // 4. 族长带兵
                try
                {
                    var leader = clan.Leader;
                    if (leader != null)
                        LordPartyComponent.CreateLordParty(leader.StringId, leader, homeSettlement.GatePosition, 3f, homeSettlement, leader);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"新家族 {clanName} 族长建队失败：{ex.Message}");
                }

                // 5. 通知游戏 + 入史
                CampaignEventDispatcher.Instance.OnClanCreated(clan, true);
                var joinText = isMercenary ? $"以雇佣兵身份效力于{kingdom.Name}" : $"投效{kingdom.Name}";
                HistoryRecorder.RecordClanCreated($"{clanName}建立（{cultureObj.Name}，家族等级2，{heroes.Count}名成员），{joinText}，族长为{heroes[0].Name}");
                if (!string.IsNullOrEmpty(motivation))
                    HistoryRecorder.RecordClanCreated($"新家族{clanName}之立族宣言：{motivation}");

                return $"已降下新的贵族血脉：{clanName}（{cultureObj.Name}文化，家族等级2，{heroes.Count}名成员）——{joinText}，族长{heroes[0].Name}。{(!string.IsNullOrEmpty(motivation) ? " 立族宣言：" + motivation : "")}";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"create_clan 异常：{ex.Message}");
                return $"[错误] 创建家族失败：{ex.Message}";
            }
        }
    }
}
