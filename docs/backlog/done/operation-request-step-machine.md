# 自动步骤机与操作请求

- Status: Done
- Priority: High
- Depends on: 建解决方案、项目骨架与门禁工程；内核领域模型与六状态不变量

## 要解决的问题

现在没有任何"轮到你了"的机制。玩家端要么得自己盯着看，要么得靠外部语音喊——
这正是官方魔典那种"说书人手工推进"的老路。需求方要求做成**类似狼人杀的自动步骤系统**：
轮到 1 号玩家发动技能时，**服务端主动向他推送操作请求**。

同时明确：**操作请求暂不设计超时时间**。

## 要做的事

1. **步骤机 `StepMachine`**（内核）：
   - 驱动阶段（首夜 / 其他夜晚 / 白天 / 结算中）与夜晚顺序表上的步骤推进；
   - 能在**挂起点**暂停，并把挂起状态**变成可持久化的数据**（不是内存里的等待）；
   - 挂起期间接受"响应""作废""代填"三种输入。
2. **操作请求 `OperationRequest`**（内核 + 应用层）：
   - 与说书人的裁定点（`DecisionPoint`）**同源不同受众**：同一个内核原语，两套投影；
   - 持久化；断线重连后**重新投递**（这是自动重连的内在部分，不是附加功能）；
   - **无超时字段**。内核对请求只有"作废原因"，没有"过期时间"（`D-0011`）。
3. **推送通道**：SignalR 定向到该玩家的连接；服务端发起，客户端**不轮询**。
4. **无超时的对冲（不是可选项）**：
   - 说书人可**强制作废**请求，也可**代填**；
   - 说书人端有「谁在卡着」的可见列表（卡了多久、卡在哪一步）；
   - 每次作废/代填都记录：谁操作的、原因、当时状态。
5. **作废的触发条件**：上游状态变化使请求失去意义时（例如目标玩家已死亡、角色已变更、
   阶段已推进），必须自动作废并给出原因，而不是静默丢弃。
6. **自动化只是建议（D-0014）**：说书人可随时接管——强推当前槽位（跳过剩余配额）、
   切换接管模式手动驱动、按事件日志重建房间状态；这些动作在"玩家卡住 / 逻辑出错 /
   无合法选项阻塞"时都必须可用，且全部带审计（谁、为什么、当时状态）。

## 验收矩阵

| # | 场景 | 期望 | 依据 |
|---|---|---|---|
| 1 | 夜晚推进到某玩家的行动步骤 | 该玩家连接收到定向操作请求；**其他玩家收不到** | D-0011 |
| 2 | 该玩家断线后重连 | **重新收到**同一个未响应的请求，内容与序号一致 | D-0011 硬约束 2 |
| 3 | 玩家长时间不响应 | 请求**一直有效**，不自动过期、不自动跳过 | 需求方明确"暂不设计超时" |
| 4 | 说书人查看卡点 | 能看到"谁在卡着、卡在哪一步、卡了多久" | D-0011 代价条款 |
| 5 | 说书人强制作废该请求 | 请求作废、结算按细则继续；有审计记录 | D-0011 代价条款 |
| 6 | 说书人代填 | 请求被响应，事件里标明"由说书人代填" | D-0011 代价条款 |
| 7 | 请求挂起期间**服务端重启** | 重启后请求仍在，玩家重连即可响应 | D-0011 硬约束 4 |
| 8 | 请求挂起期间目标玩家死亡/角色变更 | 请求自动作废并记录原因 | 本票据第 5 条 |
| 9 | 玩家提交不在合法集合里的选项 | 被**合法性闸**拒绝，状态不变，有日志 | D-0012 四道闸 ③ |
| 10 | 玩家在非自己回合提交响应 | 被**阶段闸**拒绝 | D-0012 四道闸 ② |
| 11 | 同一响应重复投递两次 | 幂等：只生效一次 | D-0012 四道闸 ④ |

### 防时序泄漏（D-0013，与上面同等重要）

| # | 场景 | 期望 | 依据 |
|---|---|---|---|
| 12 | 「全部夜间角色死亡」的夜晚 vs「全部夜间角色存活」的夜晚 | 两条**客户端可观测时间线**观察不到可区分差异 | D-0013 可测性 |
| 13 | 某角色的槽位因角色不在场/已死亡被跳过 | 仍是**空槽位**，照样走完配额 | D-0013 §1 |
| 14 | 玩家在槽位内**秒回**响应 | 不提前进入下一步，走完该槽位剩余配额 | D-0013 §4 |
| 15 | 请求被作废 / 被说书人代填 | **不缩短**夜晚 | D-0013 §4 |
| 16 | 玩家端在夜晚查看界面 | 只有统一界面：**无**轮次指示、无进度、无"谁在思考" | D-0013 §5 |
| 17 | 非当事玩家的设备在他人槽位期间 | **无任何活动指示**（操作请求是单播） | D-0013 §5 |
| 18 | 比较连续两个夜晚的时长 | 节奏一致（首夜与其他夜晚的差异属公开知识，其余不可观测） | D-0013 §2 |
| 19 | 黎明宣布的等待 | 纳入同一节奏，不是独立计时 | D-0013 §3 |
| 20 | 客户端本地时钟被篡改/加速 | 不影响服务端节奏判定 | D-0013 §6 |

### 接管、兜底与恢复（D-0014）

| # | 场景 | 期望 | 依据 |
|---|---|---|---|
| 21 | 玩家卡住 / 程序逻辑出错 / 无合法选项阻塞时，说书人强推当前槽位 | 挂起请求按 Override 原因了结、状态机继续推进；审计含谁 / 原因 / 当时状态 | D-0014 能力 1 |
| 22 | 说书人切接管模式 | 自动节拍暂停、手动逐步推进生效；交还后自动推进恢复 | D-0014 能力 2 |
| 23 | 房间数据异常（投影与事件不一致 / 重放失败） | 说书人触发重建后状态与事件日志一致；重建失败显式报错、不静默继续 | D-0014 能力 3 |
| 24 | 重建失败之后 | 兜底入口仍可用（可强推继续），房间不因异常而永久锁死 | D-0014 能力 1 + 3 |

## 决定与依据

- 自动化是建议、说书人可接管（强推 / 接管模式 / 重建）：`docs/decisions/active.md` D-0014
- 操作请求的定位、硬约束与代价：`docs/decisions/active.md` D-0011
- 步骤机与两种原语的对照：`docs/architecture/current.md` §2.7
- 四道闸的定义：`docs/architecture/current.md` §4.2
- 术语：`docs/standard/terminology.md` §8（操作请求 / 步骤机 / 挂起点 / 作废 / 代填）

## 备注

"暂不设超时"是**需求方的明确选择**，不是遗漏。将来若要加超时，必须新增一条决策，
并说明它如何与"请求持久化 + 重连重投"共存——不允许用前端计时器假装超时。

## 落地与运行证据（2026-10-01）

### 已交付

- **Kernel**：`ChoicePrompt` 同源选择契约（`DecisionPoint` 改为其投影）；`StepPlan` / `StepSlot` /
  `StepMachineState` / `StepMachine`（纯迁移 `Handle` 与事件折叠 `Apply` 成对）；操作请求生命周期
  （**无超时字段**，只有作废原因）；座位依赖自动作废；强推 / 接管 / 释放 / 阻塞兜底；16 种事件
  （含 `SeatStateChangedEvent`：谁因何原因变化，供说书人上帝视角）；`StepMachineStateComparer` 供重建校验。
- **Application**：`GameSession` 编排（四道闸 → 内核 → 事件 + 快照 + 回执**原子提交** → 投影 / 通知）；
  玩家 / 说书人投影（玩家投影无轮次与进度字段）；重连包（快照 + 补齐）；卡点时长；房间重建。
- **Server**：ASP.NET Core 宿主 + SignalR **定向单播**；EF Core + SQLite（事件 / 快照 / 回执 / 会话票据）；
  服务端节拍器（接管暂停、重启补 tick）；演示步骤表为**显式占位**（真实顺序表属 `OpenClockTower.Rules`；**已被取代**，见残余事项 5）。
- **依赖安全**：EF Core 10.0.0 传递依赖的 `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 有已知高危漏洞
  （GHSA-2m69-gcr7-jv3q / CVE-2025-6965），已显式升到修复版 2.1.13，未压制 NU1903。
- **测试**：Kernel 61 / 规范门禁 8 / 集成 19 = **88 条全绿**；新增两条门禁先见红后复绿。

### 门禁（真实运行输出）

| 门禁 | 结果 |
|---|---|
| `dotnet build OpenClockTower.slnx` | 0 警告 0 错误 |
| `dotnet test OpenClockTower.slnx` | 93/93 通过（Kernel 63 + Gates 8 + Integration 22） |
| `dotnet format --verify-no-changes` | exit 0 |
| 新门禁先红 | `OperationRequestNoTimeoutGateTests` → FAIL（违规 token：`Timeout`）；`PlayerProjectionLeakGateTests` → FAIL（`PlayerViewDto.cs → Slot`）；还原后复绿 |
| 独立对抗性自检 | 子代理在冻结树上复核，报 4 高 / 6 中 / 5 低；高危与中危**本轮全部修复并补回归**（见下），低危一并处理 |

**自检发现与处置**（2026-10-01）：

| 发现 | 处置 |
|---|---|
| 重连包把整条原始事件流下发给玩家（他人请求、槽位、夜晚顺序全泄露） | 新增 `PlayerEvent` / `PlayerEventDto` **白名单投影**；`JoinSeat` 只下发"公开阶段 + 发给自己的请求/响应/作废"，并对 `lastSequence` 做钳制；新增行 16 反方向用例与 JSON 断言 |
| 泄露断言只扫 3 个文件、没有运行时反方向证据 | 门禁扫描范围扩到 6 个玩家面向文件；新增集成用例断言旁观者对所有推送类型零消息 |
| 行 7"重启"断言不可能失败（请求 ID 是确定性字符串） | 补断言：重启后序号与首次一致、`PhaseStartedEvent` 恰好一条、重连包序号与首次一致（若"重新播种"这条错误路径发生则必红） |
| 事件载荷损坏会让宿主启动崩溃、房间永久锁死 | 序列化层把 `JsonException` 包成显式错误；引导服务捕获后停在空状态并记 Critical；恢复失败时保留序号连续性；新增损坏载荷用例（起得来、重建显式失败、宿主可显式重开阶段） |
| 行 8 缺少"只改角色"分支 | 演示计划声明角色依赖；新增 `Row8b`（只改角色也必须作废，且不得编造未观测维度） |
| 重建结果不暴露"是否一致" | `RoomRebuildReport`（内存一致 / 快照一致）经 `CommandResultDto` 回给说书人；用例断言脏快照 → `SnapshotEquivalent=false` |
| 状态变化输入强制同时给生死与角色，可能误判 | 两个维度改为可空 + `Reason`/`CausedBy`；内核只对**实际观测到**的维度判定，新增两条内核用例 |
| 客户端序号从未被使用 | 进入接受 / 重放日志；行 20 用例断言事件序号连续且客户端传值不出现在服务端序号里 |
| 阶段闸把"请求已了结"与"不是你的请求"混成一个错误码 | 拆成 `phase.request_resolved` / `phase.not_your_request` |
| 连接登记在重连后可能误清座位绑定 | 改为 `connection → seat` 反查表，只清理该连接自己的绑定 |
| 视图读取与命令提交并发时可能读到半更新状态 | 视图读取改为在同一把锁下取快照 |

### 矩阵逐行结论

| 行 | 运行时证据 | 结论 |
|---|---|---|
| 1 | `StepMachineHostTests.Row1_NightSlot_PushesRequestOnlyToActor`（真实宿主 + 真实 SignalR 客户端） | 通过 |
| 2 | `Row2_Reconnect_RedeliversIdenticalRequest` | 通过 |
| 3 | `Row3_NoResponse_RequestStaysPendingAndSlotDoesNotAdvance` | 通过 |
| 4 | `Row4_StorytellerSeesWhoIsStuckAndWhere`（含等待秒数） | 通过 |
| 5 | `Row5_StorytellerForceVoid_RecordedAndContinuesAfterQuota`（事件含原因与说明） | 通过 |
| 6 | `Row6_StorytellerProxyFill_MarkedAsProxy`（`ResponseSource.StorytellerProxy`） | 通过 |
| 7 | `Row7_HostRestart_KeepsPendingRequestAndTicket`（新增"未重新播种"反证：序号与 `PhaseStarted` 计数）+ **真实进程会话**（见下） | 通过 |
| 8 | `Row8_SeatDependencyLost_AutoVoidsPendingRequest` + `Row8b_CharacterChangeOnly_AutoVoidsPendingRequest`（内核另有死亡 / 角色变更 / 只观测一个维度 / 无关变化用例） | 通过 |
| 9 | `Row9_IllegalOption_RejectedAndStateUnchanged`（`legality.option_not_legal`） | 通过 |
| 10 | `Row10_NonActorResponse_RejectedByPhaseGate`（`phase.not_your_request`） | 通过 |
| 11 | `Row11_DuplicateResponse_AppliesOnceAndReturnsSameResult`（第二次 kind=Duplicate、同序号） | 通过 |
| 12 | `PacingIsolationTests.NightTimeline_IsIndependentOfActorAvailability`（两条受控时间线差 < 400ms；非当事玩家零消息） | 通过 |
| 13 | `Slots_ConsumeQuota_AndInstantResponseDoesNotShorten`（4 个槽位逐一 ≥ 配额） | 通过 |
| 14 | 同上（秒回后仍等满配额才推进） | 通过 |
| 15 | `Void_DoesNotShortenSlot`（作废后间隔 ≥ 配额） | 通过 |
| 16 | 协议层：`Row16_ReconnectBundle_IsPlayerScoped`（重连补齐按接收者投影 + JSON 反方向断言）+ 扩展后的 `PlayerProjectionLeakGateTests`；**真机玩家端 UI（2026-10-02 批次 E2）**：请求态与空态两个窗口都无进度语义（无槽位 / 进度 / 轮次 / 计数 / "谁在思考"），见下方批次结论与截图 06 / 08 | **通过** |
| 17 | `Row1`（旁观者对"操作请求 / 作废 / 说书人视图"三类推送均零消息）+ 节奏用例内断言 | 通过 |
| 18 | `Slots_ConsumeQuota…` 内"各槽位节奏差 < 500ms"；第二夜节奏对比由内核确定性迁移与统一配额保证 | 通过 |
| 19 | 黎明槽位在计划内、与其余槽位同配额（`Slots_ConsumeQuota…` 第 4 个槽位即黎明） | 通过 |
| 20 | `ClientSuppliedValues_CannotChangeServerRhythm`（契约无时间字段；事件序号连续且与客户端传值无关；节奏只由服务端时钟决定） | 通过 |
| 21 | `TakeoverAndRecoveryTests.BlockedSlot_ForceAdvance_ContinuesGame` + 内核 `ForceAdvance_SkipsQuotaAndVoidsPendingRequest` | 通过 |
| 22 | `TakeOver_PausesAutomaticAdvance_ReleaseResumes`（接管下 1.5s 不推进，交还后恢复） | 通过 |
| 23 | `Rebuild_RepairsStaleSnapshot_AndFailsExplicitlyOnCorruptEvents`（脏快照 → `SnapshotEquivalent=false` 并修复；未知事件 → `Failed` 写明原因） | 通过 |
| 24 | 同用例末段（重建失败后 `ForceAdvance` 仍 Accepted）+ `CorruptEventPayload_RoomStartsEmpty_AndCanBeRevivedExplicitly`（载荷损坏时宿主起得来、重建显式失败、可显式重开阶段续屋） | 通过 |

### 真实进程会话（非测试宿主）

Kestrel 真实进程 + 真实 TCP SignalR 客户端（完整输出在 `artifacts/live/`，gitignored、收尾随清场移除；摘要在下，修复后已重跑）：

1. 进程 1：1 号收到 `demo:night-1:demo-seat-1` 定向推送，其重连包 `events=2`（公开阶段 + 自己的请求）；
   **2 号的重连包 `events=1`（只有公开阶段，没有他人请求）**，1 秒观察窗内零消息（单播）；
   说书人视图显示卡点与 `waiting=2.317s`。
2. **强杀进程**后用同一 SQLite 库重启进程 2：1 号重连（序号 3 → 4）后**重新收到同一请求**（标识逐字一致）。
3. 1 号响应 → `Accepted`（序号 7）→ 槽位按配额推进；2 号全程零消息。

### 验收批次 E2（2026-10-02，真机多客户端会话）

装置：`tools/verify-storyteller-panel.mjs`（说书人 + 每席一个玩家，各自独立浏览器上下文；真宿主 + 真 Vite + 真 SQLite；
**57 项断言全过**，退出码 0；截图 `artifacts/web/01…10`、运行日志 `artifacts/web/batch-run.log`）。
本批次只补判此前悬着的两行，其余行的既有证据不变：

| 行 | 本批次证据 | 结论 |
|---|---|---|
| 16 | 2 号玩家在夜里收到定向请求时，界面只有：席位 + 大阶段 + 请求（上下文与合法选项）+ 自己的信息；请求态与空态两个窗口都无进度语义；钟表匠槽位（没有玩家选项）期间三席玩家均无请求 | **通过** |
| 17 | 请求窗口内 8 次采样：1 / 3 号玩家页面持续 `data-request-state=idle`、零信息、零诊断；纯旁观玩家 3 号全程没有收到任何信息 | **通过**（宿主侧原证据保留；这是真机 UI 侧的补充） |

**本批次发现（另立票，不阻塞本票矩阵）**：玩家端对"请求作废 / 代填"没有推送处理、阶段变化不刷新、
阶段枚举未本地化——见 `docs/backlog/todo/player-view-freshness.md`。

### 残余事项（不消失）

1. ~~**行 16** 需要"玩家端 UI 的一次真实会话"才有载体~~ **已闭环（2026-10-02 批次 E2）**：
   协议层仍由 `Row16…` 与 `PlayerProjectionLeakGateTests` 锁死；界面侧由批次 E2 在请求态 / 空态两个窗口判过（见下）。
2. 开新阶段 / 开夜入口：说书人面板已提供（`web/src/features/storyteller/OperationsControl.vue`：
   开夜 / 强推 / 接管 / 交还 / 重建），真机验证里已实际点到并通过；验收批次开始后即可判这一条。
3. 回退 / 撤销到任意序号（截断重放）按 D-0010 属后续能力；本票据只交付"按事件重建"。
4. EF Core 目前用 `EnsureCreated`；首个可部署版本前补迁移。
5. ~~演示步骤表是显式占位~~ **已解决**：`DemoStepPlan` 已从生产代码删除，宿主不再自动开阶段；
   集成测试改用测试夹具 `TestNightPlan`（`tests/` 内、非规则数据）。
   真实顺序表见 `src/OpenClockTower.Rules`（两套口径，R-0014）；建表属 `done/settlement-engine.md`。
6. **说书人上帝视角**（每步状态归因 + 最终计算结论）已单独立票
   `docs/backlog/todo/storyteller-step-insights.md`；本票据只落了数据面
   （`SeatStateChangedEvent` + `RecentSeatChanges` / `CurrentSlotActor` / `CurrentSlotContext`）。
   该票的**第二片**（状态账 + 效果归因链）已落地，事件模型由 16 种扩到 20 种，
   `SeatStateChangedEvent` 由 2 个观测维度扩到 5 个。
7. 事件载荷损坏时房间以**空状态**启动（记 Critical、保留序号连续性），续屋需宿主显式重开阶段：
   这是明示的数据损失，不是静默继续；"从损坏事件流里抢救部分状态"不在本票据范围。
8. 说书人视图的"能力是否生效 / 信息是否可能错误"已由结算引擎算出来并进视图
   （`LastResolution` / 账本 / 裁定点的上下文与合法选项，见 `done/settlement-engine.md`）；
   每步摘要（StepDigest）的面板呈现仍属
   `docs/backlog/todo/storyteller-step-insights.md`。
