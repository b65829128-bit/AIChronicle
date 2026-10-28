using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace AIChronicle
{
    /// <summary>KitScreen 的占位数据源；KitHost 本身不做数据绑定。</summary>
    public sealed class KitVM : ViewModel
    {
    }

    /// <summary>
    /// 原生 Kit 屏幕基类：统一处理 GauntletLayer、KitHost 加载、输入与清理。
    /// 子类只实现 Build。
    /// </summary>
    public abstract class KitScreen
    {
        private static int _openCount;

        private GauntletLayer? _layer;
        private GauntletMovieIdentifier? _movie;
        private ScreenBase? _parent;

        protected KitRoot? Root { get; private set; }
        protected UIContext? Context => Root?.Context ?? _layer?.UIContext;

        public bool IsOpen => _layer != null;
        public static bool AnyOpen => _openCount > 0;

        public void Open()
        {
            if (_layer != null)
                return;

            var topScreen = ScreenManager.TopScreen;
            if (topScreen == null)
            {
                Info("无法打开 UI：TopScreen 为空");
                return;
            }

            try
            {
                UiTrace.Log("Open start top=" + topScreen.GetType().Name);
                _parent = topScreen;
                _layer = new GauntletLayer("KitLayer", 1000);
                UiTrace.Log("layer created");
                _movie = _layer.LoadMovie("KitHost", new KitVM());
                UiTrace.Log("LoadMovie returned movie=" + (_movie?.Movie != null));
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                _layer.IsFocusLayer = true;
                _parent.AddLayer(_layer);
                UiTrace.Log("layer added");
                _layer.UIContext.BrushFactory.BrushChange += OnBrushChanged;

                var rootWidget = _movie?.Movie?.RootWidget;
                if (rootWidget == null)
                    throw new InvalidOperationException("KitHost 未加载");

                UiTrace.Log("root widget ok");
                Root = new KitRoot(rootWidget.Context, rootWidget);
                UiTrace.Log("Build start");
                Build(Root);
                UiTrace.Log("Build done");
                MainThreadExecutor.Post(() => UiTrace.Log("Open post-build tick"));
                _openCount++;
            }
            catch (Exception ex)
            {
                UiTrace.Log("Open failed: " + ex);
                Cleanup();
                Info($"打开 UI 失败：{ex.Message}");
            }
        }

        public void Close()
        {
            if (_layer == null)
                return;

            UiTrace.Log("Close start");
            Cleanup();
            if (_openCount > 0)
                _openCount--;
            OnClosed();
        }

        public void Rebuild()
        {
            if (Root == null)
                return;

            Root.Clear();
            Build(Root);
        }

        protected abstract void Build(KitRoot root);

        protected virtual void OnClosed()
        {
        }

        /// <summary>
        /// 主题 brush 热重载后，GauntletMovie 可能重建根节点；下一帧重新取根并重建 Kit 树。
        /// </summary>
        private void OnBrushChanged()
        {
            MainThreadExecutor.Post(() =>
            {
                if (_layer == null || _movie?.Movie == null)
                    return;

                var rootWidget = _movie.Movie.RootWidget;
                if (rootWidget == null)
                    return;

                Root = new KitRoot(rootWidget.Context, rootWidget);
                Root.Clear();
                Build(Root);
            });
        }

        private void Cleanup()
        {
            try
            {
                if (_layer != null)
                    _layer.UIContext.BrushFactory.BrushChange -= OnBrushChanged;
            }
            catch
            {
                // UIContext 可能已释放
            }

            try
            {
                if (_layer != null && _movie != null)
                    _layer.ReleaseMovie(_movie);
            }
            catch
            {
                // 资源已释放时不阻塞关闭流程
            }

            try
            {
                if (_layer != null && _parent != null)
                    _parent.RemoveLayer(_layer);
            }
            catch
            {
                // 同上
            }

            _layer = null;
            _movie = null;
            _parent = null;
            Root = null;
        }

        private static void Info(string message)
        {
            InformationManager.DisplayMessage(new InformationMessage($"[AI编年史·UI] {message}", Colors.Red));
        }
    }
}
