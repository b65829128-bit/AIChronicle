using System;
using System.Collections.Generic;
using System.Linq;

namespace AIChronicle
{
    /// <summary>
    /// 全局后台 LLM 预算：滚动 5 分钟内已消耗的 prompt token 超限时，
    /// 调度器推迟可延迟的后台事件（自省/国王政务/问询/密使等）。
    /// 玩家聊天不在调度器队列里，不受影响；朝议投票有截止日，也不推迟。
    /// </summary>
    public static class TokenBudget
    {
        private static readonly object _lock = new();
        private static readonly Queue<(DateTime At, long Tokens)> _window = new();
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
        private static DateTime _lastDeferralLog = DateTime.MinValue;

        public static void Reset()
        {
            lock (_lock) _window.Clear();
            _lastDeferralLog = DateTime.MinValue;
        }

        public static void Record(long promptTokens)
        {
            if (promptTokens <= 0) return;
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                _window.Enqueue((now, promptTokens));
                Trim(now);
            }
        }

        public static long CurrentTokens
        {
            get
            {
                lock (_lock)
                {
                    Trim(DateTime.UtcNow);
                    return _window.Sum(x => x.Tokens);
                }
            }
        }

        public static bool IsOverBudget
        {
            get
            {
                var limit = MySettings.Instance?.BackgroundTokenBudgetPer5Min ?? 500000;
                if (limit <= 0) return false;
                return CurrentTokens >= limit;
            }
        }

        /// <summary>朝议投票有截止日，不推迟；其他后台事件可延后。</summary>
        public static bool IsDeferrable(ActivationEventType type)
        {
            return type != ActivationEventType.CuriaVote
                && type != ActivationEventType.CuriaDefense
                && type != ActivationEventType.YearlyChronicle
                && type != ActivationEventType.SpecialChronicle;
        }

        public static void LogDeferral()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastDeferralLog).TotalSeconds < 60) return;
            _lastDeferralLog = now;
            DebugLogger.Log($"TokenBudget：滚动5分钟已用 {CurrentTokens} prompt tokens，推迟后台事件");
        }

        private static void Trim(DateTime now)
        {
            while (_window.Count > 0 && now - _window.Peek().At > Window)
                _window.Dequeue();
        }
    }
}
