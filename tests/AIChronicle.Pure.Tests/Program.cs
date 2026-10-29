using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AIChronicle;

namespace AIChronicle.Tests
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;
        private static readonly List<string> _failures = new();

        private static int Main()
        {
            Console.WriteLine("=== AI Chronicle pure logic tests ===");

            Run("parse chronicle file names", TestParseChronicleNames);
            Run("parse kingdom document file names", TestParseDocumentNames);
            Run("catalog: group order, single 政务文献 header, sorting", TestCatalogOrder);
            Run("catalog: permissions (mercenary / vassal / ruler / no kingdom)", TestPermissions);
            Run("catalog: empty campaign dir", TestEmptyCatalog);
            Run("read: markdown, empty file, missing file, header", TestReadArticle);
            Run("markdown -> plain text fallback", TestMarkdownPlainText);

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
