# 疯狂与裁定式处决：洗脑师 + 畸形秀演员

- Status: Done
- Priority: High
- Depends on: 白天阶段（`done/day-phase.md`）；事件触发 / 能力存续管线与提示标记投影（`done/witch-curse.md`）；内核疯狂要求载体（`done/kernel-domain-model.md`，R-0003）；同批后续：心上人 / 理发师（死亡触发家族）另立票

## 要解决的问题

白天链路已经能跑（提名 / 投票 / 计票 / 处决），女巫把「夜晚施加 → 白天触发」打通，但**两名与白天正相关的角色仍然开不了局**：`DayActions.IsCovered` 只认女巫，洗脑师与畸形秀演员在场时开白天被 `legality.day_contract_missing` 显式拒绝；洗脑师还挂在夜晚顺序表上，开夜先被 `plan.contract_missing` 拒。后果不是"少两个角色"：

1. **洗脑师的核心玩法没有载体**：夜里点名"谁必须装成什么角色"、白天不装就可能被处决。内核里 `MadnessRequirement` 与 `MadnessRequirementIssuedEvent` 只有类型、折叠与呈现，没有任何产生方与终止方；
2. **畸形秀演员的"可能被处决"不是任何既有流程的变体**：它需要一条说书人在**任意时刻**（含夜晚、含提名阶段之外）主动处决的命令面。现有处决只存在于 `CloseDay`（计票 → 处决）一条路径，没有任何"绕过提名直接处决"的入口；
3. **处决上限的账实一致没有被表达**：处罚处决要与「每个白天最多一次处决」共账（处罚计入），夜晚处罚又是明文例外（当晚可处决、次日仍可有处决）。不把这层关系写进事件与折叠，就会出现"处罚完还能再处决一次"这类账实不符，且重放无法发现。

## 机制清点（来源：百科 · 2026-10-01 抓取）

| # | 环节 | 规则 | 来源区域 |
|---|---|---|---|
| 1 | 洗脑师能力 | 「每个夜晚，你要选择一名玩家和一个善良角色。他明天白天和夜晚需要"疯狂"地证明自己是这个角色，不然他可能被处决。」 | 《洗脑师》· 角色能力 |
| 2 | 选人范围 | 一名玩家 + 一个镇民 / 外来者角色；「任意玩家」含自己与已死亡玩家（《重要细节》三-1） | 《洗脑师》· 运作方式 / 角色简介 2；《重要细节》三-1 |
| 3 | 目标知情 | 唤醒被选玩家，依次展示「该角色的能力对你生效」+ 洗脑师标记 + 要证明的角色标记 | 《洗脑师》· 运作方式 |
| 4 | 提示标记 | 放置：夜晚行动并选择后；**若洗脑师当时醉酒 / 中毒则不放置**。移除：下一个夜晚的黎明，或洗脑师死亡 / 离场 | 《洗脑师》· 提示标记 |
| 5 | 跨夜转移 | 第二夜转移标记时，**上一夜的目标仍然疯狂到黎明**（那时才解除） | 《洗脑师》· 提示标记第 15 条 |
| 6 | 处罚处决 | 下一个白天或夜晚，目标没尽最大努力 → 说书人可处决；**向所有玩家宣布死亡**；白天常规处决前处决 → 直接进入夜晚；每个白天最多一次处决，**惩罚计入** | 《洗脑师》· 运作方式 / 角色简介 7 / 规则细节 7 |
| 7 | 畸形秀演员能力 | 「如果你"疯狂"地证明自己是外来者，你可能被处决。」 | 《畸形秀演员》· 角色能力 |
| 8 | 任意时刻 | 认为他在以任何方式证明自己是外来者即可处决——**提名阶段之外，甚至夜晚**都可以 | 《畸形秀演员》· 角色简介 5 |
| 9 | 白天处罚 | 发生在白天常规处决前 → 直接进入夜晚；"正常情况下这意味今天白天不会再有其他人被处决"（每个白天只有一次处决） | 《畸形秀演员》· 角色简介 5 / 运作方式 |
| 10 | **夜晚处罚的明文例外** | 若他在**夜晚**暗示自己是畸形秀演员，当晚可处决——**即使当天白天已有人被处决**；死亡照常宣布、夜晚正常继续；**明天仍然可能发生一场处决** | 《畸形秀演员》· 角色简介 6 |
| 11 | 死亡目标 | 洗脑师可以选中已死亡的玩家；已死亡的目标仍可能因不够疯狂被处决（目标死亡不解除疯狂要求） | 《重要细节》三-1；《洗脑师》· 运作方式 10 |
| 12 | 死亡的畸形秀演员 | 死亡后失去能力，不再受"疯狂"后果约束 | 《疯狂》· 术语介绍 |
| 13 | 引擎不判定疯狂 | 说书人对"是否疯狂"有最终裁决权；引擎只负责产生要求、计时提醒、按裁定触发后果 | R-0003；《疯狂》· 术语介绍 |
| 14 | 暗示自己疯狂 | 玩家明示或暗示自己处于疯狂状态 = 没有做出疯狂行为，说书人可据此处罚 | 《疯狂》· 术语介绍 |

## 实施方案（2026-10-02 架构审视）

### 1. 现状与缺口

- **要求载体**：`MadnessRequirement` 已挂在 `SeatStateEntry.Madnesses` 上，并已投影到说书人端（`SeatStateDto.Madnesses` → 魔典「疯狂」标记 / 账本面板 / 每步摘要）。缺的是：稳定标识、来源（谁的能力、施加时是什么角色）、存续窗口与终止事件；
- **处决路径**：`DayMachine.CloseDay` 是唯一路径，`ExecutedEvent.DayNumber` 必填，`DayLedgerFolder` 强制"每白天最多一次"；夜晚处决没有任何表达方式；
- **命令面**：`StepMachine.Handle` 只认既有输入族；说书人没有主动处决入口；
- **提示形态**：`ChoicePrompt` 只有一维选项（女巫选人 / 筑梦师选人）；洗脑师需要"玩家 × 善良角色"两维选择，现有前端只渲染扁平单选列表。

### 2. 目标域模型

| 概念 | 落点 | 理由 |
|---|---|---|
| 疯狂要求 | 扩展既有 `MadnessRequirement`（沿用既有说书人投影与前端标记） | 已有载体缺的是身份 / 来源 / 存续，不是"要不要存在"；不另起第二套生命周期账（D-0015：一个写入方） |
| 要求身份 | `MadnessRequirementId`（`{PlanLabel}:{SlotId}:madness`） | 终止事件按身份认人；与效果标识同族，重放稳定 |
| 要求来源 | 席位 + 施加时角色 + 能力 `cerenovus.madness` | 来源死亡 / 换角 → 立即解除（走与 `PersistentEffect` 同一条折叠传播，不新开传播口） |
| 存续窗口 | `ExpiresAtDay`：施加夜的**次日白天 + 其后夜晚**，下一个黎明到期 | 《洗脑师》提示标记移除时机 + 第 15 条；到期由对 `DayStartedEvent` 的触发收口（`NoLongerApplies`） |
| 终止 | 复用 `EffectTermination`，新增 `MadnessRequirementTerminatedEvent` + 折叠 | 与效果终止同一套分类与措辞（`SourceDied` / `SourceLostAbility` / `NoLongerApplies` / `StorytellerVoided`） |
| 处罚处决 | 内核 `AdjudicatedExecutionMachine` + 命令 `PunishExecutionCommand` | 说书人主动处决是独立于计票的另一条路径；白天形态要收口白天、夜晚形态不动阶段 |
| 处决记录 | `ExecutedEvent` 扩展为 `{ DayNumber: int?, Kind, Note }`；`DayRecord` 记 `ExecutedKind` | 处决 ≠ 死亡不变；夜晚处决无白天可归，但事实必须进事件流并可重放 |
| 两维选择 | `ChoicePrompt.SecondaryOptions` + 组合答案 `{primary}\|{secondary}` | 一次选择里的两个指针是**原子行为**；拆成两次请求会引入"答了一半"的挂起状态，重放与作废语义都要跟着改 |

### 3. 边界与依赖方向

- **Kernel**：要求模型与折叠、终止事件、来源失效传播、到期触发契约；`ExecutedEvent` / `DayRecord` 扩展；`AdjudicatedExecutionMachine`（纯计算，读账与座次，产出事件）。
- **Rules**：`CerenovusAbility`（口径唯一出处）、`CerenovusNightAction`（提示 + 结算）、`CerenovusRequirementTrigger`（到期收口）；`DayActions` 覆盖位登记两名角色；`NightActions` 登记洗脑师；`RoleContracts` 登记触发器。畸形秀演员没有夜晚行动、没有提示标记，只登记白天覆盖位。
- **Application / Server / Web**：`PunishExecutionCommand` 四道闸 + 派发；`OperationRequestDto.SecondaryOptions`（含前端契约镜像）；说书人处罚控件 + 洗脑师两维选择渲染；目标知情走既有「信息类结果」（收件人 = 目标），不新增玩家投影字段。
- **不静默跳过的两条出口**：
  1. 还有未计票的提名时，白天处罚**显式拒绝**（`day.nomination_not_counted`，沿用 `ForceAdvance` 的 F-2 口径：不替说书人拍板计票结论）；
  2. 能力依据不成立（要求不存在 / 已终止 / 来源不生效；畸形秀演员不在场、已死或不清醒健康）时**显式拒绝**（`punishment.*`），不产出任何事件。

### 4. 处罚处决的账实语义（R-0020 的代码侧落点）

- **白天处罚**（存在进行中的白天）：消耗当天处决上限（`DayRecord.Executed`），随后 `DayClosedEvent` + 槽位收口——与 `CloseDay` 同一形状，夜晚照常开始；
- **夜晚处罚**：不推进阶段、不写白天账、不占任何白天的上限；`ExecutedEvent.DayNumber = null`（事实留在事件流，说书人视图可见）；
- **目标已死亡**：只记「被处决」，不重复记死亡（`ExecutedEvent` 照写，`SeatStateChangedEvent` 不产出）——与 `CloseDay` 的同名口径一致；
- **死亡原因**：`Reason = "cerenovus.madness"` / `"mutant.madness"`（机器可读 + 人可读说明），归因到能力来源。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 洗脑师夜晚选人 + 选角色 | 生效时目标席位出现疯狂要求（说书人魔典标记 + 账本面板），目标收到私密告知（内容含要证明的角色与后果）；无关玩家收包无任何相关字段 | 规则 / 内核用例 + 装置（魔典截图 + 玩家收包扫描） |
| 2 | 洗脑师行动当时醉酒 / 中毒 | 不产生要求、不给目标发信息（同女巫"不放置标记"）；槽位照走、原因进流 | 规则 / 内核负向用例 |
| 3 | 白天处罚处决（洗脑师） | 目标死亡 + `ExecutedEvent` 进流、白天立即结束（进夜晚）；当天不能再处决 | 内核 + 集成用例 + 装置 |
| 4 | 夜晚处罚处决（畸形秀演员，当天白天已处决过） | 仍可处决；夜晚不被打断；**次日白天仍可正常处决**（不占次日上限） | 内核用例 + 装置 |
| 5 | 白天处罚处决（畸形秀演员） | 直接进入夜晚；`DayRecord.Executed` 被占用 | 内核 + 集成用例 |
| 6 | 已死亡目标被处罚处决 | 只记「被处决」、不重复记死亡；白天形态照常消耗上限 | 内核用例 |
| 7 | 要求存续与撤销 | 施加夜的次日白天 + 其后夜晚有效；下一个黎明到期（`NoLongerApplies`）；洗脑师死亡 / 换角立即终止（`SourceDied` / `SourceLostAbility`）；洗脑师醉酒期间要求保留但不生效 | 内核 + 规则用例 + 装置效果链 |
| 8 | 拒绝面 | 未计票提名 / 当天已处决 / 无要求（或来源不生效）/ 畸形秀演员不在场、已死、不清醒健康 → 显式拒绝且状态不变；重复投递幂等 | 内核 + 集成负向用例 |
| 9 | 视角与隔离 | 说书人视图含要求（牌面标记与操作台显示要证明的角色；来源 / 期限见残余）与处决归因；玩家视图无新增说书人专属字段；被处决与「谁被洗脑」对无关玩家不可见 | 集成投影用例 + 装置收包扫描 |
| 10 | 契约闸 | 两名角色在场时开夜 / 开天不再被拒；其余白天相关角色仍 `legality.day_contract_missing`；其余夜晚角色仍 `plan.contract_missing` | 规范门禁测试 + 规则 / 集成用例 |
| 11 | 重启 / 重连 / 重放 | 要求与处决随快照恢复、随事件折叠；重放等价；进行中的白天在重启后仍可处罚 | 集成用例 + 装置 |

> **2026-10-02 修正（R-0022）**：行 9 的"被处决……对无关玩家不可见"指**处罚理由与来源**
> （疯狂要求、谁被洗脑）；处罚处决造成的**死亡**自本日起进入公开生死面——玩家端只看到"谁死了"，
> 看不到"为什么被处决"（衔接 `docs/backlog/done/player-death-announcement.md`）。

## 决定与依据

- 规则来源：百科《洗脑师》《畸形秀演员》《疯狂》· 2026-10-01 抓取（区域见机制清点表）；《重要细节》三-1 · 2026-10-01 抓取（"任意玩家"含自己与已死亡玩家）。
- 裁定登记：`docs/standard/rulings.md` R-0020（处罚处决与处决上限 / 阶段收口）、R-0021（疯狂要求的产生、存续窗口与在线选择形态）。
- 引擎不判定疯狂：R-0003；效果与要求的挂起 / 终止口径：R-0012；事件唯一事实来源：D-0010；投影强制：D-0012；账边界与唯一写入方：D-0015；说书人兜底与无超时：D-0011 / D-0014。
- 白天不替说书人拍板计票：`done/day-phase.md` 对抗性复核 F-2（强推与未计票提名）。

## 已落地与运行证据（2026-10-02，批次 E12）

**实现**：

- Kernel：`MadnessRequirement` 扩展（标识 / 来源席位 + 施加时角色 / 能力 / 到期日 / 终止事实）与
  `MadnessRequirementTerminatedEvent` + 折叠；来源死亡 / 换角与持续型效果共用同一条失效传播；
  `ExecutedEvent` 扩展 `Kind` 与可空 `DayNumber`、`DayRecord.ExecutedKind`；`AdjudicatedExecutionMachine`
  （白天占上限 + 立即收口；夜晚不写白天账、不推进阶段；已死亡目标只记被处决）；`ChoicePrompt` 两维选择
  与共用拆分规则；`GameState` 增加要求生效判定。
- Rules：`CerenovusAbility`（口径唯一出处）、`CerenovusNightAction`（提示 + 结算 + 目标私密告知）、
  `CerenovusRequirementTrigger`（黎明到期）、`CerenovusMadnessPunishment` / `MutantMadnessPunishment`
  （处罚依据契约）；`DayActions` 覆盖位登记两名角色、`NightActions` 登记洗脑师、`RoleContracts` 登记
  触发器与两条处罚依据。
- Application / Server / Web：`PunishExecutionCommand`（四道闸 + 幂等 + 审计）、Hub `PunishExecution`、
  `OperationRequestDto.SecondaryOptions` 与玩家端两维选择渲染（含防御性归一化）、说书人席位操作台
  「处罚处决」控件与疯狂要求行。

**门禁（冻结树）**：

| 门禁 | 结果 |
|---|---|
| `dotnet build OpenClockTower.slnx` | 0 警告 0 错误 |
| `dotnet test OpenClockTower.slnx` | **379/379**（Kernel 179 / Rules 110 / Integration 68 / NormativeGates 22） |
| `dotnet format OpenClockTower.slnx --verify-no-changes` | exit 0 |
| `npm run gate`（typecheck + lint + vitest 88 + build） | exit 0 |

**装置（批次 E12，2026-10-02）**：

| 装置 | 结果 |
|---|---|
| `tools/verify-storyteller-panel.mjs`（主装置回归） | **139 项断言全通过 + 34 张截图** |
| `tools/verify-zero-trust.mjs`（负向回归） | **42 项断言全通过** |
| `tools/verify-madness.mjs`（**新增**，4 席：真宿主 + 两个真浏览器 + 三席 Node SignalR 客户端） | **27 项断言全通过 + 5 张截图** |

（三个装置自报的条数见各自日志，不手抄；`madness-run.log` 最后一行是 `全部通过（27 项）`。）

日志 `artifacts/web/batch-run.log`、`zero-trust-run.log`、`madness-run.log`（gitignored，可重生成）；
截图 `madness-01-two-dimensional-request.png` … `madness-05-cerenovus-view.png` 已目视复核：
①洗脑师真玩家页面出现两维选择（4 个席位选项 + 17 个镇民 / 外来者选项，2 号与「博学者」已选）；
②魔典 2 号牌面出现疯狂要求、操作台给出「处罚处决（洗脑师）」控件与 R-0020 提示；
③夜晚处罚 4 号（畸形秀演员）后牌面翻死亡、归因写着 `mutant.madness`、夜晚仍在继续（槽位 9/13）；
④白天处罚 2 号后牌面翻死亡、`st-day` 显示「第 1 天 · 已结束 · 已处决：2 号」；
⑤洗脑师本人页面只有公开事实，没有要求 / 效果链字样。

**验收矩阵逐行**：1 洗脑师选人（`Resolve_Effective_*` + `Prompt_*` + 装置 ①② + 目标收包正断言）·
2 醉酒 / 中毒不产要求（`Resolve_Ineffective_ProducesNothing`）· 3 白天处罚（`DayPunishment_ConsumesTheDailyLimit_AndClosesTheDay` + 集成 `CerenovusMadness_DayPunishment_...` + 装置 ④）· 4 夜晚处罚不占次日（`Mutant_NightPunishment_DoesNotConsumeTheNextDay` + `Punishment_DuringANight_...` + 装置 ③ 与「白天账无执行记录」断言）· 5 白天处罚（畸形秀演员，同一命令面）· 6 已死亡目标（`Punishment_OnADeadSeat_RecordsOnlyTheExecution`）· 7 存续与撤销（`Termination_IsRecordedAndIrreversible` + `SourceDeathOrRoleChange_...` + `TargetDeath_DoesNotTerminate` + `Trigger_ExpiresOnTheNextDawn_...` + `IsOperative_...` + 集成到期断言）· 8 拒绝面（`Rejections_AreExplicit` + `DayPunishment_WithAnOpenNomination_...` + `DayPunishment_WhenTheDayAlreadyExecuted_...`）· 9 视角与隔离（集成 wire 扫描 + 装置 ⑤ 与两席收包扫描）· 10 契约闸（`Catalog_...` + `OnlyImplementedDayContractsAreCovered` + 集成开夜 / 开天受理）· 11 重启 / 重放（`RestartDuringTheMadnessDay_...` + `StepMachineStateComparerTests` + 折叠负向用例）。

## 对抗性复核处置（2026-10-02，独立上下文只读复核）

| 发现 | 严重度 | 处置 |
|---|---|---|
| F-1 说书人视图只下发「要证明的角色」，来源 / 期限未呈现，与本票矩阵行 9 的措辞冲突 | Med | **按实际口径改**：矩阵行 9 改为"牌面标记与操作台显示要证明的角色 + 处决归因"；`SeatStateDto` 注释与残余写明来源 / 期限只活在账本与事件流、面板暂不呈现（模型字段已就位） |
| F-2 `StepMachineStateComparer` 没比 `ExecutedKind`，新增字段逃过重放等价断言 | Med | **已修**：补 `ExecutedKind` 与 `SecondaryOptions` 两处比较；新增 `StepMachineStateComparerTests` 两条"改一个字段必须不等价"的用例 |
| F-3 内核「夜晚处罚不推进阶段」的断言落在目标分支之外（假绿） | Med | **已修**：改为真实夜晚计划（两个空槽位），断言 `SlotIndex` 不变、无 `SlotAdvancedEvent` / `PhaseCompletedEvent`、白天账未物化；顺带修掉 `StepMachineFolder` 把 null 白天账物化成空账的问题 |
| F-4 「白天已关、夜晚未开」间隙被记成夜晚处罚 | Low | **登记口径**：R-0020 增补第 6 条（该窗口按夜晚形态处理并写明理由），不再算静默默认 |
| F-5 到期触发器只处理批内第一条 `DayStartedEvent` | Low | **已修**：按批内最晚一天一次性到期；新增 `Trigger_HandlesMultipleDayStartsInOneBatch` |
| F-6 两维拆分规则在内核与规则层各写一份 | Low | **已修**：收进 `ChoicePrompt.TrySplitAnswer`，`IsLegalAnswer` 与 `CerenovusNightAction` 共用 |
| F-7 旧库缺 `Kind` 的 `ExecutedEvent` 无法反序列化（升级兼容） | Low | **记录取舍**（见残余）：不做旧库迁移 |

**残余（留给后续）**：

- **说书人面板暂不呈现要求的来源与到期日**：模型里 `Source` / `SourceCharacter` / `ExpiresAtDay` 已就位、
  事件流可审计；跨夜多条并存时牌面只能看到"要证明的角色"，靠操作台标记与事件流区分。
- **不做旧库迁移**：`ExecutedEvent.Kind` 是 required，升级前落盘的处决事件形状不含它；本项目定位小圈子自用、
  无在线对局迁移要求，重建请在全新库上进行——这是显式取舍，不是遗漏。
- 洗脑师与哥布林的相克（跨剧本）不实现；《疯狂》家族其余角色随各自剧本另立票。
- 心上人 / 理发师（死亡触发家族）与博学者 / 艺术家 / 杂耍艺人（白天信息家族）仍按 `done/day-phase.md`
  的残余分批实现。
