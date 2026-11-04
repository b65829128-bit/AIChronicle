using Newtonsoft.Json.Linq;

namespace AIChronicle
{
    public static partial class ToolExecutor
    {
        private static string? MeetingToolError()
            => AIChatClient.CurrentIntent != "conversation" || !MeetingService.IsActive(MeetingContext.SessionId, AIChatClient.CurrentHero)
                ? "[错误] 此工具只供当前面对面会晤使用。" : null;

        private static string ExecuteQueryMeeting()
            => MeetingToolError() ?? MeetingPageBuilder.BuildContext();

        private static string ExecuteProposeMeeting(JObject args)
        {
            var error = MeetingToolError();
            if (error != null) return error;
            var terms = args["terms"] as JObject;
            if (terms == null) return "[错误] terms 必须是完整的结构化条款对象。";
            return MeetingService.Propose(MeetingContext.SessionId!, MeetingSide.Self,
                MeetingTerms.Parse(terms), args["request_id"]?.ToString() ?? "", args["version"]?.ToObject<int>() ?? 0,
                args["reason"]?.ToString() ?? "");
        }

        private static string ExecuteRespondMeeting(JObject args)
            => MeetingToolError() ?? MeetingService.Respond(MeetingContext.SessionId!, MeetingSide.Self,
                args["request_id"]?.ToString() ?? "", args["version"]?.ToObject<int>() ?? 0,
                args["decision"]?.ToString() ?? "", args["reason"]?.ToString() ?? "");
    }
}
