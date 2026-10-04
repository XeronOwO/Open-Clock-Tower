# 印刷规则书原件回核：R-0005 / R-0012 / R-0013 / R-0018 引文（R-0029 顺带）

- Status: Todo
- Priority: Medium
- Depends on: `docs/standard/sources.md` §1（印刷规则书条）；`references/rulebook-index.json`

## 要解决的问题

本轮把 R-0012 / R-0013 / R-0018 收口为 `Decided`、并给 R-0005 补了「角色能力 > 核心规则」的依据，
引文全部来自**第三方逐字提取文本**（本地留档 `references/rulebook/`，哈希见 `references/rulebook-index.json`），
官方原件不在手。拿到实物规则书后必须回核：

- R-0012：「死亡终止持续效果 / 醉酒中毒不撤标记待恢复」；
- R-0013：「死亡导致的终止限定为持续效果」；
- R-0018：「Yes, players can nominate or vote for themselves.」；
- R-0005：「角色能力打破核心规则（例外：旅行者流放）」；
- R-0029 已由需求方 2026-10-04 拍板定案；若实物规则书里恰有「恶魔清零」条文，顺带补进条目
  （没有也不阻塞——不再是收口条件）。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 回核 | 四处引文在实物规则书里逐字对上（补页码） | 条目补页码 / 勘误记录 |
| 2 | 不符处理 | 任一引文不符 → 按维护规则改条目并处理对应实现 | 条目改动 / 测试 |
| 3 | R-0029（顺带） | 若实物有条文则补进条目；与平台口径冲突时按维护规则处理 | R-0029 条目 |

## 决定与依据

- 提取文本来源与 SHA256：`references/rulebook-index.json`；正文留档 gitignored；
- 口径：`docs/standard/sources.md` §1「拿到实物原件后回核」。
