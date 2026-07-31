using System;
using System.Collections.Concurrent;
using System.Threading;
using TaleWorlds.Library;

namespace MyFirstMod
{
    /// <summary>
    /// 将需要在游戏主线程执行的操作排队分发。
    /// Bannerlord 的游戏对象（MobileParty / Hero / Kingdom / Clan 等）是主线程独占的，
    /// 而模组的 LLM 工具在后台线程执行——所有修改游戏状态的调用必须经此类回到主线程。
    /// OnApplicationTick 每帧调用 Tick() 消费队列。
    /// </summary>
    public static class MainThreadExecutor
    {
        private static readonly ConcurrentQueue<Action> _queue = new();
        private static volatile int _mainThreadId;

        /// <summary>在 OnSubModuleLoad（游戏主线程）调用，绑定主线程 ID。</summary>
        public static void Initialize()
        {
            _mainThreadId = Environment.CurrentManagedThreadId;
        }

        public static bool IsMainThread =>
            _mainThreadId != 0 && Environment.CurrentManagedThreadId == _mainThreadId;

        /// <summary>每帧在 OnApplicationTick 主线程调用，消费待执行队列。</summary>
        public static void Tick()
        {
            if (_mainThreadId == 0)
                _mainThreadId = Environment.CurrentManagedThreadId; // 延迟绑定主线程

            while (_queue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e)
                {
                    try
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"[MyFirstMod] 主线程执行异常：{e.Message}", Colors.Red));
                    }
                    catch { }
                }
            }
        }

        /// <summary>异步投递，不等待结果（用于 UI 消息等无需回执的操作）。</summary>
        public static void Post(Action action)
        {
            _queue.Enqueue(action);
        }

        /// <summary>在后台线程调用：阻塞直到主线程执行完 func 并返回结果。若已在主线程则直接执行。</summary>
        public static T RunOnMainThread<T>(Func<T> func)
        {
            if (IsMainThread)
                return func();

            using var mre = new ManualResetEventSlim(false);
            T result = default!;
            Exception? error = null;
            Post(() =>
            {
                try { result = func(); }
                catch (Exception e) { error = e; }
                finally { mre.Set(); }
            });

            if (!mre.Wait(TimeSpan.FromSeconds(30)))
                throw new InvalidOperationException("[MyFirstMod] 主线程分发超时（主线程可能未在运行）。");

            if (error != null)
                throw new InvalidOperationException("[MyFirstMod] 主线程工具执行失败：" + error.Message, error);
            return result;
        }

        public static void RunOnMainThread(Action action)
        {
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
    }
}
