using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace MyFirstMod
{
    public class LetterListEntryVM : ViewModel
    {
        private string _npcLabel = "";
        private readonly LetterListVM _parent;
        private readonly Hero? _writerHero;
        private readonly string _inboxFileName;

        [DataSourceProperty]
        public string NpcLabel
        {
            get => _npcLabel;
            set => SetField(ref _npcLabel, value, "NpcLabel");
        }

        public LetterListEntryVM(string label, Hero? hero, LetterListVM parent)
        {
            _npcLabel = label;
            _writerHero = hero;
            _parent = parent;
            _inboxFileName = "";
        }

        public LetterListEntryVM(string label, string inboxFileName, LetterListVM parent)
        {
            _npcLabel = label;
            _writerHero = null;
            _parent = parent;
            _inboxFileName = inboxFileName;
        }

        public void ExecuteSelect()
        {
            if (!string.IsNullOrEmpty(_inboxFileName))
                _parent.OnInboxSelected(_inboxFileName);
            else
                _parent.OnEntrySelected(_writerHero);
        }
    }

    public class LetterListVM : ViewModel
    {
        [DataSourceProperty]
        public MBBindingList<LetterListEntryVM> Messages { get; } = new();

        public Action? OnClose { get; set; }
        public Action<Hero?>? OnSelectNpc { get; set; }
        public Action<string>? OnSelectInbox { get; set; }

        public void AddEntry(string label, Hero? hero)
        {
            Messages.Add(new LetterListEntryVM(label, hero, this));
        }

        public void AddInboxEntry(string label, string fileName)
        {
            Messages.Add(new LetterListEntryVM(label, fileName, this));
        }

        public void OnEntrySelected(Hero? hero)
        {
            OnSelectNpc?.Invoke(hero);
        }

        public void OnInboxSelected(string fileName)
        {
            OnSelectInbox?.Invoke(fileName);
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

            var playerId = "main_hero";
            if (Hero.MainHero != null)
                playerId = EntityManager.GetOrCreateEntity(Hero.MainHero).Id;

            var inboxFiles = AgentManager.ListInbox(playerId);
            foreach (var fileName in inboxFiles)
            {
                var senderName = GetSenderName(fileName);
                _vm.AddInboxEntry("[来信] " + senderName, fileName);
            }

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
                    "[MyFirstMod] 你还没有对话过任何领主，也没有收到过信件。", Colors.Yellow));
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

            _vm.OnSelectInbox = (fileName) =>
            {
                Close();
                var playerEntity = Hero.MainHero != null
                    ? EntityManager.GetOrCreateEntity(Hero.MainHero)
                    : null;
                if (playerEntity == null) return;

                var letterContent = AgentManager.ReadInboxLetter(playerEntity.Id, fileName);
                if (string.IsNullOrEmpty(letterContent)) return;

                var senderName = GetSenderName(fileName);
                var senderEntity = ResolveSenderEntity(fileName);

                InformationManager.ShowInquiry(new InquiryData(
                    "来信 — " + senderName,
                    letterContent.Length > 800 ? letterContent.Substring(0, 800) + "..." : letterContent,
                    true, true, "回复", "关闭",
                    () =>
                    {
                        if (senderEntity?.HeroRef == null) return;
                        var player = Hero.MainHero;
                        if (player != null)
                            EntityManager.ActivateInteraction(senderEntity.HeroRef, player);
                        AIChatScreen.RequestOpenLetter(senderEntity.HeroRef);
                    },
                    () => { }
                ), false, false);
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

        private static string GetSenderName(string fileName)
        {
            var entity = EntityManager.GetEntityById(fileName);
            if (entity != null) return entity.Name;
            return fileName;
        }

        private static Entity? ResolveSenderEntity(string fileName)
        {
            return EntityManager.GetOrCreateEntityById(fileName);
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
