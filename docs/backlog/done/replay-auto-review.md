# 自动化复盘：在圆盘上逐步回放每一个原子步骤

- Status: Done（批次 E29 判出：矩阵 9 行全部通过；行 3 / 行 8 的本次运行证据见「验收矩阵逐行判定」与「批次 E29」）
- Priority: High
- Depends on: 事件流（已就位：D-0010 事件是唯一事实来源）；魔典圆环（`done/grimoire-view.md`）；
  结束态与终局面（`done/win-loss-and-game-end.md`）；投影白名单（D-0012）
- 开工前定稿：R-0043（终局揭示范围）、D-0020（复盘 = 事件流呈现投影 / 两张可见面 / 步骤目录）

## 要解决的问题

一局结束后，玩家（和说书人）**没有地方看懂"这一局到底发生了什么"**：每个夜晚谁被唤醒、谁被刀、
谁中毒 / 醉酒、谁换了角色、哪一步是裁量、白天谁提名谁、票数怎么走——这些事实都在事件流里，
但界面上只留下最终状态，复盘只能靠记忆与口头还原。

目标：做一个**自动化复盘（回放）**——在魔典圆环上一步步重演整局的原子步骤，让每个人自己
就能看清来龙去脉：

1. 每一步只呈现**一个原子步骤**（一条事件 / 一次裁定），圆盘上给出对应的可视化：
   例如恶魔刀人用**红色箭头**指向目标、角色死亡在他身上标出**「死亡」**、换角 / 中毒 / 醉酒 /
   换手各自有标记；上部的步骤条显示**当前这一步发生了什么**（行动者、目标、结果）；
2. 时间轴可上一步 / 下一步 / 连续播放 / 回到开头，进度以事件序号为准；
3. **保密红线**：复盘在**游戏进行期间不得给玩家提前泄露任何隐藏信息**——它必须与其它玩家面
   一样走零信任投影，只有本局结束（`GameEndedEvent`）之后才允许对局内玩家开放。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 进行中的保密 | 结束批次之前，任何玩家端收包与投影里**零复盘字段 / 零复盘数据**（反方向断言） | 零信任装置 + 投影用例 |
| 2 | 结束后的入口 | 结束批次之后，复盘入口对局内玩家出现；无过期数据、不依赖宿主内存态 | 真机装置截图 |
| 3 | 逐步回放 · 圆盘 | 每个原子步骤在圆盘上呈现可视化：恶魔击杀 = 红色箭头；死亡 = 「死亡」标记；换角 / 中毒 / 醉酒 / 换手按事件类型各有标记 | 截图逐张复核 |
| 4 | 步骤说明 | 上部显示当前步骤的事件说明（谁、对谁、结果、原因），文案与说书人视角账本口径一致 | 截图 + 前端单测 |
| 5 | 时间轴 | 上一步 / 下一步 / 播放 / 暂停 / 回到开头可用；步进粒度 = 原子步骤，不跳步、不合并 | 真机装置 |
| 6 | 顺序保真 | 回放顺序 = 事件流序号顺序（含黄昏 / 夜晚 / 黎明 / 白天 / 裁定点 / 处决），与"按住事件流重建"同源 | 内核/集成用例 |
| 7 | 断线重连 | 刷新 / 重连后回放数据与当前位置不丢（可按序号重建）；不产生第二份事实账本 | 真机装置 + 集成用例 |
| 8 | 大局面 | 事件多时不一次性渲染全部（分页 / 懒加载 / 虚拟化），拖动与播放不卡顿 | 性能采样 |
| 9 | 相关族一致 | 复盘使用的标记 / 文案与实时魔典同一套组件与口径，不出现两套说法 | 结构审查 |

## 实施结论（2026-10-04）

- **投影**：`ReplayProjection`（Application）读整条事件流 → `StepMachine` + `GameState` 同源折叠 →
  逐步输出步骤；`ReplayStepCatalog` 显式登记 9 个 presenter 族（阶段 / 槽位 / 选择 / 能力 / 状态与效果 /
  白天 / 触发 / 控制 / 终局），**每个 `GameEvent` 类型要么被认领、要么在排除清单里**（覆盖率门禁锁死）。
  排除项只有两类：说书人注记（D-0019，任何复盘都不呈现）与步骤机节拍内部事件
  （`SlotEntered` / `SlotQuotaElapsed` / `SlotAdvanced` / `SlotForceAdvanced`——D-0013 / D-0014 的驱动机制）。
- **可见性闸**：`ReplayQueryService` **只读 `IGameStore`**（不依赖宿主内存态），按事件流自己判结束；
  玩家在结束批次之前一律拒绝并记审计；Hub `GetReplay` 按事件序号分页（`afterSequence` / `pageSize`）。
- **契约**：`ReplayViewDto` / `ReplayStepDto` / `ReplaySeatDeltaDto` / `ReplayMarkerDto`——终局后才下发的
  **新契约类别**，在 `PlayerProjectionLeakGateTests` 里显式登记（既不进进行中玩家面扫描，也不冒充说书人专属）。
- **前端**：`features/replay/ReplayPanel.vue` + `ReplayCircle.vue`；席位牌抽到
  `web/src/features/grimoire/GrimoireSeatCard.vue` 供实时魔典与复盘共用（矩阵行 9）；
  位置写 URL `?replay=<事件序号>`，刷新 / 重连后按序号重建并自动重开（矩阵行 7）。
- 播放节拍是**呈现态**，与 D-0013 的对局配额无关；复盘不建第二账本、不写新事件。
- **E29 补正（2026-10-04）**：真机取证发现侵染时**原方古自死**（方古侵染的另一半事实，`CausedBy` = 自己）
  会被画成「3 号 → 3 号」的恶魔击杀红箭头——判据虽然与 R-0038 同源，但"自己导致自己的死亡"不是"被击杀"。
  先红后绿：`ReplayProjectionTests.DemonSelfDeath_HasNoKillArrow` 先失败 → `StateReplayPresenter.IsDemonKill`
  增加自指排除 → 转绿；装置复跑该步只剩死亡帷幕（截图 `cc-21`）。

### 验收矩阵逐行判定（证据 = 2026-10-04 批次 E28 运行；行 3 / 行 8 = 批次 E29 运行）

| # | 判定 | 证据 |
|---|---|---|
| 1 | 通过 | 集成用例 `ReplayHostTests`（结束前玩家 `GetReplay` 被拒 + Application 审计）；`verify-zero-trust.mjs` 43 项全绿（禁词表加入 `replay` / `markers` / `steps`）；`verify-winloss.mjs` 段 3「结束批次之前：玩家端没有复盘入口」 |
| 2 | 通过 | 取证档 `verify-winloss.mjs --quota 2 --screenshots-all` 段 6/7：结束后玩家端出现入口、面板打开并停在首步；数据由 `GetReplay` 从持久化事件流重建（截图 `winloss-04-player-replay.png`） |
| 3 | 通过 | 取证档 `verify-character-change.mjs --quota 2 --screenshots-all`：说书人实时面复盘逐步回放到五类标记——`cc-15` 实时面（口径「说书人实时面：进行中的事实」）/ `cc-16` 醉酒（第 13 步）/ `cc-17` 换角（第 28 步，带「呆瓜 → 方古」归属）/ `cc-18` 恶魔击杀（第 41 步，5 号 → 4 号，SVG 红箭头存在）/ `cc-19` 换手（第 47 步，「原行动者 2 号 → 1 号」）/ `cc-20` 中毒（第 52 步）；`cc-21` 记侵染时原方古自死只画死亡帷幕、不画箭头；断言含圆盘图例文案与标记归属。`ReplayProjectionTests` 覆盖各标记逻辑 |
| 4 | 通过 | 取证档截图可见步骤说明栏（步骤族 / 短文案 / 原因）；`ReplayStepCatalogTests` 覆盖全事件类型；`display/replay.spec.ts` 8 项 |
| 5 | 通过 | 取证档段 6/7：第 2/26 步 ↔ 第 3/26 步、事件序号 2 ↔ 3，控制条（回到开头 / 上一步 / 播放 / 下一步）真机可用 |
| 6 | 通过 | `ReplayProjectionTests`：顺序 = 事件序号；`ReplayHostTests`：步骤序号严格递增；目录覆盖率门禁保证 1 步 = 1 事件、不跳步不合并 |
| 7 | 通过 | 取证档段 6/7：刷新后自动重开复盘并回到「第 2 / 26 步 · 事件序号 2」（截图 `winloss-05-player-replay-restored.png`） |
| 8 | 通过 | 新装置 `verify-replay-scale.mjs --quota 2 --screenshots-all`：2600 条真实命令写入 2605 步事件流（分页 6 页 / 492ms，序号严格递增）；服务端投影首页 / 中段 / 深页各 3 次采样 = 66–78ms / 66–72ms / 70–74ms；前端首屏 91ms、第 200→201 步翻页 55ms、深页定位（第 2000 步）542ms、第 2000→2001 步翻页 75ms；已加载窗口严格 200 → 400 → 2000 → 2200（懒加载证据）；截图 `replay-scale-01/02`。契约侧 `ReplayProjectionTests.LargeStream_PagesInDefaultWindows_WithoutSkippingOrDuplicating` |
| 9 | 通过（结构审查） | 席位牌抽到 `web/src/features/grimoire/GrimoireSeatCard.vue` 共用；标记 slug 先登记术语表（§7）；中毒 / 醉酒标记复用 `buildSeatMarks`，枚举文案复用 `display/labels` |

### 残余事项（批次 E29 后）

1. ~~行 3 的真机取证~~：已补——说书人实时面五类标记逐张截图与断言（`cc-15…cc-21`），见行 3。
2. ~~行 8 的性能采样~~：已补——新装置 `tools/verify-replay-scale.mjs` 的 ≥ 2000 事件规模采样，见行 8。
3. ~~复盘文案里的「谁」仍是席位号~~：**已关闭（批次 E30，冻结版本 `451ab23`）**——
   `account-and-display-name` 票落地后，复盘步骤文案与圆盘标记统一为「N 号 · 玩家名」
   （服务端口径 `ReplaySeatText`，前端标记走 `seatDisplayOf`）；无名字的席位仍回退「N 号」。
   证据：`verify-accounts.mjs` 第 20–21 项 + 截图 `accounts-05`，本票矩阵行 1 的「谁」不再需要心算翻译。

### 批次 E28（冻结版本 `b52ecdb`，2026-10-04）

- `dotnet build` / `dotnet test`：**798 通过**（Kernel 327 / Rules 314 / NormativeGates 24 / Integration 133）；
- `dotnet format OpenClockTower.slnx` 就地通过；
- `npm run gate`：typecheck + lint + **vitest 125 通过** + build；
- `node tools/verify-winloss.mjs --quota 2 --screenshots-all`：**25 项全绿**，截图 `winloss-01..05`；
- `node tools/verify-zero-trust.mjs`：**43 项全绿**（含复盘禁词扫描）。

### 批次 E29（冻结版本 `e454032`，2026-10-04）

本批含一处复盘呈现修正（自指击杀箭头，见「实施结论」），故按「本票装置 + 同族回归 + 规模采样」取证：

- `node tools/verify-character-change.mjs --quota 2 --screenshots-all`：**84 项全绿**（截图 `cc-15…cc-21` 本次运行写入）；
- `node tools/verify-replay-scale.mjs --quota 2 --screenshots-all`：**18 项全绿**（截图 `replay-scale-01/02`）；
- `node tools/verify-winloss.mjs --quota 2 --screenshots-all`：**25 项全绿**（玩家复盘面同族回归）；
- `node tools/verify-zero-trust.mjs`：**43 项全绿**；
- 冻结版门禁：`dotnet build` 0 警告 / 0 错误；`dotnet test` **800 通过 / 0 失败**
  （Kernel 327 · Rules 314 · Integration 135 · NormativeGates 24）；`dotnet format` 就地通过；
  `npm run gate` 全绿（typecheck + lint + **125** 前端单测 + build）；
- 范围说明：产品改动只在 `StateReplayPresenter`（复盘读侧投影），主装置不经过 `GetReplay`，故未重跑（E27 先例）。

## 决定与依据

- **不新增事实源**：复盘是**事件流的呈现投影**（D-0010：事件是唯一事实来源）——不建第二份账本、
  不写新事件；任意时刻可由「快照 + 序号之后的事件」重建，因此断线重连天然成立（D-0011 / D-0014）。
- **保密红线（D-0012）**：复盘面在**结束批次之后**才对局内玩家开放；进行中任何玩家端不得出现
  复盘字段。结束后的揭示范围按 **R-0043**（终局揭示）执行；说书人面为实时面，两面同数据、闸不同。
- **说书人端**：说书人在游戏中已有上帝视角；"复盘"对说书人可以是实时面，对玩家必须是终局面——
  两条可见面分开建模（同一份数据、两种投影），口径见 D-0020。
- **呈现复用**：圆环 / 席位牌 / 标记复用 `grimoire-view` 的组件与术语；新增标记先进
  `docs/standard/terminology.md` 再落代码（本轮登记：换角 `character-change`、换手 `role-rebind`、
  击杀箭头 `kill-arrow`、复盘 `replay` / `replay-step` / `replay-marker`）。
- **性能口径**：回放数据随局时长增长，必须有分页 / 懒加载策略；播放节拍与既有恒定配额（D-0013）
  无关（回放不是对局节奏）。
- **自指归因不算「被恶魔击杀」**（E29 补正）：方古侵染时原方古的死亡事件 `CausedBy` 是自己；
  判据与 R-0038 同源，但"自己导致自己的死亡"不是"被击杀"——复盘只画死亡帷幕、不画红色箭头。
