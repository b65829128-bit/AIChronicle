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
        private static string _cachedToolCallPrompt = "";
        private static DateTime _lastToolCallPromptCheck;
        private static List<ToolDef> _cachedTools = new();
        private static DateTime _lastToolsCheck;
        private static List<ToolDef> _cachedAgentTools = new();
        private static DateTime _lastAgentToolsCheck;
        private static string _cachedAgentPrompt = "";
        private static DateTime _lastAgentPromptCheck;

        public static string PromptsBaseDir => _promptsBaseDir;

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

            var destSystemPrompt = Path.Combine(_campaignDir, "system_prompt.txt");
            if (!File.Exists(destSystemPrompt))
            {
                var defaultPath = Path.Combine(_promptsBaseDir, "system_prompt.txt");
                if (File.Exists(defaultPath))
                    File.Copy(defaultPath, destSystemPrompt);
            }

            var npcBase = Path.Combine(_campaignDir, "NPCs");
            AgentManager.Initialize(npcBase);
            EntityManager.Initialize(npcBase);
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

        public static string BuildAgentSystemPrompt(Hero hero, CharacterPrompt charPrompt, string intent = "conversation")
        {
            var agentId = AgentManager.ActiveAgentId;
            var targetId = AgentManager.ActiveTargetId;
            if (!string.IsNullOrEmpty(agentId) && !string.IsNullOrEmpty(targetId))
                return ContextBuilder.Build(agentId, targetId, intent);

            var template = LoadAgentSystemPromptTemplate();
            var worldInfo = MySettings.Instance?.UseWorldInfo == true ? LoadWorldInfo() : "";
            var persona = AgentManager.LoadPersona(hero);

            return template
                .Replace("{persona}", persona)
                .Replace("{world_info}", worldInfo)
                .Replace("{player_knowledge}", "")
                .Replace("{current_time}", GetCurrentTimeString());
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

        public static string? GetWorldInfoPath()
        {
            var path = Path.Combine(_campaignDir, "world_info.txt");
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

        public static string LoadToolCallPrompt()
        {
            var path = Path.Combine(_promptsBaseDir, "tool_call_prompt.txt");
            if (!File.Exists(path))
                return "你是工具调用代理。根据以下对话，判断是否需要调用函数。如果没有新信息，不要调用。";

            var lastWrite = File.GetLastWriteTimeUtc(path);
            if (_cachedToolCallPrompt == "" || lastWrite > _lastToolCallPromptCheck)
            {
                _cachedToolCallPrompt = File.ReadAllText(path, Encoding.UTF8);
                _lastToolCallPromptCheck = lastWrite;
            }
            return _cachedToolCallPrompt;
        }

        public static List<ToolDef> LoadTools()
        {
            var path = Path.Combine(_promptsBaseDir, "tools.json");
            if (!File.Exists(path))
                return _cachedTools;

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
                return new List<ToolDef>();

            var lastWrite = File.GetLastWriteTimeUtc(path);
            if (_cachedAgentTools.Count == 0 || lastWrite > _lastAgentToolsCheck)
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                _cachedAgentTools = JsonConvert.DeserializeObject<List<ToolDef>>(json) ?? new List<ToolDef>();
                _lastAgentToolsCheck = lastWrite;
            }
            return _cachedAgentTools;
        }

        private static string LoadWorldInfo()
        {
            var path = Path.Combine(_campaignDir, "world_info.txt");
            if (!File.Exists(path))
                return "";

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
            var path = AgentManager.GetChatLogPath();
            if (path == null || !File.Exists(path))
                return new List<ChatHistoryEntry>();

            var entries = new List<ChatHistoryEntry>();
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                string role;
                string content;
                var colonIdx = line.IndexOf(": ");

                if (line.StartsWith("[") && colonIdx > 0)
                {
                    var bracketEnd = line.IndexOf("] ");
                    if (bracketEnd > 0 && bracketEnd < colonIdx)
                        role = line.Substring(bracketEnd + 2, colonIdx - bracketEnd - 2);
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
                entries.Add(new ChatHistoryEntry { Role = role, Content = content });
            }
            return entries;
        }

        public static void AppendChatLog(string role, string content)
        {
            var path = AgentManager.GetChatLogPath();
            if (path == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var timestamp = GetCurrentTimeString();
            File.AppendAllText(path, $"[{timestamp}] {role}: " + content.Replace("\n", "\\n") + Environment.NewLine, Encoding.UTF8);
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
    }
}
