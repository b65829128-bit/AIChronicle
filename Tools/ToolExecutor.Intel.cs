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
        private static string ExecuteQueryPartyTroops(string? targetEntityId)
        {
            var party = FindPartyByEntityId(targetEntityId);
            if (party == null)
                return $"[错误] 未找到目标部队：{targetEntityId}";

            var leader = party.LeaderHero;

            // 军情迷雾：自己/同阵营全量精确；异国按距离与可达性分级（近距/远距/传闻）。
            // 背景：原实现无视距离与阵营，任何部队的金币/经验/装备等机密一览无余，导致「完美信息 → 和平均衡」。
            var intelTier = DetermineIntelTier(party, leader);
            if (intelTier != IntelTier.Full)
                return BuildFuzzyPartyReport(party, leader, intelTier);

            var gold = leader?.Gold ?? 0;
            var wage = party.TotalWage;
            var memberRoster = party.MemberRoster;
            var elements = memberRoster.GetTroopRoster();
            var totalMen = elements.Sum(e => e.Number);
            var totalWounded = elements.Sum(e => e.WoundedNumber);
            var maxSize = party.Party.PartySizeLimit;
            var foodDays = party.GetNumDaysForFoodToLast();

            var sb = new StringBuilder();
            sb.AppendLine($"===== {(leader != null ? $"{leader.Name}的部队" : party.Name?.ToString() ?? "部队")} =====");
            sb.AppendLine($"金币: {gold}  |  日薪: {wage}");
            sb.AppendLine($"兵力 {totalMen} 人, 伤 {totalWounded} 人, 上限 {maxSize}");
            sb.AppendLine($"粮食: {party.TotalFoodAtInventory} (约 {foodDays:F1} 天)");
            AppendArmyInfo(sb, party, leader);
            sb.AppendLine();

            if (elements.Count == 0)
            {
                sb.AppendLine("（无士兵）");
                sb.AppendLine();
            }
            else
            {
                var upgradeModel = Campaign.Current.Models.PartyTroopUpgradeModel;
                foreach (var elem in elements.Where(e => e.Character != null && !e.Character.IsHero))
                {
                    var troop = elem.Character;
                    var tier = troop.GetBattleTier();
                    var hasXp = elem.Xp > 0;
                    sb.Append($"  {troop.Name} (T{tier}) × {elem.Number}");
                    if (elem.WoundedNumber > 0)
                        sb.Append($" (伤 {elem.WoundedNumber})");
                    var upgrades = troop.UpgradeTargets;
                    if (upgrades != null && upgrades.Length > 0)
                    {
                        var xpRequired = upgradeModel.GetXpCostForUpgrade(party.Party, troop, upgrades[0]);
                        sb.Append($" [经验 {elem.Xp}/{xpRequired}]");
                    }
                    sb.AppendLine();

                    if (upgrades != null && upgradeModel.IsTroopUpgradeable(party.Party, troop))
                    {
                        foreach (var target in upgrades)
                        {
                            if (target == null) continue;
                            if (!upgradeModel.CanPartyUpgradeTroopToTarget(party.Party, troop, target))
                                continue;
                            var xpCost = upgradeModel.GetXpCostForUpgrade(party.Party, troop, target);
                            var goldCost = (int)upgradeModel.GetGoldCostForUpgrade(party.Party, troop, target).ResultNumber;
                            sb.AppendLine($"    → {target.Name} [需 {xpCost} XP, {goldCost} 金]");
                        }
                    }
                }
            }

            var prisoners = party.PrisonRoster.GetTroopRoster();
            var prisonerCount = prisoners.Sum(p => p.Number);
            if (prisonerCount > 0)
            {
                sb.AppendLine("===== 俘虏 =====");
                foreach (var p in prisoners.Where(p => p.Character != null && p.Character.IsHero))
                {
                    var hero = p.Character.HeroObject;
                    if (hero == null) continue;
                    var desc = hero.Clan != null ? hero.Clan.Name?.ToString() ?? "无氏族" : "无氏族";
                    sb.AppendLine($"  [贵族] {hero.Name}（{desc}）");
                }
                foreach (var p in prisoners.Where(p => p.Character != null && !p.Character.IsHero))
                {
                    var tier = p.Character.GetBattleTier();
                    var recruitStatus = "";
                    try
                    {
                        if (Campaign.Current.Models.PrisonerRecruitmentCalculationModel
                            .IsPrisonerRecruitable(party.Party, p.Character, out var _))
                            recruitStatus = " — 可招募";
                    }
                    catch { }
                    sb.AppendLine($"  {p.Character.Name} (T{tier}) × {p.Number}{recruitStatus}");
                }
                sb.AppendLine();
            }

            var itemRoster = party.ItemRoster;
            var itemGroups = new Dictionary<string, List<(string name, int count)>>();
            foreach (var ie in itemRoster)
            {
                var item = ie.EquipmentElement.Item;
                if (item == null) continue;
                var count = ie.Amount;
                var name = item.Name?.ToString() ?? "?";

                var category = item.ItemType switch
                {
                    ItemObject.ItemTypeEnum.Horse => item.ItemCategory == DefaultItemCategories.WarHorse ? "战马" : "马",
                    ItemObject.ItemTypeEnum.OneHandedWeapon => "武器",
                    ItemObject.ItemTypeEnum.TwoHandedWeapon => "武器",
                    ItemObject.ItemTypeEnum.Polearm => "武器",
                    ItemObject.ItemTypeEnum.Bow => "武器",
                    ItemObject.ItemTypeEnum.Crossbow => "武器",
                    ItemObject.ItemTypeEnum.Thrown => "武器",
                    ItemObject.ItemTypeEnum.Arrows => "弹药",
                    ItemObject.ItemTypeEnum.Bolts => "弹药",
                    ItemObject.ItemTypeEnum.Shield => "武器",
                    ItemObject.ItemTypeEnum.Sling => "武器",
                    ItemObject.ItemTypeEnum.SlingStones => "弹药",
                    ItemObject.ItemTypeEnum.HeadArmor => "盔甲",
                    ItemObject.ItemTypeEnum.BodyArmor => "盔甲",
                    ItemObject.ItemTypeEnum.LegArmor => "盔甲",
                    ItemObject.ItemTypeEnum.HandArmor => "盔甲",
                    ItemObject.ItemTypeEnum.Cape => "盔甲",
                    ItemObject.ItemTypeEnum.ChestArmor => "盔甲",
                    ItemObject.ItemTypeEnum.HorseHarness => "马铠",
                    ItemObject.ItemTypeEnum.Banner => "旗帜",
                    ItemObject.ItemTypeEnum.Book => "书籍",
                    _ => item.IsFood ? "粮食" : "货物"
                };
                if (!itemGroups.ContainsKey(category))
                    itemGroups[category] = new List<(string, int)>();
                itemGroups[category].Add((name, count));
            }

            if (itemGroups.Count > 0)
            {
                sb.AppendLine("===== 物品 =====");
                foreach (var kv in itemGroups.OrderBy(k => k.Key))
                {
                    var grouped = kv.Value.GroupBy(x => x.name)
                        .Select(g => (name: g.Key, total: g.Sum(x => x.count)))
                        .ToList();
                    foreach (var g in grouped)
                        sb.AppendLine($"  [{kv.Key}] {g.name} × {g.total}");
                }
            }

            if (leader != null)
            {
                var eq = leader.BattleEquipment;
                var eqSlots = new (EquipmentIndex Index, string Label)[]
                {
                    (EquipmentIndex.Weapon0, "武器"),
                    (EquipmentIndex.Weapon1, "武器"),
                    (EquipmentIndex.Weapon2, "武器"),
                    (EquipmentIndex.Weapon3, "武器"),
                    (EquipmentIndex.Horse, "坐骑"),
                    (EquipmentIndex.HorseHarness, "马铠"),
                    (EquipmentIndex.Head, "头盔"),
                    (EquipmentIndex.Body, "身甲"),
                    (EquipmentIndex.Leg, "腿甲"),
                    (EquipmentIndex.Gloves, "手甲"),
                    (EquipmentIndex.Cape, "披风"),
                };

                var eqLines = new List<string>();
                foreach (var (index, label) in eqSlots)
                {
                    var elem = eq.GetEquipmentFromSlot(index);
                    if (elem.Item != null)
                        eqLines.Add($"{label}: {elem.Item.Name}");
                }

                if (eqLines.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"===== 装备栏（{leader.Name}） =====");
                    foreach (var line in eqLines)
                        sb.AppendLine($"  {line}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 军团上下文（地图上可见的公开信息）：查询部队是否隶属军团、谁是军团长、全军合计兵力与成员。
        /// 修复：agent 查询自己部队时若身处军团，会误以为只带本部人马。
        /// </summary>
        private static void AppendArmyInfo(StringBuilder sb, MobileParty party, Hero? leader)
        {
            var army = party.Army;
            if (army == null) return;

            var armyLeader = army.LeaderParty?.LeaderHero;
            var total = 0;
            var memberNames = new List<string>();
            try
            {
                foreach (var mp in army.Parties)
                {
                    if (mp.MemberRoster != null) total += mp.MemberRoster.TotalManCount;
                    var ml = mp.LeaderHero?.Name?.ToString();
                    if (!string.IsNullOrEmpty(ml)) memberNames.Add(ml);
                }
            }
            catch { }

            var armyLeaderName = armyLeader?.Name?.ToString() ?? "未知";
            var own = party.MemberRoster?.TotalManCount ?? 0;
            if (leader != null && armyLeader == leader)
                sb.AppendLine($"军团：该部队是军团首领 {armyLeaderName} 的本部（全军约 {total} 人）");
            else
                sb.AppendLine($"军团：隶属 {armyLeaderName} 的军团（全军约 {total} 人，含本部 {own} 人）");
            sb.AppendLine($"军团成员：{string.Join("、", memberNames)}");
        }

        /// <summary>军情情报等级：Full=全量精确，NearScout=近距侦察，FarScout=远方模糊，Rumor=传闻。</summary>
        private enum IntelTier { Full, NearScout, FarScout, Rumor }

        /// <summary>
        /// 决定查看某支部队的情报等级。
        /// 自己/自己的部队、同阵营（王国/同盟/同一服役王国）→ 全量精确；
        /// 异国 → 按「距离 + 能否直接观察（跨海/不可通行地形）」分级。
        /// </summary>
        private static IntelTier DetermineIntelTier(MobileParty party, Hero? leader)
        {
            var actor = AIChatClient.CurrentHero;
            // 无当前代理上下文（如后台事件）时保守给全量，避免误伤合法调用
            if (actor == null || leader == null)
                return IntelTier.Full;

            if (leader == actor) return IntelTier.Full;
            if (actor.PartyBelongedTo == party) return IntelTier.Full;

            // 同阵营：军情共享（含雇佣兵服役于同一王国）
            if (actor.MapFaction != null && actor.MapFaction == party.MapFaction)
                return IntelTier.Full;

            var actorPos = GetHeroPosition(actor);
            var targetPos = GetPartyPosition(party, leader);
            if (actorPos == null || targetPos == null)
                return IntelTier.Rumor; // 位置不明，只有传闻

            // 跨海 / 中间隔着不可通行地形：无法实地侦察，降为传闻
            if (!IsDirectlyObservable(actor, party, actorPos.Value, targetPos.Value))
                return IntelTier.Rumor;

            var dist = actorPos.Value.Distance(targetPos.Value);
            var (scoutRadius, midRadius) = GetIntelRadii();
            if (dist <= scoutRadius) return IntelTier.NearScout;
            if (dist <= midRadius) return IntelTier.FarScout;
            return IntelTier.Rumor;
        }

        private static Vec2? GetHeroPosition(Hero? hero)
        {
            if (hero == null) return null;
            var p = hero.PartyBelongedTo;
            if (p != null) return p.GetPosition2D;
            if (hero.CurrentSettlement != null) return hero.CurrentSettlement.GatePosition.ToVec2();
            return null;
        }

        private static Vec2? GetPartyPosition(MobileParty party, Hero? leader)
        {
            if (party != null) return party.GetPosition2D;
            if (leader?.CurrentSettlement != null) return leader.CurrentSettlement.GatePosition.ToVec2();
            return null;
        }

        /// <summary>
        /// 判断能否「直接观察」目标：双方是否在海中，以及两点连线是否被不可通行地形（海域/山脉）阻断。
        /// 出错时回退为可达，避免误伤。
        /// </summary>
        private static bool IsDirectlyObservable(Hero actor, MobileParty party, Vec2 fromPos, Vec2 toPos)
        {
            try
            {
                if (actor.PartyBelongedTo?.IsCurrentlyAtSea == true) return false;
                if (party?.IsCurrentlyAtSea == true) return false;

                PathFaceRecord? fromFace = null;
                if (actor.PartyBelongedTo != null)
                    fromFace = actor.PartyBelongedTo.CurrentNavigationFace;
                else if (actor.CurrentSettlement != null)
                    fromFace = actor.CurrentSettlement.GatePosition.Face;
                if (fromFace == null) return true;

                return Campaign.Current.MapSceneWrapper.IsLineToPointClear(fromFace.Value, fromPos, toPos, 0.5f);
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// 情报分级半径。用「地图实际尺度 × 比例」作相对锚，不依赖绝对 km——
        /// 骑砍全图仅约 2000-3000 地图单位（1 单位 ≈ 1 米），凭直觉定绝对值必然踩坑。
        /// </summary>
        private static (float scoutRadius, float midRadius) GetIntelRadii()
        {
            var extent = ComputeMapExtent();
            var frac = MySettings.Instance?.IntelligenceScoutRadiusFraction ?? 0.2f;
            var scout = Math.Max(100f, extent * frac);
            var mid = Math.Max(scout * 1.5f, extent * 0.45f);
            return (scout, mid);
        }

        /// <summary>全图尺度：城镇/城堡包围盒的最大边。兜底 2500（原版地图实测量级）。</summary>
        private static float ComputeMapExtent()
        {
            try
            {
                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;
                int count = 0;
                foreach (var s in Settlement.All)
                {
                    if (!s.IsTown && !s.IsCastle) continue;
                    var p = s.GatePosition.ToVec2();
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                    count++;
                }
                if (count == 0) return 2500f;
                var extent = Math.Max(maxX - minX, maxY - minY);
                return extent > 0 ? extent : 2500f;
            }
            catch
            {
                return 2500f;
            }
        }

        /// <summary>异国部队的模糊化报告（不含金币/军饷/经验/升级/俘虏/物品/装备等机密）。</summary>
        private static string BuildFuzzyPartyReport(MobileParty party, Hero? leader, IntelTier tier)
        {
            var name = leader != null ? leader.Name?.ToString() ?? "?" : party.Name?.ToString() ?? "?";
            var totalMen = SafeTotalMen(party);
            var sb = new StringBuilder();

            if (tier == IntelTier.NearScout)
            {
                var (lo, hi) = FuzzBand(totalMen, 0.2f);
                sb.AppendLine($"===== {name}的部队（近距侦察） =====");
                sb.AppendLine($"兵力：约 {hi} 人（{lo}-{hi}，情报误差 ±20%）");
                var (loMax, hiMax) = FuzzBand(SafePartySizeLimit(party), 0.2f);
                if (hiMax > 0)
                    sb.AppendLine($"规模上限：约 {hiMax} 人（{loMax}-{hiMax}，情报误差 ±20%）");
                var wounded = SafeWounded(party);
                if (wounded > 0)
                    sb.AppendLine($"伤兵：约 {FuzzRound(wounded, 10)} 人");
                sb.AppendLine($"兵种构成：{SafeComposition(party, totalMen)}");
                sb.AppendLine($"当前位置：{SafeLocationDesc(party, leader)}");
                sb.Append(BuildFuzzyArmyLine(party));
                sb.AppendLine("【侦察情报】近距观察所得，兵力、规模上限与兵种构成基本可信；金币、军饷、兵种经验、升级路线、俘虏与物资详情无法从外部探知。");
                return sb.ToString().TrimEnd();
            }

            if (tier == IntelTier.FarScout)
            {
                var (lo, hi) = FuzzBand(totalMen, 0.4f);
                sb.AppendLine($"===== {name}的部队（远方情报） =====");
                sb.AppendLine($"兵力：约 {lo}-{hi} 人（情报不确定）");
                var (loMax, hiMax) = FuzzBand(SafePartySizeLimit(party), 0.4f);
                if (hiMax > 0)
                    sb.AppendLine($"规模上限：约 {loMax}-{hiMax} 人（情报不确定）");
                sb.AppendLine($"兵种构成：{SafeComposition(party, totalMen)}");
                sb.AppendLine($"装备水平：{SafeGearQuality(party, totalMen)}");
                sb.AppendLine($"当前位置：{SafeLocationDesc(party, leader)}");
                sb.Append(BuildFuzzyArmyLine(party));
                sb.AppendLine("【情报不确定】远处得来的消息，仅有大致兵力、规模上限与构成印象；具体军情（金币、军饷、兵种经验、升级路线、俘虏、物资、装备）无从得知。");
                return sb.ToString().TrimEnd();
            }

            // Rumor：纯定性传闻
            sb.AppendLine($"===== {name}的部队（传闻） =====");
            sb.AppendLine($"据说：{StrengthDesc(totalMen)}");
            sb.AppendLine($"位置：{SafeLocationDesc(party, leader)}");
            sb.AppendLine("【传闻】未经证实，可能严重失真。不可作为决策依据。");
            return sb.ToString().TrimEnd();
        }

        private static int SafeTotalMen(MobileParty party)
        {
            try { return party.MemberRoster?.TotalManCount ?? 0; } catch { return 0; }
        }

        /// <summary>军团隶属（地图可见的公开信息，供近/远侦察档位）：该部队是否隶属某军团、军团长是谁。</summary>
        private static string BuildFuzzyArmyLine(MobileParty party)
        {
            try
            {
                if (party.Army == null) return "";
                var armyLeader = party.Army.LeaderParty?.LeaderHero?.Name?.ToString();
                return $"军团：隶属{(string.IsNullOrEmpty(armyLeader) ? "一支军团" : armyLeader + "的军团")}\n";
            }
            catch { return ""; }
        }

        private static int SafePartySizeLimit(MobileParty party)
        {
            try { return party.Party?.PartySizeLimit ?? 0; } catch { return 0; }
        }

        private static int SafeWounded(MobileParty party)
        {
            try { return party.MemberRoster?.GetTroopRoster().Sum(e => e.WoundedNumber) ?? 0; } catch { return 0; }
        }

        private static (int lo, int hi) FuzzBand(int n, float ratio)
        {
            var lo = FuzzRound(Math.Max(0, n) * (1f - ratio), 10);
            var hi = FuzzRound(Math.Max(0, n) * (1f + ratio), 10);
            return (lo, hi);
        }

        private static int FuzzRound(float v, int step)
        {
            return (int)Math.Round(v / step) * step;
        }

        private static string SafeComposition(MobileParty party, int totalMen)
        {
            try
            {
                if (totalMen <= 0) return "不详";
                var elements = party.MemberRoster?.GetTroopRoster();
                if (elements == null) return "不详";
                int inf = 0, ranged = 0, cav = 0, horse = 0;
                foreach (var e in elements)
                {
                    var t = e.Character;
                    if (t == null || t.IsHero) continue;
                    var n = e.Number;
                    switch (t.DefaultFormationClass)
                    {
                        case FormationClass.Infantry: inf += n; break;
                        case FormationClass.Ranged: ranged += n; break;
                        case FormationClass.Cavalry: cav += n; break;
                        case FormationClass.HorseArcher: horse += n; break;
                    }
                }
                var cats = new (string name, int count)[]
                {
                    ("步兵", inf), ("射手", ranged), ("骑兵", cav), ("弓骑兵", horse)
                };
                var top = cats.OrderByDescending(c => c.count).Where(c => c.count > 0).Take(2).ToList();
                if (top.Count == 0) return "不详";
                var parts = top.Select(c => $"{c.name}约{Math.Round(c.count * 100f / totalMen)}%");
                return string.Join("、", parts) + "为主";
            }
            catch { return "不详"; }
        }

        private static string SafeGearQuality(MobileParty party, int totalMen)
        {
            try
            {
                if (totalMen <= 0) return "不详";
                var elements = party.MemberRoster?.GetTroopRoster();
                if (elements == null) return "不详";
                double totalTier = 0;
                int count = 0;
                foreach (var e in elements)
                {
                    var t = e.Character;
                    if (t == null || t.IsHero) continue;
                    totalTier += t.GetBattleTier() * e.Number;
                    count += e.Number;
                }
                if (count == 0) return "不详";
                var avg = totalTier / count;
                if (avg >= 3.5) return "精良";
                if (avg >= 2.0) return "普通";
                return "简陋";
            }
            catch { return "不详"; }
        }

        private static string StrengthDesc(int totalMen)
        {
            if (totalMen <= 0) return "兵力微弱";
            if (totalMen < 100) return "兵力薄弱（不足百人）";
            if (totalMen < 300) return "兵力一般（数百人规模）";
            if (totalMen < 800) return "兵力较强（近千人之众）";
            return "兵力雄厚（千军之众）";
        }

        private static string SafeLocationDesc(MobileParty party, Hero? leader)
        {
            try
            {
                var s = party?.CurrentSettlement ?? leader?.CurrentSettlement;
                if (s != null) return s.Name?.ToString() ?? "某定居点";
                if (party != null && !party.IsActive) return "已解散";
                return "野外行军";
            }
            catch { return "不明"; }
        }

    }
}
