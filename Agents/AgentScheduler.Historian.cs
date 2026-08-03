using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace AIChronicle
{
    public static partial class AgentScheduler
{
        private static async Task ProcessHistorianEvent(ActivationEvent evt)
        {
            var settings = MySettings.Instance;
            // 用「史官」场景的生效密钥判断是否可跑（未配置则本场景与兜底均空）
            if (settings == null || string.IsNullOrWhiteSpace(ConnectionResolver.Resolve("historian").ApiKey))
                return;

            try
            {
                var eventLabel = evt.Type == ActivationEventType.YearlyChronicle ? "编年史" : "专题史";
                MainThreadExecutor.DisplayMessage(new InformationMessage(
                    $"史官正在编纂{eventLabel}...",
                    Colors.Cyan));

                EntityManager.ActivateHistorian();

                // 专题史：从合并缓冲取全部事件（一次史官激活处理一批）；年度编年史直接用 evt.Content
                var evtContent = evt.Type == ActivationEventType.SpecialChronicle
                    ? ConsumeSpecialChronicleContent()
                    : evt.Content;
                if (string.IsNullOrEmpty(evtContent))
                {
                    MainThreadExecutor.DisplayMessage(new InformationMessage(
                        "[AI编年史] 史官专题史缓冲为空，跳过。", Colors.Yellow));
                    return;
                }

                var charPrompt = new CharacterPrompt
                {
                    HeroId = "__historian__",
                    HeroName = "史官",
                    ChatHistory = new List<ChatHistoryEntry>
                    {
                        new() { Role = "user", Content = evtContent }
                    }
                };

                // 记录 chronicles 目录现有文件，结束后对比是否有新文件/被修改（用于判定史官是否真写出编年史）
                var chronicleDir = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "history", "chronicles");
                var beforeFiles = new HashSet<string>();
                var beforeTimes = new Dictionary<string, DateTime>();
                try
                {
                    Directory.CreateDirectory(chronicleDir);
                    foreach (var f in Directory.GetFiles(chronicleDir))
                    {
                        beforeFiles.Add(f);
                        beforeTimes[f] = File.GetLastWriteTimeUtc(f);
                    }
                }
                catch { }

                var response = await AIChatClient.SendMessage(
                    charPrompt, hero: null, includeTools: true, intent: "historian");

                // 修复：成功判定改为"出现新文件或文件被修改"——传记是自命名文件（不叫 chronicle_*），
                // 原检查只找 chronicle_*.txt 会把已成功写入的传记误报为"未生成"。
                var wroteFile = HasChronicleChanged(chronicleDir, beforeFiles, beforeTimes);

                // 修复：finish_reason="length"（被 max_tokens 截断）且未落盘 → 重试一次。
                // 史官长编年史常在思考阶段耗尽 token 被截断（Content 为空、未调 write_file），谏言已有同类重试。
                if (!wroteFile && response.FinishReason == "length")
                {
                    var year = ExtractYearFromContent(evtContent);
                    var retryHint = year > 0
                        ? $"你上一轮思考到一半被截断，未能写成编年史。现在请调用 write_file 将编年史写入 history/chronicles/chronicle_{year}.txt，尽快成文落盘。"
                        : "你上一轮思考到一半被截断，未能写成内容。现在请调用 write_file 将内容写入 history/chronicles/ 目录（文件名自定），尽快成文落盘。";
                    DebugLogger.Log($"史官因 token 截断重试 eventLabel={eventLabel} year={year}");
                    var retryPrompt = new CharacterPrompt
                    {
                        HeroId = "__historian__",
                        HeroName = "史官",
                        ChatHistory = new List<ChatHistoryEntry>
                        {
                            new() { Role = "user", Content = retryHint }
                        }
                    };
                    response = await AIChatClient.SendMessage(retryPrompt, hero: null, includeTools: true, intent: "historian");
                    wroteFile = HasChronicleChanged(chronicleDir, beforeFiles, beforeTimes);
                }

                if (wroteFile)
                {
                    MainThreadExecutor.DisplayMessage(new InformationMessage(
                        $"史官已完成{eventLabel}的编纂。",
                        Colors.Green));
                }
                else if (!string.IsNullOrEmpty(response.Content))
                {
                    MainThreadExecutor.DisplayMessage(new InformationMessage(
                        $"史官{eventLabel}编纂已结束，但编年史文件未生成（可能读取史料失败）。",
                        Colors.Yellow));
                }
                else
                {
                    MainThreadExecutor.DisplayMessage(new InformationMessage(
                        $"史官{eventLabel}编纂未产生文本输出。",
                        Colors.Yellow));
                }
            }
            catch (Exception ex)
            {
                MainThreadExecutor.DisplayMessage(new InformationMessage(
                    $"[AI编年史] 史官处理异常：{ex.Message}",
                    Colors.Red));
            }
        }

        private static int ExtractYearFromContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return -1;
            var prefix = "第";
            var suffix = "年";
            var start = content.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0) return -1;
            start += prefix.Length;
            var end = content.IndexOf(suffix, start, StringComparison.Ordinal);
            if (end < 0) return -1;
            if (int.TryParse(content.Substring(start, end - start), out var year))
                return year;
            return -1;
        }

        private static bool HasChronicleChanged(string chronicleDir, HashSet<string> beforeFiles, Dictionary<string, DateTime> beforeTimes)
        {
            try
            {
                foreach (var f in Directory.GetFiles(chronicleDir))
                {
                    if (!beforeFiles.Contains(f)
                        || (beforeTimes.TryGetValue(f, out var t) && File.GetLastWriteTimeUtc(f) > t))
                        return true;
                }
            }
            catch { }
            return false;
        }

        // ============ 封臣谏言系统 ============
    }
}
