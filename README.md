# OpenClockTower

《血染钟楼》(Blood on the Clocktower) 的线上平台。目标是解决官方魔典的两个痛点：

1. **没有智能自动化** —— 机制覆盖、链式反应、复杂状态全靠说书人手工记，容易出错。
2. **网络不稳** —— 断线不自动重连、状态会不同步。

做法：**服务端权威状态** + **确定性规则内核**自动推演状态与链式反应，
但把说书人的自由裁量权完整保留——平台只做 **记录 + 校验 + 推演**，不替说书人拍板。

> **当前状态：首版已完成，已在一台公网 Linux 机器上跑起来。** 《梦殒春宵》30 个角色（含 5 名旅行者）、
> 说书人端 + 每名玩家的设备端、断线重连、复盘回放、账号与玩家名都已落地，并有真机验收装置逐条判过。
> 怎么部署见 [docs/operations/deploy.md](docs/operations/deploy.md)；还剩什么没做以
> [docs/backlog/README.md](docs/backlog/README.md) 为准。

## 快速了解

| 你想知道 | 去读 |
|---|---|
| 怎么部署给别人玩 | [docs/operations/deploy.md](docs/operations/deploy.md) |
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
dotnet test  OpenClockTower.slnx            # 后端全绿（含 NormativeGates：规范写成会失败的测试）
dotnet format OpenClockTower.slnx
```

需要 **.NET SDK 10.0**（实测 10.0.401）与 **Node.js**（实测 v24.14.1）。
TFM、可空性、警告即错误等共享编译设定集中在 `Directory.Build.props`，包版本集中在
`Directory.Packages.props`——换 SDK 或升级包时只改这两处。

前端在 `web/`（Vue 3 + TypeScript + Vite）。先 `npm run build`，宿主会把产物带进 `wwwroot` 一起发页面；
只跑后端也不受影响（没有产物时宿主照常启动，只是没有页面）。本机怎么跑见
[web/AGENTS.md](web/AGENTS.md) §2。

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
