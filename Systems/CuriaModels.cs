using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AIChronicle
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum CuriaAgendaType
    {
        DeclareWar,
        ProposePeace,
        ProposeTrade,
        ProposeAlliance,
        BreakAlliance,
        EnactPolicy,
        RevokePolicy,
        TransferFief,
        ExpelClan,
        ProposeTreaty
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum CuriaAgendaStatus
    {
        Proposed,
        Voting,
        Passed,
        Rejected,
        Applied,
        Failed,
        Cancelled
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum CuriaStance
    {
        Support,
        Oppose,
        Abstain
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum CuriaAgendaKind
    {
        External,
        VoluntaryTransfer,
        ImposedTransfer,
        Expulsion,
        Policy
    }

    /// <summary>一个应召投票者。Stance 为 null 表示尚未表态。</summary>
    public class CuriaVote
    {
        public string ClanId { get; set; } = "";
        public string ClanName { get; set; } = "";
        public string LeaderEntityId { get; set; } = "";
        public string LeaderName { get; set; } = "";
        public float Weight { get; set; }
        public CuriaStance? Stance { get; set; }
        public string Reason { get; set; } = "";
        public string SummonReason { get; set; } = "";
        public bool IsKing { get; set; }
        public bool IsPlayer { get; set; }
        public bool IsProposer { get; set; }
        public bool IsAutoSupport { get; set; }
        public int VotedDay { get; set; } = -1;

        [JsonIgnore]
        public bool HasVoted => Stance.HasValue;
    }

    /// <summary>未出席者（记录在案，便于叙事合理化与页面展示）。</summary>
    public class CuriaAbsentVoter
    {
        public string ClanId { get; set; } = "";
        public string ClanName { get; set; } = "";
        public string LeaderName { get; set; } = "";
        public string Reason { get; set; } = "未获召见";
    }

    public class CuriaAgenda
    {
        public string Id { get; set; } = "";
        public string KingdomId { get; set; } = "";
        public string KingdomName { get; set; } = "";
        public CuriaAgendaType Type { get; set; }
        public CuriaAgendaKind Kind { get; set; }
        public CuriaAgendaStatus Status { get; set; } = CuriaAgendaStatus.Proposed;

        public string ProposerEntityId { get; set; } = "";
        public string ProposerClanId { get; set; } = "";
        public string ProposerName { get; set; } = "";
        public string Reason { get; set; } = "";
        public string Terms { get; set; } = "";

        /// <summary>结构化条约条款（战前条约/带条件议和）；无条件议和与旧议程为 null。</summary>
        public TreatyTerms? Treaty { get; set; }

        // 外交类目标
        public string TargetKingdomId { get; set; } = "";
        public string TargetKingdomName { get; set; } = "";

        // 政策类目标
        public string TargetPolicyId { get; set; } = "";
        public string TargetPolicyName { get; set; } = "";
        public string PolicyEffect { get; set; } = "";
        public string PolicyDescription { get; set; } = "";

        // 封地类目标
        public string TargetSettlementId { get; set; } = "";
        public string TargetSettlementName { get; set; } = "";
        public string TargetClanId { get; set; } = "";
        public string TargetClanName { get; set; } = "";
        public string NewOwnerClanId { get; set; } = "";
        public string NewOwnerClanName { get; set; } = "";
        public string NewOwnerEntityId { get; set; } = "";

        // 驱逐类目标复用 TargetClanId / TargetClanName

        public string CreatedLabel { get; set; } = "";
        public string ResolvedLabel { get; set; } = "";
        public int CreatedDay { get; set; }
        public int DeadlineDay { get; set; }
        public int DefenseDeadlineDay { get; set; }
        public int VotingStartedDay { get; set; } = -1;

        public string TargetDefense { get; set; } = "";
        public string TargetDefenseBy { get; set; } = "";
        public bool TargetDefended { get; set; }

        public List<CuriaVote> Votes { get; set; } = new();
        public List<CuriaAbsentVoter> Absent { get; set; } = new();
        public List<string> SummonedClanIds { get; set; } = new();

        public float SupportWeight { get; set; }
        public float OpposeWeight { get; set; }
        public float AbstainWeight { get; set; }
        public float SummonedWeight { get; set; }
        public float TotalWeight { get; set; }
        public float QuorumWeight { get; set; }
        public float Threshold { get; set; } = 0.5f;

        public string KingStance { get; set; } = "";
        public string KingReason { get; set; } = "";

        public string Outcome { get; set; } = "";
        public string EffectResult { get; set; } = "";
        public string FailureReason { get; set; } = "";
        public string EffectApplied { get; set; } = "";

        [JsonIgnore]
        public float DecidedWeight => SupportWeight + OpposeWeight;

        [JsonIgnore]
        public float SupportRatio => DecidedWeight <= 0.01f ? 0f : SupportWeight / DecidedWeight;

        [JsonIgnore]
        public bool IsTransferOrExpel => Type == CuriaAgendaType.TransferFief || Type == CuriaAgendaType.ExpelClan;

        [JsonIgnore]
        public bool NeedsTargetDefense => Kind == CuriaAgendaKind.ImposedTransfer || Kind == CuriaAgendaKind.Expulsion;

        [JsonIgnore]
        public bool AllSummonedVoted
        {
            get
            {
                if (Votes.Count == 0) return false;
                foreach (var v in Votes)
                {
                    if (!v.HasVoted) return false;
                    // 提议者的自动支持与 AI 投票一样计入最终表态：所有应召者投完即结算，
                    // 不再要求玩家作为提议者重复点一次“支持”。
                }
                return true;
            }
        }
    }

    /// <summary>每王国朝议状态（只放轻量状态，议程本体在 agenda_{id}.json）。</summary>
    public class CuriaState
    {
        public string KingdomId { get; set; } = "";
        public string KingdomName { get; set; } = "";
        public string ActiveAgendaId { get; set; } = "";
        public int LastResolvedDay { get; set; } = -1;
        public Dictionary<string, int> LastSummonedDay { get; set; } = new();
        public Dictionary<string, int> ProposerCooldownDay { get; set; } = new();
    }
}
