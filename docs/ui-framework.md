# UI 框架（原生 Kit + Web 内容层）

> 目标：为 AIChronicle 搭一套**可复用、AI 友好、主题一致**的 UI 基础，让后续新增机制（朝堂、投票、外交账本、人物档案等）只写"内容"，不再手调按钮大小、间距和颜色。
>
> 本 session 只做 UI 与 UI 框架，不实现游戏机制。原版界面（背包/队伍/地图/菜单）保持不动。

## 1. 范围

- **做**：原生 code-first Kit（组件、主题、屏幕基类、Gallery）、CEF live 页面宿主（CefSharp/CefBridge/数据注入/输入）、文档。
- **不做**：聊天/书信/秘书处的重写；原版界面替换；游戏机制（朝堂/投票/外交规则）。
- **原则**：交互归原生，氛围归 Web；一个机制 = 一个数字 + 一张账 + 一个动词 + 一条入史。

## 2. 分层

| 层 | 负责 | 技术 |
|---|---|---|
| 交互层 | 聊天、书信、秘书处、表单、设置、决策按钮 | 原生 Gauntlet + `UI/Kit` |
| 内容层 | 史书、朝堂报告、投票结果、档案、账本 | HTML/CSS → CEF live 帧 → 纹理（全幅 1280×720 / 史书窗口 1100×700 / 30fps） |
| 外壳 | 窗口、关闭、翻页、滚动、加载/失败态 | 原生 Kit |

## 3. 原生 Kit

### 3.1 目录

```
UI/
├── Kit/
│   ├── KitTheme.cs        # token：间距/字号/颜色/笔刷
│   ├── KitWidgets.cs      # 叶子组件：Text/Button/Badge/Divider/Spacer...
│   ├── KitContainers.cs   # 容器：Panel/Card/Row/Column/Scroll/FormRow
│   ├── KitRoot.cs         # 根节点
│   ├── KitScreen.cs       # 屏幕基类（GauntletLayer/LoadMovie/清理）
│   ├── KitTexture.cs      # 动态纹理组件（Web/CEF 帧）
│   └── KitGalleryScreen.cs# 组件画廊
├── Web/
│   ├── CefWebHost.cs      # CEF 初始化 / OffScreen 会话 / JS 桥
│   ├── CefWebScreen.cs    # 通用 CEF 页面宿主（数据注入/输入/PageAction）
│   └── WebPage*.cs        # render-once PNG 脚手架（保留）
└── Screens/               # 具体屏幕（逐步迁入）
```

### 3.2 主题

- 尺寸：4/8/12/16/24/32（`KitTheme.S1..S6`）。
- 字号：标题 30 / 小标题 22 / 正文 17 / 注释 14。
- 色板：羊皮纸 `#F2E6CF`、墨 `#1C1814`、金 `#C9A227`、血红 `#8C3A3A`、灰 `#B89B72`。
- 文本笔刷：`_Module/GUI/Brushes/Kit.xml`（`Kit.Text.*` / `Kit.Button.Text`），可热重载；文件缺失时回退到 C# token 克隆 `ChatLog.Text`。
- 按钮/面板笔刷：运行时克隆 `ButtonBrush2` / `ButtonSimpleBrush`，颜色由 `KitTheme` token 控制。
- 改主题顺序：先改 `Kit.xml` 看效果，需要新语义色再加 `KitTheme`。

### 3.3 组件目录

| 组件 | 用途 | 关键约束 |
|---|---|---|
| `Kit.Text` | 文本 | 只能选 `KitTextStyle`，不写字号/颜色 |
| `Kit.Button` | 按钮 | 不写宽高；宽度由组件估算或 token 给 |
| `Kit.IconButton` | 图标按钮 | 固定方形 token |
| `Kit.Badge` | 状态标签 | 颜色由语义给 |
| `Kit.Divider` | 分隔线 | 高度 token |
| `Kit.Spacer` | 间距 | 只用 S1..S6 |
| `Kit.Panel` | 面板 | 背景/内边距由组件处理 |
| `Kit.Card` | 卡片/对话框 | 宽度 token，高度包内容 |
| `Kit.Row` / `Kit.Column` | 排列 | 间距由 gap 参数 |
| `Kit.Scroll` | 滚动区 | 内部自动建 ClipRect/Scrollbar |
| `Kit.FormRow` | 设置项 | label + control + hint |
| `Kit.Toggle` | 开关 | 点击回调 |
| `Kit.Slider` | 滑块 | min/max/value + 回调 |
| `KitTexturePanel` / `KitTextureWidget` | 动态纹理 | Web 页面/图片；由 `Kit.Scroll` 包裹滚动 |

### 3.4 屏幕基类

```csharp
public sealed class CourtScreen : KitScreen
{
    protected override void Build(KitRoot root)
    {
        var ctx = Context!;
        root.Scrim();

        var card = Kit.Card(ctx, KitTheme.DialogWidth);
        card.Add(Kit.Header(ctx, "朝堂",
            Kit.Button(ctx, "关闭", Close, KitButtonStyle.Ghost)));
        card.Add(Kit.Divider(ctx));

        var row = Kit.Row(ctx);
        row.Add(Kit.Text(ctx, "……", KitTextStyle.Body));
        row.Add(Kit.Button(ctx, "准", OnApprove));
        card.Add(row.Root);

        root.Add(card);
    }
}
```

- `KitScreen.Open()`：建 `GauntletLayer`、`LoadMovie("KitHost")`、拿 `RootWidget`、调 `Build`。
- `Close()`：释放 movie、移除 layer、清理纹理。
- `KitScreen.AnyOpen`：给 O/M/H 等全局快捷键做互斥。
- 所有屏幕共用 `_Module/GUI/Prefabs/KitHost.xml`（5 行）。

### 3.5 规则（AI 必读）

1. 新屏幕禁止直接写 `SuggestedWidth/Height`、`Margin*`、颜色、字号。
2. 只能用 `Kit.*` 组件和 `KitTheme` token；需要新尺寸时先加到 `KitTheme`。
3. 屏幕只写 `Build()` 和数据绑定；不写 `GauntletLayer`/`LoadMovie` 样板。
4. 动态列表用 `Kit.Scroll(...)` + 重建内容（v1 不做虚拟化/数据绑定）；不要依赖 XML 数据绑定。
5. 文本输入（聊天/书信）继续用现有原生屏幕，不要迁到 Web。
6. 每个新屏幕必须能在 Gallery 里找到对应组件和状态。

### 3.6 Gallery

- 调试键 **F8** 打开/关闭 `KitGalleryScreen`（仅大地图）。
- 展示：标题/正文/注释、按钮四态、Badge、Divider、Toggle、Slider、滚动列表、FormRow。
- 底部 CEF 区：打开 CEF 演示页 / CEF 史书页；先通过 F8 的 Gallery 入口再进 CEF。
- 视觉回归：1080p / 1440p / 4K 各看一遍；改主题先看 Gallery。

### 3.7 快速开始

1. 编译部署：`dotnet build -c Release -f net472`；CEF 原生文件由 `CopyCefRuntimeToModule` 自动复制，首次部署约 395MB。
2. 进游戏、回到大地图，按 **F8** 打开 Gallery；再按 F8 或"关闭"退出。
3. 新增屏幕：在 `UI/Screens/` 建一个 `KitScreen` 子类，实现 `Build`；在入口处 `new YourScreen().Open()`。
4. 调主题：只改 `KitTheme` token；需要新尺寸/颜色先加 token，不要写进屏幕。
5. Web 预览：直接用 Chrome 打开 `_Module/WebUI/preview.html`；改 CSS/HTML 刷新即可，不需要进游戏。
6. CEF 演示/编年史：F8 →「打开 CEF」/「打开 CEF 史书」（示例页）；全 Web 史书：MCM 开启「史书使用 CEF 阅读页」后按 H。

## 4. Web 内容层

详见 [ui-web.md](ui-web.md)。摘要：

- **CEF live**：CefSharp OffScreen 140.1.140 / CEF 140，30fps，BGRA 帧 → RGBA 引擎纹理；演示/编年史页 1280×720，史书窗口 1100×700。
- 游戏 → 网页：`window.__game.render(json)`；网页 → 游戏：`aiChronicle.dispatch(id, payload)` → `CefWebScreen.PageAction`。
- 鼠标点击/悬停/滚轮回传；文本输入/IME 仍走原生。
- 依赖体积约 395MB，仅 net472/Steam；net6/Xbox `#if NET472` 排除。
- render-once PNG 脚手架（`WebPageService`）保留，但 CEF live 页面不走缓存。

## 5. 迁移顺序

1. `KitGalleryScreen`（本 session）
2. `LetterListScreen`（列表简单，验证 List/Row/Button）
3. `HistoryScreen` 全 Web 史书（已完成：1100×700 窗口 + 全 Web 目录/正文 + 原生 fallback；`HistoryArchive` 已抽离，正史 Markdown 渲染）
4. 新屏幕（Court/Diplomacy/Dossier）直接用 Kit
5. `AIChatScreen` 只换主题/笔刷，不重写逻辑

## 6. 测试与验收

- `dotnet build -c Release -f net472` 通过。
- F8 能打开 Gallery；关闭后输入恢复正常。
- Gallery 在 1080p/1440p/4K 无溢出、无文字截断。
- 快捷键 O/M/H 与 Gallery 互斥。
- 无新增运行时依赖（CefSharp/CEF 已批准，属于 UI 运行时；net6 不加载）。

> **本 session 状态**：已实机验证——Gallery 可开、滚动区/动态纹理正常；CEF 初始化约 0.8s、首帧约 1.6s；CEF 史书颜色、鼠标点击、桥接、滚动均通过；**1280×720 / 30fps 与史书 1100×700 窗口均实机通过，无明显卡顿**。当前限制仍是每帧整张上传，不如浏览器 60fps 顺滑。

## 7. 风险

- Gauntlet 布局怪癖（Spacer/Grid/Scroll）需在 Gallery 里验证；组件内部封装。
- 字体/DPI 缩放：用 `UIContext.Scale`/token，4K 需实测。
- 动态纹理无 in-place update：CEF live 只能整帧上传；30fps 实机可用，60fps/共享纹理是后续优化。
- 聊天输入/IME 敏感：不迁 Web。

## 8. 待办（下一 session）

- 史书收尾：边界/fallback 回归、清晰度（`HistoryArchive` 已抽离，见 `plan-history-cef.md`）。
- CEF 60fps 压测；全幅铺满/1440×810；共享纹理桥评估。
- 页面 `dispatch` 动作与真实决策/投票流程对接（游戏功能 session）。
- 把按钮/面板等更多主题 token 迁到 `Kit.xml` 做热重载（文本样式已迁）。
- 迁移 LetterList，补 Gallery 实机截图。

## 9. 框架成熟度结论

**基本成形，可以承接下一批界面。** 但还不是“完全硬化”：

已完成：

- 原生 Kit：主题 token、文本/按钮/Badge/Divider/Toggle/Slider/FormRow、Panel/Card/Row/Column/Scroll、KitScreen、F8 Gallery。
- CEF：CefSharp 140/CEF 140、OffScreen 30fps、BGRA→RGBA、窗口/全屏宿主、数据注入、双向桥、鼠标点击/悬停/滚轮、窗口模式。
- 真实页面：演示页、编年史页、全 Web 史书（目录 + 正文，1100×700，MCM 开关，原生 fallback；正史 Markdown 渲染）。
- 史书数据层：`HistoryArchive` 统一目录/分组/权限/正文；原生与 CEF 共用。
- 测试：`tests/AIChronicle.Pure.Tests`（目录/权限/读取）+ `tests/web`（Markdown 渲染，Node DOM 桩），无需游戏 DLL。
- 构建/部署：x64、CEF 原生文件复制、全量打包、第三方许可、文档。
- 实机：CEF 初始化/首帧、颜色、桥接、滚动、窗口模式均通过。

还未完成/不应假设：

- 史书数据已抽到 `HistoryArchive`，双入口共用目录/权限/正文；但边界场景仍需进游戏回归。
- CEF 仍是固定位图 + 每帧整张上传；30fps 可用，60fps/共享纹理未验证。
- 只有 net472/Steam；net6/Xbox 只能走原生。
- 页面不处理文本输入/IME；聊天/书信不迁 Web。
- 没有自动化 UI/像素回归；Gallery 仍有诊断模式；CEF 包约 395MB。
- 史书边界已有单测；进游戏回归（超长文、滚动、字号、CEF 失败回退）仍待做。

结论：**布局/主题/屏幕/CEF 页面链路已经成形，可作为后续朝堂/投票/档案的 UI 基础；数据抽离已完成，下一阶段做进游戏回归，再开新页面。**
