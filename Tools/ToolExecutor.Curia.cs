using System;
using TaleWorlds.CampaignSystem;

namespace AIChronicle
{
    public static partial class ToolExecutor
    {
        private static string ExecuteProposeAgenda(string type, string targetKingdom, string settlementName,
            string newOwnerEntityId, string targetClanName, string targetPolicy, string reason)
        {
            var hero = AIChatClient.CurrentHero;
            if (hero == null) return "[错误] 无当前领主";
            return CuriaService.Propose(hero, type, targetKingdom, settlementName, newOwnerEntityId, targetClanName, targetPolicy, reason, out var message)
                ? message
                : message;
        }

        private static string ExecuteRespondToAgenda(string agendaId, string stanceText, string reason)
        {
            var hero = AIChatClient.CurrentHero;
            if (hero == null) return "[错误] 无当前领主";
            if (!CuriaText.TryParseStance(stanceText, out var stance))
                return "[错误] 立场必须是 support / oppose / abstain（或 支持/反对/弃权）";
            return CuriaService.SubmitVote(hero, agendaId, stance, reason, out var message)
                ? message
                : message;
        }

        private static string ExecuteDefendAgenda(string agendaId, string reason)
        {
            var hero = AIChatClient.CurrentHero;
            if (hero == null) return "[错误] 无当前领主";
            return CuriaService.SubmitDefense(hero, agendaId, reason, out var message)
                ? message
                : message;
        }

        private static string ExecuteQueryPolicies(string policyName)
        {
            var hero = AIChatClient.CurrentHero;
            if (hero == null) return "[错误] 无当前领主";
            return CuriaService.QueryPoliciesForHero(hero, policyName);
        }

        private static string ExecuteQueryAgenda(string agendaId, bool history, bool detail)
        {
            var hero = AIChatClient.CurrentHero;
            if (hero == null) return "[错误] 无当前领主";
            return CuriaService.QueryForHero(hero, agendaId, history, detail);
        }
    }
}
