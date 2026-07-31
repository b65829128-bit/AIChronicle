using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Library;

namespace MyFirstMod
{
    public static class AgentManager
    {
        private static string _baseDir = "";
        // 并发修复：活动 Agent 上下文改为 AsyncLocal，每个异步流程独立持有，互不覆盖。
        private static readonly AsyncLocal<string> _agentEntityId = new();
        private static readonly AsyncLocal<string> _targetEntityId = new();
        private static string _agentDir => Path.Combine(_baseDir, _agentEntityId.Value ?? "");
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
        private static readonly Random _rng = new();

        private static string _cachedPersonaPrompt = "";
        private static DateTime _lastPersonaPromptCheck;  
        private static readonly HashSet<string> _readableDirs = new()
        {
            "", "knowledge", "relationships", "goals", "chat_logs", "decisions",
            "mailbox", "mailbox/inbox", "mailbox/sent", "diplomacy"
        };

        private static readonly HashSet<string> _writableDirs = new()
        {
            "knowledge", "relationships", "goals", "chat_logs", "decisions",
            "mailbox", "mailbox/inbox", "mailbox/sent"
        };

        private static readonly HashSet<string> _readableWorldFiles = new()
        {
            "factions.txt", "settlements.txt"
        };

        private static readonly HashSet<string> _readableWorldDirs = new()
        {
            "history", "history/chronicles", "advisory"
        };

        public static string ActiveAgentId => _agentEntityId.Value ?? "";
        public static string ActiveTargetId => _targetEntityId.Value ?? "";

        public static void Initialize(string baseDir)
        {
            _baseDir = baseDir;
            Directory.CreateDirectory(_baseDir);
        }

        public static void Activate(string agentEntityId, string targetEntityId)
        {
            _agentEntityId.Value = SanitizeDir(agentEntityId);
            _targetEntityId.Value = SanitizeDir(targetEntityId);
            InitAgentDirectory();
        }

        /// <summary>仅切换活动上下文，不做目录初始化（用于主线程分发工具执行时临时套用/恢复上下文）。</summary>
        internal static void SetContextOnly(string agentEntityId, string targetEntityId)
        {
            _agentEntityId.Value = SanitizeDir(agentEntityId);
            _targetEntityId.Value = SanitizeDir(targetEntityId);
        }

        [Obsolete("Use Activate(agentEntityId, targetEntityId) instead.")]
        public static void SetCurrentNpc(string npcId)
        {
            Activate(npcId, npcId);
        }

        public static string? GetAgentDir()
        {
            if (string.IsNullOrEmpty(_agentEntityId.Value)) return null;
            return _agentDir;
        }

        [Obsolete("Use GetAgentDir() instead.")]
        public static string? GetCurrentNpcDir()
        {
            return GetAgentDir();
        }

        public static string? GetChatLogPath()
        {
            return GetChatLogPathFor(_agentEntityId.Value ?? "", _targetEntityId.Value ?? "");
        }

        public static string? GetChatLogPathFor(string agentEntityId, string targetEntityId)
        {
            if (string.IsNullOrEmpty(_baseDir) || string.IsNullOrEmpty(agentEntityId) || string.IsNullOrEmpty(targetEntityId))
                return null;
            return Path.Combine(_baseDir, agentEntityId, "chat_logs", SanitizeFile(targetEntityId) + ".txt");
        }

        public static string? GetTargetKnowledgePath()
        {
            if (string.IsNullOrEmpty(_agentDir)) return null;
            return Path.Combine(_agentDir, "knowledge", SanitizeFile(_targetEntityId.Value ?? "") + ".txt");
        }

        [Obsolete("Use GetTargetKnowledgePath() instead.")]
        public static string? GetPlayerKnowledgePath()
        {
            return GetTargetKnowledgePath();
        }

        public static string? GetRelationshipPath(string targetEntityId)
        {
            if (string.IsNullOrEmpty(_agentDir)) return null;
            return Path.Combine(_agentDir, "relationships", SanitizeFile(targetEntityId) + ".txt");
        }

        public static string? GetGoalsPath()
        {
            if (string.IsNullOrEmpty(_agentDir)) return null;
            return Path.Combine(_agentDir, "goals", "current.txt");
        }

        private static void InitAgentDirectory()
        {
            var dir = _agentDir;
            if (Directory.Exists(dir)) return;

            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "knowledge"));
            Directory.CreateDirectory(Path.Combine(dir, "relationships"));
            Directory.CreateDirectory(Path.Combine(dir, "goals"));
            Directory.CreateDirectory(Path.Combine(dir, "chat_logs"));
            Directory.CreateDirectory(Path.Combine(dir, "decisions"));
            Directory.CreateDirectory(Path.Combine(dir, "mailbox", "inbox"));
            Directory.CreateDirectory(Path.Combine(dir, "mailbox", "sent"));
        }

        private static string GetPersonaMetaPath()
        {
            return Path.Combine(_agentDir, "persona_meta.json");
        }

        private class PersonaMeta
        {
            public int Ambition { get; set; }
            public int LoyaltyType { get; set; }
            public int RiskTolerance { get; set; }
            public int MandateBelief { get; set; }
        }

        private static PersonaMeta LoadOrCreatePersonaMeta(Hero hero)
        {
            var path = GetPersonaMetaPath();
            return LoadOrCreatePersonaMetaFromPath(path, new PersonaMeta
            {
                Ambition = RollWeightedTrait(skewPositive: true),
                LoyaltyType = RollLoyaltyType(),
                RiskTolerance = RollWeightedTrait(skewPositive: false),
                MandateBelief = RollMandateBelief()
            });
        }

        private static PersonaMeta LoadOrCreatePersonaMetaFromPath(string path, PersonaMeta fallback)
        {
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path, Encoding.UTF8);
                    var meta = JsonConvert.DeserializeObject<PersonaMeta>(json);
                    if (meta != null)
                    {
                        // 旧存档迁移：persona_meta 缺失天命信仰字段时补掷一次并持久化，避免每次加载都变
                        if (!json.Contains("\"MandateBelief\""))
                        {
                            meta.MandateBelief = RollMandateBelief();
                            File.WriteAllText(path, JsonConvert.SerializeObject(meta, Formatting.Indented), Encoding.UTF8);
                        }
                        return meta;
                    }
                }
                catch { }
            }

            var dir = Path.GetDirectoryName(path);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonConvert.SerializeObject(fallback, Formatting.Indented), Encoding.UTF8);
            return fallback;
        }

        private static int RollWeightedTrait(bool skewPositive)
        {
            var roll = _rng.Next(100);
            if (skewPositive)
            {
                if (roll < 5) return -2;
                if (roll < 25) return -1;
                if (roll < 63) return 0;
                if (roll < 88) return 1;
                return 2;
            }
            else
            {
                if (roll < 6) return -2;
                if (roll < 31) return -1;
                if (roll < 69) return 0;
                if (roll < 94) return 1;
                return 2;
            }
        }

        private static int RollLoyaltyType()
        {
            var roll = _rng.Next(100);
            if (roll < 10) return 0;
            if (roll < 50) return 1;
            if (roll < 85) return 2;
            return 3;
        }

        /// <summary>
        /// 天命信仰分布：不信 6% / 假托 20% / 平常 38% / 敬重 26% / 笃信 10%。
        /// 极端少、中间多，保证同一世界里的立场多元。
        /// </summary>
        private static int RollMandateBelief()
        {
            var roll = _rng.Next(100);
            if (roll < 6) return -2;
            if (roll < 26) return -1;
            if (roll < 64) return 0;
            if (roll < 90) return 1;
            return 2;
        }

        private static string BuildNativeTraitsText(Hero hero)
        {
            var sb = new StringBuilder();
            AppendTraitLine(sb, "胆气（Valor）", hero.GetTraitLevel(DefaultTraits.Valor), "勇敢无畏", "胆小怯懦");
            AppendTraitLine(sb, "仁慈（Mercy）", hero.GetTraitLevel(DefaultTraits.Mercy), "仁慈宽厚", "冷酷残忍");
            AppendTraitLine(sb, "荣誉（Honor）", hero.GetTraitLevel(DefaultTraits.Honor), "守信重诺", "狡诈无信");
            AppendTraitLine(sb, "慷慨（Generosity）", hero.GetTraitLevel(DefaultTraits.Generosity), "慷慨感恩", "自私忘恩");
            AppendTraitLine(sb, "谋略（Calculating）", hero.GetTraitLevel(DefaultTraits.Calculating), "深谋远虑", "冲动鲁莽");
            return sb.ToString().TrimEnd();
        }

        private static void AppendTraitLine(StringBuilder sb, string label, int value, string posDesc, string negDesc)
        {
            string desc;
            if (value > 0) desc = $"（{posDesc}）";
            else if (value < 0) desc = $"（{negDesc}）";
            else desc = "（中庸）";
            sb.AppendLine($"{label}：{value:+0;-0} {desc}");
        }

        private static string BuildCustomTraitsText(PersonaMeta meta)
        {
            var sb = new StringBuilder();

            var ambDesc = meta.Ambition switch
            {
                2 => "极度渴望权力与地位，不惜一切代价向上爬",
                1 => "有较强的进取心，希望在仕途上更进一步",
                0 => "对权力持平常心，有则有、无则安",
                -1 => "对权力敬而远之，更愿守好自己的一亩三分地",
                -2 => "厌恶权力斗争，只想过太平日子",
                _ => "?"
            };
            sb.AppendLine($"权力欲：{meta.Ambition:+0;-0} — {ambDesc}");

            var loyDesc = meta.LoyaltyType switch
            {
                0 => "忠于自己——利益至上，不受家族或王国束缚",
                1 => "忠于家族——家族利益高于一切",
                2 => "忠于王国——以王国和君主的利益为优先",
                3 => "忠于信念——坚持自己的理想和原则，超越世俗忠诚",
                _ => "?"
            };
            sb.AppendLine($"归属重心：类型{meta.LoyaltyType} — {loyDesc}");

            var riskDesc = meta.RiskTolerance switch
            {
                2 => "赌徒心态，愿意押上一切博取大收益",
                1 => "偏好适度的风险，善于权衡利弊",
                0 => "稳健行事，不冒不必要的风险",
                -1 => "谨慎保守，常常犹豫不决",
                -2 => "极度保守，惧怕任何冒险",
                _ => "?"
            };
            sb.AppendLine($"冒险倾向：{meta.RiskTolerance:+0;-0} — {riskDesc}");

            var mandateDesc = meta.MandateBelief switch
            {
                2 => "笃信天命，真心信奉天命与大一统，言行以此自处",
                1 => "敬重天命，大体相信，决策时会顾及名分",
                0 => "对天命之说平常心，随大流、不较真",
                -1 => "假托天命，嘴上说信、心里当权术工具",
                -2 => "不信天命，视之为欺人之谈，只信实力",
                _ => "?"
            };
            sb.AppendLine($"天命信仰：{meta.MandateBelief:+0;-0} — {mandateDesc}");

            return sb.ToString().TrimEnd();
        }

        public static string LoadPersona(Hero hero)
        {
            if (string.IsNullOrEmpty(_agentDir))
                return "名字：" + (hero.Name?.ToString() ?? "未知") + "\n性别：" + (hero.IsFemale ? "女" : "男") + "\n文化：" + (hero.Culture?.Name?.ToString() ?? "未知") + "\n说话风格：使用中世纪贵族的正式口吻。";

            var path = Path.Combine(_agentDir, "persona.txt");
            if (File.Exists(path))
                return File.ReadAllText(path, Encoding.UTF8);

            if (hero == Hero.MainHero)
                return "[MOTIVATION]\n你是一位在卡拉迪亚大陆闯荡的冒险者。\n\n[TRAITS]\n- 待探索\n\n[SPEECH_STYLE]\n自由发挥。";

            return GeneratePersona(hero);
        }

        private static string GeneratePersona(Hero hero)
        {
            var name = hero.Name?.ToString() ?? "未知领主";
            var culture = hero.Culture?.Name?.ToString() ?? "未知";
            var clan = hero.Clan?.Name?.ToString() ?? "";
            var kingdom = hero.Clan?.Kingdom?.Name?.ToString() ?? "";
            var isFemale = hero.IsFemale ? "女" : "男";
            var encyclopedia = hero.EncyclopediaText?.ToString() ?? "";

            var basicInfo = new StringBuilder();
            basicInfo.AppendLine($"姓名：{name}");
            basicInfo.AppendLine($"性别：{isFemale}");
            basicInfo.AppendLine($"文化：{culture}");
            basicInfo.AppendLine($"家族：{clan}");
            if (!string.IsNullOrEmpty(kingdom))
                basicInfo.AppendLine($"所属王国：{kingdom}");
            if (!string.IsNullOrEmpty(encyclopedia))
                basicInfo.AppendLine($"百科描述：{encyclopedia}");

            var meta = LoadOrCreatePersonaMeta(hero);
            var nativeTraits = BuildNativeTraitsText(hero);
            var customTraits = BuildCustomTraitsText(meta);

            try
            {
                var persona = GeneratePersonaViaLLM(basicInfo.ToString(), name, nativeTraits, customTraits).Result;
                if (!string.IsNullOrEmpty(persona))
                {
                    var p = Path.Combine(_agentDir, "persona.txt");
                    File.WriteAllText(p, persona, Encoding.UTF8);
                    return persona;
                }
            }
            catch { }

            var fallback = $"[MOTIVATION]\n{basicInfo}\n[TRAITS]\n- 未知\n\n[SPEECH_STYLE]\n使用中世纪贵族的正式口吻。";
            var fallbackPath = Path.Combine(_agentDir, "persona.txt");
            File.WriteAllText(fallbackPath, fallback, Encoding.UTF8);
            return fallback;
        }

        private static async System.Threading.Tasks.Task<string> GeneratePersonaViaLLM(string info, string name, string nativeTraits, string customTraits)
        {
            var settings = MySettings.Instance;
            if (settings == null || string.IsNullOrEmpty(settings.ApiKey))
                return "";

            var prompt = LoadPersonaGenerationPrompt()
                .Replace("{npc_name}", name)
                .Replace("{npc_info}", info)
                .Replace("{native_traits}", nativeTraits)
                .Replace("{custom_traits}", customTraits);

            var payload = new
            {
                model = settings.Model,
                messages = new[] { new { role = "user", content = prompt } },
                max_tokens = 1200,
                temperature = 0.7f
            };

            var json = JsonConvert.SerializeObject(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", "Bearer " + settings.ApiKey);

            var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            var response = await _http.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(body);
            return result["choices"]?[0]?["message"]?["content"]?.ToString()?.Trim() ?? "";
        }

        private static string LoadPersonaGenerationPrompt()
        {
            var path = Path.Combine(PromptManager.CampaignDir, "persona_generation.txt");
            if (!File.Exists(path))
                path = Path.Combine(PromptManager.PromptsBaseDir, "persona_generation.txt");
            if (!File.Exists(path))
                return
                    "你正在为游戏角色生成性格描述。根据以下信息，为名为{npc_name}的NPC生成性格。\n\n"
                    + "严格按格式：\n[MOTIVATION]\n...\n[TRAITS]\n- ...\n[SPEECH_STYLE]\n...\n\n"
                    + "{npc_info}";

            var lastWrite = File.GetLastWriteTimeUtc(path);
            if (_cachedPersonaPrompt == "" || lastWrite > _lastPersonaPromptCheck)
            {
                _cachedPersonaPrompt = File.ReadAllText(path, Encoding.UTF8);
                _lastPersonaPromptCheck = lastWrite;
            }
            return _cachedPersonaPrompt;
        }

        public static string ExecuteReadFile(string path, int? lineStart, int? lineCount)
        {
            if (!IsPathAllowed(path, read: true))
                return "[拒绝] 没有权限读取：" + path;

            var fullPath = ResolvePath(path);
            if (fullPath == null)
                return "[错误] 路径解析失败：" + path;

            if (!File.Exists(fullPath))
                return "[不存在] " + path;

            var lines = SafeFileIO.ReadAllLines(fullPath);

            var start = (lineStart ?? 1) - 1;
            if (start < 0) start = 0;
            if (start >= lines.Length) return "[空] " + path + " 只有 " + lines.Length + " 行";

            var count = lineCount ?? (lines.Length - start);
            var end = Math.Min(start + count, lines.Length);

            var result = new StringBuilder();
            for (var i = start; i < end; i++)
                result.AppendLine((i + 1) + ": " + lines[i]);

            return result.ToString().TrimEnd();
        }

        public static string ExecuteAppendFile(string path, string content)
        {
            if (!IsPathAllowed(path, read: true, write: true))
                return "[拒绝] 没有写入权限：" + path;

            var cleanPath = path.Replace('\\', '/').Trim('/');
            if (cleanPath.StartsWith("chat_logs/") || cleanPath == "chat_logs")
                return "[拒绝] 聊天记录不可修改";
            if (_immutableFiles.Contains(Path.GetFileName(cleanPath)))
                return "[拒绝] 此文件不可修改：" + path;

            var fullPath = ResolvePath(path);
            if (fullPath == null)
                return "[错误] 路径解析失败";

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            var line = content.Replace("\r", "").Replace("\n", " ").Trim();
            SafeFileIO.AppendAllText(fullPath, line + Environment.NewLine);
            return "已写入。";
        }

        public static string ExecuteGrep(string pattern, string path, int maxResults, int contextLines, bool caseSensitive)
        {
            if (string.IsNullOrEmpty(pattern))
                return "[错误] 搜索模式不能为空";
            if (maxResults <= 0 || maxResults > 100)
                maxResults = 20;
            if (contextLines < 0 || contextLines > 10)
                contextLines = 2;

            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var results = new StringBuilder();
            int matchCount = 0;

            if (path == "World")
            {
                var worldDir = Path.Combine(_baseDir, "World");
                if (Directory.Exists(worldDir))
                {
                    foreach (var file in Directory.GetFiles(worldDir, "*.txt", SearchOption.AllDirectories))
                        if (SearchFile(file, worldDir, "World", pattern, comparison, contextLines, maxResults, ref matchCount, results))
                            break;
                }
            }
            else
            {
                if (!IsPathAllowed(path, read: true))
                    return "[拒绝] 没有读取权限：" + path;

                var searchDir = ResolvePath(path);
                if (searchDir == null || string.IsNullOrEmpty(_agentDir))
                    return "[错误] 路径解析失败";

                if (Directory.Exists(searchDir))
                {
                    foreach (var file in Directory.GetFiles(searchDir, "*.*", SearchOption.AllDirectories))
                        if (SearchFile(file, _agentDir, "", pattern, comparison, contextLines, maxResults, ref matchCount, results))
                            break;
                }
                else if (File.Exists(searchDir))
                {
                    SearchFile(searchDir, _agentDir, "", pattern, comparison, contextLines, maxResults, ref matchCount, results);
                }
                else
                {
                    return "[不存在] " + path;
                }
            }

            if (results.Length == 0)
                return "(无匹配结果)";

            var output = results.ToString().TrimEnd();
            if (matchCount >= maxResults)
                output += $"\n…（匹配较多，已显示前 {maxResults} 处；可用更精确的关键词或指定目录缩小范围）";
            return output;
        }

        /// <summary>
        /// 搜索单个文件，返回匹配行及其上下文（contextLines 前后 N 行）。匹配行用 ▶ 标记。
        /// 达到 maxResults 时返回 true，让外层停止继续搜索。相邻匹配的上下文块去重。
        /// </summary>
        private static bool SearchFile(string filePath, string baseDir, string prefix, string pattern, StringComparison comparison, int contextLines, int maxResults, ref int matchCount, StringBuilder results)
        {
            var relPath = filePath.Substring(baseDir.Length).TrimStart('/', '\\').Replace('\\', '/').TrimStart('/');
            if (!string.IsNullOrEmpty(prefix))
                relPath = prefix + "/" + relPath;

            var lines = SafeFileIO.ReadAllLines(filePath);
            int lastShown = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf(pattern, comparison) < 0) continue;

                matchCount++;
                var start = Math.Max(0, i - contextLines);
                var end = Math.Min(lines.Length - 1, i + contextLines);
                for (int j = Math.Max(start, lastShown + 1); j <= end; j++)
                {
                    var marker = j == i ? "▶ " : "  ";
                    results.AppendLine($"{relPath}:{j + 1}: {marker}{lines[j].Trim()}");
                }
                lastShown = Math.Max(lastShown, end);

                if (matchCount >= maxResults)
                    return true;
            }
            return false;
        }

        private static readonly HashSet<string> _immutableFiles = new()
        {
            "persona.txt", "character.json"
        };

        public static string ExecuteEditFile(string path, string oldString, string newString)
        {
            if (!IsPathAllowed(path, read: true, write: true))
                return "[拒绝] 没有写入权限：" + path;

            var cleanPath = path.Replace('\\', '/').Trim('/');
            if (cleanPath.StartsWith("chat_logs/") || cleanPath == "chat_logs")
                return "[拒绝] 聊天记录不可修改";
            if (_immutableFiles.Contains(Path.GetFileName(cleanPath)))
                return "[拒绝] 此文件不可修改：" + path;

            var fullPath = ResolvePath(path);
            if (fullPath == null)
                return "[错误] 路径解析失败：" + path;

            if (!File.Exists(fullPath))
                return "[不存在] " + path;

            var content = SafeFileIO.ReadAllText(fullPath);
            var count = 0;
            var idx = 0;
            while ((idx = content.IndexOf(oldString, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += oldString.Length;
            }

            if (count == 0)
                return "[未找到] 指定的文本在文件中不存在。请用 read_file 确认内容后再试。";
            if (count > 1)
                return $"[冲突] 找到 {count} 处匹配，请提供更多上下文使其唯一。";

            var newContent = content.Replace(oldString, newString);
            SafeFileIO.WriteAllText(fullPath, newContent);
            return "已修改。";
        }

        public static string ExecuteDeleteFile(string path)
        {
            if (!IsPathAllowed(path, read: true, write: true))
                return "[拒绝] 没有删除权限：" + path;

            var cleanPath = path.Replace('\\', '/').Trim('/');
            if (cleanPath.StartsWith("chat_logs/") || cleanPath == "chat_logs")
                return "[拒绝] 聊天记录不可删除";
            if (_immutableFiles.Contains(Path.GetFileName(cleanPath)))
                return "[拒绝] 此文件不可删除：" + path;

            var fullPath = ResolvePath(path);
            if (fullPath == null)
                return "[错误] 路径解析失败：" + path;

            if (!File.Exists(fullPath))
                return "[不存在] " + path;

            File.Delete(fullPath);
            return "已删除。";
        }

        public static string ExecuteMoveFile(string oldPath, string newPath)
        {
            if (!IsPathAllowed(oldPath, read: true, write: true) || !IsPathAllowed(newPath, read: true, write: true))
                return "[拒绝] 没有移动权限";

            var cleanOld = oldPath.Replace('\\', '/').Trim('/');
            var cleanNew = newPath.Replace('\\', '/').Trim('/');
            if (cleanOld.StartsWith("chat_logs/") || cleanOld == "chat_logs"
                || cleanNew.StartsWith("chat_logs/") || cleanNew == "chat_logs")
                return "[拒绝] 聊天记录不可移动";
            if (_immutableFiles.Contains(Path.GetFileName(cleanOld))
                || _immutableFiles.Contains(Path.GetFileName(cleanNew)))
                return "[拒绝] 此文件不可移动";

            var fullOld = ResolvePath(oldPath);
            var fullNew = ResolvePath(newPath);
            if (fullOld == null || fullNew == null)
                return "[错误] 路径解析失败";

            if (!File.Exists(fullOld))
                return "[不存在] " + oldPath;
            if (File.Exists(fullNew))
                return "[已存在] " + newPath + "，目标文件已存在，请用其他名称";

            Directory.CreateDirectory(Path.GetDirectoryName(fullNew)!);
            File.Move(fullOld, fullNew);
            return "已移动。";
        }

        public static string ExecuteWriteFile(string path, string content)
        {
            if (!IsPathAllowed(path, read: true, write: true))
                return "[拒绝] 没有写入权限：" + path;

            var cleanPath = path.Replace('\\', '/').Trim('/');
            if (cleanPath.StartsWith("chat_logs/") || cleanPath == "chat_logs")
                return "[拒绝] 聊天记录不可修改";
            if (_immutableFiles.Contains(Path.GetFileName(cleanPath)))
                return "[拒绝] 此文件不可修改：" + path;

            var fullPath = ResolvePath(path);
            if (fullPath == null)
                return "[错误] 路径解析失败：" + path;

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            SafeFileIO.WriteAllText(fullPath, content.Trim());
            return "已写入。";
        }

        public static string ExecuteGlob(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return "[错误] 模式不能为空";

            pattern = pattern.Replace('\\', '/').Trim('/');

            var isWorld = pattern.StartsWith("World/") || pattern == "World";
            var isWorldHistory = pattern.StartsWith("history/") || pattern == "history";

            string baseDir;
            string searchPattern;

            if (isWorld)
            {
                baseDir = Path.Combine(_baseDir, "World");
                searchPattern = pattern.Substring(6).TrimStart('/');
            }
            else if (isWorldHistory)
            {
                baseDir = Path.Combine(_baseDir, "World");
                searchPattern = pattern;
            }
            else
            {
                baseDir = _agentDir;
                searchPattern = pattern;
            }

            if (string.IsNullOrEmpty(_agentDir) && !isWorld)
                return "[错误] 未设置 agent 目录";
            if (!Directory.Exists(baseDir))
                return "(空)";

            var results = new System.Text.StringBuilder();
            var dir = new DirectoryInfo(baseDir);
            try
            {
                var lastSlash = searchPattern.LastIndexOf('/');
                if (lastSlash >= 0)
                {
                    var subDir = searchPattern.Substring(0, lastSlash);
                    var filePattern = searchPattern.Substring(lastSlash + 1);
                    var searchDir = Path.Combine(baseDir, subDir);
                    if (Directory.Exists(searchDir))
                    {
                        var subDirInfo = new DirectoryInfo(searchDir);
                        var files = subDirInfo.GetFiles(filePattern, SearchOption.TopDirectoryOnly);
                        foreach (var f in files)
                        {
                            var rel = f.FullName.Substring(baseDir.Length).TrimStart('/', '\\').Replace('\\', '/').TrimStart('/');
                            if (isWorld || isWorldHistory) rel = "World/" + rel;
                            results.AppendLine("[FILE] " + rel);
                        }
                    }
                }
                else
                {
                    var files = dir.GetFiles(searchPattern, SearchOption.AllDirectories);
                    foreach (var f in files)
                    {
                        var rel = f.FullName.Substring(baseDir.Length).TrimStart('/', '\\').Replace('\\', '/').TrimStart('/');
                        if (isWorld || isWorldHistory) rel = "World/" + rel;
                        results.AppendLine("[FILE] " + rel);
                    }
                }
            }
            catch { }

            if (results.Length == 0)
                return "(无匹配)";

            return results.ToString().TrimEnd();
        }

        public static string ExecuteListDir(string path)
        {
            var fullPath = ResolvePath(path);
            if (fullPath == null)
                return "[错误] 路径解析失败";

            if (!Directory.Exists(fullPath))
                return "[不存在] " + path;

            var entries = Directory.GetFileSystemEntries(fullPath);
            if (entries.Length == 0)
                return "(空)";

            var result = new StringBuilder();
            foreach (var entry in entries.OrderBy(e => e))
            {
                var fname = Path.GetFileName(entry);
                if (Directory.Exists(entry))
                    result.AppendLine("[DIR]  " + fname + "/");
                else
                    result.AppendLine("[FILE] " + fname);
            }
            return result.ToString().TrimEnd();
        }

        public static List<string> GetAllRelationshipIds()
        {
            if (string.IsNullOrEmpty(_agentDir)) return new List<string>();

            var relDir = Path.Combine(_agentDir, "relationships");
            if (!Directory.Exists(relDir)) return new List<string>();

            var ids = new List<string>();
            foreach (var file in Directory.GetFiles(relDir, "*.txt"))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrEmpty(id))
                    ids.Add(id);
            }
            return ids;
        }

        public static string? ReadRelationship(string targetEntityId)
        {
            var path = GetRelationshipPath(targetEntityId);
            if (path == null || !File.Exists(path)) return null;
            return File.ReadAllText(path, Encoding.UTF8).Trim();
        }

        public static string? ReadKnowledge(string targetEntityId)
        {
            if (string.IsNullOrEmpty(_agentDir)) return null;

            var path = Path.Combine(_agentDir, "knowledge", SanitizeFile(targetEntityId) + ".txt");
            if (File.Exists(path))
                return File.ReadAllText(path, Encoding.UTF8).Trim();

            var entity = EntityManager.GetEntityById(targetEntityId);
            if (entity != null)
            {
                var namePath = Path.Combine(_agentDir, "knowledge", SanitizeFile(entity.Name) + ".txt");
                if (File.Exists(namePath))
                    return File.ReadAllText(namePath, Encoding.UTF8).Trim();
            }

            return null;
        }

        public static string? ReadGoals()
        {
            if (string.IsNullOrEmpty(_agentDir)) return null;
            var path = Path.Combine(_agentDir, "goals", "current.txt");
            if (!File.Exists(path)) return null;
            return File.ReadAllText(path, Encoding.UTF8).Trim();
        }

        private static string GetAgentDirPath(string agentId)
        {
            return Path.Combine(_baseDir, SanitizeDir(agentId));
        }

        public static string LoadPersonaFor(string agentId, Hero hero)
        {
            var agentDir = GetAgentDirPath(agentId);
            if (string.IsNullOrEmpty(_baseDir))
                return "名字：" + (hero.Name?.ToString() ?? "未知") + "\n性别：" + (hero.IsFemale ? "女" : "男") + "\n文化：" + (hero.Culture?.Name?.ToString() ?? "未知") + "\n说话风格：使用中世纪贵族的正式口吻。";

            var path = Path.Combine(agentDir, "persona.txt");
            if (File.Exists(path))
                return File.ReadAllText(path, Encoding.UTF8);

            if (hero == Hero.MainHero)
                return "[MOTIVATION]\n你是一位在卡拉迪亚大陆闯荡的冒险者。\n\n[TRAITS]\n- 待探索\n\n[SPEECH_STYLE]\n自由发挥。";

            return GeneratePersonaFor(agentId, hero);
        }

        public static string? ReadKnowledgeFor(string agentId, string targetEntityId)
        {
            if (string.IsNullOrEmpty(_baseDir)) return null;
            var agentDir = GetAgentDirPath(agentId);
            var path = Path.Combine(agentDir, "knowledge", SanitizeFile(targetEntityId) + ".txt");
            if (File.Exists(path))
                return File.ReadAllText(path, Encoding.UTF8).Trim();

            var entity = EntityManager.GetEntityById(targetEntityId);
            if (entity != null)
            {
                var namePath = Path.Combine(agentDir, "knowledge", SanitizeFile(entity.Name) + ".txt");
                if (File.Exists(namePath))
                    return File.ReadAllText(namePath, Encoding.UTF8).Trim();
            }

            return null;
        }

        public static string? ReadRelationshipFor(string agentId, string targetEntityId)
        {
            if (string.IsNullOrEmpty(_baseDir)) return null;
            var agentDir = GetAgentDirPath(agentId);
            var path = Path.Combine(agentDir, "relationships", SanitizeFile(targetEntityId) + ".txt");
            if (!File.Exists(path)) return null;
            return File.ReadAllText(path, Encoding.UTF8).Trim();
        }

        public static string? ReadGoalsFor(string agentId)
        {
            if (string.IsNullOrEmpty(_baseDir)) return null;
            var agentDir = GetAgentDirPath(agentId);
            var path = Path.Combine(agentDir, "goals", "current.txt");
            if (!File.Exists(path)) return null;
            return File.ReadAllText(path, Encoding.UTF8).Trim();
        }

        public static void AppendDecisionFor(string agentId, string entry)
        {
            if (string.IsNullOrEmpty(_baseDir)) return;
            var agentDir = GetAgentDirPath(agentId);
            var decisionsDir = Path.Combine(agentDir, "decisions");
            Directory.CreateDirectory(decisionsDir);
            File.AppendAllText(Path.Combine(decisionsDir, "diplomacy.txt"), entry, Encoding.UTF8);
        }

        private static string GeneratePersonaFor(string agentId, Hero hero)
        {
            var agentDir = GetAgentDirPath(agentId);
            var name = hero.Name?.ToString() ?? "未知领主";
            var culture = hero.Culture?.Name?.ToString() ?? "未知";
            var clan = hero.Clan?.Name?.ToString() ?? "";
            var kingdom = hero.Clan?.Kingdom?.Name?.ToString() ?? "";
            var isFemale = hero.IsFemale ? "女" : "男";
            var encyclopedia = hero.EncyclopediaText?.ToString() ?? "";

            var basicInfo = new StringBuilder();
            basicInfo.AppendLine($"姓名：{name}");
            basicInfo.AppendLine($"性别：{isFemale}");
            basicInfo.AppendLine($"文化：{culture}");
            basicInfo.AppendLine($"家族：{clan}");
            if (!string.IsNullOrEmpty(kingdom))
                basicInfo.AppendLine($"所属王国：{kingdom}");
            if (!string.IsNullOrEmpty(encyclopedia))
                basicInfo.AppendLine($"百科描述：{encyclopedia}");

            var meta = LoadOrCreatePersonaMetaFor(agentId);
            var nativeTraits = BuildNativeTraitsText(hero);
            var customTraits = BuildCustomTraitsText(meta);

            try
            {
                var persona = GeneratePersonaViaLLM(basicInfo.ToString(), name, nativeTraits, customTraits).Result;
                if (!string.IsNullOrEmpty(persona))
                {
                    var p = Path.Combine(agentDir, "persona.txt");
                    Directory.CreateDirectory(agentDir);
                    File.WriteAllText(p, persona, Encoding.UTF8);
                    return persona;
                }
            }
            catch { }

            var fallback = $"[MOTIVATION]\n{basicInfo}\n[TRAITS]\n- 未知\n\n[SPEECH_STYLE]\n使用中世纪贵族的正式口吻。";
            var fallbackPath = Path.Combine(agentDir, "persona.txt");
            Directory.CreateDirectory(agentDir);
            File.WriteAllText(fallbackPath, fallback, Encoding.UTF8);
            return fallback;
        }

        private static PersonaMeta LoadOrCreatePersonaMetaFor(string agentId)
        {
            var agentDir = GetAgentDirPath(agentId);
            var path = Path.Combine(agentDir, "persona_meta.json");
            return LoadOrCreatePersonaMetaFromPath(path, new PersonaMeta
            {
                Ambition = RollWeightedTrait(skewPositive: true),
                LoyaltyType = RollLoyaltyType(),
                RiskTolerance = RollWeightedTrait(skewPositive: false),
                MandateBelief = RollMandateBelief()
            });
        }

        public static void StoreOutgoingLetter(string senderId, string recipientId, string content)
        {
            var senderDir = Path.Combine(_baseDir, SanitizeDir(senderId));
            var recipientDir = Path.Combine(_baseDir, SanitizeDir(recipientId));

            Directory.CreateDirectory(Path.Combine(senderDir, "mailbox", "sent"));
            Directory.CreateDirectory(Path.Combine(recipientDir, "mailbox", "inbox"));

            var sentPath = Path.Combine(senderDir, "mailbox", "sent", SanitizeFile(recipientId) + ".txt");
            var inboxPath = Path.Combine(recipientDir, "mailbox", "inbox", SanitizeFile(senderId) + ".txt");

            var timestamp = PromptManager.GetCurrentTimeString();
            var entry = "[" + timestamp + "]\n" + content.Trim() + "\n";

            File.AppendAllText(sentPath, entry, Encoding.UTF8);
            File.AppendAllText(inboxPath, entry, Encoding.UTF8);
        }

        public static List<string> ListInbox(string entityId)
        {
            var inboxDir = Path.Combine(_baseDir, entityId, "mailbox", "inbox");
            if (!Directory.Exists(inboxDir))
                return new List<string>();
            return Directory.GetFiles(inboxDir, "*.txt")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList()!;
        }

        public static string? ReadInboxLetter(string entityId, string fileName)
        {
            var path = Path.Combine(_baseDir, entityId, "mailbox", "inbox", SanitizeFile(fileName) + ".txt");
            if (!File.Exists(path)) return null;
            return File.ReadAllText(path, Encoding.UTF8).Trim();
        }

        public static string GetDiplomacyDir()
        {
            var dir = Path.Combine(_baseDir, "World", "diplomacy");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static void StoreDiplomacyProposal(string proposerId, string targetId, string proposalType, string? tributeArg = null, string? message = null)
        {
            var dir = GetDiplomacyDir();
            var fileName = $"{SanitizeDir(proposerId)}_to_{SanitizeDir(targetId)}_{proposalType}.proposal";
            var path = Path.Combine(dir, fileName);
            var content = $"proposer={proposerId}\ntarget={targetId}\ntype={proposalType}";
            if (tributeArg != null)
                content += $"\ntribute={tributeArg}";
            if (!string.IsNullOrEmpty(message))
                content += $"\nmessage={message}";
            File.WriteAllText(path, content, Encoding.UTF8);
        }

        public static List<string> ListPendingProposals(string entityId)
        {
            var dir = GetDiplomacyDir();
            var results = new List<string>();
            if (!Directory.Exists(dir)) return results;
            var sanitizedId = SanitizeDir(entityId);
            foreach (var file in Directory.GetFiles(dir, $"*_to_{sanitizedId}_*.proposal"))
            {
                results.Add(Path.GetFileNameWithoutExtension(file));
            }
            return results;
        }

        public static string? ReadDiplomacyProposal(string proposalFileName)
        {
            var dir = GetDiplomacyDir();
            var path = Path.Combine(dir, SanitizeFile(proposalFileName) + ".proposal");
            if (!File.Exists(path)) return null;
            return File.ReadAllText(path, Encoding.UTF8).Trim();
        }

        public static void DeleteDiplomacyProposal(string proposalFileName)
        {
            var dir = GetDiplomacyDir();
            var path = Path.Combine(dir, SanitizeFile(proposalFileName) + ".proposal");
            if (File.Exists(path)) File.Delete(path);
        }

        public static string? FuzzyFindProposal(string fuzzyId, string targetEntityId)
        {
            var content = ReadDiplomacyProposal(fuzzyId);
            if (content != null) return fuzzyId;

            var pending = ListPendingProposals(targetEntityId);
            if (pending.Count == 0) return null;

            var lowerFuzzy = fuzzyId.ToLowerInvariant();

            string? bestMatch = null;
            int bestScore = 0;

            foreach (var p in pending)
            {
                var pContent = ReadDiplomacyProposal(p);
                if (pContent == null) continue;

                var lowerP = p.ToLowerInvariant();
                var score = 0;

                var typeProposer = ParseProposalMeta(pContent);
                var typeName = FuzzyTypeName(typeProposer.Type);

                if (lowerFuzzy.Contains(typeName)) score += 10;
                if (lowerFuzzy.Contains(typeProposer.Type)) score += 10;

                var proposerParts = typeProposer.ProposerId.ToLowerInvariant().Split('_');
                foreach (var part in proposerParts)
                {
                    if (part.Length >= 2 && lowerFuzzy.Contains(part))
                        score += 5;
                }

                if (lowerP.Contains(lowerFuzzy)) score += 20;
                var commonChars = lowerFuzzy.Intersect(lowerP).Count();
                score += commonChars / 2;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = p;
                }
            }

            return bestScore >= 10 ? bestMatch : null;
        }

        public static (string ProposerId, string TargetId, string Type) ParseProposalMeta(string content)
        {
            var proposerId = "";
            var targetId = "";
            var type = "";
            foreach (var line in content.Split('\n'))
            {
                if (line.StartsWith("proposer=")) proposerId = line.Substring(9);
                else if (line.StartsWith("target=")) targetId = line.Substring(7);
                else if (line.StartsWith("type=")) type = line.Substring(5);
            }
            return (proposerId, targetId, type);
        }

        private static string FuzzyTypeName(string type) => type switch
        {
            "peace" => "peace",
            "alliance" => "alliance",
            "trade" => "trade",
            _ => type
        };

        public static List<(string Id, string Type)> GetProposalsBetween(string entityA, string entityB)
        {
            var result = new List<(string Id, string Type)>();
            var sanitizedA = SanitizeDir(entityA);
            var sanitizedB = SanitizeDir(entityB);

            AddProposalsFromTargetList("A→B");
            AddProposalsFromTargetList("B→A");
            return result;

            void AddProposalsFromTargetList(string direction)
            {
                var targetId = direction == "A→B" ? entityB : entityA;
                var proposerSanitized = direction == "A→B" ? sanitizedA : sanitizedB;
                foreach (var p in ListPendingProposals(targetId))
                {
                    if (p.StartsWith(proposerSanitized + "_to_"))
                    {
                        var parts = p.Split('_');
                        var type = parts.Length >= 2 ? parts[parts.Length - 1] : "?";
                        result.Add((p, type));
                    }
                }
            }
        }

        /// <summary>
        /// 规范化相对路径：统一分隔符、去首尾斜杠。
        /// 安全修复：拒绝任何 `..` 路径穿越段，拒绝盘符/根路径等绝对路径。
        /// </summary>
        private static string? NormalizeRelPath(string relPath)
        {
            if (string.IsNullOrEmpty(relPath)) return null;
            var normalized = relPath.Replace('\\', '/').Trim('/');
            if (normalized.Length == 0) return null;
            if (normalized.Split('/').Any(seg => seg == "..")) return null;
            if (normalized.Contains(':')) return null;
            if (normalized.StartsWith("/")) return null;
            return normalized;
        }

        private static bool IsPathAllowed(string relPath, bool read, bool write = false)
        {
            relPath = NormalizeRelPath(relPath) ?? "";
            if (relPath.Length == 0) return false;

            var dirPart = Path.GetDirectoryName(relPath)?.Replace('\\', '/') ?? "";

            var isWorldPath = _readableWorldFiles.Contains(relPath)
                || _readableWorldDirs.Any(d => relPath.StartsWith(d + "/") || relPath == d);

            if (read && !write)
            {
                if (_readableDirs.Contains(dirPart)) return true;
                if (isWorldPath) return true;
            }

            if (write && _writableDirs.Contains(dirPart))
                return true;

            if (write && _agentEntityId.Value == "__historian__")
            {
                if (relPath.StartsWith("history/chronicles/") || relPath == "history/chronicles")
                    return true;
            }

            return false;
        }

        private static string? ResolvePath(string relPath)
        {
            relPath = NormalizeRelPath(relPath) ?? "";
            // 空路径 = Agent 自己的根目录（如 list_dir("")）
            if (relPath.Length == 0)
            {
                if (string.IsNullOrEmpty(_agentDir)) return null;
                return Path.GetFullPath(_agentDir);
            }

            if (_readableWorldFiles.Contains(relPath))
                return Path.Combine(_baseDir, "World", relPath);

            if (_readableWorldDirs.Any(d => relPath.StartsWith(d + "/") || relPath == d))
                return Path.Combine(_baseDir, "World", relPath);

            if (string.IsNullOrEmpty(_agentDir))
                return null;

            var full = Path.GetFullPath(Path.Combine(_agentDir, relPath));
            var agentRoot = Path.GetFullPath(_agentDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(agentRoot, StringComparison.Ordinal) ? full : null;
        }

        private static string SanitizeDir(string name)
        {
            foreach (var c in Path.GetInvalidPathChars())
                name = name.Replace(c, '_');
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private static string SanitizeFile(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
