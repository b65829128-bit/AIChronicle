using System;

namespace AIChronicle
{
    /// <summary>场景生效连接信息（URL / Model / APIKey / 接口类型）。</summary>
    public readonly record struct ConnectionInfo(string Url, string Model, string ApiKey, LLMProviderKind ProviderKind);

    /// <summary>
    /// 场景连接解析器：把 intent（场景）映射到生效的 URL/Model/APIKey/接口类型。
    /// 每个字段独立兜底——场景配置里留空的字段回退到全局「连接设置（兜底）」，逐字段判定。
    /// 接口类型同样是逐字段兜底：场景「接口类型」留空/跟随兜底 → 用全局兜底的接口类型。
    /// </summary>
    public static class ConnectionResolver
    {
        /// <summary>按 intent 解析生效连接。未知/兜底场景直接返回全局连接设置。</summary>
        public static ConnectionInfo Resolve(string intent)
        {
            var s = MySettings.Instance!;

            // 逐字段兜底：场景字段留空 → 用全局兜底对应字段
            ConnectionInfo C(string url, string model, string key, string providerType) => new(
                string.IsNullOrWhiteSpace(url) ? s.ApiUrl : url,
                string.IsNullOrWhiteSpace(model) ? s.Model : model,
                string.IsNullOrWhiteSpace(key) ? s.ApiKey : key,
                s.ResolveProviderKind(providerType));

            return intent switch
            {
                // 对话与书信共享一个场景（两者本就同属 chat_logs 单一线程）
                "conversation" or "letter" => C(s.ChatApiUrl, s.ChatModel, s.ChatApiKey, s.ChatProviderType.SelectedValue),
                "diplomacy" => C(s.DiplomacyApiUrl, s.DiplomacyModel, s.DiplomacyApiKey, s.DiplomacyProviderType.SelectedValue),
                "king_consult" => C(s.KingConsultApiUrl, s.KingConsultModel, s.KingConsultApiKey, s.KingConsultProviderType.SelectedValue),
                "fief_review" => C(s.FiefReviewApiUrl, s.FiefReviewModel, s.FiefReviewApiKey, s.FiefReviewProviderType.SelectedValue),
                // 自省与密使回应复用「封臣谏言」场景连接（自省即谏言槽位泛化，不新增配置组）
                "advisory" or "self_review" or "envoy_reply" => C(s.AdvisoryApiUrl, s.AdvisoryModel, s.AdvisoryApiKey, s.AdvisoryProviderType.SelectedValue),
                "clan_replenishment" => C(s.ClanReplenishmentApiUrl, s.ClanReplenishmentModel, s.ClanReplenishmentApiKey, s.ClanReplenishmentProviderType.SelectedValue),
                "historian" => C(s.HistorianApiUrl, s.HistorianModel, s.HistorianApiKey, s.HistorianProviderType.SelectedValue),
                "consolidation" => C(s.ConsolidationApiUrl, s.ConsolidationModel, s.ConsolidationApiKey, s.ConsolidationProviderType.SelectedValue),
                "chat" => C(s.CheckInApiUrl, s.CheckInModel, s.CheckInApiKey, s.CheckInProviderType.SelectedValue),
                "chancery" => C(s.ChanceryApiUrl, s.ChanceryModel, s.ChanceryApiKey, s.ChanceryProviderType.SelectedValue),
                _ => new ConnectionInfo(s.ApiUrl, s.Model, s.ApiKey, s.ProviderType)
            };
        }

        /// <summary>场景显示名（MCM 测试提示与弹窗用）。"default" 表示全局兜底。</summary>
        public static string DisplayName(string intent) => intent switch
        {
            "conversation" or "letter" => "对话与书信",
            "diplomacy" => "政务外交",
            "king_consult" => "外交问询",
            "fief_review" => "封地审视",
            "advisory" => "封臣谏言",
            "self_review" => "封臣自省",
            "envoy_reply" => "密使回应",
            "clan_replenishment" => "天意建族",
            "historian" => "史官",
            "consolidation" => "记忆巩固",
            "chat" => "签到",
            "chancery" => "秘书处",
            _ => "兜底"
        };
    }
}
