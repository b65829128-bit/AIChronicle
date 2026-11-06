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

namespace AIChronicle
{
    public static partial class AgentManager
{
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
            // 文件较长时给出总行数与本次区间：否则工具结果被截断后，agent 会以为文件已读完。
            if (lines.Length > 40)
                result.AppendLine($"（共 {lines.Length} 行；本次第 {start + 1}–{end} 行）");
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
            if (cleanPath == "decisions/diary.txt")
                MemoryConsolidator.AdvanceWatermark(_agentDir);
            return "已写入。";
        }

        /// <summary>日记日期前缀（与日记检索解析兼容的紧凑格式）：[1090春3] / [1089冬12]。</summary>
        public static string GetDiaryDatePrefix()
        {
            var now = CampaignTime.Now;
            var season = now.GetSeasonOfYear switch
            {
                CampaignTime.Seasons.Spring => "春",
                CampaignTime.Seasons.Summer => "夏",
                CampaignTime.Seasons.Autumn => "秋",
                CampaignTime.Seasons.Winter => "冬",
                _ => "?"
            };
            return $"[{now.GetYear}{season}{now.GetDayOfSeason + 1}]";
        }

        /// <summary>强制日记格式落笔：decisions/diary.txt 追加「[年季节日] 类型：内容」。类型白名单校验，防止日记格式破坏导致记忆检索失效。</summary>
        public static string ExecuteRecordResolve(string type, string content)
        {
            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(content))
                return "[错误] 类型与内容不能为空";
            if (string.IsNullOrEmpty(_agentDir))
                return "[错误] 无当前实体目录";

            var validTypes = new HashSet<string> { "决心", "决定", "承诺", "计策", "情报", "评价", "结果", "战略" };
            if (!validTypes.Contains(type))
                return "[错误] 无效的日记类型：" + type + "。可选：" + string.Join(" / ", validTypes);

            var contentClean = content.Replace("\r", "").Replace("\n", " ").Trim();
            var line = $"{GetDiaryDatePrefix()} {type}：{contentClean}";

            var diaryPath = Path.Combine(_agentDir, "decisions", "diary.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(diaryPath)!);
            SafeFileIO.AppendAllText(diaryPath, line + Environment.NewLine);
            MemoryConsolidator.AdvanceWatermark(_agentDir);
            return "已记入日记。";
        }

        /// <summary>
        /// 系统代写的日记条目（朝议/条约/静默见闻用）：沿用 [年季节日] 类型：内容 格式。
        /// 只给 AI 实体写；玩家（Controller=Human）不写，避免污染玩家档案。
        /// </summary>
        public static void AppendSystemDiary(string entityId, string type, string content)
        {
            AppendSystemDiary(entityId, type, content, GetDiaryDatePrefix());
        }

        /// <summary>同上，但由调用方指定日期前缀（静默见闻用事件发生日，而非 flush 日）。</summary>
        public static void AppendSystemDiary(string entityId, string type, string content, string datePrefix)
        {
            if (string.IsNullOrWhiteSpace(entityId) || string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(content))
                return;
            try
            {
                var entity = EntityManager.GetOrCreateEntityById(entityId);
                if (entity == null || entity.Controller == EntityController.Human)
                    return;
                var dir = EntityManager.GetEntityDirectory(entityId);
                Directory.CreateDirectory(Path.Combine(dir, "decisions"));
                var prefix = string.IsNullOrWhiteSpace(datePrefix) ? GetDiaryDatePrefix() : datePrefix;
                var line = $"{prefix} {type}：{content.Replace("\r", "").Replace("\n", " ").Trim()}";
                SafeFileIO.AppendAllText(Path.Combine(dir, "decisions", "diary.txt"), line + Environment.NewLine);
            }
            catch
            {
                // 日记失败不影响朝议结算
            }
        }

        /// <summary>
        /// 读取自己的日记（默认最近 40 条）。日记会随年数增长，read_file 读整本会被工具结果上限头截断、
        /// 反而看不到最新条目——所以日记用这个工具从尾部读，行号保留，便于继续用 read_file 精确读旧段。
        /// </summary>
        public static string ExecuteReadDiary(int recent, string? type)
        {
            if (string.IsNullOrEmpty(_agentDir))
                return "[错误] 无当前实体目录";

            var diaryPath = Path.Combine(_agentDir, "decisions", "diary.txt");
            var lines = File.Exists(diaryPath) ? SafeFileIO.ReadAllLines(diaryPath) : Array.Empty<string>();
            var total = DiaryStore.CountEntries(lines);
            var filter = string.IsNullOrWhiteSpace(type) ? null : type!.Trim();
            var entries = DiaryStore.SelectRecent(lines, recent <= 0 ? 40 : recent, filter);
            var archives = DiaryStore.ListArchives(_agentDir);

            var sb = new StringBuilder();
            sb.Append("你的日记（decisions/diary.txt）：共 ").Append(total).Append(" 条");
            if (filter != null)
                sb.Append("，其中「").Append(filter).Append("」").Append(DiaryStore.CountByType(lines, filter)).Append(" 条");

            if (entries.Count == 0)
            {
                if (total == 0)
                    sb.AppendLine("（尚是空白）。");
                else if (filter != null)
                    sb.AppendLine($"（没有「{filter}」类型的条目）。");
                else
                    sb.AppendLine("。");
            }
            else
            {
                sb.AppendLine($"；以下为最近 {entries.Count} 条（第 {entries[0].LineNumber}–{entries[entries.Count - 1].LineNumber} 行）。");
            }

            if (archives.Count > 0)
            {
                sb.Append("往年日记：");
                sb.Append(string.Join("、", archives.Select(a => $"decisions/{a.FileName}（{a.Count} 条）")));
                sb.AppendLine("（可用 read_file 查阅）。");
            }

            sb.AppendLine("---");

            if (entries.Count == 0)
                return sb.ToString().TrimEnd();

            // 从最新往回装，保证最新条目一定可见；更早的整条省略。
            // 预算跟随 MCM 的工具结果上限：否则用户把上限调低后，尾部最新条目又会被头截断。
            var toolCap = MySettings.Instance?.MaxToolResultChars ?? 4000;
            var budget = Math.Max(400, Math.Min(3000, toolCap - 1200));
            var outLines = new List<string>();
            var used = 0;
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var line = entries[i].LineNumber + ": " + entries[i].Text;
                if (used + line.Length + 1 > budget)
                {
                    // 单条极长时至少保留最新一条（截断），否则会出现"有日记但什么也没显示"
                    if (outLines.Count == 0)
                    {
                        var cut = Math.Max(40, budget - 20);
                        outLines.Add(line.Length > cut ? line.Substring(0, cut) + "…" : line);
                    }
                    break;
                }
                outLines.Insert(0, line);
                used += line.Length + 1;
            }

            var omitted = entries.Count - outLines.Count;
            if (omitted > 0)
                sb.AppendLine($"（更早的 {omitted} 条已省略；从第 {entries[0].LineNumber} 行起可用 read_file 读）");

            foreach (var l in outLines)
                sb.AppendLine(l);

            return sb.ToString().TrimEnd();
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
                    {
                        // 只搜当前 agent 有权读取的文件（防 grep World 绕过权限读到别国密陈/外交）
                        var rel = file.Substring(worldDir.Length).TrimStart('\\', '/').Replace('\\', '/');
                        if (!IsPathAllowed(rel, read: true)) continue;
                        if (SearchFile(file, worldDir, "World", pattern, comparison, contextLines, maxResults, ref matchCount, results))
                            break;
                    }
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
            if (MemoryProgress.IsMetadataPath(filePath)) return false;
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
            "persona.txt", "character.json", MemoryProgress.MetadataFileName
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

            if (string.IsNullOrEmpty(oldString))
                return "[参数错误] old_string 不能为空。edit_file 用于替换文件中已存在的文本，你必须提供文件中实际存在的 old_string。若你想在文件末尾添加新内容，请改用 append_file；若想整体重写文件，请改用 write_file；若想精确替换，请先用 read_file 读出原文，把要替换的确切片段作为 old_string。";

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
            if (cleanPath == "decisions/diary.txt")
                MemoryConsolidator.AdvanceWatermark(_agentDir);
            return "已写入。";
        }

        /// <summary>史官体例白名单。write_chronicle 只接受这五种体例，防止史官自创体例导致命名混乱。</summary>
        private static readonly string[] ChronicleGenres = { "编年史", "本纪", "世家", "列传", "纪事" };

        /// <summary>
        /// 史官专用成文落盘工具：按「{名称}{体例}.txt」规范自动命名，归档 history/chronicles/。
        /// 史官只负责判断体例与内容，文件名由代码强制规范化——杜绝此前"文件名自定"造成的命名乱象。
        /// </summary>
        public static string ExecuteWriteChronicle(string genre, string name, string content)
        {
            if (_agentEntityId.Value != "__historian__")
                return "[拒绝] write_chronicle 仅史官可用。";

            if (string.IsNullOrWhiteSpace(genre) || !ChronicleGenres.Contains(genre))
                return $"[错误] 体例必须为：{string.Join(" / ", ChronicleGenres)}。你传入的是：{genre}";
            if (string.IsNullOrWhiteSpace(name))
                return "[错误] 名称不能为空";
            if (string.IsNullOrWhiteSpace(content))
                return "[错误] 正文不能为空";

            // 名称清洗：去 .txt 后缀、若名称已误含体例字样则剥除（防 "拉盖娅本纪" + 本纪 → "拉盖娅本纪本纪"）
            var cleanName = HistorianWorkflow.CleanName(genre, name, ChronicleGenres);
            cleanName = SanitizeFile(cleanName.Trim());
            if (cleanName.Length == 0)
                return "[错误] 名称无效";

            var fileName = $"{cleanName}{genre}.txt";
            var relPath = $"history/chronicles/{fileName}";
            if (!IsPathAllowed(relPath, read: true, write: true))
                return "[拒绝] 没有写入权限：" + relPath;

            var fullPath = Path.Combine(_baseDir, "World", "history", "chronicles", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            SafeFileIO.WriteAllText(fullPath, content.Trim());
            return $"已写入史册：{fileName}";
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
                            if (isWorld || isWorldHistory)
                            {
                                if (!IsPathAllowed(rel, read: true)) continue; // 只列出有权读取的文件
                                rel = "World/" + rel;
                            }
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
                        if (isWorld || isWorldHistory)
                        {
                            if (!IsPathAllowed(rel, read: true)) continue; // 只列出有权读取的文件
                            rel = "World/" + rel;
                        }
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
    }
}
