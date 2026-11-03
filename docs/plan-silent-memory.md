# 静默见闻（Silent Diary）设计契约

> 目标：让"与你有关、但不值得叫醒你"的世界事件，以零 LLM、零 UI 的方式进入 Agent 的日记，成为它下次激活时可读到的**见闻**。
>
> 关联文档：`docs/architecture.md` §10（日记即索引）、§11（日记权威化与记忆巩固）；`docs/subsystems.md` §5（历史系统）。
>
> 状态：已实现（第一批事件 + read_diary + 按年分卷 + 自写水位）。

## 0. 一句话

**代码记录"你不可能不知道的事"，Agent 自己写"这件事对你意味着什么"。**

## 1. 现状与缺口

| 通道 | 机制 | 覆盖 |
|------|------|------|
| Agent 自写 | `record_resolve` / `append_file` | 对话、自省、巩固 |
| 系统代笔 | `AgentManager.AppendSystemDiary`（只给 AI 实体写） | 朝议、条约、封地分封 |
| 世界史料 | `HistoryRecorder.RecordEvent` → `World/history/events_{year}.txt` | 史官与事后查阅，**不进个人记忆** |

缺口：世界事件只走"写史"或"强到叫醒当事人"两条路。大量"与你有关但不值得叫醒你"的事件无人记得——最典型的是玩家行为：`give_gold` 直接划转，NPC 不会记得有人给过它钱。

## 2. 原则（不可违反）

1. **本系统只写「情报」，永不代写「决定/承诺/计策/评价/战略/结果/决心」。**（朝议/条约/封地分封既有的系统代笔是另一条通道，保持不变。）
   - `结果` 的语义是"我的计划有结局了"，系统代写会与"无结果=进行中"的追踪规则冲突。
   - Agent 下次激活读到时，自己决定要不要升格为「评价」「决定」——升格即消化。
2. **系统写的是见闻，不是全知。** 受众与文本都由事件发生时的代码对象计算，不从摘要字符串猜；环越外，文本越短越模糊。
3. **不唤醒、不弹窗、不写 chat_logs、不注入上下文。** 纯文件追加，成本为一次写盘。
4. **玩家是普通 Entity。** 玩家行为照常被记录，但文本里没有"玩家"这个词；玩家自己的日记不写（沿用 `AppendSystemDiary` 的 Human 过滤）。
5. **日记格式不可破坏**：`[年季节日] 类型：内容`，类型仍在白名单内。

## 3. 数据模型

### 3.1 WorldEvent（内存结构，不落盘给 Agent 读）

```csharp
SilentDiary.Publish(
    string type,                    // 复用史料 type：war_declared / settlement_captured / fief_granted / ...
    string dedupeKey,               // 幂等键（见 §7.4）
    IEnumerable<(Hero Hero, string Text)> recipients);
```

受众在调用点用构造器组装：

```csharp
var b = SilentDiary.Begin("settlement_captured", $"settlement|{day}|{settlement.StringId}");
b.Add(oldOwner, "你的封地乌尔克汗堡被库赛特·乌尔克汗家族的巴图夺去。");
b.Add(newOwner, "你获得了南帝国·波罗斯家族的乌尔克汗堡。");
b.AddClanLeader(oldClan, "南帝国·波罗斯家族的封地乌尔克汗堡被库赛特夺去。");
b.AddKingdomRuler(newKingdom, "库赛特攻下了南帝国的乌尔克汗堡。");
b.Publish();
```

**不落盘"谁该知道"**：受众映射只在内存中计算，不写 `World/`，避免 Agent 读到"谁被通知了"。

### 3.2 幂等

- `dedupeKey = type|day|subject`（subject 用 StringId 或排序后的 id 对）。
- 进程内 `HashSet<string>`，跨日清理；同一事件多个钩子（攻城 + 易主 + 册封）只记一次。
- 不持久化：战役事件钩子不会在读档时重放；同一会话内去重足够。

## 4. 受众分派：五环

| 环 | 谁 | 文本 | 实现 |
|----|----|------|------|
| 0 亲历 | 当事人（旧主/新主/攻方主帅/死者家属） | 第二人称、细节、与"你"的关系 | `Add(hero, directText)` |
| 1 直接利害 | 相关氏族领袖 | 第三人称、事实 + 与它的关系 | `AddClanLeader(clan, text)` |
| 2 体制 | 相关王国统治者 | 一句概括，不含细节 | `AddKingdomRuler(kingdom, text)` |
| 3 地理 | 邻近定居点领主 | 模糊的"边境传来消息" | 第一期不做 |
| 4 传闻 | 其他 | "商队传言……" | 第一期不做（预留） |

规则：

- 同一 Entity 只收一条（先到先得，越具体的文本越先加）。
- 只发给 `Controller == Agent` 的在世 Hero；`Hero.MainHero` 不写。
- 氏族领袖 = `clan.Leader`；王国统治者 = `kingdom.RulingClan?.Leader`。
- 去重后同一事件最多影响一个王国的全部家族领袖（约 20–40 人），每人一行。

## 5. 第一批事件（MVP）

| 事件 | 来源钩子 | 环 0 | 环 1 | 环 2 | 文本要点 |
|------|---------|------|------|------|---------|
| `war_declared` | `HistoryRecorder.OnWarDeclared` | 双方统治者 | 双方全部家族领袖 | — | 统治者："你向X宣战/ X向你宣战"；封臣："你的君主向X宣战" |
| `peace_made` | `OnMakePeace` | 双方统治者 | 双方全部家族领袖 | — | "你与X议和" / "你的君主与X议和" |
| `settlement_captured` | `OnSettlementOwnerChanged`（BySiege 与普通易主；条约割地跳过，条约已有日记） | 旧主、新主、攻方 | 旧/新氏族领袖 | 双方统治者 | 旧主："你的封地X被Y夺去" |
| `fief_granted` | 同上（ByKingDecision/ByGift） | 旧主、新主 | 旧/新氏族领袖 | 统治者 | "X将你的封地Y转予Z" |
| `siege_started` | `OnSiegeStarted` | 守方氏族领袖 | — | 守方统治者 | "X正围攻你的城堡Y" |
| `siege_failed` / `siege_abandoned` | `OnSiegeCompleted`（败）/ `OnSiegeEnded` | 守方氏族领袖 | — | 守方统治者 | "Y之围已解" |
| `hero_killed` | `OnHeroKilled`（身份在 `OnBeforeHeroKilled` 捕获） | 同族领袖 | 同国统治者 | — | "你的家族成员X阵亡" |
| `battle_fought` | `OnMapEventEnded`（≥600 人） | 双方主帅 | — | — | "你在X附近败于Y" |
| `gift` | `ExecuteGiveGold` / `ExecuteGiveItem` | 受赠者 | — | — | "X赠予你500金币" / "X将Y交给你" |
| `request` | `ExecuteRequestGold` / `ExecuteRequestItems` | 被索要者 | — | — | "X向你索要500金币，你给了" |
| `raid` | `ExecuteRaidSettlement` | 村庄所属氏族领袖 | — | 所属王国统治者 | "X的部队正开往你的村庄Y，意图劫掠" |

暂不接入（后续）：玩家劫掠村庄（原版无干净钩子）、俘虏/释放、婚嫁、家族换国、盟约到期（已有拉取式日志）、朝议/条约（已有日记写入）。

## 6. 渲染规范

所有提及人物的地方统一经过 `SilentDiaryText.Hero`：

```
{王国}·{家族}·{名}
库赛特·乌尔克汗家族的巴图
南帝国·波罗斯家族的拉盖娅（女皇）
```

- 只用全称，**不用代词**（「他/她/其/TA」一律禁止）——日记靠 grep 检索，代词会让命中率归零。
- 无王国时省略王国段；无氏族时只写名。
- 身份后缀只给统治者/族长，普通成员不加。
- 定居点：`{名}（{所属王国}）`，例如 `乌尔克汗堡（南帝国）`。
- 对收件人用第二人称：「你的封地」「你的村子」。
- 来源前缀：`你亲见` / `据使者说` / `据闻`（第一期统一用直接陈述，不编来源）。
- 单条 ≤ 60 字；同日多条合并后 ≤ 160 字。

## 7. 写入

### 7.1 缓冲与合并

- 按 `(entityId, 事件日)` 缓冲，**不立即写盘**。
- Flush 时机：① 该 Agent 任意激活前；② 游戏日推进时 flush 全部；③ `OnGameEnd` 尽力 flush。
- 单条：`[1089夏16] 情报：<text>`
- 多条（≤3）：`[1089夏16] 情报：今日见闻——A；B；C`
- 超过上限：按到达顺序保留前 3 条，追加 `等N事`。
- 日期前缀用**事件发生日**（在钩子处取好存进缓冲），不用 flush 日。

### 7.2 每日上限

MCM `SilentDiaryDailyCap`，默认 3，范围 1–5。这是安全阀，不是目标：大多数日子应为 0 条。

上限按「Agent × 游戏日」累计，不因当天多次激活而重置：首次激活写满后，当天后续事件只留在缓冲里，跨日时丢弃。

### 7.3 线程

- 钩子都在主线程；缓冲字典加锁。
- 写入走 `SafeFileIO.AppendAllText`（带重试）。
- Flush 只做文件 I/O，不碰游戏对象（文本在缓冲时已渲染好）。

### 7.4 幂等键

`{type}|{day}|{subject}`；`subject` 优先用 `Settlement.StringId` / `Hero.StringId` / 排序后的王国名对。

## 8. 读取：read_diary 与按年分卷

### 8.1 为什么必须做

`read_file` 返回整本日记，但工具结果在 `AIChatClient` 被 `Substring(0, MaxToolResultChars)` **头截断**（默认 4000 字）。日记超过约 60–80 行后，**最新的条目永远读不到**；记忆巩固 pass 读的也是同一段头部，系统却以为它已经读过。

### 8.2 新工具 `read_diary`

```json
{"name":"read_diary","category":"file",
 "parameters":[{"name":"recent","type":"integer"},{"name":"type","type":"string"}]}
```

- 默认返回最近 40 条；`type` 过滤（决定/承诺/情报/计策/评价/结果/战略/决心）。
- 头部给出：总条数、显示区间（文件行号）、往年归档文件名。
- 输出上限约 3000 字：从**最旧**开始丢，保证最新条目一定可见。
- 行号保留，Agent 可继续用 `read_file` 精确读旧段。

### 8.3 按年分卷

- 跨年时把 `decisions/diary.txt` 中 **年份 < 当前年** 的条目移入 `decisions/diary_{year}.txt`（追加），其余保留。
- 无日期前缀的行不移动（视为本年手写内容）。
- 旧卷仍可被 `read_file` / `grep` 访问；`read_diary` 头部会列出卷名。
- 分卷后 `MemoryConsolidator` 的最新日期扫描必须覆盖 `diary_*.txt`。

### 8.4 截断提示

- `read_file` 返回超过 40 行的文件时，首行加 `（共 N 行；本次第 a–b 行）`。
- `AIChatClient` 的截断提示补上原始字符数：`（工具结果过长：共 N 字符，已显示前 M 字符）`。

## 9. 与现有机制的接缝

1. **记忆巩固（diary-as-authority）**
   - 系统代笔会把日记"日期"推进到今天，导致 `IsDiaryStale` 误判日记不落后、跳过巩固——而巩固 pass 读的又是被头截断的日记。
   - 修复：新增 `decisions/.diary_authored` 水位（日期键），只在 Agent 自己写日记（`record_resolve` / 对 diary 的 `append_file`）与巩固 pass 成功后推进；`IsDiaryStale` 优先用水位，缺失时回退到扫描全部日记卷。
2. **朝议/条约**：现有 `AppendSystemDiary` 调用保持不变，不重复写。
3. **记忆优先级**：查询工具 > 日记 > 认知，不变；见闻属日记，仍可被实时查询修正。
4. **信息隔离**：日记只写进本人目录；分派在内存完成，不泄露"谁被通知"。
5. **日期格式**：继续 `[1089夏16]`，兼容 `MemoryConsolidator` 的两种解析。

## 10. 配置（MCM，分组「游戏设置」）

| 项 | 默认 | 说明 |
|----|------|------|
| 启用静默见闻 | 开 | 关闭后不再代笔，已有日记不受影响 |
| 每日见闻上限 | 3 | 1–5，超出合并/截断 |

## 11. 改动文件清单

新增：

- `docs/plan-silent-memory.md`（本文）
- `Agents/DiaryStore.cs`：日期解析、最近条目抽取、按年分卷（纯逻辑 + 文件操作）
- `Agents/SilentDiary.cs`：事件分派、渲染、缓冲、幂等
- `Systems/SilentDiaryText.cs`：人名/地名渲染（纯逻辑，可单测）

修改：

- `Agents/AgentManager.Files.cs`：`ExecuteReadDiary`、`read_file` 行数头、`GetDiaryDatePrefix` 公开化、日记写入水位
- `Agents/AgentScheduler.cs` / `AgentScheduler.Events.cs`：激活前 flush、跨日 flush、跨年分卷
- `Agents/MemoryConsolidator.cs`：水位 + 跨卷扫描
- `Agents/AIChatClient.cs`：截断提示带总字符数
- `Systems/HistoryRecorder.cs`：战争/议和/封地/围城/死亡/战斗钩子
- `Tools/ToolExecutor.cs` / `ToolExecutor.Social.cs`：`read_diary` 分发、赠礼事件
- `UI/AIChatScreenVM.cs`：tool 显示映射
- `Core/Settings.cs`：两个 MCM 项
- `_Module/Prompts/agent_tools.json`：`read_diary` 定义
- `_Module/Prompts/{self_review_rules,diplomacy_rules,letter_rules,conversation_rules,memory_consolidation,consolidation_rules}.txt`：改用 `read_diary`、说明系统代笔的「情报」
- `tests/AIChronicle.Pure.Tests/*`：DiaryStore / SilentDiaryText 测试
- `docs/tools-reference.md`、`docs/architecture.md`

## 12. 测试

- `DiaryStore.TryParseYear`：`[1089春3]` / `[1089年冬第9日]` / 无前缀行。
- `DiaryStore.SelectRecent`：type 过滤、条数、行号、超长回退。
- `DiaryStore.SplitForRotation`：跨年条目分流、无日期行保留、多年份分卷。
- `SilentDiaryText.Hero`：王国/氏族/身份缺失时的退化格式；无代词。
- `read_diary` 输出：头部含总数与往年卷名。

## 13. 已知限制 / 后续

- 缓冲在内存，存档/退出时可能丢失当日未 flush 的见闻（最多一天）。
- 同一游戏日内，系统见闻的落盘顺序晚于 Agent 当天的自写条目（日期粒度一致，因果顺序不保证）。
- 环 3/4（地理、传闻）未实现；RAG 化后可把受众扩大到"听说过的所有人"，再用检索过滤。
- 玩家直接下令的原版劫掠、俘获/释放暂无干净钩子，后续单独接；Agent 的 `raid_settlement` 已接。
