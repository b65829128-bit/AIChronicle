using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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
                var campaignEpoch = MainThreadExecutor.CampaignEpoch;
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

                // 年度任务只核对对应年份；并行专题史不能冒充本年编纂成功。
                var chronicleDir = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "history", "chronicles");
                var year = evt.Type == ActivationEventType.YearlyChronicle ? HistorianWorkflow.ExtractYear(evtContent) : -1;
                if (evt.Type == ActivationEventType.YearlyChronicle && year <= 0)
                    throw new InvalidOperationException("年度编纂任务缺少有效年份。");
                Directory.CreateDirectory(chronicleDir);
                var before = HistorianWorkflow.Capture(chronicleDir, year);

                var response = await AIChatClient.SendMessage(
                    charPrompt, hero: null, includeTools: true, intent: "historian");

                if (MainThreadExecutor.CampaignEpoch != campaignEpoch) return;
                var wroteFile = HistorianWorkflow.HasChanged(chronicleDir, year, before);

                // 有界续写：保留原任务与实际返回的史料，仅开放写史册，再给两轮成文/纠错。
                if (!wroteFile && (response.FinishReason == "length" || response.FinishReason == "round_budget" || response.FinishReason == "stop"))
                {
                    var retryHint = year > 0
                        ? $"本次尚未生成 {year}编年史.txt。依据原任务与上文已读史料，立即调用 write_chronicle：体例=编年史，名称={year}，正文按春夏秋冬分节并附史官曰。未读到的时段注明阙载。"
                        : "本次尚未落盘。依据原任务与上文已读史料，立即调用 write_chronicle，选择相应体例，将正文写入史册。";
                    DebugLogger.Log($"史官成文续写 eventLabel={eventLabel} year={year} finish={response.FinishReason}");
                    charPrompt.ChatHistory.Add(new ChatHistoryEntry { Role = "user", Content = retryHint });
                    response = await AIChatClient.SendMessage(charPrompt, hero: null, includeTools: true, intent: "historian", historianRetry: response);
                    if (MainThreadExecutor.CampaignEpoch != campaignEpoch) return;
                    wroteFile = HistorianWorkflow.HasChanged(chronicleDir, year, before);
                }

                if (!wroteFile)
                    DebugLogger.Log($"史官未落盘 eventLabel={eventLabel} year={year} finish={response.FinishReason}");

                if (wroteFile)
                {
                    MainThreadExecutor.DisplayMessage(new InformationMessage(
                        $"史官已完成{eventLabel}的编纂。",
                        Colors.Green));
                }
                else if (!string.IsNullOrEmpty(response.Content))
                {
                    MainThreadExecutor.DisplayMessage(new InformationMessage(
                        $"史官{eventLabel}编纂未完成：未检测到对应的非空史文，详见调试日志。",
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

        // ============ 封臣谏言系统 ============
    }
}
