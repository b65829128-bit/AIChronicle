using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace AIChronicle
{
    public static class AIChatScreen
    {
        private static GauntletLayer? _layer;
        private static AIChatScreenVM? _vm;
        private static ScreenBase? _parentScreen;

        public static bool IsOpen => _layer != null;

        public static void Close()
        {
            _vm?.OnClose?.Invoke();
        }

        public static void RequestOpen(Hero hero)
        {
            if (_layer != null)
                return;

            SubModule.RequestChatOpen(hero);
        }

        public static void RequestOpenLetter(Hero hero)
        {
            if (_layer != null)
                return;

            SubModule.RequestLetterOpen(hero);
        }

        public static void DoOpen(Hero hero)
        {
            DoOpenWithIntent(hero, "conversation");
        }

        public static void DoOpenLetter(Hero hero)
        {
            DoOpenWithIntent(hero, "letter");
        }

        public static void OpenChancery()
        {
            if (_layer != null) return;
            var hero = Hero.MainHero;
            if (hero == null) return;

            var topScreen = ScreenManager.TopScreen;
            if (topScreen == null) return;

            _parentScreen = topScreen;
            _vm = new AIChatScreenVM(hero, "chancery");

            _vm.OnClose = () =>
            {
                _vm?.MarkThreadReadIfPlayerThread(); // 窗口关闭时同步已读水位（秘书处为 no-op）
                if (_layer != null && _parentScreen != null)
                    _parentScreen.RemoveLayer(_layer);
                _vm?.OnFinalize();
                _layer = null;
                _vm = null;
                _parentScreen = null;
            };

            try
            {
                _layer = new GauntletLayer("AIChatLayer", 1000);
                _layer.LoadMovie("AIChatScreen", _vm);
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                _layer.IsFocusLayer = true;
                _parentScreen.AddLayer(_layer);
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[AI编年史] 打开秘书处失败：{ex.Message}",
                    Colors.Red));
                _layer = null;
            }
        }

        private static void DoOpenWithIntent(Hero hero, string intent)
        {
            if (_layer != null)
                return;

            var topScreen = ScreenManager.TopScreen;
            if (topScreen == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[AI编年史] 错误：TopScreen 为 null",
                    Colors.Red));
                return;
            }

            _parentScreen = topScreen;
            _vm = new AIChatScreenVM(hero, intent);

            _vm.OnClose = () =>
            {
                _vm?.MarkThreadReadIfPlayerThread(); // 窗口关闭时同步已读水位
                if (_layer != null && _parentScreen != null)
                {
                    _parentScreen.RemoveLayer(_layer);
                }
                _vm?.OnFinalize();
                _layer = null;
                _vm = null;
                _parentScreen = null;
            };

            try
            {
                _layer = new GauntletLayer("AIChatLayer", 1000);
                _layer.LoadMovie("AIChatScreen", _vm);
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                _layer.IsFocusLayer = true;
                _parentScreen.AddLayer(_layer);
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[AI编年史] 打开聊天窗口失败：{ex.Message}",
                    Colors.Red));
                _layer = null;
            }
        }
    }
}
