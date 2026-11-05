using System;

namespace AIChronicle
{
    /// <summary>资产先提交，动作后执行；动作尚未生效时补偿资产，已生效的动作绝不重试。</summary>
    public static class MeetingCommit
    {
        public static Exception? Apply(Action transfer, Action rollback, Action effect, Func<bool> effectApplied)
        {
            var returned = false;
            var errors = MeetingBatch.Apply(transfer, rollback, new[] { new MeetingEffect
            {
                Name = "约定行动", Apply = () => { effect(); returned = true; }, IsApplied = () => returned || effectApplied()
            } });
            return errors.Count > 0 ? errors[0] : null;
        }
    }

    /// <summary>无游戏依赖的单会晤状态机。请求快照不可从外部修改，版本校验先于一切效果。</summary>
    public sealed class MeetingNegotiation
    {
        private MeetingRequest? _request;
        public MeetingRequest? Request => _request?.Copy();
        public bool Closed { get; private set; }

        public string? Propose(MeetingSide side, MeetingTerms terms, string requestId, int version,
            string reason, Func<MeetingTerms, string?> validate)
        {
            if (Closed) return "会晤已经结束。";
            var shapeError = MeetingTerms.ValidateShape(terms);
            if (shapeError != null) return shapeError;
            var copy = terms.Copy();
            var active = _request?.Status == MeetingStatus.Negotiating || _request?.Status == MeetingStatus.Ready;
            if (_request?.Status == MeetingStatus.Executing || _request?.Status == MeetingStatus.Running) return "当前约定正在执行。";
            if (active)
            {
                var error = Check(requestId, version);
                if (error != null) return error;
                if (_request!.Terms.TypeId != copy.TypeId) return "还价不能更换事项，请先撤回原提议。";
            }
            else if (!string.IsNullOrEmpty(requestId) || version != 0)
                return "原请求已结束；新请求应使用空编号和版本 0。";
            var invalid = validate(copy);
            if (invalid != null) return invalid;
            _request = new MeetingRequest
            {
                Id = active ? _request!.Id : Guid.NewGuid().ToString("N"),
                Version = active ? checked(_request!.Version + 1) : 1,
                Terms = copy, ProposedBy = side, OtherAccepted = side == MeetingSide.Other,
                YourAccepted = side == MeetingSide.Self, Status = MeetingStatus.Negotiating,
                Reason = Limit(reason)
            };
            return null;
        }

        public string? Respond(MeetingSide side, string requestId, int version, string decision, string reason,
            Func<MeetingTerms, string?> validate, Func<MeetingTerms, string> execute, bool deferred = false)
        {
            if (Closed) return "会晤已经结束。";
            var error = Check(requestId, version);
            if (error != null) return error;
            var request = _request!;
            if (request.Status != MeetingStatus.Negotiating && request.Status != MeetingStatus.Ready) return "这份请求已经结束，不能重复处理。";
            if (decision == "reject" || decision == "withdraw")
            {
                if (decision == "withdraw" && request.ProposedBy != side) return "只有提议者能撤回；另一方可拒绝。";
                request.Status = decision == "reject" ? MeetingStatus.Rejected : MeetingStatus.Cancelled;
                request.Reason = Limit(reason);
                return null;
            }
            if (decision != "accept") return "回应应为 accept、reject 或 withdraw。还价请提交新条款。";
            if (request.Status == MeetingStatus.Ready) return "双方已经接受这版约定，等待开始。";
            // 一方的接受本身也不得重复触发执行。
            if (side == MeetingSide.Other ? request.OtherAccepted : request.YourAccepted)
                return "你已经接受这一版条件，等待另一方回应。";
            var invalid = validate(request.Terms.Copy());
            if (invalid != null) return invalid;
            if (side == MeetingSide.Other) request.OtherAccepted = true;
            else request.YourAccepted = true;
            request.Status = MeetingStatus.Executing; // 先锁状态，防止执行器重入或双击。
            try
            {
                request.Result = execute(request.Terms.Copy());
                request.Status = deferred ? MeetingStatus.Ready : MeetingStatus.Applied;
            }
            catch (MeetingPartialExecutionException ex)
            {
                request.Result = ex.Message;
                request.Status = MeetingStatus.PartiallyApplied;
            }
            catch (Exception ex)
            {
                request.Result = "执行失败：" + ex.Message;
                request.Status = MeetingStatus.Failed;
            }
            return null;
        }

        public void Close()
        {
            Closed = true;
            if (_request?.Status == MeetingStatus.Negotiating || _request?.Status == MeetingStatus.Ready)
            {
                _request.Status = MeetingStatus.Cancelled;
                _request.Result = "会晤结束，未成交。";
            }
        }

        public string? StartDeferred(string requestId, int version, Func<MeetingTerms, string?> validate, Action<MeetingRequest> start)
        {
            if (Closed) return "会晤已经结束。";
            var error = Check(requestId, version);
            if (error != null) return error;
            var request = _request!;
            if (request.Status != MeetingStatus.Ready || !request.OtherAccepted || !request.YourAccepted) return "双方尚未确认这版约定，或已经开始。";
            error = validate(request.Terms.Copy());
            if (error != null) return error;
            request.Status = MeetingStatus.Running;
            try { start(request.Copy()); request.Result = "已开始，等待实际结果。"; }
            catch (Exception ex) { request.Status = MeetingStatus.Failed; request.Result = "未能开始：" + ex.Message; return request.Result; }
            return null;
        }

        public bool CompleteDeferred(string requestId, string result, bool partial = false, bool failed = false)
        {
            if (_request?.Id != requestId || _request.Status != MeetingStatus.Running) return false;
            _request.Result = result;
            _request.Status = partial ? MeetingStatus.PartiallyApplied : failed ? MeetingStatus.Failed : MeetingStatus.Applied;
            return true;
        }

        private string? Check(string requestId, int version)
        {
            if (_request == null || _request.Id != requestId) return "请求编号不匹配，请重新查看当前交涉。";
            if (_request.Version != version) return "条件已更新，请查看新版本后再回应。";
            return null;
        }

        private static string Limit(string text)
        {
            text ??= "";
            return text.Length > 1000 ? text.Substring(0, 1000) : text;
        }
    }
}
