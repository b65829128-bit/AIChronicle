# 条约（Treaty / 战前最后通牒）设计文档

> 状态：P0–P2 已实现；多块割地/双方同时割地（原 P3 的一部分）已追加（2026-11）。其余 P3 搁置。本文是实现的契约；代码、提示词、MCM 与文档应与此一致。

## 1. 一句话

**朝议不仅能决定“战/和”，还能决定“以什么条件战/和”：战前可向未交战国提出条约，要求割地/赔款；对方接受则不开战，拒绝则可能宣战。和平条约也可附带割地/赔款；无条件和平与直接宣战仍是独立选项。**

## 2. 范围（P0–P2）

| 场景 | 朝议类型 | 对方接受 | 对方拒绝 |
|---|---|---|---|
| 战前 | `propose_treaty` 战前条约/最后通牒 | 割地/赔款，不开战，进入休战 | 可选择自动宣战；否则只记宣战理由 |
| 战中 | `propose_peace` + 条款 | 停战 + 割地/赔款 + 休战 | 战争继续 |
| 战中 | `propose_peace` 无条款 | 无条件和平 | 战争继续 |
| 任意 | `declare_war` | —— | 直接开战 |

P0：议和赔款恢复。P1：和平条约条款（割地 + 赔款，双向）。P2：战前条约（拒绝后果 + 休战）。

### 明确不做（P3）

- 多笔赔款（v1 仍只有一笔每日 tribute）。
- 对方朝议批准/否决条约（v1 仍由对方国王个人拍板）。
- 一次性赔款（v1 只用原版每日 tribute）。
- 讨价还价/反提案。
- 割地后的封地权利拆分、教会/法庭、文化专属条约名号。
- 撕毁条约的独立“背约”动作（v1 只能等休战到期；原版路径开战会被记为 treaty_broken）。

## 3. 条款（结构化载荷）

```text
TreatyTerms {
  Cessions: [ { SettlementId, SettlementName, CedeFrom: "proposer" | "target" } ],
  // 旧版单块字段 SettlementId/SettlementName/CedeFrom 仅用于兼容，读入后并入 Cessions
  TributeAmount,   // 正数 = 我方每日付对方；负数 = 对方每日付我方
  TributeDays,
  TruceDays,       // 0 = 不设休战
  WarOnReject      // 仅战前条约
}
```

- `CedeFrom=proposer`：我方把该封地割给对方；`CedeFrom=target`：对方把该封地割给我方；同一份条约可同时包含两种方向。
- 一条约支持 **多块城镇/城堡 + 一笔每日赔款**；双方可同时割地；村庄随主城。
- 战前条约必须至少包含割地或赔款；纯休战/互不侵犯留到 P3。
- 无条件和平 = `propose_peace` 且无任何条款。
- 休战期默认：战前条约 60 天；附条件和平默认 0（玩家/AI 可显式填写）。

## 4. 流程

```text
提议（朝议，公开条款）
  → 本国表决（纯赔款 50%；涉及割地 2/3）
  → 通过后由国王发出 .proposal（含 terms= JSON）
  → 对方国王收到 KingDiplomacy 激活 + 条款/后果
  → 接受：停战（若在战）→ 赔款 → 割地 → 休战记录 → 史料/日记
  → 拒绝：treaty_rejected；战前条约若 war_on_reject=true 自动宣战
```

- 提议时条款写入 `CuriaAgenda.Treaty`，投票者可见；不能“先授权再私下加条件”。
- 对方国王仍是个人拍板（与现有外交提案一致）；v1 不要求对方开朝议。
- 接受时全部条款重新校验：封地仍属原主、王国仍存在、统治者仍在世、是否已停战/已签休战。
- 若战前条约送达后世界已开战，接受时按和平条约处理（停战 + 条款）。
- 割地失败：若停战/赔款已生效，返回 `[警告]`，记 `treaty_failed`，不记 `treaty_signed`；提案仍会被消费。
- **受地国王先分封**：割地先归受地国王家族（保证不产生 unassigned 忠诚惩罚），随后立即排队一次国王分封决策：AI 国王收到 `KingDiplomacy` 提示，应优先用 `gift_fief` 将新城赐予有功/无地家族，只有确无合适家族时才保留王室；玩家国王收到左下角提示，在 M 键「封地」页自行分封。同一批割地只提示一次；单家族王国跳过。
- 所有效果在主线程执行；异常记 `debug_logs` 并返回错误/警告，不崩档。

## 5. 校验

- 提议者：朝议现有规则（家族领袖、非雇佣兵、非被俘/流亡、无活跃议程、冷却）。
- 战前条约：目标未交战、未灭亡、有在世统治者、无活跃休战。
- 议和：目标正在交战。
- 割地：
  - 每块必须是城镇/城堡；归属方必须与该项 `CedeFrom` 一致；同一块地不能重复出现。
  - `CedeFrom=proposer` 时，国王可割任何本国封地；封臣只能割自己家族的封地。
  - `CedeFrom=target` 时，由对方国王接受/拒绝；其封臣的封地被割会触发 FiefReview。
  - 可同时割多块，且可双方同时割地。
  - 受地一方先归对方/我方国王家族，之后可按本国制度再封赏。
- 赔款：金额夹取 ±100000；天数 1–3600；金额非 0 时天数必须 > 0。
- 休战：0–720 天。
- 一次只能有一个活跃议程；同一提议者冷却沿用 `CuriaProposalCooldownDays`。

## 6. 门槛

- 纯赔款/无割地：`CuriaWarThreshold`（默认 50%）。
- 涉及割地：`CuriaFiefThreshold`（默认 2/3）。
- 政策修正：
  - Precarial Land Tenure：封地/割地条约门槛 −8%。
  - Feudal Inheritance：封地/割地条约门槛 +8%。
  - War Tax：宣战/战前条约（无割地）门槛 +10%。
  - Royal Privilege：国王票权 ×1.2（沿用）。

## 7. 休战与毁约

- 签署条约且 `TruceDays > 0` 时，写 `TreatyRecord`，`TruceUntilDay = Today + TruceDays`。
- `ExecuteDeclareWar` 与 `CuriaService.Propose(declare_war)` 检查活跃休战并拒绝；M 页目标过滤 `canWar/canTreaty=false`。
- 默认 `BanVanillaDiplomacy` 开启时玩家原版外交按钮不可用、原版 AI 决策被拦截，因此对走本模组的路径基本是硬约束。
- 若通过其他路径开战（关闭原版外交拦截、剧情/任务等），`HistoryRecorder.OnWarDeclared` 会标记 `treaty_broken` 并写双方家族领袖日记。
- 休战到期惰性标记 `expired`；不主动激活 LLM。

## 8. 数据与文件

```text
World/diplomacy/
  {proposer}_to_{target}_{type}.proposal   # 旧格式 + terms= JSON + tribute=
  treaties.json                            # TreatyBook：已签署条约/休战记录
```

```text
CuriaAgenda {
  ...,
  Treaty: TreatyTerms | null
}
```

- 条约记录与朝议档案分离：朝议档案会按 `CuriaHistoryKeep` 清理，休战义务必须跨存档保留。
- 提案文件仍是纯文本行格式；`terms=` 是单行 JSON，旧文件无 `terms=` 时回退解析 `tribute=amount_days`。
- 所有写入走 `SafeFileIO`。

## 9. UI

- M 键参政页 → 朝议 → 提议表单：
  - 类型增加“战前条约”。
  - 条约条款区：可动态添加多行割地（每行：我方/对方 + 城镇/城堡；可双方同时割地）、赔款方向（不赔/我方/对方）、每日金额、持续天数、休战天数、拒绝即宣战（仅战前条约）。
  - 战前条约默认休战 60 天；议和默认 0（即无条件和平）。
- 议程详情显示 `Terms` 人类可读条款；外交提案卡片显示对方视角条款。
- 当前外交表增加“休战”列。
- 所有页面动作仍走 `CefBridge.dispatch` → `CuriaCefScreen` → `MainThreadExecutor`。

## 10. 提示词与 AI 工具

- `propose_agenda` 新增可选参数：`cessions`（`side:城;side:城` 多块）/ `cede_from` / `settlement_name`（单块兼容）/ `tribute_amount` / `tribute_days` / `truce_days` / `war_on_reject`。
- `propose_peace` 旧工具保留（朝议关闭时），描述中注明条款需走 `propose_agenda`。
- `query_pending_proposals` 显示完整条约条款与拒绝后果。
- `respond_to_diplomacy_proposal` 描述提示权衡割地/赔款/战争。
- 更新 `curia_rules.txt` / `diplomacy_rules.txt` / `self_review_rules.txt` / `letter_rules.txt` / `game_rules.txt` / `historian_rules.txt`。
- 投票提示词额外给出：割地归属/守军、若割的是投票者家族封地的警告、赔款方向、休战、拒绝即宣战。
- 协议 AI 的 `BuildSelfStatus` 注入现存休战；`query_world_state` 显示休战至第 N 日。

## 11. 史料与日记

- 史料新增：`treaty_proposed` / `treaty_signed` / `treaty_rejected` / `treaty_broken` / `treaty_cession` / `treaty_failed`。
- `treaty_cession` 通过 `HistoryRecorder.PendingTreatyCessionText` 拦截 `OnSettlementOwnerChanged`，不误记为 `settlement_captured`。
- `treaty_signed` 摘要含中立视角条款（双方可读，不会把“我方”理解反）。
- 日记：条约签署/被拒/被撕毁时，写**双方王国所有主要封臣（家族领袖，含国王）**的 `decisions/diary.txt`：
  - 签署：`结果：A与B签订条约：...`
  - 被拒：`情报：B拒绝了A的条约：...`
  - 撕毁：`情报：A撕毁了与B的休战条约，向B宣战`
  - 战前条约被拒并自动宣战：追加 `结果：...最后通牒被拒，战争爆发`
- 玩家（Human）不写系统日记，沿用 `AppendSystemDiary` 规则。
- 史官可读 `World/curia/` 与原始史料；条约条款在 `terms` 与史料摘要中可见。

## 12. 代码落点

- `Systems/TreatyModels.cs`：TreatyTerms / TreatyCession / TreatyRecord / TreatyBook（旧单块字段自动并入 Cessions）。
- `Systems/TreatyText.cs`：纯逻辑文案（三方视角）。
- `Systems/TreatyStore.cs`：treaties.json、活跃休战查询、过期/撕毁。
- `Systems/TreatyService.cs`：校验、执行、入史、日记、拒绝/毁约。
- `Systems/CuriaService*.cs`、`Systems/CuriaModels.cs`：`propose_treaty`、条款校验/门槛/执行器。
- `Systems/DiplomacyService.cs`：带条款议和、战前条约提案、响应、休战拦截、`ApplyPeaceWithTribute`。
- `Systems/HistoryRecorder.cs`：treaty_cession 拦截、treaty_broken。
- `Systems/FiefAssignmentPatch.cs`：攻城/条约受地后的国王分封提示（共享 `QueueKingFiefDecision`）。
- `Tools/ToolExecutor*.cs`、`UI/Web/CuriaCefScreen.cs`、`_Module/WebUI/curia.js/css`。
- `Agents/AgentManager.Proposals.cs`：terms= 落盘。
- `Agents/ContextBuilder.cs`、`Agents/AgentScheduler.Events.cs`、`Tools/ToolExecutor.Query.cs`。
- 提示词与 `tools.json`；`docs/`。

## 13. 测试

- 纯逻辑：`TreatyText` 的三方视角、多块割地/双方同时割地、赔款方向、休战/拒绝后果；`TreatyTerms` 的 Cessions/旧字段迁移/HasCession/HasTribute/HasConcession/HasAnyTerm。
- `dotnet build -c Release -f net472`；`dotnet run --project tests/AIChronicle.Pure.Tests`；`node scripts/ci-health.mjs`。
- 进游戏重点验：
  - 议和带赔款（P0）。
  - 和平条约割地/赔款双向、休战、存读档。
  - 战前条约接受（不开战）与拒绝（自动宣战/不自动）。
  - 休战期内宣战被拒；原版路径开战记 treaty_broken。
  - 双方家族领袖日记是否落盘。
  - 受地国王是否收到分封提示（AI `KingDiplomacy` / 玩家 M 页提示），且能 `gift_fief` 分封；单家族王国跳过。
  - CEF 表单与外国提案卡片条款显示。
