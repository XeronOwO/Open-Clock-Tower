# 待办（Backlog）

一个工作项 = 一个文件。**移动票据到另一个状态目录，就是状态流转**。

```text
todo/  →  in-progress/  →  review/  →  done/
                              ↓
                          future/     延后 / 低优先级 / 未来架构
                          resolved/   已决策，无需改代码
                          watchlist/  可观测性 / 架构看护项
```

## 规则

- **一个工作项 = 一个文件。**
- **下面的索引是指针表，不是第二份摘要。** 一行的组成是
  `- [标题](路径) — **优先级** — 一句话`：那一句说的是这个工作项**是什么**，
  永远不是它的历史。所有事实（决定、日期、计数、阶段进度、结论）都活在票据文件里。
  一行的**章节名就是它的状态**，所以行里不重复写状态。
- 优先级取自票据自己的 `- Priority:` 字段（第一个词）。闭环的存档记录不写优先级。
- 索引里出现"超长行、优先级与票据不一致、票据漏登记、挂在错误章节下"，都算缺陷。
- **代码完成即移入 `review/`**，review 是等待**验收批次**的等待态，不是"看起来还行"。
  代码审查通过和测试全绿都**不算**验收。
- 验收通过的记录把票据移到 `done/`；某一行不通过就移回 `todo/`，并在票据里标出被拒的行；
  缺依赖则留在 `review/` 并写明缺的是什么——**等待是诚实的状态，含糊不是**。

## 状态目录

| 目录 | 含义 |
|---|---|
| `todo/` | 未开始 |
| `in-progress/` | 正在做 |
| `review/` | 代码完成，等待验收批次 |
| `done/` | 已落地 / 已关闭 |
| `future/` | 延后或未来架构 |
| `resolved/` | 已决策，无需改代码 |
| `watchlist/` | 可维护性 / 架构看护 |

## 票据模板

```markdown
# 标题

- Status: Todo
- Priority: High
- Depends on: <票据或"无">

## 要解决的问题
（用户视角：现在发生了什么坏事）

## 验收矩阵
| # | 场景 | 期望 | 证据 |
|---|---|---|---|

## 决定与依据
（规则的指引用 页名 + 抓取日期；不确定的登记 rulings.md 编号）
```

## 索引

### Todo

- [说书人注记：魔典上的自由文本提示标记](todo/storyteller-annotation.md) — **Low** — 魔典上的自由文本 token；首版刻意不做，待定归属 / 持久化 / 审计边界
- [玩家重连「补齐缺口」判据与白名单投影不一致](todo/player-reconnect-gap-semantics.md) — **Medium** — 可见事件条数不可能覆盖全局序号区间，重连会报假缺口并把 watermark 卡住

### In progress

### Review

- [零信任安全模型](review/zero-trust-security-model.md) — **High** — 鉴权两级凭据 + 操作四道闸 + 服务端强制投影，负向测试为主

### Done

- [重建报告对比状态账](done/rebuild-state-ledger-comparison.md) — 重建报告对状态账五账做等价对比，与步骤机 / 快照并列成三项结论
- [恢复失败后的房间健康位](done/room-health-degradation-flag.md) — 区分「空账」与「数据丢了」；降级位置位后显式重建才清除，玩家侧不下发
- [界面游戏化：魔典式小镇视图](done/grimoire-view.md) — 说书人看板改为以席位为中心的魔典（圆环 / 帷幕 / 标记 / 就近操作 / 数据下钻）；批次 E6 行 1–8 全通过
- [玩家端视图新鲜度：请求了结与阶段变化的推送 / 呈现](done/player-view-freshness.md) — 作废 / 代填 / 阶段变化在线到达玩家界面（含阶段中文化）；批次 E4 行 1–5 全通过
- [说书人上帝视角：每步状态信息、归因与最终计算结论](done/storyteller-step-insights.md) — 说书人每步摘要：行动者全部已观测状态及归因、能力生效判定、无选项行为、解除与作废说明；批次 E3 行 1–6 全通过
- [自动步骤机与操作请求](done/operation-request-step-machine.md) — 轮到你时服务端主动推送请求；无超时，说书人可接管/强推/重建；批次 E2 判行 16 / 17 通过
- [《梦殒春宵》夜晚顺序表（结算引擎的输入）](done/sects-and-violets-night-order.md) — 规则层第一块数据：两套口径 + 逐条来源 + 占位移除；批次 E2 真机建表 13 槽位
- [结算引擎与能力生效判定](done/settlement-engine.md) — 逐步结算 + 能力生效判定 + 信息结果 + 维度 → 效果链接与解除；批次 E2 补呈现面证据
- [建解决方案、项目骨架与门禁工程](done/solution-and-gates-bootstrap.md) — 7 个项目 + 4 条规范门禁，三条提交门禁全绿且逐条见红验证
- [百科知识基线与引用索引](done/wiki-knowledge-baseline.md) — 显式 79 页抓取脚本与 SHA256 索引，25 角色类型与 slug 核对完毕，规则细节与范例清单落库
- [内核领域模型与六状态不变量](done/kernel-domain-model.md) — 六状态正交数据模型 + 效果生命周期 + 两本账 + 裁定点契约；10 行验收矩阵逐行留证

### Future

### Resolved

### Watchlist

- [结算结论的三态表达：把「未知」写进契约](watchlist/settlement-conclusion-three-state.md) — **Low** — 三处 `bool` 表达不了"服务端没给结论"，前端只能退化成 false
