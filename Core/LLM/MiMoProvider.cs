using Newtonsoft.Json.Linq;

namespace AIChronicle
{
    /// <summary>
    /// MiMo（小米）= OpenAI 兼容 + reasoning_content 回传 + 缓存机制。
    /// 与 DeepSeek 机制几乎一致，但差异有三：①思考用 thinking 开关（非 reasoning_effort，默认开启）；
    /// ②最大输出用 max_completion_tokens（非 max_tokens）；③多轮工具调用必须回传 reasoning_content（否则 400）。
    /// 缓存字段名（prompt_tokens_details 内的 key）待实测确认，先留空不做缓存统计。
    /// </summary>
    public sealed class MiMoProvider : DeepSeekProvider
    {
        public override LLMCapabilities Capabilities { get; } = new()
        {
            SupportsReasoningEffort = false,
            ReasoningContentField = "reasoning_content",
            ReturnReasoningInAssistant = true,
            CacheHitField = "",
            CacheMissField = "",
            SupportsStreamOptionsUsage = true,
            UsesMaxCompletionTokens = true,
            ExtraBodyParams = new JObject { ["thinking"] = new JObject { ["type"] = "enabled" } }
        };
    }
}
