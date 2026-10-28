# 史书 UI 迁移到 CEF — 开发计划

> 结论：**合适，但建议分阶段迁移，不建议一次性把整个史书界面都改成 Web。**
>
> 第一阶段保留原生目录（交互归原生），只把右侧正文阅读面板迁到 CEF（氛围归 Web）。验证后再决定是否把目录也 Web 化。

## 1. 为什么史书适合作为 CEF 的第一个真实用例

- 史书是**模组自带界面**，不涉及替换原版 UI。
- 以**只读长文**为主，没有聊天/写信那种中文 IME 输入压力。
- 现有界面是“左侧目录 + 右侧 RichTextWidget”，正文排版正是 CEF 的强项。
- 已有数据管线：编年史/本纪/世家/列传/纪事/政务文献/密陈都落在本地 txt。
- CEF 基线已经验证：1280×720、30fps、鼠标点击/滚轮、C# 数据注入、JS 桥。

## 2. 目标

1. 把史书**正文阅读区**迁到 CEF，获得纸张、首字下沉、章节标题、注释、印章等排版。
2. 保留原生目录、选中态、关闭按钮、H 键入口和 MCM 字号。
3. 保留原生 `HistoryScreen` 作为回退：CEF 不可用、net6/Xbox、初始化失败时仍能看史书。
4. 数据读取从 Gauntlet ViewModel 中抽离，原生与 CEF 共用同一份目录/正文数据。

## 3. 非目标

- 不迁移聊天/书信/秘书处输入框。
- 不做页面内搜索输入；史书当前没有搜索，未来若加仍用原生输入。
- 不在第一阶段改史书数据格式、权限规则、史官生成逻辑。
- 不删除原生 `HistoryScreen`；至少保留一个发布周期作为回退。
- 不在第一阶段做整屏 Web 目录/翻页动画。

## 4. 当前基线

```
H 键
  → HistoryScreen（原生 Gauntlet，1100×700）
       ├─ 左侧 340px：分组目录（本纪/世家/列传/编年史/纪事/政务文献）
       ├─ 右侧：RichTextWidget 正文
       └─ 关闭按钮
```

数据来源：

- 编年史：`NPCs/World/history/chronicles/*.txt`（兼容 `World/history/chronicles`）。
- 政务文献：`NPCs/World/advisory/{王国}_*.txt`、`edict`。
- 密陈：`NPCs/World/secret_advisory/{王国}_*.txt`，仅玩家为该国国王可见。
- 文件名解析体例；未能识别的归入「其他」。
- 正文格式：首行标题、`【章节】`、空行分段；当前 RichTextWidget 直接显示纯文本。

MCM：`ChronicleFontSize`（史书字体大小）。

## 5. 推荐形态：原生目录 + CEF 正文

```
HistoryCefScreen（KitScreen）
├─ 原生标题栏：史书标题 + 关闭（Kit.Header）
├─ 原生左侧目录：Kit.Scroll + 分组/条目（点击选中）
└─ 右侧：CefWebPanel
      └─ history_article.html（CEF，正文阅读）
```

理由：

- 目录是**交互**：分组、选中、键盘/滚轮、未来搜索都更适合原生。
- 正文是**内容**：排版、纸张、章节层级收益最大。
- 不把中文输入/IME 带进 Web。
- 出问题时可以直接切回原生 `HistoryScreen`。

整屏 Web（目录也在 CEF）作为**可选第四阶段**，不作为第一版目标。

## 6. 数据层设计

新增 `HistoryArchive`（建议 `Systems/HistoryArchive.cs` 或 `UI/HistoryArchive.cs`），与 Gauntlet 解耦：

```csharp
HistoryCatalog LoadCatalog();
HistoryArticle ReadArticle(string id);
```

DTO：

```json
{
  "groups": [
    { "id": "本纪", "title": "本纪", "entries": [
      { "id": "e1", "title": "拉盖娅本纪", "kind": "本纪", "year": 0 }
    ]}
  ]
}
```

```json
{
  "id": "e1",
  "title": "拉盖娅本纪",
  "body": "首行标题\n\n【章节】\n段落……",
  "fontSize": 28
}
```

要点：

- 页面只拿 `id/title/body/fontSize`，**不拿绝对文件路径**。
- id 用稳定短 id（路径 hash 或序号），由 C# 映射回文件。
- 正文仍是纯文本；页面用 `textContent`/DOM API 生成标题、`【】` 章节和段落，禁止 `innerHTML`。
- 目录顺序、分组、密陈权限与现有 `HistoryScreenVM` 完全一致。
- 原生 `HistoryScreenVM` 改为复用 `HistoryArchive`，避免两套解析逻辑。

## 7. CEF 面板与通信

新增 `HistoryCefScreen`（或先抽 `CefWebPanel` 复用 `CefWebSession`）：

- 原生目录点击 → `HistoryCefScreen.Select(entry)` → `HistoryArchive.ReadArticle(id)` → `CefWebSession.ShowArticle(json)`。
- 页面：`window.__game.showArticle(data)`，只负责排版/滚动，不读文件。
- 页面上的链接/未来动作如需要，走 `aiChronicle.dispatch(id, payload)` → `CefWebScreen.PageAction`；第一版正文只读，可不用。
- 字号：把 MCM `ChronicleFontSize` 映射为页面 CSS 变量。
- CEF 页面建议 `history_article.html`，viewport 约 **960×620**（右侧面板），15fps 即可；只读页面不需要 30fps。

## 8. 生命周期 / 入口

- H 键：优先打开 `HistoryCefScreen`；若 MCM 开关关闭、CEF 未初始化、net6/Xbox，则打开原生 `HistoryScreen`。
- 保持 `HistoryScreen.IsOpen` 兼容：新增 `HistoryCefScreen.IsOpen`，`SubModule` 的 O/M/F8 互斥列表同时判断两者。
- 原生关闭按钮；CEF 面板不提供 Web 关闭，避免焦点/层级问题。
- 关闭时释放 CEF 会话/纹理；CEF 进程保持初始化，可重开。
- 新增 MCM「史书使用 CEF 阅读页（实验性）」：
  - 默认**关闭**，先在开发档验证；
  - 验证通过后默认开启；
  - 保留原生回退。

## 9. 实施阶段

### Phase 0：数据抽离（不改 UI）

- 新增 `HistoryArchive` + DTO；迁移文件名解析、目录查找、advisory/edict/secret 读取。
- `HistoryScreenVM` 改为调用 `HistoryArchive`，原生界面行为不变。
- 验收：所有分组/条目/权限与旧版一致；缺文件/权限不足不崩。

### Phase 1：Chrome 页面原型

- `history_article.html` + `history_article.css`：纸张、标题、`【】`章节、段落、首字下沉、注释、滚动。
- 用真实长文（如《卡拉迪亚上古编年史》6KB+）在 Chrome 预览；确认换行、标点、字号。
- 验收：960×620 下可读；长文滚动；不出现横向溢出。

### Phase 2：CEF 正文面板 + 原生目录

- 新建 `HistoryCefScreen` / `CefWebPanel`；原生目录用 `Kit.Scroll`，右侧 CEF 纹理。
- 选中条目 → 读取正文 → 注入页面；加载中/读取失败显示状态。
- 保留原生 `HistoryScreen` 作为 fallback；MCM 开关默认关。
- 验收：H 打开；目录/正文同步；长文滚动；关闭后输入恢复。

### Phase 3：入口切换与验证

- H 键按 MCM 开关选择 CEF/原生；O/M/F8 互斥。
- 验证密陈权限、字体大小、缺失文件、CEF 初始化失败、net6 编译。
- 打包验证 CEF 原生文件 + WebUI 资源。
- 验收通过后把 MCM 默认改为开启。

### Phase 4（可选）：整屏 Web

- 如果正文面板效果明显优于原生，再评估把目录也移入 CEF。
- 需要重新处理列表滚动、选中态、键盘导航、错误态；没有文本搜索前才考虑。
- 不作为第一版承诺。

## 10. 性能预算

- CEF 正文面板 960×620：约 2.38MB/帧；15fps ≈ 36MB/s，30fps ≈ 71MB/s（低于当前 1280×720/30fps 的 110MB/s）。
- 只读页面用 15fps 足够；选中条目时才更新内容，滚动由 CEF 页面处理。
- 不新增每帧 C# 数据查询；目录只加载一次。
- 若 CEF 面板仍卡顿，可降 15fps / 降低面板分辨率，不影响原生 fallback。

## 11. 测试 / 验收

- 目录：本纪/世家/列传/编年史/纪事/政务文献/密陈分组顺序与原生一致。
- 正文：所有现有文件可打开；超长文、空文件、缺文件、无权限文件有明确状态。
- 字号：MCM 修改后 CEF 页面生效；重启/重开仍生效。
- 入口：H 开/关；O/M/F8 与史书互斥；关闭后鼠标/键盘恢复。
- 回退：删除/改名 `libcef.dll` 或模拟 CEF 初始化失败 → 仍能打开原生史书。
- net6/Xbox：CEF 代码 `#if NET472` 不参与编译，原生史书仍可用。
- 打包：zip 包含 `LICENSES`、WebUI、CEF 原生文件；无 x86/pdb。
- 性能：1280×720 桌面下打开/翻页/滚动无主线程红字；连续开关 20 次无泄漏。

## 12. 风险与止损

| 风险 | 缓解 |
|---|---|
| 原生史书回退被破坏 | Phase 0 只抽数据；原生 UI 不动；MCM 默认关 |
| 两套目录解析不一致 | 共用 `HistoryArchive`，删除 ViewModel 内重复逻辑 |
| 密陈权限泄漏到页面 | 目录生成阶段就过滤；页面只拿已授权 id |
| 长文/特殊字符 | `textContent`；Chrome 预览真实文件；空行/【】解析测试 |
| CEF 初始化失败/Steam 外环境 | 自动回退原生；状态提示；net6 不加载 |
| 全屏 Web 目录没有键盘/搜索 | 第一版保留原生目录；整屏 Web 仅可选 Phase 4 |
| CEF 包体积 | 已是既有 395MB 依赖；史书不新增原生库 |

**止损线**：如果 Phase 1 的正文排版在真实长文上并不明显优于现有 RichText，停止迁移，保留原生界面；不要为了“用 CEF”而重写一个已经能用的史书。

## 13. 预计工作量

- Phase 0：0.5 天（数据抽离 + 原生回归）
- Phase 1：0.5–1 天（页面/长文预览）
- Phase 2：1–1.5 天（原生目录 + CEF 面板 + 桥）
- Phase 3：0.5–1 天（入口/回退/打包/验证）
- Phase 4：另议（整屏 Web 才需要）

建议按 **Phase 0 → 1 → 2 → 3** 顺序做，每阶段结束都能独立验收，不把原生史书弄坏。

## 14. 需要用户确认的决策

1. 是否接受“**原生目录 + CEF 正文**”作为第一版，而不是立刻整屏 Web。
2. MCM 开关默认关闭、验证后开启，是否接受。
3. 第一版是否只做正文只读（不做页面内搜索/翻页），搜索继续原生。
