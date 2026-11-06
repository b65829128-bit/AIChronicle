using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using AIChronicle;

namespace AIChronicle.Tests
{
    internal static partial class Program
    {
        private static int _passed;
        private static int _failed;
        private static readonly List<string> _failures = new();

        private static int Main(string[] args)
        {
            if (args.Length > 0) return args[0] == "--duel-mission-smoke" ? SmokeDuelMission(args) : ReplayDuel(args);
            Console.WriteLine("=== AI Chronicle pure logic tests ===");

            Run("parse chronicle file names", TestParseChronicleNames);
            Run("parse kingdom document file names", TestParseDocumentNames);
            Run("catalog: group order, single 政务文献 header, sorting", TestCatalogOrder);
            Run("catalog: permissions (mercenary / vassal / ruler / no kingdom)", TestPermissions);
            Run("catalog: empty campaign dir", TestEmptyCatalog);
            Run("read: markdown, empty file, missing file, header", TestReadArticle);
            Run("markdown -> plain text fallback", TestMarkdownPlainText);
            Run("main-thread epoch: drop stale + cancel waiters", TestMainThreadEpoch);
            Run("curia: weighted tally + quorum + threshold", TestCuriaVoteMath);
            Run("treaty: terms/cession/tribute perspective", TestTreatyText);
            Run("diary: parse dates + select recent", TestDiaryParseAndSelect);
            Run("diary: rotation plan", TestDiaryRotationPlan);
            Run("diary: rotation files + dedupe", TestDiaryRotationFiles);
            Run("silent diary: hero naming", TestSilentDiaryText);
            RunLlmTests();
            RunMemoryTests();
            RunHistorianTests();
            RunEncounterTests();
            RunMeetingTests();
            RunDuelTests();
            RunClanDepartureTests();
            RunStoryHistoryTests();
            RunAllegianceHistoryTests();
            RunWorldHistoryTests();

            Console.WriteLine();
            Console.WriteLine($"passed: {_passed}, failed: {_failed}");
            if (_failed > 0)
            {
                Console.WriteLine("FAILED:");
                foreach (var failure in _failures)
                    Console.WriteLine("  - " + failure);
                return 1;
            }

            return 0;
        }

        private static int ReplayDuel(string[] args)
        {
            if (args.Length != 2 || args[0] != "--duel-replay")
            { Console.Error.WriteLine("Usage: --duel-replay <World/duels/ID.json>"); return 2; }
            try
            {
                var audit = Newtonsoft.Json.JsonConvert.DeserializeObject<DuelAudit>(SafeFileIO.ReadAllText(args[1]));
                var saved = audit?.AppliedReward ?? throw new ArgumentException("此记录尚未确定胜者或没有成长奖励结算。");
                if (saved.AlgorithmVersion != DuelRewardMath.Version) throw new ArgumentException("未知算法版本：" + saved.AlgorithmVersion);
                var replay = DuelRewardMath.Calculate(saved.Winner, saved.Loser, saved.PriorWins, audit!.RewardEligible);
                var matches = replay.Renown == saved.Renown && replay.Influence == saved.Influence;
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    id = audit.Id, algorithmVersion = saved.AlgorithmVersion, matches,
                    recorded = new { renown = saved.Renown, influence = saved.Influence },
                    replayed = new { renown = replay.Renown, influence = replay.Influence },
                    actuallyPaid = new { renown = audit.ActualRenown, influence = audit.ActualInfluence }
                }, Newtonsoft.Json.Formatting.Indented));
                return matches ? 0 : 1;
            }
            catch (Exception ex) { Console.Error.WriteLine("决斗复算失败：" + ex.Message); return 2; }
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("  ok   " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                _failures.Add(name + ": " + ex.Message);
                Console.WriteLine("  FAIL " + name + ": " + ex.Message);
            }
        }

        private static void TestParseChronicleNames()
        {
            AssertEqual(("编年史", "第1084年编年史", 1084), HistoryArchive.ParseChronicleName("1084编年史"));
            AssertEqual(("本纪", "拉盖娅本纪", 0), HistoryArchive.ParseChronicleName("拉盖娅本纪"));
            AssertEqual(("世家", "南帝国世家", 0), HistoryArchive.ParseChronicleName("南帝国世家"));
            AssertEqual(("列传", "芬·格鲁芬多克列传", 0), HistoryArchive.ParseChronicleName("芬·格鲁芬多克列传"));
            AssertEqual(("纪事", "帝国分裂纪事", 0), HistoryArchive.ParseChronicleName("帝国分裂纪事"));
            AssertEqual(("其他", "散佚残卷", 0), HistoryArchive.ParseChronicleName("散佚残卷"));
            AssertEqual(("其他", "编年史", 0), HistoryArchive.ParseChronicleName("编年史"));
            AssertEqual(("编年史", "卡拉迪亚上古编年史", 0), HistoryArchive.ParseChronicleName("卡拉迪亚上古编年史"));
            AssertEqual(("纪事", "北帝国源流纪事", 0), HistoryArchive.ParseChronicleName("北帝国源流纪事"));
        }

        private static void TestParseDocumentNames()
        {
            AssertEqual("封臣谏言 · 南帝国 · 第1084年", HistoryArchive.ParseAdvisoryName("南帝国_1084"));
            AssertEqual("封臣谏言 · 南帝国", HistoryArchive.ParseAdvisoryName("南帝国"));
            AssertEqual("国王诏令 · 北帝国 · 第1085年", HistoryArchive.ParseEdictName("北帝国_1085"));
            AssertEqual("国王密陈 · 南帝国 · 第1084年", HistoryArchive.ParseSecretAdvisoryName("南帝国_1084"));
        }

        private static void TestCatalogOrder()
        {
            var root = NewTempDir();
            try
            {
                var campaign = Path.Combine(root, "campaign");
                Write(campaign, "NPCs/World/history/chronicles/1084编年史.txt", "# 1084\n\n## 春\n旧事");
                Write(campaign, "NPCs/World/history/chronicles/1085编年史.txt", "## 春\n新事");
                Write(campaign, "NPCs/World/history/chronicles/拉盖娅本纪.txt", "本纪正文");
                Write(campaign, "NPCs/World/history/chronicles/南帝国世家.txt", "世家正文");
                Write(campaign, "NPCs/World/history/chronicles/芬·格鲁芬多克列传.txt", "列传正文");
                Write(campaign, "NPCs/World/history/chronicles/帝国分裂纪事.txt", "纪事正文");
                Write(campaign, "NPCs/World/history/chronicles/散佚残卷.txt", "残卷正文");
                Write(campaign, "NPCs/World/advisory/南帝国_1084.txt", "谏言");
                Write(campaign, "NPCs/World/advisory/南帝国_1085.txt", "新谏言");
                Write(campaign, "NPCs/World/advisory/北帝国_1085.txt", "他国谏言");
                Write(campaign, "NPCs/World/edict/南帝国_1084.txt", "诏令");
                Write(campaign, "NPCs/World/secret_advisory/南帝国_1084.txt", "密陈");

                var catalog = Load(campaign, "南帝国", ruler: true);

                var headers = catalog.Entries.Where(e => e.IsHeader).Select(e => e.Title).ToList();
                AssertSequence(new[] { "本纪", "世家", "列传", "编年史", "纪事", "其他", "政务文献" }, headers);
                AssertEqual(1, catalog.Entries.Count(e => e.IsHeader && e.Title == HistoryArchive.KingdomDocumentsGroup));

                var chronicleTitles = catalog.Entries
                    .Where(e => e.Kind == HistoryEntryKind.Chronicle && e.Year > 0)
                    .Select(e => e.Title)
                    .ToList();
                AssertSequence(new[] { "第1085年编年史", "第1084年编年史" }, chronicleTitles);

                var advisoryTitles = catalog.Entries
                    .Where(e => e.Kind == HistoryEntryKind.Advisory)
                    .Select(e => e.Title)
                    .ToList();
                AssertSequence(new[] { "封臣谏言 · 南帝国 · 第1085年", "封臣谏言 · 南帝国 · 第1084年" }, advisoryTitles);

                var firstChronicle = catalog.Entries.First(e => e.Title == "第1085年编年史");
                AssertEqual(HistoryTextFormat.Markdown, firstChronicle.Format);
                var advisory = catalog.Entries.First(e => e.Kind == HistoryEntryKind.Advisory);
                AssertEqual(HistoryTextFormat.PlainText, advisory.Format);

                for (int i = 0; i < catalog.Entries.Count; i++)
                    AssertEqual(i, catalog.Entries[i].Index, "entry index");

                AssertEqual(1, catalog.FirstSelectableIndex);
                AssertEqual("拉盖娅本纪", catalog.Entries[catalog.FirstSelectableIndex].Title);

                var titles = catalog.Entries.Select(e => e.Title).ToList();
                AssertTrue(titles.Contains("国王诏令 · 南帝国 · 第1084年"), "edict entry");
                AssertTrue(titles.Contains("国王密陈 · 南帝国 · 第1084年"), "secret advisory entry");
            }
            finally
            {
                Cleanup(root);
            }
        }

        private static void TestPermissions()
        {
            var root = NewTempDir();
            try
            {
                var campaign = Path.Combine(root, "campaign");
                Write(campaign, "NPCs/World/history/chronicles/1084编年史.txt", "正文");
                Write(campaign, "NPCs/World/advisory/南帝国_1084.txt", "谏言");
                Write(campaign, "NPCs/World/edict/南帝国_1084.txt", "诏令");
                Write(campaign, "NPCs/World/secret_advisory/南帝国_1084.txt", "密陈");

                var mercenary = Load(campaign, "南帝国", mercenary: true);
                AssertFalse(mercenary.Entries.Any(e => e.Kind == HistoryEntryKind.Advisory), "mercenary sees advisory");
                AssertFalse(mercenary.Entries.Any(e => e.Kind == HistoryEntryKind.Edict), "mercenary sees edict");
                AssertFalse(mercenary.Entries.Any(e => e.Kind == HistoryEntryKind.SecretAdvisory), "mercenary sees secret");
                AssertFalse(mercenary.Entries.Any(e => e.IsHeader && e.Title == HistoryArchive.KingdomDocumentsGroup), "mercenary sees 政务文献 group");
                AssertTrue(mercenary.Entries.Any(e => e.Kind == HistoryEntryKind.Chronicle), "mercenary still sees chronicles");

                var vassal = Load(campaign, "南帝国");
                AssertTrue(vassal.Entries.Any(e => e.Kind == HistoryEntryKind.Advisory), "vassal sees advisory");
                AssertTrue(vassal.Entries.Any(e => e.Kind == HistoryEntryKind.Edict), "vassal sees edict");
                AssertFalse(vassal.Entries.Any(e => e.Kind == HistoryEntryKind.SecretAdvisory), "vassal sees secret");

                var ruler = Load(campaign, "南帝国", ruler: true);
                AssertTrue(ruler.Entries.Any(e => e.Kind == HistoryEntryKind.SecretAdvisory), "ruler sees secret");

                var noKingdom = Load(campaign, null);
                AssertFalse(noKingdom.Entries.Any(e => e.IsHeader && e.Title == HistoryArchive.KingdomDocumentsGroup), "no kingdom sees 政务文献 group");
                AssertTrue(noKingdom.Entries.Any(e => e.Kind == HistoryEntryKind.Chronicle), "no kingdom still sees chronicles");
            }
            finally
            {
                Cleanup(root);
            }
        }

        private static void TestEmptyCatalog()
        {
            var root = NewTempDir();
            try
            {
                var campaign = Path.Combine(root, "campaign");
                var catalog = HistoryArchive.LoadCatalog(HistoryViewer.Empty, campaign);
                AssertEqual(0, catalog.Entries.Count);
                AssertEqual(-1, catalog.FirstSelectableIndex);
                AssertTrue(catalog.IsEmpty, "empty catalog");
                AssertTrue(Directory.Exists(Path.Combine(campaign, "NPCs", "World", "history", "chronicles")), "chronicles dir created");
            }
            finally
            {
                Cleanup(root);
            }
        }

        private static void TestReadArticle()
        {
            var root = NewTempDir();
            try
            {
                var campaign = Path.Combine(root, "campaign");
                Write(campaign, "NPCs/World/history/chronicles/1084编年史.txt", "# 1084\r\n\r\n## 春\r\n正文");
                Write(campaign, "NPCs/World/history/chronicles/空篇编年史.txt", "");
                Write(campaign, "NPCs/World/history/chronicles/待删列传.txt", "将被删除");

                var catalog = Load(campaign);
                var chronicle = catalog.Entries.First(e => e.Title == "第1084年编年史");
                var article = HistoryArchive.ReadArticle(chronicle);
                AssertFalse(article.HasError, "markdown article readable");
                AssertEqual(HistoryTextFormat.Markdown, article.Format);
                AssertEqual("第1084年编年史", article.Title);
                AssertTrue(article.Body.Contains("\n## 春\n"), "line endings normalized to LF");
                AssertFalse(article.Body.Contains("\r"), "no CR left");

                var emptyEntry = catalog.Entries.First(e => e.Title == "空篇编年史");
                var emptyArticle = HistoryArchive.ReadArticle(emptyEntry);
                AssertFalse(emptyArticle.HasError, "empty article readable");
                AssertEqual("", emptyArticle.Body);

                var header = catalog.Entries.First(e => e.IsHeader);
                AssertTrue(HistoryArchive.ReadArticle(header).HasError, "header not readable");

                var missingEntry = catalog.Entries.First(e => e.Title == "待删列传");
                File.Delete(missingEntry.FilePath);
                var missingArticle = HistoryArchive.ReadArticle(missingEntry);
                AssertTrue(missingArticle.HasError, "missing file reported");
            }
            finally
            {
                Cleanup(root);
            }
        }

        private static void TestMarkdownPlainText()
        {
            var markdown = @"# 卡拉迪亚第1084年编年史

## 春

**加粗**与`代码`

> 史官曰：好

- 一
- 二

---

普通段落。";
            var text = HistoryMarkdown.ToPlainText(markdown);
            AssertTrue(text.Contains("卡拉迪亚第1084年编年史"), "title kept");
            AssertTrue(text.Contains("春"), "heading text kept");
            AssertTrue(text.Contains("加粗与代码"), "inline markers removed");
            AssertTrue(text.Contains("史官曰：好"), "blockquote text kept");
            AssertTrue(text.Contains("一"), "list item kept");
            AssertFalse(text.Contains("##"), "heading markers removed");
            AssertFalse(text.Contains("**"), "bold markers removed");
            AssertFalse(text.Contains(">"), "blockquote marker removed");
            AssertFalse(text.Contains("`"), "code marker removed");
            AssertTrue(HistoryMarkdown.ToPlainText("见 CharacterObject_1664").Contains("CharacterObject_1664"),
                "single underscore identifier preserved");
            AssertEqual("", HistoryMarkdown.ToPlainText(null));
            AssertEqual("", HistoryMarkdown.ToPlainText(""));
        }

        private static void TestMainThreadEpoch()
        {
            MainThreadExecutor.Initialize();
            MainThreadExecutor.BeginCampaign();
            int executed = 0;

            MainThreadExecutor.Post(() => executed++);
            MainThreadExecutor.Tick();
            AssertEqual(1, executed, "current-epoch action runs");

            MainThreadExecutor.Post(() => executed += 100);
            AssertTrue(MainThreadExecutor.PendingCount >= 1, "action queued");
            MainThreadExecutor.EndCampaign();
            MainThreadExecutor.Tick();
            AssertEqual(1, executed, "queued action from ended campaign dropped");

            MainThreadExecutor.BeginCampaign();
            string? cancelMessage = null;
            var worker = new Thread(() =>
            {
                try
                {
                    MainThreadExecutor.RunOnMainThread(() =>
                    {
                        executed += 1000;
                        return true;
                    });
                }
                catch (Exception ex)
                {
                    cancelMessage = ex.Message;
                }
            })
            {
                IsBackground = true
            };
            worker.Start();
            AssertTrue(SpinWait.SpinUntil(() => MainThreadExecutor.PendingCount > 0, 2000),
                "background waiter queued");
            MainThreadExecutor.EndCampaign();
            AssertTrue(worker.Join(2000), "background waiter canceled promptly");
            AssertTrue(cancelMessage != null && cancelMessage.Contains("已取消"),
                "background waiter received cancel error");
            AssertEqual(1, executed, "canceled waiter did not execute");

            MainThreadExecutor.BeginCampaign();
            MainThreadExecutor.Post(() => executed++);
            MainThreadExecutor.Tick();
            AssertEqual(2, executed, "new-campaign action runs");
        }

        private static HistoryCatalog Load(string campaignDir, string? kingdom = null,
            bool mercenary = false, bool ruler = false)
        {
            return HistoryArchive.LoadCatalog(new HistoryViewer(kingdom, mercenary, ruler), campaignDir);
        }

        private static void Write(string root, string relativePath, string content)
        {
            var full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(full, content, new UTF8Encoding(false));
        }

        private static string NewTempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "aichronicle-pure-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Cleanup(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch
            {
                // 临时目录清理失败不影响测试结论
            }
        }

        private static void TestCuriaVoteMath()
        {
            // 支持 10、反对 20、弃权 5、未表态 99：应召权重 134，明确票 30，支持率 1/3。
            var tally = CuriaVoteMath.Tally(new (float Weight, int Stance)[]
            {
                (10f, 1), (20f, -1), (5f, 0), (99f, -2)
            });
            AssertNear(10f, tally.Support, "support");
            AssertNear(20f, tally.Oppose, "oppose");
            AssertNear(5f, tally.Abstain, "abstain");
            AssertNear(134f, tally.Summoned, "summoned");
            AssertNear(1f / 3f, CuriaVoteMath.SupportRatio(10f, 20f), "ratio");
            AssertTrue(CuriaVoteMath.MeetsQuorum(134f, 67f), "quorum met");
            AssertFalse(CuriaVoteMath.MeetsQuorum(66f, 67f), "quorum not met");
            AssertFalse(CuriaVoteMath.Passes(10f, 20f, 0.5f), "minority fails");
            AssertTrue(CuriaVoteMath.Passes(20f, 10f, 0.5f), "majority passes");
            AssertFalse(CuriaVoteMath.Passes(0f, 0f, 0.5f), "no votes fails");
            AssertTrue(CuriaVoteMath.Passes(20f, 10f, 2f / 3f), "2/3 threshold passes");
            AssertFalse(CuriaVoteMath.Passes(19f, 11f, 2f / 3f), "below 2/3 fails");
        }

        private static void TestTreatyText()
        {
            AssertEqual("proposer", TreatyText.NormalizeCedeFrom("us"), "us -> proposer");
            AssertEqual("target", TreatyText.NormalizeCedeFrom("them"), "them -> target");
            AssertEqual("proposer", TreatyText.NormalizeCedeFrom("我方"), "我方 -> proposer");
            AssertEqual("", TreatyText.NormalizeCedeFrom("nonsense"), "unknown -> none");

            var parsed = TreatyText.ParseCessionSpec("target:阿耳戈斯;proposer:泽昂尼卡；我方:北城");
            AssertEqual(3, parsed.Count, "parsed cession count");
            AssertEqual("target", parsed[0].CedeFrom, "parsed target side");
            AssertEqual("阿耳戈斯", parsed[0].SettlementName, "parsed target fief");
            AssertEqual("proposer", parsed[1].CedeFrom, "parsed proposer side");
            AssertEqual("proposer", parsed[2].CedeFrom, "parsed Chinese side");

            var terms = new TreatyTerms
            {
                TributeAmount = -500,
                TributeDays = 30,
                TruceDays = 60,
                WarOnReject = true
            };
            terms.Cessions.Add(new TreatyCession { SettlementId = "town_1", SettlementName = "阿耳戈斯", CedeFrom = "target" });
            terms.Cessions.Add(new TreatyCession { SettlementId = "town_2", SettlementName = "泽昂尼卡", CedeFrom = "proposer" });
            AssertTrue(terms.HasCession(), "has cession");
            AssertTrue(terms.HasTribute(), "has tribute");
            AssertTrue(terms.HasConcession(), "has concession");
            AssertTrue(terms.HasAnyTerm(), "has any term");
            AssertEqual(2, terms.GetCessions().Count, "two cessions");

            var proposerView = TreatyText.Describe(terms, "库赛特", "瓦兰迪亚");
            AssertTrue(proposerView.Contains("我方将「泽昂尼卡」割让给瓦兰迪亚"), "proposer own cession");
            AssertTrue(proposerView.Contains("对方将「阿耳戈斯」割让给我方"), "proposer enemy cession");
            AssertTrue(proposerView.Contains("对方每日向我方支付 500 金"), "proposer tribute view");
            AssertTrue(proposerView.Contains("休战 60 天"), "proposer truce view");
            AssertTrue(proposerView.Contains("若对方拒绝，我方将宣战"), "proposer war-on-reject view");

            var targetView = TreatyText.DescribeForTarget(terms, "库赛特", "瓦兰迪亚");
            AssertTrue(targetView.Contains("对方将「泽昂尼卡」割让给你方"), "target receives cession");
            AssertTrue(targetView.Contains("你方将「阿耳戈斯」割让给库赛特"), "target own cession");
            AssertTrue(targetView.Contains("你方每日向库赛特支付 500 金"), "target tribute view");
            AssertTrue(targetView.Contains("若你拒绝，对方将宣战"), "target war-on-reject view");

            var legacy = new TreatyTerms
            {
                SettlementId = "town_3",
                SettlementName = "旧城",
                CedeFrom = "target"
            };
            AssertEqual(1, legacy.GetCessions().Count, "legacy cession fallback");
            legacy.MigrateLegacy();
            AssertEqual(1, legacy.Cessions.Count, "legacy migrated");
            AssertEqual("", legacy.SettlementName, "legacy field cleared");

            var truceOnly = new TreatyTerms { TruceDays = 60 };
            AssertFalse(truceOnly.HasConcession(), "truce-only has no concession");
            AssertTrue(truceOnly.HasAnyTerm(), "truce-only has a term");
        }

        private static void TestDiaryParseAndSelect()
        {
            var lines = new[]
            {
                "[1089春3] 决定：与南帝国结盟",
                "[1089年冬第9日] 情报：库赛特·乌尔克汗家族的巴图劫掠了你的村子",
                "[1090夏1] 结果：结盟已成",
                "",
                "无日期的手写行"
            };

            AssertTrue(DiaryStore.TryParseYear("[1089春3] 决定：x", out var y1) && y1 == 1089, "compact year");
            AssertTrue(DiaryStore.TryParseYear("[1089年冬第9日] 情报：x", out var y2) && y2 == 1089, "long year");
            AssertFalse(DiaryStore.TryParseYear("无日期", out _), "no date");
            AssertTrue(DiaryStore.TryParseDateKey("[1089春3] x", out var key1)
                       && DiaryStore.TryParseDateKey("[1089春3] y", out var key2)
                       && key1 == key2, "date key stable");

            AssertEqual("决定", DiaryStore.ParseType(lines[0]), "type");
            AssertEqual("情报", DiaryStore.ParseType(lines[1]), "long type");
            AssertEqual<string?>(null, DiaryStore.ParseType("无日期的手写行"), "no type");

            var recent = DiaryStore.SelectRecent(lines, 2, null);
            AssertEqual(2, recent.Count, "recent count");
            AssertEqual(3, recent[0].LineNumber, "recent keeps line numbers");
            AssertEqual(5, recent[1].LineNumber, "free text kept");

            var intel = DiaryStore.SelectRecent(lines, 10, "情报");
            AssertEqual(1, intel.Count, "type filter");
            AssertEqual(2, intel[0].LineNumber, "type filter line");
            AssertEqual(1, DiaryStore.CountByType(lines, "情报"), "count by type");
            AssertEqual(4, DiaryStore.CountEntries(lines), "count entries");
        }

        private static void TestDiaryRotationPlan()
        {
            var lines = new[]
            {
                "[1088春1] 决定：a",
                "[1089夏2] 情报：b",
                "[1090秋3] 决定：c",
                "无日期的手写行",
                ""
            };

            var plan = DiaryStore.PlanRotation(lines, 1090);
            AssertEqual(2, plan.MovedCount, "moved");
            AssertEqual(1, plan.MoveByYear[1088].Count, "1088 bucket");
            AssertEqual(1, plan.MoveByYear[1089].Count, "1089 bucket");
            AssertEqual(2, plan.KeepLines.Count, "current year + undated kept");
            AssertTrue(plan.KeepLines.Contains("[1090秋3] 决定：c"), "current year kept");
            AssertTrue(plan.KeepLines.Contains("无日期的手写行"), "undated kept");
        }

        private static void TestDiaryRotationFiles()
        {
            var root = NewTempDir();
            var nl = Environment.NewLine;
            try
            {
                Write(root, "decisions/diary.txt", "[1089春1] 决定：a" + nl + "[1090夏2] 决定：b" + nl);
                Write(root, "decisions/diary_1089.txt", "[1089春1] 决定：a" + nl);

                var moved = DiaryStore.Rotate(root, 1090);
                AssertEqual(1, moved, "one line moved");

                var current = File.ReadAllText(Path.Combine(root, "decisions", "diary.txt"));
                AssertFalse(current.Contains("1089春1"), "old line removed from current diary");
                AssertTrue(current.Contains("1090夏2"), "current year kept");

                var archive = File.ReadAllText(Path.Combine(root, "decisions", "diary_1089.txt"));
                var lines = archive.Split(new[] { nl }, StringSplitOptions.None);
                AssertEqual(1, lines.Count(l => l.Contains("1089春1")), "archive dedupe");
            }
            finally
            {
                Cleanup(root);
            }
        }

        private static void TestSilentDiaryText()
        {
            AssertEqual("库赛特·乌尔克汗家族的巴图", SilentDiaryText.Hero("库赛特", "乌尔克汗", "巴图"), "full hero");
            AssertEqual("德赫家族的炎萌", SilentDiaryText.Hero("", "德赫", "炎萌"), "clan only");
            AssertEqual("瓦兰迪亚的德瑟特", SilentDiaryText.Hero("瓦兰迪亚", "", "德瑟特"), "kingdom only");
            AssertEqual("某人", SilentDiaryText.Hero("", "", ""), "fallback");
            AssertEqual("德赫家族的炎萌（女皇）", SilentDiaryText.Hero("", "德赫家族", "炎萌", "女皇"), "role, no double suffix");
            AssertEqual("乌尔克汗堡（南帝国）", SilentDiaryText.Settlement("乌尔克汗堡", "南帝国"), "settlement");
            AssertEqual("乌尔克汗堡", SilentDiaryText.Settlement("乌尔克汗堡", ""), "settlement without kingdom");

            var line = "库赛特·乌尔克汗家族的巴图劫掠了你的村子。";
            AssertFalse(line.Contains("他") || line.Contains("她") || line.Contains("其"), "no pronouns");
            AssertEqual("今日见闻", SilentDiaryText.Clean("今日见闻。"), "clean trims punctuation");
        }

        private static void AssertNear(float expected, float actual, string context)
        {
            if (Math.Abs(expected - actual) > 0.001f)
                throw new Exception($"{context}: expected {expected}, got {actual}");
        }

        private static void AssertEqual<T>(T expected, T actual, string? context = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"expected {Format(expected)}, got {Format(actual)}" +
                                    (context != null ? $" ({context})" : ""));
        }

        private static void AssertSequence<T>(IReadOnlyList<T> expected, IEnumerable<T> actual, string? context = null)
        {
            var list = actual.ToList();
            if (list.Count != expected.Count)
                throw new Exception($"expected {expected.Count} items [{string.Join(", ", expected.Select(Format))}], " +
                                    $"got {list.Count} [{string.Join(", ", list.Select(Format))}]" +
                                    (context != null ? $" ({context})" : ""));
            for (int i = 0; i < expected.Count; i++)
                AssertEqual(expected[i], list[i], context != null ? context + "[" + i + "]" : "[" + i + "]");
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        private static void AssertFalse(bool condition, string message)
        {
            if (condition)
                throw new Exception(message);
        }

        private static string Format<T>(T? value)
        {
            if (value == null)
                return "<null>";
            return value is string s ? "\"" + s + "\"" : value.ToString() ?? "<null>";
        }
    }
}
