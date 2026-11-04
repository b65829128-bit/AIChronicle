using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace AIChronicle
{
    public static class MeetingPageBuilder
    {
        private static readonly JsonSerializer Camel = new() { ContractResolver = new CamelCasePropertyNamesContractResolver() };

        public static JObject BuildData()
        {
            var s = MeetingService.Current;
            if (s == null) return new JObject { ["closed"] = true };
            return JObject.FromObject(new
            {
                sessionId = s.Id,
                otherName = s.Player.Name.ToString(), yourName = s.Npc.Name.ToString(),
                yourClan = s.Npc.Clan?.Name?.ToString() ?? "无家族", yourTitle = s.Npc.Clan?.Leader == s.Npc ? "家族领袖" : "家族成员",
                relation = s.Player.GetRelation(s.Npc), otherGold = s.Player.Gold, yourGold = s.Npc.Gold,
                otherInventory = MeetingAssets.Inventory(s.Player), yourInventory = MeetingAssets.Inventory(s.Npc),
                otherClauseTypes = ClauseTypes(s, MeetingSide.Other), yourClauseTypes = ClauseTypes(s, MeetingSide.Self),
                inventoryHint = "只列领队可处置的部队物资；保留物品品质，不含穿戴装备与任务物品。标价仅供参考，实际条件由双方协商。",
                types = MeetingRegistry.All.Select(a => new
                {
                    id = a.Id, title = a.Title, description = a.Description,
                    unavailable = a.Unavailable(s) ?? (a.Fields(s).Any(f => f.Kind == "choice" && f.Choices.Count == 0) ? "当前没有符合条件的人选。" : ""),
                    fields = a.Fields(s)
                }).ToArray(),
                request = s.Negotiation.Request,
                description = s.Summary,
                statuses = new { negotiating = "协商中", executing = "执行中", applied = "已成交", rejected = "已拒绝", cancelled = "已撤回", failed = "执行失败", partiallyApplied = "部分履行" }
            }, Camel);
        }

        private static object[] ClauseTypes(MeetingSession session, MeetingSide side)
        {
            var context = new MeetingClauseContext(session, side);
            return MeetingClauses.All.Select(h => (object)new
            {
                id = h.Id, title = h.Title, description = h.Description,
                unavailable = h.Unavailable(context) ?? (h.Fields(context).Any(f => f.Kind == "choice" && f.Choices.Count == 0) ? "当前没有有效目标。" : ""),
                fields = h.Fields(context)
            }).ToArray();
        }

        public static string BuildContext()
        {
            var data = BuildData();
            var s = MeetingService.Current;
            if (s == null) return "会晤已结束。";
            var path = Path.Combine(PromptManager.CampaignDir, "meeting_rules.txt");
            if (!File.Exists(path)) path = Path.Combine(PromptManager.PromptsBaseDir, "meeting_rules.txt");
            var rules = File.Exists(path) ? SafeFileIO.ReadAllText(path) : "会晤条款必须通过 propose_meeting / respond_meeting 表达，只有双方接受同一版本才生效。";
            data.Remove("description"); // UI 侧的摘要不当作另一条有独立视角的系统事实。
            var current = new JObject
            {
                ["request"] = data["request"]?.DeepClone(),
                ["summary"] = s.Summary,
                ["otherName"] = data["otherName"]?.DeepClone(), ["yourName"] = data["yourName"]?.DeepClone(),
                ["otherGold"] = data["otherGold"]?.DeepClone(), ["yourGold"] = data["yourGold"]?.DeepClone()
            };
            if (current["request"] is JObject request)
                request["statusName"] = s.Negotiation.Request!.Status.ToString();
            data.Remove("request");
            // 字段始终相对于当前 Agent：other 是对方，your 是你，不随发起方倒置。
            return "【当前面对面会晤】\n" + current.ToString(Formatting.None) + "\n" + rules + "\n"
                + "字段方向固定：otherOffer 是对方提供的财物和承诺履行的行动，yourOffer 是你提供的财物和承诺履行的行动。"
                + "request.proposedBy 的 0 表示对方提出，1 表示你提出；otherAccepted 表示对方接受，yourAccepted 表示你接受。"
                + "人物和物品必须用以下真实 id；参考标价不强制成交价。\n"
                + data.ToString(Formatting.None);
        }
    }
}
