# 夜晚顺序口径默认值统一（引擎默认 Original / 面板默认 Recommended）

- Status: Todo
- Priority: Medium
- Depends on: `docs/standard/rulings.md` R-0014；`docs/backlog/done/settlement-engine.md` 残余事项 2

## 要解决的问题

R-0014 第 2 条写「平台默认取 `Original`」，但说书人面板的「口径」下拉初始选中值是
`Recommended`（`web/src/features/storyteller/OperationsControl.vue`）——说书人直接点「开夜」时
实际用的是 Recommended，与登记口径不一致。命令层 `StartNightCommand.Variant` 的默认值虽是
`Original`，但面板总会显式传值，实际默认由 UI 决定。

这是 2026-10-04 过 R-0014 时发现的、登记在案的不一致；必须有一个方向收口，不能两处各说各话。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 说书人打开面板直接开夜 | 实际生效口径与 R-0014 的「平台默认」表述一致 | 装置断言 + 截图 |
| 2 | 重启 / 重连后再开面板 | 默认值稳定且与条目一致（若改「记住上次选择」，口径须写进 R-0014） | 装置断言 |

## 决定与依据

- 两条收口路线（二选一，开工时定）：
  - A. 面板默认值改为 `Original`（与 R-0014 第 2 条一致；代价：说书人要手动切到 Recommended）；
  - B. 保留面板默认 `Recommended`，把 R-0014 第 2 条改成「引擎默认 Original、面板默认 Recommended」
     并说明理由（推荐顺序本来就是百科给说书人的推荐档）——同时更新「对局前告知玩家」的表述。
- 依据：`docs/standard/rulings.md` R-0014（2026-10-04 核对段）；百科《夜晚行动顺序一览》开头
  （说书人可自行选择两套顺序）。
