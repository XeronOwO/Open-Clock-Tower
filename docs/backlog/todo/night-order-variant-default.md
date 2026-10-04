# 夜晚顺序默认值对齐：命令层默认值改为官方魔典顺序（Recommended）

- Status: Todo
- Priority: Low
- Depends on: `docs/standard/rulings.md` R-0014（已定案：默认 = 官方魔典顺序 = `Recommended` 变体）

## 要解决的问题

R-0014 已定案：平台默认取**官方魔典行动顺序**（= `NightOrderTable.Recommended`；需求方 2026-10-04
提供魔典表，逐条核对一致）。现状：

- 说书人面板初始选中 `Recommended`（`web/src/features/storyteller/OperationsControl.vue`）——**已符合**；
- 命令层 `StartNightCommand.Variant` 的默认值仍是 `Original`——只在无参调用路径（宿主脚本 / 装置 /
  集成夹具）可见，与定案的默认准则不一致。

本票把命令层默认值对齐到 `Recommended`，消除「文档默认 vs 无参默认」的最后一处偏差。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 无参开夜 | 未显式给口径时建出的表是官方魔典顺序（`Recommended`） | 内核用例 / 集成用例 |
| 2 | 面板开夜 | 行为不变（面板显式传值，默认仍是 `Recommended`） | 前端门禁 + 装置断言 |
| 3 | 显式选 Original | 仍可按说书人选择走原本顺序，记录进 `StepPlan.Variant` | 既有 `NightOrderVariantDiffTests` |

## 决定与依据

- 定案与魔典表来源：`docs/standard/rulings.md` R-0014（留档索引 `references/source-images-index.json`，含 SHA256）；
- 两套口径仍都保留（百科明示说书人可选），本票只动「默认值」这一处。
