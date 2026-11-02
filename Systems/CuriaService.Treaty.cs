using System;
using TaleWorlds.CampaignSystem;

namespace AIChronicle
{
    public static partial class CuriaService
    {
        /// <summary>
        /// 朝议提议阶段的条约条款校验：战前条约必须有条款；附条件议和可空。
        /// 成功时 finalized 为归一化后的条款（无条件议和为 null）。
        /// </summary>
        private static bool TryResolveTreatyTerms(Kingdom kingdom, Kingdom target, CuriaAgendaType type,
            Hero proposer, TreatyTerms? input, out TreatyTerms? finalized, out string error)
        {
            finalized = null;
            error = "";
            if (type == CuriaAgendaType.ProposeTreaty)
            {
                if (input == null)
                {
                    error = "战前条约缺少条款：请指定割地或赔款";
                    return false;
                }
                var validation = TreatyService.Validate(kingdom, target, input, preWar: true, proposer);
                if (!validation.Ok)
                {
                    error = validation.Error;
                    return false;
                }
                finalized = validation.Terms;
                return true;
            }

            if (type == CuriaAgendaType.ProposePeace && input != null && input.HasAnyTerm())
            {
                var validation = TreatyService.Validate(kingdom, target, input, preWar: false, proposer);
                if (!validation.Ok)
                {
                    error = validation.Error;
                    return false;
                }
                finalized = validation.Terms;
            }
            return true;
        }
    }
}
