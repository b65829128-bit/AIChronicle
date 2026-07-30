# MyFirstMod — Bannerlord 模组开发文档

## 文档维护规则（AI 必读）

> ⚠️ **最重要的规则：每次修改代码后，必须立即检查并更新文档！不更新文档视为未完成工作。**

你有权修改本文档（AGENTS.md），但必须遵循以下条件：

1. **修改前，必须向用户（yangui）明确说明**：
   - 你要改哪一章节
   - 为什么需要改（例如：新增了项目依赖、发现更好的模式、目录结构变了）
   - 具体的改动内容（用 diff 形式或文字描述）
2. **得到用户明确同意后，才能执行修改**。
3. **修改完成后，口头告知用户改动已完成**。

此规则适用于本文档（AGENTS.md）以及 **README_MOD.md**（模组功能文档），不适用于普通代码文件（代码的增删改按正常开发流程，无需额外审批）。

### 必须更新文档的情况（强制）

以下任一情况发生，**必须在代码修改完成后立即更新 README_MOD.md 和/或 AGENTS.md**，不得等到用户提醒：

| 代码变更 | 需要更新的文档 |
|---------|--------------|
| 新增/删除功能 | README_MOD.md（功能描述、使用方法） |
| 新增/修改 MCM 配置项 | README_MOD.md（设置面板表格） |
| 新增/删除/重命名文件 | README_MOD.md（文件结构）、AGENTS.md（目录结构） |
| 改变架构或入口模式 | AGENTS.md（对应章节） |
| 新增依赖或 NuGet 包 | AGENTS.md（环境概况） |
| 改变默认值或行为 | README_MOD.md（对应功能描述） |
| 新增 UI 界面或交互流程 | README_MOD.md（功能描述、使用方法） |
| 修改 API 调用方式或参数 | README_MOD.md 和 AGENTS.md（如有相关说明） |

**禁止行为：** 完成代码变更后直接结束，等待用户提醒才去更新文档。文档维护是开发工作的一部分，不是可选的。

### 代码修改后文档自检清单（每轮必须过一遍）

代码修改完成后，对照以下清单逐项检查：

```
[ ] 新增/删除了文件？          → 更新 README_MOD.md 文件结构 + AGENTS.md 目录结构
[ ] 新增/修改了功能？          → 更新 README_MOD.md 功能描述/使用方法
[ ] 改变了 MCM 配置项？        → 更新 README_MOD.md 设置面板表格
[ ] 改变了架构或入口模式？     → 更新 AGENTS.md 对应章节
[ ] 新增了 NuGet 包或依赖？    → 更新 AGENTS.md 环境概况
[ ] 修改了默认值或行为？       → 更新 README_MOD.md 对应描述
[ ] 新增了 UI 或交互流程？     → 更新 README_MOD.md 功能描述 + 使用方法
```

**全通过才算完成工作。**

**交叉参考：** 在实现具体功能前，先读 **README_MOD.md** 了解当前模组有哪些功能、UI 入口在哪、配置项有哪些。不要仅凭 AGENTS.md 做技术决策——两份文档一起看。

---

## 开发工作流改进（AI 必读，从实际踩坑中总结）

### 1. 需求先对齐，再动手

涉及 **UI 交互入口**、**功能行为**、**用户体验** 的改动，先列出多种方案让用户选择，**不要自行判断哪个更好**。

> 反例：AI 自行判断「遭遇菜单入口比对话入口更合理」并直接实现，结果用户想要恰恰相反。
> 正例：先问「AI 聊天入口放在遭遇菜单还是对话选项中？各有什么利弊」，等用户回复后再编码。

### 2. 在关键路径上预先埋日志，不要失败后再猜

涉及 **新 UI、新屏幕、异步操作、跨系统调用** 的功能，在以下节点预埋 `InformationManager.DisplayMessage`：
- 入口函数被调用时（参数打印出来）
- 关键操作前后（如 `LoadMovie`、`AddLayer`）
- 异常捕获处

这样第一次出问题就能直接定位，不必反复打补丁。

### 3. 写 GauntletUI 前，先拆解游戏已有的同类 Prefab

游戏自带大量 GauntletUI XML 文件（`Modules/Native/GUI/Prefabs/`、`Modules/Multiplayer/GUI/Prefabs/`）。实现类似功能时：
1. 找到游戏里**行为最接近**的现有 Prefab（如聊天 → SPChatLog.xml，弹窗 → FullScreenNotice.xml）
2. 逐段分析它的 Widget 层级、`DoNotAcceptEvents` 逻辑、`InputRestrictions` 设置
3. 基于现有模板修改，不要从零写

> 典型踩坑：`DoNotAcceptEvents="true"` 的含义是「本 Widget 不接事件，但透传给子级」——这个语义如果不先看 SPChatLog 源码，纯靠试错至少浪费两轮。

---

## 架构概览（必读）

### 核心理念

本模组采用 **Agent 驱动架构**，受 opencode 设计启发。不是"玩家 ↔ LLM 对话"，而是：

```
Entity（玩家/NPC，统一抽象）
      ↕
Context Builder（动态组装：身份 + 记忆 + 目标 + 感知 + 可用工具）
      ↕
Agent 核心（读文件、做决策、写回、调游戏函数）
      ↕
文件系统（每个 Entity 的独立目录 = 结构化记忆）
```

Agent 不区分玩家和 NPC——玩家只是一个 Controller 类型为 Human 的 Entity。

### 关键设计

| 原则 | 说明 |
|------|------|
| **Entity 平等** | 玩家和所有 NPC 统一为 Entity，由 EntityController（Human/Agent）区分 |
| **动态 Context** | ContextBuilder 根据当前交互双方动态组装系统提示词 |
| **能力过滤** | 每个 Entity 有 EntityCapability 集合，工具列表按能力自动过滤 |
| **书信模式** | 支持书信 intent，O 键唤起收信人列表 |
| **文件即知识库** | NPC 的记忆、目标、对目标的认知都是文件，Agent 通过 `read_file`/`write_file`/`append_file`/`edit_file`/`delete_file`/`move_file` 精确读写 |
| **信息隔离** | 每个 NPC 只能操作自己目录下的文件 + `World/`，不知道其他 NPC 和玩家的对话 |
| **工具定义文件化** | 52 个工具定义在 `tools.json`（42 个游戏工具）和 `agent_tools.json`（10 个文件工具）中，热重载，不硬编码 |
| **工具分类系统** | 每个工具归属 8 个分类之一（universal/query/social/movement/military/diplomacy/file/communication），Agent 按场景默认激活相关分类，需要其他分类时调用 `browse_tools` 元工具按需解锁 |
| **提示词全部可编辑** | `system_prompt.txt`、`agent_system.txt`、`tool_call_prompt.txt`、`persona_generation.txt`、`context_template.txt`、`chancery_rules.txt` 均为文件，战役创建时自动复制到战役目录，热重载优先读战役目录 |
| **多轮工具调用** | `SendMessage` 内建 SSE 流式循环（max N 轮或无限），模型调用工具 → 执行 → 追加结果 → 重请求 |
| **秘书处** | M 键打开，玩家的个人行政助手。固定 persona（无条件服从），不读玩家 persona。国王/封臣/平民均可使用，可用工具取决于玩家当前身份 |
| **提示词人称统一** | 上下文只出现「你」(Agent 自己) 和「对方」(交互对象) 两角色，"TA"等模糊指代全部禁用 |

### 两个模型

| 模型 | 用途 | 提示词来源 |
|------|------|-----------|
| Agent 模型 | 思考、读写文件、调工具、决定回什么 | `context_template.txt` → ContextBuilder 动态组装 |
| 前台模型 | 把 Agent 给的上下文变成自然对话 | 极简，不带工具，只做角色扮演 |

### 信息隔离规则（硬约束）

| 当前 NPC 可访问 | 权限 |
|---------------|------|
| `NPCs/{自己}/persona.txt` | 只读 |
| `NPCs/{自己}/character.json` | 只读 |
| `NPCs/{自己}/knowledge/{entity_id}.txt` | 读 + 写 + 追加 + 编辑 + 删除 |
| `NPCs/{自己}/relationships/{其他}.txt` | 读 + 写 + 追加 + 编辑 + 删除 |
| `NPCs/{自己}/goals/*.txt` | 读 + 写 + 追加 + 编辑 + 删除 |
| `NPCs/{自己}/chat_logs/*.txt` | 只读（不可修改） |
| `NPCs/{自己}/decisions/*.txt` | 读 + 写 + 追加 + 编辑 + 删除 |
| `NPCs/{自己}/mailbox/**` | 读 + 写 + 追加 + 编辑 + 删除 |
| `World/factions.txt` | 只读 |
| `World/settlements.txt` | 只读 |
| 其他 NPC 的任何文件 | **禁止** |

### 扩展方式

新工具需要以下步骤：

1. `tools.json` 或 `agent_tools.json` 中加一条工具定义，**必须指定 `category`**（遵循 opencode 风格的 `description` 格式）
2. `ToolExecutor.ExecuteToolCall` 的 `switch` 中加一个 `case`
3. 如果工具涉及部队行为（移动、劫掠、围攻等），还需：
   - 在 `Execute*` 方法中注册 `PendingAction`（通过 `PartyBehaviorManager.GetOrCreateAction` 设置 `Behavior` 和目标）
   - 在 `PartyBehaviorManager.Tick()` 的 switch 中添加对应行为的恢复/清理逻辑
   - 对于持续性行为（驻防、巡逻、护送），设置 `CheckInHours` 以启用定时签到
4. `AIChatScreenVM` 的 toolCall 显示 switch 中加对应描述
5. `ContextBuilder.CapabilityToolMap` 中按能力映射（若需能力过滤）

`SubModule.OnApplicationTick` 已配置为每帧调用 `PartyBehaviorManager.Tick()`。

#### 新增 Entity 类型

1. 在 `Entity.cs` 的 `EntityCapability` 枚举中添加新能力
2. 在 `EntityManager.ComputeCapabilities()` 中为新 Entity 类型计算能力集合
3. 在 `ContextBuilder` 的 `CapabilityToolMap` 中建立能力→工具映射
4. 在 `tools.json` 的 `parameters.properties` 中添加 `capability_required` 字段

### 提示词设计规范（人称约定）

所有提示词文件必须遵守统一的人称约定，防止 Agent 混淆自己与对方的信息：

- 「你」= Agent 自己（如"你是拉盖娅，现任南帝国女皇"）
- 「对方」= 正在交互的目标 Entity（如"对方是炎萌"）
- 「该人物」= `query_character` 返回结果的前缀（如"该人物：拉盖娅"）
- 禁止出现「TA」、「他/她」、「其」等模糊指代

此约定适用于 `context_template.txt`、`agent_system.txt`、`tool_call_prompt.txt` 以及所有新增的工具返回格式。

---

## 环境概况

| 项目 | 值 |
|------|-----|
| 游戏 | Mount & Blade II: Bannerlord v1.4.7 |
| 游戏路径 | `D:\steam\steamapps\common\Mount & Blade II Bannerlord` |
| IDE | JetBrains Rider 2026.2 |
| 开发目录 | `C:\Users\yangui\BLMods\MyFirstMod` |
| .NET SDK | 9.0+ |
| 目标框架 | net472 (Windows) + net6 (Xbox/Store) |
| 模组框架 | Harmony 2.3.3 (运行时补丁) |
| 四前置 | Harmony, ButterLib, UIExtenderEx, MCM (MBOptionScreen) |

## 目录结构

```
C:\Users\yangui\BLMods\MyFirstMod\
├── SubModule.cs              ← 模组入口，生命周期回调
├── MyFirstMod.csproj          ← 项目配置（引用、框架、部署）
├── Settings.cs                ← MCM 设置（API URL、Model、Key、双倍声望开关）
├── AIChatClient.cs            ← LLM API 客户端（HTTP 流式请求、SSE 解析、多轮循环），工具调用委托给 ToolExecutor
├── ToolExecutor.cs            ← 工具执行器（30+ 个游戏工具的具体实现 + browse_tools 元工具）
├── DiplomacyService.cs        ← 外交服务（宣战/议和/结盟/贸易协定/回复提案 + FindKingdom）
├── PartyBehaviorManager.cs    ← 部队行为管理器（PendingAction 状态机、Tick()、定时签到）
├── AIChatScreen.cs            ← 聊天屏幕管理器（静态类，GauntletLayer 挂载）
├── AIChatScreenVM.cs          ← 聊天 ViewModel（消息列表、输入绑定、function calling 处理）
├── LordChatBehavior.cs        ← 对话中插入聊天选项，战役 ID 管理
├── PromptManager.cs           ← 提示词管理器（文件热重载、战役目录、角色 JSON）
├── AgentManager.cs            ← Agent 管理器（NPC 文件系统、路径权限、LLM 生成 persona）
├── Entity.cs                  ← Entity 抽象（玩家/NPC 统一模型、EntityCapability）
├── EntityManager.cs           ← Entity 生命周期管理
├── ContextBuilder.cs          ← Context 动态组装器
├── LetterListScreen.cs        ← 书信收信人列表屏幕
├── AgentScheduler.cs          ← 信件异步事件驱动调度器
├── HistoryRecorder.cs         ← 历史记录器（监听游戏事件写入原始史料）
├── _Module/
│   ├── SubModule.xml         ← 模组元数据（ID、依赖、DLL路径）
│   ├── GUI/
│   │   └── Prefabs/
│   │       ├── AIChatScreen.xml  ← 聊天窗口布局
│   │       └── LetterListScreen.xml ← 书信收信人列表
│   └── Prompts/
│       ├── system_prompt.txt  ← 系统提示词模板（玩家可编辑，热重载）
│       ├── world_info.txt     ← 默认世界背景
│       ├── tools.json         ← 游戏工具定义（热重载）
│       ├── agent_system.txt   ← Agent 系统提示词模板
│       ├── agent_tools.json   ← Agent 文件工具定义（热重载）
│       ├── tool_call_prompt.txt ← 独立工具调用代理提示词（热重载）
│       ├── persona_generation.txt ← NPC性格生成提示词（玩家可编辑，热重载）
│       ├── Templates/         ← NPC 目录模板
│       │   ├── context_template.txt ← Context 模板
│       └── Campaigns/         ← 各战役目录（运行时创建）
│           └── {战役名}/
│               ├── system_prompt.txt    ← 本战役系统提示词（可独立编辑，热重载）
│               ├── world_info.txt       ← 本战役世界背景（可独立编辑，热重载）
│               ├── agent_system.txt     ← 本战役 Agent 提示词（热重载）
│               ├── tool_call_prompt.txt ← 本战役工具调用提示词（热重载）
│               ├── persona_generation.txt ← 本战役性格生成提示词（热重载）
│               ├── context_template.txt ← 本战役 Context 模板（热重载）
│               └── NPCs/          ← Agent 管理的 NPC 文件系统
│                   └── {entity_id}/                 ← {Name}_{StringId}（如 博泰罗_CharacterObject_1664）
│                       ├── persona.txt   ← [MOTIVATION]/[TRAITS]/[SPEECH_STYLE]
│                       ├── persona_meta.json ← 自定义人格维度（权力欲/归属重心/冒险倾向）
│                       ├── knowledge/
│                       ├── chat_logs/
│                       ├── mailbox/
│                       │   ├── inbox/
│                       │   └── outbox/
│                       ├── relationships/
│                       ├── goals/
│                       └── decisions/
├── BLSource/                 ← 反编译的游戏源码（AI 只读，不参与编译）
│   ├── TaleWorlds.CampaignSystem/   ← 战役逻辑
│   ├── TaleWorlds.Core/             ← 核心类型
│   ├── TaleWorlds.MountAndBlade/    ← 引擎层
│   ├── TaleWorlds.Library/          ← 基础库（MBList, InformationMessage 等）
│   ├── TaleWorlds.Localization/     ← 本地化
│   ├── TaleWorlds.GauntletUI/       ← UI 框架
│   ├── TaleWorlds.Engine.GauntletUI/  ← GauntletLayer
│   ├── TaleWorlds.ScreenSystem/     ← ScreenBase, ScreenManager
│   └── ...（共 50+ 个 DLL 的反编译源码）
└── bin/Release/net472/       ← 编译产物
```

部署后自动复制到：`D:\...\Modules\MyFirstMod\bin\Win64_Shipping_Client\MyFirstMod.dll`

## 开发工作流

### 每次修改代码后

```powershell
# 1. 编译 + 自动部署（必须设置环境变量）
$env:BANNERLORD_GAME_DIR = "D:\steam\steamapps\common\Mount & Blade II Bannerlord"
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
cd C:\Users\yangui\BLMods\MyFirstMod
dotnet build -c Release

# 2. 启动游戏测试
Start-Process "D:\steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client\Bannerlord.exe"

# 3. 在启动器中勾选 MyFirstMod → Play
```

> ⚠️ 定期执行 `dotnet clean -c Release && dotnet build -c Release` 做全量编译。增量编译可能掩盖文件损坏（如 IDE 后台索引/保存过程中的异常写入），clean build 才能暴露真实编译错误。

### 版本管理（Git）

项目使用 Git 进行版本管理。仓库已初始化在 `C:\Users\yangui\BLMods\MyFirstMod\`。

**提交前检查：**
```powershell
$env:Path = "C:\Program Files\Git\bin;" + [Environment]::GetEnvironmentVariable("Path","Machine")
git status
git diff
```

**提交：**
```powershell
git add -A
git commit -m "描述你的改动"
```

**`.gitignore` 排除项：**
| 目录/文件 | 原因 |
|-----------|------|
| `bin/` `obj/` | 编译产物 |
| `BLSource/` | 反编译的游戏源码（5332 文件，只读参考） |
| `_Module/Prompts/Campaigns/` | 运行时生成的战役存档数据 |
| `.idea/` `.vscode/` `Thumbs.db` | IDE 和系统文件 |

### 常见操作：新建 Harmony 补丁

在项目目录下创建新 `.cs` 文件（如 `Patches\MyNewPatch.cs`），格式如下：

```csharp
using HarmonyLib;

namespace MyFirstMod
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

### 常见操作：访问游戏内的游戏数据

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

## Harmony 补丁核心模式

### 访问私有字段（三个下划线前缀）

```csharp
[HarmonyPatch(typeof(SomeClass), "SomeMethod")]
public static void Postfix(SomeClass __instance, MBList<Something> ____privateFieldName)
{
    // __instance = 被补丁的对象实例
    // ____privateFieldName = 私有字段（Harmony 自动注入，命名规则：_ + 字段名）
}
```

### 访问方法的 ref 参数

```csharp
[HarmonyPatch(typeof(SomeClass), "SomeMethod")]
public static void Prefix(ref int parameterName, ref ExplainedNumber __result)
{
    // ref 参数直接修改
    // __result 对应方法的 ref 返回值
}
```

### 获取方法的参数值

```csharp
[HarmonyPatch(typeof(SomeClass), "SomeMethod")]
public static void Postfix(int param1, float param2)
{
    // 参数名必须与原方法参数名一致（不区分大小写）
    // Harmony 自动传递原方法实参值到你的补丁
}
```

## 使用 BLSource 搜索游戏逻辑

BLSource 包含完整的反编译游戏源码（5332 个 .cs 文件），供 AI 搜索和理解游戏内部实现。常用搜索路径：

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

## 模组类型与入口模式

Bannerlord 支持以下几种模组入口方式，本模组使用 **MBSubModuleBase**：

| 方式 | 用途 | 生命周期 |
|------|------|---------|
| `MBSubModuleBase` | 通用入口（本模组使用） | OnSubModuleLoad → OnGameStart → OnApplicationTick... |
| `CampaignBehaviorBase` | 战役专属行为 | RegisterEvents → 各种 CampaignEvents 回调 |
| `MissionBehavior` | 场景/战斗内行为 | OnMissionTick → 各种 Mission 事件 |
| `MissionView` | 场景/战斗内 UI | 类似 MissionBehavior 但有 UI 渲染回调 |

SubModule.cs 中的生命周期回调：

```csharp
OnSubModuleLoad()           // 模组被游戏加载时（最早时机, 激活Harmony）
OnBeforeInitialModuleScreenSetAsRoot()  // 主菜单显示前
OnGameStart(Game, IGameStarter)         // 新游戏/载入存档时
OnApplicationTick(float dt)             // 每帧调用
OnGameEnd(Game)                          // 游戏结束时
OnSubModuleUnloaded()                    // 模组被卸载时
```

## UI 开发（GauntletUI）

Bannerlord 使用自研的 GauntletUI 框架。UI 开发分两层：

1. **XML 布局**（Widget/Prefab）：定义界面结构和样式
2. **C# ViewModel**：定义数据绑定和行为逻辑

本模组如果涉及 UI，参考 `BLSource/TaleWorlds.GauntletUI/` 和 `BLSource/TaleWorlds.Engine.GauntletUI/`。

## AI 交互架构（function calling）

### 设计原则

本模组使用 OpenAI 的 **function calling（工具调用）** 机制处理 AI 与游戏世界的交互。当前可用工具包括：认知更新、定居点查询、世界状态查询、NPC 行军移动、等待停留、修改好感度、赠送/索要金币。未来所有 AI-世界交互都应基于此机制扩展。

### 为什么用 function calling 而不是文本标记

- 模型在训练阶段学过 function calling 协议，遵从度远高于自创的文本标记格式
- 结构化输出（JSON schema）比自由文本解析更可靠
- 文本输出和工具调用是分离通道，不会互相干扰
- 弱模型也能稳定遵循 function calling 协议

### 工具描述设计规范（参考 opencode 模式）

**核心原则：工具调用行为完全由函数 `description` 字段驱动，不在系统提示词中重复写工具指令。**

系统提示词只负责角色扮演和行为风格。函数描述承担全部工具调用逻辑。这样做的好处：
- 模型对工具定义通道的注意力与文本生成通道独立，不会因角色扮演而遗忘工具调用
- description 遵循固定结构化格式，弱模型也能稳定解析

**description 必须遵循以下格式：**

```
一句话总结工具功能。

Usage:
- 触发条件（Call this function whenever / after every...）
- 操作要求（Record / Capture / Note what the other person...）
- 参数说明（The xxx parameter should be...）
- 禁止条件（Do NOT call this function if...）
- 附加说明（This function is for system use only...）
```

**示例（`update_knowledge`）：**

```
Record new information learned about the other person in this conversation turn.

Usage:
- Call this function whenever the other person reveals or claims information
  about themselves: their identity, background, intentions, position, experiences,
  or opinions.
- Record what they claimed even if you are uncertain or skeptical.
- The knowledge parameter must be a single concise sentence summarizing the
  new information.
- Do NOT call this function if no new information was revealed in this turn.
- This function is for system use only; the other person does not see its output.
```

**注意事项：**
- 使用英文编写 description（模型对英文工具定义的训练量远大于中文）
- 每条规则独立成行，以动词开头（Call / Record / Do NOT call）
- 触发条件要显式枚举"whenever"的情况，减少模型判断负担
- 类型说明（"This function is for system use only..."）

**弱模型加强：在系统提示词末尾加一句工具提醒**

虽然工具调用主要由 `description` 驱动，但对于较弱模型（如 DeepSeek-V4-Flash），角色扮演任务可能完全吞没工具调用意愿。此时在系统提示词末尾加一句极其简短的提醒（不超过一行），能有效提高弱模型的工具遵从率：

```
你有 update_knowledge 工具可用——当对方透露新信息时使用它来记录。
```

这条提醒不应超过一行，不展开规则（规则已在 description 中写清楚），避免和角色扮演抢注意力。

### 工具调用状态提示（强制规则）

**所有 function calling 调用都必须通过 `InformationManager.DisplayMessage` 向玩家显式反馈结果。** 玩家有权知道 AI 做出了什么行动。

| 调用结果 | 提示内容 | 颜色 |
|---------|---------|------|
| 知识更新成功 | `{领主名} 更新了对你的认知` | Cyan |
| 知识更新失败 | `{领主名} 知识更新失败：{原因}` | Red |
| NPC 开始移动 | `{领主名} 决定前往{目的地}` | Cyan |
| NPC 移动失败 | `{领主名} 移动失败：{原因}` | Red |
| NPC 等待结束 | `{领主名} 结束了在{定居点}的停留` | Cyan |
| NPC 修改好感 | `{领主名} 对玩家的好感变化了{+n/-n}点` | Cyan |
| NPC 赠送金币 | `{领主名} 赠予了你 {n} 金币` | Cyan |
| NPC 索要金币 | （弹出确认对话框，无自动提示） | — |

实现位置：`AIChatScreenVM.ExecuteSend` 中解析 `ChatResponse.ToolCalls`（未来会扩展为复数工具调用）的循环中。

**注意：** 玩家长时间聊天时，屏幕左下角的信息弹窗是有限的。如果消息太多会被新消息顶掉，建议在未来的 UI 升级中考虑将状态提示显示在聊天窗口内部。

### 当前实现

`AIChatClient.cs` 中定义了所有 23 个工具（15 个游戏工具 + 8 个文件/通信工具）。每次 API 请求都附带能力过滤后的工具定义。模型自行判断是否需要调用。返回的 `ChatResponse` 包含：
- `Content`：角色扮演文本回复
- `LearnedKnowledge`：如果模型调用了 `update_knowledge`，这里包含新认知
- `ToolCalls`：原始工具调用数据（用于构建反馈闭环）
- `ToolResults`：工具执行结果，追加到历史形成反馈闭环

### 工具调用模式

本模组支持两种工具调用模式，通过 MCM 中的「独立工具调用」开关控制：

**正常模式（默认，关闭）：**
角色扮演和工具调用在同一次 API 请求中完成。模型同时输出文本回复和 tool_calls。
- 对强模型效果好，延迟低，token 消耗少
- 弱模型可能在角色扮演中遗忘工具调用

**独立模式（开启「独立工具调用」）：**
```
第 1 次 API 请求：纯角色扮演（不含 tools）
第 2 次 API 请求：纯工具决策（opencode 风格提示词，专门判断是否调用工具）
```
- `AIChatClient.EvaluateToolCalls()` 负责第 2 次请求
- 系统提示词极简（"Only call functions or stay silent. Do NOT roleplay."）
- 延迟和 token 消耗翻倍，但对弱模型更可靠

### 工具调用反馈闭环

所有 assistant 消息如果包含 tool_calls，会在聊天历史中追加对应的 `role: "tool"` 确认消息。这样模型在下一轮对话中看到自己的工具调用被处理，形成正反馈循环。这是方案二的实现。

### 扩展方向（未来）

基于同一套 `tools` 机制可以扩展：
- ~~领主决定去某个领地 → `travel_to(settlement)`~~ （已实现为 `move_to_settlement` + `wait_at_settlement`）
- ~~给玩家物品/金钱 → `give_item(item, amount)`~~ （已实现为 `give_gold` + `request_gold`，支持 target_entity_id）
- ~~改变对目标的态度 → `change_relation(delta)`~~ （已实现，支持 target_entity_id）
- ~~领主军事行动 → 劫掠/围攻/追击/驻防/巡逻/护送/绕行~~ （已全部实现为 7 个部队 AI 工具 + 中断恢复 + 定时签到）
- ~~文件编辑 → `write_file`/`edit_file`/`delete_file`/`glob`~~ （已实现，chat_logs 和 persona.txt 为只读保护）
- ~~发起外交/王国级动作~~ → `declare_war` / `propose_peace` / `propose_alliance` / `propose_trade` / `gift_fief`（已全部实现）
- 物品交易 → `give_item` / `give_item` / `request_items`（已全部实现）
- ~~氏族阵营切换~~ → `change_kingdom`（已实现，支持离国/加入/叛逃/雇佣兵四种模式）
- NPC 自立建国 → `create_kingdom`（待开发）<br>
  `ChangeKingdomAction.ApplyByCreateKingdom(Clan, Kingdom)` 后端方法已存在，接受任意氏族。<br>
  阻碍：① `Kingdom` 对象构造函数不在反编译源码中，可能需要反射或查找静态工厂；<br>
  ② `DefaultKingdomCreationModel` 全部方法硬编码为 `IsPlayerKingdomCreationPossible`（只检 `Hero.MainHero`），需替换或 patch。<br>
  条件门槛（四级家族+有封地+100兵）本身合理，移植给 NPC 直接可用。

### 流式架构（已实现，参考 opencode）

使用 SSE（Server-Sent Events）流式连接，参考 opencode 的 processor 架构：
- 模型在一个持续流中边输出文本边调工具
- 工具结果实时返回，流持续到模型自然 finish
- 无轮次上限（由 `MaxAgentRounds` 或「不限制」模式控制），模型自主决定何时停止

实现要点：
- HTTP 请求 payload 中 `stream: true`
- 用 `HttpCompletionOption.ResponseHeadersRead` 获取流式响应
- 逐行解析 `data:` 前缀的 SSE 事件
- 文本增量（delta）累积成完整回复
- `reasoning_content` delta 跨 chunk 累积（DeepSeek 默认思考模式开启，思维链内容需捕获并在工具调用轮次中回传）
- tool_calls delta 跨 chunk 累积（DeepSeek 协议中 tool_call 分多次 delta 传输）

### 信件激活机制（AgentScheduler）

信件系统采用异步事件驱动模型：

```
send_letter → StoreOutgoingLetter(文件) → AgentScheduler.QueueEvent(LetterReceived)
                                                      ↓
OnApplicationTick → AgentScheduler.Tick() → 取出1个事件 → Task.Run异步处理
```

- 每帧消费一个激活事件（`ConcurrentQueue`，线程安全）
- `ActivationEvent.Depth` 控制级联深度，MCM 可调（默认 5）
- 支持三种事件类型：`LetterReceived`（来信）、`BehaviorCheckIn`（定时签到）、`KingDiplomacy`（国王外交审视）
- 被俘/逃亡的国王统治者现在也会被激活（仅跳过已死亡和 null 的），`BuildSelfStatus` 中会提示"你仍是王国统治者"
- 玩家可见：左下角弹 `xxx 给 xxx 写了一封信` / `xxx 正在思考下一步行动...` / `xxx 正在处理外交事务...`
- 防递归：书信规则强调"除非必要不回信" + 深度硬上限
- 聊天记录使用显式路径（`GetChatLogPathFor`）防线程竞态
- 外交提案感知：`LetterReceived` 处理时自动检测双方是否有待处理的外交提案（`AgentManager.GetProposalsBetween`），如有则将提案摘要注入上下文提示 Agent

### 历史系统（HistoryRecorder + 史官 Agent）

历史系统由两部分组成：**事件记录器** 和 **史官 Agent**。

#### 事件记录器（HistoryRecorder）

`HistoryRecorder.cs` 是一个 `CampaignBehavior`，在 `OnGameStart` 中注册。它监听以下 `CampaignEvents`：

| 事件 | 游戏钩子 | 史料 type |
|------|---------|-----------|
| 宣战 | `WarDeclared` | `war_declared` |
| 议和 | `MakePeace` | `peace_made` |
| 城镇/城堡易主 | `OnSettlementOwnerChangedEvent`（过滤 IsTown/IsCastle） | `settlement_captured` |
| 王国灭亡 | `KingdomDestroyedEvent` | `kingdom_destroyed` |
| 新王国建立 | `OnKingdomCreatedEvent`（反射注册） | `kingdom_created` |
| 贵族死亡 | `HeroKilledEvent`（过滤有 clan 的） | `hero_killed` |
| 氏族叛变 | `OnClanChangedKingdomEvent` | `clan_changed_kingdom` |
| 贵族婚嫁 | `MarriageOfferedToPlayerEvent` 等（反射注册） | `marriage` |

每条事件以 JSONL 格式追加到 `World/history/events_{year}.txt`：
```json
{"year":1084,"season":"春","day":12,"type":"war_declared","summary":"瓦兰迪亚向库赛特宣战"}
```

#### 史官 Agent

- **触发时机**：每年年终（年份推进时），`AgentScheduler.CheckYearAdvance()` 检测年份变化并队列 `YearlyChronicle` 事件
- **专题触发**：灭国/新王国建立时，`HistoryRecorder` 调用 `AgentScheduler.QueueSpecialChronicle()` 即时队列 `SpecialChronicle` 事件
- **Entity**：史官是虚拟 Entity（ID: `__historian__`，`HeroRef = null`），不映射任何游戏 NPC
- **工具**：`query` + `file` 分类的工具（`ActivatedCategories = {"universal", "query", "file"}`）
- **权限**：可读 `World/history/` 目录，可写 `World/history/chronicles/` 目录
- **提示词**：使用 `intent = "historian"` 的 `ContextBuilder.Build()`，规则来自 `historian_rules.txt`
- **输出**：年度编年史 → `World/history/chronicles/chronicle_{year}.txt`；专题史 → 自命名文件

#### NPC 查阅历史

- `AgentManager.IsPathAllowed` 和 `ResolvePath` 新增了对 `history/` 和 `history/chronicles/` 路径的支持
- NPC Agent 可用 `read_file("history/chronicles/chronicle_1084.txt")` 直接读取史官成文
- 原始史料（`events_*.txt`）对 NPC 只读
- 写入历史目录的权限保留给 `__historian__` entity

#### 年份检测

- `AgentScheduler` 用 `_lastChronicleYear` 追踪上次处理年份，初始值 = 游戏起始年份
- 每帧 `Tick()` 中调用 `CheckYearAdvance()`，检测 `currentYear > _lastChronicleYear`
- 对每个已跳过的年份，检查 `events_{year}.txt` 是否存在且 `chronicle_{year}.txt` 不存在，满足条件才队列事件
- 防止重复生成：已存在编年史的年份跳过

## 调试方法

1. **Rider 附加进程调试**：
   - 启动游戏
   - Rider → Run → Attach to Process → Bannerlord.exe
   - 设置断点，触发你的代码时会中断

2. **日志调试**（最简单）：
   ```csharp
   InformationManager.DisplayMessage(new InformationMessage($"Debug: {value}", Colors.Red));
   ```

3. **dnSpy 调试**：打开 `C:\Users\yangui\Tools\dnSpy\dnSpy-net-win64\dnSpy.exe`，附加到 Bannerlord 进程，可在任意游戏方法上设断点

## 注意事项

- **运行 `dotnet build` 前必须设置 `$env:BANNERLORD_GAME_DIR`**，否则找不到游戏 DLL
- BLSource 虽然编译时被排除，但**不能被删除**——AI 需要它来理解游戏逻辑
- Harmony 补丁中的私有字段名必须与原始 DLL 中的字段名**完全一致**（用 dnSpy 可查看）
- 模组的 SubModule.xml 中 `Id` 和 `Name` 默认等于项目名（`$(MSBuildProjectName)`）
- 如果补丁不生效，检查：1) 方法名是否正确 2) 参数类型是否匹配 3) 是否有重载冲突（aka Ambiguous match）

## 工具清单

### 开发工具

| 工具 | 路径 | 用途 |
|------|------|------|
| dnSpy GUI | `C:\Users\yangui\Tools\dnSpy\dnSpy-net-win64\dnSpy.exe` | 反编译、调试、查看游戏源码 |
| dnSpy CLI | `C:\Users\yangui\Tools\dnSpy\dnSpy-net-win64\dnSpy.Console.exe` | 批量反编译（命令行） |
| dotnet CLI | `dotnet` | 编译、创建新项目 |
| Rider | `C:\Program Files\JetBrains\JetBrains Rider 2026.2\bin\rider64.exe` | IDE |

### 游戏工具（tools.json，41 个）

| 工具 | 类别 | 说明 |
|------|------|------|
| `query_clan_fiefs` | 查询 | 查询氏族持有的封地列表 |
| `query_character` | 查询 | 查询人物公开档案（身份/家族/王国/兵力/位置），系统权威数据 |
| `query_settlement` | 查询 | 查询定居点信息（所有者/繁荣度/类型） |
| `query_settlement_geography` | 查询 | 查询定居点地理情报（位置/周边邻居/边境标签） |
| `query_world_state` | 查询 | 获取世界局势（王国兵力/交战状态） |
| `query_recent_events` | 查询 | 查询人物近期事件（比武/俘虏/婚嫁/阵亡等百科记录） |
| `query_surroundings` | 查询 | 扫描周围环境（当前位置、附近城镇城堡、附近部队及阵营关系） |
| `update_knowledge` | 认知 | 记录关于对方的新认知 |
| `change_relation` | 关系 | 修改对任意人物的好感度（支持 target_entity_id） |
| `give_gold` | 经济 | 赠予任意人物金币（支持 target_entity_id） |
| `request_gold` | 经济 | 向任意人物索要金币（玩家需确认，NPC 自动划转） |
| `move_to_settlement` | 行军 | 部队行军到城镇/城堡/村庄（支持 activate:true 参数自动唤醒） |
| `wait_at_settlement` | 行军 | 在定居点停留指定时长（支持 activate:true 参数到期自动唤醒） |
| `raid_settlement` | 军事 | 劫掠村庄 |
| `besiege_settlement` | 军事 | 围攻城镇/城堡 |
| `engage_party` | 军事 | 追击并攻击另一支部队 |
| `defend_settlement` | 军事 | 驻防守卫定居点（持续性，72h 签到） |
| `patrol_settlement` | 军事 | 巡逻定居点周边（持续性，48h 签到） |
| `escort_party` | 军事 | 护送跟随另一支部队（持续性，24h 签到） |
| `go_around_party` | 行军 | 绕行回避某支部队 |
| `query_war_status` | 查询 | 查询王国战争统计（双方阵亡/攻城/劫掠数） |
| `query_pending_proposals` | 查询 | 列出当前王国待处理的外交提案（无需参数，自动按当前 Entity 过滤） |
| `declare_war` | 外交 | 向另一王国宣战（单向，国王专属） |
| `propose_peace` | 外交 | 向另一王国提议议和（双向，附赔偿方案，国王专属） |
| `propose_alliance` | 外交 | 向另一王国提议结盟（双向，国王专属） |
| `propose_trade` | 外交 | 向另一王国提议贸易协定（双向，国王专属） |
| `respond_to_diplomacy_proposal` | 外交 | 接受或拒绝收到的外交提案（国王专属） |
| `gift_fief` | 外交 | 国王敕令将封地直接转让给指定封臣家族领袖（国王专属，不经过选举） |
| `cancel_action` | 控制 | 取消当前任务，回归自主 AI |
| `query_party_troops` | 查询 | 查看部队详情（金币/兵力/各兵种经验升级路径/俘虏/物品栏/装备栏） |
| `query_available_troops` | 查询 | 查看当前定居点可招募兵种（需在定居点内） |
| `query_settlement_villages` | 查询 | 查看城镇/城堡的附属村庄列表 |
| `query_hero_skills` | 查询 | 查询人物 18 个技能等级和 6 个属性值 |
| `recruit_troops` | 军事 | 从当前定居点招募指定兵种（扣金币，需在定居点内） |
| `upgrade_troops` | 军事 | 升级兵种（检查经验/金币/装备/perk） |
| `buy_food` | 行军 | 在定居点买粮到够吃 N 天（自动挑最便宜的） |
| `give_item` | 社交 | 将自己物品/装备交给任意人物 |
| `request_items` | 社交 | 向任意人物索要物品（NPC 直接划转，玩家弹确认框） |

### 文件工具（agent_tools.json，10 个）

| 工具 | 说明 |
|------|------|
| `read_file` | 读取文件内容（支持行号范围） |
| `write_file` | 创建新文件或完整重写 |
| `append_file` | 追加内容到文件末尾 |
| `edit_file` | 精确替换文件中的文本（必须唯一匹配） |
| `delete_file` | 删除文件 |
| `move_file` | 移动/重命名文件（如标记计划完成） |
| `list_dir` | 列出目录内容 |
| `glob` | 按文件名模式匹配（如 `knowledge/*.txt`） |
| `grep` | 按关键词搜索文件内容 |
| `send_letter` | 给其他 Entity 写信 |

## 故障排查

### 日志文件位置

| 日志 | 路径 | 内容 |
|------|------|------|
| 游戏引擎日志 | `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_*.txt` | 模组加载顺序、DLL 加载、资源扫描 |
| 游戏错误日志 | `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_errors_*.txt` | 引擎级错误和警告 |
| 看门狗/崩溃日志 | `C:\ProgramData\Mount and Blade II Bannerlord\logs\watchdog_log_*.txt` | 崩溃时的异常码和堆栈 |
| ButterLib 日志 | `%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\butterlib*.txt` | ButterLib 加载状态和模块级异常 |
| 默认模组日志 | `%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\default*.log` | 模组标准日志输出 |
| 崩溃 Dump | `C:\ProgramData\Mount and Blade II Bannerlord\crashes\` | 崩溃时生成的 .dmp 文件 |

### 查看最新日志的命令

```powershell
# 查看最新的游戏引擎日志（按时间倒序）
Get-ChildItem "C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object { Get-Content $_.FullName }

# 查看最新的 ButterLib 日志
Get-ChildItem "$env:USERPROFILE\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\butterlib*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object { Get-Content $_.FullName }

# 搜索所有日志中的错误关键词
Get-ChildItem "C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object { Get-Content $_.FullName | Select-String "Error|Exception|crash|Null|MyFirstMod" }
```

### 生命周期与崩溃陷阱

Bannerlord 的模组加载有严格的初始化顺序。**在错误的阶段调用 UI/游戏系统 API 会直接崩溃：**

```
游戏启动 → 加载所有 DLL → OnSubModuleLoad()       ← 只能做 Harmony.PatchAll()，不能碰 UI
        → 初始化渲染 → 加载资源 →
        → OnBeforeInitialModuleScreenSetAsRoot()  ← UI 系统就绪，可以调用 DisplayMessage
        → 主菜单显示 →
        → 新游戏/读档 → OnGameStart()             ← 战役系统就绪
```

| 阶段 | 可以做什么 | 不能做什么 |
|------|-----------|-----------|
| `OnSubModuleLoad` | `Harmony.PatchAll()`（仅对已完成的类型生效）、初始化纯数据结构 | 调用 `InformationManager`、访问 `Campaign`、打补丁到未初始化的类型 |
| `OnBeforeInitialModuleScreenSetAsRoot` | 显示欢迎消息、修改主菜单 | 访问战役数据（还没进游戏） |
| `OnGameStart` | 注册 CampaignBehavior、显示消息、访问战役数据、**用 Type.GetType + harmony.Patch 手动补丁未初始化的类型** | - |

**常见崩溃码：**

| 异常码 | 含义 |
|--------|------|
| `0xE0434352` | .NET 未处理异常（最常见，通常伴随 ButterLib 弹窗显示具体错误） |
| `0xC0000005` | 内存访问违规（C++ 层崩溃，可能和 Native DLL 相关） |

### ButterLib 异常弹窗

当模组抛出未处理异常时，ButterLib 会拦截并弹出一个红色窗口显示堆栈信息。**截图这个窗口是最直接的排查方式**。弹窗信息也会同时写入 `ModLogs\butterlib*.txt`。

### 排查步骤（模组不工作或崩溃时）

1. 启动游戏，勾选你的模组，如果崩溃 → 截图 ButterLib 弹窗
2. 查看 `butterlib*.txt` 最新日志中的 `[ERR]` 行
3. 查看 `watchdog_log_*.txt` 中的异常码
4. 检查 Harmony 补丁是否存在 **Ambiguous match**（重载冲突），参考 [Harmony 章节](#harmony-补丁核心模式)
5. 确认代码中是否在 `OnSubModuleLoad` 中调用了 UI/游戏系统 API

## 创建新模组

```powershell
cd C:\Users\yangui\BLMods
dotnet new blmodfx --name "新模组名"
```
