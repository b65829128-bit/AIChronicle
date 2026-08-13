namespace AIChronicle
{
    /// <summary>
    /// DeepSeek = OpenAI 兼容 + 少量扩展（reasoning_content 回传、prompt_cache_hit_tokens 缓存字段、
    /// reasoning_effort 参数）。不是重写协议，只是用能力声明打开这些扩展——请求构建与流解析
    /// 全部复用 OpenAICompatibleProvider，依据 Capabilities 自动适配。
    /// </summary>
    public sealed class DeepSeekProvider : OpenAICompatibleProvider
    {
        public override LLMCapabilities Capabilities { get; } = new()
        {
            SupportsReasoningEffort = true,
            ReasoningContentField = "reasoning_content",
            ReturnReasoningInAssistant = true,
            CacheHitField = "prompt_cache_hit_tokens",
            CacheMissField = "prompt_cache_miss_tokens",
            SupportsStreamOptionsUsage = true
        };
    }
}
