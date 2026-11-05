using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace AIChronicle.Tests
{
    internal static partial class Program
    {
        // 可选实 DLL 检查，不加载引擎或进入战役；默认纯逻辑回归仍无需游戏 DLL。
        private static int SmokeDuelMission(string[] args)
        {
            if (args.Length != 3)
            { Console.Error.WriteLine("Usage: --duel-mission-smoke <AIChronicle.dll> <game directory>"); return 2; }
            var module = Path.GetFullPath(args[1]);
            var game = Path.GetFullPath(args[2]);
            var directories = new[]
            {
                Path.GetDirectoryName(module)!, Path.Combine(game, "bin/Win64_Shipping_Client"),
                Path.Combine(game, "Modules/SandBox/bin/Win64_Shipping_Client"), Path.Combine(game, "Modules/Native/bin/Win64_Shipping_Client")
            };
            Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
            {
                foreach (var directory in directories)
                {
                    var path = Path.Combine(directory, name.Name + ".dll");
                    if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
                }
                return null;
            }
            AssemblyLoadContext.Default.Resolving += Resolve;
            try
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(module);
                var factory = assembly.GetType("AIChronicle.DuelMission", true)!
                    .GetMethod("CreateBehaviors", BindingFlags.NonPublic | BindingFlags.Static)!;
                foreach (var arena in new[] { false, true })
                {
                    // 装配本身不得访问战役/原生引擎；这里不启动 Mission 的生命周期。
                    var behaviors = (Array)factory.Invoke(null, new object?[] { null, arena })!;
                    var names = behaviors.Cast<object>().Select(b => b.GetType().FullName!).ToArray();
                    AssertEqual(arena ? 1 : 0, names.Count(n => n == "SandBox.Missions.MissionLogics.MissionAgentHandler"));
                    AssertEqual(1, names.Count(n => n == "AIChronicle.DuelMission"));
                    AssertEqual(1, names.Count(n => n == "AIChronicle.DuelCampaignMissionComponent"));
                    AssertFalse(names.Any(n => n.EndsWith(".ArenaAgentStateDeciderLogic") || n.EndsWith(".BattleAgentLogic")),
                        "duel must not force arena knockouts or dispatch whole-party battle outcomes");
                    Console.WriteLine((arena ? "arena" : "field") + ": " + names.Length
                        + " production components, settlement handler=" + arena + "; composition passed");
                }
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("Duel mission component check failed: " + ex); return 1; }
            finally { AssemblyLoadContext.Default.Resolving -= Resolve; }
        }

        private static void RunDuelTests()
        {
            Run("duel: agreed branches wait for explicit start, version and replay guards", TestDuelDeferred);
            Run("duel: closed running meeting still resolves, unstarted closes cancel", TestDuelClose);
            Run("duel: strict conditional schema, independent copies and maximum escrow", TestDuelTerms);
            Run("duel: mutual defeat, forfeit, abort and exactly one settlement", TestDuelResolution);
            Run("duel: assets stay settled when a personal action cannot be fulfilled", TestDuelSettlement);
            Run("duel reward: calibrated samples, repeats, eligibility and JSON replay", TestDuelRewards);
            Run("duel reward: monotonic inputs, role ordering and tier continuity", TestDuelRewardProperties);
            Run("duel escrow: either result conserves gold, exact items and unused reserves", TestDuelEscrowOutcomes);
            Run("duel escrow: abort refunds, preserves outside changes and rejects stale epoch", TestDuelEscrowRefund);
            Run("duel escrow: faults after removal/delivery do not duplicate or destroy assets", TestDuelEscrowFailures);
            Run("duel store: old-save defaults, save/load branch, direction and seven-day boundary", TestDuelSaveLedger);
            Run("duel audit: atomic snapshots and production replay reject tampering", TestDuelReplayAudit);
            Run("duel return: leave flag precedes native callback, no automatic army battle", TestDuelEncounterCallback);
            Run("duel return: fallback closes only original meeting once without forced town exit", TestDuelEncounterFallback);
            Run("duel return: town/village/location and missing encounter stay intact", TestDuelEncounterSettlement);
            Run("duel return: scene/conversation must close before fallback cleanup", TestDuelEncounterProtected);
            Run("duel return: replacement encounters and previous campaigns are protected", TestDuelEncounterOwnership);
        }

        private static DuelEncounter OpenDuelEncounter(out PlayerEncounter encounter)
        {
            encounter = ResetEncounterTest();
            Campaign.Current!.ConversationManager!.IsConversationInProgress = true;
            var map = (MapState)Game.Current!.GameStateManager!.ActiveState!;
            map.MapConversationActive = true; map.AtMenu = true;
            return new DuelEncounter(encounter, MainThreadExecutor.CampaignEpoch);
        }
        private static void CloseDuelConversation()
        {
            Campaign.Current!.ConversationManager!.IsConversationInProgress = false;
            ((MapState)Game.Current!.GameStateManager!.ActiveState!).MapConversationActive = false;
        }
        private static void TestDuelEncounterCallback()
        {
            var exit = OpenDuelEncounter(out var encounter);
            var battleStarts = 0;
            exit.EndConversation(() =>
            {
                CloseDuelConversation();
                // 原版 encounter_meeting_on_init 的关键分支；不模拟整套菜单或战斗。
                if (PlayerEncounter.LeaveEncounter) PlayerEncounter.Finish();
                else { battleStarts++; encounter.MapEvent = new object(); }
            });
            AssertEqual(0, battleStarts, "the native callback must see leave before it can StartBattle");
            AssertTrue(exit.Finish(), "native meeting already closed");
            AssertTrue(exit.Finish(), "return after duel does not reopen encounter");
            AssertTrue(exit.Detached, "map meeting detached");
            AssertEqual(1, PlayerEncounter.FinishCalls);
            AssertTrue(PlayerEncounter.Current == null && PlayerEncounter.Battle == null, "no army battle remains");
            AssertFalse(((MapState)Game.Current!.GameStateManager!.ActiveState!).AtMenu, "return to free map");
        }
        private static void TestDuelEncounterFallback()
        {
            var exit = OpenDuelEncounter(out var encounter);
            exit.EndConversation(CloseDuelConversation); // 回调只关闭对话，留待主线程地图阶段收尾。
            AssertTrue(encounter.Leave, "leave request set");
            AssertTrue(ReferenceEquals(encounter, PlayerEncounter.Current), "no Finish inside active conversation");
            AssertTrue(exit.Finish(), "fallback finishes original meeting");
            AssertEqual<bool?>(false, PlayerEncounter.LastForcePlayerOutFromSettlement, "never force leaving a town");
            AssertTrue(exit.Finish(), "a second tick is harmless");
            AssertEqual(1, PlayerEncounter.FinishCalls);
        }
        private static void TestDuelEncounterSettlement()
        {
            foreach (var kind in new[] { "party town", "encounter village", "location" })
            {
                OpenDuelEncounter(out var encounter);
                var settlement = new Settlement(); var location = new object();
                if (kind == "party town") MobileParty.MainParty!.CurrentSettlement = settlement;
                if (kind == "encounter village") encounter.Settlement = settlement;
                if (kind == "location") Campaign.Current!.LocationEncounter = location;
                var exit = new DuelEncounter(encounter, MainThreadExecutor.CampaignEpoch);
                exit.EndConversation(CloseDuelConversation);
                AssertFalse(encounter.Leave, kind + " must not use map-encounter leave callback");
                AssertTrue(exit.Finish(), kind + " retains normal settlement return");
                AssertEqual(0, PlayerEncounter.FinishCalls);
                AssertTrue(ReferenceEquals(encounter, PlayerEncounter.Current), kind + " encounter preserved");
                if (kind == "party town") AssertTrue(ReferenceEquals(settlement, MobileParty.MainParty!.CurrentSettlement), "town retained");
                if (kind == "location") AssertTrue(ReferenceEquals(location, Campaign.Current!.LocationEncounter), "location retained");
            }
            OpenDuelEncounter(out _); Campaign.Current!.PlayerEncounter = null;
            var missing = new DuelEncounter(null, MainThreadExecutor.CampaignEpoch);
            missing.EndConversation(CloseDuelConversation);
            AssertTrue(missing.Finish(), "no encounter is a no-op"); AssertEqual(0, PlayerEncounter.FinishCalls);
        }
        private static void TestDuelEncounterProtected()
        {
            foreach (var block in new Action[]
            {
                () => Mission.Current = new Mission(),
                () => Game.Current!.GameStateManager!.ActiveState = new MissionState(),
                () => ((MapState)Game.Current!.GameStateManager!.ActiveState!).MapConversationActive = true,
                () => Campaign.Current!.ConversationManager!.IsConversationInProgress = true,
                () => MobileParty.MainParty!.CurrentSettlement = new Settlement(),
                () => MobileParty.MainParty!.IsActive = false
            })
            {
                var exit = OpenDuelEncounter(out var encounter); exit.EndConversation(CloseDuelConversation);
                block(); AssertFalse(exit.Finish(), "defer cleanup until back on the map");
                AssertEqual(0, PlayerEncounter.FinishCalls);
                AssertTrue(ReferenceEquals(encounter, PlayerEncounter.Current), "blocked encounter preserved");
            }
        }
        private static void TestDuelEncounterOwnership()
        {
            foreach (var replaceDuringCallback in new[] { false, true })
            {
                var exit = OpenDuelEncounter(out var original); var replacement = new PlayerEncounter();
                if (replaceDuringCallback)
                    exit.EndConversation(() => { CloseDuelConversation(); Campaign.Current!.PlayerEncounter = replacement; });
                else Campaign.Current!.PlayerEncounter = replacement;
                var failed = false;
                try
                {
                    if (replaceDuringCallback) exit.Finish();
                    else exit.EndConversation(() => throw new Exception("must not close replacement conversation"));
                }
                catch (InvalidOperationException) { failed = true; }
                AssertTrue(failed, "replacement rejected"); AssertFalse(replacement.Leave, "new encounter leave flag unchanged");
                AssertTrue(ReferenceEquals(replacement, PlayerEncounter.Current), "replacement kept");
                AssertEqual(0, PlayerEncounter.FinishCalls);
            }
            foreach (var changeCampaign in new[] { false, true })
            {
                var exit = OpenDuelEncounter(out _); var failed = false;
                if (changeCampaign) Campaign.Current = new Campaign(); else MainThreadExecutor.BeginCampaign();
                try { exit.EndConversation(() => throw new Exception("old callback must be cancelled")); }
                catch (OperationCanceledException) { failed = true; }
                AssertTrue(failed, "stale campaign rejected before callback"); AssertEqual(0, PlayerEncounter.FinishCalls);
            }
        }

        private static void AssertThrows(Action action, string message)
        {
            try { action(); }
            catch (ArgumentException) { return; }
            throw new Exception(message);
        }

        private static MeetingTerms DuelTerms() => new()
        {
            TypeId = "duel",
            Parameters = new JObject { ["mounts"] = "foot" },
            Outcomes = new()
            {
                new MeetingOutcome { Id = "otherVictory", YourOffer = new MeetingOffer { Gold = 10000 } },
                new MeetingOutcome { Id = "yourVictory", OtherOffer = new MeetingOffer { Gold = 5000 } }
            }
        };
        private static void TestDuelDeferred()
        {
            foreach (var proposer in new[] { MeetingSide.Other, MeetingSide.Self })
            {
                var n = new MeetingNegotiation(); var confirmations = 0; var starts = 0;
                n.Propose(proposer, DuelTerms(), "", 0, "", _ => null);
                var request = n.Request!;
                var responder = proposer == MeetingSide.Other ? MeetingSide.Self : MeetingSide.Other;
                AssertEqual<string?>(null, n.Respond(responder, request.Id, request.Version, "accept", "", _ => null,
                    _ => { confirmations++; return "待开始"; }, deferred: true));
                AssertEqual(MeetingStatus.Ready, n.Request!.Status); AssertEqual(1, confirmations); AssertEqual(0, starts);
                AssertTrue(n.StartDeferred(request.Id, 0, _ => null, _ => starts++) != null, "stale start rejected");
                AssertTrue(n.StartDeferred(request.Id, 1, _ => "财物已经变化", _ => starts++) != null, "revalidate before starting");
                AssertEqual(MeetingStatus.Ready, n.Request.Status);
                AssertEqual<string?>(null, n.Propose(proposer, DuelTerms(), request.Id, 1, "修改", _ => null));
                AssertEqual(2, n.Request.Version); AssertEqual(MeetingStatus.Negotiating, n.Request.Status);
                n.Respond(responder, request.Id, 2, "accept", "", _ => null, _ => "待开始", true);
                AssertEqual<string?>(null, n.StartDeferred(request.Id, 2, _ => null, _ => starts++));
                AssertEqual(MeetingStatus.Running, n.Request.Status); AssertEqual(1, starts);
                AssertTrue(n.StartDeferred(request.Id, 2, _ => null, _ => starts++) != null, "double click cannot start twice");
                AssertTrue(n.Propose(proposer, DuelTerms(), "", 0, "", _ => null) != null, "running terms locked");
                AssertTrue(n.CompleteDeferred(request.Id, "获胜并履约"), "complete once");
                AssertFalse(n.CompleteDeferred(request.Id, "重复奖励"), "result replay rejected");
                AssertEqual(MeetingStatus.Applied, n.Request.Status);
            }
        }

        private static void TestDuelClose()
        {
            foreach (var started in new[] { false, true })
            {
                var n = new MeetingNegotiation(); n.Propose(MeetingSide.Other, DuelTerms(), "", 0, "", _ => null);
                var r = n.Request!;
                n.Respond(MeetingSide.Self, r.Id, 1, "accept", "", _ => null, _ => "待开始", true);
                if (started) n.StartDeferred(r.Id, 1, _ => null, _ => { });
                n.Close();
                AssertEqual(started ? MeetingStatus.Running : MeetingStatus.Cancelled, n.Request!.Status);
                AssertEqual(started, n.CompleteDeferred(r.Id, "结束", partial: true));
                if (started) AssertEqual(MeetingStatus.PartiallyApplied, n.Request.Status);
            }
        }

        private static void TestDuelTerms()
        {
            var terms = DuelTerms();
            var json = JObject.FromObject(terms, JsonSerializer.Create(new JsonSerializerSettings
                { ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver() }));
            var parsed = MeetingTerms.Parse(json);
            var copy = parsed.Copy(); copy.Outcomes[0].YourOffer.Gold = 1;
            AssertEqual(10000, parsed.Outcomes[0].YourOffer.Gold);
            var slots = new[] { new MeetingOutcomeSlot { Id = "otherVictory" }, new MeetingOutcomeSlot { Id = "yourVictory" } };
            AssertEqual<string?>(null, MeetingTerms.ValidateOutcomes(parsed, slots));
            AssertTrue(MeetingTerms.ValidateOutcomes(parsed, Array.Empty<MeetingOutcomeSlot>()) != null, "no hidden branches on ordinary trade");
            copy.Outcomes[1].Id = "otherVictory";
            AssertTrue(MeetingTerms.ValidateShape(copy) != null, "duplicate branches rejected");
            parsed.OtherOffer.Gold = 1;
            AssertTrue(MeetingTerms.ValidateOutcomes(parsed, slots) != null, "no early payment hidden above branches");
            var bad = (JObject)json.DeepClone(); bad["outcomes"]![0]!["otherOffer"]!["gold"] = 1.5;
            AssertThrows(() => MeetingTerms.Parse(bad), "fractional branch gold rejected");
            var reserve = MeetingAssetRules.ReserveMaximum(new[]
            {
                new MeetingOffer { Gold = 100, Items = new() { new MeetingItem { Id = "a", Count = 2 } } },
                new MeetingOffer { Gold = 80, Items = new() { new MeetingItem { Id = "a", Count = 5 }, new MeetingItem { Id = "b", Count = 3 } } }
            });
            AssertEqual(100, reserve.Gold); AssertEqual(5, reserve.Items.Single(i => i.Id == "a").Count);
            AssertEqual(3, reserve.Items.Single(i => i.Id == "b").Count); AssertEqual(0, reserve.Actions.Count);
        }

        private static void TestDuelResolution()
        {
            var draw = new DuelResolution(); draw.ReportLoss(MeetingSide.Other, true); draw.ReportLoss(MeetingSide.Self, false);
            AssertEqual(DuelOutcome.Draw, draw.Observe()); draw.Seal(); AssertFalse(draw.RewardEligible, "draw has no winner reward");
            AssertTrue(draw.TryBeginSettlement(), "one settlement"); AssertFalse(draw.TryBeginSettlement(), "no duplicate settlement");
            var win = new DuelResolution(); win.ReportLoss(MeetingSide.Self, true); win.Seal();
            win.ReportLoss(MeetingSide.Other, true); AssertEqual(DuelOutcome.OtherVictory, win.Outcome);
            AssertTrue(win.RewardEligible, "native lethal win counts");
            var forfeit = new DuelResolution(); forfeit.ReportLoss(MeetingSide.Other, false, true); forfeit.Seal();
            AssertEqual(DuelOutcome.YourVictory, forfeit.Outcome); AssertFalse(forfeit.RewardEligible, "forfeit fulfills branch but no farmed fame");
            AssertTrue(forfeit.OtherForfeit && !forfeit.YourForfeit, "forfeit names the losing participant");
            var abort = new DuelResolution(); abort.Seal(); AssertEqual(DuelOutcome.Aborted, abort.Outcome);
        }

        private static void TestDuelSettlement()
        {
            var payments = 0; var calls = 0; var changed = false;
            var result = DuelSettlement.Apply(() => payments++, new[]
            {
                new MeetingEffect { Name = "亡者换国", Validate = () => "已经战死", Apply = () => calls++ },
                new MeetingEffect { Name = "释放俘虏", Order = 10, Apply = () => { changed = true; throw new Exception("回调异常"); }, IsApplied = () => changed }
            });
            AssertEqual(1, payments); AssertEqual(0, calls); AssertTrue(result.AssetsDelivered, "money follows locked result");
            AssertEqual(1, result.Done.Count); AssertEqual(1, result.Missing.Count); AssertEqual(1, result.Warnings.Count);
            var stopped = DuelSettlement.Apply(() => throw new Exception("保管异常"), new[] { new MeetingEffect { Name = "行动", Apply = () => calls++ } });
            AssertFalse(stopped.AssetsDelivered, "failed payment recorded"); AssertEqual(0, calls);
        }

        private static DuelFighterSnapshot DuelHero(string clan, double renown, DuelRole role = DuelRole.Member) => new()
        { ClanId = clan, Renown = renown, Role = role, WeaponSkill = 150, MobilitySkill = 100, ArmorMean = 40, CanGainInfluence = true };
        private static void TestDuelRewards()
        {
            var w = DuelHero("a", 0); var l = DuelHero("b", 2350, DuelRole.ClanLeader);
            foreach (var pair in new[] { (0, 86, 61), (1, 21, 15), (2, 0, 0) })
            {
                var r = DuelRewardMath.Calculate(w, l, pair.Item1);
                AssertEqual(pair.Item2, r.Renown); AssertEqual(pair.Item3, r.Influence);
                var saved = JsonConvert.DeserializeObject<DuelReward>(JsonConvert.SerializeObject(r))!;
                var replay = DuelRewardMath.Calculate(saved.Winner, saved.Loser, saved.PriorWins);
                AssertEqual(r.Renown, replay.Renown); AssertEqual(r.Influence, replay.Influence);
            }
            w.Renown = 2350; var equal = DuelRewardMath.Calculate(w, l, 0);
            AssertEqual(54, equal.Renown); AssertEqual(49, equal.Influence);
            w.CanGainInfluence = false; AssertEqual(0, DuelRewardMath.Calculate(w, l, 0).Influence);
            AssertEqual(0, DuelRewardMath.Calculate(w, l, 0, false).Renown);
            l.ClanId = "a"; AssertEqual(0, DuelRewardMath.Calculate(w, l, 0).Renown);
            AssertThrows(() => DuelRewardMath.Calculate(w, l, -1), "negative history rejected");
            w.Renown = double.NaN; AssertThrows(() => DuelRewardMath.Calculate(w, l, 0), "invalid game facts rejected");
        }
        private static void TestDuelRewardProperties()
        {
            var random = new Random(20261104);
            for (var i = 0; i < 2000; i++)
            {
                var w = DuelHero("a", random.Next(20000)); var l = DuelHero("b", random.Next(20000));
                w.WeaponSkill = random.Next(400); l.WeaponSkill = random.Next(400);
                var before = DuelRewardMath.Calculate(w, l, 0);
                var bigger = l.Copy(); bigger.Renown += 500;
                var more = DuelRewardMath.Calculate(w, bigger, 0);
                AssertTrue(more.Renown >= before.Renown && more.Influence >= before.Influence, "famous enemy cannot pay less");
                bigger = w.Copy(); bigger.Renown += 500;
                var less = DuelRewardMath.Calculate(bigger, l, 0);
                AssertTrue(less.Renown <= before.Renown && less.Influence <= before.Influence, "famous winner gets less extra fame");
                bigger = l.Copy(); bigger.WeaponSkill += 100;
                more = DuelRewardMath.Calculate(w, bigger, 0);
                AssertTrue(more.Renown >= before.Renown && more.Influence >= before.Influence, "stronger enemy cannot pay less");
                var leader = l.Copy(); leader.Role = DuelRole.ClanLeader;
                var king = l.Copy(); king.Role = DuelRole.Ruler;
                AssertTrue(DuelRewardMath.Calculate(w, leader, 0).Renown >= before.Renown, "leader prestige");
                AssertTrue(DuelRewardMath.Calculate(w, king, 0).Influence >= DuelRewardMath.Calculate(w, leader, 0).Influence, "ruler political prestige");
                AssertTrue(before.FameFactor >= 0.6 && before.FameFactor <= 1.8 && before.DifficultyFactor >= 0.75 && before.DifficultyFactor <= 1.4, "bounded factors");
            }
            var near = DuelHero("a", 149); var enemy = DuelHero("b", 149);
            var low = DuelRewardMath.Calculate(near, enemy, 0).FirstRenown; enemy.Renown = 150;
            AssertTrue(DuelRewardMath.Calculate(near, enemy, 0).FirstRenown - low < 0.1, "no tier cliff");
        }

        private static (MeetingSession session, MeetingTerms terms, TaleWorlds.Core.EquipmentElement sword, TaleWorlds.Core.EquipmentElement grain) EscrowFixture()
        {
            MainThreadExecutor.BeginCampaign();
            TaleWorlds.CampaignSystem.Campaign.Current = new TaleWorlds.CampaignSystem.Campaign();
            var p = new TaleWorlds.CampaignSystem.Hero { Name = "甲", Gold = 300 };
            var n = new TaleWorlds.CampaignSystem.Hero { Name = "乙", Gold = 500 };
            p.PartyBelongedTo = new TaleWorlds.CampaignSystem.Party.MobileParty { LeaderHero = p };
            n.PartyBelongedTo = new TaleWorlds.CampaignSystem.Party.MobileParty { LeaderHero = n };
            var sword = new TaleWorlds.Core.EquipmentElement(new TaleWorlds.Core.ItemObject { StringId = "sword", Name = "剑" }, new TaleWorlds.Core.ItemModifier { StringId = "fine" });
            var grain = new TaleWorlds.Core.EquipmentElement(new TaleWorlds.Core.ItemObject { StringId = "grain", Name = "粮食" });
            p.PartyBelongedTo.ItemRoster.AddToCounts(sword, 7);
            n.PartyBelongedTo.ItemRoster.AddToCounts(sword, 3);
            n.PartyBelongedTo.ItemRoster.AddToCounts(grain, 5);
            var terms = DuelTerms();
            terms.Outcomes[0].OtherOffer = new MeetingOffer { Gold = 20, Items = new() { new MeetingItem { Id = MeetingAssets.Key(sword), Count = 2 } } };
            terms.Outcomes[0].YourOffer = new MeetingOffer { Gold = 200, Items = new() { new MeetingItem { Id = MeetingAssets.Key(grain), Count = 3 } } };
            terms.Outcomes[1].OtherOffer = new MeetingOffer { Gold = 100, Items = new() { new MeetingItem { Id = MeetingAssets.Key(sword), Count = 5 } } };
            terms.Outcomes[1].YourOffer = new MeetingOffer { Gold = 50, Items = new() { new MeetingItem { Id = MeetingAssets.Key(grain), Count = 1 } } };
            return (new MeetingSession(p, n), terms, sword, grain);
        }

        private static int InventoryCount(TaleWorlds.CampaignSystem.Hero hero, TaleWorlds.Core.EquipmentElement element)
            => MeetingAssets.Inventory(hero).FirstOrDefault(i => i.Id == MeetingAssets.Key(element))?.Count ?? 0;

        private static void ExpectGameFailure(Action action)
        {
            try { action(); }
            catch (InvalidOperationException) { return; }
            catch (AggregateException) { return; }
            catch (OperationCanceledException) { return; }
            throw new Exception("Expected guarded game operation failure.");
        }

        private static void TestDuelEscrowOutcomes()
        {
            foreach (var win in new[] { 0, 1 })
            {
                var f = EscrowFixture(); var p = f.session.Player; var n = f.session.Npc;
                var escrow = new MeetingEscrow(f.session, f.terms); escrow.Take();
                AssertEqual(200, p.Gold); AssertEqual(300, n.Gold);
                AssertEqual(2, InventoryCount(p, f.sword)); AssertEqual(2, InventoryCount(n, f.grain));
                escrow.Deliver(f.terms.Outcomes[win]);
                AssertEqual(win == 0 ? 480 : 250, p.Gold); AssertEqual(800, p.Gold + n.Gold);
                AssertEqual(win == 0 ? 5 : 2, InventoryCount(p, f.sword));
                AssertEqual(10, InventoryCount(p, f.sword) + InventoryCount(n, f.sword));
                AssertEqual(win == 0 ? 3 : 1, InventoryCount(p, f.grain));
                AssertEqual(5, InventoryCount(p, f.grain) + InventoryCount(n, f.grain));
                var gold = p.Gold; var swords = InventoryCount(p, f.sword);
                escrow.Deliver(f.terms.Outcomes[win]); escrow.Refund();
                AssertEqual(gold, p.Gold); AssertEqual(swords, InventoryCount(p, f.sword));
                AssertTrue(escrow.Finished && !escrow.Held, "settled once");
            }
        }

        private static void TestDuelEscrowRefund()
        {
            var f = EscrowFixture(); var escrow = new MeetingEscrow(f.session, f.terms); escrow.Take();
            f.session.Player.ChangeHeroGold(17);
            f.session.Player.PartyBelongedTo!.ItemRoster.AddToCounts(f.sword, 1);
            escrow.Refund(); escrow.Refund();
            AssertEqual(317, f.session.Player.Gold); AssertEqual(500, f.session.Npc.Gold);
            AssertEqual(8, InventoryCount(f.session.Player, f.sword)); AssertEqual(5, InventoryCount(f.session.Npc, f.grain));
            f = EscrowFixture(); escrow = new MeetingEscrow(f.session, f.terms); escrow.Take();
            MainThreadExecutor.BeginCampaign();
            ExpectGameFailure(() => escrow.Refund());
            AssertEqual(200, f.session.Player.Gold); AssertEqual(300, f.session.Npc.Gold);
        }

        private static void TestDuelEscrowFailures()
        {
            var f = EscrowFixture(); var escrow = new MeetingEscrow(f.session, f.terms);
            var failed = false;
            f.session.Npc.PartyBelongedTo!.ItemRoster.AfterChange = (_, count) =>
            {
                if (count < 0 && !failed) { failed = true; throw new InvalidOperationException("remove callback"); }
            };
            ExpectGameFailure(() => escrow.Take());
            AssertEqual(300, f.session.Player.Gold); AssertEqual(500, f.session.Npc.Gold);
            AssertEqual(7, InventoryCount(f.session.Player, f.sword)); AssertEqual(5, InventoryCount(f.session.Npc, f.grain));
            f = EscrowFixture(); escrow = new MeetingEscrow(f.session, f.terms); escrow.Take();
            failed = false;
            f.session.Player.PartyBelongedTo!.ItemRoster.AfterChange = (element, count) =>
            {
                if (element.Item.StringId == "grain" && count > 0 && !failed) { failed = true; throw new InvalidOperationException("delivery callback"); }
            };
            ExpectGameFailure(() => escrow.Deliver(f.terms.Outcomes[0]));
            AssertEqual(300, f.session.Player.Gold); AssertEqual(500, f.session.Npc.Gold);
            AssertEqual(7, InventoryCount(f.session.Player, f.sword)); AssertEqual(5, InventoryCount(f.session.Npc, f.grain));
            escrow.Refund(); AssertEqual(800, f.session.Player.Gold + f.session.Npc.Gold);
            AssertEqual(0, InventoryCount(f.session.Player, f.grain));
        }

        private sealed class DuelTestDataStore : TaleWorlds.CampaignSystem.IDataStore
        {
            public readonly System.Collections.Generic.Dictionary<string, string> Values;
            public bool IsSaving { get; }
            public bool IsLoading => !IsSaving;
            public DuelTestDataStore(System.Collections.Generic.Dictionary<string, string> values, bool saving) { Values = values; IsSaving = saving; }
            public bool SyncData<T>(string key, ref T value)
            {
                if (IsSaving) { Values[key] = JsonConvert.SerializeObject(value); return true; }
                if (!Values.TryGetValue(key, out var text)) return false;
                value = JsonConvert.DeserializeObject<T>(text)!; return true;
            }
        }

        private static void TestDuelSaveLedger()
        {
            var values = new System.Collections.Generic.Dictionary<string, string>();
            var store = new DuelStore(); store.SyncData(new DuelTestDataStore(values, false));
            AssertEqual(0, store.CountWins("a", "b", 10)); AssertEqual<string?>(null, store.Error);
            AssertTrue(store.Complete("first", 10, "a", "b", true), "first record");
            AssertFalse(store.Complete("first", 10, "a", "b", true), "same id once");
            AssertEqual(0, store.CountWins("b", "a", 10));
            AssertEqual(1, store.CountWins("a", "b", 16.999)); AssertEqual(0, store.CountWins("a", "b", 17));
            store.SyncData(new DuelTestDataStore(values, true));
            var saved = new System.Collections.Generic.Dictionary<string, string>(values);
            store.Complete("later", 11, "a", "b", true); AssertEqual(2, store.CountWins("a", "b", 11));
            var loaded = new DuelStore(); loaded.SyncData(new DuelTestDataStore(saved, false));
            AssertEqual(1, loaded.CountWins("a", "b", 11));
            loaded.Complete("forfeit", 11, "a", "b", false); AssertEqual(1, loaded.CountWins("a", "b", 11));
            loaded.Complete("future", 20, "a", "b", true); AssertEqual(1, loaded.CountWins("a", "b", 20));
            saved["aichronicle_duel_rewards_v1"] = JsonConvert.SerializeObject("{\"Version\":99,\"Entries\":[]}");
            loaded.SyncData(new DuelTestDataStore(saved, false)); AssertTrue(loaded.Error != null, "unknown format stops new awards");
            ExpectGameFailure(() => loaded.CountWins("a", "b", 20));
            var bad = saved["aichronicle_duel_rewards_v1"];
            loaded.SyncData(new DuelTestDataStore(saved, true)); AssertEqual(bad, saved["aichronicle_duel_rewards_v1"]);
        }

        private static void TestDuelReplayAudit()
        {
            var directory = NewTempDir();
            var previousOutput = Console.Out; var previousError = Console.Error;
            try
            {
                var path = System.IO.Path.Combine(directory, "duel.json");
                var reward = DuelRewardMath.Calculate(DuelHero("a", 0), DuelHero("b", 2350, DuelRole.ClanLeader), 0);
                var audit = new DuelAudit { Id = "duel-sample", Phase = "completed", RewardEligible = true, AppliedReward = reward,
                    ActualRenown = reward.Renown, ActualInfluence = reward.Influence };
                SafeFileIO.WriteAllTextAtomic(path, JsonConvert.SerializeObject(audit));
                var output = new System.IO.StringWriter();
                Console.SetOut(output); Console.SetError(new System.IO.StringWriter());
                AssertEqual(0, ReplayDuel(new[] { "--duel-replay", path }));
                AssertTrue(output.ToString().Contains("\"matches\": true"), "replay reads frozen game record");
                audit.AppliedReward.Renown++;
                SafeFileIO.WriteAllTextAtomic(path, JsonConvert.SerializeObject(audit));
                AssertEqual(1, ReplayDuel(new[] { "--duel-replay", path }));
                audit.AppliedReward.AlgorithmVersion = "unknown";
                SafeFileIO.WriteAllTextAtomic(path, JsonConvert.SerializeObject(audit));
                AssertEqual(2, ReplayDuel(new[] { "--duel-replay", path }));
                AssertEqual(0, System.IO.Directory.GetFiles(directory, "*.tmp").Length);
            }
            finally
            {
                Console.SetOut(previousOutput); Console.SetError(previousError);
                System.IO.Directory.Delete(directory, true);
            }
        }
    }
}
