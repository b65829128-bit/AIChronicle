using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AIChronicle
{
    /// <summary>史册目录条目的来源类型。政务文献保持纯文本，史官编年史按 Markdown 阅读。</summary>
    public enum HistoryEntryKind
    {
        Header,
        Chronicle,
        Advisory,
        Edict,
        SecretAdvisory
    }

    /// <summary>史文正文格式。史官成文为 Markdown，谏言/诏令/密陈仍是实体角色扮演产出的纯文本。</summary>
    public enum HistoryTextFormat
    {
        PlainText,
        Markdown
    }

    /// <summary>
    /// 史册阅读者视角。由 <see cref="HistoryViewerFactory"/> 从当前战役构造；
    /// 测试可手工构造，不依赖 Campaign.Current，因此档案逻辑无需进游戏即可验证。
    /// </summary>
    public readonly struct HistoryViewer
    {
        public HistoryViewer(string? kingdomName, bool isMercenary, bool isKingdomRuler)
        {
            KingdomName = string.IsNullOrWhiteSpace(kingdomName) ? null : kingdomName!.Trim();
            IsMercenary = isMercenary;
            IsKingdomRuler = isKingdomRuler;
        }

        public string? KingdomName { get; }
        public bool IsMercenary { get; }
        public bool IsKingdomRuler { get; }
        public bool HasKingdom => !string.IsNullOrEmpty(KingdomName);

        /// <summary>非雇佣兵且有王国者，才可读本国谏言/诏令；密陈另需 IsKingdomRuler。</summary>
        public bool CanReadKingdomDocuments => HasKingdom && !IsMercenary;

        public static HistoryViewer Empty => new HistoryViewer(null, false, false);
    }

    /// <summary>目录中的一个条目；Index 等于其在 HistoryCatalog.Entries 中的位置（含分组标题）。</summary>
    public sealed class HistoryCatalogEntry
    {
        public int Index { get; init; } = -1;
        public string Title { get; init; } = "";
        public string FilePath { get; init; } = "";
        public bool IsHeader { get; init; }
        public HistoryEntryKind Kind { get; init; }
        public HistoryTextFormat Format { get; init; } = HistoryTextFormat.PlainText;
        public int Year { get; init; }
        public bool IsSelectable => !IsHeader;
    }

    public sealed class HistoryCatalog
    {
        public HistoryCatalog(IReadOnlyList<HistoryCatalogEntry> entries)
        {
            Entries = entries;
            FirstSelectableIndex = -1;
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].IsSelectable)
                {
                    FirstSelectableIndex = i;
                    break;
                }
            }
        }

        public IReadOnlyList<HistoryCatalogEntry> Entries { get; }
        public int FirstSelectableIndex { get; }
        public bool IsEmpty => FirstSelectableIndex < 0;
    }

    public sealed class HistoryArticle
    {
        public int Index { get; init; } = -1;
        public string Title { get; init; } = "";
        public string Body { get; init; } = "";
        public string FilePath { get; init; } = "";
        public HistoryTextFormat Format { get; init; } = HistoryTextFormat.PlainText;
        public string? Error { get; init; }
        public bool HasError => !string.IsNullOrEmpty(Error);
    }

    /// <summary>
    /// 史册的唯一数据入口：目录扫描、体例分组、权限过滤、正文读取。
    /// 原生史书（HistoryScreenVM）与 CEF 史书（HistoryCefScreen）都必须经过这里，
    /// 避免双入口各自实现一套目录/权限逻辑而分叉。
    /// </summary>
    public static class HistoryArchive
    {
        public const string KingdomDocumentsGroup = "政务文献";
        public const string OtherGroup = "其他";

        /// <summary>目录分组顺序（仿正史：先本纪、次世家、后列传、终编年史/纪事）。</summary>
        public static readonly IReadOnlyList<string> GroupOrder =
            new[] { "本纪", "世家", "列传", "编年史", "纪事", OtherGroup, KingdomDocumentsGroup };

        private static readonly string[] ChronicleGenres = { "编年史", "本纪", "世家", "列传", "纪事" };
        private static readonly string[] ChronicleGenreOrder = { "本纪", "世家", "列传", "编年史", "纪事" };

        public static HistoryCatalog LoadCatalog(HistoryViewer viewer, string? campaignDir)
        {
            var entries = new List<HistoryCatalogEntry>();
            if (string.IsNullOrWhiteSpace(campaignDir))
                return new HistoryCatalog(entries);

            var chronicleDir = FindChronicleDir(campaignDir!);
            if (chronicleDir != null)
                AddChronicles(entries, chronicleDir);

            if (viewer.CanReadKingdomDocuments)
                AddKingdomDocuments(entries, campaignDir!, viewer);

            return new HistoryCatalog(entries);
        }

        public static HistoryArticle ReadArticle(HistoryCatalogEntry? entry)
        {
            if (entry == null || entry.IsHeader)
            {
                return new HistoryArticle
                {
                    Index = entry?.Index ?? -1,
                    Title = entry?.Title ?? "",
                    Format = entry?.Format ?? HistoryTextFormat.PlainText,
                    FilePath = entry?.FilePath ?? "",
                    Error = "此条目不可阅读"
                };
            }

            if (string.IsNullOrEmpty(entry.FilePath) || !File.Exists(entry.FilePath))
            {
                return new HistoryArticle
                {
                    Index = entry.Index,
                    Title = entry.Title,
                    Format = entry.Format,
                    FilePath = entry.FilePath,
                    Error = "史文文件不存在：" + Path.GetFileName(entry.FilePath)
                };
            }

            try
            {
                var body = SafeFileIO.ReadAllText(entry.FilePath);
                // 统一为 LF：CEF 与原生 RichText 都按 \n 断行，也便于测试。
                body = body.Replace("\r\n", "\n").Replace('\r', '\n');
                return new HistoryArticle
                {
                    Index = entry.Index,
                    Title = entry.Title,
                    Body = body,
                    FilePath = entry.FilePath,
                    Format = entry.Format
                };
            }
            catch (Exception ex)
            {
                return new HistoryArticle
                {
                    Index = entry.Index,
                    Title = entry.Title,
                    Format = entry.Format,
                    FilePath = entry.FilePath,
                    Error = ex.Message
                };
            }
        }

        /// <summary>
        /// 解析史官文件名：{名称}{体例}.txt。编年史名称必须是年份，显示为「第N年编年史」；
        /// 未能识别体例的自由文件归入「其他」，保证历史目录不丢文件。
        /// </summary>
        public static (string group, string display, int year) ParseChronicleName(string filename)
        {
            foreach (var genre in ChronicleGenres)
            {
                if (filename.EndsWith(genre, StringComparison.Ordinal) && filename.Length > genre.Length)
                {
                    var name = filename.Substring(0, filename.Length - genre.Length);
                    if (genre == "编年史" && int.TryParse(name, out int year))
                        return ("编年史", $"第{year}年编年史", year);
                    return (genre, $"{name}{genre}", 0);
                }
            }

            return (OtherGroup, filename.Replace('_', ' '), 0);
        }

        public static string ParseAdvisoryName(string filename) => ParseDocumentName(filename, "封臣谏言");

        public static string ParseEdictName(string filename) => ParseDocumentName(filename, "国王诏令");

        public static string ParseSecretAdvisoryName(string filename) => ParseDocumentName(filename, "国王密陈");

        private static string ParseDocumentName(string filename, string label)
        {
            var parts = filename.Split('_');
            if (parts.Length >= 2 && int.TryParse(parts[parts.Length - 1], out int year))
            {
                var kingdom = string.Join("_", parts, 0, parts.Length - 1);
                return $"{label} · {kingdom} · 第{year}年";
            }

            return $"{label} · {filename}";
        }

        private static int ParseDocumentYear(string filename)
        {
            var parts = filename.Split('_');
            return parts.Length >= 2 && int.TryParse(parts[parts.Length - 1], out int year) ? year : 0;
        }

        private static void AddChronicles(List<HistoryCatalogEntry> entries, string chronicleDir)
        {
            var groups = new Dictionary<string, List<ChronicleFile>>(StringComparer.Ordinal);
            foreach (var file in SafeGetFiles(chronicleDir, "*.txt"))
            {
                var filename = Path.GetFileNameWithoutExtension(file);
                var parsed = ParseChronicleName(filename);
                if (!groups.TryGetValue(parsed.group, out var list))
                    groups[parsed.group] = list = new List<ChronicleFile>();
                list.Add(new ChronicleFile(parsed.display, file, parsed.year));
            }

            foreach (var genre in ChronicleGenreOrder)
            {
                if (!groups.TryGetValue(genre, out var list) || list.Count == 0)
                    continue;

                list.Sort(CompareChronicleFiles);
                AddHeader(entries, genre);
                foreach (var item in list)
                    Append(entries, item.Display, item.Path, HistoryEntryKind.Chronicle,
                        HistoryTextFormat.Markdown, item.Year);
            }

            if (groups.TryGetValue(OtherGroup, out var others) && others.Count > 0)
            {
                others.Sort(CompareChronicleFiles);
                AddHeader(entries, OtherGroup);
                foreach (var item in others)
                    Append(entries, item.Display, item.Path, HistoryEntryKind.Chronicle,
                        HistoryTextFormat.Markdown, item.Year);
            }
        }

        private static int CompareChronicleFiles(ChronicleFile a, ChronicleFile b)
        {
            // 编年史按年份倒序（最新在前）；本纪/世家/列传/纪事按篇名升序。
            if (a.Year != b.Year)
                return b.Year.CompareTo(a.Year);
            return string.Compare(a.Display, b.Display, StringComparison.Ordinal);
        }

        private static void AddKingdomDocuments(List<HistoryCatalogEntry> entries, string campaignDir,
            HistoryViewer viewer)
        {
            var docs = new List<HistoryDocument>();
            AddDocumentGroup(docs, Path.Combine(campaignDir, "NPCs", "World", "advisory"),
                viewer.KingdomName!, HistoryEntryKind.Advisory, ParseAdvisoryName);
            AddDocumentGroup(docs, Path.Combine(campaignDir, "NPCs", "World", "edict"),
                viewer.KingdomName!, HistoryEntryKind.Edict, ParseEdictName);

            if (viewer.IsKingdomRuler)
            {
                AddDocumentGroup(docs, Path.Combine(campaignDir, "NPCs", "World", "secret_advisory"),
                    viewer.KingdomName!, HistoryEntryKind.SecretAdvisory, ParseSecretAdvisoryName);
            }

            if (docs.Count == 0)
                return;

            // 三类政务文献共用「政务文献」一个分组头（旧实现三类各加一次，目录会出现多个同名组）。
            AddHeader(entries, KingdomDocumentsGroup);
            foreach (var doc in docs)
                Append(entries, doc.Title, doc.FilePath, doc.Kind, HistoryTextFormat.PlainText, doc.Year);
        }

        private static void AddDocumentGroup(List<HistoryDocument> docs, string dir, string kingdomName,
            HistoryEntryKind kind, Func<string, string> displayName)
        {
            if (!Directory.Exists(dir))
                return;

            var prefix = kingdomName + "_";
            var files = SafeGetFiles(dir, "*.txt")
                .Where(f => Path.GetFileNameWithoutExtension(f).StartsWith(prefix, StringComparison.Ordinal))
                .OrderByDescending(f => ParseDocumentYear(Path.GetFileNameWithoutExtension(f)))
                .ThenBy(f => Path.GetFileName(f), StringComparer.Ordinal);

            foreach (var file in files)
            {
                var filename = Path.GetFileNameWithoutExtension(file);
                docs.Add(new HistoryDocument(displayName(filename), file, kind, ParseDocumentYear(filename)));
            }
        }

        private static void AddHeader(List<HistoryCatalogEntry> entries, string title)
        {
            Append(entries, title, "", HistoryEntryKind.Header, HistoryTextFormat.PlainText, 0);
        }

        private static void Append(List<HistoryCatalogEntry> entries, string title, string path,
            HistoryEntryKind kind, HistoryTextFormat format, int year)
        {
            entries.Add(new HistoryCatalogEntry
            {
                Index = entries.Count,
                Title = title,
                FilePath = path,
                IsHeader = kind == HistoryEntryKind.Header,
                Kind = kind,
                Format = format,
                Year = year
            });
        }

        private static string? FindChronicleDir(string campaignDir)
        {
            var dir1 = Path.Combine(campaignDir, "NPCs", "World", "history", "chronicles");
            if (Directory.Exists(dir1))
                return dir1;

            var dir2 = Path.Combine(campaignDir, "World", "history", "chronicles");
            if (Directory.Exists(dir2))
                return dir2;

            try
            {
                Directory.CreateDirectory(dir1);
                return dir1;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static string[] SafeGetFiles(string dir, string pattern)
        {
            try
            {
                return Directory.GetFiles(dir, pattern);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        private readonly struct ChronicleFile
        {
            public ChronicleFile(string display, string path, int year)
            {
                Display = display;
                Path = path;
                Year = year;
            }

            public string Display { get; }
            public string Path { get; }
            public int Year { get; }
        }

        private readonly struct HistoryDocument
        {
            public HistoryDocument(string title, string filePath, HistoryEntryKind kind, int year)
            {
                Title = title;
                FilePath = filePath;
                Kind = kind;
                Year = year;
            }

            public string Title { get; }
            public string FilePath { get; }
            public HistoryEntryKind Kind { get; }
            public int Year { get; }
        }
    }
}
