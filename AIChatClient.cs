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
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
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

        private static Hero? _currentHero;

        private sealed class PendingAction
        {
            public Hero Hero = null!;
            public Settlement? TargetSettlement;
            public int WaitHours;
            public CampaignTime? ArrivedAt;
        }

        private static readonly Dictionary<string, PendingAction> _pendingActions = new();

        private sealed class PendingInquiry
        {
            public Hero Hero = null!;
            public int Amount;
            public ManualResetEventSlim Event = new(false);
            public bool Result;
        }

        private static PendingInquiry? _pendingInquiry;

        private static object BuildTools()
        {
            var gameTools = PromptManager.LoadTools();
            var agentTools = PromptManager.LoadAgentTools();

            var all = new List<ToolDef>(gameTools);
            all.AddRange(agentTools);

            return all.Select(d => new
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

        public static string ExecuteToolCall(string name, string arguments)
        {
            try
            {
                var args = JObject.Parse(arguments);
                switch (name)
                {
                    case "read_file":
                        var path = args["path"]?.ToString() ?? "";
                        var lineStart = args["line_start"]?.ToObject<int?>();
                        var lineCount = args["line_count"]?.ToObject<int?>();
                        return AgentManager.ExecuteReadFile(path, lineStart, lineCount);

                    case "append_file":
                        var apath = args["path"]?.ToString() ?? "";
                        var content = args["content"]?.ToString() ?? "";
                        return AgentManager.ExecuteAppendFile(apath, content);

                    case "list_dir":
                        var lpath = args["path"]?.ToString() ?? "";
                        return AgentManager.ExecuteListDir(lpath);

                    case "query_settlement":
                        return QuerySettlement(args["name"]?.ToString() ?? "");

                    case "query_world_state":
                        return QueryWorldState();

                    case "move_to_settlement":
                        return ExecuteMoveToSettlement(args["settlement_name"]?.ToString() ?? "");

                    case "wait_at_settlement":
                        return ExecuteWaitAtSettlement(args["hours"]?.ToObject<int>() ?? 0);

                    case "change_relation":
                        return ExecuteChangeRelation(args["delta"]?.ToObject<int>() ?? 0);

                    case "give_gold_to_player":
                        return ExecuteGiveGoldToPlayer(args["amount"]?.ToObject<int>() ?? 0);

                    case "request_gold_from_player":
                        return ExecuteRequestGoldFromPlayer(args["amount"]?.ToObject<int>() ?? 0);

                    default:
                        return $"未知工具：{name}";
                }
            }
            catch (Exception ex)
            {
                return $"工具执行错误：{ex.Message}";
            }
        }

        private static string QuerySettlement(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "[错误] 请提供定居点名称";

            foreach (var s in Settlement.All)
            {
                var sName = s.Name?.ToString() ?? "";
                if (sName.Contains(name) || name.Contains(sName))
                {
                    var type = s.IsTown ? "城镇" : s.IsCastle ? "城堡" : "村庄";
                    var owner = s.OwnerClan?.Name?.ToString() ?? "无主";
                    var kingdom = s.OwnerClan?.Kingdom?.Name?.ToString();
                    var prosperity = s.IsTown ? s.Town?.Prosperity.ToString("F0") ?? "?" : "-";

                    return $"{sName}（{type}）\n"
                        + $"所属氏族：{owner}\n"
                        + (kingdom != null ? $"所属王国：{kingdom}\n" : "")
                        + $"繁荣度：{prosperity}";
                }
            }

            return $"[未找到] 名称为 \"{name}\" 的定居点";
        }

        private static string QueryWorldState()
        {
            var sb = new System.Text.StringBuilder();

            foreach (var k in Kingdom.All)
            {
                var kName = k.Name?.ToString() ?? "未知";
                var strength = k.CurrentTotalStrength.ToString("F0");
                var ruler = k.Leader?.Name?.ToString() ?? "无";

                sb.AppendLine($"{kName}  国王：{ruler}  总兵力：{strength}");

                foreach (var enemyK in Kingdom.All)
                {
                    if (enemyK == k) continue;
                    if (k.IsAtWarWith(enemyK))
                        sb.AppendLine($"  ⚔ 与 {enemyK.Name} 交战中");
                }
            }

            return sb.ToString().TrimEnd();
        }

        private static Settlement? FindSettlement(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var s in Settlement.All)
            {
                var sName = s.Name?.ToString() ?? "";
                if (sName.Contains(name) || name.Contains(sName))
                    return s;
            }
            return null;
        }

        private static string ExecuteMoveToSettlement(string settlementName)
        {
            if (_currentHero == null)
                return "[错误] 无当前领主";

            var target = FindSettlement(settlementName);
            if (target == null)
                return $"[错误] 未找到名为 \"{settlementName}\" 的定居点";

            if (!target.IsTown && !target.IsCastle)
                return $"[错误] {target.Name} 是村庄，只能移动到城镇或城堡";

            var party = _currentHero.PartyBelongedTo;
            if (party == null)
                return $"[错误] {_currentHero.Name} 没有带领部队（可能在城中担任总督、被俘虏或编入军团）";

            if (!party.IsActive)
                return $"[错误] 部队当前不可用";

            if (party.CurrentSettlement == target)
            {
                var action = GetOrCreateAction(_currentHero);
                action.TargetSettlement = target;
                return $"已经在{target.Name}了。";
            }

            if (party.CurrentSettlement != null)
            {
                party.CurrentSettlement = null;
            }

            var navType = party.IsCurrentlyAtSea
                ? MobileParty.NavigationType.Naval
                : MobileParty.NavigationType.Default;

            party.SetMoveGoToSettlement(target, navType, false);

            var action2 = GetOrCreateAction(_currentHero);
            action2.TargetSettlement = target;

            return $"部队已出发前往{target.Name}。";
        }

        private static string ExecuteWaitAtSettlement(int hours)
        {
            if (_currentHero == null)
                return "[错误] 无当前领主";

            if (hours <= 0)
                return "[错误] 等待时长必须大于 0 小时";

            if (hours > 720)
                return "[错误] 等待时长不能超过 720 小时（30 天）";

            var party = _currentHero.PartyBelongedTo;
            var currentSettlement = party?.CurrentSettlement;

            if (currentSettlement == null)
                return "[错误] {NPC} 当前不在任何定居点内".Replace("{NPC}", _currentHero.Name?.ToString() ?? "领主");

            var key = _currentHero.Id.ToString();
            if (!_pendingActions.TryGetValue(key, out var action))
            {
                action = new PendingAction { Hero = _currentHero, TargetSettlement = currentSettlement };
                _pendingActions[key] = action;
            }

            action.WaitHours = hours;

            if (action.ArrivedAt == null)
                action.ArrivedAt = CampaignTime.Now;

            return $"将在{currentSettlement.Name}停留{hours}小时（约{hours / 24}天）。";
        }

        private static PendingAction GetOrCreateAction(Hero hero)
        {
            var key = hero.Id.ToString();
            if (!_pendingActions.TryGetValue(key, out var action))
            {
                action = new PendingAction { Hero = hero };
                _pendingActions[key] = action;
            }
            return action;
        }

        private static string ExecuteChangeRelation(int delta)
        {
            if (_currentHero == null)
                return "[错误] 无当前领主";

            var maxChange = MySettings.Instance?.MaxRelationChange ?? 5;
            if (Math.Abs(delta) > maxChange)
                delta = Math.Sign(delta) * maxChange;

            if (delta == 0)
                return "[信息] 好感变化为 0，无需操作";

            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(_currentHero, Hero.MainHero, delta, true);
            var currentRelation = _currentHero.GetRelation(Hero.MainHero);

            return $"对玩家的好感变化了{delta:+0;-0}点，当前好感度为{currentRelation}点。";
        }

        private static string ExecuteGiveGoldToPlayer(int amount)
        {
            if (_currentHero == null)
                return "[错误] 无当前领主";

            if (amount <= 0)
                return "[错误] 金币数额必须大于 0";

            if (_currentHero.Gold < amount)
                return $"[错误] {_currentHero.Name} 只有 {_currentHero.Gold} 金币，不足以赠送 {amount} 金币";

            GiveGoldAction.ApplyBetweenCharacters(_currentHero, Hero.MainHero, amount);

            return $"已赠予玩家 {amount} 金币。{_currentHero.Name} 剩余 {_currentHero.Gold} 金币。";
        }

        private static string ExecuteRequestGoldFromPlayer(int amount)
        {
            if (_currentHero == null)
                return "[错误] 无当前领主";

            if (amount <= 0)
                return "[错误] 金币数额必须大于 0";

            if (Hero.MainHero.Gold < amount)
                return $"[错误] 玩家只有 {Hero.MainHero.Gold} 金币，不足以支付 {amount} 金币";

            using var mre = new ManualResetEventSlim(false);
            _pendingInquiry = new PendingInquiry
            {
                Hero = _currentHero,
                Amount = amount,
                Event = mre,
                Result = false
            };

            mre.Wait();

            if (_pendingInquiry.Result)
            {
                GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, _currentHero, amount);
                return $"玩家同意支付 {amount} 金币。";
            }
            return $"玩家拒绝了支付 {amount} 金币的请求。";
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
                () => { inquiry.Result = false; inquiry.Event.Set(); }));
        }

        public static void Tick()
        {
            if (_pendingActions.Count == 0 || Campaign.Current == null)
                return;

            var keysToRemove = new List<string>();

            foreach (var kv in _pendingActions)
            {
                try
                {
                    var action = kv.Value;
                    var hero = action.Hero;
                    if (hero == null)
                    {
                        keysToRemove.Add(kv.Key);
                        continue;
                    }

                    var party = hero.PartyBelongedTo;
                    if (party == null || !party.IsActive)
                    {
                        keysToRemove.Add(kv.Key);
                        continue;
                    }

                    if (action.TargetSettlement != null
                        && party.CurrentSettlement == action.TargetSettlement
                        && action.ArrivedAt == null)
                    {
                        action.ArrivedAt = CampaignTime.Now;
                    }

                    if (action.ArrivedAt != null)
                    {
                        var elapsed = (CampaignTime.Now - action.ArrivedAt.Value).ToHours;
                        if (action.WaitHours <= 0 || elapsed >= action.WaitHours)
                        {
                            keysToRemove.Add(kv.Key);
                            if (action.TargetSettlement != null)
                            {
                                InformationManager.DisplayMessage(new InformationMessage(
                                    $"[MyFirstMod] {hero.Name} 结束了在{action.TargetSettlement.Name}的停留。",
                                    Colors.Cyan));
                            }
                            continue;
                        }
                        continue;
                    }

                    if (action.TargetSettlement == null)
                        continue;

                    var shortTerm = party.ShortTermBehavior;
                    bool isFleeing = shortTerm == AiBehavior.FleeToPoint
                        || shortTerm == AiBehavior.FleeToGate
                        || shortTerm == AiBehavior.FleeToParty;
                    bool isFighting = party.MapEvent != null;

                    if (!isFleeing && !isFighting)
                    {
                        if (party.DefaultBehavior != AiBehavior.GoToSettlement
                            || party.TargetSettlement != action.TargetSettlement)
                        {
                            var navType = party.IsCurrentlyAtSea
                                ? MobileParty.NavigationType.Naval
                                : MobileParty.NavigationType.Default;
                            party.SetMoveGoToSettlement(action.TargetSettlement, navType, false);
                        }
                    }
                }
                catch
                {
                    keysToRemove.Add(kv.Key);
                }
            }

            foreach (var key in keysToRemove)
                _pendingActions.Remove(key);
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

            var response = await _client.SendAsync(request);
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

        public static async Task<ChatResponse> SendMessage(CharacterPrompt charPrompt, Hero? hero = null, bool includeTools = true)
        {
            _currentHero = hero;
            var settings = MySettings.Instance!;
            var systemPrompt = hero != null
                ? PromptManager.BuildAgentSystemPrompt(hero, charPrompt)
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
                                    ["name"] = funcDelta["name"].ToString(),
                                    ["arguments"] = funcDelta["arguments"]?.ToString() ?? ""
                                };
                            }
                            else if (funcDelta?["arguments"] != null)
                            {
                                var func = existing["function"] as JObject;
                                if (func != null)
                                    func["arguments"] = (func["arguments"]?.ToString() ?? "") + funcDelta["arguments"].ToString();
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
                messageList.Add(new { role = "assistant", content = roundText, tool_calls = roundToolCallsObj });

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
                        var toolResult = ExecuteToolCall(funcName, argsStr);
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

            var response = await _client.SendAsync(request);
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
