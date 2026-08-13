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
        private static string ExecuteChangeKingdom(string action, string kingdomName, bool rebellion, string? heirEntityId)
        {
            var hero = AIChatClient.CurrentHero;
            if (hero == null) return "[错误] 无当前领主";
            if (hero.Clan == null) return "[错误] 你不属于任何氏族";
            if (hero.Clan.Leader != hero) return "[错误] 只有氏族领袖才能改变阵营";
            var clan = hero.Clan;

            switch (action)
            {
                case "abdicate":
                {
                    var k = clan.Kingdom;
                    if (k == null) return "[错误] 你当前不属于任何王国，无法禅位";
                    if (k.RulingClan != clan) return "[错误] 只有国王才能禅位";
                    if (string.IsNullOrEmpty(heirEntityId))
                        return "[错误] 必须指定继承人（heir_entity_id），继承人需与你同属一个氏族";

                    var resolvedId = EntityManager.ResolveEntityId(heirEntityId) ?? heirEntityId;
                    var heirEntity = EntityManager.GetOrCreateEntityById(resolvedId);
                    if (heirEntity?.HeroRef == null)
                        return $"[错误] 未找到继承人：{heirEntityId}";
                    var heir = heirEntity.HeroRef;
                    if (heir.Clan != clan && heir.Clan?.Kingdom != k)
                        return $"[错误] 继承人需要与你同氏族，或与你同王国的其他氏族领袖";
                    if (heir == hero)
                        return "[错误] 不能禅位给自己";

                    if (heir.Clan == clan)
                    {
                        ChangeClanLeaderAction.ApplyWithSelectedNewLeader(clan, heir);
                        return $"你已将王位禅让给同氏族的{heir.Name}。{heir.Name}现在是{k.Name}的新统治者。";
                    }
                    else
                    {
                        ChangeRulingClanAction.Apply(k, heir.Clan);
                        return $"你已将王位禅让给{heir.Clan.Name}的{heir.Name}。统治氏族已变更，{heir.Name}现在是{k.Name}的新统治者。";
                    }
                }

                case "leave_kingdom":
                {
                    if (clan.Kingdom == null) return "[错误] 你当前不属于任何王国";
                    var oldName = clan.Kingdom.Name?.ToString() ?? "?";
                    if (rebellion)
                    {
                        ChangeKingdomAction.ApplyByLeaveWithRebellionAgainstKingdom(clan);
                        return $"已以叛乱方式脱离{oldName}。你对旧王国宣战，保留了封地。";
                    }
                    else
                    {
                        ChangeKingdomAction.ApplyByLeaveKingdom(clan);
                        return $"已和平脱离{oldName}。你失去了在旧王国的封地。";
                    }
                }

                case "join_kingdom":
                case "defect_to_kingdom":
                case "join_as_mercenary":
                {
                    var target = DiplomacyService.FindKingdom(kingdomName);
                    if (target == null) return $"[错误] 未找到王国：{kingdomName}";

                    if (action == "join_kingdom")
                    {
                        if (clan.Kingdom == target) return "[错误] 你已经属于该王国";
                        if (clan.Kingdom != null) return "[错误] 你必须先脱离当前王国才能加入新王国。先用 change_kingdom(action=\"leave_kingdom\")";
                        var vassalTier = Campaign.Current.Models.ClanTierModel.VassalEligibleTier;
                        if (clan.Tier < vassalTier)
                            return $"[错误] 家族等级不足：成为封臣需要家族等级达到 {vassalTier} 级（当前 {clan.Tier} 级）。低等级家族只能作为雇佣兵加入，请用 change_kingdom(action=\"join_as_mercenary\")";
                        ChangeKingdomAction.ApplyByJoinToKingdom(clan, target);
                        return $"已加入{target.Name}，成为其封臣。";
                    }

                    if (action == "defect_to_kingdom")
                    {
                        if (clan.Kingdom == null) return "[错误] 你当前不属于任何王国，请用 join_kingdom";
                        if (clan.Kingdom == target) return "[错误] 你已经属于该王国";
                        var vassalTier = Campaign.Current.Models.ClanTierModel.VassalEligibleTier;
                        if (clan.Tier < vassalTier)
                            return $"[错误] 家族等级不足：叛逃成为对方封臣需要家族等级达到 {vassalTier} 级（当前 {clan.Tier} 级）。";
                        ChangeKingdomAction.ApplyByJoinToKingdomByDefection(clan, clan.Kingdom, target);
                        return $"已叛逃至{target.Name}。";
                    }

                    if (action == "join_as_mercenary")
                    {
                        if (clan.Kingdom == target) return "[错误] 你已经属于该王国";
                        if (clan.Kingdom != null) return "[错误] 你必须先脱离当前王国才能成为雇佣兵。先用 change_kingdom(action=\"leave_kingdom\")";
                        var mercTier = Campaign.Current.Models.ClanTierModel.MercenaryEligibleTier;
                        if (clan.Tier < mercTier)
                            return $"[错误] 家族等级不足：成为雇佣兵需要家族等级达到 {mercTier} 级（当前 {clan.Tier} 级）。";
                        ChangeKingdomAction.ApplyByJoinFactionAsMercenary(clan, target);
                        return $"已成为{target.Name}的雇佣兵。";
                    }

                    return "[错误] 未知操作";
                }

                default:
                    return $"[错误] 未知 action：{action}。可用：abdicate, leave_kingdom, join_kingdom, defect_to_kingdom, join_as_mercenary";
            }
        }

        /// <summary>
        /// 封臣/独立氏族领袖自立建国。门槛对齐原版 KingdomCreationModel（家族等级4 + 至少1座城镇/城堡 + 至少100兵）。
        /// 封臣建国先叛乱脱离旧国（保留封地、对旧国及其交战方宣战）再建国——仿原版玩家建国同款公开 API
        /// （GovernorCampaignBehavior 逐字调用 KingdomManager.CreateKingdom，接受任意氏族为 founder）。
        /// </summary>
        private static string ExecuteCreateKingdom(string kingdomName, string culture, string motto)
        {
            if (Campaign.Current == null) return "[错误] 战役未加载。";
            var hero = AIChatClient.CurrentHero;
            if (hero == null) return "[错误] 无当前领主";
            // 排除玩家：建国是重大国体变更，玩家走原版正规流程（与总督对话建国），此工具仅限 NPC 封臣/氏族领袖使用。
            if (hero == Hero.MainHero) return "[错误] 建国请走游戏内正规流程——与任意总督对话，选择建国。此工具仅限 NPC 封臣/氏族领袖使用。";
            if (hero.Clan == null) return "[错误] 你不属于任何氏族";
            if (hero.Clan.Leader != hero) return "[错误] 只有氏族领袖才能建国";
            var clan = hero.Clan;

            // 排除：已是统治者（国王）——无需另立新国
            if (clan.Kingdom?.RulingClan == clan) return "[错误] 你已是一国之君，无需另立新国";
            // 排除：雇佣兵——非正式封臣，不能持有封地、不能建国
            if (clan.IsUnderMercenaryService) return "[错误] 雇佣兵不能建国——先结束雇佣兵契约，成为独立氏族或正式封臣";

            // 对齐原版 KingdomCreationModel 门槛（DefaultKingdomCreationModel：tier4 / 1封地 / 100兵）
            var model = Campaign.Current.Models.KingdomCreationModel;
            if (clan.Tier < model.MinimumClanTierToCreateKingdom)
                return $"[错误] 家族等级不足：建国需要家族等级达到 {model.MinimumClanTierToCreateKingdom} 级（当前 {clan.Tier} 级）。";
            var ownedFiefs = clan.Settlements.Count(s => s.IsTown || s.IsCastle);
            if (ownedFiefs < model.MinimumNumberOfSettlementsOwnedToCreateKingdom)
                return $"[错误] 领地不足：建国至少需要 {model.MinimumNumberOfSettlementsOwnedToCreateKingdom} 座城镇或城堡（当前 {ownedFiefs} 座）。";
            var troopCount = clan.Fiefs.Sum(t => t.GarrisonParty?.MemberRoster?.TotalHealthyCount ?? 0)
                             + clan.WarPartyComponents.Sum(w => w.MobileParty.MemberRoster.TotalHealthyCount);
            if (troopCount < model.MinimumTroopCountToCreateKingdom)
                return $"[错误] 兵力不足：建国需要至少 {model.MinimumTroopCountToCreateKingdom} 名可战之士（当前 {troopCount} 人，含守军）。";

            if (string.IsNullOrWhiteSpace(kingdomName))
                return "[错误] 请提供新王国的名称（国号）。";
            // 国号查重：避免与现有王国同名造成世界局势/外交按名解析混淆
            foreach (var k in Kingdom.All)
            {
                if (!k.IsEliminated && (k.Name?.ToString() ?? "").Equals(kingdomName, StringComparison.OrdinalIgnoreCase))
                    return $"[错误] 已有王国名为「{kingdomName}」，请另取国号。";
            }

            // 文化：参数指定（精确优先，再模糊；忽略大小写）或取氏族文化。
            // 修复（与 query_character 同款）：原实现只有双向 Contains 模糊匹配且区分大小写——
            // 精确名未优先（如输入"帝国"时可能命中多个帝国子串）、英文小写"empire"会失配。
            CultureObject? cultureObj = null;
            if (!string.IsNullOrEmpty(culture))
            {
                try
                {
                    var cultures = Game.Current.ObjectManager.GetObjectTypeList<CultureObject>().ToList();
                    cultureObj = cultures.FirstOrDefault(c =>
                            (c.Name?.ToString() ?? "").Equals(culture, StringComparison.OrdinalIgnoreCase))
                        ?? cultures.FirstOrDefault(c =>
                        {
                            var cn = c.Name?.ToString() ?? "";
                            return cn.IndexOf(culture, StringComparison.OrdinalIgnoreCase) >= 0
                                || culture.IndexOf(cn, StringComparison.OrdinalIgnoreCase) >= 0;
                        });
                }
                catch { }
            }
            cultureObj ??= clan.Culture;
            if (cultureObj == null)
                return "[错误] 无法确定新王国的文化。";

            var oldKingdom = clan.Kingdom;
            var oldKingdomName = oldKingdom?.Name?.ToString() ?? "?";

            try
            {
                // 封臣：先叛乱脱离旧国（保留封地、对旧国及其交战方宣战），再建国。
                if (clan.Kingdom != null)
                    ChangeKingdomAction.ApplyByLeaveWithRebellionAgainstKingdom(clan);

                // 建国前从氏族纹章提取真实底色：原版封臣氏族的 Color/Color2 是默认占位值 0xFFCCC3AB（浅米黄，肉眼近白）且两者相同，
                // CreateKingdom 会把它用作新王国的 Primary/SecondaryBannerColor，再经 UpdateBannerColorsAccordingToKingdom
                // 把旗帜背景与图标刷成同一米黄色 → 图案不可见（"纯白旗"）。改用纹章实际主色/图标色保证对比。
                var clanBanner = clan.Banner;
                if (clanBanner != null)
                {
                    clan.Color = clanBanner.GetPrimaryColor();
                    clan.Color2 = clanBanner.GetFirstIconColor();
                }

                // 建国：新建王国、本族为统治氏族、施加文化默认政策、对无邦交势力宣战。
                // 原版玩家建国同款公开 API（KingdomManager.CreateKingdom 接受任意氏族为 founder），
                // kingdom_created / clan_changed_kingdom / war_declared 均由 HistoryRecorder 自动入史。
                Campaign.Current.KingdomManager.CreateKingdom(
                    new TextObject(kingdomName), new TextObject(kingdomName), cultureObj, clan,
                    null, null, null, null);

                var newKingdom = clan.Kingdom;
                if (newKingdom == null || !newKingdom.Name.ToString().Equals(kingdomName, StringComparison.OrdinalIgnoreCase))
                    return "[错误] 建国流程异常：未能建立王国。";

                // 角色转变：建国称王后刷新实体头衔与能力缓存——否则本实体拿不到 Diplomat（国王工具门控）直到下次建档
                EntityManager.RefreshEntity(hero);

                // 立国宣言补记入史（kingdom_created 已由 OnClanChangedKingdom 自动记录，此处补记宣言）
                if (!string.IsNullOrEmpty(motto))
                    HistoryRecorder.RecordDiplomacyEvent("kingdom_created", $"{clan.Name}之立国宣言：「{motto}」");

                // 建国专题纪事：史官立即编纂建国始末（增强叙事）
                AgentScheduler.QueueSpecialChronicle($"新王国建立：{newKingdom.Name}，{clan.Name}族长{clan.Leader?.Name}称王。");

                var founderLine = clan.Leader != null ? $"你即位为第一代国君。" : "";
                var secessionLine = oldKingdom != null
                    ? $"你已以叛乱之姿脱离{oldKingdomName}，对其宣战并保留了封地。"
                    : "你以独立氏族之姿举旗立国。";
                var mottoLine = !string.IsNullOrEmpty(motto) ? $" 立国宣言：「{motto}」" : "";
                return $"国号已立：{kingdomName}（{cultureObj.Name}文化）。{secessionLine} {clan.Name}成为统治氏族，{founderLine}{mottoLine}";
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"create_kingdom 异常：{ex}");
                return $"[错误] 建国失败：{ex.Message}";
            }
        }

    }
}
