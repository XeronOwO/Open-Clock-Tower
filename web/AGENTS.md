# web/ — 前端

约束 `web/` 下的代码。**这里只写"怎么跑、边界在哪"**；架构与取舍在 `docs/decisions/active.md` D-0018。

## 1. 是什么

Vue 3 + TypeScript + Vite 的单页应用，**两套视图同一个构建**：

- 说书人上帝视角面板（默认入口 `/`）：魔典席位圆环（角色 / 生死 / 状态标记）、
  席位操作台（卡点 / 裁定 / 上报）、可展开的数据与审计（当前步骤与四张表）、局务（兜底 / 开局分配）；
- 玩家端（`/#player`）：只显示服务端下发给该玩家的席位、阶段、请求与信息类结果。

两者**从不共享视图数据**：玩家侧不出现、也不该出现说书人专属字段（有门禁扫）。

## 2. 怎么跑

```bash
# 1) 起宿主（席位数量与库路径可用环境变量覆盖）
GameServer__SeatCount=5 GameServer__DatabasePath=/tmp/oct.db \
  ASPNETCORE_URLS=http://localhost:5080 dotnet run --project src/OpenClockTower.Server

# 2) 起前端（浏览器只连 Vite，/hub 由 Vite 代理到上面的 5080）
cd web
npm install
npm run dev          # http://localhost:5273
```

说书人票据由服务端引导生成，落在 `Games.StorytellerTicket`（SQLite）或启动日志里；
面板只是**记住上次输入**，不会自己造票据（D-0018）。

## 3. 命令

| 命令 | 作用 |
|---|---|
| `npm run dev` | 开发服务器（HMR） |
| `npm run typecheck` | `vue-tsc --noEmit` |
| `npm run test` | vitest（纯函数与防御性渲染） |
| `npm run build` | 类型检查 + 生产构建 |
| `npm run gate` | 上面三样串起来跑 |

`web/` 不进 `OpenClockTower.slnx`（不引入 Node 到 .NET 构建链）。改动前端后，
除 `dotnet build/test/format` 外必须补跑 `npm run gate`。

## 3.1 验收批次取证（说书人 + 玩家多客户端）

```bash
node tools/verify-storyteller-panel.mjs        # 退出码 0 = 全部断言通过
```

它起真宿主 + 真 Vite + 真 Chromium，按 `--seats` 给每一席开一个**独立浏览器上下文**
（同一 SPA 的 `#player`），走完"三客户端加入 → 分配 → 开夜（阶段推送见页头）→ 钟表匠裁定 →
筑梦师请求与作答 → 信息单播 → 第一夜走完 → 第二夜代填与强制作废（无关玩家窗口采样）→
第三夜击杀请求与依赖变化（中毒解除、请求自动作废）→ 魔典主视图逐行取证 → 重建报告与降级位（4 场景）"，
把 29 张截图写进 `artifacts/web/`（gitignored；日志由调用方重定向，
如 `2>&1 | Tee-Object artifacts/web/batch-run.log`）。
场景固定三角色（clockmaker / dreamer / no-dashii），节拍配额默认 `--quota 2` 秒。

零信任负向取证：`node tools/verify-zero-trust.mjs`（退出码 0 = 全部断言通过）——真宿主 + Node SignalR
客户端扮演**篡改前端**：伪造 / 冒用 / 旧连接凭据直调 Hub，扫描玩家收包与宿主日志（拒绝审计、无凭据明文）。
它是补充装置，不替代主批次（UI / 玩法归主批次）。

**外部耦合（换机器前先核对）**：

| 耦合 | 位置 | 失败时的表现 |
|---|---|---|
| 宿主编译产物路径 `src/OpenClockTower.Server/bin/Release/net10.0/OpenClockTower.Server[.exe]` | `tools/verify-storyteller-panel.mjs` | 进程启动失败，退出码 1（脚本自己也打印路径） |
| SQLite 表 `Games`、列 `StorytellerTicket` / `SeatsJson`（`SeatId` 序列化为 `{ "value": N }`） | 同上 | 读票据抛错并退出（票据取不到就不测） |
| 席位数量 | `--seats` 与 `--assign` 必须同数（建表要求每席都有角色） | 开夜被拒 `plan.seat_unassigned`，断言失败 |
| 场景角色 | `--assign` 必须同时含 clockmaker / dreamer / no-dashii | 参数校验直接报错退出 |
| Node ≥ 22.5（`node:sqlite`）+ `npx playwright install chromium` | 本机环境 | 脚本以退出码 2 明确报"缺少 Playwright" |

## 4. 边界

- **呈现层不判规则**：能力是否生效、信息真假、合法选项都由服务端算好；
  前端只做映射与显示，`display/` 之外不出现领域判断。
- **服务端数据是输入，不是保证**（架构 §4.4）：`display/format.ts` 负责长度 / 类型 /
  范围防御；坏字段只降级该行，不许白屏。
- **连接级凭据不进呈现层**（D-0012）：Join 成功后才拿到凭据，只存网关私有字段（内存），
  每条命令经 `CommandSender`（连接 + 凭据）发出；不渲染、不落盘、不进日志；
  掉线重连重新 Join 换新凭据——旧连接的凭据在新连接上无效。
- **本地状态只允许是"呈现态"**：选中项、折叠、诊断消息；任何游戏状态一律来自视图推送，
  禁止在前端算出服务端没给的状态。
- **同步**：掉线重连后整份重取视图（`GetStorytellerView` / `JoinSeat`），
  不做本地增量补齐、不加延迟窗口。
- **文案**：角色与枚举的中文名在 `display/labels.ts`，来源 `docs/standard/terminology.md` §9；
  未知取值原样回显，不猜、不吞。
