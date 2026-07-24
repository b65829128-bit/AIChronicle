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

        private int _maxAgentRounds = 5;

        [SettingPropertyInteger("Agent 最大轮次", 1, 10, Order = 4, RequireRestart = false,
            HintText = "Agent 工具调用循环的最大轮数。复杂 NPC 可能需要 4-5 轮。")]
        [SettingPropertyGroup("游戏设置")]
        public int MaxAgentRounds
        {
            get => _maxAgentRounds;
            set
            {
                if (_maxAgentRounds != value)
                {
                    _maxAgentRounds = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _unlimitedAgentRounds = false;

        [SettingPropertyBool("不限制 Agent 轮次", Order = 5, RequireRestart = false,
            HintText = "开启后 Agent 的工具调用没有轮次上限（直到模型自然停止）。\n可能导致 token 消耗大幅增加，仅在复杂场景下开启。")]
        [SettingPropertyGroup("游戏设置")]
        public bool UnlimitedAgentRounds
        {
            get => _unlimitedAgentRounds;
            set
            {
                if (_unlimitedAgentRounds != value)
                {
                    _unlimitedAgentRounds = value;
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

        private bool _useWorldInfo = true;

        [SettingPropertyBool("注入世界背景", Order = 6, RequireRestart = false,
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

        private int _maxTokens = 500;

        [SettingPropertyInteger("最大 Token 数", 50, 4096, Order = 5, RequireRestart = false,
            HintText = "AI 单次回复的最大 token 数。")]
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
    }
}
