using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace AIChronicle
{
    public partial class HistoryRecorder
    {
        private sealed class RebellionOrigin
        {
            public Settlement? Settlement;
            public Clan? FormerOwner;
        }
        private sealed class RaidWitness
        {
            public Hero? Attacker;
            public Clan? Owner;
            public string Actor = "";
        }
        private readonly Dictionary<Clan, RebellionOrigin> _worldRebellions = new();
        private readonly HashSet<Clan> _settledRebellions = new();
        private readonly HashSet<Clan> _failedRebellions = new();
        private readonly Dictionary<Village, RaidWitness> _worldRaids = new();
        private readonly HashSet<Village> _worldLooted = new();
        private readonly Dictionary<Village, RaidEventComponent> _worldRaidEnds = new();

        private void RegisterWarfareHistoryEvents()
        {
            CampaignEvents.RebellionFinished.AddNonSerializedListener(this,
                new Action<Settlement, Clan>((settlement, owner) => ObserveWorldHistory(() => OnWorldRebellion(settlement, owner))));
            CampaignEvents.RebelliousClanDisbandedAtSettlement.AddNonSerializedListener(this,
                new Action<Settlement, Clan>((settlement, clan) => ObserveWorldHistory(() => OnWorldRebellionEstablished(settlement, clan))));
            CampaignEvents.OnClanDestroyedEvent.AddNonSerializedListener(this,
                new Action<Clan>(clan => ObserveWorldHistory(() => OnWorldRebelDestroyed(clan))));
            CampaignEvents.VillageBeingRaided.AddNonSerializedListener(this,
                new Action<Village>(village => ObserveWorldHistory(() => OnWorldRaidStarted(village))));
            CampaignEvents.VillageLooted.AddNonSerializedListener(this,
                new Action<Village>(village => ObserveWorldHistory(() => OnWorldVillageLooted(village))));
            CampaignEvents.RaidCompletedEvent.AddNonSerializedListener(this,
                new Action<BattleSideEnum, RaidEventComponent>((_, raid) => ObserveWorldHistory(() => OnWorldRaidEnded(raid))));
            CampaignEvents.OnSiegeAftermathAppliedEvent.AddNonSerializedListener(this,
                new Action<MobileParty, Settlement, SiegeAftermathAction.SiegeAftermath, Clan, Dictionary<MobileParty, float>>(
                    (party, settlement, aftermath, owner, _) => ObserveWorldHistory(() => OnWorldSiegeAftermath(party, settlement, aftermath, owner))));
        }

        private void CaptureWarfareHistoryBaseline()
        {
            _worldRebellions.Clear(); _settledRebellions.Clear(); _failedRebellions.Clear();
            _worldRaids.Clear(); _worldLooted.Clear(); _worldRaidEnds.Clear();
            foreach (var clan in Clan.All)
                if (clan.IsRebelClan) _worldRebellions[clan] = new RebellionOrigin { Settlement = clan.HomeSettlement };
            foreach (var village in Village.All)
            {
                if (village.VillageState == Village.VillageStates.BeingRaided) _worldRaids[village] = CaptureRaidWitness(village);
                if (village.VillageState == Village.VillageStates.Looted) _worldLooted.Add(village);
            }
        }

        private void OnWorldRebellion(Settlement settlement, Clan oldOwner)
        {
            var rebels = settlement?.OwnerClan;
            if (rebels == null || !rebels.IsRebelClan || rebels == oldOwner || _worldRebellions.ContainsKey(rebels)) return;
            _worldRebellions[rebels] = new RebellionOrigin { Settlement = settlement, FormerOwner = oldOwner };
            // 名称虽为 RebellionFinished，原版是在发动叛乱、夺城之后发出。
            var fact = new WorldFact($"{settlement!.Name}爆发叛乱，{oldOwner?.Name}失去城镇，{WorldHero(rebels.Leader)}率{rebels.Name}据城自立。")
                .Clan(oldOwner).Realm(oldOwner?.Kingdom).Clan(rebels);
            PublishWorldFact("rebellion_started", fact);
        }

        private void OnWorldRebellionEstablished(Settlement settlement, Clan clan)
        {
            // 原版存续 30 天且仍有封地后，清 IsRebelClan、改名，再发此事件；不是被消灭。
            if (clan == null || clan.IsEliminated || clan.IsRebelClan || !_settledRebellions.Add(clan)) return;
            _worldRebellions.TryGetValue(clan, out var origin);
            var fact = new WorldFact($"{clan.Name}在{settlement?.Name}一带立足，叛乱中兴起的势力得以延续。")
                .Clan(clan).Clan(origin?.FormerOwner).Realm(origin?.FormerOwner?.Kingdom);
            PublishWorldFact("rebellion_established", fact);
        }

        private void OnWorldRebelDestroyed(Clan clan)
        {
            if (clan == null || !clan.IsRebelClan || _failedRebellions.Contains(clan)) return;
            _worldRebellions.TryGetValue(clan, out var origin);
            var settlement = origin?.Settlement ?? clan.HomeSettlement;
            var owner = settlement?.OwnerClan == clan ? null : settlement?.OwnerClan;
            var fact = new WorldFact($"{clan.Name}覆亡，起于{settlement?.Name?.ToString() ?? "不详之地"}的叛乱势力终结。")
                .Clan(origin?.FormerOwner).Realm(origin?.FormerOwner?.Kingdom).Clan(owner).Realm(owner?.Kingdom);
            var campaign = Campaign.Current;
            var generation = _worldHistoryGeneration;
            // OnClanDestroyed 在 DeactivateClan 之前，须确认覆亡真实生效。
            MainThreadExecutor.Post(() => ObserveWorldHistory(() =>
            {
                if (Campaign.Current != campaign || generation != _worldHistoryGeneration || !clan.IsEliminated || !_failedRebellions.Add(clan)) return;
                PublishWorldFact("rebellion_failed", fact);
            }));
        }

        private static RaidWitness CaptureRaidWitness(Village village)
        {
            var party = village.Settlement.Party.MapEvent?.AttackerSide?.LeaderParty;
            var attacker = party?.LeaderHero ?? party?.Owner;
            return new RaidWitness { Attacker = attacker, Owner = village.Settlement.OwnerClan,
                Actor = attacker == null ? "不明军队" : WorldHero(attacker) + "的部队" };
        }

        private static WorldFact RaidFact(RaidWitness witness, string text) => new WorldFact(text)
            .Add(witness.Attacker).Clan(witness.Attacker?.Clan).Realm(witness.Attacker?.Clan?.Kingdom)
            .Clan(witness.Owner).Realm(witness.Owner?.Kingdom);

        private void OnWorldRaidStarted(Village village)
        {
            if (village == null || village.VillageState != Village.VillageStates.BeingRaided || _worldRaids.ContainsKey(village)) return;
            _worldLooted.Remove(village);
            var witness = CaptureRaidWitness(village); _worldRaids[village] = witness;
            PublishWorldFact("village_raided", RaidFact(witness, $"{witness.Actor}开始劫掠{village.Settlement.Name}。"), immediate: false);
        }

        private void OnWorldVillageLooted(Village village)
        {
            if (village == null || village.VillageState != Village.VillageStates.Looted || !_worldLooted.Add(village)) return;
            var witness = _worldRaids.TryGetValue(village, out var atStart) ? atStart : CaptureRaidWitness(village);
            PublishWorldFact("village_looted", RaidFact(witness, $"{village.Settlement.Name}遭{witness.Actor}洗劫。"), immediate: false);
        }

        private void OnWorldRaidEnded(RaidEventComponent raid)
        {
            var village = raid?.MapEventSettlement?.Village;
            if (village == null || (_worldRaidEnds.TryGetValue(village, out var prior) && ReferenceEquals(prior, raid))) return;
            _worldRaidEnds[village] = raid!;
            if (village.VillageState == Village.VillageStates.Looted) OnWorldVillageLooted(village);
            else
            {
                var witness = _worldRaids.TryGetValue(village, out var atStart) ? atStart : CaptureRaidWitness(village);
                PublishWorldFact("raid_ended", RaidFact(witness, $"{witness.Actor}对{village.Settlement.Name}的劫掠结束，村庄未被洗劫一空。"), immediate: false);
            }
            _worldRaids.Remove(village);
        }

        private void OnWorldSiegeAftermath(MobileParty party, Settlement settlement, SiegeAftermathAction.SiegeAftermath aftermath, Clan oldOwner)
        {
            if (settlement == null || (!settlement.IsTown && !settlement.IsCastle)) return;
            var treatment = aftermath switch
            {
                SiegeAftermathAction.SiegeAftermath.ShowMercy => "宽赦",
                SiegeAftermathAction.SiegeAftermath.Pillage => "洗劫",
                SiegeAftermathAction.SiegeAftermath.Devastate => "毁坏",
                _ => null
            };
            if (treatment == null) return;
            var actor = party?.LeaderHero;
            var fact = new WorldFact($"{(actor == null ? "攻城军队" : WorldHero(actor))}攻陷{settlement.Name}后，对{settlement.Name}采取{treatment}处置。")
                .Add(actor).Clan(actor?.Clan).Realm(actor?.Clan?.Kingdom).Clan(oldOwner).Realm(oldOwner?.Kingdom)
                .Clan(settlement.OwnerClan).Realm(settlement.OwnerClan?.Kingdom);
            PublishWorldFact("siege_aftermath", fact); // 只陈述已应用的处置，不编造屠杀、伤亡或态度。
        }
    }
}
