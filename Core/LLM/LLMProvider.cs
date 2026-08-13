using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AIChronicle
{
    /// <summary>
    /// 厂商能力声明：厂商差异的"声明式"表达。由 MCM「接口类型」显式选择，配置时一次性确定，
    /// 运行时只读、不做探测（禁止"先发错参数再看 400 回退"的试探式兼容）。
    /// </summary>
    public sealed class LLMCapabilities
    {
        /// <summary>是否接受 reasoning_effort 参数（DeepSeek 支持；多数 OpenAI 兼容端点不支持）。</summary>
        public bool SupportsReasoningEffort { get; init; }

        /// <summary>流式 delta 中思考内容的字段名（如 "reasoning_content"）；空字符串 = 端点不返回思考内容。</summary>
        public string ReasoningContentField { get; init; } = "";

        /// <summary>assistant 消息是否回传思考字段（DeepSeek 需要回传才能续接跨轮思考；多数端点不回传以免 400）。</summary>
        public bool ReturnReasoningInAssistant { get; init; }

        /// <summary>usage 对象中缓存命中 token 的字段名（如 DeepSeek 的 "prompt_cache_hit_tokens"）；空 = 无缓存统计。</summary>
        public string CacheHitField { get; init; } = "";

        /// <summary>usage 对象中缓存未命中 token 的字段名；空 = 无缓存统计。</summary>
        public string CacheMissField { get; init; } = "";

        /// <summary>是否发送 stream_options.include_usage（OpenAI 标准，绝大多数端点支持）。</summary>
        public bool SupportsStreamOptionsUsage { get; init; } = true;

        /// <summary>额外的请求体参数（厂商特有参数，如 GLM 的 clear_thinking），构建请求时浅合并到 payload。</summary>
        public JObject? ExtraBodyParams { get; init; }
    }

    /// <summary>归一化的流式增量块。provider 负责把厂商差异解析成这个统一结构，业务代码不感知厂商。</summary>
    public sealed class LLMStreamChunk
    {
        public string Text = "";
        public string Reasoning = "";
        public List<LLMToolCallDelta> ToolCalls = new();
        public string FinishReason = "";
        public long CacheHit;
        public long CacheMiss;
    }

    /// <summary>流式工具调用的一条增量（业务代码按 Index 跨 chunk 累积）。</summary>
    public sealed class LLMToolCallDelta
    {
        public int Index;
        public string Id = "";
        public string Name = "";
        public string Arguments = "";
    }

    /// <summary>统一请求描述。业务代码填充，provider 负责转成厂商实际请求体。</summary>
    public sealed class LLMRequest
    {
        public string Url = "";
        public string Model = "";
        public string ApiKey = "";
        public List<JToken> Messages = new();
        public int MaxTokens = 32768;
        public float Temperature = 0.8f;
        public bool Stream = true;
        public bool IncludeTools;
        public JToken? Tools;
        /// <summary>思考强度（MCM 设置）。provider 根据 SupportsReasoningEffort 决定是否写入请求。</summary>
        public string? ReasoningEffort;
    }

    /// <summary>非流式响应结果（persona 生成 / 连接测试用）。</summary>
    public sealed class LLMNonStreamResult
    {
        public string Content = "";
        public JToken? ToolCalls;
    }

    /// <summary>
    /// LLM 厂商抽象。厂商差异（参数、字段名、思考开关）全部封装在 provider 实现里；
    /// 业务代码只面对统一接口，禁止在业务逻辑里写厂商 if / 运行时探测。
    /// </summary>
    public interface ILLMProvider
    {
        LLMCapabilities Capabilities { get; }

        /// <summary>构建厂商请求（含 Authorization 头）。</summary>
        HttpRequestMessage BuildRequest(LLMRequest request);

        /// <summary>解析一行 SSE data（不含 "data:" 前缀），返回归一化增量；非内容行返回 null。</summary>
        LLMStreamChunk? ParseStreamLine(string jsonLine);

        /// <summary>解析非流式响应体（persona 生成 / 连接测试用）。</summary>
        LLMNonStreamResult ParseNonStreamResponse(string body);
    }

    /// <summary>接口类型（MCM「接口类型」显式下拉）。厂商差异由用户配置时一次性声明，不做运行时探测。</summary>
    public enum LLMProviderKind
    {
        OpenAICompatible = 0,
        DeepSeek = 1,
        GLM = 2,
    }

    /// <summary>Provider 工厂：按接口类型返回对应实现。新增厂商（非 OpenAI 兼容）在此扩展。</summary>
    public static class LLMProviders
    {
        public static ILLMProvider Create(LLMProviderKind type) => type switch
        {
            LLMProviderKind.DeepSeek => new DeepSeekProvider(),
            LLMProviderKind.GLM => new GLMProvider(),
            _ => new OpenAICompatibleProvider()
        };
    }

    /// <summary>厂商无关的文本清理工具（内容后处理，非厂商特判）。</summary>
    public static class LLMText
    {
        /// <summary>
        /// 剥离模型内联输出到 content 的思考标签（如 MiniMax 的 &lt;think&gt;...&lt;/think&gt;、&lt;thinking&gt;）。
        /// DeepSeek 用独立的 reasoning_content 字段、content 不含此标签，本方法对其为 no-op。
        /// </summary>
        public static string StripThinkTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var result = Regex.Replace(text, @"<think[^>]*>[\s\S]*?</think>", "", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<thinking[^>]*>[\s\S]*?</thinking>", "", RegexOptions.IgnoreCase);
            return result.Trim();
        }
    }
}
