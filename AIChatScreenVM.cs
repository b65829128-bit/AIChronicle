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
        private string _color = "#FFFFFFFF";

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
        public string Color
        {
            get => _color;
            set => SetField(ref _color, value, "Color");
        }

        public string Role { get; }

        public ChatMessageVM(string sender, string content, string role, string color)
        {
            _senderText = sender;
            _contentText = content;
            _color = color;
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
        private List<ChatHistoryEntry> _sessionMessages = new();

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
        public MBBindingList<ChatMessageVM> Messages { get; } = new();

        public AIChatScreenVM(Hero hero)
        {
            _hero = hero;
            _charPrompt = PromptManager.LoadCharacterPrompt(hero);
            _titleText = $"与 {_charPrompt.HeroName} 对话";

            try
            {
                AgentManager.SetCurrentNpc(hero.Name?.ToString() ?? "unknown");
            }
            catch { }

            var history = PromptManager.LoadChatLog(hero);
            _sessionMessages.AddRange(history);

            foreach (var entry in _sessionMessages)
            {
                if (entry.Role == "tool") continue;
                var sender = entry.Role == "user" ? "你" : _charPrompt.HeroName;
                var color = entry.Role == "user" ? "#5DADE2FF" : "#F4D03FFF";
                Messages.Add(new ChatMessageVM(sender, entry.Content, entry.Role, color));
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

            Messages.Add(new ChatMessageVM("你", userMsg, "user", "#5DADE2FF"));
            _sessionMessages.Add(new ChatHistoryEntry { Role = "user", Content = userMsg });
            PromptManager.AppendChatLog(_hero, "user", userMsg);

            Task.Run(async () =>
            {
                try
                {
                    _charPrompt.ChatHistory = _sessionMessages;
                    var useIndependent = MySettings.Instance?.IndependentToolCalling == true;
                    var chatResponse = await AIChatClient.SendMessage(_charPrompt, _hero, !useIndependent);
                    var displayText = chatResponse.Content;

                    if (useIndependent)
                    {
                        try
                        {
                            var toolResponse = await AIChatClient.EvaluateToolCalls(_charPrompt, displayText);
                            chatResponse = new ChatResponse
                            {
                                Content = displayText,
                                LearnedKnowledge = toolResponse.LearnedKnowledge,
                                ToolCalls = toolResponse.ToolCalls
                            };
                        }
                        catch (Exception ex)
                        {
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"[MyFirstMod] 工具调用评估失败：{ex.Message}",
                                Colors.Red));
                        }
                    }

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
                    PromptManager.AppendChatLog(_hero, "assistant", displayText);

                    foreach (var tc in chatResponse.ToolCalls)
                    {
                        if (MySettings.Instance?.ShowToolCalls == true && tc.Name != "update_knowledge")
                        {
                            string toolDesc;
                            try
                            {
                                var a = Newtonsoft.Json.Linq.JObject.Parse(tc.Arguments);
                                var path = a["path"]?.ToString() ?? "";
                                toolDesc = tc.Name switch
                                {
                                    "read_file" => $"读取了记忆：{path}",
                                    "append_file" => $"更新了记忆：{path}",
                                    "list_dir" => $"浏览了目录：{path}",
                                    _ => $"调用了 {tc.Name}"
                                };
                            }
                            catch
                            {
                                toolDesc = $"调用了 {tc.Name}";
                            }

                            InformationManager.DisplayMessage(new InformationMessage(
                                $"[MyFirstMod] {_charPrompt.HeroName} {toolDesc}", Colors.Cyan));
                        }
                    }

                    if (chatResponse.LearnedKnowledge != null)
                    {
                        PromptManager.UpdatePlayerKnowledge(_hero, chatResponse.LearnedKnowledge);
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"[MyFirstMod] {_charPrompt.HeroName} 更新了对你的认知",
                            Colors.Cyan));
                    }

                    Messages.Add(new ChatMessageVM(_charPrompt.HeroName, displayText,
                        "assistant", "#F4D03FFF"));
                }
                catch (Exception ex)
                {
                    Messages.Add(new ChatMessageVM("系统", $"错误：{ex.Message}",
                        "system", "#E74C3CFF"));
                }
                finally
                {
                    IsLoading = false;
                    SendButtonText = "发送";
                }
            });
        }
    }
}
