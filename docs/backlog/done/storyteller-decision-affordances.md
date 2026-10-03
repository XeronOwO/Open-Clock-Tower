# 说书人面板：裁定归属、读数与投影补口

- Status: Done
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

## E25 验收判定（2026-10-05，冻结版本）

批次记录见 `docs/acceptance/batches.md`（E25）。取证档五装置全部前台、一次构建（首装置 `--build`）：

- 本票装置 `tools/verify-death-triggers.mjs`（`--quota 2 --screenshots-all --build`）：
  **70 项全部通过 / 0 跳过、退出码 0**（E24 为 56 项），截图 `deathtrigger-01…06` 重新落盘并逐张复核；
- 角色变更族 `tools/verify-character-change.mjs`（同档）：**67 项全部通过**（E24 为 59 项），
  新增截图 `cc-08-barber-night-marker`；
- 主装置 `tools/verify-storyteller-panel.mjs`（同档）：**判定 194 项 / 0 跳过**（通用回归）；
- 麻脸巫婆链路 `tools/verify-pit-hag.mjs`（同档）：**34 项全部通过**（候选文本读取口径同族回归）；
- 零信任 `tools/verify-zero-trust.mjs`：**43 项全部通过**。

冻结版本说明：四个装置跑在 `main` @ `f93c57c`；角色变更族因补一张取证截图在 `a365ef8` 重跑——
两者**产品代码完全一致**，差异仅是 `verify-character-change.mjs` 的一行 `screenshot(...)`。

逐行结论（本票验收矩阵 1–7）：

| # | 结论 | 证据（本次运行） |
|---|---|---|
| 1 | 通过 | 装置：裁定块「归属：2 号」+ 环头「定位到 2 号（待裁定）」+ 2 号牌面「待裁定」；截图 `deathtrigger-04`。 |
| 2 | 通过 | 装置：「归属：3 号」+ 3 号牌面「待裁定」+ 定位按钮；宿主 `DeathTriggerHostTests` 断言 `AwaitingDecisionSeat = 3`（结清后归 null）；截图 `deathtrigger-01`。 |
| 3 | 通过 | 装置 3 条（进行中 = `1 / 1`、收口后不含越界 `2 / 1`、收口后 = 「已完成」）+ web 单测（封顶 / 未建计划 / 坏载荷）；截图 `deathtrigger-01` 状态条第 3 格「已完成」。 |
| 4 | 通过 | 装置：候选里恰好 1 个「已死亡」标签且落在 `3 号玩家`，阳性对照存活候选无标签、阴性对照 `pair:` 候选零标签；web 单测（映射与非误标）；截图 `deathtrigger-01` 候选行。 |
| 5 | 通过 | 装置 8 条（「限一次」可见 / 文本 / title 记 3 号→5 号 / 第三夜仍在；「今晚理发」窗口可见 / 文本 / title 记 4 号 / 结清后消失）+ 宿主 `FangGuHostTests` / `BarberHostTests` 投影断言；截图 `cc-06`、`cc-08-barber-night-marker`。 |
| 6 | 通过 | 装置：贤者格挂起时环区 `grimoire-slot-context` 非空且含「推演」；触发型（心上人）同框见 `deathtrigger-01` 的「本步上下文：心上人（3 号）死亡…」；截图 `deathtrigger-04` / `deathtrigger-01`。 |
| 7 | 通过 | 零信任 43 项 + 投影门禁（新 DTO 已登记说书人专属豁免清单 + 契约全覆盖自检）+ 装置五席 128 帧扫描零说书人字段（含阳性对照：说书人连接确实收到含「推演」的帧）。 |

**残余（不改判行结论）**：

1. 多存活恶魔的「选哪名恶魔」裁定分支未在真机覆盖（规则级 `BarberNightTriggerTests` 覆盖归属填值）；
2. 心上人触发型裁定的「无挂起 → 回退 `currentSlotContext`」第二分支未单独取证（`decisionSeatOf` 回退链有 web 单测）；
3. 旧事件流 `AttributionSeat = null` 的界面回退路径未真机覆盖（内核有容忍用例，`decisionSeatOf` 有回退单测）；
4. 迭代档下角色变更族「换手后尚未进入的格重绑」断言偶发窗口（E24 已登记、以取证档为准；本批取证档 67 项全绿）。
