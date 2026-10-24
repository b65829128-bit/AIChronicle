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

4. **DebugLogger 调试日志**（推荐优先）：战役目录 `debug_logs/debug_*.log` 记录每次 LLM 调用的轮次/推理长度/工具名；**最终轮无文本时记录思维链摘录**（600 字）；请求结束时记录**缓存命中统计**（`LLM 完成 ... 缓存命中=X 未命中=Y 命中率=Z%`，来自 provider 按能力声明的缓存字段解析，DeepSeek 为 `usage.prompt_cache_hit_tokens`/`prompt_cache_miss_tokens`）。排查"Agent 为什么这么干/没干"首选此日志，排查"缓存是否生效"也看它。受 MCM「调试日志」开关控制（默认开）。注意：`SendMessage` 返回的 `Content` 若回退到"（已通过工具处理完毕）"表示 Agent 调了工具但没输出结语（如国王评估后决定不行动）。

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
| `OnGameStart` | 注册 CampaignBehavior、显示消息、访问战役数据、**用 Type.GetType + harmony.Patch 手动补丁未初始化的类型** | - |
| `OnGameEnd` | 清空跨档静态状态（`EntityManager.ResetForNewCampaign`/`PartyBehaviorManager`/`AgentScheduler`/`DebugLogger`）——避免新档用到旧档的实体缓存、计时器、编年史年份 | 访问战役数据（已结束） |

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
