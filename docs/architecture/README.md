# architecture/ — 架构文档索引

| 页面 | 回答什么问题 |
|---|---|
| [current.md](current.md) | 系统由哪些部分组成、依赖朝哪、游戏逻辑为什么能算对 |

## 待建页面

以下页面在**有实际内容时**才建，不预先建空壳（`docs/AGENTS.md`：指令文件只路由与约束，不装知识）：

| 页面 | 触发条件 |
|---|---|
| `domains.md` | 域划分稳定、且 `current.md` 一页装不下时 |
| `guards.md` | 不变量（六状态正交、内核纯净、信息隔离）多到需要单独成页时 |
| `glossary.md` | 术语量超出 `docs/standard/terminology.md` 的承载时 |
| `sync.md` | 重连与事件补齐协议定型后 |

## 相关阅读

- 决策与代价：`docs/decisions/active.md`
- 术语：`docs/standard/terminology.md`
- 未确证的规则：`docs/standard/rulings.md`
