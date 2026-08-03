using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.LogEntries;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AIChronicle
{
    public static partial class ToolExecutor
    {
        private static string ExecuteMoveToSettlement(string settlementName, bool activate)
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";

            var target = FindSettlement(settlementName);
            if (target == null)
                return $"[错误] 未找到名为 \"{settlementName}\" 的定居点";

            var party = AIChatClient.CurrentHero.PartyBelongedTo;
            if (party == null)
                return $"[错误] {AIChatClient.CurrentHero.Name} 没有带领部队（可能在城中担任总督、被俘虏或编入军团）";

            if (!party.IsActive)
                return $"[错误] 部队当前不可用";

            if (party.CurrentSettlement == target)
            {
                var action = PartyBehaviorManager.GetOrCreateAction(AIChatClient.CurrentHero);
                action.Behavior = AiBehavior.GoToSettlement;
                action.TargetSettlement = target;
                action.ActivateOnComplete = activate;
                return $"已经在{target.Name}了。";
            }

            if (party.CurrentSettlement != null)
            {
                party.CurrentSettlement = null;
            }

            var navType = party.IsCurrentlyAtSea
                ? MobileParty.NavigationType.Naval
                : MobileParty.NavigationType.Default;

            party.SetMoveGoToSettlement(target, navType, false);

            var action2 = PartyBehaviorManager.GetOrCreateAction(AIChatClient.CurrentHero);
            action2.Behavior = AiBehavior.GoToSettlement;
            action2.TargetSettlement = target;
            action2.ActivateOnComplete = activate;

            return $"部队已出发前往{target.Name}。" + (activate ? " 到达后将自动唤醒。" : "");
        }

        private static string ExecuteWaitAtSettlement(int hours, bool activate)
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";

            if (hours <= 0)
                return "[错误] 等待时长必须大于 0 小时";

            if (hours > 720)
                return "[错误] 等待时长不能超过 720 小时（30 天）";

            var party = AIChatClient.CurrentHero.PartyBelongedTo;
            var currentSettlement = party?.CurrentSettlement;

            if (currentSettlement == null)
                return $"[错误] {AIChatClient.CurrentHero.Name} 当前不在任何定居点内";

            var action = PartyBehaviorManager.GetOrCreateAction(AIChatClient.CurrentHero);
            action.TargetSettlement = currentSettlement;
            action.WaitHours = hours;
            action.ActivateOnComplete = activate;

            if (action.ArrivedAt == null)
                action.ArrivedAt = CampaignTime.Now;

            return $"将在{currentSettlement.Name}停留{hours}小时（约{hours / 24}天）。" + (activate ? " 等待完毕后将自动唤醒。" : "");
        }

        private static string ExecuteRaidSettlement(string settlementName)
        {
            if (AIChatClient.CurrentHero == null) return "[错误] 无当前部队指挥官";

            var target = FindSettlement(settlementName);
            if (target == null) return $"[错误] 未找到定居点：{settlementName}";
            if (!target.IsVillage) return $"[错误] {target.Name} 不是村庄，请使用 besiege_settlement 攻打城镇/城堡";

            var party = AIChatClient.CurrentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {AIChatClient.CurrentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            SetPartyAiAction.GetActionForRaidingSettlement(party, target, MobileParty.NavigationType.Default, false, false);
            var ra = PartyBehaviorManager.GetOrCreateAction(AIChatClient.CurrentHero);
            ra.Behavior = AiBehavior.RaidSettlement;
            ra.TargetSettlement = target;
            return $"部队已出发劫掠{target.Name}。";
        }

        private static string ExecuteBesiegeSettlement(string settlementName)
        {
            if (AIChatClient.CurrentHero == null) return "[错误] 无当前部队指挥官";

            var target = FindSettlement(settlementName);
            if (target == null) return $"[错误] 未找到定居点：{settlementName}";
            if (!target.IsTown && !target.IsCastle) return $"[错误] {target.Name} 不是城镇或城堡，请使用 raid_settlement 劫掠村庄";

            var party = AIChatClient.CurrentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {AIChatClient.CurrentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            SetPartyAiAction.GetActionForBesiegingSettlement(party, target, MobileParty.NavigationType.Default, false);
            var ba = PartyBehaviorManager.GetOrCreateAction(AIChatClient.CurrentHero);
            ba.Behavior = AiBehavior.BesiegeSettlement;
            ba.TargetSettlement = target;
            return $"部队已出发围攻{target.Name}。";
        }

        /// <summary>拉军团：以军事目标为指向召集本国领主成军团，交还原版 AI 指挥（集结→扑目标→攻城/劫掠→解散）。</summary>
        private static string ExecuteFormArmy(string targetSettlementName, string armyType)
        {
            if (AIChatClient.CurrentHero == null) return "[错误] 无当前部队指挥官";

            var hero = AIChatClient.CurrentHero;
            var party = hero.PartyBelongedTo;
            if (party == null) return $"[错误] {hero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            var kingdom = hero.MapFaction as Kingdom;
            if (kingdom == null) return "[错误] 你不属于任何王国，无法召集军团";
            if (hero.Clan?.IsUnderMercenaryService == true) return "[错误] 雇佣兵不能召集军团";

            var target = FindSettlement(targetSettlementName);
            if (target == null) return $"[错误] 未找到定居点：{targetSettlementName}";

            var typeLower = (armyType ?? "").Trim().ToLowerInvariant();
            Army.ArmyTypes selectedType;
            if (typeLower.Contains("围") || typeLower.Contains("攻") || typeLower == "besiege")
            {
                if (!target.IsTown && !target.IsCastle) return $"[错误] 围攻军团的目标必须是城镇或城堡";
                selectedType = Army.ArmyTypes.Besieger;
            }
            else if (typeLower.Contains("掠") || typeLower.Contains("劫") || typeLower == "raid")
            {
                if (!target.IsVillage) return $"[错误] 劫掠军团的目标必须是村庄";
                selectedType = Army.ArmyTypes.Raider;
            }
            else if (typeLower == "defend" || typeLower.Contains("解围") || typeLower.Contains("防御"))
            {
                if (!target.IsTown && !target.IsCastle) return $"[错误] 防御军团的目标必须是城镇或城堡";
                if (!target.IsUnderSiege) return "[错误] 该定居点当前未被围攻——防御军团只用于集结兵力解围；若想让部队驻守，请用 defend_settlement";
                selectedType = Army.ArmyTypes.Defender;
            }
            else if (typeLower.Contains("守") || typeLower.Contains("驻") || typeLower.Contains("护"))
            {
                return "[错误] 驻守/守护不是军团类型——军团用于进攻（围攻/劫掠）或解围（防御）。单支部队驻守请用 defend_settlement";
            }
            else
                return "[错误] 军团类型必须是 围攻/劫掠/防御（besiege/raid/defend）";

            // 原版条件：影响力>100、王国交战、部曲充足、氏族领袖 → 算候选成员
            if (!Campaign.Current.Models.ArmyManagementCalculationModel.CanLordCreateArmy(party, out var possibleMembers))
                return "[错误] 当前无法召集军团（需影响力超过 100、王国处于交战、部曲充足，且你是氏族领袖）";
            if (possibleMembers.Count == 0)
                return "[错误] 本国附近没有可召集的领主部队";

            // 创建军团：原版接管集结与作战（军团长等待成员→成员奔赴→扑向目标）
            kingdom.CreateArmy(hero, target, selectedType, possibleMembers);

            // 清掉军团长现有 PendingAction，避免模组每帧重发指令覆盖原版集结
            PartyBehaviorManager.RemoveAction(hero);

            var typeName = selectedType switch
            {
                Army.ArmyTypes.Besieger => "围攻",
                Army.ArmyTypes.Raider => "劫掠",
                _ => "防御"
            };
            return $"军团已组建（{typeName} {target.Name}），正召集本国领主集结，原版军团将接管指挥。你无需再发指令；军团使命结束后自行评估下一步。";
        }

        private static string ExecuteEngageParty(string targetEntityId)
        {
            if (AIChatClient.CurrentHero == null) return "[错误] 无当前部队指挥官";
            if (string.IsNullOrEmpty(targetEntityId)) return "[错误] 请指定要攻击的目标实体";

            var party = AIChatClient.CurrentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {AIChatClient.CurrentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            var targetParty = FindPartyByEntityId(targetEntityId);
            if (targetParty == null) return $"[错误] 未找到 {targetEntityId} 的部队";
            if (targetParty == party) return $"[错误] 不能攻击自己的部队";

            SetPartyAiAction.GetActionForEngagingParty(party, targetParty, MobileParty.NavigationType.Default, false);
            var ea = PartyBehaviorManager.GetOrCreateAction(AIChatClient.CurrentHero);
            ea.Behavior = AiBehavior.EngageParty;
            ea.TargetParty = targetParty;
            return $"部队已出发追击{targetParty.Name?.ToString() ?? targetEntityId}。";
        }

        private static string ExecuteDefendSettlement(string settlementName)
        {
            if (AIChatClient.CurrentHero == null) return "[错误] 无当前部队指挥官";

            var target = FindSettlement(settlementName);
            if (target == null) return $"[错误] 未找到定居点：{settlementName}";

            var party = AIChatClient.CurrentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {AIChatClient.CurrentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            SetPartyAiAction.GetActionForDefendingSettlement(party, target, MobileParty.NavigationType.Default, false, false);
            var da = PartyBehaviorManager.GetOrCreateAction(AIChatClient.CurrentHero);
            da.Behavior = AiBehavior.DefendSettlement;
            da.TargetSettlement = target;
            da.CheckInHours = 72f;
            da.CreatedAt = CampaignTime.Now;
            return $"部队已出发驻防{target.Name}。";
        }

        private static string ExecutePatrolSettlement(string settlementName)
        {
            if (AIChatClient.CurrentHero == null) return "[错误] 无当前部队指挥官";

            var target = FindSettlement(settlementName);
            if (target == null) return $"[错误] 未找到定居点：{settlementName}";

            var party = AIChatClient.CurrentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {AIChatClient.CurrentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            SetPartyAiAction.GetActionForPatrollingAroundSettlement(party, target, MobileParty.NavigationType.Default, false, false);
            var pa = PartyBehaviorManager.GetOrCreateAction(AIChatClient.CurrentHero);
            pa.Behavior = AiBehavior.PatrolAroundPoint;
            pa.TargetSettlement = target;
            pa.CheckInHours = 48f;
            pa.CreatedAt = CampaignTime.Now;
            return $"部队已出发巡逻{target.Name}周边。";
        }

        private static string ExecuteEscortParty(string targetEntityId)
        {
            if (AIChatClient.CurrentHero == null) return "[错误] 无当前部队指挥官";
            if (string.IsNullOrEmpty(targetEntityId)) return "[错误] 请指定要护送的目标实体";

            var party = AIChatClient.CurrentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {AIChatClient.CurrentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            var targetParty = FindPartyByEntityId(targetEntityId);
            if (targetParty == null) return $"[错误] 未找到 {targetEntityId} 的部队";
            if (targetParty == party) return $"[错误] 不能护送自己的部队";

            SetPartyAiAction.GetActionForEscortingParty(party, targetParty, MobileParty.NavigationType.Default, false, false);
            var esa = PartyBehaviorManager.GetOrCreateAction(AIChatClient.CurrentHero);
            esa.Behavior = AiBehavior.EscortParty;
            esa.TargetParty = targetParty;
            esa.CheckInHours = 24f;
            esa.CreatedAt = CampaignTime.Now;
            return $"部队已出发护送{targetParty.Name?.ToString() ?? targetEntityId}。";
        }

        private static string ExecuteGoAroundParty(string? targetEntityId)
        {
            if (AIChatClient.CurrentHero == null) return "[错误] 无当前部队指挥官";

            var party = AIChatClient.CurrentHero.PartyBelongedTo;
            if (party == null) return $"[错误] {AIChatClient.CurrentHero.Name} 没有带领部队";
            if (!party.IsActive) return "[错误] 部队当前不可用";

            var targetParty = FindPartyByEntityId(targetEntityId);
            if (targetParty == null) return $"[错误] 未找到目标部队";
            if (targetParty == party) return $"[错误] 不能绕过自己的部队";

            SetPartyAiAction.GetActionForGoingAroundParty(party, targetParty, MobileParty.NavigationType.Default, false);
            return $"部队已绕开{targetParty.Name?.ToString() ?? "目标部队"}。";
        }

        private static string ExecuteCancelAction()
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前部队指挥官";

            var hero = AIChatClient.CurrentHero;
            PartyBehaviorManager.RemoveAction(hero);

            // 修复：复位部队当前命令——原实现只删跟踪动作，DefaultBehavior 仍指向原目标，
            // 部队会继续围城/行军/劫掠。SetMoveModeHold 把行为置为 Hold 并清空移动参数，交由原版 AI 接管。
            var party = hero.PartyBelongedTo;
            if (party != null && party.IsActive)
                party.SetMoveModeHold();

            return "当前任务已取消，部队恢复自主行动。";
        }

        private static string ExecuteQueryAvailableTroops()
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";

            var hero = AIChatClient.CurrentHero;
            var party = hero.PartyBelongedTo;
            var settlement = hero.CurrentSettlement ?? party?.CurrentSettlement
                ?? FindNearbySettlement(party);
            if (settlement == null)
                return "[错误] 你当前不在任何定居点内。请先移动到城镇或村庄。";

            if (hero == party?.LeaderHero && party != null)
            { }
            else if (hero != party?.LeaderHero)
                return "[错误] 你不是部队指挥官，无法征兵。";

            var sb = new StringBuilder();
            sb.AppendLine($"===== {settlement.Name} 可招募兵种 =====");

            if (settlement.IsCastle)
            {
                sb.AppendLine("  （城堡通常没有可招募的平民兵源，请前往下属村庄征兵。）");
                sb.AppendLine();
                sb.AppendLine("使用 query_settlement_villages 查看该城堡的附属村庄。");
                return sb.ToString().TrimEnd();
            }

            if (settlement.IsVillage && settlement.Village.VillageState != Village.VillageStates.Normal)
            {
                var stateDesc = settlement.Village.VillageState switch
                {
                    Village.VillageStates.Looted => "已被劫掠",
                    Village.VillageStates.BeingRaided => "正在被劫掠",
                    Village.VillageStates.ForcedForSupplies => "已被强制征粮",
                    Village.VillageStates.ForcedForVolunteers => "已被强制征兵",
                    _ => "状态异常"
                };
                sb.AppendLine($"  （该村庄{stateDesc}，暂时无法征兵。等待村庄恢复正常后再来。）");
                return sb.ToString().TrimEnd();
            }

            var myFaction = hero.MapFaction;
            var ownerClan = settlement.OwnerClan;
            if (ownerClan != null && myFaction != null && myFaction.IsAtWarWith(ownerClan))
            {
                sb.AppendLine("  （该定居点属于敌对阵营，无法和平征兵。可用 raid_settlement 强制征兵或抢粮。）");
                return sb.ToString().TrimEnd();
            }

            var recruiter = party?.LeaderHero ?? hero;
            var hasAny = false;

            foreach (var notable in settlement.Notables)
            {
                if (notable == null || !notable.IsAlive) continue;
                var volunteers = new List<CharacterObject>();
                for (int j = 0; j < 6; j++)
                    volunteers.Add(notable.VolunteerTypes[j]);
                var hasTroops = volunteers.Any(v => v != null);
                if (!hasTroops) continue;

                var maxIndex = Campaign.Current.Models.VolunteerModel
                    .MaximumIndexHeroCanRecruitFromHero(recruiter, notable);

                var troopLines = new List<string>();
                for (int i = 0; i <= maxIndex && i < volunteers.Count; i++)
                {
                    var troop = volunteers[i];
                    if (troop == null) continue;
                    var cost = (int)Campaign.Current.Models.PartyWageModel
                        .GetTroopRecruitmentCost(troop, recruiter, false).ResultNumber;
                    troopLines.Add($"    {troop.Name} (T{troop.GetBattleTier()}) — {cost} 金/人");
                }

                if (troopLines.Count > 0)
                {
                    hasAny = true;
                    sb.AppendLine($"  {notable.Name}：");
                    foreach (var line in troopLines)
                        sb.AppendLine(line);
                }
            }

            if (!hasAny)
                sb.AppendLine("  （无可用兵种）");

            sb.AppendLine();
            sb.AppendLine("使用 recruit_troops(兵种名, 数量) 招募。");
            return sb.ToString().TrimEnd();
        }

        private static string ExecuteRecruitTroops(string troopName, int count)
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (string.IsNullOrEmpty(troopName))
                return "[错误] 请指定要招募的兵种名称";
            if (count <= 0)
                return "[错误] 招募数量必须大于 0";

            var hero = AIChatClient.CurrentHero;
            var party = hero.PartyBelongedTo;
            if (party == null)
                return $"[错误] {hero.Name} 没有带领部队";

            var settlement = hero.CurrentSettlement ?? party.CurrentSettlement;
            if (settlement == null)
                return "[错误] 当前不在任何定居点内";

            var recruiter = party.LeaderHero;
            if (recruiter == null)
                return "[错误] 无法确定招募者";

            foreach (var notable in settlement.Notables)
            {
                if (notable == null || !notable.IsAlive) continue;
                var volunteers = new List<CharacterObject>();
                for (int j = 0; j < 6; j++)
                    volunteers.Add(notable.VolunteerTypes[j]);
                var maxIndex = Campaign.Current.Models.VolunteerModel
                    .MaximumIndexHeroCanRecruitFromHero(recruiter, notable);

                for (int i = 0; i <= maxIndex && i < volunteers.Count; i++)
                {
                    var troop = volunteers[i];
                    if (troop == null) continue;
                    var tName = troop.Name?.ToString() ?? "";
                    if (!tName.Contains(troopName) && !troopName.Contains(tName)) continue;

                    var costPer = (int)Campaign.Current.Models.PartyWageModel
                        .GetTroopRecruitmentCost(troop, recruiter, false).ResultNumber;

                    // 修复：int 溢出防护（原 costPer*count 溢出为负可绕过金币检查并反向加钱）+ 部队上限检查
                    if (count > 10000)
                        return "[错误] 单次招募数量过大（上限 10000）";
                    long totalCostL = (long)costPer * count;
                    if (totalCostL > int.MaxValue)
                        return "[错误] 招募总花费超出范围";
                    var totalCost = (int)totalCostL;

                    if (recruiter.Gold < totalCost)
                        return $"[错误] 金币不足。需要 {totalCost} 金，当前只有 {recruiter.Gold} 金。";

                    var newSize = party.Party.NumberOfAllMembers + count;
                    var sizeLimit = party.Party.PartySizeLimit;
                    if (newSize > sizeLimit)
                        return $"[错误] 招募后会超过部队上限（当前 {party.Party.NumberOfAllMembers}/{sizeLimit}，需 {newSize}）。";

                    party.AddElementToMemberRoster(troop, count);
                    recruiter.ChangeHeroGold(-totalCost);
                    notable.VolunteerTypes[i] = null;

                    return $"已从 {notable.Name} 处招募 {count} 名 {tName}，花费 {totalCost} 金币。";
                }
            }

            return $"[未找到] 当前定居点无可招募的 \"{troopName}\"。使用 query_available_troops 查看可招兵种。";
        }

        private static string ExecuteUpgradeTroops(string troopName, string targetTroopName, int count)
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (string.IsNullOrEmpty(troopName) || count <= 0)
                return "[错误] 请指定要升级的兵种名称和数量";

            var hero = AIChatClient.CurrentHero;
            var party = hero.PartyBelongedTo;
            if (party == null)
                return $"[错误] {hero.Name} 没有带领部队";

            var roster = party.MemberRoster;
            var elements = roster.GetTroopRoster();
            var upgradeModel = Campaign.Current.Models.PartyTroopUpgradeModel;

            foreach (var elem in elements)
            {
                if (elem.Character == null || elem.Character.IsHero) continue;
                var tName = elem.Character.Name?.ToString() ?? "";
                if (!tName.Contains(troopName) && !troopName.Contains(tName)) continue;

                var troop = elem.Character;
                var available = elem.Number - elem.WoundedNumber;
                if (available < count)
                    return $"[错误] {tName} 只有 {available} 名可升级（另有 {elem.WoundedNumber} 名受伤），无法升级 {count} 名。";

                if (!upgradeModel.IsTroopUpgradeable(party.Party, troop))
                    return $"[错误] {tName} 没有可升级的路径。";

                CharacterObject? target = null;
                var upgrades = troop.UpgradeTargets;
                if (upgrades == null || upgrades.Length == 0)
                    return $"[错误] {tName} 没有可升级的路径。";
                if (!string.IsNullOrEmpty(targetTroopName))
                {
                    foreach (var t in upgrades)
                    {
                        if (t == null) continue;
                        var ttName = t.Name?.ToString() ?? "";
                        if (ttName.Contains(targetTroopName) || targetTroopName.Contains(ttName))
                        {
                            target = t;
                            break;
                        }
                    }
                    if (target == null)
                        return $"[错误] {tName} 不能升级为 \"{targetTroopName}\"。可用升级路径见 query_party_troops。";
                }
                else
                {
                    target = upgrades.FirstOrDefault(t => t != null);
                    if (target == null)
                        return $"[错误] {tName} 没有可用的升级目标。";
                }

                if (!upgradeModel.CanPartyUpgradeTroopToTarget(party.Party, troop, target))
                {
                    if (!upgradeModel.DoesPartyHaveRequiredItemsForUpgrade(party.Party, target))
                        return $"[错误] 缺少升级所需的装备（战马等）。";
                    if (!upgradeModel.DoesPartyHaveRequiredPerksForUpgrade(party.Party, troop, target, out var perk))
                        return $"[错误] 需要 {perk?.Name} 特长才能升级。";
                }

                var xpCost = upgradeModel.GetXpCostForUpgrade(party.Party, troop, target);
                var goldCost = (int)upgradeModel.GetGoldCostForUpgrade(party.Party, troop, target).ResultNumber;
                var totalGold = goldCost * count;

                if (elem.Xp < xpCost * count)
                    return $"[错误] 经验不足。{tName} 总经验为 {elem.Xp}，升级 {count} 名共需 {xpCost * count} XP。";

                if (hero.Gold < totalGold)
                    return $"[错误] 金币不足。需要 {totalGold} 金，当前只有 {hero.Gold} 金。";

                // 修复：扣除升级所需物品（战马等）——原版 PartyScreenLogic 会真正移除，此前只检查不扣除
                var requiredCategory = target.UpgradeRequiresItemFromCategory;
                if (requiredCategory != null)
                {
                    var itemsRemoved = 0;
                    for (int i = 0; i < party.ItemRoster.Count && itemsRemoved < count; i++)
                    {
                        var ie = party.ItemRoster[i];
                        var item = ie.EquipmentElement.Item;
                        if (item == null) continue;
                        if (item.ItemCategory == requiredCategory)
                        {
                            var toRemove = Math.Min(ie.Amount, count - itemsRemoved);
                            party.ItemRoster.AddToCounts(item, -toRemove);
                            itemsRemoved += toRemove;
                        }
                    }
                    if (itemsRemoved < count)
                        return $"[错误] 缺少升级所需的 {requiredCategory.GetName()}（需要 {count}，实际可扣除 {itemsRemoved}）。";
                }

                var oldXp = elem.Xp;
                roster.AddToCounts(troop, -count, false, 0, -(xpCost * count));
                roster.AddToCounts(target, count, false, 0, 0);
                hero.ChangeHeroGold(-totalGold);

                return $"已将 {count} 名 {tName} 升级为 {target.Name}，花费 {totalGold} 金币。";
            }

            return $"[未找到] 部队中没有兵种 \"{troopName}\"。使用 query_party_troops 查看部队详情。";
        }

        private static string ExecuteBuyFood(int days)
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (days <= 0)
                return "[错误] 天数必须大于 0";

            var hero = AIChatClient.CurrentHero;
            var party = hero.PartyBelongedTo;
            if (party == null)
                return $"[错误] {hero.Name} 没有带领部队";

            var settlement = hero.CurrentSettlement ?? party.CurrentSettlement;
            if (settlement == null)
                return "[错误] 当前不在任何定居点内";

            if (settlement.ItemRoster.TotalFood <= 0)
                return $"[错误] {settlement.Name} 没有粮食可购买。";

            var foodModel = Campaign.Current.Models.PartyFoodBuyingModel;
            var currentFood = party.TotalFoodAtInventory;
            var dailyConsumption = Math.Abs(party.FoodChange);
            if (dailyConsumption <= 0)
                dailyConsumption = (float)party.MemberRoster.TotalManCount / 20f;

            var targetFood = currentFood + (int)(dailyConsumption * days);
            var bought = 0;
            var spent = 0;
            var items = new List<string>();

            var maxAttempts = 20;
            for (int i = 0; i < maxAttempts && party.TotalFoodAtInventory < targetFood; i++)
            {
                try
                {
                    foodModel.FindItemToBuy(party, settlement, out var itemElement, out var price);
                    if (itemElement.EquipmentElement.Item == null) break;

                    var toBuy = Math.Min(10, (int)((targetFood - party.TotalFoodAtInventory) / Math.Max(1, itemElement.EquipmentElement.Item.Value)) + 1);
                    if (toBuy <= 0) toBuy = 1;

                    var cost = (int)(price * toBuy);
                    if (hero.Gold < cost) break;

                    SellItemsAction.Apply(settlement.Party, party.Party, itemElement, toBuy, settlement);
                    hero.ChangeHeroGold(-cost);
                    bought += toBuy;
                    spent += cost;
                    items.Add(itemElement.EquipmentElement.Item.Name?.ToString() ?? "粮食");
                }
                catch
                {
                    break;
                }
            }

            if (bought == 0)
            {
                if (hero.Gold <= 0)
                    return $"[错误] 金币不足，无法购买粮食。";
                return $"[错误] {settlement.Name} 粮食已售罄或价格过高。";
            }

            return $"购买了 {bought} 份粮食（{string.Join("、", items.Distinct())}），花费 {spent} 金币。当前粮食可供约 {party.GetNumDaysForFoodToLast():F0} 天。";
        }

        private static string ExecuteQuerySettlementVillages(string settlementName)
        {
            if (string.IsNullOrEmpty(settlementName))
                return "[错误] 请提供定居点名称";

            var settlement = FindSettlement(settlementName);
            if (settlement == null)
                return $"[未找到] 名称为 \"{settlementName}\" 的定居点";

            if (!settlement.IsTown && !settlement.IsCastle)
                return $"[错误] {settlement.Name} 是村庄，没有附属村庄。请查询城镇或城堡。";

            var sb = new StringBuilder();
            sb.AppendLine($"===== {settlement.Name} 的附属村庄 =====");

            var villages = settlement.BoundVillages;
            if (villages == null || villages.Count == 0)
            {
                sb.AppendLine("（无附属村庄）");
            }
            else
            {
                foreach (var v in villages)
                {
                    var vSettlement = v.Settlement;
                    var vName = vSettlement?.Name?.ToString() ?? "?";
                    var hearths = v.Hearth.ToString("F0");
                    var stateTag = v.VillageState != Village.VillageStates.Normal
                        ? $" [{v.VillageState}]" : "";
                    var ownerTag = vSettlement?.OwnerClan?.Name?.ToString();
                    sb.AppendLine($"  {vName} — 户数: {hearths}{stateTag}（{ownerTag}）");
                }
            }

            return sb.ToString().TrimEnd();
        }

        private static Settlement? FindNearbySettlement(MobileParty? party)
        {
            if (party == null || !party.IsActive) return null;
            var pos = party.GetPosition2D;
            foreach (var s in Settlement.All)
            {
                if (!s.IsTown && !s.IsCastle && !s.IsVillage) continue;
                var gate = s.GatePosition.ToVec2();
                var dx = pos.X - gate.X;
                var dy = pos.Y - gate.Y;
                if ((dx * dx + dy * dy) < 25f)
                    return s;
            }
            return null;
        }

        private static bool PrisonerNameMatches(CharacterObject? troop, string name)
        {
            if (troop?.Name == null || string.IsNullOrEmpty(name)) return false;
            var n = troop.Name.ToString();
            return n.Contains(name) || name.Contains(n);
        }

        /// <summary>释放自己部队中的俘虏（贵族英雄或普通士兵）。释放英雄 → EndCaptivityAction（对方成为逃亡者，事件入史）；
        /// 释放普通士兵 → 直接从俘虏名册移除。</summary>
        private static string ExecuteReleasePrisoner(string prisonerName, int count, bool all)
        {
            var hero = AIChatClient.CurrentHero;
            if (hero == null) return "[错误] 无当前领主";

            var party = hero.PartyBelongedTo;
            if (party == null)
                return $"[错误] {hero.Name} 没有带领部队，无法释放俘虏";

            var prisoners = party.PrisonRoster.GetTroopRoster();

            // 释放全部
            if (all || string.IsNullOrEmpty(prisonerName))
            {
                var releasedHeroes = 0;
                var releasedTroops = 0;
                foreach (var p in prisoners.Where(p => p.Character != null).ToList())
                {
                    if (p.Character!.IsHero)
                    {
                        var h = p.Character.HeroObject;
                        if (h == null) continue;
                        EndCaptivityAction.ApplyByReleasedByChoice(h, hero);
                        releasedHeroes++;
                    }
                    else
                    {
                        party.PrisonRoster.RemoveTroop(p.Character, p.Number);
                        releasedTroops += p.Number;
                    }
                }
                return $"已释放全部俘虏：贵族 {releasedHeroes} 人，士兵 {releasedTroops} 人。";
            }

            var match = prisoners.FirstOrDefault(p => p.Character != null && PrisonerNameMatches(p.Character, prisonerName));
            if (match.Character == null)
                return $"[未找到] 部队中没有名为 \"{prisonerName}\" 的俘虏。使用 query_party_troops 查看俘虏列表。";

            if (match.Character.IsHero)
            {
                var h = match.Character.HeroObject;
                if (h == null) return "[错误] 无法解析该俘虏";
                EndCaptivityAction.ApplyByReleasedByChoice(h, hero);
                return $"已释放贵族 {h.Name}。对方成为逃亡者，返回领地休整。";
            }

            var n = count > 0 ? Math.Min(count, match.Number) : match.Number;
            party.PrisonRoster.RemoveTroop(match.Character, n);
            return $"已释放 {n} 名 {match.Character.Name} 俘虏。";
        }

        /// <summary>处决自己部队中的贵族俘虏（仅限贵族英雄）。处决是不可逆的重罪：
        /// 斩首者名誉下降，受害者氏族/亲友/同阵营贵族对斩首者的好感大幅下降（移植原版玩家处决惩罚给 NPC）。</summary>
        private static string ExecuteExecutePrisoner(string prisonerName)
        {
            var hero = AIChatClient.CurrentHero;
            if (hero == null) return "[错误] 无当前领主";

            var party = hero.PartyBelongedTo;
            if (party == null)
                return $"[错误] {hero.Name} 没有带领部队，无法处决俘虏";

            var prisoners = party.PrisonRoster.GetTroopRoster();
            var match = prisoners.FirstOrDefault(p =>
                p.Character != null && p.Character.IsHero && PrisonerNameMatches(p.Character, prisonerName));
            if (match.Character == null)
                return $"[未找到] 部队中没有名为 \"{prisonerName}\" 的贵族俘虏（处决仅限贵族）。使用 query_party_troops 查看俘虏列表。";

            var victim = match.Character.HeroObject;
            if (victim == null) return "[错误] 无法解析该贵族";
            if (victim == Hero.MainHero)
                return "[错误] 不能处决玩家本人。";
            if (victim.IsPlayerCompanion)
                return $"[错误] {victim.Name} 是玩家的同伴，不能处决。";
            if (!victim.IsLord)
                return $"[错误] {victim.Name} 不是贵族，不能处以斩首之刑。";

            // isForced=true：无论"死亡模式"是否开启都强制执行（参考原版叛乱清除成员的用法）。
            KillCharacterAction.ApplyByExecution(victim, hero, true, true);

            // 政治代价——受 MCM「处决无惩罚」控制（默认开启 = 无惩罚，玩家与 NPC 都可随便处决）：
            // 仅 NPC 执行者手动施加（玩家执行者的处决代价由原版行为处理，避免叠加；原版惩罚由 Harmony 补丁禁用）。
            if (MySettings.Instance?.ExecutionNoPenalty != true && !hero.IsHumanPlayerCharacter)
            {
                // 名誉下降：移植玩家处决的 -1000 名誉经验（约降 1 级），下限 -2。
                var honorLevel = hero.GetTraitLevel(DefaultTraits.Honor);
                if (honorLevel > -2)
                    hero.SetTraitLevel(DefaultTraits.Honor, honorLevel - 1);

                // 关系惩罚：走游戏自带的 DefaultExecutionRelationModel（已内置 NPC 执行者分支），
                // 受害者氏族成员/亲友/同阵营贵族/名誉高尚的贵族对斩首者好感下降。
                var affected = new List<string>();
                foreach (var other in Hero.AllAliveHeroes)
                {
                    if (other == hero || other == victim) continue;
                    var penalty = Campaign.Current.Models.ExecutionRelationModel
                        .GetRelationChangeForExecutingHero(victim, other, out var showQuickNotification);
                    if (penalty != 0)
                    {
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(hero, other, penalty, showQuickNotification);
                        affected.Add($"{other.Name}{penalty:+0;-0}");
                    }
                }

                var result = $"已处决贵族 {victim.Name}。{hero.Name} 名誉受损，人神共愤，卡拉迪亚贵族间对其恶名昭彰。";
                if (affected.Count > 0)
                    result += " 受此影响的关系：" + string.Join("，", affected.Take(8)) + (affected.Count > 8 ? " 等" : "");
                return result;
            }

            return $"已处决贵族 {victim.Name}。";
        }

        /// <summary>
        /// 天意建族（家族补充系统）：创建一个新的贵族家族。成员 3-6 人由程序随机生成（偏向年轻），
        /// 家族等级 2（恰好够当封臣又看得出是新族），投效指定王国（封臣或雇佣兵），族长自动获得部队。
        /// 仅「天意」（__fate__）实体可调用。
        /// </summary>
        /// <summary>
        /// 天意单次激活的建族预算：一次只建一族。
        /// 背景：天意提示词虽写明"一次只创建一个家族"，但 LLM 可能连建多族——
        /// 曾出现一次激活连建 5 族（每族 3-6 英雄 + 族长部队），与游戏其他状态变更撞车导致原生层崩溃。
        /// 这里用代码强制限流：每次天意激活（ProcessClanReplenishmentEvent 开头）重置预算，超限即拒绝。
        /// </summary>
        private static int _fateClanBudget;
    }
}
