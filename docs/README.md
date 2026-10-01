# OpenClockTower 文档

《血染钟楼》(Blood on the Clocktower) 线上平台的工程文档。**中文优先**，见
[决策 D-0016](decisions/active.md#d-0016-语言策略修订注释与日志用中文)。

## 从哪里读起

| 你想知道 | 去读 |
|---|---|
| 这是什么项目、怎么构建 | [../README.md](../README.md)、[../AGENTS.md](../AGENTS.md) |
| 系统怎么组成的、为什么这么设计 | [architecture/current.md](architecture/current.md) |
| 为什么选了 A 而不是 B、代价是什么 | [decisions/active.md](decisions/active.md) |
| **哪些规则还没定论** | [standard/rulings.md](standard/rulings.md) |
| 规则断言的依据从哪来 | [standard/sources.md](standard/sources.md) |
| 某个词在这里是什么意思 | [standard/terminology.md](standard/terminology.md) |
| 某个角色怎么实现、依据在哪 | [standard/character-rules.md](standard/character-rules.md) |
| 还剩什么没做 | [backlog/README.md](backlog/README.md) |

## 三条最该先记住的事

1. **规则不许凭记忆写。** 每条规则断言都要指到一条引用或一条裁定。
   参阅 [standard/sources.md](standard/sources.md)。
2. **拿不准的一律登记，不许隐藏。** [standard/rulings.md](standard/rulings.md) 是一等交付物，
   不是附录；`Open` 状态的行为在代码里必须带裁定号注释。
3. **状态属于玩家，不属于角色。** 角色/阵营/生死/醉酒/中毒 六者相互独立。
   这是内核的头号不变量，见 [architecture/current.md](architecture/current.md)。

## 目录

| 目录 | 内容 |
|---|---|
| [standard/](standard/) | 术语、来源口径、裁定登记表 |
| [architecture/](architecture/) | 当前架构、域划分 |
| [decisions/](decisions/) | 决策记录（`D-nnnn`） |
| [backlog/](backlog/) | 待办票据，状态即目录 |
| [acceptance/](acceptance/) | 验收规程 |
| [evidence/](evidence/) | 交付清单与验证证据 |
| [development/](development/) | 代理工作细则 |

文档体系本身的规则见 [AGENTS.md](AGENTS.md)。
