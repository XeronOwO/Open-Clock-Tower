# 夜晚顺序默认值对齐：命令层默认值改为官方魔典顺序（Recommended）

- Status: Done
- Priority: Low
- Depends on: `docs/standard/rulings.md` R-0014（已定案：默认 = 官方魔典顺序 = `Recommended` 变体）

## 要解决的问题

R-0014 已定案：平台默认取**官方魔典行动顺序**（= `NightOrderTable.Recommended`；需求方 2026-10-04
提供魔典表，逐条核对一致）。本票开工前的现状：

- 说书人面板初始选中 `Recommended`（`web/src/features/storyteller/OperationsControl.vue`）——**已符合**；
- 命令层 `StartNightCommand.Variant` 的默认值仍是 `Original`——只在无参调用路径（宿主脚本 / 装置 /
  集成夹具）可见，与定案的默认准则不一致。

本票把命令层默认值对齐到 `Recommended`，消除「文档默认 vs 无参默认」的最后一处偏差。

## 实现

- `src/OpenClockTower.Application/StartNightCommand.cs`：`Variant` 默认值 `Original` → `Recommended`
  （注释同步 R-0014）；显式传值路径（面板 / Hub / `GameCommandFactory`）行为不变。
- `tests/OpenClockTower.Integration.Tests/StartNightVariantDefaultTests.cs`（新增）：
  - 无参构造 `new StartNightCommand { NightNumber = 1 }` → 计划口径 `Recommended`，且槽位顺序真按推荐表
    （首夜哲学家索引 2，不是只查标签）；
  - 显式 `Original` → 计划口径 `Original`、哲学家索引 4。
- `docs/standard/rulings.md` R-0014：删除「剩余实现差异」描述，注明默认值已对齐。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 无参开夜 | 未显式给口径时建出的表是官方魔典顺序（`Recommended`） | 集成 `StartNightVariantDefaultTests.StartNightWithoutExplicitVariant_UsesRecommendedTable`：`Plan.Variant=Recommended` + 哲学家槽位索引 2（全量测试 1078 通过 / 0 失败） |
| 2 | 面板开夜 | 行为不变（面板显式传值，默认仍是 `Recommended`） | `npm run gate` 退出 0（typecheck + lint + 169 测试 + 构建）+ 批次 E35 主装置取证档 284 项全过（`01` / `03` 截图：口径下拉初始 Recommended、开夜后首夜 14 槽） |
| 3 | 显式选 Original | 仍可按说书人选择走原本顺序，记录进 `StepPlan.Variant` | 集成 `StartNightWithExplicitOriginal_StillUsesOriginalTable`（`Original` + 索引 4）+ 既有 `NightOrderVariantDiffTests` / `NightBuildHostTests`（显式 Original 计划记录） |

## 验收判出（批次 E35 · 2026-10-04）

冻结版本 `main` @ `d8b4816`（跑批期间工作树干净）；主装置
`node tools/verify-storyteller-panel.mjs --quota 2 --screenshots-all --build`：**284 项通过 / 0 失败 /
0 跳过、退出码 0**（173.7s，54 张截图本次运行写入），覆盖面板默认口径开夜（首夜 14 槽）、
第二 / 第三夜 Recommended 开夜、旅行者 / 涡流 / 重建 / 重连全流程。矩阵 3 行全部通过 → 票据转 `Done`。
批次记录见 `docs/acceptance/batches.md` 批次 E35。

## 决定与依据

- 定案与魔典表来源：`docs/standard/rulings.md` R-0014（留档索引 `references/source-images-index.json`，含 SHA256）；
- 两套口径仍都保留（百科明示说书人可选），本票只动「默认值」这一处。
