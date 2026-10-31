using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TaleWorlds.Library;

namespace AIChronicle
{
    /// <summary>
    /// 将需要在游戏主线程执行的操作排队分发。
    /// Bannerlord 的游戏对象（MobileParty / Hero / Kingdom / Clan 等）是主线程独占的，
    /// 而模组的 LLM 工具在后台线程执行——所有修改游戏状态的调用必须经此类回到主线程。
    /// OnApplicationTick 每帧调用 Tick() 消费队列。
    ///
    /// 战役生命周期隔离：每次进入/退出战役都会递增 campaign epoch 并清空旧队列。
    /// 后台线程在旧战役里排入的动作即使晚到，也会因 epoch 不匹配被丢弃；
    /// 阻塞等待的 RunOnMainThread 会立即收到取消异常，而不是等 30 秒超时后落到新战役里。
    /// </summary>
    public static class MainThreadExecutor
    {
        private abstract class WorkItem
        {
            protected WorkItem(long epoch, string label)
            {
                Epoch = epoch;
                Label = label;
            }

            public long Epoch { get; }
            public string Label { get; }

            public abstract void Execute();

            /// <summary>队列被生命周期切换清空时调用。</summary>
            public virtual void Cancel(Exception reason) { }

            /// <summary>执行抛异常时通知等待方。</summary>
            public virtual void Fail(Exception error) { }
        }

        private sealed class ActionWorkItem : WorkItem
        {
            private readonly Action _action;

            public ActionWorkItem(Action action, long epoch, string label)
                : base(epoch, label)
            {
                _action = action;
            }

            public override void Execute() => _action();
        }

        private sealed class FuncWorkItem<T> : WorkItem
        {
            private readonly Func<T> _func;
            private readonly TaskCompletionSource<T> _completion;

            public FuncWorkItem(Func<T> func, long epoch, string label)
                : base(epoch, label)
            {
                _func = func;
                _completion = new TaskCompletionSource<T>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public Task<T> Task => _completion.Task;

            public override void Execute()
            {
                _completion.TrySetResult(_func());
            }

            public override void Cancel(Exception reason) => _completion.TrySetException(reason);

            public override void Fail(Exception error) => _completion.TrySetException(error);
        }

        private static readonly ConcurrentQueue<WorkItem> _queue = new();
        private static readonly object _gate = new();
        private static long _campaignEpoch;
        private static volatile int _mainThreadId;
        private static int _staleDropped;

        /// <summary>当前战役 epoch；用于诊断“这个动作属于哪一局”。</summary>
        public static long CampaignEpoch => Interlocked.Read(ref _campaignEpoch);

        public static int PendingCount => _queue.Count;

        /// <summary>在战役目录已就绪后写一行生命周期状态，便于从 ui_trace.log 确认当前 epoch。</summary>
        public static void TraceStatus(string context)
        {
            UiTrace.Log($"[MainThread] status epoch={CampaignEpoch}, pending={PendingCount} ({context})");
        }

        /// <summary>在 OnSubModuleLoad（游戏主线程）调用，绑定主线程 ID。</summary>
        public static void Initialize()
        {
            _mainThreadId = Environment.CurrentManagedThreadId;
            UiTrace.Log("[MainThread] initialized epoch=" + CampaignEpoch);
        }

        /// <summary>
        /// 新战役开始（OnGameStart）。递增 epoch 并丢弃上一局残留的排队动作。
        /// 必须在战役行为/Agent 开始向队列投递之前调用。
        /// </summary>
        public static void BeginCampaign()
        {
            Invalidate("新战役开始");
        }

        /// <summary>
        /// 战役结束（OnGameEnd）。递增 epoch 并取消所有仍在等待主线程的动作，
        /// 防止切档/回主菜单后旧动作落到新战役的游戏对象上。
        /// </summary>
        public static void EndCampaign()
        {
            Invalidate("战役结束");
        }

        private static void Invalidate(string reason)
        {
            int canceled = 0;
            long epoch;

            lock (_gate)
            {
                epoch = Interlocked.Increment(ref _campaignEpoch);
                _staleDropped = 0;

                while (_queue.TryDequeue(out var item))
                {
                    item.Cancel(new OperationCanceledException(
                        $"[AI编年史] 战役生命周期已切换（{reason}），排队操作已取消：{item.Label}"));
                    canceled++;
                }
            }

            UiTrace.Log($"[MainThread] campaign epoch={epoch} ({reason}), canceled={canceled}, pending={PendingCount}");
        }

        public static bool IsMainThread =>
            _mainThreadId != 0 && Environment.CurrentManagedThreadId == _mainThreadId;

        /// <summary>每帧在 OnApplicationTick 主线程调用，消费待执行队列。</summary>
        public static void Tick()
        {
            if (_mainThreadId == 0)
                _mainThreadId = Environment.CurrentManagedThreadId; // 延迟绑定主线程

            long epoch = CampaignEpoch;
            while (_queue.TryDequeue(out var item))
            {
                if (item.Epoch != epoch)
                {
                    item.Cancel(new OperationCanceledException(
                        $"[AI编年史] 排队操作属于旧战役（epoch {item.Epoch} != {epoch}），已取消：{item.Label}"));
                    _staleDropped++;
                    if (_staleDropped == 1 || _staleDropped % 25 == 0)
                        UiTrace.Log($"[MainThread] dropped stale work (epoch {item.Epoch} != {epoch}), total={_staleDropped}, item={item.Label}");
                    continue;
                }

                try
                {
                    item.Execute();
                }
                catch (Exception e)
                {
                    item.Fail(e);
                    UiTrace.Log("[MainThread] action failed: " + e);
                    try
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"[AI编年史] 主线程执行异常：{e.Message}", Colors.Red));
                    }
                    catch { }
                }
            }
        }

        /// <summary>异步投递，不等待结果（用于 UI 消息等无需回执的操作）。</summary>
        public static void Post(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            lock (_gate)
            {
                _queue.Enqueue(new ActionWorkItem(action, CampaignEpoch, Describe(action)));
            }
        }

        /// <summary>在后台线程调用：阻塞直到主线程执行完 func 并返回结果。若已在主线程则直接执行。</summary>
        public static T RunOnMainThread<T>(Func<T> func)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            if (IsMainThread)
                return func();

            FuncWorkItem<T> item;
            lock (_gate)
            {
                item = new FuncWorkItem<T>(func, CampaignEpoch, Describe(func));
                _queue.Enqueue(item);
            }

            bool finished;
            try
            {
                finished = item.Task.Wait(TimeSpan.FromSeconds(30));
            }
            catch (AggregateException ex)
            {
                var inner = ex.Flatten().InnerException ?? ex;
                if (inner is OperationCanceledException)
                    throw new InvalidOperationException(
                        "[AI编年史] 主线程操作已取消：战役生命周期已切换。", inner);
                throw new InvalidOperationException(
                    "[AI编年史] 主线程工具执行失败：" + inner.Message, inner);
            }

            if (!finished)
                throw new InvalidOperationException("[AI编年史] 主线程分发超时（主线程可能未在运行）。");

            try
            {
                return item.Task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException ex)
            {
                throw new InvalidOperationException(
                    "[AI编年史] 主线程操作已取消：战役生命周期已切换。", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "[AI编年史] 主线程工具执行失败：" + ex.Message, ex);
            }
        }

        public static void RunOnMainThread(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (IsMainThread) { action(); return; }
            RunOnMainThread(() => { action(); return true; });
        }

        /// <summary>跨线程安全地显示左下角消息（后台线程调用时自动投递到主线程）。</summary>
        public static void DisplayMessage(InformationMessage message)
        {
            if (IsMainThread) { InformationManager.DisplayMessage(message); return; }
            Post(() =>
            {
                try { InformationManager.DisplayMessage(message); }
                catch { }
            });
        }

        private static string Describe(Delegate action)
        {
            try
            {
                return (action.Method.DeclaringType?.Name ?? "?") + "." + action.Method.Name;
            }
            catch
            {
                return "delegate";
            }
        }
    }
}
