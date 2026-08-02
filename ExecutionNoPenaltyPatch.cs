namespace MyFirstMod
{
    /// <summary>
    /// 处决无惩罚（MCM「处决无惩罚」控制，默认开）：
    /// 禁用处决贵族带来的荣誉与好感代价，玩家与 NPC 均生效，配合家族补充系统让世界经得起大屠杀。
    /// 两个补丁在 OnGameStart 手动注册（参照 KDPB/册封补丁模式，避免 PatchAll 静默跳过）。
    /// </summary>
    public static class ExecutionNoPenaltyPatch
    {
        /// <summary>原版玩家处决的名誉惩罚（-1000 荣誉经验）入口：关闭时 no-op。</summary>
        public static bool OnLordExecutedPrefix()
        {
            return MySettings.Instance?.ExecutionNoPenalty != true;
        }

        /// <summary>原版/模组处决关系惩罚模型：关闭时一律返回 0（受害者氏族/亲友/同阵营贵族不再降好感）。</summary>
        public static bool GetRelationChangeForExecutingHeroPrefix(ref int __result, out bool showQuickNotification)
        {
            showQuickNotification = false;
            if (MySettings.Instance?.ExecutionNoPenalty == true)
            {
                __result = 0;
                return false;
            }
            return true;
        }
    }
}
