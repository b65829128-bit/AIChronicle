# AGENTS.md — AI编年史·言出法随 开发入口

> 本文件是 AI 开发入口（面向 DSH / opencode 等 agent，也面向人类贡献者）。**常驻硬规则与速览在此**；详细机制、工具清单、调试方法、Harmony 模式等见 `docs/`（用 `read` 工具按需查阅，不要凭记忆）。
>
> 交叉参考：实现功能前先读 `README_MOD.md`（模组能做什么），再读本文件（怎么做）。两份文档互为补充。
>
> **主开发环境已迁移到 Linux**（Arch/CachyOS）。构建、部署、打包、日志与调试边界见 `docs/linux-dev.md`；Windows 仅保留 net6/Xbox 构建和需要 dnSpy/附加调试的场景。

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

文件操作可用**相对路径**（工具默认解析到项目根目录）或**绝对路径**均可。Linux 下直接用 `/...` 形式；不要用 `C:\...` 或 Git Bash 的 `/c/...`。仓库根目录：`~/项目/AIChronicle`；游戏根目录由 `local.props`（已 gitignore）或环境变量 `BANNERLORD_GAME_DIR` 提供。

## 2. 硬规则（不可违反）

### 2.1 工具定义同步

`tools.json` / `agent_tools.json` 与 `ToolExecutor.ExecuteToolCall` 的 switch **必须同步**。新增工具：定义 → case → 显示映射（详见 `docs/architecture.md` 第 7 节「扩展方式」）。

### 2.2 禁区

- **`BLSource/`**：反编译的游戏源码（7375 个 .cs 文件，约 118MB），只读参考——**绝不修改、绝不删除、绝不提交**
- **`_Module/Prompts/Campaigns/`**：运行时生成的战役存档数据——**绝不提交**

### 2.3 信息隔离硬约束

每个 NPC Agent 只能操作自己目录下的文件 + `World/`，不能读取其他 NPC 的信息。完整权限表见 `docs/architecture.md` 第 6 节。

### 2.4 工程健壮性守则（防膨胀、保可维护）

- **类大小控制**：单个类文件原则上不超过 ~1000 行。接近时按领域拆成 partial（如 `ToolExecutor.*.cs`）或拆出独立类。
- **依赖引入需审批**：原则上不引入新的**运行时**第三方库。当前运行时 NuGet：NAudio 2.2.1；CefSharp.OffScreen 140.1.140（仅 net472，含 CEF 140 原生运行时，模块约 395MB，已按 UI 需求批准）。Linux 编译另用 `Microsoft.NETFramework.ReferenceAssemblies`（编译期参考程序集，不复制进发布包）；`Bannerlord.BuildResources` 仍是原有构建期包。若再引入第三方库，**先向用户说明引入理由、备选方案、体积/许可影响，经明确同意后才添加**。同时评估与其他模组的 DLL 冲突风险（同名/同版本覆盖、共享库版本冲突），权衡权始终在开发者。
- **新功能可维护性**：遵循现有分层（Core/Agents/Entities/Tools/UI/Systems），复用现有基础设施（SafeFileIO / MainThreadExecutor / AgentScheduler 事件队列 / DebugLogger），不重复造轮子。工具、事件、补丁等新入口必须登记到对应清单并同步文档。

### 2.5 CEF / Web 页面边界

- CEF 仅在 `net472`（Steam）编译；所有 CefSharp 代码必须放在 `#if NET472` 内，net6/Xbox 不引用、不加载。
- 新 Web 页面必须走 `CefWebScreen` / `CefWebSession` / `CefBridge`。页面只能通过 `aiChronicle.dispatch(id, payload)` 发动作，**不得直接调用游戏对象、静态状态或 ToolExecutor**。
- `CefWebScreen.PageAction` 在 CEF 线程触发；任何改游戏状态的处理必须 `MainThreadExecutor.Post` / `RunOnMainThread`。
- 只加载本地 `_Module/WebUI/` 页面；禁止远程导航；聊天/书信文本输入与中文 IME 继续用原生输入框。
- CEF 原生文件（`libcef.dll`、`locales`、`CefSharp.BrowserSubprocess.exe` 等）不进 Git；由 csproj 的 `CopyCefRuntimeToModule` 部署、`scripts/package.py` 递归打包。发布包必须保留 `LICENSES/`。

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
2. **主线程分发**：LLM 工具循环跑后台线程，但**所有修改游戏状态的工具**经 `MainThreadExecutor` 排队到主线程 `OnApplicationTick` 执行（Bannerlord 游戏对象主线程独占）。队列动作带 campaign epoch：`OnGameStart`/`OnGameEnd` 递增 epoch 并清空旧队列，切档后旧动作直接丢弃、阻塞等待立即取消；`AIChatClient.SendMessage` 也捕获 epoch，跨档的 LLM 工具循环不再执行工具。
3. **上下文隔离（AsyncLocal）**：聊天与后台信件/自省/密使/外交多流程并发时上下文互不覆盖；每次激活的 `ToolCatalog` 也按异步流程隔离，主线程工具分发时捕获/恢复。实体缓存用 `ConcurrentDictionary`。

### 分层速览

| 层 | 目录 | 职责 |
|----|------|------|
| 引擎层 | `Core/` | SubModule 入口、MCM 设置、LLM Provider 抽象、TTS、基础设施 |
| Agent 核心 | `Agents/` | ContextBuilder、AIChatClient、ToolCatalog（每次激活的工具快照）、AgentManager、调度器、记忆 |
| Entity | `Entities/` | 玩家/NPC 统一抽象 |
| 工具执行 | `Tools/` | 59 游戏工具 + 18 文件/通信工具（按领域拆 partial；含 `ToolExecutor.Curia.cs`） |
| 界面 | `UI/` | 原生 Kit（`UI/Kit`）+ CEF 页面宿主/全 Web 史书/朝议参政页（`UI/Web`）+ 聊天/书信 ViewModel |
| 游戏系统 | `Systems/` | 外交、朝议（`Curia*.cs`）、条约（`Treaty*.cs`）、历史记录、Harmony 补丁 |

完整目录结构见 `docs/architecture.md` 第 17 节。工具快照在 `Agents/ToolCatalog.cs`；实时身份采集与关系渲染在 `Agents/ContextBuilder.Identity.cs`，纯事实文案在 `Entities/IdentityFacts.cs`。`tests/AIChronicle.Pure.Tests/LlmTests.cs` 覆盖 schema 稳定、目录一致、Provider usage、前后台预算与双方身份，`IdentityStubs.cs` 为身份采集提供最小只读桩；`EncounterTests.cs` 直接链接遭遇诊断源码，用最小状态桩覆盖竞技场/定居点保护、计时重置、手动修复与延迟放行。

### 记忆查阅与巩固

记忆进度入口：`Agents/MemoryProgress.cs` 是纯文件逻辑，提醒只说明需查阅，不注入记忆内容。`decisions/.memory_progress_v1.json` 为本人系统元数据，仅存文件行数/校验和查阅/巩固位置，NPC 工具不得读写；旧 `.diary_authored` 不变且不代表巩固完成。普通读/grep 只影响查阅，巩固须按本次工具实际返回的完整编号行确认连续范围，成功结束后才提交，不能越过截断/漏读/并发新增消息。会面提醒会话只记已提示的文件版本，不代替实际查阅；所有记录限本人目录，不新增运行时库或模组存档键。

史官成文入口：`Agents/HistorianWorkflow.cs` 集中最后两轮的写入工具筛选/执行拦截、年度史料读取额度、保留消息证据的两轮续写、命名与对应年份非空正文核对。续写消息仅留本次内存并带 campaign epoch，不进入人物记忆或存档；其他场景工具快照沿用原规则。`Systems/HistoryEventText.cs` 统一 JSONL 序列化，正确转义多行宣言，不重写既有史料。回归入口 `tests/AIChronicle.Pure.Tests/HistorianTests.cs`；机制见 `docs/subsystems.md`。

回归目录项：`tests/AIChronicle.Pure.Tests/MemoryTests.cs` 直接链接生产进度逻辑，覆盖同日新增、逐文件处理、工具真实可见范围、失败/重写/并发/权限边界，以及无正文提示和跨会面已读；`LlmTests.cs` 的实时同盟回归沿原版接口签名，不模拟外交行为。

### 战役主线史料

`Systems/HistoryRecorder.Story.cs` 是历史记录器的 partial，沿原版 `OnQuestCompletedEvent` 与 StoryMode 的碎片集齐/路线选定/阴谋战争事件记录里程碑；沙盒无 StoryMode 时跳过。龙旗重组和列国战争须等同轮原版监听结束后验证，队列沿用 campaign epoch。终局按原版敌国名单全部清空判定，不以可能提前置位的 `ThirdPhase.IsCompleted` 代替。取消备选路线不算失败，拒绝敌国求和不写成议和。

奉旗王国覆亡另由 `KingdomDestroyedEvent` 兜底，覆盖原版先毁族退国、目标先取消而未发失败事件的顺序；与目标失败共享终局去重键。已经离国退出者（含读档基线）不追记原王国后来的败亡。

龙旗现世/归属/战争/最终胜败向事件当时存续王国的在世 AI 统治者立即写「情报」，复用 `AppendSystemDiary`、人物名称渲染与静默见闻开关，不占普通见闻额度、不激活、不代写态度；延迟验证保留事件日期和收件人快照。只记新发生事件，不补历史、不新增存档键；史料类型与边界同步 `docs/subsystems.md`、`docs/plan-silent-memory.md`。

回归目录项：`tests/AIChronicle.Pure.Tests/StoryHistoryTests.cs`、`StoryHistoryStubs.cs`，直接链接生产 partial，覆盖事件时序/四路线/当时君主与日期/多国终局/失败与退出/沙盒和切档隔离。

### 家族换国日记

家族换国入口在 `Systems/HistoryRecorder.Allegiance.cs`（同一行为 partial）：统一监听换国、佣兵开始/结束，向族长本人和事件当时双方存续王国的在世 AI 君主直接写「情报」。复用静默开关、`AppendSystemDiary` 和人物渲染，不占普通额度、不激活；按实际归属去重，不吞同日往返。佣兵结束的中间状态延至原版动作完成验证，保留日期和收件人，队列沿用 campaign epoch。读档仅建内存基线，不补历史、不新增存档键；灭族离国不误记为主动退国。契约同步 `docs/plan-silent-memory.md`。

回归目录项：`tests/AIChronicle.Pure.Tests/AllegianceHistoryTests.cs`、`AllegianceHistoryStubs.cs`，直接链接生产 partial，覆盖三方收件、原版连续事件、同国转封臣、日期/君主快照、同日往返、开关与切档；不模拟换国执行算法。

### 继位、婚姻、拘押与战乱史料

史事扩展入口：`Systems/HistoryRecorder.World.cs` / `HistoryRecorder.Warfare.cs`（同一行为 partial），统一注册继位、实际婚姻、拘押、叛乱、劫掠与攻城处置。原版 `BeforeHeroesMarried` 捕获婚前身份/受众，`RomanticStateChanged(Marriage)` 验证双方配偶和最终 Clan；会晤不额外记史。继位须等同轮死亡/退位结束且无未决选王，`KingdomDecisionConcluded` 兜底。`RebellionFinished` 是起事；`RebelliousClanDisbandedAtSettlement` 是叛军立足转正，不能写成镇压；灭族钩子须延迟确认已覆亡。沿用 `AppendSystemDiary`/`SilentDiary`/epoch，不新增存档键、不补历史；实际劫掠沿普通每日额度。详细 type/受众/时序见 `docs/subsystems.md` 与 `docs/plan-silent-memory.md`。

回归目录项：`tests/AIChronicle.Pure.Tests/WorldHistoryTests.cs` / `WorldHistoryStubs.cs`，直接链接生产两份 partial，核对原版事件签名、正式选王、婚前快照/实际成婚、拘押原因、叛军转正与覆亡时序、劫掠双钩去重和普通缓冲、攻城处置、开关/日期/切档。

### 会晤扩展入口

领主【会晤】以 `MeetingRegistry.Register(IMeetingAction)` 注册事项、`MeetingClauses.Register(IMeetingClauseHandler)` 注册双方报价中可组合的行动条款，玩家表单和 AI 共用版本化交涉单；保留原版交易/婚配入口。会晤工具只在当前会话开放，主线程分发必须捕获/恢复会话上下文，关闭未开始的请求、切档取消旧动作；普通会晤不新增存档字段，决斗计数见下节。详细契约见 `docs/plan-meeting.md`。

新增目录项：`Systems/MeetingModels.cs`、`MeetingNegotiation.cs`、`MeetingRegistry.cs`、`MeetingActions.cs`、`MeetingAssets.cs`、`MeetingService.cs`、`MeetingPageBuilder.cs`、`MeetingBatch.cs`、`MeetingClauses.cs`、`MeetingClauses.Prisoners.cs`、`MeetingClauses.Allegiance.cs`；`Tools/ToolExecutor.Meeting.cs`；`UI/Web/MeetingCefScreen.cs`；`_Module/WebUI/meeting.html/css/js`、`_Module/Prompts/meeting_rules.txt`；`tests/AIChronicle.Pure.Tests/MeetingTests.cs`、`tests/web/meeting-render.test.mjs`。完整树同步到 `docs/architecture.md`；菜单/参数/AI 工具/执行权限必须保持一致。

会晤专属 AI 上下文只使用“你/对方”；`otherOffer/yourOffer` 分别为对方/当前 Agent 提供，不引入“玩家”身份。嵌套工具参数由 `ToolParamDef.Schema` 声明并深复制。新增动作的专属史料写入 `RecordApplied`，核心服务不按事项 ID 分支。行动 handler.Prepare 绑定实际执行对象、资源冲突键和生效/部分变化检查；批执行器先全量预检，任何不可撤销变化已发生后不假称完整回滚，标明部分履行且不自动重试。

会晤 UI 新增本机布局回归文件 `tests/web/meeting-browser.test.mjs`，使用已安装 Chrome 在 1360×800 真实 DOM/CSS 与绘制帧上检查五事项、四个滚动区及刷新，不新增依赖；现有 DOM 桩回归继续供 CI 使用。

### 家族成员投族与自立

`Systems/MeetingClanActions.cs` 注册 `join_clan` / `found_clan` 即时事项，`Systems/ClanDeparture.cs` 统一校验家庭并迁移人物 Clan 与部队 ActualClan。只判断客观资格，不以忠诚/好感/敌我关系筛选；意愿由双方同版接受表达，无审批/回信等待。男女均可任新族长，配偶与同族未成年子女随迁，家庭快照失效须重选。保留本人金币、装备和部队，原族公共资产不迁移。成功记录通过 RecordApplied 向原族长等静默写入「情报」，不激活、不代写情绪，不泄露成交价。

新增目录项：`Systems/MeetingClanActions.cs`、`Systems/ClanDeparture.cs`；`tests/AIChronicle.Pure.Tests/ClanDepartureTests.cs`、`ClanDepartureStubs.cs`；`docs/plan-clan-departure.md`。复用三会晤工具，不增加工具、配置、运行时依赖或模组存档键。详细家庭/部队/部分履行边界见契约，目录树同步 `docs/architecture.md`。

被俘不阻止家族交涉或变更归属；转族本身不改拘押对象/俘虏名册，也不把被俘成员当作普通驻留贵族移出敌城。另行释放及原版入国释俘规则照常生效。

新族使用原版随机旗帜，入国由原版统一国家配色。当前未初始化退国恢复用的原始配色备份，新族退国后可能保留此前国家颜色；此已知限制及字段边界登记在 `docs/plan-clan-departure.md`。

### 决斗与延后履约

`IMeetingDeferredAction` 为等待外部结果的会晤事项提供扩展入口；双向胜负条件在接受后待开始，独立决斗服务接管已冻结的约定，跨场景结束后仅结算实际胜者分支。普通交易仍立即执行。战斗保留原版死亡判定，不使用竞技场强制击昏逻辑；不得把个人决斗结果写成整支部队胜负。算法版本、冻结输入、奖励与真实履约结果保留到 `World/duels/`，会晤关闭不能丢失已开始决斗的结算，切档后旧回调禁止执行。

`Systems/DuelEncounter.cs` 在结束地图对话前设置本次会面的离开标记，避免原版 `encounter_meeting` 自动 `StartBattle`；跨场景收尾只清理捕获的原遭遇，保留定居点和新遭遇，不用 `PlayerEncounter.Update()` 恢复部队交战菜单。胜负履约与放行保护独立于会面退出。

新增目录项：`Systems/MeetingDuelAction.cs`、`MeetingEscrow.cs`、`MeetingClauses.Passage.cs`、`DuelModels.cs`、`DuelRewardMath.cs`、`DuelRewardService.cs`、`DuelService.cs`、`DuelEncounter.cs`、`DuelMission.cs`、`DuelAgentOrigin.cs`、`DuelRecords.cs`、`DuelStore.cs`；`tests/AIChronicle.Pure.Tests/DuelTests.cs`、`DuelGameStubs.cs`；`docs/plan-duel.md`。`DuelStore` 为独立 `CampaignBehaviorBase`，仅新增可选键 `aichronicle_duel_rewards_v1`；旧存档缺键为空，计数与存档分支保持一致，外部审计文件不用于恢复奖励冷却。纯测试进程 `--duel-replay <记录.json>` 直接复算生产算法；详细规则和异常路径见 `docs/plan-duel.md`。

## 4. 构建与部署

Linux 是主工作流。游戏路径由 `local.props` 提供（模板 `local.props.example`；环境变量 `BANNERLORD_GAME_DIR` 优先）。

```bash
cd ~/项目/AIChronicle

# 编译 + 自动部署（Linux 上 TargetFrameworks 自动只有 net472）
dotnet build -c Release -f net472

# 全量编译（增量编译可能掩盖文件损坏，定期执行）
dotnet clean -c Release && dotnet build -c Release -f net472

# 打包发布版（编译 + 部署 + 打包，产物 dist/AIChronicle_v<版本>.zip）
python3 scripts/package.py

# 纯逻辑测试（无需游戏 DLL；CI 同款）
dotnet run --project tests/AIChronicle.Pure.Tests/AIChronicle.Pure.Tests.csproj -c Release
node tests/web/history-render.test.mjs
node tests/web/meeting-render.test.mjs
# 本机实际 CSS/滚动回归（需要已安装 Chrome，不新增依赖）
node tests/web/meeting-browser.test.mjs
node scripts/ci-health.mjs
```

部署后 DLL 自动复制到 `$BANNERLORD_GAME_DIR/Modules/AIChronicle/bin/Win64_Shipping_Client/AIChronicle.dll`（本机游戏根目录见 `local.props`）。

> **CEF 构建约束**：`PlatformTarget` 固定 `x64`；CefSharp/CEF 的原生文件（约 395MB，含 `libcef.dll`、`locales`、`CefSharp.BrowserSubprocess.exe`）由 csproj 的 `CopyCefRuntimeToModule` 目标复制到模块 bin。`scripts/package.py` 必须递归复制非 dll 文件；只复制 `*.dll` 会导致 `Cef.Initialize` 失败。

> ⚠️ **`_Module/` 目录（含 `Prompts/`、`GUI/`、`SubModule.xml`）也在 build 时拷到游戏 Modules。纯提示词改动也必须 build 才生效**——只改项目里的 `.txt`/`.json` 不 build，游戏里一直是旧版。

> ⚠️ **构建后校验部署的 `SubModule.xml` 非空**：`Bannerlord.BuildResources` 的 `ReplaceFileText` 步骤会用「截断再重写」的方式回写 `Modules/AIChronicle/SubModule.xml`（非原子），偶发情况下（与其他进程读取撞上）会把它写成 0 字节，导致启动器报 `Root element is missing`。构建完成后确认该文件约 2KB、非空：

```bash
stat -c '%s' "$BANNERLORD_GAME_DIR/Modules/AIChronicle/SubModule.xml"
```

若为 0 字节，直接把 `_Module/SubModule.xml` 复制过去覆盖即可。

> **运行验证边界**：游戏是 Windows 客户端，Linux 下经 Steam/Proton 运行；编译通过不等于进游戏验证。四前置已安装到 `Modules`（Harmony 2.4.2.248 / ButterLib 2.11.1 / UIExtenderEx 2.13.3 / MCM 5.12.2），进档前备份存档。详见 `docs/linux-dev.md`。

## 5. Harmony 两个致命坑（必读）

1. **`PatchAll()` 静默跳过未初始化类型**：`harmony.PatchAll()` 在 `OnSubModuleLoad` 时执行，此时 `TaleWorlds.CampaignSystem.CampaignBehaviors` 命名空间下的类（如 `KingdomDecisionProposalBehavior`）尚未初始化，`[HarmonyPatch]` 属性会**静默跳过**（不生效也不报错）。**解决**：这类补丁在 `OnGameStart` 中用 `Type.GetType("FullName, Assembly")` + `harmony.Patch(...)` **手动注册**。

2. **`__result` 参数类型必须与原方法返回类型完全一致**：否则 `PatchAll()` 抛 `HarmonyException` 并**中止其后所有补丁注册**（"补丁静默丢失"的另一个根因）。例：`CalculateRenownGain` 返回 `ExplainedNumber`，`Postfix` 必须写 `ref ExplainedNumber __result`，不能写 `ref float`。

完整补丁模式、私有字段访问、BLSource 搜索指南见 `docs/harmony.md`。

## 6. docs 索引（详细机制按需 read）

| 文档 | 内容 |
|------|------|
| `docs/architecture.md` | 架构详解（单一模型/场景连接/Provider 抽象/信息隔离完整表/扩展方式/提示词克制原则/记忆系统/流式架构/完整目录结构） |
| `docs/subsystems.md` | 子系统机制（信件激活/封臣自省/朝议/条约/内政/盟约到期/历史系统/静默见闻） |
| `docs/tools-reference.md` | 77 工具完整清单 + 工具分类系统 |
| `docs/debugging.md` | 调试方法/日志位置/生命周期陷阱/故障排查 |
| `docs/harmony.md` | Harmony 补丁模式/BLSource 搜索指南/创建新模组 |
| `docs/linux-dev.md` | Linux 开发工作流（环境/构建/部署/打包/Proton 运行/日志/调试边界） |
| `docs/ui-framework.md` | 原生 Kit UI 框架（主题/组件/屏幕基类/Gallery/迁移规则） |
| `docs/ui-web.md` | CEF live 页面（CefSharp/CefBridge/输入/BGRA 转换/性能与限制） |
| `docs/plan-history-cef.md` | 全 Web 史书 MVP 状态与收尾计划（HistoryArchive/边界/清晰度） |
| `docs/plan-duel.md` | 决斗契约（胜负分支/原版战死/实战/财物保管/奖励复算/史料与日记） |
| `docs/plan-meeting.md` | 会晤契约（可注册事项/同一交涉单/AI 议价/婚配/原版共存/扩展与测试） |
| `docs/plan-clan-departure.md` | 贵族投族/自立（客观资格/家庭快照/部队保留/无审批/静默日记） |
| `docs/plan-curia.md` | 朝议（Curia）设计契约（议程/应召/权重/门槛/执行器/UI/提示词/测试） |
| `docs/plan-treaty.md` | 条约（战前通牒/附条件议和）设计契约（条款/校验/休战/史料/日记/测试） |
| `docs/plan-silent-memory.md` | 静默见闻设计契约（事件清单/三环分派/渲染/缓冲/幂等/read_diary/分卷/水位/测试） |
| `docs/i18n-plan.md` | 中英双语（i18n）长线方案（规划，不实现） |

## 7. Git 提交

- 提交信息一句简短描述（建议英文、单行），如 `fix: secretary permissions`——不要多行长文
- 提交前 `git status` / `git diff` 检查改动
- 提交与推送只在用户要求时执行

**`.gitignore` 排除项：** `bin/`、`obj/`、`dist/`、`BLSource/`、`_Module/Prompts/Campaigns/`、`local.props`、`.idea/`、`.vscode/`、`Thumbs.db`

## 8. 环境概况

| 项目 | 值 |
|------|-----|
| 开发/构建 | Linux（Arch/CachyOS） |
| 游戏 | Mount & Blade II: Bannerlord v1.4.8（Steam/Proton 的 Windows 客户端） |
| 游戏路径 | `$BANNERLORD_GAME_DIR`（本机 `/mnt/data/SteamLibrary/steamapps/common/Mount & Blade II Bannerlord`；见 `local.props`） |
| IDE | JetBrains Rider（Linux，Toolbox） |
| 开发目录 | `~/项目/AIChronicle` |
| .NET SDK | 10.0+ |
| 目标框架 | net472（Steam；Linux/Windows）+ net6（Xbox/Store；仅 Windows） |
| 构建期参考程序集 | `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3 |
| 纯逻辑测试 JSON | 复用 .NET SDK 随附的 `Newtonsoft.Json.dll`，仅测试进程；模组仍引用游戏自带 DLL，无新增运行时库 |
| 模组框架 | Harmony 2.3.3（NuGet 编译）+ 2.4.2.248（本机运行时） |
| 四前置 | Harmony 2.4.2.248, ButterLib 2.11.1, UIExtenderEx 2.13.3, MCM 5.12.2（本机已装） |
| 语音合成 | NAudio 2.2.1 + 免费 Edge TTS（手写 WebSocket 客户端） |
| Web 页面渲染 | CefSharp.OffScreen 140.1.140 + CEF 140（仅 net472；原生运行时约 395MB） |

> **运行**：游戏通过 Steam/Proton 启动；四前置已安装到 `Modules`（版本见上），进游戏测试前备份存档。详见 `docs/linux-dev.md`。

> **需求先对齐再动手**：涉及 UI 入口、功能行为、用户体验的改动，先列出方案让用户选择，不要自行判断。兼容性维护（改文件格式/存档结构/配置含义）前先问用户是否需要兼容旧存档。
