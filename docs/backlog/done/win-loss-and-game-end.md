# 胜败判定与游戏结束：涡流 / 镜像双子 / 呆瓜

- Status: Done
- Priority: High
- 验收：批次 E14（2026-10-04）矩阵 1–11 全过（`docs/acceptance/batches.md`；逐行证据见本票「已落地与运行证据」）。
- Depends on: 白天阶段（`done/day-phase.md`）；事件触发与能力存续（`done/witch-curse.md`）；处罚处决（`done/madness-and-adjudicated-execution.md`）；公开生死面与公告时点（`done/player-death-announcement.md`）
- 来源口径：`references/wiki/特殊胜利失败条件.wiki`、`规则概要.wiki` 四、`处决.wiki`、`涡流.wiki`、`镜像双子.wiki`、`呆瓜.wiki`（均为 2026-10-01 抓取）

## 要解决的问题

平台**没有任何胜负判定**：`Kernel` 里没有"游戏结束"这个概念，`GameEvent` 里没有结束事件，
事件流可以无限走下去。说书人只能口头宣布"游戏结束了"——界面上既没有结束态，也没有"结束后禁止操作"。

后果（用户视角）：

1. 恶魔全死、只剩两名存活、涡流在场而白天无人被处决——这些局面在平台上**不会结束**：
   没有结束广播、没有结束横幅，结束之后玩家还能继续提名 / 投票 / 开夜；
2. 「触发条件就是胜负条件」的三个角色至今未实现（`done/day-phase.md`、`done/witch-curse.md`
   两处残余登记在案）：引擎必须先会判胜负，它们才有正确实现的载体——否则只能是三个特例补丁；
3. 呆瓜的「当你得知你死亡时」与 R-0022 的公告时点直接衔接：公告面已落地，**选择面**仍是空白，
   而它是唯一一个"玩家死亡后仍要做公开选择、且选择直接决定胜负"的能力。

## 机制清点（来源：钟楼百科 · 2026-10-01 抓取）

| # | 环节 | 规则 | 来源 |
|---|---|---|---|
| 1 | 常规胜利 | 善良：所有恶魔均死亡；邪恶：场上仅剩两名玩家存活（旅行者不计入，首版无旅行者） | 《规则概要》四；《术语汇总》获胜 / 落败；《特殊胜利失败条件》 |
| 2 | 同层优先 | 双方同时满足**同一层**条件 → 善良获胜 | 《规则概要》四；《特殊胜利失败条件》 |
| 3 | 层级优先 | 特殊胜利条件 > 常规胜利条件；最高层「照看小怪宝的最后一名恶魔死亡」不在首版剧本（登记不实现） | 《特殊胜利失败条件》 |
| 4 | 获胜被阻断 | 阵营的获胜条件可被能力阻止（如镜像双子）：**两个双子都存活时善良阵营无法获胜**，此时即使恶魔死亡，游戏也会继续 | 《镜像双子》；《特殊胜利失败条件》 |
| 5 | 判定时机 | 一次结算事务内连续死亡**不中途判定**，事务提交后统一判定（R-0008）；处决流程内部顺序：处决 + 死亡 → 死亡者能力失效 → **先查胜负** → 再结算死亡触发能力（呆瓜在此列）→ 结束白天 | 《处决》一些相关效果的触发时机 1–5；《钟楼谜团隐性规则汇总》§5 |
| 6 | 处决 ≠ 死亡 | 涡流 / 镜像双子关注「是否被处决」，**不关注**处决是否导致死亡（关注"因处决而死亡"的是另一类角色） | 《处决》关于处决 |
| 7 | 镜像双子配对 | 由说书人选择镜像双子与一名对立阵营玩家配对；首夜两名双子互认对方角色；"对立双子"标记移除时机：镜像双子死亡或离场 | 《镜像双子》运作方式 / 提示标记 |
| 8 | 镜像双子触发 | 双子里**善良方被处决 → 邪恶阵营获胜**；死去的镜像双子失去能力（死后处决善良方不再获胜） | 《镜像双子》角色简介 |
| 9 | 涡流黄昏 | 每个黄昏，如果今天白天没有玩家被处决，游戏结束，邪恶阵营获胜 | 《涡流》角色能力 / 运作方式 |
| 10 | 涡流信息 | 只要涡流存活，镇民通过能力获取信息时**必须**是错误信息（平台不生成信息，内容仍由说书人裁定，D-0002） | 《涡流》角色简介 / 运作方式 |
| 11 | 呆瓜 | 得知自己死亡时公开选择一名**存活**玩家：选到邪恶 → 呆瓜的**当前阵营落败**；选到善良 → 无事发生；属于邪恶阵营的呆瓜选到邪恶 → 改为善良阵营获胜 | 《呆瓜》角色能力 / 运作方式 / 范例 |
| 12 | 呆瓜与结束 | 若处决已导致游戏结束，呆瓜不需要再使用能力（第 3 步先于第 4 步） | 《处决》第 5 步附注 |

## 实施方案（架构审视）

### 1. 现状与缺口

- **无胜负概念**：`src/` 无 Outcome / Victory / GameEnded；步骤机走完计划后没有任何终态，
  命令闸只认"阶段 + 挂起"；
- **阵营从未被观测**：`AssignCharactersCommand` 只补 角色 / 生死 / 清醒 / 健康（R-0015 / R-0016），
  第 6 维「阵营」没有任何写入方——而三个胜负条件全都要读阵营；
- **死亡触发能力没有载体**：`IEventTrigger` 只能产出事件，开不出"玩家选择"；
  `OperationRequest` 强绑槽位（`SlotId` / `PlanLabel` / `IssuedAtSlotIndex` 必填），
  非夜间槽位的选择无处安放；
- **契约闸全关**：`DayActions.IsCovered` 把三个角色拒之门外，`NightActions` 缺涡流 / 镜像双子。

### 2. 目标域模型

| 概念 | 落点 | 理由 |
|---|---|---|
| 胜负结论 | Kernel `GameOutcome`（胜方 + 条件分类 + 说明）+ `GameEndedEvent` | 事件是唯一事实来源（D-0010）：可重放、可重连、可审计 |
| 结束态 | 折进 `StepMachineState.Outcome` | 快照已持久化步骤机；随后所有命令被闸拒绝（`phase.game_ended`），不新增第四个派生视图 |
| 判定器 | Kernel `OutcomeEvaluator`（纯静态、无 IO / 无随机） | D-0008；输入 = 状态账 + 步骤机（白天账）+ 本批事件 + 角色类型目录 |
| 判定时机 | Application 提交前编排：业务事件折完**先判一次** → 未结束才跑触发 / 对账 → **再判一次** | R-0008 事务边界 + 《处决》第 3 步先于第 4 步（游戏已结束就不开呆瓜选择） |
| 初始阵营 | 开局分配随角色一并记录「初始阵营」 | 同 R-0015 / R-0016 的"补全初始条件"，不是运行期耦合（新登记裁定） |
| 镜像双子配对 | 既有 `PersistentEffect`：`Dimension = null`、`Ability = evil-twin.pair`、`Source` = 镜像双子席、`Target` = 对立双子席 | 白拿"来源死亡 / 换角即终止"（= 标记移除时机）与效果链呈现；与女巫诅咒同族 |
| 涡流 | `VortoxNightAction`（其他夜击杀）+ 信息约束标注（只说书人可见的"必须为假" + 归因） | 让涡流作为恶魔可玩；信息内容仍由说书人裁定（D-0002） |
| 呆瓜选择 | `OperationRequest` **来源泛化**："槽位来源 / 触发来源"；触发来源无槽位、不消耗配额 | D-0011 同一原语（无超时、定向推送、重连重投、说书人代填 / 作废）不复制第二套 |
| 公开面 | 结束态对全体玩家广播（胜方 + 原因）；呆瓜的选择是公开事实 | 对局已结束，不存在继续隔离的理由；选择本身就是"公开选择" |

### 3. 边界与依赖方向

- **Kernel**：`GameOutcome` / `OutcomeCondition` / `GameEndedEvent` / `OutcomeEvaluator` /
  `IDemonCatalog`（端口，Rules 实现）；求值器只读账与事件，不改账、不产事件——
  产事件是 Application 的编排职责（同 `SessionSettlement` 的既有分工）；
- **Rules**：`EvilTwinNightAction`（首夜：说书人配对 + 双向互认 + 配对效果）、
  `VortoxNightAction`（其他夜击杀）、涡流信息约束、呆瓜触发器；新增 `IDemonCatalog` 实现；
- **Application**：求值点编排、呆瓜选择命令与四道闸、结束态闸、投影与通知；
- **Server / Web**：结束态广播与横幅、呆瓜选择控件、契约镜像；
- **不做（本票边界）**：主谋 / 圣徒 / 小怪宝等跨剧本特殊条件；旅行者；多恶魔 / 善良恶魔剧本；
  涡流的数学家计数口径（R-0004 仍 Open，本票只做"约束 + 归因"的最小记录）；
  麻脸巫婆造成的"重配对 / 创造新镜像双子"（该角色未实现，登记残余）。

### 4. 验收矩阵（定稿）

| # | 场景 | 期望 | 依据 |
|---|---|---|---|
| 1 | 常规 · 恶魔死亡 | 最后一名恶魔死亡 → 善良获胜；`GameEndedEvent` 进流；结束后一切命令被拒 | 机制 1 / 5 |
| 2 | 常规 · 仅剩两名存活 | 邪恶获胜（旅行者不计，首版无旅行者） | 机制 1 |
| 3 | 镜像双子 · 善良方被处决 | 邪恶立即获胜（看处决事实，不看是否死亡）；镜像双子死后处决善良方不再触发 | 机制 6 / 7 / 8 |
| 4 | 镜像双子 · 双双存活 | 恶魔全死也不结束（阻断善良获胜）；处决邪恶方则游戏继续 | 机制 4 |
| 5 | 涡流 · 黄昏 | 白天无人被处决 → 黄昏邪恶获胜；有处决（含"被处决但未死"）则不触发 | 机制 6 / 9 |
| 6 | 呆瓜 · 选择 | 公告时刻开出选择（夜间死亡：黎明；白天死亡：即时）；选邪恶 → 呆瓜阵营落败；选善良 → 继续；邪恶呆瓜选邪恶 → 善良获胜 | 机制 11 |
| 7 | 呆瓜 · 边界 | 处决已致游戏结束 → 不开选择；选择未了结时白天动作与开夜被拒；说书人可代填；跨重启保持 | 机制 12；D-0011 |
| 8 | 涡流 · 信息 | 涡流在场时镇民信息标"必须为假"+ 归因；该标记只说书人可见 | 机制 10；D-0012 |
| 9 | 视角 | 结束面对全体玩家一致可见（胜方 + 原因）；无关玩家在结束前看不出端倪；无越权字段 | D-0012 §4.3；验收规程 §4 |
| 10 | 重连 / 重放 | 结束后重连拿到同一结论；重放只折事件、不重算 | D-0010 |
| 11 | 契约闸 | 三个角色开闸后可开白天 / 开夜；其余白天相关角色仍显式拒绝 | 架构 §2.6 同族 |

## 决定与依据

- 规则来源：见「机制清点」逐条；不确定性一律登记 `docs/standard/rulings.md`（新条目 R-0023 起）；
- 事件是唯一事实来源：D-0010；账边界：D-0015；内核确定性：D-0008；投影强制：D-0012；
- 操作请求无超时 / 说书人兜底：D-0011 / D-0014；恒定夜晚节奏不适用于触发型选择：D-0013 的边界；
- 公开生死面与公告时点：R-0022（呆瓜的"得知死亡"以公告为准）；
- 处决 ≠ 死亡与处决关注面：百科《处决》· 2026-10-01 抓取。

## 已落地与运行证据

### 交付物（代码）

| 层 | 落地内容 |
|---|---|
| Kernel | `GameOutcome` / `OutcomeCondition` / `GameEndedEvent` / `OutcomeContext` / `IWinConditionFacts`（角色事实端口）/ `OutcomeEvaluator`（纯函数求值：特殊 → 常规 → 镜像双子阻断；观测不齐不猜）；呆瓜账目 `KlutzChoiceRecord` + `KlutzChoiceMadeEvent` / `KlutzChoiceSkippedEvent`；请求来源 `OperationRequestOrigin`（槽位 / 触发两种）+ `OperationRequestOriginKind`；`StepMachine` 结束态闸（结束后一切输入 `GameEnded` 拒绝，重复结束事件显式抛错）；`StepSlotEntry`（槽位进入与推进，拆出以满足单文件 600 行门禁） |
| Rules | `WinConditionFacts`（端口实现）/ `EvilTwinAbility` + `EvilTwinNightAction`（首夜配对：只列对立阵营、落 `Dimension=null` 的配对效果、双向互认）/ `VortoxNightAction`（夜间击杀）/ `VortoxInterference`（在场判定 + 信息注记）/ `KlutzChoiceTrigger`（公告时点开触发型请求、能力不生效可归因跳过、答题 / 作废收口、幂等）；`ClockmakerNightAction` 与 `DreamerNightAction` 的「信息必假」分支 |
| Application | `SessionCommit`（草案 / 求值 / 结束事件 / 回执重放 / 诊断日志）、`SessionQueries`（重连包数据面）分离；提交管线改为 **① 先判 → 未结束才跑触发与对账 → ② 再判**（《处决》第 3 步先于第 4 步）；已结束的批次只做账实一致的收尾（`SessionSettlement.ReconcileHousekeeping`，不跑事件触发）；`CommandGatePipeline` 新增 `phase.game_ended` 与 `phase.trigger_choice_pending` 两道闸；开局分配补初始阵营（R-0023） |
| Server / Contracts / Web | 结束面与呆瓜选择的全链路下发：`GameOutcomeDto` / `KlutzChoiceDto`（带序号）、玩家 / 说书人视图字段、`ReceiveGameEnded` / `ReceiveKlutzChoiceMade` 推送、投影与契约镜像、玩家端结束横幅（`player-outcome`）+ 公开选择列表（`player-klutz-choices`）、说书人端结束面板（`storyteller-outcome`）+ 同一份列表、状态条显示触发型卡点原因 |

### 裁定

`docs/standard/rulings.md` 新增 **R-0023**（开局即写初始阵营）· **R-0024**（判定时点与优先级：业务事件后先判、触发与对账后复判、特殊优先于常规、同层善良胜、一局只结束一次、**没有任何恶魔角色时不判**）· **R-0025**（镜像双子配对 = `Dimension=null` 常驻效果；两名双子都存活且来源生效时善良无法获胜；善良方**被处决**即邪恶获胜）· **R-0026**（涡流黄昏判定挂在白天关闭事件上；「被处决」以处决事实为准，不看死活；要求涡流存活且清醒健康）· **R-0027**（呆瓜选择在公告时刻开出、无槽位、候选为开出时存活者、允许代填、未了结时白天动作与开夜被拒）· **R-0028**（涡流在场的信息必假 = 平台打标 `MayBeFalse` + 注记，内容仍由说书人裁定）。

### 门禁（本次运行，冻结版本）

| 门禁 | 结果 |
|---|---|
| `dotnet build OpenClockTower.slnx` | 0 警告 / 0 错误 |
| `dotnet test OpenClockTower.slnx` | **427 通过 / 0 失败**（Kernel 206 · Rules 122 · Integration 76 · NormativeGates 23）；较本票开工前 +31 |
| `dotnet format OpenClockTower.slnx --verify-no-changes` | 无输出（干净） |
| `npm run gate`（typecheck + lint + vitest + build） | 89 项前端用例通过 |

### 本次新增回归测试

| 文件 | 覆盖 |
|---|---|
| `tests/OpenClockTower.Kernel.Tests/OutcomeEvaluatorTests.cs` | 常规两条（含同层善良胜）、无恶魔不判、观测不齐不猜、镜像双子阻断与解除、善良方被处决即邪恶胜、处决邪恶方不结束、涡流黄昏（有处决 / 醉酒不触发）、呆瓜两种落败方向与选善良无事、特殊优先于常规、双方特殊同层善良胜 |
| `tests/OpenClockTower.Kernel.Tests/StepMachineGameEndTests.cs` | 结束事件折叠成结束态、结束后一切输入被拒、重复结束显式失败、呆瓜账目折叠与重复拒绝 |
| `tests/OpenClockTower.Rules.Tests/KlutzChoiceTriggerTests.cs` | 黎明开请求（候选只含存活）、白天死亡即时 / 夜晚等黎明、能力不生效可归因跳过、已记录幂等、答题产出选择事实、作废产出跳过 |
| `tests/OpenClockTower.Rules.Tests/WinConditionAbilitiesTests.cs` | 镜像双子只列对立阵营 + 配对效果 + 双向互认、不生效不产出、涡流击杀与不生效、钟表匠信息打标、筑梦师信息退回自由填写 |
| `tests/OpenClockTower.Integration.Tests/WinLossHostTests.cs` | 真宿主：涡流黄昏 → 邪恶获胜 + 结束后命令被拒 + 玩家视图 / 重连快照同源；呆瓜被处决 → 公开选择开出（触发来源、候选不含自己）→ 选中邪恶 → 结束 + 旁观玩家看到公开选择与结论 |

### 真机装置（真宿主 + 真浏览器 + 真 SQLite）

| 装置 | 结果 |
|---|---|
| `tools/verify-winloss.mjs`（本票新增，5 席：涡流 / 呆瓜 / 畸形秀演员 / 女巫 / 筑梦师） | **20/20 通过**：开夜 → 过夜 → 开白天 → 呆瓜自我提名 + 3 票过半 → 处决 → 公开选择（候选 `seat:1,3,4,5`）→ 选中邪恶 → 玩家端结束横幅 + 公开选择记录 + 说书人端同一结论 → 结束后玩家操作被拒 `phase.game_ended` → 旁观席位 24 条推送无越权字段；截图 `artifacts/web/winloss-01..03`（已目视复核） |
| `tools/verify-storyteller-panel.mjs`（主装置，157 项） | **157/157 通过**（见下方「同族对齐」） |
| `tools/verify-madness.mjs` / `verify-witch.mjs` / `verify-zero-trust.mjs` | 28/28 · 28/28 · 43/43 通过 |

### 同族对齐（本票的连带改动）

新胜负条件一旦生效，**旧装置与旧集成用例里的小席位夹具会在死亡后当场满足真实的胜负条件**——那些是规则正确行为，不是缺陷。整族处理如下：

| 位置 | 处理 |
|---|---|
| `verify-storyteller-panel.mjs` 默认花名册 3 席 → **5 席**（+畸形秀演员 +呆瓜，均为不在夜晚顺序表、也不会被诺-达鲺毒到的外来者，首夜 13 槽位与中毒归因面不变）；「行 1 合法选项数量」改为按席位数派生 | 三席夹具在白天处决后即满足「仅剩两名存活 → 邪恶获胜」，第二夜再也开不起来 |
| 该装置「第三夜上报诺-达鲺死亡」→ **上报换角（换成涡流）** | 唯一恶魔死亡 = 善良获胜，游戏会当场结束，后续依赖作废 / 重建 / 重连步骤全跑不到；换角同样让常驻中毒终止进摘要，且场上仍有恶魔 |
| `MadnessPunishmentHostTests` 4 席 → 5 席（+呆瓜）、`Mutant_NightPunishment` 补第 3 / 第 5 票；`CerenovusAssignments` 的恶魔从被处罚席位移开 | 原夹具里被处罚 / 被杀死的就是唯一恶魔，或两次死亡后只剩 2 人存活 |
| `DayPhaseHostTests.FullDayFlow` 3 席 → 4 席（+呆瓜） | 处决一人后仅剩 2 人存活 |
| `WitchCurseHostTests.Assignments` 被诅咒者从 4 号（诺-达鲺）改为 4 号（筑梦师），恶魔移到 2 号 | 咒杀唯一恶魔 = 善良获胜 |
| `StepDigestHostTests.HeldSlot_ShowsReleaseReason` / `SettlementHostTests.PoisonedDreamer` | 靠 `SessionSettlement.ReconcileHousekeeping` 修好：已结束的批次不再跑事件触发，但账实一致的收尾（常驻效果终止 / 维度解除）仍要落地，否则终局账与效果链自相矛盾 |

### 残余与后续

- 涡流的信息必假**只做到"打标 + 归因"**：内容仍由说书人裁定（R-0028）；引擎级的"涡流干扰计数"（R-0004 的 `MalfunctionKind.Vortox`）不在本票范围——已立票 `todo/vortox-interference-counting.md`。
- 呆瓜候选 = 开出时刻的存活席位；平台首版没有旅行者，故不做"旅行者能否被选"的分支。
- 「没有任何恶魔角色 → 不判」是平台口径（防配置错误被静默判成一局结束，R-0024）；`麻脸巫婆`把恶魔变成非恶魔之后的口径已由 R-0029 落定，见 `done/pit-hag-character-change.md`。
- 恶魔 → 恶魔 / 恶魔 → 非恶魔的角色变更路径已在装置里被真实走过（换角后常驻效果按来源失去能力终止），`麻脸巫婆`已有票据（`done/pit-hag-character-change.md`），S&V 其余角色变更族见 `in-progress/character-change-family.md`，跨剧本部分见 `future/cross-script-extension.md`。
- 呆瓜「公开选择」列表在真机装置里只覆盖了"选中邪恶"这一支；"选善良 → 无事发生"由集成与规则测试覆盖。

### 独立对抗性复核（前台子代理，只读，硬时间盒 10 分钟）

复核确认 3 条缺陷、1 条存疑；逐条处置如下（全部已修 + 已补回归）：

| 编号 | 严重度 | 缺陷 | 处置 |
|---|---|---|---|
| F-1 | 高 | **强推越过白天会静默丢弃挂起的呆瓜请求**：`DayStepMachine.ForceAdvance` 只产 `DayClosedEvent` + 推进事件（夜间路径 `StepMachine.HandleForceAdvance` 有补作废，白天漏了），而槽位推进会清空 `PendingRequest` → 触发器既看不到请求、也看不到记录 → **下一个黎明重复开选择，终局后果可被反复重掷** | `DayStepMachine` 强推前显式产 `OperationRequestVoidedEvent`（D-0014 / R-0027 第 4 条）；`KlutzChoiceTrigger` 的请求认领改为"先认挂起请求，认不到就按**自己派生的请求标识** `klutz:{seat}` + 该席位此刻确为已死呆瓜"回收（否则推进清空后仍认不到）；回归：`DayForceAdvanceTests`（内核）+ `KlutzChoiceTriggerTests.VoidedRequest_AfterSlotAdvance_StillRecordsTheSkip` |
| F-2 | 中 | **跳过记录的原因（醉酒 / 中毒 / 被谁作废）全量下发给每一名玩家**：说书人专属维度经 `PlayerView.KlutzChoices.Detail` 泄漏 | `GameProjection.ForSeat` 对跳过记录改用公开文案（"呆瓜本次没有做出选择"），说书人视图保留完整原因；回归：`GameProjectionOutcomeTests.SkippedKlutzChoice_PlayerView_HidesTheReason`（含 wire 形状与"说书人面不裁剪"对照） |
| F-3 | 中 | **游戏结束时挂起的请求不被作废**：终局快照永久携带答不了的死信（重连会重投给玩家，而一切提交都被 `phase.game_ended` 拒） | 玩家投影在结束态不再下发任何请求（`GameProjection.ForSeat` 的 `ended` 闸）；回归：`GameProjectionOutcomeTests.EndedGame_PlayerView_DoesNotPushAPendingRequest`（含"结束前会下发"的对照）。**残余**：事件流里那条请求仍未落作废事件（要把它并进结束批次需重排"结束 → 触发管线"的顺序），已立票 `done/ended-game-pending-request-void.md` |
| F-4 | 低 / 存疑 | `HandleVoid` 没有 `HandleResponse` 那样的"非槽位来源旁路" | 复核者自判**当前不可达**（触发请求只在白天未走完时开出，而完成白天计划的两条路都会先清空请求）；已由麻脸巫婆票复核：判定**可达**并修复（`edd0273`），见 `done/pit-hag-character-change.md` |

复核同时验证了这些**未发现问题**的面：结束后无绕过路径（闸 + 内核双层，Hub 全部写路径只有一个入口）、一局只结束一次、求值"观测不齐不判"与优先级、涡流黄昏取值（不读夜间处罚处决）、重放只折事件不重算、客户端不含自算游戏状态。

### 效率（用户当场指出）

主装置（5 席 + 35 图 + 三个夜晚）单次 210–314 秒，本轮为收敛一个夹具假设连跑 3 次——全是可以避免的重复固定开销。处置：

- `AGENTS.local.md` 新增「验证成本纪律」：**改动的验证阶梯**（单用例 → 单测试项目 → 装置；装置一批只跑一次，且只用于界面 / 真机链路或收尾冻结版本）；装置报红先在秒级用例里复现同一判断，不在分钟级装置里试错；
- `tools/verify-winloss.mjs` 增加迭代快参数 `--skip-build`（复用 Release 产物）与 `--no-screenshots`（跳过 PNG 落盘）：实测 **117 秒 → 24.7 秒**，20 项断言全过；
- 主装置的快通道用既有 `--quota`（排查用 `--quota 0.3`，正式取证用默认值）。
