#if NET472
using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace AIChronicle
{
    /// <summary>
    /// 全 Web 史书：目录和正文都由 CEF 渲染；原生只保留标题栏/关闭和 fallback。
    /// 选中条目经 aiChronicle.dispatch 回主线程读取文件，再推回页面。
    /// </summary>
    public static class HistoryCefScreen
    {
        private static CefWebScreen? _screen;
        private static HistoryScreenVM? _vm;

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
                _vm = new HistoryScreenVM();
                object pageData = BuildPageData(_vm);
                var screen = new CefWebScreen(
                    "history.html",
                    "史书 · 编年史",
                    "1100×700 · CEF · 只读 · 目录与正文均由 Web 渲染",
                    pageData,
                    30,
                    1100,
                    700,
                    windowed: true);
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
                if (_vm == null || _screen == null || !_screen.IsOpen)
                    return;
                if (index < 0 || index >= _vm.Chronicles.Count)
                    return;

                var entry = _vm.Chronicles[index];
                if (entry.IsHeader)
                    return;

                try
                {
                    _vm.SelectChronicle(entry);
                    _screen.PushJson("showArticle", BuildArticle(_vm, index));
                }
                catch (Exception ex)
                {
                    UiTrace.Log("HistoryCefScreen select failed: " + ex);
                    _screen.PushJson("showError", ex.Message);
                }
            });
        }

        private static void OnScreenClosed()
        {
            var vm = _vm;
            _vm = null;
            _screen = null;
            try { vm?.OnFinalize(); } catch { }
        }

        private static void CleanupClosed()
        {
            if (_screen != null && !_screen.IsOpen)
            {
                var vm = _vm;
                _vm = null;
                _screen = null;
                try { vm?.OnFinalize(); } catch { }
            }
        }

        private static void Cleanup()
        {
            try { _screen?.Close(); } catch { }
            var vm = _vm;
            _vm = null;
            _screen = null;
            try { vm?.OnFinalize(); } catch { }
        }

        private static object BuildPageData(HistoryScreenVM vm)
        {
            var entries = new List<object>();
            int selected = -1;

            for (int i = 0; i < vm.Chronicles.Count; i++)
            {
                var entry = vm.Chronicles[i];
                if (entry.IsSelected)
                    selected = i;

                entries.Add(new Dictionary<string, object>
                {
                    ["index"] = i,
                    ["title"] = entry.Name,
                    ["header"] = entry.IsHeader,
                    ["selected"] = entry.IsSelected
                });
            }

            object? article = selected >= 0 ? BuildArticle(vm, selected) : null;
            return new Dictionary<string, object>
            {
                ["entries"] = entries,
                ["article"] = article,
                ["fontSize"] = vm.FontSize
            };
        }

        private static object BuildArticle(HistoryScreenVM vm, int index)
        {
            return new Dictionary<string, object>
            {
                ["index"] = index,
                ["title"] = vm.SelectedTitle,
                ["body"] = vm.ContentText,
                ["fontSize"] = vm.FontSize
            };
        }
    }
}
#endif
