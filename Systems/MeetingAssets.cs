using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;

namespace AIChronicle
{
    /// <summary>只操作领队有权处置的部队库存。精确传递 EquipmentElement，品质不会被普通物品替换。</summary>
    public static class MeetingAssets
    {
        public static ItemRoster? Roster(Hero hero)
            => hero.PartyBelongedTo?.LeaderHero == hero ? hero.PartyBelongedTo.ItemRoster : null;

        internal static string Key(EquipmentElement element)
            => element.Item.StringId + "|" + (element.ItemModifier?.StringId ?? "") + "|" + (element.CosmeticItem?.StringId ?? "");

        internal static bool Tradable(EquipmentElement element) => element.Item != null && !element.IsQuestItem;

        public static List<MeetingInventoryEntry> Inventory(Hero hero)
        {
            var roster = Roster(hero);
            if (roster == null) return new List<MeetingInventoryEntry>();
            return roster.Where(e => e.Amount > 0 && Tradable(e.EquipmentElement))
                .GroupBy(e => Key(e.EquipmentElement), StringComparer.Ordinal)
                .Select(g => new MeetingInventoryEntry
                {
                    Id = g.Key, Name = g.First().EquipmentElement.GetModifiedItemName().ToString(),
                    Count = g.Sum(e => e.Amount), ReferenceValue = g.First().EquipmentElement.ItemValue
                }).OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
        }

        public static string? Validate(MeetingSession session, MeetingTerms terms)
        {
            var playerRoster = Roster(session.Player);
            var npcRoster = Roster(session.Npc);
            if (terms.OtherOffer.Items.Count + terms.YourOffer.Items.Count > 0
                && (playerRoster == null || npcRoster == null || ReferenceEquals(playerRoster, npcRoster)))
                return "物品交换需要双方各自有权处置的独立部队库存；仍可只谈金币或婚配。";
            return MeetingAssetRules.Validate(terms, session.Player.Gold, session.Npc.Gold, Inventory(session.Player), Inventory(session.Npc),
                session.Player.Name.ToString(), session.Npc.Name.ToString());
        }

        public static string Commit(MeetingSession session, MeetingTerms terms, IMeetingAction action)
        {
            var invalid = Validate(session, terms) ?? action.Validate(session, terms) ?? MeetingClauses.Validate(session, terms);
            if (invalid != null) throw new InvalidOperationException(invalid);
            var description = action.Describe(session, terms);
            var playerGold = session.Player.Gold;
            var npcGold = session.Npc.Gold;
            // 婚配可能改变某人的部队，必须在执行前捕获财物所在的实际 roster。
            var playerRoster = Roster(session.Player);
            var npcRoster = Roster(session.Npc);
            var keys = new HashSet<string>(terms.OtherOffer.Items.Concat(terms.YourOffer.Items).Select(i => i.Id), StringComparer.Ordinal);
            var playerBefore = Capture(playerRoster, keys);
            var npcBefore = Capture(npcRoster, keys);
            var recovered = MeetingBatch.Apply(() =>
            {
                var net = terms.OtherOffer.Gold - terms.YourOffer.Gold;
                if (net > 0) GiveGoldAction.ApplyBetweenCharacters(session.Player, session.Npc, net, true);
                if (net < 0) GiveGoldAction.ApplyBetweenCharacters(session.Npc, session.Player, -net, true);
                Transfer(playerRoster, npcRoster, terms.OtherOffer.Items);
                Transfer(npcRoster, playerRoster, terms.YourOffer.Items);
                if (session.Player.Gold != playerGold - net || session.Npc.Gold != npcGold + net)
                    throw new InvalidOperationException("金币交付结果与约定不符。");
            }, () =>
            {
                session.Player.ChangeHeroGold(playerGold - session.Player.Gold);
                session.Npc.ChangeHeroGold(npcGold - session.Npc.Gold);
                Restore(playerRoster, playerBefore, keys);
                Restore(npcRoster, npcBefore, keys);
            }, MeetingClauses.Prepare(session, terms));
            foreach (var warning in recovered) UiTrace.Log("Meeting effect applied despite callback failure: " + warning);
            return description;
        }

        internal static List<(EquipmentElement element, int count)> Capture(ItemRoster? roster, HashSet<string> keys)
            => roster == null ? new() : roster.Where(e => Tradable(e.EquipmentElement) && keys.Contains(Key(e.EquipmentElement)))
                .Select(e => (e.EquipmentElement, e.Amount)).ToList();

        internal static void Restore(ItemRoster? roster, List<(EquipmentElement element, int count)> before, HashSet<string> keys)
        {
            if (roster == null) return;
            foreach (var current in Capture(roster, keys)) roster.AddToCounts(current.element, -current.count);
            foreach (var previous in before) roster.AddToCounts(previous.element, previous.count);
        }

        private static void Transfer(ItemRoster? from, ItemRoster? to, IEnumerable<MeetingItem> items)
        {
            foreach (var item in items)
            {
                if (from == null || to == null) throw new InvalidOperationException("部队库存已不可用。");
                var remaining = item.Count;
                foreach (var entry in from.Where(e => Tradable(e.EquipmentElement) && Key(e.EquipmentElement) == item.Id).ToList())
                {
                    var count = Math.Min(entry.Amount, remaining);
                    from.AddToCounts(entry.EquipmentElement, -count);
                    to.AddToCounts(entry.EquipmentElement, count);
                    remaining -= count;
                    if (remaining == 0) break;
                }
                if (remaining != 0) throw new InvalidOperationException("物品数量已经变化。");
            }
        }

        public static string DescribeOffer(Hero hero, MeetingOffer offer)
        {
            var inventory = Inventory(hero).ToDictionary(i => i.Id, StringComparer.Ordinal);
            var pieces = new List<string>();
            if (offer.Gold > 0) pieces.Add(offer.Gold + "金币");
            foreach (var item in offer.Items)
                pieces.Add($"{item.Count}个{(inventory.TryGetValue(item.Id, out var entry) ? entry.Name : item.Id)}");
            return pieces.Count == 0 ? "无附加财物" : string.Join("、", pieces);
        }
    }
}
