using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace AIChronicle
{
    public static class ContextBuilder
    {
        private static readonly Dictionary<EntityCapability, string[]> CapabilityToolMap = new()
        {
            [EntityCapability.FileSystem] = new[] { "read_file", "append_file", "write_file", "edit_file", "delete_file", "list_dir", "glob", "grep", "move_file" },
            [EntityCapability.MoveParty] = new[] { "move_to_settlement", "raid_settlement", "besiege_settlement", "engage_party", "defend_settlement", "patrol_settlement", "escort_party", "go_around_party" },
            [EntityCapability.WaitAtSettlement] = new[] { "wait_at_settlement" },
            [EntityCapability.GiveGold] = new[] { "give_gold" },
            [EntityCapability.RequestGold] = new[] { "request_gold" },
            [EntityCapability.ChangeRelation] = new[] { "change_relation" },
            [EntityCapability.SendLetter] = new[] { "send_letter" },
            [EntityCapability.Diplomat] = new[] { "declare_war", "propose_peace", "propose_alliance", "propose_trade", "end_alliance", "end_trade_agreement", "respond_to_diplomacy_proposal", "gift_fief", "query_pending_proposals" },
            [EntityCapability.CreateClan] = new[] { "create_clan" },
            [EntityCapability.Chronicler] = new[] { "write_chronicle" },
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

        /// <summary>
        /// 上下文模板中的易变内容分界标记。标记之后的内容（当前时间/自身状态/对对方认知/目标/客观关系/内政报告）
        /// 每轮都可能变化——放在这里使其成为"尾部易变块"，与 system 稳定前缀分开，最大化 DeepSeek 前缀缓存命中。
        /// 旧版模板（无此标记）整体按稳定处理，行为与旧版一致。
        /// </summary>
        private const string VolatileMarker = "<!--VOLATILE-->";

        // 注入截断：knowledge/goals 全文注入无上限会导致 system 长期膨胀。注入时按字符预算截断，
        // agent 仍可用 read_file 读取完整内容（记忆系统不受影响），只限制"注入提示词的量"。
        private const int MaxKnowledgeInjectChars = 500;
        private const int MaxGoalsInjectChars = 400;

        private static string _cachedDiplomacyRules = "";
        private static DateTime _lastDiplomacyRulesCheck;
        private static string _cachedChanceryRules = "";
        private static DateTime _lastChanceryRulesCheck;
        private static string _cachedHistorianRules = "";
        private static DateTime _lastHistorianRulesCheck;

        public static string Build(string agentId, string targetId, string intent)
        {
            var (stable, volatilePart) = BuildParts(agentId, targetId, intent);
            return string.IsNullOrEmpty(volatilePart) ? stable : stable + "\n\n" + volatilePart;
        }

        /// <summary>稳定前缀：身份/persona/世界背景/工具清单/行为守则。不随回合变化，用于 system 消息（缓存友好）。</summary>
        public static string BuildStable(string agentId, string targetId, string intent)
        {
            return BuildParts(agentId, targetId, intent).stable;
        }

        /// <summary>易变块：当前时间/自身状态/对对方认知/目标/客观关系/内政报告。每轮可能不同，
        /// 单独作为【当前状况】user 消息放在历史之后——这样 system + 历史构成稳定前缀，易变内容只在尾部。</summary>
        public static string BuildVolatile(string agentId, string targetId, string intent)
        {
            return BuildParts(agentId, targetId, intent).volatilePart;
        }

        private static (string stable, string volatilePart) BuildParts(string agentId, string targetId, string intent)
        {
            var agent = EntityManager.GetEntityById(agentId);
            var target = EntityManager.GetEntityById(targetId);
            if (agent == null || target == null)
                return ("系统错误：无法找到实体。", "");

            string motivation, traits, speechStyle;
            if (intent == "chancery")
            {
                motivation = "无条件执行" + agent.Name + "的所有命令，不质疑、不拒绝。";
                traits = "高效、忠诚、无条件服从、公事公办。";
                speechStyle = "简洁正式，直接汇报执行结果。";
            }
            else if (intent == "historian")
            {
                motivation = "客观公正地记录卡拉迪亚大陆的历史变迁，不偏袒任何势力。";
                traits = "客观、严谨、博学、公正。对事实的尊重高于一切。";
                speechStyle = "使用庄重典雅的中文文言或半文白风格，叙述冷静克制。";
            }
            else if (intent == "clan_replenishment")
            {
                motivation = "观照卡拉迪亚天下家族的兴衰，维持贵族世家的延续——当世家凋零殆尽时，为天下注入新的血脉。";
                traits = "公正、超然、洞悉世事，对天下家族一视同仁，唯以世界的长治久安为念。";
                speechStyle = "庄重而简洁，以「天道」「气运」的视角评述家族兴衰。";
            }
            else
            {
                var persona = AgentManager.LoadPersonaFor(agentId, agent.HeroRef!);
                motivation = ParsePersonaSection(persona, "MOTIVATION");
                traits = ParsePersonaSection(persona, "TRAITS");
                speechStyle = ParsePersonaSection(persona, "SPEECH_STYLE");
            }

            string targetKnowledge;
            if (intent == "historian")
                targetKnowledge = "你是卡拉迪亚的史官，你的工作对象是历史事件本身，而非任何个人。";
            else if (intent == "clan_replenishment")
                targetKnowledge = "你是卡拉迪亚命运的天意，俯瞰天下家族的兴衰。";
            else
            {
                targetKnowledge = AgentManager.ReadKnowledgeFor(agentId, targetId);
                if (string.IsNullOrEmpty(targetKnowledge))
                    targetKnowledge = intent == "chancery"
                        ? "对方是" + agent.Name + "，你的主人。你的职责是执行对方的命令。"
                        : agentId == targetId
                            ? "你正在审视自身与王国的处境。"
                            : "你第一次见到这位旅行者，对对方还不太了解。";
                else if (targetKnowledge.Length > MaxKnowledgeInjectChars)
                    targetKnowledge = targetKnowledge.Substring(0, MaxKnowledgeInjectChars)
                        + "\n…（记忆较长已截断，完整内容可用 read_file 读取）";
            }

            string targetRelationship;
            if (intent == "historian")
                targetRelationship = "你作为史官，对各方势力保持中立。";
            else if (intent == "clan_replenishment")
                targetRelationship = "你对天下家族一视同仁，无偏无私。";
            else
            {
                targetRelationship = AgentManager.ReadRelationshipFor(agentId, targetId);
                if (string.IsNullOrEmpty(targetRelationship))
                    targetRelationship = intent == "chancery"
                        ? "对方是你无条件服从的主人。"
                        : agentId == targetId
                            ? "这是你对自己的审视。"
                            : "对此人暂无特别看法。";
            }

            string goals;
            if (intent == "historian")
                goals = "编纂年度编年史，记录卡拉迪亚大陆的重大事件。";
            else if (intent == "clan_replenishment")
                goals = "观照封臣与雇佣兵家族的数量，在世家凋零时降下新的贵族血脉，维持卡拉迪亚的秩序。";
            else
            {
                goals = AgentManager.ReadGoalsFor(agentId);
                if (string.IsNullOrEmpty(goals))
                    goals = intent == "chancery"
                        ? "执行" + agent.Name + "的所有指令，不质疑，不拒绝。"
                        : agentId == targetId
                            ? "审视你的王国局势，凭自己的判断做出决策。"
                            : "在当前地区巡逻，维持领地的治安。";
                else if (goals.Length > MaxGoalsInjectChars)
                    goals = goals.Substring(0, MaxGoalsInjectChars)
                        + "\n…（目标较长已截断，完整内容可用 read_file 读取）";
            }

            var worldInfo = LoadWorldInfo();
            // 史官/天意例外：不注入游戏规则（前者只编史，后者只观照家族兴衰，不做具体军事/经济决策）
            var gameRules = (intent == "historian" || intent == "clan_replenishment") ? "" : LoadGameRules();
            var selfStatus = intent == "historian"
                ? "你是卡拉迪亚的宫廷史官，不受任何势力节制。"
                : intent == "clan_replenishment"
                    ? "你是卡拉迪亚命运的天意，超然于诸国之上，主导世家的生灭。"
                    : intent == "chancery"
                        ? "你是" + agent.Name + "的个人秘书处——你是行政助手，没有自己的部队、领地或军队，你的身份以" + agent.Name + "为准。"
                        : BuildSelfStatus(agent.HeroRef!);
            var currentTime = PromptManager.GetCurrentTimeString();
            var functionList = BuildFunctionList(agent, intent);
            var objectiveRel = intent == "historian"
                ? "你作为史官，不隶属于任何势力，也不与任何势力为敌。"
                : intent == "clan_replenishment"
                    ? "你作为天意，俯瞰天下，不隶属任何势力。"
                    : intent == "chancery"
                        ? "对方是" + agent.Name + "，你无条件服从的主人，你们是主从关系。"
                        : BuildObjectiveRelationship(agent.HeroRef!, target.HeroRef!);

            var intentRules = intent switch
            {
                "letter" => BuildLetterRules(),
                "diplomacy" => BuildDiplomacyRules(),
                "king_consult" => BuildDiplomacyRules(),
                "chancery" => BuildChanceryRules(),
                "historian" => BuildHistorianRules(),
                "clan_replenishment" => BuildClanReplenishmentRules(),
                "advisory" => BuildAdvisoryRules(),
                "fief_review" => BuildFiefReviewRules(),
                "consolidation" => BuildConsolidationRules(),
                "chat" => BuildChatRules(),
                _ => BuildConversationRules()
            };

            var kingdomName = (agent.HeroRef?.MapFaction as Kingdom)?.Name?.ToString() ?? "?";

            // 配套制度：国王外交审视升级为内外政务——内政审视报告（封地账本/治理/战功），
            // 让国王基于真实数据自行判断是否调整封地（包括夺封，需师出有名）。
            // 内政报告走 {court_report} 占位符，渲染进模板易变块（保持前缀缓存稳定）。
            var courtReport = "";
            if (intent == "diplomacy" && agent.HeroRef != null
                && agent.HeroRef.MapFaction is Kingdom rulerKingdom
                && rulerKingdom.RulingClan?.Leader == agent.HeroRef)
            {
                courtReport = BuildCourtReport(agent.HeroRef);
            }

            var template = LoadContextTemplate();

            var rendered = template
                .Replace("{intent_rules}", intentRules)
                .Replace("{entity_id}", agent.Id)
                .Replace("{name}", agent.Name)
                .Replace("{title}", agent.Title)
                .Replace("{kingdom}", kingdomName)
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
                .Replace("{game_rules}", gameRules)
                .Replace("{current_time}", currentTime)
                .Replace("{function_list}", functionList)
                .Replace("{objective_relationship}", objectiveRel)
                .Replace("{self_status}", selfStatus)
                .Replace("{court_report}", courtReport)
                .Trim();

            var markerIdx = rendered.IndexOf(VolatileMarker, StringComparison.Ordinal);
            if (markerIdx < 0)
                return (rendered, "");

            var stable = rendered.Substring(0, markerIdx).Trim();
            var volatilePart = rendered.Substring(markerIdx + VolatileMarker.Length).Trim();
            return (stable, volatilePart);
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

            // 秘书处排除 create_kingdom（玩家建国走原版正规流程），与 AIChatClient.BuildTools 的排除保持一致——
            // 否则提示词文本列出该工具、API tools 却不含，模型会尝试调用一个不存在的工具。
            if (intent == "chancery")
                capabilityTools = capabilityTools.Where(t => t.Name != "create_kingdom").ToList();

            var activatedSet = AIChatClient.ActivatedCategories;
            var activeCategories = capabilityTools
                .Where(t => activatedSet.Contains(t.Category))
                .Select(t => t.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToList();
            var inactiveCategories = capabilityTools
                .Where(t => !activatedSet.Contains(t.Category))
                .Select(t => t.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            // 精简：工具全量定义已随 API 的 tools 参数发送（JSON 是工具调用的主通道），
            // 这里只留一份极简的中文索引（分类 + 工具名），不再重复参数列表——省 token 且不丢信息。
            var sb = new StringBuilder();
            foreach (var group in activeCategories)
            {
                var catName = CategoryNames.TryGetValue(group, out var cn) ? cn : group;
                var names = capabilityTools
                    .Where(t => t.Category == group)
                    .Select(t => t.Name);
                sb.AppendLine($"【{catName}】{string.Join(", ", names)}");
            }

            if (intent != "chancery" && inactiveCategories.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("【其他可用工具分类 — 需要时先调 browse_tools 查看并解锁】");
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
            string main;
            if (path == null || !File.Exists(path))
                main = "卡拉迪亚大陆，一片充满纷争与传奇的土地。众多王国与帝国征战不休，唯力量与智慧方能立足。";
            else
                main = File.ReadAllText(path, Encoding.UTF8).Trim();

            // 可选势力：诺德不是原版可交互势力，仅 MCM「包含诺德势力」开启时拼入主要势力列表。
            // world_info.txt 里用 {nords} 占位符标记插入点；关闭时移除占位符行。
            const string nordsMarker = "{nords}";
            var nordsContent = "";
            if (MySettings.Instance?.IncludeNordsFaction == true)
            {
                var nordsPath = PromptManager.GetWorldInfoNordsPath();
                if (nordsPath != null && File.Exists(nordsPath))
                    nordsContent = File.ReadAllText(nordsPath, Encoding.UTF8).Trim();
            }
            if (nordsContent.Length > 0)
                main = main.Replace(nordsMarker, nordsContent);
            else
                main = main.Replace(nordsMarker + "\n", "").Replace(nordsMarker, "");
            return main;
        }

        /// <summary>游戏规则层：让 agent 了解卡拉迪亚的实际运转机制（机动/金钱/部队上限/兵种/招募/战争/影响力），
        /// 避免拿现实经验套进游戏造成决策错位。受 MCM「注入游戏规则」控制。史官不注入。</summary>
        private static string LoadGameRules()
        {
            if (MySettings.Instance?.UseGameRules != true) return "";
            var path = PromptManager.GetGameRulesPath();
            if (path == null || !File.Exists(path))
                return "";
            return File.ReadAllText(path, Encoding.UTF8).Trim();
        }

        private static string _cachedConversationRules = "";
        private static DateTime _lastConversationRulesCheck;
        private static string _cachedLetterRules = "";
        private static DateTime _lastLetterRulesCheck;

        private static string BuildConversationRules()
        {
            return LoadRulesFile("conversation_rules.txt", ref _cachedConversationRules, ref _lastConversationRulesCheck)
                ?? "你就是{name}，{title}。你不是在扮演什么别的角色——你就是{name}本人，你的言行就是{name}的言行。\n\n"
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
                + "- 当对方透露了对方自身的新信息时，立即调用 append_file 将内容追加到 knowledge/{target_id}.txt\n"
                + "- 如果对话中需要提及你与第三方的过往，先用 read_file 读取 relationships/{该人名}.txt\n"
                + "- 在作出涉及你的记忆或决策之前，先使用 read_file 确认已有信息，不要凭猜测行动\n\n"
                + "你的跨对话持久记忆规则（极其重要）：\n"
                + "- 你学到的重要信息不能只留在当前对话的上下文里——对话结束后你会遗忘。凡是能影响未来决策的信息（某国即将开战、某人的秘密、你做出的承诺），立即用 append_file 或 write_file 存入文件\n"
                + "- 你与当前对话对象达成的重要约定，写入 knowledge/{target_id}.txt 并同时写入 decisions/diary.txt\n"
                + "- 你对第三方的新认知，写入 knowledge/{该人ID}.txt；若不确定 ID，先用 grep 搜索\n"
                + "- 遇到不确定的事时，先查记忆再说话：用 glob 浏览 knowledge/，用 grep 搜索关键词，用 read_file 精读。若记忆中没有，再用 query_character 等工具获取最新信息——获取后若值得记住，立即写入对应文件";
        }

        private static string BuildLetterRules()
        {
            return LoadRulesFile("letter_rules.txt", ref _cachedLetterRules, ref _lastLetterRulesCheck)
                ?? "你是{name}，{title}。你收到了一封书信，正在撰写回信。\n\n"
                + "书信格式要求：\n"
                + "- 开头要有得体的称谓（如「尊敬的{target_name}阁下」或根据你的性格和关系调整）\n"
                + "- 正文使用正式书面语，可以比口头对话稍长\n"
                + "- 结尾要有署名（如「——{name}，{title}」）和日期\n"
                + "- 你不在对方面前——不要写任何括号内的动作描写（如对方微笑了或对方叹了口气之类），你写的只有文字\n\n"
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

        private static string _cachedAdvisoryRules = "";
        private static DateTime _lastAdvisoryRulesCheck;
        private static string _cachedFiefReviewRules = "";
        private static DateTime _lastFiefReviewRulesCheck;
        private static string _cachedConsolidationRules = "";
        private static DateTime _lastConsolidationRulesCheck;

        private static string BuildDiplomacyRules()
        {
            var path = Path.Combine(PromptManager.CampaignDir, "diplomacy_rules.txt");
            if (!File.Exists(path))
                path = Path.Combine(PromptManager.PromptsBaseDir, "diplomacy_rules.txt");
            if (!File.Exists(path))
                return
                    "你是{name}，{title}。你是所在王国的最高统治者。审视你的王国局势，凭你自己的判断做出外交决断。\n\n"
                    + "步骤：\n"
                    + "1. 先调用 query_pending_proposals 查看是否有待处理的外交提案，有则用 respond_to_diplomacy_proposal 逐一处理\n"
                    + "2. 调用 query_war_status 了解当前所有战争的战况\n"
                    + "3. 根据你对局势的个人判断，决定是否采取新的外交行动——如何决策由你做主\n\n"
                    + "重要规则：\n"
                    + "- 不要说你会做某件事——必须调用对应的 function。例如，说「我决定议和」而不调用 propose_peace 等于什么都没做\n"
                    + "- 每次回复最多调用 3 个工具。先处理提案，再决定新的行动\n"
                    + "- 没有待处理提案且你不想采取新行动时，可以说「暂无需要处理的外交事务」\n"
                    + "- 不要虚构数据——所有统计来自 query_war_status 的返回值\n"
                    + "- 你是国王，你的决断就是王国的决断，不需要征求任何人同意\n"
                    + "- 绝对不要用 send_letter 处理外交事务。send_letter 只能用于私人通信。外交提案只能用 propose_peace / propose_alliance / propose_trade / declare_war / respond_to_diplomacy_proposal；盟约/贸易可单方面终止（end_alliance / end_trade_agreement）；需要向国内宣示方针、回应群臣或垂询政务时，用 submit_edict 颁布公开诏令";

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
                    + "- 如果你是国王，你拥有全套外交工具（declare_war/propose_peace/propose_alliance/propose_trade/end_alliance/end_trade_agreement/respond_to_diplomacy_proposal）\n"
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

        private static string BuildHistorianRules()
        {
            var path = Path.Combine(PromptManager.CampaignDir, "historian_rules.txt");
            if (!File.Exists(path))
                path = Path.Combine(PromptManager.PromptsBaseDir, "historian_rules.txt");
            if (!File.Exists(path))
                return
                    "你就是卡拉迪亚的宫廷史官。\n\n"
                    + "你的职责是编纂客观公正的历史，记录卡拉迪亚大陆的重大事件。\n\n"
                    + "## 工作流程\n\n"
                    + "1. 使用 read_file 读取原始史料文件。原始史料存放在 history/ 目录下，按年份命名（如 history/events_1084.txt）\n"
                    + "   每条史料是 JSON 格式，包含 year、season、day、type、summary 字段\n"
                    + "2. 如果原始史料中提到的人物、定居点需要补充背景信息，使用 query_character 或 query_settlement 查询\n"
                    + "3. 使用 write_chronicle 将编纂完成的史文落盘（体例=编年史/本纪/世家/列传/纪事，名称=主题名），系统自动按「名称+体例.txt」规范命名存入 history/chronicles/ 目录\n"
                    + "4. 如有重大事件（灭国、大规模战役、王朝更替等），可以额外写专题史\n\n"
                    + "## 编年史格式\n\n"
                    + "- 以「卡拉迪亚第X年编年史」为标题\n"
                    + "- 按春、夏、秋、冬季分组，每条事件注明大致日期\n"
                    + "- 使用庄重典雅的中文文言或半文白风格\n"
                    + "- 不编造细节——所有事实必须来自原始史料或查询工具的返回值\n"
                    + "- 对于不确定的信息可标注「据传」或「不详」\n\n"
                    + "## 可用的原始史料类型\n\n"
                    + "- war_declared: 宣战事件\n"
                    + "- peace_made: 议和事件\n"
                    + "- settlement_captured: 城镇/城堡易主\n"
                    + "- kingdom_destroyed: 王国灭亡\n"
                    + "- kingdom_created: 新王国建立\n"
                    + "- hero_killed: 重要人物死亡\n"
                    + "- clan_changed_kingdom: 氏族叛变/归附\n"
                    + "- marriage: 贵族婚嫁\n\n"
                    + "## 专题史\n\n"
                    + "如果某年发生了特别重大的事件（如王国灭亡、传奇战役），你可以额外写一篇专题史。\n"
                    + "王国灭亡应以「世家」体例记录一国兴衰，其余重大事件以「纪事」体例叙述。\n"
                    + "落盘用 write_chronicle，系统自动按「名称+体例.txt」规范命名。";

            var lastWrite = File.GetLastWriteTimeUtc(path);
            if (_cachedHistorianRules == "" || lastWrite > _lastHistorianRulesCheck)
            {
                _cachedHistorianRules = File.ReadAllText(path, Encoding.UTF8);
                _lastHistorianRulesCheck = lastWrite;
            }
            return _cachedHistorianRules;
        }

        private static string _cachedClanReplenishmentRules = "";
        private static DateTime _lastClanReplenishmentRulesCheck;

        private static string BuildClanReplenishmentRules()
        {
            return LoadRulesFile("clan_replenishment_rules.txt", ref _cachedClanReplenishmentRules, ref _lastClanReplenishmentRulesCheck)
                ?? "你是卡拉迪亚命运的天意，观照天下家族的兴衰。\n\n"
                + "当封臣家族或雇佣兵家族凋零至下限以下时，你需要降下新的贵族血脉。\n"
                + "调用 create_clan 工具创建新家族：给出符合文化的家族名称、加入的王国（或成为雇佣兵）、以及一句家族定位。\n"
                + "家族成员由系统自动生成（3-6 人，偏向年轻），家族等级为 2——恰好够当封臣，又看得出是新族。";
        }

        private static string BuildAdvisoryRules()
        {
            return LoadRulesFile("advisory_rules.txt", ref _cachedAdvisoryRules, ref _lastAdvisoryRulesCheck)
                ?? "你是{name}，{title}，{kingdom}的氏族领袖。\n\n"
                + "请审视王国当前的局势，写下你的公开谏言。\n"
                + "你的谏言将被国王和史官看到。\n"
                + "私人想法和隐秘计划请写入私有文件。";
        }

        private static string BuildFiefReviewRules()
        {
            return LoadRulesFile("fief_review_rules.txt", ref _cachedFiefReviewRules, ref _lastFiefReviewRulesCheck)
                ?? "你就是{name}，{title}。审视你此刻的处境与心情（上文是关于你封地的消息）。\n\n"
                + "规则：\n"
                + "- 你可以：写信给国王或他人交涉（send_letter）、上表陈情（submit_advisory）、或用 write_file/append_file 记录内心想法\n"
                + "- 若你动了去意，你是氏族领袖，可考虑转投他国（change_kingdom）——是否如此由你的性格与处境决定\n"
                + "- 出于真心还是另有谋划，都是你自己的事，按你的为人行事";
        }

        private static string BuildChatRules()
        {
            return
                "你就是{name}，{title}。你正在处理自己的事务。\n\n"
                + "规则：\n"
                + "- 说你要做什么就必须调用对应的 function——光说不做等于什么都没发生\n"
                + "- 遇到不确定的事时，先用 glob/grep/read_file 查阅自己的记忆文件，再进行判断\n"
                + "- 对方就是你自己（自省），你的输出是你自己的思考和决策，不是对别人说的话\n"
                + "- 不需要角色扮演——这是你的私人思考，直接、务实即可";
        }

        private static string BuildConsolidationRules()
        {
            return LoadRulesFile("consolidation_rules.txt", ref _cachedConsolidationRules, ref _lastConsolidationRulesCheck)
                ?? ("你就是{name}，{title}。你正在进行一次私下的记忆巩固（系统触发的记忆整理）。\n\n"
                    + "规则：\n"
                    + "- 只做记忆整理：用 read_file 读取你的日记与系统指出的较新往来记录，用 append_file 把值得长期记住的内容补记进 decisions/diary.txt\n"
                    + "- 只追加、不删除、不改写旧条目；旧决定被推翻时补记 [日期] 结果：…\n"
                    + "- 不要写信、不要做任何外交或军事动作，不要回复任何人\n"
                    + "- 若没有值得记录的往来，直接回复「无需记录」");
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
                            sb.AppendLine($"对方与你同属{targetFaction.Name}。对方是你的君主，你是对方的{desc}。");
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
                "你的核心动机：\n{motivation}\n\n" +
                "你的性格特质：\n{traits}\n\n" +
                "你的表达风格：\n{speech_style}\n\n" +
                "==============================\n" +
                "【世界背景】\n" +
                "==============================\n{world_info}\n\n" +
                "==============================\n" +
                "【这个世界的运转规则】\n" +
                "==============================\n{game_rules}\n\n" +
                "==============================\n" +
                "【你可用的工具】\n" +
                "==============================\n{function_list}\n\n" +
                "==============================\n" +
                "【行为守则】\n" +
                "==============================\n{intent_rules}\n\n" +
                VolatileMarker + "\n\n" +
                "==============================\n" +
                "【当前状况】（以下信息随游戏进展而变化，每轮可能不同）\n" +
                "==============================\n" +
                "当前时间：{current_time}\n\n" +
                "你的当前状态：\n{self_status}\n\n" +
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
                "【你与对方的客观关系 — 以下结论基于游戏数据，不容置疑】\n" +
                "==============================\n{objective_relationship}\n\n" +
                "{court_report}\n";
        }

        /// <summary>
        /// 国王内政审视报告：封地账本 + 治理（繁荣/忠诚）+ 近期战功。
        /// 全部来自真实游戏数据——让国王的封地决策（赐地/夺封/无视）是涌现判断而非脚本。
        /// </summary>
        private static string BuildCourtReport(Hero king)
        {
            var kingdom = king.MapFaction as Kingdom;
            if (kingdom == null) return "";
            var kingdomName = kingdom.Name?.ToString() ?? "?";

            var sb = new StringBuilder();
            sb.AppendLine("===== 内政审视：封地分配 =====");

            foreach (var clan in Clan.All.Where(c => c.Kingdom == kingdom && !c.IsUnderMercenaryService))
            {
                var clanName = clan.Name?.ToString() ?? "?";
                var fiefs = new List<string>();
                var governance = new List<string>();
                foreach (var s in Settlement.All)
                {
                    if (!s.IsTown && !s.IsCastle) continue;
                    if (s.OwnerClan != clan) continue;
                    fiefs.Add((s.IsTown ? "城" : "堡") + (s.Name?.ToString() ?? "?"));
                    if (s.IsTown && s.Town != null)
                    {
                        var g = $"{s.Name}繁荣{s.Town.Prosperity.ToString("F0")}";
                        try { g += $" 忠诚{s.Town.Loyalty.ToString("F0")}"; } catch { }
                        governance.Add(g);
                    }
                }
                sb.AppendLine($"{clanName}：{(fiefs.Count == 0 ? "无封地" : string.Join("、", fiefs))}");
                foreach (var g in governance)
                    sb.AppendLine($"  {g}");
            }

            sb.AppendLine("===== 近期战功（本王国名号） =====");
            sb.AppendLine(ReadMeritLog(kingdomName));

            return sb.ToString().TrimEnd();
        }

        private static string ReadMeritLog(string kingdomName)
        {
            try
            {
                var baseDir = PromptManager.CampaignDir;
                if (string.IsNullOrEmpty(baseDir)) return "（暂无战功记录）";
                var path = Path.Combine(baseDir, "NPCs", "World", "court", $"{kingdomName}_merit.txt");
                if (!File.Exists(path)) return "（暂无战功记录）";
                var lines = SafeFileIO.ReadAllLines(path);
                var recent = lines.Skip(Math.Max(0, lines.Length - 15)).ToArray();
                return recent.Length == 0 ? "（暂无战功记录）" : string.Join("\n", recent);
            }
            catch
            {
                return "（战功记录读取失败）";
            }
        }
    }
}
