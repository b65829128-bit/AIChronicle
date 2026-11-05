using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;

namespace AIChronicle
{
    /// <summary>结果互斥：每一方只保管各财物在所有分支中的最大数额。只在 Mission 初始化成功后 Take。</summary>
    public sealed class MeetingEscrow
    {
        private sealed class Account
        {
            public Hero Hero = null!;
            public ItemRoster? Roster;
            public MeetingOffer Reserved = new();
            public List<(EquipmentElement element, int count)> Items = new();
        }
        private readonly long _epoch;
        private readonly Account[] _accounts;
        public bool Held { get; private set; }
        public bool Finished { get; private set; }
        public string Result { get; private set; } = "尚未保管财物。";

        public MeetingEscrow(MeetingSession session, MeetingTerms terms)
        {
            _epoch = session.Epoch;
            _accounts = new[]
            {
                new Account { Hero = session.Player, Roster = MeetingAssets.Roster(session.Player), Reserved = MeetingAssetRules.ReserveMaximum(terms.Outcomes.Select(o => o.OtherOffer)) },
                new Account { Hero = session.Npc, Roster = MeetingAssets.Roster(session.Npc), Reserved = MeetingAssetRules.ReserveMaximum(terms.Outcomes.Select(o => o.YourOffer)) }
            };
        }

        private void CheckEpoch()
        {
            if (_epoch != MainThreadExecutor.CampaignEpoch || Campaign.Current == null)
                throw new OperationCanceledException("这场决斗所属战役已结束。");
        }

        private List<(Account account, int gold, HashSet<string> keys, List<(EquipmentElement element, int count)> items)> Capture()
            => _accounts.Select(a =>
            {
                var keys = new HashSet<string>(_accounts.SelectMany(x => x.Reserved.Items).Select(i => i.Id), StringComparer.Ordinal);
                return (a, a.Hero.Gold, keys, MeetingAssets.Capture(a.Roster, keys));
            }).ToList();

        private static void Restore(List<(Account account, int gold, HashSet<string> keys, List<(EquipmentElement element, int count)> items)> before)
        {
            foreach (var entry in before)
            {
                entry.account.Hero.ChangeHeroGold(entry.gold - entry.account.Hero.Gold);
                MeetingAssets.Restore(entry.account.Roster, entry.items, entry.keys);
            }
        }

        public void Take()
        {
            CheckEpoch();
            if (Held || Finished) throw new InvalidOperationException("财物已经保管或结算。");
            foreach (var account in _accounts)
            {
                if (!account.Hero.IsAlive || account.Hero.Gold < account.Reserved.Gold)
                    throw new InvalidOperationException(account.Hero.Name + "的可用财物已变化。");
                if (account.Reserved.Items.Count > 0 && !ReferenceEquals(account.Roster, MeetingAssets.Roster(account.Hero)))
                    throw new InvalidOperationException("物资处置权已经变化。");
                var inventory = MeetingAssets.Inventory(account.Hero).ToDictionary(i => i.Id);
                if (account.Reserved.Items.Any(i => !inventory.TryGetValue(i.Id, out var available) || available.Count < i.Count))
                    throw new InvalidOperationException("胜负条件所需物资已不足。");
            }
            var before = Capture();
            try
            {
                foreach (var account in _accounts)
                {
                    account.Hero.ChangeHeroGold(-account.Reserved.Gold);
                    foreach (var item in account.Reserved.Items)
                    {
                        var remaining = item.Count;
                        foreach (var entry in account.Roster!.Where(e => MeetingAssets.Tradable(e.EquipmentElement)
                            && MeetingAssets.Key(e.EquipmentElement) == item.Id).ToList())
                        {
                            var count = Math.Min(remaining, entry.Amount);
                            account.Items.Add((entry.EquipmentElement, count));
                            account.Roster!.AddToCounts(entry.EquipmentElement, -count);
                            remaining -= count;
                            if (remaining == 0) break;
                        }
                        if (remaining != 0) throw new InvalidOperationException("物资保管数量核对失败。");
                    }
                }
                Held = true;
                Result = "胜负条件所需财物已保管，尚未交付任何一方。";
            }
            catch (Exception ex)
            {
                Finished = true;
                try { Restore(before); Result = "保管失败，已恢复财物。"; }
                catch (Exception restore) { Result = "财物保管和恢复均出现异常，不自动重试：" + restore.Message; throw new AggregateException(Result, ex, restore); }
                throw;
            }
        }

        public void Refund() => Distribute(null);
        public void Deliver(MeetingOutcome outcome) => Distribute(outcome);

        private void Distribute(MeetingOutcome? outcome)
        {
            CheckEpoch();
            if (Finished) return;
            if (!Held) { Finished = true; Result = "未保管财物，无需退还。"; return; }
            // 先关闭入口；任何异常均留下实际结果，不允许再次领取。
            Finished = true;
            var before = Capture();
            var outgoing = new[] { outcome?.OtherOffer ?? new MeetingOffer(), outcome?.YourOffer ?? new MeetingOffer() };
            try
            {
                var credits = new List<(EquipmentElement element, int count)>[] { new(), new() };
                for (var side = 0; side < 2; side++)
                {
                    var account = _accounts[side];
                    if (outgoing[side].Gold < 0 || outgoing[side].Gold > account.Reserved.Gold)
                        throw new InvalidOperationException("结算条件超出已保管金币。");
                    var amounts = outgoing[side].Items.ToDictionary(i => i.Id, i => i.Count, StringComparer.Ordinal);
                    foreach (var held in account.Items)
                    {
                        var key = MeetingAssets.Key(held.element);
                        var send = amounts.TryGetValue(key, out var needed) ? Math.Min(needed, held.count) : 0;
                        if (send > 0) { credits[1 - side].Add((held.element, send)); amounts[key] -= send; }
                        if (held.count > send) credits[side].Add((held.element, held.count - send));
                    }
                    if (amounts.Values.Any(n => n != 0)) throw new InvalidOperationException("结算物资超出已保管数量。");
                }
                // 收回未使用部分，同时领取另一方承诺的部分；预检两个方向后再修改。
                for (var side = 0; side < 2; side++)
                {
                    var account = _accounts[side];
                    var gold = (long)account.Hero.Gold + account.Reserved.Gold - outgoing[side].Gold + outgoing[1 - side].Gold;
                    if (gold > int.MaxValue) throw new InvalidOperationException("结算金币超出游戏范围。");
                    if (credits[side].Count > 0 && account.Roster == null) throw new InvalidOperationException("物资接收库存已不可用。");
                    foreach (var group in credits[side].GroupBy(c => MeetingAssets.Key(c.element)))
                    {
                        var existing = account.Roster!.Where(e => MeetingAssets.Tradable(e.EquipmentElement) && MeetingAssets.Key(e.EquipmentElement) == group.Key).Sum(e => (long)e.Amount);
                        if (existing + group.Sum(c => (long)c.count) > int.MaxValue) throw new InvalidOperationException("结算物资数量超出游戏范围。");
                    }
                }
                for (var side = 0; side < 2; side++)
                {
                    var account = _accounts[side];
                    account.Hero.ChangeHeroGold(account.Reserved.Gold - outgoing[side].Gold + outgoing[1 - side].Gold);
                    foreach (var credit in credits[side]) account.Roster!.AddToCounts(credit.element, credit.count);
                }
                Held = false;
                Result = outcome == null ? "保管财物已退回原主。" : "保管财物已按胜负交付，未使用部分已退回原主。";
            }
            catch (Exception ex)
            {
                try
                {
                    Restore(before); // 恢复到发放前的空手状态，不覆盖战斗期间的其他正常财物变化。
                    foreach (var account in _accounts)
                    {
                        account.Hero.ChangeHeroGold(account.Reserved.Gold);
                        foreach (var held in account.Items) account.Roster!.AddToCounts(held.element, held.count);
                    }
                    Held = false;
                    Result = "胜负财物交付失败，已退回原主：" + ex.Message;
                }
                catch (Exception restore) { Result = "交付或退还存在未完成部分，不自动重试：" + restore.Message; throw new AggregateException(Result, ex, restore); }
                throw new InvalidOperationException(Result, ex);
            }
        }
    }
}
