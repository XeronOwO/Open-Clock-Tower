# 女巫：夜晚诅咒 → 白天提名咒杀

- Status: Done
- Priority: High
- Depends on: 白天阶段（`done/day-phase.md`）；结算引擎与常驻效果对账（`done/settlement-engine.md`）；同批后续：洗脑师 / 畸形秀演员（另立票）

## 要解决的问题

白天链路（开白天 / 提名 / 投票 / 计票 / 处决）已经完整，但**没有一条角色能力落在白天**：
`DayActions.IsCovered` 恒为 false，于是"与白天相关"的 11 个角色一旦在场，开白天就被
`legality.day_contract_missing` 显式拒绝。后果不是"少一个角色"：

1. 玩家在白天看到的所有事实（提名、票面、处决）只有流程、没有能力面的因果——"某人一发起提名就倒下"
   这类白天事件在平台上根本不可能发生，说书人只能靠口头叙述，事件流里没有对应事实；
2. 「夜晚选择 → 白天触发」这条链路从未走通：现有三名已实现角色（钟表匠 / 筑梦师 / 诺-达鲺）的能力
   结果全部在夜晚内闭环；效果模型里那条 `Dimension = null` 的持续型效果（注释原文写着"保护、**诅咒**等"）
   一直是空位；
3. 女巫的槽位每天都在夜晚顺序表里走，玩家点进去只拿到未实现契约的拒绝——角色在场即开不了局。

## 机制清点（来源：百科《女巫》· 2026-10-01 抓取）

| # | 环节 | 规则 | 来源区域 |
|---|---|---|---|
| 1 | 能力原文 | 「每个夜晚，你要选择一名玩家：如果他明天白天发起提名，他死亡。如果只有三名存活的玩家，你失去此能力」 | 角色能力 |
| 2 | 诅咒范围 | 被诅咒者**下个白天**提名任何人即死；**他的提名仍然生效**（投票与计票照常） | 角色简介 1 |
| 3 | 持续时间 | 诅咒只持续一个白天；女巫可以每晚重复诅咒同一名玩家 | 角色简介 2 |
| 4 | 失去能力 | 只剩三名玩家存活时，诅咒**立即解除**，女巫也无法再进行夜晚行动 | 角色简介 2 |
| 5 | 提示标记 | 女巫夜晚行动后放置「被诅咒」；**女巫当时醉酒 / 中毒则不放置**；移除时机：黄昏、或女巫死亡 / 离场 | 提示标记 |
| 6 | 目标集合 | 「选择一名玩家」含自己与已死亡玩家（同《重要细节》三-1 的口径） | 重要细节 三-1；范例 2 / 提示与技巧 |
| 7 | 触发是自动的 | 「说书人**立即**宣布他死亡」，不是裁量——引擎产出死亡事实，人人可见 | 运作方式 |
| 8 | 不影响流放 | 诅咒不影响发起流放（首版无旅行者，本条不适用） | 范例 5 |

## 实施方案（架构审视）

### 1. 现状与缺口

- 白天：`DayMachine` 是纯迁移（提名 / 投票 / 计票 / 处决），**没有任何"事件 → 能力后果"的求值点**；
  白天窗口不排角色步骤，所以触发型能力无处挂载。
- 夜晚：`IAbilityResolution` 能产出效果事件，但已实现的三名角色产出的都是**夜晚内闭环**的结果；
  「夜晚施加的持续型效果，在下个白天产生后果」这条路径没有任何消费者。
- 对账：`SettlementReconciler` 只管**常驻效果期望**（诺-达鲺的中毒）与**维度重算**
  （中毒 / 醉酒两维）；"能力本身没了，它的效果必须跟着解除"这种情况没有表达方式。
- 契约闸：`DayActions` 只有名单没有实现，`IsCovered` 恒 false。

### 2. 目标域模型

| 概念 | 落点 | 理由 |
|---|---|---|
| 诅咒 | 既有 `PersistentEffect`，`Dimension = null`，`Ability = witch.curse` | 白拿来源归因、醉酒 / 中毒**挂起**（R-0012 的挂号条文举的例子正是女巫）、来源死亡 / 换角**终止**、效果链呈现；不新造状态位 |
| 诅咒标识 | `{PlanLabel}:{SlotId}:curse`（如 `sv:night-2:witch:curse`） | 与诺-达鲺击杀同族（`{PlanLabel}:{SlotId}:kill`）；每夜唯一，重放稳定，不需要代数后缀 |
| 提名即死 | 新增**事件触发契约** `IEventTrigger`（Kernel 出接口，Rules 实现） | 白天窗口不排步骤，触发只能由事件驱动；与 `IStandingEffectSource` 同族：规则层只算后果、不写账 |
| 失去能力（存活 ≤3） | 新增**能力存续契约** `IAbilityPresence`，由 `SettlementReconciler` 收口 | "能力没了 → 效果必须解除"是账实一致问题，与来源死亡同族；必须与常驻效果共用同一个固定点与同一个写入方（D-0015） |
| 黄昏移除 | 触发契约对 `DayClosedEvent` 的反应（终止未终止的诅咒） | 黄昏 = 白天结束到下一夜开始之间；诅咒只活一个白天，此时撤下 |

### 3. 边界与依赖方向

- **Kernel**：`IEventTrigger` / `EventTriggerContext` / `IAbilityPresence` / `AbilityPresenceContext` 四个契约 +
  `EventTriggerReconciler`（有界级联：本轮新事件 → 触发器 → 折账 → 再喂新事件，超限显式抛错，同
  `SettlementReconciler` 的防呆姿态）；`SettlementReconciler` 增加能力存续一趟（与常驻效果同一趟）。
  契约不引用 Rules，纯函数、可重放（D-0008）。
- **Rules**：`WitchNightAction`（提示 + 结算，同族于既有三名角色）、`WitchCurseTrigger`、
  `WitchCursePresence`、`WitchAbility`（存续条件的唯一出处：提示、触发、存续三处共用）、
  目录登记（`NightActions` 加女巫、`DayActions.IsCovered` 开闸、新增触发 / 存续目录）。
- **Application**：`SessionSettlement` 在同一次原子提交里编排「触发 → 常驻 / 存续 / 维度对账」；
  新增一行参数把本批业务事件交给触发管线。`GameSession` 的依赖面与行数**不变**（600 行门禁零余量）。
- **Server / Web**：诅咒是**提示标记**——只说书人可见（魔典牌面标记 + 效果链），玩家端只看到
  "提名被受理"与"该玩家死亡"两个公开事实；投影按既有白名单走，**不需要新的下发面**。
- **白天的死亡不是处决**：`SeatStateChangedEvent`（`Reason = witch.curse`，`CausedBy` = 女巫席位，
  `EffectId` = 该条诅咒）与 `NominationMadeEvent` **同批落库**；不产生 `ExecutedEvent`，
  不消耗"每天最多一次处决"。

### 4. 不静默跳过的两条出口

- 女巫的**能力已失去**（存活 ≤3）：`BuildPrompt` 返回空选项 + `OnNoOption = Skip`（R-0009），
  槽位照走配额、不发请求；跳过原因写进 `PromptSkippedEvent.Reason`（含提示上下文），
  说书人查得到"为什么今晚没把女巫叫起来"。
- **触发条件不成立**（诅咒挂起、目标已死、女巫已失去能力）：返回空事件，**不产出任何后果**，
  也不写"可能发生过"的日志——账上只有事实。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 女巫夜晚选人 | 被选席位出现「被诅咒」持续型效果（生效中）；说书人效果链可见；玩家端收包**无**任何诅咒字段 | 规则 / 内核用例 + 装置（说书人魔典截图 + 玩家收包扫描） |
| 2 | 被诅咒者下个白天发起提名 | 提名被受理且**仍然成立**（票面继续、白天不能提前关）；同一次原子提交里该玩家死亡，不产生 `ExecutedEvent`（死亡事实进事件流、说书人可见；玩家端的死亡公告面见残余） | 内核触发用例 + 集成用例 + 装置 |
| 3 | 未被诅咒者提名 / 女巫不在场 | 无额外死亡，事件流里没有多余状态变化 | 内核负向用例 |
| 4 | 诅咒只活一个白天 | 白天结束（黄昏）时诅咒被终止；下一个白天同一玩家再提名不再死亡 | 内核用例 + 装置效果链「已终止」 |
| 5 | 女巫醉酒 / 中毒 | 行动当时不放置诅咒（能力未生效）；已存在的诅咒**挂起**：期间提名不致死，来源恢复后恢复生效（R-0012） | 规则 / 内核用例 |
| 6 | 只剩三名存活 | 诅咒立即解除（终止原因 = 条件不再满足）；夜晚不再向女巫发请求（`PromptSkippedEvent`，配额照走） | 内核 + 规则 + 集成用例 |
| 7 | 女巫死亡 / 换角色 | 诅咒由既有折叠链路终止（`SourceDied` / `SourceLostAbility`） | 内核用例 |
| 8 | 女巫诅咒自己（百科范例 2） | 自己提名 → 自己死亡，提名仍生效 | 内核用例 |
| 9 | 视角与信息隔离 | 说书人视图含诅咒效果与归因；玩家视图不含效果链 / 归因字段；无关玩家在触发前看不出任何征兆 | 集成投影用例 + 零信任装置收包扫描 |
| 10 | 契约闸 | 女巫开闸后不再被拒；其余 10 个白天相关角色在场仍 `legality.day_contract_missing` | 规范门禁测试 + 集成用例 |
| 11 | 重启 / 重连 / 重放 | 诅咒随快照恢复、随事件折叠，重放等价；白天进行中重启后触发仍成立 | 集成用例 + 既有重放等价用例族 |

## 决定与依据

- 规则来源：百科《女巫》· 2026-10-01 抓取（角色能力 / 角色简介 / 运作方式 / 提示标记 / 范例）；
  《重要细节》三-1 · 2026-10-01 抓取（"任意玩家"含自己与已死亡玩家）。
- 挂起与终止：`docs/standard/rulings.md` R-0012（来源醉酒 / 中毒 → 挂起；来源死亡 / 换角 → 终止）。
- 在线口径：`rulings.md` R-0019（诅咒标记的可见面、提名即死的同批时序、黄昏移除）。
- 无超时与说书人兜底：D-0011 / D-0014；事件唯一事实来源：D-0010；账边界与唯一写入方：D-0015；
  投影强制：D-0012。
- 白天契约闸的取舍：`done/day-phase.md` 验收矩阵行 10（未实现即拒绝，不静默跳过）。

## 已落地与运行证据（2026-10-02，批次 E11）

**实现**：

- Kernel：新增两族契约与一条管线——`IEventTrigger` / `EventTriggerContext`（事件触发）、
  `IAbilityPresence` / `AbilityPresenceContext`（能力存续）、`EventTriggerReconciler`（有界级联，
  超限显式抛错）；`SettlementReconciler` 增加「能力存续」一趟（失去能力 → 解除它名下的效果），
  与常驻效果 / 维度重算共用同一个固定点与同一个写入方（D-0015）。
- Rules：`WitchAbility`（一份存续判定，提示 / 触发 / 存续三处共用）、`WitchNightAction`（提示 + 结算：
  施加 `Dimension = null` 的持续型效果）、`WitchCurseTrigger`（提名即死 + 黄昏撤下）、
  `WitchCursePresence`、`RoleContracts` 目录；`NightActions` 登记女巫，`DayActions.IsCovered("witch")` 开闸。
- Application：`SessionSettlement` 在同一次原子提交里编排「事件触发 → 常驻 / 存续 / 维度」；
  `GameSession` 只改一处调用点、行数不变（600 行门禁零余量）。
- Server / Web：不需要新的下发面——诅咒走既有说明人视角效果链，玩家端本来就不含效果字段；
  前端补了「提示标记名」登记（`witch.curse` → 「被诅咒」）与终止原因 `NoLongerApplies` 的中文映射。

**门禁（冻结树）**：

| 门禁 | 结果 |
|---|---|
| `dotnet build OpenClockTower.slnx` | 0 警告 0 错误 |
| `dotnet test OpenClockTower.slnx` | **355/355**（Kernel 167 / Rules 101 / Integration 65 / NormativeGates 22） |
| `dotnet format OpenClockTower.slnx --verify-no-changes` | exit 0 |
| `npm run gate`（typecheck + lint + vitest 87 + build） | exit 0 |

**装置（批次 E11，2026-10-02）**：

| 装置 | 结果 |
|---|---|
| `tools/verify-storyteller-panel.mjs`（主装置回归） | **139 项断言全通过 + 34 张截图** |
| `tools/verify-zero-trust.mjs`（负向回归） | **42 项断言全通过** |
| `tools/verify-witch.mjs`（**新增**女巫装置，4 席真宿主 + 两个真浏览器 + 三席 Node SignalR 客户端） | **26 项断言全通过 + 5 张截图** |

（三个装置自报的条数见各自日志，不手抄：`全部通过（139 项）` / `（42 项）` / `（26 项）`。）

日志 `artifacts/web/batch-run.log`、`zero-trust-run.log`、`witch-run.log`（gitignored，可重生成）；
截图 `witch-01-curse-mark.png` … `witch-05-cursed-player-view.png` 已目视复核：
①魔典 4 号牌面标着「被诅咒·生效中」且仍是「存活」；②效果归因链在生效中；
③提名进入公开账目后 4 号牌面翻「死亡」、诅咒标成「被诅咒·已终止」；
④状态账里那格死亡写明「女巫的诅咒：被诅咒者发起提名即死（提名仍然生效）」、归因 1 号、效果 `sv:night-1:witch:curse`；
⑤被诅咒者自己的页面上只有「4 号 提名 1 号 —— 0 票（投票中）」这条公开事实，没有任何诅咒字样。

**验收矩阵逐行**：1 诅咒落地与说书人可见（装置 ①② + `Resolve_*` 用例）· 2 提名即死且提名成立
（`Trigger_CursedNominatorDies_*` + 集成 `CursedNominator_DiesOnNomination_AndTheNominationStands`：
死亡进账、票面仍开、未计票前 `CloseDay` 被 `day.nomination_not_counted` 拒、且**提名 / 咒杀 / 解除三条事件
序号连续**（同一次原子提交）+ 装置 ③）· 3 未诅咒 / 不在场不触发
（`Trigger_UncursedNominator_ProducesNothing`）· 4 只活一个白天（`Trigger_DayClosed_TerminatesLiveCurse` +
集成 `UntriggeredCurse_IsRemovedAtDusk` + 装置 ④）· 5 醉酒中毒挂起与恢复
（`Trigger_SuspendedCurse_DoesNotKill` + `Trigger_CurseResumesAfterTheSourceSoberUp` +
`Resolve_Ineffective_PlacesNoCurse`，R-0012）· 6 只剩三名存活（`Presence_AliveCountBoundary` +
`Prompt_WhenAbilityLost_SkipsWithoutAnyOption` + 集成「存活降到 3 即解除」）· 7 来源死亡 / 换角终止
（`Curse_IsTerminatedWhenTheWitchDies` + 既有折叠用例族）· 8 自我诅咒（`Trigger_WitchCursingHerself_*`，百科范例 2）·
9 视角与隔离（装置 ⑤ + **三席**收包扫描「无越权字段」+ 集成 wire 断言 + 零信任装置回归）·
10 契约闸（`OnlyImplementedDayContractsAreCovered` + 既有 `StartDay_WithUnimplementedDayRelevantCharacter_IsRejected`）·
11 重启 / 重放（集成 `RestartDuringCursedDay_KeepsTheCurseAndStillTriggers`：白天进行中重启后诅咒仍在账上、
提名仍触发；落库事件流断言咒杀与解除都在，重放只折事件、不重算）。

## 对抗性复核处置（2026-10-02，独立上下文只读复核）

| 发现 | 严重度 | 处置 |
|---|---|---|
| F-1 同批里「施加诅咒 + 白天结束」可能产出"终止一条不存在的效果"，让整条命令 Failed | Med | **加用例锁定**：契约本来就要求调用方**先把业务事件折进账**再进触发管线（`EventTriggerReconciler` remarks 已写明），补 `Trigger_TerminationInTheSameBatchAsApplication_FoldsInOrder` 证明按序折叠成立、不抛错；现实流程不可达（施加在夜晚、`DayClosedEvent` 在白天），不为此加机制 |
| F-2 「只活一个白天」没有直接表示，只靠"白天结束就撤下"这条挂点兜住 | Med | **写成显式边界**：语义落在 `DayClosedEvent` 上（一条规则一个触发点）已写进 `WitchCurseTrigger` remarks；触发器目录因此**必须常驻注册**，缺席时账上会留跨白天的假事实——列入本票残余 |
| F-3 装置条数（139 / 42）与脚本静态 `check(` 调用点数不符 | Med | **假阳性**：装置自报 `results.length`（含循环内断言），本轮日志明写 `全部通过（139 项）` / `（42 项）` / `（26 项）`；票据补上"条数取自日志"，不手抄 |
| F-4 矩阵行 11（重启 / 重放）只有落库事件流断言，没有重启 | Med | **已补运行时证据**：`RestartDuringCursedDay_KeepsTheCurseAndStillTriggers`（白天进行中重启 → 诅咒仍在账上 → 提名仍触发死亡） |
| F-5 「同一次原子提交」没有被断言（两次轮询之间可能插进别的提交） | Med | **已补**：集成断言提名 / 咒杀 / 解除三条事件**序号连续** |
| F-6 装置没扫被诅咒席（4 号）的收包，只扫了女巫席 | Med | **已改**：装置接三席 Node 客户端（女巫 + 两名无关玩家）做收包扫描，禁用词表补 `sv:night-` / `effectId` / `sourceCharacter` / `terminated` 等；被诅咒席走真浏览器（页面文本）＋ 集成 wire 断言 |
| F-7 幂等只写在接口注释，两个实现没有承诺、也没有用例 | Low | **已补**：`Trigger_RepeatedEvaluationOfTheSameNomination_ProducesNothingTwice`；两个实现的 remarks 写明幂等与无副作用 |
| F-8 未登记能力的牌面标记回落成"来源角色名"，与"提示标记"的说法有漂移 | Low | **不改**：回落是有意兜底（`labels.ts` 注释已写）；票据正文把装置证据的措辞收紧到"牌面标记" |
| F-9 `web/AGENTS.md` 里主装置的场景清单被裁剪掉了白天下钻 | Low | **已改回**：恢复「白天提名 / 计票 / 处决」细项（并同步裁剪同文件冗余描述以守住 5120B 门禁） |
| 复核无法判定：行 5 后半段「来源恢复后诅咒继续生效」无用例 | — | **已补**：`Trigger_CurseResumesAfterTheSourceSoberUp`（同一效果挂起 → 恢复生效，非重新施加） |
| 复核无法判定：术语「被诅咒」未登记 | — | **已补**：登记进 `docs/standard/terminology.md` §7 机制术语表 |
| 复核无法判定：`GameSession` 600 行是否仍安全 | — | 门禁阈值是「类 > 600 行」才算违反，600 行本身不触发；本轮行数未变 |

**残余（留给后续）**：

- **玩家端没有死亡公告面**（本票发现的同族缺口）：`PlayerViewDto` 只有阶段 / 请求 / 信息 / 白天公开事实，
  不含任何席位生死；因此夜间击杀与诅咒致死对**玩家**都不可见（现实桌面靠说书人口头宣布）。
  另立票据 `done/player-death-announcement.md`，与本票的"提名即死是公开事实"一并处理；
- 洗脑师 / 畸形秀演员（疯狂后果与裁定式处决，需新增命令面）另立票；
- 镜像双子 / 涡流 / 呆瓜（触发条件即胜负条件，须与胜败判定同批）另立票；
- 心上人 / 理发师（死亡触发家族）与博学者 / 艺术家 / 杂耍艺人（白天信息家族）随各自票据实现；
- 旅行者与流放（R-0007 待需求方决定）——诅咒不影响流放的条文在首版无适用面；
- 一夜之内多次触发同一诅咒（同一玩家当天只能提名一次，当前不可达）；
- **「只活一个白天」靠触发点兜住**：诅咒本身不记"出生在哪一夜"，撤下完全落在
  `WitchCurseTrigger` 对 `DayClosedEvent` 的反应上；因此 `RoleContracts` 必须常驻注册。
  将来若出现"触发器按局开关"这类需求，得先给效果加有效期字段再动（当前单写入方、不可能缺席）。
