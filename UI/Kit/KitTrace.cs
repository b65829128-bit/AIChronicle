using System;
using System.IO;
using System.Text;

namespace AIChronicle
{
    /// <summary>
    /// UI 诊断日志：同时写 DebugLogger 与战役 debug_logs/ui_trace.log。
    /// 后者不受 MCM「调试日志」开关影响，专门用于定位实机卡死/黑屏步骤。
    /// </summary>
    internal static class UiTrace
    {
        private static readonly object _lock = new();
        private const long MaxBytes = 1024 * 1024;

        public static void Log(string message)
        {
            DebugLogger.Log("[UI] " + message);

            try
            {
                string campaign = PromptManager.CampaignDir;
                if (string.IsNullOrEmpty(campaign))
                    return;

                string directory = Path.Combine(campaign, "debug_logs");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "ui_trace.log");

                lock (_lock)
                {
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxBytes)
                        return;

                    File.AppendAllText(path,
                        $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}",
                        Encoding.UTF8);
                }
            }
            catch
            {
                // 日志绝不影响 UI 主流程
            }
        }
    }
}
