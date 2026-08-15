using Newtonsoft.Json.Linq;

namespace AIChronicle
{
    /// <summary>
    /// Qwen（阿里云百炼）= OpenAI 兼容 + 少量扩展（enable_thinking 思考开关、prompt_tokens_details.cached_tokens
    /// 嵌套缓存统计）。思考内容经 reasoning_content 字段返回；多轮工具调用无需回传 reasoning
    /// （preserve_thinking 默认 false，回传反而按输入计费），故 ReturnReasoningInAssistant = false。
    /// 请求构建与流解析全部复用 OpenAICompatibleProvider，依据 Capabilities 自动适配。
    ///
    /// 思考量控制（v2.3.0，用户确认）：Qwen 无 reasoning_effort（强弱），只有 thinking_budget（思考长度上限，
    /// 超过即收尾输出回复）。按官方档位映射 MCM「思考强度」——low→4096、high→16384(medium)、max→262144(xhigh)；
    /// 史官（intent 为 null）按 high 档处理。enable_thinking 恒 true（思考全开）。
    /// </summary>
    public class QwenProvider : OpenAICompatibleProvider
    {
        public override LLMCapabilities Capabilities { get; } = new()
        {
            SupportsReasoningEffort = false,
            ReasoningContentField = "reasoning_content",
            ReturnReasoningInAssistant = false,
            CacheHitField = "prompt_tokens_details.cached_tokens",
            PromptTokensField = "prompt_tokens",
            SupportsStreamOptionsUsage = true,
            ExtraBodyParams = new JObject { ["enable_thinking"] = true }
        };

        /// <summary>把 MCM 思考强度映射为 thinking_budget（官方档位：low=4096 / medium=16384 / xhigh=262144）。</summary>
        protected override void ApplyDynamicBodyParams(JObject payload, LLMRequest request)
        {
            payload["thinking_budget"] = request.ReasoningEffort switch
            {
                "low" => 4096,
                "max" => 262144,
                _ => 16384   // "high" 与史官（null）都落 medium 档
            };
        }
    }
}
