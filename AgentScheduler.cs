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

namespace MyFirstMod
{
    public enum ActivationEventType
    {
        LetterReceived,
        BehaviorCheckIn,
        KingDiplomacy,
        PlanCheckIn,
        YearlyChronicle,
        SpecialChronicle,
        Advisory,
        FiefReview
    }

    public class ActivationEvent
    {
        public ActivationEventType Type { get; set; }
        public string AgentId { get; set; } = "";
        public string TargetId { get; set; } = "";
        public string Content { get; set; } = "";
        public int Depth { get; set; }

        /// <summary>调度优先级：0 最高（史官，永不跳过），4 最低（概率激活的谏言）。</summary>
        public int Priority => Type switch
        {
            ActivationEventType.YearlyChronicle or ActivationEventType.SpecialChronicle => 0,
            ActivationEventType.KingDiplomacy => 1,
            ActivationEventType.LetterReceived => 2,
            ActivationEventType.BehaviorCheckIn or ActivationEventType.PlanCheckIn or ActivationEventType.FiefReview => 3,
            _ => 4
        };
    }

    public static class AgentScheduler
    {
        // 有限并行 + 优先级：5 个按优先级分队列（P0 最高），最多 MaxConcurrent 个任务同时在飞
        private static readonly ConcurrentQueue<ActivationEvent>[] _priorityQueues = new ConcurrentQueue<ActivationEvent>[5];
        private static readonly List<(CampaignTime DueTime, ActivationEvent Event)> _delayedEvents = new();
        private static int _inFlightCount;
        private static int MaxConcurrent => Math.Max(1, MySettings.Instance?.MaxAgentConcurrency ?? 5);
        private static readonly AsyncLocal<int> _currentProcessingDepth = new();

        static AgentScheduler()
        {
            for (int i = 0; i < _priorityQueues.Length; i++)
                _priorityQueues[i] = new ConcurrentQueue<ActivationEvent>();
        }
        private static readonly Dictionary<Kingdom, CampaignTime> _lastKingActivation = new();
        private static readonly Dictionary<Kingdom, CampaignTime> _lastKingDailyCheck = new();
        private static int _warmupFrames = 120;
        private static int _nextKingIndex = 0;
        private static readonly Dictionary<string, CampaignTime> _lastProposalActivation = new();
        // 修复：玩家外交提案改为队列逐个弹出——单槽位在并发时第二个王国覆盖第一个，提案被吞
        private static readonly System.Collections.Concurrent.ConcurrentQueue<ActivationEvent> _pendingPlayerProposals = new();
        private static bool _playerProposalShowing;
        private static int _lastChronicleYear;
        private static bool _historianInitialized;
        private static int _warmupFramesHistorian = 60;
        private static readonly Random _rng = new();
        private static readonly Dictionary<Kingdom, string> _lastAdvisorySpeaker = new();
        private static readonly Dictionary<Kingdom, CampaignTime> _lastAdvisoryCheck = new();

        public static bool IsProcessing => _inFlightCount > 0;
        public static int CurrentProcessingDepth
        {
            get => _currentProcessingDepth.Value;
            set => _currentProcessingDepth.Value = value;
        }

        /// <summary>战役结束/切档时清空调度器跨档状态，避免新档残留旧档的计时器/事件/编年史年份。</summary>
        public static void ResetForNewCampaign()
        {
            for (int p = 0; p < _priorityQueues.Length; p++)
                while (_priorityQueues[p].TryDequeue(out _)) { }
            _delayedEvents.Clear();
            Interlocked.Exchange(ref _inFlightCount, 0);
            _currentProcessingDepth.Value = -1;
            lock (_specialChronicleLock)
            {
                _pendingSpecialSummaries.Clear();
                _specialQueued = false;
            }
            _lastKingActivation.Clear();
            _lastKingDailyCheck.Clear();
            _lastProposalActivation.Clear();
            while (_pendingPlayerProposals.TryDequeue(out _)) { }
            _playerProposalShowing = false;
            _lastChronicleYear = 0;
            _historianInitialized = false;
            _lastAdvisorySpeaker.Clear();
            _lastAdvisoryCheck.Clear();
            _nextKingIndex = 0;
            _warmupFrames = 120;
            _warmupFramesHistorian = 60;
        }

        public static void RecordProposalActivation(string entityId)
        {
            _lastProposalActivation[entityId] = CampaignTime.Now;
        }

        public static void ForceDiplomacyRound()
        {
            _lastKingActivation.Clear();
            _lastKingDailyCheck.Clear();
            _lastProposalActivation.Clear();
            foreach (var k in Kingdom.All)
            {
                if (!k.IsEliminated)
                    _lastKingActivation[k] = CampaignTime.Zero;
            }
            MainThreadExecutor.DisplayMessage(new InformationMessage(
                "[MyFirstMod] 外交计时器已重置，所有国王将在接下来的游戏日中依次被激活。",
                Colors.Cyan));
        }

        public static void ForceAdvisory()
        {
            _lastAdvisoryCheck.Clear();
            _lastAdvisorySpeaker.Clear();
            MainThreadExecutor.DisplayMessage(new InformationMessage(
                "[MyFirstMod] 封臣谏言计时器已重置，封臣们将在接下来的游戏日中陆续进谏。",
                Colors.Cyan));
        }

        public static void QueueEvent(ActivationEvent evt)
        {
            var p = Math.Max(0, Math.Min(4, evt.Priority));
            _priorityQueues[p].Enqueue(evt);
        }

        public static void QueueDelayedEvent(ActivationEvent evt, float delayHours)
        {
            var dueTime = CampaignTime.HoursFromNow(delayHours);
            lock (_delayedEvents)
            {
                _delayedEvents.Add((dueTime, evt));
            }
        }

        private static void CheckDelayedEvents()
        {
            lock (_delayedEvents)
            {
                for (int i = _delayedEvents.Count - 1; i >= 0; i--)
                {
                    if (_delayedEvents[i].DueTime.IsPast)
                    {
                        QueueEvent(_delayedEvents[i].Event);
                        _delayedEvents.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>取最高优先级队列的事件（P0 优先）。</summary>
        private static bool TryDequeueHighestPriority(out ActivationEvent evt)
        {
            for (int p = 0; p < _priorityQueues.Length; p++)
            {
                if (_priorityQueues[p].TryDequeue(out evt!))
                    return true;
            }
            evt = null!;
            return false;
        }

        private static int PendingEventCount()
        {
            int count = 0;
            for (int p = 0; p < _priorityQueues.Length; p++)
                count += _priorityQueues[p].Count;
            return count;
        }

        /// <summary>占用一个并发槽位执行处理函数，完成后释放（线程安全计数）。</summary>
        private static void SpawnTask(Func<Task> processor)
        {
            Interlocked.Increment(ref _inFlightCount);
            _ = Task.Run(async () =>
            {
                try { await processor(); }
                finally { Interlocked.Decrement(ref _inFlightCount); }
            });
        }

        public static void Tick()
        {
            if (_inFlightCount >= MaxConcurrent) return;

            CheckDelayedEvents();

            if (!TryDequeueHighestPriority(out var evt))
            {
                CheckYearAdvance();
                if (PendingEventCount() <= 3)
                {
                    CheckKingActivations();
                    CheckAdvisoryActivations();
                }
                return;
            }

            var maxDepth = MySettings.Instance?.MaxLetterChainDepth ?? 5;
            if (evt.Depth > maxDepth)
            {
                MainThreadExecutor.DisplayMessage(new InformationMessage(
                    $"[MyFirstMod] 信件级联已达上限({maxDepth}+)，剩余信件已存档不再处理。",
                    Colors.Yellow));
                return;
            }

            SpawnTask(() => ProcessEvent(evt));
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
                if (ruler == null || !ruler.IsAlive)
                    continue;

                var entity = EntityManager.GetOrCreateEntity(ruler);
                if (entity == null) continue;
                if (entity.Controller == EntityController.Human) continue;

                if (_lastProposalActivation.TryGetValue(entity.Id, out var lastProposalTime)
                    && (CampaignTime.Now - lastProposalTime).ToDays < (MySettings.Instance?.KingCooldownDays ?? 3))
                    continue;

                var now = CampaignTime.Now;
                var cooldownDays = MySettings.Instance?.KingCooldownDays ?? 3;

                if (_lastKingActivation.TryGetValue(kingdom, out var lastActivation)
                    && (now - lastActivation).ToDays < cooldownDays)
                    continue;

                if (!_lastKingDailyCheck.TryGetValue(kingdom, out var lastCheck))
                {
                    _lastKingDailyCheck[kingdom] = now;
                    continue;
                }
                if ((now - lastCheck).ToDays < 1)
                    continue;
                _lastKingDailyCheck[kingdom] = now;

                var chance = MySettings.Instance?.DiplomacyChancePerDay ?? 0.1f;
                if (_rng.NextDouble() > chance)
                    continue;

                _lastKingActivation[kingdom] = now;

                var pendingProposals = AgentManager.ListPendingProposals(entity.Id);
                var proposalLines = pendingProposals.Count > 0
                    ? $"\n（提示：先调用 query_pending_proposals 查看 {pendingProposals.Count} 份待处理的外交提案）\n"
                    : "";

                var currentYear = CampaignTime.Now.GetYear;
                var advisoryNote = $"\n（提示：先用 read_file 阅读 World/advisory/{kingdom.Name}_{currentYear}.txt 了解封臣们的近期谏言。群臣意见是你的决策参考，但你的决定权至高无上。）\n";

                var activationMsg =
                    $"你是{kingdom.Name}的至高统治者。审视你的王国局势，凭自己的判断做出外交决断。\n\n"
                    + $"步骤1：调用 query_pending_proposals 查看是否有待处理的提案，有则用 respond_to_diplomacy_proposal 逐一处理\n"
                    + $"步骤2：调用 query_war_status 了解当前所有战争的战况\n"
                    + proposalLines
                    + advisoryNote
                    + $"\n（提示：你可以在 goals/strategy.txt 中记录你的长期战略方针，如交好谁、提防谁、扩张方向等。每次外交审视前，先 read_file 查看已有战略，据此做出连贯的决策；局势变化时可 edit_file 调整。）\n\n"
                    + $"然后依据你自己的判断采取行动——以下是你可用的外交工具：\n"
                    + "- propose_peace：结束一场战争（可附带赔款条件）\n"
                    + "- propose_alliance：与中立王国结盟\n"
                    + "- propose_trade：与中立王国签订贸易协定\n"
                    + "- declare_war：宣战\n\n"
                    + "不要幻想或虚构数据。不要用 send_letter 处理外交，外交方案只用上述 function 发起。";

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

        private static void CheckYearAdvance()
        {
            if (Campaign.Current == null) return;
            if (string.IsNullOrEmpty(PromptManager.CampaignDir)) return;
            if (_warmupFramesHistorian > 0)
            {
                _warmupFramesHistorian--;
                return;
            }

            if (!_historianInitialized)
            {
                _historianInitialized = true;
                _lastChronicleYear = CampaignTime.Now.GetYear;
                return;
            }

            var currentYear = CampaignTime.Now.GetYear;
            if (currentYear <= _lastChronicleYear) return;

            var interval = MySettings.Instance?.ChronicleInterval ?? 1;
            if (currentYear - _lastChronicleYear < interval) return;

            for (var y = _lastChronicleYear; y < currentYear; y++)
            {
                if (y <= 0) continue;

                var yearFile = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "history", $"events_{y}.txt");
                if (!File.Exists(yearFile)) continue;

                var chronicleFile = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "history", "chronicles", $"chronicle_{y}.txt");
                if (File.Exists(chronicleFile)) continue;

                QueueEvent(new ActivationEvent
                {
                    Type = ActivationEventType.YearlyChronicle,
                    AgentId = "__historian__",
                    TargetId = "__historian__",
                    Content = PromptManager.LoadYearlyChroniclePrompt().Replace("{year}", y.ToString()),
                    Depth = 0
                });
            }

            _lastChronicleYear = currentYear;
        }

        // 史官合并缓冲：短时间内的多起专题事件（如玩家大屠杀触发连串"重要人物之死"）合并成一次史官激活，
        // 避免杀人潮导致史官被疯狂激活、P0 队列被连环编年史事件淹没。
        private static readonly object _specialChronicleLock = new();
        private static readonly List<string> _pendingSpecialSummaries = new();
        private static bool _specialQueued;

        public static void QueueSpecialChronicle(string eventSummary)
        {
            if (string.IsNullOrEmpty(PromptManager.CampaignDir)) return;

            lock (_specialChronicleLock)
            {
                _pendingSpecialSummaries.Add(eventSummary);
                if (_specialQueued) return; // 已有一个待处理专题史事件，追加到缓冲即可
                _specialQueued = true;
            }

            QueueEvent(new ActivationEvent
            {
                Type = ActivationEventType.SpecialChronicle,
                AgentId = "__historian__",
                TargetId = "__historian__",
                Content = "", // 真实内容在处理时从缓冲取（合并多起事件）
                Depth = 0
            });
        }

        /// <summary>封地审视激活：某家族被夺封后，队列激活其领袖审视处境并决定反应（矛盾触发点）。</summary>
        public static void QueueFiefReview(string agentEntityId, string content)
        {
            if (string.IsNullOrEmpty(agentEntityId)) return;
            QueueEvent(new ActivationEvent
            {
                Type = ActivationEventType.FiefReview,
                AgentId = agentEntityId,
                TargetId = agentEntityId, // 自省：对方是自己
                Content = content,
                Depth = 1
            });
        }

        /// <summary>取走并清空合并缓冲，构建史官提示词（传记 vs 专题史按首条判断）。</summary>
        private static string ConsumeSpecialChronicleContent()
        {
            lock (_specialChronicleLock)
            {
                if (_pendingSpecialSummaries.Count == 0) return "";
                var summaries = new List<string>(_pendingSpecialSummaries);
                _pendingSpecialSummaries.Clear();
                _specialQueued = false;

                var isBiography = summaries[0].StartsWith("重要人物之死");
                var prompt = isBiography
                    ? PromptManager.LoadBiographyPrompt()
                    : PromptManager.LoadSpecialChroniclePrompt();
                var joined = string.Join("\n", summaries);
                return prompt.Replace("{event_summary}", joined);
            }
        }

        /// <summary>构建事件处理的对话上下文：信件处理带上双方此前聊天记录，保证对方记得你（跨信记忆连续性）。
        /// 若最后一条日志已是同内容 user 消息（信件可能已入日志），跳过重复追加。</summary>
        private static List<ChatHistoryEntry> BuildEventChatHistory(ActivationEvent evt)
        {
            var history = new List<ChatHistoryEntry>();
            if (evt.Type == ActivationEventType.LetterReceived)
            {
                var prior = PromptManager.LoadChatLogFor(evt.AgentId, evt.TargetId);
                if (prior != null) history.AddRange(prior);
            }
            if (history.Count == 0
                || !(history[history.Count - 1].Role == "user" && history[history.Count - 1].Content == evt.Content))
            {
                history.Add(new() { Role = "user", Content = evt.Content });
            }
            return history;
        }

        private static async Task ProcessEvent(ActivationEvent evt)
        {
            var prevAgentId = EntityManager.ActiveAgentId;
            var prevTargetId = EntityManager.ActiveTargetId;
            // 有限并行：信件级联深度按任务隔离（AsyncLocal），避免并发任务互相覆盖
            _currentProcessingDepth.Value = evt.Depth;

            try
            {
                if (evt.Type == ActivationEventType.YearlyChronicle || evt.Type == ActivationEventType.SpecialChronicle)
                {
                    await ProcessHistorianEvent(evt);
                    return;
                }

                if (evt.Type == ActivationEventType.Advisory)
                {
                    var vassal = EntityManager.GetEntityById(evt.AgentId);
                    var kingdom = vassal?.HeroRef?.Clan?.Kingdom;
                    if (vassal != null && kingdom != null)
                        await ProcessAdvisory(vassal, kingdom);
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
                            $"你收到了{replySenderName}的回信，按 O 键打开信箱查看。", Colors.Green));
                    }
                }
            }
            catch (Exception ex)
            {
                MainThreadExecutor.DisplayMessage(new InformationMessage(
                    $"[MyFirstMod] 信件处理异常：{ex.Message}", Colors.Red));
            }
            finally
            {
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
                    $"你收到了来自 {senderEntity.Name} 的一封信。按 O 键打开信箱查看。",
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

        private static async Task ProcessHistorianEvent(ActivationEvent evt)
        {
            var settings = MySettings.Instance;
            if (settings == null || string.IsNullOrEmpty(settings.ApiKey))
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
                        "[MyFirstMod] 史官专题史缓冲为空，跳过。", Colors.Yellow));
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
                    $"[MyFirstMod] 史官处理异常：{ex.Message}",
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

        /// <summary>是否算"已提交谏言"：仅当 submit_advisory 实际执行成功（结果非错误）。</summary>
        private static bool IsAdvisorySubmitted(ChatResponse response)
        {
            return response.ToolCalls.Any(tc =>
                tc.Name == "submit_advisory"
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

                    var advisoryDir = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "advisory");
                    Directory.CreateDirectory(advisoryDir);
                    var advisoryFile = Path.Combine(advisoryDir, $"{kingdomName}_{currentYear}.txt");

                    var advisoryContent =
                        $"你是{vassal.Name}，{vassal.Title}，{kingdomName}的氏族领袖。\n\n"
                        + $"当前时间：{currentTime}\n\n"
                        + $"作为{kingdomName}的封臣，请审视王国当前的局势，向国王进谏。\n"
                        + "步骤：\n"
                        + "1. 如需回顾你之前的私人记录，可用 read_file 阅读 decisions/personal_notes.txt\n"
                        + "2. 用 query_world_state、query_war_status 等工具了解当前局势\n"
                        + "3. 用 submit_advisory 工具提交你的公开谏言（在 content 参数里直接写谏言正文）\n"
                        + "\n注意：谏言正文直接填进 submit_advisory 的 content 参数，不要写在你的回复文本里。"
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
                    $"[MyFirstMod] 封臣谏言处理异常：{ex.Message}",
                    Colors.Red));
            }
        }
    }
}
