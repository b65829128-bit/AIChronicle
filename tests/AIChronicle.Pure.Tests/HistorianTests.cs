using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIChronicle;
using Newtonsoft.Json.Linq;

namespace AIChronicle.Tests
{
    internal static partial class Program
    {
        private static void RunHistorianTests()
        {
            Run("historian: reserve two writing rounds and reject further reads", TestHistorianWritingRounds);
            Run("historian: annual source cap covers large file without changing NPC caps", TestHistorianSourceCap);
            Run("historian: retry preserves original task, evidence, and tool pairing", TestHistorianResume);
            Run("historian: annual success requires corresponding nonempty changed text", TestHistorianAnnualSuccess);
            Run("historian: special success ignores timestamps, blank files, and unrelated files", TestHistorianSpecialSuccess);
            Run("historian: normalize annual names and suffixes", TestHistorianNames);
            Run("history: multiline declarations remain one lossless JSONL record", TestHistoryJsonLine);
        }

        private static void TestHistorianWritingRounds()
        {
            var rounds = Enumerable.Range(0, 12).Select(r => HistorianWorkflow.IsFinalizing("historian", r, 12)).ToArray();
            AssertEqual(2, rounds.Count(x => x));
            AssertFalse(rounds[9], "第十轮仍可读");
            AssertTrue(rounds[10] && rounds[11], "最后两轮须成文");
            AssertTrue(HistorianWorkflow.IsFinalizing("historian", 0, 2, retry: true), "续写从第一轮即只写");
            foreach (var intent in new[] { "conversation", "letter", "consolidation", "self_review" })
                AssertFalse(HistorianWorkflow.IsFinalizing(intent, 11, 12), "其他场景不改变工具快照");
            var schema = new JArray(new[] { "read_file", "list_dir", "query_character", "write_chronicle" }
                .Select(n => new JObject { ["type"] = "function", ["function"] = new JObject { ["name"] = n } }));
            var writing = HistorianWorkflow.WritingTools(schema);
            AssertEqual(1, writing.Count);
            AssertEqual("write_chronicle", (string?)writing[0]["function"]?["name"]);
            AssertEqual(4, schema.Count);
            foreach (var tool in new[] { "read_file", "grep", "list_dir", "query_character", "write_file", "browse_tools" })
                AssertFalse(HistorianWorkflow.CanExecute(true, tool), "不能靠调用仍可见的旧工具继续调查");
            AssertTrue(HistorianWorkflow.CanExecute(true, "write_chronicle"), "保留成文工具");
            AssertTrue(HistorianWorkflow.CanExecute(false, "read_file"), "阅读阶段可读");
        }

        private static void TestHistorianSourceCap()
        {
            const string args = "{\"path\":\"history/events_1084.txt\"}";
            AssertEqual(64000, HistorianWorkflow.ToolResultCap("historian", "read_file", args, 4000));
            AssertTrue(42364 < HistorianWorkflow.ToolResultCap("historian", "read_file", args, 4000), "1084 年约 42k 字符带行号史料应可一次返回");
            AssertEqual(80000, HistorianWorkflow.ToolResultCap("historian", "read_file", args, 80000));
            AssertEqual(0, HistorianWorkflow.ToolResultCap("historian", "read_file", args, 0));
            AssertEqual(4000, HistorianWorkflow.ToolResultCap("conversation", "read_file", args, 4000));
            AssertEqual(4000, HistorianWorkflow.ToolResultCap("historian", "query_character", args, 4000));
            AssertEqual(4000, HistorianWorkflow.ToolResultCap("historian", "read_file", "{\"path\":\"curia/a.txt\"}", 4000));
            AssertEqual(4000, HistorianWorkflow.ToolResultCap("historian", "read_file", "{broken", 4000));
            AssertEqual(64000, HistorianWorkflow.ToolResultCap("historian", "read_file", "{\"path\":\"World/history/events_1084.txt\"}", 4000));
        }

        private static void TestHistorianResume()
        {
            var evidence = new string('史', 42000);
            var messages = new List<Newtonsoft.Json.Linq.JToken>
            {
                new JObject { ["role"] = "system", ["content"] = "史官原规则" },
                new JObject { ["role"] = "user", ["content"] = "请编纂卡拉迪亚第1084年的编年史。" }
            };
            // 超过普通聊天的 20 条裁剪线；重试必须保留最早的年度史料。
            for (var i = 0; i < 12; i++)
            {
                messages.Add(new JObject
                {
                    ["role"] = "assistant", ["content"] = "", ["reasoning_content"] = "查阅史料",
                    ["tool_calls"] = new JArray(new JObject
                    {
                        ["id"] = "read" + i, ["type"] = "function",
                        ["function"] = new JObject { ["name"] = "read_file", ["arguments"] = "{\"path\":\"history/events_1084.txt\"}" }
                    })
                });
                messages.Add(new JObject { ["role"] = "tool", ["tool_call_id"] = "read" + i, ["content"] = i == 0 ? evidence : "续页" });
            }
            var resumed = HistorianWorkflow.ResumeMessages(messages, "立即完成1084年正篇");
            AssertEqual(messages.Count + 1, resumed.Count);
            AssertEqual(evidence, (string?)resumed[3]["content"]);
            AssertEqual("请编纂卡拉迪亚第1084年的编年史。", (string?)resumed[1]["content"]);
            for (var i = 2; i < messages.Count; i += 2)
            {
                AssertEqual((string?)resumed[i]["tool_calls"]?[0]?["id"], (string?)resumed[i + 1]["tool_call_id"]);
                AssertEqual("查阅史料", (string?)resumed[i]["reasoning_content"]);
            }
            resumed[0]["content"] = "新规则";
            AssertEqual("史官原规则", (string?)messages[0]["content"]);
            AssertEqual("user", (string?)resumed.Last()["role"]);
        }

        private static void TestHistorianAnnualSuccess()
        {
            var root = NewTempDir();
            try
            {
                var before = HistorianWorkflow.Capture(root, 1084);
                File.WriteAllText(Path.Combine(root, "加里俄斯本纪.txt"), "专题成文");
                File.WriteAllText(Path.Combine(root, "1085编年史.txt"), "下一年");
                AssertFalse(HistorianWorkflow.HasChanged(root, 1084, before), "其他成文不能冒充1084年成功");
                var annual = Path.Combine(root, "1084编年史.txt");
                File.WriteAllText(annual, " \n\t");
                AssertFalse(HistorianWorkflow.HasText(annual), "空白不是史文");
                AssertFalse(HistorianWorkflow.HasChanged(root, 1084, before), "空白不能算成功");
                File.WriteAllText(annual, "## 春\n事迹阙载。\n\n## 夏\n围城。");
                AssertTrue(HistorianWorkflow.HasChanged(root, 1084, before), "有效本年正文已生成");
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestHistorianSpecialSuccess()
        {
            var root = NewTempDir();
            try
            {
                var path = Path.Combine(root, "一国世家.txt");
                File.WriteAllText(path, "旧史");
                var before = HistorianWorkflow.Capture(root, -1);
                var time = File.GetLastWriteTimeUtc(path);
                File.SetLastWriteTimeUtc(path, time.AddSeconds(10));
                File.WriteAllText(Path.Combine(root, "空传.txt"), "\n");
                File.WriteAllText(Path.Combine(root, "temp.json"), "其他进程写入");
                AssertFalse(HistorianWorkflow.HasChanged(root, -1, before), "只看正文变化");
                File.WriteAllText(path, "新史");
                File.SetLastWriteTimeUtc(path, time);
                AssertTrue(HistorianWorkflow.HasChanged(root, -1, before), "时间戳相同也可检出重写");
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestHistorianNames()
        {
            var genres = new[] { "本纪", "世家", "列传", "编年史", "纪事" };
            foreach (var name in new[] { "1084", "1084编年史", "1084编年史.txt", "第1084年编年史.txt", " 第1084年 " })
                AssertEqual("1084", HistorianWorkflow.CleanName("编年史", name, genres));
            AssertEqual("拉盖娅", HistorianWorkflow.CleanName("本纪", "拉盖娅本纪.TXT", genres));
            AssertEqual("卡拉迪亚上古", HistorianWorkflow.CleanName("编年史", "卡拉迪亚上古编年史", genres));
            AssertEqual(1084, HistorianWorkflow.ExtractYear("请编纂卡拉迪亚第1084年的编年史。"));
            AssertEqual(-1, HistorianWorkflow.ExtractYear("一位君主之死"));
        }

        private static void TestHistoryJsonLine()
        {
            const string summary = "朝议执行：已宣战。\r\n宣战声明：\"守边\"\t军令\\文书\n第二段。";
            var line = HistoryEventText.Serialize(1084, "秋", 12, "curia_applied", summary);
            AssertFalse(line.Contains('\n') || line.Contains('\r'), "每个事件严格占一行");
            var parsed = JObject.Parse(line);
            AssertEqual(summary, (string?)parsed["summary"]);
            AssertEqual(1084, (int)parsed["year"]!);
            AssertEqual("curia_applied", (string?)parsed["type"]);
            var lines = (line + Environment.NewLine + HistoryEventText.Serialize(1084, "冬", 8, "war_declared", summary)).Split('\n');
            AssertEqual(2, lines.Length);
            foreach (var record in lines) AssertEqual(summary, (string?)JObject.Parse(record)["summary"]);
        }
    }
}
