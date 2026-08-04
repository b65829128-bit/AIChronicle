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
        private static async Task ProcessClanReplenishmentEvent(ActivationEvent evt)
        {
            var settings = MySettings.Instance;
            // 用「天意建族」场景的生效密钥判断是否可跑（未配置则本场景与兜底均空）
            if (settings == null || string.IsNullOrWhiteSpace(ConnectionResolver.Resolve("clan_replenishment").ApiKey))
                return;

            try
            {
                MainThreadExecutor.DisplayMessage(new InformationMessage(
                    "天意降下新的贵族血脉...",
                    Colors.Cyan));

                EntityManager.ActivateFate();

                // 单次激活只建一族（代码强制限流，防连建多族导致游戏状态剧变而原生崩溃）
                ToolExecutor.ResetFateClanBudget();

                if (string.IsNullOrEmpty(evt.Content))
                {
                    MainThreadExecutor.DisplayMessage(new InformationMessage(
                        "[AI编年史] 家族补充事件内容为空，跳过。", Colors.Yellow));
                    return;
                }

                var charPrompt = new CharacterPrompt
                {
                    HeroId = "__fate__",
                    HeroName = "天意",
                    ChatHistory = new List<ChatHistoryEntry>
                    {
                        new() { Role = "user", Content = evt.Content }
                    }
                };

                var response = await AIChatClient.SendMessage(
                    charPrompt, hero: null, includeTools: true, intent: "clan_replenishment");

                // 从工具执行结果精确判断建族是否落地（结果以「已降下」开头 = 成功），
                // 成功/失败都让玩家可见——之前只在 debug 日志里记录，玩家看到开始消息后全程无反馈。
                var createCall = response.ToolCalls.FirstOrDefault(tc => tc.Name == "create_clan");
                string? createResult = null;
                if (createCall != null)
                    response.ToolResults.TryGetValue(createCall.Id, out createResult);
                var createdClan = createResult?.StartsWith("已降下") == true;

                if (createdClan)
                {
                    MainThreadExecutor.DisplayMessage(new InformationMessage(
                        $"天意降下了新的贵族血脉：{createResult}", Colors.Green));
                }
                else
                {
                    var snippet = response.Content;
                    if (snippet != null && snippet.Length > 100) snippet = snippet.Substring(0, 100);
                    DebugLogger.Log($"天意未成功降下血脉。工具调用={(createCall != null ? createCall.Name : "无")} 结果={createResult} response={snippet}");
                    MainThreadExecutor.DisplayMessage(new InformationMessage(
                        "[AI编年史] 天意沉吟未决，本次未降下血脉（数日后再试）。若反复如此，请检查「天意建族」场景的 API 连接配置。",
                        Colors.Yellow));
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"家族补充处理异常：{ex.Message}");
            }
        }
    }
}
