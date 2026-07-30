using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace MyFirstMod
{
    public static class EntityManager
    {
        private static string _npcBaseDir = "";
        private static readonly Dictionary<string, Entity> _entityCache = new();
        private static readonly Dictionary<Hero, Entity> _heroToEntity = new();

        private static string? _activeAgentId;
        private static string? _activeTargetId;

        public static Entity? ActiveAgent => _activeAgentId != null && _entityCache.TryGetValue(_activeAgentId, out var e) ? e : null;
        public static Entity? ActiveTarget => _activeTargetId != null && _entityCache.TryGetValue(_activeTargetId, out var e) ? e : null;
        public static string? ActiveAgentId => _activeAgentId;
        public static string? ActiveTargetId => _activeTargetId;

        public static void Initialize(string npcBaseDir)
        {
            _npcBaseDir = npcBaseDir;
            Directory.CreateDirectory(_npcBaseDir);
        }

        public static void ActivateInteraction(Hero agentHero, Hero targetHero)
        {
            var agent = GetOrCreateEntity(agentHero);
            var target = GetOrCreateEntity(targetHero);
            _activeAgentId = agent.Id;
            _activeTargetId = target.Id;
            AgentManager.Activate(agent.Id, target.Id);
        }

        public static void ActivateHistorian()
        {
            var historianId = "__historian__";
            if (!_entityCache.TryGetValue(historianId, out var historian))
            {
                historian = new Entity
                {
                    Id = historianId,
                    Name = "史官",
                    Title = "卡拉迪亚编年史官",
                    Culture = "帝国",
                    Controller = EntityController.Agent,
                    HeroRef = null,
                    Capabilities = new HashSet<EntityCapability>
                    {
                        EntityCapability.FileSystem,
                        EntityCapability.SendLetter
                    }
                };
                _entityCache[historianId] = historian;
            }

            _activeAgentId = historianId;
            _activeTargetId = historianId;
            AgentManager.Activate(historianId, historianId);
        }

        public static Entity GetOrCreateEntity(Hero hero)
        {
            if (_heroToEntity.TryGetValue(hero, out var existing))
                return existing;

            var id = GenerateEntityId(hero);
            if (_entityCache.TryGetValue(id, out var cached))
            {
                _heroToEntity[hero] = cached;
                return cached;
            }

            var entity = new Entity
            {
                Id = id,
                Name = hero.Name?.ToString() ?? "未知",
                Culture = hero.Culture?.Name?.ToString() ?? "未知",
                Controller = hero == Hero.MainHero ? EntityController.Human : EntityController.Agent,
                HeroRef = hero
            };

            entity.Title = ComputeTitle(hero);
            entity.Capabilities = ComputeCapabilities(hero, entity.Controller);
            InitEntityDirectory(entity);

            _entityCache[id] = entity;
            _heroToEntity[hero] = entity;
            return entity;
        }

        public static Entity? GetEntityById(string id)
        {
            return _entityCache.TryGetValue(id, out var e) ? e : null;
        }

        public static Entity? GetOrCreateEntityById(string id)
        {
            if (_entityCache.TryGetValue(id, out var cached))
                return cached;

            if (Hero.MainHero != null)
            {
                var mainId = GenerateEntityId(Hero.MainHero);
                if (mainId == id)
                    return GetOrCreateEntity(Hero.MainHero);
            }

            foreach (var hero in Hero.AllAliveHeroes)
            {
                if (GenerateEntityId(hero) == id)
                    return GetOrCreateEntity(hero);
            }

            foreach (var kv in _entityCache)
            {
                if (kv.Value.HeroRef?.StringId == id)
                    return kv.Value;
            }

            return null;
        }

        public static string? ResolveEntityId(string idOrName)
        {
            if (_entityCache.TryGetValue(idOrName, out _))
                return idOrName;

            foreach (var kv in _entityCache)
            {
                if (kv.Value.Name == idOrName)
                    return kv.Key;
            }

            foreach (var hero in Hero.AllAliveHeroes)
            {
                if (hero.Name?.ToString() == idOrName)
                {
                    var entity = GetOrCreateEntity(hero);
                    return entity.Id;
                }
            }
            return null;
        }

        public static Entity? GetEntityByHero(Hero hero)
        {
            return _heroToEntity.TryGetValue(hero, out var e) ? e : null;
        }

        public static string GetEntityDirectory(string entityId)
        {
            return Path.Combine(_npcBaseDir, SanitizeDir(entityId));
        }

        private static string GenerateEntityId(Hero hero)
        {
            var name = hero.Name?.ToString()?.Trim();
            if (string.IsNullOrEmpty(name)) name = "unknown";

            var namePart = SanitizeDir(name);

            if (!string.IsNullOrEmpty(hero.StringId))
                return namePart + "_" + SanitizeDir(hero.StringId);

            var hash = Math.Abs(namePart.GetHashCode()).ToString("X4");
            return namePart + "_" + hash;
        }

        private static string ComputeTitle(Hero hero)
        {
            var parts = new List<string>();

            if (hero.Clan?.Kingdom?.RulingClan?.Leader == hero)
                parts.Add(hero.Clan.Kingdom.Name?.ToString() + "统治者");
            if (hero.Clan?.Leader == hero)
                parts.Add(hero.Clan.Name?.ToString() + "领袖");
            else if (hero.Clan != null)
                parts.Add(hero.Clan.Name + "成员");

            if (hero == Hero.MainHero && parts.Count == 0)
                parts.Add("冒险者");

            return parts.Count > 0 ? string.Join("、", parts) : "旅行者";
        }

        private static HashSet<EntityCapability> ComputeCapabilities(Hero hero, EntityController controller)
        {
            var caps = new HashSet<EntityCapability>();
            if (controller == EntityController.Agent)
            {
                caps.Add(EntityCapability.FileSystem);
                caps.Add(EntityCapability.SendLetter);
            }
            if (hero.PartyBelongedTo != null && hero.PartyBelongedTo.LeaderHero == hero)
            {
                caps.Add(EntityCapability.MoveParty);
                caps.Add(EntityCapability.WaitAtSettlement);
            }
            if (hero.Clan?.Kingdom?.RulingClan?.Leader == hero)
                caps.Add(EntityCapability.Diplomat);
            if (hero.Gold > 0)
                caps.Add(EntityCapability.GiveGold);
            caps.Add(EntityCapability.RequestGold);
            caps.Add(EntityCapability.ChangeRelation);
            return caps;
        }

        private static void InitEntityDirectory(Entity entity)
        {
            var dir = GetEntityDirectory(entity.Id);
            if (Directory.Exists(dir)) return;
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "knowledge"));
            Directory.CreateDirectory(Path.Combine(dir, "relationships"));
            Directory.CreateDirectory(Path.Combine(dir, "goals"));
            Directory.CreateDirectory(Path.Combine(dir, "chat_logs"));
            Directory.CreateDirectory(Path.Combine(dir, "decisions"));
            Directory.CreateDirectory(Path.Combine(dir, "mailbox", "inbox"));
            Directory.CreateDirectory(Path.Combine(dir, "mailbox", "sent"));
        }

        private static string SanitizeDir(string name)
        {
            foreach (var c in Path.GetInvalidPathChars()) name = name.Replace(c, '_');
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
    }
}
