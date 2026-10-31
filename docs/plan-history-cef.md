# 史书 CEF 迁移 — 实施与收尾计划

> 状态：MVP 已实机验证；Phase A（HistoryArchive 抽离）已完成，Phase B 代码级边界/权限回归已完成（进游戏回归待做）。正史改为 Markdown 渲染，谏言/诏令/密陈保持纯文本。
>
> 用户已确认：目录也 Web 化；新版 Web 阅读页默认开启，保留原生 fallback；初版只读。

## 1. 已完成

| 项 | 实现 |
|---|---|
| MCM 开关 | 史书使用 Web 阅读页，默认开启；关闭后回原生史书 |
| H 入口 | 开启后 H 打开 HistoryCefScreen；失败/关闭时打开原生 HistoryScreen |
| 全 Web 目录 | history.html 左侧目录，分组/选中/滚动 |
| 正文 | history.html 右侧书页正文，Markdown 渲染、MCM 字号；古籍式目录/页眉/阅读进度/字数 |
| 数据桥 | 目录点击 → aiChronicle.dispatch(select, index) → C# 读文件 → showArticle(json) |
| 窗口 | CefWebScreen 窗口模式：1100×700 内容区 + 原生标题栏，页面铺满无外框，居中 |
| 输入 | 鼠标点击/悬停；滚轮按鼠标命中目录或正文，分别平滑滚动 |
| fallback | CEF 初始化失败或 MCM 关闭 → 原生史书 |
| 数据层 | HistoryArchive + HistoryViewer；原生/CEF 共用目录、体例分组、权限、正文读取 |
| Markdown | 史官成文 Markdown；history.js 渲染（标题/强调/引用/列表）；HistoryMarkdown 为原生 fallback |
| 单测 | tests/AIChronicle.Pure.Tests（目录/权限/读取）+ tests/web（Markdown 渲染，Node DOM 桩） |
| 政务文献 | 三类文献共用一个「政务文献」分组头，按年份倒序 |

当前代码：

- Systems/HistoryArchive.cs：目录/分组/权限/正文数据层
- Systems/HistoryViewerFactory.cs：从当前战役取权限视角
- Systems/HistoryMarkdown.cs：Markdown → 原生纯文本
- UI/HistoryScreenVM.cs：原生史书 ViewModel（只做绑定/选中）
- UI/Web/HistoryCefScreen.cs：CEF 入口/选中/fallback（不再持有 ViewModel）
- UI/Web/CefWebScreen.cs：窗口/全屏 CEF 宿主，PushJson
- UI/Web/CefWebHost.cs：CEF 会话、桥、滚轮
- _Module/WebUI/history.html/css/js：全 Web 史书 + Markdown 渲染
- tests/AIChronicle.Pure.Tests + tests/web：纯逻辑/渲染测试
- Core/Settings.cs：MCM 开关
- Core/SubModule.cs：H 路由

## 2. 当前架构

    H
    ├─ MCM 开 + CEF 可用
    │    └─ HistoryCefScreen
    │         ├─ CefWebScreen（原生标题/关闭）
    │         └─ history.html（目录 + 正文）
    └─ 否则 / CEF 失败
         └─ HistoryScreen（原生 1100×700）

数据流：

    HistoryArchive.LoadCatalog(viewer, campaignDir)
      → catalog.Entries（index/title/header/format）
      → HistoryArchive.ReadArticle(entry)
      → CefWebScreen 初始 pageData
      → window.__game.render
    点击条目
      → aiChronicle.dispatch(select, index)
      → HistoryCefScreen（CEF 线程）
      → MainThreadExecutor.Post
      → HistoryArchive.ReadArticle(entry)
      → window.__game.showArticle(article)

## 3. 已完成的分层

HistoryArchive 已抽出，原生 HistoryScreenVM 与 CEF HistoryCefScreen 都只调用它：

    HistoryCatalog LoadCatalog(HistoryViewer viewer, string campaignDir);
    HistoryArticle ReadArticle(HistoryCatalogEntry entry);

- ViewModel 只做 Gauntlet 绑定/选中态，不再解析文件名或判断权限；
- CEF 不再复用原生 ViewModel；
- 权限由 HistoryViewer 描述（王国/雇佣兵/国王），可脱离 Campaign.Current 单测。

剩余：进游戏回归边界（空目录/空文件/缺文件/无权限/超长文/CEF 失败回退），以及清晰度选项。

## 4. 窗口与清晰度

- 史书内容区 1100×700（原生标题栏另计），比之前 1280 全屏更接近 1:1，文字更清楚。
- 浏览器路径：按 devicePixelRatio 直接栅格化，GPU 合成 1:1。
- 本路径：CEF 固定 1100×700 位图 → 纹理 → Gauntlet 绘制；若 UI scale 不是 1，仍会二次采样。
- 当前 DeviceScaleFactor=1；没有超采样。
- 剩余可选项：
  1. 1.25×/1.5× CEF 超采样后缩小（更清晰，纹理/CPU 成本增加）；
  2. 打包 Noto Serif CJK，避免 CEF 使用不同中文 fallback；
  3. 让 CEF viewport 跟随控件实际像素尺寸，做真正 1:1。
- 全屏选项以后可加 MCM；当前窗口模式已满足使用。

## 5. 收尾阶段

### Phase A：数据抽离（已完成）

- 已完成：HistoryArchive + DTO；目录查找、文件名解析、advisory/edict/secret 权限已迁入。
- HistoryScreenVM 改为调用 HistoryArchive，原生界面行为不变。
- HistoryCefScreen 不再持有 ViewModel，只持有 catalog/article。
- 验收：原生/CEF 目录顺序、权限、正文完全一致。

### Phase B：边界与回归（代码级已完成，进游戏待做）

- 已有单测覆盖：空目录、空文件、缺文件、无权限、目录分组/排序、Markdown 渲染。
- 待进游戏回归：超长文（卡拉迪亚上古编年史）、滚动/字号、CEF 失败回退。
- 目录滚动、正文滚动、选中项自动滚入视野。
- MCM 字号 14–36；重启/重开保持。
- H 开/关、O/M/F8 互斥、关闭后输入恢复。
- CEF 初始化失败/删 libcef → 自动回退原生。
- net6/Xbox 编译（NET472 条件）。

### Phase C：清晰度（可选，0.5 天）

- 先确认 UI scale；若明显 != 1，再做 CEF 超采样或自适应 viewport。
- 字体打包作为独立选项；不要为了清晰度先上共享纹理桥。

### Phase D：文档/打包/提交（0.5 天）

- README：MCM 表、功能说明、文件结构（已加基础条目，待补截图/限制）。
- docs/ui-web：史书全 Web 布局、窗口、滚动、清晰度限制。
- docs/ui-framework：迁移状态。
- 打包验证：history.html/css/js 进 zip；LICENSES 保留。
- 提交。

## 6. 验收

- H 打开 1100×700 内容区（原生标题栏另计），居中，不铺满全屏；页面无外层画框。
- 目录分组/选中正确；点击切换正文；长文可滚动。
- 目录和正文都可滚，滚轮不误滚另一个区域。
- 正文章节、空行、首行标题处理正确。
- MCM 关闭时 H 回原生；CEF 失败回原生；net6 不引用 CEF。
- 连续开关 20 次无泄漏；无主线程红字。

## 7. 风险

| 风险 | 缓解 |
|---|---|
| 直接复用 HistoryScreenVM 造成耦合（已解决） | 已抽 HistoryArchive；两入口只依赖数据层 |
| 目录/正文滚动区域互相误伤 | 按鼠标坐标 elementFromPoint 的 .scrollable |
| 长文/特殊字符 | textContent；真实文件预览；空行/章节测试 |
| 清晰度仍不足 | 窗口 1:1 优先；必要时超采样/字体 |
| CEF 包体积 | 既有依赖；史书不新增原生库 |
| 全屏/窗口偏好 | 当前窗口 1100×700；全屏可后续加开关 |

## 8. 明确不做

- 不做页面内搜索/输入框；搜索继续原生（若未来加）。
- 不把聊天/书信输入迁到 Web。
- 不删除原生史书；至少保留一个版本作为 fallback。
- 不为了清晰度直接上原生共享纹理桥；先完成数据/回归收尾。
