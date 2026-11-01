using TaleWorlds.CampaignSystem;

namespace AIChronicle
{
    /// <summary>
    /// 朝议启用时，接管原版影响力投票：
    /// - 政策决策 / 封地归属决策 IsAllowed() = false；
    /// - 政策、封地、驱逐的原生 UI 按钮禁用，Execute 方法也直接跳过；
    /// - 防止旧存档里已存在的政策/封地表决继续走原版流程。
    /// 所有补丁在 OnGameStart 手动注册（ViewModelCollection 不在编译引用里，Election 类
    /// 在 OnSubModuleLoad 时也未初始化，PatchAll 会静默跳过）。
    /// </summary>
    public static class VanillaDecisionBanPatch
    {
        /// <summary>用于 IsAllowed / CanXxx 属性 getter：朝议启用时返回 false。</summary>
        public static bool BlockBoolResult(ref bool __result)
        {
            if (CuriaService.Enabled)
            {
                __result = false;
                return false;
            }
            return true;
        }

        /// <summary>用于 ExecuteXxx 命令：朝议启用时跳过原版决策创建。</summary>
        public static bool BlockExecute()
        {
            return !CuriaService.Enabled;
        }
    }
}
