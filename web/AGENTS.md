# web/ — 前端

约束 `web/` 下的代码。**这里只写"怎么跑、边界在哪"**；架构与取舍在 `docs/decisions/active.md` D-0018。

## 1. 是什么

Vue 3 + TypeScript + Vite 的单页应用，**两套视图同一个构建**，按**地址路径**分面
（空地址与 `/home` = 首页 · `/play` = 玩家 · `/storyteller` = 说书人；解析在 `display/navigation.ts`，
地址怎么变在 `display/routing.ts`）：

- 说书人面（`/storyteller`）：魔典圆环、席位操作台（卡点 / 裁定 / 上报）、数据与审计抽屉、
  局务（开夜 / 白天控制 / 接管 / 重建 / 开局分配）；
- 玩家面（`/play`）：只显示服务端下发给该玩家的席位、阶段、请求、信息结果与白天公开事实 / 操作。

两者**从不共享视图数据**：玩家侧不出现说书人专属字段（有门禁扫）。

## 2. 怎么跑

```bash
# 1) 起宿主（席位数量与库路径可用环境变量覆盖）
GameServer__SeatCount=5 GameServer__DatabasePath=/tmp/oct.db \
  ASPNETCORE_URLS=http://localhost:5080 dotnet run --project src/OpenClockTower.Server

# 2) 起前端（浏览器只连 Vite，/hub 由它代理到 5080）
cd web && npm install && npm run dev     # http://localhost:5273
```

账号是唯一的身份证（D-0027）：没登录时每个面只有一张登录卡（`features/account/AccountGate.vue`）；
登录后会话由 `services/accountSession.ts` 的模块级单例持有，三个面共用（换面不重登），票据已整个退场；
刷新不掉登录、关标签页即清（D-0029，边界见第 4 节）。

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

装置清单（二十一个装置 / 夹具 / 档位 / 分段 / 退出码）在 `docs/acceptance/devices.md`；本页只留入口与外部耦合。

```bash
node tools/verify-storyteller-panel.mjs        # 主装置：退出码 0 = 全部断言通过
```

真宿主 + 真 Vite + 真 Chromium，按 `--seats` 每席开**独立浏览器上下文**（同一 SPA 的 `/play`）；
截图与日志进 `artifacts/web/`（gitignored，可重生成）。默认迭代档（0.3s/槽、不落盘截图），
**正式取证必须显式** `--quota 2 --screenshots-all`（一批一次、只对冻结版本）。

**外部耦合（换机器前先核对；逐条清单与失败表现见 `docs/acceptance/devices.md` §3）**：
宿主编译产物路径 · SQLite `Games.SeatsJson` 列形状（读法收在 `tools/lib/entrance.mjs`）·
入场界面锚点（同文件头部注释）· Hub 方法名（SignalR 不支持重载，改名即换契约）·
SignalR 帧尾 `\x1e` 分隔符 · **每席位只保留一条连接**（给同席另开客户端会假绿）·
Node ≥ 22.5 + `npx playwright install chromium` · `--seats` 与 `--assign` 同数。

## 4. 边界

- **呈现层不判规则**：能力是否生效、信息真假、合法选项、白天能不能提名 / 投票都由服务端算好；
  前端只做映射与显示，`display/` 之外不出现领域判断。
- **服务端数据是输入，不是保证**（架构 §4.4）：`display/format.ts` 负责长度 / 类型 / 范围防御；
  坏字段只降级该行，不许白屏。
- **选项文案只换称呼、不丢语境**：`optionDisplayOf` 对 `seat:` 选项把「N 号玩家」换成「N 号 · 名字」，
  服务端预览的后半句（如「（已死亡）：…」）原样保留——那是玩家做选择的判据。
- **凭据的落盘边界**（D-0012 / D-0029）：连接级凭据只存网关私有字段（内存），每条命令经
  `CommandSender`（连接 + 凭据）发出，不渲染、不进日志；**账号会话凭据**持久化在 `sessionStorage`，
  唯一出口 `services/browserSession.ts`（启动经 `AccountHub.Resume` 确认后回到原处，关标签页即清）。
  掉线重连重新 Join 换新凭据——旧连接的凭据在新连接上无效。
- **本地状态只允许是"呈现态"**：选中项、折叠、诊断消息；任何游戏状态一律来自视图推送，
  禁止在前端算出服务端没给的状态。
- **同步**：掉线重连后整份重取视图（`GetStorytellerView` / `JoinSeat`），不做本地增量补齐、不加延迟窗口。
- **文案**：角色与枚举的中文名在 `display/labels.ts`，来源 `docs/standard/terminology.md` §9；未知取值原样回显。
- **控制字符清洗不用正则**：ESLint `no-control-regex` 会拦下 `[\u0000-\u001f]` 这类字面量
  （2026-10-03 实测报错）；逐字符判定 `codePoint < 0x20 || === 0x7f`，见 `display/format.ts`。
