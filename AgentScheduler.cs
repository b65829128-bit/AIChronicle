using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace MyFirstMod
{
    public enum ActivationEventType
    {
        LetterReceived,
        BehaviorCheckIn,
        KingDiplomacy
    }

    public class ActivationEvent
    {
        public ActivationEventType Type { get; set; }
        public string AgentId { get; set; } = "";
        public string TargetId { get; set; } = "";
        public string Content { get; set; } = "";
        public int Depth { get; set; }
    }

    public static class AgentScheduler
    {
        private static readonly ConcurrentQueue<ActivationEvent> _eventQueue = new();
        private static Task? _currentTask;
        private static int _currentProcessingDepth = -1;
        private static readonly Dictionary<Kingdom, CampaignTime> _lastKingActivation = new();
        private static int _warmupFrames = 120;
        private static int _nextKingIndex = 0;

        public static bool IsProcessing => _currentTask != null && !_currentTask.IsCompleted;
        public static int CurrentProcessingDepth => _currentProcessingDepth;

        public static void QueueEvent(ActivationEvent evt)
        {
            _eventQueue.Enqueue(evt);
        }

        public static void Tick()
        {
            if (_currentTask != null && !_currentTask.IsCompleted) return;
            _currentTask = null;

            if (!_eventQueue.TryDequeue(out var evt))
            {
                CheckKingActivations();
                return;
            }

            var maxDepth = MySettings.Instance?.MaxLetterChainDepth ?? 5;
            if (evt.Depth > maxDepth)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[MyFirstMod] 信件级联已达上限({maxDepth}+)，剩余信件已存档不再处理。",
                    Colors.Yellow));
                return;
            }

            _currentProcessingDepth = evt.Depth;
            _currentTask = Task.Run(() => ProcessEvent(evt));
        }

        private static void CheckKingActivations()
        {
            if (Campaign.Current == null) return;
            if (_warmupFrames > 0)
            {
                _warmupFrames--;
                return;
            }

            var kingdoms = new List<Kingdom>();
            foreach (var k in Kingdom.All)
                if (!k.IsEliminated) kingdoms.Add(k);

            if (kingdoms.Count == 0) return;

            var attempts = 0;
            while (attempts < kingdoms.Count)
            {
                var idx = _nextKingIndex % kingdoms.Count;
                _nextKingIndex = (idx + 1) % kingdoms.Count;
                attempts++;

                var kingdom = kingdoms[idx];
                var ruler = kingdom.RulingClan?.Leader;
                if (ruler == null || ruler.IsPrisoner || ruler.IsFugitive || !ruler.IsAlive)
                    continue;

                var now = CampaignTime.Now;
                var intervalDays = MySettings.Instance?.KingActivationDays ?? 30;
                if (_lastKingActivation.TryGetValue(kingdom, out var lastActivation)
                    && (now - lastActivation).ToDays < intervalDays)
                    continue;

                _lastKingActivation[kingdom] = now;

                var entity = EntityManager.GetOrCreateEntity(ruler);
                if (entity == null) continue;

                var pendingProposals = AgentManager.ListPendingProposals(entity.Id);
                var proposalLines = "";
                if (pendingProposals.Count > 0)
                {
                    proposalLines = "\n你当前有待处理的外交提案：\n";
                    foreach (var p in pendingProposals)
                    {
                        var pContent = AgentManager.ReadDiplomacyProposal(p);
                        if (pContent != null)
                        {
                            var lines = pContent.Split('\n');
                            var proposerLine = lines.FirstOrDefault(l => l.StartsWith("proposer="))?.Substring(9) ?? "?";
                            var typeLine = lines.FirstOrDefault(l => l.StartsWith("type="))?.Substring(5) ?? "?";
                            var typeName = typeLine switch
                            {
                                "peace" => "议和",
                                "alliance" => "结盟",
                                "trade" => "贸易协定",
                                _ => typeLine
                            };
                            proposalLines += $"- {proposerLine} 提出{typeName}（ID: {p}）\n";
                        }
                    }
                    proposalLines += "先处理以上提案，否则不要做其他外交动作。\n";
                }

                var activationMsg =
                    $"你是{kingdom.Name}的至高统治者。现在审视你的王国外交局势。\n\n"
                    + $"步骤1：调用 query_war_status 查看战争状况。\n"
                    + proposalLines
                    + $"\n然后执行外交决策：\n"
                    + "- 对于不利的战争 → propose_peace（提出议和条件）\n"
                    + "- 对于有利的战争 → 继续，或 propose_peace 趁胜谈判\n"
                    + "- 需要盟友 → propose_alliance（只能向中立王国提议）\n"
                    + "- 发现战略良机 → declare_war（宣战）\n"
                    + "- 加强经济 → propose_trade（贸易协定）\n\n"
                    + "你的决策必须基于 query_war_status 的实数据。不要幻想、不要虚构。";

                QueueEvent(new ActivationEvent
                {
                    Type = ActivationEventType.KingDiplomacy,
                    AgentId = entity.Id,
                    TargetId = entity.Id,
                    Content = activationMsg,
                    Depth = 0
                });

                return;
            }
        }

        private static async Task ProcessEvent(ActivationEvent evt)
        {
            var prevAgentId = EntityManager.ActiveAgentId;
            var prevTargetId = EntityManager.ActiveTargetId;

            try
            {
                var agentEntity = EntityManager.GetOrCreateEntityById(evt.AgentId);
                var targetEntity = EntityManager.GetOrCreateEntityById(evt.TargetId);

                if (agentEntity?.HeroRef == null || targetEntity?.HeroRef == null)
                    return;

                if (agentEntity.Controller != EntityController.Agent)
                    return;

                var agentName = agentEntity.Name;
                var targetName = targetEntity.Name;

                if (evt.Type == ActivationEventType.BehaviorCheckIn)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"[MyFirstMod] {agentName} 正在重新评估当前任务...",
                        Colors.Cyan));
                }
                else if (evt.Type == ActivationEventType.KingDiplomacy)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"[MyFirstMod] {agentName} 正在处理外交事务...",
                        Colors.Cyan));
                }
                else
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"[MyFirstMod] {targetName} 给 {agentName} 写了一封信",
                        Colors.Cyan));
                }

                EntityManager.ActivateInteraction(agentEntity.HeroRef, targetEntity.HeroRef);

                var charPrompt = new CharacterPrompt
                {
                    HeroId = agentEntity.Id,
                    HeroName = agentEntity.Name,
                    ChatHistory = new List<ChatHistoryEntry>
                    {
                        new() { Role = "user", Content = evt.Content }
                    }
                };

                InformationManager.DisplayMessage(new InformationMessage(
                    $"[MyFirstMod] {agentName} 正在思考下一步行动...",
                    Colors.Cyan));

                var intent = evt.Type switch
                {
                    ActivationEventType.BehaviorCheckIn => "chat",
                    ActivationEventType.KingDiplomacy => "diplomacy",
                    _ => "letter"
                };
                var response = await AIChatClient.SendMessage(
                    charPrompt, agentEntity.HeroRef, includeTools: true, intent: intent);

                if (!string.IsNullOrEmpty(response.Content))
                {
                    PromptManager.AppendChatLogFor(evt.AgentId, evt.TargetId, "assistant", response.Content);
                }
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[MyFirstMod] 信件处理异常：{ex.Message}", Colors.Red));
            }
            finally
            {
                if (prevAgentId != null && prevTargetId != null)
                {
                    var prevAgent = EntityManager.GetOrCreateEntityById(prevAgentId);
                    var prevTarget = EntityManager.GetOrCreateEntityById(prevTargetId);
                    if (prevAgent?.HeroRef != null && prevTarget?.HeroRef != null)
                        EntityManager.ActivateInteraction(prevAgent.HeroRef, prevTarget.HeroRef);
                }
            }
        }
    }
}
