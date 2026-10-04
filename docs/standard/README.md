# standard/ — 登记表

本目录放**必须逐条维护、且会被代码引用**的登记表。它不是"参考资料"，缺一条就是缺一条。

| 页面 | 内容 | 谁依赖它 |
|---|---|---|
| [terminology.md](terminology.md) | 术语正名与中英 slug | 所有代码标识符、界面文案、文档 |
| [sources.md](sources.md) | 什么算规则依据、怎么引用、抓什么不提交什么 | 每一条规则断言 |
| [rulings.md](rulings.md) | **裁定登记表**：查不到 / 拿不准 / 有冲突的规则怎么算 | 结算内核、说书人端 |
| [character-rules.md](character-rules.md) | 30 个《梦殒春宵》角色（25 非旅行者 + 5 旅行者）的规则细节与实现依据（含相克条目汇总） | `Rules` 层的角色实现 |
| [character-examples.md](character-examples.md) | 百科范例 → 回归测试用例登记（当前 25 个非旅行者角色；5 名旅行者随能力实现补） | `tests/Kernel.Tests` |

## 新增页面的条件

只有当一类登记项**数量大到需要单独成页**、且**代码会引用它**时才新建页面。
零散补充直接进现有页。

## 相关阅读

- 文档体系规则：`../AGENTS.md`
- 决策（本项目自己的取舍，与"规则"分开）：`../decisions/index.md`
- 架构：`../architecture/current.md`
