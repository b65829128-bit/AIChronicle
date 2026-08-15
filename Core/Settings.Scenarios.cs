using System;
using MCM.Abstractions;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using TaleWorlds.Library;

namespace AIChronicle
{
    internal sealed partial class MySettings : AttributeGlobalSettings<MySettings>
{
        private string _chatApiUrl = "";

        [SettingPropertyText("URL", Order = 1, RequireRestart = false,
            HintText = "本场景专属 API 地址。留空则使用「连接设置（兜底）」中的地址。")]
        [SettingPropertyGroup("对话与书信场景", GroupOrder = 1)]
        public string ChatApiUrl
        {
            get => _chatApiUrl;
            set
            {
                if (_chatApiUrl != value)
                {
                    _chatApiUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _chatModel = "";

        [SettingPropertyText("模型", Order = 2, RequireRestart = false,
            HintText = "本场景专属模型名称。留空则使用「连接设置（兜底）」中的模型。")]
        [SettingPropertyGroup("对话与书信场景", GroupOrder = 1)]
        public string ChatModel
        {
            get => _chatModel;
            set
            {
                if (_chatModel != value)
                {
                    _chatModel = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _chatApiKey = "";

        [SettingPropertyText("API 密钥", Order = 3, RequireRestart = false,
            HintText = "本场景专属 API 密钥。留空则使用「连接设置（兜底）」中的密钥。")]
        [SettingPropertyGroup("对话与书信场景", GroupOrder = 1)]
        public string ChatApiKey
        {
            get => _chatApiKey;
            set
            {
                if (_chatApiKey != value)
                {
                    _chatApiKey = value;
                    OnPropertyChanged();
                }
            }
        }

        private MCM.Common.Dropdown<string> _chatProviderType = new(new[] { "跟随兜底", "OpenAI 兼容", "DeepSeek", "GLM", "MiMo", "Qwen（百炼）" }, 0);

        [SettingPropertyDropdown("接口类型", Order = 4, RequireRestart = false,
            HintText = "本场景接口方言。跟随兜底 = 用全局「接口类型」；其余为显式覆盖（例如史官用 DeepSeek、其余用便宜模型）。")]
        [SettingPropertyGroup("对话与书信场景", GroupOrder = 1)]
        public MCM.Common.Dropdown<string> ChatProviderType
        {
            get => _chatProviderType;
            set
            {
                if (_chatProviderType != value)
                {
                    _chatProviderType = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyButton("测试", Content = "测试此场景", Order = 5,
            RequireRestart = false, HintText = "用本场景生效配置（留空字段回退到兜底）测试 API 连通性与 function calling 支持。")]
        [SettingPropertyGroup("对话与书信场景", GroupOrder = 1)]
        public Action ChatTestConnection { get; set; } = () => { _ = AIChatClient.TestConnection("conversation"); };

        private string _diplomacyApiUrl = "";

        [SettingPropertyText("URL", Order = 1, RequireRestart = false,
            HintText = "本场景专属 API 地址。留空则使用「连接设置（兜底）」中的地址。")]
        [SettingPropertyGroup("政务外交场景", GroupOrder = 2)]
        public string DiplomacyApiUrl
        {
            get => _diplomacyApiUrl;
            set
            {
                if (_diplomacyApiUrl != value)
                {
                    _diplomacyApiUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _diplomacyModel = "";

        [SettingPropertyText("模型", Order = 2, RequireRestart = false,
            HintText = "本场景专属模型名称。留空则使用「连接设置（兜底）」中的模型。")]
        [SettingPropertyGroup("政务外交场景", GroupOrder = 2)]
        public string DiplomacyModel
        {
            get => _diplomacyModel;
            set
            {
                if (_diplomacyModel != value)
                {
                    _diplomacyModel = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _diplomacyApiKey = "";

        [SettingPropertyText("API 密钥", Order = 3, RequireRestart = false,
            HintText = "本场景专属 API 密钥。留空则使用「连接设置（兜底）」中的密钥。")]
        [SettingPropertyGroup("政务外交场景", GroupOrder = 2)]
        public string DiplomacyApiKey
        {
            get => _diplomacyApiKey;
            set
            {
                if (_diplomacyApiKey != value)
                {
                    _diplomacyApiKey = value;
                    OnPropertyChanged();
                }
            }
        }

        private MCM.Common.Dropdown<string> _diplomacyProviderType = new(new[] { "跟随兜底", "OpenAI 兼容", "DeepSeek", "GLM", "MiMo", "Qwen（百炼）" }, 0);

        [SettingPropertyDropdown("接口类型", Order = 4, RequireRestart = false,
            HintText = "本场景接口方言。跟随兜底 = 用全局「接口类型」；其余为显式覆盖。")]
        [SettingPropertyGroup("政务外交场景", GroupOrder = 2)]
        public MCM.Common.Dropdown<string> DiplomacyProviderType
        {
            get => _diplomacyProviderType;
            set
            {
                if (_diplomacyProviderType != value)
                {
                    _diplomacyProviderType = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyButton("测试", Content = "测试此场景", Order = 5,
            RequireRestart = false, HintText = "用本场景生效配置（留空字段回退到兜底）测试 API 连通性与 function calling 支持。")]
        [SettingPropertyGroup("政务外交场景", GroupOrder = 2)]
        public Action DiplomacyTestConnection { get; set; } = () => { _ = AIChatClient.TestConnection("diplomacy"); };

        private string _kingConsultApiUrl = "";

        [SettingPropertyText("URL", Order = 1, RequireRestart = false,
            HintText = "本场景专属 API 地址。留空则使用「连接设置（兜底）」中的地址。")]
        [SettingPropertyGroup("外交问询场景", GroupOrder = 3)]
        public string KingConsultApiUrl
        {
            get => _kingConsultApiUrl;
            set
            {
                if (_kingConsultApiUrl != value)
                {
                    _kingConsultApiUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _kingConsultModel = "";

        [SettingPropertyText("模型", Order = 2, RequireRestart = false,
            HintText = "本场景专属模型名称。留空则使用「连接设置（兜底）」中的模型。")]
        [SettingPropertyGroup("外交问询场景", GroupOrder = 3)]
        public string KingConsultModel
        {
            get => _kingConsultModel;
            set
            {
                if (_kingConsultModel != value)
                {
                    _kingConsultModel = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _kingConsultApiKey = "";

        [SettingPropertyText("API 密钥", Order = 3, RequireRestart = false,
            HintText = "本场景专属 API 密钥。留空则使用「连接设置（兜底）」中的密钥。")]
        [SettingPropertyGroup("外交问询场景", GroupOrder = 3)]
        public string KingConsultApiKey
        {
            get => _kingConsultApiKey;
            set
            {
                if (_kingConsultApiKey != value)
                {
                    _kingConsultApiKey = value;
                    OnPropertyChanged();
                }
            }
        }

        private MCM.Common.Dropdown<string> _kingConsultProviderType = new(new[] { "跟随兜底", "OpenAI 兼容", "DeepSeek", "GLM", "MiMo", "Qwen（百炼）" }, 0);

        [SettingPropertyDropdown("接口类型", Order = 4, RequireRestart = false,
            HintText = "本场景接口方言。跟随兜底 = 用全局「接口类型」；其余为显式覆盖。")]
        [SettingPropertyGroup("外交问询场景", GroupOrder = 3)]
        public MCM.Common.Dropdown<string> KingConsultProviderType
        {
            get => _kingConsultProviderType;
            set
            {
                if (_kingConsultProviderType != value)
                {
                    _kingConsultProviderType = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyButton("测试", Content = "测试此场景", Order = 5,
            RequireRestart = false, HintText = "用本场景生效配置（留空字段回退到兜底）测试 API 连通性与 function calling 支持。")]
        [SettingPropertyGroup("外交问询场景", GroupOrder = 3)]
        public Action KingConsultTestConnection { get; set; } = () => { _ = AIChatClient.TestConnection("king_consult"); };

        private string _fiefReviewApiUrl = "";

        [SettingPropertyText("URL", Order = 1, RequireRestart = false,
            HintText = "本场景专属 API 地址。留空则使用「连接设置（兜底）」中的地址。")]
        [SettingPropertyGroup("封地审视场景", GroupOrder = 4)]
        public string FiefReviewApiUrl
        {
            get => _fiefReviewApiUrl;
            set
            {
                if (_fiefReviewApiUrl != value)
                {
                    _fiefReviewApiUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _fiefReviewModel = "";

        [SettingPropertyText("模型", Order = 2, RequireRestart = false,
            HintText = "本场景专属模型名称。留空则使用「连接设置（兜底）」中的模型。")]
        [SettingPropertyGroup("封地审视场景", GroupOrder = 4)]
        public string FiefReviewModel
        {
            get => _fiefReviewModel;
            set
            {
                if (_fiefReviewModel != value)
                {
                    _fiefReviewModel = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _fiefReviewApiKey = "";

        [SettingPropertyText("API 密钥", Order = 3, RequireRestart = false,
            HintText = "本场景专属 API 密钥。留空则使用「连接设置（兜底）」中的密钥。")]
        [SettingPropertyGroup("封地审视场景", GroupOrder = 4)]
        public string FiefReviewApiKey
        {
            get => _fiefReviewApiKey;
            set
            {
                if (_fiefReviewApiKey != value)
                {
                    _fiefReviewApiKey = value;
                    OnPropertyChanged();
                }
            }
        }

        private MCM.Common.Dropdown<string> _fiefReviewProviderType = new(new[] { "跟随兜底", "OpenAI 兼容", "DeepSeek", "GLM", "MiMo", "Qwen（百炼）" }, 0);

        [SettingPropertyDropdown("接口类型", Order = 4, RequireRestart = false,
            HintText = "本场景接口方言。跟随兜底 = 用全局「接口类型」；其余为显式覆盖。")]
        [SettingPropertyGroup("封地审视场景", GroupOrder = 4)]
        public MCM.Common.Dropdown<string> FiefReviewProviderType
        {
            get => _fiefReviewProviderType;
            set
            {
                if (_fiefReviewProviderType != value)
                {
                    _fiefReviewProviderType = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyButton("测试", Content = "测试此场景", Order = 5,
            RequireRestart = false, HintText = "用本场景生效配置（留空字段回退到兜底）测试 API 连通性与 function calling 支持。")]
        [SettingPropertyGroup("封地审视场景", GroupOrder = 4)]
        public Action FiefReviewTestConnection { get; set; } = () => { _ = AIChatClient.TestConnection("fief_review"); };

        private string _advisoryApiUrl = "";

        [SettingPropertyText("URL", Order = 1, RequireRestart = false,
            HintText = "本场景专属 API 地址。留空则使用「连接设置（兜底）」中的地址。")]
        [SettingPropertyGroup("封臣谏言场景", GroupOrder = 5)]
        public string AdvisoryApiUrl
        {
            get => _advisoryApiUrl;
            set
            {
                if (_advisoryApiUrl != value)
                {
                    _advisoryApiUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _advisoryModel = "";

        [SettingPropertyText("模型", Order = 2, RequireRestart = false,
            HintText = "本场景专属模型名称。留空则使用「连接设置（兜底）」中的模型。")]
        [SettingPropertyGroup("封臣谏言场景", GroupOrder = 5)]
        public string AdvisoryModel
        {
            get => _advisoryModel;
            set
            {
                if (_advisoryModel != value)
                {
                    _advisoryModel = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _advisoryApiKey = "";

        [SettingPropertyText("API 密钥", Order = 3, RequireRestart = false,
            HintText = "本场景专属 API 密钥。留空则使用「连接设置（兜底）」中的密钥。")]
        [SettingPropertyGroup("封臣谏言场景", GroupOrder = 5)]
        public string AdvisoryApiKey
        {
            get => _advisoryApiKey;
            set
            {
                if (_advisoryApiKey != value)
                {
                    _advisoryApiKey = value;
                    OnPropertyChanged();
                }
            }
        }

        private MCM.Common.Dropdown<string> _advisoryProviderType = new(new[] { "跟随兜底", "OpenAI 兼容", "DeepSeek", "GLM", "MiMo", "Qwen（百炼）" }, 0);

        [SettingPropertyDropdown("接口类型", Order = 4, RequireRestart = false,
            HintText = "本场景接口方言。跟随兜底 = 用全局「接口类型」；其余为显式覆盖。")]
        [SettingPropertyGroup("封臣谏言场景", GroupOrder = 5)]
        public MCM.Common.Dropdown<string> AdvisoryProviderType
        {
            get => _advisoryProviderType;
            set
            {
                if (_advisoryProviderType != value)
                {
                    _advisoryProviderType = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyButton("测试", Content = "测试此场景", Order = 5,
            RequireRestart = false, HintText = "用本场景生效配置（留空字段回退到兜底）测试 API 连通性与 function calling 支持。")]
        [SettingPropertyGroup("封臣谏言场景", GroupOrder = 5)]
        public Action AdvisoryTestConnection { get; set; } = () => { _ = AIChatClient.TestConnection("advisory"); };

        private string _clanReplenishmentApiUrl = "";

        [SettingPropertyText("URL", Order = 1, RequireRestart = false,
            HintText = "本场景专属 API 地址。留空则使用「连接设置（兜底）」中的地址。")]
        [SettingPropertyGroup("天意建族场景", GroupOrder = 6)]
        public string ClanReplenishmentApiUrl
        {
            get => _clanReplenishmentApiUrl;
            set
            {
                if (_clanReplenishmentApiUrl != value)
                {
                    _clanReplenishmentApiUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _clanReplenishmentModel = "";

        [SettingPropertyText("模型", Order = 2, RequireRestart = false,
            HintText = "本场景专属模型名称。留空则使用「连接设置（兜底）」中的模型。")]
        [SettingPropertyGroup("天意建族场景", GroupOrder = 6)]
        public string ClanReplenishmentModel
        {
            get => _clanReplenishmentModel;
            set
            {
                if (_clanReplenishmentModel != value)
                {
                    _clanReplenishmentModel = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _clanReplenishmentApiKey = "";

        [SettingPropertyText("API 密钥", Order = 3, RequireRestart = false,
            HintText = "本场景专属 API 密钥。留空则使用「连接设置（兜底）」中的密钥。")]
        [SettingPropertyGroup("天意建族场景", GroupOrder = 6)]
        public string ClanReplenishmentApiKey
        {
            get => _clanReplenishmentApiKey;
            set
            {
                if (_clanReplenishmentApiKey != value)
                {
                    _clanReplenishmentApiKey = value;
                    OnPropertyChanged();
                }
            }
        }

        private MCM.Common.Dropdown<string> _clanReplenishmentProviderType = new(new[] { "跟随兜底", "OpenAI 兼容", "DeepSeek", "GLM", "MiMo", "Qwen（百炼）" }, 0);

        [SettingPropertyDropdown("接口类型", Order = 4, RequireRestart = false,
            HintText = "本场景接口方言。跟随兜底 = 用全局「接口类型」；其余为显式覆盖。")]
        [SettingPropertyGroup("天意建族场景", GroupOrder = 6)]
        public MCM.Common.Dropdown<string> ClanReplenishmentProviderType
        {
            get => _clanReplenishmentProviderType;
            set
            {
                if (_clanReplenishmentProviderType != value)
                {
                    _clanReplenishmentProviderType = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyButton("测试", Content = "测试此场景", Order = 5,
            RequireRestart = false, HintText = "用本场景生效配置（留空字段回退到兜底）测试 API 连通性与 function calling 支持。")]
        [SettingPropertyGroup("天意建族场景", GroupOrder = 6)]
        public Action ClanReplenishmentTestConnection { get; set; } = () => { _ = AIChatClient.TestConnection("clan_replenishment"); };

        private string _historianApiUrl = "";

        [SettingPropertyText("URL", Order = 1, RequireRestart = false,
            HintText = "本场景专属 API 地址。留空则使用「连接设置（兜底）」中的地址。")]
        [SettingPropertyGroup("史官场景", GroupOrder = 7)]
        public string HistorianApiUrl
        {
            get => _historianApiUrl;
            set
            {
                if (_historianApiUrl != value)
                {
                    _historianApiUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _historianModel = "";

        [SettingPropertyText("模型", Order = 2, RequireRestart = false,
            HintText = "本场景专属模型名称。留空则使用「连接设置（兜底）」中的模型。")]
        [SettingPropertyGroup("史官场景", GroupOrder = 7)]
        public string HistorianModel
        {
            get => _historianModel;
            set
            {
                if (_historianModel != value)
                {
                    _historianModel = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _historianApiKey = "";

        [SettingPropertyText("API 密钥", Order = 3, RequireRestart = false,
            HintText = "本场景专属 API 密钥。留空则使用「连接设置（兜底）」中的密钥。")]
        [SettingPropertyGroup("史官场景", GroupOrder = 7)]
        public string HistorianApiKey
        {
            get => _historianApiKey;
            set
            {
                if (_historianApiKey != value)
                {
                    _historianApiKey = value;
                    OnPropertyChanged();
                }
            }
        }

        private MCM.Common.Dropdown<string> _historianProviderType = new(new[] { "跟随兜底", "OpenAI 兼容", "DeepSeek", "GLM", "MiMo", "Qwen（百炼）" }, 0);

        [SettingPropertyDropdown("接口类型", Order = 4, RequireRestart = false,
            HintText = "本场景接口方言。跟随兜底 = 用全局「接口类型」；其余为显式覆盖（史官文笔核心，可单独配 DeepSeek）。")]
        [SettingPropertyGroup("史官场景", GroupOrder = 7)]
        public MCM.Common.Dropdown<string> HistorianProviderType
        {
            get => _historianProviderType;
            set
            {
                if (_historianProviderType != value)
                {
                    _historianProviderType = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyButton("测试", Content = "测试此场景", Order = 5,
            RequireRestart = false, HintText = "用本场景生效配置（留空字段回退到兜底）测试 API 连通性与 function calling 支持。")]
        [SettingPropertyGroup("史官场景", GroupOrder = 7)]
        public Action HistorianTestConnection { get; set; } = () => { _ = AIChatClient.TestConnection("historian"); };

        private string _consolidationApiUrl = "";

        [SettingPropertyText("URL", Order = 1, RequireRestart = false,
            HintText = "本场景专属 API 地址。留空则使用「连接设置（兜底）」中的地址。")]
        [SettingPropertyGroup("记忆巩固场景", GroupOrder = 8)]
        public string ConsolidationApiUrl
        {
            get => _consolidationApiUrl;
            set
            {
                if (_consolidationApiUrl != value)
                {
                    _consolidationApiUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _consolidationModel = "";

        [SettingPropertyText("模型", Order = 2, RequireRestart = false,
            HintText = "本场景专属模型名称。留空则使用「连接设置（兜底）」中的模型。")]
        [SettingPropertyGroup("记忆巩固场景", GroupOrder = 8)]
        public string ConsolidationModel
        {
            get => _consolidationModel;
            set
            {
                if (_consolidationModel != value)
                {
                    _consolidationModel = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _consolidationApiKey = "";

        [SettingPropertyText("API 密钥", Order = 3, RequireRestart = false,
            HintText = "本场景专属 API 密钥。留空则使用「连接设置（兜底）」中的密钥。")]
        [SettingPropertyGroup("记忆巩固场景", GroupOrder = 8)]
        public string ConsolidationApiKey
        {
            get => _consolidationApiKey;
            set
            {
                if (_consolidationApiKey != value)
                {
                    _consolidationApiKey = value;
                    OnPropertyChanged();
                }
            }
        }

        private MCM.Common.Dropdown<string> _consolidationProviderType = new(new[] { "跟随兜底", "OpenAI 兼容", "DeepSeek", "GLM", "MiMo", "Qwen（百炼）" }, 0);

        [SettingPropertyDropdown("接口类型", Order = 4, RequireRestart = false,
            HintText = "本场景接口方言。跟随兜底 = 用全局「接口类型」；其余为显式覆盖。")]
        [SettingPropertyGroup("记忆巩固场景", GroupOrder = 8)]
        public MCM.Common.Dropdown<string> ConsolidationProviderType
        {
            get => _consolidationProviderType;
            set
            {
                if (_consolidationProviderType != value)
                {
                    _consolidationProviderType = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyButton("测试", Content = "测试此场景", Order = 5,
            RequireRestart = false, HintText = "用本场景生效配置（留空字段回退到兜底）测试 API 连通性与 function calling 支持。")]
        [SettingPropertyGroup("记忆巩固场景", GroupOrder = 8)]
        public Action ConsolidationTestConnection { get; set; } = () => { _ = AIChatClient.TestConnection("consolidation"); };

        private string _checkInApiUrl = "";

        [SettingPropertyText("URL", Order = 1, RequireRestart = false,
            HintText = "本场景专属 API 地址。留空则使用「连接设置（兜底）」中的地址。")]
        [SettingPropertyGroup("签到场景", GroupOrder = 9)]
        public string CheckInApiUrl
        {
            get => _checkInApiUrl;
            set
            {
                if (_checkInApiUrl != value)
                {
                    _checkInApiUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _checkInModel = "";

        [SettingPropertyText("模型", Order = 2, RequireRestart = false,
            HintText = "本场景专属模型名称。留空则使用「连接设置（兜底）」中的模型。")]
        [SettingPropertyGroup("签到场景", GroupOrder = 9)]
        public string CheckInModel
        {
            get => _checkInModel;
            set
            {
                if (_checkInModel != value)
                {
                    _checkInModel = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _checkInApiKey = "";

        [SettingPropertyText("API 密钥", Order = 3, RequireRestart = false,
            HintText = "本场景专属 API 密钥。留空则使用「连接设置（兜底）」中的密钥。")]
        [SettingPropertyGroup("签到场景", GroupOrder = 9)]
        public string CheckInApiKey
        {
            get => _checkInApiKey;
            set
            {
                if (_checkInApiKey != value)
                {
                    _checkInApiKey = value;
                    OnPropertyChanged();
                }
            }
        }

        private MCM.Common.Dropdown<string> _checkInProviderType = new(new[] { "跟随兜底", "OpenAI 兼容", "DeepSeek", "GLM", "MiMo", "Qwen（百炼）" }, 0);

        [SettingPropertyDropdown("接口类型", Order = 4, RequireRestart = false,
            HintText = "本场景接口方言。跟随兜底 = 用全局「接口类型」；其余为显式覆盖。")]
        [SettingPropertyGroup("签到场景", GroupOrder = 9)]
        public MCM.Common.Dropdown<string> CheckInProviderType
        {
            get => _checkInProviderType;
            set
            {
                if (_checkInProviderType != value)
                {
                    _checkInProviderType = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyButton("测试", Content = "测试此场景", Order = 5,
            RequireRestart = false, HintText = "用本场景生效配置（留空字段回退到兜底）测试 API 连通性与 function calling 支持。")]
        [SettingPropertyGroup("签到场景", GroupOrder = 9)]
        public Action CheckInTestConnection { get; set; } = () => { _ = AIChatClient.TestConnection("chat"); };

        private string _chanceryApiUrl = "";

        [SettingPropertyText("URL", Order = 1, RequireRestart = false,
            HintText = "本场景专属 API 地址。留空则使用「连接设置（兜底）」中的地址。")]
        [SettingPropertyGroup("秘书处场景", GroupOrder = 10)]
        public string ChanceryApiUrl
        {
            get => _chanceryApiUrl;
            set
            {
                if (_chanceryApiUrl != value)
                {
                    _chanceryApiUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _chanceryModel = "";

        [SettingPropertyText("模型", Order = 2, RequireRestart = false,
            HintText = "本场景专属模型名称。留空则使用「连接设置（兜底）」中的模型。")]
        [SettingPropertyGroup("秘书处场景", GroupOrder = 10)]
        public string ChanceryModel
        {
            get => _chanceryModel;
            set
            {
                if (_chanceryModel != value)
                {
                    _chanceryModel = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _chanceryApiKey = "";

        [SettingPropertyText("API 密钥", Order = 3, RequireRestart = false,
            HintText = "本场景专属 API 密钥。留空则使用「连接设置（兜底）」中的密钥。")]
        [SettingPropertyGroup("秘书处场景", GroupOrder = 10)]
        public string ChanceryApiKey
        {
            get => _chanceryApiKey;
            set
            {
                if (_chanceryApiKey != value)
                {
                    _chanceryApiKey = value;
                    OnPropertyChanged();
                }
            }
        }

        private MCM.Common.Dropdown<string> _chanceryProviderType = new(new[] { "跟随兜底", "OpenAI 兼容", "DeepSeek", "GLM", "MiMo", "Qwen（百炼）" }, 0);

        [SettingPropertyDropdown("接口类型", Order = 4, RequireRestart = false,
            HintText = "本场景接口方言。跟随兜底 = 用全局「接口类型」；其余为显式覆盖。")]
        [SettingPropertyGroup("秘书处场景", GroupOrder = 10)]
        public MCM.Common.Dropdown<string> ChanceryProviderType
        {
            get => _chanceryProviderType;
            set
            {
                if (_chanceryProviderType != value)
                {
                    _chanceryProviderType = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyButton("测试", Content = "测试此场景", Order = 5,
            RequireRestart = false, HintText = "用本场景生效配置（留空字段回退到兜底）测试 API 连通性与 function calling 支持。")]
        [SettingPropertyGroup("秘书处场景", GroupOrder = 10)]
        public Action ChanceryTestConnection { get; set; } = () => { _ = AIChatClient.TestConnection("chancery"); };
    }
}
