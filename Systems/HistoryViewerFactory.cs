using TaleWorlds.CampaignSystem;

namespace AIChronicle
{
    /// <summary>
    /// 从当前战役读取史册权限视角。放在独立文件里，让 HistoryArchive 保持无 TaleWorlds 依赖，
    /// 纯逻辑测试无需游戏 DLL 即可运行。
    /// </summary>
    public static class HistoryViewerFactory
    {
        public static HistoryViewer ForCurrentGame()
        {
            if (Campaign.Current == null)
                return HistoryViewer.Empty;

            var clan = Clan.PlayerClan;
            if (clan == null)
                return HistoryViewer.Empty;

            var kingdom = clan.Kingdom;
            bool isMercenary = clan.IsUnderMercenaryService;
            bool isRuler = kingdom != null && Hero.MainHero != null &&
                            kingdom.RulingClan?.Leader == Hero.MainHero;
            return new HistoryViewer(kingdom?.Name?.ToString(), isMercenary, isRuler);
        }
    }
}
