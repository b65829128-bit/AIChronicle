using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIChronicle
{
    /// <summary>
    /// 通用 OpenAI 兼容实现。覆盖所有讲 OpenAI chat/completions 协议的端点
    /// （MiniMax / Qwen / GLM / 豆包 / Ollama / LM Studio 等）。
    ///
    /// 宽容解析是内置的通用行为（非厂商特判）：空 choices:[]、缺失的 reasoning 字段、
    /// 无 index 的 tool_calls 增量等，任何 OpenAI 兼容端点都可能出现，统一无条件容忍。
    /// </summary>
    public class OpenAICompatibleProvider : ILLMProvider
    {
        public virtual LLMCapabilities Capabilities { get; } = new()
        {
            SupportsReasoningEffort = false,
            ReasoningContentField = "reasoning_content",
            ReturnReasoningInAssistant = false,
            CacheHitField = "prompt_tokens_details.cached_tokens",
            CacheMissField = "",
            SupportsStreamOptionsUsage = true
        };

        public virtual HttpRequestMessage BuildRequest(LLMRequest request)
        {
            var payload = new JObject
            {
                ["model"] = request.Model,
                ["messages"] = new JArray(request.Messages),
                ["temperature"] = request.Temperature,
                ["stream"] = request.Stream
            };
            if (Capabilities.UsesMaxCompletionTokens)
                payload["max_completion_tokens"] = request.MaxTokens;
            else
                payload["max_tokens"] = request.MaxTokens;
            if (request.IncludeTools && request.Tools != null)
            {
                payload["tools"] = request.Tools;
                payload["tool_choice"] = "auto";
            }
            if (request.Stream && Capabilities.SupportsStreamOptionsUsage)
                payload["stream_options"] = new JObject { ["include_usage"] = true };
            if (Capabilities.SupportsReasoningEffort && !string.IsNullOrEmpty(request.ReasoningEffort))
                payload["reasoning_effort"] = request.ReasoningEffort;

            // 厂商特有的额外请求体参数（声明式，如 GLM 的 clear_thinking）
            if (Capabilities.ExtraBodyParams != null)
            {
                foreach (var kv in Capabilities.ExtraBodyParams)
                    payload[kv.Key] = kv.Value;
            }

            ApplyDynamicBodyParams(payload, request);

            var http = new HttpRequestMessage(HttpMethod.Post, request.Url)
            {
                Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json")
            };
            http.Headers.Add("Authorization", "Bearer " + request.ApiKey);
            return http;
        }

        public virtual LLMStreamChunk? ParseStreamLine(string jsonLine)
        {
            JObject root;
            try { root = JObject.Parse(jsonLine); }
            catch { return null; }

            var cap = Capabilities;

            var result = new LLMStreamChunk();

            // usage 独立解析：未声明/未返回缓存字段的端点仍有输入与输出用量。
            if (root["usage"] is JObject usageObj)
            {
                result.RawUsageJson = usageObj.ToString(Formatting.None);
                result.Usage = ParseUsage(usageObj);
            }

            // 宽容：末尾 usage 帧可能返回 "choices":[]（空数组），直接跳过。
            var choicesArr = root["choices"] as JArray;
            if (choicesArr == null || choicesArr.Count == 0) return result;

            var choice = choicesArr[0];
            result.FinishReason = choice["finish_reason"]?.ToString() ?? "";

            var delta = choice["delta"];
            if (delta == null) return result;

            var content = delta["content"];
            if (content != null && content.Type != JTokenType.Null)
                result.Text = content.ToString();

            if (!string.IsNullOrEmpty(cap.ReasoningContentField))
            {
                var reasoning = delta[cap.ReasoningContentField];
                if (reasoning != null && reasoning.Type != JTokenType.Null)
                    result.Reasoning = reasoning.ToString();
            }

            if (delta["tool_calls"] is JArray toolCalls)
            {
                foreach (var dtc in toolCalls)
                {
                    var func = dtc["function"];
                    result.ToolCalls.Add(new LLMToolCallDelta
                    {
                        // -1 表示该增量未携带 index 字段（部分非标准端点的宽容情况）
                        Index = dtc["index"]?.ToObject<int>() ?? -1,
                        Id = dtc["id"]?.ToString() ?? "",
                        Name = func?["name"]?.ToString() ?? "",
                        Arguments = func?["arguments"]?.ToString() ?? ""
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// 每请求动态参数钩子（默认无操作）。厂商如需按请求字段决定额外请求体参数
        /// （如 Qwen 按 MCM 思考强度映射 thinking_budget），覆写此方法即可，避免动静态能力声明。
        /// </summary>
        protected virtual void ApplyDynamicBodyParams(JObject payload, LLMRequest request) { }

        private LLMUsage ParseUsage(JObject usage)
        {
            var cap = Capabilities;
            var result = new LLMUsage
            {
                PromptTokens = GetUsageLong(usage, cap.PromptTokensField),
                CompletionTokens = GetUsageLong(usage, cap.CompletionTokensField),
                ReasoningTokens = GetUsageLong(usage, cap.ReasoningTokensField),
                CacheHit = GetUsageLong(usage, cap.CacheHitField),
                CacheMiss = GetUsageLong(usage, cap.CacheMissField)
            };
            if (!result.PromptTokens.HasValue && result.CacheHit.HasValue && result.CacheMiss.HasValue)
                result.PromptTokens = result.CacheHit.Value + result.CacheMiss.Value;
            if (!result.CacheMiss.HasValue && result.PromptTokens.HasValue && result.CacheHit.HasValue)
                result.CacheMiss = System.Math.Max(0, result.PromptTokens.Value - result.CacheHit.Value);
            if (!result.CacheHit.HasValue && result.PromptTokens.HasValue && result.CacheMiss.HasValue)
                result.CacheHit = System.Math.Max(0, result.PromptTokens.Value - result.CacheMiss.Value);
            return result;
        }

        /// <summary>缺失/空字段保持未知；显式返回 0 才记为 0。</summary>
        private static long? GetUsageLong(JObject usage, string dottedPath)
        {
            if (string.IsNullOrEmpty(dottedPath)) return null;
            JToken? cur = usage;
            foreach (var part in dottedPath.Split('.'))
            {
                if (cur is not JObject obj) return null;
                cur = obj[part];
                if (cur == null) return null;
            }
            return cur.Type == JTokenType.Integer ? System.Math.Max(0, cur.Value<long>()) : null;
        }

        public virtual LLMNonStreamResult ParseNonStreamResponse(string body)
        {
            var result = new LLMNonStreamResult();
            JObject root;
            try { root = JObject.Parse(body); }
            catch { return result; }

            if (root["usage"] is JObject usage) result.Usage = ParseUsage(usage);

            var message = root["choices"]?[0]?["message"];
            if (message == null) return result;

            var content = message["content"];
            if (content != null && content.Type != JTokenType.Null)
                result.Content = LLMText.StripThinkTags(content.ToString());

            result.ToolCalls = message["tool_calls"];
            return result;
        }
    }
}
