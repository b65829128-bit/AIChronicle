using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIChronicle
{
    public class ToolParamDef
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class ToolDef
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public List<ToolParamDef> Parameters { get; set; } = new();
    }

    /// <summary>一次激活的工具快照：API schema、提示词索引与浏览结果共用筛选规则。
    /// 只有显式 browse_tools 解锁会更新快照；预算和运行中的能力变化不重写已发送的定义。</summary>
    public sealed class ToolCatalog
    {
        private readonly List<ToolDef> _available;
        private readonly HashSet<string> _categories;
        private readonly bool _canBrowse;
        private IReadOnlyList<ToolDef> _visible = Array.Empty<ToolDef>();

        public JArray ApiTools { get; private set; } = new();
        public string SchemaHash { get; private set; } = "";
        public IReadOnlyList<ToolDef> VisibleTools => _visible;

        private static readonly Dictionary<string, string> CategoryNames = new()
        {
            ["universal"] = "通用", ["query"] = "查询", ["social"] = "社交",
            ["movement"] = "行军", ["military"] = "军事", ["diplomacy"] = "外交",
            ["council"] = "朝议", ["file"] = "文件", ["communication"] = "通信", ["meta"] = "元工具"
        };

        private static readonly HashSet<string> SelfReviewExcluded = new(StringComparer.Ordinal)
        {
            "recruit_troops", "upgrade_troops", "buy_food", "send_letter", "wait_at_settlement",
            "query_available_troops", "query_settlement_villages", "update_knowledge", "let_go",
            "query_pending_proposals", "give_item", "request_items", "escort_party"
        };

        private static readonly HashSet<string> ChanceryExcluded = new(StringComparer.Ordinal)
        {
            "update_knowledge", "cancel_action", "create_clan", "create_kingdom",
            "write_file", "append_file", "edit_file", "delete_file", "move_file", "write_chronicle", "record_resolve",
            "send_letter", "send_envoy", "reply_envoy", "submit_secret_advisory", "consult_king", "reply_consult", "change_kingdom",
            "declare_war", "propose_peace", "propose_alliance", "propose_trade", "end_alliance", "end_trade_agreement",
            "respond_to_diplomacy_proposal", "gift_fief", "propose_agenda", "respond_to_agenda", "defend_agenda"
        };

        private static readonly HashSet<string> CuriaQueries = new(StringComparer.Ordinal)
        {
            "query_agenda", "query_policies", "query_character", "query_clan_fiefs",
            "query_war_status", "query_settlement", "query_party_troops", "query_world_state"
        };

        public ToolCatalog(IEnumerable<ToolDef> capabilityTools, string intent, IEnumerable<string>? unlocked = null, bool includeTools = true)
        {
            _categories = new HashSet<string>(DefaultCategories(intent), StringComparer.Ordinal);
            _canBrowse = includeTools && (intent == "conversation" || intent == "chat" || intent == "letter");
            // 固定菜单场景不接受其他会话遗留的解锁记录。
            if (_canBrowse && unlocked != null) _categories.UnionWith(unlocked);
            _available = (includeTools ? capabilityTools : Array.Empty<ToolDef>()).Where(t => IsAllowed(t.Name, intent))
                .GroupBy(t => t.Name, StringComparer.Ordinal).Select(g => Clone(g.First())).ToList();
            Refresh();
        }

        private static IEnumerable<string> DefaultCategories(string intent) => intent switch
        {
            "conversation" => new[] { "universal", "query", "social", "file", "diplomacy", "movement", "military", "communication", "council" },
            "letter" => new[] { "universal", "query", "file", "communication", "movement", "military", "diplomacy", "council" },
            "diplomacy" => new[] { "universal", "query", "diplomacy", "file", "council" },
            "king_consult" => new[] { "universal", "query", "diplomacy", "file" },
            "historian" or "clan_replenishment" or "chancery" => new[] { "universal", "query", "file" },
            "fief_review" => new[] { "universal", "query", "file", "communication", "diplomacy", "council" },
            "curia" => new[] { "universal", "query", "council", "file" },
            "curia_defense" => new[] { "universal", "query", "council", "file", "communication" },
            "consolidation" => new[] { "universal", "file" },
            "chat" => new[] { "universal", "query", "file", "social", "communication" },
            "self_review" => new[] { "universal", "query", "social", "movement", "military", "diplomacy", "communication", "file", "council" },
            "envoy_reply" => new[] { "universal", "query", "file", "communication" },
            _ => new[] { "universal", "query", "social", "military", "movement", "diplomacy", "file", "communication" }
        };

        private static bool IsAllowed(string name, string intent)
        {
            if (intent == "self_review" && SelfReviewExcluded.Contains(name)) return false;
            if (intent == "chancery" && ChanceryExcluded.Contains(name)) return false;
            if ((intent == "curia" || intent == "curia_defense") && IsQueryTool(name) && !CuriaQueries.Contains(name)) return false;
            if (intent == "king_consult" && name == "consult_king") return false;
            return intent != "envoy_reply" || name != "send_envoy";
        }

        public static bool IsQueryTool(string name)
            => name.StartsWith("query_", StringComparison.Ordinal) || name == "browse_tools";

        public IReadOnlyList<ToolDef> GetCategoryTools(string category)
            => _available.Where(t => t.Category == category).ToList().AsReadOnly();

        public bool TryUnlock(string category)
        {
            if (!_canBrowse || !_available.Any(t => t.Category == category)) return false;
            if (_categories.Add(category)) Refresh();
            return true;
        }

        public string BuildFunctionList()
        {
            var sb = new StringBuilder();
            foreach (var group in _visible.GroupBy(t => t.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var label = CategoryNames.TryGetValue(group.Key, out var translated) ? translated : group.Key;
                sb.AppendLine($"【{label}】{string.Join(", ", group.Select(t => t.Name))}");
            }
            var inactive = _available.Where(t => !_categories.Contains(t.Category))
                .GroupBy(t => t.Category).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
            if (_canBrowse && inactive.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("【其他可用工具分类 — 需要时先调 browse_tools 查看并解锁】");
                foreach (var group in inactive)
                {
                    var label = CategoryNames.TryGetValue(group.Key, out var translated) ? translated : group.Key;
                    sb.AppendLine($"  {label}（{group.Count()}个工具）— browse_tools(\"{group.Key}\")");
                }
            }
            return sb.ToString().TrimEnd();
        }

        private void Refresh()
        {
            var visible = _available.Where(t => _categories.Contains(t.Category)).ToList();
            if (_canBrowse) visible.Add(BrowseToolsDef());
            _visible = visible.AsReadOnly();
            ApiTools = new JArray(visible.Select(d => new JObject
            {
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = d.Name,
                    ["description"] = d.Description,
                    ["parameters"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject(d.Parameters.Select(p => new JProperty(p.Name,
                            new JObject { ["type"] = p.Type, ["description"] = p.Description }))),
                        ["required"] = new JArray(d.Parameters.Select(p => p.Name))
                    }
                }
            }));
            using var sha = SHA256.Create();
            SchemaHash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(ApiTools.ToString(Formatting.None))))
                .Replace("-", "").ToLowerInvariant();
        }

        private static ToolDef Clone(ToolDef tool) => new()
        {
            Name = tool.Name, Category = tool.Category, Description = tool.Description,
            Parameters = tool.Parameters.Select(p => new ToolParamDef
                { Name = p.Name, Type = p.Type, Description = p.Description }).ToList()
        };

        private static ToolDef BrowseToolsDef() => new()
        {
            Name = "browse_tools", Category = "meta",
            Description = "Unlock an available tool category for the next response. Only use this for categories not already in your function list. Categories: military, movement, diplomacy, council, file, social, query, communication.",
            Parameters = new List<ToolParamDef>
            {
                new() { Name = "category", Type = "string", Description = "The available category to unlock." }
            }
        };
    }
}
