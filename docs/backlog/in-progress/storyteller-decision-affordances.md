# 说书人面板：裁定归属、读数与投影补口

- Status: In progress
- Priority: Medium
- Depends on: 无（呈现层 + 服务端投影新字段；不新增事件类型、不改规则）
- 来源：E24 死亡触发族装置的产品侧疑点三条（2026-10-03 记录）+ E17 残余③（`done/character-change-family.md`）
  + E21 残余（`done/mathematician.md`）——三处共享同一投影面，合并为一票。

## 要解决的问题

1. **触发格 / 触发型裁定在圆环上没有席位归属**：`decisionSeatOf` 取
   `currentSlotActor ?? stepDigest?.seat`，而触发格（`StepSlot.Trigger`）没有 `Actor`、
   触发型裁定（如心上人）没有槽位——圆环既没有「待裁定」chip、也没有「定位到 N 号」按钮，
   说书人看不出「谁在等」（E24 截图 `deathtrigger-04`）。
2. **白天计划收口后读数越界**：白天只有 1 个槽位，走完后 `slotIndex + 1 > slotCount`，
   状态条与环心显示「槽位 2 / 1」「第 2 / 1 步」（截图 `deathtrigger-02/03`）。
3. **裁定候选不区分生死**：心上人的醉酒候选允许已死亡玩家（R-0039 第 3 条），界面没有标注。
4. **E17 残余③**：「限一次」整局事实与「今晚理发」跨阶段事实未进 `StorytellerViewDto`，
   说书人看不到魔典中心的标记 / 待处理事实。
5. **E21 残余**：「本夜推演」只出现在挂起裁定块或数据抽屉里，不常驻可见。

## 口径决定（本次）

- **归属席位是内核事实**：`DecisionPointRaisedEvent.AttributionSeat` → `StepMachineState.AwaitingDecisionSeat`
  → 说书人投影 `awaitingDecisionSeat`；前端只读，不解析裁定 id、不判规则（`web/AGENTS.md` §4）。
  五个开点来源各自填值：入槽（`slot.Actor`）/ 玩家作答后的信息裁定（`slot.Actor`）/
  贤者（`SageNight.Sage`）/ 理发师（`BarberNight.Source`）/ 心上人（`death.Seat`）。
  字段是后加的**可空**字段：旧版本事件流里没有它，重放**容忍为 null**（界面退回行动者 / 摘要回退，
  不猜）；"新开点必填"由 5 处开点的用例锁住，而不是靠重放时抛错。
- **读数越界**：纯前端封顶——`planCompleted` 显示「已完成」，否则 `min(slotIndex, slotCount - 1)`；
  不改服务端语义（`slotIndex == slotCount` 是"已走完"的正常表示）。
- **候选生死标注**：只对 `seat:N` 形式的选项值按状态账 `Life` 打「已死亡」；
  未观测不标（未观测 ≠ 默认值）；不改候选集合、不改服务端（R-0039 不变）。
- **「限一次」/「今晚理发」入投影**：新增 `FangGuInfectionDto` / `BarberNightDto`
  （说书人专属，D-0012），面板在魔典中心显示「限一次」标记、在面板显示理发师之夜事实。
- **本夜推演常驻**：常驻可见区显示「本步上下文」，取值 `awaitingDecisionContext ?? currentSlotContext`
  （含入槽实时重建的推演值）；不改内核、不新增投影字段。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 触发格裁定（贤者展示） | 圆环标出归属席位 + 「定位到 N 号」按钮出现 | 装置截图逐张复核 |
| 2 | 触发型裁定（心上人） | 归属席位的呈现口径 = 服务端 `awaitingDecisionSeat`（死亡席位） | 宿主用例 + 截图 |
| 3 | 白天计划收口 | 读数不再越界（显示「已完成」/封顶为 1 / 1），不再出现 2 / 1 | 截图 + 前端单测 |
| 4 | 候选生死标注 | 已死亡候选加「已死亡」标注；不影响服务端候选集合（R-0039 不变） | 截图 + 前端单测（标注映射） |
| 5 | 限一次 / 今晚理发 | 魔典中心出现「限一次」标记；理发师之夜事实在面板可读 | 装置截图（角色变更族）+ 宿主用例 |
| 6 | 本夜推演常驻 | 没有挂起裁定块时也能在常驻区看到本步上下文（含推演） | 装置截图（数学家 / 贤者格） |
| 7 | 信息隔离回归 | 归属 / 标注 / 新事实字段不进玩家投影（D-0012）；零信任装置全过 | `tools/verify-zero-trust.mjs` + 投影门禁 |

## 非目标

- 不改规则、不改裁定候选集合、不改推演口径；不新增事件类型。
- 不动玩家投影白名单；复盘票（`replay-auto-review`）与排版票（`ui-layout-and-onboarding`）不并入。

## 决定与依据

- 归属席位口径以 `docs/standard/rulings.md` R-0038 / R-0039 与
  `docs/architecture/storyteller-presentation.md` §2 为准；新词先登记 `docs/standard/terminology.md`。
- 「限一次」置于魔典中心 = 百科《方古》· 2026-10-01 抓取 · 提示标记「限一次」（放置条件）；
  理发师之夜事实跨阶段保留（R-0033），投影只读。
- 残余：`DeathTriggerReadings` 生效判定读批后账（自检 L-3）与本票无关，仍留在
  `done/sage-and-sweetheart.md`；E24 行 12 / 16 的组合证据残余也按该票记录跟进。
- **旧库口径（明确写死）**：本轮之前写下的、含 `DecisionPointRaisedEvent` 的事件流**不要求数据迁移**——
  归属字段缺失时走容忍路径（重放不失败、界面回退到行动者 / 摘要），也**不**在重建时补写归属
  （归属只能由开点给出，重建不得编造）。
