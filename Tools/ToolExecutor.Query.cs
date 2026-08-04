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
        private static IEnumerable<Hero> AllHeroesForQuery()
        {
            var seen = new HashSet<Hero>();
            foreach (var clan in Clan.All)
            {
                if (clan == null) continue;
                foreach (var h in clan.Heroes)
                    if (h != null && seen.Add(h))
                        yield return h;
            }
            foreach (var h in Hero.AllAliveHeroes)
                if (h != null && seen.Add(h))
                    yield return h;
        }

        private static string ExecuteQueryCharacter(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "[错误] 请提供人物名称";
            name = name.Trim();

            // 修复：支持按编号（StringId，形如 CharacterObject_2840）精确查找——重名人物多，列传/查档时
            // 优先用事件里给出的编号精查，避免按姓名子串误匹配到同名者（曾出现史官把被处决的
            // 狼皮部落利夫里斯写成另一氏族的利夫里斯，氏族、年龄全错）。
            var byId = AllHeroesForQuery().FirstOrDefault(h => h.StringId != null
                && h.StringId.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
                return BuildCharacterProfile(byId);

            // 用"所有氏族成员（含已故）+ 所有在世英雄"枚举——史官立传需要查询已死人物；原 AllAliveHeroes 查不到死人
            var matches = AllHeroesForQuery().Where(h =>
            {
                var heroName = h.Name?.ToString() ?? "";
                return heroName.Contains(name) || name.Contains(heroName);
            }).ToList();

            if (matches.Count == 0)
                return "[未找到] 名为 \"" + name + "\" 的人物";

            if (matches.Count == 1)
                return BuildCharacterProfile(matches[0]);

            // 重名消歧：列出所有候选人（含编号/氏族/王国/年龄），要求用精确编号重新查询
            var sb = new StringBuilder();
            sb.AppendLine($"【模糊匹配】名为 \"{name}\" 的人物有 {matches.Count} 位，请用精确编号重新查询（query_character(\"编号\")），避免张冠李戴：");
            foreach (var hero in matches)
            {
                var ageDesc = hero.IsDead ? "已故" : "在世";
                sb.AppendLine($"  [{hero.StringId ?? "?"}] {hero.Name}（{(hero.Clan?.Name?.ToString() ?? "无氏族")}·{(hero.Clan?.Kingdom?.Name?.ToString() ?? "无王国")}，{(int)hero.Age}岁，{ageDesc}）");
            }
            sb.AppendLine();
            sb.AppendLine("若查询的是已故人物（如为史官立传），优先使用事件描述中给出的编号精查。");
            return sb.ToString();
        }

        private static string BuildCharacterProfile(Hero hero)
        {
            var heroName = hero.Name?.ToString() ?? "";
            var sb = new StringBuilder();
            sb.AppendLine("【系统公开档案 — 以下信息为卡拉迪亚公认事实，不容质疑】");
            sb.AppendLine("===== 该人物：" + heroName + (hero.IsDead ? "（已故）" : "") + " =====");

            sb.AppendLine("性别：" + (hero.IsFemale ? "女" : "男"));
            sb.AppendLine("文化：" + (hero.Culture?.Name?.ToString() ?? "未知"));

            // 生卒年：史官立传所需；出生日期有效时输出，已故则输出卒年
            if (hero.BirthDay != CampaignTime.Zero)
                sb.AppendLine("出生：" + hero.BirthDay.GetYear + "年");
            if (hero.IsDead && hero.DeathDay != CampaignTime.Zero)
                sb.AppendLine("卒于：" + hero.DeathDay.GetYear + "年");

            var statuses = new List<string>();
            if (hero.Clan?.Kingdom?.RulingClan?.Leader == hero)
                statuses.Add("国王");
            if (hero.Clan?.Leader == hero)
                statuses.Add("家族领袖");
            else if (hero.Clan != null)
                statuses.Add("封臣");

            if (hero.Clan?.IsUnderMercenaryService == true)
                statuses.Add("雇佣兵势力");
            if (hero.IsWanderer && hero.Clan == null)
                statuses.Add("流浪者");
            if (statuses.Count == 0)
                statuses.Add("平民");
            sb.AppendLine("身份：" + string.Join("、", statuses));

            sb.AppendLine("家族：" + (hero.Clan?.Name?.ToString() ?? "无"));
            sb.AppendLine("王国：" + (hero.Clan?.Kingdom?.Name?.ToString() ?? "无"));

            var enc = hero.EncyclopediaText?.ToString();
            if (!string.IsNullOrEmpty(enc))
                sb.AppendLine("简述：" + enc);

            var clan = hero.Clan;
            if (clan != null)
            {
                sb.AppendLine("家族等级：" + clan.Tier);
                sb.AppendLine("家族声望：" + clan.Renown.ToString("F0"));
            }

            var state = "正常";
            if (hero.IsPrisoner) state = "囚禁中";
            else if (hero.IsFugitive) state = "逃亡中";
            else if (hero.IsDisabled) state = "失踪";
            sb.AppendLine("当前状态：" + state);

            var party = hero.PartyBelongedTo;
            if (party != null && party.LeaderHero == hero)
            {
                sb.AppendLine("军队：率领中");
                if (party.MemberRoster != null)
                    sb.AppendLine("兵力：约 " + party.MemberRoster.TotalManCount + " 人");
            }
            else if (party != null)
            {
                sb.AppendLine("军队：随 " + (party.LeaderHero?.Name?.ToString() ?? "他人") + " 行军");
            }
            else
            {
                sb.AppendLine("军队：无");
            }

            if (hero.CurrentSettlement != null)
                sb.AppendLine("当前位置：" + hero.CurrentSettlement.Name?.ToString());
            else if (party != null && party.CurrentSettlement != null)
                sb.AppendLine("当前位置：" + party.CurrentSettlement.Name?.ToString());
            else
                sb.AppendLine("当前位置：野外行军");

            return sb.ToString().TrimEnd();
        }

        private static string QuerySettlement(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "[错误] 请提供定居点名称";

            var s = FindSettlement(name);
            if (s == null)
                return $"[未找到] 名称为 \"{name}\" 的定居点";

            var type = s.IsTown ? "城镇" : s.IsCastle ? "城堡" : "村庄";
            var owner = s.OwnerClan?.Name?.ToString() ?? "无主";
            var kingdom = s.OwnerClan?.Kingdom?.Name?.ToString();
            var prosperity = s.IsTown ? s.Town?.Prosperity.ToString("F0") ?? "?" : "-";
            var siege = DescribeSiegeStatus(s);
            var defenders = DescribeSettlementDefenders(s);

            return $"{s.Name}（{type}）\n"
                + $"所属氏族：{owner}\n"
                + (kingdom != null ? $"所属王国：{kingdom}\n" : "")
                + $"繁荣度：{prosperity}\n"
                + $"守军兵力：{defenders}\n"
                + $"围攻状态：{(siege.Length > 0 ? siege : "无")}";
        }

        /// <summary>围攻状态描述。未被围攻时返回空字符串。</summary>
        private static string DescribeSiegeStatus(Settlement s)
        {
            try
            {
                if (!s.IsUnderSiege || s.SiegeEvent?.BesiegerCamp == null) return "";
                var attacker = s.SiegeEvent.BesiegerCamp.LeaderParty?.MapFaction?.Name?.ToString()
                    ?? s.SiegeEvent.BesiegerCamp.MapFaction?.Name?.ToString()
                    ?? "未知势力";
                return $"正被 {attacker} 围攻";
            }
            catch { return "正被围攻"; }
        }

        /// <summary>
        /// 城中守军兵力（供攻城决策）：驻军 + 民兵 + 驻扎城内的贵族部队。
        /// 设计权衡：不设情报迷雾、给准确数——agent 一次激活做出的决定没有中途取消的机会，判断必须可信。
        /// </summary>
        private static string DescribeSettlementDefenders(Settlement s)
        {
            try
            {
                int garrison = 0, militia = 0, lords = 0;
                if (s.Town?.GarrisonParty != null)
                    garrison = s.Town.GarrisonParty.MemberRoster.TotalHealthyCount;
                if (s.MilitiaPartyComponent?.MobileParty != null)
                    militia = s.MilitiaPartyComponent.MobileParty.MemberRoster.TotalHealthyCount;

                foreach (var mp in MobileParty.All)
                {
                    if (mp == s.Town?.GarrisonParty) continue;
                    if (mp == s.MilitiaPartyComponent?.MobileParty) continue;
                    if (mp.CurrentSettlement != s) continue;
                    if (!mp.IsActive || mp.LeaderHero == null) continue;
                    lords += mp.MemberRoster.TotalHealthyCount;
                }

                var total = garrison + militia + lords;
                var parts = new List<string>();
                if (garrison > 0) parts.Add($"驻军{garrison}");
                if (militia > 0) parts.Add($"民兵{militia}");
                if (lords > 0) parts.Add($"贵族部队{lords}");
                return parts.Count > 0 ? $"{total}（{string.Join("+", parts)}）" : "0（无守军）";
            }
            catch { return "未知"; }
        }

        private static string QueryWorldState(string? kingdomName)
        {
            var sb = new StringBuilder();
            var ab = Campaign.Current.GetCampaignBehavior<IAllianceCampaignBehavior>();
            var tb = Campaign.Current.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
            var expiryLines = LoadExpiryDisplayLines(); // 盟约/贸易协定到期记录（国王自查）

            IEnumerable<Kingdom> targets;
            if (!string.IsNullOrEmpty(kingdomName))
            {
                var found = DiplomacyService.FindKingdom(kingdomName!);
                if (found == null) return $"[未找到] 名为 \"{kingdomName}\" 的王国";
                targets = new[] { found };
            }
            else
            {
                targets = Kingdom.All;
            }

            foreach (var k in targets)
            {
                var kName = k.Name?.ToString() ?? "未知";
                var strength = k.CurrentTotalStrength.ToString("F0");
                var ruler = k.Leader?.Name?.ToString() ?? "无";

                sb.AppendLine($"{kName}  国王：{ruler}  总兵力：{strength}");

                foreach (var enemyK in Kingdom.All)
                {
                    if (enemyK == k) continue;
                    if (k.IsAtWarWith(enemyK))
                        sb.AppendLine($"  ⚔ 与 {enemyK.Name} 交战中");
                }

                foreach (var other in Kingdom.All)
                {
                    if (other == k) continue;
                    if (ab?.IsAllyWithKingdom(k, other) == true)
                        sb.AppendLine($"  🤝 与 {other.Name} 军事同盟");
                    if (tb != null && tb.HasTradeAgreement(k, other, out _))
                        sb.AppendLine($"  🏪 与 {other.Name} 贸易协定");
                }

                foreach (var el in expiryLines)
                {
                    if (el.Contains(kName))
                        sb.AppendLine($"  📜 {el}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>读取到期日志中的人类可读部分（盟约/贸易协定 X与Y 于…到期）。</summary>
        private static List<string> LoadExpiryDisplayLines()
        {
            var list = new List<string>();
            try
            {
                var logPath = Path.Combine(AgentManager.GetDiplomacyDir(), "expiry_log.txt");
                if (File.Exists(logPath))
                {
                    foreach (var raw in SafeFileIO.ReadAllLines(logPath))
                    {
                        var parts = raw.Split('|');
                        if (parts.Length >= 5 && parts[4].Trim().Length > 0)
                            list.Add(parts[4]);
                    }
                }
            }
            catch { }
            return list;
        }

        private static string ExecuteQueryKingdomSettlements(string kingdomName)
        {
            if (string.IsNullOrEmpty(kingdomName))
                return "[错误] 请提供王国名称";

            foreach (var kingdom in Kingdom.All)
            {
                var kName = kingdom.Name?.ToString() ?? "";
                if (!kName.Contains(kingdomName) && !kingdomName.Contains(kName)) continue;

                var sb = new StringBuilder();
                sb.AppendLine("===== " + kName + " 的领土 =====");

                var towns = new List<string>();
                var castles = new List<string>();

                foreach (var s in Settlement.All)
                {
                    if (s.OwnerClan?.Kingdom != kingdom) continue;

                    var isBorder = IsBorderSettlement(s);
                    var tag = isBorder ? " ⚔边境" : "";
                    var siegeTag = DescribeSiegeStatus(s).Length > 0 ? " ⚠被围" : "";
                    var entry = s.Name + tag + siegeTag + "（" + (s.OwnerClan?.Name?.ToString() ?? "?") + "）";
                    if (s.IsTown) towns.Add(entry);
                    else if (s.IsCastle) castles.Add(entry);
                }

                sb.AppendLine("城镇（" + towns.Count + "座）：");
                if (towns.Count == 0) sb.AppendLine("  （无）");
                else foreach (var t in towns) sb.AppendLine("  " + t);

                sb.AppendLine("城堡（" + castles.Count + "座）：");
                if (castles.Count == 0) sb.AppendLine("  （无）");
                else foreach (var c in castles) sb.AppendLine("  " + c);

                return sb.ToString().TrimEnd();
            }

            return "[未找到] 名为 \"" + kingdomName + "\" 的王国";
        }

        /// <summary>
        /// 边境判据（尺度无关，修复原绝对距离阈值过大导致几乎所有定居点被判边境）：
        /// 最近的"他国定居点"比最近的"本国定居点"更近或相当（≤1.5×）→ 本城处于国土边缘，暴露于敌；
        /// 孤立定居点（无本国邻居）也视为边境。返回边境标记 + 最近他国距离/名称。
        /// </summary>
        private static (bool isBorder, float nearestOtherDist, string nearestOtherName) ComputeBorderInfo(Settlement s)
        {
            var myKingdom = s.OwnerClan?.Kingdom;
            if (myKingdom == null || (!s.IsTown && !s.IsCastle))
                return (false, float.MaxValue, "");

            var pos = s.GatePosition.ToVec2();
            float nearestOwn = float.MaxValue;
            float nearestOther = float.MaxValue;
            Kingdom? nearestOtherKingdom = null;

            foreach (var other in Settlement.All)
            {
                if (!other.IsTown && !other.IsCastle) continue;
                if (other == s) continue;
                var otherKingdom = other.OwnerClan?.Kingdom;
                if (otherKingdom == null) continue;

                var oPos = other.GatePosition.ToVec2();
                var dx = pos.X - oPos.X;
                var dy = pos.Y - oPos.Y;
                var dist = (float)Math.Sqrt(dx * dx + dy * dy);

                if (otherKingdom == myKingdom)
                {
                    if (dist < nearestOwn) nearestOwn = dist;
                }
                else
                {
                    if (dist < nearestOther) { nearestOther = dist; nearestOtherKingdom = otherKingdom; }
                }
            }

            var isBorder = nearestOther < float.MaxValue
                && (nearestOwn == float.MaxValue || nearestOther <= nearestOwn * 1.5f);
            return (isBorder, nearestOther, nearestOtherKingdom?.Name?.ToString() ?? "");
        }

        private static bool IsBorderSettlement(Settlement s)
        {
            return ComputeBorderInfo(s).isBorder;
        }

        private static string ExecuteQuerySettlementGeography(string settlementName)
        {
            if (string.IsNullOrEmpty(settlementName))
                return "[错误] 请提供定居点名称";

            var settlement = FindSettlement(settlementName);
            if (settlement == null)
                return $"[错误] 未找到定居点：{settlementName}";
            if (!settlement.IsTown && !settlement.IsCastle)
                return $"[错误] {settlement.Name} 是村庄，地理查询仅支持城镇和城堡。";

            var pos = settlement.GatePosition.ToVec2();
            var myKingdom = settlement.OwnerClan?.Kingdom;
            var myFaction = AIChatClient.CurrentHero?.MapFaction;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var s in Settlement.All)
            {
                if (!s.IsTown && !s.IsCastle) continue;
                var p = s.GatePosition.ToVec2();
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            var midX = (minX + maxX) / 2f;
            var midY = (minY + maxY) / 2f;
            var nsDir = pos.Y > midY ? "北" : "南";
            var ewDir = pos.X > midX ? "东" : "西";

            var neighbors = new List<(float pathDist, float lineDist, string direction, Settlement s)>();
            foreach (var s in Settlement.All)
            {
                if (s == settlement) continue;
                if (!s.IsTown && !s.IsCastle) continue;
                var sPos = s.GatePosition.ToVec2();
                var dx = sPos.X - pos.X;
                var dy = sPos.Y - pos.Y;
                var lineDist = (float)Math.Sqrt(dx * dx + dy * dy);
                var pathDist = Campaign.Current.Models.MapDistanceModel.GetDistance(settlement, s, false, false, MobileParty.NavigationType.Default);
                var dir = CompassDirection(dx, dy);
                neighbors.Add((pathDist, lineDist, dir, s));
            }
            neighbors.Sort((a, b) => a.pathDist.CompareTo(b.pathDist));
            var top = neighbors.Take(8).ToList();

            // 修复：尺度无关的相对边境判据（原 `pathDist <= 5000f` 阈值大于典型城间距，导致城镇动辄被判边境）
            var borderInfo = ComputeBorderInfo(settlement);
            var isBorder = borderInfo.isBorder;
            var nearestOtherDist = borderInfo.nearestOtherDist;
            var nearestOtherKingdom = borderInfo.nearestOtherName;

            string FactionTag(Settlement s)
            {
                var sk = s.OwnerClan?.Kingdom;
                if (sk == null) return "中立";
                if (sk == myFaction) return "友方";
                if (myFaction != null && myFaction.IsAtWarWith(sk)) return "敌方";
                return "中立";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"===== {settlement.Name} 地理情报 =====");
            sb.AppendLine($"类型：{(settlement.IsTown ? "城镇" : "城堡")}");
            sb.AppendLine($"坐标位置：大陆{nsDir}{ewDir}部 · {myKingdom?.Name?.ToString() ?? "中立区"}");
            sb.AppendLine($"所属家族：{settlement.OwnerClan?.Name?.ToString() ?? "无主"}");
            sb.AppendLine($"守军兵力：{DescribeSettlementDefenders(settlement)}");
            var siegeDesc = DescribeSiegeStatus(settlement);
            if (siegeDesc.Length > 0)
                sb.AppendLine($"围攻状态：⚠ {siegeDesc}");
            if (isBorder)
            {
                var nearestKm = nearestOtherDist / 1000f;
                var nearestText = nearestKm < 1f ? $"{nearestKm:F1}" : $"{nearestKm:F0}";
                sb.AppendLine($"战略位置：边境前哨 — 距{nearestOtherKingdom}领土仅{nearestText}km（直线距离）");
            }
            else
            {
                sb.AppendLine("战略位置：核心腹地");
            }

            sb.AppendLine();
            sb.AppendLine("周边定居点（最近" + top.Count + "个，按寻路距离排序）：");
            for (int i = 0; i < top.Count; i++)
            {
                var (pathDist, lineDist, dir, s) = top[i];
                var tag = FactionTag(s);
                var kingdomTag = s.OwnerClan?.Kingdom?.Name?.ToString() ?? "中立区";
                var type = s.IsTown ? "城镇" : "城堡";
                sb.AppendLine($"  {i + 1}. {s.Name}  {type}  {kingdomTag}  [{tag}]  {dir}方向");
                sb.AppendLine($"     寻路{pathDist / 1000f:F0}km  直线{lineDist / 1000f:F1}km");
            }

            return sb.ToString().TrimEnd();
        }

        private static string CompassDirection(float dx, float dy)
        {
            if (Math.Abs(dx) < 0.1f && Math.Abs(dy) < 0.1f) return "同地";
            var angle = Math.Atan2(dy, dx) * (180.0 / Math.PI);
            if (angle < 0) angle += 360;

            if (angle < 22.5 || angle >= 337.5) return "东";
            if (angle < 67.5) return "东北";
            if (angle < 112.5) return "北";
            if (angle < 157.5) return "西北";
            if (angle < 202.5) return "西";
            if (angle < 247.5) return "西南";
            if (angle < 292.5) return "南";
            return "东南";
        }

        private static string ExecuteQueryClanMembers(string clanName)
        {
            if (string.IsNullOrEmpty(clanName))
                return "[错误] 请提供家族名称";

            foreach (var clan in Clan.All)
            {
                var cName = clan.Name?.ToString() ?? "";
                if (!cName.Contains(clanName) && !clanName.Contains(cName)) continue;

                var sb = new StringBuilder();
                sb.AppendLine("===== 家族：" + cName + " =====");
                sb.AppendLine("等级：" + clan.Tier + "  声望：" + clan.Renown.ToString("F0"));
                if (clan.Kingdom != null)
                    sb.AppendLine("所属王国：" + clan.Kingdom.Name);

                var leaders = new List<string>();
                var members = new List<string>();

                foreach (var hero in Hero.AllAliveHeroes)
                {
                    if (hero.Clan != clan) continue;

                    var age = (int)hero.Age;
                    var info = hero.Name + "（" + age + "岁，" + (hero.IsFemale ? "女" : "男") + "）";

                    if (hero == clan.Leader)
                        leaders.Add(info + " ☆族长");
                    else
                    {
                        var tags = new List<string>();
                        if (hero.Spouse != null) tags.Add("配偶：" + hero.Spouse.Name);
                        if (hero.Mother != null) tags.Add("母：" + hero.Mother.Name);
                        if (hero.Father != null) tags.Add("父：" + hero.Father.Name);
                        info += tags.Count > 0 ? "（" + string.Join("，", tags) + "）" : "";
                        members.Add(info);
                    }
                }

                foreach (var l in leaders) sb.AppendLine("  " + l);
                foreach (var m in members) sb.AppendLine("  " + m);

                if (leaders.Count + members.Count == 0)
                    sb.AppendLine("  （无在世成员）");

                return sb.ToString().TrimEnd();
            }

            return "[未找到] 名为 \"" + clanName + "\" 的家族";
        }

        private static string ExecuteQueryClanFiefs(string clanName)
        {
            if (string.IsNullOrEmpty(clanName))
                return "[错误] 请提供家族名称";

            Clan? targetClan = null;
            foreach (var clan in Clan.All)
            {
                var cName = clan.Name?.ToString() ?? "";
                if (cName.Contains(clanName) || clanName.Contains(cName))
                {
                    targetClan = clan;
                    break;
                }
            }
            if (targetClan == null)
                return "[未找到] 名为 \"" + clanName + "\" 的家族";

            var sb = new StringBuilder();
            sb.AppendLine("===== " + targetClan.Name + " 的封地 =====");
            sb.AppendLine("族长：" + (targetClan.Leader?.Name?.ToString() ?? "?"));
            if (targetClan.Kingdom != null)
                sb.AppendLine("所属王国：" + targetClan.Kingdom.Name);

            var towns = new List<string>();
            var castles = new List<string>();

            foreach (var s in Settlement.All)
            {
                if (s.OwnerClan != targetClan) continue;
                if (s.IsTown)
                    towns.Add(s.Name?.ToString() ?? "?");
                else if (s.IsCastle)
                    castles.Add(s.Name?.ToString() ?? "?");
            }

            var total = towns.Count + castles.Count;
            sb.AppendLine("总计：" + total + " 处");
            sb.AppendLine("城镇（" + towns.Count + "座）：");
            if (towns.Count == 0) sb.AppendLine("  （无）");
            else foreach (var t in towns) sb.AppendLine("  " + t);
            sb.AppendLine("城堡（" + castles.Count + "座）：");
            if (castles.Count == 0) sb.AppendLine("  （无）");
            else foreach (var c in castles) sb.AppendLine("  " + c);

            return sb.ToString().TrimEnd();
        }

        private static string ExecuteQueryKingdomClans(string kingdomName)
        {
            if (string.IsNullOrEmpty(kingdomName))
                return "[错误] 请提供王国名称";

            foreach (var kingdom in Kingdom.All)
            {
                var kName = kingdom.Name?.ToString() ?? "";
                if (!kName.Contains(kingdomName) && !kingdomName.Contains(kName)) continue;

                var sb = new StringBuilder();
                sb.AppendLine("===== " + kName + " 的家族 =====");

                var count = 0;
                foreach (var clan in Clan.All)
                {
                    if (clan.Kingdom == kingdom && !clan.IsBanditFaction)
                    {
                        count++;
                        var prefix = clan == kingdom.RulingClan ? "★" : " ";
                        sb.AppendLine($"  {prefix}{clan.Name}（等级{clan.Tier}，族长：{clan.Leader?.Name?.ToString() ?? "?"}）");
                    }
                }

                if (count == 0)
                    sb.AppendLine("  （无）");

                return sb.ToString().TrimEnd();
            }

            return "[未找到] 名为 \"" + kingdomName + "\" 的王国";
        }

        /// <summary>查询本族当前影响力（政治资财：拉军团/推行政策的消耗资源）。</summary>
        private static string ExecuteQueryInfluence()
        {
            if (Campaign.Current == null)
                return "[错误] 战役未加载";
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";

            var hero = AIChatClient.CurrentHero;
            var clan = hero.Clan;
            if (clan == null)
                return "[错误] 你没有家族";

            var influence = clan.Influence;
            var kingdom = hero.MapFaction as Kingdom;
            var kingdomName = kingdom?.Name?.ToString() ?? "（无王国）";
            var canArmy = influence > 100f && kingdom != null
                && !clan.IsUnderMercenaryService
                && kingdom.FactionsAtWarWith.Any(f => f.Fiefs.Any());

            var sb = new StringBuilder();
            sb.AppendLine($"===== {hero.Name} 的影响力报告 =====");
            sb.AppendLine($"当前影响力：{influence:F0}（家族：{clan.Name}，王国：{kingdomName}）");
            sb.AppendLine();
            sb.AppendLine("影响力是你的政治资财，主要用途：");
            sb.AppendLine("- 拉军团：影响力超过 100 时，可召集本国领主组成军团协同作战（当前" + (canArmy ? "已可召集" : "尚不足或条件未满足") + "）");
            sb.AppendLine("- 推行政策：在王国朝堂提议立法");
            sb.AppendLine("影响力随战功、封地与政务积累；拉军团与推行政策会消耗");
            return sb.ToString().TrimEnd();
        }

        private static string ExecuteQueryWarStatus(string? kingdomName)
        {
            if (Campaign.Current == null)
                return "[错误] 战役未加载";

            Kingdom? kingdom;
            if (string.IsNullOrEmpty(kingdomName))
            {
                if (AIChatClient.CurrentHero == null)
                    return "[错误] 无当前领主，且未指定王国名称";
                kingdom = null;
                foreach (var k in Kingdom.All)
                {
                    if (k.RulingClan?.Leader == AIChatClient.CurrentHero
                        || AIChatClient.CurrentHero.Clan?.Kingdom == k)
                    {
                        kingdom = k;
                        break;
                    }
                }
                if (kingdom == null)
                    return "[错误] 当前领主不属于任何王国";
            }
            else
            {
                kingdom = DiplomacyService.FindKingdom(kingdomName!);
                if (kingdom == null)
                    return $"[未找到] 名为 \"{kingdomName}\" 的王国";
            }

            var wars = new List<Kingdom>();
            foreach (var other in Kingdom.All)
            {
                if (other == kingdom || other.IsEliminated) continue;
                if (kingdom.IsAtWarWith(other))
                    wars.Add(other);
            }

            if (wars.Count == 0)
            {
                var name = kingdom.Name?.ToString() ?? "?";
                return $"{name} 当前处于和平状态。";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"===== {kingdom.Name} 战争状态 =====");

            foreach (var enemy in wars)
            {
                var stance = kingdom.GetStanceWith(enemy);
                var warDays = (int)stance.WarStartDate.ElapsedDaysUntilNow;

                var ourCasualties = stance.GetCasualties(kingdom);
                var enemyCasualties = stance.GetCasualties(enemy);
                var ourSieges = stance.GetSuccessfulSieges(kingdom);
                var enemySieges = stance.GetSuccessfulSieges(enemy);
                var ourTowns = stance.GetSuccessfulTownSieges(kingdom);
                var enemyTowns = stance.GetSuccessfulTownSieges(enemy);
                var ourRaids = stance.GetSuccessfulRaids(kingdom);
                var enemyRaids = stance.GetSuccessfulRaids(enemy);

                var ourCastles = ourSieges - ourTowns;
                var enemyCastles = enemySieges - enemyTowns;

                sb.AppendLine();
                sb.AppendLine($"【对 {enemy.Name}】已持续 {warDays} 天");

                if (ourCasualties > 0 || enemyCasualties > 0)
                    sb.AppendLine($"  我方阵亡 {ourCasualties} | 敌方阵亡 {enemyCasualties}");
                if (ourTowns > 0 || enemyTowns > 0)
                    sb.AppendLine($"  攻下城镇 {ourTowns} 座 | 丢失城镇 {enemyTowns} 座");
                if (ourCastles > 0 || enemyCastles > 0)
                    sb.AppendLine($"  攻下城堡 {ourCastles} 座 | 丢失城堡 {enemyCastles} 座");
                if (ourRaids > 0 || enemyRaids > 0)
                    sb.AppendLine($"  劫掠村庄 {ourRaids} 次 | 被劫掠 {enemyRaids} 次");
            }

            return sb.ToString().TrimEnd();
        }

        private static string ExecuteQueryRecentEvents(string? targetEntityId, int maxEvents, int maxDaysAgo)
        {
            if (Campaign.Current == null)
                return "[错误] 战役未加载";

            var hero = ResolveTargetHero(targetEntityId);
            if (hero == null)
                return $"[错误] 未找到目标实体：{targetEntityId}";

            if (maxEvents <= 0 || maxEvents > 30)
                maxEvents = 10;
            if (maxDaysAgo <= 0 || maxDaysAgo > 84)
                maxDaysAgo = 14;

            var now = CampaignTime.Now;
            var cutoffHours = now.ToHours - (maxDaysAgo * 24);

            var logs = Campaign.Current.LogEntryHistory.GameActionLogs;
            var results = new List<string>();

            for (int i = logs.Count - 1; i >= 0 && results.Count < maxEvents; i--)
            {
                var log = logs[i];
                if (log.GameTime.ToHours < cutoffHours) continue;

                string? text = null;

                if (log is TournamentWonLogEntry twl && twl.Winner == hero)
                    text = twl.GetEncyclopediaText().ToString();
                else if (log is TakePrisonerLogEntry tpl)
                {
                    if (tpl.Prisoner == hero || tpl.CapturerHero == hero)
                        text = tpl.GetNotificationText().ToString();
                }
                else if (log is EndCaptivityLogEntry ecl && ecl.Prisoner == hero)
                    text = ecl.GetEncyclopediaText().ToString();
                else if (log is CharacterKilledLogEntry ckl && (ckl.Victim == hero || ckl.Killer == hero))
                    text = ckl.GetEncyclopediaText().ToString();
                else if (log is CharacterMarriedLogEntry cml && (cml.MarriedHero == hero || cml.MarriedTo == hero))
                    text = cml.GetEncyclopediaText().ToString();
                else if (log is CharacterBornLogEntry cbl
                    && (cbl.BornCharacter == hero
                        || cbl.BornCharacter.Mother == hero
                        || cbl.BornCharacter.Father == hero))
                    text = cbl.GetEncyclopediaText().ToString();
                else if (log is CharacterBecameFugitiveLogEntry cbf && cbf.Hero == hero)
                    text = cbf.GetEncyclopediaText().ToString();
                else if (log is KingdomDestroyedLogEntry kdl
                    && hero.MapFaction is Kingdom k
                    && kdl.IsVisibleInEncyclopediaPageOf(k))
                    text = kdl.GetEncyclopediaText().ToString();
                else if (log is ClanLeaderChangedLogEntry clc && (clc.OldLeader == hero || clc.NewLeader == hero))
                    text = clc.GetEncyclopediaText().ToString();

                if (text == null) continue;

                var hoursAgo = (now.ToHours - log.GameTime.ToHours);
                var daysAgo = (int)(hoursAgo / 24);
                var timeLabel = hoursAgo < 24 ? "【今日】" : $"【{daysAgo}天前】";
                results.Add($"{timeLabel} {text}");
            }

            if (results.Count == 0)
                return $"{hero.Name} 在过去{maxDaysAgo}天内没有值得注意的事件。";

            var sb = new StringBuilder();
            sb.AppendLine($"===== {hero.Name} 的近期事件 =====");
            foreach (var r in results)
                sb.AppendLine(r);

            return sb.ToString().TrimEnd();
        }

        private static string ExecuteQuerySurroundings(float radiusFraction, int maxSettlements, int maxParties)
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";

            // 扫描半径 = 地图实际尺度 × 比例（同 query_party_troops 情报分级，不依赖绝对 km）。
            // 背景：原实现用绝对 km（默认 20km = 20000 地图单位），而全图仅约 2500 单位——半径几乎覆盖全图，"扫描"形同虚设。
            var configFraction = MySettings.Instance?.SurroundingsScanRadiusFraction ?? 0.2f;
            if (radiusFraction <= 0 || radiusFraction > configFraction)
                radiusFraction = configFraction;
            if (maxSettlements <= 0 || maxSettlements > 15)
                maxSettlements = 5;
            if (maxParties <= 0 || maxParties > 20)
                maxParties = 8;

            var hero = AIChatClient.CurrentHero;
            var party = hero.PartyBelongedTo;
            Vec2 myPos;
            var locationDesc = "";

            if (hero.CurrentSettlement != null)
            {
                myPos = hero.CurrentSettlement.GatePosition.ToVec2();
                var s = hero.CurrentSettlement;
                var ownerKingdom = s.OwnerClan?.Kingdom?.Name?.ToString();
                var type = s.IsTown ? "城镇" : s.IsCastle ? "城堡" : "村庄";
                locationDesc = $"{s.Name}（{type}" + (ownerKingdom != null ? $" · {ownerKingdom}" : "") + "）";
            }
            else if (party != null)
            {
                myPos = party.GetPosition2D;
                locationDesc = $"野外行军（{party.Name}）";
            }
            else
            {
                return "[错误] 无法确定当前领主的位置";
            }

            float Distance(Vec2 pos)
            {
                var dx = myPos.X - pos.X;
                var dy = myPos.Y - pos.Y;
                return (float)Math.Sqrt(dx * dx + dy * dy);
            }

            var radiusMeters = Math.Max(100f, ComputeMapExtent() * radiusFraction);
            var radiusKm = radiusMeters / 1000f;
            var myFaction = hero.MapFaction;

            string FactionRelation(MobileParty p)
            {
                if (p.LeaderHero == hero || p == party)
                    return "自己";
                if (p.IsBandit)
                    return "敌对";
                if (p.MapFaction == myFaction)
                    return "友方";
                if (myFaction != null && p.MapFaction != null && myFaction.IsAtWarWith(p.MapFaction))
                    return "敌对";
                return "中立";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"===== 当前位置 =====");
            sb.AppendLine(locationDesc);

            var nearbySettlements = new List<(float dist, string desc)>();
            foreach (var s in Settlement.All)
            {
                if (!s.IsTown && !s.IsCastle) continue;
                if (s == hero.CurrentSettlement) continue;

                var dist = Distance(s.GatePosition.ToVec2());
                if (dist > radiusMeters) continue;

                var km = dist / 1000f;
                var owner = s.OwnerClan?.Name?.ToString() ?? "无主";
                var kingdom = s.OwnerClan?.Kingdom?.Name?.ToString();
                var type = s.IsTown ? "城镇" : "城堡";
                nearbySettlements.Add((dist, $"{s.Name} {type} {owner}" + (kingdom != null ? $"（{kingdom}）" : "") + $" {km:F0}km"));
            }
            nearbySettlements.Sort((a, b) => a.dist.CompareTo(b.dist));

            var nearbyParties = new List<(float dist, string desc)>();
            foreach (var mp in MobileParty.All)
            {
                if (!mp.IsActive) continue;
                if (mp == party) continue;
                if (mp.CurrentSettlement != null) continue;

                var dist = Distance(mp.GetPosition2D);
                if (dist > radiusMeters) continue;

                var km = dist / 1000f;
                var leader = mp.LeaderHero?.Name?.ToString() ?? mp.Name?.ToString() ?? "不明";
                var troops = mp.MemberRoster?.TotalManCount ?? 0;
                var faction = mp.IsBandit ? "强盗" : (mp.MapFaction?.Name?.ToString() ?? "?");
                var relation = FactionRelation(mp);
                var relTag = relation switch
                {
                    "敌对" => "[敌对]",
                    "友方" => "[友方]",
                    "中立" => "[中立]",
                    _ => "[自己]"
                };
                nearbyParties.Add((dist, $"{leader} {troops}人 {faction} {relTag} {km:F0}km"));
            }
            nearbyParties.Sort((a, b) => a.dist.CompareTo(b.dist));

            sb.AppendLine();
            sb.AppendLine($"===== 附近定居点（半径{radiusKm:F1}km） =====");
            if (nearbySettlements.Count == 0)
                sb.AppendLine("（无）");
            else
                foreach (var (_, desc) in nearbySettlements.Take(maxSettlements))
                    sb.AppendLine(desc);

            sb.AppendLine();
            sb.AppendLine($"===== 附近部队（半径{radiusKm:F1}km） =====");
            if (nearbyParties.Count == 0)
                sb.AppendLine("（无）");
            else
                foreach (var (_, desc) in nearbyParties.Take(maxParties))
                    sb.AppendLine(desc);

            return sb.ToString().TrimEnd();
        }

        private static string ExecuteQueryPendingProposals()
        {
            var actingHero = DiplomacyService.GetDiplomacyHero();
            if (actingHero == null) return "[错误] 只有王国统治者才能查看外交提案";
            var myEntity = EntityManager.GetOrCreateEntity(actingHero);
            if (myEntity == null) return "[错误] 无法解析当前实体";

            var pending = AgentManager.ListPendingProposals(myEntity.Id);
            if (pending.Count == 0)
                return "你当前没有待处理的外交提案。";

            var sb = new StringBuilder();
            sb.AppendLine($"你当前有 {pending.Count} 份待处理的外交提案：");
            sb.AppendLine();

            for (int i = 0; i < pending.Count; i++)
            {
                var p = pending[i];
                var pContent = AgentManager.ReadDiplomacyProposal(p);
                if (pContent == null) continue;

                var (proposerId, _, type) = AgentManager.ParseProposalMeta(pContent);
                var typeName = type switch
                {
                    "peace" => "议和",
                    "alliance" => "结盟",
                    "trade" => "贸易协定",
                    _ => type
                };
                var proposerName = "?";
                var pe = EntityManager.GetEntityById(proposerId);
                if (pe != null) proposerName = pe.Name;

                var msgLine = pContent.Split('\n')
                    .FirstOrDefault(l => l.StartsWith("message="))?.Substring(8);

                var tributeLine = pContent.Split('\n')
                    .FirstOrDefault(l => l.StartsWith("tribute="));

                sb.AppendLine($"{i + 1}. [{typeName}] 来自 {proposerName}（{proposerId}）");
                sb.AppendLine($"   ID: {p}");

                if (tributeLine != null && type == "peace")
                {
                    var tributeData = tributeLine.Substring(8); // "500_30"
                    var parts = tributeData.Split('_');
                    if (parts.Length == 2
                        && int.TryParse(parts[0], out var tAmount)
                        && int.TryParse(parts[1], out var tDays))
                    {
                        var tributeDesc = tAmount > 0
                            ? $"对方愿每日付 {tAmount} 金币 × {tDays} 天"
                            : tAmount < 0
                                ? $"对方要求每日付 {Math.Abs(tAmount)} 金币 × {tDays} 天"
                                : "无赔款";
                        sb.AppendLine($"   赔款：{tributeDesc}");
                    }
                }

                if (!string.IsNullOrEmpty(msgLine))
                    sb.AppendLine($"   附言：\"{msgLine}\"");
                sb.AppendLine();
            }

            sb.AppendLine("对每个提案，用 respond_to_diplomacy_proposal(proposal_id, accepted) 逐一处理。");
            return sb.ToString().TrimEnd();
        }

        private static string ExecuteQueryHeroSkills(string? targetEntityId)
        {
            var hero = ResolveTargetHero(targetEntityId);
            if (hero == null) return $"[错误] 未找到目标实体：{targetEntityId}";

            var sb = new StringBuilder();
            sb.AppendLine($"===== {hero.Name} 的技能与属性 =====");
            sb.AppendLine();

            var skills = new (string Name, SkillObject Skill)[]
            {
                ("单手", DefaultSkills.OneHanded),
                ("双手", DefaultSkills.TwoHanded),
                ("长杆", DefaultSkills.Polearm),
                ("弓", DefaultSkills.Bow),
                ("弩", DefaultSkills.Crossbow),
                ("投掷", DefaultSkills.Throwing),
                ("骑术", DefaultSkills.Riding),
                ("跑动", DefaultSkills.Athletics),
                ("锻造", DefaultSkills.Crafting),
                ("侦查", DefaultSkills.Scouting),
                ("战术", DefaultSkills.Tactics),
                ("流氓", DefaultSkills.Roguery),
                ("魅力", DefaultSkills.Charm),
                ("统御", DefaultSkills.Leadership),
                ("交易", DefaultSkills.Trade),
                ("管理", DefaultSkills.Steward),
                ("医术", DefaultSkills.Medicine),
                ("工程", DefaultSkills.Engineering),
            };

            sb.AppendLine("技能：");
            foreach (var (name, skill) in skills)
                sb.AppendLine($"  {name}: {hero.GetSkillValue(skill)}");

            sb.AppendLine();
            sb.AppendLine("属性：");
            sb.AppendLine($"  活力: {hero.CharacterAttributes.GetPropertyValue(DefaultCharacterAttributes.Vigor)}");
            sb.AppendLine($"  控制: {hero.CharacterAttributes.GetPropertyValue(DefaultCharacterAttributes.Control)}");
            sb.AppendLine($"  耐力: {hero.CharacterAttributes.GetPropertyValue(DefaultCharacterAttributes.Endurance)}");
            sb.AppendLine($"  狡诈: {hero.CharacterAttributes.GetPropertyValue(DefaultCharacterAttributes.Cunning)}");
            sb.AppendLine($"  社交: {hero.CharacterAttributes.GetPropertyValue(DefaultCharacterAttributes.Social)}");
            sb.AppendLine($"  智力: {hero.CharacterAttributes.GetPropertyValue(DefaultCharacterAttributes.Intelligence)}");

            return sb.ToString().TrimEnd();
        }

    }
}
