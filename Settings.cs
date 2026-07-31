using System;
using MCM.Abstractions;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using TaleWorlds.Library;

namespace MyFirstMod
{
    internal sealed class MySettings : AttributeGlobalSettings<MySettings>
    {
        public override string Id => "MyFirstMod_v1";
        public override string DisplayName => "MyFirstMod — AI 聊天";
        public override string FolderName => "MyFirstMod";
        public override string FormatType => "json";

        private string _apiUrl = "https://api.deepseek.com/v1/chat/completions";

        [SettingPropertyText("API 地址", Order = 1, RequireRestart = false,
            HintText = "LLM API 端点地址。默认使用 DeepSeek。")]
        [SettingPropertyGroup("连接设置")]
        public string ApiUrl
        {
            get => _apiUrl;
            set
            {
                if (_apiUrl != value)
                {
                    _apiUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _model = "deepseek-chat";

        [SettingPropertyText("模型名称", Order = 2, RequireRestart = false,
            HintText = "模型名称，例如 deepseek-chat、gpt-4o。")]
        [SettingPropertyGroup("连接设置")]
        public string Model
        {
            get => _model;
            set
            {
                if (_model != value)
                {
                    _model = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _apiKey = "";

        [SettingPropertyText("API 密钥", Order = 3, RequireRestart = false,
            HintText = "LLM 服务的 API 密钥。")]
        [SettingPropertyGroup("连接设置")]
        public string ApiKey
        {
            get => _apiKey;
            set
            {
                if (_apiKey != value)
                {
                    _apiKey = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _chatFontSize = 24;
        private int _chatSenderFontSize = 22;
        private int _chatTimeFontSize = 22;

        [SettingPropertyInteger("对话字体大小", 8, 40, Order = 0, RequireRestart = false,
            HintText = "聊天窗口中对话内容的字体大小。")]
        [SettingPropertyGroup("聊天界面")]
        public int ChatFontSize
        {
            get => _chatFontSize;
            set
            {
                if (_chatFontSize != value)
                {
                    _chatFontSize = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyInteger("角色名字体大小", 6, 32, Order = 1, RequireRestart = false,
            HintText = "聊天窗口中角色名称的字体大小。")]
        [SettingPropertyGroup("聊天界面")]
        public int ChatSenderFontSize
        {
            get => _chatSenderFontSize;
            set
            {
                if (_chatSenderFontSize != value)
                {
                    _chatSenderFontSize = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyInteger("时间戳字体大小", 4, 24, Order = 2, RequireRestart = false,
            HintText = "聊天窗口中时间戳的字体大小。")]
        [SettingPropertyGroup("聊天界面")]
        public int ChatTimeFontSize
        {
            get => _chatTimeFontSize;
            set
            {
                if (_chatTimeFontSize != value)
                {
                    _chatTimeFontSize = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _messageSpacing = 60;
        private int _contentIndent = 15;
        private int _senderTopGap = 6;
        private int _contentTopGap = 6;

        [SettingPropertyInteger("消息间距", 0, 80, Order = 3, RequireRestart = false,
            HintText = "两条消息之间的垂直间距。")]
        [SettingPropertyGroup("聊天界面")]
        public int MessageSpacing
        {
            get => _messageSpacing;
            set
            {
                if (_messageSpacing != value)
                {
                    _messageSpacing = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyInteger("对话缩进", 0, 60, Order = 4, RequireRestart = false,
            HintText = "对话内容相对于角色名的左侧缩进。")]
        [SettingPropertyGroup("聊天界面")]
        public int ContentIndent
        {
            get => _contentIndent;
            set
            {
                if (_contentIndent != value)
                {
                    _contentIndent = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyInteger("角色名上间距", 0, 30, Order = 5, RequireRestart = false,
            HintText = "角色名与时间戳之间的间距。")]
        [SettingPropertyGroup("聊天界面")]
        public int SenderTopGap
        {
            get => _senderTopGap;
            set
            {
                if (_senderTopGap != value)
                {
                    _senderTopGap = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyInteger("对话上间距", 0, 30, Order = 6, RequireRestart = false,
            HintText = "对话内容与角色名之间的间距。")]
        [SettingPropertyGroup("聊天界面")]
        public int ContentTopGap
        {
            get => _contentTopGap;
            set
            {
                if (_contentTopGap != value)
                {
                    _contentTopGap = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyButton("重置聊天界面", Content = "恢复默认", Order = 7,
            RequireRestart = false, HintText = "将所有聊天界面设置恢复为推荐默认值。")]
        [SettingPropertyGroup("聊天界面")]
        public Action ResetChatLayout { get; set; } = () =>
        {
            var s = MySettings.Instance!;
            s.ChatFontSize = 24;
            s.ChatSenderFontSize = 22;
            s.ChatTimeFontSize = 22;
            s.MessageSpacing = 60;
            s.ContentIndent = 15;
            s.SenderTopGap = 6;
            s.ContentTopGap = 6;
        };

        private int _maxRelationChange = 5;

        [SettingPropertyInteger("最大好感变化", 1, 30, Order = 0, RequireRestart = false,
            HintText = "Agent 调用 change_relation 时，单次好感变化的上限。绝对值不会被超出。")]
        [SettingPropertyGroup("游戏设置")]
        public int MaxRelationChange
        {
            get => _maxRelationChange;
            set
            {
                if (_maxRelationChange != value)
                {
                    _maxRelationChange = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _doubleRenownEnabled = false;

        [SettingPropertyBool("双倍声望", Order = 1, RequireRestart = false,
            HintText = "开启后战斗中获得的声望翻倍。")]
        [SettingPropertyGroup("游戏设置")]
        public bool DoubleRenownEnabled
        {
            get => _doubleRenownEnabled;
            set
            {
                if (_doubleRenownEnabled != value)
                {
                    _doubleRenownEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _independentToolCalling = false;

        [SettingPropertyBool("独立工具调用", Order = 2, RequireRestart = false,
            HintText = "仅在模型消极调用工具时开启。\n开启后角色扮演和工具调用分离为两次 API 请求。\n会增加延迟和 token 消耗，不建议长期开启。")]
        [SettingPropertyGroup("游戏设置")]
        public bool IndependentToolCalling
        {
            get => _independentToolCalling;
            set
            {
                if (_independentToolCalling != value)
                {
                    _independentToolCalling = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _showToolCalls = true;

        [SettingPropertyBool("显示工具调用提示", Order = 3, RequireRestart = false,
            HintText = "在左下角显示 Agent 调用了哪些文件工具（读取/写入记忆等）。")]
        [SettingPropertyGroup("游戏设置")]
        public bool ShowToolCalls
        {
            get => _showToolCalls;
            set
            {
                if (_showToolCalls != value)
                {
                    _showToolCalls = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _chatHistoryLimit = 20;

        [SettingPropertyInteger("聊天历史上限（条）", 5, 100, Order = 5, RequireRestart = false,
            HintText = "保留最近 N 条消息发给 AI。超出的旧消息会被截断，防止 token 爆炸。")]
        [SettingPropertyGroup("游戏设置")]
        public int ChatHistoryLimit
        {
            get => _chatHistoryLimit;
            set
            {
                if (_chatHistoryLimit != value)
                {
                    _chatHistoryLimit = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _maxLetterChainDepth = 5;

        [SettingPropertyInteger("信件级联深度上限", 1, 10, Order = 6, RequireRestart = false,
            HintText = "NPC 间连环写信的最大层数。超过此深度的收信只存档不处理。")]
        [SettingPropertyGroup("游戏设置")]
        public int MaxLetterChainDepth
        {
            get => _maxLetterChainDepth;
            set
            {
                if (_maxLetterChainDepth != value)
                {
                    _maxLetterChainDepth = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _useWorldInfo = true;

        [SettingPropertyBool("注入世界背景", Order = 7, RequireRestart = false,
            HintText = "是否在提示词中加入卡拉迪亚大陆的背景介绍。关闭可节省 token。")]
        [SettingPropertyGroup("游戏设置")]
        public bool UseWorldInfo
        {
            get => _useWorldInfo;
            set
            {
                if (_useWorldInfo != value)
                {
                    _useWorldInfo = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _surroundingsScanRadius = 20;

        [SettingPropertyInteger("环境扫描半径（km）", 5, 200, Order = 8, RequireRestart = false,
            HintText = "query_surroundings 的扫描半径硬上限。Agent 无论请求多大范围，实际都不会超过此值。")]
        [SettingPropertyGroup("游戏设置")]
        public int SurroundingsScanRadius
        {
            get => _surroundingsScanRadius;
            set
            {
                if (_surroundingsScanRadius != value)
                {
                    _surroundingsScanRadius = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _banVanillaDiplomacy = true;

        [SettingPropertyBool("禁止原版外交（Agent 主导）", Order = 9, RequireRestart = false,
            HintText = "开启后原版 AI 不再自动宣战/议和/结盟/贸易，所有外交行为由国王 Agent 决策。\n便于调试和体验完整的 Agent 驱动外交。")]
        [SettingPropertyGroup("游戏设置")]
        public bool BanVanillaDiplomacy
        {
            get => _banVanillaDiplomacy;
            set
            {
                if (_banVanillaDiplomacy != value)
                {
                    _banVanillaDiplomacy = value;
                    OnPropertyChanged();
                }
            }
        }

        private float _diplomacyChancePerDay = 0.1f;

        [SettingPropertyFloatingInteger("外交触发几率/天", 0.01f, 0.5f, Order = 10, RequireRestart = false,
            HintText = "每个国王每天有独立几率触发外交审视。\n0.1=平均十天一次，0.5=平均两天一次。\n默认0.1。")]
        [SettingPropertyGroup("游戏设置")]
        public float DiplomacyChancePerDay
        {
            get => _diplomacyChancePerDay;
            set
            {
                if (Math.Abs(_diplomacyChancePerDay - value) > 0.001f)
                {
                    _diplomacyChancePerDay = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _kingCooldownDays = 3;

        [SettingPropertyInteger("国王冷静期（天）", 1, 30, Order = 13, RequireRestart = false,
            HintText = "国王每次外交激活后的冷却时间。冷却期内不会被定时激活，但收到的外交提案仍会正常触发。\n默认3天。")]
        [SettingPropertyGroup("游戏设置")]
        public int KingCooldownDays
        {
            get => _kingCooldownDays;
            set
            {
                if (_kingCooldownDays != value)
                {
                    _kingCooldownDays = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _chronicleInterval = 1;

        [SettingPropertyInteger("编年史间隔（年）", 1, 10, Order = 11, RequireRestart = false,
            HintText = "史官编纂编年史的间隔。\n1 = 每年编纂，3 = 每三年编纂，以此类推。\n间隔越大 Token 消耗越低，但历史细节越粗糙。")]
        [SettingPropertyGroup("游戏设置")]
        public int ChronicleInterval
        {
            get => _chronicleInterval;
            set
            {
                if (_chronicleInterval != value)
                {
                    _chronicleInterval = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _advisoryEnabled = true;

        [SettingPropertyBool("启用封臣谏言", Order = 13, RequireRestart = false,
            HintText = "开启后每天每王国有概率触发一位封臣进谏（仅氏族领袖，排除玩家和国王）。\n谏言公开写入 World/advisory/，国王外交激活时会阅读。\n关闭后封臣不再进谏。")]
        [SettingPropertyGroup("游戏设置")]
        public bool AdvisoryEnabled
        {
            get => _advisoryEnabled;
            set
            {
                if (_advisoryEnabled != value)
                {
                    _advisoryEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        private float _advisoryProbability = 0.1f;

        [SettingPropertyFloatingInteger("封臣谏言概率/天", 0.01f, 0.5f, Order = 14, RequireRestart = false,
            HintText = "每个王国每天有独立概率触发一位封臣进谏。\n0.1=平均十天一次，0.5=平均两天一次。\n同一封臣不会连续进谏。")]
        [SettingPropertyGroup("游戏设置")]
        public float AdvisoryProbability
        {
            get => _advisoryProbability;
            set
            {
                if (Math.Abs(_advisoryProbability - value) > 0.001f)
                {
                    _advisoryProbability = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _biographyAllNobles = true;

        [SettingPropertyBool("所有贵族立传", Order = 15, RequireRestart = false,
            HintText = "勾选：所有有氏族的贵族死后都立传。\n不勾选：仅氏族领袖和国王死后立传。")]
        [SettingPropertyGroup("游戏设置")]
        public bool BiographyAllNobles
        {
            get => _biographyAllNobles;
            set
            {
                if (_biographyAllNobles != value)
                {
                    _biographyAllNobles = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _chronicleFontSize = 28;

        [SettingPropertyInteger("史书字体大小", 14, 36, Order = 12, RequireRestart = false,
            HintText = "史书 UI 中编年史正文的字体大小。")]
        [SettingPropertyGroup("游戏设置")]
        public int ChronicleFontSize
        {
            get => _chronicleFontSize;
            set
            {
                if (_chronicleFontSize != value)
                {
                    _chronicleFontSize = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _maxTokens = 4096;

        [SettingPropertyInteger("最大 Token 数", 50, 8192, Order = 5, RequireRestart = false,
            HintText = "AI 单次回复的最大 token 数。DeepSeek V4 最高支持 384K 输出。")]
        [SettingPropertyGroup("连接设置")]
        public int MaxTokens
        {
            get => _maxTokens;
            set
            {
                if (_maxTokens != value)
                {
                    _maxTokens = value;
                    OnPropertyChanged();
                }
            }
        }

        private float _temperature = 0.8f;

        [SettingPropertyFloatingInteger("回复创造性 (Temperature)", 0.1f, 2.0f, Order = 6, RequireRestart = false,
            HintText = "AI 回复的随机性。越低越稳定保守，越高越有创造性。")]
        [SettingPropertyGroup("连接设置")]
        public float Temperature
        {
            get => _temperature;
            set
            {
                if (Math.Abs(_temperature - value) > 0.001f)
                {
                    _temperature = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _timeout = 30;

        [SettingPropertyInteger("API 超时（秒）", 10, 120, Order = 7, RequireRestart = false,
            HintText = "API 请求超时时间，网络慢时可调大。")]
        [SettingPropertyGroup("连接设置")]
        public int TimeoutSeconds
        {
            get => _timeout;
            set
            {
                if (_timeout != value)
                {
                    _timeout = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyButton("测试连接", Content = "点击测试", Order = 8,
            RequireRestart = false, HintText = "点击按钮测试 API 是否能连通，包括 function calling 支持检测。")]
        [SettingPropertyGroup("连接设置")]
        public Action TestConnection { get; set; } = () =>
        {
            var settings = MySettings.Instance!;
            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[MyFirstMod] API 密钥为空，请先填写。",
                    Colors.Red));
                return;
            }

            AIChatClient.TestConnection();
        };

        [SettingPropertyButton("强制开始外交", Content = "立即触发", Order = 12,
            RequireRestart = false, HintText = "立即激活所有国王 Agent 进行一轮外交审视，并重置计时器。")]
        [SettingPropertyGroup("游戏设置")]
        public Action ForceDiplomacy { get; set; } = () =>
        {
            AgentScheduler.ForceDiplomacyRound();
        };

        [SettingPropertyButton("强制封臣进谏", Content = "重置计时", Order = 17,
            RequireRestart = false, HintText = "立即清除所有王国的封臣谏言计时器，\n使得下一刻起各王国封臣开始进谏。")]
        [SettingPropertyGroup("游戏设置")]
        public Action ForceAdvisory { get; set; } = () =>
        {
            AgentScheduler.ForceAdvisory();
        };
    }
}
