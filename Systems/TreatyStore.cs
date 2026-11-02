using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;

namespace AIChronicle
{
    /// <summary>
    /// 已签署条约的持久化（World/diplomacy/treaties.json）。
    /// 条约与朝议档案分离：朝议记录会被清理，条约的休战义务必须跨存档保留。
    /// </summary>
    public static class TreatyStore
    {
        private static readonly object _sync = new();
        private static TreatyBook? _book;
        private static string _loadedDir = "";

        public static void ResetForNewCampaign()
        {
            lock (_sync)
            {
                _book = null;
                _loadedDir = "";
            }
        }

        private static string FilePath
        {
            get
            {
                var baseDir = PromptManager.CampaignDir;
                if (string.IsNullOrEmpty(baseDir)) baseDir = PromptManager.PromptsBaseDir;
                var dir = Path.Combine(baseDir, "NPCs", "World", "diplomacy");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "treaties.json");
            }
        }

        private static TreatyBook Load()
        {
            var baseDir = PromptManager.CampaignDir;
            if (string.IsNullOrEmpty(baseDir)) baseDir = PromptManager.PromptsBaseDir;
            if (_book != null && string.Equals(_loadedDir, baseDir, StringComparison.Ordinal))
                return _book;

            _loadedDir = baseDir;
            _book = new TreatyBook();
            try
            {
                var path = FilePath;
                if (File.Exists(path))
                {
                    var json = SafeFileIO.ReadAllText(path);
                    var loaded = JsonConvert.DeserializeObject<TreatyBook>(json, CuriaStore.JsonSettings);
                    if (loaded?.Treaties != null) _book = loaded;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("条约记录读取失败：" + ex.Message);
            }
            return _book!;
        }

        private static void Save()
        {
            try
            {
                SafeFileIO.WriteAllText(FilePath, JsonConvert.SerializeObject(Load(), CuriaStore.JsonSettings));
            }
            catch (Exception ex)
            {
                DebugLogger.Log("条约记录写入失败：" + ex.Message);
            }
        }

        public static void Add(TreatyRecord record)
        {
            if (record == null) return;
            lock (_sync)
            {
                AddCore(record);
            }
        }

        private static void AddCore(TreatyRecord record)
        {
            var book = Load();
            book.Treaties.RemoveAll(t => t.Id == record.Id);
            // 同一对王国的新条约取代旧条约：旧休战义务作废，避免“新约不设休战、旧约仍在拦宣战”。
            foreach (var old in book.Treaties)
            {
                if (old.Id != record.Id && old.Status == "active" && SameRecordPair(old, record))
                    old.Status = "superseded";
            }
            book.Treaties.Add(record);
            Save();
        }

        public static TreatyRecord? FindActiveTruce(Kingdom? a, Kingdom? b)
        {
            if (a == null || b == null || a == b) return null;
            lock (_sync)
            {
                return FindActiveTruceCore(a, b);
            }
        }

        private static TreatyRecord? FindActiveTruceCore(Kingdom a, Kingdom b)
        {
            var today = CuriaStore.Today;
            var book = Load();
            TreatyRecord? latest = null;
            var changed = false;
            foreach (var t in book.Treaties)
            {
                if (!SamePair(t, a, b)) continue;
                if (t.Status == "active" && t.TruceUntilDay > 0 && !t.IsActiveTruce(today))
                {
                    t.Status = "expired";
                    changed = true;
                }
                if (latest == null || t.SignedDay >= latest.SignedDay) latest = t;
            }
            if (changed) Save();
            return latest != null && latest.IsActiveTruce(today) ? latest : null;
        }

        public static List<TreatyRecord> ListActiveFor(Kingdom? kingdom, int max = 8)
        {
            if (kingdom == null) return new List<TreatyRecord>();
            lock (_sync)
            {
                return ListActiveForCore(kingdom, max);
            }
        }

        private static List<TreatyRecord> ListActiveForCore(Kingdom kingdom, int max)
        {
            var result = new List<TreatyRecord>();
            var today = CuriaStore.Today;
            var book = Load();
            var changed = false;
            foreach (var t in book.Treaties.OrderByDescending(x => x.SignedDay))
            {
                if (!SameKingdom(t, kingdom)) continue;
                if (t.Status == "active" && t.TruceUntilDay > 0 && !t.IsActiveTruce(today))
                {
                    t.Status = "expired";
                    changed = true;
                    continue;
                }
                if (t.IsActiveTruce(today)) result.Add(t);
                if (result.Count >= max) break;
            }
            if (changed) Save();
            return result;
        }

        public static TreatyRecord? FindByProposal(string proposalId)
        {
            if (string.IsNullOrWhiteSpace(proposalId)) return null;
            lock (_sync)
            {
                return Load().Treaties.FirstOrDefault(t => t.ProposalId == proposalId);
            }
        }

        public static void MarkBroken(TreatyRecord? record, string brokenBy)
        {
            if (record == null) return;
            lock (_sync)
            {
                record.Status = "broken";
                record.BrokenDay = CuriaStore.Today;
                record.BrokenBy = brokenBy ?? "";
                Save();
            }
        }

        private static bool SameKingdom(TreatyRecord t, Kingdom k)
        {
            var id = k.StringId ?? "";
            var name = k.Name?.ToString() ?? "";
            return t.ProposerKingdomId == id || t.TargetKingdomId == id
                   || t.ProposerKingdomName == name || t.TargetKingdomName == name;
        }

        private static bool SameRecordPair(TreatyRecord a, TreatyRecord b)
        {
            return (RecordMatch(a.ProposerKingdomId, a.ProposerKingdomName, b.ProposerKingdomId, b.ProposerKingdomName)
                    && RecordMatch(a.TargetKingdomId, a.TargetKingdomName, b.TargetKingdomId, b.TargetKingdomName))
                   || (RecordMatch(a.ProposerKingdomId, a.ProposerKingdomName, b.TargetKingdomId, b.TargetKingdomName)
                       && RecordMatch(a.TargetKingdomId, a.TargetKingdomName, b.ProposerKingdomId, b.ProposerKingdomName));
        }

        private static bool RecordMatch(string id, string name, string otherId, string otherName)
        {
            return (!string.IsNullOrEmpty(id) && id == otherId)
                   || (!string.IsNullOrEmpty(name) && name == otherName);
        }

        private static bool SamePair(TreatyRecord t, Kingdom a, Kingdom b)
        {
            return (Matches(t.ProposerKingdomId, t.ProposerKingdomName, a) && Matches(t.TargetKingdomId, t.TargetKingdomName, b))
                   || (Matches(t.ProposerKingdomId, t.ProposerKingdomName, b) && Matches(t.TargetKingdomId, t.TargetKingdomName, a));
        }

        private static bool Matches(string id, string name, Kingdom k)
        {
            var kid = k.StringId ?? "";
            var kname = k.Name?.ToString() ?? "";
            return (!string.IsNullOrEmpty(id) && id == kid) || (!string.IsNullOrEmpty(name) && name == kname);
        }

        public static string DescribeActiveFor(Kingdom? kingdom, int max = 5)
        {
            if (kingdom == null) return "";
            var list = ListActiveFor(kingdom, max);
            if (list.Count == 0) return "";
            var lines = new List<string>();
            foreach (var t in list)
            {
                var other = Matches(t.ProposerKingdomId, t.ProposerKingdomName, kingdom)
                    ? t.TargetKingdomName
                    : t.ProposerKingdomName;
                lines.Add($"与{other}休战至第{t.TruceUntilDay}日（{TreatyText.DescribeNeutral(t.Terms, t.ProposerKingdomName, t.TargetKingdomName)}）");
            }
            return string.Join("；", lines);
        }
    }
}
