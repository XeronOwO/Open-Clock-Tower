# 终局残留挂起：阻塞报警的收口

- Status: Todo
- Priority: Low
- Depends on: 结束批次作废挂起请求（`done/ended-game-pending-request-void.md`）；操作请求与四道兜底闸（`done/operation-request-step-machine.md`）
- 来源：结束批次票实现期的同族检查 + 该票独立对抗性复核

## 要解决的问题

同类挂起有三件：挂起操作请求、等待说书人的裁定点、阻塞报警（`StepMachineState.Block`）。
前两类已由 `done/ended-game-pending-request-void.md` 在结束批次收口；**`Block` 仍会随终局快照残留**：

1. `IsHeld` 保持 `true`——"结束后一切输入被拒"（R-0024）与"状态仍自称挂起"自相矛盾；
2. 说书人视图显示一条永远处理不了的阻塞原因（`BlockedReason`），终局面板上是一块死控件；
3. 重放/重建会把这条残留状态原样折回来，账实一致但语义是死的。

**可达性**：`Block` 由 `SlotBlockedEvent` 置位（空槽位却有存活持有者且无行动契约 / 行动槽位缺行动者或契约 /
提示求值落到 `BlockAndAlert`）；游戏结束的常见入口是说书人上报状态（`ApplySeatStateCommand` 不受阶段限制），
所以"阻塞还在、上报最后一名恶魔死亡"可以走到。

**影响面**：只说书人视图（玩家投影不含 `Block`，无玩家侧死信）——因此优先级 Low。

## 验收矩阵（草案）

| # | 场景 | 期望 | 依据 |
|---|---|---|---|
| 1 | 结束批次遇阻塞报警 | 阻塞被显式收口（事件 + 原因），快照 `Block == null` 且 `IsHeld == false` | R-0024 / D-0010 |
| 2 | 结束批次无阻塞 | 不产生多余事件 | 幂等 |
| 3 | 重放 / 重启 | 只折事件可重放出同一终局状态 | D-0010 |

## 决定与依据

- **内核没有"只清阻塞"的原语**：`Block` 只在 `SlotEnteredEvent` / `SlotAdvancedEvent` / `SlotForceAdvancedEvent`
  折叠时被清空（`StepMachineFolder.ApplyAdvance` 一并清 `PendingRequest` / `AwaitingDecision` / `Block` 并重置配额）。
  结束批次不该伪造"推进"事件去顺手清它——那会把步骤机的位置与配额一起改掉，投影与快照立刻分叉。
- **先定原语再落点**：候选 A——新增 `SlotUnblockedEvent { SlotId, Reason }`（与 `SlotBlockedEvent` 对偶，
  两个折叠器都要登记）；候选 B——结束批次专用收口事件。定稿时要按"事件是唯一事实来源"与"一规则一个触发点"取舍，
  并同步 `StorytellerView.BlockedReason` 的投影行为。
- 依据：R-0024（结束后一切输入被拒）、D-0010（事件是唯一事实来源）、D-0014（兜底入口与显式失败）、
  `docs/backlog/done/operation-request-step-machine.md`（四道兜底闸与"兜底入口永远开着"）。
