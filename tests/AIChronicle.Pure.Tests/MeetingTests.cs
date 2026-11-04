using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AIChronicle;
using Newtonsoft.Json.Linq;

namespace AIChronicle.Tests
{
    internal static partial class Program
    {
        private static void RunMeetingTests()
        {
            Run("meeting: either party proposes + exactly-once commit", TestMeetingAcceptance);
            Run("meeting: counteroffer invalidates old acceptance + immutable snapshots", TestMeetingCounter);
            Run("meeting: reject/withdraw/close + old request ids", TestMeetingEndings);
            Run("meeting: revalidate assets before acceptance", TestMeetingRevalidation);
            Run("meeting: failed effects cannot retry + execution reentrancy", TestMeetingFailure);
            Run("meeting: strict structured terms + empty marriage offers", TestMeetingTerms);
            Run("meeting: quality ids, missing assets + integer bounds", TestMeetingAssets);
            Run("meeting: partial transfer compensation + irreversible effect", TestMeetingCompensation);
            Run("meeting: tool scopes + deep nested schema snapshot", TestMeetingTools);
            Run("meeting: prompt preserves you/other identity", TestMeetingPrompt);
            Run("meeting: asynchronous session context isolation", TestMeetingContext);
            Run("meeting: compound offer actor direction + deep copies", TestMeetingCompoundTerms);
            Run("meeting: batch preflight + duplicate resource conflicts", TestMeetingBatchValidation);
            Run("meeting: pay for allegiance + ordered prisoner release", TestMeetingBatchSuccess);
            Run("meeting: partial fulfillment is final + no false refunds", TestMeetingBatchPartial);
            Run("meeting: unknown effect state stops safely", TestMeetingBatchUnknown);
        }

        private static MeetingTerms Offer(string type = "trade", int gold = 100) => new()
        {
            TypeId = type, OtherOffer = new MeetingOffer { Gold = gold },
            Parameters = type == "marriage" ? JObject.Parse("{ 'otherHeroId':'a', 'yourHeroId':'b' }") : new JObject()
        };
        private static string? Valid(MeetingTerms terms) => null;
        private static string? Reply(MeetingNegotiation n, MeetingSide side, string decision = "accept", Func<MeetingTerms, string?>? validate = null,
            Func<MeetingTerms, string>? execute = null) => n.Respond(side, n.Request!.Id, n.Request.Version, decision, "", validate ?? Valid, execute ?? (_ => "完成"));

        private static void TestMeetingAcceptance()
        {
            foreach (var proposer in new[] { MeetingSide.Other, MeetingSide.Self })
            {
                var n = new MeetingNegotiation(); var effects = 0;
                AssertEqual<string?>(null, n.Propose(proposer, Offer(), "", 0, "", Valid));
                AssertTrue(Reply(n, proposer, execute: _ => { effects++; return ""; }) != null, "cannot accept own proposal twice");
                AssertEqual(0, effects);
                AssertEqual<string?>(null, Reply(n, proposer == MeetingSide.Other ? MeetingSide.Self : MeetingSide.Other,
                    execute: _ => { effects++; return "成交"; }));
                AssertEqual(MeetingStatus.Applied, n.Request!.Status);
                AssertTrue(n.Request.OtherAccepted && n.Request.YourAccepted, "same version accepted by both");
                AssertTrue(Reply(n, proposer, execute: _ => { effects++; return ""; }) != null, "no replay");
                AssertEqual(1, effects);
            }
        }

        private static void TestMeetingCounter()
        {
            var n = new MeetingNegotiation(); var terms = Offer("marriage", 0);
            n.Propose(MeetingSide.Other, terms, "", 0, "", Valid);
            var old = n.Request!;
            terms.Parameters["otherHeroId"] = "forged";
            AssertEqual("a", n.Request!.Terms.Parameters["otherHeroId"]!.ToString());
            var counter = old.Terms.Copy(); counter.YourOffer.Gold = 800;
            AssertEqual<string?>(null, n.Propose(MeetingSide.Self, counter, old.Id, old.Version, "新的条件", Valid));
            AssertEqual(old.Id, n.Request!.Id); AssertEqual(2, n.Request.Version);
            AssertFalse(n.Request.OtherAccepted, "old acceptance reset"); AssertTrue(n.Request.YourAccepted, "counterproposer accepts");
            var effects = 0;
            AssertTrue(n.Respond(MeetingSide.Other, old.Id, old.Version, "accept", "", Valid, _ => { effects++; return ""; }) != null, "stale version rejected");
            var snapshot = n.Request; snapshot!.Terms.YourOffer.Gold = 9999;
            AssertEqual(800, n.Request!.Terms.YourOffer.Gold);
            AssertTrue(n.Propose(MeetingSide.Other, Offer(), old.Id, 2, "", Valid) != null, "cannot change action while countering");
            Reply(n, MeetingSide.Other, execute: t => { effects++; AssertEqual(800, t.YourOffer.Gold); return ""; });
            AssertEqual(1, effects);
        }

        private static void TestMeetingEndings()
        {
            var n = new MeetingNegotiation(); n.Propose(MeetingSide.Other, Offer(), "", 0, "", Valid);
            var old = n.Request!;
            AssertTrue(Reply(n, MeetingSide.Self, "withdraw") != null, "only author withdraws");
            AssertEqual<string?>(null, Reply(n, MeetingSide.Self, "reject"));
            AssertEqual(MeetingStatus.Rejected, n.Request!.Status);
            AssertTrue(Reply(n, MeetingSide.Self) != null, "rejected proposal is final");
            AssertEqual<string?>(null, n.Propose(MeetingSide.Self, Offer("new_registered_action", 0), "", 0, "", Valid));
            AssertTrue(n.Request!.Id != old.Id, "new action gets fresh request id");
            AssertTrue(n.Respond(MeetingSide.Other, old.Id, 1, "accept", "", Valid, _ => "") != null, "old request cannot accept new offer");
            AssertEqual<string?>(null, Reply(n, MeetingSide.Self, "withdraw"));
            n.Propose(MeetingSide.Other, Offer(), "", 0, "", Valid); n.Close();
            AssertEqual(MeetingStatus.Cancelled, n.Request!.Status);
            AssertTrue(Reply(n, MeetingSide.Self) != null, "closed meeting cannot settle");
            AssertTrue(n.Propose(MeetingSide.Other, Offer(), "", 0, "", Valid) != null, "closed meeting cannot reopen");
        }

        private static void TestMeetingRevalidation()
        {
            var n = new MeetingNegotiation(); var balance = 100; var count = 0;
            string? Check(MeetingTerms t) => balance < t.OtherOffer.Gold ? "金币不足" : null;
            n.Propose(MeetingSide.Other, Offer(), "", 0, "", Check);
            balance = 50;
            AssertTrue(Reply(n, MeetingSide.Self, validate: Check, execute: _ => { count++; return ""; }) != null, "live validation on accept");
            AssertEqual(MeetingStatus.Negotiating, n.Request!.Status); AssertFalse(n.Request.YourAccepted, "failed accept does not persist");
            AssertEqual(0, count); balance = 100;
            Reply(n, MeetingSide.Self, validate: Check, execute: _ => { count++; return ""; }); AssertEqual(1, count);
        }

        private static void TestMeetingFailure()
        {
            var n = new MeetingNegotiation(); n.Propose(MeetingSide.Other, Offer(), "", 0, "", Valid);
            var count = 0;
            Reply(n, MeetingSide.Self, execute: _ =>
            {
                count++; AssertTrue(Reply(n, MeetingSide.Self) != null, "executing state guards reentrancy");
                throw new InvalidOperationException("执行器拒绝");
            });
            AssertEqual(MeetingStatus.Failed, n.Request!.Status); AssertTrue(n.Request.Result.Contains("执行器拒绝"), "failure recorded");
            AssertTrue(Reply(n, MeetingSide.Self, execute: _ => { count++; return ""; }) != null, "no automatic failed-effect retry"); AssertEqual(1, count);
        }

        private static JObject TermsJson() => JObject.Parse("{'typeId':'marriage','parameters':{'otherHeroId':'a','yourHeroId':'b'},'otherOffer':{'gold':0,'items':[]},'yourOffer':{'gold':0,'items':[]}}");
        private static void MustReject(Action action)
        {
            var rejected = false; try { action(); } catch (Exception) { rejected = true; }
            AssertTrue(rejected, "malformed terms must fail");
        }
        private static void TestMeetingTerms()
        {
            var free = MeetingTerms.Parse(TermsJson()); AssertEqual(0, free.OtherOffer.Gold); AssertEqual(0, free.YourOffer.Items.Count);
            var json = TermsJson(); json["otherOffer"]!["gold"] = 1.5; MustReject(() => MeetingTerms.Parse(json));
            json = TermsJson(); json["otherOffer"]!["items"] = JArray.Parse("[{'id':'sword|fine|','count':1.5}]"); MustReject(() => MeetingTerms.Parse(json));
            json = TermsJson(); json["otherOffer"] = null; MustReject(() => MeetingTerms.Parse(json));
            json = TermsJson(); json["unpromisedAction"] = "transfer_fief"; MustReject(() => MeetingTerms.Parse(json));
            json = TermsJson(); json["yourOffer"]!["gold"] = -1; MustReject(() => MeetingTerms.Parse(json));
            json = TermsJson(); json["otherOffer"]!["items"] = JArray.Parse("[{'id':'x','count':1},{'id':'x','count':1}]"); MustReject(() => MeetingTerms.Parse(json));
        }

        private static void TestMeetingAssets()
        {
            var inventory = new List<MeetingInventoryEntry> { new() { Id = "sword|fine|", Count = 2 } };
            var empty = new List<MeetingInventoryEntry>(); var terms = Offer();
            AssertEqual<string?>(null, MeetingAssetRules.Validate(terms, 100, 0, inventory, empty));
            AssertTrue(MeetingAssetRules.Validate(terms, 99, 0, inventory, empty) != null, "cannot spend future incoming gold");
            AssertTrue(MeetingAssetRules.Validate(terms, 100, int.MaxValue, inventory, empty) != null, "recipient gold overflow");
            terms.OtherOffer.Items.Add(new MeetingItem { Id = "sword||", Count = 1 });
            AssertTrue(MeetingAssetRules.Validate(terms, 100, 0, inventory, empty) != null, "plain sword cannot substitute quality sword");
            terms.OtherOffer.Items[0].Id = "sword|fine|"; AssertEqual<string?>(null, MeetingAssetRules.Validate(terms, 100, 0, inventory, empty));
            terms.OtherOffer.Items[0].Count = 3; AssertTrue(MeetingAssetRules.Validate(terms, 100, 0, inventory, empty) != null, "inventory shortage");
            terms.OtherOffer.Items[0].Count = 1;
            AssertTrue(MeetingAssetRules.Validate(terms, 100, 0, inventory, new[] { new MeetingInventoryEntry { Id = "sword|fine|", Count = int.MaxValue } }) != null, "recipient item overflow");
        }

        private static void TestMeetingCompensation()
        {
            var gold = 100; var items = 2; var calls = 0;
            MustReject(() => MeetingCommit.Apply(() => { gold -= 30; items--; throw new InvalidOperationException("transfer interrupted"); },
                () => { gold = 100; items = 2; }, () => calls++, () => false));
            AssertEqual(100, gold); AssertEqual(2, items); AssertEqual(0, calls);
            MustReject(() => MeetingCommit.Apply(() => gold -= 30, () => gold = 100,
                () => throw new InvalidOperationException("effect failed"), () => false)); AssertEqual(100, gold);
            var applied = false;
            var recovered = MeetingCommit.Apply(() => gold -= 30, () => gold = 100,
                () => { applied = true; throw new InvalidOperationException("notification failed after effect"); }, () => applied);
            AssertTrue(recovered != null, "post-effect exception retained for diagnostics"); AssertEqual(70, gold);
        }

        private static void TestMeetingTools()
        {
            var definitions = LoadToolDefinitions();
            definitions.Add(new ToolDef { Name = "extension_tool", Category = "extra", Description = "test registration" });
            var catalog = new ToolCatalog(definitions, "conversation", meeting: true);
            AssertTrue(catalog.VisibleTools.Any(t => t.Name == "propose_meeting"), "meeting action tools visible");
            AssertFalse(catalog.VisibleTools.Any(t => ToolCatalog.IsMeetingBypass(t.Name)), "direct transfers or prisoner release cannot bypass offer");
            AssertTrue(catalog.VisibleTools.Any(t => t.Name == "change_kingdom"), "unrelated existing family actions remain available");
            foreach (var intent in new[] { "conversation", "letter", "chancery", "self_review", "curia" })
                AssertFalse(new ToolCatalog(definitions, intent).VisibleTools.Any(t => ToolCatalog.IsMeetingTool(t.Name)), "no meeting outside active session: " + intent);
            var nested = catalog.ApiTools.First(t => t["function"]!["name"]!.ToString() == "propose_meeting")["function"]!["parameters"]!["properties"]!["terms"]!;
            AssertEqual("array", nested["properties"]!["otherOffer"]!["properties"]!["items"]!["type"]!.ToString());
            definitions.First(t => t.Name == "propose_meeting").Parameters.First(p => p.Name == "terms").Schema!["type"] = "string";
            AssertEqual("object", nested["type"]!.ToString(), "deep schema snapshot is immutable");
            catalog.TryUnlock("extra");
            AssertEqual("object", catalog.ApiTools.First(t => t["function"]!["name"]!.ToString() == "propose_meeting")
                ["function"]!["parameters"]!["properties"]!["terms"]!["type"]!.ToString(), "browse refresh retains copied nested schema");
        }

        private static void TestMeetingPrompt()
        {
            var rules = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Prompts", "meeting_rules.txt"));
            AssertFalse(rules.Contains("玩家"), "role prompt must never introduce player identity");
            foreach (var tool in LoadToolDefinitions().Where(t => ToolCatalog.IsMeetingTool(t.Name)))
                AssertFalse(tool.Description.ToLowerInvariant().Contains("player"), "tool description must use you/other");
            AssertTrue(rules.Contains("otherOffer") && rules.Contains("yourOffer"), "offer directions explicit");
        }

        private static void TestMeetingContext()
        {
            MeetingContext.SessionId = "outer";
            try
            {
                var tasks = new[] { "a", "b" }.Select(id => Task.Run(async () =>
                {
                    MeetingContext.SessionId = id; await Task.Yield(); return MeetingContext.SessionId;
                })).ToArray();
                var values = Task.WhenAll(tasks).GetAwaiter().GetResult(); AssertSequence(new[] { "a", "b" }, values!);
                AssertEqual("outer", MeetingContext.SessionId, "child calls do not overwrite caller");
            }
            finally { MeetingContext.SessionId = null; }
        }

        private static void TestMeetingCompoundTerms()
        {
            var terms = TermsJson(); terms["typeId"] = "trade"; terms["parameters"] = new JObject();
            terms["otherOffer"]!["gold"] = 10000;
            terms["yourOffer"]!["actions"] = JArray.Parse("[{'typeId':'change_allegiance','parameters':{'kingdomId':'kingdom-a'}},{'typeId':'release_prisoner','parameters':{'prisonerId':'duke','count':'1'}}]");
            var parsed = MeetingTerms.Parse(terms); var n = new MeetingNegotiation();
            AssertEqual<string?>(null, n.Propose(MeetingSide.Other, parsed, "", 0, "", Valid));
            parsed.YourOffer.Actions[0].Parameters["kingdomId"] = "forged";
            AssertEqual("kingdom-a", n.Request!.Terms.YourOffer.Actions[0].Parameters["kingdomId"]!.ToString(), "action parameters are copied deeply");
            Reply(n, MeetingSide.Self, execute: t =>
            {
                AssertEqual(10000, t.OtherOffer.Gold); AssertEqual(0, t.YourOffer.Gold);
                AssertEqual("change_allegiance", t.YourOffer.Actions[0].TypeId); AssertEqual(0, t.OtherOffer.Actions.Count);
                return "对方付钱，你方家族转换阵营并释放俘虏";
            });
            AssertEqual(MeetingStatus.Applied, n.Request!.Status);
            terms["yourOffer"]!["actions"]![0]!["parameters"]!["kingdomId"] = 7; MustReject(() => MeetingTerms.Parse(terms));
            var fields = new[] { new MeetingField { Key = "count", Kind = "number", Label = "人数", Minimum = 1, Maximum = 3 } };
            AssertTrue(MeetingFieldRules.Validate(fields, JObject.Parse("{'count':'4'}")) != null, "server enforces action field limits");
            AssertTrue(MeetingFieldRules.Validate(fields, JObject.Parse("{'count':'1','unapproved':'x'}")) != null, "unknown action fields rejected");
        }

        private static void TestMeetingBatchValidation()
        {
            var transfers = 0;
            var duplicate = new[] {
                new MeetingEffect { Name = "首次换国", Resources = new[] { "clan:a" } },
                new MeetingEffect { Name = "重复换国", Resources = new[] { "clan:a" } }
            };
            MustReject(() => MeetingBatch.Apply(() => transfers++, () => { }, duplicate)); AssertEqual(0, transfers);
            var invalid = new[] { new MeetingEffect { Name = "释放俘虏", Validate = () => "俘虏已经离开" } };
            MustReject(() => MeetingBatch.Apply(() => transfers++, () => { }, invalid)); AssertEqual(0, transfers, "all preconditions checked before payment");
        }

        private static void TestMeetingBatchSuccess()
        {
            var goldA = 10000; var goldB = 0; var freed = false; var allegiance = "old"; var sequence = new List<string>();
            var effects = new[] {
                new MeetingEffect { Name = "家族加入A国", Order = 30, Resources = new[] { "clan:b" },
                    Apply = () => { AssertEqual(10000, goldB); allegiance = "A"; sequence.Add("join"); }, IsApplied = () => allegiance == "A" },
                new MeetingEffect { Name = "释放领主", Order = 10, Resources = new[] { "prisoner:duke" },
                    Apply = () => { freed = true; sequence.Add("release"); }, IsApplied = () => freed }
            };
            var errors = MeetingBatch.Apply(() => { goldA -= 10000; goldB += 10000; sequence.Add("pay"); }, () => { goldA = 10000; goldB = 0; }, effects);
            AssertEqual(0, errors.Count); AssertEqual(0, goldA); AssertEqual(10000, goldB);
            AssertSequence(new[] { "pay", "release", "join" }, sequence);
        }

        private static void TestMeetingBatchPartial()
        {
            var n = new MeetingNegotiation(); n.Propose(MeetingSide.Other, Offer(), "", 0, "", Valid);
            var paid = false; var freed = false; var attempts = 0;
            string Execute(MeetingTerms _) {
                attempts++;
                MeetingBatch.Apply(() => paid = true, () => paid = false, new[] {
                    new MeetingEffect { Name = "释放领主", Apply = () => freed = true, IsApplied = () => freed },
                    new MeetingEffect { Name = "加入王国", Apply = () => throw new InvalidOperationException("原版动作失败"), IsApplied = () => false }
                });
                return "全部生效";
            }
            Reply(n, MeetingSide.Self, execute: Execute);
            AssertEqual(MeetingStatus.PartiallyApplied, n.Request!.Status); AssertTrue(paid && freed, "cannot pretend irreversible release was undone");
            AssertTrue(n.Request.Result.Contains("释放领主") && n.Request.Result.Contains("加入王国"), "actual and incomplete actions visible");
            AssertTrue(Reply(n, MeetingSide.Self, execute: Execute) != null, "partial batch is final and cannot repeat"); AssertEqual(1, attempts);
            var allegiance = "old"; paid = false;
            var partial = false;
            try {
                MeetingBatch.Apply(() => paid = true, () => paid = false, new[] {
                    new MeetingEffect { Name = "叛逃加入新国", Apply = () => { allegiance = "independent"; throw new InvalidOperationException("入国回调失败"); },
                        IsApplied = () => allegiance == "new", HasChanged = () => allegiance != "old" }
                });
            }
            catch (MeetingPartialExecutionException) { partial = true; }
            AssertTrue(partial && paid, "a partially changed first action must not be treated as fully undone");
        }

        private static void TestMeetingBatchUnknown()
        {
            var paid = false; var partial = false;
            try {
                MeetingBatch.Apply(() => paid = true, () => paid = false, new[] {
                    new MeetingEffect { Name = "无法核实的原版动作", Apply = () => { }, IsApplied = () => throw new InvalidOperationException("查询失败") }
                });
            }
            catch (MeetingPartialExecutionException) { partial = true; }
            AssertTrue(paid && partial, "unknown irreversible state cannot grant a free action via refund");
        }
    }
}
