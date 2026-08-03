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

                var createdClan = response.ToolCalls.Any(tc => tc.Name == "create_clan");
                if (!createdClan)
                    DebugLogger.Log($"天意未调用 create_clan，本次补充未落地（稍后会再次尝试）。response={response.Content?.Substring(0, Math.Min(100, response.Content?.Length ?? 0))}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"家族补充处理异常：{ex.Message}");
            }
        }
    }
}
