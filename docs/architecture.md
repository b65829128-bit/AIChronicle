# 架构详解（AI 参考文档）

> 本文件是 `AGENTS.md` 的补充参考。常驻硬规则与速览见 `AGENTS.md`；实现具体功能需要深入了解某一块时，用 `read` 工具按需查阅本文件。内容与 `AGENTS.md` 同源，整理自完整架构说明。

## 1. 核心理念

本模组采用 **Agent 驱动架构**，受 [opencode](https://github.com/anomalyco/opencode) 设计启发。不是"玩家 ↔ LLM 对话"，而是：

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

## 2. 关键设计

| 原则 | 说明 |
|------|------|
| **Entity 平等** | 玩家和所有 NPC 统一为 Entity，由 EntityController（Human/Agent）区分 |
| **动态 Context** | ContextBuilder 根据当前交互双方动态组装系统提示词 |
| **能力过滤** | 每个 Entity 有 EntityCapability 集合，工具列表按能力自动过滤 |
| **书信模式** | 支持书信 intent，O 键唤起书信往来面板（统一线程列表 + 未读角标） |
| **文件即知识库** | NPC 的记忆、目标、对目标的认知都是文件，Agent 通过 `read_file`/`write_file`/`append_file`/`edit_file`/`delete_file`/`move_file` 精确读写 |
| **信息隔离** | 每个 NPC 只能操作自己目录下的文件 + `World/`，不知道其他 NPC 和玩家的对话 |
| **工具定义文件化** | 70 个工具定义在 `tools.json`（51 个游戏工具）和 `agent_tools.json`（19 个文件/通信工具，含 `write_chronicle`/`submit_advisory`/`submit_edict`/`consult_king`/`send_envoy`/`reply_envoy`/`record_resolve`）中，热重载，不硬编码；两文件缺失时回退内嵌最小工具集 |
| **工具分类系统** | 每个工具归属 8 个分类之一（universal/query/social/movement/military/diplomacy/file/communication），Agent 按场景默认激活相关分类，需要其他分类时调用 `browse_tools` 元工具按需解锁。**玩家发起的聊天（conversation）是全功能通道——全部分类默认激活**（AI 几乎不主动聊天，绝大多数对话由玩家发起，若工具不全则对话里达成的承诺无法兑现；能力门控照旧，国王专属工具仍只有国王拿到） |
| **提示词全部可编辑** | `context_template.txt`、`persona_generation.txt`、`chancery_rules.txt`、`world_info.txt`、`game_rules.txt` 等均为文件，战役创建时自动复制到战役目录，热重载优先读战役目录 |
| **多轮工具调用** | `SendMessage` 内建 SSE 流式循环，模型调用工具 → 执行 → 追加结果 → 重请求，直到模型自然停止（无轮数限制，仅保留极高安全阀防死循环） |
| **主线程分发** | LLM 工具循环跑在后台线程，但**所有修改游戏状态的工具**经 `MainThreadExecutor` 排队到主线程 `OnApplicationTick` 执行（后台线程阻塞等待结果）；仅 `request_gold`/`request_items`/`browse_tools` 留在后台线程（前两者需主线程弹窗等待玩家，后者改本流程上下文）。背景：Bannerlord 游戏对象（MobileParty/Hero/Kingdom）是主线程独占的 |
| **上下文隔离（AsyncLocal）** | `CurrentHero`/`CurrentIntent`/`ActivatedCategories`（AIChatClient）、`_agentEntityId`/`_targetEntityId`（AgentManager）、`_activeAgentId`/`_activeTargetId`（EntityManager）均为 `AsyncLocal`——聊天与后台信件/自省/密使/外交多个流程并发时上下文互不覆盖；实体缓存用 `ConcurrentDictionary`（线程安全） |
| **秘书处** | M 键打开，玩家的个人行政助手。固定 persona（无条件服从），不读玩家 persona。国王/封臣/平民均可使用。**工具范围严格受限**：仅查询、谏言（`submit_advisory`/`submit_secret_advisory`）、诏令与问询回复（`submit_edict`/`reply_consult`，国王）、快速加入/脱离王国（`change_kingdom`，受家族等级限制）——行军/军事/金钱物品/好感/写信一律不可用（有替代路径），且不提供 `browse_tools`（防止解锁受限分类绕过权限）。玩家可经秘书处调 `submit_advisory` 提交公开谏言（雇佣兵除外） |
| **天意** | 虚拟实体（ID: `__fate__`，HeroRef=null，与史官并列）。家族补充系统：在世贵族/佣兵家族**总数**（封臣世家 + 雇佣兵公司，佣兵不论是否受雇都计入）低于「家族总数补充阈值」时被激活，决定新家族名称/文化/投效势力/以封臣还是佣兵身份入世（`create_clan`，能力门控仅天意可用），成员程序生成、等级 2、族长带兵、旗帜随机，入原始史料但不激活史官。只在玩家位于大地图时检测（捏脸/初始化阶段计数失真），有 MCM 冷却防连补；成员模板用 `RebelliousHeroTemplates`（`GetRandomTemplateByOccupation(Lord)` 查不到 Lord 模板、恒 null 的历史死路径已弃） |
| **提示词人称统一** | 上下文只出现「你」(Agent 自己) 和「对方」(交互对象) 两角色，"TA"等模糊指代全部禁用 |

> **AI 聊天对话入口**：`LordChatBehavior` 在 `hero_main_options`（普通领主对话）与 `player_responds_to_surrender_demand`（被强敌擒获的投降/应战对话）两个节点注册【AI 聊天】选项。后者让被擒玩家可谈判，agent 可用 `let_go` 放行；`AIChatScreenVM` 在回复完成后检测 `PlayerEncounter.LeaveEncounter` 为真时自动关闭聊天并 `EndConversation()` 结束对话（对话中遭遇不会自动 tick 结束），玩家获释。

> **提示词同步规则（newer-wins）**：基础目录 `_Module/Prompts/` 是模板源，战役创建时复制到 `Campaigns/{战役}/`。每次进档（`OnSessionLaunched` → `PromptManager.StartCampaign`）会做同步——**只要基础目录文件比战役副本新，就覆盖战役副本**。玩家在战役副本上的手动编辑（时间戳更新）会被保留。
>
> **上下文模板结构（缓存优化）**：`context_template.txt` 以 `<!--VOLATILE-->` 标记分界。标记前为**稳定前缀**（身份/persona/世界背景/游戏规则/工具清单/行为守则）→ system 消息；标记后为**易变块**（当前时间/自身状态/对对方认知/目标/客观关系/内政报告）→ 单独作为【当前状况】**system 消息**插在历史之后、当前 user 消息之前。目的：让 system+历史构成逐字节稳定的前缀，最大化 DeepSeek 前缀缓存命中（命中输入 0.02元/M vs 未命中 1元/M，价差 50 倍）。该优化参考 DeepSeek 官方上下文缓存文档与 [DeepSeek-Reasonix](https://github.com/esengine/DeepSeek-Reasonix)。`ContextBuilder` 相应提供 `BuildStable`/`BuildVolatile`（合并 `Build` 保留兼容）。**史官 intent 例外**：不拆易变块，走合并 `Build()` 保持单一 system 消息——文笔是模组核心，结构与旧版一致。旧版模板（无标记）整体按稳定处理，行为不变。
>
> **易变块必须用 system 角色，不得用 user**（v2.0.2 修复的回归）：v1.4.0 曾把【当前状况】作为 `user` 消息插在玩家消息前——模型会把这条系统情境当成"对方说的话"，导致回复引用对话中不存在的"请教/答复"等幻觉。改回 `system` 角色后模型明确这是系统注入的情境，缓存优化保留。易变块**固定为 system 角色**（OpenAI 兼容协议允许中间 system，不再运行时探测回退）。
>
> **已知边界情况（待解决）**：如果玩家在战役副本自定义了某文件（副本时间戳更新），之后基础目录又修改了（基础更晚）→ newer-wins 会覆盖副本，玩家自定义丢失。当前对开发期是期望行为；未来如需保护战役自定义，需引入"用户修改标记"机制（如哈希比对或 .custom 标记文件）。
>
> **运行期热重载**：游戏运行中，编辑战役副本文件立即生效（加载器按 LastWriteTime 重新读取）；编辑基础目录要下次进档才同步。

## 3. 单一模型架构

整个模组只有**一套 LLM 调用链路**（无第二段独立调用）——角色扮演与工具调用在同一次请求内完成，模型同时输出文本和 tool_calls：

| 项 | 说明 |
|------|------|
| 提示词来源 | `context_template.txt` → ContextBuilder 动态组装（`BuildStable`/`BuildVolatile`/合并 `Build`） |
| 消费入口 | `AIChatClient.SendMessage`（SSE 流式多轮循环） |
| 模板文件 | 只有 `context_template.txt` 生效；`system_prompt.txt`/`agent_system.txt` 旧模板已废弃删除 |

> 历史：早期架构曾有「前台模型」概念（第二次调用把 Agent 上下文变成自然对话），已在单模型架构演进中移除——当前角色扮演就是 Agent 模型本身输出的内容，不存在单独的对话润色调用。

## 4. 场景连接配置（每场景独立 URL/模型/密钥/接口类型）

每个 intent（场景）可以配置独立的 API URL / 模型 / API 密钥 / 接口类型，**逐字段兜底**到全局「连接设置（兜底）」——场景字段留空就用兜底的对应字段。

- **`ConnectionResolver.cs`**：`Resolve(intent)` 返回生效的 `ConnectionInfo(Url, Model, ApiKey, ProviderKind)`。场景映射见 `ConnectionResolver.Resolve` 的 switch（对话与书信合并一个场景，因为两者同属 chat_logs 单一线程）。接口类型同样逐字段兜底。
- **消费点**：`AIChatClient.SendMessage` 按 `intent` 解析连接并 `LLMProviders.Create(conn.ProviderKind)` 建 provider；`AgentManager` persona 生成走「对话与书信」场景；`AgentScheduler` 天意建族/史官门控与 `LordChatBehavior` 聊天入口判断均用对应场景的生效密钥
- **测试**：`AIChatClient.TestConnection(scenario)` 按场景生效配置测试（原始请求，不依赖战役上下文，主菜单可用）；每个场景组在 MCM 有「测试此场景」按钮
- **统一 max_tokens**：全模组最大 Token 数只由 MCM「最大 Token 数」控制（`settings.MaxTokens`），含 persona 生成与连接测试，禁止各处硬编码
- **v2.0.0 初版**：设置 Id/FolderName 已改为 `AIChronicle_v1`/`AIChronicle`，旧的 MCM 配置会重置一次（需重填 API Key）

## 5. LLM 厂商兼容（Provider 抽象层）

> ⚠️ **架构铁律：厂商差异声明化，禁止运行时探测。**

本模组面向多家 LLM 厂商（DeepSeek / MiniMax / Qwen / GLM / 豆包 等），但**不在业务代码里写 `if 厂商` 特判**，而是参考 [opencode](https://opencode.ai/docs/providers/) 的做法——绝大多数厂商共用一个 OpenAI 兼容适配器，差异用配置声明，不靠运行时探测。

对应地，本模组抽象出 **`ILLMProvider` 层**（`Core/LLM/`）：

| 文件 | 职责 |
|------|------|
| `LLMProvider.cs` | 接口 `ILLMProvider` + 能力声明 `LLMCapabilities` + 统一数据结构（请求 / 归一化流块） |
| `OpenAICompatibleProvider.cs` | 通用实现：标准 OpenAI 协议 + 宽容解析（空 `choices:[]`、内联 `<think>` 剥离等，任何 OpenAI 兼容端点都可能有） |
| `DeepSeekProvider.cs` | OpenAI 兼容 + DeepSeek 扩展（`reasoning_content` 回传、`prompt_cache_hit_tokens` 缓存字段、`reasoning_effort` 参数） |
| `GLMProvider.cs` | 继承 DeepSeek + `clear_thinking: false`（保留式思考）；缓存字段嵌套不做统计 |
| `MiMoProvider.cs` | 继承 DeepSeek + `thinking` 开关 + `max_completion_tokens`；多轮工具调用必须回传 reasoning（否则 400） |
| `QwenProvider.cs` | 继承 OpenAICompatible + `enable_thinking: true`（思考全开，v2.3.0 用户决策）+ 嵌套缓存统计（`usage.prompt_tokens_details.cached_tokens`，未命中=总数-命中推算）；多轮不回传 reasoning（Qwen `preserve_thinking` 默认 false，回传反而按输入计费）。**思考量用 `thinking_budget`（长度上限）控制**：Qwen 无 `reasoning_effort`，按官方档位映射 MCM 思考强度——low=4096 / high=16384(medium) / max=262144(xhigh)，史官(null)按 high 档 |

**核心原则：**

1. **厂商差异 = 能力声明，不是运行时探测。** 每个 provider 静态声明自己的能力（是否支持 `reasoning_effort`、reasoning 字段名、缓存字段名、thinking 开关方式），由 MCM「接口类型」下拉显式选择。**禁止**"先发错参数再看 400 回退"的探测式兼容。
2. **OpenAI 兼容是通用底座。** MiniMax/Qwen/GLM/豆包/DeepSeek 全都讲 OpenAI 兼容协议，共用一个 `OpenAICompatibleProvider`。DeepSeek 只是"OpenAI 兼容 + 少量扩展字段"，单独一个 `DeepSeekProvider` 打开这些扩展，而非重写协议。
3. **宽容解析内置在通用层。** 空 `choices:[]`、内联 `<think>` 标签剥离、`[DONE]` 结束等，是 OpenAI 兼容端点的常见行为，由通用 provider 无条件宽容处理（对 DeepSeek 是 no-op），**不是厂商特判**。

**扩展方式**：新增厂商 = 先判断它是不是 OpenAI 兼容——是则直接配 URL/模型（无需改码）；不是（如 Anthropic 的 Messages API）才新增一个 provider 实现。**绝不在 `AIChatClient`/`AgentManager` 的业务逻辑里加厂商 `if`。**（Qwen/百炼即是 OpenAI 兼容：通用层可直接用，`QwenProvider` 仅为其打开思考开关与缓存统计两项声明式扩展。）

> **缓存统计字段差异**：DeepSeek 用平铺字段 `prompt_cache_hit_tokens`/`prompt_cache_miss_tokens`；Qwen 用嵌套字段 `usage.prompt_tokens_details.cached_tokens`（只报命中不报未命中，未命中 = `prompt_tokens - cached_tokens` 推算）。`LLMCapabilities` 相应支持 `PromptTokensField`（点分嵌套路径），`CacheMissField` 留空时自动走推算分支。调试日志的命中率统计对两类端点统一生效。

### 屎山教训（为什么禁止运行时探测）

早期为兼容 MiniMax 曾在业务代码里加运行时探测标志（`_streamUsageSupported`/`_reasoningEffortSupported`/`_volatileSystemSupported`）+ 400 回退重试 + 散落的 `StripThinkTags`/空 `choices` 特判。这是错误方向：

- 每遇到一个厂商差异就加一个标志/回退，差异越积越多，`SendMessage` 变成 if-else 泥潭；
- 运行时探测靠"故意发错参数再捕获 400"来摸端点能力，脆弱且难测；
- 厂商能力应当是**配置时一次性声明**的静态事实，而不是每次请求时试探。

重构后，厂商差异全部收敛到 `Core/LLM/` 的 provider 实现里，业务代码（`AIChatClient.SendMessage` 多轮循环）只面对统一的请求/流解析接口，不感知厂商。

## 6. 信息隔离规则（硬约束）

| 当前 NPC 可访问 | 权限 |
|---------------|------|
| `NPCs/{自己}/persona.txt` | 只读 |
| `NPCs/{自己}/character.json` | 只读 |
| `NPCs/{自己}/knowledge/{entity_id}.txt` | 读 + 写 + 追加 + 编辑 + 删除 |
| `NPCs/{自己}/relationships/{其他}.txt` | 读 + 写 + 追加 + 编辑 + 删除 |
| `NPCs/{自己}/goals/*.txt` | 读 + 写 + 追加 + 编辑 + 删除 |
| `NPCs/{自己}/chat_logs/*.txt` | 只读（不可修改） |
| `NPCs/{自己}/decisions/*.txt` | 读 + 写 + 追加 + 编辑 + 删除 |
| `World/factions.txt` | 只读 |
| `World/settlements.txt` | 只读 |
| `World/history/**`（原始史料/编年史） | 只读 |
| `World/advisory/`（封臣公开谏言） | 只读：史官可读任何国家；其他 agent 仅本国 |
| `World/edict/`（国王诏令） | 只读：史官可读任何国家；其他 agent 仅本国 |
| `World/secret_advisory/`（秘密谏言） | 只读：**仅本国国王**；本国封臣与史官不可读 |
| `World/diplomacy/consults/`（国王外交问询） | 只读：史官可读任何国家；参与双方国王可读；第三方不可读 |
| `World/correspondence/`（私有密使） | 只读：**仅参与者双方**（实体 ID 匹配）；史官与任何第三方不可读 |
| 其他 NPC 的任何文件 | **禁止** |

> 玩家的实体目录（`{Name}_main_hero`）含 `thread_read_state.json`（各线程已读水位，O 键未读角标依据）——仅玩家可写，Agent 在信息隔离下不可访问。

## 7. 扩展方式

### 新工具（5 步）

1. `tools.json` 或 `agent_tools.json` 中加一条工具定义，**必须指定 `category`**（遵循 opencode 风格的 `description` 格式）
2. `ToolExecutor.ExecuteToolCall` 的 `switch` 中加一个 `case`
3. 如果工具涉及部队行为（移动、劫掠、围攻等），还需：
   - 在 `Execute*` 方法中注册 `PendingAction`（通过 `PartyBehaviorManager.GetOrCreateAction` 设置 `Behavior` 和目标）
   - 在 `PartyBehaviorManager.Tick()` 的 switch 中添加对应行为的恢复/清理逻辑
   - 对于持续性行为（驻防、巡逻、护送），设置 `CheckInHours` 以启用定时签到
4. `AIChatScreenVM` 的 toolCall 显示 switch 中加对应描述
5. `ContextBuilder.CapabilityToolMap` 中按能力映射（若需能力过滤）

`SubModule.OnApplicationTick` 已配置为每帧调用 `PartyBehaviorManager.Tick()`。

### 新增 Entity 类型（4 步）

1. 在 `Entity.cs` 的 `EntityCapability` 枚举中添加新能力
2. 在 `EntityManager.ComputeCapabilities()` 中为新 Entity 类型计算能力集合
3. 在 `ContextBuilder` 的 `CapabilityToolMap` 中建立能力→工具映射
4. 在 `tools.json` 的 `parameters.properties` 中添加 `capability_required` 字段

## 8. 提示词设计规范（人称约定）

所有提示词文件必须遵守统一的人称约定，防止 Agent 混淆自己与对方的信息：

- 「你」= Agent 自己（如"你是拉盖娅，现任南帝国女皇"）
- 「对方」= 正在交互的目标 Entity（如"对方是炎萌"）
- 「该人物」= `query_character` 返回结果的前缀（如"该人物：拉盖娅"）
- 禁止出现「TA」、「他/她」、「其」等模糊指代

此约定适用于 `context_template.txt` 以及所有新增的工具返回格式。

## 9. 提示词克制原则（模糊化，最核心的提示词设计原则）

提示词**只引导、不规定**——让 LLM 自行判断，保持涌现与多元。写任何提示词时对照以下几条：

- **不列举具体场景/类别清单**：会框死 LLM 的判断，让行为模板化。例如秘密谏言说明不写「私议王上、密报、密约」，而写「你认为不适合被历史记录或旁人知晓的事」
- **用开放性表述**：「若你觉得…」「是否如此由你的性格与处境决定」，而非「必须/应当/禁止」的强制清单
- **个人立场不强制度**：天命信仰、名分、谏言批判等，由人格与处境自然涌现，不写成统一规则（为此 persona 维度用随机掷定而非 LLM 自定，见 AgentManager 天命信仰）
- **不注入上下文**：能靠提示词提醒 LLM 调用工具（如读写 diary、检索），就不要注入内容——注入会温水煮青蛙式稀释所有提示。这条有先例教训：别的 AI 模组因持续注入而系统崩坏
- **游戏规则层是例外**：`game_rules.txt` 注入卡拉迪亚的实际运转机制（机动/金钱/部队上限/兵种/招募/战争/影响力/阵营归属——氏族加入哪国全在族长自决、无须国王准许），让 agent 按游戏机制而非现实经验做决策。它与「注入叙事/记忆内容」不同——是**稳定参考知识**（同世界背景），不是易变剧情。注入稳定前缀（缓存友好）；**史官不注入**。受 MCM「注入游戏规则」开关控制
- **度**：提示词给方向但不给答案。玩家可编辑的提示词默认按此原则起草

## 10. 记忆系统设计思想：日记即索引

长期记忆的核心突破点是每个 Agent 的 `decisions/diary.txt`——格式 `[年季节日] 类型：内容`（类型：决定/承诺/情报/计策/评价/结果）。

**日记是记忆的索引，聊天记录是全文**：
- 日记时间戳（`[1089春16]`）与 `chat_logs/` 时间戳（`[第1089年，春季第16日]`）**天然对应**
- 回忆路径：先读日记定位某条决定/计策 → 需要细节时 grep 在 `chat_logs/` 搜对应日期或关键词 → 看到完整对话
- 时间戳就是日记和聊天的「外键」，grep 按日期 join——比读整个聊天省 token，比只读日记有上下文

**结果追踪（防止旧计划永悬）**：提示词强制「计策/承诺/计划必须追踪结果」——日记里没有「结果」条目对应的计策/承诺/计划会被当作仍在进行中。规则：
1. 每次对话/审视开始先回顾日记里**没有结果标记**的条目，确认其当前实况
2. 一旦得知某个计策/承诺/计划的结果（成功/失败/中止/改变/已兑现），立即补记 `[年季节日] 结果：…`
3. 一件事的现状以日记里关于它的最后一条记录为准——有了结果就别再当它进行中

**设计优先级**：未来设计记忆相关系统时，**优先以日记为锚点**，围绕「日记索引 + 聊天全文」的互证结构展开，不要另起炉灶。日记格式必须保持 `[年季节日] 类型：内容` 一致，否则检索失效。

**为什么是日记而不是注入**：日记是拉取式（agent 自觉读写，见克制原则），不占上下文；注入任何记忆内容都会膨胀上下文、稀释提示。检索工具（grep 的 context_lines/max_results）为此设计。

## 11. 日记权威化与记忆巩固（diary-as-authority）

**记忆优先级（解决"看到日记、战略、知识就茫然"）**：查询工具实时数据（query_character/query_world_state，系统权威）> 日记（`decisions/diary.txt`，自我记忆权威）> 认知（`knowledge/{对方}.txt`，对第三方的了解）。长期战略不再单列文件，以日记「战略」类型条目形式存在（strategy.txt 已并入日记）。

**日记可被比它更新的聊天记录修正**：chat_logs 是系统自动写入的客观往来记录（Agent 只读、不会漏），日记是 LLM 自写的索引（可能漏记/滞后）。若 chat_logs/ 中有比日记新的往来，**以聊天记录为准**，并补记日记（旧决定被推翻则补记 `[日期] 结果：…`，只追加不改写旧条目）。

**记忆巩固机制（`MemoryConsolidator.cs`，保底而非依赖自觉）**：
- 自我审视类激活前（**国王政务 KingDiplomacy / 封地审视 FiefReview / 外交问询回应 KingConsult / 封臣自省 SelfReview / 密使回应 EnvoyReceived**），先比较日记最新条目日期与 chat_logs 各文件最新消息日期
- 若日记落后（存在"比日记更新的往来"），跑一次**巩固 pass**：agent 自读日记 + 较新往来 → 把值得记住的决定/承诺/计策/结果/战略 `append_file` 补记进 diary。静默执行，不写 chat_logs、不弹玩家消息
- 只在落后时触发，多数时候零成本；受 MCM「启用记忆巩固」开关控制（默认开）
- 日期解析兼容两种写法：日记 `[1090春3]`/`[1090年冬第9日]`、聊天 `[第1090年，春季第15日]`——日记格式务必保持统一，否则程序无法可靠判断"谁更新"
- 巩固提示词文件化、可热重载：`consolidation_rules.txt`（intent 规则）+ `memory_consolidation.txt`（激活指令）

## 12. 模组类型与入口模式

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

## 13. UI 开发（GauntletUI）

Bannerlord 使用自研的 GauntletUI 框架。UI 开发分两层：

1. **XML 布局**（Widget/Prefab）：定义界面结构和样式
2. **C# ViewModel**：定义数据绑定和行为逻辑

本模组如果涉及 UI，参考 `BLSource/TaleWorlds.GauntletUI/` 和 `BLSource/TaleWorlds.Engine.GauntletUI/`。

> 写 GauntletUI 前先拆解游戏已有同类 Prefab（`Modules/Native/GUI/Prefabs/`、`Modules/Multiplayer/GUI/Prefabs/`）：找到行为最接近的现有 Prefab，逐段分析 Widget 层级、`DoNotAcceptEvents` 逻辑、`InputRestrictions` 设置，基于现有模板改，不要从零写。
>
> 典型踩坑：`DoNotAcceptEvents="true"` 的含义是「本 Widget 不接事件，但透传给子级」。

## 14. AI 交互架构（function calling）

### 设计原则

本模组使用 OpenAI 的 **function calling（工具调用）** 机制处理 AI 与游戏世界的交互。未来所有 AI-世界交互都应基于此机制扩展。

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

实现位置：`AIChatScreenVM.ExecuteSend` 中解析 `ChatResponse.ToolCalls` 的循环中。

**注意：** 玩家长时间聊天时，屏幕左下角的信息弹窗是有限的。如果消息太多会被新消息顶掉，建议在未来的 UI 升级中考虑将状态提示显示在聊天窗口内部。

### 当前实现

所有 70 个工具（51 个游戏工具 + 19 个文件/通信工具）定义在 `tools.json`/`agent_tools.json`（热重载，不硬编码）。每次 API 请求都附带能力过滤后的工具定义。模型自行判断是否需要调用。返回的 `ChatResponse` 包含：
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

所有 assistant 消息如果包含 tool_calls，会在聊天历史中追加对应的 `role: "tool"` 确认消息。这样模型在下一轮对话中看到自己的工具调用被处理，形成正反馈循环。

## 15. 流式架构（参考 opencode）

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
- **思考强度（reasoning_effort）**：MCM「思考强度」可调（low/high/max），默认 `low`——思考按输出价计费，是成本大头。**史官固定 high**（文笔核心，且不发送该参数、用 API 默认值以兼容）；其余 intent 发送 MCM 设置值。**是否写入请求由 provider 能力声明决定**（`SupportsReasoningEffort`）——DeepSeek 发、OpenAI 兼容端点（MiniMax/Qwen/GLM 等）不发，不做运行时 400 探测。
- tool_calls delta 跨 chunk 累积（DeepSeek 协议中 tool_call 分多次 delta 传输）

## 16. 环境概况

| 项目 | 值 |
|------|-----|
| 游戏 | Mount & Blade II: Bannerlord v1.4.8 |
| 游戏路径 | `D:\steam\steamapps\common\Mount & Blade II Bannerlord` |
| IDE | JetBrains Rider 2026.2 |
| 开发目录 | `C:\Users\<用户名>\BLMods\AIChronicle` |
| .NET SDK | 9.0+ |
| 目标框架 | net472 (Windows) + net6 (Xbox/Store) |
| 模组框架 | Harmony 2.3.3 (运行时补丁) |
| 四前置 | Harmony, ButterLib, UIExtenderEx, MCM (MBOptionScreen) |
| 语音合成 | NAudio 2.2.1（NuGet，播放 mp3）+ 免费 Edge TTS（手写 WebSocket 客户端，无第三方库） |

## 17. 完整目录结构

```
C:\Users\<用户名>\BLMods\AIChronicle\
├── AIChronicle.csproj          ← 项目配置（引用、框架、部署）
├── Core/                       ← 引擎层：入口、设置、基础设施
│   ├── SubModule.cs            ← 模组入口，生命周期回调
│   ├── Settings.cs             ← MCM 设置基类（连接兜底 + 游戏设置）
│   ├── Settings.Scenarios.cs   ← MCM 十个场景连接配置组（URL/模型/密钥/接口类型 + 测试按钮）
│   ├── ConnectionResolver.cs   ← 场景连接解析器（intent → 生效 URL/模型/密钥/接口类型，逐字段兜底到全局连接设置）
│   ├── DebugLogger.cs          ← 调试日志（LLM 调用摘要/思维链摘录 → 战役 debug_logs/）
│   ├── SafeFileIO.cs           ← 带重试的文件 IO（并发读写同一文件时避免"文件正被使用"异常）
│   ├── MainThreadExecutor.cs   ← 主线程分发器（后台线程的工具执行排队回主线程，防跨线程崩溃）
│   ├── TtsService.cs           ← 语音合成门面（ITtsProvider 接口 + 合成/磁盘缓存/播放/打断）
│   ├── EdgeTtsProvider.cs      ← Edge TTS 免费引擎（手写 WebSocket 客户端 EdgeWsClient，可设浏览器 UA）
│   └── LLM/                    ← LLM 厂商兼容层（Provider 抽象，声明式厂商差异）
│       ├── LLMProvider.cs      ← ILLMProvider 接口 + 能力声明 LLMCapabilities + 统一数据结构
│       ├── OpenAICompatibleProvider.cs ← 通用 OpenAI 兼容实现（宽容解析：空 choices/内联 think）
│       ├── DeepSeekProvider.cs ← DeepSeek 扩展（reasoning 回传 / 缓存字段 / reasoning_effort）
│       ├── GLMProvider.cs      ← 智谱（clear_thinking 保留式思考）
│       ├── MiMoProvider.cs     ← 小米（thinking 开关 / max_completion_tokens）
│       └── QwenProvider.cs     ← 阿里云百炼 Qwen（enable_thinking 思考全开 / 嵌套缓存统计）
├── Agents/                     ← Agent 核心：上下文、调度、记忆
│   ├── AgentManager.cs         ← Agent 管理器基类（NPC 文件系统、路径权限、persona 生成）
│   ├── AgentManager.Files.cs   ← 文件工具执行（read/write/edit/delete/move/glob/grep/list）
│   ├── AgentManager.Threads.cs ← 玩家书信线程已读水位
│   ├── AgentManager.Proposals.cs ← 外交提案存储与解析
│   ├── AgentManager.Permissions.cs ← 路径权限模型（信息隔离硬约束）
│   ├── ContextBuilder.cs       ← Context 动态组装器
│   ├── AIChatClient.cs         ← LLM API 客户端（HTTP 流式请求、SSE 解析、多轮循环），工具调用委托给 ToolExecutor
│   ├── PromptManager.cs        ← 提示词管理器（文件热重载、战役目录、角色 JSON）
│   ├── MemoryConsolidator.cs   ← 记忆巩固（diary 权威化保底：自我审视前检测日记落后并补记）
│   ├── AgentScheduler.cs       ← 事件调度器基类（优先级队列、Tick、国王激活、盟约到期检测）
│   ├── AgentScheduler.Events.cs  ← 事件处理（ProcessEvent/玩家事件/外交提案轮询）
│   ├── AgentScheduler.Advisory.cs ← 封臣自省系统（公平轮转、选池、QueueSelfReview）
│   ├── AgentScheduler.Historian.cs ← 史官事件处理（年度编年史 + 专题）
│   ├── AgentScheduler.Fate.cs   ← 天意建族事件处理
│   └── PartyBehaviorManager.cs ← 部队行为管理器（PendingAction 状态机、Tick()、定时签到）
├── Entities/                   ← Entity 统一抽象
│   ├── Entity.cs               ← Entity 抽象（玩家/NPC 统一模型、EntityCapability）
│   └── EntityManager.cs        ← Entity 生命周期管理
├── Tools/                      ← 工具执行器（50+ 游戏工具，按领域拆 partial）
│   ├── ToolExecutor.cs         ← 基类（工具入口 ExecuteToolCall + 共享助手）
│   ├── ToolExecutor.Query.cs   ← 查询类工具（人物/定居点/世界/内政/技能）
│   ├── ToolExecutor.Intel.cs   ← 军情迷雾（query_party_troops + 距离/传闻三档模糊模型）
│   ├── ToolExecutor.Military.cs ← 行军/军事（move/raid/besiege/form_army/驻防/招募/俘虏）
│   ├── ToolExecutor.Social.cs  ← 社交/通信（金钱物品/好感/放行/书信/谏言/诏令/问询/密使）
│   ├── ToolExecutor.Diplomacy.cs ← 阵营变更（change_kingdom 全模式 + 家族等级限制）
│   └── ToolExecutor.Fate.cs    ← 天意建族（create_clan + 预算成功才占用 + 随机旗帜/定居点）
├── UI/                         ← 界面层
│   ├── AIChatScreen.cs         ← 聊天屏幕管理器（静态类，GauntletLayer 挂载）
│   ├── AIChatScreenVM.cs       ← 聊天 ViewModel（消息列表、输入绑定、function calling 处理）
│   ├── LetterListScreen.cs     ← 书信收信人列表屏幕
│   └── HistoryScreenVM.cs      ← 史书屏幕 ViewModel
├── Systems/                    ← 游戏系统与 Harmony 补丁
│   ├── DiplomacyService.cs     ← 外交服务（宣战/议和/结盟/贸易协定/回复提案 + FindKingdom + 盟约/贸易到期记录与清除）
│   ├── HistoryRecorder.cs      ← 历史记录器（监听游戏事件写入原始史料）
│   ├── LordChatBehavior.cs     ← 对话中插入聊天选项，战役 ID 管理
│   ├── DiplomacyBanPatch.cs    ← 拦截原版外交（禁止原版外交开关）
│   ├── FiefAssignmentPatch.cs  ← 攻城后册封由 Agent 主导
│   ├── ExecutionNoPenaltyPatch.cs ← 处决无惩罚
│   └── ClanDiscontinuationPatch.cs ← 独立家族永续（禁用原版 28 天灭族，MCM 可开关）
├── docs/                       ← AI 参考文档（架构/子系统/工具清单/调试/Harmony）
├── AGENTS.md                  ← 常驻开发指令（本文件）
├── README.md                  ← 开源主入口（英文简介，指向 README_MOD.md / AGENTS.md）
├── CONTRIBUTING.md            ← 贡献指南（构建方法、文档维护规则、PR 流程）
├── scripts/
│   ├── ci-health.mjs          ← 仓库健康检查（JSON 解析、JSON↔switch 同步、密钥/个人路径扫描，CI 与本地共用）
│   └── package.ps1            ← 编译+部署+打包纯净发布版（dist/AIChronicle_v<版本>.zip，给其他玩家用）
├── .github/workflows/
│   ├── health.yml             ← 轻量 CI（无需游戏 DLL，push/PR 自动跑，绿标）
│   └── build.yml              ← 手动编译工作流（workflow_dispatch，需 BANNERLORD_GAME_DIR 或 GAME_DLLS_URL 密钥）
├── _Module/
│   ├── SubModule.xml         ← 模组元数据（ID、依赖、DLL路径）
│   ├── GUI/
│   │   └── Prefabs/
│   │       ├── AIChatScreen.xml  ← 聊天窗口布局
│   │       └── LetterListScreen.xml ← 书信收信人列表
│   └── Prompts/
│       ├── world_info.txt     ← 默认世界背景（六大王国 + 天命）
│       ├── world_info_nords.txt ← 可选诺德势力段（MCM「包含诺德势力」开启时由 ContextBuilder 拼入主世界观）
│       ├── InitialHistory/     ← 开局预置初始历史（《卡拉迪亚上古编年史》+ 六国《XX源流纪事》，史官风格，止于1084年；战役进档时复制到 World/history/chronicles/，只补缺失不覆盖）
│       ├── game_rules.txt     ← 游戏运转规则（玩家可编辑，热重载）
│       ├── tools.json         ← 游戏工具定义（热重载）
│       ├── agent_tools.json   ← Agent 文件工具定义（热重载）
│       ├── persona_generation.txt ← NPC性格生成提示词（玩家可编辑，热重载）
│       ├── advisory_rules.txt  ← 封臣谏言规则（保留，自省进谏仍走此套归档，热重载）
│       ├── self_review_rules.txt ← 封臣自省规则（热重载）
│       ├── fief_review_rules.txt ← 封地审视规则（被夺方激活，热重载）
│       ├── clan_replenishment_rules.txt ← 天意建族规则（家族补充，热重载）
│       ├── consolidation_rules.txt ← 记忆巩固行为规则（热重载）
│       ├── memory_consolidation.txt ← 记忆巩固激活指令（热重载）
│       ├── Templates/         ← NPC 目录模板
│       │   ├── context_template.txt ← Context 模板
│       └── Campaigns/         ← 各战役目录（运行时创建）
│           └── {战役名}/
│               ├── world_info.txt       ← 本战役世界背景（可独立编辑，热重载）
│               ├── world_info_nords.txt ← 本战役诺德势力段（可独立编辑，热重载）
│               ├── game_rules.txt       ← 本战役游戏运转规则（可独立编辑，热重载）
│               ├── persona_generation.txt ← 本战役性格生成提示词（热重载）
│               ├── context_template.txt ← 本战役 Context 模板（热重载）
│               └── NPCs/          ← Agent 管理的 NPC 文件系统
│                   └── {entity_id}/                 ← {Name}_{StringId}（如 博泰罗_CharacterObject_1664）
│                       ├── persona.txt   ← [MOTIVATION]/[TRAITS]/[SPEECH_STYLE]
│                       ├── persona_meta.json ← 自定义人格维度（权力欲/归属重心/冒险倾向/天命信仰/战争倾向）
│                       ├── knowledge/
│                       ├── chat_logs/
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

部署后自动复制到：`D:\...\Modules\AIChronicle\bin\Win64_Shipping_Client\AIChronicle.dll`

> ⚠️ **`_Module/` 目录（含 `Prompts/`、`GUI/`、`SubModule.xml`）也在 build 时由 Bannerlord.BuildResources 拷到游戏 Modules。纯提示词改动也必须 build 才生效**——只改项目里的 `.txt`/`.json` 不 build，游戏里一直是旧版。战役副本（`Campaigns/{战役}/`）在下次进档时按 newer-wins 同步覆盖。
