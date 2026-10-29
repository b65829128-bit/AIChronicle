using System;
using System.Linq;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace AIChronicle
{
    /// <summary>史册目录项。数据来自 HistoryArchive，本类只负责 Gauntlet 绑定与选中态。</summary>
    public class ChronicleEntryVM : ViewModel
    {
        private bool _isSelected;

        [DataSourceProperty]
        public string Name => _entry.Title;

        [DataSourceProperty]
        public bool IsHeader => _entry.IsHeader;

        [DataSourceProperty]
        public bool IsSelectable => _entry.IsSelectable;

        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value, "IsSelected");
        }

        public HistoryCatalogEntry Entry => _entry;

        private readonly HistoryCatalogEntry _entry;
        private readonly HistoryScreenVM _parent;

        public ChronicleEntryVM(HistoryCatalogEntry entry, HistoryScreenVM parent)
        {
            _entry = entry;
            _parent = parent;
        }

        public void ExecuteSelect()
        {
            if (!IsSelectable) return;
            _parent.SelectChronicle(this);
        }
    }

    /// <summary>
    /// 原生史书 ViewModel：只做绑定/选中态/字体，目录扫描与正文读取全部走 HistoryArchive。
    /// CEF 史书（HistoryCefScreen）使用同一份 HistoryArchive，不再复用本类。
    /// </summary>
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
            var firstSelectable = Chronicles.FirstOrDefault(c => c.IsSelectable);
            if (firstSelectable != null)
                SelectChronicle(firstSelectable);
        }

        private void LoadChronicleList()
        {
            var catalog = HistoryArchive.LoadCatalog(
                HistoryViewerFactory.ForCurrentGame(),
                PromptManager.CampaignDir);

            foreach (var entry in catalog.Entries)
                Chronicles.Add(new ChronicleEntryVM(entry, this));
        }

        public void SelectChronicle(ChronicleEntryVM entry)
        {
            if (entry == null || entry.IsHeader) return;

            foreach (var c in Chronicles)
                c.IsSelected = ReferenceEquals(c, entry);

            var article = HistoryArchive.ReadArticle(entry.Entry);
            SelectedTitle = entry.Name;

            if (article.HasError)
            {
                ContentText = "无法读取：" + article.Error;
            }
            else if (string.IsNullOrWhiteSpace(article.Body))
            {
                ContentText = "（此篇暂无正文）";
            }
            else if (article.Format == HistoryTextFormat.Markdown)
            {
                // 原生 RichText 不认识 Markdown；CEF 不可用时降级为去标记的纯文本。
                ContentText = HistoryMarkdown.ToPlainText(article.Body);
                if (string.IsNullOrWhiteSpace(ContentText))
                    ContentText = "（此篇暂无正文）";
            }
            else
            {
                ContentText = article.Body;
            }

            IsContentVisible = true;
        }

        public void ExecuteClose()
        {
            OnClose?.Invoke();
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
