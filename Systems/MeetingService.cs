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
            var pending = session?.Negotiation.Request?.Status == MeetingStatus.Negotiating || session?.Negotiation.Request?.Status == MeetingStatus.Ready;
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
            var deferred = action as IMeetingDeferredAction;
            invalid = MeetingFieldRules.Validate(action.Fields(s), terms.Parameters) ?? action.Validate(s, terms)
                ?? MeetingTerms.ValidateOutcomes(terms, deferred?.OutcomeSlots(s) ?? Array.Empty<MeetingOutcomeSlot>());
            if (invalid != null) return invalid;
            if (deferred == null) return MeetingAssets.Validate(s, terms) ?? MeetingClauses.Validate(s, terms);
            foreach (var outcome in terms.Outcomes)
            {
                var branch = outcome.AsTerms(terms.TypeId);
                invalid = MeetingAssets.Validate(s, branch) ?? MeetingClauses.Validate(s, branch, includePrimary: false);
                if (invalid != null) return deferred.OutcomeSlots(s).First(slot => slot.Id == outcome.Id).Title + "：" + invalid;
            }
            return null;
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
            var deferred = before == null ? null : MeetingRegistry.Find(before.Terms.TypeId) as IMeetingDeferredAction;
            var description = before == null ? "" : s.Summary;
            var error = s.Negotiation.Respond(side, requestId, version, decision, reason,
                t => Validate(s, t), t =>
                {
                    try { return deferred != null ? deferred.Confirm(s, t) : MeetingAssets.Commit(s, t, MeetingRegistry.Find(t.TypeId)!); }
                    catch (Exception ex) { UiTrace.Log("Meeting commit failed " + requestId + ": " + ex); throw; }
                }, deferred != null);
            if (error != null) return "[错误] " + error;
            var request = s.Negotiation.Request!;
            var result = request.Status switch
            {
                MeetingStatus.Applied => "约定已生效：" + request.Result + " " + description,
                MeetingStatus.Failed => request.Result,
                MeetingStatus.PartiallyApplied => request.Result,
                MeetingStatus.Rejected => "这份提议已被拒绝。" + request.Reason,
                MeetingStatus.Cancelled => "提议者已撤回这份提议。" + request.Reason,
                MeetingStatus.Ready => "双方已确认约定，等待开始。胜负条件尚未交付。 " + description,
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
        {
            var action = MeetingRegistry.Find(terms.TypeId)!;
            var description = action.Describe(s, terms);
            if (!(action is IMeetingDeferredAction deferred)) return description + " " + DescribeOffers(s, terms.OtherOffer, terms.YourOffer);
            return description + " " + string.Join("；", terms.Outcomes.Select(o => deferred.OutcomeSlots(s).FirstOrDefault(slot => slot.Id == o.Id)?.Title
                + "时：" + DescribeOffers(s, o.OtherOffer, o.YourOffer)));
        }

        private static string DescribeOffers(MeetingSession s, MeetingOffer other, MeetingOffer yours)
            => s.Player.Name + "提供：" + MeetingClauses.Describe(s, MeetingSide.Other, other)
                + "；" + s.Npc.Name + "提供：" + MeetingClauses.Describe(s, MeetingSide.Self, yours);

        public static string StartDeferred(string sessionId, string requestId, int version)
        {
            var s = Current;
            if (s == null || !IsActive(sessionId)) return "[错误] 会晤已结束。";
            var action = s.Negotiation.Request == null ? null : MeetingRegistry.Find(s.Negotiation.Request.Terms.TypeId) as IMeetingDeferredAction;
            if (action == null) return "[错误] 当前事项没有待开始的过程。";
            var error = s.Negotiation.StartDeferred(requestId, version, t => Validate(s, t), r => action.Start(s, r));
            if (error != null) { NotifyChanged(); return "[错误] " + error; }
            Record(s, "约定已锁定，即将开始；只在实际结果确定后交付对应条件。 " + s.Summary);
            NotifyChanged();
            return "已准备开始。";
        }

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
