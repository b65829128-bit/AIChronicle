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

        /// <summary>最终轮的思维链（reasoning_content）摘录，供调试日志复盘。</summary>
        public string? LastReasoning { get; set; }

        /// <summary>最终轮 finish_reason（"stop"=自然结束，"length"=被 max_tokens 截断）——用于区分主动沉默与被截断。</summary>
        public string? FinishReason { get; set; }
    }

    public static class AIChatClient
    {
        private static readonly HttpClient _client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // 并发修复：聊天与后台事件（信件/谏言/外交）可同时跑 SendMessage，
        // 静态字段会被互相覆盖导致工具作用到错误的 NPC。改用 AsyncLocal，
        // 每个异步流程持有自己的上下文，互不干扰。
        private static readonly AsyncLocal<Hero?> _currentHero = new();
        private static readonly AsyncLocal<string?> _currentIntent = new();
        private static readonly AsyncLocal<HashSet<string>?> _activatedCategories = new();

        internal static Hero? CurrentHero
        {
            get => _currentHero.Value;
            set => _currentHero.Value = value;
        }

        internal static string CurrentIntent
        {
            get => _currentIntent.Value ?? "conversation";
            set => _currentIntent.Value = value;
        }

        internal static HashSet<string> ActivatedCategories
        {
            get => _activatedCategories.Value ?? new HashSet<string>();
            set => _activatedCategories.Value = value;
        }

        internal sealed class PendingInquiry
        {
            public Hero Hero = null!;
            public int Amount;
            public string ItemName = "";
            public int ItemCount;
            public ManualResetEventSlim Event = new(false);
            public bool Result;
        }

        // 修复：并发索要/请求改为队列逐个弹出——原单槽位在并发时后写覆盖先写，导致一次请求被静默吞掉
        private static readonly System.Collections.Concurrent.ConcurrentQueue<PendingInquiry> _pendingInquiryQueue = new();
        private static bool _inquiryShowing;

        internal static void SetPendingInquiry(PendingInquiry? inquiry)
        {
            if (inquiry != null)
                _pendingInquiryQueue.Enqueue(inquiry);
        }

        private static string[] GetDefaultCategories(string intent) => intent switch
        {
            "conversation" => new[] { "universal", "query", "social", "file" },
            "letter" => new[] { "universal", "query", "file", "communication", "movement", "military", "diplomacy" },
            "diplomacy" => new[] { "universal", "query", "diplomacy", "communication" },
            "historian" => new[] { "universal", "query", "file" },
            "advisory" => new[] { "universal", "query", "file", "communication" },
            "fief_review" => new[] { "universal", "query", "file", "communication", "diplomacy" },
            "chat" => new[] { "universal", "query", "file", "social", "communication" },
            _ => new[] { "universal", "query", "social", "military", "movement", "diplomacy", "file", "communication" },
        };

        internal static void ActivateCategory(string category)
        {
            ActivatedCategories.Add(category);
        }

        private static ToolDef BrowseToolsDef => new()
        {
            Name = "browse_tools",
            Category = "meta",
            Description = "Browse available tools in a category to unlock them for use.\n\nUsage:\n- Call this to see detailed descriptions and unlock tools in a specific category.\n- The category parameter must be one of: military, movement, diplomacy, file, social, query, communication.\n- After calling, the tools in that category become available in your next response.\n- You do NOT need to call this for categories already shown in your current function list.\n- Only call this when the situation requires tools from a category you cannot currently access.",
            Parameters = new List<ToolParamDef>
            {
                new() { Name = "category", Type = "string", Description = "Category to browse and unlock: military, movement, diplomacy, file, social, query, communication." }
            }
        };

        private static object BuildTools()
        {
            if (ActivatedCategories.Count == 0)
                ActivatedCategories = new HashSet<string>(GetDefaultCategories("conversation"));

            var activeAgent = EntityManager.ActiveAgent;
            var activeTarget = EntityManager.ActiveTarget;
            List<ToolDef> toolDefs;
            if (activeAgent != null)
            {
                var capabilityTools = ContextBuilder.GetFilteredTools(activeAgent);
                toolDefs = capabilityTools.Where(t => ActivatedCategories.Contains(t.Category)).ToList();
                if (activeTarget?.HasCapability(EntityCapability.Diplomat) == true)
                {
                    var targetTools = ContextBuilder.GetFilteredTools(activeTarget);
                    foreach (var t in targetTools)
                    {
                        if (!toolDefs.Any(existing => existing.Name == t.Name) && ActivatedCategories.Contains(t.Category))
                            toolDefs.Add(t);
                    }
                }
            }
            else
            {
                toolDefs = PromptManager.LoadAllTools().Where(t => ActivatedCategories.Contains(t.Category)).ToList();
            }
            toolDefs.Add(BrowseToolsDef);
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
            if (_inquiryShowing) return;
            if (!_pendingInquiryQueue.TryDequeue(out var inquiry)) return;
            _inquiryShowing = true;

            if (inquiry.Amount > 0)
            {
                var hero = inquiry.Hero;
                var amount = inquiry.Amount;
                InformationManager.ShowInquiry(new InquiryData(
                    $"{hero.Name} 向你索要金币",
                    $"{hero.Name} 向你要 {amount} 金币。\n你当前拥有 {Hero.MainHero.Gold} 金币。",
                    true, true, "同意", "拒绝",
                    () => { inquiry.Result = true; TrySignalInquiry(inquiry); },
                    () => { inquiry.Result = false; TrySignalInquiry(inquiry); }),
                    pauseGameActiveState: true,
                    prioritize: true);
            }
            else if (!string.IsNullOrEmpty(inquiry.ItemName))
            {
                var hero = inquiry.Hero;
                var itemName = inquiry.ItemName;
                var count = inquiry.ItemCount;
                InformationManager.ShowInquiry(new InquiryData(
                    $"{hero.Name} 向你要物品",
                    $"{hero.Name} 向你要 {itemName} × {count}。",
                    true, true, "同意", "拒绝",
                    () =>
                    {
                        var myParty = Hero.MainHero.PartyBelongedTo;
                        var heroParty = hero.PartyBelongedTo;
                        if (myParty != null && heroParty != null)
                        {
                            foreach (var ie in myParty.ItemRoster)
                            {
                                var item = ie.EquipmentElement.Item;
                                if (item == null) continue;
                                var name = item.Name?.ToString() ?? "";
                                if (!name.Contains(itemName) && !itemName.Contains(name)) continue;
                                if (ie.Amount < count) continue; // 修复：该槽位数量不足时找下一槽，而非放弃整个转移
                                myParty.ItemRoster.AddToCounts(item, -count);
                                heroParty.ItemRoster.AddToCounts(item, count);
                                break;
                            }
                        }
                        inquiry.Result = true;
                        TrySignalInquiry(inquiry);
                    },
                    () => { inquiry.Result = false; TrySignalInquiry(inquiry); }),
                    pauseGameActiveState: true,
                    prioritize: true);
            }
        }

        /// <summary>弹窗回调统一出口：复位"正在展示"标记并唤醒等待线程。
        /// 修复：后台线程 30s 超时已释放 mre 时，Set() 会抛 ObjectDisposedException——try 吞掉。</summary>
        private static void TrySignalInquiry(PendingInquiry inquiry)
        {
            _inquiryShowing = false;
            try { inquiry.Event.Set(); }
            catch { }
        }

        public static async Task<ChatResponse> SendMessage(CharacterPrompt charPrompt, Hero? hero = null, bool includeTools = true, string intent = "conversation")
        {
            CurrentHero = hero;
            CurrentIntent = intent;
            ActivatedCategories = new HashSet<string>(GetDefaultCategories(intent));
            var settings = MySettings.Instance!;
            var systemPrompt = hero != null
                ? PromptManager.BuildAgentSystemPrompt(hero, charPrompt, intent)
                : intent == "historian"
                    ? ContextBuilder.Build("__historian__", "__historian__", "historian")
                    : PromptManager.BuildSystemPrompt(charPrompt.HeroName, charPrompt);

            var historyLimit = settings.ChatHistoryLimit;
            var trimmedHistory = charPrompt.ChatHistory;
            if (trimmedHistory.Count > historyLimit)
            {
                trimmedHistory = trimmedHistory.Skip(trimmedHistory.Count - historyLimit).ToList();
                // 修复：去掉开头被截断成孤儿的 tool 消息（其对应的 assistant(tool_calls) 已被裁掉），
                // 否则发送给 API 的消息序列不合法（tool 必须引用前置 tool_call_id）→ 400。
                while (trimmedHistory.Count > 0 && trimmedHistory[0].Role == "tool")
                    trimmedHistory.RemoveAt(0);
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
            var lastMeaningfulText = "";
            var lastReasoning = "";
            var lastFinishReason = "";

            // 不再限制工具调用轮数——模型直到自然停止。
            // 仅保留一个极高的安全阀（50 轮），防止病态死循环。
            const int MaxSafetyRounds = 50;

            for (int round = 0; round < MaxSafetyRounds; round++)
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
                var roundFinishReason = "";

                using var stream = await httpResponse.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream, Encoding.UTF8);

                // 修复：流读取加超时——服务端卡流不再永久挂起聊天窗口和事件队列。
                // 放宽到 TimeoutSeconds×3（至少 60s）：慢的思考模型可能长时间不吐新块，不能被误杀。
                var readTimeout = TimeSpan.FromSeconds(Math.Max(settings.TimeoutSeconds * 3, 60));
                var streamTimedOut = false;

                while (!reader.EndOfStream)
                {
                    var readTask = reader.ReadLineAsync();
                    if (await Task.WhenAny(readTask, Task.Delay(readTimeout)) != readTask)
                    {
                        streamTimedOut = true;
                        break;
                    }
                    var line = await readTask;
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

                    // 捕获 finish_reason：最后一帧非空（"stop"/"length"等），用于判断是否被 max_tokens 截断
                    var fr = choices["finish_reason"]?.ToString();
                    if (!string.IsNullOrEmpty(fr))
                        roundFinishReason = fr;

                    var deltaContent = delta["content"]?.ToString();
                    if (deltaContent != null)
                    {
                        roundText += deltaContent;
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

                if (streamTimedOut)
                {
                    // 流读取超时：丢弃本轮的半成品工具调用（可能是不完整的 JSON），把已累积文本作为最终回复返回
                    roundToolCalls.Clear();
                    DebugLogger.Log($"LLM 流读取超时 intent={intent} agent={hero?.Name?.ToString() ?? "?"} round={round} textLen={roundText.Length}");
                }

                lastReasoning = roundReasoning;
                lastFinishReason = roundFinishReason;

                // 修复陈旧文本：带"游戏工具"的轮次文本多为过渡说明（如"让我进一步了解…"），
                // 不该作为最终回复的回退值。仅无工具、或仅有维护工具(update_knowledge)的轮次文本
                // 才算有效最终文本（模型常在说完回答后顺手调 update_knowledge 记认知）。
                var hasGameTool = roundToolCalls.Any(rt =>
                {
                    var n = rt["function"]?["name"]?.ToString();
                    return !string.IsNullOrEmpty(n) && n != "update_knowledge";
                });
                if (!string.IsNullOrEmpty(roundText) && !hasGameTool)
                    lastMeaningfulText = roundText;

                var toolNames = roundToolCalls
                    .Select(rt => rt["function"]?["name"]?.ToString())
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToArray();
                DebugLogger.Log($"LLM 轮次 intent={intent} agent={hero?.Name?.ToString() ?? "?"} round={round} textLen={roundText.Length} reasoningLen={roundReasoning.Length} tools=[{string.Join(",", toolNames)}]");

                if (roundToolCalls.Count == 0)
                {
                    var finalContent = !string.IsNullOrEmpty(roundText) ? roundText
                        : !string.IsNullOrEmpty(lastMeaningfulText) ? lastMeaningfulText
                        : (allToolCalls.Count > 0 ? "（已通过工具处理完毕）" : "（领主沉默不语）");
                    // 记录"最终轮无文本"的思维链摘录——无论之前是否调过工具。
                    // 覆盖两类：完全沉默（未调工具）、以及"调工具调查后无结语"（如国王评估后决定不行动）。
                    if (string.IsNullOrEmpty(roundText) && !string.IsNullOrEmpty(roundReasoning))
                        DebugLogger.Log($"LLM 静默结束 intent={intent} agent={hero?.Name?.ToString() ?? "?"} toolsCalled={allToolCalls.Count} reasoning={DebugLogger.Truncate(roundReasoning, 600)}");
                    return new ChatResponse
                    {
                        Content = finalContent,
                        LearnedKnowledge = learnedKnowledge,
                        ToolCalls = allToolCalls,
                        ToolResults = toolResults,
                        LastReasoning = roundReasoning,
                        FinishReason = roundFinishReason
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
                Content = !string.IsNullOrEmpty(lastMeaningfulText) ? lastMeaningfulText
                    : (allToolCalls.Count > 0 ? "（已通过工具处理完毕）" : "（领主沉默不语）"),
                LearnedKnowledge = learnedKnowledge,
                ToolCalls = allToolCalls,
                ToolResults = toolResults,
                LastReasoning = lastReasoning,
                FinishReason = lastFinishReason
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
