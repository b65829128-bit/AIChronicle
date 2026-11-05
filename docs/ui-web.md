# Web 内容层（CEF live 页面）

> 当前实现：**CefSharp 140.1.140 + CEF 140 OffScreen**，已在本机 Proton 下实机跑通。
>
> 页面不是预生成图片：CEF 实时渲染，BGRA 帧逐帧转成引擎纹理；C# 注入数据，网页通过 `aiChronicle` 桥回传动作。

## 1. 现状

- CEF 仅在 `net472`（Steam）编译；`net6`/Xbox 用 `#if NET472` 完全排除。
- 默认页面 1280×720（16:9）、30fps，软件渲染（`--disable-gpu` / `--no-sandbox`）。
- 实机：`Cef.Initialize` 约 0.8s，首帧约 1.6s；浏览器关闭后 CEF 保持初始化，可直接重开。
- 入口：大地图按 **F8** → Kit Gallery →「打开 CEF」/「打开 CEF 史书」（演示/编年史示例）。
- 全 Web 史书：默认按 H 打开新版 Web 阅读页；MCM 关闭或 CEF 失败时回退原生史书。正文按 Markdown 渲染，密陈（及旧档谏言/诏令）保持纯文本。
- CEF 原生运行时约 395MB（`libcef.dll` 236MB + locales + subprocess + paks）。

## 2. 架构

```
C# 数据（Dictionary/匿名对象）
   │ JsonConvert.SerializeObject
   ▼
FrameLoadEnd（主 frame 加载完成）
   │ EvaluateScriptAsync：window.__game.render(json)
   ▼
CEF OffScreen（本地 HTML/CSS/JS）
   │ OnPaint：BGRA 缓冲
   ▼
CefWebSession：逐像素 B/R 交换（CEF BGRA → 引擎 RGBA）
   │ KitTextureWidget.SetRawBgra → Texture.CreateFromByteArray
   ▼
KitDynamicSprite → Brush → ImageWidget → 原生屏幕
```

反向：

```
网页 aiChronicle.dispatch(id, payload)
   │ CefSharp JavascriptObjectRepository
   ▼
CefBridge.ActionReceived
   │ CefWebScreen.PageAction（CEF 线程）
   ▼
调用方 MainThreadExecutor.Post → 游戏主线程
```

## 3. 代码位置

| 文件 | 职责 |
|---|---|
| `UI/Web/CefWebHost.cs` | `CefWebHost` 初始化/路径/日志；`CefWebSession` 浏览器、帧、鼠标、JS；`CefBridge` JS 对象 |
| `UI/Web/CefWebScreen.cs` | 通用 CEF 屏幕：标题、页面文件、C# 数据注入、输入接线、窗口/全屏、`PageAction` |
| `UI/Web/HistoryCefScreen.cs` | 史书入口：调用 `HistoryArchive` 取目录/正文、条目选择、原生 fallback |
| `Systems/HistoryArchive.cs` | 史册数据层：目录扫描、体例分组、权限过滤、正文读取（原生/CEF 共用） |
| `Systems/HistoryMarkdown.cs` | Markdown → 原生纯文本（CEF 失败时的 fallback） |
| `_Module/WebUI/history.html/css/js` | 全 Web 史书：1100×700 古籍式目录 + 书页正文、阅读进度/字数，页面铺满无外框 |
| `UI/Web/CuriaCefScreen.cs` | M 键参政页宿主：五页签、投票/提议/陈情/外交/密陈动作回主线程、原生文本输入、刷新 |
| `UI/Web/MeetingCefScreen.cs` | 领主会晤：复用现有聊天 VM，三栏事项/聊天/交涉卡，原生输入、请求版本与会话校验 |
| `_Module/WebUI/meeting.html/css/js` | 1360×800 会晤页；事项及参数字段由注册表生成，双向报价、婚配预览、接受/还价/拒绝和状态 |
| `_Module/WebUI/curia.html/css/js` | 朝议参政页：朝议/外交/封地/密陈/秘书；1280×760；页面只 dispatch，不碰游戏对象 |
| `_Module/WebUI/history.js` | 目录渲染、Markdown/纯文本正文渲染、dispatch、逐元素平滑滚动 |
| `UI/Kit/KitTexture.cs` | `KitTextureWidget`：动态纹理、BGRA/raw 帧、指针/滚轮事件、旧纹理延迟释放 |
| `_Module/WebUI/live.html` | CEF 演示页：CSS 动画、JS 时钟、`ping` 桥测试按钮 |
| `_Module/WebUI/chronicle.html` | CEF 史书页：等待 C# 注入数据 |
| `_Module/WebUI/app.js` | 共享渲染器：`window.__game.render(data)`、`__scrollBySmooth`、安全 `textContent` |
| `_Module/WebUI/theme.css` | 纸张/朝堂/投票主题 |
| `_Module/WebUI/cef.css` | 1280×720 CEF 版式（贴满纸张、滚动条） |
| `UI/Kit/KitGalleryScreen.cs` | F8 Gallery；CEF 入口按钮 |

`UI/Web/WebPageModels.cs`、`WebPageService.cs` 的 render-once PNG 缓存脚手架仍在，但 **CEF live 页面不走它**；后续接入真实史书数据时可直接复用 `CefWebScreen` 的数据注入。

## 4. 帧管线

- `BrowserSettings.WindowlessFrameRate = 30`。
- `CefWebSession.OnPaint` 在 CEF UI 线程拷贝 BGRA，做 R/B 交换，然后触发 `FrameReady`。
- `CefWebScreen` 合并主线程投递：主线程忙时丢弃中间帧，只保留最新帧。
- `KitTextureWidget.SetRawBgra` → `Texture.CreateFromByteArray` → `GauntletEngineTexture` → `KitDynamicSprite` → Brush。
- 旧纹理用 `ReleaseAfterNumberOfFrames(2)` 延迟释放，避免 GPU 仍引用。
- 纹理只有在 `IsValid && IsLoaded` 后才设置 Brush；未就绪时挂起轮询，避免未上传纹理把渲染器挂死。

## 5. 数据 / JS 桥

- 注册：`JavascriptObjectRepository.Register("aiChronicle", bridge, isAsync: true, BindingOptions.DefaultBinder)`。
- 网页必须先 `await CefSharp.BindObjectAsync("aiChronicle")`。
- 可用方法（JS 小驼峰）：
  - `ping(message)` → 返回 `pong: ...`
  - `log(message)` → 写 `ui_trace.log`
  - `dispatch(id, payload)` → 触发 `CefWebScreen.PageAction`
  - `close()` → 关闭 CEF 屏幕
- 游戏 → 网页：`FrameLoadEnd` 后执行 `window.__game.render(json)`；不要在首帧 Paint 时执行，此时 `CanExecuteJavascriptInMainFrame` 可能还是 false。史书页的 `article` 带 `format`（`markdown` / `plain`）与 `error`，页面据此选择渲染器。
- `PageAction` 在 CEF 线程触发；处理游戏状态必须回主线程。页面不得直接访问 `ToolExecutor`、游戏对象或静态状态。

## 6. 输入

- `KitTextureWidget` 把 Gauntlet 指针坐标换算到当前 CEF view 尺寸（全幅 1280×720 / 史书 1100×700）。
- 点击：`SendMouseClickEvent`（左键，down/up）；悬停：`SendMouseMoveEvent`。
- 滚轮：`OnPreviewMouseScroll()` 必须返回 `true`，否则 Gauntlet 不会把滚轮派发给普通 Widget。
- 滚轮算法：
  - Gauntlet 原始 delta 通常 ±120；
  - `notches = clamp(|delta|/120, 0.1, 3)`；
  - 目标速度 = `viewportHeight * 1.6 * notches`；
  - 指数逼近速度，新 burst 加 `viewportHeight * 0.08` 冲量；
  - 页面用 `__scrollBySmooth` 逐帧逼近目标（rAF + 0.30 插值），避免一帧到底。
- 文本输入/中文 IME 不交给 CEF；聊天/书信/朝议理由继续原生输入框（`InformationManager.ShowTextInquiry`）。

## 7. 页面与排版

- 全幅 CEF viewport 1280×720（16:9）；史书内容区 1100×700（原生标题栏另计）；页面按对应尺寸设计。
- `cef.css` 覆盖 `theme.css` 的 1040px/1400px 长图版式：纸张贴满、正文 17.5px、可滚动。
- 史书示例由 C# `BuildChronicleData()` 注入，包含北境/朝议/盟变/岁末/诸国纪事/史官附记。
- 页面背景与纸张同色，纹理内部无黑边；屏幕边缘的暗区来自固定纹理尺寸/原生面板留白，不是页面版式留边。
- 中文字体使用系统衬线栈（Noto Serif CJK SC / Source Han Serif / SimSun）；目标机器缺字体时需打包字体或调整字体栈。

### 7.1 史书全 Web（已实现 MVP）

- MCM「史书使用 Web 阅读页」默认开启；关闭后 H 打开原生史书。
- HistoryCefScreen + _Module/WebUI/history.html/css/js：
  - 古籍式版式：深色木纹/皮革目录栏 + 书脊 + 暖色书页，页面直接铺满；
  - 左侧 Web 目录（分组/选中/滚动、篇数统计）；
  - 右侧书页正文（Markdown 渲染、MCM 字号、滚动、字数/纪年/阅读进度）；
  - 点击目录 → aiChronicle.dispatch("select", index) → C# 读取 → showArticle。
- 内容区 1100×700，页面铺满 CEF 纹理，**不再有页面边距/外层画框**；原生只保留标题/关闭，不显示技术状态行。
- 原生 HistoryScreen 保留为 fallback；CEF 初始化失败/MCM 关闭时 H 仍打开原生史书。
- 目录/正文数据已抽到 `HistoryArchive`（HistoryCefScreen 不再持有 HistoryScreenVM）；两套入口共用同一份目录顺序、权限与正文读取。
- 正史正文按 Markdown 渲染：`##` 分节、`**` 强调、`>` 史官曰引用块、`- ` 列表；页面用 DOM 节点构造，不拼 `innerHTML`。密陈（及旧档谏言/诏令）按 `format=plain` 原样显示，`*`/`#` 等字符不被解释。
- 清晰度：窗口比 1280 全屏更接近 1:1；浏览器按 DPR 栅格化，CEF 固定位图仍可能被 Gauntlet 二次采样，必要时再做超采样/字体。

### 7.2 朝议参政页（M 键，v2.4.0）

- `CuriaCefScreen` + `_Module/WebUI/curia.html/css/js`：1280×760，深色议政厅 + 羊皮纸卡片；五页签：朝议/外交/封地/密陈/秘书。
- 朝议页：提议者/理由/条款/目标、票数/权重/quorum/门槛条、应召家族立场与理由、未出席、被针对家族陈情、国王态度、结果与执行；投票/附理由/陈情按钮。
- 提议表：类型/目标王国/封地/新主/目标家族的下拉选择；理由由 C# `InformationManager.ShowTextInquiry` 原生输入。
- 外交页：外国提案接受/拒绝、当前战和/盟约/贸易、国王问询线程与遣使（仅国王）。
- 封地页：国王自己封地自由转让；封臣封地提转让/夺封议程；驱逐家族；封臣可提自己的封地转让。
- 密陈页：封臣写密陈；国王读本国密陈。
- 秘书页：打开 `AIChatScreen.OpenChancery()`（query-only）；不提供任何政治动作。
- 页面动作 id：`vote` / `voteReason` / `propose` / `transferOwnFief` / `defend` / `respondProposal` / `secretAdvisory` / `consultSend` / `consultReply` / `openSecretary` / `openLetters` / `refresh`；全部经 `CefBridge.dispatch` + `MainThreadExecutor.Post`。
- CEF 失败：`CuriaCefScreen.Open()` 返回 false，M 键回退 `AIChatScreen.OpenChancery()` 查询聊天并提示。
- 原生文本输入在 Linux/Proton + Fcitx5 下仍可能不可用；先用外部输入后粘贴，输入法修复作为独立议题（见 `docs/plan-curia.md` 第 14 节）。
- **CEF OffScreen 不弹原生 `<select>` 菜单**：页面内选择器全部用 `createDropdown`（按钮 + 绝对定位面板）实现；不要再用 `<select>`/`<option>`。
- **滚轮**：`CefWebHost.SendMouseWheel` 会调用 `window.__scrollBySmooth(delta,x,y)`；页面必须实现该函数，并从鼠标位置向上找 `overflow-y: auto/scroll` 且内容超高的祖先。优先滚下拉菜单/历史列表，最后回退 `main.main.scrollable`；否则 `window.scrollBy` 对固定 760px 的 body 无效。
- **政策效果**：提出/废除政策时，页面用 `PolicyObject.SecondaryEffects` 显示实际效果；没有有效政策时类型不出现在下拉里。
- **议程前提过滤**：`BuildKingdoms` 的 `canWar/canPeace/canTrade/canAlliance/canBreakAlliance` 供页面过滤类型与目标；`renderProposeForm` 没有有效类型时直接显示空态，不渲染提交按钮。
- **下拉图层**：仅给 `.dropdown-menu` 设 `z-index` 不够——`.card > *` 的 `z-index:1` 会形成层叠上下文，菜单会被后面的按钮/卡片压住。展开时给所在 `.card` 加 `.dropdown-open`（`z-index:30`），并把 `.form-grid`/`.actions`/`.send-row` 提到 `z-index:3`、`.form-actions` 保持 2。

### 7.3 会晤

- 领主对话【会晤】使用 `MeetingCefScreen`；原版交易/婚配入口保留。CEF 不可用或非领主时回退原生聊天，net6 继续使用原生聊天。
- 事项来自 `MeetingRegistry`；普通 choice/text/number 字段由页面生成。金币与数量输入、文字与附言通过原生 inquiry；双方报价可按对应 clauseTypes 添加多个行动，目标字段由同一套元数据表单生成。页面不使用文本输入或原生 select。
- `AIChatScreenVM` 复用原对话连接、往来线程、TTS 与工具链。`Changed` 通知刷新；动作和结果以 system 事实进入原线程，关闭时停止语音并取消未成交请求。
- `dispatch` 每次带 sessionId；宿主捕获创建时的 sessionId，排队处理及原生输入回调再次校验。提交/回应携带 requestId/version，客户端按钮也捕获渲染时版本；旧点击不能接受新条件。
- 原生数字/文字输入还带 editorToken，婚配/条件预览另带 revision，避免迟到回调覆盖新草稿。接受按钮固定在交涉区底部。
- 会晤单独采用 `wheelPixelsPerNotch=96`（小 delta 按比例）；页面只做一次 38ms 时间常数插值，完整累加、及时反向，停止使用浏览器 smooth 重启动画。聊天/交涉/下拉独立滚动，重建 DOM 保留原阅读位置。其他页面映射保持原状。
- 延后事项的 `outcomeSlots` 生成多组双向报价，数值/行动路径携带结果索引；旧 editorToken 不能跨草稿、版本或确认状态回填。Ready 显示注册 handler 的 startLabel，仍可修改或取消；开始操作验证 session/id/version 后关闭会晤，独立服务接管实战。接受不会自动跳入战斗；普通事项无结果分支。
- `actionTypes` 同时过滤网页行动选项和服务端校验，放行首版只在决斗中提供。战后原生结果提示显示胜负、实际成长、家族升级和履约，完整约定进入往来记录与 World/duels 审计。
- 回归：`node tests/web/meeting-render.test.mjs`，覆盖双向报价、无条件婚配、动态新事项、安全文本、过期点击/输入、组合条款的执行方、条款移除后输入隔离、双胜负分支路径、显式开始、限定事项条款、忙碌状态和滚动累加/反向/边界/阅读位置保持。

## 8. 构建 / 打包

- `AIChronicle.csproj`：
  - `PlatformTarget` 固定 `x64`；
  - `CefSharp.OffScreen 140.1.140` 仅 `net472`；
  - `CopyCefRuntimeToModule` 目标在构建后把 CEF 原生文件复制到模块 `bin/Win64_Shipping_Client`。
- CEF 原生文件不进 Git；`scripts/package.py` 递归复制非 dll 文件（`libcef.dll`、`locales`、`CefSharp.BrowserSubprocess.exe`、paks、dat）。
- `LICENSES/` 随发布包复制。
- 只复制 `*.dll` 会导致 `Cef.Initialize` 失败；x86 目录不需要。

## 9. 性能与限制

- 当前 30fps；全幅 CEF 页为 1280×720（约 3.7MB/帧、110MB/s），史书内容区为 1100×700（约 3.1MB/帧、92MB/s）；实机无明显卡顿，但仍不如浏览器 60fps 顺滑。
- 每帧成本：CEF 软件渲染 + BGRA→RGBA 逐像素交换 + `CreateFromByteArray` + Brush 更新。
- 引擎 `Texture` 没有 in-place update；不能只更新脏矩形。
- 可选优化：
  1. 60fps 压测（30fps 已通过）；
  2. 16:9 已做 1280×720；下一步 1440×810 或全幅铺满；
  3. 长期：CEF `OnAcceleratedPaint` + 原生 D3D11 共享纹理，绕过 CPU 上传（工程量大）。
- 普通 CefWebScreen 启动失败会在屏幕内显示错误；HistoryCefScreen 会把失败上报给 H 入口，回退原生史书。

## 10. 安全

- 只加载本地 `_Module/WebUI/` 文件；禁止远程导航。
- 页面文本用 `textContent`，不使用 `innerHTML`。
- JS 桥只暴露 `ping/log/dispatch/close`；不暴露游戏对象。
- release 应关闭远程调试/DevTools；本地页面不执行 LLM 生成的脚本。
- CEF 以 `--no-sandbox` 运行是本机 Proton 兼容措施；不要把 CEF 暴露给不可信远程内容。

## 11. 排查

日志：`Prompts/Campaigns/<campaign>/debug_logs/ui_trace.log`、`cef.log`。

常见现象：

| 现象 | 检查 |
|---|---|
| 页面蓝色/偏色 | CEF BGRA 是否交换成 RGBA |
| 不能滚 | `KitTextureWidget.OnPreviewMouseScroll` 是否返回 true；日志是否有 `CefWebScreen wheel` |
| 一下滚到底 | 原始 delta ±120 是否被当像素；是否重复上报；velocity 算法参数 |
| 左下角红字 | `[MainThread] action failed` 全堆栈；不要在第一帧执行 JS |
| 首帧黑/空白 | `BrowserInitialized` → `FrameLoadEnd` → `inject page data` |
| 启动失败 | `CefWebHost baseDir/subprocess exists`、`Cef.Initialize result`、`cef.log` |

## 12. 下一步

> 史书迁移的详细计划见 [plan-history-cef.md](plan-history-cef.md)：全 Web 目录 + 正文 MVP 已完成，HistoryArchive 已抽离，剩余边界回归与清晰度。

1. **60fps 压测**（30fps 已通过，先不改架构）。
2. **全幅 16:9 版式**（当前 1280×720；下一步 1440×810 或自适应纹理尺寸）。
3. 其余 CEF 页面接入真实游戏数据（当前战役/家族/战事）到 `render(data)`；史书已接入。
4. 页面动作 `dispatch` 与真实决策/投票流程对接（游戏功能 session）。
5. 长期评估原生共享纹理桥。

![史书页面预览（新版古籍式版式）](ui-preview-chronicle.png)

> 上图为 `_Module/WebUI/history.html` 在 Chrome 中注入示例数据的预览（1100×700）；游戏内由 C# 注入真实目录与正文。
