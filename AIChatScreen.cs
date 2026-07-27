using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace MyFirstMod
{
    public static class AIChatScreen
    {
        private static GauntletLayer? _layer;
        private static AIChatScreenVM? _vm;
        private static ScreenBase? _parentScreen;

        public static bool IsOpen => _layer != null;

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

        private static void DoOpenWithIntent(Hero hero, string intent)
        {
            if (_layer != null)
                return;

            var topScreen = ScreenManager.TopScreen;
            if (topScreen == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[MyFirstMod] 错误：TopScreen 为 null",
                    Colors.Red));
                return;
            }

            _parentScreen = topScreen;
            _vm = new AIChatScreenVM(hero, intent);

            _vm.OnClose = () =>
            {
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
                    $"[MyFirstMod] 打开聊天窗口失败：{ex.Message}",
                    Colors.Red));
                _layer = null;
            }
        }
    }
}
