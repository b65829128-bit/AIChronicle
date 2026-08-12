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
        private static async Task ProcessEvent(ActivationEvent evt)
        {
            var prevAgentId = EntityManager.ActiveAgentId;
            var prevTargetId = EntityManager.ActiveTargetId;
            // 有限并行：信件级联深度按任务隔离（AsyncLocal），避免并发任务互相覆盖
            _currentProcessingDepth.Value = evt.Depth;

            try
            {
                // 并入去重期间累积的补充内容（如攻城归属指示），再构建审视上下文
                if (evt.Type == ActivationEventType.KingDiplomacy)
                {
                    lock (_diplomacyLock)
                    {
                        if (_diplomacySupplements.TryGetValue(evt.AgentId, out var sb))
                        {
                            evt.Content += "\n\n" + sb;
                            _diplomacySupplements.Remove(evt.AgentId);
                        }
                    }
                }

                if (evt.Type == ActivationEventType.YearlyChronicle || evt.Type == ActivationEventType.SpecialChronicle)
                {
                    await ProcessHistorianEvent(evt);
                    return;
                }

                if (evt.Type == ActivationEventType.ClanReplenishment)
                {
                    await ProcessClanReplenishmentEvent(evt);
                    return;
                }

                var agentEntity = EntityManager.GetOrCreateEntityById(evt.AgentId);
                var targetEntity = EntityManager.GetOrCreateEntityById(evt.TargetId);

                if (agentEntity?.HeroRef == null || targetEntity?.HeroRef == null)
                    return;

                if (agentEntity.Controller != EntityController.Agent)
                {
                    if (agentEntity.Controller == EntityController.Human)
                        HandlePlayerEvent(evt, agentEntity, targetEntity);
                    return;
                }

                var agentName = agentEntity.Name;
                var targetName = targetEntity.Name;

                if (evt.Type == ActivationEventType.BehaviorCheckIn)
                {
                    MainThreadExecutor.DisplayMessage(new InformationMessage(
                        $"{agentName} 正在重新评估当前任务...",
                        Colors.Cyan));
                }
                else if (evt.Type == ActivationEventType.KingDiplomacy)
                {
                    MainThreadExecutor.DisplayMessage(new InformationMessage(
                        $"{agentName} 正在处理内外政务...",
                        Colors.Cyan));
                }
                else if (evt.Type == ActivationEventType.PlanCheckIn)
                {
                    var shortContent = evt.Content.Length > 80 ? evt.Content.Substring(0, 77) + "..." : evt.Content;
                    MainThreadExecutor.DisplayMessage(new InformationMessage(
                        $"{agentName} {shortContent}，正在继续执行计划...",
                        Colors.Cyan));
                }
                else if (evt.Type == ActivationEventType.FiefReview)
                {
                    MainThreadExecutor.DisplayMessage(new InformationMessage(
                        $"{agentName} 发现自己被夺封了...",
                        Colors.Yellow));
                }
                else if (evt.Type == ActivationEventType.KingConsult)
                {
                    MainThreadExecutor.DisplayMessage(new InformationMessage(
                        $"{targetName} 遣使问询 {agentName}，{agentName} 正在回应...",
                        Colors.Cyan));
                }
                else if (evt.Type == ActivationEventType.SelfReview)
                {
                    MainThreadExecutor.DisplayMessage(new InformationMessage(
                        $"{agentName} 正在独自思量自己的处境...",
                        Colors.Cyan));
                }
                else if (evt.Type == ActivationEventType.EnvoyReceived)
                {
                    MainThreadExecutor.DisplayMessage(new InformationMessage(
                        $"{targetName} 遣密使来见 {agentName}，{agentName} 正在考虑如何回应...",
                        Colors.Cyan));
                }
                else
                {
                    MainThreadExecutor.DisplayMessage(new InformationMessage(
                        $"{targetName} 给 {agentName} 写了一封信",
                        Colors.Cyan));

                    var proposalsBetween = AgentManager.GetProposalsBetween(evt.AgentId, evt.TargetId);
                    if (proposalsBetween.Count > 0)
                    {
                        var proposalNote = "\n\n【系统提示】你们之间存在待处理的外交提案：\n";
                        foreach (var (id, type) in proposalsBetween)
                        {
                            var typeName = type switch
                            {
                                "peace" => "议和",
                                "alliance" => "结盟",
                                "trade" => "贸易协定",
                                _ => type
                            };
                            var pContent = AgentManager.ReadDiplomacyProposal(id);
                            var proposerName = "?";
                            if (pContent != null)
                            {
                                var lines = pContent.Split('\n');
                                foreach (var line in lines)
                                {
                                    if (line.StartsWith("proposer="))
                                    {
                                        var pid = line.Substring(9);
                                        var pe = EntityManager.GetEntityById(pid);
                                        proposerName = pe?.Name ?? pid;
                                        break;
                                    }
                                }
                            }
                            proposalNote += $"- {proposerName} 提出的{typeName}提案（ID: {id}），尚待回应\n";
                        }
                        proposalNote += "这封信可能是对方关于提案的回复，你处理后可以考虑是否回应提案。";
                        evt.Content += proposalNote;
                    }
                }

                EntityManager.ActivateInteraction(agentEntity.HeroRef, targetEntity.HeroRef);

                // 记忆巩固（diary 权威化的保底）：自我审视类激活前，若日记落后于聊天记录，先补记 diary。
                // 否则国王照陈旧日记/战略行事（如"上次还要请和库赛特"，实则在最新聊天里已改为专攻库赛特）。
                // 静默执行，仅日记落后时触发一次便宜的巩固 pass，多数时候零成本。
                if (evt.Type is ActivationEventType.KingDiplomacy
                    or ActivationEventType.FiefReview
                    or ActivationEventType.KingConsult
                    or ActivationEventType.SelfReview
                    or ActivationEventType.EnvoyReceived)
                {
                    await MemoryConsolidator.EnsureDiaryCurrentAsync(evt.AgentId);
                }

                var charPrompt = new CharacterPrompt
                {
                    HeroId = agentEntity.Id,
                    HeroName = agentEntity.Name,
                    ChatHistory = BuildEventChatHistory(evt)
                };

                var intent = evt.Type switch
                {
                    ActivationEventType.BehaviorCheckIn => "chat",
                    ActivationEventType.KingDiplomacy => "diplomacy",
                    ActivationEventType.PlanCheckIn => "chat",
                    ActivationEventType.FiefReview => "fief_review",
                    ActivationEventType.KingConsult => "king_consult",
                    ActivationEventType.SelfReview => "self_review",
                    ActivationEventType.EnvoyReceived => "envoy_reply",
                    _ => "letter"
                };
                var response = await AIChatClient.SendMessage(
                    charPrompt, agentEntity.HeroRef, includeTools: true, intent: intent);

                if (!string.IsNullOrEmpty(response.Content))
                {
                    // 方案A：回信只进聊天线程（标记为信件📜），不再投递信箱收件箱——信箱退化为线程入口
                    var isLetterReply = evt.Type == ActivationEventType.LetterReceived;
                    PromptManager.AppendChatLogFor(evt.AgentId, evt.TargetId, "assistant", response.Content, isLetterReply);

                    if (isLetterReply && Hero.MainHero != null
                        && evt.TargetId == EntityManager.GetOrCreateEntity(Hero.MainHero).Id)
                    {
                        var replySenderName = EntityManager.GetEntityById(evt.AgentId)?.Name ?? "对方";
                        MainThreadExecutor.DisplayMessage(new InformationMessage(
                            $"你收到了{replySenderName}的回信，按 O 键打开书信面板查看。", Colors.Green));
                    }
                }

                // 结束声明：明确告知玩家该次激活已处理完毕——避免"只有开始没有结束"的困惑
                var doneMsg = evt.Type switch
                {
                    ActivationEventType.KingDiplomacy => $"{agentName} 已处理完内外政务。",
                    ActivationEventType.SelfReview => $"{agentName} 已完成对自己的审视。",
                    ActivationEventType.FiefReview => $"{agentName} 已处置好夺封之事。",
                    ActivationEventType.KingConsult => $"{agentName} 已回应 {targetName} 的使者。",
                    ActivationEventType.EnvoyReceived => $"{agentName} 已处理 {targetName} 的密使。",
                    ActivationEventType.BehaviorCheckIn => $"{agentName} 已重新评估当前任务。",
                    ActivationEventType.PlanCheckIn => $"{agentName} 已继续执行计划。",
                    _ => ""
                };
                if (doneMsg.Length > 0)
                    MainThreadExecutor.DisplayMessage(new InformationMessage(doneMsg, Colors.Green));
            }
            catch (Exception ex)
            {
                MainThreadExecutor.DisplayMessage(new InformationMessage(
                    $"[AI编年史] 信件处理异常：{ex.Message}", Colors.Red));
            }
            finally
            {
                // 审视结束，释放该国王的去重标记（下次可再激活）
                if (evt.Type == ActivationEventType.KingDiplomacy)
                {
                    lock (_diplomacyLock)
                    {
                        _diplomacyReviewAgents.Remove(evt.AgentId);
                        _diplomacySupplements.Remove(evt.AgentId);
                    }
                }
                _currentProcessingDepth.Value = -1;
                if (prevAgentId != null && prevTargetId != null)
                {
                    var prevAgent = EntityManager.GetOrCreateEntityById(prevAgentId);
                    var prevTarget = EntityManager.GetOrCreateEntityById(prevTargetId);
                    if (prevAgent?.HeroRef != null && prevTarget?.HeroRef != null)
                        EntityManager.ActivateInteraction(prevAgent.HeroRef, prevTarget.HeroRef);
                }
            }
        }

        private static void HandlePlayerEvent(ActivationEvent evt, Entity playerEntity, Entity senderEntity)
        {
            if (evt.Type == ActivationEventType.KingDiplomacy)
            {
                _pendingPlayerProposals.Enqueue(evt);
                return;
            }

            if (evt.Type == ActivationEventType.LetterReceived)
            {
                MainThreadExecutor.DisplayMessage(new InformationMessage(
                    $"你收到了来自 {senderEntity.Name} 的一封信。按 O 键打开书信面板查看。",
                    Colors.Cyan));
                return;
            }

            if (evt.Type == ActivationEventType.FiefReview)
            {
                MainThreadExecutor.DisplayMessage(new InformationMessage(
                    $"你的封地遭变故：{evt.Content}",
                    Colors.Red));
                return;
            }

            if (evt.Type == ActivationEventType.KingConsult)
            {
                MainThreadExecutor.DisplayMessage(new InformationMessage(
                    $"{senderEntity.Name} 遣使问询你，按 M 键秘书处查看并回应。",
                    Colors.Cyan));
                return;
            }
        }

        public static void CheckPlayerProposal()
        {
            if (_playerProposalShowing) return;
            if (!_pendingPlayerProposals.TryDequeue(out var evt)) return;
            _playerProposalShowing = true;

            var playerEntity = EntityManager.GetOrCreateEntityById(evt.AgentId);
            if (playerEntity?.HeroRef == null) return;

            var pending = AgentManager.ListPendingProposals(playerEntity.Id);
            var senderEntity = EntityManager.GetOrCreateEntityById(evt.TargetId);
            var senderName = senderEntity?.Name ?? evt.TargetId;

            var relevantProposals = new List<string>();
            var relevantTypes = new List<string>();
            foreach (var p in pending)
            {
                var pContent = AgentManager.ReadDiplomacyProposal(p);
                if (pContent == null) continue;
                var (proposerId, _, type) = AgentManager.ParseProposalMeta(pContent);
                if (proposerId == evt.TargetId)
                {
                    relevantProposals.Add(p);
                    relevantTypes.Add(type);
                }
            }

            if (relevantProposals.Count == 0)
            {
                MainThreadExecutor.DisplayMessage(new InformationMessage(
                    $"{senderName} 向你发送了外交提案，但提案文件已丢失。",
                    Colors.Yellow));
                return;
            }

            var proposalId = relevantProposals[0];
            var proposalType = relevantTypes[0];
            var typeName = proposalType switch
            {
                "peace" => "议和",
                "alliance" => "结盟",
                "trade" => "贸易协定",
                _ => proposalType
            };

            InformationManager.ShowInquiry(new InquiryData(
                $"{senderName} 提议{typeName}",
                evt.Content,
                true, true, "接受", "拒绝",
                () =>
                {
                    _playerProposalShowing = false;
                    var savedHero = AIChatClient.CurrentHero;
                    AIChatClient.CurrentHero = null;
                    try
                    {
                        var result = DiplomacyService.ExecuteRespondToProposal(proposalId, true);
                        MainThreadExecutor.DisplayMessage(new InformationMessage(
                            $"[外交] {result}", Colors.Green));
                    }
                    finally { AIChatClient.CurrentHero = savedHero; }
                },
                () =>
                {
                    _playerProposalShowing = false;
                    var savedHero = AIChatClient.CurrentHero;
                    AIChatClient.CurrentHero = null;
                    try
                    {
                        var result = DiplomacyService.ExecuteRespondToProposal(proposalId, false);
                        MainThreadExecutor.DisplayMessage(new InformationMessage(
                            $"[外交] {result}", Colors.Yellow));
                    }
                    finally { AIChatClient.CurrentHero = savedHero; }
                }),
                pauseGameActiveState: true,
                prioritize: true);
        }

        /// <summary>家族补充：激活「天意」实体，让它按当前家族统计创建新家族（create_clan 工具）。</summary>
    }
}
