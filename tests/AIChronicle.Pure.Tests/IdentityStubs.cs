// 身份采集的最小只读测试桩：仅提供被读取的属性；不加载或模拟游戏决策系统。
namespace TaleWorlds.CampaignSystem
{
    public interface IFaction
    {
        string Name { get; }
        bool IsKingdomFaction { get; }
        bool IsMinorFaction { get; }
        bool IsAtWarWith(IFaction other);
    }

    public sealed partial class Kingdom : IFaction
    {
        public string Name { get; set; } = "";
        public Clan? RulingClan { get; set; }
        public Hero? Leader => RulingClan?.Leader;
        public bool IsKingdomFaction => true;
        public bool IsMinorFaction => false;
        public bool IsAtWarWith(IFaction other) => false;
    }

    public sealed partial class Clan : IFaction
    {
        public string Name { get; set; } = "";
        public Hero? Leader { get; set; }
        public bool IsUnderMercenaryService { get; set; }
        public bool IsKingdomFaction => false;
        public bool IsMinorFaction { get; set; }
        public bool Hostile { get; set; }
        public bool IsAtWarWith(IFaction other) => Hostile;
    }

    public sealed partial class Hero
    {
        public string Name { get; set; } = "";
        public bool IsFemale { get; set; }
        public bool IsLord { get; set; }
        public bool IsPrisoner { get; set; }
        private Clan? _clan;
        public Clan? Clan
        {
            get => _clan;
            set
            {
                if (value == _clan) return;
                _clan?.Heroes.Remove(this); _clan = value; value?.Heroes.Add(this);
                AfterClanChange?.Invoke();
            }
        }
        public IFaction? MapFaction { get; set; }
        public Hero? Spouse { get; set; }
        public Hero? Mother { get; set; }
        public Hero? Father { get; set; }
    }
}
