# R-0041 的 6 人分布行取证：实物《旅行者列表 / 初始设置表》

- Status: Todo
- Priority: Low
- Depends on: `docs/standard/rulings.md` R-0041（Open：6 人行为结构外推）

## 要解决的问题

分布表 11 行里 10 行有据（6 行 Attested + 4 行 Derived），只有 **6 人 = 3 / 1 / 1 / 1** 是结构外推。
这张表不在百科，也不在规则书提取文本里（规则书写「按 setup sheet 上的数量取」）——只在实物
《旅行者列表 / 初始设置表》上。拿到实物照片（或等价官方素材）后回填并转 `Decided`。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 取证 | 6 人行的实物照片 / 官方素材（含可引用的区域） | 来源登记进 `sources.md`；必要时加 `references/` 索引 |
| 2 | 收口 | R-0041 全部 11 行有据；`SectsAndVioletsDistribution` 的 6 人行为与实物一致 | 条目改动 / 分布表用例 |
| 3 | 不符处理 | 若实物与 3 / 1 / 1 / 1 不符 → 改数据并补回归 | 测试输出 |

## 决定与依据

- 现状依据：`docs/standard/rulings.md` R-0041（Attested / Derived / Extrapolated 逐行标注）；
- 实物表存在的旁证：百科《圣洁之魂》「标准的邪恶玩家数量印在了旅行者列表和初始设置表上」。
