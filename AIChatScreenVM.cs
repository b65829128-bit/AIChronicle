using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace MyFirstMod
{
    public class ChatMessageVM : ViewModel
    {
        private string _senderText = "";
        private string _contentText = "";
        private string _timeText = "";
        private string _color = "#FFFFFFFF";
        private int _fontSize = 18;
        private int _senderFontSize = 14;
        private int _timeFontSize = 10;
        private int _messageSpacing = 16;
        private int _contentIndent = 12;
        private int _senderTopGap = 2;
        private int _contentTopGap = 8;

        [DataSourceProperty]
        public string SenderText
        {
            get => _senderText;
            set => SetField(ref _senderText, value, "SenderText");
        }

        [DataSourceProperty]
        public string ContentText
        {
            get => _contentText;
            set => SetField(ref _contentText, value, "ContentText");
        }

        [DataSourceProperty]
        public string TimeText
        {
            get => _timeText;
            set => SetField(ref _timeText, value, "TimeText");
        }

        [DataSourceProperty]
        public string Color
        {
            get => _color;
            set => SetField(ref _color, value, "Color");
        }

        [DataSourceProperty]
        public int FontSize
        {
            get => _fontSize;
            set => SetField(ref _fontSize, value, "FontSize");
        }

        [DataSourceProperty]
        public int SenderFontSize
        {
            get => _senderFontSize;
            set => SetField(ref _senderFontSize, value, "SenderFontSize");
        }

        [DataSourceProperty]
        public int TimeFontSize
        {
            get => _timeFontSize;
            set => SetField(ref _timeFontSize, value, "TimeFontSize");
        }

        [DataSourceProperty]
        public int MessageSpacing
        {
            get => _messageSpacing;
            set => SetField(ref _messageSpacing, value, "MessageSpacing");
        }

        [DataSourceProperty]
        public int ContentIndent
        {
            get => _contentIndent;
            set => SetField(ref _contentIndent, value, "ContentIndent");
        }

        [DataSourceProperty]
        public int SenderTopGap
        {
            get => _senderTopGap;
            set => SetField(ref _senderTopGap, value, "SenderTopGap");
        }

        [DataSourceProperty]
        public int ContentTopGap
        {
            get => _contentTopGap;
            set => SetField(ref _contentTopGap, value, "ContentTopGap");
        }

        public string Role { get; }

        public ChatMessageVM(string sender, string content, string role, string color,
            string timeText, int fontSize, int senderFontSize, int timeFontSize,
            int messageSpacing, int contentIndent, int senderTopGap, int contentTopGap)
        {
            _senderText = sender;
            _contentText = content;
            _timeText = timeText;
            _color = color;
            _fontSize = fontSize;
            _senderFontSize = senderFontSize;
            _timeFontSize = timeFontSize;
            _messageSpacing = messageSpacing;
            _contentIndent = contentIndent;
            _senderTopGap = senderTopGap;
            _contentTopGap = contentTopGap;
            Role = role;
        }
    }

    public class AIChatScreenVM : ViewModel
    {
        private string _titleText;
        private string _inputText = "";
        private string _sendButtonText = "发送";
        private bool _isLoading;
        private readonly Hero _hero;
        private readonly CharacterPrompt _charPrompt;
        private readonly string _intent;
        private readonly string _agentId = "";
        private readonly string _targetId = "";
        private List<ChatHistoryEntry> _sessionMessages = new();
        private int _chatFontSize = 24;
        private int _chatSenderFontSize = 22;
        private int _chatTimeFontSize = 22;
        private int _messageSpacing = 60;
        private int _contentIndent = 15;
        private int _senderTopGap = 6;
        private int _contentTopGap = 6;

        public Action? OnClose { get; set; }

        [DataSourceProperty]
        public string TitleText
        {
            get => _titleText;
            set => SetField(ref _titleText, value, "TitleText");
        }

        [DataSourceProperty]
        public string InputText
        {
            get => _inputText;
            set => SetField(ref _inputText, value, "InputText");
        }

        [DataSourceProperty]
        public string SendButtonText
        {
            get => _sendButtonText;
            set => SetField(ref _sendButtonText, value, "SendButtonText");
        }

        [DataSourceProperty]
        public bool IsLoading
        {
            get => _isLoading;
            set => SetField(ref _isLoading, value, "IsLoading");
        }

        [DataSourceProperty]
        public int ChatFontSize
        {
            get => _chatFontSize;
            set => SetField(ref _chatFontSize, value, "ChatFontSize");
        }

        [DataSourceProperty]
        public MBBindingList<ChatMessageVM> Messages { get; } = new();

        public AIChatScreenVM(Hero hero, string intent = "conversation")
        {
            _hero = hero;
            _charPrompt = PromptManager.LoadCharacterPrompt(hero);
            _intent = intent;
            _titleText = intent switch
            {
                "letter" => $"给 {_charPrompt.HeroName} 写信",
                "chancery" => "秘书处",
                _ => $"与 {_charPrompt.HeroName} 对话"
            };
            _chatFontSize = MySettings.Instance?.ChatFontSize ?? 24;
            _chatSenderFontSize = MySettings.Instance?.ChatSenderFontSize ?? 22;
            _chatTimeFontSize = MySettings.Instance?.ChatTimeFontSize ?? 22;
            _messageSpacing = MySettings.Instance?.MessageSpacing ?? 60;
            _contentIndent = MySettings.Instance?.ContentIndent ?? 15;
            _senderTopGap = MySettings.Instance?.SenderTopGap ?? 6;
            _contentTopGap = MySettings.Instance?.ContentTopGap ?? 6;

            try
            {
                var playerHero = Hero.MainHero;
                if (playerHero != null)
                {
                    EntityManager.ActivateInteraction(hero, playerHero);
                    _agentId = EntityManager.ActiveAgentId ?? "";
                    _targetId = EntityManager.ActiveTargetId ?? "";
                }
            }
            catch { }

            if (intent != "council_statement")
            {
                var history = PromptManager.LoadChatLogFor(_agentId, _targetId);
                _sessionMessages.AddRange(history);

                var loadTime = PromptManager.GetCurrentTimeString();
                foreach (var entry in _sessionMessages)
                {
                if (entry.Role == "tool") continue;
                var sender = entry.Role == "user" ? "你" : _charPrompt.HeroName;
                string color;
                if (entry.IsLetter)
                {
                    // 信件消息：📜 前缀 + 古铜色，与面对面说话区分
                    sender = "📜 " + sender;
                    color = "#B8860BFF";
                }
                else
                    color = entry.Role == "user" ? "#5DADE2FF" : "#F4D03FFF";
                // 修复：历史消息显示原始时间戳（entry.Time），而不是打开窗口的当前时间
                Messages.Add(new ChatMessageVM(sender, entry.Content, entry.Role, color, entry.Time ?? loadTime,
                    _chatFontSize, _chatSenderFontSize, _chatTimeFontSize,
                    _messageSpacing, _contentIndent, _senderTopGap, _contentTopGap));
            }
            }
        }

        public void ExecuteClose()
        {
            OnClose?.Invoke();
        }

        public void ExecuteSend()
        {
            if (_isLoading || string.IsNullOrWhiteSpace(_inputText))
                return;

            var userMsg = _inputText.Trim();
            InputText = "";
            IsLoading = true;
            SendButtonText = "思考中...";

            var now = PromptManager.GetCurrentTimeString();
            _chatFontSize = MySettings.Instance?.ChatFontSize ?? 24;
            _chatSenderFontSize = MySettings.Instance?.ChatSenderFontSize ?? 22;
            _chatTimeFontSize = MySettings.Instance?.ChatTimeFontSize ?? 22;
            _messageSpacing = MySettings.Instance?.MessageSpacing ?? 60;
            _contentIndent = MySettings.Instance?.ContentIndent ?? 15;
            _senderTopGap = MySettings.Instance?.SenderTopGap ?? 6;
            _contentTopGap = MySettings.Instance?.ContentTopGap ?? 6;
            Messages.Add(new ChatMessageVM("你", userMsg, "user", "#5DADE2FF", now,
                _chatFontSize, _chatSenderFontSize, _chatTimeFontSize,
                _messageSpacing, _contentIndent, _senderTopGap, _contentTopGap));
            // 修复：只有写信（_intent=="letter"）才标记为书信；普通对话不加【信】前缀（原实现硬编码 true 导致正常对话也被标成书信）
            var isLetter = _intent == "letter";
            _sessionMessages.Add(new ChatHistoryEntry { Role = "user", Content = userMsg, IsLetter = isLetter });
            PromptManager.AppendChatLogFor(_agentId, _targetId, "user", userMsg, isLetter);

            if (_intent == "letter")
            {
                // 修复：写信 = 发送真实信件（带延时），回复稍后作为信件送达——而非直聊即时回复。
                // 原实现把"写信"当直聊，无延时、也无记忆连续性。
                var senderHero = Hero.MainHero;
                var senderEntity = senderHero != null ? EntityManager.GetOrCreateEntity(senderHero) : null;
                var delayHours = 0f;
                if (senderHero != null && _hero != null)
                    delayHours = ToolExecutor.CalculateLetterDelay(senderHero, _hero);

                var evt = new ActivationEvent
                {
                    Type = ActivationEventType.LetterReceived,
                    AgentId = _agentId,            // 收信人（对方）
                    TargetId = senderEntity?.Id ?? _agentId, // 发信人（玩家）
                    Content = userMsg,
                    Depth = 0
                };
                if (delayHours > 0.1f)
                    AgentScheduler.QueueDelayedEvent(evt, delayHours);
                else
                    AgentScheduler.QueueEvent(evt);

                var delayNote = delayHours > 0.1f ? $"（预计{delayHours:F0}小时后送达）" : "";
                MainThreadExecutor.DisplayMessage(new InformationMessage(
                    $"信件已寄出给 {_charPrompt.HeroName}{delayNote}", Colors.Cyan));
                // ExecuteSend 本身就在主线程（UI 回调）——直接更新 UI，勿用 RunOnMainThread（主线程等自己处理会卡死）
                Messages.Add(new ChatMessageVM("系统",
                    $"信件已寄出{delayNote}，等待对方回信...",
                    "system", "#E67E22FF", PromptManager.GetCurrentTimeString(),
                    _chatFontSize, _chatSenderFontSize, _chatTimeFontSize,
                    _messageSpacing, _contentIndent, _senderTopGap, _contentTopGap));
                IsLoading = false;
                SendButtonText = "发送";
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    _charPrompt.ChatHistory = _sessionMessages;
                    var chatResponse = await AIChatClient.SendMessage(_charPrompt, _hero, true, _intent);
                    var displayText = chatResponse.Content;

                    _sessionMessages.Add(new ChatHistoryEntry
                    {
                        Role = "assistant",
                        Content = displayText,
                        ToolCalls = chatResponse.ToolCalls.Count > 0 ? chatResponse.ToolCalls : null
                    });

                    foreach (var kv in chatResponse.ToolResults)
                    {
                        _sessionMessages.Add(new ChatHistoryEntry
                        {
                            Role = "tool",
                            Content = kv.Value,
                            ToolCallId = kv.Key
                        });
                    }
                    PromptManager.AppendChatLogFor(_agentId, _targetId, "assistant", displayText);

                    foreach (var tc in chatResponse.ToolCalls)
                    {
                        if (MySettings.Instance?.ShowToolCalls == true && tc.Name != "update_knowledge")
                        {
                            string toolDesc;
                            try
                            {
                                var a = Newtonsoft.Json.Linq.JObject.Parse(tc.Arguments);
                                var path = a["path"]?.ToString() ?? "";
                                var pattern = a["pattern"]?.ToString() ?? "";
                                var set = a["settlement_name"]?.ToString() ?? "";
                                var tid = a["target_entity_id"]?.ToString();
                                var tname = !string.IsNullOrEmpty(tid)
                                    ? (EntityManager.ResolveEntityId(tid!) != null
                                        ? EntityManager.GetEntityById(EntityManager.ResolveEntityId(tid!)!)?.Name ?? tid
                                        : tid)
                                    : "";
                                toolDesc = tc.Name switch
                                {
                                    "read_file" => $"读取了记忆：{path}",
                                    "append_file" => $"更新了记忆：{path}",
                                    "write_file" => $"写入了文件：{path}",
                                    "edit_file" => $"修改了文件：{path}",
                                    "delete_file" => $"删除了文件：{path}",
                                    "list_dir" => $"浏览了目录：{path}",
                                    "glob" => $"搜索了文件：{pattern}",
                                    "grep" => $"搜索了关键词：{pattern}",
                                    "send_letter" => $"给 {tname} 写了一封信",
                                    "move_to_settlement" => $"下令部队前往 {set}",
                                    "wait_at_settlement" => $"决定在当前位置停留",
                                    "raid_settlement" => $"下令劫掠 {set}",
                                    "besiege_settlement" => $"下令围攻 {set}",
                                    "form_army" => "召集了军团",
                                    "engage_party" => $"下令追击 {tname} 的部队",
                                    "defend_settlement" => $"下令驻防 {set}",
                                    "patrol_settlement" => $"下令巡逻 {set} 周边",
                                    "escort_party" => $"下令护送 {tname} 的部队",
                                    "go_around_party" => $"下令绕开 {tname} 的部队",
                                    "cancel_action" => "取消了当前任务",
                                    "change_relation" => $"调整了对{tname}的好感度",
                                    "give_gold" => $"赠予了{tname}金币",
                                    "request_gold" => $"向{tname}索要了金币",
                                    "query_character" => "查询了人物信息",
                                    "query_settlement" => "查询了定居点信息",
                                    "query_settlement_geography" => "查询了地理情报",
                                    "query_world_state" => "获取了世界局势",
                                    "query_kingdom_settlements" => "查询了王国领土",
                                    "query_clan_members" => "查询了家族成员",
                                    "query_clan_fiefs" => "查询了家族封地",
                                    "query_kingdom_clans" => "查询了王国家族",
                                    "query_recent_events" => "查询了近期事件",
                                    "query_surroundings" => "扫描了周围环境",
                                    "query_war_status" => "查询了战争状态",
                                    "query_influence" => "查询了影响力",
                                    "declare_war" => "宣战了",
                                    "propose_peace" => "提出了议和",
                                    "propose_alliance" => "提出了结盟",
                                    "propose_trade" => "提出了贸易协定",
                                    "end_alliance" => "终止了盟约",
                                    "end_trade_agreement" => "终止了贸易协定",
                                    "respond_to_diplomacy_proposal" => "回复了外交提案",
                                    "gift_fief" => $"将 {set} 转让了",
                                    "submit_advisory" => "向国王进谏了",
                                    "submit_secret_advisory" => "向国王密陈了",
                                    "submit_edict" => "颁布了诏令",
                                    "consult_king" => "遣使问询了",
                                    "reply_consult" => "回复了外交问询",
                                    "release_prisoner" => $"释放了俘虏 {a["prisoner_name"]?.ToString()}",
                                    "execute_prisoner" => $"处决了俘虏 {a["prisoner_name"]?.ToString()}",
                                    "create_clan" => $"降下了新的贵族血脉 {a["clan_name"]?.ToString()}",
                                    _ => $"调用了 {tc.Name}"
                                };
                            }
                            catch
                            {
                                toolDesc = $"调用了 {tc.Name}";
                            }

                            MainThreadExecutor.DisplayMessage(new InformationMessage(
                                $"[MyFirstMod] {_charPrompt.HeroName} {toolDesc}", Colors.Cyan));
                        }
                    }

                    if (chatResponse.LearnedKnowledge != null)
                    {
                        PromptManager.UpdateTargetKnowledge(chatResponse.LearnedKnowledge);
                        MainThreadExecutor.DisplayMessage(new InformationMessage(
                            $"[MyFirstMod] {_charPrompt.HeroName} 更新了对你的认知",
                            Colors.Cyan));
                    }

                    // 修复：UI 绑定集合/属性只在主线程修改——原实现从后台线程直接改 MBBindingList，跨线程竞态
                    MainThreadExecutor.RunOnMainThread(() =>
                    {
                        // 修复：assistant 消息时间戳用"回复时刻"而非发送时捕获的 now（信件/慢回复时曾显示错误时间）
                        Messages.Add(new ChatMessageVM(_charPrompt.HeroName, displayText,
                            "assistant", "#F4D03FFF", PromptManager.GetCurrentTimeString(),
                            MySettings.Instance?.ChatFontSize ?? 24,
                            MySettings.Instance?.ChatSenderFontSize ?? 22,
                            MySettings.Instance?.ChatTimeFontSize ?? 22,
                            MySettings.Instance?.MessageSpacing ?? 60,
                            MySettings.Instance?.ContentIndent ?? 15,
                            MySettings.Instance?.SenderTopGap ?? 6,
                            MySettings.Instance?.ContentTopGap ?? 6));
                    });
                }
                catch (Exception ex)
                {
                    MainThreadExecutor.RunOnMainThread(() =>
                    {
                        Messages.Add(new ChatMessageVM("系统", $"错误：{ex.Message}",
                            "system", "#E74C3CFF", PromptManager.GetCurrentTimeString(),
                            24, 22, 22, 60, 15, 6, 6));
                    });
                }
                finally
                {
                    MainThreadExecutor.RunOnMainThread(() =>
                    {
                        IsLoading = false;
                        SendButtonText = "发送";
                    });
                }
            });
        }
    }
}
