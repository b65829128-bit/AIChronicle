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
        private static string _currentNpcId = "";
        private static string _currentNpcDir => Path.Combine(_baseDir, _currentNpcId);
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

        private static readonly HashSet<string> _readableDirs = new()
        {
            "", "knowledge", "relationships", "goals", "chat_logs", "decisions"
        };

        private static readonly HashSet<string> _writableDirs = new()
        {
            "knowledge", "relationships", "goals", "chat_logs", "decisions"
        };

        private static readonly HashSet<string> _readableWorldFiles = new()
        {
            "factions.txt", "settlements.txt"
        };

        public static void Initialize(string baseDir)
        {
            _baseDir = baseDir;
            Directory.CreateDirectory(_baseDir);
        }

        public static void SetCurrentNpc(string npcId)
        {
            _currentNpcId = SanitizeDir(npcId);
            InitNpcDirectory();
        }

        public static string? GetCurrentNpcDir()
        {
            if (string.IsNullOrEmpty(_currentNpcDir)) return null;
            return _currentNpcDir;
        }

        public static string? GetChatLogPath()
        {
            if (string.IsNullOrEmpty(_currentNpcDir)) return null;
            var playerName = Hero.MainHero?.Name?.ToString() ?? "player";
            return Path.Combine(_currentNpcDir, "chat_logs", SanitizeFile(playerName) + ".txt");
        }

        public static string? GetPlayerKnowledgePath()
        {
            if (string.IsNullOrEmpty(_currentNpcDir)) return null;
            var playerName = Hero.MainHero?.Name?.ToString() ?? "player";
            return Path.Combine(_currentNpcDir, "knowledge", SanitizeFile(playerName) + ".txt");
        }

        private static void InitNpcDirectory()
        {
            var dir = _currentNpcDir;
            if (Directory.Exists(dir)) return;

            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "knowledge"));
            Directory.CreateDirectory(Path.Combine(dir, "relationships"));
            Directory.CreateDirectory(Path.Combine(dir, "goals"));
            Directory.CreateDirectory(Path.Combine(dir, "chat_logs"));
            Directory.CreateDirectory(Path.Combine(dir, "decisions"));
        }

        public static string LoadPersona(Hero hero)
        {
            var path = Path.Combine(_currentNpcDir, "persona.txt");
            if (File.Exists(path))
                return File.ReadAllText(path, Encoding.UTF8);

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
                    var p = Path.Combine(_currentNpcDir, "persona.txt");
                    File.WriteAllText(p, persona, Encoding.UTF8);
                    return persona;
                }
            }
            catch { }

            var fallback = basicInfo + "说话风格：使用中世纪贵族的正式口吻。";
            var fallbackPath = Path.Combine(_currentNpcDir, "persona.txt");
            File.WriteAllText(fallbackPath, fallback, Encoding.UTF8);
            return fallback;
        }

        private static async System.Threading.Tasks.Task<string> GeneratePersonaViaLLM(string info, string name)
        {
            var settings = MySettings.Instance;
            if (settings == null || string.IsNullOrEmpty(settings.ApiKey))
                return "";

            var prompt = $"你正在为一个游戏角色生成简短的性格描述。根据以下信息，为名叫{name}的NPC生成一段2-3句话的角色描述，包括他的性格特点和说话风格。只输出描述本身，不要加任何前缀。\n\n{info}";

            var payload = new
            {
                model = settings.Model,
                messages = new[] { new { role = "user", content = prompt } },
                max_tokens = 200,
                temperature = 0.7f
            };

            var json = JsonConvert.SerializeObject(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");

            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(body);
            return result["choices"]?[0]?["message"]?["content"]?.ToString()?.Trim() ?? "";
        }

        public static string ExecuteReadFile(string path, int? lineStart, int? lineCount)
        {
            if (!IsPathAllowed(path, read: true))
                return $"[拒绝] 没有权限读取：{path}";

            var fullPath = ResolvePath(path);
            if (fullPath == null)
                return $"[错误] 路径解析失败：{path}";

            if (!File.Exists(fullPath))
                return $"[不存在] {path}";

            var lines = File.ReadAllLines(fullPath, Encoding.UTF8);

            var start = (lineStart ?? 1) - 1;
            if (start < 0) start = 0;
            if (start >= lines.Length) return $"[空] {path} 只有 {lines.Length} 行";

            var count = lineCount ?? (lines.Length - start);
            var end = Math.Min(start + count, lines.Length);

            var result = new StringBuilder();
            for (var i = start; i < end; i++)
                result.AppendLine($"{i + 1}: {lines[i]}");

            return result.ToString().TrimEnd();
        }

        public static string ExecuteAppendFile(string path, string content)
        {
            if (!IsPathAllowed(path, read: true, write: true))
                return $"[拒绝] 没有写入权限：{path}";

            var fullPath = ResolvePath(path);
            if (fullPath == null)
                return $"[错误] 路径解析失败";

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            var line = content.Replace("\r", "").Replace("\n", " ").Trim();
            File.AppendAllText(fullPath, line + Environment.NewLine, Encoding.UTF8);
            return "已写入。";
        }

        public static string ExecuteListDir(string path)
        {
            var fullPath = ResolvePath(path);
            if (fullPath == null)
                return $"[错误] 路径解析失败";

            if (!Directory.Exists(fullPath))
                return $"[不存在] {path}";

            var entries = Directory.GetFileSystemEntries(fullPath);
            if (entries.Length == 0)
                return "(空)";

            var result = new StringBuilder();
            foreach (var entry in entries.OrderBy(e => e))
            {
                var fname = Path.GetFileName(entry);
                if (Directory.Exists(entry))
                    result.AppendLine($"[DIR]  {fname}/");
                else
                    result.AppendLine($"[FILE] {fname}");
            }
            return result.ToString().TrimEnd();
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

            if (string.IsNullOrEmpty(_currentNpcDir))
                return null;

            var full = Path.Combine(_currentNpcDir, relPath);
            return full.StartsWith(_currentNpcDir) ? full : null;
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
