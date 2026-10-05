# 结算引擎与能力生效判定

- Status: Done
- Priority: High
- Depends on: 自动步骤机与操作请求（`done/operation-request-step-machine.md`）；说书人上帝视角·第二片（`done/storyteller-step-insights.md`）；《梦殒春宵》夜晚顺序表（已在 `OpenClockTower.Rules` 落地，票在 `done/`）

## 要解决的问题

状态账、效果归因链、两本账、裁定点契约都已就位，但**没有任何东西产出事件**：
没有建表者、没有逐步结算、没有"能力是否生效"的判定。说书人问"这一步为什么是这样"，
平台能查、能折、能显示，但答案没有来源；效果事件没有施加者；两本账没有产生方。
本票把"产生方"建起来——结算引擎是状态账的**唯一写入方**（D-0015）。

## 要做的事

1. **建表**：按剧本的完整夜晚顺序表（`NightOrderTable`）与在场角色展开今晚的步骤序列；
   角色不在场 / 已死亡 / 被跳过 → 生成空槽位、照样走完配额（D-0013 §1）。
   口径变体（原本 / 推荐，R-0014）是引擎输入：首版默认 `Original`；
   说书人选择入口随说书人面板交付（选择结果进事件流、可重放）。
2. **逐步推进**：每一步求值触发条件；需要裁量时暂停等说书人（D-0002 裁定点）。
3. **能力生效判定**：来源中毒 / 醉酒 / 死亡 → 能力不生效（百科《重要细节》三-3；
   挂起语义见 R-0012）；"对中毒玩家使用的能力仍然正常生效"（别人查他，结果正确）。
4. **信息类结果**：说书人自行决定真假，平台只提示"可能错误"，**禁止引擎自动判定**
   （D-0002 + 三-3）。
5. **产出事件**（状态账的唯一写入方）：`SeatStateChangedEvent`、
   `PersistentEffectApplied` / `Terminated`、`InstantaneousEffectApplied`；
   两本账（`AbilityUseLedger` / `MalfunctionLedger`）挂进 `GameState`，不另起状态仓；
   `MadnessRequirementIssuedEvent` 由裁定点链路产出（R-0003）。
6. **维度 → 效果链接**：状态变化事件携带对应 `EffectId`，使说书人面板能回答
   "这一格中毒是哪条效果造成的"，并能提示"来源效果已终止但维度还没解除"（复核 M1 / M2）。
7. **解除维度**：效果终止后，由引擎重算目标维度并产出新的状态变化事件（D-0015 推论 1）。

## 已落地（2026-10-02）

| 项 | 状态 |
|---|---|
| 1 建表 | **已落地**：`NightPlanBuilder`（顺序表 + 状态账 + 席位名单 + 行动契约 → `StepPlan`）；口径记录进 `StepPlan.Variant`（R-0014）；说书人选择入口仍随面板 |
| 前置：角色分配载体 | **已落地**：`AssignCharactersCommand` 进事件流（D-0017），同一条事件记录角色 + 初始生死（R-0015）+ 初始清醒 / 健康（R-0016）；角色唯一在提交时跨批校验 |
| 前置：阶段前事件可重放 | **已落地**：步骤机折叠容忍账事件先于任何阶段（重启恢复路径的硬前提） |
| 角色契约 | **已落地**：提示契约 `INightAction` + 结算契约 `IAbilityResolution`（同一批角色对象同时实现）；首批 = 钟表匠 / 筑梦师 / 诺-达鲺；未实现契约的夜晚角色在场时开夜显式拒绝（架构 §2.6 能力边界），25 个角色的逐角色实现另立票。**2026-10-05 收口**：逐角色实现已全部落地（见残余事项 1），本行"另立票"所指的工作已完成 |
| 2 逐步推进 | **已落地**：`AbilitySettlement` 在「玩家答毕 / 说书人裁毕」后按 `StepSlot.Owner` 取契约结算；信息类先挂一次说书人裁定（`BuildPostChoiceDecision`）；Skip / 强推 / 作废不结算、不记「使用」 |
| 3 能力生效判定 | **已落地**：`AbilityEffectivenessEvaluator`——存活 + 清醒 + 健康 = 生效；中毒 / 醉酒 / 死亡 = 不生效；同时中毒且醉酒记**两条并列**（`MalfunctionKind.Poisoned` + `Drunk`，R-0004 闭合后不再记 `Open`）；维度未观测 → 整条输入被拒绝（不猜） |
| 4 信息类结果 | **已落地**：`InformationResultIssuedEvent`（说书人裁定的内容 + 「可能为假」标记）；引擎不判定真假；标记只说书人可见，玩家只收内容（百科《重要细节》三-1） |
| 5 事件产出与两本账 | **已落地**：`AbilityResolvedEvent` 折进 `GameState.AbilityUses` / `Malfunctions`；`PersistentEffectApplied` / `Terminated` / `InstantaneousEffectApplied` 由角色契约与对账产出 |
| 6 维度 → 效果链接 | **已落地**：`SeatStateChangedEvent.EffectId` 折进 `StateFact<T>.EffectId`；`PersistentEffect.Dimension` 声明压制哪一维 |
| 7 解除维度 | **已落地**：`SettlementReconciler` 固定点对账 + `DimensionEffectReconciler`——效果终止 / 挂起 → 解除，恢复 → 重挂，支持效果迁移 → 换链接；派生事件与业务事件**同一次提交**落库 |

运行证据（2026-10-02）：

| 证据 | 结果 |
|---|---|
| `dotnet build OpenClockTower.slnx` | 0 警告 0 错误 |
| `dotnet test OpenClockTower.slnx` | **226/226**（门禁 14 / 内核 123 / 规则 57 / 集成 32） |
| `dotnet format OpenClockTower.slnx --verify-no-changes` | exit 0 |
| 真实宿主链路（`SettlementHostTests`，矩阵 1–8） | 分配 → 诺-达鲺常驻中毒落账（带 EffectId 与归因）→ 首夜筑梦师请求 → 被毒者能力未生效 → 信息裁定点 → 信息只推给当事人 → 来源死亡即解除并归因；第二夜「中毒 + 醉酒」并存、**两条原因并列进账**（`Poisoned` + `Drunk`；当时记的是 `Open`，R-0004 随后闭合为并列，见残余 5）；重启后两本账、状态账与生效中的效果仍在 |
| 内核与规则用例 | 内核新增 31 条（生效判定 / 结算调度 / 两本账与链接 / 维度重算 / 固定点对账）；规则新增 25 条（花名册档案 / 诺-达鲺常驻毒与夜间契约 / 信息类契约） |
| 单文件 ≤ 600 行 | `GameSession` 超限后拆出 `SessionSettlement`（结算管线），门禁复绿 |

## 验收矩阵

| # | 场景 | 期望 | 依据 | 证据 |
|---|---|---|---|---|
| 1 | 某玩家中毒后其夜间能力结算 | 摘要显示"中毒"、能力**未生效**、归因到施加者 | 百科《重要细节》三-3 | `SettlementHostTests.PoisonedDreamer_IsIneffective_AndReleaseIsAttributed`：2 号 Poison=Poisoned（CausedBy=1、EffectId 非空）→ `LastResolution{Effective=false, Malfunction=Poisoned}`；内核 `SlotSettlementTests.PoisonedActor_ResolvesIneffectiveButStillRecordsUse`。**呈现侧**（「中毒」等中文文案）随残余 2 的面板，本轮证据是引擎给出的分类值与归因 |
| 2 | 玩家同时中毒且醉酒 | 两种状态并存、不相互抵消；能力未生效 | 同上 | `SettlementHostTests.PoisonedAndDrunk_DoNotCancel_AndLedgersSurviveRestart`：两格并存、`Malfunction=["Poisoned","Drunk"]` 两条并列 + Note「同时中毒且醉酒」；内核 `AbilityEffectivenessEvaluatorTests.PoisonedAndDrunk_IsIneffectiveWithBothCauses` |
| 3 | 中毒玩家执行信息类能力 | 显示"信息由说书人决定（可能错误）"，**不自动生成**真假结论 | D-0002 + 三-3 | `SettlementHostTests.PoisonedDreamer_...`：裁定点上下文含「未生效」、内容为说书人自由文本；规则 `InfoResolutionTests.DreamerIneffective_TakesFreeFormInfoFromStoryteller` |
| 4 | 对中毒玩家使用的能力 | 正常生效（查中毒的恶魔仍得正确阵营） | 三-3 | `SettlementHostTests.HealthyDreamerTargetingPoisonedDemon_IsEffective`：健康的筑梦师查中毒的恶魔 → `LastResolution{Effective=true}`、说书人候选 17 个善良角色、**信息内容里出现目标的真实角色（「诺-达鲺」）**；同一场景还钉住 R-0012 的挂起方向（来源中毒 → 目标解除且效果不终止 → 来源恢复 → **同一 EffectId** 重挂） |
| 5 | 中毒来源死亡 / 能力终止 | 状态解除，摘要显示解除原因与对应事件 | 三-3 一-3 / D-0015 | `SettlementHostTests.PoisonedDreamer_...`：效果 `Terminated / SourceDied`、2 号 Poison=Healthy 且 EffectId 指向被终止效果、解除原因含「解除」；内核 `DimensionEffectReconcilerTests.TerminatedEffect_ReleasesDimension` / `SuspendedEffect_ReleasesDimension` |
| 6 | 建表 | 口径变体选择生效；不在场角色的槽位是空槽位、照样走配额 | D-0013 §1 / R-0014 | 由第 1 条交付覆盖：`NightBuildHostTests`（口径随 `PhaseStartedEvent.Plan.Variant` 落库）与 `NightPlanBuilderTests`（空槽位 / 不在场 / 缺事实拒绝）；本轮未回归该链路 |
| 7 | 状态变化 | 每个维度变化都带 `EffectId`（可编程追问"是哪条效果"） | 复核 M1 / M2 | `SettlementHostTests.PoisonedDreamer_...`：**状态事实**与**变化流**（`RecentSeatChanges`）两处都带同一条 EffectId（中毒与解除各一次）；内核 `AbilityLedgerTests.SeatStateChange_KeepsEffectLink` 与 `DimensionEffectReconcilerTests.ReportedFactWithoutLink_GetsLinkFromOperativeEffect` |
| 8 | 两本账 | 能力使用 / 未正常生效写入 `GameState`，重启后仍在 | R-0004 / 架构 §2.2 | `SettlementHostTests.PoisonedAndDrunk_DoNotCancel_AndLedgersSurviveRestart`（换宿主进程、同一库重启后 `AbilityUses` / `Malfunctions` 仍在）；内核 `AbilityLedgerTests` |

### 独立对抗性复核（2026-10-02）

按项目要求，把冻结版本交给独立子代理做**只读**对抗性复核（按指示未复跑 dotnet，结论按文件内容判断）。
其结论是「内核结算与对账骨架逐条读下来**没发现实现级错误**」，报 2 高 / 3 中 / 4 低。本轮逐条处置：

| 复核发现 | 处置 |
|---|---|
| 高 A1：矩阵 7 的义务在说书人视图被投影丢掉——`SeatChangeSnapshot` / `SeatChangeDto` 没有 `EffectId`，面板无法对"变化本身"追问是哪条效果 | 两个类型补 `EffectId` 并透传；宿主链路断言升级为「变化流的 EffectId == 状态事实的 EffectId」 |
| 高 A2：效果终止后同标识的期望永不重回（来源复活 / 换回原角色被永久冻结） | `SettlementReconciler` 引入**世代规则**：已终止的同源效果不复用标识，重新生效产生新的一条（`id#2`、`id#3`…）；R-0012 补第 4 条说明；新增回归 `ExpectationReturningAfterTermination_IsReappliedAsNewGeneration` |
| 中 B1：矩阵 4 的实例"同构但不完全同命题"（未触及「仍得正确阵营」） | 宿主链路补断言：信息内容里出现目标的真实角色（「诺-达鲺」）；矩阵 4 的证据列写明证到了什么 |
| 中 B2：R-0012「挂起 → 恢复后同一 `EffectId` 重挂」只有内核单测 | 矩阵 4 的宿主链路加走「来源中毒 → 解除且不终止 → 恢复 → **同标识**重挂」 |
| 中 B3：`MalfunctionKind` 只以英文枚举名出网，矩阵 1 的「摘要显示中毒」无服务端落点 | 不新增服务端文案层（呈现属面板）；矩阵 1 的证据列改写成"呈现侧随面板"，残余 2 明确包含枚举值中文文案 |
| 低 C1：`MayBeFalse` / `Note` 不下发只是代码事实，没有测试钉住 | 宿主链路对玩家实际收到的 `InformationResultDto` 做序列化断言：不含 `MayBeFalse` / `Note` |
| 低 C2：「说书人上报的无链接事实 + 存在生效效果 → 引擎补链接」无用例 | 新增内核用例 `ReportedFactWithoutLink_GetsLinkFromOperativeEffect` |
| 低 C3：重启证据只覆盖"结算完成后"，未覆盖生效中的效果 | 重启断言补「生效中的 `no-dashii.poison` 效果未终止且仍在」 |
| 低 C4：矩阵与测试的对应只写在类注释里，矩阵 6 本轮零实例 | 三条宿主链路的注释写明覆盖的矩阵行；矩阵 6 的证据列改指第 1 条交付的既有链路，并注明「本轮未回归」 |
| 复核的盲区：静态复核、未实跑 | 运行证据由本会话实跑产出（见上方「运行证据」表）；门禁三条全绿 |

**复核中「已核对通过」的项**：生效判定（三维缺失即拒绝、死亡优先且不带分类）、两本账同源分写
（用过 ≠ 生效过）、判不了就拒绝整条命令、`EffectId` 全链路（事件 → 事实 → 三处投影）、
无链接事实不被误删、终止 / 挂起与 R-0012 一致、对账逐轮折账与失败姿态、幂等、
派生事件与业务事件同批提交、无时间 / 随机 / 哈希序依赖、`StepSlot.Owner` 只存值类型、
三处生产契约都守「未生效不产效果」。

## 验收批次 E2（2026-10-02，真机多客户端会话）

`tools/verify-storyteller-panel.mjs`（说书人 + 三席玩家；57 项断言全过）在真宿主 + 真浏览器 + 真 SQLite 上
补上了**面板与玩家端**这一面的证据：

| 行 | 本批次证据 | 结论 |
|---|---|---|
| 1 | 2 号常驻中毒（归因 3 号、带 `EffectId`）→ 筑梦师结算 `未正常生效 / 原因：中毒`；能力使用账本与失效账本各记一条 | **通过** |
| 2 | 1 号"中毒 + 醉酒"两格并存；钟表匠结算 `未正常生效`，失效账本记 `未定（R-0004）` | **通过** |
| 3 | 筑梦师（未生效）裁定点上下文："能力未生效：信息由你说书人裁定，可以是错的"；说书人自由文本 → 只有 2 号玩家收到内容 + 页面提示"信息可能是错的"；引擎没有自动生成真假 | **通过** |
| 4 | 沿用宿主用例（`SettlementHostTests.HealthyDreamerTargetingPoisonedDemon_IsEffective`）；本批次场景两名镇民都被常驻中毒，未复跑该行 | **通过**（证据沿用） |
| 5 | 上报诺-达鲺死亡 → 两条常驻中毒 `已终止 / 来源死亡`；1 / 2 号状态账由"中毒"变"健康"（带解除说明与同一 `EffectId`） | **通过** |
| 6 | 真机开夜用 `FirstNightRecommended` 建出 13 槽位并自行推进（空槽位照走配额）；口径是面板默认值 | **通过** |
| 7 | 状态账 / 最近状态变化两处都带 `EffectId`（中毒与解除各一次） | **通过** |
| 8 | 面板两本账可见（能力使用 / 失效）；重启后仍在由 `SettlementHostTests` 覆盖 | **通过** |

残余 2 的"每步摘要（StepDigest）"仍由 `docs/backlog/done/storyteller-step-insights.md` 收口（该票已 Done）
（批次 E2 曾把该票的行 1 / 2 / 5 判为不通过）。

## 残余事项（随票，不许消失）

> 2026-10-05 复核：逐条给出**当前状态**——已收口的写收口时间与证据指针，仍有效的保留并写明缺什么。
> 清单不许留着"看起来像待办、其实早已做完"的条目（本票残余 1 / 2 / 4 / 6 都属这一类）。

1. **25 个角色的逐角色实现**（选项生成 + 能力结算 + 相克数据）——**已收口（2026-10-05）**：
   首版花名册 30 个角色三族归属无遗漏（21 个夜晚行动格 + 3 个夜晚触发格 + 13 个白天相关），
   判据在架构 §2.6「能力边界」，由 `tests/OpenClockTower.Rules.Tests/CharacterContractCoverageTests.cs`
   四条断言守住（逐条做过"抽掉一条登记 → 变红"的验证）。相克数据首版零触发面，
   随 `future/cross-script-extension.md`；跨剧本角色的显式拒绝守卫仍在。
2. **说书人面板呈现**——**已收口**：口径选择入口、裁定点上下文、账本与结算结论、效果链接由批次 E2 判过；
   每步摘要（`StepDigest`）由 `done/storyteller-step-insights.md` 交付（`StepDigestDto` 已在契约里）；
   `MalfunctionKind` 等枚举值的中文文案在 `src/OpenClockTower.Application/ReplayText.cs`。
3. **玩家端的角色展示**——**仍有效，已立票**：信息结果已按收件人下发（含重连补齐），
   但玩家投影（`src/OpenClockTower.Contracts/PlayerViewDto.cs`）仍无「本人角色」出口：
   玩家看不到自己拿到什么角色。2026-10-05 由 `todo/player-own-character.md` 接管
   （本条原先只写「随玩家端票据」，而那张票据并不存在）。
4. **疯狂要求的产生方**——**已收口**：洗脑师的夜间契约直接产出
   `MadnessRequirementIssuedEvent`（`CerenovusNightAction`），折叠与重放呈现都在链路里。
5. **R-0004 的路径**——**部分收口**：涡流（`VortoxInterference`）、咖啡师（R-0004 表 2026-10-04 已收口）、
   「同时中毒且醉酒」都已落地——后者按**两条并列**（`MalfunctionKind.Poisoned` + `Drunk`）进账，
   不再记 `Open`（`Open` 现在只表示"原因未定"）；**仍待核对**的是 R-0004 表内「说书人裁定」一行
   （2026-10-04 复核为仍无实现路径）。
6. **胜败判定与处决流程**——**已收口**：由 `done/win-loss-and-game-end.md` 交付（本票当时声明不在范围）。

## 决定与依据

- 自动化边界与说书人裁量：`docs/decisions/active.md` D-0002
- 无超时与挂起语义：D-0011；接管 / 兜底：D-0014
- 状态账边界与引擎义务（含"按仍生效的效果重算维度"）：D-0015 与推论 1
- 夜晚顺序表两口径：`docs/standard/rulings.md` R-0014
- 来源失效时持续型效果挂起：R-0012；开局分配补全初始生死：R-0015、初始清醒 / 健康：R-0016
- 结算流程与建表职责：`docs/architecture/current.md` §2.6；状态账与效果归因链：§2.8
- 数学家计数口径：R-0004
- 诺-达鲺常驻中毒与夜间击杀：百科《诺-达鲺》· 2026-10-01 抓取 · 角色简介 / 运作方式 / 提示标记
