# 重建报告对比状态账

- Status: Done
- Priority: Medium
- Depends on: 自动步骤机与操作请求（`done/operation-request-step-machine.md`）；说书人上帝视角·第二片（`done/storyteller-step-insights.md`）

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
- 票据来源：`done/storyteller-step-insights.md` 残余第 5 条

## 验收证据与结论（2026-10-02，批次 E7）

**门禁**（与健康位票同批真跑）：

| 门禁 | 结果 |
|---|---|
| `dotnet build OpenClockTower.slnx` | 0 警告 0 错误 |
| `dotnet test OpenClockTower.slnx` | 265 通过 / 0 失败（Gates 22 + Kernel 132 + Rules 57 + Integration 54） |
| `dotnet format OpenClockTower.slnx` | 通过（无改动） |
| `cd web; npm run gate` | 通过（typecheck + lint + 55 前端单测 + build） |

**真机取证**：主装置 `tools/verify-storyteller-panel.mjs` **121/121 通过 + 28 张截图**（本票新增
24 / 25 两张；第 11 步同批覆盖降级位的置位 / 保持 / 清除）；零信任装置
`tools/verify-zero-trust.mjs` **32/32 通过**。日志与截图在 `artifacts/web/`（gitignored，一条命令可重生成）。

**验收矩阵逐行结论**：

| # | 场景 | 结论 | 本批次证据 |
|---|---|---|---|
| 1 | 状态账与事件流一致 | **通过** | `24-rebuild-clean.png`：干净流重建回执三旗"内存 一致；快照 一致；状态账 一致"；集成测试断言 `LedgerEquivalent=true` |
| 2 | 状态账分叉（派生数据被改脏） | **通过** | `25-rebuild-ledger-repaired.png`：改脏事件流原因文本后重建报"状态账 不一致"（内存仍一致），下钻"最近状态变化"已显示事件流里的新原因（修回）；集成测试同场景断言 false + 视图修回 |
| 3 | 事件流损坏 | **通过** | `27-room-health-rebuild-failed.png` 同屏回执："失败 序号 195 · 事件日志无法重建：事件载荷损坏：SeatStateChangedEvent"，无任何"等价"结论；集成测试断言失败回执 `LedgerEquivalent=null` |
| 4 | 重启恢复 | **通过** | 第 11 步停宿主 → 改库 → 重启 → 修复后重建成功；恢复失败时内存已清空，报告照实说"重建前内存 / 状态账不一致、快照一致"（装置逐属性断言）；重启恢复路径另由 Integration 套件覆盖 |

**边界（如实记录）**：

- 等价比较**不含集合顺序**：席位按座位号、效果按效果标识配对，两本账与疯狂要求按多重集合比；
  "顺序变化"不构成分叉，口径已写进 `GameStateComparer` 注释；
- 比较覆盖 `GameState` **五账全比**（席位 / 持续效果 / 即时效果 / 使用账 / 失效账），比票面点名的三项更严一档。
