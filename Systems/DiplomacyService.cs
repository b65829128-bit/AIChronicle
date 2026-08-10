using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace AIChronicle
{
    public static class DiplomacyService
    {
        private static readonly Dictionary<string, string> _kingdomNameMap = new()
        {
            ["battania"] = "巴旦尼亚", ["battanian"] = "巴旦尼亚",
            ["vlandia"] = "瓦兰迪亚", ["vlandian"] = "瓦兰迪亚",
            ["sturgia"] = "斯特吉亚", ["sturgian"] = "斯特吉亚",
            ["western empire"] = "西帝国", ["west empire"] = "西帝国",
            ["southern empire"] = "南帝国", ["south empire"] = "南帝国",
            ["northern empire"] = "北帝国", ["north empire"] = "北帝国",
            ["khuzait"] = "库赛特",
            ["aserai"] = "阿塞莱",
        };

        internal static Kingdom? FindKingdom(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var searchName = name.Trim();
            var lower = searchName.ToLowerInvariant();
            if (_kingdomNameMap.TryGetValue(lower, out var mapped))
                searchName = mapped;
            foreach (var k in Kingdom.All)
            {
                var kName = k.Name?.ToString() ?? "";
                if (kName.Contains(searchName) || searchName.Contains(kName))
                    return k;
            }
            foreach (var k in Kingdom.All)
            {
                var kName = (k.Name?.ToString() ?? "").ToLowerInvariant();
                if (kName.Contains(lower) || lower.Contains(kName))
                    return k;
            }
            return null;
        }

        private static Kingdom? GetHeroKingdom(Hero hero)
        {
            foreach (var k in Kingdom.All)
            {
                if (k.RulingClan?.Leader == hero)
                    return k;
                if (k.Clans.Contains(hero.Clan))
                    return k;
            }
            return hero.MapFaction as Kingdom;
        }

        internal static Hero? GetDiplomacyHero()
        {
            if (AIChatClient.CurrentHero != null)
            {
                var k = AIChatClient.CurrentHero.MapFaction as Kingdom;
                if (k?.RulingClan?.Leader == AIChatClient.CurrentHero)
                    return AIChatClient.CurrentHero;
            }
            if (Hero.MainHero != null)
            {
                var k = Hero.MainHero.MapFaction as Kingdom;
                if (k?.RulingClan?.Leader == Hero.MainHero)
                    return Hero.MainHero;
            }
            return null;
        }

        internal static string ExecuteDeclareWar(string targetKingdomName, string? message)
        {
            var actingHero = GetDiplomacyHero();
            if (actingHero == null) return "[错误] 只有王国统治者才能宣战";
            var myKingdom = actingHero.MapFaction as Kingdom;
            if (myKingdom == null) return "[错误] 你当前不属于任何王国";
            var target = FindKingdom(targetKingdomName);
            if (target == null) return $"[错误] 未找到王国：{targetKingdomName}";
            if (target == myKingdom) return "[错误] 不能对自己宣战";
            if (myKingdom.IsAtWarWith(target)) return $"已经与{target.Name}处于交战状态。";
            // 宣战宣言：供 HistoryRecorder 记 war_declared 史料（历史与现实对照），用完清空
            HistoryRecorder.PendingWarDeclaration = string.IsNullOrWhiteSpace(message) ? null : message;
            try { DeclareWarAction.ApplyByDefault(myKingdom, target); }
            finally
            {
                HistoryRecorder.PendingWarDeclaration = null;
            }
            var msgSuffix = !string.IsNullOrEmpty(message) ? $"\n宣战声明：{message}" : "";
            InformationManager.DisplayMessage(new InformationMessage(
                $"{myKingdom.Name} 向 {target.Name} 宣战！{msgSuffix}", Colors.Red));
            return $"已向{target.Name}宣战。{msgSuffix}";
        }

        internal static string ExecuteProposePeace(string targetKingdomName, int tributeAmount, int tributeDays, string? message)
        {
            var actingHero = GetDiplomacyHero();
            if (actingHero == null) return "[错误] 只有王国统治者才能提出议和";
            var myKingdom = GetHeroKingdom(actingHero);
            if (myKingdom == null) return "[错误] 你当前不属于任何王国";
            var target = FindKingdom(targetKingdomName);
            if (target == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{actingHero.Name} 提议议和失败：未找到王国「{targetKingdomName}」", Colors.Red));
                return $"[错误] 未找到王国：{targetKingdomName}";
            }

            if (!myKingdom.IsAtWarWith(target))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{actingHero.Name} 提议议和失败：并未与{target.Name}交战", Colors.Red));
                return $"[错误] 当前并未与{target.Name}交战";
            }
            var myEntity = EntityManager.GetOrCreateEntity(actingHero);
            var targetRuler = target.RulingClan?.Leader;
            if (targetRuler == null) return $"[错误] {target.Name} 没有统治者";
            var targetEntity = EntityManager.GetOrCreateEntity(targetRuler);
            var tributeArg = $"{tributeAmount}_{tributeDays}";
            AgentManager.StoreDiplomacyProposal(myEntity.Id, targetEntity.Id, "peace", tributeArg, message);
            var msgPart = !string.IsNullOrEmpty(message) ? $"\n\n附言：\"{message}\"" : "";
            var tributeDesc = tributeAmount > 0
                ? $"愿意每日支付 {tributeAmount} 金币、持续 {tributeDays} 天作为赔款"
                : tributeAmount < 0
                    ? $"要求对方每日赔偿 {Math.Abs(tributeAmount)} 金币、持续 {tributeDays} 天"
                    : "不附带赔款条件";
            QueueKingActivation(targetEntity.Id, myEntity.Id,
                $"来自 {myEntity.Name}（{myKingdom.Name} 统治者）的议和提案：{tributeDesc}。你接受这个议和条件吗？请用 respond_to_diplomacy_proposal 回复。{msgPart}");
            InformationManager.DisplayMessage(new InformationMessage(
                $"{myEntity.Name}（{myKingdom.Name}）向 {target.Name} 提议议和", Colors.Cyan));
            return $"议和提案已发送给{target.Name}的统治者{targetRuler.Name}，等待回复。";
        }

        internal static string ExecuteProposeAlliance(string targetKingdomName, string? message)
        {
            var actingHero = GetDiplomacyHero();
            if (actingHero == null) return "[错误] 只有王国统治者才能提议结盟";
            var myKingdom = actingHero.MapFaction as Kingdom;
            if (myKingdom == null) return "[错误] 你当前不属于任何王国";
            var target = FindKingdom(targetKingdomName);
            if (target == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{actingHero.Name} 提议结盟失败：未找到王国「{targetKingdomName}」", Colors.Red));
                return $"[错误] 未找到王国：{targetKingdomName}";
            }
            if (target == myKingdom) return "[错误] 不能和自己结盟";
            if (myKingdom.IsAtWarWith(target)) return $"[错误] 无法与交战中的王国结盟";
            var targetRuler = target.RulingClan?.Leader;
            if (targetRuler == null) return $"[错误] {target.Name} 没有统治者";
            var myEntity = EntityManager.GetOrCreateEntity(actingHero);
            var targetEntity = EntityManager.GetOrCreateEntity(targetRuler);
            AgentManager.StoreDiplomacyProposal(myEntity.Id, targetEntity.Id, "alliance", null, message);
            var msgPart = !string.IsNullOrEmpty(message) ? $"\n\n附言：\"{message}\"" : "";
            QueueKingActivation(targetEntity.Id, myEntity.Id,
                $"来自 {myEntity.Name}（{myKingdom.Name} 统治者）的结盟提案。你接受这个结盟邀请吗？请用 respond_to_diplomacy_proposal 回复。{msgPart}");
            InformationManager.DisplayMessage(new InformationMessage(
                $"{myEntity.Name}（{myKingdom.Name}）向 {target.Name} 提议结盟", Colors.Cyan));
            return $"结盟提案已发送给{target.Name}的统治者{targetRuler.Name}，等待回复。";
        }

        internal static string ExecuteProposeTrade(string targetKingdomName, string? message)
        {
            var actingHero = GetDiplomacyHero();
            if (actingHero == null) return "[错误] 只有王国统治者才能提议贸易协定";
            var myKingdom = actingHero.MapFaction as Kingdom;
            if (myKingdom == null) return "[错误] 你当前不属于任何王国";
            var target = FindKingdom(targetKingdomName);
            if (target == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{actingHero.Name} 提议贸易协定失败：未找到王国「{targetKingdomName}」", Colors.Red));
                return $"[错误] 未找到王国：{targetKingdomName}";
            }
            if (target == myKingdom) return "[错误] 不能和自己签订贸易协定";
            if (myKingdom.IsAtWarWith(target)) return $"[错误] 无法与交战中的王国签订贸易协定";
            var targetRuler = target.RulingClan?.Leader;
            if (targetRuler == null) return $"[错误] {target.Name} 没有统治者";
            var myEntity = EntityManager.GetOrCreateEntity(actingHero);
            var targetEntity = EntityManager.GetOrCreateEntity(targetRuler);
            AgentManager.StoreDiplomacyProposal(myEntity.Id, targetEntity.Id, "trade", null, message);
            var msgPart = !string.IsNullOrEmpty(message) ? $"\n\n附言：\"{message}\"" : "";
            QueueKingActivation(targetEntity.Id, myEntity.Id,
                $"来自 {myEntity.Name}（{myKingdom.Name} 统治者）的贸易协定提案。你接受这个贸易协定吗？请用 respond_to_diplomacy_proposal 回复。{msgPart}");
            InformationManager.DisplayMessage(new InformationMessage(
                $"{myEntity.Name}（{myKingdom.Name}）向 {target.Name} 提议贸易协定", Colors.Cyan));
            return $"贸易协定提案已发送给{target.Name}的统治者{targetRuler.Name}，等待回复。";
        }

        internal static string ExecuteEndAlliance(string targetKingdomName)
        {
            var actingHero = GetDiplomacyHero();
            if (actingHero == null) return "[错误] 只有王国统治者才能终止盟约";
            var myKingdom = actingHero.MapFaction as Kingdom;
            if (myKingdom == null) return "[错误] 你当前不属于任何王国";
            var target = FindKingdom(targetKingdomName);
            if (target == null) return $"[错误] 未找到王国：{targetKingdomName}";
            if (target == myKingdom) return "[错误] 不能和自己终止盟约";

            var allianceBehavior = Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
            if (allianceBehavior == null || !allianceBehavior.IsAllyWithKingdom(myKingdom, target))
                return $"[错误] {target.Name} 与你的王国没有盟约";

            allianceBehavior.EndAlliance(myKingdom, target); // 单向终止，无需对方确认
            ClearAgreementTracking("盟约", myKingdom, target); // 主动背约 → 清到期日志+观察项，防误判为期满而罢
            HistoryRecorder.RecordDiplomacyEvent("alliance_broken", $"{myKingdom.Name}单方面终止了与{target.Name}的盟约");
            InformationManager.DisplayMessage(new InformationMessage(
                $"{myKingdom.Name} 终止了与 {target.Name} 的盟约", Colors.Cyan));
            return $"你单方面终止了与 {target.Name} 的盟约。";
        }

        internal static string ExecuteEndTradeAgreement(string targetKingdomName)
        {
            var actingHero = GetDiplomacyHero();
            if (actingHero == null) return "[错误] 只有王国统治者才能终止贸易协定";
            var myKingdom = actingHero.MapFaction as Kingdom;
            if (myKingdom == null) return "[错误] 你当前不属于任何王国";
            var target = FindKingdom(targetKingdomName);
            if (target == null) return $"[错误] 未找到王国：{targetKingdomName}";
            if (target == myKingdom) return "[错误] 不能和自己终止贸易协定";

            var tradeBehavior = Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
            if (tradeBehavior == null || !tradeBehavior.HasTradeAgreement(myKingdom, target, out _))
                return $"[错误] {target.Name} 与你的王国没有贸易协定";

            tradeBehavior.EndTradeAgreement(myKingdom, target); // 单向终止，无需对方确认
            ClearAgreementTracking("贸易", myKingdom, target); // 主动背约 → 清到期日志+观察项，防误判为期满而罢
            HistoryRecorder.RecordDiplomacyEvent("trade_broken", $"{myKingdom.Name}单方面终止了与{target.Name}的贸易协定");
            InformationManager.DisplayMessage(new InformationMessage(
                $"{myKingdom.Name} 终止了与 {target.Name} 的贸易协定", Colors.Cyan));
            return $"你单方面终止了与 {target.Name} 的贸易协定。";
        }

        internal static string ExecuteTransferFief(string settlementName, string targetEntityId, string reason = "")
        {
            var actingHero = GetDiplomacyHero();
            if (actingHero == null) return "[错误] 只有王国统治者才能转让封地";
            var myKingdom = actingHero.MapFaction as Kingdom;
            if (myKingdom == null) return "[错误] 你当前不属于任何王国";

            var settlement = FindSettlement(settlementName);
            if (settlement == null) return $"[错误] 未找到定居点：{settlementName}";
            if (!settlement.IsTown && !settlement.IsCastle)
                return $"[错误] 只能转让城镇或城堡，村庄归属其主城无法单独转让。";

            var ownerClan = settlement.OwnerClan;
            if (ownerClan == null) return $"[错误] {settlement.Name} 没有归属家族";
            if (ownerClan.Kingdom != myKingdom)
                return $"[错误] {settlement.Name} 不在你的王国内";

            // 被夺方（原主）在转让前捕获——转让后 OwnerClan 已变
            var deprivedLeader = ownerClan.Leader;

            var targetId = EntityManager.ResolveEntityId(targetEntityId) ?? targetEntityId;
            var targetEntity = EntityManager.GetEntityById(targetId);
            if (targetEntity == null) return $"[错误] 未找到目标实体：{targetEntityId}";
            var targetHero = targetEntity.HeroRef;
            if (targetHero == null) return $"[错误] 目标实体无效";

            if (targetHero.Clan?.Kingdom != myKingdom)
                return $"[错误] {targetHero.Name} 不在你的王国内";

            if (targetHero.Clan?.Leader != targetHero)
                return $"[错误] {targetHero.Name} 不是家族领袖，只有家族领袖能持有封地";

            if (targetHero.Clan.IsUnderMercenaryService)
                return $"[错误] {targetHero.Name} 的家族是雇佣兵，雇佣兵不能持有封地";

            if (targetHero.Clan == ownerClan)
                return $"[错误] {settlement.Name} 已经属于 {targetHero.Clan.Name}";

            var oldClanName = ownerClan.Name?.ToString() ?? "?";
            var oldLeaderName = ownerClan.Leader?.Name?.ToString() ?? "?";
            var newClanName = targetHero.Clan.Name?.ToString() ?? "?";

            // 册封宣言：供 HistoryRecorder 记 fief_granted 史料（从谁给谁 + 王曰），用完清空
            HistoryRecorder.PendingFiefGrantText = string.IsNullOrWhiteSpace(reason)
                ? $"{actingHero.Name}册封"
                : $"{actingHero.Name}以「{reason}」册封";
            try
            {
                ChangeOwnerOfSettlementAction.ApplyByKingDecision(targetHero, settlement);
            }
            finally
            {
                HistoryRecorder.PendingFiefGrantText = null;
            }

            var notice = string.IsNullOrWhiteSpace(reason)
                ? $"{actingHero.Name} 将 {settlement.Name} 从 {oldClanName} 转让给了 {newClanName}"
                : $"{actingHero.Name} 以「{reason}」为由，将 {settlement.Name} 从 {oldClanName} 转让给了 {newClanName}";
            InformationManager.DisplayMessage(new InformationMessage(notice, Colors.Cyan));

            // 被夺方激活：原主（非国王本人）被夺封 → 队列封地审视，激起矛盾（矛盾来源是"失去的人"）
            if (deprivedLeader != null && deprivedLeader != actingHero)
            {
                var deprivedEntity = EntityManager.GetEntityByHero(deprivedLeader);
                if (deprivedEntity != null)
                {
                    var reasonText = string.IsNullOrWhiteSpace(reason) ? "（国王未明示理由）" : reason;
                    var content = $"你的封地 {settlement.Name} 被国王 {actingHero.Name} 夺走，赐予了 {newClanName}（{targetHero.Name}）。国王给出的理由是：{reasonText}";
                    AgentScheduler.QueueFiefReview(deprivedEntity.Id, content);
                }
            }

            return $"已将{settlement.Name}从{oldClanName}（{oldLeaderName}）转让给{newClanName}（{targetHero.Name}）。";
        }

        internal static string ExecuteRespondToProposal(string proposalId, bool accepted)
        {
            var actingHero = GetDiplomacyHero();
            if (actingHero == null) return "[错误] 只有王国统治者才能处理外交提案";
            var myKingdom = GetHeroKingdom(actingHero);
            if (myKingdom == null) return "[错误] 你当前不属于任何王国";

            var content = AgentManager.ReadDiplomacyProposal(proposalId);
            var matchedId = proposalId;
            if (content == null)
            {
                var selfEntity = EntityManager.GetEntityByHero(actingHero);
                if (selfEntity != null)
                {
                    matchedId = AgentManager.FuzzyFindProposal(proposalId, selfEntity.Id);
                    if (matchedId != null)
                        content = AgentManager.ReadDiplomacyProposal(matchedId);
                }
            }
            if (content == null || matchedId == null) return $"[错误] 未找到提案：{proposalId}";

            var parts = matchedId.Split('_');
            if (parts.Length < 3) return $"[错误] 无效的提案ID格式";

            var proposerId = "";
            var targetId = "";
            var type = "";
            var toIdx = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "to")
                {
                    proposerId = string.Join("_", parts.Take(i));
                    toIdx = i;
                    break;
                }
            }
            if (toIdx < 0) return $"[错误] 无法解析提案ID";
            for (int i = toIdx + 1; i < parts.Length; i++)
            {
                if (parts[i] == "peace" || parts[i] == "alliance" || parts[i] == "trade")
                {
                    targetId = string.Join("_", parts.Skip(toIdx + 1).Take(i - toIdx - 1));
                    type = parts[i];
                    break;
                }
            }

            var myEntity = EntityManager.GetOrCreateEntity(actingHero);
            if (myEntity == null || myEntity.Id != targetId)
                return "[错误] 该提案不是发给你的";

            var proposerEntity = EntityManager.GetOrCreateEntityById(proposerId);
            if (proposerEntity?.HeroRef == null) return "[错误] 无法找到提案发起人";

            if (!accepted)
            {
                AgentManager.DeleteDiplomacyProposal(matchedId);
                RecordDecision(myEntity.Id, proposerId, type, "拒绝", matchedId);
                var rejectTypeName = type switch { "peace" => "议和", "alliance" => "结盟", "trade" => "贸易协定", _ => type };
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{myEntity.Name} 拒绝了 {proposerEntity.Name} 的{rejectTypeName}提案", Colors.Yellow));
                return "已拒绝该提案。";
            }

            var proposerKingdom = GetHeroKingdom(proposerEntity.HeroRef);
            if (proposerKingdom == null) return "[错误] 提案发起人已不属于任何王国";

            switch (type)
            {
                case "peace":
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"[诊断] respond→peace：{myKingdom.Name} IsAtWarWith {proposerKingdom.Name} = {myKingdom.IsAtWarWith(proposerKingdom)}",
                        Colors.Cyan));

                    if (!myKingdom.IsAtWarWith(proposerKingdom)) return "[错误] 当前并未与该王国交战";
                    var lines = content.Split('\n');
                    var tribute = "0_0";
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("tribute="))
                        {
                            tribute = line.Substring(8);
                            break;
                        }
                    }
                    var tributeParts = tribute.Split('_');
                    var tributeAmount = int.TryParse(tributeParts[0], out var a) ? a : 0;
                    var tributeDays = int.TryParse(tributeParts[1], out var d) ? d : 0;
                    var detailType = typeof(MakePeaceAction).GetNestedType("MakePeaceDetail",
                        BindingFlags.Public | BindingFlags.NonPublic);
                    var defaultDetail = Enum.ToObject(detailType!, 0);
                    var applyInternal = typeof(MakePeaceAction).GetMethod("ApplyInternal",
                        BindingFlags.NonPublic | BindingFlags.Static);
                    applyInternal!.Invoke(null, new object[] { proposerKingdom, myKingdom, tributeAmount, tributeDays, defaultDetail });
                    AgentManager.DeleteDiplomacyProposal(matchedId);
                    RecordDecision(myEntity.Id, proposerId, type, "接受", matchedId);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{myEntity.Name}（{myKingdom.Name}）接受了 {proposerEntity.Name} 的议和提案", Colors.Green));
                    return $"已接受议和，与{proposerKingdom.Name}达成和平。";
                }
                case "alliance":
                {
                    var ab = Campaign.Current.GetCampaignBehavior<IAllianceCampaignBehavior>();
                    if (ab == null) return "[错误] 联盟系统未初始化";
                    ab.StartAlliance(proposerKingdom, myKingdom);
                    ClearAgreementTracking("盟约", proposerKingdom, myKingdom); // 重新结盟生效 → 立即清旧到期日志+观察项
                    HistoryRecorder.RecordDiplomacyEvent("alliance_made", $"{proposerKingdom.Name}与{myKingdom.Name}结盟");
                    AgentManager.DeleteDiplomacyProposal(matchedId);
                    RecordDecision(myEntity.Id, proposerId, type, "接受", matchedId);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{myEntity.Name}（{myKingdom.Name}）接受了 {proposerEntity.Name} 的结盟提案", Colors.Green));
                    return $"已接受结盟，与{proposerKingdom.Name}组成军事同盟。";
                }
                case "trade":
                {
                    var tb = Campaign.Current.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
                    if (tb == null) return "[错误] 贸易系统未初始化";
                    tb.MakeTradeAgreement(proposerKingdom, myKingdom, CampaignTime.Years(1f));
                    ClearAgreementTracking("贸易", proposerKingdom, myKingdom); // 重签生效 → 立即清旧到期日志+观察项
                    HistoryRecorder.RecordDiplomacyEvent("trade_made", $"{proposerKingdom.Name}与{myKingdom.Name}订立贸易协定");
                    AgentManager.DeleteDiplomacyProposal(matchedId);
                    RecordDecision(myEntity.Id, proposerId, type, "接受", matchedId);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{myEntity.Name}（{myKingdom.Name}）接受了 {proposerEntity.Name} 的贸易协定", Colors.Green));
                    return $"已接受贸易协定，与{proposerKingdom.Name}建立贸易关系。";
                }
                default:
                    return $"[错误] 未知的提案类型：{type}";
            }
        }

        private static Settlement? FindSettlement(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var trimmed = name.Trim();
            var lower = trimmed.ToLowerInvariant();

            foreach (var s in Settlement.All)
            {
                var sName = s.Name?.ToString() ?? "";
                if (sName.Trim().Equals(trimmed, StringComparison.OrdinalIgnoreCase))
                    return s;
            }

            var candidates = new List<Settlement>();
            foreach (var s in Settlement.All)
            {
                var sName = s.Name?.ToString() ?? "";
                if (sName.Contains(trimmed) || trimmed.Contains(sName))
                    candidates.Add(s);
            }

            if (candidates.Count > 0)
            {
                var fort = candidates.FirstOrDefault(c => c.IsTown || c.IsCastle);
                if (fort != null) return fort;
                return candidates[0];
            }

            foreach (var s in Settlement.All)
            {
                var sName = (s.Name?.ToString() ?? "").ToLowerInvariant();
                if (sName.Contains(lower) || lower.Contains(sName))
                    candidates.Add(s);
            }

            if (candidates.Count > 0)
            {
                var fort = candidates.FirstOrDefault(c => c.IsTown || c.IsCastle);
                if (fort != null) return fort;
                return candidates[0];
            }

            return null;
        }

        private static void QueueKingActivation(string agentId, string targetId, string message)
        {
            AgentScheduler.RecordProposalActivation(agentId);
            AgentScheduler.QueueEvent(new ActivationEvent
            {
                Type = ActivationEventType.KingDiplomacy,
                AgentId = agentId,
                TargetId = targetId,
                Content = message,
                Depth = 0
            });
        }

        private static void RecordDecision(string myId, string otherId, string proposalType, string result, string proposalId)
        {
            var typeName = proposalType switch
            {
                "peace" => "议和",
                "alliance" => "结盟",
                "trade" => "贸易协定",
                _ => proposalType
            };
            var timestamp = PromptManager.GetCurrentTimeString();
            var entry = $"[{timestamp}] {result}了来自 {otherId} 的{typeName}提案（{proposalId}）\n";
            AgentManager.AppendDecisionFor(myId, entry);
        }

        // ==================== 盟约/贸易协定到期记录（供国王自查） ====================

        /// <summary>到期记录保留的游戏天数：超过即从日志清除，防止信息无限堆积。</summary>
        private const double ExpiryLogKeepDays = 90;

        /// <summary>剩余不足此天数视为「即将到期」：在到期当天早上记录，早于被惰性清理（HasTradeAgreement 查询即删）。</summary>
        private const double ExpiryLogThresholdDays = 1;

        /// <summary>
        /// 每日轻量检测（无 LLM、不激活 Agent）：
        /// 1) 把当天到期的盟约/贸易协定写进 World/diplomacy/expiry_log.txt（国王自查用）。每对王国+类型最多保留
        ///    一条最近记录，超 90 游戏天的旧记录自动清除。到期之前不记录、不提示；国王下次 query_world_state 时自行看到。
        /// 2) 盟约/贸易的**自然到期**写进原始史料（alliance_expired / trade_expired）——与单方背约
        ///    （alliance_broken / trade_broken，ExecuteEnd* 记录）区分：期满而罢是"约期已尽"，背约是"单方毁约"。
        ///    用 agreements_tracked.txt 做观察集（key=类型|王国1ID|王国2ID → 到期日day）：到期当天协定被惰性清理
        ///    （HasTradeAgreement/IsAllyWithKingdom 查询即删）时，比对观察集判定"自然到期"，记一次即移除；
        ///    到期前消失（战争/灭国/单方背约，已由对应路径清观察项）静默移除，不误报为到期。
        /// </summary>
        internal static void CheckExpiringAgreements()
        {
            if (Campaign.Current == null) return;
            var ab = Campaign.Current.GetCampaignBehavior<IAllianceCampaignBehavior>();
            var tb = Campaign.Current.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
            if (ab == null && tb == null) return;

            var logPath = System.IO.Path.Combine(AgentManager.GetDiplomacyDir(), "expiry_log.txt");
            var nowDays = CampaignTime.Now.ToDays;

            // 1) 读旧到期日志：key = 类型|王国1ID|王国2ID。超 90 游戏天的旧记录直接丢弃（防无限堆积）。
            var records = new Dictionary<string, string>(StringComparer.Ordinal);
            if (System.IO.File.Exists(logPath))
            {
                foreach (var raw in SafeFileIO.ReadAllLines(logPath))
                {
                    if (!TryParseExpiryLine(raw, out var key, out var endDay)) continue;
                    if (nowDays - endDay >= ExpiryLogKeepDays) continue;
                    records[key] = raw; // 同 key 保留最后一条
                }
            }

            // 2) 读观察集（自然到期检测用）：key = 类型|王国1ID|王国2ID → 到期日day
            var trackedPath = System.IO.Path.Combine(AgentManager.GetDiplomacyDir(), "agreements_tracked.txt");
            var tracked = new Dictionary<string, double>(StringComparer.Ordinal);
            if (System.IO.File.Exists(trackedPath))
            {
                foreach (var raw in SafeFileIO.ReadAllLines(trackedPath))
                {
                    if (TryParseTrackedLine(raw, out var key, out var endDay))
                        tracked[key] = endDay;
                }
            }

            // 3) 扫描王国对：更新到期日志 + 刷新观察集（当前生效协定的到期日）
            var active = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var k1 in Kingdom.All)
            {
                if (k1.IsEliminated) continue;
                foreach (var k2 in Kingdom.All)
                {
                    if (k2.IsEliminated || k2 == k1) continue;
                    if (string.CompareOrdinal(k1.StringId, k2.StringId) >= 0) continue; // 每对只处理一次

                    // 贸易协定：仍生效且未临期 → 清掉旧记录（续约后不残留矛盾信息）；临期 → 写/覆盖记录
                    var tradeKey = $"贸易|{k1.StringId}|{k2.StringId}";
                    if (tb != null && tb.HasTradeAgreement(k1, k2, out var t))
                    {
                        active[tradeKey] = t.EndTime.ToDays;
                        if (t.EndTime.RemainingDaysFromNow < ExpiryLogThresholdDays)
                            records[tradeKey] = BuildExpiryLine("贸易协定", k1, k2, t.EndTime);
                        else
                            records.Remove(tradeKey);
                    }
                    // 协定已不存在 → 保留最近一条到期记录（保留期内）

                    // 盟约：同上
                    var allianceKey = $"盟约|{k1.StringId}|{k2.StringId}";
                    if (ab != null && ab.IsAllyWithKingdom(k1, k2))
                    {
                        var end = ab.GetAllianceEndDate(k1, k2);
                        active[allianceKey] = end.ToDays;
                        if (end.RemainingDaysFromNow < ExpiryLogThresholdDays)
                            records[allianceKey] = BuildExpiryLine("盟约", k1, k2, end);
                        else
                            records.Remove(allianceKey);
                    }
                }
            }

            // 4) 自然到期检测：观察集里有、当前已不生效的协定 → 到期日已过则记 _expired 史料（期满而罢）；
            //    到期前消失（战争/灭国/单方背约）静默移除——背约已由 ExecuteEnd* 记 _broken，战争/灭国由对应史料事件叙事。
            foreach (var kv in tracked)
            {
                if (active.ContainsKey(kv.Key)) continue; // 仍在生效（含续约）
                if (kv.Value > nowDays) continue;         // 到期前消失，非自然到期
                if (TryParseTrackedKey(kv.Key, out var type, out var id1, out var id2))
                {
                    var kA = FindKingdomById(id1);
                    var kB = FindKingdomById(id2);
                    if (kA == null || kB == null) continue;
                    var dueText = PromptManager.FormatCampaignDate(CampaignTime.Days((float)kv.Value));
                    if (type == "盟约")
                        HistoryRecorder.RecordDiplomacyEvent("alliance_expired", $"{kA.Name}与{kB.Name}的盟约于{dueText}期满而罢");
                    else
                        HistoryRecorder.RecordDiplomacyEvent("trade_expired", $"{kA.Name}与{kB.Name}的贸易协定于{dueText}期满而罢");
                }
            }

            // 5) 写回观察集：只保留当前生效协定（到期/消失项不保留 → 自然到期只记一次）
            var trackedLines = active.Select(kv => $"{kv.Key}|{(int)kv.Value}").ToList();
            if (trackedLines.Count > 0)
                SafeFileIO.WriteAllText(trackedPath, string.Join("\n", trackedLines));
            else if (System.IO.File.Exists(trackedPath))
            {
                try { System.IO.File.Delete(trackedPath); } catch { }
            }

            // 6) 写回到期日志
            if (records.Count > 0)
                SafeFileIO.WriteAllText(logPath, string.Join("\n", records.Values));
            else if (System.IO.File.Exists(logPath))
            {
                try { System.IO.File.Delete(logPath); } catch { }
            }
        }

        /// <summary>到期日志行格式：类型|王国1ID|王国2ID|到期日day|人类可读文本（如 盟约 斯特吉亚与瓦兰迪亚 于第1089年夏第12日到期）。</summary>
        private static string BuildExpiryLine(string type, Kingdom k1, Kingdom k2, CampaignTime endTime)
        {
            var dateText = PromptManager.FormatCampaignDate(endTime);
            return $"{type}|{k1.StringId}|{k2.StringId}|{(int)endTime.ToDays}|{type} {k1.Name}与{k2.Name} 于{dateText}到期";
        }

        private static bool TryParseExpiryLine(string line, out string key, out double endDay)
        {
            key = null!;
            endDay = 0;
            var parts = line.Split('|');
            if (parts.Length < 5) return false;
            if (!double.TryParse(parts[3], out endDay)) return false;
            key = parts[0] + "|" + parts[1] + "|" + parts[2];
            return true;
        }

        /// <summary>观察集行解析：类型|王国1ID|王国2ID|到期日day（见 agreements_tracked.txt）。</summary>
        private static bool TryParseTrackedLine(string line, out string key, out double endDay)
        {
            key = null!;
            endDay = 0;
            var parts = line.Split('|');
            if (parts.Length < 4) return false;
            if (!double.TryParse(parts[3], out endDay)) return false;
            key = parts[0] + "|" + parts[1] + "|" + parts[2];
            return true;
        }

        private static bool TryParseTrackedKey(string key, out string type, out string kingdom1Id, out string kingdom2Id)
        {
            type = "";
            kingdom1Id = "";
            kingdom2Id = "";
            var parts = key.Split('|');
            if (parts.Length != 3) return false;
            type = parts[0];
            kingdom1Id = parts[1];
            kingdom2Id = parts[2];
            return true;
        }

        private static Kingdom? FindKingdomById(string id)
        {
            foreach (var k in Kingdom.All)
            {
                if (k.StringId == id) return k;
            }
            return null;
        }

        /// <summary>
        /// 协定因非自然原因终止（单方背约/战争毁约/灭国）时调用：清掉到期日志与该对的自然到期观察项，
        /// 防止观察集把"战争毁约/背约"误判为"期满而罢"。type 用"盟约"/"贸易"（与观察集 key 前缀一致）。
        /// </summary>
        internal static void ClearAgreementTracking(string type, Kingdom k1, Kingdom k2)
        {
            ClearExpiryRecord(type == "贸易" ? "贸易协定" : type, k1, k2); // 到期日志类型前缀沿用原"贸易协定"写法
            var trackedPath = System.IO.Path.Combine(AgentManager.GetDiplomacyDir(), "agreements_tracked.txt");
            try
            {
                if (!System.IO.File.Exists(trackedPath)) return;
                var id1 = k1.StringId;
                var id2 = k2.StringId;
                var prefixes = new[] { $"{type}|{id1}|{id2}|", $"{type}|{id2}|{id1}|" };
                var remaining = SafeFileIO.ReadAllLines(trackedPath)
                    .Where(l => !prefixes.Any(p => l.StartsWith(p, StringComparison.Ordinal))).ToList();
                if (remaining.Count == 0)
                {
                    try { System.IO.File.Delete(trackedPath); } catch { }
                }
                else
                {
                    SafeFileIO.WriteAllText(trackedPath, string.Join("\n", remaining));
                }
            }
            catch { }
        }

        /// <summary>
        /// 清除某王国对的到期记录：盟约/贸易协定重新建立（或主动结束）时立即调用，
        /// 确保国王下一次激活时看不到已失效的「到期」信息，避免反复查询求证浪费 token。
        /// </summary>
        internal static void ClearExpiryRecord(string type, Kingdom k1, Kingdom k2)
        {
            try
            {
                var id1 = k1.StringId;
                var id2 = k2.StringId;
                var key = string.CompareOrdinal(id1, id2) <= 0 ? $"{type}|{id1}|{id2}" : $"{type}|{id2}|{id1}";
                var logPath = System.IO.Path.Combine(AgentManager.GetDiplomacyDir(), "expiry_log.txt");
                if (!System.IO.File.Exists(logPath)) return;
                var remaining = SafeFileIO.ReadAllLines(logPath)
                    .Where(l => !l.StartsWith(key + "|", StringComparison.Ordinal)).ToList();
                if (remaining.Count == 0)
                {
                    try { System.IO.File.Delete(logPath); } catch { }
                }
                else
                {
                    SafeFileIO.WriteAllText(logPath, string.Join("\n", remaining));
                }
            }
            catch { }
        }
    }
}
