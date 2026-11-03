# 调试与故障排查（AI 参考文档）

> 本文件是 `AGENTS.md` 的补充参考，整理调试方法、日志位置、生命周期陷阱与故障排查步骤。排查问题时用 `read` 按需查阅。

## 1. 调试方法

> Linux 下无法把 Rider/调试器附加到 Proton 里的游戏进程；下面的附加调试方法只在 Windows 上可用。Linux 用日志 + `ilspycmd`。

1. **Rider 附加进程调试（仅 Windows）**：
   - 启动游戏
   - Rider → Run → Attach to Process → Bannerlord.exe
   - 设置断点，触发你的代码时会中断

2. **日志调试**（最简单）：
   ```csharp
   InformationManager.DisplayMessage(new InformationMessage($"Debug: {value}", Colors.Red));
   ```

3. **ILSpy / dnSpy 反编译**：Linux 用 `dotnet tool install -g ilspycmd` 反编译游戏 DLL 或对照 `BLSource/`；Windows 上仍可用 dnSpy 附加调试

4. **DebugLogger 调试日志**（推荐优先）：战役目录 `debug_logs/debug_*.log` 记录每次 LLM 调用的轮次/推理长度/工具名；**最终轮无文本时记录思维链摘录**（600 字）。`LLM 请求` 带 activation/request ID、Provider、模型、round、是否后台和工具 schema 的 SHA-256；同次激活中只有显式浏览解锁才应改变该 hash。`SendMessage` 每次 HTTP 尝试结束写一条 `LLM usage`（含重试/失败），记录原始 usage、耗时、状态及归一化输入/输出/思考/缓存命中与未命中；同一流的累计 usage 仅取最后一帧记账一次。缺失字段显示 `N/A`，思考包含在输出内，勿再相加。`LLM 完成` 汇总并标明 `usage=已报告请求数/总尝试数` 和缓存统计覆盖数；不完整统计不能当成完整账单。DeepSeek 缓存字段为 `prompt_cache_hit_tokens`/`prompt_cache_miss_tokens`，通用兼容/Qwen 为嵌套 `prompt_tokens_details.cached_tokens`。排查"Agent 为什么这么干/没干"与缓存失效首选此日志。受 MCM「调试日志」开关控制（默认开）。注意：`SendMessage` 返回的 `Content` 若回退到"（已通过工具处理完毕）"表示 Agent 调了工具但没输出结语（如国王评估后决定不行动）。

## 2. 日志文件位置

| 日志 | 路径（Proton prefix） | 内容 |
|------|------|------|
| 游戏引擎日志 | `$PREFIX/drive_c/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_*.txt` | 模组加载顺序、DLL 加载、资源扫描 |
| 游戏错误日志 | `$PREFIX/drive_c/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_errors_*.txt` | 引擎级错误和警告 |
| 看门狗/崩溃日志 | `$PREFIX/drive_c/ProgramData/Mount and Blade II Bannerlord/logs/watchdog_log_*.txt` | 崩溃时的异常码和堆栈 |
| ButterLib 日志 | `$PREFIX/drive_c/users/steamuser/Documents/Mount and Blade II Bannerlord/Configs/ModLogs/butterlib*.txt` | ButterLib 加载状态和模块级异常 |
| 默认模组日志 | `$PREFIX/drive_c/users/steamuser/Documents/Mount and Blade II Bannerlord/Configs/ModLogs/default*.log` | 模组标准日志输出 |
| 战役内 AI 日志 | `$BANNERLORD_GAME_DIR/Modules/AIChronicle/Prompts/Campaigns/<campaign>/debug_logs/debug_*.log` | LLM 轮次/工具/缓存统计 |
| 崩溃 Dump | `$PREFIX/drive_c/ProgramData/Mount and Blade II Bannerlord/crashes/` | 崩溃时生成的 .dmp 文件 |

本机 prefix：`/mnt/data/SteamLibrary/steamapps/compatdata/261550/pfx`。

### 查看最新日志的命令

```bash
PREFIX="/mnt/data/SteamLibrary/steamapps/compatdata/261550/pfx"

# 最新的游戏引擎日志
LATEST=$(ls -t "$PREFIX/drive_c/ProgramData/Mount and Blade II Bannerlord/logs"/rgl_log_*.txt | head -1)
tail -n 200 "$LATEST"

# 最新的 ButterLib 日志
LATEST_BL=$(ls -t "$PREFIX/drive_c/users/steamuser/Documents/Mount and Blade II Bannerlord/Configs/ModLogs"/butterlib*.txt | head -1)
tail -n 200 "$LATEST_BL"

# 搜索当前最新引擎日志中的错误关键词
grep -nE "Error|Exception|crash|Null|AIChronicle" "$LATEST"
```

## 3. 生命周期与崩溃陷阱

Bannerlord 的模组加载有严格的初始化顺序。**在错误的阶段调用 UI/游戏系统 API 会直接崩溃：**

```
游戏启动 → 加载所有 DLL → OnSubModuleLoad()       ← 只能做 Harmony.PatchAll()，不能碰 UI
        → 初始化渲染 → 加载资源 →
        → OnBeforeInitialModuleScreenSetAsRoot()  ← UI 系统就绪，可以调用 DisplayMessage
        → 主菜单显示 →
        → 新游戏/读档 → OnGameStart()             ← 战役系统就绪
        → 战役结束（切档/回主菜单/关游戏）→ OnGameEnd()  ← 必须清空跨档静态状态
```

| 阶段 | 可以做什么 | 不能做什么 |
|------|-----------|-----------|
| `OnSubModuleLoad` | `Harmony.PatchAll()`（仅对已完成的类型生效）、初始化纯数据结构 | 调用 `InformationManager`、访问 `Campaign`、打补丁到未初始化的类型 |
| `OnBeforeInitialModuleScreenSetAsRoot` | 显示欢迎消息、修改主菜单 | 访问战役数据（还没进游戏） |
| `OnGameStart` | 递增主线程队列 epoch（`MainThreadExecutor.BeginCampaign`）、注册 CampaignBehavior、显示消息、访问战役数据、**用 Type.GetType + harmony.Patch 手动补丁未初始化的类型** | - |
| `OnGameEnd` | 先 `MainThreadExecutor.EndCampaign` 取消旧队列动作，再清空跨档静态状态（`EntityManager.ResetForNewCampaign`/`PartyBehaviorManager`/`AgentScheduler`/`DebugLogger`）——避免新档用到旧档的实体缓存、计时器、编年史年份 | 访问战役数据（已结束） |

**常见崩溃码：**

| 异常码 | 含义 |
|--------|------|
| `0xE0434352` | .NET 未处理异常（最常见，通常伴随 ButterLib 弹窗显示具体错误） |
| `0xC0000005` | 内存访问违规（C++ 层崩溃，可能和 Native DLL 相关） |

## 4. ButterLib 异常弹窗

当模组抛出未处理异常时，ButterLib 会拦截并弹出一个红色窗口显示堆栈信息。**截图这个窗口是最直接的排查方式**。弹窗信息也会同时写入 Proton 前缀下的 `.../Configs/ModLogs/butterlib*.txt`。

## 5. 排查步骤（模组不工作或崩溃时）

1. 启动游戏，勾选你的模组，如果崩溃 → 截图 ButterLib 弹窗
2. 查看 `butterlib*.txt` 最新日志中的 `[ERR]` 行
3. 查看 `watchdog_log_*.txt` 中的异常码
4. 检查 Harmony 补丁是否存在 **Ambiguous match**（重载冲突），参考 [Harmony 章节](harmony.md)
5. 确认代码中是否在 `OnSubModuleLoad` 中调用了 UI/游戏系统 API

## 6. 注意事项

- **运行 `dotnet build` 前必须让 `BANNERLORD_GAME_DIR` 指向游戏目录**（`local.props` 或环境变量），否则找不到游戏 DLL
- BLSource 虽然编译时被排除，但**不能被删除**——AI 需要它来理解游戏逻辑
- Harmony 补丁中的私有字段名必须与原始 DLL 中的字段名**完全一致**（Linux 用 `ilspycmd`/Rider 反编译，Windows 可用 dnSpy）
- 模组的 SubModule.xml 中 `Id` 和 `Name` 默认等于项目名（`$(MSBuildProjectName)`）
- 如果补丁不生效，检查：1) 方法名是否正确 2) 参数类型是否匹配 3) 是否有重载冲突（aka Ambiguous match）

## 7. 纯逻辑测试（无需游戏）

史册数据层与 CEF 史书页渲染有独立测试，不需要游戏 DLL，Linux/CI 均可跑：

```bash
# 史册目录/权限/读取 + 主线程 epoch（dotnet，目标 net10.0）
dotnet run --project tests/AIChronicle.Pure.Tests/AIChronicle.Pure.Tests.csproj -c Release

# 史书页 Markdown 渲染（Node + 最小 DOM 桩）
node tests/web/history-render.test.mjs

# 仓库健康检查（JSON/switch 同步、CEF 条件编译、文件行数、密钥扫描）
node scripts/ci-health.mjs
```

测试源码通过 `<Compile Include>` 链接 `Systems/HistoryArchive.cs`/`HistoryMarkdown.cs`/`Core/SafeFileIO.cs`/`Core/MainThreadExecutor.cs`，不引用真实 TaleWorlds；`MainThreadStubs.cs` 只提供 `MainThreadExecutor` 需要的最小日志/消息桩，不模拟游戏行为，也不打进模组发布包。新增纯逻辑模块时优先采用这种方式。

## 8. 遭遇无反应（PlayerEncounter 残留）

**现象**：大地图上部队相遇/点击敌对部队没有遭遇菜单，时间仍可流动，AI 仍会活动。

**原理**：`EncounterManager.HandleEncounterForMobileParty` 在 `PlayerEncounter.Current != null` 时会跳过主队伍；如果遭遇只在存档里留下 `PlayerEncounter`、却没有对应菜单/对话/MapEvent（例如在遭遇菜单上叠了 H/F8 界面后存档，或 `let_go` 只设置 `LeaveEncounter` 而没有 `Finish`），新遭遇就被永久挡住。

**排查**：看战役 `debug_logs/ui_trace.log` 的 `[Encounter]` 行：

    [Encounter] session launched current=... state=... leave=... atMenu=... mapConversation=... conversation=... mapEvent=... modUi=... activeState=... mission=... insideSettlement=... locationEncounter=... encountered=... settlement=... time=...

- 仅 `activeState=MapState`、`mission=False`、主队伍有效且不在定居点，并且没有菜单/地图对话/对话/MapEvent/模组界面/LocationEncounter/EncounterSettlement 时，`current=True` 才可能是残留；
- `current=True` 且仍在 Mission、竞技场/竞技大赛、城镇/村庄、菜单、对话或地图事件中 → 正常流程仍可能依赖遭遇，不要清；
- `time=Stop` 且部队不移动 → 先按空格恢复时间，不是遭遇残留。

**恢复**：

- 自动：`EncounterDiagnostics.Tick` 仅在上述空闲大地图条件下，对**同一个** `Current` 连续观察 8 秒后自动 `PlayerEncounter.Finish()` 并写日志/黄字。进入场景/界面或更换遭遇都会重新计时，不累计场景里的时间；
- 手动：MCM「游戏设置 → 修复遭遇状态」复用相同场景保护，只在空闲大地图执行；
- 代码侧：`let_go` 结束对话后排队收尾，只操作投递时的同一个遭遇，允许结束其遭遇菜单，但仍受 Mission/定居点/对话/地图事件/模组界面保护；H/O/M/F8 入口要求 `MapState.AtMenu == false`、无对话、无 `PlayerEncounter`、无 MapEvent，避免在遭遇菜单上叠 UI。

**竞技场误清理回归**：2026-11-03 的日志显示，15:28:40 进入厄庇克洛忒亚的竞技场，15:28:48 被旧版自动恢复误判为残留并 `Finish()`。竞技场没有大地图菜单或 MapEvent；`Finish()` 却会清空 `LocationEncounter`，默认还会使玩家离开定居点。比赛控制器仍会读取 `PlayerEncounter.EncounterSettlement.Culture`，因此这类清理会破坏比赛/返回城镇的状态。当前日志没有崩溃堆栈，不能据此确定最后抛异常的具体位置。回归测试直接链接 `EncounterDiagnostics.cs`，覆盖自动、手动和 `let_go` 三个入口的场景保护及倒计时重置。

## 9. 切档后旧动作/主线程队列残留

**现象**：切档/读档/回主菜单后，屏幕上出现旧档的消息，或新档里突然执行了旧档排队的行军/外交/UI 动作；严重时红字异常或游戏对象失效。

**原理**：LLM 工具在后台线程运行，通过 `MainThreadExecutor` 投递到主线程。旧版队列只有一条 `ConcurrentQueue`，`OnGameEnd` 不清空、动作也不带战役标识；旧档动作如果在新档的 `OnApplicationTick` 才被消费，就会落到新档的游戏对象上。

**现在的保护**：

- `OnGameStart` → `MainThreadExecutor.BeginCampaign()`，`OnGameEnd` → `EndCampaign()`，每次生命周期切换递增 campaign epoch 并清空队列；
- 入队动作记录 epoch；`Tick` 发现 epoch 不匹配直接丢弃；
- 正在等待 `RunOnMainThread` 的后台线程会收到取消异常（不再等 30 秒超时）；
- `AIChatClient.SendMessage` 在请求开始时捕获 epoch，切档后不再执行后续工具，返回 `FinishReason=campaign_ended`。

**日志**（战役 `debug_logs/ui_trace.log`）：

    [MainThread] initialized epoch=0
    [MainThread] campaign epoch=1 (新战役开始), canceled=0, pending=0
    [MainThread] status epoch=1, pending=0 (session launched)
    [MainThread] campaign epoch=2 (战役结束), canceled=3, pending=0
    [MainThread] dropped stale work (epoch 1 != 2), total=1, item=...

如果看到 `campaign epoch` 正常递增但仍有旧动作，优先检查该动作是否在新 epoch 下才第一次入队（例如旧 Agent 在新档才发起新的 `SendMessage`）——这属于 Agent/调度器生命周期，需继续在调用方加 epoch 校验。

**回归测试**：

1. 进档，确认 `ui_trace.log` 有 `campaign epoch=1`；
2. 触发一个后台动作（写信/聊天/自省），在它完成前回主菜单或读另一个档；
3. 确认日志出现新的 `campaign epoch` 和 `canceled=N`，且新档没有旧档的提示/动作；
4. 若正在跑 LLM，确认日志出现 `LLM 取消 ... 战役生命周期已切换`，没有工具副作用。
