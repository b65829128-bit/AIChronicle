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

namespace AIChronicle
{
    public static partial class ToolExecutor
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

                    case "write_chronicle":
                        return AgentManager.ExecuteWriteChronicle(
                            args["genre"]?.ToString() ?? "",
                            args["name"]?.ToString() ?? "",
                            args["content"]?.ToString() ?? "");

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
                            args["radius_fraction"]?.ToObject<float>() ?? 0.2f,
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

                    case "create_kingdom":
                        return ExecuteCreateKingdom(
                            args["kingdom_name"]?.ToString() ?? "",
                            args["culture"]?.ToString() ?? "",
                            args["motto"]?.ToString() ?? "");

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

    }
}
