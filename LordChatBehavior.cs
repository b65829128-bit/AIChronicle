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

        public List<string> KnownNpcIds => _knownNpcIds;

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
            if (!_knownNpcIds.Contains(entity.Id))
                _knownNpcIds.Add(entity.Id);

            AIChatScreen.RequestOpen(hero);
        }
    }
}
