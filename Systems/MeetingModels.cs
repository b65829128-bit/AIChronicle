using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace AIChronicle
{
    /// <summary>仅表示一次异步调用属于哪场会晤；不得用全局静态 Hero 代替。</summary>
    public static class MeetingContext
    {
        private static readonly AsyncLocal<string?> Current = new();
        public static string? SessionId { get => Current.Value; set => Current.Value = value; }
    }

    public enum MeetingSide { Other, Self }
    public enum MeetingStatus { Negotiating, Executing, Applied, Rejected, Cancelled, Failed, PartiallyApplied, Ready, Running }

    public sealed class MeetingItem
    {
        public string Id { get; set; } = "";
        public int Count { get; set; }
        public MeetingItem Copy() => new() { Id = Id, Count = Count };
    }

    public sealed class MeetingOffer
    {
        public int Gold { get; set; }
        public List<MeetingItem> Items { get; set; } = new();
        public List<MeetingClause> Actions { get; set; } = new();
        public MeetingOffer Copy() => new() { Gold = Gold, Items = Items.Select(i => i.Copy()).ToList(), Actions = Actions.Select(a => a.Copy()).ToList() };
    }

    public sealed class MeetingClause
    {
        public string TypeId { get; set; } = "";
        public JObject Parameters { get; set; } = new();
        public MeetingClause Copy() => new() { TypeId = TypeId, Parameters = (JObject)Parameters.DeepClone() };
    }

    public sealed class MeetingOutcome
    {
        public string Id { get; set; } = "";
        public MeetingOffer OtherOffer { get; set; } = new();
        public MeetingOffer YourOffer { get; set; } = new();
        public MeetingOutcome Copy() => new() { Id = Id, OtherOffer = OtherOffer.Copy(), YourOffer = YourOffer.Copy() };
        public MeetingTerms AsTerms(string typeId) => new() { TypeId = typeId, OtherOffer = OtherOffer.Copy(), YourOffer = YourOffer.Copy() };
    }

    public sealed class MeetingOutcomeSlot
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
    }

    /// <summary>双方财物是公共条款，动作专属参数留给注册的 handler。字段名也是 AI schema 与网页协议。</summary>
    public sealed class MeetingTerms
    {
        public string TypeId { get; set; } = "";
        public MeetingOffer OtherOffer { get; set; } = new();
        public MeetingOffer YourOffer { get; set; } = new();
        public JObject Parameters { get; set; } = new();
        public List<MeetingOutcome> Outcomes { get; set; } = new();
        public MeetingTerms Copy() => new()
        {
            TypeId = TypeId, OtherOffer = OtherOffer.Copy(), YourOffer = YourOffer.Copy(),
            Parameters = (JObject)Parameters.DeepClone(), Outcomes = Outcomes.Select(o => o.Copy()).ToList()
        };

        /// <summary>拒绝缺失字段、浮点数量、未知条款；不让 JSON 转换悄悄把报价取整或忽略内容。</summary>
        public static MeetingTerms Parse(JObject json)
        {
            RequireKeys(json, json["outcomes"] == null ? new[] { "typeId", "parameters", "otherOffer", "yourOffer" }
                : new[] { "typeId", "parameters", "otherOffer", "yourOffer", "outcomes" });
            if (json["typeId"]?.Type != JTokenType.String || !(json["parameters"] is JObject parameters)
                || parameters.Properties().Any(p => p.Value.Type != JTokenType.String))
                throw new ArgumentException("事项编号和专属参数须为文本。");
            ValidateOffersJson(json);
            if (json["outcomes"] != null)
            {
                if (!(json["outcomes"] is JArray outcomes)) throw new ArgumentException("胜负条件须为列表。");
                foreach (var token in outcomes)
                {
                    if (!(token is JObject outcome)) throw new ArgumentException("胜负条件格式不正确。");
                    RequireKeys(outcome, "id", "otherOffer", "yourOffer");
                    if (outcome["id"]?.Type != JTokenType.String) throw new ArgumentException("结果编号须为文本。");
                    ValidateOffersJson(outcome);
                }
            }
            var terms = json.ToObject<MeetingTerms>()!;
            var error = ValidateShape(terms);
            if (error != null) throw new ArgumentException(error);
            return terms;
        }

        private static void ValidateOffersJson(JObject json)
        {
            foreach (var name in new[] { "otherOffer", "yourOffer" })
            {
                if (!(json[name] is JObject offer)) throw new ArgumentException("请填写双方完整报价。");
                // actions 为新增可选字段；旧的空行动报价仍按空列表理解，战役存档格式不变。
                RequireKeys(offer, offer["actions"] == null ? new[] { "gold", "items" } : new[] { "gold", "items", "actions" });
                if (offer["gold"]?.Type != JTokenType.Integer || !(offer["items"] is JArray items))
                    throw new ArgumentException("金币须为整数，物品须为列表。");
                foreach (var token in items)
                {
                    if (!(token is JObject item)) throw new ArgumentException("物品格式不正确。");
                    RequireKeys(item, "id", "count");
                    if (item["id"]?.Type != JTokenType.String || item["count"]?.Type != JTokenType.Integer)
                        throw new ArgumentException("物品须有精确编号和整数数量。");
                }
                if (offer["actions"] != null)
                {
                    if (!(offer["actions"] is JArray actions)) throw new ArgumentException("行动条款须为列表。");
                    foreach (var token in actions)
                    {
                        if (!(token is JObject clause)) throw new ArgumentException("行动条款格式不正确。");
                        RequireKeys(clause, "typeId", "parameters");
                        if (clause["typeId"]?.Type != JTokenType.String || !(clause["parameters"] is JObject args)
                            || args.Properties().Any(p => p.Value.Type != JTokenType.String))
                            throw new ArgumentException("行动条款须有类型编号和文本参数。");
                    }
                }
            }
        }

        private static void RequireKeys(JObject value, params string[] keys)
        {
            if (value.Count != keys.Length || keys.Any(k => value[k] == null))
                throw new ArgumentException("条款字段不完整或包含未知字段。");
        }

        public static string? ValidateShape(MeetingTerms? terms)
        {
            if (terms == null || string.IsNullOrWhiteSpace(terms.TypeId) || terms.TypeId.Length > 80)
                return "请选择有效事项。";
            if (terms.Outcomes == null || terms.Outcomes.Count > 4 || terms.Outcomes.Any(o => o == null
                || string.IsNullOrWhiteSpace(o.Id) || o.Id.Length > 80)
                || terms.Outcomes.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count() != terms.Outcomes.Count)
                return "结果分支须有唯一编号，最多 4 项。";
            foreach (var offer in new[] { terms.OtherOffer, terms.YourOffer }.Concat(terms.Outcomes.SelectMany(o => new[] { o.OtherOffer, o.YourOffer })))
            {
                if (offer == null || offer.Gold < 0 || offer.Items == null || offer.Items.Count > 32 || offer.Actions == null || offer.Actions.Count > 8)
                    return "金币不能为负，单方最多提供 32 种物品和 8 项行动。";
                if (offer.Actions.Any(a => a == null || string.IsNullOrWhiteSpace(a.TypeId) || a.TypeId.Length > 80
                    || a.Parameters == null || a.Parameters.Count > 20 || a.Parameters.ToString().Length > 4000))
                    return "行动条款格式不正确，单方最多承诺 8 项行动。";
                if (offer.Items.Any(i => i == null || string.IsNullOrWhiteSpace(i.Id) || i.Id.Length > 200 || i.Count <= 0))
                    return "物品必须有精确编号和正整数数量。";
                if (offer.Items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != offer.Items.Count)
                    return "同一种物品请合并数量，不要重复列出。";
            }
            if (terms.Parameters == null || terms.Parameters.Count > 20 || terms.Parameters.ToString().Length > 4000
                || terms.Parameters.Properties().Any(p => p.Value.Type != JTokenType.String))
                return "事项参数过长。";
            return null;
        }

        public static bool IsEmpty(MeetingOffer offer) => offer.Gold == 0 && offer.Items.Count == 0 && offer.Actions.Count == 0;

        public static string? ValidateOutcomes(MeetingTerms terms, IReadOnlyList<MeetingOutcomeSlot> slots)
        {
            if (slots.Count == 0) return terms.Outcomes.Count == 0 ? null : "这类事项没有延后结果，不能附带胜负条件。";
            if (terms.Outcomes.Count != slots.Count || slots.Any(s => !terms.Outcomes.Any(o => o.Id == s.Id)))
                return "请填写全部有效结果分支；不附条件的分支也须保留空报价。";
            return !IsEmpty(terms.OtherOffer) || !IsEmpty(terms.YourOffer)
                ? "此事项的财物与行动须写入对应结果分支，接受约定时不提前交付。" : null;
        }
    }

    public sealed class MeetingRequest
    {
        public string Id { get; internal set; } = "";
        public int Version { get; internal set; }
        public MeetingTerms Terms { get; internal set; } = new();
        public MeetingSide ProposedBy { get; internal set; }
        public bool OtherAccepted { get; internal set; }
        public bool YourAccepted { get; internal set; }
        public MeetingStatus Status { get; internal set; }
        public string Reason { get; internal set; } = "";
        public string Result { get; internal set; } = "";
        public MeetingRequest Copy() => new()
        {
            Id = Id, Version = Version, Terms = Terms.Copy(), ProposedBy = ProposedBy,
            OtherAccepted = OtherAccepted, YourAccepted = YourAccepted, Status = Status, Reason = Reason, Result = Result
        };
    }

    public sealed class MeetingChoice
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
    }

    public sealed class MeetingField
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Kind { get; set; } = "choice";
        public int Minimum { get; set; }
        public int Maximum { get; set; } = int.MaxValue;
        public List<MeetingChoice> Choices { get; set; } = new();
    }

    public sealed class MeetingInventoryEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public int ReferenceValue { get; set; }
    }

    public static class MeetingFieldRules
    {
        public static string? Validate(IReadOnlyList<MeetingField> fields, JObject parameters)
        {
            foreach (var property in parameters.Properties())
                if (!fields.Any(f => f.Key == property.Name) || property.Value.Type != JTokenType.String)
                    return "事项包含未知参数或错误格式。";
            foreach (var field in fields)
            {
                var value = parameters[field.Key]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(value)) return "请填写“" + field.Label + "”。";
                if (field.Kind == "choice" && !field.Choices.Any(c => c.Id == value)) return "请选择有效的“" + field.Label + "”。";
                if (field.Kind == "number" && (!int.TryParse(value, out var amount) || amount < field.Minimum || amount > field.Maximum))
                    return "“" + field.Label + "”须为范围内的整数。";
            }
            return null;
        }
    }

    /// <summary>不依赖游戏的报价约束，运行时预检与离线回归共用。</summary>
    public static class MeetingAssetRules
    {
        public static MeetingOffer ReserveMaximum(IEnumerable<MeetingOffer> offers)
        {
            var list = offers.ToList();
            return new MeetingOffer
            {
                Gold = list.Count == 0 ? 0 : list.Max(o => o.Gold),
                Items = list.SelectMany(o => o.Items).GroupBy(i => i.Id, StringComparer.Ordinal)
                    .Select(g => new MeetingItem { Id = g.Key, Count = g.Max(i => i.Count) }).ToList()
            };
        }

        public static string? Validate(MeetingTerms terms, int otherGold, int yourGold,
            IReadOnlyList<MeetingInventoryEntry> otherInventory, IReadOnlyList<MeetingInventoryEntry> yourInventory,
            string otherName = "对方", string yourName = "你")
        {
            var shape = MeetingTerms.ValidateShape(terms);
            if (shape != null) return shape;
            if (otherGold < terms.OtherOffer.Gold) return otherName + "的金币不足以履行报价。";
            if (yourGold < terms.YourOffer.Gold) return yourName + "的金币不足以履行报价。";
            var net = (long)terms.OtherOffer.Gold - terms.YourOffer.Gold;
            if ((long)otherGold - net > int.MaxValue || (long)yourGold + net > int.MaxValue)
                return "成交后的金币超出游戏数值范围。";
            if (terms.OtherOffer.Items.Any(i => terms.YourOffer.Items.Any(j => j.Id == i.Id)))
                return "同一种品质的物品请合并成单向净数量，避免来回交换。";
            foreach (var pair in new[] { (otherInventory, terms.OtherOffer), (yourInventory, terms.YourOffer) })
            {
                var available = pair.Item1.ToDictionary(e => e.Id, StringComparer.Ordinal);
                foreach (var item in pair.Item2.Items)
                    if (!available.TryGetValue(item.Id, out var entry) || entry.Count < item.Count)
                        return "提供方缺少报价中的物品或数量（编号 " + item.Id + "）。";
            }
            foreach (var pair in new[] { (otherInventory, terms.YourOffer), (yourInventory, terms.OtherOffer) })
            {
                var receiving = pair.Item1.ToDictionary(e => e.Id, StringComparer.Ordinal);
                foreach (var item in pair.Item2.Items)
                    if ((long)(receiving.TryGetValue(item.Id, out var entry) ? entry.Count : 0) + item.Count > int.MaxValue)
                        return "收取物品后数量超出游戏范围。";
            }
            return null;
        }
    }
}
