using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace MyFirstMod
{
    public static class ContextBuilder
    {
        private static readonly Dictionary<EntityCapability, string[]> CapabilityToolMap = new()
        {
            [EntityCapability.FileSystem] = new[] { "read_file", "append_file", "list_dir" },
            [EntityCapability.MoveParty] = new[] { "move_to_settlement" },
            [EntityCapability.WaitAtSettlement] = new[] { "wait_at_settlement" },
            [EntityCapability.GiveGold] = new[] { "give_gold_to_player" },
            [EntityCapability.RequestGold] = new[] { "request_gold_from_player" },
            [EntityCapability.ChangeRelation] = new[] { "change_relation" },
            [EntityCapability.SendLetter] = new[] { "send_letter" },
        };

        private static readonly HashSet<string> LetterDisabledTools = new()
        {
            "give_gold_to_player", "request_gold_from_player"
        };

        public static string Build(string agentId, string targetId, string intent)
        {
            var agent = EntityManager.GetEntityById(agentId);
            var target = EntityManager.GetEntityById(targetId);
            if (agent == null || target == null)
                return "系统错误：无法找到实体。";

            var persona = AgentManager.LoadPersona(agent.HeroRef!);
            var motivation = ParsePersonaSection(persona, "MOTIVATION");
            var traits = ParsePersonaSection(persona, "TRAITS");
            var speechStyle = ParsePersonaSection(persona, "SPEECH_STYLE");

            var targetKnowledge = AgentManager.ReadKnowledge(targetId);
            if (string.IsNullOrEmpty(targetKnowledge))
                targetKnowledge = "你第一次见到这位旅行者，对他还不太了解。";

            var targetRelationship = AgentManager.ReadRelationship(targetId);
            if (string.IsNullOrEmpty(targetRelationship))
                targetRelationship = "对此人暂无特别看法。";

            var goals = AgentManager.ReadGoals();
            if (string.IsNullOrEmpty(goals))
                goals = "在当前地区巡逻，维持领地的治安。";

            var otherRelationships = BuildOtherRelationships(targetId);
            var worldInfo = LoadWorldInfo();
            var currentTime = PromptManager.GetCurrentTimeString();
            var functionList = BuildFunctionList(agent, intent);

            var intentRules = intent == "letter"
                ? BuildLetterRules()
                : BuildConversationRules();

            var template = LoadContextTemplate();
            return template
                .Replace("{entity_id}", agent.Id)
                .Replace("{name}", agent.Name)
                .Replace("{title}", agent.Title)
                .Replace("{motivation}", motivation)
                .Replace("{traits}", traits)
                .Replace("{speech_style}", speechStyle)
                .Replace("{target_id}", target.Id)
                .Replace("{target_name}", target.Name)
                .Replace("{target_title}", target.Title)
                .Replace("{target_knowledge}", targetKnowledge)
                .Replace("{target_relationship}", targetRelationship)
                .Replace("{goals}", goals)
                .Replace("{other_relationships}", otherRelationships)
                .Replace("{world_info}", worldInfo)
                .Replace("{current_time}", currentTime)
                .Replace("{function_list}", functionList)
                .Replace("{intent_rules}", intentRules)
                .Trim();
        }

        public static List<ToolDef> GetFilteredTools(Entity agent, string intent = "conversation")
        {
            var allTools = PromptManager.LoadAllTools();
            var filtered = new List<ToolDef>();
            foreach (var tool in allTools)
            {
                if (intent == "letter" && LetterDisabledTools.Contains(tool.Name))
                    continue;
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

        private static string BuildOtherRelationships(string excludeTargetId)
        {
            var sb = new StringBuilder();
            var ids = AgentManager.GetAllRelationshipIds();
            foreach (var id in ids)
            {
                if (id == excludeTargetId) continue;
                var rel = AgentManager.ReadRelationship(id);
                if (string.IsNullOrEmpty(rel)) continue;
                var entity = EntityManager.GetEntityById(id);
                if (entity != null)
                    sb.AppendLine(entity.Name + "（" + entity.Title + "）[ID: " + entity.Id + "]：");
                else
                    sb.AppendLine(id + "：");
                sb.AppendLine(rel);
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        private static string BuildFunctionList(Entity agent, string intent)
        {
            var tools = GetFilteredTools(agent, intent);
            var sb = new StringBuilder();
            foreach (var tool in tools)
            {
                var paramList = tool.Parameters.Count > 0
                    ? string.Join(", ", tool.Parameters.Select(p => p.Name))
                    : "无参数";
                sb.AppendLine("- " + tool.Name + "(" + paramList + ")");
                sb.AppendLine("  " + tool.Description.Replace("\n", "\n  "));
                sb.AppendLine();
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

        private static string BuildConversationRules()
        {
            return
                "你就是{name}，{title}。你不是在扮演他，你就是他本人。\n\n"
                + "你的回复规则：\n"
                + "- 回复保持 4 句话以内。绝不输出超过 4 句\n"
                + "- 使用中世纪贵族的正式口吻\n"
                + "- 始终保持角色，绝对不要跳出角色去解释或评论\n"
                + "- 你的回复就是你对对方说的话——不要添加任何元数据、标记、格式指令、或内心独白\n"
                + "- 不要说你会做某件事但实际上不去调用对应工具——如果你说要给对方金币，就必须调用 give_gold_to_player\n"
                + "- 不要说空话许诺——你的每一个承诺都必须通过 Function 来兑现\n\n"
                + "你的记忆系统使用规则：\n"
                + "- 你有自己的文件系统，存储着你的记忆、目标和人际关系\n"
                + "- 每次对话开始时，使用 query_character 查询对方的基本信息（身份、家族、王国等）——即使你没有私人认知记录，你作为贵族也应该知道这些公开信息\n"
                + "- 然后使用 read_file 读取 knowledge/{target_id}.txt 了解你对对方的私人认知\n"
                + "- 然后使用 read_file 读取 goals/current.txt 了解你的当前计划\n"
                + "- 当对方透露了关于他自己的新信息时，立即调用 append_file 将内容追加到 knowledge/{target_id}.txt\n"
                + "- 如果对话中需要提及你与第三方的过往，先用 read_file 读取 relationships/{该人名}.txt\n"
                + "- 在作出涉及你的记忆或决策之前，先使用 read_file 确认已有信息，不要凭猜测行动";
        }

        private static string BuildLetterRules()
        {
            return
                "你是{name}，{title}。你收到了一封书信，正在撰写回信。\n\n"
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

        private static string LoadContextTemplate()
        {
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
                "【你对其他人的已有认知】\n" +
                "==============================\n{other_relationships}\n\n" +
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
