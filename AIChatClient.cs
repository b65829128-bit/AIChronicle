using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace MyFirstMod
{
    public class ChatResponse
    {
        public string Content { get; set; } = "";
        public string? LearnedKnowledge { get; set; }
        public List<ToolCallData> ToolCalls { get; set; } = new();
        public Dictionary<string, string> ToolResults { get; set; } = new();
    }

    public static class AIChatClient
    {
        private static readonly HttpClient _client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        internal static Hero? CurrentHero;
        internal static string CurrentIntent = "conversation";

        internal sealed class PendingInquiry
        {
            public Hero Hero = null!;
            public int Amount;
            public ManualResetEventSlim Event = new(false);
            public bool Result;
        }

        private static PendingInquiry? _pendingInquiry;

        internal static void SetPendingInquiry(PendingInquiry? inquiry)
        {
            _pendingInquiry = inquiry;
        }

        private static object BuildTools()
        {
            var activeAgent = EntityManager.ActiveAgent;
            var activeTarget = EntityManager.ActiveTarget;
            List<ToolDef> toolDefs;
            if (activeAgent != null)
            {
                toolDefs = ContextBuilder.GetFilteredTools(activeAgent, CurrentIntent);
                if (activeTarget?.HasCapability(EntityCapability.Diplomat) == true)
                {
                    var diplomatTools = ContextBuilder.GetFilteredTools(activeTarget, CurrentIntent);
                    foreach (var t in diplomatTools)
                    {
                        if (!toolDefs.Any(existing => existing.Name == t.Name))
                            toolDefs.Add(t);
                    }
                }
            }
            else
            {
                toolDefs = PromptManager.LoadAllTools();
            }
            return BuildTools(toolDefs);
        }

        private static object BuildTools(List<ToolDef> toolDefs)
        {
            return toolDefs.Select(d => new
            {
                type = "function",
                function = new
                {
                    name = d.Name,
                    description = d.Description,
                    parameters = new
                    {
                        type = "object",
                        properties = d.Parameters.ToDictionary(
                            p => p.Name,
                            p => (object)new { type = p.Type, description = p.Description }
                        ),
                        required = d.Parameters.Select(p => p.Name).ToArray()
                    }
                }
            }).ToArray();
        }

        public static void CheckPendingInquiry()
        {
            var inquiry = _pendingInquiry;
            if (inquiry == null) return;

            _pendingInquiry = null;

            var hero = inquiry.Hero;
            var amount = inquiry.Amount;

            InformationManager.ShowInquiry(new InquiryData(
                $"{hero.Name} 向你索要金币",
                $"{hero.Name} 向你要 {amount} 金币。\n你当前拥有 {Hero.MainHero.Gold} 金币。",
                true, true, "同意", "拒绝",
                () => { inquiry.Result = true; inquiry.Event.Set(); },
                () => { inquiry.Result = false; inquiry.Event.Set(); }),
                pauseGameActiveState: true,
                prioritize: true);
        }

        public static async Task<ChatResponse> EvaluateToolCalls(CharacterPrompt charPrompt, string roleplayResponse)
        {
            var settings = MySettings.Instance!;
            var systemPrompt = PromptManager.LoadToolCallPrompt();

            var messageList = new List<object> { new { role = "system", content = systemPrompt } };

            foreach (var entry in charPrompt.ChatHistory)
            {
                if (entry.Role == "tool")
                {
                    messageList.Add(new
                    {
                        role = "tool",
                        tool_call_id = entry.ToolCallId ?? "",
                        content = entry.Content
                    });
                }
                else if (entry.ToolCalls != null && entry.ToolCalls.Count > 0)
                {
                    messageList.Add(new
                    {
                        role = entry.Role,
                        content = entry.Content,
                        tool_calls = entry.ToolCalls.Select(tc => new
                        {
                            id = tc.Id,
                            type = "function",
                            function = new { name = tc.Name, arguments = tc.Arguments }
                        })
                    });
                }
                else
                {
                    messageList.Add(new { role = entry.Role, content = entry.Content });
                }
            }

            if (!string.IsNullOrEmpty(roleplayResponse))
            {
                messageList.Add(new { role = "assistant", content = roleplayResponse });
            }

            var payload = new
            {
                model = settings.Model,
                messages = messageList,
                tools = BuildTools(),
                tool_choice = "auto",
                max_tokens = 200,
                temperature = 0.1f
            };

            var json = JsonConvert.SerializeObject(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            var response = await _client.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(responseBody);
            var message = result["choices"]?[0]?["message"];

            string? learnedKnowledge = null;
            var responseToolCalls = new List<ToolCallData>();

            var toolCalls = message?["tool_calls"];
            if (toolCalls != null)
            {
                foreach (var call in toolCalls)
                {
                    var callId = call["id"]?.ToString() ?? Guid.NewGuid().ToString("N");
                    var funcName = call["function"]?["name"]?.ToString() ?? "";
                    var argsStr = call["function"]?["arguments"]?.ToString() ?? "{}";

                    responseToolCalls.Add(new ToolCallData
                    {
                        Id = callId,
                        Name = funcName,
                        Arguments = argsStr
                    });

                    if (funcName == "update_knowledge")
                    {
                        try
                        {
                            var args = JObject.Parse(argsStr);
                            learnedKnowledge = args["knowledge"]?.ToString();
                        }
                        catch { }
                    }
                }
            }

            return new ChatResponse
            {
                Content = "",
                LearnedKnowledge = learnedKnowledge,
                ToolCalls = responseToolCalls
            };
        }

        public static async Task<ChatResponse> SendMessage(CharacterPrompt charPrompt, Hero? hero = null, bool includeTools = true, string intent = "conversation")
        {
            CurrentHero = hero;
            CurrentIntent = intent;
            var settings = MySettings.Instance!;
            var systemPrompt = hero != null
                ? PromptManager.BuildAgentSystemPrompt(hero, charPrompt, intent)
                : PromptManager.BuildSystemPrompt(charPrompt.HeroName, charPrompt);

            var historyLimit = settings.ChatHistoryLimit;
            var trimmedHistory = charPrompt.ChatHistory;
            if (trimmedHistory.Count > historyLimit)
            {
                trimmedHistory = trimmedHistory.Skip(trimmedHistory.Count - historyLimit).ToList();
            }

            var messageList = new List<object> { new { role = "system", content = systemPrompt } };

            foreach (var entry in trimmedHistory)
            {
                if (entry.Role == "tool")
                {
                    if (includeTools)
                    {
                        messageList.Add(new
                        {
                            role = "tool",
                            tool_call_id = entry.ToolCallId ?? "",
                            content = entry.Content
                        });
                    }
                }
                else if (entry.ToolCalls != null && entry.ToolCalls.Count > 0)
                {
                    if (includeTools)
                    {
                        if (!string.IsNullOrEmpty(entry.ReasoningContent))
                        {
                            messageList.Add(new JObject
                            {
                                ["role"] = entry.Role,
                                ["content"] = entry.Content,
                                ["tool_calls"] = new JArray(entry.ToolCalls.Select(tc => new JObject
                                {
                                    ["id"] = tc.Id,
                                    ["type"] = "function",
                                    ["function"] = new JObject
                                    {
                                        ["name"] = tc.Name,
                                        ["arguments"] = tc.Arguments
                                    }
                                })),
                                ["reasoning_content"] = entry.ReasoningContent
                            });
                        }
                        else
                        {
                            messageList.Add(new
                            {
                                role = entry.Role,
                                content = entry.Content,
                                tool_calls = entry.ToolCalls.Select(tc => new
                                {
                                    id = tc.Id,
                                    type = "function",
                                    function = new
                                    {
                                        name = tc.Name,
                                        arguments = tc.Arguments
                                    }
                                })
                            });
                        }
                    }
                    else
                    {
                        messageList.Add(new { role = entry.Role, content = entry.Content });
                    }
                }
                else
                {
                    messageList.Add(new { role = entry.Role, content = entry.Content });
                }
            }

            string? learnedKnowledge = null;
            var allToolCalls = new List<ToolCallData>();
            var toolResults = new Dictionary<string, string>();
            var accumulatedText = "";

            var maxRounds = settings.UnlimitedAgentRounds ? int.MaxValue : settings.MaxAgentRounds;

            for (int round = 0; round < maxRounds; round++)
            {
                object payload;
                if (includeTools)
                {
                    payload = new
                    {
                        model = settings.Model,
                        messages = messageList,
                        tools = BuildTools(),
                        tool_choice = "auto",
                        max_tokens = settings.MaxTokens,
                        temperature = settings.Temperature,
                        stream = true
                    };
                }
                else
                {
                    payload = new
                    {
                        model = settings.Model,
                        messages = messageList,
                        max_tokens = settings.MaxTokens,
                        temperature = settings.Temperature,
                        stream = true
                    };
                }

                var json = JsonConvert.SerializeObject(payload);
                var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiUrl)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
                var httpResponse = await _client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cts.Token);
                httpResponse.EnsureSuccessStatusCode();

                var roundToolCalls = new List<JToken>();
                var roundText = "";
                var roundReasoning = "";

                using var stream = await httpResponse.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream, Encoding.UTF8);

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(line)) continue;
                    if (!line.StartsWith("data: ")) continue;

                    var data = line.Substring(6);
                    if (data == "[DONE]") break;

                    JObject chunk;
                    try { chunk = JObject.Parse(data); }
                    catch { continue; }

                    var choices = chunk["choices"]?[0];
                    if (choices == null) continue;

                    var delta = choices["delta"];
                    if (delta == null) continue;

                    var deltaContent = delta["content"]?.ToString();
                    if (deltaContent != null)
                    {
                        roundText += deltaContent;
                        accumulatedText += deltaContent;
                    }

                    var deltaReasoning = delta["reasoning_content"]?.ToString();
                    if (deltaReasoning != null)
                    {
                        roundReasoning += deltaReasoning;
                    }

                    var deltaToolCalls = delta["tool_calls"];
                    if (deltaToolCalls != null)
                    {
                        foreach (var dtc in deltaToolCalls)
                        {
                            var idx = dtc["index"]?.ToObject<int>() ?? 0;
                            while (roundToolCalls.Count <= idx)
                                roundToolCalls.Add(new JObject());

                            var existing = (JObject)roundToolCalls[idx];
                            var funcDelta = dtc["function"];
                            if (funcDelta?["name"] != null)
                            {
                                existing["id"] = dtc["id"];
                                existing["type"] = "function";
                                existing["function"] = new JObject
                                {
                                    ["name"] = funcDelta["name"]!.ToString(),
                                    ["arguments"] = funcDelta["arguments"]?.ToString() ?? ""
                                };
                            }
                            else if (funcDelta?["arguments"] != null)
                            {
                                var func = existing["function"] as JObject;
                                if (func != null)
                                    func["arguments"] = (func["arguments"]?.ToString() ?? "") + funcDelta["arguments"]!.ToString();
                            }
                            if (dtc["index"] == null)
                            {
                                existing["id"] = dtc["id"];
                                existing["type"] = "function";
                            }
                        }
                    }
                }

                if (roundToolCalls.Count == 0)
                {
                    return new ChatResponse
                    {
                        Content = string.IsNullOrEmpty(accumulatedText) ? "（领主沉默不语）" : accumulatedText,
                        LearnedKnowledge = learnedKnowledge,
                        ToolCalls = allToolCalls,
                        ToolResults = toolResults
                    };
                }

                var roundToolCallsObj = new JArray(roundToolCalls);
                var assistantMsg = new JObject
                {
                    ["role"] = "assistant",
                    ["content"] = roundText,
                    ["tool_calls"] = roundToolCallsObj
                };
                if (!string.IsNullOrEmpty(roundReasoning))
                    assistantMsg["reasoning_content"] = roundReasoning;
                messageList.Add(assistantMsg);

                foreach (var rt in roundToolCalls)
                {
                    var callId = rt["id"]?.ToString() ?? Guid.NewGuid().ToString("N");
                    var func = rt["function"];
                    var funcName = func?["name"]?.ToString() ?? "";
                    var argsStr = func?["arguments"]?.ToString() ?? "{}";

                    allToolCalls.Add(new ToolCallData { Id = callId, Name = funcName, Arguments = argsStr });

                    if (funcName == "update_knowledge")
                    {
                        try { var args = JObject.Parse(argsStr); learnedKnowledge = args["knowledge"]?.ToString(); }
                        catch { }
                        toolResults[callId] = "已记录。";
                        messageList.Add(new { role = "tool", tool_call_id = callId, content = "已记录。" });
                    }
                    else
                    {
                        var toolResult = ToolExecutor.ExecuteToolCall(funcName, argsStr);
                        toolResults[callId] = toolResult;
                        messageList.Add(new { role = "tool", tool_call_id = callId, content = toolResult });
                    }
                }
            }

            return new ChatResponse
            {
                Content = string.IsNullOrEmpty(accumulatedText) ? "（领主沉默不语）" : accumulatedText,
                LearnedKnowledge = learnedKnowledge,
                ToolCalls = allToolCalls,
                ToolResults = toolResults
            };
        }

        public static async Task<string> TestFunctionCalling()
        {
            var settings = MySettings.Instance!;
            var payload = new
            {
                model = settings.Model,
                messages = new[]
                {
                    new { role = "user", content = "我叫炎瑰。现在请用一句话跟我打招呼并介绍你自己，同时调用 update_knowledge 函数记录你对我的认知。" }
                },
                tools = BuildTools(),
                temperature = 0.7f,
                max_tokens = 300
            };

            var json = JsonConvert.SerializeObject(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            var response = await _client.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(responseBody);
            var message = result["choices"]?[0]?["message"];

            var toolCalls = message?["tool_calls"];
            if (toolCalls == null || !toolCalls.Any())
                return "模型不支持 function calling，或该模型未启用此功能。";

            var func = toolCalls[0]?["function"];
            var name = func?["name"]?.ToString();
            var args = func?["arguments"]?.ToString() ?? "{}";
            var parsed = JObject.Parse(args);

            return $"支持 function calling。\n函数名: {name}\n参数: knowledge={parsed["knowledge"]}";
        }

        public static async void TestConnection()
        {
            var settings = MySettings.Instance!;
            InformationManager.DisplayMessage(new InformationMessage(
                "[MyFirstMod] 正在测试 API 连接...",
                Colors.Cyan));

            try
            {
                var normalTest = await SendMessage(new CharacterPrompt
                {
                    HeroName = "测试领主",
                    ChatHistory = new List<ChatHistoryEntry>
                    {
                        new() { Role = "user", Content = "你好，请用一句话介绍卡拉迪亚大陆。" }
                    }
                });

                InformationManager.DisplayMessage(new InformationMessage(
                    $"[MyFirstMod] 连接成功！回复：{normalTest.Content}",
                    Colors.Green));

                InformationManager.DisplayMessage(new InformationMessage(
                    "[MyFirstMod] 正在检测 function calling 支持...",
                    Colors.Cyan));

                var fcResult = await TestFunctionCalling();
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[MyFirstMod] {fcResult}",
                    Colors.Green));
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[MyFirstMod] 连接失败：{ex.Message}",
                    Colors.Red));
            }
        }
    }
}
