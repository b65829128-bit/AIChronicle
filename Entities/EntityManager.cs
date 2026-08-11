using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace AIChronicle
{
    public static class EntityManager
    {
        private static string _npcBaseDir = "";
        // 并发修复：后台 Agent 任务与主线程聊天会同时读写实体缓存，普通 Dictionary 并发访问会抛异常。
        private static readonly ConcurrentDictionary<string, Entity> _entityCache = new();
        private static readonly ConcurrentDictionary<Hero, Entity> _heroToEntity = new();

        // 并发修复：活动交互上下文改为 AsyncLocal，每个异步流程持有自己的值，互不覆盖。
        private static readonly AsyncLocal<string?> _activeAgentId = new();
        private static readonly AsyncLocal<string?> _activeTargetId = new();

        public static Entity? ActiveAgent
        {
            get
            {
                var id = _activeAgentId.Value;
                return id != null && _entityCache.TryGetValue(id, out var e) ? e : null;
            }
        }

        public static Entity? ActiveTarget
        {
            get
            {
                var id = _activeTargetId.Value;
                return id != null && _entityCache.TryGetValue(id, out var e) ? e : null;
            }
        }

        public static string? ActiveAgentId => _activeAgentId.Value;
        public static string? ActiveTargetId => _activeTargetId.Value;

        public static void Initialize(string npcBaseDir)
        {
            _npcBaseDir = npcBaseDir;
            Directory.CreateDirectory(_npcBaseDir);
        }

        /// <summary>战役结束/切档时清空跨档残留，避免新档命中旧档的实体缓存（旧 Hero 引用/陈旧能力）。</summary>
        public static void ResetForNewCampaign()
        {
            _entityCache.Clear();
            _heroToEntity.Clear();
            _activeAgentId.Value = null;
            _activeTargetId.Value = null;
            _npcBaseDir = "";
        }

        public static void ActivateInteraction(Hero agentHero, Hero targetHero)
        {
            var agent = GetOrCreateEntity(agentHero);
            var target = GetOrCreateEntity(targetHero);
            _activeAgentId.Value = agent.Id;
            _activeTargetId.Value = target.Id;
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
                        EntityCapability.SendLetter,
                        EntityCapability.Chronicler
                    }
                };
                _entityCache[historianId] = historian;
            }

            _activeAgentId.Value = historianId;
            _activeTargetId.Value = historianId;
            AgentManager.Activate(historianId, historianId);
        }

        /// <summary>天意（家族补充）：观照天下家族兴衰的虚拟实体，当封臣/雇佣兵家族凋零时补充新的贵族家族。</summary>
        public static void ActivateFate()
        {
            var fateId = "__fate__";
            if (!_entityCache.TryGetValue(fateId, out var fate))
            {
                fate = new Entity
                {
                    Id = fateId,
                    Name = "天意",
                    Title = "卡拉迪亚命运的天意",
                    Culture = "帝国",
                    Controller = EntityController.Agent,
                    HeroRef = null,
                    Capabilities = new HashSet<EntityCapability>
                    {
                        EntityCapability.FileSystem,
                        EntityCapability.SendLetter,
                        EntityCapability.CreateClan
                    }
                };
                _entityCache[fateId] = fate;
            }

            _activeAgentId.Value = fateId;
            _activeTargetId.Value = fateId;
            AgentManager.Activate(fateId, fateId);
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

        /// <summary>角色状态变化后刷新缓存的实体头衔与能力（如氏族领袖建国称王后需立即获得 Diplomat 国王工具门控）。
        /// 实体缓存仅在建档/首次遇到时计算一次，中途角色转变（禅位/建国/继任）会过期——本方法在此补刷新。</summary>
        public static void RefreshEntity(Hero hero)
        {
            if (hero == null) return;
            if (_heroToEntity.TryGetValue(hero, out var entity))
            {
                entity.Title = ComputeTitle(hero);
                entity.Capabilities = ComputeCapabilities(hero, entity.Controller);
            }
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
            var isNoble = hero.Clan != null;
            if (controller == EntityController.Agent)
            {
                caps.Add(EntityCapability.FileSystem);
                if (isNoble)
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
            if (isNoble)
            {
                caps.Add(EntityCapability.RequestGold);
            }
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
        }

        private static string SanitizeDir(string name)
        {
            foreach (var c in Path.GetInvalidPathChars()) name = name.Replace(c, '_');
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
    }
}
