using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace MyFirstMod
{
    public static class AgentManager
    {
        private static string _baseDir = "";
        private static string _agentEntityId = "";
        private static string _targetEntityId = "";
        private static string _agentDir => Path.Combine(_baseDir, _agentEntityId);
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

        private static readonly HashSet<string> _readableDirs = new()
        {
            "", "knowledge", "relationships", "goals", "chat_logs", "decisions",
            "mailbox", "mailbox/inbox", "mailbox/sent"
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

        public static string ActiveAgentId => _agentEntityId;
        public static string ActiveTargetId => _targetEntityId;

        public static void Initialize(string baseDir)
        {
            _baseDir = baseDir;
            Directory.CreateDirectory(_baseDir);
        }

        public static void Activate(string agentEntityId, string targetEntityId)
        {
            _agentEntityId = SanitizeDir(agentEntityId);
            _targetEntityId = SanitizeDir(targetEntityId);
            InitAgentDirectory();
        }

        [Obsolete("Use Activate(agentEntityId, targetEntityId) instead.")]
        public static void SetCurrentNpc(string npcId)
        {
            Activate(npcId, npcId);
        }

        public static string? GetAgentDir()
        {
            if (string.IsNullOrEmpty(_agentEntityId)) return null;
            return _agentDir;
        }

        [Obsolete("Use GetAgentDir() instead.")]
        public static string? GetCurrentNpcDir()
        {
            return GetAgentDir();
        }

        public static string? GetChatLogPath()
        {
            return GetChatLogPathFor(_agentEntityId, _targetEntityId);
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
            return Path.Combine(_agentDir, "knowledge", SanitizeFile(_targetEntityId) + ".txt");
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

            try
            {
                var persona = GeneratePersonaViaLLM(basicInfo.ToString(), name).Result;
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

        private static async System.Threading.Tasks.Task<string> GeneratePersonaViaLLM(string info, string name)
        {
            var settings = MySettings.Instance;
            if (settings == null || string.IsNullOrEmpty(settings.ApiKey))
                return "";

            var prompt = "你正在为一个游戏角色生成性格描述。根据以下信息，为名叫" + name + "的NPC生成性格描述。\n\n"
                + "请严格按以下格式输出（保留所有标签）：\n"
                + "[MOTIVATION]\n"
                + "（2-3句话描述这个角色的核心动机和人生追求）\n\n"
                + "[TRAITS]\n"
                + "- 性格特质1：简要描述\n"
                + "- 性格特质2：简要描述\n"
                + "- 性格特质3：简要描述\n\n"
                + "[SPEECH_STYLE]\n"
                + "（1句话描述这个角色的说话风格和语言习惯，使用中文中世纪贵族口吻）\n\n"
                + "人物信息：\n" + info;

            var payload = new
            {
                model = settings.Model,
                messages = new[] { new { role = "user", content = prompt } },
                max_tokens = 400,
                temperature = 0.7f
            };

            var json = JsonConvert.SerializeObject(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", "Bearer " + settings.ApiKey);

            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(body);
            return result["choices"]?[0]?["message"]?["content"]?.ToString()?.Trim() ?? "";
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

            var lines = File.ReadAllLines(fullPath, Encoding.UTF8);

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

            var fullPath = ResolvePath(path);
            if (fullPath == null)
                return "[错误] 路径解析失败";

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            var line = content.Replace("\r", "").Replace("\n", " ").Trim();
            File.AppendAllText(fullPath, line + Environment.NewLine, Encoding.UTF8);
            return "已写入。";
        }

        public static string ExecuteGrep(string pattern, string path)
        {
            if (string.IsNullOrEmpty(pattern))
                return "[错误] 搜索模式不能为空";

            var results = new StringBuilder();
            var comparison = StringComparison.OrdinalIgnoreCase;

            if (path == "World")
            {
                var worldDir = Path.Combine(_baseDir, "World");
                if (Directory.Exists(worldDir))
                {
                    foreach (var file in Directory.GetFiles(worldDir, "*.txt", SearchOption.AllDirectories))
                        SearchFile(file, worldDir, "World", pattern, comparison, results);
                }
            }
            else
            {
                var searchDir = ResolvePath(path);
                if (searchDir == null || string.IsNullOrEmpty(_agentDir))
                    return "[错误] 路径解析失败";

                if (Directory.Exists(searchDir))
                {
                    foreach (var file in Directory.GetFiles(searchDir, "*.*", SearchOption.AllDirectories))
                        SearchFile(file, _agentDir, "", pattern, comparison, results);
                }
                else if (File.Exists(searchDir))
                {
                    SearchFile(searchDir, _agentDir, "", pattern, comparison, results);
                }
                else
                {
                    return "[不存在] " + path;
                }
            }

            if (results.Length == 0)
                return "(无匹配结果)";

            return results.ToString().TrimEnd();
        }

        private static void SearchFile(string filePath, string baseDir, string prefix, string pattern, StringComparison comparison, StringBuilder results)
        {
            var relPath = filePath.Substring(baseDir.Length).TrimStart('/', '\\').Replace('\\', '/').TrimStart('/');
            if (!string.IsNullOrEmpty(prefix))
                relPath = prefix + "/" + relPath;

            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf(pattern, comparison) >= 0)
                    results.AppendLine(relPath + ":" + (i + 1) + ": " + lines[i].Trim());
            }
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

            var content = File.ReadAllText(fullPath, Encoding.UTF8);
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
            File.WriteAllText(fullPath, newContent, Encoding.UTF8);
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
            File.WriteAllText(fullPath, content.Trim(), Encoding.UTF8);
            return "已写入。";
        }

        public static string ExecuteGlob(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return "[错误] 模式不能为空";

            pattern = pattern.Replace('\\', '/').Trim('/');

            var isWorld = pattern.StartsWith("World/") || pattern == "World";
            var baseDir = isWorld ? Path.Combine(_baseDir, "World") : _agentDir;
            var searchPattern = isWorld ? pattern.Substring(6).TrimStart('/') : pattern;

            if (string.IsNullOrEmpty(_agentDir) && !isWorld)
                return "[错误] 未设置 agent 目录";
            if (!Directory.Exists(baseDir))
                return "(空)";

            var results = new System.Text.StringBuilder();
            var dir = new DirectoryInfo(baseDir);
            try
            {
                var files = dir.GetFiles(searchPattern, System.IO.SearchOption.AllDirectories);
                foreach (var f in files)
                {
                    var rel = f.FullName.Substring(baseDir.Length).TrimStart('/', '\\').Replace('\\', '/').TrimStart('/');
                    if (isWorld) rel = "World/" + rel;
                    results.AppendLine("[FILE] " + rel);
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

        private static bool IsPathAllowed(string relPath, bool read, bool write = false)
        {
            relPath = relPath.Replace('\\', '/').Trim('/');
            var dirPart = Path.GetDirectoryName(relPath)?.Replace('\\', '/') ?? "";

            if (read && !write)
            {
                if (_readableDirs.Contains(dirPart)) return true;
                if (_readableWorldFiles.Contains(relPath)) return true;
            }

            if (write && _writableDirs.Contains(dirPart))
                return true;

            return false;
        }

        private static string? ResolvePath(string relPath)
        {
            relPath = relPath.Replace('\\', '/').Trim('/');

            if (_readableWorldFiles.Contains(relPath))
                return Path.Combine(_baseDir, "World", relPath);

            if (string.IsNullOrEmpty(_agentDir))
                return null;

            var full = Path.Combine(_agentDir, relPath);
            return full.StartsWith(_agentDir) ? full : null;
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
