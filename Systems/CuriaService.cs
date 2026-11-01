using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace AIChronicle
{
    /// <summary>
    /// 朝议核心：提议、应召、投票、陈情、结算、执行。
    /// 全部在主线程调用；持久化在 World/curia/{王国}/。
    /// </summary>
    public static partial class CuriaService
    {
        private static readonly Dictionary<string, CuriaState> _states = new();
        private static readonly Dictionary<string, CuriaAgenda> _activeAgendas = new();
        private static bool _settingsWarned;
        private static int _lastTickDay = -1;

        public static bool Enabled => MySettings.Instance?.CuriaEnabled == true;

        public static void ResetForNewCampaign()
        {
            _states.Clear();
            _activeAgendas.Clear();
            _settingsWarned = false;
            _lastTickDay = -1;
            // 读档时顺手清理旧档积压；正常情况下每次议程结束也会按 CuriaHistoryKeep 清理。
            try
            {
                var keep = MySettings.Instance?.CuriaHistoryKeep ?? 200;
                foreach (var kingdom in Kingdom.All)
                    CuriaStore.PruneAgendas(kingdom.Name?.ToString() ?? "", keep);
            }
            catch { }
        }

        public static CuriaState GetState(Kingdom kingdom)
        {
            if (kingdom == null) return new CuriaState();
            var key = kingdom.StringId ?? kingdom.Name?.ToString() ?? "";
            if (_states.TryGetValue(key, out var state))
            {
                // 战役内 Kingdom 对象稳定；名字/ID 变化时同步
                state.KingdomName = kingdom.Name?.ToString() ?? state.KingdomName;
                return state;
            }
            state = CuriaStore.LoadState(kingdom);
            _states[key] = state;
            return state;
        }

        public static CuriaAgenda? GetActiveAgenda(Kingdom kingdom)
        {
            if (kingdom == null) return null;
            var key = kingdom.StringId ?? kingdom.Name?.ToString() ?? "";
            var state = GetState(kingdom);
            if (string.IsNullOrEmpty(state.ActiveAgendaId))
            {
                _activeAgendas.Remove(key);
                return null;
            }
            if (_activeAgendas.TryGetValue(key, out var cached) && cached.Id == state.ActiveAgendaId
                && !IsTerminal(cached.Status))
                return cached;

            var agenda = CuriaStore.LoadAgenda(state.KingdomName, state.ActiveAgendaId);
            if (agenda == null || IsTerminal(agenda.Status))
            {
                state.ActiveAgendaId = "";
                CuriaStore.SaveState(state);
                _activeAgendas.Remove(key);
                return null;
            }
            _activeAgendas[key] = agenda;
            return agenda;
        }

        public static bool HasActiveAgenda(Kingdom kingdom) => GetActiveAgenda(kingdom) != null;

        public static bool IsTerminal(CuriaAgendaStatus status)
            => status == CuriaAgendaStatus.Rejected || status == CuriaAgendaStatus.Applied
               || status == CuriaAgendaStatus.Failed || status == CuriaAgendaStatus.Cancelled;

        public static void Tick()
        {
            if (!Enabled || Campaign.Current == null) return;
            if (string.IsNullOrEmpty(PromptManager.CampaignDir))
            {
                if (!_settingsWarned)
                {
                    _settingsWarned = true;
                    DebugLogger.Log("朝议：战役目录尚未就绪，跳过 Tick");
                }
                return;
            }

            // 每游戏日最多扫一次：朝议的推进由提议/陈情/投票事件即时触发，
            // Tick 只负责"防御截止/投票截止"这类日期边界，逐帧读盘没有意义。
            var today = CuriaStore.Today;
            if (today == _lastTickDay) return;
            _lastTickDay = today;

            foreach (var kingdom in Kingdom.All)
            {
                if (kingdom == null || kingdom.IsEliminated) continue;
                try
                {
                    var agenda = GetActiveAgenda(kingdom);
                    if (agenda == null) continue;

                    if (agenda.Status == CuriaAgendaStatus.Proposed)
                    {
                        if (agenda.NeedsTargetDefense && !agenda.TargetDefended
                            && CuriaStore.Today < agenda.DefenseDeadlineDay)
                            continue;

                        if (agenda.NeedsTargetDefense && !agenda.TargetDefended)
                        {
                            agenda.TargetDefense = "（被针对家族未及陈情）";
                            agenda.TargetDefended = true;
                        }
                        StartVoting(kingdom, agenda);
                    }
                    else if (agenda.Status == CuriaAgendaStatus.Voting)
                    {
                        if (agenda.AllSummonedVoted || CuriaStore.Today >= agenda.DeadlineDay)
                            Resolve(kingdom, agenda);
                    }
                    else if (agenda.Status == CuriaAgendaStatus.Passed)
                    {
                        // 崩溃/切档后恢复：已通过但尚未执行
                        ApplyEffectAndFinish(kingdom, agenda);
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"朝议 Tick 异常（{kingdom.Name}）：{ex}");
                    try
                    {
                        var state = GetState(kingdom);
                        state.ActiveAgendaId = "";
                        CuriaStore.SaveState(state);
                    }
                    catch { }
                }
            }
        }

        // ================= 提议 =================

        public static bool Propose(Hero proposer, string typeText, string targetKingdom, string settlementName,
            string newOwnerEntityId, string targetClanName, string targetPolicy, string reason, out string message)
        {
            message = "";
            if (!Enabled)
            {
                message = "[错误] 朝议未启用";
                return false;
            }
            if (proposer == null)
            {
                message = "[错误] 无当前领主";
                return false;
            }
            if (string.IsNullOrWhiteSpace(reason))
            {
                message = "[错误] 议题理由不能为空";
                return false;
            }
            if (!CuriaText.TryParseType(typeText, out var type))
            {
                message = "[错误] 未知议题类型。可选：declare_war / propose_peace / propose_trade / propose_alliance / break_alliance / enact_policy / revoke_policy / transfer_fief / expel_clan";
                return false;
            }
            if (proposer.Clan == null || proposer.Clan.Leader != proposer)
            {
                message = "[错误] 只有家族领袖才能提出朝议";
                return false;
            }
            var kingdom = CuriaStore.GetHeroKingdom(proposer);
            if (kingdom == null)
            {
                message = "[错误] 你不属于任何王国，无法提出朝议";
                return false;
            }
            if (CuriaStore.IsMercenary(proposer.Clan))
            {
                message = "[错误] 雇佣兵无权参与朝议";
                return false;
            }
            if (proposer.IsPrisoner || proposer.IsFugitive)
            {
                message = "[错误] 你当前无法出席朝议";
                return false;
            }
            if (proposer.Clan.Kingdom != kingdom)
            {
                message = "[错误] 你不在该王国";
                return false;
            }
            if (kingdom.IsEliminated)
            {
                message = "[错误] 王国已灭亡";
                return false;
            }

            var state = GetState(kingdom);
            var active = GetActiveAgenda(kingdom);
            if (active != null)
            {
                message = $"[错误] 本国已有进行中的朝议「{CuriaText.TypeLabel(active.Type)}」，请待其结束";
                return false;
            }

            var proposerEntityId = CuriaStore.EntityIdOf(proposer);
            var cooldown = MySettings.Instance?.CuriaProposalCooldownDays ?? 3;
            if (state.ProposerCooldownDay.TryGetValue(proposerEntityId, out var lastDay)
                && CuriaStore.Today - lastDay < cooldown)
            {
                var remaining = cooldown - (CuriaStore.Today - lastDay);
                message = $"[错误] 你刚提过朝议，需再等 {remaining} 游戏天";
                return false;
            }

            var king = CuriaStore.GetKing(kingdom);
            if (king == null)
            {
                message = "[错误] 王国没有在世统治者，无法召开朝议";
                return false;
            }
            var agenda = new CuriaAgenda
            {
                Id = DateTime.UtcNow.Ticks.ToString("X") + "_" + Math.Abs(proposerEntityId.GetHashCode()).ToString("X4"),
                KingdomId = kingdom.StringId ?? "",
                KingdomName = kingdom.Name?.ToString() ?? "",
                Type = type,
                Status = CuriaAgendaStatus.Proposed,
                ProposerEntityId = proposerEntityId,
                ProposerClanId = proposer.Clan.StringId ?? "",
                ProposerName = proposer.Name?.ToString() ?? "",
                Reason = reason.Trim(),
                CreatedLabel = CuriaStore.CurrentLabel,
                CreatedDay = CuriaStore.Today,
                DeadlineDay = CuriaStore.Today + (MySettings.Instance?.CuriaVoteDays ?? 5),
                DefenseDeadlineDay = CuriaStore.Today + 1,
                Threshold = GetThreshold(kingdom, type)
            };

            switch (type)
            {
                case CuriaAgendaType.DeclareWar:
                case CuriaAgendaType.ProposePeace:
                case CuriaAgendaType.ProposeTrade:
                case CuriaAgendaType.ProposeAlliance:
                case CuriaAgendaType.BreakAlliance:
                    {
                        var target = CuriaStore.FindKingdom(targetKingdom);
                        if (target == null)
                        {
                            message = "[错误] 找不到目标王国：" + targetKingdom;
                            return false;
                        }
                        if (target == kingdom)
                        {
                            message = "[错误] 不能以自己为目标";
                            return false;
                        }
                        if (target.IsEliminated)
                        {
                            message = "[错误] 目标王国已灭亡";
                            return false;
                        }
                        var targetRuler = CuriaStore.GetKing(target);
                        if (targetRuler == null)
                        {
                            message = "[错误] 目标王国没有在世统治者";
                            return false;
                        }

                        if (type == CuriaAgendaType.DeclareWar && kingdom.IsAtWarWith(target))
                        {
                            message = $"[错误] 你已与{target.Name}处于战争状态";
                            return false;
                        }
                        if (type == CuriaAgendaType.ProposePeace && !kingdom.IsAtWarWith(target))
                        {
                            message = $"[错误] 你并未与{target.Name}交战，无需议和";
                            return false;
                        }
                        if (type == CuriaAgendaType.ProposeTrade)
                        {
                            if (kingdom.IsAtWarWith(target))
                            {
                                message = $"[错误] 无法与交战国{target.Name}签订贸易协定";
                                return false;
                            }
                            var trade = Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
                            if (trade != null && trade.HasTradeAgreement(kingdom, target, out _))
                            {
                                message = $"[错误] 你已与{target.Name}签有贸易协定";
                                return false;
                            }
                        }
                        if (type == CuriaAgendaType.ProposeAlliance || type == CuriaAgendaType.BreakAlliance)
                        {
                            if (type == CuriaAgendaType.ProposeAlliance && kingdom.IsAtWarWith(target))
                            {
                                message = $"[错误] 无法与交战国{target.Name}结盟";
                                return false;
                            }
                            var alliance = Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
                            if (alliance == null)
                            {
                                message = "[错误] 当前没有可用的同盟系统";
                                return false;
                            }
                            var isAlly = alliance.IsAllyWithKingdom(kingdom, target);
                            if (type == CuriaAgendaType.ProposeAlliance && isAlly)
                            {
                                message = $"[错误] 你已与{target.Name}结盟";
                                return false;
                            }
                            if (type == CuriaAgendaType.BreakAlliance && !isAlly)
                            {
                                message = $"[错误] 你与{target.Name}没有盟约";
                                return false;
                            }
                        }

                        agenda.Kind = CuriaAgendaKind.External;
                        agenda.TargetKingdomId = target.StringId ?? "";
                        agenda.TargetKingdomName = target.Name?.ToString() ?? "";
                        agenda.Terms = type switch
                        {
                            CuriaAgendaType.DeclareWar => $"王国对{target.Name}宣战",
                            CuriaAgendaType.ProposePeace => $"授权国王向{target.Name}提出议和",
                            CuriaAgendaType.ProposeAlliance => $"授权国王向{target.Name}提出结盟",
                            CuriaAgendaType.BreakAlliance => $"解除{kingdom.Name}与{target.Name}的盟约",
                            _ => $"授权国王向{target.Name}提出贸易协定"
                        };
                        break;
                    }

                case CuriaAgendaType.EnactPolicy:
                case CuriaAgendaType.RevokePolicy:
                    {
                        var policy = FindPolicy(targetPolicy);
                        if (policy == null)
                        {
                            message = "[错误] 找不到政策：" + targetPolicy;
                            return false;
                        }
                        var permission = Campaign.Current?.Models?.KingdomDecisionPermissionModel;
                        if (permission != null && !permission.IsPolicyDecisionAllowed(policy))
                        {
                            message = "[错误] 当前不允许提出或废除该政策";
                            return false;
                        }
                        var isActive = kingdom.HasPolicy(policy);
                        if (type == CuriaAgendaType.EnactPolicy && isActive)
                        {
                            message = $"[错误] 政策「{policy.Name}」已经实施";
                            return false;
                        }
                        if (type == CuriaAgendaType.RevokePolicy && !isActive)
                        {
                            message = $"[错误] 政策「{policy.Name}」尚未实施";
                            return false;
                        }
                        agenda.Kind = CuriaAgendaKind.Policy;
                        agenda.TargetPolicyId = policy.StringId ?? "";
                        agenda.TargetPolicyName = policy.Name?.ToString() ?? "";
                        agenda.PolicyEffect = policy.SecondaryEffects?.ToString() ?? "";
                        agenda.PolicyDescription = policy.Description?.ToString() ?? "";
                        agenda.Terms = type == CuriaAgendaType.EnactPolicy
                            ? $"在{kingdom.Name}实施政策「{agenda.TargetPolicyName}」"
                            : $"废除{kingdom.Name}的政策「{agenda.TargetPolicyName}」";
                        break;
                    }

                case CuriaAgendaType.TransferFief:
                    {
                        var settlement = CuriaStore.FindSettlement(settlementName);
                        if (settlement == null || (!settlement.IsTown && !settlement.IsCastle))
                        {
                            message = "[错误] 未找到城镇/城堡：" + settlementName;
                            return false;
                        }
                        if (settlement.OwnerClan == null)
                        {
                            message = "[错误] 该定居点没有归属家族";
                            return false;
                        }
                        var ownerClan = settlement.OwnerClan;
                        if (!CuriaStore.IsVassalClan(ownerClan, kingdom))
                        {
                            message = "[错误] 该封地不属于本王国的封臣家族";
                            return false;
                        }
                        if (ownerClan == kingdom.RulingClan)
                        {
                            message = "[错误] 统治者家族的封地可自由处置，不需朝议";
                            return false;
                        }

                        var newOwnerHero = CuriaStore.FindHeroByEntityId(newOwnerEntityId);
                        if (newOwnerHero == null)
                        {
                            var nc = CuriaStore.FindClan(newOwnerEntityId);
                            newOwnerHero = nc?.Leader;
                        }
                        if (newOwnerHero?.Clan == null || newOwnerHero.Clan.Leader != newOwnerHero)
                        {
                            message = "[错误] 新主必须是家族领袖";
                            return false;
                        }
                        if (!CuriaStore.IsVassalClan(newOwnerHero.Clan, kingdom))
                        {
                            message = "[错误] 新主必须是本王国的封臣家族领袖";
                            return false;
                        }
                        if (newOwnerHero.Clan == ownerClan)
                        {
                            message = "[错误] 该封地已属于该家族";
                            return false;
                        }

                        var isKing = king != null && proposer == king;
                        var isOwner = ownerClan.Leader == proposer;
                        if (!isKing && !isOwner)
                        {
                            message = "[错误] 只有国王或该封地所属家族领袖才能提出封地转让";
                            return false;
                        }

                        agenda.Kind = isOwner && !isKing ? CuriaAgendaKind.VoluntaryTransfer : CuriaAgendaKind.ImposedTransfer;
                        agenda.TargetSettlementId = settlement.StringId ?? "";
                        agenda.TargetSettlementName = settlement.Name?.ToString() ?? "";
                        agenda.TargetClanId = ownerClan.StringId ?? "";
                        agenda.TargetClanName = ownerClan.Name?.ToString() ?? "";
                        agenda.NewOwnerClanId = newOwnerHero.Clan.StringId ?? "";
                        agenda.NewOwnerClanName = newOwnerHero.Clan.Name?.ToString() ?? "";
                        agenda.NewOwnerEntityId = CuriaStore.EntityIdOf(newOwnerHero);
                        agenda.Terms = $"将{settlement.Name}从{ownerClan.Name}转予{newOwnerHero.Clan.Name}";
                        break;
                    }

                case CuriaAgendaType.ExpelClan:
                    {
                        if (king == null || proposer != king)
                        {
                            message = "[错误] 只有国王才能提出驱逐家族";
                            return false;
                        }
                        var targetClan = CuriaStore.FindClan(targetClanName);
                        if (targetClan == null)
                        {
                            message = "[错误] 找不到目标家族：" + targetClanName;
                            return false;
                        }
                        if (!CuriaStore.IsVassalClan(targetClan, kingdom))
                        {
                            message = "[错误] 目标不是本王国的封臣家族";
                            return false;
                        }
                        if (targetClan == kingdom.RulingClan)
                        {
                            message = "[错误] 不能驱逐统治家族";
                            return false;
                        }
                        agenda.Kind = CuriaAgendaKind.Expulsion;
                        agenda.TargetClanId = targetClan.StringId ?? "";
                        agenda.TargetClanName = targetClan.Name?.ToString() ?? "";
                        agenda.Terms = $"将{targetClan.Name}逐出{kingdom.Name}";
                        break;
                    }
            }

            // 提议者自动支持（若在选民池内；被针对的强制夺封/驱逐提议者是国王，不会被排除）
            var voters = BuildVoters(kingdom, agenda);
            var proposerVote = voters.FirstOrDefault(v => v.IsProposer) ?? voters.FirstOrDefault(v => v.ClanId == agenda.ProposerClanId);
            if (proposerVote == null)
            {
                var clan = proposer.Clan;
                var details = $"kingdom={kingdom.StringId}, clan={clan?.StringId}, " +
                              $"merc={CuriaStore.IsMercenary(clan)}, minor={clan?.IsMinorFaction}, bandit={clan?.IsBanditFaction}, " +
                              $"clanKingdom={clan?.Kingdom?.StringId}, kingdomClans={kingdom.Clans.Count}";
                DebugLogger.Log("朝议：提议者不在选民池 " + details);
                message = "[错误] 你的家族不在本次朝议的选民池内（" + details + "）。" +
                          "请检查是否已被标记为雇佣兵/小派系，或读档后重新尝试。";
                return false;
            }
            proposerVote.Stance = CuriaStance.Support;
            proposerVote.Reason = agenda.Reason;
            proposerVote.VotedDay = CuriaStore.Today;

            state.ActiveAgendaId = agenda.Id;
            _activeAgendas[kingdom.StringId ?? kingdom.Name?.ToString() ?? ""] = agenda;
            state.ProposerCooldownDay[proposerEntityId] = CuriaStore.Today;
            CuriaStore.SaveState(state);
            CuriaStore.SaveAgenda(agenda, writeText: false);
            CuriaStore.RecordHistory("curia_proposed",
                $"{agenda.ProposerName}在{kingdom.Name}提出朝议：{CuriaText.TypeLabel(agenda.Type)} {CuriaText.TargetSummary(agenda)}。理由：{agenda.Reason}");

            if (agenda.NeedsTargetDefense)
            {
                QueueDefense(kingdom, agenda);
                if (ShouldNotifyPlayer(kingdom))
                    CuriaStore.Notify($"朝议开始：{agenda.ProposerName}提议{CuriaText.TypeLabel(agenda.Type)}（{CuriaText.TargetSummary(agenda)}），被针对家族正在陈情。按 M 查看。", Colors.Cyan);
                if (Hero.MainHero != null && proposer == Hero.MainHero)
                    CuriaStore.Notify("你的议题已提交，等待被针对家族陈情与其他家族表决。", Colors.Green);
            }
            else
            {
                StartVoting(kingdom, agenda);
            }

            message = $"朝议已提出：{CuriaText.TypeLabel(agenda.Type)}（{CuriaText.TargetSummary(agenda)}）。等待表决。";
            return true;
        }

        private static PolicyObject? FindPolicy(string policyText)
        {
            if (string.IsNullOrWhiteSpace(policyText)) return null;
            var key = policyText.Trim();
            if (Campaign.Current == null) return null;
            var all = PolicyObject.All;
            if (all == null) return null;
            return all.FirstOrDefault(p => string.Equals(p.StringId, key, StringComparison.OrdinalIgnoreCase))
                ?? all.FirstOrDefault(p => string.Equals(p.Name?.ToString(), key, StringComparison.OrdinalIgnoreCase));
        }

        private static float GetThreshold(Kingdom? kingdom, CuriaAgendaType type)
        {
            var s = MySettings.Instance;
            float value;
            if (type == CuriaAgendaType.EnactPolicy || type == CuriaAgendaType.RevokePolicy)
                value = s?.CuriaPolicyThreshold ?? 0.5f;
            else if (type == CuriaAgendaType.TransferFief || type == CuriaAgendaType.ExpelClan
                     || type == CuriaAgendaType.BreakAlliance)
                value = s?.CuriaFiefThreshold ?? 0.66f;
            else
                value = s?.CuriaWarThreshold ?? 0.5f;
            return CuriaPolicyEffects.AdjustThreshold(kingdom, type, value);
        }

        private static List<CuriaVote> BuildVoters(Kingdom kingdom, CuriaAgenda agenda)
        {
            var voters = new List<CuriaVote>();
            if (kingdom == null) return voters;
            var king = CuriaStore.GetKing(kingdom);
            var player = Hero.MainHero;
            var excludedClanId = agenda.NeedsTargetDefense ? agenda.TargetClanId : "";

            bool Eligible(Clan clan)
            {
                if (clan == null || clan.IsEliminated) return false;
                if (CuriaStore.IsMercenary(clan)) return false;
                if (clan.IsMinorFaction || clan.IsBanditFaction) return false;
                var leader = clan.Leader;
                if (leader == null || !leader.IsAlive) return false;
                // 玩家即使被俘/逃亡也保留投票权（模组既有“被俘国王仍可理政”的口径）；AI 无法出席。
                if ((leader.IsPrisoner || leader.IsFugitive) && leader != Hero.MainHero) return false;
                if (!string.IsNullOrEmpty(excludedClanId) && clan.StringId == excludedClanId) return false;
                return true;
            }

            void AddVote(Clan clan)
            {
                var leader = clan.Leader!;
                var entityId = CuriaStore.EntityIdOf(leader);
                var weight = CuriaStore.WeightOf(clan);
                if (leader == king) weight *= CuriaPolicyEffects.KingVoteWeightMultiplier(kingdom);
                voters.Add(new CuriaVote
                {
                    ClanId = clan.StringId ?? "",
                    ClanName = clan.Name?.ToString() ?? "",
                    LeaderEntityId = entityId,
                    LeaderName = leader.Name?.ToString() ?? "",
                    Weight = weight,
                    IsKing = leader == king,
                    IsPlayer = leader == player,
                    IsProposer = entityId == agenda.ProposerEntityId
                });
            }

            foreach (var clan in kingdom.Clans)
            {
                if (Eligible(clan)) AddVote(clan);
            }

            // 容错：少数存档/Mod 状态下 Kingdom.Clans 可能漏掉玩家氏族，或其 IsMinorFaction 标记异常；
            // 只要它确实以该王国封臣身份存在，并且是提议者，就补进选民池，避免“我是封臣却不在选民池”。
            if (!voters.Any(v => v.IsProposer))
            {
                var proposerClan = Clan.All.FirstOrDefault(c => c.StringId == agenda.ProposerClanId);
                if (proposerClan != null)
                {
                    // 提议者容错：只要它确实以该王国封臣身份存在、不是雇佣兵、领袖可出席，
                    // 即使被错误标记为 minor/bandit 也让它进入自己的议程——否则玩家会“我是封臣却不在选民池”。
                    var leader = proposerClan.Leader;
                    var proposerEligible = !proposerClan.IsEliminated
                        && !CuriaStore.IsMercenary(proposerClan)
                        && proposerClan.Kingdom == kingdom
                        && leader != null && leader.IsAlive
                        && (!(leader.IsPrisoner || leader.IsFugitive) || leader == Hero.MainHero)
                        && (string.IsNullOrEmpty(excludedClanId) || proposerClan.StringId != excludedClanId);
                    if (Eligible(proposerClan) || proposerEligible)
                    {
                        AddVote(proposerClan);
                        DebugLogger.Log($"朝议：提议者氏族 {proposerClan.StringId} 不在 Kingdom.Clans，已按封臣身份补入选民池 " +
                                        $"minor={proposerClan.IsMinorFaction} bandit={proposerClan.IsBanditFaction}");
                    }
                    else
                    {
                        DebugLogger.Log($"朝议：提议者氏族不可投票 clan={proposerClan.StringId} " +
                                        $"kingdom={proposerClan.Kingdom?.StringId} merc={CuriaStore.IsMercenary(proposerClan)} " +
                                        $"minor={proposerClan.IsMinorFaction} bandit={proposerClan.IsBanditFaction} " +
                                        $"leader={proposerClan.Leader?.StringId}");
                    }
                }
            }

            return voters;
        }

        // ================= 投票 =================

        private static void StartVoting(Kingdom kingdom, CuriaAgenda agenda)
        {
            if (kingdom == null || agenda == null) return;
            var state = GetState(kingdom);
            var voters = BuildVoters(kingdom, agenda);
            if (voters.Count == 0)
            {
                FailAgenda(kingdom, agenda, "没有合格的朝议选民");
                return;
            }

            var totalWeight = voters.Sum(v => v.Weight);
            var quorum = totalWeight * (MySettings.Instance?.CuriaQuorum ?? 0.5f);
            var fullThreshold = MySettings.Instance?.CuriaFullCouncilThreshold ?? 6;
            var cap = MySettings.Instance?.CuriaAttendanceCap ?? 6;
            var forceFull = MySettings.Instance?.CuriaForceFullCouncil == true;

            List<CuriaVote> selected;
            if (forceFull || voters.Count <= fullThreshold)
            {
                selected = voters;
                foreach (var v in selected) v.SummonReason = "全量";
            }
            else
            {
                selected = SelectAttendance(voters, agenda, quorum, cap);
            }

            var absent = voters.Where(v => !selected.Contains(v)).ToList();
            foreach (var v in absent) v.SummonReason = "未获召见";

            agenda.Votes = selected;
            agenda.Absent = absent.Select(v => new CuriaAbsentVoter
            {
                ClanId = v.ClanId,
                ClanName = v.ClanName,
                LeaderName = v.LeaderName,
                Reason = "未获召见"
            }).ToList();
            agenda.SummonedClanIds = selected.Select(v => v.ClanId).ToList();
            agenda.TotalWeight = totalWeight;
            agenda.QuorumWeight = quorum;
            agenda.SummonedWeight = selected.Sum(v => v.Weight);
            agenda.Threshold = GetThreshold(kingdom, agenda.Type);
            agenda.Status = CuriaAgendaStatus.Voting;
            agenda.VotingStartedDay = CuriaStore.Today;
            agenda.DeadlineDay = CuriaStore.Today + (MySettings.Instance?.CuriaVoteDays ?? 5);
            agenda.TargetDefended = true;

            // 提议者的支持票（BuildVoters 已含提议者；除非被强制排除——不会）
            var proposerVote = selected.FirstOrDefault(v => v.IsProposer) ?? selected.FirstOrDefault(v => v.ClanId == agenda.ProposerClanId);
            if (proposerVote != null && !proposerVote.Stance.HasValue)
            {
                proposerVote.Stance = CuriaStance.Support;
                proposerVote.Reason = agenda.Reason;
                proposerVote.VotedDay = CuriaStore.Today;
                proposerVote.IsAutoSupport = true;
                if (!proposerVote.IsPlayer)
                    CuriaStore.AppendDiary(proposerVote.LeaderEntityId, "决定",
                        $"朝议「{CuriaText.TypeLabel(agenda.Type)}：{CuriaText.TargetSummary(agenda)}」由我提出并支持，因为{agenda.Reason}。");
            }

            RecomputeWeights(agenda);
            foreach (var v in selected)
            {
                state.LastSummonedDay[v.ClanId] = CuriaStore.Today;
                if (v.IsPlayer) continue;
                if (v.IsProposer && v.Stance.HasValue) continue;
                QueueVote(kingdom, agenda, v);
            }
            CuriaStore.SaveState(state);
            CuriaStore.SaveAgenda(agenda, writeText: true);
            if (ShouldNotifyPlayer(kingdom))
                CuriaStore.Notify($"朝议开始：{agenda.ProposerName}提议{CuriaText.TypeLabel(agenda.Type)}（{CuriaText.TargetSummary(agenda)}），{agenda.DeadlineDay - CuriaStore.Today} 天内表决。按 M 查看。", Colors.Cyan);

            if (agenda.AllSummonedVoted) Resolve(kingdom, agenda);
        }


        private static void QueueVote(Kingdom kingdom, CuriaAgenda agenda, CuriaVote voter)
        {
            if (string.IsNullOrEmpty(voter.LeaderEntityId)) return;
            var content = CuriaPageBuilder.BuildVotePrompt(agenda, voter);
            AgentScheduler.QueueEvent(new ActivationEvent
            {
                Type = ActivationEventType.CuriaVote,
                AgentId = voter.LeaderEntityId,
                TargetId = voter.LeaderEntityId,
                Content = content,
                Depth = 0
            });
        }

        private static void QueueDefense(Kingdom kingdom, CuriaAgenda agenda)
        {
            var targetClan = Clan.All.FirstOrDefault(c => c.StringId == agenda.TargetClanId);
            var leader = targetClan?.Leader;
            if (leader == null) return;
            if (leader == Hero.MainHero)
            {
                CuriaStore.Notify($"你被提议{CuriaText.TypeLabel(agenda.Type)}（{agenda.Terms}）。按 M 查看并陈情。", Colors.Yellow);
                return;
            }
            var entityId = CuriaStore.EntityIdOf(leader);
            if (string.IsNullOrEmpty(entityId)) return;
            AgentScheduler.QueueEvent(new ActivationEvent
            {
                Type = ActivationEventType.CuriaDefense,
                AgentId = entityId,
                TargetId = entityId,
                Content = CuriaPageBuilder.BuildDefensePrompt(agenda),
                Depth = 0
            });
        }

        public static bool SubmitVote(Hero voter, string agendaId, CuriaStance stance, string? reason, out string message)
        {
            message = "";
            if (voter == null)
            {
                message = "[错误] 无当前领主";
                return false;
            }
            var kingdom = CuriaStore.GetHeroKingdom(voter);
            if (kingdom == null)
            {
                message = "[错误] 你不属于任何王国";
                return false;
            }
            var agenda = GetActiveAgenda(kingdom);
            if (agenda == null)
            {
                message = "[错误] 当前没有进行中的朝议";
                return false;
            }
            if (!string.IsNullOrEmpty(agendaId) && agenda.Id != agendaId)
            {
                message = "[错误] 该议程不是当前活跃议程";
                return false;
            }
            if (agenda.Status != CuriaAgendaStatus.Voting)
            {
                message = "[错误] 当前议程尚未进入表决或已结束";
                return false;
            }
            if (CuriaStore.Today >= agenda.DeadlineDay)
            {
                message = "[错误] 本次朝议已截止";
                return false;
            }
            if (voter.Clan == null || voter.Clan.Leader != voter)
            {
                message = "[错误] 只有家族领袖才能投票";
                return false;
            }
            var vote = agenda.Votes.FirstOrDefault(v => v.ClanId == voter.Clan.StringId);
            if (vote == null)
            {
                message = "[错误] 你不是本次朝议的应召投票者";
                return false;
            }
            if (vote.Stance.HasValue)
            {
                message = vote.IsAutoSupport
                    ? "[错误] 你是提议者，默认支持已计入，不能更改"
                    : "[错误] 你已投过票，不能更改";
                return false;
            }

            vote.Stance = stance;
            vote.Reason = string.IsNullOrWhiteSpace(reason) ? "" : reason!.Trim();
            vote.VotedDay = CuriaStore.Today;
            vote.IsAutoSupport = false;
            RecomputeWeights(agenda);

            if (voter != Hero.MainHero)
            {
                var stanceLabel = CuriaText.StanceLabel(stance);
                var reasonPart = string.IsNullOrWhiteSpace(vote.Reason) ? "" : $"，因为{vote.Reason}";
                CuriaStore.AppendDiary(vote.LeaderEntityId, "决定",
                    $"朝议「{CuriaText.TypeLabel(agenda.Type)}：{CuriaText.TargetSummary(agenda)}」我投了{stanceLabel}{reasonPart}。");
            }

            CuriaStore.SaveAgenda(agenda, writeText: true);
            message = $"已记录你的立场：{CuriaText.StanceLabel(stance)}。";
            if (agenda.AllSummonedVoted) Resolve(kingdom, agenda);
            return true;
        }

        public static bool SubmitDefense(Hero target, string agendaId, string? reason, out string message)
        {
            message = "";
            if (target?.Clan == null)
            {
                message = "[错误] 无当前家族";
                return false;
            }
            var kingdom = CuriaStore.GetHeroKingdom(target);
            if (kingdom == null)
            {
                message = "[错误] 你不属于任何王国";
                return false;
            }
            var agenda = GetActiveAgenda(kingdom);
            if (agenda == null)
            {
                message = "[错误] 当前没有进行中的朝议";
                return false;
            }
            if (!string.IsNullOrEmpty(agendaId) && agenda.Id != agendaId)
            {
                message = "[错误] 该议程不是当前活跃议程";
                return false;
            }
            if (agenda.TargetClanId != target.Clan.StringId)
            {
                message = "[错误] 你不是本次朝议的被针对家族";
                return false;
            }
            if (agenda.Status != CuriaAgendaStatus.Proposed || agenda.TargetDefended
                || CuriaStore.Today >= agenda.DefenseDeadlineDay)
            {
                message = "[错误] 陈情期已过，朝议已进入表决";
                return false;
            }
            agenda.TargetDefense = string.IsNullOrWhiteSpace(reason) ? "（被针对家族未作陈情）" : reason!.Trim();
            agenda.TargetDefenseBy = target.Name?.ToString() ?? "";
            agenda.TargetDefended = true;
            CuriaStore.SaveAgenda(agenda, writeText: true);
            message = "陈情已记录，朝议将进入表决。";
            if (ShouldNotifyPlayer(kingdom))
                CuriaStore.Notify($"{agenda.TargetDefenseBy} 已就被提议的{CuriaText.TypeLabel(agenda.Type)}作出陈情。", Colors.Cyan);
            StartVoting(kingdom, agenda);
            return true;
        }

        public static void EnsureVoteFromAgent(Hero hero, string fallbackText)
        {
            if (hero == null) return;
            var kingdom = CuriaStore.GetHeroKingdom(hero);
            if (kingdom == null) return;
            var agenda = GetActiveAgenda(kingdom);
            if (agenda == null || agenda.Status != CuriaAgendaStatus.Voting) return;
            var vote = agenda.Votes.FirstOrDefault(v => v.ClanId == hero.Clan?.StringId);
            if (vote == null || vote.HasVoted) return;
            var reason = string.IsNullOrWhiteSpace(fallbackText) ? "未能明确表态" : fallbackText.Trim();
            if (reason.Length > 200) reason = reason.Substring(0, 200);
            SubmitVote(hero, agenda.Id, CuriaStance.Abstain, reason, out _);
        }

        public static void EnsureDefenseFromAgent(Hero hero, string fallbackText)
        {
            if (hero == null) return;
            var kingdom = CuriaStore.GetHeroKingdom(hero);
            if (kingdom == null) return;
            var agenda = GetActiveAgenda(kingdom);
            if (agenda == null || agenda.Status != CuriaAgendaStatus.Proposed) return;
            if (agenda.TargetClanId != hero.Clan?.StringId) return;
            if (agenda.TargetDefended) return;
            var reason = string.IsNullOrWhiteSpace(fallbackText) ? "（被针对家族未作陈情）" : fallbackText.Trim();
            if (reason.Length > 400) reason = reason.Substring(0, 400);
            SubmitDefense(hero, agenda.Id, reason, out _);
        }

        private static void RecomputeWeights(CuriaAgenda agenda)
        {
            var tally = CuriaVoteMath.Tally(agenda.Votes.Select(v => (v.Weight, StanceCode(v.Stance))));
            agenda.SupportWeight = tally.Support;
            agenda.OpposeWeight = tally.Oppose;
            agenda.AbstainWeight = tally.Abstain;
            agenda.SummonedWeight = tally.Summoned;

            var kingVote = agenda.Votes.FirstOrDefault(v => v.IsKing);
            agenda.KingStance = kingVote?.Stance.HasValue == true ? CuriaText.StanceKey(kingVote.Stance.Value) : "";
            agenda.KingReason = kingVote?.Reason ?? "";
        }

        private static int StanceCode(CuriaStance? stance)
        {
            if (!stance.HasValue) return -2;
            return stance.Value switch
            {
                CuriaStance.Support => 1,
                CuriaStance.Oppose => -1,
                _ => 0
            };
        }


        public static void OnKingdomDestroyed(Kingdom kingdom)
        {
            if (kingdom == null) return;
            var agenda = GetActiveAgenda(kingdom);
            if (agenda == null) return;
            agenda.Status = CuriaAgendaStatus.Cancelled;
            agenda.FailureReason = "王国灭亡，议程取消";
            agenda.Outcome = "王国灭亡，议程取消";
            CuriaStore.SaveAgenda(agenda, writeText: true);
            var state = GetState(kingdom);
            state.ActiveAgendaId = "";
            CuriaStore.SaveState(state);
            _activeAgendas.Remove(kingdom.StringId ?? kingdom.Name?.ToString() ?? "");
        }

        private static bool ShouldNotifyPlayer(Kingdom kingdom)
        {
            var player = Hero.MainHero;
            if (player == null || kingdom == null) return false;
            return player.Clan?.Kingdom == kingdom || player.MapFaction == kingdom;
        }

        public static string QueryPoliciesForHero(Hero hero)
        {
            return CuriaPageBuilder.BuildPolicyQueryText(hero);
        }

        public static string QueryForHero(Hero hero, string agendaId, bool history)
        {
            return CuriaPageBuilder.BuildQueryText(hero, agendaId, history);
        }

        public static object BuildPageData(Hero player) => CuriaPageBuilder.BuildPageData(player);
    }
}
