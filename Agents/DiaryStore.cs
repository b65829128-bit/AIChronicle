using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AIChronicle
{
    /// <summary>日记中的一条记录（LineNumber 为原文件行号，1 起）。</summary>
    public sealed class DiaryEntry
    {
        public int LineNumber { get; set; }
        public string Text { get; set; } = "";
    }

    /// <summary>按年分卷计划：哪些行留在 diary.txt，哪些行移入 diary_{year}.txt。</summary>
    public sealed class DiaryRotationPlan
    {
        public List<string> KeepLines { get; } = new();
        public Dictionary<int, List<string>> MoveByYear { get; } = new();
        public int MovedCount => MoveByYear.Values.Sum(v => v.Count);
    }

    /// <summary>
    /// 日记文件的结构化操作：日期解析、最近条目抽取、按年分卷。
    /// 纯逻辑 + SafeFileIO，不依赖 TaleWorlds，可直接进 tests/AIChronicle.Pure.Tests。
    ///
    /// 日记格式（不可破坏）：[年季节日] 类型：内容，例如 [1089春16] 决定：与南帝国议和。
    /// 日期解析与 MemoryConsolidator 的键编码保持一致（year*400 + season*32 + day），否则"日记是否落后"会误判。
    /// </summary>
    public static class DiaryStore
    {
        public const string DecisionsDir = "decisions";
        public const string DiaryFileName = "diary.txt";

        private static readonly Regex DateRegex =
            new(@"^\s*\[(\d{3,4})年?([春夏秋冬])(?:第)?(\d{1,2})(?:日)?\]", RegexOptions.Compiled);

        // 类型段最多 8 字，避免把内容里的冒号误当类型分隔
        private static readonly Regex TypeRegex =
            new(@"^\s*\[[^\]]+\]\s*([^：:\s]{1,8})\s*[：:]", RegexOptions.Compiled);

        private static readonly Dictionary<char, int> SeasonIndex = new()
        {
            ['春'] = 0, ['夏'] = 1, ['秋'] = 2, ['冬'] = 3
        };

        public static string DiaryPath(string agentDir) =>
            Path.Combine(agentDir, DecisionsDir, DiaryFileName);

        public static string ArchivePath(string agentDir, int year) =>
            Path.Combine(agentDir, DecisionsDir, $"diary_{year}.txt");

        public static bool TryParseDate(string line, out int year, out int season, out int day)
        {
            year = 0;
            season = -1;
            day = 0;
            if (string.IsNullOrEmpty(line)) return false;
            var m = DateRegex.Match(line);
            if (!m.Success) return false;
            year = int.Parse(m.Groups[1].Value);
            if (!SeasonIndex.TryGetValue(m.Groups[2].Value[0], out season)) return false;
            day = int.Parse(m.Groups[3].Value);
            return true;
        }

        /// <summary>日期键：与 MemoryConsolidator 的 DateKey 编码一致。</summary>
        public static bool TryParseDateKey(string line, out int key)
        {
            key = -1;
            if (!TryParseDate(line, out var year, out var season, out var day)) return false;
            key = year * 400 + season * 32 + day;
            return true;
        }

        public static bool TryParseYear(string line, out int year)
        {
            year = 0;
            if (!TryParseDate(line, out var y, out _, out _)) return false;
            year = y;
            return true;
        }

        /// <summary>取条目类型（如 决定/情报）；非标准格式返回 null。</summary>
        public static string? ParseType(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            var m = TypeRegex.Match(line);
            return m.Success ? m.Groups[1].Value : null;
        }

        public static int CountEntries(IEnumerable<string> lines) =>
            lines.Count(l => !string.IsNullOrWhiteSpace(l));

        public static int CountByType(IEnumerable<string> lines, string type) =>
            lines.Count(l => !string.IsNullOrWhiteSpace(l) && string.Equals(ParseType(l), type, StringComparison.Ordinal));

        /// <summary>取最近 N 条（type 非空时先按类型过滤），返回带原文件行号的条目。</summary>
        public static List<DiaryEntry> SelectRecent(string[] lines, int recent, string? type)
        {
            var result = new List<DiaryEntry>();
            if (lines == null || lines.Length == 0) return result;

            var limit = recent <= 0 ? 40 : Math.Min(recent, 200);
            var filter = string.IsNullOrWhiteSpace(type) ? null : type!.Trim();

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (filter != null && !string.Equals(ParseType(line), filter, StringComparison.Ordinal)) continue;
                result.Add(new DiaryEntry { LineNumber = i + 1, Text = line.Trim() });
            }

            if (result.Count > limit)
                result = result.Skip(result.Count - limit).ToList();
            return result;
        }

        /// <summary>
        /// 分卷计划：年份 &lt; currentYear 的条目移入对应年卷；无日期前缀的行（手写/异常格式）留在本年。
        /// </summary>
        public static DiaryRotationPlan PlanRotation(string[] lines, int currentYear)
        {
            var plan = new DiaryRotationPlan();
            if (lines == null) return plan;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (TryParseYear(line, out var year) && year < currentYear)
                {
                    if (!plan.MoveByYear.TryGetValue(year, out var bucket))
                    {
                        bucket = new List<string>();
                        plan.MoveByYear[year] = bucket;
                    }
                    bucket.Add(line);
                }
                else
                {
                    plan.KeepLines.Add(line);
                }
            }

            return plan;
        }

        /// <summary>把 diary.txt 中往年条目并入 diary_{year}.txt（去重追加），只保留本年条目。</summary>
        public static int Rotate(string agentDir, int currentYear)
        {
            try
            {
                var diaryPath = DiaryPath(agentDir);
                if (!File.Exists(diaryPath)) return 0;

                var lines = SafeFileIO.ReadAllLines(diaryPath);
                var plan = PlanRotation(lines, currentYear);
                if (plan.MovedCount == 0) return 0;

                foreach (var kv in plan.MoveByYear)
                {
                    var target = ArchivePath(agentDir, kv.Key);
                    var existing = new HashSet<string>(StringComparer.Ordinal);
                    if (File.Exists(target))
                    {
                        foreach (var old in SafeFileIO.ReadAllLines(target))
                            if (!string.IsNullOrWhiteSpace(old)) existing.Add(old.TrimEnd());
                    }

                    var sb = new StringBuilder();
                    foreach (var line in kv.Value)
                        if (!string.IsNullOrWhiteSpace(line) && existing.Add(line.TrimEnd()))
                            sb.AppendLine(line);

                    if (sb.Length > 0)
                        SafeFileIO.AppendAllText(target, sb.ToString());
                }

                var kept = plan.KeepLines.Count == 0
                    ? ""
                    : string.Join(Environment.NewLine, plan.KeepLines) + Environment.NewLine;
                SafeFileIO.WriteAllText(diaryPath, kept);
                return plan.MovedCount;
            }
            catch
            {
                // 分卷失败不拖垮跨年流程；下个游戏日会再次尝试
                return 0;
            }
        }

        /// <summary>对战役下所有 NPC 目录执行分卷，返回移动的条目数。</summary>
        public static int RotateAll(string npcBaseDir, int currentYear)
        {
            if (string.IsNullOrEmpty(npcBaseDir) || !Directory.Exists(npcBaseDir)) return 0;

            var moved = 0;
            try
            {
                foreach (var dir in Directory.GetDirectories(npcBaseDir))
                {
                    var name = Path.GetFileName(dir);
                    if (string.Equals(name, "World", StringComparison.OrdinalIgnoreCase)) continue;
                    moved += Rotate(dir, currentYear);
                }
            }
            catch
            {
                // 单个目录失败不影响其他 Agent
            }
            return moved;
        }

        /// <summary>列出往年日记卷（新年份在前）及条目数。</summary>
        public static List<(string FileName, int Count)> ListArchives(string agentDir)
        {
            var result = new List<(string FileName, int Count)>();
            var dir = Path.Combine(agentDir, DecisionsDir);
            if (!Directory.Exists(dir)) return result;

            try
            {
                foreach (var file in Directory.GetFiles(dir, "diary_*.txt"))
                {
                    var fname = Path.GetFileName(file);
                    result.Add((fname, CountEntries(SafeFileIO.ReadAllLines(file))));
                }
            }
            catch { }

            return result
                .OrderByDescending(x => ParseArchiveYear(x.FileName))
                .ToList();
        }

        private static int ParseArchiveYear(string fileName)
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var idx = stem.LastIndexOf('_');
            return idx >= 0 && int.TryParse(stem.Substring(idx + 1), out var y) ? y : 0;
        }
    }
}
