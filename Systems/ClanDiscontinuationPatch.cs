namespace AIChronicle
{
    /// <summary>
    /// 独立家族永续（MCM「独立家族永不失踪」控制，默认开）：
    /// 禁用原版 FactionDiscontinuationCampaignBehavior 的「无王国独立家族 28 天后自动灭族」逻辑。
    /// 灭国幸存者、自愿离国者、丢光封地者一律永久保留——契合模组「封臣自省 / 玩家劝降」设计，
    /// 这些独立氏族正是自省与劝降的主要对象，不应被原版计时器抹掉。
    /// 补丁在 OnGameStart 手动注册（CampaignBehaviors 类 PatchAll 会静默跳过，见 SubModule）。
    /// </summary>
    public static class ClanDiscontinuationPatch
    {
        /// <summary>拦截 DiscontinueClan（私有）：设置开启时跳过 DestroyClanAction，独立家族永不灭族。</summary>
        public static bool DiscontinueClanPrefix()
        {
            return MySettings.Instance?.DisableClanDiscontinuation != true;
        }
    }
}
