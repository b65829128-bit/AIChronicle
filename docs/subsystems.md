# 子系统机制（AI 参考文档）

> 本文件是 `AGENTS.md` 的补充参考，整理各异步子系统的机制细节。实现/修改这些子系统时用 `read` 按需查阅。

## 1. 信件激活机制（AgentScheduler）

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
| 2 | LetterReceived / EnvoyReceived（来信/密使） | 中 |
| 3 | BehaviorCheckIn / PlanCheckIn / FiefReview（签到/封地审视） | 低 |
| 4 | SelfReview（概率激活的自省） | 最低，只在无更高优先工作时处理 |

- 并发安全：每任务上下文（`CurrentHero`/agent/深度等）经 `AsyncLocal` 隔离，工具仍统一回主线程串行执行
- `ActivationEvent.Depth` 控制级联深度（`AsyncLocal` 按任务隔离，MCM 可调默认 5）
- 支持事件类型：`LetterReceived`（来信）、`BehaviorCheckIn`（签到）、`KingDiplomacy`（国王内外政务）、`PlanCheckIn`（计划）、`YearlyChronicle`/`SpecialChronicle`（史官）、`SelfReview`（封臣自省）、`EnvoyReceived`（私有密使，立即激活对方一次）、`FiefReview`（封地审视，被夺方激活触发内政矛盾）
- **检查站冷却**：签到类激活（BehaviorCheckIn/PlanCheckIn）每 agent 至少间隔 **15 真实分钟**（`PartyBehaviorManager._lastCheckInByAgent`，用真实时间而非游戏时间——游戏时间加速时游戏小时冷却无效）。防止「move/wait 到达→立刻签到→再发指令」的 token 死循环
- **卡死保险（持久行为）**：驻防/巡逻/护送（`CheckInHours > 0`）若下发后长时间**未到达目标点**（被拦截/目标遥不可及/巡逻绕圈不进 5 单位判定圈），签到永不触发、`PendingAction` 永不移除、mod 每帧重发覆盖原版、agent 永不激活 → 静默卡死。兜底：`PendingAction.CreatedAt` 记录下发时间，超时（`2× 签到周期`，驻防 6 天/巡逻 4 天/护送 2 天）且仍未到达 → 强制触发一次 BehaviorCheckIn（提示"你一直未能到达 X，是否放弃"）并释放 PendingAction，部队回归原版 AI。正常到达时此分支永不触发，零额外成本
- 被俘/逃亡的国王统治者现在也会被激活（仅跳过已死亡和 null 的），`BuildSelfStatus` 中会提示"你仍是王国统治者"
- 玩家可见：左下角弹 `xxx 给 xxx 写了一封信` / `xxx 正在思考下一步行动...` / `xxx 正在处理内外政务...` / `xxx 发现自己被夺封了...` / `xxx 正在独自思量自己的处境...` / `xxx 遣密使来见 yyy，yyy 正在考虑如何回应...`
- 防递归：书信规则强调"除非必要不回信" + 深度硬上限
- 聊天记录使用显式路径（`GetChatLogPathFor`）防线程竞态
- **信件记忆连续性**：信件处理（`ProcessEvent`）会先 `LoadChatLogFor` 注入双方此前聊天记录，再追加信件内容——对方能记得过去见过面/聊过什么（原实现只给信件文本，导致跨信"不认得你"）
- 外交提案感知：`LetterReceived` 处理时自动检测双方是否有待处理的外交提案（`AgentManager.GetProposalsBetween`），如有则将提案摘要注入上下文提示 Agent

## 2. 封臣自省机制（由谏言泛化）

封臣自省是"流式、单次激活"的内部政治压力系统，替代了早期的同步"封臣大会"（当时因 48 人批量 LLM 同步激活会卡死事件队列而废弃）。**该限制已随 v1.2.0 并发架构解除**：现在批量后台事件只是排队按 `MaxAgentConcurrency`（默认 5）并发槽位消化；新增后台 Agent 事件（记忆巩固、请封、夺权等）可直接排入 AgentScheduler，用优先级和 token 预算约束频度。

**v2.3.0 泛化**：激活就是激活，激活后可做写信之外的任何事，**谏言只是其中一个选项**——封臣不再围着国王转，而是围着自己的处境转。选择由"权重抽签"改为**公平轮转**（距上次自省最久者优先，`_lastSelfReviewDay` 按游戏日记），选池放宽为**封臣 + 雇佣兵 + 独立氏族领袖**：

```
Tick 无事件时 → CheckSelfReviewActivations()
    ↓
每王国每天 10% 概率（MCM AdvisoryProbability 可调）+ 独立领袖各自半概率
    ↓
SelectSelfReviewLeader：公平轮转（距上次自省最久优先）
  排除：俘虏、逃亡、国王本人、玩家
  独立领袖：Kingdom==null 且非 Minor/Bandit 的氏族领袖（IndependentClanLeaders）
    ↓
QueueSelfReview（入队 P4，由有限并行槽位调度处理，最低优先）
    → intent="self_review"：一次激活 = 一件自包含的事
    → 派密使 / 进谏(submit_advisory/submit_secret_advisory) / 整备 / 移防 / 处置战俘
      / 思量立场(change_kingdom 叛变/叛逃、create_kingdom 自立) / record_resolve 记决心 / 按兵不动
```

要点：
- **一次性行动原则**：命令一经发出立即执行完毕，无续接激活。工具级排除（`ContextBuilder.GetExcludedToolsForIntent("self_review")`，API tools 与提示词索引共用）：招兵/买粮/升级（链式"先到定居点再激活"）、`send_letter`（级联）、`wait_at_settlement`、`query_available_troops`/`query_settlement_villages`、`update_knowledge`/`let_go`/`query_pending_proposals`、`give_item`/`request_items`、`escort_party`、`browse_tools`（固定菜单，不按需解锁分类）
- **自省先读日记**：`self_review_rules.txt` 要求先 `read_file decisions/diary.txt` 回顾没有「结果」标记的计策/承诺/计划（仍视为进行中），`chat_logs/` 比日记新则以聊天为准；`MemoryConsolidator.EnsureDiaryCurrentAsync` 在自省前静默补记（SelfReview 已列入自我审视类）
- **自省摘要**：`BuildSelfReviewDigest`（零 token 代码组装，注入【当前状况】易变块）——封地/部队规模/君主关系/王国战和/未读密使线程。**只提供事实摘要，不注入日记内容**（克制原则：能提醒就别注入，日记由 agent 自己 `read_file`）
- `submit_advisory` 是 agent_tools.json 里的专用工具，归档格式由代码控制，agent 只需填内容
- 私人笔记 `decisions/personal_notes.txt` 非强制；若 LLM 写成了别的文件名，旧 `ProcessAdvisory` 的强制合并归位逻辑已随 v2.3.0 移除（自省不再强制进谏）
- 国王外交激活（`KingDiplomacy`）的提示词自动注入"先 read_file World/advisory/ 了解封臣谏言"，但国王决策权不受限
- **国王↔封臣闭环（诏令）**：国王政务审视时可颁布公开诏令（`submit_edict`，归档 `World/edict/{王国}_{年}.txt`，仅王国统治者可用、非国王被拒）；封臣自省进谏前先读国王诏令（`self_review_rules.txt` + `advisory_rules.txt`），若国王垂询某事应在谏言中回应；诏令读取走 `IsPublicDocAllowed`（史官任何国家、其他 agent 仅本国），玩家 H 键可见本国诏令
- **国王外交问询（跨国王互通）**：国王可用 `consult_king` 遣使问询他国国王（`KingConsult` 事件 P1，落盘 `World/diplomacy/consults/{A}_and_{B}.txt`），对方以 `reply_consult` 答复，问询方下次政务激活拉取看到。**严格单向防环**：问询会话（intent=`"king_consult"`）中 `consult_king` 被 BuildTools 排除，链深恒 1。史官可读任何国家问询线程（`IsConsultAllowed`），参与双方国王可读，第三方不可读。每王国对 7 游戏天冷却（`TryConsult`/`RecordConsult`）
- **私有密使（封臣互通，不入史册）**：任何家族领袖可用 `send_envoy`/`reply_envoy`（`EnvoyReceived` 事件 P2，落盘 `World/correspondence/{idA}_and_{idB}.txt`，**史官与第三方不可读**——`IsCorrespondenceAllowed` 仅参与者实体 ID 匹配；`__historian__` 天然无权）。立即激活对方一次回应（`envoy_reply` 会话中 `send_envoy` 被 BuildTools 排除，单跳防环），回复不激活任何人、对方下次自省时读到；每实体对 7 游戏天冷却（`TryEnvoy`/`RecordEnvoy`）；收信方在押/逃亡时线程在其下次自省摘要浮现。**玩家 O 键面板**：联系人 📨 标记 + 未读角标，密使往来窗口（intent=`"envoy"`）展示线程并回复（写入线程、不惊动对方），书信窗口亦只读展示密使记录
- 事件队列积压 >3 时暂停生成新的国王外交/封臣自省，先消化积压（Tick 中 `PendingEventCount() <= 3` 门槛）
- **token 截断重试**：`SendMessage` 捕获 `finish_reason`（`"length"`=被 `max_tokens` 截断）。自省/史官若被截断且无输出 → 自动重试一次（更坚决的提示直接行动）；主动沉默（`finish_reason="stop"`）不重试。MCM「最大 Token 数」上限 65536、默认 32768
- 历史（H 键）可读本国公开谏言；史官 `_readableWorldDirs` 含 `"advisory"` 可读取
- **玩家谏言**：秘书处（M 键）的 chancery 提示词引导使用 `submit_advisory`（玩家封臣/国王均可，雇佣兵被工具拒绝）——玩家谏言与 AI 谏言同一归档，可被史官写入编年史
- **史官联动**：`historian_rules.txt` 和 `yearly_chronicle_prompt.txt` 引导史官可选读 `advisory/` 作为补充视角（补充事实背后的观点和史料未载细节）；原始史料仍为权威，引用须注明"某封臣当时的谏言"
- **秘密谏言（不入史册）**：`submit_secret_advisory` 密陈给国王，写 `World/secret_advisory/{王国}_{年}.txt`，**史官无权读取**（`IsSecretAdvisoryAllowed` 仅本国王可读本国密陈）。公开谏言进史、密陈只呈国王，封臣可公开一套、私下另一套。提示词按克制原则模糊化：「你认为不适合被历史记录或旁人知晓的事」

## 3. 内政审视与封地政治（配套制度）

国王外交审视升级为**内外政务**——先内政后外交，内政缺地会自然驱动战争（内部驱动外部）：

- **ContextBuilder**：`intent="diplomacy"` 且为统治者时，自动注入 `BuildCourtReport`（内政审视报告：封地账本 + 治理[`Town.Prosperity`/`Town.Loyalty`] + 近期战功）
- **HistoryRecorder**：`RecordMerit` 写 `World/court/{王国}_merit.txt`（围攻/攻克/失利，真实事件记录），供内政审视读取
- **diplomacy_rules.txt**：明示国王内外政务、赐地/夺封职权、夺封须师出有名（`gift_fief` 可附 `reason`）
- **被夺方激活（FiefReview）**：`DiplomacyService.ExecuteTransferFief` 转让封地后，若原主（非国王本人）被夺封 → `AgentScheduler.QueueFiefReview` 激活原主审视处境（可写信/上表/转投他国，`intent="fief_review"` 分类含 diplomacy）。矛盾来自「失去的人」，得利方不激活
- **攻城后定归属（册封由 Agent 主导）**：`FiefAssignmentPatch.cs` 拦截原版攻城后的 `SettlementClaimantDecision` 投票（Prefix 拦 `DailyTickSettlement`）；Postfix 在 `OnSettlementOwnerChanged`（openToClaim、多家族王国）取消 unassigned 标记（攻城后默认归国王氏族，防忠诚惩罚）并激活国王 Agent（P1 级 KingDiplomacy，带归属指示）。不区分攻城者是玩家或 AI——统一国王决定；国王是玩家时由玩家经秘书处处理。**手动注册**（OnGameStart Type.GetType + harmony.Patch，CampaignBehaviors 类 PatchAll 会静默跳过）。MCM「册封由 Agent 主导」
- **军情迷雾**：`query_party_troops` 自己/同阵营全量精确；异国按距离与可达性分近距/远距/传闻三档（`GetIntelRadii` 按地图尺度相对锚），跨海不可达降为传闻——打破「完美信息→和平均衡」

## 4. 盟约/贸易协定到期记录（轻量拉取式，无 LLM）

盟约（84 天）与贸易协定（1 年）由原版定时到期。模组接管外交后**不主动激活 Agent 处理到期**（避免 token 消耗），改为拉取式记录，国王下次激活时自行看到：

> **盟约"号召盟友宣战"投票已拦截**：`SubModule.OnGameStart` 手动 patch `ProposeCallToWarAgreementDecision`/`AcceptCallToWarAgreementDecision` 的 `IsAllowed()`（Prefix 强制返回 false，`ShouldBeCancelled()` 在投票触发前将其取消；Election 类 PatchAll 会静默跳过，须手动注册）；并 patch `LordConversationsCampaignBehavior.conversation_player_wants_to_sponsor_call_to_war_on_condition` 隐藏玩家对话里的原版"号召盟友"选项（否则玩家会花影响力/金币却无声失效）。军事同盟只保留名义作用——是否号召盟友/宣战由国王 Agent 激活时自行决定。受 MCM「禁止原版外交」开关控制。

- **记录**：`AgentScheduler.Tick` 每游戏日一次调 `DiplomacyService.CheckExpiringAgreements()`（无 LLM）——扫描剩余不足 1 天的盟约/贸易协定，写入 `World/diplomacy/expiry_log.txt`（行格式 `类型|王国1ID|王国2ID|到期日day|人类可读文本`，每对王国+类型一条，超 90 游戏天清除防堆积）
- **自然到期入史（与背约区分）**：盟约/贸易协定的**自然到期**会写进原始史料（`alliance_expired`/`trade_expired`，如"X与Y的盟约于第1089年夏第12日期满而罢"），与单方背约（`alliance_broken`/`trade_broken`，"X单方面终止了与Y的盟约"）明确区分——期满而罢是"约期已尽"，背约是"单方毁约"，叙事价值完全不同。检测用观察集文件 `World/diplomacy/agreements_tracked.txt`（`类型|王国1ID|王国2ID|到期日day`）记录生效协定的到期日：到期当天协定被惰性清理（`HasTradeAgreement`/`IsAllyWithKingdom` 查询即删）时比对观察集判定自然到期，记一次即移除；到期前消失的协定静默移除不误报
- **查看**：`query_world_state` 输出各王国名下附带「📜 盟约 X与Y 于…到期」；到期前不记录不提示，国王不查就不知道
- **防矛盾**：`DiplomacyService.ClearAgreementTracking` 在协约**重新建立**（对方接受提案 `ExecuteRespondToProposal`）、**主动背约**（`ExecuteEndAlliance`/`ExecuteEndTradeAgreement`）、**战争毁约**（`HistoryRecorder.OnWarDeclared`，两国开战原版自动终止协定）与**灭国**（`HistoryRecorder.OnKingdomDestroyed`）时清除对应到期日志与观察项，防止国王再次激活时看到失效的「到期」信息，也防止观察集把"战争毁约/背约"误判为"期满而罢"。key 排序规则（王国 ID 字典序）与写入端一致；旧存档无记录时调用为安全 no-op
- **续约**：不新增续约工具；国王若想续约，走现有 `propose_alliance`/`propose_trade` 流程

## 5. 历史系统（HistoryRecorder + 史官 Agent）

历史系统由两部分组成：**事件记录器** 和 **史官 Agent**。

### 事件记录器（HistoryRecorder）

`HistoryRecorder.cs` 是一个 `CampaignBehavior`，在 `OnGameStart` 中注册。它监听以下 `CampaignEvents`：

| 事件 | 游戏钩子 | 史料 type |
|------|---------|-----------|
| 宣战 | `WarDeclared`（含宣战宣言 PendingWarDeclaration） | `war_declared` |
| 议和 | `MakePeace` | `peace_made` |
| 围城开始/失败/放弃 | `OnSiegeEventStartedEvent` / `SiegeCompletedEvent`（败） / `OnSiegeEventEndedEvent` | `siege_started` / `siege_failed` / `siege_abandoned` |
| 野战/解围野战 | `MapEventEnded`（EventType=FieldBattle/SiegeOutside，**双方总兵力≥600 才入史**，附兵力/胜负/损失） | `battle_fought` |
| 城镇/城堡易主 | `OnSettlementOwnerChangedEvent`（过滤 IsTown/IsCastle） | `settlement_captured` |
| 国王册封/转让封地 | `OnSettlementOwnerChangedEvent`（detail=ByKingDecision/ByGift，含册封宣言 PendingFiefGrantText）。**去重顺序**：国王册封分支先于攻城去重标记判断（`_recentSiegeCaptures`）——否则攻城后国王用 `gift_fief` 重新册封时，去重标记残留会把 `fief_granted` 吞掉，史官误以为封地直接归统治者 | `fief_granted` |
| 王国灭亡 | `KingdomDestroyedEvent` | `kingdom_destroyed` |
| 新王国建立 | 无独立事件，从 `OnClanChangedKingdomEvent` 的 `CreateKingdom` 详情补记 | `kingdom_created` |
| 贵族死亡 | `HeroKilledEvent`（过滤有 clan 的）；身份用 `BeforeHeroKilledEvent` 在继任改选**之前**捕获（`_pendingHeroDeathTitle`，死亡时原始身份：统治者/族长/成员/冒险者——KillCharacterAction 触发 HeroKilled 前已完成族长/国王改选，实时判断会误判身份，导致关闭「所有贵族立传」时国王/族长不立传） | `hero_killed` |
| 氏族叛变 | `OnClanChangedKingdomEvent` | `clan_changed_kingdom` |
| 氏族领袖更替 | `OnClanLeaderChangedEvent` | `clan_leader_changed` |
| 贵族婚嫁 | `OnMarriageOfferedToPlayerEvent`（直接注册） | `marriage` |
| 天意建族 | 静态入口 `HistoryRecorder.RecordClanCreated`（create_clan 调用） | `clan_created` |
| 结盟/背盟/盟约期满 | `DiplomacyService` 接受 alliance 提案 / `ExecuteEndAlliance` / 每日到期检测 | `alliance_made` / `alliance_broken` / `alliance_expired` |
| 贸易协定/终止/期满 | `DiplomacyService` 接受 trade 提案 / `ExecuteEndTradeAgreement` / 每日到期检测 | `trade_made` / `trade_broken` / `trade_expired` |

每条事件以 JSONL 格式追加到 `World/history/events_{year}.txt`：
```json
{"year":1084,"season":"春","day":12,"type":"war_declared","summary":"瓦兰迪亚向库赛特宣战"}
```

### 史官 Agent

- **触发时机**：每年年终（年份推进时），`AgentScheduler.CheckYearAdvance()` 检测年份变化并队列 `YearlyChronicle` 事件
- **专题触发**：灭国/新王国建立时，`HistoryRecorder` 调用 `AgentScheduler.QueueSpecialChronicle()` 即时队列 `SpecialChronicle` 事件
- **专题合并（防杀人潮）**：`QueueSpecialChronicle` 会先写入合并缓冲——若已有一个待处理专题史事件，后续事件只追加不新开；处理时一次史官激活合并全部（如玩家连杀十几人 → 只生成一次传记专题，而非连环激活）
- **传记质量**：`query_character` 现可查已故人物（枚举所有氏族成员含已故 + 在世英雄）并返回出生/卒年；传记提示词要求开头点明身份（统治者/族长/成员）与生卒年。**立传身份在死亡前一刻捕获**（`BeforeHeroKilledEvent` 存 `_pendingHeroDeathTitle`，先于原版继任改选），关闭「所有贵族立传」后国王/族长仍立传。成功判定改为"chronicles 目录出现新文件"（传记/世家/纪事是自命名体例文件，原只查 `chronicle_*.txt` 会误报"未生成"）
- **Entity**：史官是虚拟 Entity（ID: `__historian__`，`HeroRef = null`），不映射任何游戏 NPC
- **工具**：`query` + `file` 分类的工具（`ActivatedCategories = {"universal", "query", "file"}`），含专属落盘工具 `write_chronicle`（`Chronicler` 能力门控，仅史官可用）
- **权限**：可读 `World/history/` 目录，可写 `World/history/chronicles/` 目录
- **提示词**：使用 `intent = "historian"` 的 `ContextBuilder.Build()`，规则来自 `historian_rules.txt`
- **体例规范（史书命名，代码强制）**：史文统一按五种体例落盘，文件名由系统自动生成（`AgentManager.ExecuteWriteChronicle` 校验体例白名单 + 清洗名称 + 拼 `{名称}{体例}.txt`）——**本纪**（`{国王名}本纪`，一国之君生平与在位大事）、**世家**（`{国名}世家`，一国/大族兴衰史）、**列传**（`{人名}列传`，单个人物生平）、**编年史**（`{年份}编年史`，年度大事记）、**纪事**（`{事件名}纪事`，重大事件始末）。史官不得再用 `write_file` 自行命名（提示词已改指 `write_chronicle`）
- **体例建议（代码给建议，史官可调整）**：`ConsumeSpecialChronicleContent` 按事件性质注入体例建议——「重要人物之死」中死者为统治者→本纪、其余→列传；「王国灭亡：X」→世家；其他→纪事。判定标准同时写入 `historian_rules.txt` 的「史书体例」表
- **输出**：年度编年史 → `World/history/chronicles/{year}编年史.txt`；传记/世家/纪事 → `{名称}{体例}.txt`

### NPC 查阅历史

- `AgentManager.IsPathAllowed` 和 `ResolvePath` 新增了对 `history/` 和 `history/chronicles/` 路径的支持
- NPC Agent 可用 `read_file("history/chronicles/{year}编年史.txt")` 直接读取史官成文
- 原始史料（`events_*.txt`）对 NPC 只读
- 写入历史目录的权限保留给 `__historian__` entity

### 年份检测

- `AgentScheduler` 用 `_lastChronicleYear` 追踪上次处理年份，初始值 = 游戏起始年份
- 每帧 `Tick()` 中调用 `CheckYearAdvance()`，检测 `currentYear > _lastChronicleYear`
- 对每个已跳过的年份，检查 `events_{year}.txt` 是否存在且 `{year}编年史.txt` 不存在，满足条件才队列事件
- 防止重复生成：已存在编年史的年份跳过
