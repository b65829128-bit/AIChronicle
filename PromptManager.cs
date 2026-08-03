using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace MyFirstMod
{
    public class ToolCallData
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Arguments { get; set; } = "";
    }

    public class ChatHistoryEntry
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
        public string? ToolCallId { get; set; }
        public List<ToolCallData>? ToolCalls { get; set; }
        public string? ReasoningContent { get; set; }

        /// <summary>原始时间戳（如"第1088年，秋季第10日，上午"）——加载聊天记录时保留，UI 显示原始发送/回复时间。</summary>
        public string? Time { get; set; }

        /// <summary>是否为信件消息（区别于面对面说话），UI 用 📜 标记。</summary>
        public bool IsLetter { get; set; }

        public static string SerializeList(List<ChatHistoryEntry> entries)
        {
            var sb = new StringBuilder();
            foreach (var e in entries)
            {
                if (e.ToolCalls != null && e.ToolCalls.Count > 0)
                {
                    sb.AppendLine(e.Role + ": " + e.Content.Replace("\n", "\\n"));
                    foreach (var tc in e.ToolCalls)
                        sb.AppendLine($"  -> {tc.Name}({tc.Arguments})");
                }
                else
                {
                    sb.AppendLine(e.Role + ": " + e.Content.Replace("\n", "\\n"));
                }
            }
            return sb.ToString();
        }
    }

    public static class ChatLog
    {
        /// <summary>信件消息在聊天记录中的内容前缀标记——加载时剥掉（不污染给 LLM 的内容），仅用于 UI 识别信件。</summary>
        public const string LetterMarker = "【信】";
    }

    public class CharacterPrompt
    {
        public int Version { get; set; } = 1;
        public string HeroId { get; set; } = "";
        public string HeroName { get; set; } = "";
        [JsonIgnore]
        public List<ChatHistoryEntry> ChatHistory { get; set; } = new();
    }

    public class ToolParamDef
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class ToolDef
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public List<ToolParamDef> Parameters { get; set; } = new();
    }

    public static class PromptManager
    {
        private static string _promptsBaseDir = "";
        private static string _campaignDir = "";
        private static string _charactersDir = "";

        private static string _cachedSystemPrompt = "";
        private static DateTime _lastSystemPromptCheck;
        private static string _cachedWorldInfo = "";
        private static DateTime _lastWorldInfoCheck;
        private static List<ToolDef> _cachedTools = new();
        private static DateTime _lastToolsCheck;
        private static List<ToolDef> _cachedAgentTools = new();
        private static DateTime _lastAgentToolsCheck;
        private static string _cachedAgentPrompt = "";
        private static DateTime _lastAgentPromptCheck;
        private static string _cachedYearlyChroniclePrompt = "";
        private static DateTime _lastYearlyChroniclePromptCheck;
        private static string _cachedSpecialChroniclePrompt = "";
        private static DateTime _lastSpecialChroniclePromptCheck;
        private static string _cachedBiographyPrompt = "";
        private static DateTime _lastBiographyPromptCheck;
        private static string _cachedMemoryConsolidationPrompt = "";
        private static DateTime _lastMemoryConsolidationPromptCheck;

        public static string PromptsBaseDir => _promptsBaseDir;
        public static string CampaignDir => _campaignDir;

        public static bool IsInitialized => _campaignDir != "";

        public static void Initialize(string modulePath)
        {
            _promptsBaseDir = Path.Combine(modulePath, "Prompts");
            Directory.CreateDirectory(_promptsBaseDir);
        }

        public static void StartCampaign(string campaignId)
        {
            _campaignDir = Path.Combine(_promptsBaseDir, "Campaigns", campaignId);
            _charactersDir = Path.Combine(_campaignDir, "Characters");

            Directory.CreateDirectory(_campaignDir);
            Directory.CreateDirectory(_charactersDir);

            var destWorldInfo = Path.Combine(_campaignDir, "world_info.txt");
            if (!File.Exists(destWorldInfo))
            {
                var defaultPath = Path.Combine(_promptsBaseDir, "world_info.txt");
                if (File.Exists(defaultPath))
                    File.Copy(defaultPath, destWorldInfo);
                else
                    File.WriteAllText(destWorldInfo, "（卡拉迪亚大陆，一片充满纷争的土地。）", Encoding.UTF8);
            }
            else
            {
                var defaultPath = Path.Combine(_promptsBaseDir, "world_info.txt");
                try
                {
                    if (File.Exists(defaultPath) && File.GetLastWriteTimeUtc(defaultPath) > File.GetLastWriteTimeUtc(destWorldInfo))
                        File.Copy(defaultPath, destWorldInfo, true);
                }
                catch { }
            }

            // 游戏规则层（与 world_info 相同：基础目录比战役副本新则覆盖，玩家副本手动编辑保留）
            var destGameRules = Path.Combine(_campaignDir, "game_rules.txt");
            if (!File.Exists(destGameRules))
            {
                var defaultGameRules = Path.Combine(_promptsBaseDir, "game_rules.txt");
                if (File.Exists(defaultGameRules))
                    File.Copy(defaultGameRules, destGameRules);
            }
            else
            {
                var defaultGameRules = Path.Combine(_promptsBaseDir, "game_rules.txt");
                try
                {
                    if (File.Exists(defaultGameRules) && File.GetLastWriteTimeUtc(defaultGameRules) > File.GetLastWriteTimeUtc(destGameRules))
                        File.Copy(defaultGameRules, destGameRules, true);
                }
                catch { }
            }

            var destSystemPrompt = Path.Combine(_campaignDir, "system_prompt.txt");
            if (!File.Exists(destSystemPrompt))
            {
                var defaultPath = Path.Combine(_promptsBaseDir, "system_prompt.txt");
                if (File.Exists(defaultPath))
                    File.Copy(defaultPath, destSystemPrompt);
            }
            else
            {
                var defaultPath = Path.Combine(_promptsBaseDir, "system_prompt.txt");
                try
                {
                    if (File.Exists(defaultPath) && File.GetLastWriteTimeUtc(defaultPath) > File.GetLastWriteTimeUtc(destSystemPrompt))
                        File.Copy(defaultPath, destSystemPrompt, true);
                }
                catch { }
            }

            var npcBase = Path.Combine(_campaignDir, "NPCs");
            AgentManager.Initialize(npcBase);
            EntityManager.Initialize(npcBase);
            DebugLogger.Init(_campaignDir);

            CopyPromptToCampaign("agent_system.txt");
            CopyPromptToCampaign("persona_generation.txt");
            CopyPromptToCampaign("diplomacy_rules.txt");
            CopyPromptToCampaign("chancery_rules.txt");
            CopyPromptToCampaign("conversation_rules.txt");
            CopyPromptToCampaign("letter_rules.txt");
            CopyPromptToCampaign("historian_rules.txt");
            CopyPromptToCampaign("yearly_chronicle_prompt.txt");
            CopyPromptToCampaign("special_chronicle_prompt.txt");
            CopyPromptToCampaign("biography_prompt.txt");
            CopyPromptToCampaign("advisory_rules.txt");
            CopyPromptToCampaign("fief_review_rules.txt");
            CopyPromptToCampaign("clan_replenishment_rules.txt");
            CopyPromptToCampaign("consolidation_rules.txt");
            CopyPromptToCampaign("memory_consolidation.txt");
            CopyTemplateToCampaign("context_template.txt");
        }

        private static void CopyPromptToCampaign(string filename)
        {
            var dest = Path.Combine(_campaignDir, filename);
            var src = Path.Combine(_promptsBaseDir, filename);
            if (!File.Exists(src)) return;
            if (!File.Exists(dest))
            {
                File.Copy(src, dest);
                return;
            }
            // 基础目录更新则覆盖战役副本（开发期热同步）；玩家在战役副本上的编辑仍会被保留
            try
            {
                if (File.GetLastWriteTimeUtc(src) > File.GetLastWriteTimeUtc(dest))
                    File.Copy(src, dest, true);
            }
            catch { }
        }

        private static void CopyTemplateToCampaign(string filename)
        {
            var dest = Path.Combine(_campaignDir, filename);
            var src = Path.Combine(_promptsBaseDir, "Templates", filename);
            if (!File.Exists(src)) return;
            if (!File.Exists(dest))
            {
                File.Copy(src, dest);
                return;
            }
            try
            {
                if (File.GetLastWriteTimeUtc(src) > File.GetLastWriteTimeUtc(dest))
                    File.Copy(src, dest, true);
            }
            catch { }
        }

        public static string BuildSystemPrompt(string lordName, CharacterPrompt charPrompt)
        {
            var template = LoadSystemPromptTemplate();
            var worldInfo = MySettings.Instance?.UseWorldInfo == true ? LoadWorldInfo() : "";

            return template
                .Replace("{lord_name}", lordName)
                .Replace("{basic_info}", charPrompt.HeroName)
                .Replace("{world_info}", worldInfo)
                .Replace("{player_knowledge}", "")
                .Replace("{current_time}", GetCurrentTimeString());
        }

        /// <summary>解析当前交互的 agent/target 实体 ID（与构建稳定前缀使用同一套逻辑，保证易变块与稳定前缀指向同一对实体）。</summary>
        public static (string agentId, string targetId) GetAgentTargetIds(Hero hero, string intent = "conversation")
        {
            var agentEntity = EntityManager.GetOrCreateEntity(hero);
            var agentId = agentEntity.Id;

            string targetId;
            if (hero != Hero.MainHero && intent == "conversation" && Hero.MainHero != null)
                targetId = EntityManager.GetOrCreateEntity(Hero.MainHero).Id;
            else
                targetId = AgentManager.ActiveTargetId ?? agentId;

            return (agentId, targetId);
        }

        /// <summary>构建稳定 system 前缀（身份/persona/世界背景/工具清单/行为守则）。
        /// 易变内容（时间/状态/认知/目标/客观关系）由 AIChatClient 单独用 BuildVolatile 作为【当前状况】消息注入。</summary>
        public static string BuildAgentSystemPrompt(Hero hero, CharacterPrompt charPrompt, string intent = "conversation")
        {
            var (agentId, targetId) = GetAgentTargetIds(hero, intent);
            return ContextBuilder.BuildStable(agentId, targetId, intent);
        }

        public static string GetCurrentTimeString()
        {
            if (Campaign.Current == null)
                return "（未知时间）";

            var now = CampaignTime.Now;
            var year = now.GetYear;
            var season = now.GetSeasonOfYear switch
            {
                CampaignTime.Seasons.Spring => "春季",
                CampaignTime.Seasons.Summer => "夏季",
                CampaignTime.Seasons.Autumn => "秋季",
                CampaignTime.Seasons.Winter => "冬季",
                _ => "?"
            };
            var day = now.GetDayOfSeason + 1;
            var hour = now.ToHours % 24;
            var timeOfDay = hour switch
            {
                < 6 => "凌晨",
                < 12 => "上午",
                < 18 => "下午",
                _ => "晚上"
            };

            return $"第{year}年，{season}第{day}日，{timeOfDay}";
        }

        /// <summary>格式化任意时间点为紧凑日期：第{年}年{季}第{日}日（如 第1089年夏第12日），用于到期记录等。</summary>
        public static string FormatCampaignDate(CampaignTime time)
        {
            var year = time.GetYear;
            var season = time.GetSeasonOfYear switch
            {
                CampaignTime.Seasons.Spring => "春",
                CampaignTime.Seasons.Summer => "夏",
                CampaignTime.Seasons.Autumn => "秋",
                CampaignTime.Seasons.Winter => "冬",
                _ => "?"
            };
            var day = time.GetDayOfSeason + 1;
            return $"第{year}年{season}第{day}日";
        }

        public static string? GetWorldInfoPath()
        {
            var path = Path.Combine(_campaignDir, "world_info.txt");
            return File.Exists(path) ? path : null;
        }

        public static string? GetGameRulesPath()
        {
            var path = Path.Combine(_campaignDir, "game_rules.txt");
            return File.Exists(path) ? path : null;
        }

        public static List<ToolDef> LoadAllTools()
        {
            var combined = new List<ToolDef>();
            combined.AddRange(LoadTools());
            combined.AddRange(LoadAgentTools());
            return combined;
        }

        private static string LoadSystemPromptTemplate()
        {
            var path = Path.Combine(_campaignDir, "system_prompt.txt");
            if (!File.Exists(path))
                path = Path.Combine(_promptsBaseDir, "system_prompt.txt");
            if (!File.Exists(path))
                return "你是{lord_name}。{basic_info}\n{world_info}\n你对对方的了解：{player_knowledge}";

            var lastWrite = File.GetLastWriteTimeUtc(path);
            if (_cachedSystemPrompt == "" || lastWrite > _lastSystemPromptCheck)
            {
                _cachedSystemPrompt = File.ReadAllText(path, Encoding.UTF8);
                _lastSystemPromptCheck = lastWrite;
            }
            return _cachedSystemPrompt;
        }

        private static string LoadAgentSystemPromptTemplate()
        {
            var path = Path.Combine(_campaignDir, "agent_system.txt");
            if (!File.Exists(path))
                path = Path.Combine(_promptsBaseDir, "agent_system.txt");
            if (!File.Exists(path))
                return "你是{persona}\n\n{world_info}\n\n你对对方的了解：{player_knowledge}";

            var lastWrite = File.GetLastWriteTimeUtc(path);
            if (_cachedAgentPrompt == "" || lastWrite > _lastAgentPromptCheck)
            {
                _cachedAgentPrompt = File.ReadAllText(path, Encoding.UTF8);
                _lastAgentPromptCheck = lastWrite;
            }
            return _cachedAgentPrompt;
        }

        public static List<ToolDef> LoadTools()
        {
            var path = Path.Combine(_promptsBaseDir, "tools.json");
            if (!File.Exists(path))
                return GetFallbackTools();

            var lastWrite = File.GetLastWriteTimeUtc(path);
            if (_cachedTools.Count == 0 || lastWrite > _lastToolsCheck)
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                _cachedTools = JsonConvert.DeserializeObject<List<ToolDef>>(json) ?? new List<ToolDef>();
                _lastToolsCheck = lastWrite;
            }
            return _cachedTools;
        }

        public static List<ToolDef> LoadAgentTools()
        {
            var path = Path.Combine(_promptsBaseDir, "agent_tools.json");
            if (!File.Exists(path))
                return GetFallbackAgentTools();

            var lastWrite = File.GetLastWriteTimeUtc(path);
            if (_cachedAgentTools.Count == 0 || lastWrite > _lastAgentToolsCheck)
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                _cachedAgentTools = JsonConvert.DeserializeObject<List<ToolDef>>(json) ?? new List<ToolDef>();
                _lastAgentToolsCheck = lastWrite;
            }
            return _cachedAgentTools;
        }

        // tools.json 被删除时的最小兜底工具集（保证 Agent 仍能基本交互）
        private static List<ToolDef> GetFallbackTools()
        {
            return new List<ToolDef>
            {
                new() { Name = "update_knowledge", Category = "universal",
                    Description = "记录对方透露的新信息。",
                    Parameters = new List<ToolParamDef> { new() { Name = "knowledge", Type = "string", Description = "新信息摘要" } } },
                new() { Name = "cancel_action", Category = "universal",
                    Description = "取消当前任务，回归自主行动。" },
                new() { Name = "query_character", Category = "query",
                    Description = "查询人物公开信息（身份/家族/王国/兵力/位置）。",
                    Parameters = new List<ToolParamDef> { new() { Name = "name", Type = "string", Description = "人物名称" } } },
                new() { Name = "query_world_state", Category = "query",
                    Description = "查询世界局势（各王国兵力与交战状态）。" },
                new() { Name = "query_settlement", Category = "query",
                    Description = "查询定居点信息（所有者/繁荣度/类型）。",
                    Parameters = new List<ToolParamDef> { new() { Name = "name", Type = "string", Description = "定居点名称" } } },
                new() { Name = "change_relation", Category = "social",
                    Description = "修改对任意人物的好感度。",
                    Parameters = new List<ToolParamDef>
                    {
                        new() { Name = "target_entity_id", Type = "string", Description = "目标实体 ID 或名称" },
                        new() { Name = "delta", Type = "integer", Description = "好感度变化量" }
                    } },
                new() { Name = "give_gold", Category = "social",
                    Description = "赠予任意人物金币。",
                    Parameters = new List<ToolParamDef>
                    {
                        new() { Name = "target_entity_id", Type = "string", Description = "目标实体 ID 或名称" },
                        new() { Name = "amount", Type = "integer", Description = "金币数量" }
                    } },
                new() { Name = "move_to_settlement", Category = "movement",
                    Description = "移动部队到指定定居点。",
                    Parameters = new List<ToolParamDef> { new() { Name = "settlement_name", Type = "string", Description = "定居点名称" } } }
            };
        }

        // agent_tools.json 被删除时的最小兜底工具集
        private static List<ToolDef> GetFallbackAgentTools()
        {
            return new List<ToolDef>
            {
                new() { Name = "read_file", Category = "file",
                    Description = "读取文件内容。",
                    Parameters = new List<ToolParamDef> { new() { Name = "path", Type = "string", Description = "相对路径" } } },
                new() { Name = "append_file", Category = "file",
                    Description = "追加内容到文件末尾。",
                    Parameters = new List<ToolParamDef>
                    {
                        new() { Name = "path", Type = "string", Description = "相对路径" },
                        new() { Name = "content", Type = "string", Description = "要追加的内容" }
                    } },
                new() { Name = "write_file", Category = "file",
                    Description = "写入/覆盖文件。",
                    Parameters = new List<ToolParamDef>
                    {
                        new() { Name = "path", Type = "string", Description = "相对路径" },
                        new() { Name = "content", Type = "string", Description = "内容" }
                    } },
                new() { Name = "list_dir", Category = "file",
                    Description = "列出目录内容。",
                    Parameters = new List<ToolParamDef> { new() { Name = "path", Type = "string", Description = "相对路径" } } },
                new() { Name = "glob", Category = "file",
                    Description = "按文件名模式匹配。",
                    Parameters = new List<ToolParamDef> { new() { Name = "pattern", Type = "string", Description = "模式，如 knowledge/*.txt" } } },
                new() { Name = "grep", Category = "file",
                    Description = "按关键词搜索文件内容。",
                    Parameters = new List<ToolParamDef> { new() { Name = "keyword", Type = "string", Description = "关键词" } } },
                new() { Name = "send_letter", Category = "communication",
                    Description = "给其他实体写信。",
                    Parameters = new List<ToolParamDef>
                    {
                        new() { Name = "recipient_entity_id", Type = "string", Description = "收信人 ID 或名称" },
                        new() { Name = "content", Type = "string", Description = "信件正文" }
                    } },
                new() { Name = "submit_advisory", Category = "communication",
                    Description = "向国王提交公开谏言。",
                    Parameters = new List<ToolParamDef> { new() { Name = "content", Type = "string", Description = "谏言正文" } } }
            };
        }

        private static string LoadWorldInfo()
        {
            var path = Path.Combine(_campaignDir, "world_info.txt");
            if (!File.Exists(path))
                path = Path.Combine(_promptsBaseDir, "world_info.txt");
            if (!File.Exists(path))
                return "卡拉迪亚大陆，一片充满纷争与传奇的土地。众多王国与帝国征战不休，唯力量与智慧方能立足。";

            var lastWrite = File.GetLastWriteTimeUtc(path);
            if (_cachedWorldInfo == "" || lastWrite > _lastWorldInfoCheck)
            {
                _cachedWorldInfo = File.ReadAllText(path, Encoding.UTF8);
                _lastWorldInfoCheck = lastWrite;
            }
            return _cachedWorldInfo;
        }

        public static CharacterPrompt LoadCharacterPrompt(Hero hero)
        {
            var path = GetCharacterFilePath(hero);
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path, Encoding.UTF8);
                    var prompt = JsonConvert.DeserializeObject<CharacterPrompt>(json);
                    if (prompt != null)
                        return prompt;
                }
                catch { }
            }
            return CreateNewCharacterPrompt(hero);
        }

        public static void SaveCharacterPrompt(CharacterPrompt prompt)
        {
            var path = Path.Combine(_charactersDir, SanitizeFileName(prompt.HeroId) + ".json");
            Directory.CreateDirectory(_charactersDir);
            var json = JsonConvert.SerializeObject(prompt, Formatting.Indented);
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        public static void UpdateTargetKnowledge(string newKnowledge)
        {
            var path = AgentManager.GetTargetKnowledgePath();
            if (path == null) return;

            var existing = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8).Trim() : "";
            var isFirstTime = string.IsNullOrEmpty(existing)
                || existing.Contains("第一次见到")
                || existing.Contains("还不太了解");

            var content = isFirstTime ? newKnowledge : existing + " " + newKnowledge;
            File.WriteAllText(path, content, Encoding.UTF8);
        }

        public static List<ChatHistoryEntry> LoadChatLog()
        {
            return LoadChatLogFor(EntityManager.ActiveAgentId ?? "", EntityManager.ActiveTargetId ?? "");
        }

        public static List<ChatHistoryEntry> LoadChatLogFor(string agentId, string targetId)
        {
            var path = AgentManager.GetChatLogPathFor(agentId, targetId);
            if (path == null || !File.Exists(path))
                return new List<ChatHistoryEntry>();

            var entries = new List<ChatHistoryEntry>();
            var lines = SafeFileIO.ReadAllLines(path); // 带重试：并发追加同一 chat_log 时读可能撞"文件正被使用"
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                string role;
                string content;
                string? time = null;
                var colonIdx = line.IndexOf(": ");

                if (line.StartsWith("[") && colonIdx > 0)
                {
                    var bracketEnd = line.IndexOf("] ");
                    if (bracketEnd > 0 && bracketEnd < colonIdx)
                    {
                        // 保留原始时间戳，供 UI 显示"消息发出的时刻"而非加载时的当前时间
                        time = line.Substring(1, bracketEnd - 1);
                        role = line.Substring(bracketEnd + 2, colonIdx - bracketEnd - 2);
                    }
                    else
                        role = line.Substring(0, colonIdx);
                }
                else if (colonIdx > 0)
                {
                    role = line.Substring(0, colonIdx);
                }
                else
                {
                    continue;
                }

                content = line.Substring(colonIdx + 2).Replace("\\n", "\n");
                var isLetter = content.StartsWith(ChatLog.LetterMarker);
                if (isLetter)
                    content = content.Substring(ChatLog.LetterMarker.Length);
                entries.Add(new ChatHistoryEntry { Role = role, Content = content, Time = time, IsLetter = isLetter });
            }
            return entries;
        }

        public static void AppendChatLog(string role, string content)
        {
            AppendChatLogFor(EntityManager.ActiveAgentId ?? "", EntityManager.ActiveTargetId ?? "", role, content);
        }

        public static void AppendChatLogFor(string agentId, string targetId, string role, string content, bool isLetter = false, string? timestamp = null)
        {
            var path = AgentManager.GetChatLogPathFor(agentId, targetId);
            if (path == null) return;
            // 可选原始时间戳（旧档 mailbox 迁移时保留信件原发出时间）；缺省用当前时间
            var ts = timestamp ?? GetCurrentTimeString();
            // 带重试：同一 chat_log 可能被该 agent 的两个并发事件同时追加（文件正被使用），重试而非报错
            // 信件消息加标记前缀，加载时剥掉用于 UI 区分（不污染给 LLM 的内容）
            var text = (isLetter ? ChatLog.LetterMarker : "") + content.Replace("\n", "\\n");
            SafeFileIO.AppendAllText(path, $"[{ts}] {role}: " + text + Environment.NewLine);
        }

        public static string? ExtractLearnedTag(string response, out string cleanedResponse)
        {
            cleanedResponse = response;

            var tagStart = response.LastIndexOf("[LEARNED:");
            if (tagStart < 0)
                return null;

            var tagEnd = response.IndexOf("]", tagStart);
            if (tagEnd < 0)
                return null;

            var learned = response.Substring(tagStart + 9, tagEnd - tagStart - 9).Trim();
            cleanedResponse = response.Remove(tagStart, tagEnd - tagStart + 1).Trim();
            return learned;
        }

        private static string GetCharacterFilePath(Hero hero)
        {
            var npcDir = AgentManager.GetAgentDir();
            if (npcDir != null)
                return Path.Combine(npcDir, "character.json");

            var name = hero.Name?.ToString() ?? "unknown";
            return Path.Combine(_charactersDir, SanitizeFileName(name) + ".json");
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private static CharacterPrompt CreateNewCharacterPrompt(Hero hero)
        {
            var name = hero.Name?.ToString() ?? "未知领主";
            var prompt = new CharacterPrompt { HeroId = name, HeroName = name };
            SaveCharacterPrompt(prompt);
            return prompt;
        }

        public static string LoadYearlyChroniclePrompt()
        {
            return LoadPromptFile("yearly_chronicle_prompt.txt", ref _cachedYearlyChroniclePrompt, ref _lastYearlyChroniclePromptCheck)
                ?? "请编纂卡拉迪亚第{year}年的编年史。";
        }

        public static string LoadSpecialChroniclePrompt()
        {
            return LoadPromptFile("special_chronicle_prompt.txt", ref _cachedSpecialChroniclePrompt, ref _lastSpecialChroniclePromptCheck)
                ?? "卡拉迪亚发生了一件重大事件：{event_summary}\n\n请为此事件编纂一篇专题史。";
        }

        public static string LoadBiographyPrompt()
        {
            return LoadPromptFile("biography_prompt.txt", ref _cachedBiographyPrompt, ref _lastBiographyPromptCheck)
                ?? "重要人物之死：{event_summary}\n\n请为此人编纂一篇列传。";
        }

        /// <summary>记忆巩固激活指令（自我审视前，日记落后于聊天记录时触发）。{newer_files} 由调用方替换为较新往来文件清单。</summary>
        public static string LoadMemoryConsolidationPrompt()
        {
            return LoadPromptFile("memory_consolidation.txt", ref _cachedMemoryConsolidationPrompt, ref _lastMemoryConsolidationPromptCheck)
                ?? "你的日记（decisions/diary.txt）可能落后于你的往来记录。以下往来记录比日记新：\n{newer_files}\n\n请阅读这些记录，把其中值得长期记住的决定/承诺/计策/约定/结果/战略用 append_file 补记进 decisions/diary.txt（格式 [年季节日] 类型：内容，类型用 决定/承诺/计策/情报/评价/结果/战略）；旧决定被推翻时补记 [日期] 结果：…。不值得记录就回复「无需记录」。";
        }

        private static string? LoadPromptFile(string filename, ref string cache, ref DateTime lastCheck)
        {
            var path = Path.Combine(_campaignDir, filename);
            if (!File.Exists(path))
                path = Path.Combine(_promptsBaseDir, filename);
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
    }
}
