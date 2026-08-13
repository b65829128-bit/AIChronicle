# Harmony 补丁与 BLSource 参考（AI 参考文档）

> 本文件是 `AGENTS.md` 的补充参考，整理 Harmony 补丁的完整模式示例与 BLSource 搜索指南。写新补丁或查游戏逻辑时用 `read` 按需查阅。两个**致命坑**（PatchAll 静默跳过、`__result` 类型）在 `AGENTS.md` 硬规则里已常驻，这里展开完整模式。

## 1. 新建 Harmony 补丁

在项目目录下创建新 `.cs` 文件（如 `Patches\MyNewPatch.cs`），格式如下：

```csharp
using HarmonyLib;

namespace AIChronicle
{
    // Prefix: 在原方法执行前拦截，可修改参数或跳过原方法
    [HarmonyPatch(typeof(TargetGameClass), "TargetMethodName")]
    public static class MyPrefixPatch
    {
        public static bool Prefix(ref int someParameter)
        {
            someParameter *= 2;  // 修改参数
            return true;         // true=继续执行原方法, false=跳过
        }
    }

    // Postfix: 在原方法执行后拦截，可修改返回值
    [HarmonyPatch(typeof(TargetGameClass), "TargetMethodName")]
    public static class MyPostfixPatch
    {
        public static void Postfix(ref float __result)
        {
            __result *= 2f;  // 修改返回值
        }
    }
}
```

补丁在 `SubModule.OnSubModuleLoad()` 中通过 `harmony.PatchAll()` 自动激活。

> **⚠️ 重要发现（v0.5）**：`harmony.PatchAll()` 在 `OnSubModuleLoad` 时执行，此时部分游戏类型（特别是 `TaleWorlds.CampaignSystem.CampaignBehaviors` 命名空间下的类，如 `KingdomDecisionProposalBehavior`）**尚未完成运行时初始化**，导致这些 `[HarmonyPatch]` 属性静默跳过——补丁既不生效也不报错。**解决方案**：对于这类补丁，在 `OnGameStart` 中用 `Type.GetType("FullName, Assembly")` 获取类型，再用 `harmony.Patch(method, prefix/postfix)` **手动注册**。之前多位 agent 尝试修复原版外交拦截失败，根因均在此。
>
> **⚠️ 重要发现（v1.1，PatchAll 中止之谜）**：`DoubleRenownPatch`（双倍声望）曾写 `Postfix(ref float __result)`，但 `DefaultBattleRewardModel.CalculateRenownGain` 返回 `ExplainedNumber`——**`__result` 参数类型必须与原方法返回类型完全一致**，否则 `PatchAll()` 抛 `HarmonyException` 并**中止其后所有 `[HarmonyPatch]` 补丁的注册**（这是"补丁静默丢失"的另一个根因）。已修复为 `ref TaleWorlds.CampaignSystem.ExplainedNumber __result`（双倍用 `__result.Add(__result.ResultNumber)`）。

## 2. 访问私有字段（三个下划线前缀）

```csharp
[HarmonyPatch(typeof(SomeClass), "SomeMethod")]
public static void Postfix(SomeClass __instance, MBList<Something> ____privateFieldName)
{
    // __instance = 被补丁的对象实例
    // ____privateFieldName = 私有字段（Harmony 自动注入，命名规则：_ + 字段名）
}
```

## 3. 访问方法的 ref 参数

```csharp
[HarmonyPatch(typeof(SomeClass), "SomeMethod")]
public static void Prefix(ref int parameterName, ref ExplainedNumber __result)
{
    // ref 参数直接修改
    // __result 对应方法的 ref 返回值
}
```

## 4. 获取方法的参数值

```csharp
[HarmonyPatch(typeof(SomeClass), "SomeMethod")]
public static void Postfix(int param1, float param2)
{
    // 参数名必须与原方法参数名一致（不区分大小写）
    // Harmony 自动传递原方法实参值到你的补丁
}
```

## 5. 访问游戏内的游戏数据

```csharp
// 获取玩家氏族
Clan playerClan = Clan.PlayerClan;

// 获取所有王国
foreach (Kingdom kingdom in Kingdom.All) { ... }

// 获取所有定居点
foreach (Settlement settlement in Settlement.All) { ... }

// 获取当前战役时间
float days = CampaignTime.Now.ToDays();

// 显示消息
InformationManager.DisplayMessage(new InformationMessage("Hello", Colors.Green));
```

## 6. 使用 BLSource 搜索游戏逻辑

BLSource 包含完整的反编译游戏源码（6288 个 .cs 文件），供 AI 搜索和理解游戏内部实现。常用搜索路径：

| 要找的内容 | 搜索目录 |
|-----------|---------|
| 战役行为/事件 | `BLSource/TaleWorlds.CampaignSystem/` |
| 游戏模型（经济/战斗/经验） | `BLSource/TaleWorlds.CampaignSystem/GameComponents/` |
| UI 与界面 | `BLSource/TaleWorlds.GauntletUI/` |
| 物品/装备/武器 | `BLSource/TaleWorlds.Core/` |
| 英雄/角色/NPC | `BLSource/TaleWorlds.CampaignSystem/` (Hero, CharacterObject) |
| 任务/剧情 | `BLSource/TaleWorlds.CampaignSystem/` (Quest, StoryMode) |
| 地图/定居点 | `BLSource/TaleWorlds.CampaignSystem/` (Settlement, MapScene) |
| 存档系统 | `BLSource/TaleWorlds.SaveSystem/` |
| 基础类型（MBList等） | `BLSource/TaleWorlds.Library/` |
| 本地化/翻译 | `BLSource/TaleWorlds.Localization/` |

**使用方式**：向 AI 提问时附带 "在 BLSource 中搜索..."，例如：
- "在 BLSource/TaleWorlds.CampaignSystem/GameComponents 中找 DefaultClanFinanceModel，告诉我每日收入的计算逻辑"
- "在 BLSource 中找 Tournament 相关的类，列出所有可覆写的虚拟方法"

## 7. 创建新模组

```powershell
cd C:\Users\<用户名>\BLMods
dotnet new blmodfx --name "新模组名"
```
