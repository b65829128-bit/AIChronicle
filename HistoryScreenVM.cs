using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace AIChronicle
{
    public class ChronicleEntryVM : ViewModel
    {
        private string _name = "";
        private string _filePath = "";
        private bool _isSelected;

        [DataSourceProperty]
        public string Name
        {
            get => _name;
            set => SetField(ref _name, value, "Name");
        }

        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value, "IsSelected");
        }

        public string FilePath => _filePath;

        private readonly HistoryScreenVM _parent;

        public ChronicleEntryVM(string name, string filePath, HistoryScreenVM parent)
        {
            _name = name;
            _filePath = filePath;
            _parent = parent;
        }

        public void ExecuteSelect()
        {
            _parent.SelectChronicle(this);
        }
    }

    public class HistoryScreenVM : ViewModel
    {
        [DataSourceProperty]
        public MBBindingList<ChronicleEntryVM> Chronicles { get; } = new();

        private string _titleText = "卡拉迪亚编年史";

        [DataSourceProperty]
        public string TitleText
        {
            get => _titleText;
            set => SetField(ref _titleText, value, "TitleText");
        }

        private string _contentText = "";

        [DataSourceProperty]
        public string ContentText
        {
            get => _contentText;
            set => SetField(ref _contentText, value, "ContentText");
        }

        private bool _isContentVisible;

        [DataSourceProperty]
        public bool IsContentVisible
        {
            get => _isContentVisible;
            set => SetField(ref _isContentVisible, value, "IsContentVisible");
        }

        private string _selectedTitle = "";

        [DataSourceProperty]
        public string SelectedTitle
        {
            get => _selectedTitle;
            set => SetField(ref _selectedTitle, value, "SelectedTitle");
        }

        private int _fontSize = 28;

        [DataSourceProperty]
        public int FontSize
        {
            get => _fontSize;
            set => SetField(ref _fontSize, value, "FontSize");
        }

        public Action? OnClose { get; set; }

        public HistoryScreenVM()
        {
            FontSize = MySettings.Instance?.ChronicleFontSize ?? 28;
            LoadChronicleList();
            if (Chronicles.Count > 0)
                SelectChronicle(Chronicles[0]);
        }

        private void LoadChronicleList()
        {
            var chronicleDir = FindChronicleDir();
            if (chronicleDir != null && Directory.Exists(chronicleDir))
            {
                var files = Directory.GetFiles(chronicleDir, "*.txt");
                Array.Sort(files, (a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.Ordinal));
                Array.Reverse(files);

                foreach (var file in files)
                {
                    var fname = Path.GetFileNameWithoutExtension(file);
                    var displayName = ParseChronicleName(fname);
                    Chronicles.Add(new ChronicleEntryVM(displayName, file, this));
                }
            }

            LoadAdvisoryList();
            LoadEdictList();
            LoadSecretAdvisoryList();
        }

        private void LoadAdvisoryList()
        {
            if (Campaign.Current == null) return;
            if (Clan.PlayerClan?.IsUnderMercenaryService == true) return;

            var playerKingdom = Clan.PlayerClan?.Kingdom;
            if (playerKingdom == null) return;

            var campaignDir = PromptManager.CampaignDir;
            if (string.IsNullOrEmpty(campaignDir)) return;

            var advisoryDir = Path.Combine(campaignDir, "NPCs", "World", "advisory");
            if (!Directory.Exists(advisoryDir)) return;

            var kingdomName = playerKingdom.Name.ToString();
            var files = Directory.GetFiles(advisoryDir, $"{kingdomName}_*.txt");
            Array.Sort(files, (a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.Ordinal));
            Array.Reverse(files);

            foreach (var file in files)
            {
                var fname = Path.GetFileNameWithoutExtension(file);
                var displayName = ParseAdvisoryName(fname);
                Chronicles.Add(new ChronicleEntryVM(displayName, file, this));
            }
        }

        private static string ParseAdvisoryName(string filename)
        {
            var parts = filename.Split('_');
            if (parts.Length >= 2 && int.TryParse(parts[parts.Length - 1], out int year))
            {
                var kingdom = string.Join("_", parts, 0, parts.Length - 1);
                return $"封臣谏言 · {kingdom} · 第{year}年";
            }
            return $"封臣谏言 · {filename}";
        }

        /// <summary>国王诏令（本国）：读 World/edict/{玩家王国}_*.txt，按年份倒序列出。诏令全公开，玩家可见本国。</summary>
        private void LoadEdictList()
        {
            if (Campaign.Current == null) return;
            if (Clan.PlayerClan?.IsUnderMercenaryService == true) return;

            var playerKingdom = Clan.PlayerClan?.Kingdom;
            if (playerKingdom == null) return;

            var campaignDir = PromptManager.CampaignDir;
            if (string.IsNullOrEmpty(campaignDir)) return;

            var edictDir = Path.Combine(campaignDir, "NPCs", "World", "edict");
            if (!Directory.Exists(edictDir)) return;

            var kingdomName = playerKingdom.Name.ToString();
            var files = Directory.GetFiles(edictDir, $"{kingdomName}_*.txt");
            Array.Sort(files, (a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.Ordinal));
            Array.Reverse(files);

            foreach (var file in files)
            {
                var fname = Path.GetFileNameWithoutExtension(file);
                var displayName = ParseEdictName(fname);
                Chronicles.Add(new ChronicleEntryVM(displayName, file, this));
            }
        }

        private static string ParseEdictName(string filename)
        {
            var parts = filename.Split('_');
            if (parts.Length >= 2 && int.TryParse(parts[parts.Length - 1], out int year))
            {
                var kingdom = string.Join("_", parts, 0, parts.Length - 1);
                return $"国王诏令 · {kingdom} · 第{year}年";
            }
            return $"国王诏令 · {filename}";
        }

        /// <summary>国王密陈（仅本国国王）：读 World/secret_advisory/{玩家王国}_*.txt。只有玩家是本国统治者（国王）时可见。</summary>
        private void LoadSecretAdvisoryList()
        {
            if (Campaign.Current == null) return;
            if (Clan.PlayerClan?.IsUnderMercenaryService == true) return;

            var playerKingdom = Clan.PlayerClan?.Kingdom;
            if (playerKingdom == null) return;
            if (playerKingdom.RulingClan?.Leader != Hero.MainHero) return; // 仅本国国王可看密陈

            var campaignDir = PromptManager.CampaignDir;
            if (string.IsNullOrEmpty(campaignDir)) return;

            var secretDir = Path.Combine(campaignDir, "NPCs", "World", "secret_advisory");
            if (!Directory.Exists(secretDir)) return;

            var kingdomName = playerKingdom.Name.ToString();
            var files = Directory.GetFiles(secretDir, $"{kingdomName}_*.txt");
            Array.Sort(files, (a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.Ordinal));
            Array.Reverse(files);

            foreach (var file in files)
            {
                var fname = Path.GetFileNameWithoutExtension(file);
                var displayName = ParseSecretAdvisoryName(fname);
                Chronicles.Add(new ChronicleEntryVM(displayName, file, this));
            }
        }

        private static string ParseSecretAdvisoryName(string filename)
        {
            var parts = filename.Split('_');
            if (parts.Length >= 2 && int.TryParse(parts[parts.Length - 1], out int year))
            {
                var kingdom = string.Join("_", parts, 0, parts.Length - 1);
                return $"国王密陈 · {kingdom} · 第{year}年";
            }
            return $"国王密陈 · {filename}";
        }

        private static string ParseChronicleName(string filename)
        {
            if (filename.StartsWith("chronicle_") && filename.Length >= 15)
            {
                var rest = filename.Substring(10);
                var yearPart = rest.Length >= 4 ? rest.Substring(0, 4) : rest;
                if (int.TryParse(yearPart, out int year))
                {
                    if (rest.Length == 4)
                        return $"第{year}年编年史";

                    var topic = rest.Substring(5).Replace("_", " ");
                    if (topic.Length > 30)
                        topic = topic.Substring(0, 30) + "...";
                    return $"专题 · {topic}（{year}年）";
                }
            }

            return filename.Replace("_", " ").Replace("chronicle", "编年史");
        }

        public void SelectChronicle(ChronicleEntryVM entry)
        {
            foreach (var c in Chronicles)
                c.IsSelected = c == entry;

            if (entry == null) return;

            try
            {
                var content = File.ReadAllText(entry.FilePath, Encoding.UTF8);
                SelectedTitle = entry.Name;
                ContentText = content;
                IsContentVisible = true;
            }
            catch (Exception ex)
            {
                ContentText = $"无法读取：{ex.Message}";
                IsContentVisible = true;
            }
        }

        public void ExecuteClose()
        {
            OnClose?.Invoke();
        }

        private static string? FindChronicleDir()
        {
            var campaignDir = PromptManager.CampaignDir;
            if (string.IsNullOrEmpty(campaignDir))
                return null;

            var dir1 = Path.Combine(campaignDir, "NPCs", "World", "history", "chronicles");
            if (Directory.Exists(dir1)) return dir1;

            var dir2 = Path.Combine(campaignDir, "World", "history", "chronicles");
            if (Directory.Exists(dir2)) return dir2;

            Directory.CreateDirectory(dir1);
            return dir1;
        }
    }

    public static class HistoryScreen
    {
        private static GauntletLayer? _layer;
        private static HistoryScreenVM? _vm;
        private static ScreenBase? _parentScreen;

        public static bool IsOpen => _layer != null;

        public static void Open()
        {
            if (_layer != null) return;

            var topScreen = ScreenManager.TopScreen;
            if (topScreen == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[AI编年史] 无法打开史书：当前无活动画面", Colors.Red));
                return;
            }

            _parentScreen = topScreen;
            _vm = new HistoryScreenVM();

            _vm.OnClose = () =>
            {
                if (_layer != null && _parentScreen != null)
                    _parentScreen.RemoveLayer(_layer);
                _vm?.OnFinalize();
                _layer = null;
                _vm = null;
                _parentScreen = null;
            };

            try
            {
                _layer = new GauntletLayer("HistoryLayer", 2000);
                _layer.LoadMovie("HistoryScreen", _vm);
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                _layer.IsFocusLayer = true;
                _parentScreen.AddLayer(_layer);
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[AI编年史] 打开史书失败：{ex.Message}", Colors.Red));
                _layer = null;
            }
        }

        public static void Close()
        {
            _vm?.OnClose?.Invoke();
        }
    }
}
