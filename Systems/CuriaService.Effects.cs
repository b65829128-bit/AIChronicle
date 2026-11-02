using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace AIChronicle
{
    public static partial class CuriaService
    {
        private static void Resolve(Kingdom kingdom, CuriaAgenda agenda)
        {
            if (agenda == null || kingdom == null) return;
            RecomputeWeights(agenda);
            var quorumMet = CuriaVoteMath.MeetsQuorum(agenda.SummonedWeight, agenda.QuorumWeight);
            var passed = false;
            string outcome;

            if (!quorumMet)
            {
                outcome = $"出席权重 {agenda.SummonedWeight:F1} 未达 quorum {agenda.QuorumWeight:F1}，流会。";
            }
            else if (agenda.DecidedWeight <= 0.01f)
            {
                outcome = "无人明确支持或反对，视为未通过。";
            }
            else if (CuriaVoteMath.Passes(agenda.SupportWeight, agenda.OpposeWeight, agenda.Threshold))
            {
                passed = true;
                outcome = $"通过：支持 {agenda.SupportWeight:F1} / 反对 {agenda.OpposeWeight:F1}（{agenda.SupportRatio:P1}，门槛 {agenda.Threshold:P0}）。";
            }
            else
            {
                outcome = $"未通过：支持 {agenda.SupportWeight:F1} / 反对 {agenda.OpposeWeight:F1}（{agenda.SupportRatio:P1}，门槛 {agenda.Threshold:P0}）。";
            }

            agenda.Status = passed ? CuriaAgendaStatus.Passed : CuriaAgendaStatus.Rejected;
            agenda.Outcome = outcome;
            agenda.ResolvedLabel = CuriaStore.CurrentLabel;
            if (!passed)
            {
                CuriaStore.SaveAgenda(agenda, writeText: true);
                CuriaStore.RecordHistory("curia_rejected",
                    $"{kingdom.Name}朝议「{CuriaText.TypeLabel(agenda.Type)}：{CuriaText.TargetSummary(agenda)}」{outcome}");
                FinishAgenda(kingdom, agenda, passed: false);
                return;
            }

            CuriaStore.SaveAgenda(agenda, writeText: false);
            CuriaStore.RecordHistory("curia_passed",
                $"{kingdom.Name}朝议通过「{CuriaText.TypeLabel(agenda.Type)}：{CuriaText.TargetSummary(agenda)}」：{outcome}");
            ApplyEffectAndFinish(kingdom, agenda);
        }

        private static void ApplyEffectAndFinish(Kingdom kingdom, CuriaAgenda agenda)
        {
            var king = CuriaStore.GetKing(kingdom);
            if (king == null || !king.IsAlive)
            {
                FailAgenda(kingdom, agenda, "王国没有在世统治者");
                return;
            }

            string result;
            var prevHero = AIChatClient.CurrentHero;
            AIChatClient.CurrentHero = king;
            try
            {
                switch (agenda.Type)
                {
                    case CuriaAgendaType.DeclareWar:
                        result = DiplomacyService.ExecuteDeclareWar(agenda.TargetKingdomName, agenda.Reason);
                        break;
                    case CuriaAgendaType.ProposePeace:
                        result = DiplomacyService.ExecuteProposePeace(
                            agenda.TargetKingdomName,
                            agenda.Treaty?.TributeAmount ?? 0,
                            agenda.Treaty?.TributeDays ?? 0,
                            agenda.Reason,
                            agenda.Treaty);
                        break;
                    case CuriaAgendaType.ProposeTreaty:
                        result = DiplomacyService.ExecuteProposeTreaty(
                            agenda.TargetKingdomName, agenda.Treaty, agenda.Reason);
                        break;
                    case CuriaAgendaType.ProposeTrade:
                        result = DiplomacyService.ExecuteProposeTrade(agenda.TargetKingdomName, agenda.Reason);
                        break;
                    case CuriaAgendaType.ProposeAlliance:
                        result = DiplomacyService.ExecuteProposeAlliance(agenda.TargetKingdomName, agenda.Reason);
                        break;
                    case CuriaAgendaType.BreakAlliance:
                        result = DiplomacyService.ExecuteEndAlliance(agenda.TargetKingdomName);
                        break;
                    case CuriaAgendaType.EnactPolicy:
                    case CuriaAgendaType.RevokePolicy:
                        {
                            var policy = FindPolicy(agenda.TargetPolicyId);
                            if (policy == null)
                            {
                                result = "[错误] 政策已不存在：" + agenda.TargetPolicyName;
                                break;
                            }
                            var active = kingdom.HasPolicy(policy);
                            if (agenda.Type == CuriaAgendaType.EnactPolicy)
                            {
                                if (active)
                                {
                                    result = $"政策「{policy.Name}」已经处于实施状态。";
                                }
                                else
                                {
                                    kingdom.AddPolicy(policy);
                                    result = $"已实施政策「{policy.Name}」。效果：{policy.SecondaryEffects}";
                                    CuriaStore.RecordHistory("policy_enacted",
                                        $"{kingdom.Name}实施政策「{policy.Name}」：{policy.SecondaryEffects}");
                                }
                            }
                            else
                            {
                                if (!active)
                                {
                                    result = $"政策「{policy.Name}」已经处于废除状态。";
                                }
                                else
                                {
                                    kingdom.RemovePolicy(policy);
                                    result = $"已废除政策「{policy.Name}」。";
                                    CuriaStore.RecordHistory("policy_revoked",
                                        $"{kingdom.Name}废除政策「{policy.Name}」");
                                }
                            }
                            break;
                        }

                    case CuriaAgendaType.TransferFief:
                        {
                            var settlement = CuriaStore.FindSettlement(agenda.TargetSettlementName);
                            var newOwner = CuriaStore.FindHeroByEntityId(agenda.NewOwnerEntityId);
                            if (settlement == null || newOwner?.Clan == null)
                            {
                                AIChatClient.CurrentHero = prevHero;
                                FailAgenda(kingdom, agenda, "目标封地或新主已不存在");
                                return;
                            }
                            if (settlement.OwnerClan?.StringId == agenda.NewOwnerClanId)
                            {
                                result = $"封地{settlement.Name}已归{newOwner.Clan.Name}（重复执行跳过）";
                                break;
                            }
                            if (settlement.OwnerClan?.StringId != agenda.TargetClanId)
                            {
                                AIChatClient.CurrentHero = prevHero;
                                FailAgenda(kingdom, agenda, "封地已易主，议程失效");
                                return;
                            }
                            result = DiplomacyService.ExecuteTransferFief(settlement.Name?.ToString() ?? "", agenda.NewOwnerEntityId, agenda.Reason, byCuria: true, voluntary: agenda.Kind == CuriaAgendaKind.VoluntaryTransfer);
                            break;
                        }
                    case CuriaAgendaType.ExpelClan:
                        {
                            var targetClan = Clan.All.FirstOrDefault(c => c.StringId == agenda.TargetClanId);
                            if (targetClan == null || targetClan.Kingdom != kingdom)
                            {
                                AIChatClient.CurrentHero = prevHero;
                                FailAgenda(kingdom, agenda, "目标家族已不在王国");
                                return;
                            }
                            result = DiplomacyService.ExecuteExpelClan(agenda.TargetClanId);
                            break;
                        }
                    default:
                        AIChatClient.CurrentHero = prevHero;
                        FailAgenda(kingdom, agenda, "未知议程类型");
                        return;
                }
            }
            catch (Exception ex)
            {
                AIChatClient.CurrentHero = prevHero;
                FailAgenda(kingdom, agenda, "执行异常：" + ex.Message);
                DebugLogger.Log("朝议执行异常：" + ex);
                return;
            }
            finally
            {
                AIChatClient.CurrentHero = prevHero;
            }

            if (string.IsNullOrWhiteSpace(result))
            {
                FailAgenda(kingdom, agenda, "执行器未返回结果");
                return;
            }
            if (result.StartsWith("[错误]", StringComparison.Ordinal) || result.StartsWith("[失败]", StringComparison.Ordinal))
            {
                FailAgenda(kingdom, agenda, result);
                return;
            }

            agenda.Status = CuriaAgendaStatus.Applied;
            agenda.EffectResult = result;
            agenda.ResolvedLabel = CuriaStore.CurrentLabel;
            CuriaStore.SaveAgenda(agenda, writeText: true);
            CuriaStore.RecordHistory("curia_applied",
                $"{kingdom.Name}朝议「{CuriaText.TypeLabel(agenda.Type)}：{CuriaText.TargetSummary(agenda)}」执行：{result}");
            FinishAgenda(kingdom, agenda, passed: true);
        }

        private static void FailAgenda(Kingdom kingdom, CuriaAgenda agenda, string reason)
        {
            agenda.Status = CuriaAgendaStatus.Failed;
            agenda.FailureReason = reason;
            agenda.Outcome = string.IsNullOrEmpty(agenda.Outcome) ? "执行失败" : agenda.Outcome;
            agenda.ResolvedLabel = CuriaStore.CurrentLabel;
            CuriaStore.SaveAgenda(agenda, writeText: true);
            CuriaStore.RecordHistory("curia_failed",
                $"{kingdom.Name}朝议「{CuriaText.TypeLabel(agenda.Type)}：{CuriaText.TargetSummary(agenda)}」未能执行：{reason}");
            if (ShouldNotifyPlayer(kingdom))
                CuriaStore.Notify($"朝议「{CuriaText.TypeLabel(agenda.Type)}」未能执行：{reason}", Colors.Red);
            FinishAgenda(kingdom, agenda, passed: false);
        }

        private static void FinishAgenda(Kingdom kingdom, CuriaAgenda agenda, bool passed)
        {
            var state = GetState(kingdom);
            state.ActiveAgendaId = "";
            state.LastResolvedDay = CuriaStore.Today;
            CuriaStore.SaveState(state);
            _activeAgendas.Remove(kingdom.StringId ?? kingdom.Name?.ToString() ?? "");

            var resultText = agenda.Status == CuriaAgendaStatus.Applied
                ? "通过"
                : agenda.Status == CuriaAgendaStatus.Failed
                    ? "通过但未能执行"
                    : "未通过";
            var target = CuriaText.TargetSummary(agenda);
            var outcome = string.IsNullOrWhiteSpace(agenda.Outcome) ? resultText : agenda.Outcome;

            // 参与者日记：投票者、国王、提议者
            foreach (var v in agenda.Votes)
            {
                if (string.IsNullOrEmpty(v.LeaderEntityId)) continue;
                CuriaStore.AppendDiary(v.LeaderEntityId, "结果",
                    $"朝议「{CuriaText.TypeLabel(agenda.Type)}：{target}」{resultText}。{outcome}");
            }
            // 被针对家族
            var targetClan = Clan.All.FirstOrDefault(c => c.StringId == agenda.TargetClanId);
            if (targetClan?.Leader != null)
            {
                var tid = CuriaStore.EntityIdOf(targetClan.Leader);
                if (!string.IsNullOrEmpty(tid))
                    CuriaStore.AppendDiary(tid, "结果", $"朝议「{CuriaText.TypeLabel(agenda.Type)}：{target}」{resultText}。{outcome}");
            }
            // 未出席者：叙事合理化
            foreach (var a in agenda.Absent)
            {
                var clan = Clan.All.FirstOrDefault(c => c.StringId == a.ClanId);
                var leader = clan?.Leader;
                if (leader == null) continue;
                var eid = CuriaStore.EntityIdOf(leader);
                if (string.IsNullOrEmpty(eid)) continue;
                var absence = CuriaPageBuilder.BuildAbsenceReason(leader, kingdom);
                CuriaStore.AppendDiary(eid, "情报",
                    $"朝议「{CuriaText.TypeLabel(agenda.Type)}：{target}」我因{absence}未能出席；结果{resultText}。");
            }

            if (ShouldNotifyPlayer(kingdom))
            {
                CuriaStore.Notify($"朝议结果：{CuriaText.TypeLabel(agenda.Type)}（{target}）{resultText}。", passed ? Colors.Green : Colors.Yellow);
                CuriaStore.Notify("按 M 查看朝议记录。", Colors.Cyan);
            }

            CuriaStore.PruneAgendas(agenda.KingdomName, MySettings.Instance?.CuriaHistoryKeep ?? 200);
        }
    }
}
