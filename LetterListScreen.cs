using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace AIChronicle
{
    public class LetterListEntryVM : ViewModel
    {
        private string _npcLabel = "";
        private readonly LetterListVM _parent;
        private readonly Hero? _writerHero;

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
        }

        public void ExecuteSelect()
        {
            _parent.OnEntrySelected(_writerHero);
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

            var playerId = "main_hero";
            if (Hero.MainHero != null)
                playerId = EntityManager.GetOrCreateEntity(Hero.MainHero).Id;

            // 统一联系人列表：KnownNpcIds（面对面聊过 + 来信过的 NPC），每行附未读角标
            var rows = new List<(string label, Hero? hero, int unread, DateTime lastWrite)>();
            foreach (var entityId in SubModule.GetKnownNpcIds())
            {
                var entity = EntityManager.GetOrCreateEntityById(entityId);
                if (entity == null || entity.HeroRef == null) continue;

                var unread = AgentManager.GetThreadUnreadCount(entityId, playerId);

                var label = entity.Name;
                if (!string.IsNullOrEmpty(entity.Title))
                    label += "  " + entity.Title;
                if (unread > 0)
                    label += $"  · {unread} 条未读";

                var lastWrite = DateTime.MinValue;
                try
                {
                    var threadPath = AgentManager.GetChatLogPathFor(entityId, playerId);
                    if (threadPath != null && File.Exists(threadPath))
                        lastWrite = File.GetLastWriteTimeUtc(threadPath);
                }
                catch { }

                rows.Add((label, entity.HeroRef, unread, lastWrite));
            }

            // 排序：未读优先 → 线程最近活跃优先
            rows = rows
                .OrderByDescending(r => r.unread > 0)
                .ThenByDescending(r => r.lastWrite)
                .ToList();

            foreach (var row in rows)
                _vm.AddEntry(row.label, row.hero);

            if (_vm.Messages.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[AI编年史] 你还没有往来过任何领主。", Colors.Yellow));
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
                    $"[AI编年史] 打开书信列表失败：{ex.Message}", Colors.Red));
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
