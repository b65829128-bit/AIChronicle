#if NET472
using System;
using System.Collections.Generic;

namespace AIChronicle
{
    /// <summary>
    /// 全 Web 史书：目录和正文都由 CEF 渲染；原生只保留标题栏/关闭和 fallback。
    /// 目录与正文统一由 HistoryArchive 提供，不再复用原生 HistoryScreenVM。
    /// 选中条目经 aiChronicle.dispatch 回主线程读取文件，再推回页面。
    /// </summary>
    public static class HistoryCefScreen
    {
        private static CefWebScreen? _screen;
        private static HistoryCatalog? _catalog;
        private static int _selectedIndex = -1;
        private static int _fontSize = 28;

        public static bool IsOpen => _screen != null && _screen.IsOpen;

        public static bool Open()
        {
            if (IsOpen)
                return true;
            if (!CefWebHost.EnsureInitialized())
            {
                UiTrace.Log("HistoryCefScreen.Open skipped: CEF init failed");
                return false;
            }

            CleanupClosed();
            try
            {
                _fontSize = MySettings.Instance?.ChronicleFontSize ?? 28;
                _catalog = HistoryArchive.LoadCatalog(
                    HistoryViewerFactory.ForCurrentGame(),
                    PromptManager.CampaignDir);
                _selectedIndex = _catalog.FirstSelectableIndex;

                HistoryArticle? article = _selectedIndex >= 0
                    ? HistoryArchive.ReadArticle(_catalog.Entries[_selectedIndex])
                    : null;

                object pageData = BuildPageData(article);
                var screen = new CefWebScreen(
                    "history.html",
                    "卡拉迪亚编年史",
                    "史官所录 · 只读",
                    pageData,
                    30,
                    1100,
                    700,
                    windowed: true,
                    contentPadding: 0f,
                    showStatus: false);
                screen.PageAction += OnPageAction;
                screen.Closed += OnScreenClosed;
                _screen = screen;
                screen.Open();

                if (!screen.IsOpen)
                {
                    UiTrace.Log("HistoryCefScreen.Open failed: KitScreen not open");
                    Cleanup();
                    return false;
                }

                UiTrace.Log("HistoryCefScreen.Open ok");
                return true;
            }
            catch (Exception ex)
            {
                UiTrace.Log("HistoryCefScreen.Open failed: " + ex);
                Cleanup();
                return false;
            }
        }

        public static void Close()
        {
            _screen?.Close();
        }

        private static void OnPageAction(string id, string payload)
        {
            UiTrace.Log($"HistoryCefScreen.PageAction id={id} payload={payload}");
            if (id != "select")
                return;

            int index;
            if (!int.TryParse(payload, out index))
                return;

            MainThreadExecutor.Post(() =>
            {
                var catalog = _catalog;
                if (catalog == null || _screen == null || !_screen.IsOpen)
                    return;
                if (index < 0 || index >= catalog.Entries.Count)
                    return;

                var entry = catalog.Entries[index];
                if (!entry.IsSelectable)
                    return;

                _selectedIndex = index;
                var article = HistoryArchive.ReadArticle(entry);
                _screen.PushJson("showArticle", BuildArticle(article));
            });
        }

        private static void OnScreenClosed()
        {
            _screen = null;
            _catalog = null;
            _selectedIndex = -1;
        }

        private static void CleanupClosed()
        {
            if (_screen != null && !_screen.IsOpen)
            {
                _screen = null;
                _catalog = null;
                _selectedIndex = -1;
            }
        }

        private static void Cleanup()
        {
            try { _screen?.Close(); } catch { }
            _screen = null;
            _catalog = null;
            _selectedIndex = -1;
        }

        private static object BuildPageData(HistoryArticle? article)
        {
            var entries = new List<object>();
            var catalog = _catalog;
            if (catalog != null)
            {
                foreach (var entry in catalog.Entries)
                {
                    entries.Add(new Dictionary<string, object>
                    {
                        ["index"] = entry.Index,
                        ["title"] = entry.Title,
                        ["header"] = entry.IsHeader,
                        ["selected"] = entry.Index == _selectedIndex
                    });
                }
            }

            return new Dictionary<string, object?>
            {
                ["entries"] = entries,
                ["article"] = article != null ? BuildArticle(article) : null,
                ["fontSize"] = _fontSize
            };
        }

        private static object BuildArticle(HistoryArticle article)
        {
            string category = "史册";
            int year = 0;
            int position = 0;
            int total = 0;

            var catalog = _catalog;
            if (catalog != null)
            {
                string currentCategory = "史册";
                for (int i = 0; i < catalog.Entries.Count; i++)
                {
                    var entry = catalog.Entries[i];
                    if (entry.IsHeader)
                    {
                        currentCategory = entry.Title;
                        continue;
                    }

                    total++;
                    if (i == article.Index)
                    {
                        category = currentCategory;
                        year = entry.Year;
                        position = total;
                    }
                }
            }

            return new Dictionary<string, object?>
            {
                ["index"] = article.Index,
                ["title"] = article.Title,
                ["body"] = article.Body,
                ["format"] = article.Format == HistoryTextFormat.Markdown ? "markdown" : "plain",
                ["error"] = article.Error ?? "",
                ["fontSize"] = _fontSize,
                ["category"] = category,
                ["year"] = year,
                ["position"] = position,
                ["total"] = total
            };
        }
    }
}
#endif
