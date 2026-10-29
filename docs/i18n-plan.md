# 中英双语（i18n）长线方案

> 状态：**规划，不实现**。当前版本仍是完全中文体验。本文只记录架构方向，避免日后重复讨论。
>
> 定位：中文为主、英文为辅；不做全语言全球化，后续语言靠社区维护。

## 1. 目标与非目标

**目标**

- MCM 增加「界面语言」：Auto（跟随游戏）/ 中文 / English。
- 提示词、MCM 文案、左下角提示、聊天/书信界面、史书阅读页可按语言切换。
- LLM 输出语言跟随玩家语言；史官文风随语言切换（中文《资治通鉴》白话史笔 / 英文编年史笔法）。
- 每个战役记录「创建语言」，旧存档不被强制翻译。

**非目标**

- 不翻译已有战役中的旧史文、旧日记、旧聊天记录。
- 不为每种语言重写游戏机制或 tool schema。
- 不引入在线翻译服务作为运行时依赖。

## 2. 关键约束

1. **提示词是语义资产，不只是字符串**：historian_rules.txt、self_review_rules.txt 等包含中文语境（天命、谏言、诏令）。英文版不是逐字翻译，而是「同一机制的英文表达」，需要单独维护。
2. **工具描述继续用英文**（模型训练量更大，见 docs/architecture.md 第 14 节），但工具**返回值**需要按界面语言本地化，否则英文玩家会看到中文提示。
3. **既有战役数据是中文**：persona、日记、史文、谏言都是中文文本。语言切换只影响新生成内容与界面，不回写旧文件。
4. **CEF 页面**只加载本地资源；i18n 词典随 _Module/WebUI/ 打包，不远程拉取。

## 3. 建议架构

### 3.1 语言解析

新增 Core/Localization/LanguageService.cs：

- Current：zh-CN / en-US；
- 解析顺序：MCM 显式设置 > 游戏语言 > 默认 zh-CN；
- CampaignLanguage：进档时从战役 campaign_meta.json 读取；没有则按当前语言写入；
- T(key, args)：C# 文案查表；
- PromptPath(fileName)：按语言返回提示词路径，缺失时回退中文。

### 3.2 提示词目录

    _Module/Prompts/
    ├── zh-CN/                 # 现有中文文件迁入（或保留在根目录作为 zh 回退）
    │   ├── historian_rules.txt
    │   └── ...
    └── en-US/
        ├── historian_rules.txt
        └── ...

- 短期兼容：根目录文件继续作为 zh-CN 的 fallback，新增 en-US/ 只放已翻译文件；PromptManager 先找 {lang}/{file}，再找根目录。
- 战役覆盖：Campaigns/{campaign}/{lang}/... > _Module/Prompts/{lang}/... > 根目录。
- Campaigns/{campaign}/campaign_meta.json 记录 language 与 schema 版本；旧档无此文件按中文处理。

### 3.3 输出语言

- ContextBuilder 在稳定前缀末尾注入一行：请使用{语言}回复 / Respond in English.（不放进易变块，避免破坏前缀缓存）。
- persona 生成提示词按语言选择；已生成 persona 不重写。
- game_rules / world_info 的英文版单独维护，不机翻。

### 3.4 UI 文案

- 原生：MCM 设置标签、按钮、InformationManager 消息、聊天/书信/史书屏幕。
  - 建议 _Module/Localization/strings_zh-CN.json / strings_en-US.json，启动时加载为字典；MCM 属性使用 key（MCM 支持 {=key} 本地化语法时优先用官方机制）。
- CEF：页面从 C# 注入 lang 与词典（window.__game.render({ lang, strings })），或构建时把 strings_{lang}.json 作为 script 引入；页面内固定文案改为查表。
- 史书页的 articleMeta、目录标题、错误提示都要走词典。

### 3.5 史官文风

- 中文：现有《资治通鉴》白话史笔，Markdown 结构不变。
- 英文：chronicle register（简洁过去时、按季节分节、> The Historian Says: 评语），同样输出 Markdown。
- 文件名/体例（本纪/世家/列传…）是代码枚举，**保持中文内部标识**，显示名按语言映射，避免破坏已有目录解析。

### 3.6 工具返回值

- 工具定义（JSON schema）保持英文，不随语言变。
- 工具执行结果里的玩家可见文本（如「已写入史册」）按 LanguageService 本地化。
- 返回给 LLM 的结构化字段（ID、数字、枚举）不变；只翻译人类可读后缀。

## 4. 实施顺序

1. **基础**：LanguageService + MCM 界面语言 + campaign_meta.json 语言记录 + 词典加载。
2. **UI**：MCM/原生屏幕/CEF 页面文案抽 key；中文先跑通，再加英文词典。
3. **提示词**：先迁 historian 四件套（规则/年度/列传/专题）与 conversation/letter，再扩到 self_review 等。
4. **输出语言**：ContextBuilder 注入语言指令；persona 生成按语言。
5. **史官英文文风**：英文 prompt + 样例，单独回归。
6. **社区化**：语言包目录规范、缺 key 回退、贡献指南。

## 5. 验收

- 中文模式与现版本行为完全一致（回归既有中文档）。
- 英文模式下：MCM、聊天、书信、史书目录/正文、左下角提示均为英文；LLM 回复为英文。
- 旧中文档切英文：旧史文/日记保持中文可读，新史文按英文生成；不崩溃、不丢文件。
- 缺失翻译 key 时回退中文并在调试日志记录，不显示空白。
- 提示词缺失 en-US 文件时回退根目录中文，不阻塞进档。

## 6. 风险

| 风险 | 缓解 |
|---|---|
| 英文提示词语义走样（天命/谏言等） | 单独维护、人工审校，不逐字机翻 |
| 旧档数据混合语言 | 战役级语言记录 + 不回写旧文件 |
| MCM 文案 key 迁移破坏旧配置 | 保留现有字段名/顺序，只换显示文案；旧配置可读 |
| CEF 页面文案遗漏 | 词典集中管理 + ci-health 检查 key 覆盖 |
| 维护成本翻倍 | 先做 UI + 史官最小集，社区按语言包补充 |
