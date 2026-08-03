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

    }
}
