# 重建报告对比状态账

- Status: Todo
- Priority: Medium
- Depends on: 自动步骤机与操作请求（`done/operation-request-step-machine.md`）；说书人上帝视角·第二片（`todo/storyteller-step-insights.md`）

## 要解决的问题

`RoomRebuildReport` 目前只对比**步骤机状态**（`MachineEquivalent` / `SnapshotEquivalent`）：
一次重建可能报"一致"，而状态账（`GameState`）已经与事件流分叉——它是同一条事件流上的
另一个派生视图，却没有被对比过。说书人拿到的"重建成功"因此是半个结论。

## 要做的事

1. 新增 `GameStateComparer`：等价比较 `Seats`（逐维度含值 / 原因 / 导致方）、
   `PersistentEffects`（含终止事实）、`InstantaneousEffects`，不依赖集合的枚举顺序。
2. `RoomRebuildReport` 增加状态账等价字段，并在重建时计算。
3. 说书人回执与日志带上该字段；不一致时显式报告，不许静默继续。
4. 回归：脏状态账 → 重建后报告 false 且派生数据被修回；干净流 → true；损坏流 → 仍显式失败。

## 验收矩阵

| # | 场景 | 期望 | 依据 |
|---|---|---|---|
| 1 | 状态账与事件流一致 | 重建报告标记状态账等价 | D-0010 |
| 2 | 状态账分叉（派生数据被改脏） | 报告不一致；重建把派生数据修回与事件一致 | D-0014 能力 3 |
| 3 | 事件流损坏 | 重建显式失败，不返回"等价"假结论 | D-0014 能力 3 |
| 4 | 重启恢复 | 重放后的状态账与重建对比结论一致 | D-0010 |

## 决定与依据

- 事件是唯一事实来源、当前状态是可重建的派生结果：D-0009 / D-0010
- 重建失败显式报错、不静默继续：D-0014 能力 3
- 票据来源：`todo/storyteller-step-insights.md` 残余第 5 条
