# 数学家：窗口取值与说书人裁定面

- Status: Done（批次 E21 矩阵 10 行全部通过，见「E21 验收判定」；先提交实现与装置 `5e370f7`，
  再补装置的涡流段 `365c102` 并对该冻结版本跑取证档）
- Priority: Medium
- Depends on: R-0004（已闭合）；E18 涡流干扰计数（引擎级口径已就位）

## 要解决的问题

《梦殒春宵》25 个角色里数学家还没有夜间行动契约：只要它在场（含被创造 / 被获得），开夜会被
`plan.contract_missing` 直接拒绝。R-0004 留下的两件事也没有取值路径：

1. **窗口**：「从上一个黎明到数学家被唤醒」——失效账本只有全账 `CountedSeats`，没有黎明边界；
2. **自身不计**：数字必须排除数学家自己的席位（R-0004 第 3 条）。

第三件事在平台面：数学家的数字由说书人给出（R-0004 平台边界），平台要**推演**它、并让说书人在
裁定前看见。裁定提示是**计划期冻结**的 prompt，而窗口要含当夜更早槽位的失效——计划期算出的数字
天然过期。因此本票同时补一条支撑机制：**入槽时对说书人裁定类提示做实时重建**。

## 验收矩阵

| # | 场景 | 期望 | 依据 |
|---|---|---|---|
| 1 | 首夜在位、窗口内无失效 | 推演 0；裁定提示带推演值；说书人给出后数学家收到它 | R-0004 |
| 2 | 当夜更早槽位失效（诺-达鲺毒筑梦师） | 失效入账（`Poisoned`）；数学家**入槽时**的提示推演 = 1（不是计划期快照） | R-0004 第 2 条 |
| 3 | 跨黎明 | 次夜窗口只含本黎明之后的记录，首夜记录不计 | R-0004 第 2 条 |
| 4 | 同一玩家多条记录（中毒 + 醉酒） | 按玩家去重只算 1 | R-0004 第 1 条 |
| 5 | 数学家自己的席位有失效记录 | 推演排除该席位 | R-0004 第 3 条 |
| 6 | 信息下发 | 只发给数学家本人；无关玩家**零下发**（反方向断言） | D-0012 §4.3 / D-0013 §5 |
| 7 | 涡流存活 | 提示注明「必须为假」；结果 `MayBeFalse = true` 且说明带 R-0028 | R-0028 / R-0004 |
| 8 | 数学家中毒 / 醉酒 | 能力未生效但照常开裁定点；`MayBeFalse = true`；它自己的失效记录入账、不进自己的数字 | 《重要细节》三-3；R-0004 第 3 条 |
| 9 | 说书人未给内容 | 显式拒绝，不产出信息事件 | 与钟表匠同族（非空校验） |
| 10 | 相关族回归 | 钟表匠等既有说书人裁定类提示重建后内容不变；玩家选项契约仍走计划快照 | 本票作用域声明 |

## 决定与依据

- **窗口实现**：失效账本记「黎明水位」（`MalfunctionLedger.SinceDawnStart`），`DayStartedEvent`
  折叠时推进；**不删记录**——R-0004 第 4 条要求记录与数学家是否在场无关，物理提示标记的
  「移除」在平台上由窗口表达。不采用「给每条记录加白天号」：水位不改事件 JSON 与快照形状，
  语义等价且可重放。
- **首夜**：首夜不存在「上一个黎明」，窗口下界取**本局开局**（首夜唤醒前已记录的失效都计入）。
  官方页无首夜专条，本口径登记进 `docs/standard/rulings.md` R-0004 补充。
- **数字来源**：仍由说书人给出；平台只推演 + 记录，不判信息真假（D-0002 / R-0028 同族先例）。
  推演值写进裁定提示上下文与信息结果的 `Note`（只说书人可见）；玩家投影只有 `Content`。
- **提示实时重建**：只对 `OnNoOption = StorytellerDecides` 的提示做；重建账 = 已提交账 +
  **本批已产出事件**（同批更早槽位的失效必须进窗口）；重建结果求值去向与计划快照不一致时
  回退快照，不改本步语义。玩家选项契约不在本票范围（仍走计划快照，另票评估）。
- **自身不计**：只在取值时排除该席位，账本照记（R-0004 第 3 / 4 条）。
- **代行槽位**：能力契约按 `slot.Owner` 解析（哲学家获得能力时是「被获得角色」），提示重建
  与结算同一把键，避免「结算找得到、重建找不到」。

## E21 验收判定（2026-10-03，冻结版本 `main` @ `365c102`）

批次记录见 `docs/acceptance/batches.md`；装置 `tools/verify-mathematician.mjs` 取证档
（`--quota 2 --screenshots-all --build`）**38 项全部通过**，截图 `math-01…math-05` 逐张复核。
逐行结论：

| # | 结论 | 证据（本次运行） |
|---|---|---|
| 1 | 通过 | 空窗 → 推演 0：`MalfunctionLedgerTests.AdvanceDawn_KeepsEntries_ButMovesTheWindow` + 装置 `math-05`（跨黎明后的空窗：提示「推演：0」）；首夜全账口径由 `MalfunctionLedgerTests.CountedSeatsSinceDawn_FirstNight_CoversTheWholeLedger` 覆盖（矩阵草稿的「首夜空窗」在装置里以跨黎明后的空窗跑出）；带推演值的提示与数字下发见 `math-01` / `math-03`。 |
| 2 | 通过 | 装置 `math-01`：当夜更早的两名中毒信息角色（2 号 dreamer / 6 号 clockmaker）先结算后，数学家入槽提示「按失效账本推演：2」——计划期快照会是 0，证明入槽实时重建生效（矩阵草稿按单条失效写 1，装置场景实际两条，语义一致）；`SlotPromptRefreshTests.StorytellerDecision_IsRebuiltWithBatchLedger`（同批事件折进重建账）与真宿主 `MathematicianHostTests` 同断言。 |
| 3 | 通过 | `MalfunctionLedgerTests.ConsecutiveDawns_MoveTheWindowEachTime`、`GameStateLedgerTests.DayStarted_AdvancesMalfunctionWindow_WithoutDeletingEntries`；装置 `math-05`：开白天 / 结束白天后第二夜推演 = 0。 |
| 4 | 通过 | `MalfunctionLedgerTests.CountedSeatsSinceDawn_DeduplicatesAndExcludesNonCountedKinds` + `MathematicianTests.Prompt_CountsWindowedPlayers_ExcludingSelf`（同一玩家 Poisoned + Drunk 只算 1）。 |
| 5 | 通过 | `MathematicianTests.Prompt_CountsWindowedPlayers_ExcludingSelf`（数学家自己的席位记录不进推演）。 |
| 6 | 通过 | 装置 `math-03`（3 号玩家端只有 `mathematician 2`）+ 无关席位（5 号浏览器 count=0、SignalR 零条）与「数字没有下发给其他任何席位」断言；真宿主 `MathematicianHostTests` 反方向断言。 |
| 7 | 通过 | 装置 `math-05`：第二夜（说书人在真界面把 4 号上报为涡流）提示注明「必须为假」+ R-0028，说书人给出假数字「1」并下发本人；`MathematicianTests.Prompt_WithVortox_…` / `Resolve_WithVortox_MarksInfoAsMustBeFalse` 覆盖 `MayBeFalse` 标记与说明（该标记与说明不投影给玩家）。 |
| 8 | 通过 | `MathematicianTests.Resolve_Ineffective_MarksInfoAsPossiblyFalse`（未生效照常给信息、`MayBeFalse = true`、说明沿用中毒原因）；装置 `math-04` 失效账本两条「中毒」与真宿主账本断言。 |
| 9 | 通过 | `MathematicianTests.Resolve_WithoutStorytellerNumber_IsRefused`（抛错、不产出信息事件）。 |
| 10 | 通过 | `SlotPromptRefreshTests.PlayerChoiceRequest_IsNotRebuilt`（玩家选项不重建、来源不被问到）+ `InfoResolutionTests` 全绿 + 全量 **618 通过 / 0 失败**（钟表匠 / 筑梦师等既有行为不变）。 |

**残余**：无阻塞项。面板若要常驻显示「本夜推演」（不依赖裁定提示），随面板迭代另票（与 E17 残余③同族）。
