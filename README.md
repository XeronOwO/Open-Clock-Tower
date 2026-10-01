# OpenClockTower

《血染钟楼》(Blood on the Clocktower) 的线上平台。目标是解决官方魔典的两个痛点：

1. **没有智能自动化** —— 机制覆盖、链式反应、复杂状态全靠说书人手工记，容易出错。
2. **网络不稳** —— 断线不自动重连、状态会不同步。

做法：**服务端权威状态** + **确定性规则内核**自动推演状态与链式反应，
但把说书人的自由裁量权完整保留——平台只做 **记录 + 校验 + 推演**，不替说书人拍板。

> **当前状态：工程骨架阶段。** 规范体系、解决方案、7 个项目与 4 条规范门禁已落地并通过验证；
> 规则内核只完成了六状态正交这一块，前端与服务端尚未开始。
> 进度以 [docs/backlog/README.md](docs/backlog/README.md) 为准。

## 快速了解

| 你想知道 | 去读 |
|---|---|
| 系统怎么组成、为什么这么设计 | [docs/architecture/current.md](docs/architecture/current.md) |
| 选了什么、放弃了什么 | [docs/decisions/active.md](docs/decisions/active.md) |
| **哪些规则还没定论** | [docs/standard/rulings.md](docs/standard/rulings.md) |
| 规则依据从哪来 | [docs/standard/sources.md](docs/standard/sources.md) |
| 术语 | [docs/standard/terminology.md](docs/standard/terminology.md) |
| 还剩什么没做 | [docs/backlog/README.md](docs/backlog/README.md) |
| 给 AI 代理/贡献者的规则 | [AGENTS.md](AGENTS.md) |

## 构建

三条门禁全绿才允许提交，见 [AGENTS.md](AGENTS.md)。

```bash
dotnet build OpenClockTower.slnx            # 0 警告 0 错误
dotnet test  OpenClockTower.slnx            # 12 通过 / 0 失败
dotnet format OpenClockTower.slnx --verify-no-changes
```

需要 **.NET SDK 10.0**（实测 10.0.401）与 **Node.js**（实测 v24.14.1 / pnpm 11.7.0）。
TFM、可空性、警告即错误等共享编译设定集中在 `Directory.Build.props`，包版本集中在
`Directory.Packages.props`——换 SDK 或升级包时只改这两处。

前端在 `web/`，使用 Vue 3 + TypeScript（**尚未创建**）。

## 首版范围

- 剧本：**《梦殒春宵》**（Sects & Violets），25 个角色全集。
- 形态：说书人端 + 每名玩家各自的设备端。
- 存储：SQLite（EF Core，持久化层可换）。
- 实时通道：SignalR。

**首版明确不做**：多实例/高并发、多级缓存、语音视频通话、AI 全自动说书人、
社区剧本与实验性角色的全量覆盖。

## 许可与致谢

[MIT](LICENSE)。

本仓库**不包含**《血染钟楼》的任何官方美术资源、角色立绘或标记图，也不包含钟楼百科的正文内容；
程序在运行期以外部链接引用图片。规则依据来自[钟楼百科](https://clocktower-wiki.gstonegames.com)，
一切权利归其各自权利人所有。本项目与 The Pandemonium Institute、集石及钟楼百科无隶属或背书关系。
