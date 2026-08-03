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

        private static bool TryConsumeFateClanBudget()
        {
            lock (_fateClanBudgetLock)
            {
                if (_fateClanBudget >= 1) return false;
                _fateClanBudget++;
                return true;
            }
        }

        private static string ExecuteCreateClan(string clanName, string kingdomName, string culture, string motivation, bool isMercenary)
        {
            if (AgentManager.ActiveAgentId != "__fate__")
                return "[拒绝] 只有天意（家族补充系统）能创建家族。";
            if (!TryConsumeFateClanBudget())
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

            var homeSettlement = kingdom.Settlements.FirstOrDefault(s => s.IsTown)
                ?? Settlement.All.FirstOrDefault(s => s.IsTown && s.Culture == cultureObj)
                ?? Settlement.All.FirstOrDefault(s => s.IsTown);
            if (homeSettlement == null)
                return "[错误] 找不到合适的定居点作为家族根基。";

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
                    var donor = Clan.All.FirstOrDefault(c => c.Banner != null);
                    if (donor?.Banner != null)
                        clan.Banner = new Banner(donor.Banner);
                }
                catch { }
                clan.SetInitialHomeSettlement(homeSettlement);

                // 2. 生成成员（3-6，偏向年轻：族长 25-40，其余 14-35）
                // 对齐游戏叛乱建族的模式：先建英雄（clan=null，暂不挂族），
                // 全部创建成功后再统一 hero.Clan = clan 注册进族 + ChangeState(Active) 激活。
                // 原实现直接把 clan 传进 CreateSpecialHero 且不激活，英雄与族关联可能不完整，
                // 游戏后续迭代该族/英雄时可能原生崩溃。
                var memberCount = 3 + _rng.Next(4);
                var heroes = new List<Hero>();
                for (var i = 0; i < memberCount; i++)
                {
                    try
                    {
                        var age = i == 0 ? _rng.Next(25, 41) : _rng.Next(14, 36);
                        var template = Campaign.Current.Models.HeroCreationModel.GetRandomTemplateByOccupation(Occupation.Lord, homeSettlement);
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

                return $"已降下新的贵族血脉：{clanName}（{cultureObj.Name}文化，家族等级2，{heroes.Count}名成员）——{joinText}，族长{heroes[0].Name}。{(!string.IsNullOrEmpty(motivation) ? " 立族宣言：" + motivation : "")}";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"create_clan 异常：{ex.Message}");
                return $"[错误] 创建家族失败：{ex.Message}";
            }
        }
    }
}
