// 决斗财物/保存回归的最小容器：执行生产 MeetingAssets / MeetingEscrow / DuelStore，
// 提供计数与可注入故障，不模拟战斗、死亡概率、战役决策或继任。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace TaleWorlds.CampaignSystem
{
    public sealed partial class Hero
    {
        public int Gold { get; set; }
        public bool IsAlive { get; set; } = true;
        public Party.MobileParty? PartyBelongedTo { get; set; }
        public void ChangeHeroGold(int delta) => Gold = checked(Gold + delta);
    }
    public sealed partial class Campaign
    {
        public readonly Dictionary<Type, CampaignBehaviorBase> Behaviors = new();
        public T? GetCampaignBehavior<T>() where T : class => Behaviors.TryGetValue(typeof(T), out var value) ? (T)(object)value : null;
    }
    public abstract class CampaignBehaviorBase
    {
        public abstract void RegisterEvents();
        public abstract void SyncData(IDataStore store);
    }
    public interface IDataStore
    {
        bool IsSaving { get; }
        bool IsLoading { get; }
        bool SyncData<T>(string key, ref T data);
    }
}
namespace TaleWorlds.CampaignSystem.Party
{
    public sealed partial class MobileParty
    {
        public Hero? LeaderHero { get; set; }
        public Roster.ItemRoster ItemRoster { get; } = new();
    }
}
namespace TaleWorlds.CampaignSystem.Roster
{
    public readonly struct ItemRosterElement
    {
        public readonly TaleWorlds.Core.EquipmentElement EquipmentElement;
        public readonly int Amount;
        public ItemRosterElement(TaleWorlds.Core.EquipmentElement element, int amount) { EquipmentElement = element; Amount = amount; }
    }
    public sealed class ItemRoster : IEnumerable<ItemRosterElement>
    {
        private readonly List<ItemRosterElement> _items = new();
        public Action<TaleWorlds.Core.EquipmentElement, int>? AfterChange;
        public void AddToCounts(TaleWorlds.Core.EquipmentElement element, int change)
        {
            var index = _items.FindIndex(i => AIChronicle.MeetingAssets.Key(i.EquipmentElement) == AIChronicle.MeetingAssets.Key(element));
            var count = checked((index < 0 ? 0 : _items[index].Amount) + change);
            if (count < 0) throw new InvalidOperationException("库存不能为负。");
            if (index >= 0) _items.RemoveAt(index);
            if (count > 0) _items.Add(new ItemRosterElement(element, count));
            AfterChange?.Invoke(element, change);
        }
        public IEnumerator<ItemRosterElement> GetEnumerator() => _items.GetEnumerator();
        public int GetItemNumber(TaleWorlds.Core.ItemObject item) => _items.Where(i => i.EquipmentElement.Item == item).Sum(i => i.Amount);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
namespace TaleWorlds.Core
{
    public sealed class ItemObject { public string StringId { get; set; } = ""; public string Name { get; set; } = ""; }
    public sealed class ItemModifier { public string StringId { get; set; } = ""; }
    public readonly struct EquipmentElement
    {
        public readonly ItemObject Item;
        public readonly ItemModifier? ItemModifier;
        public ItemObject? CosmeticItem => null;
        public bool IsQuestItem => false;
        public int ItemValue => 1;
        public EquipmentElement(ItemObject item, ItemModifier? modifier = null) { Item = item; ItemModifier = modifier; }
        public string GetModifiedItemName() => Item.Name;
    }
}
namespace TaleWorlds.CampaignSystem.Actions
{
    public static class GiveGoldAction
    {
        public static void ApplyBetweenCharacters(Hero donor, Hero receiver, int amount, bool disableNotification)
        { donor.ChangeHeroGold(-amount); receiver.ChangeHeroGold(amount); }
    }
}
namespace AIChronicle
{
    public sealed class MeetingSession
    {
        public long Epoch { get; } = MainThreadExecutor.CampaignEpoch;
        public TaleWorlds.CampaignSystem.Hero Player { get; }
        public TaleWorlds.CampaignSystem.Hero Npc { get; }
        public MeetingSession(TaleWorlds.CampaignSystem.Hero player, TaleWorlds.CampaignSystem.Hero npc) { Player = player; Npc = npc; }
    }
    public interface IMeetingAction
    {
        string? Validate(MeetingSession session, MeetingTerms terms);
        string Describe(MeetingSession session, MeetingTerms terms);
    }
    public static class MeetingClauses
    {
        public static string? Validate(MeetingSession session, MeetingTerms terms) => null;
        public static List<MeetingEffect> Prepare(MeetingSession session, MeetingTerms terms) => new();
    }
}
