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
    public enum ActivationEventType
    {
        LetterReceived,
        BehaviorCheckIn,
        KingDiplomacy,
        PlanCheckIn,
        YearlyChronicle,
        SpecialChronicle,
        Advisory,
        FiefReview,
        KingConsult,
        ClanReplenishment
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
            ActivationEventType.KingDiplomacy or ActivationEventType.KingConsult => 1,
            ActivationEventType.LetterReceived => 2,
            ActivationEventType.BehaviorCheckIn or ActivationEventType.PlanCheckIn or ActivationEventType.FiefReview or ActivationEventType.ClanReplenishment => 3,
            _ => 4
        };
    }

    public static partial class AgentScheduler
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
        // 同一国王的自我审视（KingDiplomacy）去重：已排队或进行中的审视期间，新 KingDiplomacy 只并入内容、
        // 不重复入队——防止「每日随机激活 + 攻城册封激活」同时触发导致国王重复审视、重复行动
        // （重复转移封地、意外夺封）。不同国王仍可并行。
        private static readonly object _diplomacyLock = new();
        private static readonly HashSet<string> _diplomacyReviewAgents = new();
        private static readonly Dictionary<string, StringBuilder> _diplomacySupplements = new();
        private static int _warmupFrames = 120;
        private static int _nextKingIndex = 0;
        private static readonly Dictionary<string, CampaignTime> _lastProposalActivation = new();
        // 修复：玩家外交提案改为队列逐个弹出——单槽位在并发时第二个王国覆盖第一个，提案被吞
        private static readonly System.Collections.Concurrent.ConcurrentQueue<ActivationEvent> _pendingPlayerProposals = new();
        private static bool _playerProposalShowing;
        private static int _lastChronicleYear;
        private static int _lastExpiryCheckDay = -1;
        private static int _lastClanReplenishDay = -1;
        private static bool _historianInitialized;
        private static int _warmupFramesHistorian = 60;
        private static readonly Random _rng = new();
        private static readonly Dictionary<Kingdom, string> _lastAdvisorySpeaker = new();
        private static readonly Dictionary<Kingdom, CampaignTime> _lastAdvisoryCheck = new();
        private static readonly Dictionary<string, CampaignTime> _lastConsultByPair = new();
        private const double ConsultCooldownDays = 7.0;

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
            _lastExpiryCheckDay = -1;
            _lastClanReplenishDay = -1;
            _historianInitialized = false;
            _lastAdvisorySpeaker.Clear();
            _lastAdvisoryCheck.Clear();
            _lastConsultByPair.Clear();
            lock (_diplomacyLock)
            {
                _diplomacyReviewAgents.Clear();
                _diplomacySupplements.Clear();
            }
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
                "[AI编年史] 外交计时器已重置，所有国王将在接下来的游戏日中依次被激活。",
                Colors.Cyan));
        }

        public static void ForceAdvisory()
        {
            _lastAdvisoryCheck.Clear();
            _lastAdvisorySpeaker.Clear();
            MainThreadExecutor.DisplayMessage(new InformationMessage(
                "[AI编年史] 封臣谏言计时器已重置，封臣们将在接下来的游戏日中陆续进谏。",
                Colors.Cyan));
        }

        /// <summary>外交问询冷却（每王国对）：问询后 N 游戏天才能再次遣使，防刷。返回是否可问询，不可时给出剩余天数。</summary>
        public static bool TryConsult(string pairKey, out int daysRemaining)
        {
            daysRemaining = 0;
            if (_lastConsultByPair.TryGetValue(pairKey, out var last))
            {
                var elapsed = (CampaignTime.Now - last).ToDays;
                if (elapsed < ConsultCooldownDays)
                {
                    daysRemaining = (int)Math.Ceiling(ConsultCooldownDays - elapsed);
                    return false;
                }
            }
            return true;
        }

        public static void RecordConsult(string pairKey)
        {
            _lastConsultByPair[pairKey] = CampaignTime.Now;
        }

        /// <summary>国王政务审视时注入的外交问询回音：扫 consults/ 下含本国的对，取最后一条对方发来的问询/答复。</summary>
        private static string BuildConsultInjection(Kingdom kingdom)
        {
            if (string.IsNullOrEmpty(PromptManager.CampaignDir)) return "";
            var kingdomName = kingdom.Name?.ToString();
            if (string.IsNullOrEmpty(kingdomName)) return "";
            var consultsDir = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "diplomacy", "consults");
            if (!Directory.Exists(consultsDir)) return "";

            var notes = new List<string>();
            try
            {
                foreach (var file in Directory.GetFiles(consultsDir, "*.txt"))
                {
                    var fname = Path.GetFileNameWithoutExtension(file);
                    if (!fname.Contains(kingdomName)) continue; // 只关心含本国的对
                    var all = SafeFileIO.ReadAllLines(file);
                    if (all.Length == 0) continue;
                    var last = all[all.Length - 1].Trim();
                    if (last.Length == 0) continue;
                    // 最后一条是自己发出的（问询尚无回音），不注入；对方发来的才注入
                    if (last.Contains("（" + kingdomName + "国王）")) continue;
                    notes.Add(last);
                }
            }
            catch { }

            if (notes.Count == 0) return "";
            return "\n（提示：你与他国的外交问询记录——\n" + string.Join("\n", notes) + "\n可用 reply_consult 回复对方，或置之不理。）\n";
        }

        public static void QueueEvent(ActivationEvent evt)
        {
            if (evt.Type == ActivationEventType.KingDiplomacy)
            {
                lock (_diplomacyLock)
                {
                    if (!_diplomacyReviewAgents.Add(evt.AgentId))
                    {
                        // 同一国王已有审视排队或进行中——只并入本次内容（如新攻城归属指示），不重复激活
                        if (!string.IsNullOrEmpty(evt.Content))
                        {
                            if (!_diplomacySupplements.TryGetValue(evt.AgentId, out var sb))
                            {
                                sb = new StringBuilder();
                                _diplomacySupplements[evt.AgentId] = sb;
                            }
                            sb.AppendLine().Append(evt.Content);
                        }
                        return;
                    }
                }
            }

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

        /// <summary>每日一次（无 LLM、不激活 Agent）：把当天到期的盟约/贸易协定写入到期日志，供国王下次查世界局势时自行看到。</summary>
        private static void CheckExpiringAgreements()
        {
            if (Campaign.Current == null) return;
            var nowDays = (int)CampaignTime.Now.ToDays;
            if (nowDays == _lastExpiryCheckDay) return;
            _lastExpiryCheckDay = nowDays;
            DiplomacyService.CheckExpiringAgreements();
        }

        /// <summary>统计当前在世贵族/佣兵家族。返回 (总家族数, 正式封臣, 雇佣兵公司)。
        /// 封臣与佣兵在本模组是动态身份（随时可换国/改佣兵），且「当前受雇」随签约状态波动（和平期大量未受雇），
        /// 因此主口径是「家族总数」——只要不是真正灭族就不拉低计数，避免和平期/新档误判凋零而疯狂补族。
        /// 封臣/佣兵拆分只作为天意的决策参考，不参与触发判定。
        /// 存活判定用「家族成员有在世者」而非「族长非空」——族长死后接任需要时间（无继承人时原版甚至会暂留已故族长），
        /// 若按族长判空会把族长交接的短暂窗口误判为家族凋零，从而错误激活天意。</summary>
        private static (int Total, int Vassals, int Mercenaries) CountClans()
        {
            if (Campaign.Current == null) return (0, 0, 0);
            var total = 0;
            var vassals = 0;
            var mercenaries = 0;
            foreach (var clan in Clan.All)
            {
                if (clan == null || clan.IsEliminated || clan.IsRebelClan || clan.IsBanditFaction) continue;
                var hasLivingMember = false;
                if (clan.Heroes != null)
                {
                    foreach (var member in clan.Heroes)
                    {
                        if (member != null && member.IsAlive)
                        {
                            hasLivingMember = true;
                            break;
                        }
                    }
                }
                if (!hasLivingMember) continue;
                if (clan.IsUnderMercenaryService || clan.IsClanTypeMercenary)
                {
                    mercenaries++;
                    total++;
                }
                else if (clan.IsNoble)
                {
                    vassals++;
                    total++;
                }
            }
            return (total, vassals, mercenaries);
        }

        /// <summary>每日检测：在世贵族/佣兵家族总数低于下限时，激活「天意」补充新家族（家族补充系统，防大屠杀导致世家凋零）。
        /// 有冷却（MCM「家族补充冷却」可调），且已有待处理的补充事件时不重复排队。
        /// 只在玩家位于战役大地图时检测——捏脸/战役初始化阶段世界数据未稳定（家族族长未挂接、雇佣兵未签约），
        /// 计数失真会误判凋零而错误激活天意。</summary>
        private static void CheckClanReplenishment()
        {
            if (Campaign.Current == null) return;
            var settings = MySettings.Instance;
            if (settings?.ClanReplenishmentEnabled != true) return;
            if (!SubModule.IsPlayerFreeOnMap()) return;

            var nowDays = (int)CampaignTime.Now.ToDays;
            var cooldownDays = settings.ClanReplenishmentCooldownDays;
            if (_lastClanReplenishDay >= 0 && nowDays - _lastClanReplenishDay < cooldownDays) return;
            if (string.IsNullOrEmpty(PromptManager.CampaignDir)) return;

            var (total, vassals, mercenaries) = CountClans();
            var totalThreshold = settings.MinTotalClans;
            if (total >= totalThreshold)
                return;

            // 差距过大时连续激活：一次补一个，_lastClanReplenishDay 只在排队成功后推进
            _lastClanReplenishDay = nowDays;
            var need = Math.Max(0, totalThreshold - total);

            // 程序建议（仅参考，天意最终裁定）：某类明显凋敝则倾向补该类，否则由天意自行斟酌。
            // 阈值只是提示——佣兵公司持续凋零（<4）建议佣兵，封臣世家凋敝（<40）建议封臣。
            string recommendation;
            if (mercenaries < 4)
                recommendation = "封臣尚有余裕，而雇佣兵公司凋敝，建议以雇佣兵身份降下";
            else if (vassals < 40)
                recommendation = "雇佣兵尚可，而封臣世家凋敝，建议以正式封臣身份降下";
            else
                recommendation = "封臣与雇佣兵皆有余裕，可依天下大势自行斟酌";

            var content = $"当前在世贵族/佣兵家族共 {total} 个（下限 {totalThreshold}，缺 {need} 个）。其中正式封臣家族 {vassals}、雇佣兵公司 {mercenaries}。{recommendation}。请按规则创建一个新家族。";

            MainThreadExecutor.DisplayMessage(new InformationMessage(
                $"[AI编年史] 世家凋零：在世贵族/佣兵家族仅 {total}/{totalThreshold} 家，天意将降下新的血脉。",
                Colors.Cyan));

            QueueEvent(new ActivationEvent
            {
                Type = ActivationEventType.ClanReplenishment,
                AgentId = "__fate__",
                TargetId = "__fate__",
                Content = content,
                Depth = 0
            });
        }

        public static void Tick()
        {
            CheckExpiringAgreements();

            if (_inFlightCount >= MaxConcurrent) return;

            CheckDelayedEvents();

            if (!TryDequeueHighestPriority(out var evt))
            {
                CheckYearAdvance();
                if (PendingEventCount() <= 3)
                {
                    CheckKingActivations();
                    CheckAdvisoryActivations();
                    CheckClanReplenishment();
                }
                return;
            }

            var maxDepth = MySettings.Instance?.MaxLetterChainDepth ?? 5;
            if (evt.Depth > maxDepth)
            {
                MainThreadExecutor.DisplayMessage(new InformationMessage(
                    $"[AI编年史] 信件级联已达上限({maxDepth}+)，剩余信件已存档不再处理。",
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
                var advisoryNote = $"\n（提示：先用 read_file 阅读 advisory/{kingdom.Name}_{currentYear}.txt 了解封臣们的公开谏言，再用 read_file 阅读 secret_advisory/{kingdom.Name}_{currentYear}.txt 查看封臣们的秘密陈奏——后者仅你可见、不入史册。群臣意见是你的决策参考，但你的决定权至高无上。若需向国内宣示方针、回应群臣或垂询政务，可调用 submit_edict 颁布公开诏令——封臣进谏前会先读你的诏令，史官也会记载。）\n";
                var consultNote = BuildConsultInjection(kingdom);

                var activationMsg =
                    $"你是{kingdom.Name}的至高统治者。审视你的王国局势，凭自己的判断做出外交决断。\n\n"
                    + $"步骤1：调用 query_pending_proposals 查看是否有待处理的提案，有则用 respond_to_diplomacy_proposal 逐一处理\n"
                    + $"步骤2：调用 query_war_status 了解当前所有战争的战况\n"
                    + proposalLines
                    + advisoryNote
                    + consultNote
                    + $"\n（提示：你的长期战略方针、承诺、计策都记在 decisions/diary.txt（条目类型含「战略」）。每次审视先 read_file 读取日记回顾你的战略走向与既往承诺，据此做连贯决策；若 chat_logs/ 有比日记更新的往来，以它们为准，并把新决定/新战略补记进日记。）\n\n"
                    + $"然后依据你自己的判断采取行动——以下是你可用的外交工具：\n"
                    + "- propose_peace：结束一场战争（可附带赔款条件）\n"
                    + "- propose_alliance：与中立王国结盟\n"
                    + "- propose_trade：与中立王国签订贸易协定\n"
                    + "- declare_war：宣战\n"
                    + "- submit_edict：颁布公开诏令，向国内宣示方针、回应群臣或垂询政务（可选）\n\n"
                    + "不要幻想或虚构数据。不要用 send_letter 处理外交事务——外交方案只用上述 function 发起；需要向国内传达旨意时，用 submit_edict 颁布公开诏令。";

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

                var chronicleFile = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "history", "chronicles", $"{y}编年史.txt");
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

        /// <summary>取走并清空合并缓冲，构建史官提示词（传记 vs 专题史按首条判断），并按事件性质注入体例建议。</summary>
        private static string ConsumeSpecialChronicleContent()
        {
            lock (_specialChronicleLock)
            {
                if (_pendingSpecialSummaries.Count == 0) return "";
                var summaries = new List<string>(_pendingSpecialSummaries);
                _pendingSpecialSummaries.Clear();
                _specialQueued = false;

                var first = summaries[0];
                var joined = string.Join("\n", summaries);

                if (first.StartsWith("重要人物之死"))
                {
                    var prompt = PromptManager.LoadBiographyPrompt().Replace("{event_summary}", joined);
                    return prompt + "\n\n体例建议：" + SuggestBiographyGenre(first);
                }

                if (first.StartsWith("王国灭亡"))
                {
                    var prompt = PromptManager.LoadSpecialChroniclePrompt().Replace("{event_summary}", joined);
                    return prompt + "\n\n体例建议：此为王国灭亡，应以「世家」体例为该王国作兴衰史（如《南帝国世家》）。write_chronicle(体例=世家, 名称=该王国名)。";
                }

                var sprompt = PromptManager.LoadSpecialChroniclePrompt().Replace("{event_summary}", joined);
                return sprompt + "\n\n体例建议：此系一般重大事件，应以「纪事」体例叙述（如《帝国分裂纪事》）。write_chronicle(体例=纪事, 名称=事件名)。";
            }
        }

        /// <summary>按死者身份建议立传体例：一国之君 → 本纪；其余（族长/成员/冒险者）→ 列传。史官可依实调整。</summary>
        private static string SuggestBiographyGenre(string summary)
        {
            if (summary.Contains("统治者"))
                return "该人物为一国之君，应以「本纪」体例立传（如《某某本纪》）。write_chronicle(体例=本纪, 名称=人物名)。";
            return "该人物为贵族或冒险者，应以「列传」体例立传（如《某某列传》）。write_chronicle(体例=列传, 名称=人物名)。";
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
    }
}
