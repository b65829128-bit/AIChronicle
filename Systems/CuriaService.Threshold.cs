using TaleWorlds.CampaignSystem;

namespace AIChronicle
{
    public static partial class CuriaService
    {
        /// <summary>朝议门槛：政策/封地/背盟各自门槛，条约按是否含割地选择战/和或封地门槛。</summary>
        private static float GetThreshold(Kingdom? kingdom, CuriaAgendaType type, bool hasCession = false)
        {
            var s = MySettings.Instance;
            float value;
            if (type == CuriaAgendaType.EnactPolicy || type == CuriaAgendaType.RevokePolicy)
                value = s?.CuriaPolicyThreshold ?? 0.5f;
            else if (type == CuriaAgendaType.TransferFief || type == CuriaAgendaType.ExpelClan
                     || type == CuriaAgendaType.BreakAlliance)
                value = s?.CuriaFiefThreshold ?? 0.66f;
            else if (type == CuriaAgendaType.ProposePeace || type == CuriaAgendaType.ProposeTreaty)
                value = hasCession ? (s?.CuriaFiefThreshold ?? 0.66f) : (s?.CuriaWarThreshold ?? 0.5f);
            else
                value = s?.CuriaWarThreshold ?? 0.5f;
            return CuriaPolicyEffects.AdjustThreshold(kingdom, type, value, hasCession);
        }
    }
}
