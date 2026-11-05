using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace AIChronicle
{
    /// <summary>一次已经绑定人物、家庭和原部队的转族。不判断忠诚、好感、资助金额或敌我关系。</summary>
    public sealed class ClanDeparture
    {
        public const string Independent = "independent";
        public Hero Founder { get; }
        public IReadOnlyList<Hero> Members { get; }
        public Clan Source { get; }
        public Clan? Destination { get; private set; }
        public bool CreatesClan { get; }
        public string Description { get; }
        private readonly Hero _oldLeader;
        private readonly Kingdom? _kingdom;
        private readonly string _name;
        private readonly string _familyId;
        private readonly long _epoch = MainThreadExecutor.CampaignEpoch;
        private readonly Settlement? _home;
        private readonly Dictionary<Hero, MobileParty?> _parties;
        private readonly Dictionary<Hero, object?> _governors;
        private readonly Dictionary<MobileParty, Clan> _partyClans;
        private readonly Dictionary<MobileParty, Army?> _armies;
        private bool _started;
        private bool _created;
        private bool _finished;

        public ClanDeparture(Hero founder, Clan? destination, string name, Kingdom? kingdom)
        {
            var error = Family(founder, out var members);
            if (error != null) throw new InvalidOperationException(error);
            Founder = founder;
            Members = members;
            Source = founder.Clan!;
            _oldLeader = Source.Leader!;
            Destination = destination;
            CreatesClan = destination == null;
            _kingdom = destination?.Kingdom ?? kingdom;
            _name = name.Trim();
            _familyId = FamilyId(founder, members);
            _home = founder.HomeSettlement ?? Source.HomeSettlement ?? founder.BornSettlement;
            _parties = members.ToDictionary(h => h, h => (MobileParty?)h.PartyBelongedTo);
            _governors = members.ToDictionary(h => h, h => (object?)h.GovernorOf);
            _partyClans = members.Where(h => h.PartyBelongedTo?.LeaderHero == h)
                .Select(h => h.PartyBelongedTo!).Distinct().ToDictionary(p => p, p => p.ActualClan);
            _armies = _partyClans.Keys.ToDictionary(p => p, p => (Army?)p.Army);
            Description = founder.Name + "离开" + Source.Name + "家族，" + (CreatesClan
                ? "建立" + _name + "家族并担任领袖；" + (_kingdom == null ? "保持独立" : "加入" + _kingdom.Name)
                : "加入" + destination!.Name + "家族")
                + "。迁移成员：" + string.Join("、", members.Select(h => h.Name.ToString()))
                + "；保留各自金币、装备及本人率领的部队，封地与原族公共资产留在原族；原版外交释俘规则照常生效。";
        }

        public static string? Family(Hero hero, out List<Hero> members)
        {
            members = new List<Hero>();
            if (Campaign.Current == null) return "战役未加载。";
            var clan = hero.Clan;
            if (clan == null || !ValidClan(clan)) return "须属于仍然存在的普通贵族家族。";
            if (!hero.IsLord || hero.IsChild || !hero.IsAlive || hero.IsTemplate) return "只有成年、存活的贵族本人可以决定离族。";
            if (clan.Leader == hero) return "家族领袖不能通过成员离族事项迁走整个家族。";
            members.Add(hero);
            var spouse = hero.Spouse;
            if (spouse?.IsAlive == true)
            {
                if (spouse == clan.Leader || spouse.Clan?.Leader == spouse) return "配偶是家族领袖，不能通过此事项拆分夫妻。";
                if (spouse.Clan != clan || spouse.Spouse != hero || !spouse.IsLord || spouse.IsChild)
                    return "夫妻当前归属或婚姻状态不一致，不能共同迁移。";
                members.Add(spouse);
            }
            // 仅迁移同属原族的未成年子女；成年子女、父母、兄弟姐妹各自决定。
            var parents = members.ToArray();
            members.AddRange(parents.SelectMany(h => h.Children).Where(h => h.IsAlive && h.IsChild && h.Clan == clan).Distinct());
            foreach (var member in members)
            {
                // 原版儿童通常尚未出场；只改归属，保留儿童状态，不能因此阻止整个家庭分家。
                if (!member.IsAlive || member.IsDisabled || (member.IsNotSpawned && !member.IsChild) || member.IsFugitive || member.IsTraveling)
                    return member.Name + "当前无法迁移（未出场、逃亡、旅行中或不可用）。";
                // 俘虏的 CurrentSettlement 来自关押地点；牢城被围不等于俘虏本人参战。
                if (!member.IsPrisoner)
                {
                    var subject = (member == hero ? "" : "随迁成员") + member.Name;
                    if (member.PartyBelongedTo?.MapEvent != null) return subject + "的部队正在交战，暂时不能迁移。";
                    if (member.PartyBelongedTo?.SiegeEvent != null) return subject + "的部队正在参与围城，暂时不能迁移。";
                    if (member.CurrentSettlement?.SiegeEvent != null) return subject + "所在定居点正在被围攻，暂时不能迁移。";
                }
                if (member.PartyBelongedTo?.LeaderHero == member && !member.PartyBelongedTo.IsLordParty)
                    return member.Name + "率领的不是普通贵族部队，暂时不能连同迁移。";
                if (member.PartyBelongedTo != null && (!member.PartyBelongedTo.IsActive || member.PartyBelongedTo.IsDisbanding))
                    return member.Name + "所在部队不可用或正在解散，暂时不能迁移。";
                if (member.IsPregnant && member.Spouse?.IsAlive != true)
                    return member.Name + "尚有未出生子女，须在分娩后处理家庭归属。";
            }
            return null;
        }

        public static bool ValidClan(Clan clan) => !clan.IsEliminated && !clan.IsBanditFaction && !clan.IsRebelClan
            && !clan.IsMinorFaction && (clan.IsNoble || clan == Clan.PlayerClan) && clan.Leader?.IsAlive == true;

        /// <summary>选项同时固定随迁家庭，婚姻、成年或部队变化后旧版同意不能扩张到新的人物。</summary>
        public static string FamilyId(Hero founder, IReadOnlyList<Hero> members) => founder.StringId + "|" + founder.Clan!.Leader!.StringId + "|"
            + string.Join(";", members.OrderBy(h => h.StringId, StringComparer.Ordinal).Select(h => h.StringId + ":"
                + h.Clan!.StringId + ":" + h.Spouse?.StringId + ":" + h.PartyBelongedTo?.StringId));

        public string? Validate()
        {
            if (_epoch != MainThreadExecutor.CampaignEpoch) return "原战役已经结束。";
            if (_started) return "这次家族迁移已经开始，不能重复执行。";
            var invalid = Family(Founder, out var current);
            if (invalid != null) return invalid;
            if (Founder.Clan != Source || Source.Leader != _oldLeader || FamilyId(Founder, current) != _familyId)
                return "原家族、随迁家庭或部队已经变化，请重新拟定条件。";
            if (_partyClans.Any(p => p.Key.ActualClan != p.Value || !Members.Contains(p.Key.LeaderHero)))
                return "部队归属或领队已经变化，请重新拟定条件。";
            if (CreatesClan)
            {
                if (_home == null || Founder.Culture == null) return "无法确定新家族的文化或居所。";
                if (_name.Length > 50 || _name.Any(char.IsControl)) return "家族名称须为 1 到 50 个有效字符。";
                var naming = FactionHelper.IsClanNameApplicable(_name);
                if (!naming.Item1) return naming.Item2;
                if (_kingdom?.IsEliminated == true) return "目标王国已不存在。";
            }
            else if (Destination == null || !ValidClan(Destination) || Destination == Source || Destination.Kingdom != _kingdom)
                return "目标家族或所属王国已经变化。";
            return null;
        }

        public MeetingEffect Effect() => new()
        {
            Name = Description, Order = 25,
            Resources = new[] { "clan:" + Source.StringId }.Concat(Destination == null ? Array.Empty<string>() : new[] { "clan:" + Destination.StringId })
                .Concat(Members.Select(h => "hero:" + h.StringId)).ToArray(),
            Validate = Validate, Apply = Apply, IsApplied = IsApplied, HasChanged = HasChanged
        };

        public void Apply()
        {
            var invalid = Validate();
            if (invalid != null) throw new InvalidOperationException(invalid);
            _started = true;
            try { ApplyCore(); }
            finally
            {
                foreach (var member in Members.Where(h => h.Clan != Source)) EntityManager.RefreshEntity(member);
            }
        }

        private void ApplyCore()
        {
            // 先处理原部队/任职；这些原版动作可能触发外部回调，不能让失败留下无族长的新族。
            var targetFaction = (IFaction?)_kingdom ?? Destination;
            foreach (var party in _partyClans.Keys)
            {
                if (party.MapFaction != targetFaction && party.Army != null)
                {
                    if (party.Army.LeaderParty == party) DisbandArmyAction.ApplyByUnknownReason(party.Army);
                    else party.Army = null;
                }
            }
            foreach (var member in Members)
            {
                if (member.GovernorOf != null) ChangeGovernorAction.RemoveGovernorOf(member);
                var party = member.PartyBelongedTo;
                if (party != null && party.LeaderHero != member && !_partyClans.ContainsKey(party) && party.ActualClan != Destination)
                    MakeHeroFugitiveAction.Apply(member); // 只离开他人部队，绝不销毁本人率领的部队。
            }
            if (CreatesClan)
            {
                var banner = Banner.CreateRandomClanBanner();
                var name = new TextObject(_name);
                Destination = Clan.CreateClan("aichronicle_branch");
                _created = true; // 即使后续初始化失败，原生对象已存在，不能退款后自动重建。
                Destination.ChangeClanName(name, name);
                Destination.Culture = Founder.Culture;
                Destination.IsNoble = true;
                Destination.Banner = banner;
                Destination.Color = Destination.Banner.GetPrimaryColor();
                Destination.Color2 = Destination.Banner.GetFirstIconColor();
            }
            var destination = Destination!;
            var newFaction = (IFaction?)_kingdom ?? destination;
            // 人物和部队归属是两个独立索引，均要更新。
            foreach (var member in Members)
            {
                if (CreatesClan && member == Founder) destination.SetLeader(member);
                else member.Clan = destination;
            }
            if (CreatesClan)
            {
                destination.SetInitialHomeSettlement(_home!);
                var model = Campaign.Current!.Models.ClanTierModel;
                var tier = _kingdom == null ? model.CompanionToLordClanStartingTier : Math.Max(model.CompanionToLordClanStartingTier, model.VassalEligibleTier);
                destination.AddRenown(model.GetRequiredRenownForTier(tier), false);
            }
            foreach (var party in _partyClans.Keys)
            {
                party.ActualClan = destination;
                party.Party.SetVisualAsDirty();
                PartyBehaviorManager.RemoveAction(party.LeaderHero!);
                party.SetMoveModeHold();
            }
            if (CreatesClan && _kingdom != null) ChangeKingdomAction.ApplyByJoinToKingdom(destination, _kingdom);
            foreach (var member in Members)
            {
                // 同伴和外族贵族可能搭乘本人的部队：只迁移明确同意的家庭，其他英雄留在原归属。
                if (member.PartyBelongedTo?.LeaderHero == member)
                {
                    foreach (var other in member.PartyBelongedTo.MemberRoster.GetTroopRoster().Where(t => t.Character.IsHero)
                        .Select(t => t.Character.HeroObject).Where(h => h.Clan != destination).ToList())
                        MakeHeroFugitiveAction.Apply(other);
                    var party = member.PartyBelongedTo;
                    if (party.CurrentSettlement != null && newFaction.IsAtWarWith(party.CurrentSettlement.MapFaction))
                    {
                        if (party.IsTransitionInProgress) party.CancelNavigationTransition();
                        LeaveSettlementAction.ApplyForParty(party);
                    }
                }
                // 俘虏的 CurrentSettlement 可能是关押城堡；转族不把俘虏作为普通驻留人物移出牢笼。
                else if (!member.IsPrisoner && member.PartyBelongedTo == null && member.CurrentSettlement != null
                    && newFaction.IsAtWarWith(member.CurrentSettlement.MapFaction)) MakeHeroFugitiveAction.Apply(member);
                member.UpdateHomeSettlement();
            }
            foreach (var member in Source.Heroes) member.UpdateHomeSettlement();
            foreach (var member in destination.Heroes) member.UpdateHomeSettlement();
            destination.CalculateMidSettlement();
            _finished = true;
            if (!IsApplied()) throw new InvalidOperationException("未能核实全部家族成员与部队的迁移结果。");
            if (CreatesClan) CampaignEventDispatcher.Instance.OnClanCreated(destination, false);
        }

        public bool IsApplied() => _finished && Destination != null && Members.All(h => h.Clan == Destination && h.GovernorOf == null)
            && _partyClans.Keys.All(p => p.ActualClan == Destination && p.IsActive && p.LeaderHero?.PartyBelongedTo == p)
            && (!CreatesClan || Destination.Leader == Founder && Destination.Kingdom == _kingdom);

        public bool HasChanged() => _created || Members.Any(h => h.Clan != Source || h.PartyBelongedTo != _parties[h]
            || !ReferenceEquals(h.GovernorOf, _governors[h])) || _partyClans.Any(p => p.Key.ActualClan != p.Value || p.Key.Army != _armies[p.Key]);

        public void Record()
        {
            var text = Founder.Name + "离开" + Source.Name + "家族，" + (CreatesClan ? "建立" + Destination!.Name + "家族并担任领袖" : "加入" + Destination!.Name + "家族")
                + "；迁移成员：" + string.Join("、", Members.Select(h => h.Name.ToString()))
                + "；现属" + (_kingdom?.Name.ToString() ?? "独立家族") + "。";
            // 家族成员变更是关键客观事实，静默直接入日记，不受普通见闻的日上限挤掉，也不激活收件人。
            foreach (var hero in new[] { _oldLeader, Destination!.Leader }.Concat(Members).Distinct())
                if (hero != null && hero.IsAlive && hero != Hero.MainHero)
                    AgentManager.AppendSystemDiary(EntityManager.GetOrCreateEntity(hero).Id, "情报", text);
            HistoryRecorder.RecordDiplomacyEvent(CreatesClan ? "clan_created" : "clan_member_joined", text);
        }
    }
}
