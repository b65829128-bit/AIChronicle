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
            CacheHitField = "",
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

            // 缓存统计：usage 可能为 null（普通帧）或对象（末尾帧）；字段名由能力声明决定。
            if (root["usage"] is JObject usageObj && !string.IsNullOrEmpty(cap.CacheHitField))
            {
                result.RawUsageJson = usageObj.ToString(Formatting.None);
                result.CacheHit = GetUsageLong(usageObj, cap.CacheHitField);
                if (!string.IsNullOrEmpty(cap.CacheMissField))
                {
                    result.CacheMiss = GetUsageLong(usageObj, cap.CacheMissField);
                }
                else if (!string.IsNullOrEmpty(cap.PromptTokensField))
                {
                    // 端点只报告总输入与缓存命中（如 Qwen 的 prompt_tokens + cached_tokens）→ 未命中 = 总输入 - 命中。
                    var total = GetUsageLong(usageObj, cap.PromptTokensField);
                    result.CacheMiss = total > result.CacheHit ? total - result.CacheHit : 0;
                }
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

        /// <summary>按点分路径读取 usage 嵌套字段（如 "prompt_tokens_details.cached_tokens"），缺失返回 0。</summary>
        private static long GetUsageLong(JObject usage, string dottedPath)        {
            JToken? cur = usage;
            foreach (var part in dottedPath.Split('.'))
            {
                if (cur is not JObject obj) return 0;
                cur = obj[part];
                if (cur == null) return 0;
            }
            return cur.ToObject<long>();
        }

        public virtual LLMNonStreamResult ParseNonStreamResponse(string body)
        {
            var result = new LLMNonStreamResult();
            JObject root;
            try { root = JObject.Parse(body); }
            catch { return result; }

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
