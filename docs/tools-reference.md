# 工具清单（AI 参考文档）

> 本文件是 `AGENTS.md` 的补充参考，列出全部工具的权威清单。新增/修改工具时，`tools.json`/`agent_tools.json` 与 `ToolExecutor.ExecuteToolCall` 的 switch 必须同步（详见 `AGENTS.md` 硬规则）。

## 1. 开发工具

| 工具 | 路径 | 用途 |
|------|------|------|
| dnSpy GUI | `C:\Users\<用户名>\Tools\dnSpy\dnSpy-net-win64\dnSpy.exe` | 反编译、调试、查看游戏源码 |
| dnSpy CLI | `C:\Users\<用户名>\Tools\dnSpy\dnSpy-net-win64\dnSpy.Console.exe` | 批量反编译（命令行） |
| dotnet CLI | `dotnet` | 编译、创建新项目 |
| Rider | `C:\Program Files\JetBrains\JetBrains Rider 2026.2\bin\rider64.exe` | IDE |

## 2. 游戏工具（tools.json，51 个）

| 工具 | 类别 | 说明 |
|------|------|------|
| `query_clan_fiefs` | 查询 | 查询氏族持有的封地列表 |
| `query_character` | 查询 | 查询人物公开档案（身份/家族/王国/兵力/位置），系统权威数据 |
| `query_settlement` | 查询 | 查询定居点信息（所有者/繁荣度/类型/**守军兵力**（驻军+民兵+驻城贵族部队，不设迷雾）/围攻状态） |
| `query_settlement_geography` | 查询 | 查询定居点地理情报（位置/周边邻居/边境标签/守军兵力/围攻状态） |
| `query_world_state` | 查询 | 获取世界局势（王国兵力/交战状态） |
| `query_recent_events` | 查询 | 查询人物近期事件（比武/俘虏/婚嫁/阵亡等百科记录） |
| `query_surroundings` | 查询 | 扫描周围环境（当前位置、附近城镇城堡、附近部队及阵营关系；半径按地图比例） |
| `update_knowledge` | 认知 | 记录关于对方的新认知 |
| `change_relation` | 关系 | 修改对任意人物的好感度（支持 target_entity_id） |
| `give_gold` | 经济 | 赠予任意人物金币（支持 target_entity_id） |
| `request_gold` | 经济 | 向任意人物索要金币（玩家需确认，弹窗 60 秒倒计时超时视为拒绝，NPC 自动划转） |
| `move_to_settlement` | 行军 | 部队行军到城镇/城堡/村庄（支持 activate:true 参数自动唤醒） |
| `wait_at_settlement` | 行军 | 在定居点停留指定时长（支持 activate:true 参数到期自动唤醒） |
| `raid_settlement` | 军事 | 劫掠村庄 |
| `besiege_settlement` | 军事 | 围攻城镇/城堡（返回时附守军评估：守军总数 + 己方兵力对比，明显不足时提醒拉军团或另择弱城） |
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
| `request_items` | 社交 | 向任意人物索要物品（NPC 直接划转，玩家弹确认框，60 秒倒计时超时视为拒绝） |
| `let_go` | 社交 | 遭遇战中放走玩家（仅当己方兵力占优时可用，含冷却期） |
| `release_prisoner` | 军事 | 释放自己部队中的俘虏（贵族英雄→逃亡者回领地，普通士兵→移除；支持按名单个释放或 all 全放） |
| `execute_prisoner` | 军事 | 处决自己部队中的贵族俘虏（仅限贵族；受 MCM「处决无惩罚」控制，默认开=无惩罚） |
| `create_clan` | 通用 | 天意建族（家族补充系统）：建新贵族家族（成员 3-6 人程序生成、家族等级 2、族长带兵、旗帜随机、入原始史料但不激活史官）。仅 `__fate__` 实体可用（能力门控）。**代码强制每次激活只建一族**（LLM 可能连建多族，曾致原生崩溃）；英雄创建对齐游戏叛乱建族模式（先建英雄→注册进族→置 Active）；成员模板用 `CultureObject.RebelliousHeroTemplates`（Lord 模板不在 NotableTemplates，`GetRandomTemplateByOccupation(Lord)` 恒 null 的死路径已弃）；**预算只在建族成功后才占用**（失败不消耗、可重试） |
| `create_kingdom` | 外交 | 封臣/独立氏族领袖自立建国（**实验性功能：未经实机测试，为未来功能提前做的准备**）。门槛对齐原版 KingdomCreationModel（tier4 + 城镇/城堡≥1 + 兵≥100）；排除玩家（走原版总督对话）/国王/雇佣兵；封臣建国先叛乱脱离旧国（保封地、对旧国及其交战方宣战）再建国；文化参数精确优先再模糊匹配，不自创文化（CultureObject 是 XML 静态内容）；国号查重；史官自动入史 + 建国纪事；称王后 `EntityManager.RefreshEntity` 补 Diplomat 能力 |

## 3. 文件工具（agent_tools.json，19 个）

| 工具 | 说明 |
|------|------|
| `read_file` | 读取文件内容（支持行号范围） |
| `write_file` | 创建新文件或完整重写 |
| `write_chronicle` | 史官成文落盘（体例/名称/正文，系统按「名称+体例.txt」规范命名，仅 `__historian__` 可用，Chronicler 能力门控） |
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
| `send_envoy` | 私有密使：派使者联络其他家族领袖/他国国王（`World/correspondence/{idA}_and_{idB}.txt`，**史官与第三方不可读**，仅参与者双方；立即激活对方一次回应，单跳防环；每实体对 7 游戏天冷却。仅家族领袖可用） |
| `reply_envoy` | 回复收到的私有密使（回写密使线程，不激活任何人，发送方下次自省/政务审视时读到） |
| `record_resolve` | 日记落笔（`decisions/diary.txt` 强制 `[年季节日] 类型：内容` 格式，类型白名单：决心/决定/承诺/计策/情报/评价/结果/战略——防止 write_file 把日记格式写坏导致记忆检索失效） |

## 4. 工具分类系统

所有工具按 8 个分类组织，Agent 按场景默认激活相关分类，需要其他分类时调用 `browse_tools` 元工具按需解锁：

| 分类 | 包含工具 | 默认激活场景 |
|------|---------|:--|
| universal | update_knowledge, cancel_action, create_clan（仅天意） | 全部 |
| query | query_character, query_settlement, query_settlement_geography, query_world_state, query_recent_events, query_surroundings, query_party_troops, query_available_troops, query_settlement_villages, query_kingdom_settlements, query_clan_members, query_clan_fiefs, query_kingdom_clans, query_war_status, query_pending_proposals, query_hero_skills, query_influence | 全部 |
| social | change_relation, give_gold, request_gold, give_item, request_items, let_go | conversation |
| movement | move_to_settlement, wait_at_settlement, go_around_party | autonomous（conversation 亦激活） |
| military | raid_settlement, besiege_settlement, engage_party, defend_settlement, patrol_settlement, escort_party, recruit_troops, upgrade_troops, form_army, release_prisoner, execute_prisoner | autonomous（conversation 亦激活） |
| diplomacy | declare_war, propose_peace, propose_alliance, propose_trade, respond_to_diplomacy_proposal, gift_fief, change_kingdom, create_kingdom（实验性）, submit_edict, consult_king, reply_consult | diplomacy（conversation 亦激活） |
| file | read_file, write_file, write_chronicle（仅史官）, append_file, edit_file, delete_file, move_file, list_dir, glob, grep, record_resolve | letter, autonomous, conversation, self_review |
| communication | send_letter, send_envoy, reply_envoy, submit_advisory, submit_secret_advisory, submit_edict | letter（conversation 亦激活）；send_envoy/reply_envoy 在 self_review 激活 |

**玩家发起的聊天（conversation）是全功能通道**——所有分类默认激活。理由：AI 几乎不主动聊天，绝大多数对话由玩家发起，若工具不全，对话里达成的承诺（议和/出兵/换国/写信/放人）就无法兑现。能力门控照旧：国王专属工具（宣战/议和/结盟/诏令/问询）仍只有国王拿到，部队工具仍只有带兵者拿到，`change_kingdom` 仅氏族领袖可用——所以对话里每个 NPC 的可用工具由身份决定。

Agent 任何时候都可以调 `browse_tools("military")` 解锁某类工具，下一轮即可使用。
