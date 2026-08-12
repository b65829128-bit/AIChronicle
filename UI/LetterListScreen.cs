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
        private readonly bool _isEnvoy;

        [DataSourceProperty]
        public string NpcLabel
        {
            get => _npcLabel;
            set => SetField(ref _npcLabel, value, "NpcLabel");
        }

        public LetterListEntryVM(string label, Hero? hero, LetterListVM parent, bool isEnvoy = false)
        {
            _npcLabel = label;
            _writerHero = hero;
            _parent = parent;
            _isEnvoy = isEnvoy;
        }

        public void ExecuteSelect()
        {
            _parent.OnEntrySelected(_writerHero, _isEnvoy);
        }
    }

    public class LetterListVM : ViewModel
    {
        [DataSourceProperty]
        public MBBindingList<LetterListEntryVM> Messages { get; } = new();

        public Action? OnClose { get; set; }
        public Action<Hero?, bool>? OnSelectNpc { get; set; }

        public void AddEntry(string label, Hero? hero, bool isEnvoy = false)
        {
            Messages.Add(new LetterListEntryVM(label, hero, this, isEnvoy));
        }

        public void OnEntrySelected(Hero? hero, bool isEnvoy)
        {
            OnSelectNpc?.Invoke(hero, isEnvoy);
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

            // 统一联系人列表：KnownNpcIds（面对面聊过 + 来信过的 NPC），每行附未读角标；
            // 兼有私有密使往来（World/correspondence/）时附 📨 标记与密使未读角标。
            var rows = new List<(string label, Hero? hero, int unread, DateTime lastWrite, bool pendingEnvoy)>();
            foreach (var entityId in SubModule.GetKnownNpcIds())
            {
                var entity = EntityManager.GetOrCreateEntityById(entityId);
                if (entity == null || entity.HeroRef == null) continue;

                var unread = AgentManager.GetThreadUnreadCount(entityId, playerId);
                var envoyUnread = AgentManager.GetEnvoyUnreadCount(entityId, playerId);
                var hasEnvoyThread = AgentManager.ReadCorrespondenceThread(entityId, playerId).Count > 0;

                var label = entity.Name;
                if (!string.IsNullOrEmpty(entity.Title))
                    label += "  " + entity.Title;
                if (hasEnvoyThread)
                    label += "  📨密使";
                if (unread + envoyUnread > 0)
                    label += $"  · {unread + envoyUnread} 条未读";

                var lastWrite = DateTime.MinValue;
                try
                {
                    var threadPath = AgentManager.GetChatLogPathFor(entityId, playerId);
                    if (threadPath != null && File.Exists(threadPath))
                        lastWrite = File.GetLastWriteTimeUtc(threadPath);
                    var corrPath = AgentManager.GetCorrespondencePathFor(entityId, playerId);
                    if (corrPath != null && File.Exists(corrPath))
                    {
                        var corrWrite = File.GetLastWriteTimeUtc(corrPath);
                        if (corrWrite > lastWrite) lastWrite = corrWrite;
                    }
                }
                catch { }

                // 有待回复的密使（对方最新发来）→ 打开密使往来窗口回复；否则走正常书信窗口
                var pendingEnvoy = envoyUnread > 0;
                rows.Add((label, entity.HeroRef, unread + envoyUnread, lastWrite, pendingEnvoy));
            }

            // 排序：未读优先 → 线程最近活跃优先
            rows = rows
                .OrderByDescending(r => r.unread > 0)
                .ThenByDescending(r => r.lastWrite)
                .ToList();

            foreach (var row in rows)
                _vm.AddEntry(row.label, row.hero, row.pendingEnvoy);

            if (_vm.Messages.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[AI编年史] 你还没有往来过任何领主。", Colors.Yellow));
                _parentScreen = null;
                _vm = null;
                return;
            }

            _vm.OnSelectNpc = (hero, isEnvoy) =>
            {
                if (hero == null) return;
                Close();
                var player = Hero.MainHero;
                if (player != null)
                    EntityManager.ActivateInteraction(hero, player);
                if (isEnvoy)
                    AIChatScreen.RequestOpenEnvoy(hero);
                else
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
