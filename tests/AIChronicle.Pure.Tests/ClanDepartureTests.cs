using System;
using System.Linq;
using AIChronicle;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;

namespace AIChronicle.Tests
{
    internal static partial class Program
    {
        private static void RunClanDepartureTests()
        {
            Run("clan departure: either gender + spouse/minors + no adult relatives", TestDepartureFamily);
            Run("clan departure: objective eligibility + leader spouse + unstable states", TestDepartureEligibility);
            Run("clan departure: captive founder and spouse can join/found without escaping", TestDepartureCaptives);
            Run("clan departure: besieged prison is not personal combat + family blockers identify member", TestDepartureCaptiveSiege);
            Run("clan departure: explicit and native release remain separate from membership", TestDepartureCaptiveRelease);
            Run("clan departure: unfunded founder + keep native party/inventory/wealth", TestDepartureFounder);
            Run("clan departure: optional funding + join existing clan + foreign army", TestDepartureJoin);
            Run("clan departure: same kingdom army stays + only listed family migrates", TestDepartureArmy);
            Run("clan departure: immutable household + old family/party/leader rejects", TestDepartureSnapshot);
            Run("clan departure: no third-party adult + invalid destinations/names", TestDepartureParameters);
            Run("clan departure: clan resource conflicts + campaign epoch", TestDepartureConflicts);
            Run("clan departure: partial effects retain payment + no retry", TestDeparturePartial);
            Run("clan departure: applied callback failure recovered + factual diaries once", TestDepartureRecords);
            Run("identity: adopted clan members are not invented blood relatives", TestDepartureIdentity);
        }

        private static (MeetingSession Session, Clan Source, Clan Target, Hero Founder, Hero Spouse) DepartureWorld(bool female = false)
        {
            MainThreadExecutor.BeginCampaign();
            Clan.All.Clear(); Kingdom.All.Clear(); AgentManager.DepartureDiaries.Clear();
            CampaignEventDispatcher.Instance.Created = 0; CampaignEventDispatcher.Instance.ThrowOnCreated = false;
            TaleWorlds.CampaignSystem.Actions.DisbandArmyAction.AfterDisband = null;
            TaleWorlds.CampaignSystem.Actions.ChangeKingdomAction.AfterJoin = null;
            Campaign.Current = new Campaign();
            var source = new Clan { Name = "原族", StringId = "source" };
            source.Leader = new Hero { Name = "原族长", StringId = "source-leader", Clan = source, Gold = 80000, IsLord = true };
            source.AddRenown(600, false);
            var target = new Clan { Name = "接收族", StringId = "target" };
            var player = new Hero { Name = "交涉者", StringId = "other", Clan = target, Gold = 5000, IsLord = true };
            target.Leader = player; Hero.MainHero = player;
            var founder = new Hero { Name = "自立者", StringId = "founder", Clan = source, Gold = 17, IsLord = true, IsFemale = female };
            var spouse = new Hero { Name = "配偶", StringId = "spouse", Clan = source, Gold = 23, IsLord = true, IsFemale = !female };
            founder.Spouse = spouse; spouse.Spouse = founder;
            return (new MeetingSession(player, founder), source, target, founder, spouse);
        }

        private static Hero DepartureChild(Hero parent, string id, int age)
        {
            var child = new Hero { Name = id, StringId = id, Clan = parent.Clan, Age = age, IsLord = true,
                Father = parent.IsFemale ? parent.Spouse : parent, Mother = parent.IsFemale ? parent : parent.Spouse };
            parent.Children.Add(child); parent.Spouse?.Children.Add(child);
            return child;
        }

        private static MobileParty DepartureParty(Hero hero)
        {
            var party = new MobileParty { LeaderHero = hero, ActualClan = hero.Clan! };
            hero.PartyBelongedTo = party;
            party.MemberRoster.Entries.Add(new TroopRosterElement { Character = hero.CharacterObject });
            return party;
        }

        private static MeetingTerms DepartureTerms(MeetingClanAction action, MeetingSession session) => new()
        {
            TypeId = action.Id,
            Parameters = action.Id == "found_clan"
                ? new JObject { ["familyId"] = action.Fields(session)[0].Choices.Single().Id, ["clanName"] = "新枝家族", ["kingdomId"] = ClanDeparture.Independent }
                : new JObject { ["familyId"] = action.Fields(session)[0].Choices.Single().Id, ["clanId"] = session.Player.Clan!.StringId }
        };

        private static void TestDepartureFamily()
        {
            foreach (var female in new[] { false, true })
            {
                var w = DepartureWorld(female);
                var child = DepartureChild(w.Founder, "child", 8);
                child.IsNotSpawned = true;
                var adult = DepartureChild(w.Founder, "adult", 18);
                var outsider = new Hero { Name = "兄弟", IsLord = true, Clan = w.Source };
                AssertEqual<string?>(null, ClanDeparture.Family(w.Founder, out var family));
                AssertEqual(3, family.Count);
                AssertTrue(family.Contains(w.Spouse) && family.Contains(child), "spouse and same-clan child included once");
                AssertFalse(family.Contains(adult) || family.Contains(outsider), "other adults decide for themselves");
                var plan = new ClanDeparture(w.Founder, null, "新族", null); plan.Apply();
                AssertTrue(plan.Destination!.Leader == w.Founder && w.Founder.IsFemale == female, "selected person leads regardless of gender");
                AssertTrue(w.Founder.Spouse == w.Spouse && w.Spouse.Spouse == w.Founder, "marriage preserved");
                AssertTrue(child.Father == (female ? w.Spouse : w.Founder), "parentage preserved");
                AssertTrue(child.IsNotSpawned, "native child state is preserved without spawning adult agents");
                AssertTrue(adult.Clan == w.Source && outsider.Clan == w.Source, "unlisted adults stay");
            }
        }

        private static void TestDepartureEligibility()
        {
            foreach (Action<Hero> block in new Action<Hero>[]
            {
                h => h.IsDisabled = true, h => h.IsNotSpawned = true,
                h => h.IsFugitive = true, h => h.IsTraveling = true, h => h.Age = 17,
                h => h.IsAlive = false, h => h.IsLord = false, h => h.IsTemplate = true,
                h => DepartureParty(h).MapEvent = new object(), h => DepartureParty(h).SiegeEvent = new object(),
                h => DepartureParty(h).IsLordParty = false, h => DepartureParty(h).IsDisbanding = true,
                h => DepartureParty(h).IsActive = false
            })
            {
                var w = DepartureWorld(); block(w.Founder);
                AssertTrue(ClanDeparture.Family(w.Founder, out _) != null, "unstable/invalid actor rejected");
            }
            var couple = DepartureWorld(); couple.Source.Leader = couple.Spouse;
            AssertTrue(ClanDeparture.Family(couple.Founder, out _) != null, "leader's spouse cannot split couple");
            couple = DepartureWorld(); couple.Founder.Spouse = null; couple.Founder.IsPregnant = true;
            AssertTrue(ClanDeparture.Family(couple.Founder, out _) != null, "unborn old-father clan safeguarded");
        }

        private static PartyBase HoldDepartureHero(Hero hero, Clan captor)
        {
            var holder = new PartyBase();
            holder.PrisonRoster.Entries.Add(new TroopRosterElement { Character = hero.CharacterObject });
            hero.IsPrisoner = true;
            hero.PartyBelongedToAsPrisoner = holder;
            hero.CurrentSettlement = new TaleWorlds.CampaignSystem.Settlements.Settlement { MapFaction = captor };
            return holder;
        }

        private static void ReleaseDepartureHero(Hero hero)
        {
            hero.PartyBelongedToAsPrisoner!.PrisonRoster.Entries.RemoveAll(e => e.Character.HeroObject == hero);
            hero.IsPrisoner = false;
            hero.PartyBelongedToAsPrisoner = null;
            hero.CurrentSettlement = null;
        }

        private static void TestDepartureCaptives()
        {
            foreach (var mode in new[] { "join", "independent", "kingdom" })
            {
                var w = DepartureWorld(true);
                var holder = HoldDepartureHero(w.Founder, w.Source);
                var spouseHolder = HoldDepartureHero(w.Spouse, w.Source);
                var dungeon = w.Founder.CurrentSettlement;
                var spouseDungeon = w.Spouse.CurrentSettlement;
                w.Target.Hostile = true; // 转族后关押城仍为敌城，也不能调用普通离城/逃亡动作。
                var action = new MeetingClanAction(mode != "join");
                AssertEqual<string?>(null, action.Unavailable(w.Session), "captivity does not hide the action");
                var terms = DepartureTerms(action, w.Session);
                if (mode == "kingdom")
                {
                    var kingdom = new Kingdom { Name = "目标国" };
                    terms.Parameters["kingdomId"] = kingdom.StringId;
                }
                AssertEqual<string?>(null, action.Validate(w.Session, terms));
                MeetingBatch.Apply(() => { }, () => throw new Exception("unexpected refund"), new[] { action.PrepareEffect(w.Session, terms) });
                AssertTrue(action.IsApplied(w.Session, terms), "captive household can change membership");
                AssertTrue(w.Founder.IsPrisoner && !w.Founder.IsFugitive && w.Founder.PartyBelongedToAsPrisoner == holder,
                    "founder remains in the original custody");
                AssertTrue(w.Spouse.IsPrisoner && !w.Spouse.IsFugitive && w.Spouse.PartyBelongedToAsPrisoner == spouseHolder,
                    "captive spouse remains in separate custody");
                AssertTrue(w.Founder.CurrentSettlement == dungeon && w.Spouse.CurrentSettlement == spouseDungeon, "not teleported out of dungeons");
                AssertTrue(holder.PrisonRoster.Entries.Single().Character.HeroObject == w.Founder
                    && spouseHolder.PrisonRoster.Entries.Single().Character.HeroObject == w.Spouse, "prisoner rosters preserved");
                AssertEqual(17, w.Founder.Gold); AssertEqual(23, w.Spouse.Gold);
                AssertTrue(w.Founder.PartyBelongedTo == null && w.Spouse.PartyBelongedTo == null, "no party spawned for captive nobles");
                AssertTrue(w.Founder.Clan == w.Spouse.Clan && w.Founder.Clan != w.Source, "family belongs to destination");
            }
        }

        private static void TestDepartureCaptiveSiege()
        {
            foreach (var found in new[] { false, true })
            {
                var w = DepartureWorld();
                var holder = HoldDepartureHero(w.Founder, w.Source);
                var spouseHolder = HoldDepartureHero(w.Spouse, w.Source);
                w.Founder.CurrentSettlement!.SiegeEvent = new object();
                w.Spouse.CurrentSettlement!.SiegeEvent = new object();
                var dungeon = w.Founder.CurrentSettlement;
                var action = new MeetingClanAction(found); var terms = DepartureTerms(action, w.Session);
                AssertEqual<string?>(null, action.Unavailable(w.Session), "besieged holding locations do not block captives");
                AssertEqual<string?>(null, action.Validate(w.Session, terms));
                MeetingBatch.Apply(() => { }, () => throw new Exception("unexpected refund"), new[] { action.PrepareEffect(w.Session, terms) });
                AssertTrue(action.IsApplied(w.Session, terms) && w.Founder.IsPrisoner && w.Spouse.IsPrisoner, "only clan membership changes");
                AssertTrue(w.Founder.CurrentSettlement == dungeon && w.Founder.PartyBelongedToAsPrisoner == holder
                    && w.Spouse.PartyBelongedToAsPrisoner == spouseHolder, "custody stays intact during siege");
            }
            var family = DepartureWorld(); HoldDepartureHero(family.Founder, family.Source);
            DepartureParty(family.Spouse).SiegeEvent = new object();
            var reason = ClanDeparture.Family(family.Founder, out _);
            AssertTrue(reason != null && reason.Contains("随迁成员配偶") && reason.Contains("部队正在参与围城"),
                "a free spouse's actual siege names the family member rather than blaming the prisoner");
            family.Spouse.PartyBelongedTo!.SiegeEvent = null;
            family.Spouse.PartyBelongedTo.MapEvent = new object();
            reason = ClanDeparture.Family(family.Founder, out _);
            AssertTrue(reason != null && reason.Contains("随迁成员配偶") && reason.Contains("部队正在交战"), "actual combat remains protected");
        }

        private static void TestDepartureCaptiveRelease()
        {
            var w = DepartureWorld(); var holder = HoldDepartureHero(w.Founder, w.Source);
            var action = new MeetingClanAction(false); var terms = DepartureTerms(action, w.Session);
            var effect = action.PrepareEffect(w.Session, terms);
            var release = new MeetingEffect
            {
                Name = "另行同意释放俘虏", Order = 10,
                Apply = () => ReleaseDepartureHero(w.Founder), IsApplied = () => !w.Founder.IsPrisoner,
                HasChanged = () => !w.Founder.IsPrisoner
            };
            MeetingBatch.Apply(() => { }, () => throw new Exception("unexpected refund"), new[] { effect, release });
            AssertTrue(action.IsApplied(w.Session, terms) && !w.Founder.IsPrisoner && holder.PrisonRoster.Entries.Count == 0,
                "explicit earlier release does not invalidate the same household agreement");

            w = DepartureWorld(); holder = HoldDepartureHero(w.Founder, w.Target);
            var kingdom = new Kingdom { Name = "接纳国" };
            action = new MeetingClanAction(true); terms = DepartureTerms(action, w.Session); terms.Parameters["kingdomId"] = kingdom.StringId;
            // 只模拟原版入国事件已释放俘虏的结果，不在生产迁移逻辑里重写释俘规则。
            TaleWorlds.CampaignSystem.Actions.ChangeKingdomAction.AfterJoin = (clan, _) => ReleaseDepartureHero(clan.Leader!);
            MeetingBatch.Apply(() => { }, () => throw new Exception("unexpected refund"), new[] { action.PrepareEffect(w.Session, terms) });
            AssertTrue(action.IsApplied(w.Session, terms) && !w.Founder.IsPrisoner && w.Founder.PartyBelongedToAsPrisoner == null,
                "native admission release is not overridden by restoring custody");
            AssertEqual(0, holder.PrisonRoster.Entries.Count);
        }

        private static void TestDepartureFounder()
        {
            var w = DepartureWorld(true);
            var party = DepartureParty(w.Founder);
            var spouseParty = DepartureParty(w.Spouse);
            var item = new EquipmentElement(new ItemObject { StringId = "sword", Name = "剑" }, new ItemModifier { StringId = "fine" });
            party.ItemRoster.AddToCounts(item, 7);
            var other = new Hero { Name = "搭乘贵族", Clan = w.Source, IsLord = true, PartyBelongedTo = party };
            party.MemberRoster.Entries.Add(new TroopRosterElement { Character = other.CharacterObject });
            w.Founder.GovernorOf = new object();
            Campaign.Current!.Models.ClanTierModel.CompanionToLordClanStartingTier = 3;
            var action = new MeetingClanAction(true); var terms = DepartureTerms(action, w.Session);
            AssertEqual<string?>(null, action.Validate(w.Session, terms), "zero funding accepted");
            MeetingBatch.Apply(() => { }, () => throw new Exception("unexpected refund"), new[] { action.PrepareEffect(w.Session, terms) });
            AssertTrue(action.IsApplied(w.Session, terms), "production handler bound result");
            var created = w.Founder.Clan!;
            AssertEqual(3, created.Tier, "read game starting tier model");
            AssertTrue(created.Leader == w.Founder && created.Kingdom == null, "independent founder");
            AssertEqual(17, w.Founder.Gold); AssertEqual(23, w.Spouse.Gold);
            AssertEqual(80000, w.Source.Leader!.Gold); AssertEqual(600f, w.Source.Renown);
            AssertTrue(w.Founder.PartyBelongedTo == party && w.Spouse.PartyBelongedTo == spouseParty, "party identity retained");
            AssertTrue(party.ActualClan == created && spouseParty.ActualClan == created, "both party indices moved");
            AssertEqual(7, party.ItemRoster.Single().Amount); AssertTrue(party.ItemRoster.Single().EquipmentElement.ItemModifier == item.ItemModifier, "quality retained");
            AssertTrue(w.Founder.GovernorOf == null, "old governorship removed");
            AssertTrue(other.Clan == w.Source && other.PartyBelongedTo == null, "unlisted noble not carried across clans");
        }

        private static void TestDepartureJoin()
        {
            var w = DepartureWorld(); var party = DepartureParty(w.Founder);
            w.Source.Kingdom = new Kingdom { Name = "旧国" }; w.Target.Kingdom = new Kingdom { Name = "新国" };
            party.Army = new Army { LeaderParty = party };
            var action = new MeetingClanAction(false); var terms = DepartureTerms(action, w.Session); terms.OtherOffer.Gold = 100;
            var n = new MeetingNegotiation();
            AssertEqual<string?>(null, n.Propose(MeetingSide.Other, terms, "", 0, "", t => action.Validate(w.Session, t)));
            var effect = action.PrepareEffect(w.Session, terms);
            AssertEqual<string?>(null, Reply(n, MeetingSide.Self, validate: t => action.Validate(w.Session, t), execute: _ =>
            {
                MeetingBatch.Apply(() => { w.Session.Player.ChangeHeroGold(-100); w.Founder.ChangeHeroGold(100); },
                    () => throw new Exception("unexpected refund"), new[] { effect }); return "完成";
            }));
            AssertEqual(117, w.Founder.Gold); AssertEqual(4900, w.Session.Player.Gold);
            AssertTrue(w.Founder.Clan == w.Target && w.Spouse.Clan == w.Target && party.ActualClan == w.Target, "target clan and party");
            AssertTrue(party.Army == null, "foreign army detached");
            AssertEqual("旧国", w.Source.Kingdom!.Name, "source clan does not defect");
            AssertEqual(2, Clan.All.Count, "join does not create a clan");
        }

        private static void TestDepartureArmy()
        {
            var w = DepartureWorld(); var kingdom = new Kingdom { Name = "共同国家" };
            w.Source.Kingdom = w.Target.Kingdom = kingdom;
            var party = DepartureParty(w.Founder); var army = new Army { LeaderParty = party }; party.Army = army;
            var plan = new ClanDeparture(w.Founder, w.Target, "", null); plan.Apply();
            AssertTrue(party.Army == army, "same faction does not disband army");
            w = DepartureWorld(); party = DepartureParty(w.Session.Player);
            w.Founder.PartyBelongedTo = party; party.MemberRoster.Entries.Add(new TroopRosterElement { Character = w.Founder.CharacterObject });
            plan = new ClanDeparture(w.Founder, null, "新族", null); plan.Apply();
            AssertTrue(w.Founder.PartyBelongedTo == null && party.ActualClan == w.Target && party.LeaderHero == w.Session.Player,
                "passenger leaves, host party preserved");
            w = DepartureWorld(); party = DepartureParty(w.Founder); w.Target.Hostile = true;
            party.CurrentSettlement = new TaleWorlds.CampaignSystem.Settlements.Settlement { MapFaction = w.Source };
            party.IsTransitionInProgress = true; w.Spouse.CurrentSettlement = party.CurrentSettlement;
            new ClanDeparture(w.Founder, w.Target, "", null).Apply();
            AssertTrue(party.CurrentSettlement == null && !party.IsTransitionInProgress && w.Spouse.CurrentSettlement == null,
                "newly hostile settlement cleared for party and idle spouse");
            AssertTrue(w.Founder.PartyBelongedTo == party && party.IsActive, "hostile settlement departure preserves army");
        }

        private static void TestDepartureSnapshot()
        {
            foreach (Action<Hero> change in new Action<Hero>[]
            {
                h => h.Spouse = null, h => DepartureChild(h, "new-child", 1), h => DepartureParty(h),
                h => h.Clan!.Leader = new Hero { IsLord = true, Clan = h.Clan }
            })
            {
                var w = DepartureWorld(); var action = new MeetingClanAction(true); var terms = DepartureTerms(action, w.Session);
                var effect = action.PrepareEffect(w.Session, terms); change(w.Founder);
                AssertTrue(action.Validate(w.Session, terms) != null, "old accepted household rejected");
                AssertTrue(effect.Validate() != null, "bound plan rechecks same snapshot");
                AssertEqual(2, Clan.All.Count, "preview never creates native objects");
            }
        }

        private static void TestDepartureParameters()
        {
            var w = DepartureWorld(); var found = new MeetingClanAction(true); var terms = DepartureTerms(found, w.Session);
            terms.Parameters["clanName"] = w.Source.Name;
            AssertTrue(found.Validate(w.Session, terms) != null, "duplicate name");
            terms.Parameters["clanName"] = ""; AssertTrue(found.Validate(w.Session, terms) != null, "empty name");
            terms = DepartureTerms(found, w.Session); terms.Parameters["familyId"] = "unrelated-third-party";
            AssertTrue(found.Validate(w.Session, terms) != null, "cannot force third party adult");
            var join = new MeetingClanAction(false); terms = DepartureTerms(join, w.Session); terms.Parameters["clanId"] = w.Source.StringId;
            AssertTrue(join.Validate(w.Session, terms) != null, "same clan rejected");
            terms = DepartureTerms(join, w.Session); w.Target.IsNoble = false;
            AssertEqual<string?>(null, join.Validate(w.Session, terms), "early player clan need not be ennobled");
            terms = DepartureTerms(join, w.Session); w.Target.IsEliminated = true;
            AssertTrue(join.Validate(w.Session, terms) != null, "eliminated target rejected");
        }

        private static void TestDepartureConflicts()
        {
            var w = DepartureWorld(); var plan = new ClanDeparture(w.Founder, w.Target, "", null); var effect = plan.Effect();
            AssertTrue(MeetingBatch.Validate(new[] { effect, new MeetingEffect { Resources = new[] { "clan:" + w.Source.StringId } } }) != null,
                "conflicts with source marriage/allegiance");
            AssertTrue(MeetingBatch.Validate(new[] { effect, new MeetingEffect { Resources = new[] { "clan:" + w.Target.StringId } } }) != null,
                "conflicts with destination allegiance");
            MainThreadExecutor.BeginCampaign(); AssertTrue(effect.Validate() != null, "old campaign cannot execute");
            AssertTrue(new MeetingClanAction(true).Validate(w.Session, new MeetingTerms()) != null, "old meeting cannot build a new-epoch plan");
        }

        private static void TestDeparturePartial()
        {
            var w = DepartureWorld(); var plan = new ClanDeparture(w.Founder, null, "部分新族", null);
            w.Founder.AfterClanChange = () => throw new InvalidOperationException("原版成员事件异常");
            var partial = false; var refunded = false;
            try { MeetingBatch.Apply(() => w.Founder.ChangeHeroGold(100), () => refunded = true, new[] { plan.Effect() }); }
            catch (MeetingPartialExecutionException) { partial = true; }
            AssertTrue(partial && !refunded, "native object/member mutation is irreversible partial");
            AssertEqual(117, w.Founder.Gold); AssertEqual(3, Clan.All.Count);
            AssertTrue(plan.Validate() != null, "no retry of created native clan");
            w = DepartureWorld(); var party = DepartureParty(w.Founder); party.Army = new Army { LeaderParty = party };
            plan = new ClanDeparture(w.Founder, null, "未注册族", null);
            TaleWorlds.CampaignSystem.Actions.DisbandArmyAction.AfterDisband = () => throw new InvalidOperationException("军团回调异常");
            partial = false; refunded = false;
            try { MeetingBatch.Apply(() => { }, () => refunded = true, new[] { plan.Effect() }); }
            catch (MeetingPartialExecutionException) { partial = true; }
            AssertTrue(partial && !refunded && party.Army == null, "army changes counted as partial");
            AssertEqual(2, Clan.All.Count, "army callback failure cannot leave a headless registered clan");
            AssertTrue(w.Founder.Clan == w.Source, "membership has not yet changed");
        }

        private static void TestDepartureRecords()
        {
            var w = DepartureWorld(); var kingdom = new Kingdom { Name = "入国" };
            Campaign.Current!.Models.ClanTierModel.VassalEligibleTier = 4;
            var action = new MeetingClanAction(true); var terms = DepartureTerms(action, w.Session); terms.Parameters["kingdomId"] = kingdom.StringId;
            CampaignEventDispatcher.Instance.ThrowOnCreated = true;
            var warnings = MeetingBatch.Apply(() => { }, () => throw new Exception("unexpected refund"), new[] { action.PrepareEffect(w.Session, terms) });
            AssertEqual(1, warnings.Count); AssertTrue(action.IsApplied(w.Session, terms), "callback failed after true effect");
            AssertTrue(w.Founder.Clan!.Kingdom == kingdom && w.Founder.Clan.Tier == 4, "read current vassal tier");
            action.RecordApplied(w.Session, terms, "交涉者支付1234金币");
            var count = AgentManager.DepartureDiaries.Count;
            AssertEqual(3, count, "old leader and both migrating adults only");
            AssertTrue(AgentManager.DepartureDiaries.All(d => d.Kind == "情报" && d.Text.Contains("建立") && !d.Text.Contains("1234")
                && !d.Text.Contains("愤怒") && !d.Text.Contains("报复")), "objective facts, no negotiation leakage");
            action.RecordApplied(w.Session, terms, ""); AssertEqual(count, AgentManager.DepartureDiaries.Count, "one record per committed effect");
        }

        private static void TestDepartureIdentity()
        {
            var w = DepartureWorld(); w.Founder.Clan = w.Target;
            w.Founder.Spouse = null;
            var text = ContextBuilder.BuildObjectiveRelationship(w.Founder, w.Session.Player);
            AssertTrue(text.Contains("同一家族的成员"), "adopted member described accurately");
            AssertFalse(text.Contains("同母") || text.Contains("同父") || text.Contains("同族亲属"), "null parents are not common ancestors");
        }
    }
}
