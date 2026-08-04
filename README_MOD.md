# AI编年史·言出法随 — 说做合一的 AI 活世模组

> **交叉参考：** 实现功能前请同时阅读 **AGENTS.md**，其中包含开发环境、编译命令、Harmony 模式、BLSource 使用方法等技术细节。两份文档互为补充——README_MOD.md 告诉你模组"能做什么"，AGENTS.md 告诉你"怎么做"。

在《骑马与砍杀2：霸主》中，与 AI 领主进行基于 LLM 的自然语言对话。

**言出法随——AI 的话语就是世界的现实**：AI 领主不仅"说"，还能"做"。他们在对话里说出的承诺（议和、出兵、放人、换国）都会真实兑现到游戏世界——军队真的会开拔、外交真的会生效、说的每一句话都被史官写进编年史，成为卡拉迪亚的共同记忆。这是骑砍2 里**唯一把 LLM 的「说」与「做」统一**的模组：因为言行合一，所以言行皆可入史。

---

## 当前功能

### AI 领主聊天

- 与任意领主对话时，对话选项中均出现 **「【AI 聊天】」** 选项
- 点击后打开 **专用聊天窗口**（模态屏幕），窗口中显示完整的对话历史
- 输入任意消息发给 LLM，AI 会获取**完整对话上下文**（之前的聊天记录都会传给 AI）
- LLM 会以领主的身份角色扮演回复（中文）。**对话风格为自然口语、有来有回**（中世纪背景，措辞不过分现代）：短句、顺着对方的话接、对方问什么答什么；性格体现在语气态度上，而非堆砌文言修辞
- 关掉聊天窗口后**回到对话界面**，可以继续正常交谈
- AI 可以在对话中了解玩家，通过 **function calling** 机制自动更新对玩家的认知
- 首次对话时自动用 LLM 为 NPC 生成**结构化 persona**（动机、性格特质、表达风格三段式）
- **Entity 系统**：玩家和所有 NPC 统一为 Entity，Agent 不区分"玩家"和"其他 NPC"
- **动态上下文组装**：ContextBuilder 根据交互双方动态构建系统提示词
- **工具能力过滤**：每个 Entity 有 EntityCapability 集合，无部队的 NPC 不拿到行军工具
- 认知更新机制使用 OpenAI function calling 协议
  - Agent 可以调用 `query_settlement` 查询任意定居点实时信息（所有者、繁荣度、**守军兵力**——驻军+民兵+驻城贵族部队的准确数、**围攻状态**）
  - Agent 可以调用 `query_settlement_geography` 查询任意城镇/城堡的地理情报（大陆方位、周边定居点及阵营关系、边境/腹地标签，距离精确到km，全部动态计算实时地图数据）
  - Agent 可以调用 `query_world_state` 获取当前世界局势（各王国兵力、交战状态，含近期到期的盟约/贸易协定）
  - Agent 可以调用 `move_to_settlement` 工具，让 NPC 部队实际行军移动到地图上的城镇/城堡（非瞬移）
  - Agent 可以调用 `wait_at_settlement` 工具，让 NPC 在到达城镇后停留指定时长（游戏内小时）
  - Agent 可以调用 `raid_settlement` 劫掠村庄（强征物资 / 强拉壮丁 / 洗劫）
  - Agent 可以调用 `besiege_settlement` 围攻城镇或城堡（返回时附带**守军评估**：守军总数与己方兵力对比，明显不足时提醒可拉军团或另择弱城——守军数不设迷雾，保证 agent 决策可信）
  - Agent 可以调用 `engage_party` 追击并攻击另一支部队
  - Agent 可以调用 `defend_settlement` 驻防守卫某个定居点
  - Agent 可以调用 `form_army` 召集军团：以军事目标（攻城/劫掠/**解围**——防御军团只用于某城正被围攻时集结兵力打破围城，非驻守）为指向，召集本国领主组成军团，**交还原版 AI 指挥**（集结→扑目标→作战→解散），agent 不再逐帧发令。需影响力>100、王国交战、部曲充足、氏族领袖
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
- Agent 可以调用 `let_go` 在遭遇战中放玩家一马（仅当 NPC 兵力占优时可用，设置冷却期避免立即追击）——玩家被强敌擒获时，投降/应战对话中会出现【AI 聊天】选项，可谈判让 agent 放行；放行后对话自动结束、玩家获释
- **对话即全功能通道**：AI 聊天时其可用工具由身份与能力决定——国王可当场宣战/议和/结盟/颁诏，氏族领袖可当场换国/出兵/驻防/招募，被擒的敌方家族领袖也可在谈判中履行"加入某国"的承诺（先 `change_kingdom` 离旧国、再 `join_kingdom` 投效）。对话中谈成的协议都能立刻兑现，不会"口头答应却做不到"
- **已知限制：** `request_gold` 和 `request_items` 向 NPC 索要时直接划转，NPC 不会经过 LLM 决策——未来应改为异步事件，让 NPC Agent 自行判断是否给
  - Agent 可以调用 `query_character` 查询任意人物的公开信息
  - Agent 可以调用 `query_clan_fiefs` 查询任意家族的封地情况（城镇/城堡列表、族长、所属王国）
  - Agent 可以调用 `query_recent_events` 查询任意人物的近期事件（比武夺冠、被俘、释放、婚嫁、阵亡等百科记录）
  - Agent 可以调用 `query_surroundings` 扫描周围环境：当前位置、附近城镇/城堡、附近部队及其阵营关系和距离（扫描半径按**地图比例**而非绝对 km，0.2 ≈ 1-2 座城池间距）
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
  - **家族等级限制（对齐原版 ClanTierModel，对所有 Entity 生效）**：成为封臣（`join_kingdom`/`defect_to_kingdom`）需家族等级 ≥ 2；等级 1 家族只能当雇佣兵（`join_as_mercenary`）——等级不足时工具返回明确错误提示
- 外交提案存储在 `World/diplomacy/` 目录，对方国王的定期激活由 `AgentScheduler` 管理（每天按 MCM「外交触发几率/天」概率触发，激活后进入 MCM「国王冷静期（天）」冷却；冷却期内不被定时激活，但收到的外交提案仍会正常触发）
  - 被俘或逃亡的国王统治者仍会被激活，状态提示中会标明"你仍是王国统治者"，确保外交工具可正常使用
  - "禁止原版外交" 开启（默认）时，**玩家自己的王国界面外交按钮也会被禁用**（宣战/议和/结盟/贸易变灰），外交统一走 M 键秘书处执行。
  - **盟约"号召盟友宣战"的原版国内投票同样被拦截**，玩家对话里的原版"号召盟友"选项也被隐藏——军事同盟只保留名义作用，是否号召盟友/宣战由国王 Agent 激活时自行决定（或玩家经秘书处）
  - Agent 可以调用 `grep` 在个人文件系统中按关键词搜索，定位到具体文件和行号后再用 `read_file` 精读

### AI 语音朗读（TTS）

- **免费 Edge TTS**：AI 聊天（【AI 聊天】入口）收到回复时，用微软免费神经语音朗读（无需 API Key、无额度限制，需联网）。女角色用女声（晓晓）、男角色用男声（云希），性别音色映射集中一处便于日后扩展
- **仅聊天窗口**：现场对话朗读；**书信与秘书处不朗读**（书信与现场对话同源存储于 chat_logs 线程，按 intent 区分，避免书信内容被误读）
- **打断与停止**：玩家发消息自动打断旧语音；关闭聊天窗口、切档/退出游戏停止播放
- **失败静默降级**：合成/播放失败不影响其他功能，失败原因写入调试日志
- **缓存**：合成音频缓存在战役目录 `tts_cache/`，重复文本不重复合成
- **主菜单可测试**：MCM「测试语音」按钮在主菜单也能试听（用默认男声验证网络/设备），进入战役后按主角性别
- **架构可拓展**：`ITtsProvider` 接口 + `TtsService` 门面（合成→缓存→播放），未来接 Azure/OpenAI 等付费引擎只需新增 Provider 类
- 技术说明：Edge 服务器校验浏览器 UA，而 .NET Framework 的 `ClientWebSocket` 禁止设置该 header——模组内置手写 WebSocket 客户端（`EdgeWsClient`）解决，同时规避 Sec-MS-GEC token 的科学计数法坑；若微软调整协议导致失效，自动静默降级

### 书信系统

- 战役地图上按 **O 键**打开**书信往来**面板——统一的联系人列表，展示你往来过的所有领主（面对面聊过 + 书信往来过）
- 每个联系人显示姓名头衔，**有未读消息时附角标**（`· N 条未读`）；未读优先置顶，其余按最近活跃排序
- 点击联系人 → 关闭面板、打开与该人的**往来线程窗口**：面对面说话与信件在同一个窗口连贯展示，信件用 📜 标记（古铜色）区分；打开线程即标记已读，角标清零
- **写信 = 发送真实信件**：在线程窗口输入内容发送，左下角显示"预计 X 小时后送达"，对方收到后以信件形式回信（进入同一线程，并带上你们此前的聊天记录——对方记得你们认识过）
- 战役地图上按 **M 键**打开秘书处（玩家的个人行政助手）
  - 秘书处是玩家的**个人行政办公室**，不是玩家本人——固定 persona（无条件服从），不会拒绝玩家的命令
  - 无论玩家是国王、封臣还是平民，秘书处都可以使用（只是可用工具随身份变化）
  - **职责范围受严格限制**：只提供 查询 / 谏言（`submit_advisory`、`submit_secret_advisory`）/ 国王诏令与问询回复（`submit_edict`、`reply_consult`）/ 快速加入或脱离王国（`change_kingdom`，受家族等级限制）。**部队行动、军事行动、金钱物品往来、修改好感、写信一律不可用**——这些有替代路径（部队命令、游戏 UI、O 键书信），不走秘书处
  - 工具列表根据玩家身份动态过滤：国王获得外交工具，封臣只能谏言/换国等
  - **玩家可以经秘书处提交公开谏言**（`submit_advisory`）：以玩家名义写入本国谏言记录，可被史官写入编年史——玩家能借此影响历史记载。雇佣兵无权谏言
  - **玩家国王可以经秘书处颁布公开诏令**（`submit_edict`）：以玩家名义向全国宣示方针、回应群臣或垂询政务，封臣进谏前会先读它
- Agent 可调用 `send_letter` 给任意人物写信（支持中文名或 entity ID）——信件写入双方的书信往来线程（chat_logs），不再是独立收件箱
- 收信端由 `AgentScheduler` 异步激活处理（每帧一个事件，最多 N 层级联）
- 级联深度在 MCM 中可调（默认 5，超出的只存档不处理）
- 所有信件的收发对玩家可见（左下角提示）；来信/回信时提示"按 O 键打开书信面板查看"
- 当书信双方之间存在待处理的外交提案时，收信 Agent 的上下文会自动注入提案摘要（提示 Agent 此信可能是对方对提案的回复）
- 书信有**距离延时**：距离越远到达越慢（最低 3 小时，跨图约半天），发信时左下角显示预计送达时间
- 收信人回信同样计算延时，形成自然的往返时间差
- 信件处理会带上双方**此前的聊天记录**——对方记得你们以前见过面/聊过什么（跨信记忆连续，不会"不认得你"）
- **信件 = 线程**：你与某人的所有交流（面对面 + 信件）在**同一个聊天窗口**里连贯展示，信件消息用 📜 标记（古铜色）与当面说话区分。信件与当面说话**同源存储**（chat_logs 线程），来信与回信都在线程里，没有独立的信箱收件箱
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
- **初始历史（开局前既成史）**：战役创建时预置《卡拉迪亚上古编年史》与六国《XX源流纪事》（卡拉德帝国/巴旦尼亚/瓦兰迪亚/斯特吉亚/库赛特/阿塞莱），存于 `NPCs/World/history/chronicles/`——开局即有历史可读，玩家 H 键可见、NPC 可 `read_file` 查阅、史官编纂时可对照旧史。体例与命名刻意避开未来灭国时生成的 `{国名}世家`，不会撞名；模板源 `_Module/Prompts/InitialHistory/`，进档只复制缺失文件、不覆盖玩家已有史文
- 氏族领袖、国王、玩家死亡时，史官自动编纂**列传/本纪**（人物传记）。**传记精准定位**：`query_character` 支持**重名消歧**（多个同名者时列出全部候选人：编号/氏族/王国/年龄）与**按编号精确查询**（`CharacterObject_XXXX`）；传记事件携带死者编号，提示词要求史官用编号精查——修复了重名人物导致传记张冠李戴（氏族、生卒年全错）的 bug
- 灭国触发**世家**（该王国前世今生的兴衰史），建国、大战役等重大事件触发**纪事**（专题始末）
- **史书体例规范**：史文统一按五种体例落盘，文件名由系统自动生成——**本纪**（`{国王名}本纪`，一国之君的生平与治国大事）、**世家**（`{国名}世家`，一国或大族兴衰史）、**列传**（`{人名}列传`，单个人物生平）、**编年史**（`{年份}编年史`，年度大事记）、**纪事**（`{事件名}纪事`，重大事件始末）。史官用专用工具 `write_chronicle`（体例/名称/正文）落盘，系统强制规范命名，杜绝"文件名自定"的乱象；体例判定由代码给出建议（统治者→本纪、贵族→列传、灭国→世家），史官可依实调整
- 史文存储于 `NPCs/World/history/chronicles/`，以《资治通鉴》白话风格书写，年末附「史官曰」评论
- 战役地图上按 **H 键**打开史书 UI（1100×700 大屏，左侧目录右侧正文），**目录按体例分组显示**（本纪 → 世家 → 列传 → 编年史 → 纪事 → 其他 → 政务文献，编年史按年份倒序），字体大小可调
- NPC Agent 可通过 `read_file` 阅读编年史——历史成为 NPC 的共同知识
- 史官提示词（`historian_rules.txt`、`yearly_chronicle_prompt.txt`、`biography_prompt.txt`、`special_chronicle_prompt.txt`）全部热重载
- 史料记录类型：war_declared（含宣战宣言）/ peace_made / siege_started / siege_failed / siege_abandoned / settlement_captured / fief_granted（国王册封，含册封宣言）/ kingdom_destroyed / kingdom_created / hero_killed / clan_changed_kingdom / clan_leader_changed / marriage

### 天命意识形态

- 世界共享「天命/大一统」意识形态（`world_info.txt`）：天无二日、天下终当归于一统，分裂被视为乱世而非长久之态
- 重大外交行动（宣战/结盟/议和/贸易）若国王重视名分，应师出有名——无名之师、与僭越者结盟、求和于不义之邦会损害威信；是否看重名分由国王人格决定（`diplomacy_rules.txt`）
- 封臣可在谏言中援引天命批判国王失德、兴无名之师、与僭越者结盟；直不直谏、出于真心还是个人谋划，由封臣自决（`advisory_rules.txt`）
- NPC 性格新增「天命信仰」维度（`persona_meta.json`）：笃信 / 敬重 / 平常 / 假托 / 不信，随机分布（10/26/38/20/6），保持立场多元
- NPC 性格新增「战争倾向」维度（`persona_meta.json`）：-2 极力避战（和平解决）/ -1 非万不得已不战 / 0 看形势利益 / +1 主动求战（能打就打）/ +2 穷兵黩武（不打仗就难受，哪怕劣势）。**分布（v2，有形的大手）：+2 占 50%、+1 占 30%、0 占 10%、-1 占 6%、-2 占 4%——80% 的人好战**，故意大幅拉高以激活 AI 战争循环——好战的国王发动战争不太权衡利弊，厌战的国王极力避战，保持少量立场多元；封臣谏言时也可借此施加政治压力（好战大臣谏言主战、厌战大臣谏言慎战）。**旧存档迁移**：已有 `persona_meta.json` 的 NPC 会被按新分布重掷战争倾向，并强制重新生成 persona（一次性，重生成后按新好战倾向行事）
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
- **秘密谏言**：封臣可用 `submit_secret_advisory` 密陈给国王（写 `World/secret_advisory/`，**仅本国国王可读、不入史册**——本国封臣与史官亦不可读）——公开谏言表达立场进史，秘密谏言说那些不适合被历史记录或旁人知晓的事，封臣可公开一套、私下另一套。**玩家国王**按 H 键可在史书中查看本国密陈（「国王密陈 · 王国 · 第X年」），玩家封臣/平民看不到
- 提示词：`advisory_rules.txt` 支持热重载；`tools.json`/`agent_tools.json` 删除时自动回退内嵌最小工具集

### 国王诏令（国内政务闭环）

- 国王 Agent 政务审视时，可向国内颁布**公开诏令**（`submit_edict` 工具）：宣示治国方针、回应群臣谏言、或向封臣垂询政务
- 诏令公开归档到 `World/edict/{王国}_{年份}.txt`（带时间戳、姓名、头衔，按年分文件封存），**只有王国统治者可颁布**——非国王调用会被系统拒绝
- **史官可读任何国家的诏令**（作为补充视角，与谏言相互印证）；本国封臣/国王可读本国诏令，**他国人员不可读**；**玩家 H 键可查看本国诏令**（「国王诏令 · 王国 · 第X年」）
- **闭环**：封臣进谏前先读国王的公开诏令，了解王上的旨意与垂询；若国王在诏令中垂询某事，封臣应在谏言中回应——国王诏令与封臣谏言构成国内政务的往来闭环
- **政务激活时国王不再写信**：`send_letter` 回归"仅私人通信"（回信仍可用），外交审视期间禁止主动写信——向国内发声用诏令，外交动作用对应 function

### 国王外交问询（跨国外交有限沟通）

- 国王可**遣使问询另一王国国王**（`consult_king`）：求证谣言、试探意图、在重大决策前弄清对方立场。**任何国家都可问**（含敌国）
- 对方国王被激活回应（可用 `reply_consult` 答复，可据实、可虚与委蛇、可置之不理），答复在问询方**下次政务审视时拉取看到**（慢一轮，符合中世纪信息传递）
- **严格单向防环**：被问询方（问询会话）拿不到 `consult_king`，不能发起新问询——链深恒为 1，杜绝无限激活
- 问询线程存档于 `World/diplomacy/consults/{A国}_and_{B国}.txt`：**史官可读任何国家**（作为编年史补充视角），参与双方国王可读自己的线程，**第三方不可读**
- **每王国对冷却**（7 游戏天）：冷却中再问询会被明确告知「使者尚在途中」
- 玩家国王被问询时收到提示，可经秘书处（M 键）读取本国问询线程并回复
- 离间计这类计谋因此有了**失败的可能**——被离间的国王可直接问询求证，谣言可被否认

### 工具分类系统

所有工具按 8 个分类组织，Agent 按场景默认激活相关分类，需要其他分类时调用 `browse_tools` 元工具按需解锁：

| 分类 | 包含工具 | 默认激活场景 |
|------|---------|:--|
| universal | update_knowledge, cancel_action, create_clan（仅天意） | 全部 |
| query | query_character, query_settlement, query_settlement_geography, query_world_state, query_recent_events, query_surroundings, query_party_troops, query_available_troops, query_settlement_villages, query_kingdom_settlements, query_clan_members, query_clan_fiefs, query_kingdom_clans, query_war_status, query_pending_proposals, query_hero_skills, query_influence | 全部 |
| social | change_relation, give_gold, request_gold, give_item, request_items, let_go | conversation |
| movement | move_to_settlement, wait_at_settlement, go_around_party | autonomous（conversation 亦激活） |
| military | raid_settlement, besiege_settlement, engage_party, defend_settlement, patrol_settlement, escort_party, recruit_troops, upgrade_troops, form_army, release_prisoner, execute_prisoner | autonomous（conversation 亦激活） |
| diplomacy | declare_war, propose_peace, propose_alliance, propose_trade, respond_to_diplomacy_proposal, gift_fief, change_kingdom, submit_edict, consult_king, reply_consult | diplomacy（conversation 亦激活） |
| file | read_file, write_file, write_chronicle（仅史官）, append_file, edit_file, delete_file, move_file, list_dir, glob, grep | letter, autonomous, conversation |
| communication | send_letter | letter（conversation 亦激活） |

**玩家发起的聊天（conversation）是全功能通道**——所有分类默认激活。理由：AI 几乎不主动聊天，绝大多数对话由玩家发起，若工具不全，对话里达成的承诺（议和/出兵/换国/写信/放人）就无法兑现。能力门控照旧：国王专属工具（宣战/议和/结盟/诏令/问询）仍只有国王拿到，部队工具仍只有带兵者拿到，`change_kingdom` 仅氏族领袖可用——所以对话里每个 NPC 的可用工具由身份决定。

Agent 任何时候都可以调 `browse_tools("military")` 解锁某类工具，下一轮即可使用。

### 征兵与部队管理

- Agent 可以调用 `query_party_troops` 查看部队详情。**军情迷雾**：自己与同阵营部队全量（金币、日薪、兵力/伤兵/上限、各兵种数量经验升级路径、俘虏可招募性、物品栏、装备栏）；异国部队仅侦察估计——近距给兵力带、**规模上限**与兵种构成（约 ±20%），远距给宽泛区间与定性描述（含规模上限区间），跨海/远处只有传闻，不泄露军饷、经验、装备等机密
- Agent 可以调用 `query_available_troops` 查看当前定居点可招募兵种（需在定居点内，被劫掠/敌对村庄无法招兵）
- Agent 可以调用 `query_settlement_villages` 查看城镇/城堡下属村庄——可用作征兵路线规划
- Agent 可以调用 `recruit_troops(兵种名, 数量)` 招募士兵（需在该定居点，自动扣金币）
- Agent 可以调用 `upgrade_troops(原兵种, 目标, 数量)` 升级兵种（自动检查经验、金币、所需装备和特长）
- Agent 可以调用 `buy_food(天数)` 在定居点自动采购最便宜的粮到够吃 N 天
- Agent 可以调用 `query_hero_skills` 查看任意人物的 18 个技能等级和 6 个属性值
- Agent 可以调用 `query_influence` 查看本族当前影响力——政治资财，主要用于拉军团（超过 100 时可召集本国领主）与推行政策
- `move_to_settlement` 现在可以移动到村庄（之前只能到城镇/城堡）

### 俘虏管理

- `query_party_troops` 现在会列出部队中**所有俘虏**：贵族英雄（标记 `[贵族]`，含所属氏族）+ 普通士兵（含可招募性）
- Agent 可以调用 `release_prisoner(名字)` 释放自己部队中的单个俘虏（支持中英文名）；`release_prisoner(all: true)` 一次释放全部
  - 释放**贵族英雄** → 对方成为逃亡者返回领地（`EndCaptivityAction`，可成为善意的外交信号）
  - 释放**普通士兵** → 直接从俘虏名册移除，可用 `count` 参数只释放一部分
- Agent 可以调用 `execute_prisoner(名字)` 处决自己部队中的**贵族俘虏**（仅限贵族英雄，普通士兵与玩家本人、玩家同伴不可处决）
  - 处决受 MCM「处决无惩罚」控制（**默认开**）：开启时处决不承担任何政治代价（斩首者名誉不降、全图贵族好感不降），玩家与 NPC 均生效；关闭时恢复原版/模组惩罚（名誉大降 + 受害者氏族/亲友/同阵营贵族好感大降）
  - 处决事件记入史料（`hero_killed`），受害者家族可能寻仇——由 Agent 自行权衡

### 家族补充系统

> **⚠️ 实验性功能，默认关闭**：仍不完善，建议保持关闭（MCM「启用家族补充」）。

- 当**在世贵族/佣兵家族总数**低于下限（默认 70，MCM「家族总数补充阈值」可调）时，激活「天意」agent 补充新的贵族家族——防止大屠杀导致世家凋零、世界崩解。**计数口径 = 在世贵族世家 + 雇佣兵公司合计**（排除叛军/土匪；雇佣兵公司无论是否受雇都计入——封臣与佣兵在本模组是动态身份，随时可换国/改佣兵，若只算"当前受雇"会在和平期/新档误判凋零而疯狂补族）。只有真正灭族才会拉低计数；家族换国/叛变不误触发补充
- **只在玩家位于战役大地图时检测**（捏脸/战役初始化阶段世界数据未稳定，家族族长未挂接、雇佣兵未签约，计数失真会误触发）；有**冷却**（MCM「家族补充冷却」默认 5 游戏天）防止世界长期凋零时连补一族的"永动机"式刷族
- **天意**（`__fate__` 虚拟实体，与史官并列）：决定家族名称（符合文化）、投效哪个王国（看世界局势）、家族定位，并**自行裁定新家族以正式封臣还是雇佣兵身份入世**（触发消息附当前封臣/佣兵分布与程序建议供参考，最终由天意依天下大势决定）；家族成员由**程序随机生成 3-6 人（年龄有梯度：族长 25-45，成员以青年为主、兼有壮年与偶见长者）**，**家族等级 2**（恰好够当封臣又看得出是新族）
- **家族旗帜随机生成**（`Banner.CreateRandomClanBanner`）——每个新世家都有自己的纹章，而非复制既有家族的
- 新家族族长自动获得部队（`LordPartyComponent`），可正常参战
- 家族建立只记入**原始史料**（`clan_created`：X建立、投效Y、族长为Z），**不激活史官、不改史官提示词**——简单记一笔即可
- **代码强制每次激活只补 1 个家族**（提示词虽已写明"一次只创建一族"，LLM 仍可能连建多族——曾一次激活连建 5 族、每族 3-6 英雄+部队，与游戏其他状态变更撞车导致原生层崩溃）。家族成员生成对齐游戏叛乱建族模式（先建英雄、注册进族、置为 Active），族长建队后入原始史料（`clan_created`），不激活史官
- **建族模板来源**：成员用 `CultureObject.RebelliousHeroTemplates`（原版叛乱建族同款，与 `CreateSpecialHero` 配合已验证），兜底从对象管理器筛 Lord 职业模板——早期实现用 `GetRandomTemplateByOccupation(Lord)` 查 `NotableTemplates`，而 Lord 模板（`lords.xml` 等）不在其中、恒返回 null，导致"未能生成家族成员"的快速失败
- **预算只在建族成功后占用**：失败不消耗，天意可在本次唤醒内重试；激活成功/失败均对玩家可见（左下角绿色提示新家族名，黄色提示未降下并指配置排查方向）
- 差距过大时在后续激活中逐个补齐；受 MCM「启用家族补充」开关控制

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

### 记忆权威化与记忆巩固

- **记忆优先级**：查询工具实时数据（`query_character`/`query_world_state`，系统权威）> **日记**（`decisions/diary.txt`，自我记忆权威）> 认知（`knowledge/{对方}.txt`）。长期战略不再单列文件，以日记「战略」类型条目形式存在（strategy.txt 已并入日记）
- **日记可被比它更新的聊天记录修正**：chat_logs 是系统自动保存的客观往来记录（Agent 只读、不会漏），日记是 Agent 自写的索引（可能漏记）。若聊天记录比日记新，以聊天为准，并补记日记（旧决定被推翻则补记 `[日期] 结果：…`，只追加不改写）
- **记忆巩固（`MemoryConsolidator.cs`，保底机制）**：自我审视激活前（国王政务 / 封地审视 / 外交问询回应 / 封臣进谏），先比较日记最新条目日期与 chat_logs 最新消息日期——若存在"比日记更新的往来"，先跑一次**巩固 pass**：Agent 自读日记与较新往来，把值得记住的决定/承诺/计策/结果/战略 `append_file` 补记进日记，再开始正事
  - 只在日记落后时触发，多数时候零成本；静默执行，不写聊天记录、不弹玩家消息
  - 开关：MCM「启用记忆巩固」（默认开）
  - 提示词可热重载：`consolidation_rules.txt`（行为规则）+ `memory_consolidation.txt`（激活指令）

### 提示词系统（文件化、可热重载）

所有提示词均为**中文文本文件**，存储在模组目录下，玩家可随时编辑，游戏内实时生效（热重载）。

提示词分三层：**世界层**（`world_info.txt`，大陆背景与天命）、**人物层**（persona 与记忆）、**游戏规则层**（`game_rules.txt`，卡拉迪亚的实际运转机制——机动/金钱/部队上限/兵种/招募/战争/影响力/**阵营归属**（氏族加入哪国全在族长自决，无须国王准许））。规则层让 agent 按游戏机制而非现实经验做决策（例：士兵阵亡随时可补，真正约束是钱、部队上限、兵种等级；机动性远高于现实，滞守一隅常无收益）。规则层注入稳定前缀（缓存友好），**史官不注入**。

```
_Module/Prompts/
├── system_prompt.txt            # 默认系统提示词模板（新战役复制为初始值）
├── world_info.txt               # 默认世界背景介绍（六大王国 + 天命，独立成段可热重载）
├── world_info_nords.txt         # 可选的诺德势力世界背景段（MCM「包含诺德势力」开启时拼入）
├── InitialHistory/              # 开局预置初始历史（《卡拉迪亚上古编年史》+ 六国《XX源流纪事》，止于1084年）
├── game_rules.txt               # 游戏运转规则（agent 按游戏机制决策，玩家可编辑，热重载）
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
├── clan_replenishment_rules.txt # 天意建族规则（家族补充，热重载）
├── consolidation_rules.txt      # 记忆巩固行为规则（热重载）
├── memory_consolidation.txt     # 记忆巩固激活指令（热重载）
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
        ├── world_info_nords.txt  # 本战役的诺德势力段（可编辑，热重载）
        ├── game_rules.txt        # 本战役的游戏运转规则（可编辑，热重载）
        ├── agent_system.txt      # 本战役 Agent 提示词（热重载）
        ├── persona_generation.txt # 本战役性格生成提示词（热重载）
        ├── context_template.txt  # 本战役 Context 模板（热重载）
        ├── diplomacy_rules.txt   # 本战役外交决策规则（热重载）
        ├── historian_rules.txt  # 本战役史官编年史规则（热重载）
        ├── consolidation_rules.txt # 本战役记忆巩固行为规则（热重载）
        ├── memory_consolidation.txt # 本战役记忆巩固激活指令（热重载）
        └── NPCs/                 # Agent 管理的 NPC 文件系统
            └── {entity_id}/        # 每个 Entity 独立目录
                ├── character.json # 基础 ID 信息（只读，自动生成）
                ├── persona.txt    # 结构化 persona（动机、性格特质、表达风格三段式）
                ├── knowledge/
                ├── chat_logs/
                ├── relationships/
                ├── goals/
                └── decisions/
```

> 玩家的实体目录（`{Name}_main_hero`）额外含 `thread_read_state.json`——各线程的已读水位，O 键未读角标据此计算。

> **人称约定**：所有提示词文件中只使用「你」指代 Agent 自己、「对方」指代交互对象。
> `query_character` 返回结果以「该人物：」开头作为补充约定。
> 禁止使用「TA」「他/她」「其」等模糊人称。未来添加新提示词文件时必须遵守此约定。

> **上下文结构（缓存优化）**：`context_template.txt` 以 `<!--VOLATILE-->` 标记分为两段——
> **稳定前缀**（身份/persona/世界背景/工具清单/行为守则）进 system 消息；**易变块**（当前时间/自身状态/对对方认知/目标/客观关系/内政报告）单独作为【当前状况】user 消息插在最新消息前。
> 这样 system+历史构成逐字节稳定的前缀，最大化 DeepSeek 前缀缓存命中（缓存命中输入价格仅为未命中的 1/50）。
> **史官例外**：史官 intent 不拆易变块，保持单一 system 消息——文笔是模组核心，结构与旧版一致，绝对保真。
> 旧版模板（无标记）整体按稳定处理，行为与旧版一致；模板改动随 newer-wins 同步到战役副本。
> `conversation_rules.txt`/`letter_rules.txt` 采用**条件式记忆读取**：基本信息/当前目标/对对方的认知已由【当前状况】提供，不再每回合机械重复 query/read，仅当需要更新的实时信息时才查询。
> 聊天/书信上下文只携带最近 N 条消息（MCM「聊天历史上限」可调）；agent 被明确告知完整往来记录在 `chat_logs/{对方ID}.txt`——若上下文被裁掉后感到困惑（对方提到记不清的旧事），可主动 `grep`/`read_file` 检索完整记录。**当历史确实被截断时，还会在【当前状况】易变块中注入一句系统提示**（告知截断、引导检索、并明示不得向对方提及），注入在易变块而非稳定前缀，不影响前缀缓存命中。

- **Agent 系统**：每个 NPC 有独立文件系统，Agent 通过 `read_file`/`write_file`/`append_file`/`edit_file`/`delete_file`/`list_dir`/`glob`/`grep`/`send_letter` 工具管理记忆
- **信息隔离**：Agent 只能操作自己目录下的文件 + World/ 目录，不能读取其他 NPC 的信息
- **解耦存储**：聊天记录（`chat_logs/`）、对 Entity 认知（`knowledge/`）、NPC 性格（`persona.txt`）全部独立文件，Agent 按需精确读取
- **LLM 生成 persona**：首次对话时自动调用 LLM 为 NPC 生成结构化 persona（玩家角色除外，使用静态占位文本）。生成使用 `reasoning_effort=low` + 4096 token 上限（机械任务不需要深度思考，防止思考占满 token 导致正文截断）；加载时若检测到 persona.txt 缺失标准段落标记（被截断的残缺文件）会自动重新生成
- **ContextBuilder**：根据交互双方动态组装提示词，通过 `context_template.txt` 模板注入 persona 和能力信息；输出拆分为稳定前缀（system）+ 易变块（【当前状况】user 消息），史官 intent 例外（合并为单一 system）
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

在主菜单 **Options → Mod Options → AI编年史·言出法随** 中可配置：

| 设置项 | 说明 | 默认值 |
|--------|------|--------|
| API 地址（兜底） | 全局兜底 LLM API 端点。各场景留空的字段回退到这里 | `https://api.deepseek.com/v1/chat/completions` |
| 模型名称（兜底） | 全局兜底模型名称。各场景留空的字段回退到这里 | `deepseek-v4-flash` |
| API 密钥（兜底） | 全局兜底 API 密钥。各场景留空的字段回退到这里 | 空（需自行填入） |
| 最大 Token 数 | **全模组统一**的 AI 单次回复 token 上限（含 persona 生成、连接测试）。DeepSeek V4 最高 384K 输出；默认 32768 足够长编年史/长思考，特殊场景可上调至 65536 | `32768` |
| 回复创造性 | Temperature 值，越低越稳定保守 | `0.8` |
| 思考强度 (reasoning_effort) | AI 思考强度（成本大头，见下）。史官固定 high 不受此设置影响；部分模型不支持该参数则不生效 | `low` |
| API 超时（秒） | 请求超时时间 | `30` |
| Test Connection | 测试兜底连接（含 function calling 支持检测） | — |

**场景连接配置**（懒人可只填上面兜底三件套，各场景留空即全用兜底）：

| 场景组 | 设置项 | 说明 |
|--------|--------|------|
| 对话与书信场景 | URL / 模型 / API 密钥 | 玩家对话（【AI 聊天】）与写信共用（同一线程）；留空回退兜底 |
| 政务外交场景 | URL / 模型 / API 密钥 | 国王内外政务审视（KingDiplomacy）；留空回退兜底 |
| 外交问询场景 | URL / 模型 / API 密钥 | 国王遣使问询他国（KingConsult）；留空回退兜底 |
| 封地审视场景 | URL / 模型 / API 密钥 | 被夺封方审视处境（FiefReview）；留空回退兜底 |
| 封臣谏言场景 | URL / 模型 / API 密钥 | 封臣进谏（Advisory）；留空回退兜底 |
| 天意建族场景 | URL / 模型 / API 密钥 | 家族补充「天意」（clan_replenishment）；留空回退兜底 |
| 史官场景 | URL / 模型 / API 密钥 | 编年史/列传/专题史（historian）；留空回退兜底 |
| 记忆巩固场景 | URL / 模型 / API 密钥 | 记忆巩固 pass（consolidation）；留空回退兜底 |
| 签到场景 | URL / 模型 / API 密钥 | 行为/计划签到（chat）；留空回退兜底 |
| 秘书处场景 | URL / 模型 / API 密钥 | 玩家秘书处（chancery，M 键）；留空回退兜底 |

> 每个场景组各有「测试此场景」按钮，用本场景生效配置（留空字段回退到兜底）测试连通性与 function calling 支持。**逐字段兜底**：哪个字段留空就用兜底的哪个字段——例如只给史官配高规格模型，URL 和密钥沿用兜底即可。
| 双倍声望 | 战斗中声望翻倍 | 关闭 |
| 显示工具调用提示 | 左下角显示 Agent 的文件操作 | 开启 |
| 调试日志 | 将 LLM 调用摘要、思维链摘录写入战役目录 `debug_logs/`，便于排查 agent 行为 | 开启 |
| Agent 并发数 | 同时运行的 Agent 任务数上限。越大吞吐越高，但工具在主线程串行执行，过大会帧卡顿 | `5` |
| 聊天历史上限（条） | 保留最近 N 条消息发给 AI | `20` |
| 注入世界背景 | 是否在提示词中加入卡拉迪亚背景 | 开启 |
| 包含诺德势力 | 是否在世界背景中额外加入「诺德」势力（大陆之外的北境异族）。默认关闭——诺德不是原版可交互势力，仅在装了相关 DLC/模组、希望 LLM 知道诺德存在时打开 | 关闭 |
| 注入游戏规则 | 是否在提示词中加入卡拉迪亚实际运转规则（机动/金钱/部队上限/兵种/招募/战争/影响力/阵营归属——氏族加入哪国全在族长自决，无须国王准许），让 agent 按游戏机制而非现实经验决策。史官不注入 | 开启 |
| 最大好感变化 | Agent 单次修改好感度的上限 | `5` |
| 信件级联深度上限 | NPC 间连环写信的最大层数 | `5` |
| 环境扫描半径（占地图比例） | query_surroundings 扫描半径硬上限 = 此比例 × 地图尺度（0.2 ≈ 500 单位 ≈ 1-2 座城池间距） | `0.2` |
| 情报侦察半径（占地图比例） | query_party_troops 查看异国部队的近距侦察半径（地图尺度比例，0.2 ≈ 1-2 座城池间距），之外的情报降为模糊/传闻 | `0.2` |
| 禁止原版外交（Agent 主导） | 禁止原版 AI 外交（宣战/议和/结盟/贸易/盟约号召宣战投票），所有外交由国王 Agent 决策 | 开启 |
| 册封由 Agent 主导 | 攻下的城（无论玩家或 AI）不再触发原版影响力投票，改由国王 Agent 决定归属；攻城后默认归国王氏族 | 开启 |
| 外交触发几率/天 | 每个国王每天触发外交审视的概率（0.1=平均十天一次，0.5=平均两天一次） | `0.1` |
| 国王冷静期（天） | 国王每次外交激活后的冷却时间（冷却期内不被定时激活，但收到的外交提案仍会正常触发） | `3` |
| 编年史间隔（年） | 史官编纂编年史的间隔（1=每年，3=每三年） | `1` |
| 启用封臣谏言 | 开启后封臣按概率陆续进谏 | 开启 |
| 封臣谏言概率/天 | 每个王国每天触发封臣进谏的概率 | `0.1` |
| 所有贵族立传 | 死后立传范围：所有氏族贵族 或 仅氏族领袖和国王 | 开启 |
| 处决无惩罚 | 开启后处决贵族不承担任何政治代价（名誉不降、好感不降），玩家与 NPC 均生效 | 开启 |
| 启用家族补充 | 在世贵族/佣兵家族总数低于阈值时激活「天意」补充新家族（**实验性，默认关闭**） | 关闭 |
| 家族总数补充阈值 | 在世贵族/佣兵家族总数低于此值触发补充（单位：家族，原版约 80）。封臣与佣兵是动态身份，故只按总数判定——只有真正灭族才拉低计数 | `70` |
| 家族补充冷却（游戏天） | 天意每次降下血脉后的冷却。冷却期内即使仍低于下限也不重复激活，避免"永动机"式连补 | `5` |
| 启用记忆巩固 | 自我审视（国王政务/封地审视/外交问询/封臣进谏）激活前，若日记落后于聊天记录，先跑一次巩固——把最近往来中值得记住的决定/承诺/计策/战略补记进 decisions/diary.txt，避免照陈旧日记行事。只在日记落后时触发 | 开启 |
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
| 启用语音朗读（TTS） | AI 聊天（【AI 聊天】入口）收到回复时用语音朗读。免费 Edge TTS，需联网，无需 API Key。书信与秘书处不朗读；女角色女声、男角色男声 | 关闭 |
| 语速（%） | 朗读语速偏移。-50 慢一半，+50 快一半，0 为正常 | `0` |
| 音量（%） | 朗读音量，0 静音，100 最大 | `80` |
| 测试语音 | 用当前设置合成并播放一句测试语音，验证网络与设备可用。主菜单也可测试（默认男声） | — |

> **成本提示**：AI 成本主要由两部分构成——**思考输出**（`reasoning_content`，按输出价计费，占比可超 1/3）与**缓存未命中输入**（每次新对话冷启动重编 system+工具数组）。「思考强度」设为 `low` 可显著降低思考 token（默认 high 时每次决策都产生大量思维链）；DeepSeek v4-flash 支持 `low/high/max` 三档。**史官固定 high**（文笔核心）。部分模型（非思考模式或不支持该参数的端点）此设置不生效。

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

1. 启动游戏，在启动器中勾选 **AI Chronicle: Words Become Deeds** 及四个前置模组
2. 进入主菜单后，在 **Mod Options → AI编年史·言出法随** 中填入 API Key
3. 开新档或读档 → 模组自动在 `Prompts/Campaigns/` 下创建本战役的提示词目录
4. （可选）编辑 `system_prompt.txt`、`world_info.txt` 或角色 JSON 文件来定制 AI 行为
5. 与任意领主对话 → 点击 **「【AI 聊天】」**
6. 在聊天窗口中输入消息，按「发送」按钮
7. AI 回复会显示在聊天窗口中，支持多轮对话
8. 点击聊天窗口右上角的 X 关闭，回到对话界面

---

## 文件结构

```
AIChronicle/
├── Core/                    # 引擎层：入口、设置、基础设施
│   ├── SubModule.cs         # 模组入口，Harmony 激活，初始化 PromptManager
│   ├── Settings.cs          # MCM 设置类（连接兜底 + 游戏设置）
│   ├── Settings.Scenarios.cs # MCM 十个场景连接配置组（URL/模型/密钥 + 测试按钮）
│   ├── ConnectionResolver.cs # 场景连接解析器（intent → 生效 URL/模型/密钥，逐字段兜底）
│   ├── DebugLogger.cs       # 调试日志（LLM 调用摘要/思维链摘录 → 战役 debug_logs/）
│   ├── SafeFileIO.cs        # 带重试的文件 IO（并发读写同一文件时避免"文件正被使用"异常）
│   ├── MainThreadExecutor.cs # 主线程分发器（后台线程工具执行回主线程，防跨线程崩溃）
│   ├── TtsService.cs        # 语音合成门面（ITtsProvider 接口 + 合成/缓存/播放/打断）
│   └── EdgeTtsProvider.cs   # Edge TTS 免费引擎实现（手写 WebSocket 客户端，可设浏览器 UA）
├── Agents/                  # Agent 核心：上下文、调度、记忆
│   ├── AgentManager.cs      # Agent 管理器基类（NPC 文件系统、路径权限、persona 生成）
│   ├── AgentManager.Files.cs / Threads.cs / Proposals.cs / Permissions.cs  # 文件工具/书信水位/外交提案/权限模型
│   ├── ContextBuilder.cs    # 动态上下文组装（persona + 能力 + 模板）
│   ├── AIChatClient.cs      # HTTP 客户端，SSE 流式请求，多轮工具调度
│   ├── PromptManager.cs     # 提示词管理器（文件热重载、战役目录、角色 JSON 读写）
│   ├── MemoryConsolidator.cs # 记忆巩固（diary 权威化保底）
│   ├── AgentScheduler.cs    # 事件调度器基类（优先级队列、Tick）
│   ├── AgentScheduler.Events.cs / Advisory.cs / Historian.cs / Fate.cs  # 事件处理/谏言/史官/天意
│   └── PartyBehaviorManager.cs # 部队行为状态机（PendingAction + Tick）
├── Entities/                # Entity 统一抽象
│   ├── Entity.cs            # Entity 数据模型（统一玩家/NPC，附能力标签）
│   └── EntityManager.cs     # Entity 生命周期管理、查找与缓存
├── Tools/                   # 工具执行器（50+ 游戏工具，按领域拆 partial）
│   ├── ToolExecutor.cs      # 基类（工具入口 ExecuteToolCall + 共享助手）
│   ├── ToolExecutor.Query.cs / Intel.cs / Military.cs / Social.cs / Diplomacy.cs / Fate.cs
├── UI/                      # 界面层
│   ├── AIChatScreen.cs      # 聊天屏幕管理器（GauntletLayer 挂载）
│   ├── AIChatScreenVM.cs    # 聊天 ViewModel（消息列表、输入绑定、function calling 处理）
│   ├── LetterListScreen.cs  # 书信系统屏幕管理器（战役地图 O 键入口）
│   └── HistoryScreenVM.cs   # 史书 UI ViewModel（编年史列表、内容加载）
├── Systems/                 # 游戏系统与 Harmony 补丁
│   ├── DiplomacyService.cs  # 外交服务（宣战/议和/结盟/贸易/回复提案）
│   ├── HistoryRecorder.cs   # 历史记录器（监听游戏事件自动写入原始史料）
│   ├── LordChatBehavior.cs  # CampaignBehavior：对话中插入聊天选项，管理战役 ID
│   ├── DiplomacyBanPatch.cs # Harmony 补丁，禁止原版 AI 外交（MCM 可开关）
│   ├── FiefAssignmentPatch.cs # 攻城后册封由 Agent 主导
│   └── ExecutionNoPenaltyPatch.cs # 处决无惩罚
├── AGENTS.md                # AI 开发工作流文档
├── README_MOD.md            # 本文件（功能说明）
├── CLAUDE.md                # Claude Code 入口文档（指向 AGENTS.md / README_MOD.md）
├── _Module/
│   ├── SubModule.xml     # 模组元数据
│   ├── GUI/Prefabs/
│   │   ├── AIChatScreen.xml      # 聊天窗口 GauntletUI 布局
│   │   ├── LetterListScreen.xml  # 书信系统界面布局
│   │   └── HistoryScreen.xml     # 史书 UI 布局（1100×700 双栏）
│   └── Prompts/
│       ├── system_prompt.txt      # 系统提示词模板（玩家可编辑，热重载）
│       ├── world_info.txt         # 默认世界背景
│       ├── world_info_nords.txt   # 可选的诺德势力世界背景段（MCM 开关）
│       ├── InitialHistory/        # 开局预置初始历史（止于1084年）
│       ├── game_rules.txt         # 游戏运转规则（热重载）
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
- 模组版本：**v2.2.0**

### v2.2.0 更新要点

- **初始历史（开局前既成史）**：战役创建时预置史官早已写成的《卡拉迪亚上古编年史》与六国《XX源流纪事》（卡拉德帝国/巴旦尼亚/瓦兰迪亚/斯特吉亚/库赛特/阿塞莱），存于 `World/history/chronicles/`——开局即有历史可读，玩家 H 键可见、NPC 可 `read_file` 查阅、史官编纂时可对照旧史。体例避开灭国时生成的 `{国名}世家` 防撞名；模板源 `_Module/Prompts/InitialHistory/`，进档只复制缺失文件、不覆盖已有史文
- **修复史书目录渲染 bug**：HistoryScreen.xml 的 ItemTemplate 含多个顶层控件，而 GauntletUI 只取第一个作为条目模板，导致史书目录只渲染分组标题、条目按钮从未出现。改为单一容器 Widget 后目录正常
- **初始历史译名全面对齐游戏**：卡拉狄乌斯大帝/沙拉斯/巴拉维诺斯/帕拉汶德/吕卡隆/阿契特/阿基娜等均按游戏内本地化译名，史官 `query_character` 返回与史书一致
- **世界观时局锚与地理权威声明**：`world_info` 主要势力段标注「1084年（帝国三分之初）」基准时局；新增「史料」段引导可 `read_file` 查阅初始历史；巴旦尼亚位置措辞修正；地理方位与势力分布明确以查询工具（`query_world_state` / `query_settlement_geography` / `query_surroundings`）为权威
- **史官规则引导读初始历史**：`historian_rules.txt`「查阅旧史」补充初始历史可作对照；`world_info` 提示大陆既成之史见 `history/chronicles/`

- **家族补充系统（天意）全面修复与完善**——从概念性变成真正可用：
  - **修复建族死路径**：成员模板改用 `CultureObject.RebelliousHeroTemplates`（原版叛乱建族同款）+ 对象管理器 Lord 模板兜底——原实现用 `GetRandomTemplateByOccupation(Lord)` 查 `NotableTemplates` 而 Lord 模板不在其中，恒返回 null 导致「未能生成家族成员」的快速失败，此路径此前从未跑通过
  - **检测口径改为家族总数**：不再分封臣/雇佣兵双阈值（二者在本模组是动态身份，且"当前受雇"随签约波动，新档/和平期会误判凋零）。改为统计**在世贵族世家+雇佣兵公司合计**，低于「家族总数补充阈值」（默认 70）才触发；新档不再开局误激活
  - **天意自行裁定封臣/佣兵**：触发消息附当前封臣/佣兵分布与程序建议，最终由天意依天下大势决定（`is_mercenary`）
  - **新增 MCM「家族补充冷却（游戏天）」**（默认 5）：防世界长期凋零时连补一族的"永动机"式刷族；原「封臣家族补充阈值」「雇佣兵家族补充阈值」合并为「家族总数补充阈值」
  - **只在大地图检测**：捏脸/战役初始化阶段（世界数据未稳定、族长未挂接、佣兵未签约）不再误触发
  - **族长更替过渡不误判**：存活判定改用「家族成员有在世者」而非「族长非空」，族长死亡→新族长接任的短暂窗口不再被当作家族凋零
  - **预算成功才占用**：建族失败不消耗本次唤醒预算，天意可重试；成功/失败均对玩家可见（绿色提示新家族名 / 黄色提示未降下并指配置排查）
  - **旗帜随机生成**（`Banner.CreateRandomClanBanner`）：每个新世家有自己纹章，不再复制首个有旗帜的家族
  - **反同质化**：家族根基城从王国城镇中随机抽取，成员年龄拉开梯度（族长 25-45，青年/壮年/偶见长者）

### v2.0.5 更新要点

- **攻城决策信息补全（围城不盲打）**：此前 agent 围城完全不知道城中守军——`query_settlement` 只返回繁荣度，`besiege_settlement` 直接发兵。现在：
  - `query_settlement` / `query_settlement_geography` 新增**守军兵力**（驻军 + 民兵 + 驻城贵族部队的准确总数与构成）
  - `besiege_settlement` 返回时附带**守军评估**：守军总数与己方兵力对比，明显不足（<70%）时提醒可 `form_army` 拉军团或另择弱城（不硬拦，保留 agent 自主权）
  - 设计权衡：守军数**不设情报迷雾**、给准确判断——玩家实时激活可反复试，但 agent 一次激活做出的决定没有中途取消的机会，判断必须可信
  - 提示词引导：`besiege_settlement` 工具描述要求"围城前先 `query_settlement` 摸守军"；`game_rules.txt` 新增「围城之守」条目（守军构成 + 悬殊勿单部强攻）
- **`query_surroundings` 扫描半径改用地图比例**：原参数为绝对 km（默认 20km = 20000 地图单位，而全图仅约 2500 单位——半径几乎覆盖全图，"扫描"形同虚设）。改为**占地图比例**（默认 0.2 ≈ 1-2 座城池间距），MCM 设置项由「环境扫描半径（km）」改为「环境扫描半径（占地图比例）」——旧配置会被重置，需重填
- **查询工具新增围攻状态**：`query_settlement` / `query_settlement_geography` 返回是否正被某势力围攻；`query_kingdom_settlements` 领土清单中被围城标 `⚠被围`
- **工程健壮性守则**（开发文档）：单类文件不超过 ~1000 行、引入第三方库需审批并评估与其他模组 DLL/共享库冲突风险、新功能须遵循现有分层并复用基础设施

### v2.0.2 更新要点

- **AI 语音朗读（TTS）**：AI 聊天回复朗读，免费 Edge TTS（无需 key），女声/男声按性别分配；MCM 开关/语速/音量/测试按钮；主菜单也可试听。详见上文「AI 语音朗读（TTS）」
- **修复：上下文角色混淆**（v1.4.0 缓存优化的回归）——易变块【当前状况】从 `user` 角色改回 `system` 角色：v1.4.0 把它作为独立 user 消息插在玩家消息前，模型会把这条系统情境当成"对方说的话"，导致回复引用对话中不存在的"请教/答复"等幻觉（如"我等着您的答复""您过谦了"）。修复后缓存优化保留（稳定前缀不变），且加 400 兜底（端点拒绝非开头 system 时自动回退 user）

### v2.0.1 更新要点

- **聊天历史上限改为固定锚点截断（缓存优化）**：对话超限时不再滑动窗口（保留最近 N 条，超限后每轮整个历史右移一位、前缀缓存全 miss），改为保留「最早 3 条 + 最近 N-3 条」——头部锚点固定，超限后跨轮次的 system+历史前缀仍逐字节稳定，DeepSeek 前缀缓存持续命中。中间被省略的旧往来 agent 仍可用 `grep`/`read_file` 检索 chat_logs 补齐。

### v2.0.0 初版要点

- **正式定名「AI编年史·言出法随」**（英文 AI Chronicle: Words Become Deeds）：告别开发代号，全面改名发布，作为**初版**——不再兼容此前任何版本的存档与配置
- **场景连接配置**：10 个场景（对话与书信 / 政务外交 / 外交问询 / 封地审视 / 封臣谏言 / 天意建族 / 史官 / 记忆巩固 / 签到 / 秘书处）可独立配置 URL/模型/API 密钥，**逐字段兜底**到全局「连接设置（兜底）」；每场景有独立测试按钮；全局默认模型 `deepseek-v4-flash`
- **max_tokens 统一**：全模组最大 Token 数只由 MCM「最大 Token 数」一处控制（含 persona 生成、连接测试），移除 4096/300 等硬编码
- **遗留代码清理**：移除旧 mailbox 信箱迁移、旧档 persona 战争倾向重掷、旧模板兼容等向前兼容逻辑
- 开发期全部功能（AI 领主聊天、书信、外交、史书、谏言、记忆巩固、家族补充等）已并入初版，见上文「当前功能」

### v1.6.0 更新要点

- **书信系统统一为单一线程模型**：`send_letter` 工具不再写独立的 mailbox 收件箱，信件与面对面聊天**同源存储**到 chat_logs 线程（📜 标记区分）——信件与对话彻底合一，跨 NPC/玩家一致
- **O 键面板重写**：从「收件箱 + 联系人」两栏改为**统一联系人列表**，每行附未读角标（`· N 条未读`），未读置顶、按最近活跃排序；点联系人直接打开往来线程，**删除 800 字截断的来信弹窗**
- **已读/未读追踪**：新增每线程已读水位（`thread_read_state.json`，存玩家实体目录），打开线程/发信/收新回复时自动推进
- **NPC 主动来信自动进联系人**：NPC 用 `send_letter` 给玩家写信即登记联系人并显示未读
- **拦截盟约"号召盟友宣战"投票**：原版军事同盟触发的"是否号召盟友向敌国宣战"国内投票被取消（玩家对话里的原版"号召盟友"选项一并隐藏），同盟只保留名义作用，是否号召盟友/宣战由国王 Agent 激活时自行决定
- **被擒场景可谈判**：玩家被强敌追上（投降/应战对话）时新增【AI 聊天】选项，可说服 agent 用 `let_go` 放行；放行后自动结束对话与遭遇，玩家获释

### v1.4.0 更新要点

- **缓存优化**：上下文拆为「稳定前缀 + 易变【当前状况】块」（`<!--VOLATILE-->` 标记），system+历史逐字节稳定，最大化 DeepSeek 前缀缓存命中（命中价仅为未命中 1/50）。史官例外保持单一 system 消息（文笔保真）。
- **思考强度可控**：MCM 新增「思考强度 (reasoning_effort)」，默认 `low`（成本大头）；史官固定 `high`。部分模型不支持该参数则不生效。
- **缓存命中统计**：调试日志记录每次请求的命中/未命中/命中率（`stream_options.include_usage`）。
- **省 token**：工具清单精简；`conversation_rules`/`letter_rules` 改条件式记忆读取（不再每回合机械重复查询）；browse_tools 解锁跨回合持久化；knowledge/goals 注入截断。
- **修复**：流式 usage 解析边界（`"usage":null` 的 chunk 不再误判）；兼容 400 自动回退（无 usage/无 reasoning_effort 重试）。

---

## 鸣谢

本项目站在前人的肩膀上：

- **[opencode](https://github.com/anomalyco/opencode)** — Agent 驱动架构（Entity 统一抽象、动态上下文、文件即记忆）以及文件读写/grep 工具与工具描述规范，均以其为蓝本
- **DeepSeek** — 提示词缓存优化（稳定前缀 + 易变块）参考 DeepSeek 官方上下文缓存文档
- **[DeepSeek-Reasonix](https://github.com/esengine/DeepSeek-Reasonix)** — 面向 DeepSeek 的 coding agent，缓存与流式设计参考了它的思路

## 待办事项

