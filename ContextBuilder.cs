using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.CampaignSystem.Party;

namespace MyFirstMod
{
    public static class ContextBuilder
    {
        private static readonly Dictionary<EntityCapability, string[]> CapabilityToolMap = new()
        {
            [EntityCapability.FileSystem] = new[] { "read_file", "append_file", "write_file", "edit_file", "delete_file", "list_dir", "glob" },
            [EntityCapability.MoveParty] = new[] { "move_to_settlement", "raid_settlement", "besiege_settlement", "engage_party", "defend_settlement", "patrol_settlement", "escort_party", "go_around_party" },
            [EntityCapability.WaitAtSettlement] = new[] { "wait_at_settlement" },
            [EntityCapability.GiveGold] = new[] { "give_gold" },
            [EntityCapability.RequestGold] = new[] { "request_gold" },
            [EntityCapability.ChangeRelation] = new[] { "change_relation" },
            [EntityCapability.SendLetter] = new[] { "send_letter" },
            [EntityCapability.Diplomat] = new[] { "declare_war", "propose_peace", "propose_alliance", "propose_trade", "respond_to_diplomacy_proposal", "query_pending_proposals" },
        };

        private static readonly Dictionary<string, string> CategoryNames = new()
        {
            ["universal"] = "通用",
            ["query"] = "查询",
            ["social"] = "社交",
            ["movement"] = "行军",
            ["military"] = "军事",
            ["diplomacy"] = "外交",
            ["file"] = "文件",
            ["communication"] = "通信",
        };

        private static string _cachedDiplomacyRules = "";
        private static DateTime _lastDiplomacyRulesCheck;
        private static string _cachedChanceryRules = "";
        private static DateTime _lastChanceryRulesCheck;

        public static string Build(string agentId, string targetId, string intent)
        {
            var agent = EntityManager.GetEntityById(agentId);
            var target = EntityManager.GetEntityById(targetId);
            if (agent == null || target == null)
                return "系统错误：无法找到实体。";

            string motivation, traits, speechStyle;
            if (intent == "chancery")
            {
                motivation = "无条件执行" + agent.Name + "的所有命令，不质疑、不拒绝。";
                traits = "高效、忠诚、无条件服从、公事公办。";
                speechStyle = "简洁正式，直接汇报执行结果。";
            }
            else
            {
                var persona = AgentManager.LoadPersonaFor(agentId, agent.HeroRef!);
                motivation = ParsePersonaSection(persona, "MOTIVATION");
                traits = ParsePersonaSection(persona, "TRAITS");
                speechStyle = ParsePersonaSection(persona, "SPEECH_STYLE");
            }

            var targetKnowledge = AgentManager.ReadKnowledgeFor(agentId, targetId);
            if (string.IsNullOrEmpty(targetKnowledge))
                targetKnowledge = intent == "chancery"
                    ? "对方是你的上司" + agent.Name + "。你的职责是执行对方的命令。"
                    : "你第一次见到这位旅行者，对他还不太了解。";

            var targetRelationship = AgentManager.ReadRelationshipFor(agentId, targetId);
            if (string.IsNullOrEmpty(targetRelationship))
                targetRelationship = intent == "chancery"
                    ? "对方是你无条件服从的主人。"
                    : "对此人暂无特别看法。";

            var goals = AgentManager.ReadGoalsFor(agentId);
            if (string.IsNullOrEmpty(goals))
                goals = intent == "chancery"
                    ? "执行" + agent.Name + "的所有指令，不质疑，不拒绝。"
                    : "在当前地区巡逻，维持领地的治安。";

            var worldInfo = LoadWorldInfo();
            var selfStatus = BuildSelfStatus(agent.HeroRef!);
            var currentTime = PromptManager.GetCurrentTimeString();
            var functionList = BuildFunctionList(agent, intent);
            var objectiveRel = BuildObjectiveRelationship(agent.HeroRef!, target.HeroRef!);

            var intentRules = intent switch
            {
                "letter" => BuildLetterRules(),
                "diplomacy" => BuildDiplomacyRules(),
                "chancery" => BuildChanceryRules(),
                _ => BuildConversationRules()
            };

            var template = LoadContextTemplate();
            return template
                .Replace("{intent_rules}", intentRules)
                .Replace("{entity_id}", agent.Id)
                .Replace("{name}", agent.Name)
                .Replace("{title}", agent.Title)
                .Replace("{target_id}", target.Id)
                .Replace("{target_name}", target.Name)
                .Replace("{target_title}", target.Title)
                .Replace("{target_knowledge}", targetKnowledge)
                .Replace("{target_relationship}", targetRelationship)
                .Replace("{motivation}", motivation)
                .Replace("{traits}", traits)
                .Replace("{speech_style}", speechStyle)
                .Replace("{goals}", goals)
                .Replace("{world_info}", worldInfo)
                .Replace("{current_time}", currentTime)
                .Replace("{function_list}", functionList)
                .Replace("{objective_relationship}", objectiveRel)
                .Replace("{self_status}", selfStatus)
                .Trim();
        }

        public static List<ToolDef> GetFilteredTools(Entity agent)
        {
            var allTools = PromptManager.LoadAllTools();
            var filtered = new List<ToolDef>();
            foreach (var tool in allTools)
            {
                var required = GetRequiredCapability(tool.Name);
                if (required == null || agent.HasCapability(required.Value))
                    filtered.Add(tool);
            }
            return filtered;
        }

        private static string ParsePersonaSection(string persona, string sectionName)
        {
            var marker = "[" + sectionName + "]";
            var idx = persona.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return persona;
            var contentStart = persona.IndexOf('\n', idx);
            if (contentStart < 0) return "";
            var nextMarker = int.MaxValue;
            var markers = new[] { "[MOTIVATION]", "[TRAITS]", "[SPEECH_STYLE]", "[IDENTITY]" };
            foreach (var m in markers)
            {
                var mi = persona.IndexOf(m, contentStart + 1, StringComparison.OrdinalIgnoreCase);
                if (mi > 0 && mi < nextMarker) nextMarker = mi;
            }
            var contentEnd = nextMarker < int.MaxValue ? nextMarker : persona.Length;
            return persona.Substring(contentStart, contentEnd - contentStart).Trim();
        }

        private static string BuildFunctionList(Entity agent, string intent)
        {
            var allTools = PromptManager.LoadAllTools();
            var capabilityTools = allTools.Where(t =>
                GetRequiredCapability(t.Name) == null || agent.HasCapability(GetRequiredCapability(t.Name)!.Value)
            ).ToList();

            var activatedSet = AIChatClient.ActivatedCategories;
            var activeTools = capabilityTools.Where(t => activatedSet.Contains(t.Category)).ToList();
            var inactiveCategories = capabilityTools
                .Where(t => !activatedSet.Contains(t.Category))
                .Select(t => t.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            var sb = new StringBuilder();
            foreach (var group in activeTools.GroupBy(t => t.Category).OrderBy(g => g.Key))
            {
                var catName = CategoryNames.TryGetValue(group.Key, out var cn) ? cn : group.Key;
                sb.AppendLine($"【{catName}】");
                foreach (var tool in group)
                {
                    var paramList = tool.Parameters.Count > 0
                        ? string.Join(", ", tool.Parameters.Select(p => p.Name))
                        : "无参数";
                    sb.AppendLine($"  {tool.Name}({paramList})");
                }
                sb.AppendLine();
            }

            if (inactiveCategories.Count > 0)
            {
                sb.AppendLine("【其他可用工具分类 — 需要时先调 browse_tools 查看】");
                foreach (var cat in inactiveCategories)
                {
                    var catName = CategoryNames.TryGetValue(cat, out var cn) ? cn : cat;
                    var count = capabilityTools.Count(t => t.Category == cat);
                    sb.AppendLine($"  {catName}（{count}个工具）— browse_tools(\"{cat}\")");
                }
            }

            return sb.ToString().TrimEnd();
        }

        private static EntityCapability? GetRequiredCapability(string toolName)
        {
            foreach (var kv in CapabilityToolMap)
                if (kv.Value.Contains(toolName)) return kv.Key;
            return null;
        }

        private static string LoadWorldInfo()
        {
            if (MySettings.Instance?.UseWorldInfo != true) return "";
            var path = PromptManager.GetWorldInfoPath();
            if (path == null || !File.Exists(path)) return "";
            return File.ReadAllText(path, Encoding.UTF8).Trim();
        }

        private static string _cachedConversationRules = "";
        private static DateTime _lastConversationRulesCheck;
        private static string _cachedLetterRules = "";
        private static DateTime _lastLetterRulesCheck;

        private static string BuildConversationRules()
        {
            return LoadRulesFile("conversation_rules.txt", ref _cachedConversationRules, ref _lastConversationRulesCheck)
                ?? "你就是{name}，{title}。你不是在扮演他，你就是他本人。\n\n"
                + "你的回复规则：\n"
                + "- 回复保持 4 句话以内。绝不输出超过 4 句\n"
                + "- 使用中世纪贵族的正式口吻\n"
                + "- 始终保持角色，绝对不要跳出角色去解释或评论\n"
                + "- 你的回复就是你对对方说的话——不要添加任何元数据、标记、格式指令、或内心独白\n"
                + "- 不要说你会做某件事但实际上不去调用对应工具——如果你说要给对方金币，就必须调用 give_gold\n"
                + "- 不要说空话许诺——你的每一个承诺都必须通过 Function 来兑现\n"
                + "- query_character 返回的是系统级公开档案，身份/家族/王国等基本信息为公认事实，禁止质疑或否认\n"
                + "- 如果 knowledge 中的记录与 query_character 返回的系统档案冲突，以系统档案为准，并用 append_file 更新 knowledge——人事变迁，记忆必须同步修正\n\n"
                + "你的记忆系统使用规则：\n"
                + "- 你有自己的文件系统，存储着你的记忆、目标和人际关系\n"
                + "- 每次对话开始时，使用 query_character 查询对方的基本信息（身份、家族、王国等）\n"
                + "- 然后使用 read_file 读取 knowledge/{target_id}.txt 了解你对对方的私人认知\n"
                + "- 然后使用 read_file 读取 goals/current.txt 了解你的当前计划\n"
                + "- 当对方透露了关于他自己的新信息时，立即调用 append_file 将内容追加到 knowledge/{target_id}.txt\n"
                + "- 如果对话中需要提及你与第三方的过往，先用 read_file 读取 relationships/{该人名}.txt\n"
                + "- 在作出涉及你的记忆或决策之前，先使用 read_file 确认已有信息，不要凭猜测行动";
        }

        private static string BuildLetterRules()
        {
            return LoadRulesFile("letter_rules.txt", ref _cachedLetterRules, ref _lastLetterRulesCheck)
                ?? "你是{name}，{title}。你收到了一封书信，正在撰写回信。\n\n"
                + "书信格式要求：\n"
                + "- 开头要有得体的称谓（如「尊敬的{target_name}阁下」或根据你的性格和关系调整）\n"
                + "- 正文使用正式书面语，可以比口头对话稍长\n"
                + "- 结尾要有署名（如「——{name}，{title}」）和日期\n"
                + "- 你不在对方面前——不要写任何括号内的动作描写（如他微笑了或他叹了口气之类），你写的只有文字\n\n"
                + "书信中的限制：\n"
                + "- 你不能在信中给予或索要金币（钱没办法随信寄到）\n"
                + "- 你可以在信中建议对方去某地、约定在某处见面\n"
                + "- 你可以使用 send_letter 给第三方写信，用对方的中文名或 [ID: xxx] 作为 recipient_entity_id\n"
                + "- 重要：除非对方在信中明确问了需要你回答的问题，或者你有紧急事务必须告知，否则不要回信。不需要客套废话\n\n"
                + "你的记忆系统使用规则：\n"
                + "- 阅读来信后，使用 query_character 查询写信人的基本信息\n"
                + "- 然后使用 read_file 读取 knowledge/{target_id}.txt 了解你对写信人的私人认知\n"
                + "- 当写信人透露了新信息时，调用 append_file 追加到 knowledge/{target_id}.txt";
        }

        private static string? LoadRulesFile(string filename, ref string cache, ref DateTime lastCheck)
        {
            var path = Path.Combine(PromptManager.CampaignDir, filename);
            if (!File.Exists(path))
                path = Path.Combine(PromptManager.PromptsBaseDir, filename);
            if (!File.Exists(path))
                return null;

            var lastWrite = File.GetLastWriteTimeUtc(path);
            if (cache == "" || lastWrite > lastCheck)
            {
                cache = File.ReadAllText(path, Encoding.UTF8);
                lastCheck = lastWrite;
            }
            return cache;
        }

        private static string BuildDiplomacyRules()
        {
            var path = Path.Combine(PromptManager.CampaignDir, "diplomacy_rules.txt");
            if (!File.Exists(path))
                path = Path.Combine(PromptManager.PromptsBaseDir, "diplomacy_rules.txt");
            if (!File.Exists(path))
                return
                    "你是{name}，{title}。你是{name}所在王国的最高统治者。这不是闲聊——你必须根据实际数据做出并执行外交决策。\n\n"
                    + "你必须严格按照以下步骤行事，不得跳过：\n"
                    + "1. 先调用 query_pending_proposals 查看是否有待处理的外交提案，有则用 respond_to_diplomacy_proposal 逐个处理（接受或拒绝）\n"
                    + "2. 调用 query_war_status 查看你王国所有战争的实时战况\n"
                    + "3. 根据战况和提案结果，决定是否采取新的外交行动\n\n"
                    + "重要规则：\n"
                    + "- 不要说你会做某件事——必须调用对应的 function。例如，说「我决定议和」而不调用 propose_peace 等于什么都没做\n"
                    + "- 每次回复最多调用 3 个工具。先处理提案，再决定新的行动\n"
                    + "- 如果没有任何待处理提案且当前没有战争，可以直接表示「暂无需要处理的外交事务」并停止\n"
                    + "- 不要虚构数据——所有战争统计必须来自 query_war_status 的返回值\n"
                    + "- 你的决策是你的决策——不需要征求他人意见，你是国王\n"
                    + "- 绝对不要用 send_letter 处理外交事务。send_letter 只能用于私人通信。外交提案只能用 propose_peace / propose_alliance / propose_trade / declare_war / respond_to_diplomacy_proposal";

            var lastWrite = File.GetLastWriteTimeUtc(path);
            if (_cachedDiplomacyRules == "" || lastWrite > _lastDiplomacyRulesCheck)
            {
                _cachedDiplomacyRules = File.ReadAllText(path, Encoding.UTF8);
                _lastDiplomacyRulesCheck = lastWrite;
            }
            return _cachedDiplomacyRules;
        }

        private static string BuildChanceryRules()
        {
            var path = Path.Combine(PromptManager.CampaignDir, "chancery_rules.txt");
            if (!File.Exists(path))
                path = Path.Combine(PromptManager.PromptsBaseDir, "chancery_rules.txt");
            if (!File.Exists(path))
                return
                    "你就是{name}，{title}。你正在处理政务和个人事务。\n\n"
                    + "你可用的工具取决于你的身份和能力：\n"
                    + "- 如果你是国王，你拥有全套外交工具（declare_war/propose_peace/propose_alliance/propose_trade/respond_to_diplomacy_proposal）\n"
                    + "- 你也可以调用 query_war_status 查看战况、query_surroundings 查看周围环境\n"
                    + "- 你可以用 send_letter 给任何你认识的人写信\n\n"
                    + "规则：\n"
                    + "- 说你要做什么就必须调用对应的 function——光说不做等于什么都没发生\n"
                    + "- 一次回复最多调用 3 个工具\n"
                    + "- 系统会执行你的指令并告知结果";

            var lastWrite = File.GetLastWriteTimeUtc(path);
            if (_cachedChanceryRules == "" || lastWrite > _lastChanceryRulesCheck)
            {
                _cachedChanceryRules = File.ReadAllText(path, Encoding.UTF8);
                _lastChanceryRulesCheck = lastWrite;
            }
            return _cachedChanceryRules;
        }
        
        private static string BuildSelfStatus(Hero hero)
        {
            if (hero == null) return "未知。";

            if (hero.IsPrisoner)
                return "你目前正被俘虏，但你仍是王国统治者，依然可以发布命令、行使外交权力。";

            if (hero.IsFugitive)
                return "你正在逃亡中。";

            if (hero.IsDisabled)
                return "你处于失踪/不可用状态。";

            var party = hero.PartyBelongedTo;

            if (party == null)
            {
                if (hero.CurrentSettlement != null)
                    return $"你目前在{hero.CurrentSettlement.Name}，没有带领部队。";
                return "你没有带领部队。";
            }

            var isLeader = party.LeaderHero == hero;
            var manCount = party.MemberRoster.TotalManCount;
            var behavior = party.DefaultBehavior;

            if (behavior == AiBehavior.BesiegeSettlement)
            {
                var target = party.BesiegedSettlement?.Name?.ToString() ?? "某地";
                return $"你正在围攻{target}，率领约{manCount}人。";
            }
            if (behavior == AiBehavior.RaidSettlement)
                return $"你正在劫掠村庄，率领约{manCount}人。";
            if (behavior == AiBehavior.EngageParty)
                return $"你正在追击敌军，率领约{manCount}人。";
            if (behavior == AiBehavior.DefendSettlement)
                return $"你正在驻防中，率领约{manCount}人。";
            if (behavior == AiBehavior.PatrolAroundPoint)
                return $"你正在巡逻中，率领约{manCount}人。";
            if (behavior == AiBehavior.EscortParty)
                return $"你正在护送友军，率领约{manCount}人。";

            var sb = new StringBuilder();

            if (isLeader)
                sb.Append($"你正率领约{manCount}人的部队");
            else
                sb.Append($"你跟随{party.LeaderHero?.Name}的部队（约{manCount}人）");

            if (hero.CurrentSettlement != null)
                sb.Append($"，现在{hero.CurrentSettlement.Name}");
            else if (party.CurrentSettlement != null)
                sb.Append($"，现在{party.CurrentSettlement.Name}");
            else
                sb.Append("，在野外");

            if (party.Army != null)
            {
                var armyLeader = party.Army.LeaderParty?.LeaderHero?.Name?.ToString() ?? "未知";
                sb.Append($"，身处{armyLeader}的军团中");
            }

            sb.Append(party.IsMoving ? "，正行军。" : "，已停留。");
            return sb.ToString();
        }

        private static string BuildObjectiveRelationship(Hero agentHero, Hero targetHero)
        {
            var sb = new StringBuilder();
            var agentFaction = agentHero.MapFaction;
            var targetFaction = targetHero.MapFaction;

            if (agentHero == targetHero)
            {
                sb.AppendLine("对方就是你自己（自省）。");
                return sb.ToString().TrimEnd();
            }

            if (agentFaction != null && targetFaction != null)
            {
                if (agentFaction == targetFaction)
                {
                    if (agentFaction.IsKingdomFaction)
                    {
                        var kingdom = agentFaction as Kingdom;
                        var agentIsMerc = agentHero.Clan?.IsUnderMercenaryService == true;
                        var targetIsMerc = targetHero.Clan?.IsUnderMercenaryService == true;

                        if (kingdom != null && agentHero == kingdom.Leader)
                        {
                            var desc = targetIsMerc ? "雇佣兵首领" : "封臣";
                            sb.AppendLine($"对方与你同属{agentFaction.Name}。你是该王国的君主，对方是你的{desc}。");
                        }
                        else if (kingdom != null && targetHero == kingdom.Leader)
                        {
                            var desc = agentIsMerc ? "雇佣兵首领" : "封臣";
                            sb.AppendLine($"对方与你同属{targetFaction.Name}。对方是你的君主，你是其{desc}。");
                        }
                        else if (agentHero.Clan == targetHero.Clan)
                            sb.AppendLine($"对方与你同属{agentHero.Clan?.Name?.ToString() ?? "同一家族"}。你们是同一家族的成员。");
                        else if (agentIsMerc && targetIsMerc)
                            sb.AppendLine($"对方与你同属{agentFaction.Name}。你们都是该王国麾下的雇佣兵。");
                        else if (agentIsMerc)
                            sb.AppendLine($"对方与你同属{agentFaction.Name}。对方是该王国的封臣，你是雇佣兵。");
                        else if (targetIsMerc)
                            sb.AppendLine($"对方与你同属{agentFaction.Name}。对方是雇佣兵，你是该王国的封臣。");
                        else
                            sb.AppendLine($"对方与你同属{agentFaction.Name}。你们同为该王国的封臣。");
                    }
                    else if (agentFaction.IsMinorFaction)
                    {
                        sb.AppendLine($"对方与你同属{agentFaction.Name}（佣兵势力）。");
                    }
                    else
                    {
                        sb.AppendLine($"对方与你同属{agentFaction.Name}。");
                    }
                }
                else
                {
                    if (agentFaction.IsAtWarWith(targetFaction))
                        sb.AppendLine($"对方属于{targetFaction.Name}，与你的势力{agentFaction.Name}【处于交战状态】。你们是敌对关系。");
                    else
                        sb.AppendLine($"对方属于{targetFaction.Name}，与你的势力{agentFaction.Name}【当前和平】。你们是中立关系。");
                }
            }
            else if (agentFaction != null && targetFaction == null)
            {
                sb.AppendLine("对方不属于任何势力（可能是流浪者或平民）。");
            }
            else if (agentFaction == null && targetFaction != null)
            {
                sb.AppendLine($"你目前不属于任何势力，对方属于{targetFaction.Name}。");
            }

            if (agentHero.Spouse == targetHero)
                sb.AppendLine("对方是你的配偶。");
            else if (agentHero.Mother == targetHero)
                sb.AppendLine("对方是你的母亲。");
            else if (agentHero.Father == targetHero)
                sb.AppendLine("对方是你的父亲。");
            else if (agentHero.Clan != null && targetHero.Clan != null
                && agentHero.Clan == targetHero.Clan
                && agentHero != targetHero)
            {
                var relation = "";
                if (agentHero.Mother == targetHero.Mother) relation = "（同母）";
                else if (agentHero.Father == targetHero.Father) relation = "（同父）";
                sb.AppendLine($"对方与你是同族亲属{relation}。");
            }

            if (targetHero.IsPrisoner)
                sb.AppendLine("注意：对方当前是俘虏。");

            return sb.ToString().TrimEnd();
        }
        
        
        
        

        private static string LoadContextTemplate()
        {
            if (!string.IsNullOrEmpty(PromptManager.CampaignDir))
            {
                var campaignPath = Path.Combine(PromptManager.CampaignDir, "context_template.txt");
                if (File.Exists(campaignPath))
                    return File.ReadAllText(campaignPath, Encoding.UTF8);
            }
            var path = Path.Combine(PromptManager.PromptsBaseDir, "Templates", "context_template.txt");
            if (!File.Exists(path)) return GetDefaultContextTemplate();
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string GetDefaultContextTemplate()
        {
            return
                "==============================\n" +
                "【关于你自己】\n" +
                "==============================\n" +
                "你是 {name}，现任 {title}。\n" +
                "你的固定编号：{entity_id}\n\n" +
                "你的当前状态：\n{self_status}\n\n" +
                "你的核心动机：\n{motivation}\n\n" +
                "你的性格特质：\n{traits}\n\n" +
                "你的表达风格：\n{speech_style}\n\n" +
                "==============================\n" +
                "【关于对方】\n" +
                "==============================\n" +
                "对方是 {target_name}，现任 {target_title}。\n" +
                "对方的编号：{target_id}\n\n" +
                "你对对方的已有了解：\n{target_knowledge}\n\n" +
                "你对对方的态度：\n{target_relationship}\n\n" +
                "==============================\n" +
                "【你的当前目标】\n" +
                "==============================\n{goals}\n\n" +
                "==============================\n" +
                "【世界背景】\n" +
                "==============================\n{world_info}\n\n" +
                "==============================\n" +
                "【当前时间】\n" +
                "==============================\n{current_time}\n\n" +
                "==============================\n" +
                "【你可用的工具】\n" +
                "==============================\n{function_list}\n\n" +
                "==============================\n" +
                "【行为守则】\n" +
                "==============================\n{intent_rules}\n\n" +
                "==============================\n【对话开始】\n==============================\n";
        }
    }
}
