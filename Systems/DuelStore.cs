using System;
using System.Linq;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;

namespace AIChronicle
{
    /// <summary>独立可选键，旧存档缺键即空账本；计数随存档恢复，不从另一条进度的外部审计记录推断。</summary>
    public sealed class DuelStore : CampaignBehaviorBase
    {
        private DuelRewardLedger _ledger = new();
        private string _saved = "";
        public string? Error { get; private set; }
        public static DuelStore? Current => Campaign.Current?.GetCampaignBehavior<DuelStore>();
        public override void RegisterEvents() { }
        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving && Error == null) _saved = JsonConvert.SerializeObject(_ledger);
            dataStore.SyncData("aichronicle_duel_rewards_v1", ref _saved);
            if (!dataStore.IsLoading) return;
            _ledger = new DuelRewardLedger(); Error = null;
            if (string.IsNullOrWhiteSpace(_saved)) return;
            try
            {
                var ledger = JsonConvert.DeserializeObject<DuelRewardLedger>(_saved);
                if (ledger == null || ledger.Version != 1 || ledger.Entries == null || ledger.Entries.Any(e => e == null
                    || string.IsNullOrWhiteSpace(e.Id) || double.IsNaN(e.Day) || double.IsInfinity(e.Day)))
                    throw new InvalidOperationException("无法识别决斗奖励计数版本或记录。");
                _ledger = ledger;
            }
            catch (Exception ex) { Error = "决斗奖励记录读取失败：" + ex.Message; UiTrace.Log(Error); }
        }
        public int CountWins(string winnerClan, string loserClan, double now)
        {
            if (Error != null) throw new InvalidOperationException(Error);
            return _ledger.Count(winnerClan, loserClan, now);
        }
        public bool Complete(string id, double day, string winnerClan, string loserClan, bool normal)
        {
            if (Error != null) throw new InvalidOperationException(Error);
            return _ledger.Complete(new DuelVictoryStamp { Id = id, Day = day, WinnerClanId = winnerClan, LoserClanId = loserClan, NormalVictory = normal });
        }
    }
}
