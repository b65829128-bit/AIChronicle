using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AIChronicle
{
    /// <summary>史官的阅读/成文边界与落盘核对；不改变普通人物的工具预算。</summary>
    internal static class HistorianWorkflow
    {
        internal const int FinalRounds = 2;

        internal static bool IsFinalizing(string intent, int round, int budget, bool retry = false)
            => intent == "historian" && (retry || round >= budget - FinalRounds);

        internal static bool CanExecute(bool finalizing, string tool)
            => !finalizing || tool == "write_chronicle";

        internal static JArray WritingTools(JArray tools)
            => new(tools.Where(t => (string?)t["function"]?["name"] == "write_chronicle").Select(t => t.DeepClone()));

        internal static int ToolResultCap(string intent, string tool, string args, int configured)
        {
            if (intent != "historian" || tool != "read_file" || configured <= 0) return configured;
            try
            {
                var path = ((string?)JObject.Parse(args)["path"] ?? "").Replace('\\', '/').Trim('/');
                if (path.StartsWith("World/", StringComparison.Ordinal)) path = path.Substring(6);
                if (path.StartsWith("history/events_", StringComparison.Ordinal) && path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    return Math.Max(configured, 64000);
            }
            catch { }
            return configured;
        }

        // 保留原任务和已经返回的工具证据，不能用一句“尽快成文”重新开空白对话。
        internal static List<JToken> ResumeMessages(IEnumerable<JToken> messages, string instruction)
        {
            var resumed = messages.Select(m => m.DeepClone()).ToList();
            resumed.Add(new JObject { ["role"] = "user", ["content"] = instruction });
            return resumed;
        }

        internal static int ExtractYear(string content)
        {
            var match = Regex.Match(content ?? "", @"第(\d+)年");
            return match.Success && int.TryParse(match.Groups[1].Value, out var year) ? year : -1;
        }

        internal static string CleanName(string genre, string name, IEnumerable<string> genres)
        {
            var clean = name.Trim();
            if (clean.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(0, clean.Length - 4).TrimEnd();
            foreach (var suffix in genres)
                if (clean.EndsWith(suffix, StringComparison.Ordinal) && clean.Length > suffix.Length)
                    clean = clean.Substring(0, clean.Length - suffix.Length).TrimEnd();
            if (genre == "编年史")
            {
                var match = Regex.Match(clean, @"^第?(\d+)年?$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var year) && year > 0) return year.ToString();
            }
            return clean;
        }

        internal static bool HasText(string path)
        {
            try { return File.Exists(path) && !string.IsNullOrWhiteSpace(SafeFileIO.ReadAllText(path)); }
            catch { return false; }
        }

        internal static Dictionary<string, string> Capture(string directory, int year)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!Directory.Exists(directory)) return result;
            var paths = year > 0 ? new[] { Path.Combine(directory, $"{year}编年史.txt") } : Directory.GetFiles(directory, "*.txt");
            foreach (var path in paths)
                if (HasText(path)) result[path] = SafeFileIO.ReadAllText(path);
            return result;
        }

        internal static bool HasChanged(string directory, int year, IReadOnlyDictionary<string, string> before)
            => Capture(directory, year).Any(p => !before.TryGetValue(p.Key, out var old) || old != p.Value);
    }
}
