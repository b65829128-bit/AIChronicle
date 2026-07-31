using System;
using System.IO;
using System.Text;
using System.Threading;

namespace MyFirstMod
{
    /// <summary>
    /// 带重试的文件 IO。并发读写同一文件（如史官后台读史料 vs 主线程写史料、封臣写谏言 vs 国王读谏言）
    /// 可能因 FileShare 限制抛 "文件正由另一进程使用"（IOException）——竞争窗口仅数微秒，重试几次几乎必成功。
    /// 超过重试次数后仍抛出，由调用方决定如何处理（不能因为日志/史料丢失而崩游戏）。
    /// </summary>
    public static class SafeFileIO
    {
        private const int MaxRetries = 5;
        private const int RetryDelayMs = 5;

        private static void Retry(Action action)
        {
            for (int i = 0; ; i++)
            {
                try { action(); return; }
                catch (IOException) when (i < MaxRetries)
                {
                    Thread.Sleep(RetryDelayMs);
                }
            }
        }

        private static void EnsureDir(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        public static void AppendAllText(string path, string content)
        {
            EnsureDir(path);
            Retry(() => File.AppendAllText(path, content, Encoding.UTF8));
        }

        public static void WriteAllText(string path, string content)
        {
            EnsureDir(path);
            Retry(() => File.WriteAllText(path, content, Encoding.UTF8));
        }

        public static string ReadAllText(string path)
        {
            string result = "";
            Retry(() => result = File.ReadAllText(path, Encoding.UTF8));
            return result;
        }

        public static string[] ReadAllLines(string path)
        {
            string[] result = Array.Empty<string>();
            Retry(() => result = File.ReadAllLines(path, Encoding.UTF8));
            return result;
        }
    }
}
