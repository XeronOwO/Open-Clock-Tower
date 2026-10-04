# 自我提名口径取证：找权威条文（规则书 / 官方英文 wiki / 设计师回复）

- Status: Resolved（2026-10-04）
- Priority: Medium
- Depends on: `docs/standard/rulings.md` R-0018

## 要解决的问题

R-0018 暂取「允许自我提名」，依据是中文百科《提名》没有明文（2026-10-01 快照）。这条直接影响
白天提名的合法性判定与玩家端候选列表（当前默认含自己），需要权威依据才能收口：

- 若权威条文**禁止**自我提名 → 改 `DayMachine.Nominate`、`PlayerDay.NominationCandidates` 与回归用例；
- 若权威条文**允许** → R-0018 转 `Decided`，保留现有实现（回归用例已存在）。

离线快照内无法完成取证，需要联网检索或实物规则书，故立票。

## 结果（2026-10-04）

已取证：印刷规则书 FAQ 章节写明「若规则书没有说你不能做某事，你就能做」，并逐项列举
「Yes, players can nominate or vote for themselves.」→ **允许自我提名**。

- R-0018 转 `Decided`，保留现有实现与回归用例
  （`DayMachineTests.Nominate_DeadPlayerCanBeNominated_AndSelfNominationIsAllowed`）；
- **无需改代码**，故本票进 `resolved/`；
- 来源与访问日期登记在 `docs/standard/sources.md`（印刷规则书提取文本，2026-10-04 访问；
  2026-10-04 需求方定案：不再要求实物复核）；
- 中文百科 2026-10-04 抓取《提名》页仍无禁止表述，与结论不冲突。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 取证 | 拿到可引用的条文（来源 + 访问 / 印刷日期 + 区域） | 来源口径进 `docs/standard/sources.md` |
| 2 | 结论落地 | R-0018 转 `Decided`（或按否定条文改实现 + 测试） | 条目改动 / 测试输出 |

## 决定与依据

- 中文百科 2026-10-01 快照（含《提名》《投票》）逐页核对无本条；
- 取证候选：官方英文 wiki（`wiki.bloodontheclocktower.com`）提名 / 规则页、印刷规则书相关小节、
  官方设计师回复存档；
- 拿到条文前维持「允许」，代码注释按 R-0018 引用本条。
