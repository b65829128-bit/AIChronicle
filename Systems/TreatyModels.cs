using System;

namespace AIChronicle
{
    /// <summary>
    /// 条约条款（战前条约 / 和平条约共用）。这是结构化载荷，不是自由文本：
    /// - CedeFrom: "proposer" = 我方割让；"target" = 对方割让；"" = 不割地
    /// - TributeAmount: 正数 = 我方每日支付对方；负数 = 对方每日支付我方
    /// - TributeDays: 赔款持续天数
    /// - TruceDays: 签约后休战天数（0 = 不设休战）
    /// - WarOnReject: 仅战前条约；对方拒绝时是否自动宣战
    /// </summary>
    /// <summary>一块割地：方向 + 城镇/城堡。方向从提议者视角：proposer = 我方割让；target = 对方割让。</summary>
    public class TreatyCession
    {
        public string SettlementId { get; set; } = "";
        public string SettlementName { get; set; } = "";
        public string CedeFrom { get; set; } = "";

        public TreatyCession Clone()
        {
            return new TreatyCession
            {
                SettlementId = SettlementId,
                SettlementName = SettlementName,
                CedeFrom = CedeFrom
            };
        }
    }

    public class TreatyTerms
    {
        // 旧版单块割地字段：只用于兼容旧议程/旧提案/旧 UI 载荷。新写入一律使用 Cessions。
        public string SettlementId { get; set; } = "";
        public string SettlementName { get; set; } = "";
        public string CedeFrom { get; set; } = "";

        /// <summary>多块割地；双方可同时割地。</summary>
        public System.Collections.Generic.List<TreatyCession> Cessions { get; set; } = new System.Collections.Generic.List<TreatyCession>();

        public int TributeAmount { get; set; }
        public int TributeDays { get; set; }
        public int TruceDays { get; set; }
        public bool WarOnReject { get; set; }

        /// <summary>返回有效割地列表；旧单块字段自动折算为一条，不修改对象。</summary>
        public System.Collections.Generic.List<TreatyCession> GetCessions()
        {
            var list = new System.Collections.Generic.List<TreatyCession>();
            if (Cessions != null)
            {
                foreach (var c in Cessions)
                {
                    if (c == null) continue;
                    if (string.IsNullOrWhiteSpace(c.SettlementId) && string.IsNullOrWhiteSpace(c.SettlementName))
                        continue;
                    list.Add(c);
                }
            }
            if (list.Count == 0
                && (!string.IsNullOrWhiteSpace(SettlementId) || !string.IsNullOrWhiteSpace(SettlementName)))
            {
                list.Add(new TreatyCession
                {
                    SettlementId = SettlementId,
                    SettlementName = SettlementName,
                    CedeFrom = CedeFrom
                });
            }
            return list;
        }

        /// <summary>把旧单块字段并入 Cessions 并清空旧字段（校验/落盘前调用）。</summary>
        public void MigrateLegacy()
        {
            if (Cessions == null) Cessions = new System.Collections.Generic.List<TreatyCession>();
            if (Cessions.Count == 0
                && (!string.IsNullOrWhiteSpace(SettlementId) || !string.IsNullOrWhiteSpace(SettlementName)))
            {
                Cessions.Add(new TreatyCession
                {
                    SettlementId = SettlementId,
                    SettlementName = SettlementName,
                    CedeFrom = CedeFrom
                });
            }
            SettlementId = "";
            SettlementName = "";
            CedeFrom = "";
        }

        public bool HasCession()
        {
            return GetCessions().Count > 0;
        }

        public bool HasTribute()
        {
            return TributeAmount != 0 && TributeDays > 0;
        }

        public bool HasConcession()
        {
            return HasCession() || HasTribute();
        }

        public bool HasAnyTerm()
        {
            return HasConcession() || TruceDays > 0;
        }

        public TreatyTerms Clone()
        {
            var copy = new TreatyTerms
            {
                SettlementId = SettlementId,
                SettlementName = SettlementName,
                CedeFrom = CedeFrom,
                TributeAmount = TributeAmount,
                TributeDays = TributeDays,
                TruceDays = TruceDays,
                WarOnReject = WarOnReject
            };
            if (Cessions != null)
            {
                foreach (var c in Cessions)
                    if (c != null) copy.Cessions.Add(c.Clone());
            }
            return copy;
        }
    }

    /// <summary>已签署条约的持久记录（与朝议档案分离，不随朝议记录清理）。</summary>
    public class TreatyRecord
    {
        public string Id { get; set; } = "";
        /// <summary>"ultimatum" = 战前条约；"peace" = 和平条约。</summary>
        public string Kind { get; set; } = "";
        public string ProposerKingdomId { get; set; } = "";
        public string ProposerKingdomName { get; set; } = "";
        public string TargetKingdomId { get; set; } = "";
        public string TargetKingdomName { get; set; } = "";
        public string ProposerRulerName { get; set; } = "";
        public string TargetRulerName { get; set; } = "";
        public TreatyTerms Terms { get; set; } = new TreatyTerms();
        public int SignedDay { get; set; }
        public string SignedLabel { get; set; } = "";
        public int TruceUntilDay { get; set; }
        /// <summary>"active" / "broken" / "expired"。</summary>
        public string Status { get; set; } = "active";
        public string AgendaId { get; set; } = "";
        public string ProposalId { get; set; } = "";
        public int BrokenDay { get; set; }
        public string BrokenBy { get; set; } = "";

        public bool IsActiveTruce(int today)
        {
            return Status == "active" && TruceUntilDay > today;
        }
    }

    public class TreatyBook
    {
        public System.Collections.Generic.List<TreatyRecord> Treaties { get; set; } = new System.Collections.Generic.List<TreatyRecord>();
    }
}
