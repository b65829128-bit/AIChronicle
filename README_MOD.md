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
- 首次对话时自动用 LLM 为 NPC 生成性格描述（基于游戏内人物数据）
- 认知更新机制使用 OpenAI function calling 协议
  - Agent 可以调用 `move_to_settlement` 工具，让 NPC 部队实际行军移动到地图上的城镇/城堡（非瞬移）
  - Agent 可以调用 `wait_at_settlement` 工具，让 NPC 在到达城镇后停留指定时长（游戏内小时）
  - NPC 移动期间保留逃离逻辑：被强敌追赶时会逃跑，逃离结束后自动恢复原目的地
  - Agent 可以调用 `change_relation` 修改对玩家的好感度（单次上限在 MCM 中设置，默认 +-5）
  - Agent 可以调用 `give_gold_to_player` 赠送玩家金币（直接转账）
  - Agent 可以调用 `request_gold_from_player` 向玩家索要金币（弹出确认对话框，玩家无法口头答应但不给钱）

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
├── Templates/                   # NPC 目录模板
│   ├── persona.txt
│   ├── knowledge_player.txt
│   ├── goals_current.txt
│   ├── archive.txt
│   └── relationship.txt
└── Campaigns/
    └── {战役名}/                 # 每个存档独立的目录
        ├── system_prompt.txt     # 本战役的系统提示词（可独立编辑，热重载）
        ├── world_info.txt        # 本战役的世界背景（可编辑，热重载）
        └── NPCs/                 # Agent 管理的 NPC 文件系统
            └── {领主名}/          # 每个 NPC 独立目录
                ├── character.json # 基础 ID 信息（只读，自动生成）
                ├── persona.txt    # 性格描述（LLM 首次对话生成）
                ├── knowledge/
                ├── chat_logs/
                ├── relationships/
                ├── goals/
                └── decisions/
```

- **Agent 系统**：每个 NPC 有独立文件系统，Agent 通过 `read_file`/`append_file`/`list_dir` 工具管理记忆
- **信息隔离**：Agent 只能操作自己目录下的文件 + World/ 目录，不能读取其他 NPC 的信息
- **解耦存储**：聊天记录（`chat_logs/`）、对玩家认知（`knowledge/`）、NPC 性格（`persona.txt`）全部独立文件，Agent 按需精确读取
- **LLM 生成性格**：首次对话时自动调用 LLM 根据游戏内人物数据生成 NPC 性格描述
- **世界信息系统**：卡拉迪亚大陆介绍，每个战役可独立编辑
- **系统提示词**：控制 AI 行为风格的核心提示，每个战役独立
- **工具定义**（`tools.json`）：定义 AI 可调用的游戏函数
- **Agent 工具**（`agent_tools.json`）：定义 Agent 的文件操作工具
- **个人信息系统**：每个 NPC 独立，对玩家的了解逐步积累，不会互相覆盖
- NPC 个人信息在**首次对话时自动生成**，之后复用
- AI 有权修改"对玩家的了解"字段（通过 function calling 自动触发），但不能修改聊天记录
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
├── PromptManager.cs      # 提示词管理器（文件热重载、战役目录、角色 JSON 读写）
├── AgentManager.cs        # Agent 管理器（NPC 文件系统、路径权限、工具执行）
├── AGENTS.md             # AI 开发工作流文档
├── README_MOD.md         # 本文件（功能说明）
├── _Module/
│   ├── SubModule.xml     # 模组元数据
│   ├── GUI/Prefabs/
│   │   └── AIChatScreen.xml  # 聊天窗口 GauntletUI 布局
│   └── Prompts/
│       ├── system_prompt.txt      # 系统提示词模板（玩家可编辑，热重载）
│       ├── world_info.txt         # 默认世界背景
│       ├── tools.json             # 游戏工具定义（热重载）
│       ├── agent_system.txt       # Agent 系统提示词模板
│       ├── agent_tools.json       # Agent 文件工具（热重载）
│       ├── tool_call_prompt.txt   # 工具调用代理提示词（热重载）
│       ├── Templates/             # NPC 目录模板
│       └── Campaigns/             # 各战役独立目录（运行时自动创建）
└── BLSource/             # 反编译的游戏源码（5332 个文件，只读）
```

---

## 版本

- 游戏版本：Bannerlord v1.4.7
- 模组版本：v0.0.1
