using System.Text;
using TaleWorlds.CampaignSystem;

namespace AIChronicle
{
    public static partial class ContextBuilder
    {
        // 每次组装读取实时对象；不沿用 Entity.Title、persona 或旧聊天中的政治身份。
        private static IdentityFacts CaptureIdentity(Hero hero)
        {
            var clan = hero.Clan;
            var kingdom = hero.MapFaction as Kingdom;
            var ruler = kingdom?.RulingClan?.Leader;
            return new IdentityFacts
            {
                Name = hero.Name?.ToString() ?? "未知",
                IsFemale = hero.IsFemale,
                ClanName = clan?.Name?.ToString() ?? "",
                IsClanLeader = clan?.Leader == hero,
                IsLord = hero.IsLord,
                KingdomName = kingdom == null ? "" : kingdom.Name?.ToString() ?? "未知王国",
                IsRuler = ruler == hero,
                IsUnderMercenaryService = clan?.IsUnderMercenaryService == true,
                RulerName = ruler?.Name?.ToString() ?? "",
                RulerIsFemale = ruler?.IsFemale
            };
        }

        internal static string BuildObjectiveRelationship(Hero agentHero, Hero targetHero)
        {
            var sb = new StringBuilder();
            var agentFacts = CaptureIdentity(agentHero);
            var targetFacts = agentHero == targetHero ? null : CaptureIdentity(targetHero);
            sb.AppendLine(IdentityFacts.DescribePair(agentFacts, targetFacts));
            var agentFaction = agentHero.MapFaction;
            var targetFaction = targetHero.MapFaction;

            if (agentHero == targetHero)
            {
                sb.AppendLine("对方就是你自己（自省）。");
                return sb.ToString().TrimEnd();
            }

            if (agentFaction != null && targetFaction != null)
            {
                if (agentFaction == targetFaction)
                {
                    if (agentFaction.IsKingdomFaction)
                    {
                        var kingdom = agentFaction as Kingdom;
                        var agentIsMerc = agentHero.Clan?.IsUnderMercenaryService == true;
                        var targetIsMerc = targetHero.Clan?.IsUnderMercenaryService == true;

                        if (kingdom != null && agentHero == kingdom.Leader)
                        {
                            var desc = targetIsMerc ? "雇佣兵首领" : "封臣";
                            sb.AppendLine($"对方与你同属{agentFaction.Name}。你是该王国的君主，对方是你的{desc}。");
                        }
                        else if (kingdom != null && targetHero == kingdom.Leader)
                        {
                            var desc = agentIsMerc ? "雇佣兵首领" : "封臣";
                            sb.AppendLine($"对方与你同属{targetFaction.Name}。对方是你的君主，你是对方的{desc}。");
                        }
                        else if (agentHero.Clan == targetHero.Clan)
                            sb.AppendLine($"对方与你同属{agentHero.Clan?.Name?.ToString() ?? "同一家族"}。你们是同一家族的成员。");
                        else if (agentIsMerc && targetIsMerc)
                            sb.AppendLine($"对方与你同属{agentFaction.Name}。你们都是该王国麾下的雇佣兵。");
                        else if (agentIsMerc)
                            sb.AppendLine($"对方与你同属{agentFaction.Name}。对方是该王国的封臣，你是雇佣兵。");
                        else if (targetIsMerc)
                            sb.AppendLine($"对方与你同属{agentFaction.Name}。对方是雇佣兵，你是该王国的封臣。");
                        else
                            sb.AppendLine($"对方与你同属{agentFaction.Name}。你们同为该王国的封臣。");
                    }
                    else if (agentFaction.IsMinorFaction)
                    {
                        sb.AppendLine($"对方与你同属{agentFaction.Name}（佣兵势力）。");
                    }
                    else
                    {
                        sb.AppendLine($"对方与你同属{agentFaction.Name}。");
                    }
                }
                else
                {
                    if (agentFaction.IsAtWarWith(targetFaction))
                        sb.AppendLine($"对方属于{targetFaction.Name}，与你的势力{agentFaction.Name}【处于交战状态】。你们是敌对关系。");
                    else
                        sb.AppendLine($"对方属于{targetFaction.Name}，与你的势力{agentFaction.Name}【当前和平】。你们是中立关系。");
                }
            }
            else if (agentFaction != null && targetFaction == null)
            {
                sb.AppendLine("对方不属于任何势力（可能是流浪者或平民）。");
            }
            else if (agentFaction == null && targetFaction != null)
            {
                sb.AppendLine($"你目前不属于任何势力，对方属于{targetFaction.Name}。");
            }

            if (agentHero.Spouse == targetHero)
                sb.AppendLine("对方是你的配偶。");
            else if (agentHero.Mother == targetHero)
                sb.AppendLine("对方是你的母亲。");
            else if (agentHero.Father == targetHero)
                sb.AppendLine("对方是你的父亲。");
            else if (agentHero.Clan != null && targetHero.Clan != null
                && agentHero.Clan == targetHero.Clan
                && agentHero != targetHero)
            {
                var relation = "";
                if (agentHero.Mother == targetHero.Mother) relation = "（同母）";
                else if (agentHero.Father == targetHero.Father) relation = "（同父）";
                sb.AppendLine($"对方与你是同族亲属{relation}。");
            }

            if (targetHero.IsPrisoner)
                sb.AppendLine("注意：对方当前是俘虏。");

            return sb.ToString().TrimEnd();
        }
    }
}
