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
  - Agent 可以调用 `query_settlement_geography` 查询任意城镇/城堡的地理情报（大陆方位、周边定居点及阵营关系、边境/腹地标签，距离精确到km，全部动态计算实时地图数据）
  - Agent 可以调用 `query_world_state` 获取当前世界局势（各王国兵力、交战状态，含近期到期的盟约/贸易协定）
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
  - Agent 可以调用 `request_gold` 向任意人物索要金币（向玩家索要时弹出确认框）
- Agent 可以调用 `give_item` 将自己物品/装备交给任意人物（直接转账）
- Agent 可以调用 `request_items` 向任意人物索要物品（向玩家索要时弹出确认框）
- Agent 可以调用 `let_go` 在遭遇战中放玩家一马（仅当 NPC 兵力占优时可用，设置冷却期避免立即追击）
- **已知限制：** `request_gold` 和 `request_items` 向 NPC 索要时直接划转，NPC 不会经过 LLM 决策——未来应改为异步事件，让 NPC Agent 自行判断是否给
  - Agent 可以调用 `query_character` 查询任意人物的公开信息
  - Agent 可以调用 `query_clan_fiefs` 查询任意家族的封地情况（城镇/城堡列表、族长、所属王国）
  - Agent 可以调用 `query_recent_events` 查询任意人物的近期事件（比武夺冠、被俘、释放、婚嫁、阵亡等百科记录）
  - Agent 可以调用 `query_surroundings` 扫描周围环境：当前位置、附近城镇/城堡、附近部队及其阵营关系和距离
  - Agent 可以调用 `query_war_status` 查询王国战争状态：双方阵亡数、攻下的城镇/城堡、劫掠村庄数
  - Agent 国王可以调用 `query_pending_proposals` 列出当前待处理的外交提案（无需参数，自动过滤本国相关提案）
  - Agent 国王可以调用 `declare_war` 宣战（单向立即生效）
  - Agent 国王可以调用 `propose_peace` / `propose_alliance` / `propose_trade` 提出外交提案（双向，需对方国王同意）
- Agent 国王可以调用 `respond_to_diplomacy_proposal` 接受或拒绝收到的外交提案
- Agent 国王可以调用 `gift_fief` 将王国范围内任意封地直接转让给某位封臣家族领袖
- Agent 氏族领袖可以调用 `change_kingdom` 变换阵营：离国、加入、叛逃、当雇佣兵、禅让王位
  - `abdicate`：国王指定继承人禅让（支持同氏族或同王国其他氏族领袖）
  - `leave_kingdom`：脱离王国（可选叛乱保留封地）
  - `join_kingdom` / `defect_to_kingdom` / `join_as_mercenary`：加入/叛逃/当佣兵
- 外交提案存储在 `World/diplomacy/` 目录，对方国王的定期激活由 `AgentScheduler` 管理（每 15 天一次）
  - 被俘或逃亡的国王统治者仍会被激活，状态提示中会标明"你仍是王国统治者"，确保外交工具可正常使用
  - "禁止原版外交" 开启（默认）时，**玩家自己的王国界面外交按钮也会被禁用**（宣战/议和/结盟/贸易变灰），外交统一走 M 键秘书处执行。
  - Agent 可以调用 `grep` 在个人文件系统中按关键词搜索，定位到具体文件和行号后再用 `read_file` 精读

### 书信系统

- 战役地图上按 **O 键**打开书信面板（收件箱 + 已知领主列表）
- **收件箱**：以 `[来信] 发信人` 格式显示收到的信件，点击可阅读全文并一键回复
- **写信**：选择一位对话过的领主，进入写信界面。**写信 = 发送真实信件**——左下角显示"预计 X 小时后送达"，对方处理后会以信件形式回信（进入你的收件箱），并带上你们此前的聊天记录（对方记得你们认识过）
- 战役地图上按 **M 键**打开秘书处（玩家的个人行政助手）
  - 秘书处是玩家的**个人行政办公室**，不是玩家本人——固定 persona（无条件服从），不会拒绝玩家的命令
  - 无论玩家是国王、封臣还是平民，秘书处都可以使用（只是可用工具随身份变化）
  - 工具列表根据玩家身份动态过滤：国王获得外交工具，封臣只能写信等
  - **玩家可以经秘书处提交公开谏言**（`submit_advisory`）：以玩家名义写入本国谏言记录，可被史官写入编年史——玩家能借此影响历史记载。雇佣兵无权谏言
- Agent 可调用 `send_letter` 给任意人物写信（支持中文名或 entity ID）
- 收信端由 `AgentScheduler` 异步激活处理（每帧一个事件，最多 N 层级联）
- 级联深度在 MCM 中可调（默认 5，超出的只存档不处理）
- 所有信件的收发对玩家可见（左下角提示）
- 当书信双方之间存在待处理的外交提案时，收信 Agent 的上下文会自动注入提案摘要（提示 Agent 此信可能是对方对提案的回复）
- 书信有**距离延时**：距离越远到达越慢（最低 3 小时，跨图约半天），发信时左下角显示预计送达时间
- 收信人回信同样计算延时，形成自然的往返时间差
- 信件处理会带上双方**此前的聊天记录**——对方记得你们以前见过面/聊过什么（跨信记忆连续，不会"不认得你"）
- **信件 = 线程**：你与某人的所有交流（面对面 + 信件）在**同一个聊天窗口**里连贯展示，信件消息用 📜 标记（古铜色）与当面说话区分；回信只进该线程，不再投递到独立的信箱收件箱。信箱（O 键）是打开线程的入口，收到回信时会弹提示
- 被俘虏、逃亡、死亡的 NPC 无法收发信
- 无氏族的路人 NPC 不能写信、不能索要金币（保留聊天和好感修改）

### AI 外交系统

- AI 国王可以向他国发起外交提案（议和/结盟/贸易协定），对方国王的 Agent 会定期审视并处理
- AI 国王也可以直接宣战（单向立即生效）
- 当 AI 国王向**玩家**发起外交提案时，玩家会**弹出按钮对话框**（接受/拒绝），不会由 AI 自动处理
- **玩家**的外交主动行为应在秘书处（M 键）执行

### 盟约/贸易协定到期记录

- 盟约（84 天）与贸易协定（1 年）到期后，系统自动把「哪一天、和谁的到期了」记入 `World/diplomacy/expiry_log.txt`
- 到期前不记录、不提示；**不主动激活 Agent、不注入提示、不给续约方法、不显示剩余天数**——国王到期前什么都不知道
- 国王下次调用 `query_world_state` 时，自己王国名下会看到 `📜 盟约 X与Y 于第1089年夏第12日到期`；不查就不知道
- 国王重新结盟/重签（对方接受提案生效）或主动结束协约的**那一刻**，对应到期记录立即清除——国王再次激活时不会看到已失效的「到期」信息，避免反复查询求证
- 每条记录按「王国对+类型」最多保留一条，超过 90 游戏天自动清除，防止无限堆积

### 历史系统

- 游戏中的重大事件（宣战/议和/城镇易主/灭国/建国/贵族阵亡/氏族叛变/婚嫁/氏族领袖更替）被自动记录为**原始史料**
- 原始史料以 JSONL 格式存储在 `NPCs/World/history/events_{年份}.txt`，永久保存
- 每当年份推进时，**史官 Agent** 自动激活，读取原始史料并编纂**年度编年史**（间隔可在 MCM 中调整）
- 氏族领袖、国王、玩家死亡时，史官自动编纂**列传**（人物传记）
- 灭国、建国等重大事件触发**专题史**
- 编年史存储于 `NPCs/World/history/chronicles/`，以《资治通鉴》白话风格书写，年末附「史官曰」评论
- 战役地图上按 **H 键**打开史书 UI（1100×700 大屏，左侧目录右侧正文），字体大小可调
- NPC Agent 可通过 `read_file` 阅读编年史——历史成为 NPC 的共同知识
- 史官提示词（`historian_rules.txt`、`yearly_chronicle_prompt.txt`、`biography_prompt.txt`）全部热重载
- 史料记录类型：war_declared（含宣战宣言）/ peace_made / siege_started / siege_failed / siege_abandoned / settlement_captured / fief_granted（国王册封，含册封宣言）/ kingdom_destroyed / kingdom_created / hero_killed / clan_changed_kingdom / clan_leader_changed / marriage

### 天命意识形态

- 世界共享「天命/大一统」意识形态（`world_info.txt`）：天无二日、天下终当归于一统，分裂被视为乱世而非长久之态
- 重大外交行动（宣战/结盟/议和/贸易）若国王重视名分，应师出有名——无名之师、与僭越者结盟、求和于不义之邦会损害威信；是否看重名分由国王人格决定（`diplomacy_rules.txt`）
- 封臣可在谏言中援引天命批判国王失德、兴无名之师、与僭越者结盟；直不直谏、出于真心还是个人谋划，由封臣自决（`advisory_rules.txt`）
- NPC 性格新增「天命信仰」维度（`persona_meta.json`）：笃信 / 敬重 / 平常 / 假托 / 不信，随机分布（10/26/38/20/6），保持立场多元
- 史官以「天命视角」编纂编年史（`historian_rules.txt`）：理解时人以天命评说兴衰，但保持中立，可在「史官曰」中借天命评王朝兴衰

### 内政审视与封地政治

- 国王外交审视升级为**内外政务**（`diplomacy_rules.txt`）：先审视内政（封地分配/治理/战功），再处理外交——内政缺地会自然推动国王开战或暂不议和
- 国王审视时自动注入**内政审视报告**：封地账本（谁有地谁无地）、各城治理（繁荣度/忠诚度）、近期战功（`World/court/{王国}_merit.txt`，围城/攻克/失利真实记录）
- 国王拥有赐地与夺封之权（`gift_fief` 可附 `reason` 名分参数）：赐地是恩赏，夺封须**师出有名**
- **被夺方激活**：国王夺封（原主非国王本人）时，被夺家族被激活审视处境（FiefReview 事件，左下角提示「xxx 发现自己被夺封了」）——可忍气吞声 / 上表抗议 / 写信交涉 / 联络他国 / 转投他国（`change_kingdom`），触发内政矛盾，史官记入编年史
- 封地审视规则：`fief_review_rules.txt`（热重载）；审视上下文含外交分类工具，封臣**知道**自己能转投
- **攻城后定归属**：城镇/城堡被攻下（无论玩家或 AI）不再触发原版影响力投票，改由国王 Agent 决定（P1 级激活，与外交提案同级），用 `gift_fief` 赐予合适家族；攻城后默认归国王氏族。国王是玩家时由玩家经秘书处处理。开关：MCM「册封由 Agent 主导」

### 封臣谏言

- 每个王国每天有概率（MCM 可调，默认 10%）触发一位氏族领袖向国王进谏
- 按权重随机选择谏言者（权重 = 氏族等级×3 + 影响力/50 + 封地数），排除雇佣兵、俘虏、逃亡者、玩家和国王本人；同一封臣不会连续进谏
- 封臣激活后阅读自己的私人笔记（`decisions/personal_notes.txt`）、查询世界局势，然后调用 **`submit_advisory` 工具**提交公开谏言
- 公开谏言由系统自动归档到 `World/advisory/{王国}_{年份}.txt`（含时间戳、姓名、头衔，按年分文件封存）
- 私人笔记 `decisions/personal_notes.txt` 非强制，封臣可自行决定是否记录
- 国王的外交提示词中自动注入"先阅读封臣谏言"的指引，但国王保留绝对决策权
- 战役地图上按 **H 键**可查阅本国封臣的公开谏言
- 史官可读取所有王国的公开谏言写入编年史
- **秘密谏言**：封臣可用 `submit_secret_advisory` 密陈给国王（写 `World/secret_advisory/`，**仅本国王可读、不入史册**）——公开谏言表达立场进史，秘密谏言说那些不适合被历史记录或旁人知晓的事，封臣可公开一套、私下另一套
- 提示词：`advisory_rules.txt` 支持热重载；`tools.json`/`agent_tools.json` 删除时自动回退内嵌最小工具集

### 工具分类系统

所有工具按 8 个分类组织，Agent 按场景默认激活相关分类，需要其他分类时调用 `browse_tools` 元工具按需解锁：

| 分类 | 包含工具 | 默认激活场景 |
|------|---------|:--|
| universal | update_knowledge, cancel_action | 全部 |
| query | query_character, query_settlement, query_settlement_geography, query_world_state, query_recent_events, query_surroundings, query_party_troops, query_available_troops, query_settlement_villages, query_kingdom_settlements, query_clan_members, query_clan_fiefs, query_kingdom_clans, query_war_status, query_pending_proposals, query_hero_skills | 全部 |
| social | change_relation, give_gold, request_gold, give_item, request_items, let_go | conversation |
| movement | move_to_settlement, wait_at_settlement, go_around_party | autonomous |
| military | raid_settlement, besiege_settlement, engage_party, defend_settlement, patrol_settlement, escort_party, recruit_troops, upgrade_troops | autonomous |
| diplomacy | declare_war, propose_peace, propose_alliance, propose_trade, respond_to_diplomacy_proposal, gift_fief, change_kingdom | diplomacy |
| file | read_file, write_file, append_file, edit_file, delete_file, move_file, list_dir, glob, grep | letter, autonomous, conversation |
| communication | send_letter | letter |

Agent 任何时候都可以调 `browse_tools("military")` 解锁某类工具，下一轮即可使用。

### 征兵与部队管理

- Agent 可以调用 `query_party_troops` 查看部队详情。**军情迷雾**：自己与同阵营部队全量（金币、日薪、兵力/伤兵/上限、各兵种数量经验升级路径、俘虏可招募性、物品栏、装备栏）；异国部队仅侦察估计——近距给兵力带与兵种构成（约 ±20%），远距给宽泛区间与定性描述，跨海/远处只有传闻，不泄露军饷、经验、装备等机密
- Agent 可以调用 `query_available_troops` 查看当前定居点可招募兵种（需在定居点内，被劫掠/敌对村庄无法招兵）
- Agent 可以调用 `query_settlement_villages` 查看城镇/城堡下属村庄——可用作征兵路线规划
- Agent 可以调用 `recruit_troops(兵种名, 数量)` 招募士兵（需在该定居点，自动扣金币）
- Agent 可以调用 `upgrade_troops(原兵种, 目标, 数量)` 升级兵种（自动检查经验、金币、所需装备和特长）
- Agent 可以调用 `buy_food(天数)` 在定居点自动采购最便宜的粮到够吃 N 天
- Agent 可以调用 `query_hero_skills` 查看任意人物的 18 个技能等级和 6 个属性值
- `move_to_settlement` 现在可以移动到村庄（之前只能到城镇/城堡）

### 计划系统

- Agent 面对复杂任务时可制定多步骤计划（存储为 `goals/plan_*.txt`），每步精确到 function 调用
- `move_to_settlement` 和 `wait_at_settlement` 支持 `activate: true` 参数——到达/到期后自动唤醒 Agent，继续执行计划
- 唤醒后自动收到指令：读计划文件 → 确认进度 → 执行下一步
- 用 `move_file` 将完成的计划移到 `goals/done_` 标记完成
- `conversation` 意图默认包含 `file` 分类，确保 Agent 在对话中就能写计划

### 物品交易

- Agent 可以调用 `give_item(目标, 物品名, 数量)` 将自己物品栏或装备栏中的东西给任意人物
- Agent 可以调用 `request_items(目标, 物品名, 数量)` 向任意人物索要物品（NPC 直接划转，玩家弹确认框）
- 已知限制：`request_gold` 和 `request_items` 向 NPC 索要时直接划转，NPC 不经过 LLM 决策

### 提示词系统（文件化、可热重载）

所有提示词均为**中文文本文件**，存储在模组目录下，玩家可随时编辑，游戏内实时生效（热重载）。

```
_Module/Prompts/
├── system_prompt.txt            # 默认系统提示词模板（新战役复制为初始值）
├── world_info.txt               # 默认世界背景介绍
├── tools.json                   # 游戏工具定义（热重载）
├── agent_system.txt             # Agent 系统提示词模板
├── agent_tools.json             # Agent 文件工具定义（热重载）
├── persona_generation.txt       # NPC性格生成提示词（玩家可编辑，热重载）
├── chancery_rules.txt           # 秘书处行为规则（热重载）
├── conversation_rules.txt       # 对话规则
├── letter_rules.txt             # 书信规则
├── diplomacy_rules.txt          # 外交决策规则（玩家可编辑，热重载）
├── historian_rules.txt          # 史官编年史规则（玩家可编辑，热重载）
├── advisory_rules.txt            # 封臣谏言规则（热重载）
├── fief_review_rules.txt         # 封地审视规则（被夺方激活，热重载）
├── yearly_chronicle_prompt.txt  # 年度编年史激活提示词（热重载）
├── biography_prompt.txt         # 人物列传激活提示词（热重载）
├── special_chronicle_prompt.txt # 专题史激活提示词（热重载）
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
        ├── persona_generation.txt # 本战役性格生成提示词（热重载）
        ├── context_template.txt  # 本战役 Context 模板（热重载）
        ├── diplomacy_rules.txt   # 本战役外交决策规则（热重载）
        ├── historian_rules.txt  # 本战役史官编年史规则（热重载）
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
| 最大 Token 数 | AI 单次回复的 token 上限（DeepSeek V4 最高 384K 输出；默认 32768 足够长编年史/长思考，特殊场景可上调至 65536） | `32768` |
| 回复创造性 | Temperature 值，越低越稳定保守 | `0.8` |
| API 超时（秒） | 请求超时时间 | `30` |
| Test Connection | 测试按钮 | 验证连通性和 function calling 支持 |
| 双倍声望 | 战斗中声望翻倍 | 关闭 |
| 显示工具调用提示 | 左下角显示 Agent 的文件操作 | 开启 |
| 调试日志 | 将 LLM 调用摘要、思维链摘录写入战役目录 `debug_logs/`，便于排查 agent 行为 | 开启 |
| Agent 并发数 | 同时运行的 Agent 任务数上限。越大吞吐越高，但工具在主线程串行执行，过大会帧卡顿 | `5` |
| 聊天历史上限（条） | 保留最近 N 条消息发给 AI | `20` |
| 注入世界背景 | 是否在提示词中加入卡拉迪亚背景 | 开启 |
| 最大好感变化 | Agent 单次修改好感度的上限 | `5` |
| 信件级联深度上限 | NPC 间连环写信的最大层数 | `5` |
| 环境扫描半径（km） | query_surroundings 扫描半径硬上限 | `20` |
| 情报侦察半径（占地图比例） | query_party_troops 查看异国部队的近距侦察半径（地图尺度比例，0.2 ≈ 1-2 座城池间距），之外的情报降为模糊/传闻 | `0.2` |
| 禁止原版外交（Agent 主导） | 禁止原版 AI 外交，所有外交由国王 Agent 决策 | 开启 |
| 册封由 Agent 主导 | 攻下的城（无论玩家或 AI）不再触发原版影响力投票，改由国王 Agent 决定归属；攻城后默认归国王氏族 | 开启 |
| 国王激活间隔（天） | 国王 Agent 定期外交审视的间隔 | `30` |
| 编年史间隔（年） | 史官编纂编年史的间隔（1=每年，3=每三年） | `1` |
| 启用封臣谏言 | 开启后封臣按概率陆续进谏 | 开启 |
| 封臣谏言概率/天 | 每个王国每天触发封臣进谏的概率 | `0.1` |
| 所有贵族立传 | 死后立传范围：所有氏族贵族 或 仅氏族领袖和国王 | 开启 |
| 史书字体大小 | 史书 UI 中编年史正文的字体大小 | `28` |
| 强制开始外交 | 立即激活所有国王 Agent 进行外交审视，重置计时器（按钮） | — |
| 强制封臣进谏 | 立即重置所有王国的封臣谏言计时器（按钮） | — |
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
├── AIChatClient.cs       # HTTP 客户端，SSE 流式请求，多轮工具调度
├── ToolExecutor.cs       # 工具执行器，所有游戏工具的 switch 分发
├── DiplomacyService.cs   # 外交服务（宣战/议和/结盟/贸易/回复提案）
├── PartyBehaviorManager.cs # 部队行为状态机（PendingAction + Tick）
├── AIChatScreen.cs       # 聊天屏幕管理器（GauntletLayer 挂载）
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
├── HistoryRecorder.cs    # 历史记录器（监听游戏事件自动写入原始史料）
├── HistoryScreenVM.cs    # 史书 UI ViewModel（编年史列表、内容加载）
├── MainThreadExecutor.cs # 主线程分发器（后台线程工具执行回主线程，防跨线程崩溃）
├── DebugLogger.cs        # 调试日志（LLM 调用摘要/思维链摘录 → 战役 debug_logs/）
├── SafeFileIO.cs         # 带重试的文件 IO（并发读写同一文件时避免"文件正被使用"异常）
├── AGENTS.md             # AI 开发工作流文档
├── README_MOD.md         # 本文件（功能说明）
├── CLAUDE.md             # Claude Code 入口文档（指向 AGENTS.md / README_MOD.md）
├── _Module/
│   ├── SubModule.xml     # 模组元数据
│   ├── GUI/Prefabs/
│   │   ├── AIChatScreen.xml      # 聊天窗口 GauntletUI 布局
│   │   ├── LetterListScreen.xml  # 书信系统界面布局
│   │   └── HistoryScreen.xml     # 史书 UI 布局（1100×700 双栏）
│   └── Prompts/
│       ├── system_prompt.txt      # 系统提示词模板（玩家可编辑，热重载）
│       ├── world_info.txt         # 默认世界背景
│       ├── tools.json             # 游戏工具定义（热重载）
│       ├── agent_system.txt       # Agent 系统提示词模板
│       ├── agent_tools.json       # Agent 文件工具（热重载）
│       ├── persona_generation.txt # NPC性格生成提示词（热重载）
│       ├── Templates/             # NPC 目录模板（含 context_template.txt）
│       └── Campaigns/             # 各战役独立目录（运行时自动创建）
└── BLSource/             # 反编译的游戏源码（5332 个文件，只读）
```

---

## 版本

- 游戏版本：Bannerlord v1.4.7
- 模组版本：v1.3.0
