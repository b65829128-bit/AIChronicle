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

    public sealed class Kingdom : IFaction
    {
        public string Name { get; set; } = "";
        public Clan? RulingClan { get; set; }
        public Hero? Leader => RulingClan?.Leader;
        public bool IsKingdomFaction => true;
        public bool IsMinorFaction => false;
        public bool IsAtWarWith(IFaction other) => false;
    }

    public sealed class Clan : IFaction
    {
        public string Name { get; set; } = "";
        public Hero? Leader { get; set; }
        public bool IsUnderMercenaryService { get; set; }
        public bool IsKingdomFaction => false;
        public bool IsMinorFaction => false;
        public bool IsAtWarWith(IFaction other) => false;
    }

    public sealed class Hero
    {
        public string Name { get; set; } = "";
        public bool IsFemale { get; set; }
        public bool IsLord { get; set; }
        public bool IsPrisoner { get; set; }
        public Clan? Clan { get; set; }
        public IFaction? MapFaction { get; set; }
        public Hero? Spouse { get; set; }
        public Hero? Mother { get; set; }
        public Hero? Father { get; set; }
    }
}
