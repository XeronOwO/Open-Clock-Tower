# 钟盘投票形态：设计与实现（R-0017 目标形态）

- Status: Done
- Priority: Medium
- Depends on: `docs/standard/rulings.md` R-0017（已转 Decided）；AGENTS.md 内核确定性硬约束；D-0008 / D-0010 / D-0011 / D-0013 / D-0015

## 要解决的问题

R-0017 旧实现是「投票开放窗口 + 说书人计票快照」。需求方 2026-10-04 给出**目标形态**：

- 提名时：蓝色时针指向提名者、红色分针指向被提名者（钟盘意象）；
- 双方发言结束后：说书人点「开始」→ 倒计时（默认 3 秒，可调）→ 分针旋转逐席收票
  （间隔默认 1 秒，可调）；
- 玩家在分针指向自己时完成表态（举手 = 投这一票）；
- 票面与结果按线下「绕圈点数」的等价语义。

本票把它设计并落地；实现与考试证据见下。

## 设计定稿（2026-10-04）

### 1. 内核确定性：节奏在控制面，内核只收显式输入

- 倒计时、旋转间隔、分针位置都是**真实时间**，只存在于服务端控制面（`VoteSweepPacer`，
  与 `SlotQuotaPacer` 同点挂进 `GameSession.TickAsync`）。
- 内核只收显式输入：`StartVoteSweepInput`（带参数）、`CollectSeatVoteInput`（第 N 席到点）、
  `CastVoteInput`（举手 / 放下）、`ResumeVoteSweepInput`（中断后续收）；产出
  `VoteSweepStartedEvent` / `SeatVoteCollectedEvent` / `VoteSweepResumedEvent` / `VoteCountedEvent`。
- 给定「输入序列」，内核结果唯一；回放 / 重建 / 重连走同一事件流（D-0010）。
- 收票顺序按**席位号升序**（绕圈一圈，与圆盘布局一致；线下点数顺序不影响结果）。
- 收票时间轴：第 k 席（1 起）在 `开始时刻 + 倒计时 + k × 间隔` 到点收票；
  全部收完才能计票（总时长 = 倒计时 + 席位数 × 间隔）。

### 2. 逐席收票语义：严格时点（先举也算、过时不候）

- 分针指向某席的那一刻，以该席**已登记的举手状态**（最近一条 `VoteCastEvent`）为准，固化成一条
  `SeatVoteCollectedEvent`；**不设反应窗口**。
- 理由：需求方口径是「线下绕圈点数的等价语义」——线下就是"指到你时手已经举着"；网络抖动由
  **可调间隔**吸收（表卡就把间隔调大），不引入隐藏宽限（AGENTS 同步纪律禁止"保护期"式写法）。
- 举手窗口 = 说书人「开始」之后、本席被收票之前；本席收票后再举手 / 放下被显式拒绝
  （`day.seat_collected`）。
- 每次举手 / 放下照记 `VoteCastEvent`（含角色快照）：卖花女孩等回溯型能力读"举手动作"的口径
  （R-0037：撤回不撤销举手）不变。

### 3. 参数归属：本次收票的显式输入，进事件流、不参与判定

- 参数由说书人在「开始」时提交：倒计时默认 `3000ms`、间隔默认 `1000ms`；范围
  `1000–10000ms` / `300–5000ms`，范围外由合法性闸与内核同尺拒绝（`VoteSweepLimits`）。
- 参数随 `VoteSweepStartedEvent` 进事件流：事件流是唯一持久真相（D-0010），
  重连 / 重启 / 重建都要能复现节奏呈现；**判定不读参数**（同输入 + 不同参数 → 同一计票结论，
  内核用例 `VoteSweep_ParametersDoNotChangeAdjudication` 锁住）。
- 参数不是会话级持久设置（避免"一局中途悄悄改口径"）；前端只把上次输入当**呈现态**记住（可丢）。
- 剩余时间由服务端读取投影时算出（`NextBeatMilliseconds`）并下发；客户端只做动画，不驱动推进；
  客户端时钟不参与（D-0013 §6），重连后以新投影为准。
- **服务端重启 / 重建的重入**：未完成的收票不追补（断线期玩家无法举手）——恢复后标记「已中断」，
  说书人点「继续收票」产出 `VoteSweepResumedEvent`，重新起一次倒计时，从下一未收席位继续；
  每席实际收集时刻仍以事件流存储时间戳如实保留，不伪造历史。

### 4. 与现状的关系：替换，不保留双形态

- 取消「投票开放窗口 + 说书人随时快照计票」。`CastVoteInput` 语义由"投票 / 撤回"改为
  "举手 / 放下"；`CountVotesInput` 保留，语义改为"**收票全部完成后的计票**"：票面 = 逐席冻结
  结论；原有三条件（严格最多 / ≥ 存活一半 / ≥1）与平局、后来者规则不变；未收完 → 显式拒绝
  `day.sweep_incomplete`。
- 不保留两种模式（另立模式会让投影、装置与回放分叉；AGENTS：废弃机制立即删干净）。
- 旧事件类型保留用于旧日志回放：`VoteCastEvent` 在 `Sweep` 为空时仍按旧形态折叠
  （内核用例 `Replay_LegacyWindowStream_StillFolds_AndMigratesToSweep`）。
- UI：说书人白天面板新增「开始收票 / 继续收票」+ 参数输入 + 钟盘；「计票」只在收票完成后可点；
  玩家端新增举手开关 + 钟盘。不开收票直接计票会被拒绝（`day.sweep_not_started`）。

### 5. 投影与公开面：举手与已收票数对全体可见

- 公开面 = 线下公开面的渲染等价物（R-0017 第 5 条在新形态下的等价物）：蓝针 = 提名者、
  红针 = 被提名者固定不变；旋转期间**当前席位**、**当前举手表**、**已收票结论（逐席冻结）**
  对全体玩家可见——线下说书人绕圈时，谁举着手、点到谁、数到几所有人都看得见。
- 死亡玩家：举手时仍受「死后仅一次」约束（票权已用 → `day.vote_token_spent`）；收票时若该席
  已死亡且举手 → 冻结为赞成，计票时消耗票权（`SpentVoteTokens` 与旧形态一致）。
- 撤销：本席被收票前可反复举手 / 放下；收票后不可改；提名本身不可撤（事件流事实）。
- 投影形状：`DayNominationDto` 含 `HandsRaised` 与 `Sweep`（`Phase` / `CurrentSeat` /
  `Collected` / 两个参数 / `NextBeatMilliseconds`）；`PlayerDayDto` 含 `SeatCollected`；
  `NextBeatMilliseconds` 是读取时算出的呈现便利，不构成新事实。

## 实现方案（分层）

| 层 | 改动 | 要点 |
|---|---|---|
| Kernel | `VoteSweepState` / `VoteSweepLimits` / 三个收票事件与输入；`DayMachine` 的 `StartVoteSweep` / `CollectSeatVote` / `ResumeVoteSweep` 与 `CastVote` / `CountVotes` 改造；`DayLedgerFolder` / `StepMachineStateComparer` / `GameStateMachine` / `StepMachineFolder` / `StepMachine` 同步；旧日志回放兼容 | 内核无时间；顺序 / 重复 / 越权输入显式拒绝；回放等价 |
| Application | `StartVoteSweepCommand` / `CollectSeatVoteCommand`（系统） / `ResumeVoteSweepCommand`；`VoteSweepGate`（身份与参数形状）；`VoteSweepPacer` 挂 `TickAsync`；`SessionTrackers` 收票锚点与中断；`VoteSweepProjection` / `PlayerDay` / `StorytellerView`；`DayReplayPresenter` 收票步骤 | 时间只在应用层（D-0008）；中断不追补；投影同源 |
| Contracts / Server | `DayVoteSweepDto`；`DayNominationDto` / `PlayerDayDto` 扩展；`ProjectionMapper`；`GameHub` 的 `StartVoteSweep` / `ResumeVoteSweep`；`web/src/contracts/game.ts` 镜像 | 公开面同一份；镜像与泄漏门禁 |
| Web | `VoteDial.vue` + `voteDial.ts`（几何纯函数）；`DayControl.vue`（开始 / 参数 / 继续 / 计票 + 钟盘）；`PlayerDayPanel.vue`（举手开关 + 钟盘）；`display/format.ts` 防御性归一化 | 呈现不判规则；使能条件来自服务端 |
| 测试 / 装置 | 内核 `VoteSweepMachineTests`（10 条）；`DayMachineTests` / `DayFactSnapshotTests` / `DayPhaseFixture` 适配；集成 12 处白天流程改造 + `VoteSweepTestDriver`；主装置 day1 与四个辅助装置白天段改造 | 先红后绿；装置默认迭代档，取证显式 |

## 验收矩阵（2026-10-04 判出）

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 提名展示 | 蓝针指提名者、红针指被提名者（说书人 / 玩家同一份） | **通过**：主装置 day1 断言「蓝针 2 号 / 红针 1 号」+ 截图 `32-day-dial-countdown.png`；集成 `PlayerDayProjection_IsPublicOnlyAndIdenticalAcrossSeats` 断言两端公开事实一致 |
| 2 | 开始与旋转 | 点开始 → 倒计时（默认 3s / 可调）→ 分针逐席旋转（默认间隔 1s / 可调） | **通过**：主装置 day1 断言默认 `3 / 1`、改为 `2 / 0.5`、相位 `Countdown → Collecting → AwaitingCount` + 截图 `32/33`；内核 `StartVoteSweep_RequiresOpenNomination_AndValidParameters` |
| 3 | 逐席语义 | 先举也算、过时不候：收票前可反复举手 / 放下，收票后拒绝 | **通过**：内核 `VoteSweepMachineTests` 语义用例（收票时刻冻结 / 收票后拒绝 / 顺序校验）+ 集成 `DayPhaseHostTests` 收票链路 |
| 4 | 公开面与票权 | 举手 / 已收票 / 当前席位对全体可见；死亡玩家举手消耗票权 | **通过**：集成投影用例（举手立即公开、逐席公开一致）+ 内核 `CastVote_DeadPlayer*` + 主装置 `data-nomination-hands` 断言 |
| 5 | 计票 | 收票完成后计票；三条件与平局规则不变；参数不影响结论 | **通过**：内核 `CountVotes_*` 与 `VoteSweep_ParametersDoNotChangeAdjudication` |
| 6 | 重连 / 回放 / 重启 | 快照 + 事件重建同一过程；重启后中断 → 继续 → 从下一席收 | **通过**：内核 `Replay_FoldingTheProducedEvents_EqualsTheHandledState` / `Replay_LegacyWindowStream_StillFolds_AndMigratesToSweep` + 集成 `RestartDuringDay_RestoresDayLedgerAndCanContinue`（断言重启后 `Interrupted`、续收后收完计票） |
| 7 | 参数边界 | 默认值 / 范围外拒绝 / 可调且不影响判定 | **通过**：内核边界用例 + `VoteSweepGate` 合法性闸 + 主装置参数改 `2 / 0.5` 后链路照跑 |

## 结果（2026-10-04）

- 冻结版：`main` @ `be4d403`（实现与装置脚本；取证后只补了一条旧日志回放用例与文档，
  产品代码 / 装置脚本未再改动）。
- 门禁：`dotnet build` 0 警告 0 错误；`dotnet test` **861 通过 / 0 失败**
  （Kernel 335 · Rules 314 · Integration 188 · NormativeGates 24）；`dotnet format` 就地通过；
  `npm run gate` 全绿（typecheck + lint + **163** 前端单测 + build）。
- 装置取证档（`--quota 2 --screenshots-all`，一次完整取证档运行）：
  - 主装置 `verify-storyteller-panel.mjs`：**202 项全过**（day1 19/19；day1 段 6.8s；合计 116.2s；截图 39 张）；
  - `verify-winloss.mjs` **28** / `verify-witch.mjs` **28** / `verify-death-triggers.mjs` **73** /
    `verify-retro-info.mjs` **56**，全部通过。
- 截图复核：`32-day-dial-countdown.png`（蓝针 2 号、红针 1 号、倒计时 2）、
  `33-day-sweep-collecting.png`（当前指向 3 号、已收 2 席）——与断言一致；其余 37 张为同一次
  运行写入，未逐张复核（如实记录）。
- 诚实记录：
  - 主装置迭代档本轮 44.3s（历史 27.0s：白天钟盘收票固定约 5s + 本轮冷启动 Release 重建），
    取证档 116.2s——下一次装置迭代报耗时按此基线；
  - 旧日志兼容目前只有内核回放用例覆盖，没有真机旧库样本（自用项目旧库主要是短局）；
  - 设计定稿 5 条由代理按「线下等价 + 内核确定性 + 不引入隐藏宽限」定稿。验收时如与需求方
    预期不符，先改 R-0017 与本节，再改代码。

## 决定与依据

- 需求方口径：2026-10-04 会话（钟盘投票形态；倒计时默认 3s、间隔默认 1s，可调）；
- 设计定稿 5 条：2026-10-04（本轮）——严格时点收票、参数属本次收票的显式输入且不参与判定、
  替换旧形态、举手与已收票公开、重入不追补；
- 依据：R-0017（已转 Decided）、百科《投票》《规则概要》（2026-10-01 抓取）、
  硬约束见 `AGENTS.md` 与 D-0008 / D-0010 / D-0011 / D-0013 / D-0015。
