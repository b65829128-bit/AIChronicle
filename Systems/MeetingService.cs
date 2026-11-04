using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;

namespace AIChronicle
{
    /// <summary>所有游戏对象访问和请求状态改变均在主线程；后台通过带会话编号的工具入口访问。</summary>
    public static class MeetingService
    {
        public static MeetingSession? Current { get; private set; }
        public static event Action? Changed;
        public static event Action<string>? Recorded;

        public static MeetingSession Open(Hero npc)
        {
            Close();
            if (Hero.MainHero == null || npc == Hero.MainHero || !npc.IsAlive)
                throw new InvalidOperationException("当前没有可会晤的对象。");
            Current = new MeetingSession(Hero.MainHero, npc);
            return Current;
        }

        public static bool IsActive(string? id, Hero? npc = null)
        {
            var s = Current;
            return s != null && id == s.Id && !s.Negotiation.Closed && s.Epoch == MainThreadExecutor.CampaignEpoch
                && s.Player == Hero.MainHero && s.Player.IsAlive && s.Npc.IsAlive
                && (npc == null || s.Npc == npc) && ReferenceEquals(s.Encounter, PlayerEncounter.Current)
                && Hero.OneToOneConversationHero == s.Npc
                && Campaign.Current?.ConversationManager?.IsConversationInProgress == true;
        }

        public static void Close()
        {
            var session = Current;
            var pending = session?.Negotiation.Request?.Status == MeetingStatus.Negotiating;
            session?.Negotiation.Close();
            if (session != null && pending)
            {
                var result = "会晤结束，当前提议撤销，双方没有交付财物或执行约定。";
                Record(session, result);
                try
                {
                    AgentManager.AppendSystemDiary(EntityManager.GetOrCreateEntity(session.Npc).Id, "结果",
                        "与" + session.Player.Name + "会晤：" + result + " 原条件：" + session.Summary);
                }
                catch (Exception ex) { UiTrace.Log("Meeting close diary failed: " + ex); }
            }
            Current = null;
        }

        public static string? Validate(MeetingSession s, MeetingTerms terms)
        {
            if (!IsActive(s.Id)) return "这场会晤已经结束。";
            var invalid = MeetingTerms.ValidateShape(terms);
            if (invalid != null) return invalid;
            var action = MeetingRegistry.Find(terms.TypeId);
            if (action == null) return "此事项尚未开放。";
            invalid = action.Unavailable(s);
            if (invalid != null) return invalid;
            return MeetingFieldRules.Validate(action.Fields(s), terms.Parameters)
                ?? MeetingAssets.Validate(s, terms) ?? action.Validate(s, terms) ?? MeetingClauses.Validate(s, terms);
        }

        public static string Propose(string sessionId, MeetingSide side, MeetingTerms terms, string requestId, int version, string reason)
        {
            var s = Current;
            if (s == null || !IsActive(sessionId)) return "[错误] 会晤已结束。";
            var error = s.Negotiation.Propose(side, terms, requestId, version, reason, t => Validate(s, t));
            if (error != null) return "[错误] " + error;
            var request = s.Negotiation.Request!;
            s.Summary = Describe(s, request.Terms); // 固定物品/人物的可读名称，成交后库存和家族会变化。
            Record(s, $"{(side == MeetingSide.Other ? s.Player.Name.ToString() : s.Npc.Name.ToString())}提出{MeetingRegistry.Find(terms.TypeId)!.Title}（第{request.Version}版）："
                + s.Summary + (string.IsNullOrWhiteSpace(reason) ? "" : "；理由：" + request.Reason));
            NotifyChanged();
            return "提议已显示在交涉卡，等待另一方接受。" + RequestIdentity(request);
        }

        public static string Respond(string sessionId, MeetingSide side, string requestId, int version, string decision, string reason)
        {
            var s = Current;
            if (s == null || !IsActive(sessionId)) return "[错误] 会晤已结束。";
            var before = s.Negotiation.Request;
            var description = before == null ? "" : s.Summary;
            var error = s.Negotiation.Respond(side, requestId, version, decision, reason,
                t => Validate(s, t), t =>
                {
                    try { return MeetingAssets.Commit(s, t, MeetingRegistry.Find(t.TypeId)!); }
                    catch (Exception ex) { UiTrace.Log("Meeting commit failed " + requestId + ": " + ex); throw; }
                });
            if (error != null) return "[错误] " + error;
            var request = s.Negotiation.Request!;
            var result = request.Status switch
            {
                MeetingStatus.Applied => "约定已生效：" + request.Result + " " + description,
                MeetingStatus.Failed => request.Result,
                MeetingStatus.PartiallyApplied => request.Result,
                MeetingStatus.Rejected => "这份提议已被拒绝。" + request.Reason,
                MeetingStatus.Cancelled => "提议者已撤回这份提议。" + request.Reason,
                _ => "回应已记录。"
            };
            Record(s, result);
            if (request.Status == MeetingStatus.Applied || request.Status == MeetingStatus.Failed || request.Status == MeetingStatus.PartiallyApplied)
            {
                AgentManager.AppendSystemDiary(EntityManager.GetOrCreateEntity(s.Npc).Id, "结果", "与" + s.Player.Name + "会晤：" + result);
                if (request.Status == MeetingStatus.Applied)
                {
                    try { MeetingRegistry.Find(request.Terms.TypeId)!.RecordApplied(s, request.Terms, request.Result); }
                    catch (Exception ex) { UiTrace.Log("Meeting applied record failed: " + ex); }
                }
            }
            NotifyChanged();
            return result + RequestIdentity(request);
        }

        public static string Describe(MeetingSession s, MeetingTerms terms)
            => MeetingRegistry.Find(terms.TypeId)?.Describe(s, terms) + " "
                + s.Player.Name + "提供：" + MeetingClauses.Describe(s, MeetingSide.Other, terms.OtherOffer)
                + "；" + s.Npc.Name + "提供：" + MeetingClauses.Describe(s, MeetingSide.Self, terms.YourOffer);

        private static string RequestIdentity(MeetingRequest request) => $" 请求编号={request.Id}，版本={request.Version}。";

        private static void Record(MeetingSession s, string text)
        {
            try { Recorded?.Invoke("[会晤] " + text); }
            catch (Exception ex) { UiTrace.Log("Meeting record failed: " + ex); }
        }

        private static void NotifyChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception ex) { UiTrace.Log("Meeting refresh failed: " + ex); }
        }
    }
}
