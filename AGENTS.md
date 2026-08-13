# AGENTS.md — AI编年史·言出法随 开发入口

> 本文件是 AI 开发入口（面向 DSH / opencode 等 agent，也面向人类贡献者）。**常驻硬规则与速览在此**；详细机制、工具清单、调试方法、Harmony 模式等见 `docs/`（用 `read` 工具按需查阅，不要凭记忆）。
>
> 交叉参考：实现功能前先读 `README_MOD.md`（模组能做什么），再读本文件（怎么做）。两份文档互为补充。

## 0. 文档维护规则（最重要，AI 必读）

**每次修改代码后，必须立即检查并更新文档！不更新文档视为未完成工作。**

| 代码变更 | 需要更新的文档 |
|---------|--------------|
| 新增/删除功能 | README_MOD.md（功能描述、使用方法） |
| 新增/修改 MCM 配置项 | README_MOD.md（设置面板表格） |
| 新增/删除/重命名文件 | README_MOD.md（文件结构）、AGENTS.md（目录结构） |
| 改变架构或入口模式 | AGENTS.md（对应章节） |
| 新增依赖或 NuGet 包 | AGENTS.md（环境概况） |
| 改变默认值或行为 | README_MOD.md（对应功能描述） |
| 新增 UI 界面或交互流程 | README_MOD.md（功能描述、使用方法） |

**修改 `AGENTS.md` 或 `README_MOD.md` 前，必须先向用户说明改动并征得同意。** 代码文件的增删改走正常开发流程，无需额外审批。

**代码修改后文档自检清单（每轮必过）：**

```
[ ] 新增/删除了文件？          → 更新 README_MOD.md 文件结构 + AGENTS.md 目录结构
[ ] 新增/修改了功能？          → 更新 README_MOD.md 功能描述/使用方法
[ ] 改变了 MCM 配置项？        → 更新 README_MOD.md 设置面板表格
[ ] 改变了架构或入口模式？     → 更新 AGENTS.md 对应章节
[ ] 新增了 NuGet 包或依赖？    → 更新 AGENTS.md 环境概况
[ ] 修改了默认值或行为？       → 更新 README_MOD.md 对应描述
[ ] 新增了 UI 或交互流程？     → 更新 README_MOD.md 功能描述 + 使用方法
```

全通过才算完成工作。

## 1. 路径规则

文件操作可用**相对路径**（工具默认解析到项目根目录）或**绝对路径**均可。不要用 `/c/...` 形式（Windows 下无效）。

## 2. 硬规则（不可违反）

### 2.1 工具定义同步

`tools.json` / `agent_tools.json` 与 `ToolExecutor.ExecuteToolCall` 的 switch **必须同步**。新增工具：定义 → case → 显示映射（详见 `docs/architecture.md` 第 7 节「扩展方式」）。

### 2.2 禁区

- **`BLSource/`**：反编译的游戏源码（6288 文件），只读参考——**绝不修改、绝不删除、绝不提交**
- **`_Module/Prompts/Campaigns/`**：运行时生成的战役存档数据——**绝不提交**

### 2.3 信息隔离硬约束

每个 NPC Agent 只能操作自己目录下的文件 + `World/`，不能读取其他 NPC 的信息。完整权限表见 `docs/architecture.md` 第 6 节。

### 2.4 工程健壮性守则（防膨胀、保可维护）

- **类大小控制**：单个类文件原则上不超过 ~1000 行。接近时按领域拆成 partial（如 `ToolExecutor.*.cs`）或拆出独立类。
- **依赖引入需审批**：原则上不引入新第三方库（当前 NuGet 仅 NAudio）。若某功能用第三方库可显著降低维护成本，**先向用户说明引入理由、备选方案、体积/许可影响，经明确同意后才添加**。同时评估与其他模组的 DLL 冲突风险（同名/同版本覆盖、共享库版本冲突），权衡权始终在开发者。
- **新功能可维护性**：遵循现有分层（Core/Agents/Entities/Tools/UI/Systems），复用现有基础设施（SafeFileIO / MainThreadExecutor / AgentScheduler 事件队列 / DebugLogger），不重复造轮子。工具、事件、补丁等新入口必须登记到对应清单并同步文档。

## 3. 架构速览

### 核心理念（Agent 驱动，受 opencode 启发）

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

### 三个必须记住的机制

1. **单一模型**：整套模组只有一条 LLM 调用链路，角色扮演与工具调用在同一次 SSE 流式请求内完成（`AIChatClient.SendMessage`）。
2. **主线程分发**：LLM 工具循环跑后台线程，但**所有修改游戏状态的工具**经 `MainThreadExecutor` 排队到主线程 `OnApplicationTick` 执行（Bannerlord 游戏对象主线程独占）。
3. **上下文隔离（AsyncLocal）**：聊天与后台信件/自省/密使/外交多流程并发时上下文互不覆盖；实体缓存用 `ConcurrentDictionary`。

### 分层速览

| 层 | 目录 | 职责 |
|----|------|------|
| 引擎层 | `Core/` | SubModule 入口、MCM 设置、LLM Provider 抽象、TTS、基础设施 |
| Agent 核心 | `Agents/` | ContextBuilder、AIChatClient、AgentManager、调度器、记忆 |
| Entity | `Entities/` | 玩家/NPC 统一抽象 |
| 工具执行 | `Tools/` | 51 游戏工具 + 19 文件/通信工具（按领域拆 partial） |
| 界面 | `UI/` | 聊天 / 书信 / 史书 ViewModel |
| 游戏系统 | `Systems/` | 外交、历史记录、Harmony 补丁 |

完整目录结构见 `docs/architecture.md` 第 17 节。

## 4. 构建与部署

```powershell
# 编译 + 自动部署（BANNERLORD_GAME_DIR 已设置）
cd "C:\Users\<用户名>\BLMods\AIChronicle"
dotnet build -c Release

# 全量编译（增量编译可能掩盖文件损坏，定期执行）
dotnet clean -c Release && dotnet build -c Release

# 打包发布版（编译+部署+打包一步完成，产物 dist/AIChronicle_v<版本>.zip）
powershell -ExecutionPolicy Bypass -File scripts\package.ps1
```

部署后 DLL 自动复制到 `D:\steam\steamapps\common\Mount & Blade II Bannerlord\Modules\AIChronicle\bin\Win64_Shipping_Client\AIChronicle.dll`。

> ⚠️ **`_Module/` 目录（含 `Prompts/`、`GUI/`、`SubModule.xml`）也在 build 时拷到游戏 Modules。纯提示词改动也必须 build 才生效**——只改项目里的 `.txt`/`.json` 不 build，游戏里一直是旧版。

## 5. Harmony 两个致命坑（必读）

1. **`PatchAll()` 静默跳过未初始化类型**：`harmony.PatchAll()` 在 `OnSubModuleLoad` 时执行，此时 `TaleWorlds.CampaignSystem.CampaignBehaviors` 命名空间下的类（如 `KingdomDecisionProposalBehavior`）尚未初始化，`[HarmonyPatch]` 属性会**静默跳过**（不生效也不报错）。**解决**：这类补丁在 `OnGameStart` 中用 `Type.GetType("FullName, Assembly")` + `harmony.Patch(...)` **手动注册**。

2. **`__result` 参数类型必须与原方法返回类型完全一致**：否则 `PatchAll()` 抛 `HarmonyException` 并**中止其后所有补丁注册**（"补丁静默丢失"的另一个根因）。例：`CalculateRenownGain` 返回 `ExplainedNumber`，`Postfix` 必须写 `ref ExplainedNumber __result`，不能写 `ref float`。

完整补丁模式、私有字段访问、BLSource 搜索指南见 `docs/harmony.md`。

## 6. docs 索引（详细机制按需 read）

| 文档 | 内容 |
|------|------|
| `docs/architecture.md` | 架构详解（单一模型/场景连接/Provider 抽象/信息隔离完整表/扩展方式/提示词克制原则/记忆系统/流式架构/完整目录结构） |
| `docs/subsystems.md` | 子系统机制（信件激活/封臣自省/内政/盟约到期/历史系统） |
| `docs/tools-reference.md` | 70 工具完整清单 + 工具分类系统 |
| `docs/debugging.md` | 调试方法/日志位置/生命周期陷阱/故障排查 |
| `docs/harmony.md` | Harmony 补丁模式/BLSource 搜索指南/创建新模组 |

## 7. Git 提交

- 提交信息一句简短描述（建议英文、单行），如 `fix: secretary permissions`——不要多行长文
- 提交前 `git status` / `git diff` 检查改动
- 提交与推送只在用户要求时执行

**`.gitignore` 排除项：** `bin/`、`obj/`、`BLSource/`、`_Module/Prompts/Campaigns/`、`.idea/`、`.vscode/`、`Thumbs.db`

## 8. 环境概况

| 项目 | 值 |
|------|-----|
| 游戏 | Mount & Blade II: Bannerlord v1.4.8 |
| 游戏路径 | `D:\steam\steamapps\common\Mount & Blade II Bannerlord` |
| .NET SDK | 9.0+ |
| 目标框架 | net472 (Windows) + net6 (Xbox/Store) |
| 模组框架 | Harmony 2.3.3 (运行时补丁) |
| 四前置 | Harmony, ButterLib, UIExtenderEx, MCM (MBOptionScreen) |
| 语音合成 | NAudio 2.2.1 + 免费 Edge TTS（手写 WebSocket 客户端） |

> **需求先对齐再动手**：涉及 UI 入口、功能行为、用户体验的改动，先列出方案让用户选择，不要自行判断。兼容性维护（改文件格式/存档结构/配置含义）前先问用户是否需要兼容旧存档。
