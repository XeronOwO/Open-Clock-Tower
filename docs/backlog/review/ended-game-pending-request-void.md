# 结束批次作废挂起请求：终局事件流不留死信

- Status: Review
- Priority: Medium
- Depends on: 胜败判定与游戏结束（`done/win-loss-and-game-end.md`）；操作请求与四道兜底闸（`done/operation-request-step-machine.md`）
- 来源：胜负票独立对抗性复核 F-3 的残余（`done/win-loss-and-game-end.md`）

## 要解决的问题

游戏结束的**同一批提交**里如果还有挂起的操作请求，事件流里没有任何作废事实：

1. 终局快照永久携带一个再也答不了、也撤不掉的请求——重连会把死信重投给玩家，
   而一切提交都被结束闸拒绝（`phase.game_ended`）；
2. 审计与重放自相矛盾：投影层已把它藏起来（玩家端看不到），但事件流读起来"请求还在挂起"；
   按 D-0010「事件是唯一事实来源」，这是账实不一致；
3. 触发阶段（R-0024 的②次判定）产出的结束同样可能留下这一类死信。

现状（胜负票 F-3 的处置）只挡住了**投影**：`GameProjection.ForSeat` 在结束态不下发请求。
本票补的是**事件流事实**：结束批次里把挂起请求显式作废并写明原因——
实现期做同族检查后，把「等待说书人的裁定点」这一同类挂起也一并收口（见矩阵行 7）。

## 验收矩阵（定稿）

| # | 场景 | 期望 | 依据 |
|---|---|---|---|
| 1 | ①先判即结束、快照里有挂起请求 | 同一批产出 `OperationRequestVoidedEvent`，快照里的挂起被清掉（折成 `Voided`，`IsHeld == false`） | D-0010 / D-0014 |
| 2 | ②触发后复判才结束、期间新开出的请求 | 同样作废，不留在结束之后 | R-0024 |
| 3 | 请求在结束前已正常了结 | 不产生多余作废事件 | 幂等 |
| 4 | 重连 / 重放 | 重连包里没有请求；重放只折事件、不重算 | D-0010 / D-0012 |
| 5 | 事件顺序 | 作废事件在 `GameEndedEvent` 之前，折进步骤机后可重放 | D-0010 |
| 6 | 日志 | 作废记录带游戏局、请求标识、收件席与原因 | 可观测性 |
| 7 | 结束批次遇挂起裁定点（同族） | 同批以「本局已结束」收口（`DecisionPointResolvedEvent`，`Decision = null`），快照 `AwaitingDecision == null` | R-0024 / D-0010 |
| 8 | 系统专属作废原因不接受手动使用（同族） | 客户端手动传 `GameEnded` 被合法性闸拒绝（`legality.reason_invalid`），请求不被改动 | D-0012 / 审计真实性 |

**行 2 的可达性（实现期核查结论）**：当前规则面下不存在「② 复判结束且请求仍 `Pending`」的真实路径——
提名闸在触发型请求未了结时封死提名（`phase.trigger_choice_pending`）；白天计划只有一个 `DayWindow` 槽、
不签发请求；派生死亡只有女巫诅咒，而咒杀死者的「提名者」若是恶魔就不可能是呆瓜；诅咒需存活 >3 才生效，
故一次咒杀不可能把存活压到 ≤2。该行由**唯一收口点**（①/② 共用）与下列真机证据共同覆盖：
② 复判结束本身由 `SecondEvaluationEnd_ClosesTheBatchThroughTheSameEndingPath` 在真宿主上跑通，
「触发型请求在结束态被作废」由 `TriggerRequestPendingAtEnd_IsVoided_WhenTheEndComesFromAnotherDeath` 跑通。

## 决定与依据

- 顺序调整的来源：胜负票复核结论——「把它并进结束批次需重排『结束 → 触发管线』的顺序」。
  落点：`SessionCommit.AppendGameEnding` 在追加唯一结束事件**之前**先作废挂起请求，两条事件同批落库；
  这是①/②两条判定路径的**唯一**收口点（`GameSession` 里只有一个调用）。
- 请求出口：D-0011 硬约束 3「请求一直有效，直到被响应、或被上游状态变化作废」——游戏结束正是上游状态变化；
  兜底口径沿用 D-0014（作废必须可审计、可重放）。
- 作废原因：新增 `OperationRequestVoidReason.GameEnded`（系统专属）。既有 `Enum.IsDefined` 单闸会把
  "已登记"等同于"可手动选择"，因此合法性闸改为 `IsManuallySelectableVoidReason`：系统专属原因只由结束批次派发。
- 裁定点收口（同族）：`AwaitingDecision` 在结束后同样再也答不了（一切输入被 `phase.game_ended` 拒），
  以 `DecisionPointResolvedEvent`（`Decision = null` + 说明）收口——与强推越过裁定点的既有姿态一致（D-0014）。
- 呆瓜口径：结束批次作废触发型（呆瓜）请求时**不再补** `KlutzChoiceSkippedEvent`——结束批次不重跑触发管线
  （《处决》第 3 步先于第 4 步），而该记录的两个作用（防下一次黎明重复开选择、公开"没选"）随终局消失；
  作废事实由 `OperationRequestVoidedEvent` 承载。测试显式锁定这条口径。
- 投影层不动：`GameProjection` 的结束闸保留（双保险），本票只补事件流、快照与日志。

## 已落地与运行证据

### 交付物（代码）

| 层 | 落地内容 |
|---|---|
| Kernel | `OperationRequestVoidReason.GameEnded`（系统专属原因，带依据注释） |
| Application | `SessionCommit.AppendGameEnding`：挂起请求作废 → 挂起裁定点收口 → 唯一结束事件；两条定位日志（作废：game/request/addressee/reason/说明；裁定点：game/decisionPoint/说明；结束：game/winner/condition/detail）。`GameSession` 调用点替换（①/② 共用；文件保持 ≤600 行门禁内） |
| Application（闸） | `CommandGatePipeline.IsManuallySelectableVoidReason`：`Enum.IsDefined` **且**非系统专属；拒绝码沿用 `legality.reason_invalid` |
| Web | `labels.ts` 补 `GameEnded → 本局已结束`（说书人"最近作废"与玩家"请求已作废"文案同源）；`labels.spec.ts` 补断言；`GrimoireSeatConsole.vue` 下拉注释改为"仅可手动选择的原因" |

### 回归测试（真宿主 + 真 SignalR + 真 SQLite）

`tests/OpenClockTower.Integration.Tests/EndedGamePendingRequestVoidTests.cs`：

| 用例 | 覆盖 |
|---|---|
| `FirstEvaluationEnd_VoidsPendingSlotRequest_InTheEndingBatch` | 夹具夜晚挂起槽位请求 → 上报最后一名恶魔死亡：同批作废、作废先于结束、快照折成 `Voided` 且 `IsHeld == false`、玩家推送与重连包无死信、说书人看到作废原因、重启只折事件重放一致、日志带请求标识与原因；并断言手动传 `GameEnded` 被拒（行 8） |
| `SecondEvaluationEnd_ClosesTheBatchThroughTheSameEndingPath` | 女巫咒杀被诅咒的最后一名恶魔：死亡是触发管线产出（紧跟提名事件）、② 复判结束、结束事件是本条流最后一条、无多余作废事件（行 2 的可达面、行 3） |
| `TriggerRequestPendingAtEnd_IsVoided_WhenTheEndComesFromAnotherDeath` | 呆瓜白天死亡开出的触发型请求，在「上报最后一名恶魔死亡」时同批作废；不补跳过记录（显式锁定口径） |
| `PendingDecisionPointAtEnd_IsResolvedInTheEndingBatch` | 夹具裁定槽位挂起 → 结束批次以「本局已结束」收口、快照 `AwaitingDecision == null` 且 `IsHeld == false`（行 7） |

`tests/OpenClockTower.Integration.Tests/WinLossHostTests.cs` 补行 3 断言：请求已作答而结束的那一批，
**答题序号之后**没有多余作废事件（夜晚强推自身的作废不在此列）。
`tests/OpenClockTower.Integration.Tests/TestNightPlan.cs` 增加 `CreateDecisionFirstNight` 夹具（无选项 + `StorytellerDecides`）。

### 先红后绿（两轮）

- 先红（实现前）：`dotnet test tests/OpenClockTower.Integration.Tests --filter FullyQualifiedName~EndedGamePendingRequestVoidTests`
  → **失败 2 / 通过 1**；失败信息：`玩家没有收到作废推送`、`Assert.Single() Failure: The collection did not contain any matching items`（触发型请求未落作废事件）。
- 先红（对抗复核的两处修正前）：同一过滤 → **失败 2 / 通过 2**；失败信息：`Expected: "Rejected" Actual: "Accepted"`（手动 `GameEnded` 未设闸）、结束批次无 `DecisionPointResolvedEvent`。
- 后绿：同一过滤 → **4/4 通过**。

### 门禁（冻结版本）

| 门禁 | 结果 |
|---|---|
| `dotnet build OpenClockTower.slnx` | 0 警告 / 0 错误 |
| `dotnet test OpenClockTower.slnx` | **465 通过 / 0 失败**（Kernel 226 · Rules 135 · Integration 81 · NormativeGates 23） |
| `dotnet format OpenClockTower.slnx --verify-no-changes` | 退出码 0、无输出 |
| `npm run gate` | 90 项前端用例通过 + typecheck / lint / build 成功 |

### 独立对抗性复核（前台只读子代理，硬时间盒 10 分钟）

覆盖面：结束路径遗漏、误报/重复作废、顺序与重放、通知面、呆瓜口径、② 不可达论证、枚举与契约。
**结论：无 blocker**；1 major + 2 minor，全部已在合入前处置：

| 级别 | 发现 | 处置 |
|---|---|---|
| major | `GameEnded` 自称系统专属，但服务端只由 `Enum.IsDefined` 放行——前端下拉是唯一约束 | 合法性闸加 `IsManuallySelectableVoidReason` + 行 8 真机拒绝用例 |
| minor | 终局快照仍可能残留 `AwaitingDecision`（同类死挂起，且说书人视图显示为可操作） | 结束批次同批收口 + 行 7 真机用例 |
| minor | 作废 `Note` 里混入枚举 slug（`DemonsAllDead` 等） | 改中文：`本局已结束（善良/邪恶阵营获胜）：请求不再有意义` |

复核同时确认（未发现问题的面）：收口点唯一且覆盖①/②；与 `SeatDependencyCheck` / 强推既有作废**不会**重复；
`[作废, 结束]` 折叠与重启恢复自洽；作废通知的收件人解析在「同批新开」与「此前挂起」两条路径上都成立；
② 不可达论证的前提核实成立；新增枚举对 DTO / 前端标签 / 规范门禁无越权或镜像缺口。

## 边界与同类检查

- **同类挂起三件套**：挂起请求（本票收口）、等待裁定点（本票收口）、**阻塞报警 `Block`**（尚未收口）。
  `Block` 只在进入/推进槽位的事件里被清空，内核没有"只清阻塞"的原语；伪造推进事件去顺手清它不是正路。
  已立票 `todo/terminal-hold-residue.md`（Low，先定原语再谈落点）。
- **结束批次不跑触发管线**：① 结束走 `ReconcileHousekeeping`（不跑事件触发）、② 结束只落已经跑完的对账结果——
  本票的收口只追加"作废/裁定点了结/结束"三类事件，不改这条时序（《处决》第 3 步先于第 4 步）。
- **投影行为不变**：`GameProjection` 的结束闸继续保留；本票不改玩家可见字段的形状。

## 残余与后续

- `Block` 收口见 `todo/terminal-hold-residue.md`。
- 行 2 在当前规则面下不可达（论证见上），其构造留待未来出现"触发管线产出的结束 + 同批新开请求"的真实路径时补真机用例。
