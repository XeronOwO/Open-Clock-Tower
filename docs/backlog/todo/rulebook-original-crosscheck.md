# 印刷规则书原件回核：R-0005 / R-0012 / R-0013 / R-0018 引文与 R-0029 取证

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
- R-0029 仍未收口：顺带找「最后一名恶魔被变成非恶魔」的条文（规则书 / 设计师回复）。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 回核 | 四处引文在实物规则书里逐字对上（补页码） | 条目补页码 / 勘误记录 |
| 2 | 不符处理 | 任一引文不符 → 按维护规则改条目并处理对应实现 | 条目改动 / 测试 |
| 3 | R-0029 | 找到条文则转 `Decided`；找不到则维持平台口径并在条目写明检索范围 | R-0029 条目 |

## 决定与依据

- 提取文本来源与 SHA256：`references/rulebook-index.json`；正文留档 gitignored；
- 口径：`docs/standard/sources.md` §1「拿到实物原件后回核」。
