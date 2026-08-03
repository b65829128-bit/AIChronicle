using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace AIChronicle
{
    public enum EntityController
    {
        Human,
        Agent
    }

    public enum EntityCapability
    {
        FileSystem,
        MoveParty,
        WaitAtSettlement,
        GiveGold,
        RequestGold,
        ChangeRelation,
        SendLetter,
        Diplomat,
        CreateClan
    }

    public class Entity
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string Culture { get; set; } = "";
        public EntityController Controller { get; set; }
        public Hero? HeroRef { get; set; }
        public HashSet<EntityCapability> Capabilities { get; set; } = new();

        public bool HasCapability(EntityCapability cap)
        {
            return Capabilities.Contains(cap);
        }
    }
}
