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
| 0 | YearlyChronicle / SpecialChronicle（史官）/ CuriaVote / CuriaDefense（朝议投票/陈情） | 最高；史官永不跳过；朝议有截止日，优先于随机政务 |
| 1 | KingDiplomacy（国王内外政务/提案） | 高优先，外交提案不积压；议程期间该国随机政务/自省暂停 |
| 2 | LetterReceived / EnvoyReceived（来信/密使） | 中 |
| 3 | BehaviorCheckIn / PlanCheckIn / FiefReview（签到/封地审视） | 低 |
| 4 | SelfReview（概率激活的自省） | 最低，只在无更高优先工作时处理 |

- 并发安全：每任务上下文（`CurrentHero`/agent/深度等）经 `AsyncLocal` 隔离，工具仍统一回主线程串行执行
- `ActivationEvent.Depth` 控制级联深度（`AsyncLocal` 按任务隔离，MCM 可调默认 5）
- 支持事件类型：`LetterReceived`（来信）、`BehaviorCheckIn`（签到）、`KingDiplomacy`（国王内外政务）、`PlanCheckIn`（计划）、`YearlyChronicle`/`SpecialChronicle`（史官）、`SelfReview`（封臣自省）、`EnvoyReceived`（私有密使，立即激活对方一次）、`FiefReview`（封地审视，被夺方激活触发内政矛盾）、`CuriaVote`（朝议应召投票）、`CuriaDefense`（朝议被针对家族陈情）
- **检查站冷却**：签到类激活（BehaviorCheckIn/PlanCheckIn）每 agent 至少间隔 **15 真实分钟**（`PartyBehaviorManager._lastCheckInByAgent`，用真实时间而非游戏时间——游戏时间加速时游戏小时冷却无效）。防止「move/wait 到达→立刻签到→再发指令」的 token 死循环
- **卡死保险（持久行为）**：驻防/巡逻/护送（`CheckInHours > 0`）若下发后长时间**未到达目标点**（被拦截/目标遥不可及/巡逻绕圈不进 5 单位判定圈），签到永不触发、`PendingAction` 永不移除、mod 每帧重发覆盖原版、agent 永不激活 → 静默卡死。兜底：`PendingAction.CreatedAt` 记录下发时间，超时（`2× 签到周期`，驻防 6 天/巡逻 4 天/护送 2 天）且仍未到达 → 强制触发一次 BehaviorCheckIn（提示"你一直未能到达 X，是否放弃"）并释放 PendingAction，部队回归原版 AI。正常到达时此分支永不触发，零额外成本
- 被俘/逃亡的国王统治者仍会被激活处理政务/外交（仅跳过已死亡和 null 的），`BuildSelfStatus` 中会提示「你仍是王国统治者」；但朝议中没有国王席位
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
    → 派密使 / 提议朝议(propose_agenda) / 密陈(submit_secret_advisory) / 整备 / 移防 / 处置战俘
      / 思量立场(change_kingdom 叛变/叛逃、create_kingdom 自立) / record_resolve 记决心 / 按兵不动
```

要点：
- **一次性行动原则**：命令一经发出立即执行完毕，无续接激活。工具级排除（`ToolCatalog` 的 self_review 策略，API tools 与提示词索引共用）：招兵/买粮/升级（链式"先到定居点再激活"）、`send_letter`（级联）、`wait_at_settlement`、`query_available_troops`/`query_settlement_villages`、`update_knowledge`/`let_go`/`query_pending_proposals`、`give_item`/`request_items`、`escort_party`、`browse_tools`（固定菜单，不按需解锁分类）
- **自省先读日记**：`self_review_rules.txt` 要求先 `read_file decisions/diary.txt` 回顾没有「结果」标记的计策/承诺/计划（仍视为进行中），`chat_logs/` 比日记新则以聊天为准；`MemoryConsolidator.EnsureDiaryCurrentAsync` 在自省前静默补记（SelfReview 已列入自我审视类）
- **自省摘要**：`BuildSelfReviewDigest`（零 token 代码组装，注入【当前状况】易变块）——封地/部队规模/君主关系/王国战和/未读密使线程。**只提供事实摘要，不注入日记内容**（克制原则：能提醒就别注入，日记由 agent 自己 `read_file`）
- `propose_agenda` 是朝议提议工具（公开理由+入档/入史）；`submit_secret_advisory` 是密陈工具（只呈国王、不入史册）
- 私人笔记 `decisions/personal_notes.txt` 非强制；若 LLM 写成了别的文件名，旧 `ProcessAdvisory` 的强制合并归位逻辑已随 v2.3.0 移除（自省不再强制进谏）
- 国王外交激活（`KingDiplomacy`）的提示词改为"先处理外国提案、再看本国朝议"；国王保留提议/投票权，但不能绕过朝议独断
- **国王↔封臣闭环（朝议）**：国王/封臣都可提朝议议题（`propose_agenda`）；被召入议程的封臣在单独 `curia` 激活中投票/陈情；结果写公开档案与日记。公开谏言/诏令已移除，密陈保留
- **国王外交问询（跨国王互通）**：国王可用 `consult_king` 遣使问询他国国王（`KingConsult` 事件 P1，落盘 `World/diplomacy/consults/{A}_and_{B}.txt`），对方以 `reply_consult` 答复，问询方下次政务激活拉取看到。**严格单向防环**：问询会话（intent=`"king_consult"`）中 `consult_king` 被 BuildTools 排除，链深恒 1。史官可读任何国家问询线程（`IsConsultAllowed`），参与双方国王可读，第三方不可读。每王国对 7 游戏天冷却（`TryConsult`/`RecordConsult`）
- **私有密使（封臣互通，不入史册）**：任何家族领袖可用 `send_envoy`/`reply_envoy`（`EnvoyReceived` 事件 P2，落盘 `World/correspondence/{idA}_and_{idB}.txt`，**史官与第三方不可读**——`IsCorrespondenceAllowed` 仅参与者实体 ID 匹配；`__historian__` 天然无权）。立即激活对方一次回应（`envoy_reply` 会话中 `send_envoy` 被 BuildTools 排除，单跳防环），回复不激活任何人、对方下次自省时读到；每实体对 7 游戏天冷却（`TryEnvoy`/`RecordEnvoy`）；收信方在押/逃亡时线程在其下次自省摘要浮现。**玩家 O 键面板**：联系人 📨 标记 + 未读角标，密使往来窗口（intent=`"envoy"`）展示线程并回复（写入线程、不惊动对方），书信窗口亦只读展示密使记录
- 事件队列积压 >3 时暂停生成新的国王外交/封臣自省，先消化积压（Tick 中 `PendingEventCount() <= 3` 门槛）
- **token 截断重试**：`SendMessage` 捕获 `finish_reason`（`"length"`=被 `max_tokens` 截断）。自省/史官若被截断且无输出 → 自动重试一次（更坚决的提示直接行动）；主动沉默（`finish_reason="stop"`）不重试。MCM「最大 Token 数」上限 65536、默认 32768
- 朝议（M 键）可读本国 `World/curia/`；史官 `IsCuriaAllowed` 可读所有国家，其他 agent 仅本国
- **玩家参政**：M 键参政页（CEF）投票/提议/陈情；秘书处只查询。玩家与 AI 共用同一朝议档案，可被史官写入编年史
- **史官联动**：`historian_rules.txt` 引导史官读 `World/curia/` 的公开朝议档案（提议/陈情/票数/理由）；原始史料仍为权威，引用须注明“某次朝议”
- **秘密谏言（不入史册）**：`submit_secret_advisory` 密陈给国王，写 `World/secret_advisory/{王国}_{年}.txt`，**史官无权读取**（`IsSecretAdvisoryAllowed` 仅本国王可读本国密陈）。朝议进史、密陈只呈国王，封臣可公开投票、私下密陈。提示词按克制原则模糊化：「你认为不适合被历史记录或旁人知晓的事」

## 3. 内政审视与封地政治（配套制度）

国王外交审视升级为**内外政务**——先内政后外交，内政缺地会自然驱动战争（内部驱动外部）：

- **ContextBuilder**：`intent="diplomacy"` 且为统治者时，自动注入 `BuildCourtReport`（内政审视报告：封地账本 + 治理[`Town.Prosperity`/`Town.Loyalty`] + 近期战功）
- **HistoryRecorder**：`RecordMerit` 写 `World/court/{王国}_merit.txt`（围攻/攻克/失利，真实事件记录），供内政审视读取
- **diplomacy_rules.txt**：明示国王内外政务、赐地/夺封职权、夺封须师出有名（`gift_fief` 可附 `reason`）
- **被夺方激活（FiefReview）**：`DiplomacyService.ExecuteTransferFief` 转让封地后，若原主（非国王本人）被夺封 → `AgentScheduler.QueueFiefReview` 激活原主审视处境（可写信/上表/转投他国，`intent="fief_review"` 分类含 diplomacy）。矛盾来自「失去的人」，得利方不激活
- **攻城后定归属（册封由 Agent 主导）**：`FiefAssignmentPatch.cs` 拦截原版攻城后的 `SettlementClaimantDecision` 投票（Prefix 拦 `DailyTickSettlement`）；Postfix 在 `OnSettlementOwnerChanged`（openToClaim、多家族王国）取消 unassigned 标记（攻城后默认归国王氏族，防忠诚惩罚）并激活国王 Agent（P1 级 KingDiplomacy，带归属指示）。不区分攻城者是玩家或 AI——统一国王决定；国王是玩家时收到左下角提示，经 M 键参政页「封地」自行分封。条约割地也走同一 `QueueKingFiefDecision` 分封入口。**手动注册**（OnGameStart Type.GetType + harmony.Patch，CampaignBehaviors 类 PatchAll 会静默跳过）。MCM「册封由 Agent 主导」
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
| 新王国建立 | 从 `OnClanChangedKingdomEvent` 的 `CreateKingdom` 详情记录，不重复订阅原版 `KingdomCreatedEvent` | `kingdom_created` |
| 贵族死亡 | `HeroKilledEvent`（过滤有 clan 的）；身份用 `BeforeHeroKilledEvent` 在继任改选**之前**捕获（`_pendingHeroDeathTitle`，死亡时原始身份：统治者/族长/成员/冒险者——KillCharacterAction 触发 HeroKilled 前已完成族长/国王改选，实时判断会误判身份，导致关闭「所有贵族立传」时国王/族长不立传） | `hero_killed` |
| 氏族换国（入国/叛逃/退国等） | `OnClanChangedKingdomEvent` | `clan_changed_kingdom` |
| 氏族领袖更替 | `OnClanLeaderChangedEvent` | `clan_leader_changed` |
| 贵族婚嫁 | `BeforeHeroesMarried` 快照 + `RomanticStateChanged(Marriage)` 生效验证；原版/会晤统一，求婚不入史 | `marriage` |
| 正式君主更替 | `RulingClanChanged` / 王室 `OnClanLeaderChangedEvent` / 选王 `KingdomDecisionConcluded` 与 `KingdomDecisionCancelled`；延迟验证无未决选王，排除临时统治者，新国首任沿建国史料 | `ruler_changed` |
| 君主/族长被俘、获释 | `HeroPrisonerTaken` / `HeroPrisonerReleased`；普通贵族仅日记，死亡不写成释放，不猜付款人/营救者 | `hero_captured` / `hero_released` |
| 城镇叛乱/立足/覆亡 | `RebellionFinished` 实为起事；`RebelliousClanDisbandedAtSettlement` 实为存续转正；叛军 `OnClanDestroyedEvent` 延迟确认覆亡 | `rebellion_started` / `rebellion_established` / `rebellion_failed` |
| 实际村庄劫掠 | `VillageBeingRaided` / `VillageLooted` / `RaidCompletedEvent`，区分开始、洗劫与未洗劫一空的结束 | `village_raided` / `village_looted` / `raid_ended` |
| 攻城后处置 | `OnSiegeAftermathAppliedEvent`；只记已应用的宽赦/洗劫/毁坏，不编造屠杀或伤亡 | `siege_aftermath` |
| 天意建族 | 静态入口 `HistoryRecorder.RecordClanCreated`（create_clan 调用） | `clan_created` |
| 结盟/背盟/盟约期满 | `DiplomacyService` 接受 alliance 提案 / `ExecuteEndAlliance` / 每日到期检测 | `alliance_made` / `alliance_broken` / `alliance_expired` |
| 贸易协定/终止/期满 | `DiplomacyService` 接受 trade 提案 / `ExecuteEndTradeAgreement` / 每日到期检测 | `trade_made` / `trade_broken` / `trade_expired` |
| 条约提出/签署/被拒/撕毁/割地/失败 | `DiplomacyService.ExecuteProposeTreaty` / `TreatyService` / `HistoryRecorder.OnWarDeclared` | `treaty_proposed` / `treaty_signed` / `treaty_rejected` / `treaty_broken` / `treaty_cession` / `treaty_failed` |

`WorldHistoryTests.cs` / `WorldHistoryStubs.cs` 直接链接 `HistoryRecorder.World.cs` / `HistoryRecorder.Warfare.cs`，用最小只读状态和原版签名事件检查正式选王/婚姻生效/拘押原因/叛军时序/劫掠双钩与普通缓冲/三类攻城处置，以及日期、当时收件人、开关、加载和 epoch；不模拟游戏执行算法。

每条事件以 JSONL 格式追加到 `World/history/events_{year}.txt`：

```json
{"year":1084,"season":"春","day":12,"type":"war_declared","summary":"瓦兰迪亚向库赛特宣战"}
```

### 战役主线（HistoryRecorder.Story.cs）

仅在 `StoryModeManager.Current` 存在时注册，复用同一历史记录器与 JSONL 格式，不更改存档结构。只响应新发生的事件，不扫描旧进度补写；读档用原版进度建立去重基线。

| 史料 type | 原版来源与边界 | 君主日记 |
|-----------|----------------|----------|
| `story_banner_investigated` | `BannerInvestigationQuest` 成功：查明涅雷采斯之愚及龙旗来历 | 否 |
| `dragon_banner_assembled` | `OnBannerPieceCollectedEvent` 集齐三片后，延迟到同轮监听结束，确认完整龙旗实物已重组；不等 `AssembleTheBannerQuest` 最终完成 | 是 |
| `dragon_banner_claimed` | `OnMainStoryLineSideChosenEvent`：自立/支持现国、复兴/终结帝国四路线，记录奉旗人、王国、当时统治者与公开主张 | 是 |
| `story_empire_progress` | `AssembleEmpireQuest` / `WeakenEmpireQuest` 成功：约三分之二帝国文化城镇/帝国三国城镇合计不足四座；这是阶段成果，不是最终胜利 | 否 |
| `story_conspiracy_war` | `OnConspiracyActivatedEvent`，同轮监听结束后读取已初始化的原版敌国名单；不公开幕后私人阴谋或计量数值 | 是 |
| `story_kingdom_defeated` | `DefeatTheConspiracyQuest` 成功；只读 `_oppositionKingdom` 确定敌国，不解析本地化标题。拒绝求和也只记被击败，不假称议和/灭国 | 否 |
| `story_victory` / `story_defeat` | 最终敌国名单清空且奉旗王国存续/奉旗王国灭亡导致失败；`KingdomDestroyedEvent` 兜底覆盖灭国先毁族退国、全部目标先取消而不再发出失败事件的顺序。此前已经退出的人不追记后来的败亡，多国结算共享终局去重键。`ThirdPhase.IsCompleted` 可在首国败服时提前置位，不用于终局判定 | 是 |
| `story_banner_search_ended` | 调查、组旗、自立/支持目标失败/超时且尚未选定路线；同一轮失败合并。取消备选路线不记失败 | 否 |
| `story_withdrawn` | 统一/削弱/敌国目标因奉旗者离国取消，记录本人退出，不宣称整个王国败亡 | 否 |

重大事件只向**事件当时**所有未灭亡王国的在世 AI 统治者写「情报」，去重收件人并排除 Human。使用 `AppendSystemDiary` 立即追加，不唤醒、不弹窗、不写聊天、不代写态度；沿用静默见闻开关、不占普通每日额度。延迟验证沿 `MainThreadExecutor` 的 epoch，并保留事件日期/收件人/人物称呼快照。全部正文采用人物、家族、王国名称，不称“玩家”“主角”，不直接转录原版第二人称任务日志。零散碎片、导师会面、教程、普通支线与循环阴谋差事不单独收录。

主线回归 `StoryHistoryTests.cs` / `StoryHistoryStubs.cs` 直接链接生产 `HistoryRecorder.Story.cs`，事件沿原版后注册先调用的顺序，用最小对象桩检查事件时序、四路线、收件人快照、多国终局与灭国去重、离国后再读档，以及沙盒/切档边界；不模拟原版剧情或外交推进。

`historian_rules.txt` 与 `ContextBuilder.BuildHistorianRules` 的兜底说明同步主线 type：区分公开主张、阶段成果、单国败服与最终结局，不替史料补写议和、灭国、天下统一或他人态度。

### 史官 Agent

- **成文预算**：`HistorianWorkflow` 对史官 `read_file history/events_*.txt` 使用 `max(配置上限, 64000)` 字符额度（配置为 0 仍不限）；年度提示先不传 `line_count`，有截断才继续翻页，末行不完整须重读。普通人物不受此例外影响。12 轮中的最后两轮只发送 `write_chronicle` schema，执行端也拒绝继续读文件/查询，先完成本次任务所需篇目，年度任务优先完成本年正篇；不增加查询轮数。
- **失败续写与核对**：`length` / `round_budget` / `stop` 结束仍未落盘时，最多续写两轮，只开放写史册，保留原 system/任务/已返回的工具结果及调用配对，不套用聊天条数裁剪，不以无证据的空白提示重新开始。续写校验原 campaign epoch，切档即弃。年度只核对 `{year}编年史.txt` 非空正文实际新增/改变，专题核对非空 `.txt` 的新增/改变；其他文件、空白正文、仅时间戳变化均不算成功。轮次耗尽明确返回 `round_budget` 并记录失败原因。此机制不恢复切档/退出时丢失的排队任务，也不自动补写旧年。
- **史料格式**：`HistoryEventText.Serialize` 使用既有 Newtonsoft.Json 输出单行 JSON；正文的 CR/LF、引号与反斜杠均转义，读取后保留原义。既有原始文件不迁移、不重写。缺载季节只记阙载，不能推断太平。命名先去 `.txt` 再去体例，年度数字、`第1084年` 等规范成 `1084编年史.txt`。纯逻辑回归见 `HistorianTests.cs`。

- **触发时机**：每年年终（年份推进时），`AgentScheduler.CheckYearAdvance()` 检测年份变化并队列 `YearlyChronicle` 事件
- **专题触发**：灭国/新王国建立时，`HistoryRecorder` 调用 `AgentScheduler.QueueSpecialChronicle()` 即时队列 `SpecialChronicle` 事件
- **专题合并（防杀人潮）**：`QueueSpecialChronicle` 会先写入合并缓冲——若已有一个待处理专题史事件，后续事件只追加不新开；处理时一次史官激活合并全部（如玩家连杀十几人 → 只生成一次传记专题，而非连环激活）
- **传记质量**：`query_character` 现可查已故人物（枚举所有氏族成员含已故 + 在世英雄）并返回出生/卒年；传记提示词要求开头点明身份（统治者/族长/成员）与生卒年。**立传身份在死亡前一刻捕获**（`BeforeHeroKilledEvent` 存 `_pendingHeroDeathTitle`，先于原版继任改选），关闭「所有贵族立传」后国王/族长仍立传。专题史按目录中的非空 `.txt` 正文核对新增/改变，兼容传记/世家/纪事的自命名体例。
- **Entity**：史官是虚拟 Entity（ID: `__historian__`，`HeroRef = null`），不映射任何游戏 NPC
- **工具**：`ToolCatalog` 默认 `universal` + `query` + `file`，含专属落盘工具 `write_chronicle`（`Chronicler` 能力门控，仅史官可用）
- **正文格式**：史文正文用 Markdown 落盘——`##` 分节、`**` 强调、`>` 写「史官曰」，不重复篇名，不用 HTML/表格/图片/链接/代码块。阅读页按 Markdown 渲染；CEF 不可用时原生 fallback 由 `HistoryMarkdown` 去标记显示
- **权限**：可读 `World/history/` 目录，可写 `World/history/chronicles/` 目录
- **提示词**：使用 `intent = "historian"` 的 `ContextBuilder.Build()`，规则来自 `historian_rules.txt`
- **体例规范（史书命名，代码强制）**：史文统一按五种体例落盘，文件名由系统自动生成（`AgentManager.ExecuteWriteChronicle` 校验体例白名单 + 清洗名称 + 拼 `{名称}{体例}.txt`）——**本纪**（`{国王名}本纪`，一国之君生平与在位大事）、**世家**（`{国名}世家`，一国/大族兴衰史）、**列传**（`{人名}列传`，单个人物生平）、**编年史**（`{年份}编年史`，年度大事记）、**纪事**（`{事件名}纪事`，重大事件始末）。史官不得再用 `write_file` 自行命名（提示词已改指 `write_chronicle`）
- **体例建议（代码给建议，史官可调整）**：`ConsumeSpecialChronicleContent` 按事件性质注入体例建议——「重要人物之死」中死者为统治者→本纪、其余→列传；「王国灭亡：X」→世家；其他→纪事。判定标准同时写入 `historian_rules.txt` 的「史书体例」表
- **输出**：年度编年史 → `World/history/chronicles/{year}编年史.txt`；传记/世家/纪事 → `{名称}{体例}.txt`。文件内容为 Markdown；密陈（及旧档谏言/诏令）仍是实体角色扮演产出的纯文本
- **阅读入口**：原生 `HistoryScreenVM` 与 CEF `HistoryCefScreen` 共用 `HistoryArchive`（目录扫描/体例分组/权限过滤/正文读取），避免双入口各自实现一套逻辑

### NPC 查阅历史

- `AgentManager.IsPathAllowed` 和 `ResolvePath` 新增了对 `history/` 和 `history/chronicles/` 路径的支持
- NPC Agent 可用 `read_file("history/chronicles/{year}编年史.txt")` 直接读取史官成文
- 原始史料（`events_*.txt`）对 NPC 只读
- 写入历史目录的权限保留给 `__historian__` entity

### 静默见闻（Silent Diary）

- `HistoryRecorder.Allegiance.cs` 统一监听换国与佣兵效力开始/结束：族长本人及当时原国、新国的在世 AI 君主立即收到客观「情报」，覆盖入国/叛逃/退国/佣兵换主/同国转封臣；沿用静默开关、不占普通额度，不激活、不代写态度。按实际归属去重，同日往返分别记；佣兵结束的中间状态延至原版调用完成验证，保留当时收件人与日期，沿用 epoch。加载只采基线、不回填、不新增存档键；灭族离国跳过，灭国退国标明原因。
- `HistoryRecorder` / `ToolExecutor` / `ToolExecutor.Social` / `ToolExecutor.Military` 在事件钩子处投递 `SilentDiary`：战争/议和、封地易主（册封/攻取）、围城开始/解围/攻城失败、贵族死亡、大会战、赠礼、索取、下令劫掠
- 分派按**亲历 / 直接利害（氏族领袖）/ 体制（王国统治者）**三环；环越外文本越短；玩家按普通 Entity 处理，玩家自己的日记不写
- 以 `情报：…` 写入相关 Agent 的 `decisions/diary.txt`（零 LLM、不弹窗、不注入上下文）；按 (Agent, 事件日) 缓冲，激活前 / 跨日 / 退出时 flush；同日多条合并「今日见闻」
- 读取走 `read_diary`（默认最近 40 条，按类型过滤）；跨年自动分卷 `decisions/diary_{year}.txt`；`decisions/.diary_authored` 仅保留自写日期，巩固现以独立 `decisions/.memory_progress_v1.json` 的逐文件消息进度为准，系统代笔与普通写日记都不代表往来已巩固。
- MCM「启用静默见闻」「每日见闻上限」（默认开 / 3 条）；契约见 `docs/plan-silent-memory.md`

### 年份检测

- `AgentScheduler` 用 `_lastChronicleYear` 追踪上次处理年份，初始值 = 游戏起始年份
- 每帧 `Tick()` 中调用 `CheckYearAdvance()`，检测 `currentYear > _lastChronicleYear`
- 对每个已跳过的年份，检查 `events_{year}.txt` 是否存在且 `{year}编年史.txt` 不存在，满足条件才队列事件
- 防止重复生成：已存在编年史的年份跳过


## 5.1 成本预算与后台节流

- **每次激活**：`AIChatClient` 按 intent 限制 LLM 轮次和查询调用——朝议 3 轮/1 查询、自省 5/4、外交 6/4、国王问询 4/3、密使回应 4/3、签到（`chat`）10/6、面对面对话（`conversation`）6/4、史官 12/5。`ToolCatalog` 固定本次能力/场景工具与排列；预算耗尽或最后一轮只追加“停止查询、立即行动”提示，执行端拒绝超额 `query_*`/`browse_tools`，不删工具定义。显式浏览解锁是本轮工具表更新的唯一入口，浏览仅返回经过同一权限筛选的工具名/参数名，完整定义由下一轮 API tools 提供。
- **结果压缩**：单次工具结果超过 `MaxToolResultChars`（默认 4000）截断；`query_policies` 默认一行式摘要，指定 `policy_name` 才返回完整说明；`query_agenda` 默认简版，`detail=true` 才列全部票数与缺席。
- **Curia 预置事实**：投票提示词直接带目标战和/同盟/贸易、封地守军、家族影响力/封地、关系、当前票数/权重/门槛；最多允许 1 次补充查询。
- **全局预算**：`TokenBudget` 统计滚动 5 分钟后台总输入 token（独立于缓存统计）；`conversation` 与 `chancery` 不计入后台窗口。超过 MCM「后台 token 预算」时，调度器推迟可延迟的后台事件（自省/国王政务/问询/密使等），事件留在队列；朝议投票/史官因有截止或年度节点不推迟。端点未报告 usage 时记录未知，不伪造为 0 消耗。

## 6. 朝议（Curia）

> 设计契约见 `docs/plan-curia.md`；条约条款见 `docs/plan-treaty.md`。核心：**十类行动经王国公开表决；文本是理由，类型+参数是机制；通过执行、不通过无事发生。**

- **十类议题**：`declare_war` / `propose_peace`（可带割地/赔款/休战条款）/ `propose_treaty`（战前条约）/ `propose_trade` / `propose_alliance` / `break_alliance` / `enact_policy` / `revoke_policy` / `transfer_fief` / `expel_clan`。
- **选民**：所有封臣家族领袖（含无地）；雇佣兵无票。国王（自由时）/玩家必召；被针对家族不投票但单独陈情。玩家作为普通封臣被俘/流亡仍保留投票权；国王被俘/流亡不保留席位（界面显示原因）；其他 AI 被俘/逃亡不召；国王已故则议程失效。
- **权重**：`DiplomacyModel.GetClanStrength`（原版选王同款），公开；国王无额外票权。
- **全量/应召**：合格封臣数 ≤ `CuriaFullCouncilThreshold`（默认 6）或强制全量时全部投票；否则固定（国王/玩家/提议者）+ 利害前 3 + 权重补到 quorum 50% + 轮换 1-2，软上限 `CuriaAttendanceCap`（默认 6，quorum 优先）。每人记录 `SummonReason`（固定/利害/权重/轮换），未出席记 `未获召见`。
- **门槛**：战/和/贸/结盟/政策 50%；封地/驱逐/背盟 2/3；议和/战前条约含割地时按 2/3，纯赔款 50%。quorum 不足 → 流会。
- **状态机**：`Proposed`（待陈情，1 天）→ `Voting`（默认 5 天）→ `Passed/Rejected` → `Applied/Failed`。提议者默认支持且计入最终票数；投票一经提交不可更改；全部应召者表态可提前结算，截止日强制按已有票数结算（未表态=弃权）。一个王国同一时间一个活跃议程。
- **暂停随机激活**：`AgentScheduler.CheckKingActivations` / `CheckSelfReviewActivations` 发现该王国有活跃议程且 `CuriaPauseRandomActivations` 开启时跳过；`CuriaVote`/`CuriaDefense` 为 P0（有截止日，优先于随机政务）。
- **稳定执行**：`CuriaService.Propose` 校验；`CuriaService.Effects` 结算前重校验；执行器调用 `DiplomacyService` 的现有动作；`AIChatClient.CurrentHero` 临时设为国王以复用 `GetDiplomacyHero`；失败标记 `Failed`，不崩游戏。
- **条约**：`propose_treaty` 只对未交战且无休战的王国；至少含割地或赔款（割地可多块，双方可同时割地）。`propose_peace` 条款可选（空=无条件和平）。条款写入 `CuriaAgenda.Treaty` 并公开；通过后由 `ExecuteProposeTreaty` / `ExecuteProposePeace` 发出 `.proposal`（含 `terms=` JSON）。对方接受时 `TreatyService` 依次执行停战/赔款/割地/休战，并写 `treaty_signed` 与双方家族领袖日记；拒绝写 `treaty_rejected`，战前条约按 `war_on_reject` 决定是否自动宣战。活跃休战由 `TreatyStore` 记录，`ExecuteDeclareWar`/`propose_agenda(declare_war)` 会拒绝；其他路径开战记 `treaty_broken`。
- **条约割地**：逐块 `ChangeOwnerOfSettlementAction.ApplyByBarter`，受地一方先归对方/我方国王家族，随后 `FiefAssignmentPatch.QueueKingFiefDecision` 触发一次受地国王分封决策（AI 国王优先 `gift_fief` 分封、确无合适家族才保留王室；玩家 M 页提示；单家族王国跳过）；被割封臣按家族合并触发一次 `FiefReview`。`treaty_cession` 史料通过 `PendingTreatyCessionText` 拦截，不记成攻城。
- **国王缺席提示**：议程详情会显示“国王未参与”及原因（无统治者/已故/被俘/流亡/未获应召），便于排查。
- **数据**：`World/curia/{王国}/state.json` + `agenda_{id}.json` + `agenda_{id}.txt`（公开摘要）。权限 `AgentManager.IsCuriaAllowed`：本国成员 + 史官；他国不可读。已结束议程默认每国保留最近 200 条（`CuriaHistoryKeep`），旧档在 `FinishAgenda` 后清理，读档时也会清理一次；`query_agenda` 只读最近 10/30 条，不随存档无限增长。
- **日记/史书**：代码代写 AI 投票者「决定」、参与者「结果」、未出席者「情报」；`HistoryRecorder.RecordCuriaEvent` 写 `curia_proposed/passed/rejected/applied/failed`；条约另写 `treaty_proposed/signed/rejected/broken/cession/failed`，并把签署/被拒/撕毁写入**双方所有主要封臣（家族领袖，含国王）**的日记；史官可读 `World/curia/` 与条约史料并在编年史写朝议/条约场景。
- **玩家入口**：M 键 `CuriaCefScreen`（CEF；朝议/外交/封地/密陈/秘书）；页面动作经 `CefBridge` + `MainThreadExecutor`；文本输入走原生 `InformationManager.ShowTextInquiry`；CEF 失败回退秘书处查询。秘书处 `chancery` 只保留 query + 只读文件工具。
- **前提过滤**：`BuildKingdoms` 输出 `canWar/canPeace/canTreaty/canTrade/canAlliance/canBreakAlliance` + `hasTruce/truceUntilDay`；CEF 只列当前有效类型与目标，AI 提示词要求先查询，`CuriaService.Propose` 硬校验并拒绝重复/无对象议题/休战期宣战。
- **政策效果**：`enact_policy` / `revoke_policy` 直接 `Kingdom.AddPolicy` / `RemovePolicy`；提案、投票、公开档案都带 `PolicyObject.SecondaryEffects` 的实际效果文本；AI 可用 `query_policies` 查询。
- **失效影响力条款的替代**：`CuriaPolicyEffects` 把 4 条受影响政策映射为朝议修正——Precarial 封地/割地条约门槛 −8%、Feudal 封地/割地条约门槛 +8%、War Tax 宣战/战前条约门槛 +10%、Royal Privilege 国王票权 ×1.2；UI 与 AI 提示均显示。
- **接管原版投票**：朝议启用时，`KingdomPolicyDecision` / `SettlementClaimantDecision` / `SettlementClaimantPreliminaryDecision` 的 `IsAllowed()` 返回 false；政策/封地/驱逐的 VM 按钮 getter 与 Execute 也被 Harmony 拦截，AI 原版政策表决不会发生。
- **旧入口**：`submit_advisory`/`submit_edict` 删除；`declare_war`/`propose_peace`/`propose_treaty`/`propose_trade`/`propose_alliance`/`end_alliance` 在朝议启用时拒绝（注意 `propose_treaty` 不是独立工具，只存在于 `propose_agenda` 类型中）；`gift_fief` 仅国王自己封地；玩家 `change_kingdom` 拒绝；原版 `ExpelClanFromKingdomDecision` 在朝议启用时取消（手动 Harmony）。贸易终止 `end_trade_agreement` 仍直接。
