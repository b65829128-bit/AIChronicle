# MyFirstMod — Bannerlord 模组开发文档

## 文档维护规则（AI 必读）

> ⚠️ **最重要的规则：每次修改代码后，必须立即检查并更新文档！不更新文档视为未完成工作。**

你有权修改本文档（AGENTS.md），但必须遵循以下条件：

1. **修改前，必须向用户（yangui）明确说明**：
   - 你要改哪一章节
   - 为什么需要改（例如：新增了项目依赖、发现更好的模式、目录结构变了）
   - 具体的改动内容（用 diff 形式或文字描述）
2. **得到用户明确同意后，才能执行修改**。
3. **修改完成后，口头告知用户改动已完成**。

此规则适用于本文档（AGENTS.md）以及 **README_MOD.md**（模组功能文档），不适用于普通代码文件（代码的增删改按正常开发流程，无需额外审批）。

### 必须更新文档的情况（强制）

以下任一情况发生，**必须在代码修改完成后立即更新 README_MOD.md 和/或 AGENTS.md**，不得等到用户提醒：

| 代码变更 | 需要更新的文档 |
|---------|--------------|
| 新增/删除功能 | README_MOD.md（功能描述、使用方法） |
| 新增/修改 MCM 配置项 | README_MOD.md（设置面板表格） |
| 新增/删除/重命名文件 | README_MOD.md（文件结构）、AGENTS.md（目录结构） |
| 改变架构或入口模式 | AGENTS.md（对应章节） |
| 新增依赖或 NuGet 包 | AGENTS.md（环境概况） |
| 改变默认值或行为 | README_MOD.md（对应功能描述） |
| 新增 UI 界面或交互流程 | README_MOD.md（功能描述、使用方法） |
| 修改 API 调用方式或参数 | README_MOD.md 和 AGENTS.md（如有相关说明） |

**禁止行为：** 完成代码变更后直接结束，等待用户提醒才去更新文档。文档维护是开发工作的一部分，不是可选的。

### 代码修改后文档自检清单（每轮必须过一遍）

代码修改完成后，对照以下清单逐项检查：

```
[ ] 新增/删除了文件？          → 更新 README_MOD.md 文件结构 + AGENTS.md 目录结构
[ ] 新增/修改了功能？          → 更新 README_MOD.md 功能描述/使用方法
[ ] 改变了 MCM 配置项？        → 更新 README_MOD.md 设置面板表格
[ ] 改变了架构或入口模式？     → 更新 AGENTS.md 对应章节
[ ] 新增了 NuGet 包或依赖？    → 更新 AGENTS.md 环境概况
[ ] 修改了默认值或行为？       → 更新 README_MOD.md 对应描述
[ ] 新增了 UI 或交互流程？     → 更新 README_MOD.md 功能描述 + 使用方法
```

**全通过才算完成工作。**

**交叉参考：** 在实现具体功能前，先读 **README_MOD.md** 了解当前模组有哪些功能、UI 入口在哪、配置项有哪些。不要仅凭 AGENTS.md 做技术决策——两份文档一起看。

---

## 开发工作流改进（AI 必读，从实际踩坑中总结）

### 1. 需求先对齐，再动手

涉及 **UI 交互入口**、**功能行为**、**用户体验** 的改动，先列出多种方案让用户选择，**不要自行判断哪个更好**。

> 反例：AI 自行判断「遭遇菜单入口比对话入口更合理」并直接实现，结果用户想要恰恰相反。
> 正例：先问「AI 聊天入口放在遭遇菜单还是对话选项中？各有什么利弊」，等用户回复后再编码。

### 2. 在关键路径上预先埋日志，不要失败后再猜

涉及 **新 UI、新屏幕、异步操作、跨系统调用** 的功能，在以下节点预埋 `InformationManager.DisplayMessage`：
- 入口函数被调用时（参数打印出来）
- 关键操作前后（如 `LoadMovie`、`AddLayer`）
- 异常捕获处

这样第一次出问题就能直接定位，不必反复打补丁。

### 3. 写 GauntletUI 前，先拆解游戏已有的同类 Prefab

游戏自带大量 GauntletUI XML 文件（`Modules/Native/GUI/Prefabs/`、`Modules/Multiplayer/GUI/Prefabs/`）。实现类似功能时：
1. 找到游戏里**行为最接近**的现有 Prefab（如聊天 → SPChatLog.xml，弹窗 → FullScreenNotice.xml）
2. 逐段分析它的 Widget 层级、`DoNotAcceptEvents` 逻辑、`InputRestrictions` 设置
3. 基于现有模板修改，不要从零写

> 典型踩坑：`DoNotAcceptEvents="true"` 的含义是「本 Widget 不接事件，但透传给子级」——这个语义如果不先看 SPChatLog 源码，纯靠试错至少浪费两轮。

---

## 架构概览（必读）

### 核心理念

本模组采用 **Agent 驱动架构**，受 opencode 设计启发。不是"玩家 ↔ LLM 对话"，而是：

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

### 关键设计

| 原则 | 说明 |
|------|------|
| **Entity 平等** | 玩家和所有 NPC 统一为 Entity，由 EntityController（Human/Agent）区分 |
| **动态 Context** | ContextBuilder 根据当前交互双方动态组装系统提示词 |
| **能力过滤** | 每个 Entity 有 EntityCapability 集合，工具列表按能力自动过滤 |
| **书信模式** | 支持书信 intent，O 键唤起书信往来面板（统一线程列表 + 未读角标） |
| **文件即知识库** | NPC 的记忆、目标、对目标的认知都是文件，Agent 通过 `read_file`/`write_file`/`append_file`/`edit_file`/`delete_file`/`move_file` 精确读写 |
| **信息隔离** | 每个 NPC 只能操作自己目录下的文件 + `World/`，不知道其他 NPC 和玩家的对话 |
| **工具定义文件化** | 65 个工具定义在 `tools.json`（50 个游戏工具）和 `agent_tools.json`（15 个文件/通信工具，含 `submit_advisory`/`submit_edict`/`consult_king`）中，热重载，不硬编码；两文件缺失时回退内嵌最小工具集 |
| **工具分类系统** | 每个工具归属 8 个分类之一（universal/query/social/movement/military/diplomacy/file/communication），Agent 按场景默认激活相关分类，需要其他分类时调用 `browse_tools` 元工具按需解锁。**玩家发起的聊天（conversation）是全功能通道——全部分类默认激活**（AI 几乎不主动聊天，绝大多数对话由玩家发起，若工具不全则对话里达成的承诺无法兑现；能力门控照旧，国王专属工具仍只有国王拿到） |
| **提示词全部可编辑** | `system_prompt.txt`、`agent_system.txt`、`persona_generation.txt`、`context_template.txt`、`chancery_rules.txt` 均为文件，战役创建时自动复制到战役目录，热重载优先读战役目录 |
| **多轮工具调用** | `SendMessage` 内建 SSE 流式循环，模型调用工具 → 执行 → 追加结果 → 重请求，直到模型自然停止（无轮数限制，仅保留极高安全阀防死循环） |
| **主线程分发** | LLM 工具循环跑在后台线程，但**所有修改游戏状态的工具**经 `MainThreadExecutor` 排队到主线程 `OnApplicationTick` 执行（后台线程阻塞等待结果）；仅 `request_gold`/`request_items`/`browse_tools` 留在后台线程（前两者需主线程弹窗等待玩家，后者改本流程上下文）。背景：Bannerlord 游戏对象（MobileParty/Hero/Kingdom）是主线程独占的 |
| **上下文隔离（AsyncLocal）** | `CurrentHero`/`CurrentIntent`/`ActivatedCategories`（AIChatClient）、`_agentEntityId`/`_targetEntityId`（AgentManager）、`_activeAgentId`/`_activeTargetId`（EntityManager）均为 `AsyncLocal`——聊天与后台信件/谏言/外交多个流程并发时上下文互不覆盖；实体缓存用 `ConcurrentDictionary`（线程安全） |
| **秘书处** | M 键打开，玩家的个人行政助手。固定 persona（无条件服从），不读玩家 persona。国王/封臣/平民均可使用，可用工具取决于玩家当前身份。玩家可经秘书处调 `submit_advisory` 提交公开谏言（雇佣兵除外） |
| **天意** | 虚拟实体（ID: `__fate__`，HeroRef=null，与史官并列）。家族补充系统：封臣/雇佣兵家族低于下限时被激活，决定新家族名称/文化/投效势力（`create_clan`，能力门控仅天意可用），成员程序生成、等级 2、族长带兵，入原始史料但不激活史官 |
| **提示词人称统一** | 上下文只出现「你」(Agent 自己) 和「对方」(交互对象) 两角色，"TA"等模糊指代全部禁用 |

> **AI 聊天对话入口**：`LordChatBehavior` 在 `hero_main_options`（普通领主对话）与 `player_responds_to_surrender_demand`（被强敌擒获的投降/应战对话）两个节点注册【AI 聊天】选项。后者让被擒玩家可谈判，agent 可用 `let_go` 放行；`AIChatScreenVM` 在回复完成后检测 `PlayerEncounter.LeaveEncounter` 为真时自动关闭聊天并 `EndConversation()` 结束对话（对话中遭遇不会自动 tick 结束），玩家获释。

> **提示词同步规则（newer-wins）**：基础目录 `_Module/Prompts/` 是模板源，战役创建时复制到 `Campaigns/{战役}/`。每次进档（`OnSessionLaunched` → `PromptManager.StartCampaign`）会做同步——**只要基础目录文件比战役副本新，就覆盖战役副本**。玩家在战役副本上的手动编辑（时间戳更新）会被保留。
>
> **上下文模板结构（缓存优化）**：`context_template.txt` 以 `<!--VOLATILE-->` 标记分界。标记前为**稳定前缀**（身份/persona/世界背景/游戏规则/工具清单/行为守则）→ system 消息；标记后为**易变块**（当前时间/自身状态/对对方认知/目标/客观关系/内政报告）→ 单独作为【当前状况】user 消息插在最新消息前。目的：让 system+历史构成逐字节稳定的前缀，最大化 DeepSeek 前缀缓存命中（命中输入 0.02元/M vs 未命中 1元/M，价差 50 倍）。`ContextBuilder` 相应提供 `BuildStable`/`BuildVolatile`（合并 `Build` 保留兼容）。**史官 intent 例外**：不拆易变块，走合并 `Build()` 保持单一 system 消息——文笔是模组核心，结构与旧版一致。旧版模板（无标记）整体按稳定处理，行为不变。
>
> **已知边界情况（待解决）**：如果玩家在战役副本自定义了某文件（副本时间戳更新），之后基础目录又修改了（基础更晚）→ newer-wins 会覆盖副本，玩家自定义丢失。当前对开发期是期望行为；未来如需保护战役自定义，需引入"用户修改标记"机制（如哈希比对或 .custom 标记文件）。
>
> **运行期热重载**：游戏运行中，编辑战役副本文件立即生效（加载器按 LastWriteTime 重新读取）；编辑基础目录要下次进档才同步。

### 两个模型

| 模型 | 用途 | 提示词来源 |
|------|------|-----------|
| Agent 模型 | 思考、读写文件、调工具、决定回什么 | `context_template.txt` → ContextBuilder 动态组装 |
| 前台模型 | 把 Agent 给的上下文变成自然对话 | 极简，不带工具，只做角色扮演 |

### 信息隔离规则（硬约束）

| 当前 NPC 可访问 | 权限 |
|---------------|------|
| `NPCs/{自己}/persona.txt` | 只读 |
| `NPCs/{自己}/character.json` | 只读 |
| `NPCs/{自己}/knowledge/{entity_id}.txt` | 读 + 写 + 追加 + 编辑 + 删除 |
| `NPCs/{自己}/relationships/{其他}.txt` | 读 + 写 + 追加 + 编辑 + 删除 |
| `NPCs/{自己}/goals/*.txt` | 读 + 写 + 追加 + 编辑 + 删除 |
| `NPCs/{自己}/chat_logs/*.txt` | 只读（不可修改） |
| `NPCs/{自己}/decisions/*.txt` | 读 + 写 + 追加 + 编辑 + 删除 |
| `NPCs/{自己}/mailbox/**` | 已废弃（仅旧档遗留，不再写入） |
| `World/factions.txt` | 只读 |
| `World/settlements.txt` | 只读 |
| `World/history/**`（原始史料/编年史） | 只读 |
| `World/advisory/`（封臣公开谏言） | 只读：史官可读任何国家；其他 agent 仅本国 |
| `World/edict/`（国王诏令） | 只读：史官可读任何国家；其他 agent 仅本国 |
| `World/secret_advisory/`（秘密谏言） | 只读：**仅本国国王**；本国封臣与史官不可读 |
| `World/diplomacy/consults/`（国王外交问询） | 只读：史官可读任何国家；参与双方国王可读；第三方不可读 |
| 其他 NPC 的任何文件 | **禁止** |

> 玩家的实体目录（`{Name}_main_hero`）含 `thread_read_state.json`（各线程已读水位，O 键未读角标依据）——仅玩家可写，Agent 在信息隔离下不可访问。

### 扩展方式

新工具需要以下步骤：

1. `tools.json` 或 `agent_tools.json` 中加一条工具定义，**必须指定 `category`**（遵循 opencode 风格的 `description` 格式）
2. `ToolExecutor.ExecuteToolCall` 的 `switch` 中加一个 `case`
3. 如果工具涉及部队行为（移动、劫掠、围攻等），还需：
   - 在 `Execute*` 方法中注册 `PendingAction`（通过 `PartyBehaviorManager.GetOrCreateAction` 设置 `Behavior` 和目标）
   - 在 `PartyBehaviorManager.Tick()` 的 switch 中添加对应行为的恢复/清理逻辑
   - 对于持续性行为（驻防、巡逻、护送），设置 `CheckInHours` 以启用定时签到
4. `AIChatScreenVM` 的 toolCall 显示 switch 中加对应描述
5. `ContextBuilder.CapabilityToolMap` 中按能力映射（若需能力过滤）

`SubModule.OnApplicationTick` 已配置为每帧调用 `PartyBehaviorManager.Tick()`。

#### 新增 Entity 类型

1. 在 `Entity.cs` 的 `EntityCapability` 枚举中添加新能力
2. 在 `EntityManager.ComputeCapabilities()` 中为新 Entity 类型计算能力集合
3. 在 `ContextBuilder` 的 `CapabilityToolMap` 中建立能力→工具映射
4. 在 `tools.json` 的 `parameters.properties` 中添加 `capability_required` 字段

### 提示词设计规范（人称约定）

所有提示词文件必须遵守统一的人称约定，防止 Agent 混淆自己与对方的信息：

- 「你」= Agent 自己（如"你是拉盖娅，现任南帝国女皇"）
- 「对方」= 正在交互的目标 Entity（如"对方是炎萌"）
- 「该人物」= `query_character` 返回结果的前缀（如"该人物：拉盖娅"）
- 禁止出现「TA」、「他/她」、「其」等模糊指代

此约定适用于 `context_template.txt`、`agent_system.txt` 以及所有新增的工具返回格式。

### 提示词克制原则（模糊化，最核心的提示词设计原则）

提示词**只引导、不规定**——让 LLM 自行判断，保持涌现与多元。写任何提示词时对照以下几条：

- **不列举具体场景/类别清单**：会框死 LLM 的判断，让行为模板化。例如秘密谏言说明不写「私议王上、密报、密约」，而写「你认为不适合被历史记录或旁人知晓的事」
- **用开放性表述**：「若你觉得…」「是否如此由你的性格与处境决定」，而非「必须/应当/禁止」的强制清单
- **个人立场不强制度**：天命信仰、名分、谏言批判等，由人格与处境自然涌现，不写成统一规则（为此 persona 维度用随机掷定而非 LLM 自定，见 AgentManager 天命信仰）
- **不注入上下文**：能靠提示词提醒 LLM 调用工具（如读写 diary、检索），就不要注入内容——注入会温水煮青蛙式稀释所有提示。这条有先例教训：别的 AI 模组因持续注入而系统崩坏
- **游戏规则层是例外**：`game_rules.txt` 注入卡拉迪亚的实际运转机制（机动/金钱/部队上限/兵种/招募/战争/影响力/阵营归属——氏族加入哪国全在族长自决、无须国王准许），让 agent 按游戏机制而非现实经验做决策（如士兵阵亡随时可补，真正约束是钱/上限/兵种等级）。它与「注入叙事/记忆内容」不同——是**稳定参考知识**（同世界背景），不是易变剧情；不规定行为，只说明世界如何运转，反而让行为涌现更准。注入稳定前缀（缓存友好）；**史官不注入**（只编史不做游戏决策）。受 MCM「注入游戏规则」开关控制
- **度**：提示词给方向但不给答案。玩家可编辑的提示词默认按此原则起草

### 记忆系统设计思想：日记即索引

长期记忆的核心突破点是每个 Agent 的 `decisions/diary.txt`——格式 `[年季节日] 类型：内容`（类型：决定/承诺/情报/计策/评价/结果）。

**日记是记忆的索引，聊天记录是全文**：
- 日记时间戳（`[1089春16]`）与 `chat_logs/` 时间戳（`[第1089年，春季第16日]`）**天然对应**
- 回忆路径：先读日记定位某条决定/计策 → 需要细节时 grep 在 `chat_logs/` 搜对应日期或关键词 → 看到完整对话
- 时间戳就是日记和聊天的「外键」，grep 按日期 join——比读整个聊天省 token，比只读日记有上下文

**结果追踪（防止旧计划永悬）**：提示词强制「计策/承诺/计划必须追踪结果」——日记里没有「结果」条目对应的计策/承诺/计划会被当作仍在进行中。规则：
1. 每次对话/审视开始先回顾日记里**没有结果标记**的条目，确认其当前实况
2. 一旦得知某个计策/承诺/计划的结果（成功/失败/中止/改变/已兑现），立即补记 `[年季节日] 结果：…`
3. 一件事的现状以日记里关于它的最后一条记录为准——有了结果就别再当它进行中

**设计优先级**：未来设计记忆相关系统（记忆巩固/当日小结、生活史/生平记事、关键事件自动入忆、FiefReview 审视记录、承诺簿等）时，**优先以日记为锚点**，围绕「日记索引 + 聊天全文」的互证结构展开，不要另起炉灶。日记格式必须保持 `[年季节日] 类型：内容` 一致，否则检索失效。

**为什么是日记而不是注入**：日记是拉取式（agent 自觉读写，见克制原则），不占上下文；注入任何记忆内容都会膨胀上下文、稀释提示。检索工具（grep 的 context_lines/max_results）为此设计。

### 日记权威化与记忆巩固（diary-as-authority，v1.7）

**记忆优先级（解决"看到日记、战略、知识就茫然"）**：查询工具实时数据（query_character/query_world_state，系统权威）> 日记（`decisions/diary.txt`，自我记忆权威）> 认知（`knowledge/{对方}.txt`，对第三方的了解）。长期战略不再单列文件，以日记「战略」类型条目形式存在（strategy.txt 已并入日记）。

**日记可被比它更新的聊天记录修正**：chat_logs 是系统自动写入的客观往来记录（Agent 只读、不会漏），日记是 LLM 自写的索引（可能漏记/滞后）。若 chat_logs/ 中有比日记新的往来，**以聊天记录为准**，并补记日记（旧决定被推翻则补记 `[日期] 结果：…`，只追加不改写旧条目）。

**记忆巩固机制（`MemoryConsolidator.cs`，保底而非依赖自觉）**：
- 自我审视类激活前（**国王政务 KingDiplomacy / 封地审视 FiefReview / 外交问询回应 KingConsult / 封臣进谏 Advisory**），先比较日记最新条目日期与 chat_logs 各文件最新消息日期
- 若日记落后（存在"比日记更新的往来"），跑一次**巩固 pass**：agent 自读日记 + 较新往来 → 把值得记住的决定/承诺/计策/结果/战略 `append_file` 补记进 diary。静默执行，不写 chat_logs、不弹玩家消息
- 只在落后时触发，多数时候零成本；受 MCM「启用记忆巩固」开关控制（默认开）
- 日期解析兼容两种写法：日记 `[1090春3]`/`[1090年冬第9日]`、聊天 `[第1090年，春季第15日]`——日记格式务必保持统一，否则程序无法可靠判断"谁更新"
- 巩固提示词文件化、可热重载：`consolidation_rules.txt`（intent 规则）+ `memory_consolidation.txt`（激活指令）

---

## 环境概况

| 项目 | 值 |
|------|-----|
| 游戏 | Mount & Blade II: Bannerlord v1.4.7 |
| 游戏路径 | `D:\steam\steamapps\common\Mount & Blade II Bannerlord` |
| IDE | JetBrains Rider 2026.2 |
| 开发目录 | `C:\Users\yangui\BLMods\MyFirstMod` |
| .NET SDK | 9.0+ |
| 目标框架 | net472 (Windows) + net6 (Xbox/Store) |
| 模组框架 | Harmony 2.3.3 (运行时补丁) |
| 四前置 | Harmony, ButterLib, UIExtenderEx, MCM (MBOptionScreen) |

## 目录结构

```
C:\Users\yangui\BLMods\MyFirstMod\
├── SubModule.cs              ← 模组入口，生命周期回调
├── MyFirstMod.csproj          ← 项目配置（引用、框架、部署）
├── Settings.cs                ← MCM 设置（API URL、Model、Key、双倍声望开关）
├── AIChatClient.cs            ← LLM API 客户端（HTTP 流式请求、SSE 解析、多轮循环），工具调用委托给 ToolExecutor
├── ToolExecutor.cs            ← 工具执行器（30+ 个游戏工具的具体实现 + browse_tools 元工具）
├── DiplomacyService.cs        ← 外交服务（宣战/议和/结盟/贸易协定/回复提案 + FindKingdom + 盟约/贸易到期记录与清除）
├── PartyBehaviorManager.cs    ← 部队行为管理器（PendingAction 状态机、Tick()、定时签到）
├── AIChatScreen.cs            ← 聊天屏幕管理器（静态类，GauntletLayer 挂载）
├── AIChatScreenVM.cs          ← 聊天 ViewModel（消息列表、输入绑定、function calling 处理）
├── LordChatBehavior.cs        ← 对话中插入聊天选项，战役 ID 管理
├── PromptManager.cs           ← 提示词管理器（文件热重载、战役目录、角色 JSON）
├── AgentManager.cs            ← Agent 管理器（NPC 文件系统、路径权限、LLM 生成 persona）
├── Entity.cs                  ← Entity 抽象（玩家/NPC 统一模型、EntityCapability）
├── EntityManager.cs           ← Entity 生命周期管理
├── ContextBuilder.cs          ← Context 动态组装器
├── LetterListScreen.cs        ← 书信收信人列表屏幕
├── AgentScheduler.cs          ← 信件异步事件驱动调度器 + 每日盟约/贸易到期检测
├── HistoryRecorder.cs         ← 历史记录器（监听游戏事件写入原始史料）
├── MainThreadExecutor.cs      ← 主线程分发器（后台线程的工具执行排队回主线程，防跨线程崩溃）
├── MemoryConsolidator.cs      ← 记忆巩固（diary 权威化保底：自我审视前检测日记落后并补记）
├── DebugLogger.cs             ← 调试日志（LLM 调用摘要/思维链摘录 → 战役 debug_logs/）
├── SafeFileIO.cs              ← 带重试的文件 IO（并发读写同一文件时避免"文件正被使用"异常）
├── CLAUDE.md                  ← Claude Code 入口文档（指向本文件与 README_MOD.md）
├── _Module/
│   ├── SubModule.xml         ← 模组元数据（ID、依赖、DLL路径）
│   ├── GUI/
│   │   └── Prefabs/
│   │       ├── AIChatScreen.xml  ← 聊天窗口布局
│   │       └── LetterListScreen.xml ← 书信收信人列表
│   └── Prompts/
│       ├── system_prompt.txt  ← 系统提示词模板（玩家可编辑，热重载）
│       ├── world_info.txt     ← 默认世界背景
│       ├── game_rules.txt     ← 游戏运转规则（玩家可编辑，热重载）
│       ├── tools.json         ← 游戏工具定义（热重载）
│       ├── agent_system.txt   ← Agent 系统提示词模板
│       ├── agent_tools.json   ← Agent 文件工具定义（热重载）
│       ├── persona_generation.txt ← NPC性格生成提示词（玩家可编辑，热重载）
│       ├── advisory_rules.txt  ← 封臣谏言规则（热重载）
│       ├── fief_review_rules.txt ← 封地审视规则（被夺方激活，热重载）
│       ├── clan_replenishment_rules.txt ← 天意建族规则（家族补充，热重载）
│       ├── consolidation_rules.txt ← 记忆巩固行为规则（热重载）
│       ├── memory_consolidation.txt ← 记忆巩固激活指令（热重载）
│       ├── Templates/         ← NPC 目录模板
│       │   ├── context_template.txt ← Context 模板
│       └── Campaigns/         ← 各战役目录（运行时创建）
│           └── {战役名}/
│               ├── system_prompt.txt    ← 本战役系统提示词（可独立编辑，热重载）
│               ├── world_info.txt       ← 本战役世界背景（可独立编辑，热重载）
│               ├── game_rules.txt       ← 本战役游戏运转规则（可独立编辑，热重载）
│               ├── agent_system.txt     ← 本战役 Agent 提示词（热重载）
│               ├── persona_generation.txt ← 本战役性格生成提示词（热重载）
│               ├── context_template.txt ← 本战役 Context 模板（热重载）
│               └── NPCs/          ← Agent 管理的 NPC 文件系统
│                   └── {entity_id}/                 ← {Name}_{StringId}（如 博泰罗_CharacterObject_1664）
│                       ├── persona.txt   ← [MOTIVATION]/[TRAITS]/[SPEECH_STYLE]
│                       ├── persona_meta.json ← 自定义人格维度（权力欲/归属重心/冒险倾向/天命信仰/战争倾向）
│                       ├── knowledge/
│                       ├── chat_logs/
│                       ├── mailbox/          ← 已废弃（仅旧档首次迁移，不再写入）
│                       │   ├── inbox/
│                       │   └── sent/         ← 实际目录名（此前文档误写 outbox）
│                       ├── relationships/
│                       ├── goals/
│                       └── decisions/
├── BLSource/                 ← 反编译的游戏源码（AI 只读，不参与编译）
│   ├── TaleWorlds.CampaignSystem/   ← 战役逻辑
│   ├── TaleWorlds.Core/             ← 核心类型
│   ├── TaleWorlds.MountAndBlade/    ← 引擎层
│   ├── TaleWorlds.Library/          ← 基础库（MBList, InformationMessage 等）
│   ├── TaleWorlds.Localization/     ← 本地化
│   ├── TaleWorlds.GauntletUI/       ← UI 框架
│   ├── TaleWorlds.Engine.GauntletUI/  ← GauntletLayer
│   ├── TaleWorlds.ScreenSystem/     ← ScreenBase, ScreenManager
│   └── ...（共 50+ 个 DLL 的反编译源码）
└── bin/Release/net472/       ← 编译产物
```

部署后自动复制到：`D:\...\Modules\MyFirstMod\bin\Win64_Shipping_Client\MyFirstMod.dll`

> ⚠️ **`_Module/` 目录（含 `Prompts/`、`GUI/`、`SubModule.xml`）也在 build 时由 Bannerlord.BuildResources 拷到游戏 Modules。纯提示词改动也必须 build 才生效**——只改项目里的 `.txt`/`.json` 不 build，游戏里一直是旧版。战役副本（`Campaigns/{战役}/`）在下次进档时按 newer-wins 同步覆盖。

## 开发工作流

### 每次修改代码后

```powershell
# 1. 编译 + 自动部署（必须设置环境变量）
$env:BANNERLORD_GAME_DIR = "D:\steam\steamapps\common\Mount & Blade II Bannerlord"
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
cd C:\Users\yangui\BLMods\MyFirstMod
dotnet build -c Release

# 2. 启动游戏测试
Start-Process "D:\steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client\Bannerlord.exe"

# 3. 在启动器中勾选 MyFirstMod → Play
```

> ⚠️ 定期执行 `dotnet clean -c Release && dotnet build -c Release` 做全量编译。增量编译可能掩盖文件损坏（如 IDE 后台索引/保存过程中的异常写入），clean build 才能暴露真实编译错误。

### 版本管理（Git）

项目使用 Git 进行版本管理。仓库已初始化在 `C:\Users\yangui\BLMods\MyFirstMod\`。

**提交前检查：**
```powershell
$env:Path = "C:\Program Files\Git\bin;" + [Environment]::GetEnvironmentVariable("Path","Machine")
git status
git diff
```

**提交：**
```powershell
git add -A
git commit -m "描述你的改动"
```

**`.gitignore` 排除项：**
| 目录/文件 | 原因 |
|-----------|------|
| `bin/` `obj/` | 编译产物 |
| `BLSource/` | 反编译的游戏源码（5332 文件，只读参考） |
| `_Module/Prompts/Campaigns/` | 运行时生成的战役存档数据 |
| `.idea/` `.vscode/` `Thumbs.db` | IDE 和系统文件 |

### 常见操作：新建 Harmony 补丁

在项目目录下创建新 `.cs` 文件（如 `Patches\MyNewPatch.cs`），格式如下：

```csharp
using HarmonyLib;

namespace MyFirstMod
{
    // Prefix: 在原方法执行前拦截，可修改参数或跳过原方法
    [HarmonyPatch(typeof(TargetGameClass), "TargetMethodName")]
    public static class MyPrefixPatch
    {
        public static bool Prefix(ref int someParameter)
        {
            someParameter *= 2;  // 修改参数
            return true;         // true=继续执行原方法, false=跳过
        }
    }

    // Postfix: 在原方法执行后拦截，可修改返回值
    [HarmonyPatch(typeof(TargetGameClass), "TargetMethodName")]
    public static class MyPostfixPatch
    {
        public static void Postfix(ref float __result)
        {
            __result *= 2f;  // 修改返回值
        }
    }
}
```

补丁在 `SubModule.OnSubModuleLoad()` 中通过 `harmony.PatchAll()` 自动激活。

> **⚠️ 重要发现（v0.5）**：`harmony.PatchAll()` 在 `OnSubModuleLoad` 时执行，此时部分游戏类型（特别是 `TaleWorlds.CampaignSystem.CampaignBehaviors` 命名空间下的类，如 `KingdomDecisionProposalBehavior`）**尚未完成运行时初始化**，导致这些 `[HarmonyPatch]` 属性静默跳过——补丁既不生效也不报错。**解决方案**：对于这类补丁，在 `OnGameStart` 中用 `Type.GetType("FullName, Assembly")` 获取类型，再用 `harmony.Patch(method, prefix/postfix)` **手动注册**。之前多位 agent 尝试修复原版外交拦截失败，根因均在此。
>
> **⚠️ 重要发现（v1.1，PatchAll 中止之谜）**：`DoubleRenownPatch`（双倍声望）曾写 `Postfix(ref float __result)`，但 `DefaultBattleRewardModel.CalculateRenownGain` 返回 `ExplainedNumber`——**`__result` 参数类型必须与原方法返回类型完全一致**，否则 `PatchAll()` 抛 `HarmonyException` 并**中止其后所有 `[HarmonyPatch]` 补丁的注册**（这是"补丁静默丢失"的另一个根因，也是 AGENTS.md 早期 KDPB 补丁失效的元凶之一）。已修复为 `ref TaleWorlds.CampaignSystem.ExplainedNumber __result`（双倍用 `__result.Add(__result.ResultNumber)`）。

### 常见操作：访问游戏内的游戏数据

```csharp
// 获取玩家氏族
Clan playerClan = Clan.PlayerClan;

// 获取所有王国
foreach (Kingdom kingdom in Kingdom.All) { ... }

// 获取所有定居点
foreach (Settlement settlement in Settlement.All) { ... }

// 获取当前战役时间
float days = CampaignTime.Now.ToDays();

// 显示消息
InformationManager.DisplayMessage(new InformationMessage("Hello", Colors.Green));
```

## Harmony 补丁核心模式

### 访问私有字段（三个下划线前缀）

```csharp
[HarmonyPatch(typeof(SomeClass), "SomeMethod")]
public static void Postfix(SomeClass __instance, MBList<Something> ____privateFieldName)
{
    // __instance = 被补丁的对象实例
    // ____privateFieldName = 私有字段（Harmony 自动注入，命名规则：_ + 字段名）
}
```

### 访问方法的 ref 参数

```csharp
[HarmonyPatch(typeof(SomeClass), "SomeMethod")]
public static void Prefix(ref int parameterName, ref ExplainedNumber __result)
{
    // ref 参数直接修改
    // __result 对应方法的 ref 返回值
}
```

### 获取方法的参数值

```csharp
[HarmonyPatch(typeof(SomeClass), "SomeMethod")]
public static void Postfix(int param1, float param2)
{
    // 参数名必须与原方法参数名一致（不区分大小写）
    // Harmony 自动传递原方法实参值到你的补丁
}
```

## 使用 BLSource 搜索游戏逻辑

BLSource 包含完整的反编译游戏源码（5332 个 .cs 文件），供 AI 搜索和理解游戏内部实现。常用搜索路径：

| 要找的内容 | 搜索目录 |
|-----------|---------|
| 战役行为/事件 | `BLSource/TaleWorlds.CampaignSystem/` |
| 游戏模型（经济/战斗/经验） | `BLSource/TaleWorlds.CampaignSystem/GameComponents/` |
| UI 与界面 | `BLSource/TaleWorlds.GauntletUI/` |
| 物品/装备/武器 | `BLSource/TaleWorlds.Core/` |
| 英雄/角色/NPC | `BLSource/TaleWorlds.CampaignSystem/` (Hero, CharacterObject) |
| 任务/剧情 | `BLSource/TaleWorlds.CampaignSystem/` (Quest, StoryMode) |
| 地图/定居点 | `BLSource/TaleWorlds.CampaignSystem/` (Settlement, MapScene) |
| 存档系统 | `BLSource/TaleWorlds.SaveSystem/` |
| 基础类型（MBList等） | `BLSource/TaleWorlds.Library/` |
| 本地化/翻译 | `BLSource/TaleWorlds.Localization/` |

**使用方式**：向 AI 提问时附带 "在 BLSource 中搜索..."，例如：
- "在 BLSource/TaleWorlds.CampaignSystem/GameComponents 中找 DefaultClanFinanceModel，告诉我每日收入的计算逻辑"
- "在 BLSource 中找 Tournament 相关的类，列出所有可覆写的虚拟方法"

## 模组类型与入口模式

Bannerlord 支持以下几种模组入口方式，本模组使用 **MBSubModuleBase**：

| 方式 | 用途 | 生命周期 |
|------|------|---------|
| `MBSubModuleBase` | 通用入口（本模组使用） | OnSubModuleLoad → OnGameStart → OnApplicationTick... |
| `CampaignBehaviorBase` | 战役专属行为 | RegisterEvents → 各种 CampaignEvents 回调 |
| `MissionBehavior` | 场景/战斗内行为 | OnMissionTick → 各种 Mission 事件 |
| `MissionView` | 场景/战斗内 UI | 类似 MissionBehavior 但有 UI 渲染回调 |

SubModule.cs 中的生命周期回调：

```csharp
OnSubModuleLoad()           // 模组被游戏加载时（最早时机, 激活Harmony）
OnBeforeInitialModuleScreenSetAsRoot()  // 主菜单显示前
OnGameStart(Game, IGameStarter)         // 新游戏/载入存档时
OnApplicationTick(float dt)             // 每帧调用
OnGameEnd(Game)                          // 游戏结束时
OnSubModuleUnloaded()                    // 模组被卸载时
```

## UI 开发（GauntletUI）

Bannerlord 使用自研的 GauntletUI 框架。UI 开发分两层：

1. **XML 布局**（Widget/Prefab）：定义界面结构和样式
2. **C# ViewModel**：定义数据绑定和行为逻辑

本模组如果涉及 UI，参考 `BLSource/TaleWorlds.GauntletUI/` 和 `BLSource/TaleWorlds.Engine.GauntletUI/`。

## AI 交互架构（function calling）

### 设计原则

本模组使用 OpenAI 的 **function calling（工具调用）** 机制处理 AI 与游戏世界的交互。当前可用工具包括：认知更新、定居点查询、世界状态查询、NPC 行军移动、等待停留、修改好感度、赠送/索要金币。未来所有 AI-世界交互都应基于此机制扩展。

### 为什么用 function calling 而不是文本标记

- 模型在训练阶段学过 function calling 协议，遵从度远高于自创的文本标记格式
- 结构化输出（JSON schema）比自由文本解析更可靠
- 文本输出和工具调用是分离通道，不会互相干扰
- 弱模型也能稳定遵循 function calling 协议

### 工具描述设计规范（参考 opencode 模式）

**核心原则：工具调用行为完全由函数 `description` 字段驱动，不在系统提示词中重复写工具指令。**

系统提示词只负责角色扮演和行为风格。函数描述承担全部工具调用逻辑。这样做的好处：
- 模型对工具定义通道的注意力与文本生成通道独立，不会因角色扮演而遗忘工具调用
- description 遵循固定结构化格式，弱模型也能稳定解析

**description 必须遵循以下格式：**

```
一句话总结工具功能。

Usage:
- 触发条件（Call this function whenever / after every...）
- 操作要求（Record / Capture / Note what the other person...）
- 参数说明（The xxx parameter should be...）
- 禁止条件（Do NOT call this function if...）
- 附加说明（This function is for system use only...）
```

**示例（`update_knowledge`）：**

```
Record new information learned about the other person in this conversation turn.

Usage:
- Call this function whenever the other person reveals or claims information
  about themselves: their identity, background, intentions, position, experiences,
  or opinions.
- Record what they claimed even if you are uncertain or skeptical.
- The knowledge parameter must be a single concise sentence summarizing the
  new information.
- Do NOT call this function if no new information was revealed in this turn.
- This function is for system use only; the other person does not see its output.
```

**注意事项：**
- 使用英文编写 description（模型对英文工具定义的训练量远大于中文）
- 每条规则独立成行，以动词开头（Call / Record / Do NOT call）
- 触发条件要显式枚举"whenever"的情况，减少模型判断负担
- 类型说明（"This function is for system use only..."）

**弱模型加强：在系统提示词末尾加一句工具提醒**

虽然工具调用主要由 `description` 驱动，但对于较弱模型（如 DeepSeek-V4-Flash），角色扮演任务可能完全吞没工具调用意愿。此时在系统提示词末尾加一句极其简短的提醒（不超过一行），能有效提高弱模型的工具遵从率：

```
你有 update_knowledge 工具可用——当对方透露新信息时使用它来记录。
```

这条提醒不应超过一行，不展开规则（规则已在 description 中写清楚），避免和角色扮演抢注意力。

### 工具调用状态提示（强制规则）

**所有 function calling 调用都必须通过 `InformationManager.DisplayMessage` 向玩家显式反馈结果。** 玩家有权知道 AI 做出了什么行动。

| 调用结果 | 提示内容 | 颜色 |
|---------|---------|------|
| 知识更新成功 | `{领主名} 更新了对你的认知` | Cyan |
| 知识更新失败 | `{领主名} 知识更新失败：{原因}` | Red |
| NPC 开始移动 | `{领主名} 决定前往{目的地}` | Cyan |
| NPC 移动失败 | `{领主名} 移动失败：{原因}` | Red |
| NPC 等待结束 | `{领主名} 结束了在{定居点}的停留` | Cyan |
| NPC 修改好感 | `{领主名} 对玩家的好感变化了{+n/-n}点` | Cyan |
| NPC 赠送金币 | `{领主名} 赠予了你 {n} 金币` | Cyan |
| NPC 索要金币 | （弹出确认对话框，无自动提示） | — |

实现位置：`AIChatScreenVM.ExecuteSend` 中解析 `ChatResponse.ToolCalls`（未来会扩展为复数工具调用）的循环中。

**注意：** 玩家长时间聊天时，屏幕左下角的信息弹窗是有限的。如果消息太多会被新消息顶掉，建议在未来的 UI 升级中考虑将状态提示显示在聊天窗口内部。

### 当前实现

`AIChatClient.cs` 中定义了所有 23 个工具（15 个游戏工具 + 8 个文件/通信工具）。每次 API 请求都附带能力过滤后的工具定义。模型自行判断是否需要调用。返回的 `ChatResponse` 包含：
- `Content`：角色扮演文本回复
- `LearnedKnowledge`：如果模型调用了 `update_knowledge`，这里包含新认知
- `ToolCalls`：原始工具调用数据（用于构建反馈闭环）
- `ToolResults`：工具执行结果，追加到历史形成反馈闭环

### 工具调用模式

当前为**单一模式**：角色扮演和工具调用在同一次 API 请求中完成，模型同时输出文本回复和 tool_calls。
- 对强模型效果好，延迟低，token 消耗少
- 弱模型可能在角色扮演中遗忘工具调用（可在工具描述里加一行简短提醒缓解）

> 注：早期的「独立工具调用」模式（两次 API 请求分离角色扮演与工具决策）已在 v1.1 移除——它诞生于最初的非 Agent 驱动设计，且工具调用只被解析不执行（设计残留）。

### 工具调用反馈闭环

所有 assistant 消息如果包含 tool_calls，会在聊天历史中追加对应的 `role: "tool"` 确认消息。这样模型在下一轮对话中看到自己的工具调用被处理，形成正反馈循环。这是方案二的实现。

### 扩展方向（未来）

基于同一套 `tools` 机制可以扩展：
- ~~领主决定去某个领地 → `travel_to(settlement)`~~ （已实现为 `move_to_settlement` + `wait_at_settlement`）
- ~~给玩家物品/金钱 → `give_item(item, amount)`~~ （已实现为 `give_gold` + `request_gold`，支持 target_entity_id）
- ~~改变对目标的态度 → `change_relation(delta)`~~ （已实现，支持 target_entity_id）
- ~~领主军事行动 → 劫掠/围攻/追击/驻防/巡逻/护送/绕行~~ （已全部实现为 7 个部队 AI 工具 + 中断恢复 + 定时签到）
- ~~文件编辑 → `write_file`/`edit_file`/`delete_file`/`glob`~~ （已实现，chat_logs 和 persona.txt 为只读保护）
- ~~发起外交/王国级动作~~ → `declare_war` / `propose_peace` / `propose_alliance` / `propose_trade` / `gift_fief`（已全部实现）
- 物品交易 → `give_item` / `give_item` / `request_items`（已全部实现）
- ~~氏族阵营切换~~ → `change_kingdom`（已实现，支持离国/加入/叛逃/雇佣兵四种模式）
- NPC 自立建国 → `create_kingdom`（待开发）<br>
  `ChangeKingdomAction.ApplyByCreateKingdom(Clan, Kingdom)` 后端方法已存在，接受任意氏族。<br>
  阻碍：① `Kingdom` 对象构造函数不在反编译源码中，可能需要反射或查找静态工厂；<br>
  ② `DefaultKingdomCreationModel` 全部方法硬编码为 `IsPlayerKingdomCreationPossible`（只检 `Hero.MainHero`），需替换或 patch。<br>
  条件门槛（四级家族+有封地+100兵）本身合理，移植给 NPC 直接可用。

### 流式架构（已实现，参考 opencode）

使用 SSE（Server-Sent Events）流式连接，参考 opencode 的 processor 架构：
- 模型在一个持续流中边输出文本边调工具
- 工具结果实时返回，流持续到模型自然 finish
- 无轮次上限（由 `MaxAgentRounds` 或「不限制」模式控制），模型自主决定何时停止

实现要点：
- HTTP 请求 payload 中 `stream: true`（含 `stream_options.include_usage`，用于缓存命中统计）
- 用 `HttpCompletionOption.ResponseHeadersRead` 获取流式响应
- 逐行解析 `data:` 前缀的 SSE 事件
- 文本增量（delta）累积成完整回复
- `reasoning_content` delta 跨 chunk 累积（DeepSeek 默认思考模式开启，思维链内容需捕获并在工具调用轮次中回传）
- **思考强度（reasoning_effort）**：MCM「思考强度」可调（low/high/max），默认 `low`——思考按输出价计费，是成本大头。**史官固定 high**（文笔核心，且不发送该参数、用 API 默认值以兼容）；其余 intent 发送 MCM 设置值。部分模型/端点不支持该参数（400 时自动回退去掉它）。
- tool_calls delta 跨 chunk 累积（DeepSeek 协议中 tool_call 分多次 delta 传输）

### 信件激活机制（AgentScheduler）

信件系统采用异步事件驱动模型。**v1.1 起为"有限并行 + 优先级调度"**：最多 `MaxAgentConcurrency`（MCM 可调，默认 5，范围 1-8）个 Agent 任务同时在飞（`_inFlightCount` 计数），事件按优先级从 5 个分队列中取最高者出队。**文件读写并发防护**：史料/谏言/聊天记录等共享文件经 `SafeFileIO` 带重试写入，避免"文件正被使用"异常（主线程写史料若撞上史官后台读，重试而非崩游戏）。

```
send_letter → StoreOutgoingLetter(文件) → AgentScheduler.QueueEvent(LetterReceived)
                                                      ↓
OnApplicationTick → AgentScheduler.Tick() → 若并发槽未满 → 取最高优先级事件 → SpawnTask 异步处理
```

**优先级（P0 最高）：**
| P | 事件类型 | 说明 |
|---|---------|------|
| 0 | YearlyChronicle / SpecialChronicle（史官） | 最高，**永不跳过**（生成不受队列门槛限制，永远先出队） |
| 1 | KingDiplomacy（国王内外政务/提案） | 高优先，外交提案不积压；现同时处理内政（封地分配） |
| 2 | LetterReceived | 中 |
| 3 | BehaviorCheckIn / PlanCheckIn / FiefReview（签到/封地审视） | 低 |
| 4 | Advisory（概率激活的谏言） | 最低，只在无更高优先工作时处理 |

- 并发安全：每任务上下文（`CurrentHero`/agent/深度等）经 `AsyncLocal` 隔离，工具仍统一回主线程串行执行
- `ActivationEvent.Depth` 控制级联深度（`AsyncLocal` 按任务隔离，MCM 可调默认 5）
- 支持事件类型：`LetterReceived`（来信）、`BehaviorCheckIn`（签到）、`KingDiplomacy`（国王内外政务）、`PlanCheckIn`（计划）、`YearlyChronicle`/`SpecialChronicle`（史官）、`Advisory`（谏言）、`FiefReview`（封地审视，被夺方激活触发内政矛盾）
- **检查站冷却**：签到类激活（BehaviorCheckIn/PlanCheckIn）每 agent 至少间隔 **15 真实分钟**（`PartyBehaviorManager._lastCheckInByAgent`，用真实时间而非游戏时间——游戏时间加速时游戏小时冷却无效）。防止「move/wait 到达→立刻签到→再发指令」的 token 死循环
- **卡死保险（持久行为）**：驻防/巡逻/护送（`CheckInHours > 0`）若下发后长时间**未到达目标点**（被拦截/目标遥不可及/巡逻绕圈不进 5 单位判定圈），签到永不触发、`PendingAction` 永不移除、mod 每帧重发覆盖原版、agent 永不激活 → 静默卡死。兜底：`PendingAction.CreatedAt` 记录下发时间，超时（`2× 签到周期`，驻防 6 天/巡逻 4 天/护送 2 天）且仍未到达 → 强制触发一次 BehaviorCheckIn（提示"你一直未能到达 X，是否放弃"）并释放 PendingAction，部队回归原版 AI。正常到达时此分支永不触发，零额外成本
- 被俘/逃亡的国王统治者现在也会被激活（仅跳过已死亡和 null 的），`BuildSelfStatus` 中会提示"你仍是王国统治者"
- 玩家可见：左下角弹 `xxx 给 xxx 写了一封信` / `xxx 正在思考下一步行动...` / `xxx 正在处理内外政务...` / `xxx 发现自己被夺封了...`
- 防递归：书信规则强调"除非必要不回信" + 深度硬上限
- 聊天记录使用显式路径（`GetChatLogPathFor`）防线程竞态
- **信件记忆连续性**：信件处理（`ProcessEvent`）会先 `LoadChatLogFor` 注入双方此前聊天记录，再追加信件内容——对方能记得过去见过面/聊过什么（原实现只给信件文本，导致跨信"不认得你"）
- 外交提案感知：`LetterReceived` 处理时自动检测双方是否有待处理的外交提案（`AgentManager.GetProposalsBetween`），如有则将提案摘要注入上下文提示 Agent

### 封臣谏言机制（AgentScheduler）

封臣谏言是"流式、单次激活"的内部政治压力系统，替代了早期的同步"封臣大会"（当时因 48 人批量 LLM 同步激活会卡死事件队列而废弃）。**该限制已随 v1.2.0 并发架构解除**：现在批量后台事件只是排队按 `MaxAgentConcurrency`（默认 5）并发槽位消化，不再卡死队列；新增后台 Agent 事件（记忆巩固、请封、夺权等）可直接排入 AgentScheduler，用优先级和 token 预算约束频度：

```
Tick 无事件时 → CheckAdvisoryActivations()
    ↓
每个王国每天 10% 概率（MCM AdvisoryProbability 可调）
    ↓
SelectAdvisoryLeader：按权重抽氏族领袖
  权重 = 氏族Tier×3 + 影响力/50 + 封地数
  排除：雇佣兵、俘虏、逃亡、国王本人、玩家、上轮进谏者（同一人不连续）
    ↓
ProcessAdvisory（入队 P4，由有限并行槽位调度处理，最低优先）
    → 提示词：读 personal_notes.txt（可选）→ 查世界局势 → submit_advisory(content) 工具提交
    → 工具自动写 World/advisory/{王国}_{年}.txt（时间戳头 + 姓名 + 正文）
    → 未调工具则兜底写 response.Content，空则标"（未发表公开谏言）"
```

要点：
- `submit_advisory` 是 agent_tools.json 里的专用工具，归档格式由代码控制，agent 只需填内容
- 私人笔记 `decisions/personal_notes.txt` 非强制；若 LLM 写成了别的文件名，`ProcessAdvisory` 会强制合并归位
- 国王外交激活（`KingDiplomacy`）的提示词自动注入"先 read_file World/advisory/ 了解封臣谏言"，但国王决策权不受限
- **国王↔封臣闭环（诏令）**：国王政务审视时可颁布公开诏令（`submit_edict`，归档 `World/edict/{王国}_{年}.txt`，仅王国统治者可用、非国王被拒）；封臣进谏前先读国王诏令（`ProcessAdvisory` 提示词 + `advisory_rules.txt`），若国王垂询某事应在谏言中回应；诏令读取走 `IsPublicDocAllowed`（史官任何国家、其他 agent 仅本国），玩家 H 键可见本国诏令
- **国王外交问询（跨国王互通）**：国王可用 `consult_king` 遣使问询他国国王（`KingConsult` 事件 P1，落盘 `World/diplomacy/consults/{A}_and_{B}.txt`），对方以 `reply_consult` 答复，问询方下次政务激活拉取看到。**严格单向防环**：问询会话（intent=`"king_consult"`）中 `consult_king` 被 BuildTools 排除，链深恒 1。史官可读任何国家问询线程（`IsConsultAllowed`），参与双方国王可读，第三方不可读。每王国对 7 游戏天冷却（`TryConsult`/`RecordConsult`）
- 事件队列积压 >3 时暂停生成新的国王外交/封臣谏言，先消化积压（Tick 中 `PendingEventCount() <= 3` 门槛）
- **token 截断重试**：`SendMessage` 捕获 `finish_reason`（`"length"`=被 `max_tokens` 截断）。谏言/史官若被截断且未提交/未落盘 → 自动重试一次（更坚决的提示直接进谏/直接 write_file）；主动沉默（`finish_reason="stop"`）不重试。MCM「最大 Token 数」上限 65536、默认 32768（DeepSeek V4 最高支持 384K 输出，旧 8192 上限已过时），史官长编年史亦不易截断
- 历史（H 键）可读本国公开谏言；史官 `_readableWorldDirs` 含 `"advisory"` 可读取
- **玩家谏言**：秘书处（M 键）的 chancery 提示词引导使用 `submit_advisory`（玩家封臣/国王均可，雇佣兵被工具拒绝）——玩家谏言与 AI 谏言同一归档，可被史官写入编年史
- **史官联动**：`historian_rules.txt` 和 `yearly_chronicle_prompt.txt` 引导史官可选读 `advisory/` 作为补充视角（补充事实背后的观点和史料未载细节）；原始史料仍为权威，引用须注明"某封臣当时的谏言"
- **秘密谏言（不入史册）**：`submit_secret_advisory` 密陈给国王，写 `World/secret_advisory/{王国}_{年}.txt`，**史官无权读取**（`IsSecretAdvisoryAllowed` 仅本国王可读本国密陈）。公开谏言进史、密陈只呈国王，封臣可公开一套、私下另一套。提示词按克制原则模糊化：「你认为不适合被历史记录或旁人知晓的事」

### 内政审视与封地政治（配套制度）

国王外交审视升级为**内外政务**——先内政后外交，内政缺地会自然驱动战争（内部驱动外部）：

- **ContextBuilder**：`intent="diplomacy"` 且为统治者时，自动注入 `BuildCourtReport`（内政审视报告：封地账本 + 治理[`Town.Prosperity`/`Town.Loyalty`] + 近期战功）
- **HistoryRecorder**：`RecordMerit` 写 `World/court/{王国}_merit.txt`（围攻/攻克/失利，真实事件记录），供内政审视读取
- **diplomacy_rules.txt**：明示国王内外政务、赐地/夺封职权、夺封须师出有名（`gift_fief` 可附 `reason`）
- **被夺方激活（FiefReview）**：`DiplomacyService.ExecuteTransferFief` 转让封地后，若原主（非国王本人）被夺封 → `AgentScheduler.QueueFiefReview` 激活原主审视处境（可写信/上表/转投他国，`intent="fief_review"` 分类含 diplomacy）。矛盾来自「失去的人」，得利方不激活
- **攻城后定归属（册封由 Agent 主导）**：`FiefAssignmentPatch.cs` 拦截原版攻城后的 `SettlementClaimantDecision` 投票（Prefix 拦 `DailyTickSettlement`）；Postfix 在 `OnSettlementOwnerChanged`（openToClaim、多家族王国）取消 unassigned 标记（攻城后默认归国王氏族，防忠诚惩罚）并激活国王 Agent（P1 级 KingDiplomacy，带归属指示）。不区分攻城者是玩家或 AI——统一国王决定；国王是玩家时由玩家经秘书处处理。**手动注册**（OnGameStart Type.GetType + harmony.Patch，CampaignBehaviors 类 PatchAll 会静默跳过）。MCM「册封由 Agent 主导」
- **军情迷雾**：`query_party_troops` 自己/同阵营全量精确；异国按距离与可达性分近距/远距/传闻三档（`GetIntelRadii` 按地图尺度相对锚），跨海不可达降为传闻——打破「完美信息→和平均衡」

### 盟约/贸易协定到期记录（轻量拉取式，无 LLM）

盟约（84 天）与贸易协定（1 年）由原版定时到期。模组接管外交后**不主动激活 Agent 处理到期**（避免 token 消耗），改为拉取式记录，国王下次激活时自行看到：

> **盟约"号召盟友宣战"投票已拦截**：`SubModule.OnGameStart` 手动 patch `ProposeCallToWarAgreementDecision`/`AcceptCallToWarAgreementDecision` 的 `IsAllowed()`（Prefix 强制返回 false，`ShouldBeCancelled()` 在投票触发前将其取消；Election 类 PatchAll 会静默跳过，须手动注册）；并 patch `LordConversationsCampaignBehavior.conversation_player_wants_to_sponsor_call_to_war_on_condition` 隐藏玩家对话里的原版"号召盟友"选项（否则玩家会花影响力/金币却无声失效）。军事同盟只保留名义作用——是否号召盟友/宣战由国王 Agent 激活时自行决定。受 MCM「禁止原版外交」开关控制。

- **记录**：`AgentScheduler.Tick` 每游戏日一次调 `DiplomacyService.CheckExpiringAgreements()`（无 LLM）——扫描剩余不足 1 天的盟约/贸易协定，写入 `World/diplomacy/expiry_log.txt`（行格式 `类型|王国1ID|王国2ID|到期日day|人类可读文本`，每对王国+类型一条，超 90 游戏天清除防堆积）
- **查看**：`query_world_state` 输出各王国名下附带「📜 盟约 X与Y 于…到期」；到期前不记录不提示，国王不查就不知道
- **防矛盾**：`DiplomacyService.ClearExpiryRecord` 在协约**重新建立**（对方接受提案 `ExecuteRespondToProposal`）或**主动结束**（`ExecuteEndAlliance`/`ExecuteEndTradeAgreement`）的瞬间清除对应记录，防止国王再次激活时看到失效的「到期」信息而反复查询求证。key 排序规则（王国 ID 字典序）与写入端一致；旧存档无记录时调用为安全 no-op
- **续约**：不新增续约工具；国王若想续约，走现有 `propose_alliance`/`propose_trade` 流程

### 历史系统（HistoryRecorder + 史官 Agent）

历史系统由两部分组成：**事件记录器** 和 **史官 Agent**。

#### 事件记录器（HistoryRecorder）

`HistoryRecorder.cs` 是一个 `CampaignBehavior`，在 `OnGameStart` 中注册。它监听以下 `CampaignEvents`：

| 事件 | 游戏钩子 | 史料 type |
|------|---------|-----------|
| 宣战 | `WarDeclared` | `war_declared` |
| 议和 | `MakePeace` | `peace_made` |
| 城镇/城堡易主 | `OnSettlementOwnerChangedEvent`（过滤 IsTown/IsCastle） | `settlement_captured` |
| 王国灭亡 | `KingdomDestroyedEvent` | `kingdom_destroyed` |
| 新王国建立 | 无独立事件，从 `OnClanChangedKingdomEvent` 的 `CreateKingdom` 详情补记 | `kingdom_created` |
| 贵族死亡 | `HeroKilledEvent`（过滤有 clan 的） | `hero_killed` |
| 氏族叛变 | `OnClanChangedKingdomEvent` | `clan_changed_kingdom` |
| 贵族婚嫁 | `OnMarriageOfferedToPlayerEvent`（直接注册） | `marriage` |

每条事件以 JSONL 格式追加到 `World/history/events_{year}.txt`：
```json
{"year":1084,"season":"春","day":12,"type":"war_declared","summary":"瓦兰迪亚向库赛特宣战"}
```

#### 史官 Agent

- **触发时机**：每年年终（年份推进时），`AgentScheduler.CheckYearAdvance()` 检测年份变化并队列 `YearlyChronicle` 事件
- **专题触发**：灭国/新王国建立时，`HistoryRecorder` 调用 `AgentScheduler.QueueSpecialChronicle()` 即时队列 `SpecialChronicle` 事件
- **专题合并（防杀人潮）**：`QueueSpecialChronicle` 会先写入合并缓冲——若已有一个待处理专题史事件，后续事件只追加不新开；处理时一次史官激活合并全部（如玩家连杀十几人 → 只生成一次传记专题，而非连环激活）
- **传记质量**：`query_character` 现可查已故人物（枚举所有氏族成员含已故 + 在世英雄）并返回出生/卒年；传记提示词要求开头点明身份（统治者/族长/成员）与生卒年。成功判定改为"chronicles 目录出现新文件"（传记是自命名文件，原只查 `chronicle_*.txt` 会误报"未生成"）
- **Entity**：史官是虚拟 Entity（ID: `__historian__`，`HeroRef = null`），不映射任何游戏 NPC
- **工具**：`query` + `file` 分类的工具（`ActivatedCategories = {"universal", "query", "file"}`）
- **权限**：可读 `World/history/` 目录，可写 `World/history/chronicles/` 目录
- **提示词**：使用 `intent = "historian"` 的 `ContextBuilder.Build()`，规则来自 `historian_rules.txt`
- **输出**：年度编年史 → `World/history/chronicles/chronicle_{year}.txt`；专题史 → 自命名文件

#### NPC 查阅历史

- `AgentManager.IsPathAllowed` 和 `ResolvePath` 新增了对 `history/` 和 `history/chronicles/` 路径的支持
- NPC Agent 可用 `read_file("history/chronicles/chronicle_1084.txt")` 直接读取史官成文
- 原始史料（`events_*.txt`）对 NPC 只读
- 写入历史目录的权限保留给 `__historian__` entity

#### 年份检测

- `AgentScheduler` 用 `_lastChronicleYear` 追踪上次处理年份，初始值 = 游戏起始年份
- 每帧 `Tick()` 中调用 `CheckYearAdvance()`，检测 `currentYear > _lastChronicleYear`
- 对每个已跳过的年份，检查 `events_{year}.txt` 是否存在且 `chronicle_{year}.txt` 不存在，满足条件才队列事件
- 防止重复生成：已存在编年史的年份跳过

## 调试方法

1. **Rider 附加进程调试**：
   - 启动游戏
   - Rider → Run → Attach to Process → Bannerlord.exe
   - 设置断点，触发你的代码时会中断

2. **日志调试**（最简单）：
   ```csharp
   InformationManager.DisplayMessage(new InformationMessage($"Debug: {value}", Colors.Red));
   ```

3. **dnSpy 调试**：打开 `C:\Users\yangui\Tools\dnSpy\dnSpy-net-win64\dnSpy.exe`，附加到 Bannerlord 进程，可在任意游戏方法上设断点

4. **DebugLogger 调试日志**（推荐优先）：战役目录 `debug_logs/debug_*.log` 记录每次 LLM 调用的轮次/推理长度/工具名；**最终轮无文本时记录思维链摘录**（600 字）；请求结束时记录**缓存命中统计**（`LLM 完成 ... 缓存命中=X 未命中=Y 命中率=Z%`，来自 `stream_options.include_usage` 的 `usage.prompt_cache_hit_tokens`/`prompt_cache_miss_tokens`）。排查"Agent 为什么这么干/没干"首选此日志，排查"缓存是否生效"也看它。受 MCM「调试日志」开关控制（默认开）。注意：`SendMessage` 返回的 `Content` 若回退到"（已通过工具处理完毕）"表示 Agent 调了工具但没输出结语（如国王评估后决定不行动）；若端点拒绝 `stream_options` 会回退为无 usage 请求并记日志（功能不受影响）

## 注意事项

- **运行 `dotnet build` 前必须设置 `$env:BANNERLORD_GAME_DIR`**，否则找不到游戏 DLL
- BLSource 虽然编译时被排除，但**不能被删除**——AI 需要它来理解游戏逻辑
- Harmony 补丁中的私有字段名必须与原始 DLL 中的字段名**完全一致**（用 dnSpy 可查看）
- 模组的 SubModule.xml 中 `Id` 和 `Name` 默认等于项目名（`$(MSBuildProjectName)`）
- 如果补丁不生效，检查：1) 方法名是否正确 2) 参数类型是否匹配 3) 是否有重载冲突（aka Ambiguous match）

## 工具清单

### 开发工具

| 工具 | 路径 | 用途 |
|------|------|------|
| dnSpy GUI | `C:\Users\yangui\Tools\dnSpy\dnSpy-net-win64\dnSpy.exe` | 反编译、调试、查看游戏源码 |
| dnSpy CLI | `C:\Users\yangui\Tools\dnSpy\dnSpy-net-win64\dnSpy.Console.exe` | 批量反编译（命令行） |
| dotnet CLI | `dotnet` | 编译、创建新项目 |
| Rider | `C:\Program Files\JetBrains\JetBrains Rider 2026.2\bin\rider64.exe` | IDE |

### 游戏工具（tools.json，50 个）

| 工具 | 类别 | 说明 |
|------|------|------|
| `query_clan_fiefs` | 查询 | 查询氏族持有的封地列表 |
| `query_character` | 查询 | 查询人物公开档案（身份/家族/王国/兵力/位置），系统权威数据 |
| `query_settlement` | 查询 | 查询定居点信息（所有者/繁荣度/类型） |
| `query_settlement_geography` | 查询 | 查询定居点地理情报（位置/周边邻居/边境标签） |
| `query_world_state` | 查询 | 获取世界局势（王国兵力/交战状态） |
| `query_recent_events` | 查询 | 查询人物近期事件（比武/俘虏/婚嫁/阵亡等百科记录） |
| `query_surroundings` | 查询 | 扫描周围环境（当前位置、附近城镇城堡、附近部队及阵营关系） |
| `update_knowledge` | 认知 | 记录关于对方的新认知 |
| `change_relation` | 关系 | 修改对任意人物的好感度（支持 target_entity_id） |
| `give_gold` | 经济 | 赠予任意人物金币（支持 target_entity_id） |
| `request_gold` | 经济 | 向任意人物索要金币（玩家需确认，NPC 自动划转） |
| `move_to_settlement` | 行军 | 部队行军到城镇/城堡/村庄（支持 activate:true 参数自动唤醒） |
| `wait_at_settlement` | 行军 | 在定居点停留指定时长（支持 activate:true 参数到期自动唤醒） |
| `raid_settlement` | 军事 | 劫掠村庄 |
| `besiege_settlement` | 军事 | 围攻城镇/城堡 |
| `form_army` | 军事 | 召集军团（以攻城/劫掠/防御目标为指向，召集本国领主成军团，交还原版 AI 指挥；需影响力>100、王国交战、氏族领袖） |
| `engage_party` | 军事 | 追击并攻击另一支部队 |
| `defend_settlement` | 军事 | 驻防守卫定居点（持续性，72h 签到） |
| `patrol_settlement` | 军事 | 巡逻定居点周边（持续性，48h 签到） |
| `escort_party` | 军事 | 护送跟随另一支部队（持续性，24h 签到） |
| `go_around_party` | 行军 | 绕行回避某支部队 |
| `query_war_status` | 查询 | 查询王国战争统计（双方阵亡/攻城/劫掠数） |
| `query_influence` | 查询 | 查询本族当前影响力（政治资财，主要用于拉军团[超 100 可召集]与推行政策） |
| `query_pending_proposals` | 查询 | 列出当前王国待处理的外交提案（无需参数，自动按当前 Entity 过滤） |
| `declare_war` | 外交 | 向另一王国宣战（单向，国王专属） |
| `propose_peace` | 外交 | 向另一王国提议议和（双向，附赔偿方案，国王专属） |
| `propose_alliance` | 外交 | 向另一王国提议结盟（双向，国王专属） |
| `propose_trade` | 外交 | 向另一王国提议贸易协定（双向，国王专属） |
| `end_alliance` | 外交 | 单方面终止与盟友的盟约（无需对方确认，国王专属） |
| `end_trade_agreement` | 外交 | 单方面终止与另一王国的贸易协定（无需对方确认，国王专属） |
| `respond_to_diplomacy_proposal` | 外交 | 接受或拒绝收到的外交提案（国王专属） |
| `gift_fief` | 外交 | 国王敕令将封地直接转让给指定封臣家族领袖（国王专属，不经过选举） |
| `cancel_action` | 控制 | 取消当前任务，回归自主 AI |
| `query_party_troops` | 查询 | 查看部队详情（自己/同阵营全量：金币/兵力/上限/各兵种经验升级路径/俘虏/物品/装备；异国仅侦察估计：按距离与可达性分近距/远距/传闻三档，近距/远距含规模上限估计，不泄露军饷/经验/装备等机密） |
| `query_available_troops` | 查询 | 查看当前定居点可招募兵种（需在定居点内） |
| `query_settlement_villages` | 查询 | 查看城镇/城堡的附属村庄列表 |
| `query_hero_skills` | 查询 | 查询人物 18 个技能等级和 6 个属性值 |
| `recruit_troops` | 军事 | 从当前定居点招募指定兵种（扣金币，需在定居点内） |
| `upgrade_troops` | 军事 | 升级兵种（检查经验/金币/装备/perk） |
| `buy_food` | 行军 | 在定居点买粮到够吃 N 天（自动挑最便宜的） |
| `give_item` | 社交 | 将自己物品/装备交给任意人物 |
| `request_items` | 社交 | 向任意人物索要物品（NPC 直接划转，玩家弹确认框） |
| `let_go` | 社交 | 遭遇战中放走玩家（仅当己方兵力占优时可用，含冷却期） |
| `release_prisoner` | 军事 | 释放自己部队中的俘虏（贵族英雄→逃亡者回领地，普通士兵→移除；支持按名单个释放或 all 全放） |
| `execute_prisoner` | 军事 | 处决自己部队中的贵族俘虏（仅限贵族；受 MCM「处决无惩罚」控制，默认开=无惩罚） |
| `create_clan` | 通用 | 天意建族（家族补充系统）：建新贵族家族（成员 3-6 人程序生成、家族等级 2、族长带兵、入原始史料但不激活史官）。仅 `__fate__` 实体可用（能力门控）。**代码强制每次激活只建一族**（LLM 可能连建多族，曾致原生崩溃）；英雄创建对齐游戏叛乱建族模式（先建英雄→注册进族→置 Active） |

### 文件工具（agent_tools.json，15 个）

| 工具 | 说明 |
|------|------|
| `read_file` | 读取文件内容（支持行号范围） |
| `write_file` | 创建新文件或完整重写 |
| `append_file` | 追加内容到文件末尾 |
| `edit_file` | 精确替换文件中的文本（必须唯一匹配） |
| `delete_file` | 删除文件 |
| `move_file` | 移动/重命名文件（如标记计划完成） |
| `list_dir` | 列出目录内容 |
| `glob` | 按文件名模式匹配（如 `knowledge/*.txt`） |
| `grep` | 按关键词搜索文件内容（支持 max_results 上限、context_lines 上下文、case_sensitive） |
| `send_letter` | 给其他 Entity 写信 |
| `submit_advisory` | 向国王提交公开谏言（封臣谏言专用，系统自动归档，史官可读） |
| `submit_secret_advisory` | 向国王密陈秘密谏言（不入史册，仅本国王可读） |
| `submit_edict` | 国王颁布公开诏令/垂询群臣（公开归档 `World/edict/{王国}_{年}.txt`，史官可读，仅王国统治者可用） |
| `consult_king` | 国王遣使问询他国国王（`World/diplomacy/consults/{A}_and_{B}.txt`，激活对方回应，史官可读，仅王国统治者可用，每王国对 7 游戏天冷却） |
| `reply_consult` | 国王回复他国外交问询（落盘到问询线程，史官可读，仅王国统治者可用） |

## 故障排查

### 日志文件位置

| 日志 | 路径 | 内容 |
|------|------|------|
| 游戏引擎日志 | `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_*.txt` | 模组加载顺序、DLL 加载、资源扫描 |
| 游戏错误日志 | `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_errors_*.txt` | 引擎级错误和警告 |
| 看门狗/崩溃日志 | `C:\ProgramData\Mount and Blade II Bannerlord\logs\watchdog_log_*.txt` | 崩溃时的异常码和堆栈 |
| ButterLib 日志 | `%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\butterlib*.txt` | ButterLib 加载状态和模块级异常 |
| 默认模组日志 | `%USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\default*.log` | 模组标准日志输出 |
| 崩溃 Dump | `C:\ProgramData\Mount and Blade II Bannerlord\crashes\` | 崩溃时生成的 .dmp 文件 |

### 查看最新日志的命令

```powershell
# 查看最新的游戏引擎日志（按时间倒序）
Get-ChildItem "C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object { Get-Content $_.FullName }

# 查看最新的 ButterLib 日志
Get-ChildItem "$env:USERPROFILE\Documents\Mount and Blade II Bannerlord\Configs\ModLogs\butterlib*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object { Get-Content $_.FullName }

# 搜索所有日志中的错误关键词
Get-ChildItem "C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object { Get-Content $_.FullName | Select-String "Error|Exception|crash|Null|MyFirstMod" }
```

### 生命周期与崩溃陷阱

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

### ButterLib 异常弹窗

当模组抛出未处理异常时，ButterLib 会拦截并弹出一个红色窗口显示堆栈信息。**截图这个窗口是最直接的排查方式**。弹窗信息也会同时写入 `ModLogs\butterlib*.txt`。

### 排查步骤（模组不工作或崩溃时）

1. 启动游戏，勾选你的模组，如果崩溃 → 截图 ButterLib 弹窗
2. 查看 `butterlib*.txt` 最新日志中的 `[ERR]` 行
3. 查看 `watchdog_log_*.txt` 中的异常码
4. 检查 Harmony 补丁是否存在 **Ambiguous match**（重载冲突），参考 [Harmony 章节](#harmony-补丁核心模式)
5. 确认代码中是否在 `OnSubModuleLoad` 中调用了 UI/游戏系统 API

## 创建新模组

```powershell
cd C:\Users\yangui\BLMods
dotnet new blmodfx --name "新模组名"
```
