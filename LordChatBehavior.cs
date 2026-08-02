using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace MyFirstMod
{
    public class LordChatBehavior : CampaignBehaviorBase
    {
        private string? _campaignId;
        private List<string> _knownNpcIds = new();
        private readonly object _knownLock = new();

        public List<string> KnownNpcIds
        {
            get { lock (_knownLock) return new List<string>(_knownNpcIds); }
        }

        /// <summary>线程安全地登记一个已知 NPC（去重）。主线程（对话/写信工具）与后台信件送达都可调用。</summary>
        public void AddKnownNpc(string entityId)
        {
            if (string.IsNullOrEmpty(entityId)) return;
            lock (_knownLock)
            {
                if (!_knownNpcIds.Contains(entityId))
                    _knownNpcIds.Add(entityId);
            }
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(
                this, new Action<CampaignGameStarter>(OnSessionLaunched));
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("myfirstmod_campaign_id", ref _campaignId);
            dataStore.SyncData("myfirstmod_known_npcs", ref _knownNpcIds);
        }

        public string GetOrCreateCampaignId()
        {
            if (string.IsNullOrEmpty(_campaignId))
            {
                _campaignId = "Campaign_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            }
            return _campaignId!;
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            PromptManager.StartCampaign(GetOrCreateCampaignId());

            starter.AddPlayerLine(
                "myfirstmod_ai_chat_dialog",
                "hero_main_options",
                "myfirstmod_ai_chat_dialog_output",
                "{=MFM_chat_option}【AI 聊天】",
                new ConversationSentence.OnConditionDelegate(ChatDialogCondition),
                new ConversationSentence.OnConsequenceDelegate(ChatDialogConsequence),
                100,
                null,
                null);

            starter.AddDialogLine(
                "myfirstmod_ai_chat_dialog_done",
                "myfirstmod_ai_chat_dialog_output",
                "hero_main_options",
                "{=MFM_chat_done}（领主等待你开口……）",
                null,
                null);

            // 被强敌擒获（投降/应战）场景也提供 AI 聊天：玩家可谈判，agent 可用 let_go 放行。
            // 节点 player_responds_to_surrender_demand 是投降/应战/买路的选择节点——原版此处无对话入口。
            starter.AddPlayerLine(
                "myfirstmod_ai_chat_dialog_surrender",
                "player_responds_to_surrender_demand",
                "myfirstmod_ai_chat_dialog_surrender_output",
                "{=MFM_chat_option_surrender}【AI 聊天】",
                new ConversationSentence.OnConditionDelegate(ChatDialogCondition),
                new ConversationSentence.OnConsequenceDelegate(ChatDialogConsequence),
                100,
                null,
                null);

            starter.AddDialogLine(
                "myfirstmod_ai_chat_dialog_surrender_done",
                "myfirstmod_ai_chat_dialog_surrender_output",
                "player_responds_to_surrender_demand",
                "{=MFM_chat_done_surrender}（领主等待你开口……）",
                null,
                null);
        }

        private bool ChatDialogCondition()
        {
            if (string.IsNullOrWhiteSpace(MySettings.Instance!.ApiKey))
                return false;
            return Hero.OneToOneConversationHero != null;
        }

        private void ChatDialogConsequence()
        {
            var hero = Hero.OneToOneConversationHero;
            if (hero == null) return;

            var entity = EntityManager.GetOrCreateEntity(hero);
            AddKnownNpc(entity.Id);

            AIChatScreen.RequestOpen(hero);
        }
    }
}
