using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace AIChronicle
{
    /// <summary>
    /// 朝议的呈现层：CEF 页面数据、AI 查询文本、投票/陈情提示词、缺席叙事。
    /// 与 CuriaService 分离，保证两边都低于单文件规模上限。
    /// </summary>
    public static class CuriaPageBuilder
    {
        public static object BuildPageData(Hero? player)
        {
            var data = new Dictionary<string, object?>();
            var kingdom = CuriaStore.GetHeroKingdom(player);
            var playerEntity = player != null ? EntityManager.GetOrCreateEntity(player) : null;

            if (kingdom == null)
            {
                data["kingdom"] = "";
                data["currentDate"] = CuriaStore.CurrentLabel;
                data["notice"] = "你目前不属于任何王国，无法参与朝议。";
                data["player"] = BuildPlayer(player, null, false, false, false, false);
                data["active"] = null;
                data["history"] = new List<object>();
                data["foreignProposals"] = new List<object>();
                data["fiefs"] = new Dictionary<string, object> { ["own"] = new List<object>(), ["vassal"] = new List<object>() };
                data["clans"] = new List<object>();
                data["kingdoms"] = new List<object>();
                data["secretAdvisory"] = "";
                data["consultThreads"] = new List<object>();
                return data;
            }

            var isKing = player != null && kingdom.RulingClan?.Leader == player;
            var isMercenary = CuriaStore.IsMercenary(player?.Clan);
            var isClanLeader = player?.Clan != null && player.Clan.Leader == player;
            var active = CuriaService.GetActiveAgenda(kingdom);
            var canParticipate = !isMercenary && isClanLeader && player?.Clan?.Kingdom == kingdom;
            var activeDetail = active != null ? BuildAgendaDetail(active, player) : null;

            data["kingdom"] = kingdom.Name?.ToString() ?? "";
            data["currentDate"] = CuriaStore.CurrentLabel;
            data["player"] = BuildPlayer(player, kingdom, isKing, isMercenary, isClanLeader, canParticipate);
            data["active"] = activeDetail;
            data["history"] = CuriaStore.ListAgendas(kingdom.Name?.ToString() ?? "", 30)
                .Where(a => CuriaService.IsTerminal(a.Status))
                .Select(a => BuildAgendaDetail(a, player))
                .ToList();
            data["foreignProposals"] = isKing ? BuildForeignProposals(playerEntity?.Id ?? "") : new List<object>();
            data["fiefs"] = BuildFiefs(kingdom, player, isKing);
            data["clans"] = BuildClans(kingdom, player);
            data["kingdoms"] = BuildKingdoms(kingdom);
            data["policies"] = BuildPolicies(kingdom);
            data["policyEffects"] = CuriaPolicyEffects.DescribeKingdom(kingdom);
            data["secretAdvisory"] = isKing ? CuriaStore.ReadSecretAdvisoryText(kingdom) : "";
            data["consultThreads"] = isKing
                ? CuriaStore.ReadConsultThreads(kingdom).Select(t => (object)new Dictionary<string, object?>
                {
                    ["label"] = t.Label,
                    ["body"] = t.Body
                }).ToList()
                : new List<object>();
            data["notice"] = !canParticipate
                ? (isMercenary ? "你是雇佣兵，不参与王国朝议。" : "只有家族领袖才能参与朝议。")
                : active == null
                    ? "当前没有进行中的朝议。可提出新议题。"
                    : "朝议进行中。你的立场与理由会被公开记录。";
            data["settings"] = new Dictionary<string, object?>
            {
                ["fullThreshold"] = MySettings.Instance?.CuriaFullCouncilThreshold ?? 6,
                ["attendanceCap"] = MySettings.Instance?.CuriaAttendanceCap ?? 6,
                ["voteDays"] = MySettings.Instance?.CuriaVoteDays ?? 5,
                ["quorum"] = MySettings.Instance?.CuriaQuorum ?? 0.5f,
                ["warThreshold"] = MySettings.Instance?.CuriaWarThreshold ?? 0.5f,
                ["fiefThreshold"] = MySettings.Instance?.CuriaFiefThreshold ?? 0.66f
            };
            return data;
        }

        private static object BuildPlayer(Hero? player, Kingdom? kingdom, bool isKing, bool isMercenary, bool isClanLeader, bool canParticipate)
        {
            return new Dictionary<string, object?>
            {
                ["name"] = player?.Name?.ToString() ?? "",
                ["clan"] = player?.Clan?.Name?.ToString() ?? "",
                ["kingdom"] = kingdom?.Name?.ToString() ?? "",
                ["isKing"] = isKing,
                ["isMercenary"] = isMercenary,
                ["isClanLeader"] = isClanLeader,
                ["canParticipate"] = canParticipate
            };
        }

        private static List<object> BuildForeignProposals(string playerEntityId)
        {
            var result = new List<object>();
            if (string.IsNullOrEmpty(playerEntityId)) return result;
            try
            {
                foreach (var id in AgentManager.ListPendingProposals(playerEntityId))
                {
                    var content = AgentManager.ReadDiplomacyProposal(id);
                    if (content == null) continue;
                    var meta = AgentManager.ParseProposalMeta(content);
                    var proposer = EntityManager.GetEntityById(meta.ProposerId);
                    var typeLabel = meta.Type switch
                    {
                        "peace" => "议和",
                        "alliance" => "结盟",
                        "trade" => "贸易协定",
                        _ => meta.Type
                    };
                    var message = "";
                    foreach (var line in content.Split('\n'))
                    {
                        if (line.StartsWith("message=")) message = line.Substring(8);
                    }
                    result.Add(new Dictionary<string, object?>
                    {
                        ["id"] = id,
                        ["type"] = meta.Type,
                        ["typeLabel"] = typeLabel,
                        ["proposer"] = proposer?.Name ?? meta.ProposerId,
                        ["message"] = message
                    });
                }
            }
            catch { }
            return result;
        }

        private static Dictionary<string, object> BuildFiefs(Kingdom kingdom, Hero? player, bool isKing)
        {
            var own = new List<object>();
            var vassal = new List<object>();
            try
            {
                foreach (var s in Settlement.All)
                {
                    if (!s.IsTown && !s.IsCastle) continue;
                    var owner = s.OwnerClan;
                    if (owner == null || owner.Kingdom != kingdom) continue;
                    if (CuriaStore.IsMercenary(owner)) continue;
                    var isRulerClan = owner == kingdom.RulingClan;
                    var item = new Dictionary<string, object?>
                    {
                        ["id"] = s.StringId ?? "",
                        ["name"] = s.Name?.ToString() ?? "",
                        ["type"] = s.IsTown ? "城镇" : "城堡",
                        ["ownerClan"] = owner.Name?.ToString() ?? "",
                        ["ownerClanId"] = owner.StringId ?? "",
                        ["ownerLeader"] = owner.Leader?.Name?.ToString() ?? "",
                        ["ownerLeaderEntityId"] = CuriaStore.EntityIdOf(owner.Leader),
                        ["isRulerClan"] = isRulerClan
                    };
                    if (player?.Clan != null && owner == player.Clan) own.Add(item);
                    else if (!isRulerClan) vassal.Add(item);
                }
            }
            catch { }
            return new Dictionary<string, object> { ["own"] = own, ["vassal"] = vassal };
        }

        private static List<object> BuildClans(Kingdom kingdom, Hero? player)
        {
            var result = new List<object>();
            try
            {
                foreach (var clan in kingdom.Clans)
                {
                    if (clan == null || clan.IsEliminated) continue;
                    if (CuriaStore.IsMercenary(clan)) continue;
                    if (clan.IsMinorFaction || clan.IsBanditFaction) continue;
                    var leader = clan.Leader;
                    if (leader == null) continue;
                    var fiefs = Settlement.All.Count(s => (s.IsTown || s.IsCastle) && s.OwnerClan == clan);
                    result.Add(new Dictionary<string, object?>
                    {
                        ["id"] = clan.StringId ?? "",
                        ["name"] = clan.Name?.ToString() ?? "",
                        ["leader"] = leader.Name?.ToString() ?? "",
                        ["leaderEntityId"] = CuriaStore.EntityIdOf(leader),
                        ["tier"] = clan.Tier,
                        ["fiefs"] = fiefs,
                        ["isRulerClan"] = clan == kingdom.RulingClan,
                        ["isPlayerClan"] = player?.Clan == clan,
                        ["weight"] = CuriaStore.WeightOf(clan)
                    });
                }
            }
            catch { }
            return result;
        }

        private static List<object> BuildPolicies(Kingdom kingdom)
        {
            var result = new List<object>();
            try
            {
                if (Campaign.Current == null) return result;
                var permission = Campaign.Current.Models.KingdomDecisionPermissionModel;
                var all = PolicyObject.All;
                if (all == null) return result;
                foreach (var policy in all)
                {
                    result.Add(new Dictionary<string, object?>
                    {
                        ["id"] = policy.StringId ?? "",
                        ["name"] = policy.Name?.ToString() ?? "",
                        ["description"] = policy.Description?.ToString() ?? "",
                        ["effect"] = policy.SecondaryEffects?.ToString() ?? "",
                        ["active"] = kingdom.HasPolicy(policy),
                        ["allowed"] = permission == null || permission.IsPolicyDecisionAllowed(policy),
                        ["curiaEffect"] = CuriaPolicyEffects.DescribePolicy(policy)
                    });
                }
            }
            catch { }
            return result;
        }

        private static List<object> BuildKingdoms(Kingdom kingdom)
        {
            var result = new List<object>();
            try
            {
                var alliance = Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
                var trade = Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
                foreach (var k in Kingdom.All)
                {
                    if (k == kingdom || k.IsEliminated) continue;
                    var atWar = kingdom.IsAtWarWith(k);
                    var isAlly = alliance != null && alliance.IsAllyWithKingdom(kingdom, k);
                    var hasTrade = trade != null && trade.HasTradeAgreement(kingdom, k, out _);
                    result.Add(new Dictionary<string, object?>
                    {
                        ["id"] = k.StringId ?? "",
                        ["name"] = k.Name?.ToString() ?? "",
                        ["atWar"] = atWar,
                        ["isAlly"] = isAlly,
                        ["hasTrade"] = hasTrade,
                        ["canWar"] = !atWar,
                        ["canPeace"] = atWar,
                        ["canTrade"] = !atWar && !hasTrade,
                        ["canAlliance"] = !atWar && !isAlly,
                        ["canBreakAlliance"] = isAlly
                    });
                }
            }
            catch { }
            return result;
        }

        public static Dictionary<string, object?> BuildAgendaDetail(CuriaAgenda a, Hero? player)
        {
            var playerClanId = player?.Clan?.StringId ?? "";
            var agendaKingdom = CuriaStore.FindKingdom(a.KingdomName);
            var policyEffects = CuriaPolicyEffects.DescribeKingdom(agendaKingdom);
            var playerVote = a.Votes.FirstOrDefault(v => v.ClanId == playerClanId);
            var kingMissingReason = "";
            if (a.Votes.Count > 0 && !a.Votes.Any(v => v.IsKing))
            {
                var king = CuriaStore.GetKing(agendaKingdom);
                kingMissingReason = king == null ? "王国没有统治者"
                    : !king.IsAlive ? "国王已故"
                    : king.IsPrisoner ? "国王被俘"
                    : king.IsFugitive ? "国王流亡"
                    : "国王未获应召";
            }
            var isTarget = a.NeedsTargetDefense && a.TargetClanId == playerClanId;
            var canVote = a.Status == CuriaAgendaStatus.Voting && playerVote != null && !playerVote.Stance.HasValue;
            var canDefend = a.Status == CuriaAgendaStatus.Proposed && isTarget && !a.TargetDefended;

            var votes = a.Votes.Select(v => (object)new Dictionary<string, object?>
            {
                ["clan"] = v.ClanName,
                ["leader"] = v.LeaderName,
                ["weight"] = v.Weight,
                ["stance"] = v.Stance.HasValue ? CuriaText.StanceKey(v.Stance.Value) : "",
                ["stanceLabel"] = v.Stance.HasValue ? CuriaText.StanceLabel(v.Stance.Value) : "未表态",
                ["reason"] = v.Reason,
                ["summonReason"] = v.SummonReason,
                ["isKing"] = v.IsKing,
                ["isPlayer"] = v.IsPlayer,
                ["isProposer"] = v.IsProposer
            }).ToList();

            var absent = a.Absent.Select(v => (object)new Dictionary<string, object?>
            {
                ["clan"] = v.ClanName,
                ["leader"] = v.LeaderName,
                ["reason"] = v.Reason
            }).ToList();

            return new Dictionary<string, object?>
            {
                ["id"] = a.Id,
                ["type"] = a.Type.ToString(),
                ["typeLabel"] = CuriaText.TypeLabel(a.Type),
                ["kind"] = a.Kind.ToString(),
                ["kindLabel"] = CuriaText.KindLabel(a.Kind),
                ["status"] = a.Status.ToString(),
                ["statusLabel"] = CuriaText.StatusLabel(a.Status),
                ["proposer"] = a.ProposerName,
                ["proposerClan"] = a.ProposerClanId,
                ["reason"] = a.Reason,
                ["terms"] = a.Terms,
                ["target"] = CuriaText.TargetSummary(a),
                ["targetClan"] = a.TargetClanName,
                ["targetSettlement"] = a.TargetSettlementName,
                ["newOwner"] = a.NewOwnerClanName,
                ["created"] = a.CreatedLabel,
                ["deadline"] = a.DeadlineDay,
                ["daysLeft"] = Math.Max(0, a.DeadlineDay - CuriaStore.Today),
                ["threshold"] = a.Threshold,
                ["thresholdPct"] = a.Threshold * 100f,
                ["quorumWeight"] = a.QuorumWeight,
                ["totalWeight"] = a.TotalWeight,
                ["summonedWeight"] = a.SummonedWeight,
                ["supportWeight"] = a.SupportWeight,
                ["opposeWeight"] = a.OpposeWeight,
                ["abstainWeight"] = a.AbstainWeight,
                ["supportPct"] = a.SupportRatio * 100f,
                ["quorumMet"] = a.SummonedWeight + 0.01f >= a.QuorumWeight,
                ["targetDefense"] = a.TargetDefense,
                ["targetDefenseBy"] = a.TargetDefenseBy,
                ["kingStance"] = a.KingStance,
                ["kingReason"] = a.KingReason,
                ["targetPolicyId"] = a.TargetPolicyId,
                ["targetPolicyName"] = a.TargetPolicyName,
                ["policyEffect"] = a.PolicyEffect,
                ["policyDescription"] = a.PolicyDescription,
                ["policyEffects"] = policyEffects,
                ["kingMissingReason"] = kingMissingReason,
                ["outcome"] = a.Outcome,
                ["effect"] = a.EffectResult,
                ["failure"] = a.FailureReason,
                ["votes"] = votes,
                ["absent"] = absent,
                ["playerStance"] = playerVote?.Stance.HasValue == true ? CuriaText.StanceKey(playerVote.Stance.Value) : "",
                ["playerStanceLabel"] = playerVote?.Stance.HasValue == true ? CuriaText.StanceLabel(playerVote.Stance.Value) : "",
                ["playerAutoSupport"] = playerVote?.IsAutoSupport == true,
                ["playerReason"] = playerVote?.Reason ?? "",
                ["canVote"] = canVote,
                ["canDefend"] = canDefend,
                ["isTarget"] = isTarget,
                ["isTerminal"] = CuriaService.IsTerminal(a.Status)
            };
        }

        public static string BuildQueryText(Hero hero, string agendaId, bool history, bool detail)
        {
            var kingdom = CuriaStore.GetHeroKingdom(hero);
            if (kingdom == null) return "你不属于任何王国，无法查看朝议。";

            if (!string.IsNullOrWhiteSpace(agendaId))
            {
                var agenda = CuriaStore.LoadAgenda(kingdom.Name?.ToString() ?? "", agendaId);
                if (agenda == null) return $"未找到朝议：{agendaId}";
                return BuildAgendaText(agenda, detailed: detail);
            }

            if (history)
            {
                var list = CuriaStore.ListAgendas(kingdom.Name?.ToString() ?? "", 5)
                    .Where(a => CuriaService.IsTerminal(a.Status)).ToList();
                if (list.Count == 0) return "本国暂无已结束的朝议记录。";
                var sb = new StringBuilder();
                sb.AppendLine($"【{kingdom.Name} · 朝议记录】");
                foreach (var a in list)
                    sb.AppendLine($"- {a.CreatedLabel} {CuriaText.TypeLabel(a.Type)}：{CuriaText.TargetSummary(a)} → {CuriaText.StatusLabel(a.Status)}（{a.Outcome}）");
                return sb.ToString().TrimEnd();
            }

            var active = CuriaService.GetActiveAgenda(kingdom);
            if (active == null) return "本国当前没有进行中的朝议。";
            return BuildAgendaText(active, detailed: detail);
        }

        public static string BuildAgendaText(CuriaAgenda a, bool detailed)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"【朝议 · {a.KingdomName}】");
            sb.AppendLine($"议题：{CuriaText.TypeLabel(a.Type)}（{CuriaText.KindLabel(a.Kind)}）");
            sb.AppendLine($"状态：{CuriaText.StatusLabel(a.Status)}    截止：第{a.DeadlineDay}日");
            sb.AppendLine($"提议者：{a.ProposerName}");
            sb.AppendLine($"目标：{CuriaText.TargetSummary(a)}");
            sb.AppendLine($"理由：{a.Reason}");
            if (!string.IsNullOrEmpty(a.Terms)) sb.AppendLine($"条款：{a.Terms}");
            if (!string.IsNullOrEmpty(a.PolicyDescription)) sb.AppendLine($"政策说明：{a.PolicyDescription}");
            if (!string.IsNullOrEmpty(a.PolicyEffect)) sb.AppendLine($"政策效果：{a.PolicyEffect}");
            var policyEffects = CuriaPolicyEffects.DescribeKingdom(CuriaStore.FindKingdom(a.KingdomName));
            if (!string.IsNullOrEmpty(policyEffects)) sb.AppendLine($"当前朝议政策修正：{policyEffects}");
            if (a.Votes.Count > 0 && !a.Votes.Any(v => v.IsKing))
            {
                var king = CuriaStore.GetKing(CuriaStore.FindKingdom(a.KingdomName));
                var reason = king == null ? "王国没有统治者"
                    : !king.IsAlive ? "国王已故"
                    : king.IsPrisoner ? "国王被俘"
                    : king.IsFugitive ? "国王流亡"
                    : "国王未获应召";
                sb.AppendLine($"国王未参与：{reason}");
            }
            sb.AppendLine($"门槛：{a.Threshold:P0}    quorum：{a.SummonedWeight:F1}/{a.QuorumWeight:F1}（总权重 {a.TotalWeight:F1}）");
            sb.AppendLine($"票数：支持 {a.SupportWeight:F1} / 反对 {a.OpposeWeight:F1} / 弃权 {a.AbstainWeight:F1}");
            if (!string.IsNullOrEmpty(a.TargetDefense))
                sb.AppendLine($"陈情（{a.TargetDefenseBy}）：{a.TargetDefense}");
            if (!string.IsNullOrEmpty(a.KingStance))
                sb.AppendLine($"国王态度：{a.KingStance}{(string.IsNullOrEmpty(a.KingReason) ? "" : "（" + a.KingReason + "）")}");
            if (!string.IsNullOrEmpty(a.Outcome)) sb.AppendLine($"结果：{a.Outcome}");
            if (!string.IsNullOrEmpty(a.EffectResult)) sb.AppendLine($"执行：{a.EffectResult}");
            if (!string.IsNullOrEmpty(a.FailureReason)) sb.AppendLine($"失效：{a.FailureReason}");

            if (detailed)
            {
                sb.AppendLine();
                sb.AppendLine("应召家族：");
                foreach (var v in a.Votes)
                {
                    var stance = v.Stance.HasValue ? CuriaText.StanceLabel(v.Stance.Value) : "未表态";
                    var reason = string.IsNullOrWhiteSpace(v.Reason) ? "" : "：" + v.Reason;
                    sb.AppendLine($"- {v.ClanName}（{v.LeaderName}，权重 {v.Weight:F1}，{v.SummonReason}）{stance}{reason}");
                }
                if (a.Absent.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("未出席：");
                    foreach (var v in a.Absent)
                        sb.AppendLine($"- {v.ClanName}（{v.LeaderName}）：{v.Reason}");
                }
            }
            return sb.ToString().TrimEnd();
        }

        public static string BuildPolicyQueryText(Hero hero, string policyName)
        {
            var kingdom = CuriaStore.GetHeroKingdom(hero);
            if (kingdom == null) return "你不属于任何王国，无法查看政策。";
            if (Campaign.Current == null) return "政策数据尚未就绪。";
            var permission = Campaign.Current.Models.KingdomDecisionPermissionModel;
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(policyName))
            {
                var key = policyName.Trim();
                var selected = PolicyObject.All.FirstOrDefault(p =>
                    string.Equals(p.StringId, key, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.Name?.ToString(), key, StringComparison.OrdinalIgnoreCase));
                if (selected == null) return $"未找到政策：{policyName}";
                var active = kingdom.HasPolicy(selected);
                var allowed = permission == null || permission.IsPolicyDecisionAllowed(selected);
                sb.AppendLine($"【政策详情 · {kingdom.Name}】");
                sb.AppendLine($"{selected.Name}（{selected.StringId}）");
                sb.AppendLine($"状态：{(active ? "已实施" : "未实施")}；{(allowed ? "当前可提议" : "当前不可提议")}");
                sb.AppendLine($"说明：{selected.Description}");
                sb.AppendLine($"原版效果：{selected.SecondaryEffects}");
                var curiaEffect = CuriaPolicyEffects.DescribePolicy(selected);
                if (!string.IsNullOrEmpty(curiaEffect)) sb.AppendLine($"朝议修正：{curiaEffect}");
                return sb.ToString().TrimEnd();
            }

            sb.AppendLine($"【{kingdom.Name} · 政策摘要】");
            sb.AppendLine("只列名称、状态、一行效果和朝议修正；要看完整说明请用 policy_name 查询单条。");
            var listed = 0;
            foreach (var policy in PolicyObject.All)
            {
                var active = kingdom.HasPolicy(policy);
                var allowed = permission == null || permission.IsPolicyDecisionAllowed(policy);
                var effect = (policy.SecondaryEffects?.ToString() ?? "").Replace("{newline}", "；").Replace("\n", "；").Trim();
                if (effect.Length > 160) effect = effect.Substring(0, 160) + "…";
                sb.Append($"- {(active ? "已实施" : "未实施")}：{policy.Name}（{policy.StringId}）");
                if (!allowed) sb.Append("（当前不可提议）");
                if (!string.IsNullOrEmpty(effect)) sb.Append($" — {effect}");
                var curiaEffect = CuriaPolicyEffects.DescribePolicy(policy);
                if (!string.IsNullOrEmpty(curiaEffect)) sb.Append($"；朝议：{curiaEffect}");
                sb.AppendLine();
                listed++;
            }
            if (listed == 0) sb.AppendLine("（没有可查看的政策）");
            return sb.ToString().TrimEnd();
        }

        public static string BuildVotePrompt(CuriaAgenda agenda, CuriaVote voter)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"你是{voter.LeaderName}，{voter.ClanName}的家族领袖。{agenda.KingdomName}正在举行朝议，你是应召的投票者。");
            var policyEffects = CuriaPolicyEffects.DescribeKingdom(CuriaStore.FindKingdom(agenda.KingdomName));
            if (!string.IsNullOrEmpty(policyEffects)) sb.AppendLine($"当前朝议政策修正：{policyEffects}");
            sb.AppendLine();
            sb.AppendLine($"议题：{CuriaText.TypeLabel(agenda.Type)}（{CuriaText.KindLabel(agenda.Kind)}）");
            sb.AppendLine($"目标：{CuriaText.TargetSummary(agenda)}");
            sb.AppendLine($"提议者：{agenda.ProposerName}");
            sb.AppendLine($"理由：{agenda.Reason}");
            if (!string.IsNullOrEmpty(agenda.Terms)) sb.AppendLine($"条款：{agenda.Terms}");
            if (!string.IsNullOrEmpty(agenda.PolicyDescription)) sb.AppendLine($"政策说明：{agenda.PolicyDescription}");
            if (!string.IsNullOrEmpty(agenda.PolicyEffect)) sb.AppendLine($"政策效果：{agenda.PolicyEffect}");
            if (!string.IsNullOrEmpty(agenda.TargetDefense))
                sb.AppendLine($"被针对家族陈情（{agenda.TargetDefenseBy}）：{agenda.TargetDefense}");
            AppendVoterFacts(sb, agenda, voter);
            sb.AppendLine();
            sb.AppendLine($"你的家族权重：{voter.Weight:F1}；应召原因：{voter.SummonReason}。" +
                          (voter.IsKing && CuriaPolicyEffects.KingVoteWeightMultiplier(CuriaStore.FindKingdom(agenda.KingdomName)) > 1f
                              ? "（王室特权：票权 ×1.2 已计入）" : ""));
            sb.AppendLine($"本次门槛：{agenda.Threshold:P0}；quorum：{agenda.QuorumWeight:F1}（应召权重 {agenda.SummonedWeight:F1}）。");
            sb.AppendLine($"当前票数：支持 {agenda.SupportWeight:F1} / 反对 {agenda.OpposeWeight:F1} / 弃权 {agenda.AbstainWeight:F1}。");
            sb.AppendLine();
            sb.AppendLine("请根据你的家族利益、封地安危、贸易与盟约、你与提议者/国王/被针对家族的关系、以及你的性格，决定你的立场。");
            sb.AppendLine("上方已预置关键事实。只有确实缺少某项事实时，才做一次补充查询（query_agenda / query_character / query_clan_fiefs / query_war_status / query_settlement / query_party_troops 中选一个）；不要重复查询。");
            sb.AppendLine("调用 respond_to_agenda(stance=\"support\"|\"oppose\"|\"abstain\", reason=\"可选的一句话理由\")。");
            sb.AppendLine("只做这一件事，不要写信、不要行军、不要做其他动作。你的投票与理由会被公开记录，史官与所有封臣可见。");
            sb.AppendLine("若你确实无意见，也请投弃权——不要不表态。");
            return sb.ToString().TrimEnd();
        }

        private static void AppendVoterFacts(StringBuilder sb, CuriaAgenda agenda, CuriaVote voter)
        {
            var kingdom = CuriaStore.FindKingdom(agenda.KingdomName);
            if (kingdom == null) return;
            var voterHero = CuriaStore.FindHeroByEntityId(voter.LeaderEntityId);
            var voterClan = voterHero?.Clan;
            if (voterClan != null)
            {
                sb.AppendLine($"你的家族现状：影响力 {voterClan.Influence:F0}，封地 {voterClan.Settlements.Count} 处。");
                var proposer = CuriaStore.FindHeroByEntityId(agenda.ProposerEntityId);
                if (proposer != null)
                    sb.AppendLine($"你与提议者{proposer.Name}的家族关系：{voterHero.GetRelation(proposer):F0}。");
                var king = CuriaStore.GetKing(kingdom);
                if (king != null && king != voterHero)
                    sb.AppendLine($"你与国王{king.Name}的关系：{voterHero.GetRelation(king):F0}。");
            }

            try
            {
                if (agenda.Kind == CuriaAgendaKind.External)
                {
                    var target = CuriaStore.FindKingdom(agenda.TargetKingdomName);
                    if (target != null)
                    {
                        var alliance = Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
                        var trade = Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
                        var isAlly = alliance != null && alliance.IsAllyWithKingdom(kingdom, target);
                        var hasTrade = trade != null && trade.HasTradeAgreement(kingdom, target, out _);
                        sb.AppendLine($"目标现状：{target.Name}；交战={kingdom.IsAtWarWith(target)}，同盟={isAlly}，贸易={hasTrade}。");
                    }
                }
                else if (agenda.Type == CuriaAgendaType.TransferFief)
                {
                    var settlement = CuriaStore.FindSettlement(agenda.TargetSettlementName);
                    if (settlement != null)
                    {
                        var garrison = settlement.Town?.GarrisonParty?.Party?.MemberRoster?.TotalManCount ?? 0;
                        sb.AppendLine($"封地现状：{settlement.Name}（{settlement.OwnerClan?.Name}），守军约 {garrison}；新主 {agenda.NewOwnerClanName}。");
                    }
                }
                else if (agenda.Type == CuriaAgendaType.ExpelClan)
                {
                    var targetClan = CuriaStore.FindClan(agenda.TargetClanId);
                    if (targetClan != null)
                        sb.AppendLine($"目标家族现状：{targetClan.Name}，封地 {targetClan.Settlements.Count} 处，领袖 {targetClan.Leader?.Name}。");
                }
            }
            catch { }
        }

        public static string BuildDefensePrompt(CuriaAgenda agenda)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"你是{agenda.TargetClanName}的家族领袖。{agenda.KingdomName}正在朝议{CuriaText.TypeLabel(agenda.Type)}：{agenda.Terms}。");
            sb.AppendLine($"提议者：{agenda.ProposerName}；理由：{agenda.Reason}");
            sb.AppendLine();
            sb.AppendLine("你被针对，拥有陈情机会。请陈述你的理由——你的忠诚、战功、封地治理、家族关系、对指控的反驳，或你愿意做出的让步。");
            sb.AppendLine("需要事实时先用 query_character / query_clan_fiefs / query_war_status / query_agenda 等查询工具核实。");
            sb.AppendLine("调用 defend_agenda(reason=\"你的陈情\")。陈情会被公开记录，其他家族投票时可以看到。");
            sb.AppendLine("你也可以用 send_letter / send_envoy 游说其他家族，但时间有限；只做这一件事即可。");
            return sb.ToString().TrimEnd();
        }

        public static string BuildAbsenceReason(Hero leader, Kingdom kingdom)
        {
            try
            {
                if (leader.IsPrisoner) return "身陷囹圄";
                if (leader.IsFugitive) return "流亡在外";
                var party = leader.PartyBelongedTo;
                if (party != null)
                {
                    if (party.SiegeEvent != null) return "军务在身、正在围城";
                    if (party.MapEvent != null) return "正在作战";
                    if (party.IsMoving) return "路途遥远、未及赶到";
                }
                if (leader.CurrentSettlement == null && party == null) return "远在异地";
                if (leader.Clan != null && leader.Clan.Tier <= 2) return "未获召见";
                if (HasBesiegedFief(leader)) return "封地遭围、分身乏术";
                return "染病或家族事务缠身";
            }
            catch
            {
                return "未获召见";
            }
        }

        private static bool HasBesiegedFief(Hero leader)
        {
            try
            {
                var clan = leader.Clan;
                if (clan == null) return false;
                foreach (var s in clan.Settlements)
                {
                    if (s.IsUnderSiege) return true;
                }
            }
            catch { }
            return false;
        }
    }
}
