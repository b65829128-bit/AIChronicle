# Web 内容层（CEF live 页面）

> 当前实现：**CefSharp 140.1.140 + CEF 140 OffScreen**，已在本机 Proton 下实机跑通。
>
> 页面不是预生成图片：CEF 实时渲染，BGRA 帧逐帧转成引擎纹理；C# 注入数据，网页通过 `aiChronicle` 桥回传动作。

## 1. 现状

- CEF 仅在 `net472`（Steam）编译；`net6`/Xbox 用 `#if NET472` 完全排除。
- 默认页面 1280×720（16:9）、30fps，软件渲染（`--disable-gpu` / `--no-sandbox`）。
- 实机：`Cef.Initialize` 约 0.8s，首帧约 1.6s；浏览器关闭后 CEF 保持初始化，可直接重开。
- 入口：大地图按 **F8** → Kit Gallery →「打开 CEF」/「打开 CEF 史书」。
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
| `UI/Web/CefWebScreen.cs` | 通用 CEF 屏幕：标题、页面文件、C# 数据注入、输入接线、`PageAction` |
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
- 游戏 → 网页：`FrameLoadEnd` 后执行 `window.__game.render(json)`；不要在首帧 Paint 时执行，此时 `CanExecuteJavascriptInMainFrame` 可能还是 false。
- `PageAction` 在 CEF 线程触发；处理游戏状态必须回主线程。页面不得直接访问 `ToolExecutor`、游戏对象或静态状态。

## 6. 输入

- `KitTextureWidget` 把 Gauntlet 指针坐标换算到 1280×720 view 坐标。
- 点击：`SendMouseClickEvent`（左键，down/up）；悬停：`SendMouseMoveEvent`。
- 滚轮：`OnPreviewMouseScroll()` 必须返回 `true`，否则 Gauntlet 不会把滚轮派发给普通 Widget。
- 滚轮算法：
  - Gauntlet 原始 delta 通常 ±120；
  - `notches = clamp(|delta|/120, 0.1, 3)`；
  - 目标速度 = `viewportHeight * 1.6 * notches`；
  - 指数逼近速度，新 burst 加 `viewportHeight * 0.08` 冲量；
  - 页面用 `__scrollBySmooth` 逐帧逼近目标（rAF + 0.30 插值），避免一帧到底。
- 文本输入/中文 IME 不交给 CEF；聊天/书信继续原生输入框。

## 7. 页面与排版

- CEF viewport 固定 1280×720（16:9）；页面按 16:9 设计。
- `cef.css` 覆盖 `theme.css` 的 1040px/1400px 长图版式：纸张贴满、正文 17.5px、可滚动。
- 史书示例由 C# `BuildChronicleData()` 注入，包含北境/朝议/盟变/岁末/诸国纪事/史官附记。
- 页面背景与纸张同色，纹理内部无黑边；屏幕边缘的暗区来自固定纹理尺寸/原生面板留白，不是页面版式留边。
- 中文字体使用系统衬线栈（Noto Serif SC / Source Han Serif / SimSun）；目标机器缺字体时需打包字体或调整字体栈。

## 8. 构建 / 打包

- `AIChronicle.csproj`：
  - `PlatformTarget` 固定 `x64`；
  - `CefSharp.OffScreen 140.1.140` 仅 `net472`；
  - `CopyCefRuntimeToModule` 目标在构建后把 CEF 原生文件复制到模块 `bin/Win64_Shipping_Client`。
- CEF 原生文件不进 Git；`scripts/package.py` 递归复制非 dll 文件（`libcef.dll`、`locales`、`CefSharp.BrowserSubprocess.exe`、paks、dat）。
- `LICENSES/` 随发布包复制。
- 只复制 `*.dll` 会导致 `Cef.Initialize` 失败；x86 目录不需要。

## 9. 性能与限制

- 当前 30fps、每帧整张 1280×720 纹理上传（约 3.7MB/帧、110MB/s）；实机无明显卡顿，但仍不如浏览器 60fps 顺滑。
- 每帧成本：CEF 软件渲染 + BGRA→RGBA 逐像素交换 + `CreateFromByteArray` + Brush 更新。
- 引擎 `Texture` 没有 in-place update；不能只更新脏矩形。
- 可选优化：
  1. 60fps 压测（30fps 已通过）；
  2. 16:9 已做 1280×720；下一步 1440×810 或全幅铺满；
  3. 长期：CEF `OnAcceleratedPaint` + 原生 D3D11 共享纹理，绕过 CPU 上传（工程量大）。
- 尚未自动回退到原生史书面板；CEF 启动失败会在屏幕内显示错误，不影响游戏其他部分。

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

1. **60fps 压测**（30fps 已通过，先不改架构）。
2. **全幅 16:9 版式**（当前 1280×720；下一步 1440×810 或自适应纹理尺寸）。
3. 接入真实游戏数据（当前战役/家族/战事）到 `render(data)`。
4. 页面动作 `dispatch` 与真实决策/投票流程对接（游戏功能 session）。
5. 长期评估原生共享纹理桥。

![史书页面预览（1200px 静态版）](ui-preview-chronicle.png)

> 上图为 `_Module/WebUI/preview.html` 在 Chrome 的静态预览；游戏内是 1280×720 CEF live 页面，数据由 C# 注入。
