using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace MyFirstMod
{
    public class LetterListEntryVM : ViewModel
    {
        private string _npcLabel = "";
        private readonly Hero? _hero;
        private readonly LetterListVM _parent;

        [DataSourceProperty]
        public string NpcLabel
        {
            get => _npcLabel;
            set => SetField(ref _npcLabel, value, "NpcLabel");
        }

        public LetterListEntryVM(string label, Hero? hero, LetterListVM parent)
        {
            _npcLabel = label;
            _hero = hero;
            _parent = parent;
        }

        public void ExecuteSelect()
        {
            _parent.OnEntrySelected(_hero);
        }
    }

    public class LetterListVM : ViewModel
    {
        [DataSourceProperty]
        public MBBindingList<LetterListEntryVM> Messages { get; } = new();

        public Action? OnClose { get; set; }
        public Action<Hero?>? OnSelectNpc { get; set; }

        public void AddEntry(string label, Hero? hero)
        {
            Messages.Add(new LetterListEntryVM(label, hero, this));
        }

        public void OnEntrySelected(Hero? hero)
        {
            OnSelectNpc?.Invoke(hero);
        }

        public void ExecuteClose()
        {
            OnClose?.Invoke();
        }
    }

    public static class LetterListScreen
    {
        private static GauntletLayer? _layer;
        private static LetterListVM? _vm;
        private static ScreenBase? _parentScreen;

        public static bool IsOpen => _layer != null;

        public static void Open()
        {
            if (_layer != null) return;
            var topScreen = ScreenManager.TopScreen;
            if (topScreen == null) return;

            _parentScreen = topScreen;
            _vm = new LetterListVM();

            var knownIds = SubModule.GetKnownNpcIds();
            foreach (var entityId in knownIds)
            {
                var entity = EntityManager.GetOrCreateEntityById(entityId);
                if (entity == null || entity.HeroRef == null) continue;
                var label = entity.Name;
                if (!string.IsNullOrEmpty(entity.Title))
                    label += "  " + entity.Title;
                _vm.AddEntry(label, entity.HeroRef);
            }

            if (_vm.Messages.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[MyFirstMod] 你还没有对话过任何领主，无法写信。", Colors.Yellow));
                _parentScreen = null;
                _vm = null;
                return;
            }

            _vm.OnSelectNpc = (hero) =>
            {
                if (hero == null) return;
                Close();
                var player = Hero.MainHero;
                if (player != null)
                    EntityManager.ActivateInteraction(hero, player);
                AIChatScreen.RequestOpenLetter(hero);
            };

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
                _layer = new GauntletLayer("LetterListLayer", 2000);
                _layer.LoadMovie("LetterListScreen", _vm);
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                _layer.IsFocusLayer = true;
                _parentScreen.AddLayer(_layer);
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[MyFirstMod] 打开书信列表失败：{ex.Message}", Colors.Red));
                _layer = null;
            }
        }

        public static void Close()
        {
            if (_layer != null && _parentScreen != null)
                _parentScreen.RemoveLayer(_layer);
            _vm?.OnFinalize();
            _layer = null;
            _vm = null;
            _parentScreen = null;
        }
    }
}
