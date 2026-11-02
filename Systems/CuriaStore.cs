using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace AIChronicle
{
    /// <summary>
    /// 朝议的持久化与外围辅助：World/curia/{王国}/ 下的 state.json + agenda_*.json + agenda_*.txt。
    /// 所有方法都在主线程调用（工具/调度器/CEF 动作经 MainThreadExecutor）。
    /// </summary>
    public static class CuriaStore
    {
        public static readonly JsonSerializerSettings JsonSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            Converters = new List<JsonConverter> { new Newtonsoft.Json.Converters.StringEnumConverter() }
        };

        public static string CuriaRoot
        {
            get
            {
                var baseDir = PromptManager.CampaignDir;
                if (string.IsNullOrEmpty(baseDir))
                    baseDir = PromptManager.PromptsBaseDir;
                var dir = Path.Combine(baseDir, "NPCs", "World", "curia");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string KingdomDir(string kingdomName)
        {
            var dir = Path.Combine(CuriaRoot, SanitizeDir(kingdomName));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string SanitizeDir(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unknown";
            foreach (var c in Path.GetInvalidPathChars()) name = name.Replace(c, '_');
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        public static CuriaState LoadState(Kingdom kingdom, bool createIfMissing = true)
        {
            var state = new CuriaState
            {
                KingdomId = kingdom?.StringId ?? "",
                KingdomName = kingdom?.Name?.ToString() ?? ""
            };
            if (kingdom == null) return state;

            try
            {
                var path = Path.Combine(KingdomDir(state.KingdomName), "state.json");
                if (File.Exists(path))
                {
                    var json = SafeFileIO.ReadAllText(path);
                    var loaded = JsonConvert.DeserializeObject<CuriaState>(json, JsonSettings);
                    if (loaded != null)
                    {
                        loaded.KingdomId = state.KingdomId;
                        loaded.KingdomName = state.KingdomName;
                        loaded.LastSummonedDay ??= new Dictionary<string, int>();
                        loaded.ProposerCooldownDay ??= new Dictionary<string, int>();
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("朝议状态读取失败：" + ex.Message);
            }

            if (createIfMissing) SaveState(state);
            return state;
        }

        public static void SaveState(CuriaState state)
        {
            if (state == null || string.IsNullOrEmpty(state.KingdomName)) return;
            try
            {
                var path = Path.Combine(KingdomDir(state.KingdomName), "state.json");
                SafeFileIO.WriteAllText(path, JsonConvert.SerializeObject(state, JsonSettings));
            }
            catch (Exception ex)
            {
                DebugLogger.Log("朝议状态写入失败：" + ex.Message);
            }
        }

        public static CuriaAgenda? LoadAgenda(string kingdomName, string agendaId)
        {
            if (string.IsNullOrEmpty(kingdomName) || string.IsNullOrEmpty(agendaId)) return null;
            try
            {
                var path = Path.Combine(KingdomDir(kingdomName), "agenda_" + SanitizeDir(agendaId) + ".json");
                if (!File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<CuriaAgenda>(SafeFileIO.ReadAllText(path), JsonSettings);
            }
            catch (Exception ex)
            {
                DebugLogger.Log("朝议议程读取失败：" + ex.Message);
                return null;
            }
        }

        public static List<CuriaAgenda> ListAgendas(string kingdomName, int max = 30)
        {
            var result = new List<CuriaAgenda>();
            if (string.IsNullOrEmpty(kingdomName)) return result;
            try
            {
                var dir = KingdomDir(kingdomName);
                if (!Directory.Exists(dir)) return result;
                foreach (var file in Directory.GetFiles(dir, "agenda_*.json")
                             .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                             .Take(max))
                {
                    try
                    {
                        var agenda = JsonConvert.DeserializeObject<CuriaAgenda>(SafeFileIO.ReadAllText(file), JsonSettings);
                        if (agenda != null) result.Add(agenda);
                    }
                    catch { }
                }
                result.Sort((a, b) => b.CreatedDay.CompareTo(a.CreatedDay));
            }
            catch (Exception ex)
            {
                DebugLogger.Log("朝议议程列表失败：" + ex.Message);
            }
            return result;
        }

        public static void SaveAgenda(CuriaAgenda agenda, bool writeText = true)
        {
            if (agenda == null || string.IsNullOrEmpty(agenda.KingdomName)) return;
            try
            {
                var dir = KingdomDir(agenda.KingdomName);
                var jsonPath = Path.Combine(dir, "agenda_" + SanitizeDir(agenda.Id) + ".json");
                SafeFileIO.WriteAllText(jsonPath, JsonConvert.SerializeObject(agenda, JsonSettings));
                if (writeText) WriteAgendaText(agenda, Path.Combine(dir, "agenda_" + SanitizeDir(agenda.Id) + ".txt"));
            }
            catch (Exception ex)
            {
                DebugLogger.Log("朝议议程写入失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 每个王国只保留最近 keep 条已结束朝议的 JSON/TXT，避免长期存档无限积累。
        /// 只删除 Rejected/Applied/Failed/Cancelled；活动中的 Proposed/Voting/Passed 永不清理。
        /// </summary>
        public static void PruneAgendas(string kingdomName, int keep)
        {
            if (string.IsNullOrEmpty(kingdomName) || keep <= 0 || string.IsNullOrEmpty(PromptManager.CampaignDir)) return;
            try
            {
                var dir = KingdomDir(kingdomName);
                if (!Directory.Exists(dir)) return;
                var files = Directory.GetFiles(dir, "agenda_*.json")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();
                if (files.Count <= keep) return;

                foreach (var file in files.Skip(keep))
                {
                    try
                    {
                        var agenda = JsonConvert.DeserializeObject<CuriaAgenda>(
                            SafeFileIO.ReadAllText(file.FullName), JsonSettings);
                        if (agenda == null) continue;
                        if (agenda.Status != CuriaAgendaStatus.Rejected
                            && agenda.Status != CuriaAgendaStatus.Applied
                            && agenda.Status != CuriaAgendaStatus.Failed
                            && agenda.Status != CuriaAgendaStatus.Cancelled)
                            continue;

                        File.Delete(file.FullName);
                        var txt = Path.ChangeExtension(file.FullName, ".txt");
                        if (File.Exists(txt)) File.Delete(txt);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("朝议记录清理失败：" + ex.Message);
            }
        }

        private static void WriteAgendaText(CuriaAgenda a, string path)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"# 朝议 · {a.KingdomName}");
                sb.AppendLine();
                sb.AppendLine($"议题：{CuriaText.TypeLabel(a.Type)}");
                sb.AppendLine($"状态：{CuriaText.StatusLabel(a.Status)}");
                sb.AppendLine($"提议者：{a.ProposerName}");
                sb.AppendLine($"提出：{a.CreatedLabel}    截止：{a.DeadlineDay} 日");
                sb.AppendLine($"目标：{CuriaText.TargetSummary(a)}");
                sb.AppendLine($"理由：{a.Reason}");
                if (!string.IsNullOrEmpty(a.Terms)) sb.AppendLine($"条款：{a.Terms}");
                if (!string.IsNullOrEmpty(a.PolicyDescription)) sb.AppendLine($"政策说明：{a.PolicyDescription}");
                if (!string.IsNullOrEmpty(a.PolicyEffect)) sb.AppendLine($"政策效果：{a.PolicyEffect}");
                if (!string.IsNullOrEmpty(a.TargetDefense))
                    sb.AppendLine($"陈情（{a.TargetDefenseBy}）：{a.TargetDefense}");
                sb.AppendLine();
                sb.AppendLine("====== 表决 ======");
                sb.AppendLine($"门槛 {a.Threshold:P0}    quorum {a.QuorumWeight:F1}/{a.TotalWeight:F1}    应召权重 {a.SummonedWeight:F1}");
                sb.AppendLine($"支持 {a.SupportWeight:F1}    反对 {a.OpposeWeight:F1}    弃权 {a.AbstainWeight:F1}");
                sb.AppendLine();
                foreach (var v in a.Votes)
                {
                    var stance = v.Stance.HasValue ? CuriaText.StanceLabel(v.Stance.Value) : "未表态";
                    var reason = string.IsNullOrWhiteSpace(v.Reason) ? "" : $"：{v.Reason}";
                    sb.AppendLine($"- {v.ClanName}（{v.LeaderName}，权重 {v.Weight:F1}，{v.SummonReason}）{stance}{reason}");
                }
                foreach (var v in a.Absent)
                {
                    sb.AppendLine($"- {v.ClanName}（{v.LeaderName}）：未出席（{v.Reason}）");
                }
                sb.AppendLine();
                sb.AppendLine("====== 结果 ======");
                sb.AppendLine(string.IsNullOrEmpty(a.Outcome) ? "（表决中）" : a.Outcome);
                if (!string.IsNullOrEmpty(a.EffectResult)) sb.AppendLine($"执行：{a.EffectResult}");
                if (!string.IsNullOrEmpty(a.FailureReason)) sb.AppendLine($"失效原因：{a.FailureReason}");
                SafeFileIO.WriteAllText(path, sb.ToString());
            }
            catch (Exception ex)
            {
                DebugLogger.Log("朝议文本写入失败：" + ex.Message);
            }
        }

        public static void AppendDiary(string entityId, string type, string content)
        {
            AgentManager.AppendSystemDiary(entityId, type, content);
        }

        public static void RecordHistory(string eventType, string summary)
        {
            HistoryRecorder.RecordCuriaEvent(eventType, summary);
        }

        public static void Notify(string message, Color color)
        {
            try
            {
                MainThreadExecutor.DisplayMessage(new InformationMessage(message, color));
            }
            catch { }
        }

        public static int Today => (int)CampaignTime.Now.ToDays;

        public static string CurrentLabel => PromptManager.GetCurrentTimeString();

        public static Kingdom? FindKingdom(string nameOrId)
        {
            if (string.IsNullOrWhiteSpace(nameOrId)) return null;
            var trimmed = nameOrId.Trim();
            foreach (var k in Kingdom.All)
            {
                if (k.StringId != null && string.Equals(k.StringId, trimmed, StringComparison.OrdinalIgnoreCase))
                    return k;
            }
            return DiplomacyService.FindKingdom(trimmed);
        }

        public static Clan? FindClan(string nameOrId)
        {
            if (string.IsNullOrWhiteSpace(nameOrId)) return null;
            var trimmed = nameOrId.Trim();
            foreach (var c in Clan.All)
            {
                if (c.StringId != null && string.Equals(c.StringId, trimmed, StringComparison.OrdinalIgnoreCase))
                    return c;
            }
            foreach (var c in Clan.All)
            {
                var name = c.Name?.ToString() ?? "";
                if (name == trimmed || name.Contains(trimmed) || trimmed.Contains(name))
                    return c;
            }
            return null;
        }

        public static Settlement? FindSettlement(string nameOrId)
        {
            if (string.IsNullOrWhiteSpace(nameOrId)) return null;
            var trimmed = nameOrId.Trim();
            foreach (var s in Settlement.All)
            {
                if (s.StringId != null && string.Equals(s.StringId, trimmed, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
            foreach (var s in Settlement.All)
            {
                var name = s.Name?.ToString() ?? "";
                if (name == trimmed) return s;
            }
            foreach (var s in Settlement.All)
            {
                var name = s.Name?.ToString() ?? "";
                if (name.Contains(trimmed) || trimmed.Contains(name)) return s;
            }
            return null;
        }

        public static Hero? FindHeroByEntityId(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId)) return null;
            var entity = EntityManager.GetEntityById(entityId);
            if (entity?.HeroRef != null) return entity.HeroRef;
            var resolved = EntityManager.ResolveEntityId(entityId);
            entity = resolved != null ? EntityManager.GetEntityById(resolved) : null;
            if (entity?.HeroRef != null) return entity.HeroRef;
            foreach (var hero in Hero.AllAliveHeroes)
            {
                if (hero.StringId != null && string.Equals(hero.StringId, entityId, StringComparison.OrdinalIgnoreCase))
                    return hero;
                if (hero.Name?.ToString() == entityId) return hero;
            }
            return null;
        }

        public static bool IsMercenary(Clan? clan)
        {
            return clan != null && (clan.IsUnderMercenaryService || clan.IsClanTypeMercenary);
        }

        public static bool IsVassalClan(Clan? clan, Kingdom? kingdom)
        {
            if (clan == null || kingdom == null) return false;
            if (clan.Kingdom != kingdom) return false;
            if (IsMercenary(clan)) return false;
            if (clan.IsMinorFaction || clan.IsBanditFaction) return false;
            if (clan.IsEliminated) return false;
            return true;
        }

        public static Kingdom? GetHeroKingdom(Hero? hero)
        {
            if (hero == null) return null;
            if (hero.Clan?.Kingdom != null) return hero.Clan.Kingdom;
            return hero.MapFaction as Kingdom;
        }

        public static Hero? GetKing(Kingdom? kingdom)
        {
            var king = kingdom?.RulingClan?.Leader;
            return king != null && king.IsAlive ? king : null;
        }

        public static string EntityIdOf(Hero? hero)
        {
            if (hero == null) return "";
            var entity = EntityManager.GetOrCreateEntity(hero);
            return entity?.Id ?? "";
        }

        public static float WeightOf(Clan clan)
        {
            try
            {
                var w = Campaign.Current?.Models?.DiplomacyModel?.GetClanStrength(clan) ?? 0f;
                return Math.Max(1f, (float)Math.Round(w, 1));
            }
            catch
            {
                return 1f;
            }
        }

        public static string ReadSecretAdvisoryText(Kingdom? kingdom, int maxChars = 6000)
        {
            if (kingdom == null) return "";
            try
            {
                var path = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "secret_advisory",
                    $"{kingdom.Name}_{CampaignTime.Now.GetYear}.txt");
                if (!File.Exists(path)) return "";
                var text = SafeFileIO.ReadAllText(path);
                return text.Length <= maxChars ? text : text.Substring(text.Length - maxChars);
            }
            catch { return ""; }
        }

        public static void AppendSecretAdvisory(Hero hero, string content)
        {
            if (hero == null || string.IsNullOrWhiteSpace(content)) return;
            var kingdom = GetHeroKingdom(hero);
            if (kingdom == null) return;
            var dir = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "secret_advisory");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{kingdom.Name}_{CampaignTime.Now.GetYear}.txt");
            var entity = EntityManager.GetOrCreateEntity(hero);
            var name = entity?.Name ?? hero.Name?.ToString() ?? "?";
            var title = entity?.Title ?? "?";
            SafeFileIO.AppendAllText(path, $"\n[{PromptManager.GetCurrentTimeString()}] {name}（{title}）密陈：\n{content.Trim()}\n");
        }

        public static List<(string Label, string Body)> ReadConsultThreads(Kingdom? kingdom, int max = 8)
        {
            var result = new List<(string, string)>();
            if (kingdom == null) return result;
            try
            {
                var dir = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "diplomacy", "consults");
                if (!Directory.Exists(dir)) return result;
                foreach (var file in Directory.GetFiles(dir, "*.txt")
                             .OrderByDescending(f => File.GetLastWriteTimeUtc(f)).Take(max))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!name.Contains(kingdom.Name.ToString())) continue;
                    var lines = SafeFileIO.ReadAllLines(file);
                    if (lines.Length == 0) continue;
                    var last = lines[lines.Length - 1].Trim();
                    if (last.Length == 0) continue;
                    result.Add((name.Replace("_and_", " ↔ "), last));
                }
            }
            catch { }
            return result;
        }
    }

    /// <summary>朝议显示文案（页面/查询/史书共用，避免三处各写一套）。</summary>
    public static class CuriaText
    {
        public static string TypeLabel(CuriaAgendaType type) => type switch
        {
            CuriaAgendaType.DeclareWar => "宣战",
            CuriaAgendaType.ProposePeace => "议和提议",
            CuriaAgendaType.ProposeTrade => "贸易提议",
            CuriaAgendaType.ProposeAlliance => "结盟提议",
            CuriaAgendaType.BreakAlliance => "退出盟约",
            CuriaAgendaType.EnactPolicy => "提出政策",
            CuriaAgendaType.RevokePolicy => "废除政策",
            CuriaAgendaType.TransferFief => "封地转让",
            CuriaAgendaType.ExpelClan => "驱逐家族",
            CuriaAgendaType.ProposeTreaty => "战前条约",
            _ => type.ToString()
        };

        public static string StatusLabel(CuriaAgendaStatus status) => status switch
        {
            CuriaAgendaStatus.Proposed => "待陈情",
            CuriaAgendaStatus.Voting => "表决中",
            CuriaAgendaStatus.Passed => "已通过",
            CuriaAgendaStatus.Rejected => "未通过",
            CuriaAgendaStatus.Applied => "已执行",
            CuriaAgendaStatus.Failed => "执行失败",
            CuriaAgendaStatus.Cancelled => "已取消",
            _ => status.ToString()
        };

        public static string StanceLabel(CuriaStance stance) => stance switch
        {
            CuriaStance.Support => "支持",
            CuriaStance.Oppose => "反对",
            CuriaStance.Abstain => "弃权",
            _ => stance.ToString()
        };

        public static string StanceKey(CuriaStance stance) => stance switch
        {
            CuriaStance.Support => "support",
            CuriaStance.Oppose => "oppose",
            CuriaStance.Abstain => "abstain",
            _ => "abstain"
        };

        public static bool TryParseStance(string? text, out CuriaStance stance)
        {
            stance = CuriaStance.Abstain;
            if (string.IsNullOrWhiteSpace(text)) return false;
            switch (text!.Trim().ToLowerInvariant())
            {
                case "support":
                case "agree":
                case "yes":
                case "赞成":
                case "支持":
                    stance = CuriaStance.Support;
                    return true;
                case "oppose":
                case "object":
                case "no":
                case "反对":
                    stance = CuriaStance.Oppose;
                    return true;
                case "abstain":
                case "弃权":
                    stance = CuriaStance.Abstain;
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryParseType(string? text, out CuriaAgendaType type)
        {
            type = CuriaAgendaType.DeclareWar;
            if (string.IsNullOrWhiteSpace(text)) return false;
            switch (text!.Trim().ToLowerInvariant())
            {
                case "declare_war":
                case "war":
                case "宣战":
                    type = CuriaAgendaType.DeclareWar;
                    return true;
                case "propose_peace":
                case "peace":
                case "议和":
                    type = CuriaAgendaType.ProposePeace;
                    return true;
                case "propose_treaty":
                case "treaty":
                case "ultimatum":
                case "条约":
                case "战前条约":
                case "最后通牒":
                    type = CuriaAgendaType.ProposeTreaty;
                    return true;
                case "propose_trade":
                case "trade":
                case "贸易":
                    type = CuriaAgendaType.ProposeTrade;
                    return true;
                case "propose_alliance":
                case "alliance":
                case "结盟":
                case "结盟提议":
                    type = CuriaAgendaType.ProposeAlliance;
                    return true;
                case "break_alliance":
                case "end_alliance":
                case "背盟":
                case "退盟":
                case "退出盟约":
                    type = CuriaAgendaType.BreakAlliance;
                    return true;
                case "enact_policy":
                case "policy":
                case "提出政策":
                case "实施政策":
                    type = CuriaAgendaType.EnactPolicy;
                    return true;
                case "revoke_policy":
                case "废除政策":
                case "撤销政策":
                    type = CuriaAgendaType.RevokePolicy;
                    return true;
                case "transfer_fief":
                case "fief":
                case "transfer":
                case "封地转让":
                case "夺封":
                    type = CuriaAgendaType.TransferFief;
                    return true;
                case "expel_clan":
                case "expel":
                case "驱逐":
                case "驱逐家族":
                    type = CuriaAgendaType.ExpelClan;
                    return true;
                default:
                    return false;
            }
        }

        public static string TargetSummary(CuriaAgenda a)
        {
            return a.Type switch
            {
                CuriaAgendaType.DeclareWar or CuriaAgendaType.ProposePeace or CuriaAgendaType.ProposeTrade
                    or CuriaAgendaType.ProposeAlliance or CuriaAgendaType.BreakAlliance or CuriaAgendaType.ProposeTreaty
                    => a.TargetKingdomName,
                CuriaAgendaType.EnactPolicy or CuriaAgendaType.RevokePolicy
                    => a.TargetPolicyName,
                CuriaAgendaType.TransferFief
                    => $"{a.TargetSettlementName}（{a.TargetClanName} → {a.NewOwnerClanName}）",
                CuriaAgendaType.ExpelClan => a.TargetClanName,
                _ => ""
            };
        }

        public static string KindLabel(CuriaAgendaKind kind) => kind switch
        {
            CuriaAgendaKind.VoluntaryTransfer => "自愿转让",
            CuriaAgendaKind.ImposedTransfer => "国王夺封",
            CuriaAgendaKind.Expulsion => "驱逐",
            CuriaAgendaKind.Policy => "政策",
            _ => "对外"
        };

        public static string AttendanceLabel(string reason) => string.IsNullOrWhiteSpace(reason) ? "应召" : reason;
    }
}
