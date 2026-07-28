# MyFirstMod — AI 聊天模组

> **交叉参考：** 实现功能前请同时阅读 **AGENTS.md**，其中包含开发环境、编译命令、Harmony 模式、BLSource 使用方法等技术细节。两份文档互为补充——README_MOD.md 告诉你模组"能做什么"，AGENTS.md 告诉你"怎么做"。

在《骑马与砍杀2：霸主》中，与 AI 领主进行基于 LLM 的自然语言对话。

---

## 当前功能

### AI 领主聊天

- 与任意领主对话时，对话选项中均出现 **「【AI 聊天】」** 选项
- 点击后打开 **专用聊天窗口**（模态屏幕），窗口中显示完整的对话历史
- 输入任意消息发给 LLM，AI 会获取**完整对话上下文**（之前的聊天记录都会传给 AI）
- LLM 会以领主的身份角色扮演回复（中世纪贵族口吻，中文）
- 关掉聊天窗口后**回到对话界面**，可以继续正常交谈
- AI 可以在对话中了解玩家，通过 **function calling** 机制自动更新对玩家的认知
- 首次对话时自动用 LLM 为 NPC 生成**结构化 persona**（动机、性格特质、表达风格三段式）
- **Entity 系统**：玩家和所有 NPC 统一为 Entity，Agent 不区分"玩家"和"其他 NPC"
- **动态上下文组装**：ContextBuilder 根据交互双方动态构建系统提示词
- **工具能力过滤**：每个 Entity 有 EntityCapability 集合，无部队的 NPC 不拿到行军工具
- 认知更新机制使用 OpenAI function calling 协议
  - Agent 可以调用 `query_settlement` 查询任意定居点实时信息（所有者、繁荣度）
  - Agent 可以调用 `query_world_state` 获取当前世界局势（各王国兵力、交战状态）
  - Agent 可以调用 `move_to_settlement` 工具，让 NPC 部队实际行军移动到地图上的城镇/城堡（非瞬移）
  - Agent 可以调用 `wait_at_settlement` 工具，让 NPC 在到达城镇后停留指定时长（游戏内小时）
  - Agent 可以调用 `raid_settlement` 劫掠村庄（强征物资 / 强拉壮丁 / 洗劫）
  - Agent 可以调用 `besiege_settlement` 围攻城镇或城堡
  - Agent 可以调用 `engage_party` 追击并攻击另一支部队
  - Agent 可以调用 `defend_settlement` 驻防守卫某个定居点
  - Agent 可以调用 `patrol_settlement` 围绕定居点巡逻警戒
  - Agent 可以调用 `escort_party` 护送跟随另一支部队
  - Agent 可以调用 `go_around_party` 绕行回避某支部队
  - 所有行军/军事工具在被中断（逃离、战斗）后自动恢复原任务，不会丢失指令
  - Agent 可以调用 `cancel_action` 取消当前任务，让部队回归自主 AI 控制
  - 持续性任务（驻防/巡逻/护送）到达目标后启动定时签到：到时 Agent 自动激活，可自行决定是否继续、转去做别的事、或向阵营领袖汇报
  - Agent 可以调用 `change_relation` 修改对任意人物的好感度（单次上限在 MCM 中设置，默认 +-5），可指定目标实体
  - Agent 可以调用 `give_gold` 赠予任意人物金币（直接转账），可指定目标实体
  - Agent 可以调用 `request_gold` 向任意人物索要金币（向玩家索要时弹出确认对话框，向 NPC 索要时自动划转）
  - Agent 可以调用 `query_character` 查询任意人物的公开信息
  - Agent 可以调用 `query_recent_events` 查询任意人物的近期事件（比武夺冠、被俘、释放、婚嫁、阵亡等百科记录）
  - Agent 可以调用 `query_surroundings` 扫描周围环境：当前位置、附近城镇/城堡、附近部队及其阵营关系和距离
  - Agent 可以调用 `query_war_status` 查询王国战争状态：双方阵亡数、攻下的城镇/城堡、劫掠村庄数
  - Agent 国王可以调用 `declare_war` 宣战（单向立即生效）
  - Agent 国王可以调用 `propose_peace` / `propose_alliance` / `propose_trade` 提出外交提案（双向，需对方国王同意）
  - Agent 国王可以调用 `respond_to_diplomacy_proposal` 接受或拒绝收到的外交提案
  - 外交提案存储在 `World/diplomacy/` 目录，对方国王的定期激活由 `AgentScheduler` 管理（每 15 天一次）
  - **已知限制：** "禁止原版外交" 开关仅阻止 AI 国王通过原版机制发起外交，**不阻止玩家通过王国界面手动发起外交**（宣战/议和/结盟/贸易按钮仍然有效）。这是当前的一个已知缺口——向后兼容补丁路径未找到。玩家作为国王应使用 M 键秘书处执行外交，王国界面按钮请手动避免使用。
  - Agent 可以调用 `grep` 在个人文件系统中按关键词搜索，定位到具体文件和行号后再用 `read_file` 精读

### 书信系统

- 战役地图上按 **O 键**打开书信面板（收件箱 + 已知领主列表）
- **收件箱**：以 `[来信] 发信人` 格式显示收到的信件，点击可阅读全文并一键回复
- **写信**：选择一位对话过的领主，进入写信界面
- 战役地图上按 **P 键**打开秘书处（玩家自我政务界面）
  - 秘书处是玩家以自己身份使用 AI 工具的面板
  - 工具列表根据玩家身份动态过滤：国王获得外交工具，封臣只能写信等
  - 可用于批量写信，但只能写给聊过天的已知 NPC
- 书信模式下金币转移类工具禁用（`give_gold`、`request_gold`），行军/军事和关系类工具正常
- Agent 可调用 `send_letter` 给任意人物写信（支持中文名或 entity ID）
- 收信端由 `AgentScheduler` 异步激活处理（每帧一个事件，最多 N 层级联）
- 级联深度在 MCM 中可调（默认 5，超出的只存档不处理）
- 所有信件的收发对玩家可见（左下角提示）

### 提示词系统（文件化、可热重载）

所有提示词均为**中文文本文件**，存储在模组目录下，玩家可随时编辑，游戏内实时生效（热重载）。

```
_Module/Prompts/
├── system_prompt.txt            # 默认系统提示词模板（新战役复制为初始值）
├── world_info.txt               # 默认世界背景介绍
├── tools.json                   # 游戏工具定义（热重载）
├── agent_system.txt             # Agent 系统提示词模板
├── agent_tools.json             # Agent 文件工具定义（热重载）
├── tool_call_prompt.txt         # 独立工具调用的代理提示词（热重载）
├── persona_generation.txt       # NPC性格生成提示词（玩家可编辑，热重载）
├── diplomacy_rules.txt          # 外交决策规则（玩家可编辑，热重载）
├── Templates/                   # NPC 目录模板
│   ├── persona.txt
│   ├── context_template.txt
│   ├── knowledge_player.txt
│   ├── goals_current.txt
│   ├── archive.txt
│   └── relationship.txt
└── Campaigns/
    └── {战役名}/                 # 每个存档独立的目录
        ├── system_prompt.txt     # 本战役的系统提示词（可独立编辑，热重载）
        ├── world_info.txt        # 本战役的世界背景（可编辑，热重载）
        ├── agent_system.txt      # 本战役 Agent 提示词（热重载）
        ├── tool_call_prompt.txt  # 本战役工具调用提示词（热重载）
        ├── persona_generation.txt # 本战役性格生成提示词（热重载）
        ├── context_template.txt  # 本战役 Context 模板（热重载）
        ├── diplomacy_rules.txt   # 本战役外交决策规则（热重载）
        └── NPCs/                 # Agent 管理的 NPC 文件系统
            └── {entity_id}/        # 每个 Entity 独立目录
                ├── character.json # 基础 ID 信息（只读，自动生成）
                ├── persona.txt    # 结构化 persona（动机、性格特质、表达风格三段式）
                ├── knowledge/
                ├── chat_logs/
                ├── relationships/
                ├── goals/
                ├── decisions/
                └── mailbox/
                    └── inbox/
```

> **人称约定**：所有提示词文件中只使用「你」指代 Agent 自己、「对方」指代交互对象。
> `query_character` 返回结果以「该人物：」开头作为补充约定。
> 禁止使用「TA」「他/她」「其」等模糊人称。未来添加新提示词文件时必须遵守此约定。

- **Agent 系统**：每个 NPC 有独立文件系统，Agent 通过 `read_file`/`write_file`/`append_file`/`edit_file`/`delete_file`/`list_dir`/`glob`/`grep`/`send_letter` 工具管理记忆
- **信息隔离**：Agent 只能操作自己目录下的文件 + World/ 目录，不能读取其他 NPC 的信息
- **解耦存储**：聊天记录（`chat_logs/`）、对 Entity 认知（`knowledge/`）、NPC 性格（`persona.txt`）全部独立文件，Agent 按需精确读取
- **LLM 生成 persona**：首次对话时自动调用 LLM 为 NPC 生成结构化 persona（玩家角色除外，使用静态占位文本）
- **ContextBuilder**：根据交互双方动态组装系统提示词，通过 `context_template.txt` 模板注入 Entity 的 persona 和能力信息
- **世界信息系统**：卡拉迪亚大陆介绍，每个战役可独立编辑
- **系统提示词**：控制 AI 行为风格的核心提示，每个战役独立
- **工具定义**（`tools.json`）：定义 AI 可调用的游戏函数
- **Agent 工具**（`agent_tools.json`）：定义 Agent 的文件操作工具
- **个人信息系统**：每个 NPC 独立，对目标的了解逐步积累，不会互相覆盖
- NPC 个人信息在**首次对话时自动生成**，之后复用
- AI 有权修改"对目标的了解"字段（通过 function calling 自动触发），但不能修改聊天记录
- **玩家有权修改任何提示词文件**

### 全中文界面

- 模组内所有文本、MCM 设置面板、按钮、弹窗、系统提示词均为中文
- 支持中文输入和中文回复

### MCM 设置面板

在主菜单 **Options → Mod Options → MyFirstMod — AI Chat** 中可配置：

| 设置项 | 说明 | 默认值 |
|--------|------|--------|
| API URL | LLM API 端点 | `https://api.deepseek.com/v1/chat/completions` |
| Model | 模型名称 | `deepseek-chat` |
| API Key | 你的 API 密钥 | 空（需自行填入） |
| 最大 Token 数 | AI 单次回复的 token 上限 | `500` |
| 回复创造性 | Temperature 值，越低越稳定保守 | `0.8` |
| API 超时（秒） | 请求超时时间 | `30` |
| Test Connection | 测试按钮 | 验证连通性和 function calling 支持 |
| 双倍声望 | 战斗中声望翻倍 | 关闭 |
| 独立工具调用 | 仅在模型消极调用工具时开启（增加延迟和 token 消耗） | 关闭 |
| 显示工具调用提示 | 左下角显示 Agent 的文件操作 | 开启 |
| Agent 最大轮次 | 工具调用循环上限 | `5` |
| 不限制 Agent 轮次 | 开启后无轮次上限，直到模型自然停止 | 关闭 |
| 聊天历史上限（条） | 保留最近 N 条消息发给 AI | `20` |
| 注入世界背景 | 是否在提示词中加入卡拉迪亚背景 | 开启 |
| 最大好感变化 | Agent 单次修改好感度的上限 | `5` |
| 信件级联深度上限 | NPC 间连环写信的最大层数 | `5` |
| 环境扫描半径（km） | query_surroundings 扫描半径硬上限 | `20` |
| 禁止原版外交（Agent 主导） | 禁止原版 AI 外交，所有外交由国王 Agent 决策 | 开启 |
| 国王激活间隔（天） | 国王 Agent 定期外交审视的间隔 | `30` |
| 对话字体大小 | 聊天窗口中对话内容的字号 | `24` |
| 角色名字体大小 | 聊天窗口中角色名称的字号 | `22` |
| 时间戳字体大小 | 聊天窗口中时间戳的字号 | `22` |
| 消息间距 | 两条消息之间的垂直间距 | `60` |
| 对话缩进 | 对话内容相对于角色名的左侧缩进 | `15` |
| 角色名上间距 | 角色名与时间戳之间的间距 | `6` |
| 对话上间距 | 对话内容与角色名之间的间距 | `6` |
| 重置聊天界面 | 一键恢复聊天界面所有默认值（按钮） | — |

### 双倍声望（可选）

战斗中获得的声望翻倍（可在 MCM 中开关，默认关闭）。

---

## 支持的后端

默认使用 **DeepSeek**，但你可以换成任何兼容 OpenAI Chat Completions 格式的 API：

| 后端 | URL |
|------|-----|
| DeepSeek | `https://api.deepseek.com/v1/chat/completions` |
| OpenAI | `https://api.openai.com/v1/chat/completions` |
| 本地 Ollama | `http://localhost:11434/v1/chat/completions` |
| 其他兼容接口 | 自定义 |

> 注意：如果使用非 DeepSeek 的后端，请确保 Model 名称与你的 API 提供商匹配（如 `gpt-4o`、`qwen-plus` 等）。
> 
> **重要：** AI 认知更新依赖 **function calling** 机制。请确保你的模型支持 `tools` / `function calling`。点击「测试连接」按钮会自动检测此能力。

---

## 使用方法

1. 启动游戏，在启动器中勾选 **MyFirstMod** 及四个前置模组
2. 进入主菜单后，在 **Mod Options → MyFirstMod — AI Chat** 中填入 API Key
3. 开新档或读档 → 模组自动在 `Prompts/Campaigns/` 下创建本战役的提示词目录
4. （可选）编辑 `system_prompt.txt`、`world_info.txt` 或角色 JSON 文件来定制 AI 行为
5. 与任意领主对话 → 点击 **「【AI 聊天】」**
6. 在聊天窗口中输入消息，按「发送」按钮
7. AI 回复会显示在聊天窗口中，支持多轮对话
8. 点击聊天窗口右上角的 X 关闭，回到对话界面

---

## 文件结构

```
MyFirstMod/
├── SubModule.cs          # 模组入口，Harmony 激活，初始化 PromptManager
├── Settings.cs           # MCM 设置类（URL、APIKey、测试按钮、双倍声望开关）
├── AIChatClient.cs       # HTTP 客户端，调用 LLM API（使用 PromptManager 构建提示词）
├── AIChatScreen.cs       # 聊天屏幕管理器（静态类，GauntletLayer 挂载）
├── AIChatScreenVM.cs     # 聊天 ViewModel（消息列表、输入绑定、function calling 处理）
├── LordChatBehavior.cs   # CampaignBehavior：对话中插入聊天选项，管理战役 ID
├── LetterListScreen.cs   # 书信系统屏幕管理器（战役地图 O 键入口）
├── PromptManager.cs      # 提示词管理器（文件热重载、战役目录、角色 JSON 读写）
├── AgentManager.cs       # Agent 管理器（NPC 文件系统、路径权限、工具执行）
├── AgentScheduler.cs     # 信件异步事件驱动调度器
├── DiplomacyBanPatch.cs  # Harmony 补丁，禁止原版 AI 外交（MCM 可开关）
├── Entity.cs             # Entity 数据模型（统一玩家/NPC，附能力标签）
├── EntityManager.cs      # Entity 生命周期管理、查找与缓存
├── ContextBuilder.cs     # 动态上下文组装（persona + 能力 + 模板）
├── AGENTS.md             # AI 开发工作流文档
├── README_MOD.md         # 本文件（功能说明）
├── _Module/
│   ├── SubModule.xml     # 模组元数据
│   ├── GUI/Prefabs/
│   │   ├── AIChatScreen.xml      # 聊天窗口 GauntletUI 布局
│   │   └── LetterListScreen.xml  # 书信系统界面布局
│   └── Prompts/
│       ├── system_prompt.txt      # 系统提示词模板（玩家可编辑，热重载）
│       ├── world_info.txt         # 默认世界背景
│       ├── tools.json             # 游戏工具定义（热重载）
│       ├── agent_system.txt       # Agent 系统提示词模板
│       ├── agent_tools.json       # Agent 文件工具（热重载）
│       ├── tool_call_prompt.txt   # 工具调用代理提示词（热重载）
│       ├── persona_generation.txt # NPC性格生成提示词（热重载）
│       ├── Templates/             # NPC 目录模板（含 context_template.txt）
│       └── Campaigns/             # 各战役独立目录（运行时自动创建）
└── BLSource/             # 反编译的游戏源码（5332 个文件，只读）
```

---

## 版本

- 游戏版本：Bannerlord v1.4.7
- 模组版本：v0.0.1
