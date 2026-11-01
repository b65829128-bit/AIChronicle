# 朝议（Curia）设计文档

> 状态：已确认，v1 已实现（v2.4.0，编译与纯逻辑测试通过；游戏内实测待补）。本文是实现的契约；代码、提示词、MCM 与文档应与此一致。

## 1. 一句话

**朝议是王国层面的公开表决：封臣或国王提出一段有明确目标的议题，应召的封臣家族领袖投票，通过则执行原版动作，不通过则无事发生；结果写入公开档案、Agent 日记与史书。**

它不是公开谏言的替代品，而是公开谏言的升级：保留“一段文字”的表达，但加上**类型、目标、投票、执行、记录**。

## 2. 范围

### v1 做的七类议题

| 类型 | 提议者 | 目标参数 | 通过后的动作 | 门槛 |
|---|---|---|---|---|
| `declare_war` 宣战 | 国王或任一封臣家族领袖 | 目标王国 | `DiplomacyService.ExecuteDeclareWar(king, target, reason)` | 50% |
| `propose_peace` 议和提议 | 国王或任一封臣家族领袖 | 目标王国 | 以国王名义创建议和提案（对方国王仍要同意） | 50% |
| `propose_trade` 贸易提议 | 国王或任一封臣家族领袖 | 目标王国 | 以国王名义创建贸易提案（对方国王仍要同意） | 50% |
| `propose_alliance` 结盟提议 | 国王或任一封臣家族领袖 | 目标王国 | 以国王名义创建同盟提案（对方国王仍要同意） | 50% |
| `break_alliance` 退出盟约 | 国王或任一封臣家族领袖 | 目标王国 | `DiplomacyService.ExecuteEndAlliance(king, target)`，立即解除盟约 | 2/3 |
| `transfer_fief` 封地转让 | 国王（夺封/再分配）或该封地所属家族领袖（自愿转让） | 定居点 + 新主家族领袖 | `ChangeOwnerOfSettlementAction.ApplyByKingDecision`；被夺方触发 `FiefReview` | 2/3 |
| `expel_clan` 驱逐家族 | 仅国王 | 目标家族 | `ChangeKingdomAction.ApplyByLeaveKingdom`（家族带封地离开王国） | 2/3 |

### v1 明确不做

- 不做私战、局部战争、王国级战争之外的战争状态。
- 不做合法性数值、威望条、名分分数。
- 不做国王“独断绕过”：上述七类行动在 v1 只有朝议一条合法路径（国王自己的封地、新城封赏除外）。
- 不做征税/政策/忠诚度改造；不碰原版繁荣度与忠诚度。
- 不做玩家快速加入别国/当雇佣兵的便捷入口；使用原版界面。
- 不做文化专属门槛；先用一套通用规则，文化风味留到后续。

## 3. 身份与选民

- **封臣 = 政治身份**：`Clan.Kingdom != null` 且非雇佣兵、非小派系、非匪徒。**有没有封地都能投票、都能提议**；无地只是权重低。
- **雇佣兵 = 职业身份**：无投票权、无提议权、无朝议工具；只能被雇佣、打仗、换雇主。
- **国王**：必然是封臣家族领袖，必然参与投票；国王的家族与其他家族一样按 `GetClanStrength` 计权，没有额外票权。
- **玩家**：是封臣或国王时必然有投票权（即使无地）；是雇佣兵时没有。玩家永远被应召。
- **被针对家族**（夺封/驱逐）：**不投票**，但可陈情；陈情进入议程记录，供其他家族投票时参考。自愿转让时提议者就是封地所属家族，**不被排除**，照常投票。
- **被俘/逃亡**：AI 家族领袖不进入选民池（无法出席）；玩家若被俘仍可投票（模组既有“被俘国王仍可理政”的口径，玩家体验优先）。

## 4. 应召与权重

### 权重

- 基础权重 = `Campaign.Current.Models.DiplomacyModel.GetClanStrength(clan)`（原版选王同款，已包含影响力、封地、部队、金币、技能）。
- 不做王冠加成；国王的权重就是其家族权重。
- 权重公开显示，保留一位小数。

### 全量/应召

- **全量阈值** `FullCouncilThreshold`，默认 6，MCM 可调 2–20。
- 一个王国的合格选民数 ≤ 阈值，或开启“强制全量”时：**所有合格选民投票**。
- 超过阈值时，启用**应召制**：
  1. **固定席位**：国王、玩家（若在本国）、提议者。
  2. **利害席位**：按利害分取前 3 名（不含固定/被针对家族）。利害因素：与目标接壤/有封地争议/有贸易/交战/联姻/仇怨/同盟；夺封/驱逐时与目标家族的关系、是否可能成为下一个。
  3. **权重席位**：按权重从高到低补人，直到应召总权重 ≥ 全国合格选民总权重的 **50%**（quorum）。
  4. **轮换席位**：再补 1–2 个最久未被应召的家族，保证小家族迟早有机会。
  5. **上限** `AttendanceCap`，默认 6，MCM 可调 2–20。**软上限**：到上限仍未满足 quorum 就继续按权重补人，quorum 优先于上限。
- 被针对家族不在选民列表；它的权重不计入 quorum 分母。
- **出席理由**逐人记录：`固定` / `利害` / `权重` / `轮换`；未出席者记录 `未获召见`。
- 所有合格选民的权重总和、应召权重、quorum 都写入议程记录，公开可查。

## 5. 议程生命周期

```
提出（Proposed）
  ├─ 需要陈情：激活被针对家族陈情（1 游戏天截止）
  └─ 不需要陈情：立即进入投票
投票（Voting，默认 5 游戏天）
  ├─ 应召选民逐一表态：支持 / 反对 / 弃权（理由可选）
  ├─ 全部应召者已表态 → 提前结算
  └─ 截止日 → 结算（未表态=弃权）
结算
  ├─ quorum 未达 → 流会，不通过
  ├─ 支持 /（支持+反对）≥ 门槛 → 通过 → 执行动作
  └─ 否则 → 不通过
记录
  ├─ 写公开议程档案（World/curia/{王国}/agenda_{id}.json + .txt）
  ├─ 写 AI 日记（投票者：决定；结果：所有参与者；未出席者：情报）
  ├─ 写原始史料（curia_proposed / curia_passed / curia_rejected / curia_applied / curia_failed）
  └─ 左下角通知；清空活跃议程，恢复该国随机激活
```

- **一个王国同时只有一个活跃议程**；提议有冷却（默认 3 游戏天/人，MCM 可调）。
- **议程期间暂停该王国的随机 `SelfReview` / `KingDiplomacy` 激活**，只处理朝议相关事件；其他王国不受影响。
- 朝议事件优先级 = P1（与国王政务同级），确保不被自省/信件淹没。
- 提议者提出时自动记一票“支持”，理由=提议理由；提议者若不在选民池（雇佣兵）直接拒绝。
- 提议者默认支持计入最终票数；玩家/AI 投票一经提交不可更改（再次提交返回错误）。
- 投票截止日强制结算：未表态的应召者按弃权计入，不等待玩家或 AI；`CuriaVoteDays` 默认 5 天，MCM 可调。
- 若目标在执行前失效（王国灭亡/封地易主/家族离开/已在交战/已签贸易），结算时重新校验，标记 `failed` 并记录原因，不执行动作。

## 6. 文本如何稳定生效

**文本是叙事，结构化载荷是机制。** 议程不是自由文本，而是一张有类型、可验证、有执行器的政治契约。

### 6.1 数据

```text
Agenda {
  id, kingdomId, type, status,
  proposerEntityId, proposerClanId, reason,
  targetKingdomId | targetSettlementId + targetClanId + newOwnerClanId | targetClanId,
  terms, defense, kingReason,
  createdAt, deadline, votes[], summoned[], absent[],
  supportWeight, opposeWeight, abstainWeight, totalWeight, quorumWeight,
  threshold, outcome, effectResult, failureReason
}
```

### 6.2 提案时验证

- 提议者身份：封臣/国王；雇佣兵拒绝；被俘/逃亡的 AI 不能提议。
- 类型合法；目标存在；目标在正确状态（未交战/正在交战/未签贸易等）。
- 封地转让：目标只能是本王国封臣家族的城镇/城堡；**不能是统治者家族的封地**；新主必须是本王国封臣家族领袖；不能是原主；不能是雇佣兵。
- 驱逐家族：目标必须是本王国封臣家族，不能是统治家族，不能是雇佣兵。
- 一个王国无活跃议程；同一提议者无冷却。
- 不合法 → 直接返回错误，不进入表决。

### 6.3 结算时再验证

- 目标王国/家族/封地仍存在且状态未变。
- 封地仍属原家族、新主仍在王国、仍是家族领袖。
- 目标仍在王国、不是统治家族。
- 若失效 → `status=failed`，`failureReason`，不执行；写日记/史料/通知。

### 6.4 执行器

- 宣战：`DiplomacyService.ExecuteDeclareWar(king, target, reason)`（新增带 acting hero 的重载）。
- 议和/贸易：`DiplomacyService.ExecuteProposePeace(king, ...)` / `ExecuteProposeTrade(king, ...)`（新增重载），仍走现有提案与对方国王响应。
- 封地转让：`DiplomacyService.ApplyTransferFiefDirect(king, settlement, newOwner, reason)`（从现有 `ExecuteTransferFief` 抽出核心，保留 `PendingFiefGrantText` 与 `FiefReview`）。
- 驱逐：`DiplomacyService.ApplyExpelClan(king, targetClan, reason)`（`ChangeKingdomAction.ApplyByLeaveKingdom`）。
- 所有执行器只在主线程运行；异常捕获、写 `debug_logs`、标记 `failed`，不崩游戏。

### 6.5 幂等与审计

- 状态机：`Proposed → Voting → Passed/Rejected → Applied/Failed`。
- 每个议程唯一 `id`；效果只执行一次；执行前检查游戏状态（已交战/封地已易主/家族已离国）。
- 记录完整审计：类型、参数、票数、结果、执行器返回、时间。
- 档案持久化在战役目录，跨存档可恢复；不依赖 Agent 记忆。

## 7. 日记、史书、公开性

- **AI 投票者**：投票后写日记 `[年季节日] 决定：朝议「…」我投了支持/反对/弃权，因为…`。
- **结果**：提议者、国王、投票者、被针对家族写 `[年季节日] 结果：朝议「…」通过/未通过；…`。
- **未出席的 AI 封臣**：结果后写 `[年季节日] 情报：…朝议「…」我因{客观原因}未能出席；结果…`。原因从事实选：前线作战/封地遭围/路途遥远/未获召见/染病/家族事务；不假装投票。
- **玩家**：不强制写玩家日记；M 键参政页与秘书查询可见。
- **史书**：原始史料记 `curia_proposed/passed/rejected/applied/failed`；史官可读 `World/curia/` 的完整档案，在编年史中写“朝议”场景。
- **公开范围**：`World/curia/{王国}/` 对本王国封臣/国王 + 史官只读；史官可读所有国家；其他国家不可读。Agent 用 `query_agenda` 或 `read_file` 查看本国档案。

## 8. 玩家入口：M 键“参政”

**M 键打开 CEF 参政页**（不是 C 键，C 被原版占用）。页签：

- **朝议**：活跃议程与历史；投票（支持/反对/弃权，可选理由）；提议程；游说入口（跳 O 书信）。
- **外交**：待处理外国提案（接受/拒绝）；当前战争/盟约/贸易；国王问询线程（回复/遣使）。
- **封地**：国王自己的封地自由转让；封臣封地→提议转让/夺封；新城封赏；驱逐家族；查看封地争议记录。
- **密陈**：封臣给国王的密陈；国王可读本国密陈。
- **秘书**：打开秘书处查询聊天；秘书只保留查询工具，不做任何政治动作。

**秘书处（M 页签）只查询**：`query_*` + `query_agenda` + `read_file/list_dir/glob/grep`；不提供 `submit_advisory`、`submit_edict`、`change_kingdom`、外交动作、谏言/密陈/诏令。

**玩家家族动作**：加入别国/当雇佣兵/脱离王国使用原版界面；v1 不提供快捷键，`change_kingdom` 对玩家直接拒绝。AI 的 `change_kingdom` 保留。

**文本输入**：提议理由、密陈、问询回复使用原生 `InformationManager.ShowTextInquiry`（中文 IME 边界与聊天一致）；CEF 页面只负责选择类型/目标与展示。CEF 初始化失败时回退为秘书处查询聊天，并提示“参政页不可用”。

## 9. 移除/替换清单

| 旧 | 新 |
|---|---|
| `submit_advisory` 公开谏言 | 移除；用 `propose_agenda` 替代 |
| `submit_edict` 诏令 | 移除；国王的“评价”= 在议程上的投票+理由；国王的“宣示”= 自己提的议程 |
| 玩家经秘书处 `change_kingdom` | 移除；用原版界面 |
| `declare_war` / `propose_peace` / `propose_trade` 直接发起 | 保留工具定义但**拒绝执行**，提示“请用 propose_agenda”；仅朝议执行器可调用底层服务 |
| `gift_fief` 任意夺封 | 只允许国王转让**自己家族**的封地；其他封地必须走朝议 |
| 原版 `ExpelClanFromKingdomDecision` | Curia 开启时禁用（手动 Harmony 注册） |
| 原版 `SettlementClaimantDecision` | 已由 `FiefAssignmentPatch` 接管；新城默认归统治家族→国王自由封赏 |
| 公开谏言/诏令文件 | 旧档保留为只读历史；新系统不写 |

- 外交“接受/拒绝外国提案”（`respond_to_diplomacy_proposal`）保留为国王的对外回应，不需要内部朝议。
- 同盟与背盟已并入七类朝议；贸易终止 `end_trade_agreement` 仍保留为国王直接工具。

## 10. 数据与文件

```
世界目录/NPCs/World/curia/
  {王国名}/
    state.json                 # 活跃议程 id、冷却、上次应召日
    agenda_{id}.json           # 结构化议程（唯一事实源）
    agenda_{id}.txt            # 人类/史官可读摘要（提议/陈情/票数/结果）
  （无全局 index；扫描目录）
```

- 文件名中的王国名用 `SanitizeDir`。
- 写文件统一 `SafeFileIO`；JSON 用 Newtonsoft，枚举字符串化。
- 权限：`AgentManager.IsCuriaAllowed`；史官任何国家；其他 Agent 仅本国；根目录仅史官可列。

## 11. MCM / 场景

新增 MCM「朝议」组：

| 设置 | 默认 | 范围 |
|---|---|---|
| 启用朝议 | 开 | — |
| 全量阈值 | 6 | 2–20 |
| 应召上限 | 6 | 2–20 |
| 强制全量 | 关 | — |
| 投票天数 | 5 | 1–15 |
| 出席 quorum | 0.5 | 0.3–0.9 |
| 战/和/贸/结盟门槛 | 0.5 | 0.5–0.9 |
| 封地/驱逐/背盟门槛 | 0.66 | 0.5–0.9 |
| 议程期间暂停随机激活 | 开 | — |
| 提议冷却（天） | 3 | 0–30 |
| 保留朝议记录 | 200 | 20–1000 |

新增场景连接「朝议场景」：URL/模型/密钥/接口类型 + 测试按钮；逐字段兜底；不配置跟随兜底。朝议 AI 激活 intent=`curia` / `curia_defense`，与封臣自省分开计费。

## 12. 代码落点（实现清单）

- `Systems/CuriaModels.cs`：枚举、`CuriaVote`、`CuriaAgenda`、`CuriaState`。
- `Systems/CuriaStore.cs`：读写 JSON/TXT、目录、权限辅助、日记、史料、通知。
- `Systems/CuriaService.cs`：验证、提议、应召、投票、陈情、结算、执行器、页面数据、查询文本。
- `Systems/CuriaEffects.cs`（可并入 Service）：七类执行器与重校验。
- `Agents/AgentScheduler.Curia.cs`：`CuriaVote`/`CuriaDefense` 事件、队列、暂停随机激活、Tick 结算。
- `Tools/ToolExecutor.Curia.cs`：`propose_agenda`、`respond_to_agenda`、`defend_agenda`、`query_agenda`。
- `UI/Web/CuriaCefScreen.cs`：M 键 CEF 参政页宿主；页面动作回主线程；原生文本输入；刷新。
- `_Module/WebUI/curia.html` / `curia.css` / `curia.js`：朝议/外交/封地/密陈/秘书五页签。
- `_Module/Prompts/curia_rules.txt`、`curia_vote_prompt.txt`、`curia_defense_prompt.txt`；更新 `diplomacy_rules.txt`、`self_review_rules.txt`、`historian_rules.txt`、`chancery_rules.txt`、`conversation_rules.txt`、`letter_rules.txt`、`tools.json`、`agent_tools.json`、`context_template.txt`。
- `Core/Settings.cs` / `Settings.Scenarios.cs` / `ConnectionResolver.cs` / `AIChatClient.cs` / `ContextBuilder.cs` / `EntityManager.cs`：MCM、场景、intent、工具分类、能力。
- `Core/SubModule.cs`：M 键、`CuriaService.ResetForNewCampaign`、原版驱逐决策补丁。
- `Systems/HistoryRecorder.cs`：`RecordCuriaEvent`、灭国取消议程。
- `Systems/DiplomacyService.cs`：带 acting hero 的重载、`ApplyTransferFiefDirect`、`ApplyExpelClan`、旧工具限制。
- `Agents/AgentManager.Permissions.cs`：`IsCuriaAllowed`、`ResolvePath`、`IsPathAllowed`。
- `docs/`、`README_MOD.md`、`AGENTS.md`：同步（README/AGENTS 按项目规则在实现汇报时说明）。

## 13. 测试

- 纯逻辑：权重/应召/quorum/门槛/三种结果；全量阈值边界；被针对家族排除；自愿 vs 夺封；冷却；失效重校验；幂等。
- 工具 JSON 与 switch 同步（`scripts/ci-health.mjs`）。
- 编译 `dotnet build -c Release -f net472`；纯逻辑测试；CI health。
- 运行验证边界：不能替代进游戏实测；重点检查 CEF 页面加载、M 键入口、投票流、封地转让/宣战效果。

## 14. 后续（不在 v1）

- 同盟/终止盟约并入朝议。
- 国王“独断绕过”与公开记录（如需要）。
- 文化专属朝议名号与门槛（帝国元老院/瓦兰迪亚库里亚/斯特吉亚维彻/巴旦尼亚部族大会/库赛特忽里勒台/阿塞莱舒拉）。
- 封地权利拆分（收益/司法/守备/继承）。
- 教会/法庭/继承危机。
- Linux Fcitx5 游戏内输入法（独立议题；v1 用剪贴板/原生输入绕过）。
