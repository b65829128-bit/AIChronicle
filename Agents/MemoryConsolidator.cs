using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace AIChronicle
{
    /// <summary>保底巩固：人物自行读取新增往来，只有成功且实际可见的连续消息才推进独立游标。</summary>
    public static class MemoryConsolidator
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);
        private const string WatermarkFileName = ".diary_authored";

        /// <summary>保留旧自写日期供现有文件兼容；不代表任何聊天已经巩固。</summary>
        public static void AdvanceWatermark(string agentDir)
        {
            if (string.IsNullOrEmpty(agentDir)) return;
            try
            {
                var now = CampaignTime.Now;
                var season = now.GetSeasonOfYear switch
                {
                    CampaignTime.Seasons.Spring => 0,
                    CampaignTime.Seasons.Summer => 1,
                    CampaignTime.Seasons.Autumn => 2,
                    CampaignTime.Seasons.Winter => 3,
                    _ => 0
                };
                var key = now.GetYear * 400 + season * 32 + now.GetDayOfSeason + 1;
                SafeFileIO.WriteAllText(Path.Combine(agentDir, "decisions", WatermarkFileName), key.ToString());
            }
            catch { }
        }

        public static bool IsDiaryStale(string agentDir) => MemoryProgress.PendingChats(agentDir).Count > 0;

        public static List<string> ListNewerChatLogs(string agentDir) => MemoryProgress.PendingChats(agentDir)
            .Select(r => Path.GetFileName(r.Path)).ToList();

        public static async Task EnsureDiaryCurrentAsync(string agentId)
        {
            if (MySettings.Instance?.MemoryConsolidationEnabled == false) return;
            var root = AgentManager.GetAgentDir();
            if (string.IsNullOrEmpty(root)) return;
            var epoch = MainThreadExecutor.CampaignEpoch;
            var gate = Gates.GetOrAdd(root!, _ => new SemaphoreSlim(1, 1));
            if (!await gate.WaitAsync(0)) return;
            try
            {
                using var batch = MemoryProgress.BeginConsolidation(root!);
                if (batch.Ranges.Count == 0) return;
                var agent = EntityManager.GetEntityById(agentId);
                if (agent?.HeroRef == null || MainThreadExecutor.CampaignEpoch != epoch) return;
                // 只给文件名和新增范围，不把记忆正文自动注入；仍由人物自己调用工具阅读。
                var files = string.Join("\n", batch.Ranges.Select(r => $"- {r.Path}（新增第 {r.Start}–{r.End} 行，用 read_file 按范围读取）"));
                var prompt = new CharacterPrompt
                {
                    HeroId = agentId,
                    HeroName = agent.Name,
                    ChatHistory = new List<ChatHistoryEntry> { new()
                    { Role = "user", Content = PromptManager.LoadMemoryConsolidationPrompt().Replace("{newer_files}", files) } }
                };
                var response = await AIChatClient.SendMessage(prompt, agent.HeroRef, includeTools: true, intent: "consolidation");
                // 预算耗尽、切档、工具读取截断或写入失败，均不能把未处理内容标为完成。
                if (MainThreadExecutor.CampaignEpoch == epoch && AgentManager.GetAgentDir() == root)
                {
                    var advanced = batch.Commit(string.Equals(response.FinishReason, "stop", StringComparison.OrdinalIgnoreCase));
                    DebugLogger.Log($"记忆巩固进度 agent={agentId} 文件={batch.Ranges.Count} 推进={advanced} finish={response.FinishReason}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("记忆巩固失败：" + ex.Message);
                if (MainThreadExecutor.CampaignEpoch == epoch)
                    MainThreadExecutor.DisplayMessage(new InformationMessage("[AI编年史] 记忆巩固失败：" + ex.Message, Colors.Red));
            }
            finally { gate.Release(); }
        }
    }
}
