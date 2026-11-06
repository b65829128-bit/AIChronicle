using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AIChronicle;
using Newtonsoft.Json.Linq;

namespace AIChronicle.Tests
{
    internal static partial class Program
    {
        private static void RunMemoryTests()
        {
            Run("memory: reminder contains no facts and only repeats for new file versions", TestMemoryReminder);
            Run("memory: current thread is excluded while other conversations can prompt", TestMemoryCurrentThread);
            Run("memory: actual review persists without claiming consolidation", TestMemoryReview);
            Run("memory: partial review does not repeat the same session's reminder", TestMemoryPartialReview);
            Run("memory: same-day messages use independent per-file positions", TestMemorySameDay);
            Run("memory: truncated lines and gaps cannot advance unseen messages", TestMemoryTruncation);
            Run("memory: out-of-order reads require contiguous coverage", TestMemoryReadGaps);
            Run("memory: concurrent new messages are outside the consolidation snapshot", TestMemoryConcurrentAppend);
            Run("memory: rewritten or shortened files invalidate the old prefix", TestMemoryRewrite);
            Run("memory: incomplete pass or failed diary write cannot commit", TestMemoryFailedPass);
            Run("memory: own diary writes keep unread old or concurrent facts unread", TestMemoryOwnWrites);
            Run("memory: presented history acknowledges only the captured version", TestMemoryPresented);
            Run("memory: own saved reply avoids redundant reminders", TestMemoryOwnReply);
            Run("memory: foreign paths, world files and links cannot enter private progress", TestMemoryIsolation);
            Run("memory: async consolidation scopes and campaign roots remain isolated", TestMemoryAsyncScopes);
            Run("memory: missing diary and damaged progress recover without editing sources", TestMemoryRecovery);
        }

        private sealed class MemoryFixture : IDisposable
        {
            public string Root { get; } = Path.Combine(Path.GetTempPath(), "aichronicle-memory-" + Guid.NewGuid().ToString("N"));
            public string Full(string path) => Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            public void Write(string path, params string[] lines) => SafeFileIO.WriteAllText(Full(path), string.Join("\n", lines) + "\n");
            public void Append(string path, string line) => SafeFileIO.AppendAllText(Full(path), line + "\n");
            public string Output(string path) => string.Join("\n", SafeFileIO.ReadAllLines(Full(path)).Select((line, i) => (i + 1) + ": " + line));
            public void Read(string path)
            {
                var probe = MemoryProgress.BeforeTool(Root, "read_file", new JObject { ["path"] = path }.ToString());
                MemoryProgress.AfterTool(probe, Output(path));
            }
            public string Remind(MemoryRecallSession? session = null, string? current = null) => MemoryProgress.BuildReminder(Root, current, session ?? new());
            public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
        }

        private static void TestMemoryReminder()
        {
            using var f = new MemoryFixture(); var session = new MemoryRecallSession();
            f.Write("decisions/diary.txt", "[1084秋16] 承诺：SECRET_欠炎瑰三万金币");
            var prompt = f.Remind(session);
            AssertEqual(MemoryProgress.Reminder, prompt); AssertTrue(prompt.Length < 150, "one short instruction");
            AssertFalse(prompt.Contains("SECRET") || prompt.Contains("炎瑰") || prompt.Contains("三万"), "no memory fact, name, summary or obligation injected");
            AssertEqual("", f.Remind(session), "unchanged batch only prompts once in a meeting");
            f.Append("decisions/diary.txt", "[1084秋16] 情报：同一天的新事");
            AssertEqual(MemoryProgress.Reminder, f.Remind(session)); AssertEqual("", f.Remind(session));
        }

        private static void TestMemoryCurrentThread()
        {
            using var f = new MemoryFixture();
            f.Write("chat_logs/炎瑰_main_hero.txt", "[第1084年，秋季第16日，下午] user: 近况如何");
            AssertEqual("", f.Remind(current: "炎瑰_main_hero.txt"), "current conversation is already supplied by normal history");
            f.Write("chat_logs/卡拉多格_lord_5_1.txt", "[第1084年，秋季第16日，下午] assistant: 同盟已订立");
            AssertEqual(MemoryProgress.Reminder, f.Remind(current: "炎瑰_main_hero.txt"), "other thread can contain new relevant facts");
        }

        private static void TestMemoryReview()
        {
            using var f = new MemoryFixture();
            f.Write("decisions/diary.txt", "[1084秋16] 情报：SECRET_旧事");
            f.Write("chat_logs/other.txt", "[第1084年，秋季第16日，下午] user: SECRET_往来");
            var diary = MemoryProgress.BeforeTool(f.Root, "read_diary", "{}");
            MemoryProgress.AfterTool(diary, "你的日记\n---\n" + f.Output("decisions/diary.txt")); f.Read("chat_logs/other.txt");
            AssertEqual("", f.Remind(), "new meeting respects actual prior review");
            AssertEqual(1, MemoryProgress.PendingChats(f.Root).Single().Start, "ordinary review never claims consolidation");
            var metadata = File.ReadAllText(f.Full("decisions/" + MemoryProgress.MetadataFileName));
            AssertFalse(metadata.Contains("SECRET") || metadata.Contains("情报"), "metadata stores no content");
            AssertEqual(1, JObject.Parse(metadata)["Version"]!.Value<int>(), "round-trip versioned metadata");
        }

        private static void TestMemoryPartialReview()
        {
            using var f = new MemoryFixture(); var session = new MemoryRecallSession();
            f.Write("decisions/diary.txt", "日记"); f.Write("chat_logs/other.txt", "往来");
            AssertEqual(MemoryProgress.Reminder, f.Remind(session));
            var grep = MemoryProgress.BeforeTool(f.Root, "grep", "{\"path\":\"decisions\",\"pattern\":\"旧事\"}");
            MemoryProgress.AfterTool(grep, "(无匹配结果)");
            AssertEqual("", f.Remind(session), "remaining files are the same already-notified batch");
            AssertEqual(MemoryProgress.Reminder, f.Remind(), "a later meeting can remind about the still-unread other thread");
            f.Read("chat_logs/other.txt"); AssertEqual("", f.Remind());
        }

        private static void TestMemorySameDay()
        {
            using var f = new MemoryFixture();
            f.Write("decisions/.diary_authored", "433680");
            f.Write("chat_logs/a.txt", "[第1084年，秋季第16日，凌晨] assistant: 第一件事");
            f.Write("chat_logs/b.txt", "[第1084年，秋季第16日，凌晨] assistant: 另一条往来");
            using (var batch = MemoryProgress.BeginConsolidation(f.Root))
            { f.Read("chat_logs/a.txt"); AssertEqual(1, batch.Commit(true)); }
            var pending = MemoryProgress.PendingChats(f.Root); AssertEqual("chat_logs/b.txt", pending.Single().Path);
            f.Append("chat_logs/a.txt", "[第1084年，秋季第16日，下午] assistant: 后来追加的承诺");
            pending = MemoryProgress.PendingChats(f.Root);
            AssertEqual(2, pending.Single(r => r.Path == "chat_logs/a.txt").Start, "same date does not hide new message");
            AssertEqual(1, pending.Single(r => r.Path == "chat_logs/b.txt").Start, "one conversation never marks another processed");
            AssertEqual("433680", File.ReadAllText(f.Full("decisions/.diary_authored")).Trim(), "legacy watermark is not migrated or rewritten");
        }

        private static void TestMemoryTruncation()
        {
            using var f = new MemoryFixture(); f.Write("chat_logs/a.txt", "完整消息", "这是一条没有完整返回的消息", "第三条");
            using (var batch = MemoryProgress.BeginConsolidation(f.Root))
            {
                var probe = MemoryProgress.BeforeTool(f.Root, "read_file", "{\"path\":\"chat_logs/a.txt\"}");
                MemoryProgress.AfterTool(probe, "1: 完整消息\n2: 这是一条没有\n…（工具结果过长）");
                AssertEqual(1, batch.Commit(true));
            }
            AssertEqual(2, MemoryProgress.PendingChats(f.Root).Single().Start, "partial last line is still unprocessed");
        }

        private static void TestMemoryReadGaps()
        {
            using var f = new MemoryFixture(); f.Write("chat_logs/a.txt", "一", "二", "三");
            using (var batch = MemoryProgress.BeginConsolidation(f.Root))
            {
                var probe = MemoryProgress.BeforeTool(f.Root, "read_file", "{\"path\":\"chat_logs/a.txt\"}");
                MemoryProgress.AfterTool(probe, "3: 三\n1: 一"); AssertEqual(1, batch.Commit(true));
            }
            AssertEqual(2, MemoryProgress.PendingChats(f.Root).Single().Start, "reading the tail does not skip a missing middle message");
            using (var batch = MemoryProgress.BeginConsolidation(f.Root))
            { f.Read("chat_logs/a.txt"); AssertEqual(1, batch.Commit(true)); }
            AssertEqual(0, MemoryProgress.PendingChats(f.Root).Count);
        }

        private static void TestMemoryConcurrentAppend()
        {
            using var f = new MemoryFixture(); f.Write("chat_logs/a.txt", "一", "二");
            using (var batch = MemoryProgress.BeginConsolidation(f.Root))
            {
                var probe = MemoryProgress.BeforeTool(f.Root, "read_file", "{\"path\":\"chat_logs/a.txt\"}");
                f.Append("chat_logs/a.txt", "后来到达的三");
                MemoryProgress.AfterTool(probe, f.Output("chat_logs/a.txt")); AssertEqual(1, batch.Commit(true));
            }
            AssertEqual(3, MemoryProgress.PendingChats(f.Root).Single().Start, "new arrival belongs to next batch even if file grew during a tool read");
            AssertEqual(MemoryProgress.Reminder, f.Remind(), "unread concurrent tail remains visible to reminders");
        }

        private static void TestMemoryRewrite()
        {
            using var f = new MemoryFixture(); f.Write("chat_logs/a.txt", "旧一", "旧二");
            using (var batch = MemoryProgress.BeginConsolidation(f.Root))
            { f.Read("chat_logs/a.txt"); f.Write("chat_logs/a.txt", "新一", "新二"); AssertEqual(0, batch.Commit(true)); }
            AssertEqual(1, MemoryProgress.PendingChats(f.Root).Single().Start, "same-sized rewritten file cannot reuse old read coverage");
            using (var batch = MemoryProgress.BeginConsolidation(f.Root)) { f.Read("chat_logs/a.txt"); batch.Commit(true); }
            f.Write("chat_logs/a.txt", "分支记录"); AssertEqual(1, MemoryProgress.PendingChats(f.Root).Single().Start, "shortened branch invalidates cursor");
        }

        private static void TestMemoryFailedPass()
        {
            using var f = new MemoryFixture(); f.Write("chat_logs/a.txt", "消息");
            using (var batch = MemoryProgress.BeginConsolidation(f.Root)) { f.Read("chat_logs/a.txt"); AssertEqual(0, batch.Commit(false)); }
            foreach (var arguments in new[] { "{\"type\":\"承诺\",\"content\":\"约定\"}", "bad json" })
            {
                using var batch = MemoryProgress.BeginConsolidation(f.Root); f.Read("chat_logs/a.txt");
                var write = MemoryProgress.BeforeTool(f.Root, "record_resolve", arguments);
                MemoryProgress.AfterTool(write, "[错误] 写入失败"); AssertEqual(0, batch.Commit(true), "failed write blocks a completion claim");
            }
            AssertEqual(1, MemoryProgress.PendingChats(f.Root).Single().Start);
        }

        private static void TestMemoryOwnWrites()
        {
            using var f = new MemoryFixture(); f.Write("decisions/diary.txt", "旧见闻");
            var probe = MemoryProgress.BeforeTool(f.Root, "record_resolve", "{}");
            f.Append("decisions/diary.txt", "本人写的新事"); MemoryProgress.AfterTool(probe, "已记入日记。");
            AssertEqual(MemoryProgress.Reminder, f.Remind(), "writing a note does not read older facts");
            f.Read("decisions/diary.txt");
            probe = MemoryProgress.BeforeTool(f.Root, "record_resolve", "{}");
            f.Append("decisions/diary.txt", "又写一条"); MemoryProgress.AfterTool(probe, "已记入日记。");
            AssertEqual("", f.Remind(), "known diary plus own single append stays known");
            probe = MemoryProgress.BeforeTool(f.Root, "record_resolve", "{}");
            f.Append("decisions/diary.txt", "同时传入的见闻"); f.Append("decisions/diary.txt", "本人追加");
            MemoryProgress.AfterTool(probe, "已记入日记。"); AssertEqual(MemoryProgress.Reminder, f.Remind(), "own write does not swallow concurrent tidings");
            using var fresh = new MemoryFixture(); probe = MemoryProgress.BeforeTool(fresh.Root, "record_resolve", "{}");
            fresh.Append("decisions/diary.txt", "别人先写"); fresh.Append("decisions/diary.txt", "本人后写");
            MemoryProgress.AfterTool(probe, "已记入日记。"); AssertEqual(MemoryProgress.Reminder, fresh.Remind(), "new file with concurrent entries is not wholly self-authored");
        }

        private static void TestMemoryPresented()
        {
            using var f = new MemoryFixture(); f.Write("chat_logs/a.txt", "已经呈现的消息");
            var snapshot = MemoryProgress.CaptureThread(f.Root, "a.txt"); f.Append("chat_logs/a.txt", "后来到达");
            MemoryProgress.AcknowledgePresented(snapshot); AssertEqual(MemoryProgress.Reminder, f.Remind());
            MemoryProgress.AcknowledgePresented(MemoryProgress.CaptureThread(f.Root, "a.txt")); AssertEqual("", f.Remind());
            AssertEqual(1, MemoryProgress.PendingChats(f.Root).Single().Start, "visible conversation is not automatically consolidated");
        }

        private static void TestMemoryOwnReply()
        {
            using var f = new MemoryFixture();
            f.Append("chat_logs/a.txt", "本人第一封信"); MemoryProgress.AcknowledgeOwnAppend(f.Root, "chat_logs/a.txt", null);
            AssertEqual("", f.Remind());
            var before = MemoryProgress.CaptureThread(f.Root, "a.txt"); f.Append("chat_logs/a.txt", "本人后续回复");
            MemoryProgress.AcknowledgeOwnAppend(f.Root, "chat_logs/a.txt", before); AssertEqual("", f.Remind());
            before = MemoryProgress.CaptureThread(f.Root, "a.txt"); f.Append("chat_logs/a.txt", "同时收到另一封信"); f.Append("chat_logs/a.txt", "本人回复");
            MemoryProgress.AcknowledgeOwnAppend(f.Root, "chat_logs/a.txt", before); AssertEqual(MemoryProgress.Reminder, f.Remind());
        }

        private static void TestMemoryIsolation()
        {
            using var own = new MemoryFixture(); using var other = new MemoryFixture(); other.Write("chat_logs/secret.txt", "别人的秘密");
            foreach (var path in new[] { "decisions/.memory_progress_v1.json", "decisions/.MEMORY_PROGRESS_V1.JSON", "decisions\\.memory_progress_v1.json. " })
                AssertTrue(MemoryProgress.IsMetadataPath(path), "reserved metadata remains protected across Windows path spelling: " + path);
            AssertFalse(MemoryProgress.IsMetadataPath("decisions/diary.txt"), "ordinary diary remains readable");
            foreach (var path in new[] { "../other/chat_logs/secret.txt", other.Full("chat_logs/secret.txt"), "World/history/events.txt", "decisions/" + MemoryProgress.MetadataFileName })
                AssertTrue(MemoryProgress.BeforeTool(own.Root, "read_file", new JObject { ["path"] = path }.ToString()) == null, "only own memory files tracked: " + path);
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(own.Full("chat_logs"));
                File.CreateSymbolicLink(own.Full("chat_logs/link.txt"), other.Full("chat_logs/secret.txt"));
                AssertEqual(0, MemoryProgress.PendingChats(own.Root).Count, "linked foreign file is not scanned");
            }
            AssertFalse(File.Exists(other.Full("decisions/" + MemoryProgress.MetadataFileName)), "foreign source never receives progress writes");
        }

        private static void TestMemoryAsyncScopes()
        {
            using var a = new MemoryFixture(); using var b = new MemoryFixture();
            a.Write("chat_logs/a.txt", "甲"); b.Write("chat_logs/a.txt", "乙");
            Task.WhenAll(Task.Run(async () =>
            { using var batch = MemoryProgress.BeginConsolidation(a.Root); await Task.Yield(); a.Read("chat_logs/a.txt"); AssertEqual(1, batch.Commit(true)); }),
            Task.Run(async () =>
            { using var batch = MemoryProgress.BeginConsolidation(b.Root); await Task.Yield(); b.Read("chat_logs/a.txt"); AssertEqual(1, batch.Commit(true)); })).GetAwaiter().GetResult();
            AssertEqual(0, MemoryProgress.PendingChats(a.Root).Count); AssertEqual(0, MemoryProgress.PendingChats(b.Root).Count);
            var session = new MemoryRecallSession(); a.Write("decisions/diary.txt", "本局甲"); b.Write("decisions/diary.txt", "另局乙");
            AssertEqual(MemoryProgress.Reminder, a.Remind(session)); AssertEqual(MemoryProgress.Reminder, b.Remind(session), "meeting reused in another campaign cannot inherit old notification state");
        }

        private static void TestMemoryRecovery()
        {
            using var f = new MemoryFixture(); f.Write("chat_logs/a.txt", "没有日记也有新往来");
            f.Write("decisions/" + MemoryProgress.MetadataFileName, "not json");
            AssertEqual(1, MemoryProgress.PendingChats(f.Root).Single().Start);
            using (var batch = MemoryProgress.BeginConsolidation(f.Root)) { f.Read("chat_logs/a.txt"); AssertEqual(1, batch.Commit(true)); }
            AssertEqual(0, MemoryProgress.PendingChats(f.Root).Count, "corrupt progress is rebuilt from confirmed reads");
            AssertEqual("没有日记也有新往来", File.ReadAllText(f.Full("chat_logs/a.txt")).Trim(), "source remains unchanged");
            AssertFalse(File.Exists(f.Full("decisions/diary.txt")), "tracking never authors a memory");
        }
    }
}
