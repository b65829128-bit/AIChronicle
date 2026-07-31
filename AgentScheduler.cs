using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        SpecialChronicle
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
        private static readonly List<(CampaignTime DueTime, ActivationEvent Event)> _delayedEvents = new();
        private static Task? _currentTask;
        private static int _currentProcessingDepth = -1;
        private static readonly Dictionary<Kingdom, CampaignTime> _lastKingActivation = new();
        private static readonly Dictionary<Kingdom, CampaignTime> _lastKingDailyCheck = new();
        private static int _warmupFrames = 120;
        private static int _nextKingIndex = 0;
        private static readonly Dictionary<string, CampaignTime> _lastProposalActivation = new();
        private static ActivationEvent? _pendingPlayerProposal;
        private static int _lastChronicleYear;
        private static bool _historianInitialized;
        private static int _warmupFramesHistorian = 60;
        private static readonly Random _rng = new();
        private static readonly Dictionary<Kingdom, string> _lastAdvisorySpeaker = new();
        private static readonly Dictionary<Kingdom, CampaignTime> _lastAdvisoryCheck = new();

        public static bool IsProcessing => _currentTask != null && !_currentTask.IsCompleted;
        public static int CurrentProcessingDepth => _currentProcessingDepth;

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
            InformationManager.DisplayMessage(new InformationMessage(
                "[MyFirstMod] 外交计时器已重置，所有国王将在接下来的游戏日中依次被激活。",
                Colors.Cyan));
        }

        public static void ForceAdvisory()
        {
            _lastAdvisoryCheck.Clear();
            _lastAdvisorySpeaker.Clear();
            InformationManager.DisplayMessage(new InformationMessage(
                "[MyFirstMod] 封臣谏言计时器已重置，封臣们将在接下来的游戏日中陆续进谏。",
                Colors.Cyan));
        }

        public static void QueueEvent(ActivationEvent evt)
        {
            _eventQueue.Enqueue(evt);
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
                        _eventQueue.Enqueue(_delayedEvents[i].Event);
                        _delayedEvents.RemoveAt(i);
                    }
                }
            }
        }

        public static void Tick()
        {
            if (_currentTask != null && !_currentTask.IsCompleted) return;
            _currentTask = null;

            CheckDelayedEvents();

            if (!_eventQueue.TryDequeue(out var evt))
            {
                CheckYearAdvance();
                if (_eventQueue.Count <= 3)
                {
                    CheckKingActivations();
                    CheckAdvisoryActivations();
                }
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

        public static void QueueSpecialChronicle(string eventSummary)
        {
            if (string.IsNullOrEmpty(PromptManager.CampaignDir)) return;

            var isBiography = eventSummary.StartsWith("重要人物之死");
            var prompt = isBiography
                ? PromptManager.LoadBiographyPrompt()
                : PromptManager.LoadSpecialChroniclePrompt();
            var content = prompt.Replace("{event_summary}", eventSummary);

            QueueEvent(new ActivationEvent
            {
                Type = ActivationEventType.SpecialChronicle,
                AgentId = "__historian__",
                TargetId = "__historian__",
                Content = content,
                Depth = 0
            });
        }

        private static async Task ProcessEvent(ActivationEvent evt)
        {
            var prevAgentId = EntityManager.ActiveAgentId;
            var prevTargetId = EntityManager.ActiveTargetId;

            try
            {
                if (evt.Type == ActivationEventType.YearlyChronicle || evt.Type == ActivationEventType.SpecialChronicle)
                {
                    await ProcessHistorianEvent(evt);
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
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{agentName} 正在重新评估当前任务...",
                        Colors.Cyan));
                }
                else if (evt.Type == ActivationEventType.KingDiplomacy)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{agentName} 正在处理外交事务...",
                        Colors.Cyan));
                }
                else if (evt.Type == ActivationEventType.PlanCheckIn)
                {
                    var shortContent = evt.Content.Length > 80 ? evt.Content.Substring(0, 77) + "..." : evt.Content;
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{agentName} {shortContent}，正在继续执行计划...",
                        Colors.Cyan));
                }
                else
                {
                    InformationManager.DisplayMessage(new InformationMessage(
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
                    ChatHistory = new List<ChatHistoryEntry>
                    {
                        new() { Role = "user", Content = evt.Content }
                    }
                };

                var intent = evt.Type switch
                {
                    ActivationEventType.BehaviorCheckIn => "chat",
                    ActivationEventType.KingDiplomacy => "diplomacy",
                    ActivationEventType.PlanCheckIn => "chat",
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

        private static void HandlePlayerEvent(ActivationEvent evt, Entity playerEntity, Entity senderEntity)
        {
            if (evt.Type == ActivationEventType.KingDiplomacy)
            {
                _pendingPlayerProposal = evt;
                return;
            }

            if (evt.Type == ActivationEventType.LetterReceived)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"你收到了来自 {senderEntity.Name} 的一封信。按 O 键打开信箱查看。",
                    Colors.Cyan));
                return;
            }
        }

        public static void CheckPlayerProposal()
        {
            var evt = _pendingPlayerProposal;
            if (evt == null) return;
            _pendingPlayerProposal = null;

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
                InformationManager.DisplayMessage(new InformationMessage(
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
                    var savedHero = AIChatClient.CurrentHero;
                    AIChatClient.CurrentHero = null;
                    try
                    {
                        var result = DiplomacyService.ExecuteRespondToProposal(proposalId, true);
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"[外交] {result}", Colors.Green));
                    }
                    finally { AIChatClient.CurrentHero = savedHero; }
                },
                () =>
                {
                    var savedHero = AIChatClient.CurrentHero;
                    AIChatClient.CurrentHero = null;
                    try
                    {
                        var result = DiplomacyService.ExecuteRespondToProposal(proposalId, false);
                        InformationManager.DisplayMessage(new InformationMessage(
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
                InformationManager.DisplayMessage(new InformationMessage(
                    $"史官正在编纂{eventLabel}...",
                    Colors.Cyan));

                EntityManager.ActivateHistorian();

                var charPrompt = new CharacterPrompt
                {
                    HeroId = "__historian__",
                    HeroName = "史官",
                    ChatHistory = new List<ChatHistoryEntry>
                    {
                        new() { Role = "user", Content = evt.Content }
                    }
                };

                var response = await AIChatClient.SendMessage(
                    charPrompt, hero: null, includeTools: true, intent: "historian");

                var expectedYear = ExtractYearFromContent(evt.Content);
                var chronicleDir = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "history", "chronicles");
                var fileExists = false;
                if (expectedYear > 0 && Directory.Exists(chronicleDir))
                {
                    var expectedFile = Path.Combine(chronicleDir, $"chronicle_{expectedYear}.txt");
                    fileExists = File.Exists(expectedFile);
                }
                else if (Directory.Exists(chronicleDir))
                {
                    fileExists = Directory.GetFiles(chronicleDir, "chronicle_*.txt").Length > 0;
                }

                if (fileExists)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"史官已完成{eventLabel}的编纂。",
                        Colors.Green));
                }
                else if (!string.IsNullOrEmpty(response.Content))
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"史官{eventLabel}编纂已结束，但编年史文件未生成（可能读取史料失败）。",
                        Colors.Yellow));
                }
                else
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"史官{eventLabel}编纂未产生文本输出。",
                        Colors.Yellow));
                }
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
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

        // ============ 封臣谏言系统 ============

        private static void CheckAdvisoryActivations()
        {
            if (Campaign.Current == null) return;
            if (_warmupFrames > 0) return;
            if (_currentTask != null && !_currentTask.IsCompleted) return;
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

                InformationManager.DisplayMessage(new InformationMessage(
                    $"{leader.Name} 正在向{kingdom.Name}国王进谏...",
                    Colors.Cyan));

                _currentTask = Task.Run(() => ProcessAdvisory(leader, kingdom));
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

                    var submittedAdvisory = response.ToolCalls.Any(tc => tc.Name == "submit_advisory");

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
                            var header = $"\n[{currentTime}] {vassal.Name}（{vassal.Title}）谏言：\n";
                            File.AppendAllText(advisoryFile, header, Encoding.UTF8);
                            File.AppendAllText(advisoryFile, content + "\n", Encoding.UTF8);
                        }
                        else
                        {
                            var header = $"\n[{currentTime}] {vassal.Name}（{vassal.Title}）谏言：\n";
                            File.AppendAllText(advisoryFile, header, Encoding.UTF8);
                            File.AppendAllText(advisoryFile, "（未发表公开谏言）\n", Encoding.UTF8);
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

                    InformationManager.DisplayMessage(new InformationMessage(
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
                InformationManager.DisplayMessage(new InformationMessage(
                    $"[MyFirstMod] 封臣谏言处理异常：{ex.Message}",
                    Colors.Red));
            }
        }
    }
}
