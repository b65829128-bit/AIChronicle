# 史书 CEF 迁移 — 实施与收尾计划

> 状态：MVP 已实现并实机验证。目录和正文都走 CEF，1100×700 居中窗口；原生只保留标题/关闭和 fallback。
>
> 用户已确认：目录也 Web 化；MCM 默认关闭、验证后开启；初版只读。

## 1. 已完成

| 项 | 实现 |
|---|---|
| MCM 开关 | 史书使用 CEF 阅读页（实验性），默认关闭 |
| H 入口 | 开启后 H 打开 HistoryCefScreen；失败/关闭时打开原生 HistoryScreen |
| 全 Web 目录 | history.html 左侧目录，分组/选中/滚动 |
| 正文 | history.html 右侧纸张正文，章节/空行解析、MCM 字号 |
| 数据桥 | 目录点击 → aiChronicle.dispatch(select, index) → C# 读文件 → showArticle(json) |
| 窗口 | CefWebScreen 窗口模式：1100×700 纹理 + 原生标题栏，居中，不再铺满 |
| 输入 | 鼠标点击/悬停；滚轮按鼠标命中目录或正文，分别平滑滚动 |
| fallback | CEF 初始化失败或 MCM 关闭 → 原生史书 |

当前代码：

- UI/Web/HistoryCefScreen.cs：入口/数据/选中/fallback
- UI/Web/CefWebScreen.cs：窗口/全屏 CEF 宿主，PushJson
- UI/Web/CefWebHost.cs：CEF 会话、桥、滚轮
- _Module/WebUI/history.html/css/js：全 Web 史书
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

    HistoryScreenVM（暂时复用）
      → entries（index/title/header/selected）+ article
      → CefWebScreen 初始 pageData
      → window.__game.render
    点击条目
      → aiChronicle.dispatch(select, index)
      → HistoryCefScreen（CEF 线程）
      → MainThreadExecutor.Post
      → HistoryScreenVM.SelectChronicle(index)
      → window.__game.showArticle(article)

## 3. 已知技术债

最重要：当前 HistoryCefScreen 直接 new 了 HistoryScreenVM 并复用它的私有目录逻辑。
这能快速验证 UI，但不符合长期分层：

- ViewModel 仍耦合 Gauntlet 数据绑定；
- CEF/原生两套入口共享同一 VM 的副作用；
- 后续加搜索/过滤时容易分叉。

收尾第一件事：抽出 HistoryArchive：

    HistoryCatalog LoadCatalog();
    HistoryArticle ReadArticle(int index);

原生 HistoryScreenVM 和 CEF HistoryCefScreen 都调用它；ViewModel 只做绑定/选中态。

## 4. 窗口与清晰度

- 史书窗口 1100×700，比之前 1280 全屏更接近 1:1，文字更清楚。
- 浏览器路径：按 devicePixelRatio 直接栅格化，GPU 合成 1:1。
- 本路径：CEF 固定 1100×700 位图 → 纹理 → Gauntlet 绘制；若 UI scale 不是 1，仍会二次采样。
- 当前 DeviceScaleFactor=1；没有超采样。
- 剩余可选项：
  1. 1.25×/1.5× CEF 超采样后缩小（更清晰，纹理/CPU 成本增加）；
  2. 打包 Noto Serif CJK，避免 CEF 使用不同中文 fallback；
  3. 让 CEF viewport 跟随控件实际像素尺寸，做真正 1:1。
- 全屏选项以后可加 MCM；当前窗口模式已满足使用。

## 5. 收尾阶段

### Phase A：数据抽离（0.5 天）

- 新增 HistoryArchive + DTO；迁移目录查找、文件名解析、advisory/edict/secret 权限。
- HistoryScreenVM 改为调用 HistoryArchive，原生界面行为不变。
- HistoryCefScreen 不再持有 ViewModel，只持有 catalog/article。
- 验收：原生/CEF 目录顺序、权限、正文完全一致。

### Phase B：边界与回归（0.5–1 天）

- 空目录、空文件、缺文件、无权限、超长文（卡拉迪亚上古编年史）。
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

- H 打开 1100×700 居中窗口，不铺满全屏。
- 目录分组/选中正确；点击切换正文；长文可滚动。
- 目录和正文都可滚，滚轮不误滚另一个区域。
- 正文章节、空行、首行标题处理正确。
- MCM 关闭时 H 回原生；CEF 失败回原生；net6 不引用 CEF。
- 连续开关 20 次无泄漏；无主线程红字。

## 7. 风险

| 风险 | 缓解 |
|---|---|
| 直接复用 HistoryScreenVM 造成耦合 | Phase A 抽 HistoryArchive |
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
