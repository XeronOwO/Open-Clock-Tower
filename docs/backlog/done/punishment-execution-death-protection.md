# 处罚处决接入统一死亡保护查询（R-0020 路径）

- Status: Done（批次 E37 判出，冻结版 `58d5aa6`）
- Priority: Medium
- Depends on: `docs/backlog/done/traveller-and-exile.md`（E34 残余）；`docs/standard/rulings.md` R-0020 / R-0048

## 要解决的问题

统一死亡保护查询（`DeathProtectionQuery`，R-0048 第 6 条）原只有**两条**路走它：流放计票
（`ExileMachine`）与常规处决收口（`DayCloseMachine`）。**处罚处决**（洗脑师 / 畸形秀演员的「疯狂」后果，
R-0020）是第二条**处决致死**路径——它直接写死亡事件、不问查询。今天没有覆盖
`DeathProtectionCause.Execution` 的来源，所以看不出差异；但任何新来源（例：跨剧本的「免于处决」类角色）
一落地，就会在这条路上静默漏掉，形成「同一条裁定在两条处决路径上行为不一致」。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 白天处罚处决 + 来源判「受保护」 | 只记「被处决」、不产生死亡；仍占当天上限并立即收口白天（R-0020 第 1 条形状不变） | 内核用例（脚本化保护来源） |
| 2 | 处罚处决 + 来源「待裁定」/「判定不了」 | 整条输入显式拒绝、零事件、状态不变（不猜；与 `CloseDay` 同源） | 内核用例 |
| 3 | 夜晚处罚处决 + 来源判「受保护」 | 同第 1 行的死亡语义；且不写白天账、不推进阶段（R-0020 第 2 条形状不变） | 内核用例 |
| 4 | 没有来源 / 只覆盖流放 / 明确「不受保护」 | 行为与既有实现完全一致（照常致死） | 内核阴性对照 + 集成 `MadnessPunishmentHostTests` + 真机 `verify-madness` 取证档 |
| 5 | 已死亡目标 | 只记「被处决」，不查保护、不重复记死亡（既有行为） | 内核用例 |

## 判行（批次 E37，2026-10-04）

| # | 结论 | 证据 |
|---|---|---|
| 1 | 通过（内核判出） | `DayProtectionTests.PunishmentExecution_ProtectionAppliesToTheAdjudicatedExecution`：处决事实照记、无死亡、当天上限被占、白天立即收口 |
| 2 | 通过（内核判出） | 同一用例的待裁定 / 判定不了分支 + 夜晚形态同款分支：`punishment.execution_protection_required` / `punishment.execution_protection_indeterminate`，零事件、状态不变 |
| 3 | 通过（内核判出） | `DayProtectionTests.NightPunishmentExecution_ProtectionApplies_WithoutReshapingTheNight`：不写白天账、不推进阶段 |
| 4 | 通过（真机 + 集成 + 内核） | 内核阴性对照（来源不表态 / 明确「不受保护」都照常致死）+ 集成 `MadnessPunishmentHostTests` 3 项 + 真机 `verify-madness` 取证档 28 项全过（零回归） |
| 5 | 通过（保护短路面内核判出） | `DayProtectionTests.PunishmentExecution_OnADeadSeat_SkipsTheProtectionQuery`（来源若被查会拒绝，仍 Applied） |

- 先红后绿：实现前内核过滤集 2 条失败（白天 / 夜晚「受保护」分支仍写死亡），实现后 23 项全过；
  冻结版门禁 **1088 通过 / 0 失败**（内核 439 · 规则 390 · 集成 234 · 门禁 25），build 0 警告、format 未重写工作树。

## 决定与依据

- 口径：R-0048 第 6 条「统一收口」——按死因问同一条查询；本票把收口点由两处扩为三处
  （流放计票 / `CloseDay` 处决收口 / 处罚处决），并同步登记进 R-0020 第 7 条。
- 结论语义与 `DayCloseMachine` 同源：受保护 → 跳过死亡、处决事实照记；待裁定 / 判定不了 → 显式拒绝
  （`punishment.execution_protection_required` / `punishment.execution_protection_indeterminate`）；
  不受保护 → 照常死亡。
- 保护不改变「处决 ≠ 死亡」的记账（R-0020 / 百科《处决》）：额度、白天收口与白天账都只看处决事实。
- 规则侧不新增来源：怪咖（`DeviantProtectionSource`）仍只认流放致死（R-0048 第 1 条）——本票只接线，
  不改任何规则语义，纯内向一致性收口。
- 范围登记（整族对齐）：全仓处决致死生产者只有 `DayCloseMachine`（已接）与 `AdjudicatedExecutionMachine`
  （本票接）。

## 残余（随票留档）

- 第 1 / 2 / 3 行与第 5 行的**保护短路面**真机不可达（今天没有任何覆盖 `Execution` 的来源），由内核用例判出。
- 第 5 行的**记账面**（已死目标只记「被处决」、不重复记死亡）真机可达（席位操作台的处罚控件没有生死闸），
  但既有 `verify-madness` 未走死席处罚——本批未覆盖，如实记录。
- 其余白天 / 夜晚致死点（女巫诅咒、恶魔夜杀、方古、流莺、麻脸巫婆）没有 `DeathProtectionCause` 分类、
  不属于处决家族——等对应保护来源出现时另立票，不在本票静默扩范围。
