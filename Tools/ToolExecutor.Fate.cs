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
        private static readonly object _fateClanBudgetLock = new();

        public static void ResetFateClanBudget()
        {
            lock (_fateClanBudgetLock) _fateClanBudget = 0;
        }

        /// <summary>预算只在成功建族后才占用——若创建失败（如生成成员失败）不占用预算，
        /// 让 LLM 能在本次唤醒内重试，而不是一次失败就白耗整次激活。
        /// 返回 true 表示本次占用成功（此前尚未建族）。</summary>
        private static bool TryMarkClanCreated()
        {
            lock (_fateClanBudgetLock)
            {
                if (_fateClanBudget >= 1) return false;
                _fateClanBudget = 1;
                return true;
            }
        }

        private static bool HasCreatedClanThisActivation => _fateClanBudget >= 1;

        private static string ExecuteCreateClan(string clanName, string kingdomName, string culture, string motivation, bool isMercenary)
        {
            if (AgentManager.ActiveAgentId != "__fate__")
                return "[拒绝] 只有天意（家族补充系统）能创建家族。";
            if (HasCreatedClanThisActivation)
                return "[提示] 你本次唤醒已经降下过一支贵族血脉。世家兴衰非一日之功——请静待日后再次被唤起，届时再观天下之势补充。不要连续创建。";
            if (string.IsNullOrEmpty(clanName))
                return "[错误] 请提供家族名称。";
            if (Campaign.Current == null)
                return "[错误] 战役未加载。";

            var kingdom = DiplomacyService.FindKingdom(kingdomName);
            if (kingdom == null)
                return $"[错误] 未找到王国：{kingdomName}。";

            CultureObject cultureObj = null;
            if (!string.IsNullOrEmpty(culture))
            {
                try
                {
                    cultureObj = Game.Current.ObjectManager.GetObjectTypeList<CultureObject>()
                        .FirstOrDefault(c => (c.Name?.ToString() ?? "").Contains(culture) || culture.Contains(c.Name?.ToString() ?? ""));
                }
                catch { }
            }
            cultureObj ??= kingdom.Culture;
            if (cultureObj == null)
                return "[错误] 无法确定家族文化。";
            // 反同质化：不固定取王国的第一座城，而是从该王国的城镇（次选同文化城镇、再次任意城镇）中随机选一座作家族根基，
            // 避免所有新家族都出自同一座城。
            var homeTowns = kingdom.Settlements.Where(s => s.IsTown).ToList();
            if (homeTowns.Count == 0)
                homeTowns = Settlement.All.Where(s => s.IsTown && s.Culture == cultureObj).ToList();
            if (homeTowns.Count == 0)
                homeTowns = Settlement.All.Where(s => s.IsTown).ToList();
            if (homeTowns.Count == 0)
                return "[错误] 找不到合适的定居点作为家族根基。";
            var homeSettlement = homeTowns[_rng.Next(homeTowns.Count)];

            try
            {
                // 1. 建族
                var clan = Clan.CreateClan("fate_clan_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                var nameObj = new TextObject(clanName);
                clan.ChangeClanName(nameObj, nameObj);
                clan.Culture = cultureObj;
                clan.IsNoble = true;
                // 家族等级 2（恰好够当封臣又看得出是新族）：Tier 私有 setter，改从声望抬升
                clan.AddRenown(Campaign.Current.Models.ClanTierModel.GetRequiredRenownForTier(2), false);
                try
                {
                    // 旗帜随机生成（贵族家族中央单徽章样式），而非复制别的家族的——每个新世家都有自己的纹章
                    clan.Banner = Banner.CreateRandomClanBanner();
                }
                catch { }
                clan.SetInitialHomeSettlement(homeSettlement);

                // 2. 生成成员（3-6 人）：年龄不再统一"年轻化"，而是在保证家族能延续的前提下拉开梯度，
                //    避免所有新家族都是清一色年轻人组成的同质面孔。
                //    族长 25-45；其余成员 60% 青年(16-28)、30% 壮年(28-42)、10% 长者(42-58)，
                //    偶有长者反而更有"世家故族"的沧桑感，也让家族年龄结构不千篇一律。
                //    对齐游戏叛乱建族的模式：先建英雄（clan=null，暂不挂族），
                //    全部创建成功后再统一 hero.Clan = clan 注册进族 + ChangeState(Active) 激活。
                //    原实现直接把 clan 传进 CreateSpecialHero 且不激活，英雄与族关联可能不完整，
                //    游戏后续迭代该族/英雄时可能原生崩溃。
                var memberCount = 3 + _rng.Next(4);
                var heroes = new List<Hero>();
                for (var i = 0; i < memberCount; i++)
                {
                    try
                    {
                        var age = i == 0
                            ? _rng.Next(25, 46)
                            : _rng.Next(100) < 10
                                ? _rng.Next(42, 59)
                                : _rng.Next(100) < 30
                                    ? _rng.Next(28, 43)
                                    : _rng.Next(16, 29);
                        // 修复（建族失败根因）：Lord 模板不在 NotableTemplates 里，
                        // GetRandomTemplateByOccupation(Occupation.Lord, ...) 恒返回 null → 无法生成成员。
                        // 改用叛乱建族同款来源（CultureObject.RebelliousHeroTemplates，原版验证过与 CreateSpecialHero 配合），
                        // 兜底再从对象管理器里筛 Lord 职业模板（lords.xml 等）。
                        var template = GetLordTemplateForCulture(cultureObj);
                        if (template == null) continue;
                        var h = HeroCreator.CreateSpecialHero(template, homeSettlement, null, null, age);
                        if (h != null) heroes.Add(h);
                    }
                    catch { }
                }
                if (heroes.Count == 0)
                    return "[错误] 未能生成家族成员。";
                foreach (var h in heroes)
                {
                    h.Clan = clan;
                    h.ChangeState(Hero.CharacterStates.Active);
                }
                clan.SetLeader(heroes[0]);

                // 3. 投效（封臣或雇佣兵）
                if (isMercenary)
                    ChangeKingdomAction.ApplyByJoinFactionAsMercenary(clan, kingdom, CampaignTime.Zero, 50, true);
                else
                    ChangeKingdomAction.ApplyByJoinToKingdom(clan, kingdom, CampaignTime.Zero, true);

                // 4. 族长带兵
                try
                {
                    var leader = clan.Leader;
                    if (leader != null)
                        LordPartyComponent.CreateLordParty(leader.StringId, leader, homeSettlement.GatePosition, 3f, homeSettlement, leader);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"新家族 {clanName} 族长建队失败：{ex.Message}");
                }

                // 5. 通知游戏 + 入史
                CampaignEventDispatcher.Instance.OnClanCreated(clan, true);
                var joinText = isMercenary ? $"以雇佣兵身份效力于{kingdom.Name}" : $"投效{kingdom.Name}";
                HistoryRecorder.RecordClanCreated($"{clanName}建立（{cultureObj.Name}，家族等级2，{heroes.Count}名成员），{joinText}，族长为{heroes[0].Name}");
                if (!string.IsNullOrEmpty(motivation))
                    HistoryRecorder.RecordClanCreated($"新家族{clanName}之立族宣言：{motivation}");

                // 预算在成功建族后才占用（失败不占用，允许本次唤醒内重试）
                TryMarkClanCreated();

                return $"已降下新的贵族血脉：{clanName}（{cultureObj.Name}文化，家族等级2，{heroes.Count}名成员）——{joinText}，族长{heroes[0].Name}。{(!string.IsNullOrEmpty(motivation) ? " 立族宣言：" + motivation : "")}";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"create_clan 异常：{ex.Message}");
                return $"[错误] 创建家族失败：{ex.Message}";
            }
        }

        /// <summary>取该文化的贵族模板（Lord 职业），用于 CreateSpecialHero 生成家族成员。
        /// 主源：CultureObject.RebelliousHeroTemplates（原版叛乱建族同款，均为 Lord 职业英雄模板，
        /// 已由游戏验证可与 CreateSpecialHero 配合）——注意不能用 GetRandomTemplateByOccupation(Lord)，
        /// 该 API 只查 NotableTemplates，而 Lord 模板（lords.xml/spspecialcharacters.xml）不在其中，恒返回 null。
        /// 兜底：从对象管理器筛选 Lord 职业且文化匹配的模板。</summary>
        private static CharacterObject? GetLordTemplateForCulture(CultureObject culture)
        {
            try
            {
                var rebellious = culture.RebelliousHeroTemplates;
                if (rebellious != null && rebellious.Count > 0)
                    return rebellious[_rng.Next(rebellious.Count)];
            }
            catch { }
            try
            {
                var candidates = Game.Current.ObjectManager.GetObjectTypeList<CharacterObject>()
                    .Where(c => c != null && c.Occupation == Occupation.Lord && c.Culture == culture)
                    .ToList();
                if (candidates.Count > 0)
                    return candidates[_rng.Next(candidates.Count)];
            }
            catch { }
            return null;
        }
    }
}
