using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIChronicle;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;

namespace AIChronicle.Tests
{
    internal static partial class Program
    {
        private static void RunLlmTests()
        {
            Run("usage: DeepSeek flat cache + output reasoning subset", TestDeepSeekUsage);
            Run("usage: Qwen/standard nested cache + zero vs unknown", TestNestedUsage);
            Run("usage: GLM/MiMo input without cache declarations", TestUsageWithoutCache);
            Run("usage: no frame/null fields/non-stream response", TestMissingAndNonStreamUsage);
            Run("budget: foreground excluded + background counts uncached-provider input", TestBackgroundBudget);
            Run("tools: council available in conversation/letter/diplomacy/self-review", TestCouncilTools);
            Run("tools: fixed scopes share exact API/index exclusions", TestToolScopes);
            Run("tools: snapshot survives definition reload + appended budget notice", TestToolSnapshot);
            Run("tools: browse expands once + preserves capability filtering", TestToolBrowse);
            Run("tools: concurrent catalogs + disabled tools are isolated", TestToolIsolation);
            Run("identity: each side keeps its own kingdom/ruler/gender", TestIdentityPair);
            Run("identity: succession/allegiance changes use live objects", TestIdentityRefresh);
            Run("identity: ruler/mercenary/companion/independent roles", TestIdentityRoles);
            Run("identity: unknown ruler + self-interaction", TestIdentityUnknownAndSelf);
            Run("identity: current facts remain after volatile marker", TestIdentityVolatilePlacement);
            Run("identity: live alliances distinguish allies, neutrality and war", TestIdentityAlliances);
        }

        private static void TestDeepSeekUsage()
        {
            var chunk = new DeepSeekProvider().ParseStreamLine(@"{""choices"":[],""usage"":{""prompt_tokens"":1000,""completion_tokens"":80,""completion_tokens_details"":{""reasoning_tokens"":60},""prompt_cache_hit_tokens"":768,""prompt_cache_miss_tokens"":232}}")!;
            var usage = chunk.Usage!;
            AssertEqual<long?>(1000, usage.PromptTokens);
            AssertEqual<long?>(80, usage.CompletionTokens);
            AssertEqual<long?>(60, usage.ReasoningTokens);
            AssertEqual<long?>(768, usage.CacheHit);
            AssertEqual<long?>(232, usage.CacheMiss);
            var total = new LLMUsage();
            total.Add(usage);
            total.Add(usage);
            AssertEqual<long?>(160, total.CompletionTokens, "reasoning not added to output twice");
            AssertTrue(chunk.RawUsageJson.Contains("prompt_cache_hit_tokens"), "raw accounting retained");

            var flatOnly = new DeepSeekProvider().ParseStreamLine(@"{""usage"":{""prompt_cache_hit_tokens"":64,""prompt_cache_miss_tokens"":36}}")!.Usage!;
            AssertEqual<long?>(100, flatOnly.PromptTokens, "flat fields recover missing total");
        }

        private static void TestNestedUsage()
        {
            const string json = @"{""choices"":[],""usage"":{""prompt_tokens"":900,""completion_tokens"":12,""prompt_tokens_details"":{""cached_tokens"":700}}}";
            foreach (var provider in new ILLMProvider[] { new QwenProvider(), new OpenAICompatibleProvider() })
            {
                var usage = provider.ParseStreamLine(json)!.Usage!;
                AssertEqual<long?>(900, usage.PromptTokens);
                AssertEqual<long?>(700, usage.CacheHit);
                AssertEqual<long?>(200, usage.CacheMiss);
                AssertEqual<long?>(null, usage.ReasoningTokens, "missing reasoning stays unknown");
            }
            var zero = new OpenAICompatibleProvider().ParseStreamLine(@"{""usage"":{""prompt_tokens"":100,""completion_tokens"":0,""prompt_tokens_details"":{""cached_tokens"":0}}}")!.Usage!;
            AssertEqual<long?>(0, zero.CacheHit, "reported zero cache hit is known");
            AssertEqual<long?>(100, zero.CacheMiss);
            AssertEqual<long?>(0, zero.CompletionTokens);
        }

        private static void TestUsageWithoutCache()
        {
            foreach (var provider in new ILLMProvider[] { new GLMProvider(), new MiMoProvider(), new OpenAICompatibleProvider() })
            {
                var chunk = provider.ParseStreamLine(@"{""choices"":[],""usage"":{""prompt_tokens"":321,""completion_tokens"":54,""completion_tokens_details"":{""reasoning_tokens"":20}}}")!;
                var usage = chunk.Usage!;
                AssertEqual<long?>(321, usage.PromptTokens, "input independent of cache support");
                AssertEqual<long?>(54, usage.CompletionTokens);
                AssertEqual<long?>(20, usage.ReasoningTokens);
                AssertEqual<long?>(null, usage.CacheHit);
                AssertEqual<long?>(null, usage.CacheMiss);
                AssertTrue(usage.Describe().Contains("命中率=N/A"), "unknown cache is not a false miss");
                AssertTrue(chunk.RawUsageJson.Length > 0, "raw usage available without cache");
            }
        }

        private static void TestMissingAndNonStreamUsage()
        {
            var provider = new OpenAICompatibleProvider();
            AssertTrue(provider.ParseStreamLine("not json") == null, "invalid line ignored");
            AssertTrue(provider.ParseStreamLine(@"{""choices"":[],""usage"":null}")!.Usage == null, "no usage frame");
            AssertTrue(provider.ParseStreamLine(@"{""choices"":[{""delta"":{""content"":""hi""}}]}")!.Usage == null, "text frame is not accounting");
            var empty = provider.ParseStreamLine(@"{""usage"":{""prompt_tokens"":null,""completion_tokens"":null,""prompt_tokens_details"":{""cached_tokens"":null}}}")!.Usage!;
            AssertEqual<long?>(null, empty.PromptTokens);
            AssertEqual<long?>(null, empty.CacheHit);
            var result = provider.ParseNonStreamResponse(@"{""choices"":[{""message"":{""content"":""ok""}}],""usage"":{""prompt_tokens"":17,""completion_tokens"":2}}");
            AssertEqual("ok", result.Content);
            AssertEqual<long?>(17, result.Usage!.PromptTokens);
            AssertEqual<long?>(2, result.Usage.CompletionTokens);
        }

        private static void TestBackgroundBudget()
        {
            MySettings.Instance = new MySettings { BackgroundTokenBudgetPer5Min = 300 };
            TokenBudget.Reset();
            try
            {
                TokenBudget.Record("conversation", 10000);
                TokenBudget.Record("chancery", 10000);
                AssertEqual(0L, TokenBudget.CurrentTokens, "foreground does not consume background allowance");
                var usage = new MiMoProvider().ParseStreamLine(@"{""usage"":{""prompt_tokens"":321}}")!.Usage!;
                TokenBudget.Record("self_review", usage.PromptTokens ?? 0);
                AssertEqual(321L, TokenBudget.CurrentTokens, "input counted without cache metadata");
                AssertTrue(TokenBudget.IsOverBudget, "background allowance enforced");
                AssertTrue(TokenBudget.IsBackgroundIntent("chat"), "check-in is background");
                AssertTrue(TokenBudget.IsBackgroundIntent("letter"), "letter processing is background");
                AssertFalse(TokenBudget.IsDeferrable(ActivationEventType.CuriaVote), "votes keep deadline exemption");
                AssertFalse(TokenBudget.IsDeferrable(ActivationEventType.YearlyChronicle), "historian exemption retained");
                AssertTrue(TokenBudget.IsDeferrable(ActivationEventType.SelfReview), "self-review can wait");
            }
            finally
            {
                TokenBudget.Reset();
                MySettings.Instance = null;
            }
        }

        private static List<ToolDef> LoadToolDefinitions()
        {
            return new[] { "tools.json", "agent_tools.json" }.SelectMany(name =>
                JsonConvert.DeserializeObject<List<ToolDef>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Prompts", name)))!).ToList();
        }

        private static string[] ApiToolNames(ToolCatalog catalog)
            => catalog.ApiTools.Select(t => t["function"]!["name"]!.Value<string>()!).ToArray();

        private static void AssertToolIndex(ToolCatalog catalog)
        {
            var listed = catalog.BuildFunctionList().Split('\n').Where(l => l.StartsWith("【") && !l.StartsWith("【其他"))
                .SelectMany(l => l.Substring(l.IndexOf('】') + 1).Trim().Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries));
            AssertSequence(ApiToolNames(catalog).OrderBy(n => n, StringComparer.Ordinal).ToArray(), listed.OrderBy(n => n, StringComparer.Ordinal), "API and prompt index match");
        }

        private static void TestCouncilTools()
        {
            var definitions = LoadToolDefinitions();
            foreach (var intent in new[] { "conversation", "letter", "diplomacy", "self_review" })
            {
                var catalog = new ToolCatalog(definitions, intent);
                AssertTrue(ApiToolNames(catalog).Contains("propose_agenda"), intent + " can propose agenda");
                AssertToolIndex(catalog);
            }
            var limited = new ToolCatalog(definitions.Where(t => t.Category != "council"), "conversation");
            AssertFalse(ApiToolNames(limited).Contains("propose_agenda"), "categories never bypass capability filtering");
            AssertFalse(limited.TryUnlock("council"), "browse cannot resurrect a denied capability");
        }

        private static void TestToolScopes()
        {
            var definitions = LoadToolDefinitions();
            foreach (var intent in new[] { "self_review", "chancery", "curia", "curia_defense", "king_consult", "envoy_reply", "historian", "consolidation" })
            {
                var catalog = new ToolCatalog(definitions, intent, new[] { "military", "diplomacy", "communication" });
                AssertToolIndex(catalog);
                AssertFalse(catalog.BuildFunctionList().Contains("browse_tools"), intent + " must not advertise unavailable browsing");
                AssertFalse(catalog.TryUnlock("military"), intent + " has a fixed menu");
            }
            var self = ApiToolNames(new ToolCatalog(definitions, "self_review"));
            AssertFalse(self.Contains("recruit_troops") || self.Contains("send_letter"), "chain/cascade exclusions retained");
            var chancery = ApiToolNames(new ToolCatalog(definitions, "chancery"));
            AssertFalse(chancery.Contains("write_file") || chancery.Contains("propose_agenda"), "chancery remains read-only");
            AssertFalse(ApiToolNames(new ToolCatalog(definitions, "king_consult")).Contains("consult_king"), "consult loop prevented");
            AssertFalse(ApiToolNames(new ToolCatalog(definitions, "envoy_reply")).Contains("send_envoy"), "envoy loop prevented");
            AssertFalse(ApiToolNames(new ToolCatalog(definitions, "curia")).Contains("query_surroundings"), "curia query menu retained");
        }

        private static JObject BuildTestRequest(ToolCatalog catalog, List<JToken> messages)
        {
            using var request = new DeepSeekProvider().BuildRequest(new LLMRequest
            {
                Url = "https://llm.invalid/chat/completions", Model = "offline-fixture", ApiKey = "",
                IncludeTools = true, Tools = catalog.ApiTools, Messages = messages
            });
            return JObject.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        }

        private static void TestToolSnapshot()
        {
            var definitions = LoadToolDefinitions();
            var catalog = new ToolCatalog(definitions, "conversation");
            var hash = catalog.SchemaHash;
            var messages = new List<JToken> { new JObject { ["role"] = "system", ["content"] = "stable" } };
            var before = BuildTestRequest(catalog, messages);
            definitions[0].Description = "hot reload changed a definition";
            definitions[0].Parameters[0].Description = "hot reload changed a parameter";
            definitions.Clear();
            messages.Add(new JObject { ["role"] = "system", ["content"] = "query budget exhausted: act or reply" });
            var after = BuildTestRequest(catalog, messages);
            AssertTrue(JToken.DeepEquals(before["tools"], after["tools"]), "budget notice and definition reload preserve exact schemas");
            AssertEqual(hash, catalog.SchemaHash);
            AssertTrue(ApiToolNames(catalog).Contains("query_world_state"), "query schema retained when execution is budget-limited");
            AssertTrue(JToken.DeepEquals(before["messages"]![0], after["messages"]![0]), "old prefix unchanged");
        }

        private static void TestToolBrowse()
        {
            var definitions = LoadToolDefinitions();
            // 模拟能力门控只给这位 NPC 提供一种军事动作。
            definitions.RemoveAll(t => t.Category == "military" && t.Name != "form_army");
            var catalog = new ToolCatalog(definitions, "chat");
            var initialHash = catalog.SchemaHash;
            AssertFalse(ApiToolNames(catalog).Contains("form_army"), "military deferred in check-ins");
            AssertTrue(catalog.TryUnlock("military"), "explicit unlock accepted");
            AssertTrue(initialHash != catalog.SchemaHash, "explicit unlock changes snapshot");
            AssertSequence(new[] { "form_army" }, catalog.GetCategoryTools("military").Select(t => t.Name), "browse is capability-filtered");
            AssertToolIndex(catalog);
            var unlockedHash = catalog.SchemaHash;
            var unlockedApi = catalog.ApiTools;
            AssertTrue(catalog.TryUnlock("military"), "already unlocked is harmless");
            AssertEqual(unlockedHash, catalog.SchemaHash, "repeated browse does not rebuild definitions");
            AssertTrue(ReferenceEquals(unlockedApi, catalog.ApiTools), "unchanged schema snapshot reused");
            AssertFalse(catalog.TryUnlock("missing-category"), "unknown category rejected");
        }

        private static void TestToolIsolation()
        {
            var definitions = LoadToolDefinitions();
            var first = new ToolCatalog(definitions, "chat");
            var second = new ToolCatalog(definitions, "chat");
            first.TryUnlock("military");
            AssertFalse(ApiToolNames(second).Contains("form_army"), "another activation remains isolated");
            var disabled = new ToolCatalog(definitions, "conversation", includeTools: false);
            AssertEqual(0, disabled.ApiTools.Count, "disabled tools have no schema");
            AssertEqual("", disabled.BuildFunctionList(), "disabled tools not advertised");
            AssertFalse(disabled.TryUnlock("military"), "disabled tools cannot be re-enabled by browse");
        }

        private static (Hero Agent, Hero Target, Kingdom North) IdentityScene()
        {
            var queen = new Hero { Name = "拉盖娅", IsFemale = true, IsLord = true };
            var king = new Hero { Name = "卢孔", IsLord = true };
            var south = new Kingdom { Name = "南帝国", RulingClan = new Clan { Leader = queen } };
            var north = new Kingdom { Name = "北帝国", RulingClan = new Clan { Leader = king } };
            var agent = new Hero
            {
                Name = "俄布戎", IsLord = true, MapFaction = south,
                Clan = new Clan { Name = "翁革洛斯", Leader = new Hero { Name = "族长" } }
            };
            var target = new Hero { Name = "炎瑰", IsLord = true, MapFaction = north, Clan = new Clan { Name = "炎氏" } };
            target.Clan.Leader = target;
            return (agent, target, north);
        }

        private static void TestIdentityPair()
        {
            var (agent, target, _) = IdentityScene();
            var context = ContextBuilder.BuildObjectiveRelationship(agent, target);
            AssertTrue(context.Contains("你：俄布戎（男）；家族：翁革洛斯（成员）；所属王国：南帝国；政治身份：封臣"), "NPC public identity explicit");
            AssertTrue(context.Contains("你所属王国的统治者：拉盖娅（女）"), "NPC queen correctly assigned");
            AssertTrue(context.Contains("对方：炎瑰（男）；家族：炎氏（领袖）；所属王国：北帝国；政治身份：封臣"), "player public identity explicit");
            AssertTrue(context.Contains("对方所属王国的统治者：卢孔（男）"), "player king correctly assigned");
            AssertFalse(context.Contains("对方所属王国的统治者：拉盖娅"), "no ruler crossover");
        }

        private static void TestIdentityRefresh()
        {
            var (agent, target, north) = IdentityScene();
            var before = ContextBuilder.BuildObjectiveRelationship(agent, target);
            north.RulingClan = new Clan { Leader = new Hero { Name = "新君主", IsFemale = true, IsLord = true } };
            var after = ContextBuilder.BuildObjectiveRelationship(agent, target);
            AssertTrue(before.Contains("对方所属王国的统治者：卢孔（男）"), "old ruler initially present");
            AssertTrue(after.Contains("对方所属王国的统治者：新君主（女）"), "succession read without cache refresh");
            AssertFalse(after.Contains("卢孔"), "old ruler removed");
            target.MapFaction = agent.MapFaction;
            var joined = ContextBuilder.BuildObjectiveRelationship(agent, target);
            AssertTrue(joined.Contains("对方所属王国的统治者：拉盖娅（女）"), "allegiance change reflected");
            AssertFalse(joined.Contains("北帝国"), "no old kingdom retained");
            target.MapFaction = target.Clan;
            var independent = ContextBuilder.BuildObjectiveRelationship(agent, target);
            AssertTrue(independent.Contains("所属王国：无；政治身份：独立家族领袖"), "departure reflected");
        }

        private static void TestIdentityRoles()
        {
            var (agent, target, north) = IdentityScene();
            north.RulingClan = target.Clan;
            var ruler = ContextBuilder.BuildObjectiveRelationship(agent, target);
            AssertTrue(ruler.Contains("所属王国：北帝国；政治身份：王国统治者"), "ruler distinct from vassal");
            AssertTrue(ruler.Contains("对方所属王国的统治者：炎瑰（男）"), "self is current ruler");
            north.RulingClan = null;
            target.Clan!.IsUnderMercenaryService = true;
            var mercenary = ContextBuilder.BuildObjectiveRelationship(agent, target);
            AssertTrue(mercenary.Contains("所属王国：北帝国；政治身份：雇佣兵首领"), "contract defines mercenary role");
            target.Clan.IsUnderMercenaryService = false;
            target.IsLord = false;
            var clanLeader = ContextBuilder.BuildObjectiveRelationship(agent, target);
            AssertTrue(clanLeader.Contains("所属王国：北帝国；政治身份：封臣"), "clan leader membership does not depend on occupation");
            target.Clan.Leader = new Hero { Name = "族长" };
            var companion = ContextBuilder.BuildObjectiveRelationship(agent, target);
            AssertTrue(companion.Contains("所属王国：北帝国；政治身份：王国家族成员"), "companion not mislabeled vassal");
            target.Clan = null;
            var resident = ContextBuilder.BuildObjectiveRelationship(agent, target);
            AssertTrue(resident.Contains("家族：无；所属王国：北帝国；政治身份：王国居民"), "resident does not acquire a noble clan");
        }

        private static void TestIdentityUnknownAndSelf()
        {
            var (agent, target, north) = IdentityScene();
            north.RulingClan = null;
            var unknown = ContextBuilder.BuildObjectiveRelationship(agent, target);
            AssertTrue(unknown.Contains("对方所属王国的统治者：未知"), "missing ruler explicit");
            AssertFalse(unknown.Contains("统治者：未知（男）"), "unknown gender not guessed male");
            var self = ContextBuilder.BuildObjectiveRelationship(agent, agent);
            AssertTrue(self.Contains("你所属王国的统治者：拉盖娅（女）"), "self-review retains live identity");
            AssertFalse(self.Contains("对方所属王国的统治者"), "self-review does not invent a second person");
            AssertTrue(self.Contains("对方就是你自己（自省）"), "self-interaction semantics retained");
            target.Clan = null;
            target.MapFaction = null;
            var unaffiliated = ContextBuilder.BuildObjectiveRelationship(agent, target);
            AssertTrue(unaffiliated.Contains("家族：无；所属王国：无；政治身份：无王国身份"), "no fabricated political role");
        }

        private static void TestIdentityVolatilePlacement()
        {
            var (agent, target, north) = IdentityScene();
            var template = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Prompts", "context_template.txt"));
            var before = template.Replace("{objective_relationship}", ContextBuilder.BuildObjectiveRelationship(agent, target));
            north.RulingClan = new Clan { Leader = new Hero { Name = "继任女王", IsFemale = true } };
            var after = template.Replace("{objective_relationship}", ContextBuilder.BuildObjectiveRelationship(agent, target));
            var marker = before.IndexOf("<!--VOLATILE-->", StringComparison.Ordinal);
            AssertTrue(marker >= 0, "template volatile marker present");
            AssertEqual(before.Substring(0, marker), after.Substring(0, marker), "ruler change preserves stable prefix");
            AssertTrue(before.Substring(marker).Contains("对方所属王国的统治者：卢孔（男）"), "old current fact in suffix");
            AssertTrue(after.Substring(marker).Contains("对方所属王国的统治者：继任女王（女）"), "new current fact in suffix");
        }

        private sealed class IdentityAllianceBehavior : TaleWorlds.CampaignSystem.CampaignBehaviorBase,
            TaleWorlds.CampaignSystem.CampaignBehaviors.IAllianceCampaignBehavior
        {
            public bool Active;
            public bool IsAllyWithKingdom(TaleWorlds.CampaignSystem.Kingdom first, TaleWorlds.CampaignSystem.Kingdom second)
                => Active && !first.IsEliminated && !second.IsEliminated;
            public override void RegisterEvents() { }
            public override void SyncData(TaleWorlds.CampaignSystem.IDataStore store) { }
        }

        private static void TestIdentityAlliances()
        {
            var original = Campaign.Current;
            try
            {
                Campaign.Current = new Campaign();
                var (agent, target, north) = IdentityScene();
                var south = (Kingdom)agent.MapFaction!;
                var alliance = new IdentityAllianceBehavior { Active = true };
                Campaign.Current.Behaviors[typeof(TaleWorlds.CampaignSystem.CampaignBehaviors.IAllianceCampaignBehavior)] = alliance;
                var allied = ContextBuilder.BuildObjectiveRelationship(agent, target);
                AssertTrue(allied.Contains("已缔结军事同盟") && allied.Contains("盟友关系"), "existing alliance is a live fact");
                AssertFalse(allied.Contains("中立关系"), "allies must not be neutral");
                target.Clan!.IsUnderMercenaryService = true;
                AssertTrue(ContextBuilder.BuildObjectiveRelationship(agent, target).Contains("盟友关系"), "mercenary realm affiliation also uses live alliance");
                alliance.Active = false;
                AssertTrue(ContextBuilder.BuildObjectiveRelationship(agent, target).Contains("中立关系"), "ended alliance immediately stops being allied");
                alliance.Active = true; south.Hostile = true;
                var war = ContextBuilder.BuildObjectiveRelationship(agent, target);
                AssertTrue(war.Contains("交战状态"), "war takes priority over alliance residue");
                AssertFalse(war.Contains("盟友关系"), "contradictory alliance cannot override war");
                south.Hostile = false; Campaign.Current.Behaviors.Clear();
                var unknown = ContextBuilder.BuildObjectiveRelationship(agent, target);
                AssertTrue(unknown.Contains("当前未交战") && unknown.Contains("盟约状态尚未核实"), "missing behavior does not fabricate neutrality or alliance");
                AssertFalse(unknown.Contains("中立关系"), "unknown is not asserted neutral");
                target.MapFaction = south;
                AssertTrue(ContextBuilder.BuildObjectiveRelationship(agent, target).Contains("同属"), "same realm remains a domestic relationship");
            }
            finally { Campaign.Current = original; }
        }
    }
}

// TokenBudget 的纯逻辑测试桩，不加载 MCM/TaleWorlds，也不模拟游戏状态。
namespace AIChronicle
{
    public sealed class MySettings
    {
        public static MySettings? Instance { get; set; }
        public int BackgroundTokenBudgetPer5Min { get; set; }
    }

    public enum ActivationEventType { CuriaVote, CuriaDefense, YearlyChronicle, SpecialChronicle, SelfReview }

    public static class DebugLogger
    {
        public static void Log(string message) { }
    }
}
