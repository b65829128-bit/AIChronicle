using System;
using MCM.Abstractions;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace AIChronicle
{
    internal sealed partial class MySettings : AttributeGlobalSettings<MySettings>
{
        public override string Id => "AIChronicle_v1";
        public override string DisplayName => "AI编年史·言出法随";
        public override string FolderName => "AIChronicle";
        public override string FormatType => "json";

        private string _apiUrl = "https://api.deepseek.com/v1/chat/completions";

        [SettingPropertyText("API 地址（兜底）", Order = 1, RequireRestart = false,
            HintText = "LLM API 端点地址。默认使用 DeepSeek。这是全局兜底——各场景留空的字段会回退到这里。")]
        [SettingPropertyGroup("连接设置（兜底）")]
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

        private string _model = "deepseek-v4-flash";

        [SettingPropertyText("模型名称（兜底）", Order = 2, RequireRestart = false,
            HintText = "兜底模型名称，例如 deepseek-v4-flash、gpt-4o。各场景留空的字段会回退到这里。")]
        [SettingPropertyGroup("连接设置（兜底）")]
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

        [SettingPropertyText("API 密钥（兜底）", Order = 3, RequireRestart = false,
            HintText = "LLM 服务的 API 密钥。这是全局兜底——各场景留空的字段会回退到这里。")]
        [SettingPropertyGroup("连接设置（兜底）")]
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

        // ============ 场景专属连接配置 ============
        // 每个场景一组 URL/Model/APIKey（留空=逐字段回退到「连接设置（兜底）」）+ 测试按钮。
        // 懒得配置时只需填全局兜底三件套即可。


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

        private bool _debugLogging = true;

        [SettingPropertyBool("调试日志", Order = 4, RequireRestart = false,
            HintText = "将 LLM 调用摘要、思维链摘录、谏言/信件结果写入战役目录 debug_logs/，便于排查 agent 行为。")]
        [SettingPropertyGroup("游戏设置")]
        public bool DebugLogging
        {
            get => _debugLogging;
            set
            {
                if (_debugLogging != value)
                {
                    _debugLogging = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _maxAgentConcurrency = 5;

        [SettingPropertyInteger("Agent 并发数", 1, 8, Order = 5, RequireRestart = false,
            HintText = "同时运行的 Agent 任务数上限。越大吞吐越高，但工具在主线程串行执行，过大会造成帧卡顿。")]
        [SettingPropertyGroup("游戏设置")]
        public int MaxAgentConcurrency
        {
            get => _maxAgentConcurrency;
            set
            {
                if (_maxAgentConcurrency != value)
                {
                    _maxAgentConcurrency = value;
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

        private bool _useGameRules = true;

        [SettingPropertyBool("注入游戏规则", Order = 8, RequireRestart = false,
            HintText = "是否在提示词中加入卡拉迪亚的实际运转规则（机动/金钱/部队上限/兵种/招募/战争/影响力），让 agent 按游戏机制而非现实经验做决策。关闭可节省 token。")]
        [SettingPropertyGroup("游戏设置")]
        public bool UseGameRules
        {
            get => _useGameRules;
            set
            {
                if (_useGameRules != value)
                {
                    _useGameRules = value;
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

        private float _intelligenceScoutRadiusFraction = 0.2f;

        [SettingPropertyFloatingInteger("情报侦察半径（占地图比例）", 0.05f, 0.5f, Order = 9, RequireRestart = false,
            HintText = "query_party_troops 查看异国部队时，此比例 × 地图实际尺度 = 近距侦察半径（范围内兵力较准，之外模糊）。\n地图尺度用城镇/城堡包围盒实测，全图约 2000-3000 地图单位；0.2 ≈ 500 单位 ≈ 1-2 座城池间距。")]
        [SettingPropertyGroup("游戏设置")]
        public float IntelligenceScoutRadiusFraction
        {
            get => _intelligenceScoutRadiusFraction;
            set
            {
                if (Math.Abs(_intelligenceScoutRadiusFraction - value) > 0.001f)
                {
                    _intelligenceScoutRadiusFraction = value;
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

        private bool _fiefAssignmentByAgent = true;

        [SettingPropertyBool("册封由 Agent 主导", Order = 10, RequireRestart = false,
            HintText = "开启后，AI 攻下的城镇/城堡不再触发原版影响力投票，改由国王 Agent 决定归属（用 gift_fief 赐予合适的家族）。\n玩家亲自攻下的城保留原版选择菜单。关闭则恢复原版投票。")]
        [SettingPropertyGroup("游戏设置")]
        public bool FiefAssignmentByAgent
        {
            get => _fiefAssignmentByAgent;
            set
            {
                if (_fiefAssignmentByAgent != value)
                {
                    _fiefAssignmentByAgent = value;
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

        private bool _executionNoPenalty = true;

        [SettingPropertyBool("处决无惩罚", Order = 16, RequireRestart = false,
            HintText = "开启：处决贵族俘虏不承受任何政治代价——斩首者名誉不降、全图贵族好感不降（玩家与 NPC 均生效）。\n关闭：恢复原版/模组处决惩罚（名誉大降 + 受害者氏族/亲友/同阵营贵族好感大降）。")]
        [SettingPropertyGroup("游戏设置")]
        public bool ExecutionNoPenalty
        {
            get => _executionNoPenalty;
            set
            {
                if (_executionNoPenalty != value)
                {
                    _executionNoPenalty = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _clanReplenishmentEnabled = false;

        [SettingPropertyBool("启用家族补充", Order = 17, RequireRestart = false,
            HintText = "【实验性功能，默认关闭】开启：封臣家族或雇佣兵家族数量低于下限时，激活「天意」agent 补充新的贵族家族，防止大屠杀导致世家凋零、世界崩解。该功能仍不完善，建议先保持关闭。")]
        [SettingPropertyGroup("游戏设置")]
        public bool ClanReplenishmentEnabled
        {
            get => _clanReplenishmentEnabled;
            set
            {
                if (_clanReplenishmentEnabled != value)
                {
                    _clanReplenishmentEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _minVassalClans = 70;

        [SettingPropertyInteger("封臣家族补充阈值", 20, 150, Order = 18, RequireRestart = false,
            HintText = "封臣家族（非雇佣兵、非叛军的贵族氏族）数量低于此值时触发「天意」补充新家族。单位：家族。原版卡拉迪亚约 70 个封臣家族——调低则世界更冷清、家族更稀有，调高则世家更兴旺。")]
        [SettingPropertyGroup("游戏设置")]
        public int MinVassalClans
        {
            get => _minVassalClans;
            set
            {
                if (_minVassalClans != value)
                {
                    _minVassalClans = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _minMercenaryClans = 8;

        [SettingPropertyInteger("雇佣兵家族补充阈值", 2, 30, Order = 19, RequireRestart = false,
            HintText = "雇佣兵家族数量低于此值时触发「天意」补充。单位：家族。原版卡拉迪亚约 8 个雇佣兵家族。")]
        [SettingPropertyGroup("游戏设置")]
        public int MinMercenaryClans
        {
            get => _minMercenaryClans;
            set
            {
                if (_minMercenaryClans != value)
                {
                    _minMercenaryClans = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _memoryConsolidationEnabled = true;

        [SettingPropertyBool("启用记忆巩固", Order = 20, RequireRestart = false,
            HintText = "开启：自我审视（国王政务/封地审视/外交问询）激活前，若日记落后于聊天记录，先跑一次巩固——把最近往来中值得记住的决定/承诺/计策/战略补记进 decisions/diary.txt，避免照着陈旧日记行事。只在日记落后时触发。")]
        [SettingPropertyGroup("游戏设置")]
        public bool MemoryConsolidationEnabled
        {
            get => _memoryConsolidationEnabled;
            set
            {
                if (_memoryConsolidationEnabled != value)
                {
                    _memoryConsolidationEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _maxTokens = 32768;

        [SettingPropertyInteger("最大 Token 数", 50, 65536, Order = 5, RequireRestart = false,
            HintText = "AI 单次回复的最大 token 数。DeepSeek V4 最高支持 384K 输出；默认 32768 对长编年史/长思考都足够，特殊场景可上调至 65536。")]
        [SettingPropertyGroup("连接设置（兜底）")]
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
        [SettingPropertyGroup("连接设置（兜底）")]
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

        private MCM.Common.Dropdown<string> _reasoningEffort = new(new[] { "low", "high", "max" }, 0);

        [SettingPropertyDropdown("思考强度 (reasoning_effort)", Order = 9, RequireRestart = false,
            HintText = "AI 思考强度（DeepSeek reasoning_effort）。这是成本大头：思考按输出价计费（flash 2元/M），默认 high 时每次决策都会产生大量思维链。\nlow=最省 token、决策更直接；high=更周全但更贵；max=最强推理。\n史官固定为 high（文笔核心），不受此设置影响。\n部分模型（非思考模式或不支持该参数的端点）此设置不生效。")]
        [SettingPropertyGroup("连接设置（兜底）")]
        public MCM.Common.Dropdown<string> ReasoningEffort
        {
            get => _reasoningEffort;
            set
            {
                if (_reasoningEffort != value)
                {
                    _reasoningEffort = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _timeout = 30;

        [SettingPropertyInteger("API 超时（秒）", 10, 120, Order = 7, RequireRestart = false,
            HintText = "API 请求超时时间，网络慢时可调大。")]
        [SettingPropertyGroup("连接设置（兜底）")]
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
            RequireRestart = false, HintText = "点击按钮测试兜底连接设置（含 function calling 支持检测）。")]
        [SettingPropertyGroup("连接设置（兜底）")]
        public Action TestConnection { get; set; } = () => { _ = AIChatClient.TestConnection("default"); };

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

        // ============ 语音（TTS） ============

        private bool _ttsEnabled = false;

        [SettingPropertyBool("启用语音朗读（TTS）", Order = 0, RequireRestart = false,
            HintText = "AI 聊天（【AI 聊天】入口）收到回复时用语音朗读。默认关闭。\n使用免费 Edge TTS（微软神经语音，需联网，无需 API Key）。\n书信与秘书处不朗读；女角色用女声、男角色用男声。")]
        [SettingPropertyGroup("语音（TTS）")]
        public bool TtsEnabled
        {
            get => _ttsEnabled;
            set
            {
                if (_ttsEnabled != value)
                {
                    _ttsEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _ttsSpeed = 0;

        [SettingPropertyInteger("语速（%）", -50, 50, Order = 1, RequireRestart = false,
            HintText = "朗读语速偏移。-50 慢一半，+50 快一半，0 为正常。")]
        [SettingPropertyGroup("语音（TTS）")]
        public int TtsSpeed
        {
            get => _ttsSpeed;
            set
            {
                if (_ttsSpeed != value)
                {
                    _ttsSpeed = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _ttsVolume = 80;

        [SettingPropertyInteger("音量（%）", 0, 100, Order = 2, RequireRestart = false,
            HintText = "朗读音量，0 静音，100 最大。")]
        [SettingPropertyGroup("语音（TTS）")]
        public int TtsVolume
        {
            get => _ttsVolume;
            set
            {
                if (_ttsVolume != value)
                {
                    _ttsVolume = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyButton("测试语音", Content = "试听", Order = 3,
            RequireRestart = false, HintText = "用当前设置合成并播放一句测试语音，验证网络与设备可用。\n主菜单也可测试（用默认男声）；进入战役后按主角性别选音色。")]
        [SettingPropertyGroup("语音（TTS）")]
        public Action TestTts { get; set; } = () =>
        {
            // 修复：Hero.MainHero 的实现是 CharacterObject.PlayerCharacter.HeroObject——
            // 主菜单时 PlayerCharacter 为空，直接访问会抛 NullReferenceException 导致崩溃。
            // 这里安全获取：主菜单取不到就用 null（TtsService 回退默认男声，仍可测试网络/设备）。
            Hero hero = null;
            try
            {
                if (Campaign.Current != null)
                    hero = Hero.MainHero;
            }
            catch
            {
                hero = null;
            }
            TtsService.Speak(hero, "你好，欢迎来到卡拉迪亚。愿天命护佑你。");
        };
    }
}
