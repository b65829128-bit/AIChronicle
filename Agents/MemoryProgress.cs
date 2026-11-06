using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIChronicle
{
    /// <summary>一次会面内只提醒尚未提醒过的文件版本；不保存记忆正文。</summary>
    public sealed class MemoryRecallSession
    {
        internal string Root = "";
        internal readonly Dictionary<string, string> Notified = new(StringComparer.Ordinal);
    }

    /// <summary>本人记忆的查阅与巩固进度。纯文件逻辑：提示只含查阅指引，进度只含行数/摘要校验。</summary>
    public static class MemoryProgress
    {
        public const string MetadataFileName = ".memory_progress_v1.json";
        public const string Reminder = "你有尚未回顾的新日记或其他往来。答复前请先用 read_diary 查阅，涉及旧事再检索 chat_logs/；不要向对方提及本提示。";
        private static readonly object Gate = new();
        private static readonly AsyncLocal<ConsolidationBatch?> ActiveBatch = new();
        private static readonly Regex NumberedLine = new(@"^(\d+): (.*)$", RegexOptions.Multiline | RegexOptions.Compiled);

        public static bool IsMetadataPath(string path) => string.Equals(
            System.IO.Path.GetFileName(path.Replace('\\', '/')).TrimEnd(' ', '.'), MetadataFileName, StringComparison.OrdinalIgnoreCase);

        private sealed class Cursor
        {
            public int Lines { get; set; }
            public string Hash { get; set; } = "";
        }
        private sealed class State
        {
            public int Version { get; set; } = 1;
            public Dictionary<string, Cursor> Reviewed { get; set; } = new(StringComparer.Ordinal);
            public Dictionary<string, Cursor> Consolidated { get; set; } = new(StringComparer.Ordinal);
        }
        public sealed class Snapshot
        {
            public string Root { get; }
            public string Path { get; }
            internal string[] Content { get; }
            public int Count => Content.Length;
            internal Snapshot(string root, string path, string[] content) { Root = root; Path = path; Content = content; }
            internal string Hash(int count) => Digest(string.Join("\n", Content.Take(count)));
        }
        public sealed class PendingRange
        {
            internal readonly Snapshot Source;
            public string Path => Source.Path;
            public int Start { get; }
            public int End => Source.Count;
            internal PendingRange(Snapshot source, int start) { Source = source; Start = start; }
        }
        public sealed class ToolProbe
        {
            internal string Root = "";
            internal string Tool = "";
            internal string Path = "";
            internal readonly List<Snapshot> Sources = new();
        }

        private static string Digest(string text)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
        }

        private static string? Normalize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var value = path!.Replace('\\', '/');
            if (value.StartsWith("/", StringComparison.Ordinal) || value.Contains(':')
                || value.Split('/').Any(p => p == ".." || p == ".")) return null;
            var parts = value.Split('/');
            if (parts.Length != 2 || !parts[1].EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return null;
            if (parts[0] == "chat_logs" && parts[1].Length > 4) return value;
            if (parts[0] == "decisions" && (parts[1] == "diary.txt" || parts[1].StartsWith("diary_", StringComparison.Ordinal))) return value;
            return null;
        }

        private static Snapshot? Capture(string root, string path)
        {
            var normalized = Normalize(path);
            if (normalized == null) return null;
            var full = System.IO.Path.Combine(root, normalized.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!File.Exists(full)) return null;
            // 不沿符号链接读取其他人物目录。正常记忆文件与其目录均由模组创建。
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0
                || (File.GetAttributes(System.IO.Path.GetDirectoryName(full)!) & FileAttributes.ReparsePoint) != 0) return null;
            return new Snapshot(root, normalized, SafeFileIO.ReadAllLines(full));
        }

        public static Snapshot? CaptureThread(string root, string? filename)
        {
            lock (Gate) return string.IsNullOrEmpty(filename) ? null : Capture(root, "chat_logs/" + System.IO.Path.GetFileName(filename));
        }

        private static List<Snapshot> Sources(string root)
        {
            var result = new List<Snapshot>();
            var diary = Capture(root, "decisions/diary.txt");
            if (diary != null) result.Add(diary);
            var chats = System.IO.Path.Combine(root, "chat_logs");
            if (Directory.Exists(chats) && (File.GetAttributes(chats) & FileAttributes.ReparsePoint) == 0)
                foreach (var file in Directory.GetFiles(chats, "*.txt").OrderBy(f => f, StringComparer.Ordinal))
                {
                    var snapshot = Capture(root, "chat_logs/" + System.IO.Path.GetFileName(file));
                    if (snapshot != null) result.Add(snapshot);
                }
            return result;
        }

        private static State Load(string root)
        {
            var file = System.IO.Path.Combine(root, "decisions", MetadataFileName);
            if (!File.Exists(file)) return new State();
            try
            {
                var state = JsonConvert.DeserializeObject<State>(SafeFileIO.ReadAllText(file));
                if (state?.Version == 1 && state.Reviewed != null && state.Consolidated != null) return state;
            }
            catch (JsonException) { }
            return new State(); // 旧档/缺失/损坏只影响进度，不修改日记或往来。
        }

        private static void Save(string root, State state) => SafeFileIO.WriteAllTextAtomic(
            System.IO.Path.Combine(root, "decisions", MetadataFileName), JsonConvert.SerializeObject(state));

        private static int Position(Dictionary<string, Cursor> cursors, Snapshot snapshot)
        {
            if (!cursors.TryGetValue(snapshot.Path, out var at) || at == null || at.Lines < 0 || at.Lines > snapshot.Count) return 0;
            return at.Hash == snapshot.Hash(at.Lines) ? at.Lines : 0;
        }
        private static Cursor At(Snapshot snapshot, int count) => new() { Lines = count, Hash = snapshot.Hash(count) };

        public static List<PendingRange> PendingChats(string root)
        {
            lock (Gate)
            {
                var state = Load(root);
                return Sources(root).Where(s => s.Path.StartsWith("chat_logs/", StringComparison.Ordinal))
                    .Select(s => new PendingRange(s, Position(state.Consolidated, s) + 1)).Where(r => r.Start <= r.End).ToList();
            }
        }

        public static string BuildReminder(string root, string? currentThread, MemoryRecallSession session)
        {
            lock (Gate)
            {
                if (session.Root != root) { session.Root = root; session.Notified.Clear(); }
                var state = Load(root);
                var changed = false;
                foreach (var snapshot in Sources(root))
                {
                    if (snapshot.Count == 0 || snapshot.Path == "chat_logs/" + currentThread) continue;
                    if (Position(state.Reviewed, snapshot) == snapshot.Count) continue;
                    if (snapshot.Path.StartsWith("chat_logs/", StringComparison.Ordinal)
                        && Position(state.Consolidated, snapshot) == snapshot.Count) continue;
                    var hash = snapshot.Hash(snapshot.Count);
                    if (!session.Notified.TryGetValue(snapshot.Path, out var prior) || prior != hash) changed = true;
                    session.Notified[snapshot.Path] = hash;
                }
                return changed ? Reminder : "";
            }
        }

        public static void AcknowledgePresented(Snapshot? snapshot)
        {
            if (snapshot == null) return;
            lock (Gate)
            {
                var current = Capture(snapshot.Root, snapshot.Path);
                if (current == null || current.Count < snapshot.Count || current.Hash(snapshot.Count) != snapshot.Hash(snapshot.Count)) return;
                var state = Load(snapshot.Root);
                if (Position(state.Reviewed, current) >= snapshot.Count) return;
                state.Reviewed[snapshot.Path] = At(snapshot, snapshot.Count); Save(snapshot.Root, state);
            }
        }

        public static ToolProbe? BeforeTool(string root, string tool, string arguments)
        {
            if (tool != "read_file" && tool != "read_diary" && tool != "grep"
                && tool != "record_resolve" && tool != "append_file" && tool != "write_file") return null;
            lock (Gate)
            {
                JObject args;
                try { args = JObject.Parse(arguments); }
                catch (JsonException)
                {
                    return tool == "record_resolve" || tool == "append_file" || tool == "write_file"
                        ? new ToolProbe { Root = root, Tool = tool, Path = tool == "record_resolve" ? "decisions/diary.txt" : "" } : null;
                }
                var path = tool == "read_diary" || tool == "record_resolve" ? "decisions/diary.txt" : args["path"]?.ToString() ?? "";
                var probe = new ToolProbe { Root = root, Tool = tool, Path = path };
                if (Normalize(path) != null)
                {
                    var snapshot = Capture(root, path);
                    if (snapshot != null) probe.Sources.Add(snapshot);
                }
                else if (tool == "grep" && (path == "" || path == "." || path == "decisions" || path == "chat_logs"))
                    probe.Sources.AddRange(Sources(root).Where(s => path == "" || path == "." || s.Path.StartsWith(path + "/", StringComparison.Ordinal)));
                else return null;
                return probe;
            }
        }

        private static bool Failed(string result) => result.StartsWith("[", StringComparison.Ordinal)
            && !result.StartsWith("[空]", StringComparison.Ordinal);

        public static void AfterTool(ToolProbe? probe, string result)
        {
            if (probe == null) return;
            lock (Gate)
            {
                var writing = probe.Tool == "record_resolve" || probe.Tool == "append_file" || probe.Tool == "write_file";
                var batch = ActiveBatch.Value;
                if (Failed(result))
                {
                    if (writing && (probe.Path == "decisions/diary.txt" || probe.Path == "") && batch?.Root == probe.Root) batch.WriteFailed = true;
                    return;
                }
                if (writing)
                {
                    var before = probe.Sources.FirstOrDefault();
                    var after = Capture(probe.Root, probe.Path);
                    if (after == null) return;
                    var state = Load(probe.Root);
                    // 自己写的新一条不算陌生见闻；原来未读的旧内容仍保持未读。
                    if ((before == null && after.Count == 1) || (before != null && probe.Tool != "write_file" && after.Count == before.Count + 1
                        && after.Hash(before.Count) == before.Hash(before.Count)
                        && Position(state.Reviewed, before) == before.Count))
                    { state.Reviewed[after.Path] = At(after, after.Count); Save(probe.Root, state); }
                    return;
                }
                var readState = Load(probe.Root);
                var dirty = false;
                foreach (var source in probe.Sources)
                {
                    var rows = new HashSet<int>();
                    if (probe.Tool != "grep")
                        foreach (Match m in NumberedLine.Matches(result))
                        {
                            if (!int.TryParse(m.Groups[1].Value, out var n) || n < 1 || n > source.Count) continue;
                            if (m.Groups[2].Value.TrimEnd('\r', ' ', '\t') == source.Content[n - 1].TrimEnd(' ', '\t')) rows.Add(n);
                        }
                    if (batch?.Root == probe.Root && probe.Tool == "read_file") batch.Observe(source, rows);
                    var reviewed = probe.Tool == "grep"
                        ? Normalize(probe.Path) != null || result.Contains(source.Path + ":") || result == "(无匹配结果)"
                        : source.Count == 0 || rows.Contains(source.Count);
                    if (reviewed && Position(readState.Reviewed, source) != source.Count)
                    { readState.Reviewed[source.Path] = At(source, source.Count); dirty = true; }
                }
                if (dirty) Save(probe.Root, readState);
            }
        }

        /// <summary>自动保存本人发言后，只承认新增的本人发言；不吞同时到达的其他记录。</summary>
        public static void AcknowledgeOwnAppend(string root, string path, Snapshot? before)
        {
            lock (Gate)
            {
                var after = Capture(root, path);
                if (after == null) return;
                var state = Load(root);
                if (before != null && (after.Count != before.Count + 1 || after.Hash(before.Count) != before.Hash(before.Count)
                    || Position(state.Reviewed, before) != before.Count)) return;
                if (before == null && after.Count != 1) return;
                state.Reviewed[path] = At(after, after.Count); Save(root, state);
            }
        }

        public sealed class ConsolidationBatch : IDisposable
        {
            internal string Root { get; }
            public IReadOnlyList<PendingRange> Ranges { get; }
            private readonly Dictionary<string, HashSet<int>> _read = new(StringComparer.Ordinal);
            private readonly ConsolidationBatch? _previous;
            internal bool WriteFailed;
            internal ConsolidationBatch(string root)
            {
                Root = root; Ranges = PendingChats(root); _previous = ActiveBatch.Value; ActiveBatch.Value = this;
            }
            internal void Observe(Snapshot source, HashSet<int> rows)
            {
                var range = Ranges.FirstOrDefault(r => r.Path == source.Path);
                if (range == null || source.Count < range.End || source.Hash(range.End) != range.Source.Hash(range.End)) return;
                if (!_read.TryGetValue(source.Path, out var seen)) _read[source.Path] = seen = new HashSet<int>();
                foreach (var row in rows) if (row >= range.Start && row <= range.End) seen.Add(row);
            }
            public int Commit(bool completed)
            {
                if (!completed || WriteFailed) return 0;
                lock (Gate)
                {
                    var state = Load(Root);
                    var changed = 0;
                    foreach (var range in Ranges)
                    {
                        if (!_read.TryGetValue(range.Path, out var seen)) continue;
                        var current = Capture(Root, range.Path);
                        if (current == null || current.Count < range.End || current.Hash(range.End) != range.Source.Hash(range.End)) continue;
                        var at = Position(state.Consolidated, current);
                        var end = at;
                        while (end < range.End && seen.Contains(end + 1)) end++;
                        if (end <= at) continue;
                        state.Consolidated[range.Path] = At(range.Source, end); changed++;
                    }
                    if (changed > 0) Save(Root, state);
                    return changed;
                }
            }
            public void Dispose() { if (ReferenceEquals(ActiveBatch.Value, this)) ActiveBatch.Value = _previous; }
        }

        public static ConsolidationBatch BeginConsolidation(string root) => new(root);
    }
}
