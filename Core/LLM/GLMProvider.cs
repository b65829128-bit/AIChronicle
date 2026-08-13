using Newtonsoft.Json.Linq;

namespace AIChronicle
{
    /// <summary>
    /// GLM（智谱）= OpenAI 兼容 + reasoning_content 回传（保留式思考）+ reasoning_effort。
    /// 与 DeepSeek 机制几乎一致，唯一差异：标准 API 默认关闭「保留式思考」，需 clear_thinking: false 开启。
    /// 缓存字段是嵌套的 usage.prompt_tokens_details.cached_tokens（非平铺），当前能力模型不做缓存统计（仅日志）。
    /// </summary>
    public sealed class GLMProvider : DeepSeekProvider
    {
        public override LLMCapabilities Capabilities { get; } = new()
        {
            SupportsReasoningEffort = true,
            ReasoningContentField = "reasoning_content",
            ReturnReasoningInAssistant = true,
            CacheHitField = "",
            CacheMissField = "",
            SupportsStreamOptionsUsage = true,
            ExtraBodyParams = new JObject { ["clear_thinking"] = false }
        };
    }
}
