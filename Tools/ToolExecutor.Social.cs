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
        private static string ExecuteChangeRelation(int delta, string? targetEntityId)
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";

            var target = ResolveTargetHero(targetEntityId);
            if (target == null)
                return $"[错误] 未找到目标实体：{targetEntityId}";

            var maxChange = MySettings.Instance?.MaxRelationChange ?? 5;
            if (Math.Abs(delta) > maxChange)
                delta = Math.Sign(delta) * maxChange;

            if (delta == 0)
                return "[信息] 好感变化为 0，无需操作";

            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(AIChatClient.CurrentHero, target, delta, true);
            var currentRelation = AIChatClient.CurrentHero.GetRelation(target);

            return $"对{target.Name}的好感变化了{delta:+0;-0}点，当前好感度为{currentRelation}点。";
        }

        private static string ExecuteGiveGold(int amount, string? targetEntityId)
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";

            var target = ResolveTargetHero(targetEntityId);
            if (target == null)
                return $"[错误] 未找到目标实体：{targetEntityId}";

            if (amount <= 0)
                return "[错误] 金币数额必须大于 0";

            if (AIChatClient.CurrentHero.Gold < amount)
                return $"[错误] {AIChatClient.CurrentHero.Name} 只有 {AIChatClient.CurrentHero.Gold} 金币，不足以赠送 {amount} 金币";

            GiveGoldAction.ApplyBetweenCharacters(AIChatClient.CurrentHero, target, amount);

            return $"已赠予{target.Name} {amount} 金币。{AIChatClient.CurrentHero.Name} 剩余 {AIChatClient.CurrentHero.Gold} 金币。";
        }

        /// <summary>request_gold/request_items 的主线程解析结果：错误 / 已直接完成 / 需玩家弹窗确认。</summary>
        private sealed class RequestResolution
        {
            public string? ErrorMessage;
            public string? DoneMessage;

            public static RequestResolution Error(string msg) => new() { ErrorMessage = msg };
            public static RequestResolution Done(string msg) => new() { DoneMessage = msg };
            public static RequestResolution Popup() => new();
        }

        private static string ExecuteRequestGold(int amount, string? targetEntityId)
        {
            var currentHero = AIChatClient.CurrentHero;
            if (currentHero == null)
                return "[错误] 无当前领主";

            if (amount <= 0)
                return "[错误] 金币数额必须大于 0";

            // 解析目标与金币校验在主线程执行（游戏对象主线程独占）。目标是 NPC 时直接划转完成。
            // 之前 ResolveTargetHero（枚举 Hero.AllAliveHeroes）与读 target.Gold 都在后台线程触碰游戏对象，属主线程独占违规。
            var resolution = MainThreadExecutor.RunOnMainThread(() =>
            {
                var target = ResolveTargetHero(targetEntityId);
                if (target == null)
                    return RequestResolution.Error("[错误] 未找到目标实体：" + targetEntityId);
                if (target.Gold < amount)
                    return RequestResolution.Error($"[错误] {target.Name} 只有 {target.Gold} 金币，不足以支付 {amount} 金币");

                if (target != Hero.MainHero)
                {
                    GiveGoldAction.ApplyBetweenCharacters(target, currentHero, amount);
                    return RequestResolution.Done($"{target.Name} 支付了 {amount} 金币。");
                }
                return RequestResolution.Popup();
            });

            if (resolution.ErrorMessage != null) return resolution.ErrorMessage;
            if (resolution.DoneMessage != null) return resolution.DoneMessage;

            // 目标是玩家：弹窗等待（弹窗带倒计时，超时自动按拒绝处理，见 CheckPendingInquiry）
            using var mre = new ManualResetEventSlim(false);
            var inquiry = new AIChatClient.PendingInquiry
            {
                Hero = currentHero,
                Amount = amount,
                Event = mre,
                Result = false
            };
            AIChatClient.SetPendingInquiry(inquiry);

            mre.Wait(TimeSpan.FromSeconds(AIChatClient.PendingInquiry.PopupWaitSeconds));

            if (inquiry.Result)
            {
                MainThreadExecutor.RunOnMainThread(() => GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, currentHero, amount));
                return $"对方同意支付 {amount} 金币。";
            }
            return $"对方拒绝了支付 {amount} 金币的请求。";
        }

        private static string ExecuteGiveItem(string targetEntityId, string itemName, int count)
        {
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (string.IsNullOrEmpty(itemName) || count <= 0)
                return "[错误] 请指定物品名称和数量";

            var hero = AIChatClient.CurrentHero;
            var myParty = hero.PartyBelongedTo;
            if (myParty == null)
                return $"[错误] {hero.Name} 没有带领部队，无法转移物品";

            var target = ResolveTargetHero(targetEntityId);
            if (target == null)
                return $"[错误] 未找到目标实体：{targetEntityId}";
            if (target == hero)
                return "[错误] 不能把物品给自己";

            var targetParty = target.PartyBelongedTo;
            if (targetParty == null)
                return $"[错误] {target.Name} 没有带领部队，无法接收物品";

            foreach (var ie in myParty.ItemRoster)
            {
                var item = ie.EquipmentElement.Item;
                if (item == null) continue;
                var itemNameStr = item.Name?.ToString() ?? "";
                if (!itemNameStr.Contains(itemName) && !itemName.Contains(itemNameStr)) continue;

                var available = ie.Amount;
                if (available < count)
                    return $"[错误] {itemNameStr} 只有 {available} 个，无法给出 {count} 个。";

                myParty.ItemRoster.AddToCounts(item, -count);
                targetParty.ItemRoster.AddToCounts(item, count);
                return $"已将 {count} 个 {itemNameStr} 交给 {target.Name}。";
            }

            var eqSlots = new[] { EquipmentIndex.Weapon0, EquipmentIndex.Weapon1, EquipmentIndex.Weapon2, EquipmentIndex.Weapon3,
                EquipmentIndex.Head, EquipmentIndex.Body, EquipmentIndex.Leg, EquipmentIndex.Gloves, EquipmentIndex.Cape,
                EquipmentIndex.Horse, EquipmentIndex.HorseHarness };
            var eq = hero.BattleEquipment;
            foreach (var slot in eqSlots)
            {
                var elem = eq.GetEquipmentFromSlot(slot);
                if (elem.Item == null) continue;
                var itemNameStr = elem.Item.Name?.ToString() ?? "";
                if (!itemNameStr.Contains(itemName) && !itemName.Contains(itemNameStr)) continue;

                // 刻意的设计：允许"脱下装备给对方"——装备栏每槽 1 件，脱下即移交，是否脱装由 LLM 自行判断。
                eq[slot] = EquipmentElement.Invalid;
                targetParty.ItemRoster.AddToCounts(elem.Item, 1);
                return $"已将装备栏中的 {itemNameStr} 脱下并交给 {target.Name}。";
            }

            return $"[未找到] 部队和装备栏中都没有 \"{itemName}\"。使用 query_party_troops 查看详情。";
        }

        private static string ExecuteRequestItems(string targetEntityId, string itemName, int count)
        {
            var currentHero = AIChatClient.CurrentHero;
            if (currentHero == null)
                return "[错误] 无当前领主";
            if (string.IsNullOrEmpty(itemName) || count <= 0)
                return "[错误] 请指定物品名称和数量";

            // 解析目标/部队/物品校验在主线程执行（游戏对象主线程独占）。目标是 NPC 时直接划转完成。
            var resolution = MainThreadExecutor.RunOnMainThread(() =>
            {
                var myParty = currentHero.PartyBelongedTo;
                if (myParty == null)
                    return RequestResolution.Error($"[错误] {currentHero.Name} 没有带领部队");

                var target = ResolveTargetHero(targetEntityId);
                if (target == null)
                    return RequestResolution.Error("[错误] 未找到目标实体：" + targetEntityId);
                if (target == currentHero)
                    return RequestResolution.Error("[错误] 不能向自己要物品");

                if (target != Hero.MainHero)
                {
                    var targetParty = target.PartyBelongedTo;
                    if (targetParty == null)
                        return RequestResolution.Error($"[错误] {target.Name} 没有带领部队");

                    foreach (var ie in targetParty.ItemRoster)
                    {
                        var item = ie.EquipmentElement.Item;
                        if (item == null) continue;
                        var name = item.Name?.ToString() ?? "";
                        if (!name.Contains(itemName) && !itemName.Contains(name)) continue;
                        if (ie.Amount < count)
                            return RequestResolution.Error($"[错误] {target.Name} 只有 {ie.Amount} 个 {name}");

                        targetParty.ItemRoster.AddToCounts(item, -count);
                        myParty.ItemRoster.AddToCounts(item, count);
                        return RequestResolution.Done($"{target.Name} 给出了 {count} 个 {name}。");
                    }
                    return RequestResolution.Error($"[未找到] {target.Name} 身上没有 \"{itemName}\"。");
                }

                // 玩家目标：只检查"对方（玩家）"是否持有该物品——原实现还检查请求方自己的部队，
                // 导致物品在请求方背包时 hasItem=true 弹出确认框，但回调只搜玩家背包 → 假成功。
                var hasItem = false;
                foreach (var ie in target.PartyBelongedTo?.ItemRoster ?? Enumerable.Empty<ItemRosterElement>())
                {
                    var item = ie.EquipmentElement.Item;
                    if (item == null) continue;
                    var name = item.Name?.ToString() ?? "";
                    if (name.Contains(itemName) || itemName.Contains(name))
                    {
                        hasItem = true;
                        break;
                    }
                }
                if (!hasItem)
                {
                    var eq = target.BattleEquipment;
                    var eqSlots = new[] { EquipmentIndex.Weapon0, EquipmentIndex.Weapon1, EquipmentIndex.Weapon2, EquipmentIndex.Weapon3,
                        EquipmentIndex.Head, EquipmentIndex.Body, EquipmentIndex.Leg, EquipmentIndex.Gloves, EquipmentIndex.Cape };
                    foreach (var slot in eqSlots)
                    {
                        var elem = eq.GetEquipmentFromSlot(slot);
                        if (elem.Item != null && (elem.Item.Name?.ToString() ?? "").Contains(itemName))
                        {
                            hasItem = true;
                            break;
                        }
                    }
                }
                if (!hasItem)
                    return RequestResolution.Error("[错误] 对方身上没有 \"" + itemName + "\"。");
                return RequestResolution.Popup();
            });

            if (resolution.ErrorMessage != null) return resolution.ErrorMessage;
            if (resolution.DoneMessage != null) return resolution.DoneMessage;

            // 目标是玩家：弹窗等待（弹窗带倒计时，超时自动按拒绝处理）
            using var mre = new ManualResetEventSlim(false);
            var inquiry = new AIChatClient.PendingInquiry
            {
                Hero = currentHero,
                ItemName = itemName,
                ItemCount = count,
                Event = mre,
                Result = false
            };
            AIChatClient.SetPendingInquiry(inquiry);
            mre.Wait(TimeSpan.FromSeconds(AIChatClient.PendingInquiry.PopupWaitSeconds));

            if (inquiry.Result)
                return $"对方同意了，{itemName} 已转移给你。";
            return $"对方拒绝了交出 {itemName}。";
        }

        private static string ExecuteSendLetter(string recipientId, string content)
        {
            if (string.IsNullOrEmpty(recipientId)) return "[错误] 请提供收信人实体 ID 或名称";
            if (string.IsNullOrEmpty(content)) return "[错误] 信件内容不能为空";
            if (AIChatClient.CurrentHero == null) return "[错误] 无当前领主";
            if (AIChatClient.CurrentHero.IsPrisoner) return "[错误] 你正在被俘虏，无法发信";
            if (AIChatClient.CurrentHero.IsFugitive) return "[错误] 你正在逃亡中，无法发信";
            var senderEntity = EntityManager.GetOrCreateEntity(AIChatClient.CurrentHero);
            var resolvedId = EntityManager.ResolveEntityId(recipientId);
            if (resolvedId == null) return $"[错误] 未找到名为 \"{recipientId}\" 的实体";

            if (AIChatClient.CurrentHero == Hero.MainHero)
            {
                var known = SubModule.GetKnownNpcIds();
                if (!known.Contains(resolvedId))
                    return $"[错误] 你还没有和 {recipientId} 交谈过，无法给陌生人写信。请先与对方进行 AI 聊天。";
            }
            var recipientEntity = EntityManager.GetEntityById(resolvedId);
            var recipientName = recipientEntity?.Name ?? resolvedId;
            var recipientHero = recipientEntity?.HeroRef;
            if (recipientHero != null)
            {
                if (recipientHero.IsPrisoner) return $"[错误] {recipientName} 正在被俘虏，无法收信";
                if (recipientHero.IsFugitive) return $"[错误] {recipientName} 正在逃亡中，无法收信";
                if (recipientHero.IsDisabled) return $"[错误] {recipientName} 处于不可用状态，无法收信";
            }
            // 统一线程模型：信件写入双方的书信往来线程（chat_logs），不再投递 mailbox。
            // send 时即落线程——即使事件被深度上限丢弃，信件也不会丢（与写信窗口行为一致，收信方处理时去重）。
            var recipientIsPlayer = recipientHero != null && recipientHero == Hero.MainHero;
            AgentManager.StoreLetterInThread(senderEntity.Id, resolvedId, content, recipientIsPlayer);

            if (recipientIsPlayer)
                SubModule.MarkNpcKnown(senderEntity.Id); // NPC 主动给玩家写信 → 玩家 O 面板可见

            // 玩家自己发信时推进已读水位，避免把自己刚写的信算成未读
            if (AIChatClient.CurrentHero == Hero.MainHero && !recipientIsPlayer)
            {
                var playerId = EntityManager.GetOrCreateEntity(Hero.MainHero).Id;
                AgentManager.MarkThreadRead(resolvedId, playerId);
            }

            var nextDepth = AgentScheduler.IsProcessing
                ? AgentScheduler.CurrentProcessingDepth + 1
                : 0;

            var delayHours = CalculateLetterDelay(AIChatClient.CurrentHero, recipientEntity?.HeroRef);

            var evt = new ActivationEvent
            {
                Type = ActivationEventType.LetterReceived,
                AgentId = resolvedId,
                TargetId = senderEntity.Id,
                Content = content,
                Depth = nextDepth
            };

            if (delayHours > 0.1f)
                AgentScheduler.QueueDelayedEvent(evt, delayHours);
            else
                AgentScheduler.QueueEvent(evt);

            var delayNote = delayHours > 0.1f ? $"（预计{delayHours:F0}小时后送达）" : "";
            InformationManager.DisplayMessage(new InformationMessage(
                $"{senderEntity.Name} 给 {recipientName} 写了一封信{delayNote}",
                Colors.Cyan));

            return $"信件已发送给 {recipientName}。{delayNote}";
        }

        private static string ExecuteSubmitAdvisory(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return "[错误] 谏言内容不能为空";
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (string.IsNullOrEmpty(PromptManager.CampaignDir))
                return "[错误] 战役目录未就绪";

            var hero = AIChatClient.CurrentHero;
            var kingdom = hero.MapFaction as Kingdom;
            if (kingdom == null)
                return "[错误] 你不属于任何王国，无法进谏";
            if (hero.Clan?.IsUnderMercenaryService == true)
                return "[错误] 雇佣兵无权进谏";

            var kingdomName = kingdom.Name.ToString();
            var currentYear = CampaignTime.Now.GetYear;
            var currentTime = PromptManager.GetCurrentTimeString();

            var entity = EntityManager.GetOrCreateEntity(hero);
            var name = entity?.Name ?? hero.Name?.ToString() ?? "?";
            var title = entity?.Title ?? "?";

            var advisoryDir = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "advisory");
            Directory.CreateDirectory(advisoryDir);
            var advisoryFile = Path.Combine(advisoryDir, $"{kingdomName}_{currentYear}.txt");

            var header = $"\n[{currentTime}] {name}（{title}）谏言：\n";
            File.AppendAllText(advisoryFile, header, Encoding.UTF8);
            File.AppendAllText(advisoryFile, content.Trim() + "\n", Encoding.UTF8);

            return "谏言已提交归档。";
        }

        /// <summary>秘密谏言：只呈本国王，不入史册（史官无权读取）。写 World/secret_advisory/{王国}_{年}.txt。</summary>
        private static string ExecuteSubmitSecretAdvisory(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return "[错误] 秘密谏言内容不能为空";
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (string.IsNullOrEmpty(PromptManager.CampaignDir))
                return "[错误] 战役目录未就绪";

            var hero = AIChatClient.CurrentHero;
            var kingdom = hero.MapFaction as Kingdom;
            if (kingdom == null)
                return "[错误] 你不属于任何王国，无法进谏";
            if (hero.Clan?.IsUnderMercenaryService == true)
                return "[错误] 雇佣兵无权进谏";

            var kingdomName = kingdom.Name.ToString();
            var currentYear = CampaignTime.Now.GetYear;
            var currentTime = PromptManager.GetCurrentTimeString();

            var entity = EntityManager.GetOrCreateEntity(hero);
            var name = entity?.Name ?? hero.Name?.ToString() ?? "?";
            var title = entity?.Title ?? "?";

            var secretDir = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "secret_advisory");
            Directory.CreateDirectory(secretDir);
            var secretFile = Path.Combine(secretDir, $"{kingdomName}_{currentYear}.txt");

            var header = $"\n[{currentTime}] {name}（{title}）密陈：\n";
            File.AppendAllText(secretFile, header, Encoding.UTF8);
            File.AppendAllText(secretFile, content.Trim() + "\n", Encoding.UTF8);

            return "秘密谏言已密陈给国王。";
        }

        /// <summary>国王诏令：只有王国统治者可颁布。公开归档 World/edict/{王国}_{年}.txt，史官可读、本国史书可见。</summary>
        private static string ExecuteSubmitEdict(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return "[错误] 诏令内容不能为空";
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (string.IsNullOrEmpty(PromptManager.CampaignDir))
                return "[错误] 战役目录未就绪";

            var hero = AIChatClient.CurrentHero;
            var kingdom = hero.MapFaction as Kingdom;
            if (kingdom == null)
                return "[错误] 你不属于任何王国，无法颁布诏令";
            if (kingdom.RulingClan?.Leader != hero)
                return "[错误] 只有王国统治者才能颁布诏令";

            var kingdomName = kingdom.Name.ToString();
            var currentYear = CampaignTime.Now.GetYear;
            var currentTime = PromptManager.GetCurrentTimeString();

            var entity = EntityManager.GetOrCreateEntity(hero);
            var name = entity?.Name ?? hero.Name?.ToString() ?? "?";
            var title = entity?.Title ?? "?";

            var edictFile = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "edict", $"{kingdomName}_{currentYear}.txt");

            var header = $"\n[{currentTime}] {name}（{title}）诏令：\n";
            SafeFileIO.AppendAllText(edictFile, header);
            SafeFileIO.AppendAllText(edictFile, content.Trim() + "\n");

            return "诏令已昭告天下。";
        }

        /// <summary>国王外交问询：遣使问询另一王国国王，激活对方（KingConsult 事件）。问询落盘 World/diplomacy/consults/，史官可读。</summary>
        private static string ExecuteConsultKing(string targetKingdomName, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "[错误] 问询内容不能为空";
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (string.IsNullOrEmpty(PromptManager.CampaignDir))
                return "[错误] 战役目录未就绪";

            var hero = AIChatClient.CurrentHero;
            var myKingdom = hero.MapFaction as Kingdom;
            if (myKingdom == null || myKingdom.RulingClan?.Leader != hero)
                return "[错误] 只有王国统治者才能遣使问询他国";

            var target = DiplomacyService.FindKingdom(targetKingdomName);
            if (target == null)
                return "[错误] 找不到王国：" + targetKingdomName;
            if (target.IsEliminated)
                return "[错误] " + target.Name + " 已灭亡，无处遣使";
            var targetRuler = target.RulingClan?.Leader;
            if (targetRuler == null || !targetRuler.IsAlive)
                return "[错误] " + target.Name + " 国王已亡或空缺，无法问询";
            if (target == myKingdom)
                return "[错误] 你不能问询自己";

            var myName = myKingdom.Name.ToString();
            var targetName = target.Name.ToString();
            var pairKey = BuildPairKey(myName, targetName);

            if (!AgentScheduler.TryConsult(pairKey, out var daysRemaining))
                return $"[冷却] 使者尚未从 {targetName} 归来，需再等 {daysRemaining} 游戏天。你可以先处理其他政务。";

            var currentTime = PromptManager.GetCurrentTimeString();
            var entity = EntityManager.GetOrCreateEntity(hero);
            var name = entity?.Name ?? hero.Name?.ToString() ?? "?";

            var threadPath = GetConsultThreadPath(myName, targetName);
            SafeFileIO.AppendAllText(threadPath, $"[{currentTime}] {name}（{myName}国王）问询：{message.Trim()}\n");

            AgentScheduler.RecordConsult(pairKey);

            var targetEntity = EntityManager.GetOrCreateEntity(targetRuler);
            if (targetEntity == null)
                return "[错误] 无法解析目标国王实体";
            var framed = $"你是{targetName}的至高统治者。{myName}国王{name}遣使问询你：\n「{message.Trim()}」\n\n请决定如何回应——可用 reply_consult 工具答复使者（可据实回答，也可虚与委蛇），或不予理睬。你仍可照常审视本国政务。";
            AgentScheduler.QueueEvent(new ActivationEvent
            {
                Type = ActivationEventType.KingConsult,
                AgentId = targetEntity.Id,
                TargetId = entity.Id,
                Content = framed,
                Depth = 1
            });

            return $"已遣使问询 {targetName}，使者将带回其答复。此问询会作为公开外交记录，史官可查阅。";
        }

        /// <summary>回复外交问询：向另一王国国王回话，落盘到双方问询线程。</summary>
        private static string ExecuteReplyConsult(string targetKingdomName, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "[错误] 答复内容不能为空";
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (string.IsNullOrEmpty(PromptManager.CampaignDir))
                return "[错误] 战役目录未就绪";

            var hero = AIChatClient.CurrentHero;
            var myKingdom = hero.MapFaction as Kingdom;
            if (myKingdom == null || myKingdom.RulingClan?.Leader != hero)
                return "[错误] 只有王国统治者才能回复外交问询";

            var target = DiplomacyService.FindKingdom(targetKingdomName);
            if (target == null)
                return "[错误] 找不到王国：" + targetKingdomName;
            if (target == myKingdom)
                return "[错误] 不能回复自己";

            var myName = myKingdom.Name.ToString();
            var targetName = target.Name.ToString();
            var currentTime = PromptManager.GetCurrentTimeString();
            var entity = EntityManager.GetOrCreateEntity(hero);
            var name = entity?.Name ?? hero.Name?.ToString() ?? "?";

            var threadPath = GetConsultThreadPath(myName, targetName);
            SafeFileIO.AppendAllText(threadPath, $"[{currentTime}] {name}（{myName}国王）答复：{message.Trim()}\n");

            return $"答复已送达 {targetName}。";
        }

        private static string GetConsultThreadPath(string kingdomA, string kingdomB)
        {
            var pair = BuildPairKey(kingdomA, kingdomB);
            var dir = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "diplomacy", "consults");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, pair + ".txt");
        }

        /// <summary>密使线程文件名：实体 ID 排序拼接（{idA}_and_{idB}），与 BuildPairKey 王国名版本区分（实体 ID 不含 "_and_"）。</summary>
        private static string BuildEntityPairKey(string idA, string idB)
        {
            if (string.CompareOrdinal(idA, idB) <= 0)
                return idA + "_and_" + idB;
            return idB + "_and_" + idA;
        }

        private static string GetCorrespondenceThreadPath(string idA, string idB)
        {
            var pair = BuildEntityPairKey(idA, idB);
            var dir = Path.Combine(PromptManager.CampaignDir, "NPCs", "World", "correspondence");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, pair + ".txt");
        }

        private static bool IsClanLeader(Hero hero)
        {
            return hero?.Clan != null && hero.Clan.Leader == hero;
        }

        /// <summary>私有密使（封臣/独立领袖/佣兵/国王互通）：落盘 World/correspondence/，立即激活对方一次，单跳防环，史官不可读。</summary>
        private static string ExecuteSendEnvoy(string targetEntityId, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "[错误] 口信内容不能为空";
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (string.IsNullOrEmpty(PromptManager.CampaignDir))
                return "[错误] 战役目录未就绪";
            if (string.IsNullOrWhiteSpace(targetEntityId))
                return "[错误] 请提供接收密使的家族领袖（实体 ID 或中文名）";

            var hero = AIChatClient.CurrentHero;
            if (!IsClanLeader(hero))
                return "[错误] 只有家族领袖才能派遣密使";
            if (hero.IsPrisoner) return "[错误] 你正在被俘虏，无法遣使";
            if (hero.IsFugitive) return "[错误] 你正在逃亡中，无法遣使";

            var senderEntity = EntityManager.GetOrCreateEntity(hero);
            var resolvedId = EntityManager.ResolveEntityId(targetEntityId);
            if (resolvedId == null) return $"[错误] 未找到名为 \"{targetEntityId}\" 的实体";
            if (resolvedId == senderEntity.Id) return "[错误] 你不能向自己派遣密使";

            var recipientEntity = EntityManager.GetEntityById(resolvedId);
            var recipientHero = recipientEntity?.HeroRef;
            var recipientName = recipientEntity?.Name ?? resolvedId;
            if (recipientHero == null)
                return $"[错误] 无法解析目标 {targetEntityId} 的实体";
            if (!IsClanLeader(recipientHero))
                return $"[错误] {recipientName} 不是家族领袖，无法接收密使";
            if (recipientHero.IsPrisoner) return $"[错误] {recipientName} 正在被俘虏，无法收信";
            if (recipientHero.IsFugitive) return $"[错误] {recipientName} 正在逃亡中，无法收信";
            if (recipientHero.IsDisabled) return $"[错误] {recipientName} 处于不可用状态，无法收信";

            var pairKey = BuildEntityPairKey(senderEntity.Id, resolvedId);
            if (!AgentScheduler.TryEnvoy(pairKey, out var daysRemaining))
                return $"[冷却] 使者尚未从 {recipientName} 处归来，需再等 {daysRemaining} 游戏天。";

            var currentTime = PromptManager.GetCurrentTimeString();
            var name = senderEntity.Name ?? hero.Name?.ToString() ?? "?";
            var title = senderEntity.Title ?? "?";

            var threadPath = GetCorrespondenceThreadPath(senderEntity.Id, resolvedId);
            SafeFileIO.AppendAllText(threadPath, $"[{currentTime}] {name}（{title}）遣使：{message.Trim()}\n");

            AgentScheduler.RecordEnvoy(pairKey);

            var recipientIsPlayer = recipientHero == Hero.MainHero;
            if (recipientIsPlayer)
            {
                SubModule.MarkNpcKnown(senderEntity.Id); // 玩家 O 面板可见该联系人
                MainThreadExecutor.DisplayMessage(new InformationMessage(
                    $"{name} 遣密使来见你，按 O 键书信面板查看并回复。", Colors.Cyan));
            }
            else
            {
                var framed = $"{name}（{title}）遣密使送来私人口信：\n「{message.Trim()}」\n\n这封密使往来仅你二人可知，史官与外人不会知晓。可用 reply_envoy 工具答复（可据实、可虚与委蛇、可置之不理），或先处理自己的事务。";
                AgentScheduler.QueueEvent(new ActivationEvent
                {
                    Type = ActivationEventType.EnvoyReceived,
                    AgentId = resolvedId,
                    TargetId = senderEntity.Id,
                    Content = framed,
                    Depth = 1
                });
            }

            return $"密使已遣往 {recipientName}。此往来仅你二人知晓，不会进入史册。";
        }

        /// <summary>回复私有密使：回写双方密使线程，不激活任何人（发送方下次自省/政务审视时读到）。</summary>
        private static string ExecuteReplyEnvoy(string targetEntityId, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "[错误] 答复内容不能为空";
            if (AIChatClient.CurrentHero == null)
                return "[错误] 无当前领主";
            if (string.IsNullOrEmpty(PromptManager.CampaignDir))
                return "[错误] 战役目录未就绪";
            if (string.IsNullOrWhiteSpace(targetEntityId))
                return "[错误] 请提供密使发送方的家族领袖（实体 ID 或中文名）";

            var hero = AIChatClient.CurrentHero;
            if (!IsClanLeader(hero))
                return "[错误] 只有家族领袖才能回复密使";

            var senderEntity = EntityManager.GetOrCreateEntity(hero);
            var resolvedId = EntityManager.ResolveEntityId(targetEntityId);
            if (resolvedId == null) return $"[错误] 未找到名为 \"{targetEntityId}\" 的实体";
            if (resolvedId == senderEntity.Id) return "[错误] 不能回复自己";

            var currentTime = PromptManager.GetCurrentTimeString();
            var name = senderEntity.Name ?? hero.Name?.ToString() ?? "?";
            var title = senderEntity.Title ?? "?";

            var threadPath = GetCorrespondenceThreadPath(senderEntity.Id, resolvedId);
            SafeFileIO.AppendAllText(threadPath, $"[{currentTime}] {name}（{title}）答复：{message.Trim()}\n");

            return $"答复已密送。";
        }

        private static string BuildPairKey(string kingdomA, string kingdomB)
        {
            if (string.CompareOrdinal(kingdomA, kingdomB) <= 0)
                return kingdomA + "_and_" + kingdomB;
            return kingdomB + "_and_" + kingdomA;
        }

        internal static float CalculateLetterDelay(Hero sender, Hero? recipient)
        {
            if (sender == null || recipient == null) return 0f;
            var senderParty = sender.PartyBelongedTo;
            var recipientParty = recipient.PartyBelongedTo;
            Vec2 senderPos, recipientPos;

            if (senderParty != null)
                senderPos = senderParty.GetPosition2D;
            else if (sender.CurrentSettlement != null)
                senderPos = sender.CurrentSettlement.GetPosition2D;
            else
                return 0f;

            if (recipientParty != null)
                recipientPos = recipientParty.GetPosition2D;
            else if (recipient.CurrentSettlement != null)
                recipientPos = recipient.CurrentSettlement.GetPosition2D;
            else
                return 0f;

            var dist = senderPos.Distance(recipientPos);
            var km = dist / 1000f;
            // 信使送信延时：地图单位尺度偏小（全图约 2-3"公里"），原公式 km/4 几乎恒 <1 被钳到 1h，等于无延时。
            // 改为按距离线性放大（km*4h）+ 最低 3h，让信件往返有可感知的时间差（跨图约半天）。
            return Math.Max(3f, km * 4f);
        }

        private static string ExecuteLetGo()
        {
            var hero = AIChatClient.CurrentHero;
            if (hero == null) return "[错误] 无当前领主";

            var encounter = PlayerEncounter.Current;
            if (encounter == null) return "[错误] 当前没有遭遇战，无法放行";

            var encounteredParty = PlayerEncounter.EncounteredMobileParty;
            if (encounteredParty == null) return "[错误] 没有遭遇方";

            var myParty = hero.PartyBelongedTo;
            if (myParty == null) return "[错误] 你没有带领部队";

            // 修复：PlayerEncounter.EncounteredMobileParty 恒为"非玩家一侧"（即 NPC 自己的部队），
            // 原判断 `encounteredParty.LeaderHero != MainHero` 恒真导致永远报"对方不是玩家"。
            // 正确语义：遭遇对象是自己的部队即代表对方是玩家，允许放行。
            if (encounteredParty != myParty)
                return "[错误] 遭遇对象不是你的部队，此工具仅供对玩家放行使用";

            PlayerEncounter.LeaveEncounter = true;

            foreach (var party in MobileParty.All)
            {
                if (party != encounteredParty && party.MapFaction == myParty.MapFaction)
                {
                    if (party.Ai != null)
                        party.Ai.SetDoNotAttackMainParty(12);
                }
            }

            if (myParty.Ai != null)
                myParty.Ai.SetDoNotAttackMainParty(12);

            MobileParty.MainParty.IgnoreForHours(6f);

            return $"你决定放{encounteredParty.LeaderHero?.Name?.ToString() ?? "玩家"}一马。对方已安全离开，你的部队短时间内不会再次追击。";
        }

        /// <summary>按名称在部队俘虏名册中匹配俘虏（支持中英文名，双向包含）。</summary>
    }
}
