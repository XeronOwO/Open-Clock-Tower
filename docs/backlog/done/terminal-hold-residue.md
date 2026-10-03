# 终局残留挂起：阻塞报警的收口

- Status: Done
- Priority: Low
- 验收：批次 E19（2026-10-03）矩阵 1–8 全部通过（`docs/acceptance/batches.md`；逐行证据见下方「E19 验收判定」）。
- Depends on: 结束批次作废挂起请求（`done/ended-game-pending-request-void.md`）；操作请求与四道兜底闸（`done/operation-request-step-machine.md`）
- 来源：结束批次票实现期的同族检查 + 该票独立对抗性复核

## 要解决的问题

同类挂起有三件：挂起操作请求、等待说书人的裁定点、阻塞报警（`StepMachineState.Block`）。
前两类已由 `done/ended-game-pending-request-void.md` 在结束批次收口；**`Block` 仍会随终局快照残留**：

1. `IsHeld` 保持 `true`——"结束后一切输入被拒"（R-0024）与"状态仍自称挂起"自相矛盾；
2. 说书人视图显示一条永远处理不了的阻塞原因（`BlockedReason`），终局面板上是一块死控件；
3. 重放/重建会把这条残留状态原样折回来，账实一致但语义是死的。

**根因**：内核的三种挂起里，前两类各有一条对偶收口事件（`OperationRequestVoidedEvent` /
`DecisionPointResolvedEvent`），而 `Block` 只有置位事件 `SlotBlockedEvent`——**内核没有"只清阻塞"
的原语**。`Block` 只在进入 / 推进槽位的事件折叠时被顺带清空，而伪造推进事件会把槽位位置与最小配额
一起改掉，投影与快照立刻分叉。

**可达性**：`Block` 由 `SlotBlockedEvent` 置位（空槽位却有存活持有者且无行动契约 / 行动槽位缺
行动者或契约 / 提示求值落到 `BlockAndAlert`）；游戏结束的常见入口是说书人上报状态
（`ApplySeatStateCommand` 不受阶段限制），所以"阻塞还在、上报最后一名恶魔死亡"可以走到。

**影响面**：只说书人视图（玩家投影不含 `Block`，无玩家侧死信）——因此优先级 Low。

## 验收矩阵（定稿）

| # | 场景 | 期望 | 依据 |
|---|---|---|---|
| 1 | 结束批次遇阻塞报警 | 阻塞被显式收口（事件 + 原因），快照 `Block == null` 且 `IsHeld == false` | R-0024 / D-0010 |
| 2 | 结束批次无阻塞 | 不产生多余事件 | 幂等 |
| 3 | 重放 / 重启 | 只折事件可重放出同一终局状态（不带阻塞报警） | D-0010 |
| 4 | 收口事件只清阻塞 | 槽位下标、最小配额、挂起请求、裁定点、白天账、胜负结论一个都不动 | D-0011 硬约束 4（可重放快照） |
| 5 | 说书人视图 | 终局 `BlockedReason == null`（死控件消失），结束横幅照常 | D-0013 / 观感 |
| 6 | 事件顺序 | 收口事件排在 `GameEndedEvent` 之前，且结束事件是本批最后一条 | D-0010 |
| 7 | 日志 | 收口记录带游戏局、槽位、原阻塞原因与说明 | 可观测性 |
| 8 | 顺序损坏（内核契约） | 没有阻塞却解除 / 槽位对不上 / 还没有阶段 → 显式抛错，不静默继续 | D-0014 能力 3 |

## 决定与依据

- **原语定稿：候选 A——新增 `SlotUnblockedEvent { SlotId, Reason }`**（与 `SlotBlockedEvent` 对偶，
  折叠只把 `Block` 置空）。取舍理由：三类挂起里另两类都已有对偶事件，本例的根因是**内核词汇缺口**；
  候选 B（结束批次专属事件）会把内核词表绑在应用层某一条流程上，缺口仍在，属于打补丁。
  事件是唯一事实来源（D-0010）："这条报警在某原因下被解除"本身就是一条该落库的事实。
- **落点唯一**：`SessionCommit.AppendGameEnding` 已是 ①/② 两条判定路径的唯一收口点
  （`GameSession` 里只有一个调用）。顺序：作废请求 → 收口裁定点 → 解除阻塞 → 唯一结束事件。
- **不伪造推进**：结束批次不得用 `SlotAdvancedEvent` / `SlotForceAdvancedEvent` 顺手清阻塞——
  那会把步骤机位置与最小配额一起改掉，正是本票第 3 条问题的反面。
- **投影层不改**：`GameProjection.BlockedReason` 读的就是 `machine.Block?.Reason`；折叠清空后自然为
  null，本票只补事件流、快照与日志（与上一票"投影层不动、结束闸保留双保险"同姿态）。
- **快照形状不动**：`StepBlock` 不新增字段（`SlotId` 由事件携带，折叠时与当前槽位比对校验），
  避免旧快照反序列化 compat 风险。
- 依据：R-0024（结束后一切输入被拒）、D-0010（事件是唯一事实来源）、D-0011 硬约束 4（快照可重放）、
  D-0014（兜底入口与显式失败）、`docs/backlog/done/ended-game-pending-request-void.md`
  （三件套同族收口与唯一收口点论证）。

## 已落地与运行证据

### 交付物（代码）

| 层 | 落地内容 |
|---|---|
| Kernel | `SlotUnblockedEvent { SlotId, Reason }`（`src/OpenClockTower.Kernel/SlotUnblockedEvent.cs`）：与 `SlotBlockedEvent` 对偶，语义 = 只把 `Block` 置空 |
| Kernel（折叠） | `StepMachineFolder.ApplyUnblock`：只清 `Block`，位置 / 配额 / 挂起请求 / 裁定点一律不动；没有阻塞、槽位与当前槽位对不上、还没有阶段 → 显式抛错（D-0014 能力 3）。`GameStateMachine` 把新事件登记为账外事件（`=> current`），恢复链路的第二个折叠器不再把它当未知类型 |
| Application | `SessionCommit.AppendGameEnding` 第三段收口：作废请求 → 收口裁定点 → **解除阻塞** → 唯一结束事件；日志带游戏局 / 槽位 / 原阻塞原因 / 说明 |
| 投影层 | 不改代码：`GameProjection.BlockedReason` 读 `machine.Block?.Reason`，折叠清空后自然为 null（前端阻塞格本就以 `blockedReason` 为条件渲染） |
| 测试夹具 | `TestNightPlan.CreateBlockedFirstNight`：空槽位却绑着一名存活持有者的角色（这一格没有行动契约）→ 走生产置位路径产出 `SlotBlockedEvent` |

### 回归测试

内核 `tests/OpenClockTower.Kernel.Tests/SlotUnblockTests.cs`（5 条）：解除只清阻塞（与
`blocked with { Block = null }` 全等，`IsHeld` 由 true 变 false）/ 槽位对不上抛错 / 本来就没有阻塞抛错 /
还没有阶段抛错 / 状态账折叠 no-op。

真宿主 `tests/OpenClockTower.Integration.Tests/TerminalHoldResidueTests.cs`（2 条；真宿主 + 真 SignalR + 真 SQLite）：

| 用例 | 覆盖 |
|---|---|
| `BlockedSlotAtEnd_IsUnblocked_InTheEndingBatch` | 夹具阻塞（结束前说书人视图确实挂着 `BlockedReason`、快照 `Block != null` 且 `IsHeld`）→ 上报最后一名恶魔死亡：同批产出 `SlotUnblockedEvent`（指向被阻塞槽位、原因含「本局已结束」）、序号 < `GameEndedEvent` 且结束事件是本批最后一条、快照 `Block == null` / `IsHeld == false` 且槽位下标与配额未动、说书人视图 `BlockedReason == null`、日志带槽位与原阻塞原因；**把整条事件流交给内核重折一遍**（含新事件的序列化往返）得到同一终局；同库重启后说书人 / 玩家视图同样不再有阻塞面 |
| `EndingWithoutBlock_EmitsNoUnblockEvent` | 同一夹具拆掉阻塞面（计划里绑的角色没有存活持有者）→ 结束批次不产生多余事件 |

### 先红后绿

- 先红（把结束批次的收口段临时关掉）：`dotnet test tests/OpenClockTower.Integration.Tests --filter FullyQualifiedName~TerminalHoldResidueTests`
  → **失败 1 / 通过 1**；失败信息 `Assert.Single() Failure: The collection did not contain any matching items`
  （事件流里没有解除阻塞事件）。
- 后绿（同一过滤）：**2/2 通过**；`--filter FullyQualifiedName~SlotUnblockTests`：**5/5 通过**。

### 门禁（冻结版本）

| 门禁 | 结果 |
|---|---|
| `dotnet build OpenClockTower.slnx` | 0 警告 / 0 错误 |
| `dotnet test OpenClockTower.slnx` | **597 通过 / 0 失败**（Kernel 269 · Rules 208 · Integration 97 · NormativeGates 23） |
| `dotnet format OpenClockTower.slnx` | 退出码 0（就地格式化，未改写任何文件） |

`npm run gate`：本票无 `web/` 改动，按门禁规则跳过。

### 同轮整族检查

- **同类挂起三件套**：挂起请求 / 等待裁定点 / 阻塞报警——三条现在都有对偶收口事件，结束批次一段不留
  （本票补齐第三条）。
- **三个置位点同源**：`SlotBlockedEvent` 的三个产生点（空槽位有存活持有者且无契约 / 行动槽位缺行动者或
  契约 / 提示求值落 `BlockAndAlert`）折出的是同一个 `Block`，收口与置位路径无关。
- **事件计数订正**：`docs/architecture/current.md` 的 `GameEvent` 计数是陈旧值（写 25、实际 40），随本票
  订正为 41（`Select-String ': GameEvent'` 逐条核对，命中全部落在 `*Event.cs`，无注释误命中）。

## E19 验收判定（2026-10-03）

冻结版本 `main` @ `fc9a41f`（跑批时工作树与该提交一致，跑批期间未改产品代码）；批次记录见
`docs/acceptance/batches.md`。本批运行：`TerminalHoldResidueTests` 2 条 + `SlotUnblockTests` 5 条全过；
主装置取证档 **177 项 / 0 跳过**（39 张截图均为本次运行写入，113.5s）作为终局面与重连面的真机回归。

| # | 结论 | 本次运行的证据 |
|---|---|---|
| 1 | 通过 | `BlockedSlotAtEnd_IsUnblocked_InTheEndingBatch`：同批 `SlotUnblockedEvent`（槽位 `test-seat-1`、原因含「本局已结束」），快照 `Block == null` 且 `IsHeld == false` |
| 2 | 通过 | `EndingWithoutBlock_EmitsNoUnblockEvent`：事件流里有唯一 `GameEndedEvent`、无 `SlotUnblockedEvent` |
| 3 | 通过 | 同一用例：整条事件流（含新事件的反序列化）交给内核重折 → 终局无阻塞；同库重启后说书人 / 玩家视图一致 |
| 4 | 通过 | 内核 `Unblock_ClearsOnlyTheBlock`（与 `blocked with { Block = null }` 全等）+ 集成用例断言槽位下标与配额未动 |
| 5 | 通过 | 同一用例：结束前 `BlockedReason` 非空，结束后为 null；结束横幅（`Outcome`）照常 |
| 6 | 通过 | 同一用例：解除事件序号 < `GameEndedEvent` 序号，且结束事件是本批最后一条 |
| 7 | 通过 | 同一用例：宿主日志含「结束批次解除阻塞报警」+ 槽位 + 原阻塞原因 |
| 8 | 通过 | 内核 `Unblock_WithMismatchedSlot_Throws` / `Unblock_WithoutBlock_Throws` / `Unblock_WithoutPhase_Throws` |

**界面级取证**：本票改的是终局快照与说书人视图的阻塞面。真机花名册走不到「空槽位却绑着存活持有者、
而这一格没有契约」这类数据缺陷，因此装置不新增段；本批用主装置取证档（含结束态与重连面）做回归，
行 1–8 的行级证据来自真宿主集成用例（真 SignalR + 真 SQLite + 真投影）。

## 残余与后续

- 本票只做「结束批次收口」。`SlotUnblockedEvent` 目前唯一产出点是结束批次；若将来出现"说书人在任意
  时刻解除阻塞报警"的产品需求，它就是现成的落点（届时补说书人命令面与合法性闸，不在本票范围）。
