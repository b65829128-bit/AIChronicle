using System;
using System.IO;
using System.Text;

namespace MyFirstMod
{
    /// <summary>
    /// 调试日志：记录 LLM 调用摘要、推理（思维链）摘录、流程决策，便于复盘 agent 行为。
    /// 写到战役目录下的 debug_logs/（NPC Agent 无法读取——不在路径白名单内，避免日志成为信息泄露渠道）。
    /// </summary>
    public static class DebugLogger
    {
        private const long MaxBytes = 5 * 1024 * 1024; // 单文件上限 5MB，超出后停止写入避免膨胀
        private static readonly object _lock = new();
        private static string? _logPath;

        /// <summary>在战役开始/切换时调用，日志写入该战役目录下的 debug_logs/。</summary>
        public static void Init(string campaignDir)
        {
            lock (_lock)
            {
                if (string.IsNullOrEmpty(campaignDir)) return;
                try
                {
                    var dir = Path.Combine(campaignDir, "debug_logs");
                    Directory.CreateDirectory(dir);
                    _logPath = Path.Combine(dir, $"debug_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                    // 写一行初始化标记——若日志文件存在但只有这一行，说明 Init 跑了但 LLM 流程未触发埋点。
                    File.AppendAllText(_logPath,
                        $"=== 调试日志初始化 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}",
                        Encoding.UTF8);
                }
                catch { }
            }
        }

        /// <summary>写一行日志。受 MCM「调试日志」开关控制；线程安全（文件追加加锁）。
        /// 整个方法体都在 try 内——日志绝不允许抛异常拖垮主流程。</summary>
        public static void Log(string message)
        {
            lock (_lock)
            {
                if (_logPath == null) return;
                try
                {
                    if (MySettings.Instance?.DebugLogging == false) return;
                    if (new FileInfo(_logPath).Length > MaxBytes) return;
                    File.AppendAllText(_logPath,
                        $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}", Encoding.UTF8);
                }
                catch { }
            }
        }

        /// <summary>战役结束时清除日志路径，下个战役重新初始化（避免跨档写错目录）。</summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _logPath = null;
            }
        }

        public static string Truncate(string? text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "…";
        }
    }
}
