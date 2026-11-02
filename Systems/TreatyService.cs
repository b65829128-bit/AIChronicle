using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;

namespace AIChronicle
{
    /// <summary>
    /// 条约的校验、执行、入史与日记。所有方法在主线程调用。
    /// 结构：朝议授权 -> DiplomacyService 发出 .proposal -> 对方国王接受/拒绝 -> 这里执行。
    /// </summary>
    public static class TreatyService
    {
        public const int DefaultTruceDays = 60;
        public const int MaxTributeAmount = 100000;
        public const int MaxTributeDays = 3600;
        public const int MaxTruceDays = 720;

        public class ValidatedCession
        {
            public TreatyCession Term { get; set; } = new TreatyCession();
            public Settlement? Settlement { get; set; }
            public Clan? OwnerClan { get; set; }
            public Hero? ReceivingHero { get; set; }
            public Kingdom? CedingKingdom { get; set; }
        }

        public class ValidationResult
        {
            public bool Ok { get; set; }
            public string Error { get; set; } = "";
            public TreatyTerms Terms { get; set; } = new TreatyTerms();
            public List<ValidatedCession> Cessions { get; set; } = new List<ValidatedCession>();
        }

        /// <summary>从 propose_agenda 的可选参数构造条款；非议和/战前条约返回 null。</summary>
        public static TreatyTerms? ParseToolTerms(string type, string? settlementName, string? cedeFrom,
            int tributeAmount, int tributeDays, int? truceDays, bool? warOnReject,
            IReadOnlyList<TreatyCession>? cessions = null)
        {
            if (!CuriaText.TryParseType(type, out var agendaType)) return null;
            if (agendaType != CuriaAgendaType.ProposePeace && agendaType != CuriaAgendaType.ProposeTreaty)
                return null;

            var isTreaty = agendaType == CuriaAgendaType.ProposeTreaty;
            var terms = new TreatyTerms
            {
                SettlementName = (settlementName ?? "").Trim(),
                CedeFrom = TreatyText.NormalizeCedeFrom(cedeFrom),
                TributeAmount = tributeAmount,
                TributeDays = tributeDays,
                TruceDays = truceDays ?? (isTreaty ? DefaultTruceDays : 0),
                WarOnReject = isTreaty && (warOnReject ?? true)
            };

            if (cessions != null)
            {
                foreach (var c in cessions)
                {
                    if (c == null) continue;
                    var name = (c.SettlementName ?? "").Trim();
                    var id = (c.SettlementId ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(id)) continue;
                    var side = TreatyText.NormalizeCedeFrom(c.CedeFrom);
                    if (string.IsNullOrEmpty(side) && isTreaty) side = "target";
                    terms.Cessions.Add(new TreatyCession
                    {
                        SettlementId = id,
                        SettlementName = name,
                        CedeFrom = side
                    });
                }
                terms.SettlementId = "";
                terms.SettlementName = "";
                terms.CedeFrom = "";
            }
            else
            {
                if (terms.HasCession() && string.IsNullOrEmpty(terms.CedeFrom))
                    terms.CedeFrom = isTreaty ? "target" : "";
                if (!terms.HasCession())
                {
                    terms.SettlementName = "";
                    terms.SettlementId = "";
                }
            }
            return terms;
        }

        /// <summary>解析 AI 工具的多割地简写：side:城名;side:城名（side = proposer/target，支持我方/对方）。</summary>
        public static List<TreatyCession> ParseCessionSpec(string? text)
        {
            return TreatyText.ParseCessionSpec(text);
        }

        /// <summary>校验并归一化条款：解析封地、检查归属/提议权限、夹取赔款与休战天数。</summary>
        public static ValidationResult Validate(Kingdom? proposer, Kingdom? target, TreatyTerms? input,
            bool preWar, Hero? proposerHero)
        {
            var result = new ValidationResult();
            if (proposer == null || target == null)
            {
                result.Error = "王国不存在";
                return result;
            }
            if (proposer == target)
            {
                result.Error = "不能以自己为目标";
                return result;
            }
            if (target.IsEliminated)
            {
                result.Error = "目标王国已灭亡";
                return result;
            }
            if (proposer.IsAtWarWith(target) && preWar)
            {
                result.Error = "战前条约只能对未交战王国提出；已交战请用议和";
                return result;
            }
            if (!proposer.IsAtWarWith(target) && !preWar)
            {
                result.Error = "议和只能对正在交战的王国提出";
                return result;
            }

            var terms = (input ?? new TreatyTerms()).Clone();
            terms.TributeAmount = Math.Max(-MaxTributeAmount, Math.Min(MaxTributeAmount, terms.TributeAmount));
            terms.TributeDays = Math.Max(0, Math.Min(MaxTributeDays, terms.TributeDays));
            terms.TruceDays = Math.Max(0, Math.Min(MaxTruceDays, terms.TruceDays));
            terms.WarOnReject = preWar && terms.WarOnReject;
            if (terms.TributeAmount == 0)
            {
                terms.TributeDays = 0;
            }
            else if (terms.TributeDays <= 0)
            {
                result.Error = "赔款必须填写持续天数（至少 1 天）";
                return result;
            }

            if (!preWar) terms.WarOnReject = false;

            var legacySide = terms.CedeFrom;
            terms.MigrateLegacy();
            var requestedCessions = terms.GetCessions();
            if (requestedCessions.Count > 0)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var normalizedCessions = new List<TreatyCession>();
                foreach (var requested in requestedCessions)
                {
                    var side = TreatyText.NormalizeCedeFrom(requested.CedeFrom);
                    if (side != "proposer" && side != "target")
                    {
                        result.Error = "每块割地都必须指定割让方：我方（proposer）或对方（target）";
                        return result;
                    }
                    var settlement = CuriaStore.FindSettlement(
                        !string.IsNullOrWhiteSpace(requested.SettlementId) ? requested.SettlementId : requested.SettlementName);
                    if (settlement == null || (!settlement.IsTown && !settlement.IsCastle))
                    {
                        result.Error = "未找到可割让的城镇/城堡：" +
                                       (string.IsNullOrWhiteSpace(requested.SettlementName) ? requested.SettlementId : requested.SettlementName);
                        return result;
                    }
                    var dedupeKey = settlement.StringId ?? settlement.Name?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(dedupeKey) && !seen.Add(dedupeKey))
                    {
                        result.Error = $"「{settlement.Name}」在条约中被重复割让";
                        return result;
                    }
                    var ownerClan = settlement.OwnerClan;
                    if (ownerClan == null)
                    {
                        result.Error = $"「{settlement.Name}」没有归属家族";
                        return result;
                    }
                    var cedingKingdom = side == "proposer" ? proposer : target;
                    if (ownerClan.Kingdom != cedingKingdom)
                    {
                        result.Error = $"「{settlement.Name}」不属于{(side == "proposer" ? "我方" : "对方")}王国";
                        return result;
                    }
                    if (CuriaStore.IsMercenary(ownerClan))
                    {
                        result.Error = "雇佣兵家族不能持有或割让封地";
                        return result;
                    }
                    if (side == "proposer")
                    {
                        var king = proposer.RulingClan?.Leader;
                        var isKing = proposerHero != null && king != null && proposerHero == king;
                        if (!isKing && (proposerHero == null || ownerClan != proposerHero.Clan))
                        {
                            result.Error = "只有国王，或该封地所属家族领袖，才能提议割让我方封地";
                            return result;
                        }
                    }
                    else
                    {
                        var targetKing = target.RulingClan?.Leader;
                        if (targetKing == null || !targetKing.IsAlive)
                        {
                            result.Error = "对方王国没有在世统治者，无法接受割地条款";
                            return result;
                        }
                    }

                    var receivingKingdom = side == "proposer" ? target : proposer;
                    var receivingHero = receivingKingdom.RulingClan?.Leader;
                    if (receivingHero == null || !receivingHero.IsAlive || receivingHero.Clan == null)
                    {
                        result.Error = $"{(side == "proposer" ? "对方" : "我方")}王国没有可接收封地的在世统治者";
                        return result;
                    }

                    var term = new TreatyCession
                    {
                        SettlementId = settlement.StringId ?? "",
                        SettlementName = settlement.Name?.ToString() ?? "",
                        CedeFrom = side
                    };
                    normalizedCessions.Add(term);
                    result.Cessions.Add(new ValidatedCession
                    {
                        Term = term,
                        Settlement = settlement,
                        OwnerClan = ownerClan,
                        ReceivingHero = receivingHero,
                        CedingKingdom = cedingKingdom
                    });
                }
                terms.Cessions = normalizedCessions;
            }
            else
            {
                if (!string.IsNullOrEmpty(legacySide))
                {
                    result.Error = "割地条款缺少封地名称";
                    return result;
                }
                terms.Cessions.Clear();
            }

            if (preWar)
            {
                if (TreatyStore.FindActiveTruce(proposer, target) is { } truce)
                {
                    result.Error = $"你与{target.Name}签有休战条约（至第{truce.TruceUntilDay}日），不能提出战前条约";
                    return result;
                }
                if (!terms.HasConcession())
                {
                    result.Error = "战前条约至少要有割地或赔款；无条件和平请用议和提议";
                    return result;
                }
            }

            result.Ok = true;
            result.Terms = terms;
            return result;
        }

        /// <summary>从 .proposal 文件里解析结构化条款；兼容旧文件的 tribute= 行。</summary>
        public static TreatyTerms? ParseProposalTerms(string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;
            TreatyTerms? fallback = null;
            foreach (var line in content!.Split('\n'))
            {
                if (line.StartsWith("terms=", StringComparison.Ordinal))
                {
                    try
                    {
                        var terms = JsonConvert.DeserializeObject<TreatyTerms>(line.Substring(6));
                        if (terms != null) return terms;
                    }
                    catch { }
                }
                else if (line.StartsWith("tribute=", StringComparison.Ordinal))
                {
                    var parts = line.Substring(8).Split('_');
                    if (parts.Length == 2
                        && int.TryParse(parts[0], out var amount)
                        && int.TryParse(parts[1], out var days))
                    {
                        fallback = new TreatyTerms { TributeAmount = amount, TributeDays = days };
                    }
                }
            }
            return fallback;
        }

        /// <summary>接受一份带条款的和平条约：停战 -> 赔款（由 MakePeaceAction 施加）-> 割地 -> 记录。</summary>
        public static string AcceptPeace(Kingdom proposer, Kingdom target, TreatyTerms terms, string proposalId)
        {
            var validation = Validate(proposer, target, terms, preWar: false, proposer.RulingClan?.Leader);
            if (!validation.Ok)
                return "[错误] 条约条款失效：" + validation.Error;
            if (!proposer.IsAtWarWith(target))
                return "[错误] 当前并未与该王国交战";

            DiplomacyService.ApplyPeaceWithTribute(proposer, target,
                validation.Terms.TributeAmount, validation.Terms.TributeDays);

            var cessionError = ApplyCessions(proposer, target, validation);
            if (!string.IsNullOrEmpty(cessionError))
            {
                HistoryRecorder.RecordDiplomacyEvent("treaty_failed",
                    $"{proposer.Name}与{target.Name}的和平条约未能完成割地：{cessionError}");
                return "[警告] 停战已生效，但割地失败：" + cessionError;
            }

            RecordSigned(proposer, target, validation.Terms, "peace", proposalId);
            return $"已与{target.Name}签订和平条约：{TreatyText.DescribeNeutral(validation.Terms, proposer.Name?.ToString() ?? "", target.Name?.ToString() ?? "")}。";
        }

        /// <summary>接受一份战前条约：若已开战则按和平条约处理；否则只施加赔款/割地，不宣战。</summary>
        public static string AcceptTreaty(Kingdom proposer, Kingdom target, TreatyTerms terms, string proposalId)
        {
            var atWar = proposer.IsAtWarWith(target);
            var validation = Validate(proposer, target, terms, preWar: !atWar, proposer.RulingClan?.Leader);
            if (!validation.Ok)
                return "[错误] 条约条款失效：" + validation.Error;

            if (atWar)
                DiplomacyService.ApplyPeaceWithTribute(proposer, target,
                    validation.Terms.TributeAmount, validation.Terms.TributeDays);
            else
                ApplyTributeAtPeace(proposer, target, validation.Terms);

            var cessionError = ApplyCessions(proposer, target, validation);
            if (!string.IsNullOrEmpty(cessionError))
            {
                HistoryRecorder.RecordDiplomacyEvent("treaty_failed",
                    $"{proposer.Name}与{target.Name}的战前条约未能完成割地：{cessionError}");
                return "[警告] " + (atWar ? "停战已生效，但割地失败：" : "赔款已生效，但割地失败：") + cessionError;
            }

            RecordSigned(proposer, target, validation.Terms, "ultimatum", proposalId);
            return $"已与{target.Name}签订战前条约：{TreatyText.DescribeNeutral(validation.Terms, proposer.Name?.ToString() ?? "", target.Name?.ToString() ?? "")}。";
        }

        public static void ApplyTributeAtPeace(Kingdom proposer, Kingdom target, TreatyTerms terms)
        {
            if (terms == null || !terms.HasTribute()) return;
            try
            {
                var payer = terms.TributeAmount > 0 ? (IFaction)proposer : target;
                var stance = proposer.GetStanceWith(target);
                if (stance != null)
                {
                    // 新条约的赔款取代旧赔款：清零已付累计，否则剩余期数会按旧账计算。
                    stance.TotalTributePaidFrom1To2 = 0;
                    stance.SetDailyTributePaid(payer, Math.Abs(terms.TributeAmount), terms.TributeDays);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("战前条约赔款施加失败：" + ex);
            }
        }

        private static string ApplyCessions(Kingdom proposer, Kingdom target, ValidationResult validation)
        {
            if (validation.Cessions.Count == 0) return "";

            // 先整体重校验，避免割到一半才发现某块地已易主。
            foreach (var v in validation.Cessions)
            {
                if (v.Settlement == null || v.OwnerClan == null || v.ReceivingHero == null || v.CedingKingdom == null)
                    return "割地目标缺失";
                if (v.Settlement.OwnerClan != v.OwnerClan)
                    return $"「{v.Settlement.Name}」已易主";
                if (v.OwnerClan.Kingdom != v.CedingKingdom)
                    return $"「{v.Settlement.Name}」已不在原王国";
            }

            var lostFiefs = new Dictionary<Hero, List<string>>();
            var receivedFiefs = new Dictionary<Kingdom, List<Settlement>>();
            foreach (var v in validation.Cessions)
            {
                var settlement = v.Settlement!;
                var ownerClan = v.OwnerClan!;
                var receivingHero = v.ReceivingHero!;
                var cedingKingdom = v.CedingKingdom!;
                var receivingKingdom = v.Term.CedeFrom == "proposer" ? target : proposer;
                var oldLeader = ownerClan.Leader;
                var oldClanName = ownerClan.Name?.ToString() ?? "?";
                var newClanName = receivingHero.Clan?.Name?.ToString() ?? "?";
                var fiefName = settlement.Name?.ToString() ?? "?";
                HistoryRecorder.PendingTreatyCessionText =
                    $"条约割让：{fiefName}从{cedingKingdom.Name}的{oldClanName}转归{receivingKingdom.Name}的{newClanName}";
                try
                {
                    ChangeOwnerOfSettlementAction.ApplyByBarter(receivingHero, settlement);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log("条约割地失败：" + ex);
                    QueueFiefDistribution(receivedFiefs);
                    return ex.Message;
                }
                finally
                {
                    HistoryRecorder.PendingTreatyCessionText = null;
                }

                if (!receivedFiefs.TryGetValue(receivingKingdom, out var got))
                {
                    got = new List<Settlement>();
                    receivedFiefs[receivingKingdom] = got;
                }
                got.Add(settlement);

                // 割让封臣的封地：该封臣会记恨（与国王夺封同款 FiefReview）；同一家族多块地合并成一次激活。
                if (oldLeader != null && oldLeader != cedingKingdom.RulingClan?.Leader && oldLeader.IsAlive)
                {
                    if (!lostFiefs.TryGetValue(oldLeader, out var names))
                    {
                        names = new List<string>();
                        lostFiefs[oldLeader] = names;
                    }
                    names.Add($"「{fiefName}」");
                }
            }

            foreach (var kv in lostFiefs)
            {
                var entityId = CuriaStore.EntityIdOf(kv.Key);
                if (string.IsNullOrEmpty(entityId)) continue;
                var content = $"你的国王在条约中把 {string.Join("、", kv.Value)} 割让给了对方；你失去了这些封地。";
                AgentScheduler.QueueFiefReview(entityId, content);
            }

            QueueFiefDistribution(receivedFiefs);
            return "";
        }

        private static void QueueFiefDistribution(Dictionary<Kingdom, List<Settlement>> receivedFiefs)
        {
            foreach (var kv in receivedFiefs)
            {
                if (kv.Value.Count == 0) continue;
                var names = string.Join("、", kv.Value.Select(s => s.Name?.ToString() ?? "?"));
                FiefAssignmentPatch.QueueKingFiefDecision(kv.Key, kv.Value,
                    $"条约割地已执行，{kv.Key.Name?.ToString() ?? "王国"}获得{names}。");
            }
        }

        private static void RecordSigned(Kingdom proposer, Kingdom target, TreatyTerms terms, string kind, string proposalId)
        {
            var proposerName = proposer.Name?.ToString() ?? "?";
            var targetName = target.Name?.ToString() ?? "?";
            var proposerRuler = proposer.RulingClan?.Leader?.Name?.ToString() ?? "?";
            var targetRuler = target.RulingClan?.Leader?.Name?.ToString() ?? "?";

            var record = new TreatyRecord
            {
                Id = DateTime.UtcNow.Ticks.ToString("X") + "_" + Math.Abs((proposer.StringId ?? proposerName).GetHashCode()).ToString("X4"),
                Kind = kind,
                ProposerKingdomId = proposer.StringId ?? "",
                ProposerKingdomName = proposerName,
                TargetKingdomId = target.StringId ?? "",
                TargetKingdomName = targetName,
                ProposerRulerName = proposerRuler,
                TargetRulerName = targetRuler,
                Terms = terms.Clone(),
                SignedDay = CuriaStore.Today,
                SignedLabel = CuriaStore.CurrentLabel,
                TruceUntilDay = terms.TruceDays > 0 ? CuriaStore.Today + terms.TruceDays : 0,
                Status = "active",
                ProposalId = proposalId ?? ""
            };
            TreatyStore.Add(record);

            var summary = TreatyText.SignedSummary(proposerName, targetName, terms);
            HistoryRecorder.RecordDiplomacyEvent("treaty_signed", summary);
            AppendTreatyDiary(proposer, target, "结果", summary);

            if (IsPlayerKingdom(proposer) || IsPlayerKingdom(target))
                CuriaStore.Notify($"[条约] {summary}", TaleWorlds.Library.Colors.Green);
        }

        public static string HandleRejected(Kingdom proposer, Kingdom target, TreatyTerms? terms, bool warOnReject, string proposalId)
        {
            var proposerName = proposer.Name?.ToString() ?? "?";
            var targetName = target.Name?.ToString() ?? "?";
            var summary = TreatyText.RejectedSummary(proposerName, targetName, terms);
            HistoryRecorder.RecordDiplomacyEvent("treaty_rejected", summary);
            AppendTreatyDiary(proposer, target, "情报", summary);

            var warNote = "";
            if (warOnReject)
            {
                if (proposer.IsAtWarWith(target))
                {
                    warNote = "（双方已在交战）";
                }
                else if (TreatyStore.FindActiveTruce(proposer, target) is { } truce)
                {
                    warNote = $"（休战条约至第{truce.TruceUntilDay}日，未自动宣战）";
                }
                else
                {
                    var ruler = proposer.RulingClan?.Leader;
                    if (ruler != null && ruler.IsAlive)
                    {
                        var prev = AIChatClient.CurrentHero;
                        AIChatClient.CurrentHero = ruler;
                        try
                        {
                            var result = DiplomacyService.ExecuteDeclareWar(targetName, $"最后通牒被{targetName}拒绝");
                            if (result.StartsWith("[错误]", StringComparison.Ordinal))
                                warNote = "（自动宣战失败：" + result + "）";
                            else
                            {
                                warNote = "最后通牒被拒，战争爆发";
                                AppendTreatyDiary(proposer, target, "结果",
                                    $"{proposerName}对{targetName}宣战：最后通牒被拒");
                            }
                        }
                        finally
                        {
                            AIChatClient.CurrentHero = prev;
                        }
                    }
                }
            }
            if (IsPlayerKingdom(proposer) || IsPlayerKingdom(target))
                CuriaStore.Notify($"[条约] {summary}{warNote}", TaleWorlds.Library.Colors.Yellow);
            return warNote;
        }

        /// <summary>开战时调用：若双方有休战条约，标记撕毁并写入史料/双方家族领袖日记。</summary>
        public static void OnWarDeclared(Kingdom? attacker, Kingdom? defender)
        {
            if (attacker == null || defender == null) return;
            var record = TreatyStore.FindActiveTruce(attacker, defender);
            if (record == null) return;

            TreatyStore.MarkBroken(record, attacker.Name?.ToString() ?? "");
            var a = attacker.Name?.ToString() ?? "?";
            var b = defender.Name?.ToString() ?? "?";
            var summary = $"{a}撕毁了与{b}的休战条约，向{b}宣战";
            HistoryRecorder.RecordDiplomacyEvent("treaty_broken", summary);
            AppendTreatyDiary(attacker, defender, "情报", summary);
            if (IsPlayerKingdom(attacker) || IsPlayerKingdom(defender))
                CuriaStore.Notify($"[条约] {summary}", TaleWorlds.Library.Colors.Red);
        }

        /// <summary>把条约结果写进双方主要封臣（家族领袖）与国王的日记。</summary>
        public static void AppendTreatyDiary(Kingdom a, Kingdom b, string type, string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void WriteKingdom(Kingdom? k)
            {
                if (k == null) return;
                foreach (var clan in k.Clans)
                {
                    if (clan == null || clan.IsEliminated) continue;
                    if (clan.IsUnderMercenaryService || clan.IsClanTypeMercenary) continue;
                    if (clan.IsMinorFaction || clan.IsBanditFaction) continue;
                    var leader = clan.Leader;
                    if (leader == null || !leader.IsAlive) continue;
                    Write(leader);
                }
                var king = k.RulingClan?.Leader;
                if (king != null && king.IsAlive) Write(king);
            }
            void Write(Hero leader)
            {
                var id = CuriaStore.EntityIdOf(leader);
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) return;
                CuriaStore.AppendDiary(id, type, content);
            }
            WriteKingdom(a);
            WriteKingdom(b);
        }

        private static bool IsPlayerKingdom(Kingdom k)
        {
            var player = Hero.MainHero;
            return player != null && (player.Clan?.Kingdom == k || player.MapFaction == k);
        }
    }
}
