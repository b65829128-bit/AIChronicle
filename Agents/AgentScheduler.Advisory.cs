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
        private static void CheckAdvisoryActivations()
        {
            if (Campaign.Current == null) return;
            if (_warmupFrames > 0) return;
            if (MySettings.Instance?.AdvisoryEnabled != true) return;

            foreach (var kingdom in Kingdom.All)
            {
                if (kingdom.IsEliminated) continue;
                if (kingdom.RulingClan?.Leader == null || !kingdom.RulingClan.Leader.IsAlive)
                    continue;

            if (!_lastAdvisoryCheck.TryGetValue(kingdom, out var lastCheck))
            {
                _lastAdvisoryCheck[kingdom] = CampaignTime.Now;
                continue;
            }

            if ((CampaignTime.Now - lastCheck).ToDays < 1)
                continue;

            _lastAdvisoryCheck[kingdom] = CampaignTime.Now;

                var probability = MySettings.Instance?.AdvisoryProbability ?? 0.1f;
                if (_rng.NextDouble() > probability) continue;

                var leader = SelectAdvisoryLeader(kingdom);
                if (leader == null) continue;

                _lastAdvisorySpeaker[kingdom] = leader.Id;

                MainThreadExecutor.DisplayMessage(new InformationMessage(
                    $"{leader.Name} 正在向{kingdom.Name}国王进谏...",
                    Colors.Cyan));

                // 有限并行：谏言入队（P4 最低优先），由槽位调度器处理
                QueueEvent(new ActivationEvent
                {
                    Type = ActivationEventType.Advisory,
                    AgentId = leader.Id,
                    TargetId = leader.Id,
                    Content = "",
                    Depth = 0
                });
                return;
            }
        }

        private static Entity? SelectAdvisoryLeader(Kingdom kingdom)
        {
            var candidates = new List<(Entity entity, float weight)>();

            foreach (var clan in kingdom.Clans)
            {
                if (clan.IsUnderMercenaryService) continue;

                var leader = clan.Leader;
                if (leader == null || !leader.IsAlive) continue;
                if (leader.IsPrisoner || leader.IsFugitive) continue;

                var entity = EntityManager.GetOrCreateEntity(leader);
                if (entity == null) continue;

                // Skip the king and player
                if (leader == kingdom.RulingClan?.Leader) continue;
                if (entity.Controller == EntityController.Human) continue;

                // Skip if this leader spoke last time for this kingdom
                if (_lastAdvisorySpeaker.TryGetValue(kingdom, out var lastId) && lastId == entity.Id)
                    continue;

                // Weight: Tier×3 + Influence/50 + FiefCount
                var weight = clan.Tier * 3f + leader.Clan.Influence / 50f + clan.Fiefs.Count;
                candidates.Add((entity, weight));
            }

            if (candidates.Count == 0)
            {
                // If cooldown excluded everyone, fall back to all eligible
                foreach (var clan in kingdom.Clans)
                {
                    if (clan.IsUnderMercenaryService) continue;
                    var leader = clan.Leader;
                    if (leader == null || !leader.IsAlive || leader.IsPrisoner || leader.IsFugitive) continue;
                    if (leader == kingdom.RulingClan?.Leader) continue;
                    var entity = EntityManager.GetOrCreateEntity(leader);
                    if (entity == null || entity.Controller == EntityController.Human) continue;
                    var weight = clan.Tier * 3f + leader.Clan.Influence / 50f + clan.Fiefs.Count;
                    candidates.Add((entity, weight));
                }
            }

            if (candidates.Count == 0) return null;

            var totalWeight = candidates.Sum(c => c.weight);
            var roll = _rng.NextDouble() * totalWeight;
            var cumulative = 0f;
            foreach (var (entity, weight) in candidates)
            {
                cumulative += weight;
                if (roll <= cumulative) return entity;
            }

            return candidates.Last().entity;
        }

        /// <summary>是否算"已提交谏言"：submit_advisory（公开）或 submit_secret_advisory（密陈）实际执行成功。</summary>
        private static bool IsAdvisorySubmitted(ChatResponse response)
        {
            return response.ToolCalls.Any(tc =>
                (tc.Name == "submit_advisory" || tc.Name == "submit_secret_advisory")
                && response.ToolResults.TryGetValue(tc.Id, out var r)
                && !r.StartsWith("[错误]"));
        }

        private static async Task ProcessAdvisory(Entity vassal, Kingdom kingdom)
        {
            try
            {
                if (string.IsNullOrEmpty(PromptManager.CampaignDir)) return;

                var kingdomName = kingdom.Name.ToString();
                var currentYear = CampaignTime.Now.GetYear;
                var currentTime = PromptManager.GetCurrentTimeString();

                var prevAgentId = EntityManager.ActiveAgentId;
                var prevTargetId = EntityManager.ActiveTargetId;

                try
                {
                    EntityManager.ActivateInteraction(vassal.HeroRef!, vassal.HeroRef!);

                    // 封臣链路也做记忆巩固：进谏前若日记落后于聊天记录，先补记 diary，
                    // 否则谏言可能反映的是过时立场。静默执行，仅落后时触发。
                    await MemoryConsolidator.EnsureDiaryCurrentAsync(vassal.Id);

                    var advisoryDir = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "advisory");
                    Directory.CreateDirectory(advisoryDir);
                    var advisoryFile = Path.Combine(advisoryDir, $"{kingdomName}_{currentYear}.txt");

                    var advisoryContent =
                        $"你是{vassal.Name}，{vassal.Title}，{kingdomName}的氏族领袖。\n\n"
                        + $"当前时间：{currentTime}\n\n"
                        + $"作为{kingdomName}的封臣，请审视王国当前的局势，向国王进谏。\n"
                        + "步骤：\n"
                        + $"1. 先用 read_file 阅读国王的公开诏令（edict/{kingdomName}_{currentYear}.txt；若为空说明国王本年尚无诏令，必要时可用 glob 查看 edict/ 目录了解国王近期诏令），了解王上的旨意与垂询；若国王在诏令中垂询某事，应在谏言中回应\n"
                        + "2. 如需回顾你之前的私人记录，可用 read_file 阅读 decisions/personal_notes.txt\n"
                        + "3. 用 query_world_state、query_war_status 等工具了解当前局势\n"
                        + "4. 用 submit_advisory 工具提交你的公开谏言（在 content 参数里直接写谏言正文）；若有不便入史的内容，用 submit_secret_advisory 密陈给国王\n"
                        + "\n注意：谏言正文直接填进 submit_advisory / submit_secret_advisory 的 content 参数，不要写在你的回复文本里。"
                        + $"如有需要记录的私人想法，可用 write_file 写入 decisions/personal_notes.txt。";

                    var charPrompt = new CharacterPrompt
                    {
                        HeroId = vassal.Id,
                        HeroName = vassal.Name,
                        ChatHistory = new List<ChatHistoryEntry>
                        {
                            new() { Role = "user", Content = advisoryContent }
                        }
                    };

                    var response = await AIChatClient.SendMessage(
                        charPrompt, vassal.HeroRef, includeTools: true, intent: "advisory");
                    var submittedAdvisory = IsAdvisorySubmitted(response);

                    // 修复：finish_reason="length"（被 max_tokens 截断）且未提交/无文本 → 重试一次。
                    // 区分"被截断"（推理被掐断，非主动沉默）与"主动沉默"（finish_reason=stop，不重试）。
                    if (!submittedAdvisory && string.IsNullOrEmpty(response.Content?.Trim())
                        && response.FinishReason == "length")
                    {
                        DebugLogger.Log($"谏言因 token 截断重试 agent={vassal.Id} kingdom={kingdomName}");
                        var retryCharPrompt = new CharacterPrompt
                        {
                            HeroId = vassal.Id,
                            HeroName = vassal.Name,
                            ChatHistory = new List<ChatHistoryEntry>
                            {
                                new() { Role = "user", Content = "你上一轮思考到一半被截断，未能发表谏言。现在请直接审视局势并调用 submit_advisory 提交你的谏言——不要做冗长调查，直接进谏。" }
                            }
                        };
                        response = await AIChatClient.SendMessage(
                            retryCharPrompt, vassal.HeroRef, includeTools: true, intent: "advisory");
                        submittedAdvisory = IsAdvisorySubmitted(response);
                    }

                    DebugLogger.Log($"谏言 agent={vassal.Id} kingdom={kingdomName} submitted={submittedAdvisory} contentLen={response.Content?.Length ?? 0} reasoning={DebugLogger.Truncate(response.LastReasoning, 400)}");

                    if (submittedAdvisory)
                    {
                        // submit_advisory 工具已负责归档，不重复写入
                        if (!string.IsNullOrEmpty(response.Content))
                            PromptManager.AppendChatLogFor(vassal.Id, vassal.Id, "assistant", response.Content);
                    }
                    else
                    {
                        var content = response.Content?.Trim();
                        if (content == "（已通过工具处理完毕）" || content == "（领主沉默不语）")
                            content = "";

                        if (!string.IsNullOrEmpty(content))
                        {
                            PromptManager.AppendChatLogFor(vassal.Id, vassal.Id, "assistant", content);
                            // 一次写入（表头+正文）：避免分两次追加导致读者卡在只有表头的半条
                            SafeFileIO.AppendAllText(advisoryFile,
                                $"\n[{currentTime}] {vassal.Name}（{vassal.Title}）谏言：\n{content}\n");
                        }
                        else
                        {
                            SafeFileIO.AppendAllText(advisoryFile,
                                $"\n[{currentTime}] {vassal.Name}（{vassal.Title}）谏言：\n（未发表公开谏言）\n");
                        }
                    }

                    // 强制私人笔记命名：无论 LLM 写到哪里，统一归入 personal_notes.txt
                    try
                    {
                        var decisionsDir = Path.Combine(PromptManager.CampaignDir, "NPCs", vassal.Id, "decisions");
                        if (Directory.Exists(decisionsDir))
                        {
                            var notesPath = Path.Combine(decisionsDir, "personal_notes.txt");
                            foreach (var legacyFile in Directory.GetFiles(decisionsDir, "advisory_*.txt"))
                            {
                                if (!File.Exists(notesPath))
                                    File.Move(legacyFile, notesPath);
                                else
                                {
                                    File.AppendAllText(notesPath, "\n" + File.ReadAllText(legacyFile), Encoding.UTF8);
                                    File.Delete(legacyFile);
                                }
                            }
                        }
                    }
                    catch { }

                    MainThreadExecutor.DisplayMessage(new InformationMessage(
                        $"{vassal.Name} 已向{kingdomName}国王进谏完成。",
                        Colors.Cyan));
                }
                finally
                {
                    if (prevAgentId != null && prevTargetId != null)
                    {
                        var prevAgent = EntityManager.GetEntityById(prevAgentId);
                        var prevTarget = EntityManager.GetEntityById(prevTargetId);
                        if (prevAgent?.HeroRef != null && prevTarget?.HeroRef != null)
                            EntityManager.ActivateInteraction(prevAgent.HeroRef, prevTarget.HeroRef);
                    }
                }
            }
            catch (Exception ex)
            {
                MainThreadExecutor.DisplayMessage(new InformationMessage(
                    $"[AI编年史] 封臣谏言处理异常：{ex.Message}",
                    Colors.Red));
            }
        }
    }
}
