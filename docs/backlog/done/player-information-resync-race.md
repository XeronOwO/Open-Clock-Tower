# 补齐期间到达的信息推送会被快照覆盖丢弃

- Status: Done
- Priority: Low
- Depends on: 玩家端视图新鲜度（`done/player-view-freshness.md`）；重连语义（D-0010）

## 要解决的问题

玩家端的信息结果由两路写入 `PlayerPanel.informationResults`：

- 在线推送 `onInformation` → **追加**（`[...informationResults.value, information]`）；
- `join()` / `resync()` 在 `JoinSeat` 返回后用**快照整体覆盖**。

两路在"补齐进行中"重叠时会丢数据：服务端在锁内构建快照（序号 N）并返回，客户端覆盖之前，
若该席位在 N+1 又产生 `InformationResultIssuedEvent` 并推送到同一连接，推送先被追加、
随后被不含它的快照覆盖。后果是这条信息在界面上缺失，直到下一次补齐 / 重连才由快照补回
（可恢复，但属于静默丢失）。

来源：票据 `in-progress/player-reconnect-gap-semantics.md` 的对抗性复核 F2（2026-10-02）。

根因不止信息一处：**推送此前不带事件序号**，客户端无法判断推送与快照的先后；凡是
"快照整体覆盖 + 推送直接写"的字段（信息 / 阶段 / 白天 / 请求三态）都有同型竞态，
说书人整视图的 Join / 刷新响应同样会覆盖窗口内到达的较新推送。

## 要做的事

1. 推送携带**背书事件序号**（白天投影与说书人视图这类"读时状态"取读取时的序号）；
2. 客户端收敛为**单一写入者**：`PlayerGateway` 持有 `PlayerViewMerge`，快照与推送都只进合并态，
   按序号合并后整份交给界面——消灭"追加后又覆盖"的双写；
3. 同族核对：`onRequest` / `onRequestVoided` / `onRequestAnswered` / `onPhaseStarted` / `onDayChanged`
   与说书人 `ReceiveStorytellerViewChanged` 的同型覆盖竞态一并对齐；
4. 补并发窗口的运行时验证（先红后绿 + 真机装置）。

## 验收矩阵

| # | 场景 | 期望 | 依据 |
|---|---|---|---|
| 1 | 补齐往返中到一条本席信息推送 | 推送不丢，补齐完成后两条都在且不重复 | D-0010 / D-0012 §4.3 |
| 2 | 无关席位 | 行为不变（零消息） | D-0013 §5 |
| 3 | 同族：阶段 / 白天 / 请求三态落在补齐窗口 | 不被迟到快照拉回；已了结的请求不复活 | 架构 §5 |
| 4 | 乱序 / 重复推送（旧序号后到、同一条重复到达） | 按序号合并：不回退、不重复、仍有序 | 架构 §5 |
| 5 | 说书人：Join / 刷新响应晚于推送 | 保持较新序号视图，不被旧响应拉回 | 架构 §5 |

## 实现口径（2026-10-02）

- **契约**：`InformationResultDto` / `PhaseStartedDto` / `PlayerDayDto` / `OperationRequestDto` /
  `OperationRequestVoidedDto` / `OperationRequestAnsweredDto` 增加 `Sequence`（必填）；
  web 契约镜像同步（`ContractMirrorGateTests` 逐字段对账）。
- **服务端**：`GameNotification.Sequence` = 背书事件序号（`GameNotificationBuilder.Build` 改收
  `StoredEventDraft`）；`NotificationDispatcher` 定向推送按通知序号、白天按读取到的视图序号、
  说书人视图按视图自身序号；`JoinSeat` 重投请求用快照序号。
- **客户端**：新增 `web/src/services/playerViewMerge.ts`（纯逻辑）——信息按序号并集去重，
  阶段 / 白天 / 请求按各自字段序号取新，水位取已见最大值；`PlayerGateway` 的推送与快照都只进它，
  `onView` 是唯一视图出口；`PlayerPanel` 只在 `onView` 里写呈现态。
- **坏数据边界**：`applyBundle` 对事件越界 / 倒退 / 重复仍显式失败、不采纳；
  **快照序号低于本地已知不再算坏包**（推送先到、响应后到），由合并态按字段序号取舍。
- **说书人**：`storytellerGateway` 加序号闸（`newerView`），Join / 刷新响应不更旧才采纳。

## 决定与依据

- 快照 + 补齐、禁止本地推断：D-0010 / 架构 §5；同步的正路是补全初始条件，不是加时间窗口。
- 信息只在服务端下发方向校验：D-0012 §4.3。
- 推送序号沿用**全局事件序号**口径（`ReconnectBundleDto.Sequence` / `PlayerEventDto.Sequence`
  已按同一口径下发给玩家），不新增可渲染的进度面；D-0013 §5 的边界按"不显示活动指示"解释。
- 取证来源：`in-progress/player-reconnect-gap-semantics.md` 对抗性复核 F2；
  `web/src/features/player/PlayerPanel.vue` 的双写与 `web/src/services/playerGateway.ts` 的推送处理。

## 验收证据与结论（2026-10-02，批次 E10）

**先红后绿（并发窗口的运行时验证）**：

- 先红：`npx vitest run src/services/playerViewMerge.spec.ts` → **12 failed / 0 passed**
  （先把修复前的双写语义抽成模块，让票据行 1 的失败可见：补齐窗口里的推送被快照覆盖、
  重复合并、迟到快照把阶段 / 白天 / 请求拉回、水位回退）。
- 后绿：改按序号合并后同一套用例 → **12 passed**；补齐同族与接线后 `npm run gate` 全绿
  （typecheck / lint / **85 条 vitest** / build）。

**网关接线（运行时，不是纯函数）**：`web/src/services/playerGateway.wiring.spec.ts`（假连接，
确定性地扣住 JoinSeat 响应）5 例：行 1 交错、事件水位不被推送推高、包不可信仍采纳新凭据、
服务端序号回退重建、作废说明先于视图；`storytellerGateway.spec.ts` 纯函数 3 例 + 接线 2 例。

**门禁（最终冻结树）**：`dotnet build` 0 警告 0 错误；`dotnet test` **317 通过 / 0 失败**
（Kernel 157 / Rules 76 / Integration 62 / NormativeGates 22）；`dotnet format --verify-no-changes`
0 改动；`npm run gate` 全绿。

**真机批次 E10**（两装置同批，日志 `artifacts/web/batch-run.log` / `zero-trust-run.log`）：

- 主装置 `tools/verify-storyteller-panel.mjs`：**139 项断言全过 + 34 张截图**（退出码 0）。
  新增并发场景用 Playwright `routeWebSocket` 扣住 1 号的补齐响应帧：说书人完成钟表匠裁定 →
  信息推送先到 → 放行响应。四条断言全过：① 响应确被扣住（快照已生成、客户端尚未应用）；
  ② 补齐返回前窗口内推送已按推送呈现（信息计数 = 1）；③ 放行后推送不丢、不重复
  （信息计数 = 1 且内容在列）；④ 补齐成功且没有"重连补齐"坏包诊断。
  截图 `05b-resync-window-info-kept.png` 已目视复核：信息列表恰好一条钟表匠信息。
- 零信任装置 `tools/verify-zero-trust.mjs`：**42 项断言全过**（退出码 0）。新增
  "玩家推送携带正的事件序号，且信息推送序号互不相同"：本次收包 7 条 / 信息 2 条 / 坏序号 0。
- 反向取证复跑：无关玩家在请求窗口内零请求、诊断按**逐席显式期望值**判（1 号只允许自己点
  补齐的成功提示、3 号必须为空）；"行 8 / 9：信息只到 2 号"通过。

**验收矩阵逐行结论**：

| # | 场景 | 结论 | 本批次证据 |
|---|---|---|---|
| 1 | 补齐往返中到一条本席信息推送 | **通过** | 主装置并发票行 1 四条 + 截图 05b；单测 `playerViewMerge.spec.ts` 行 1 用例（先红后绿）与 `playerGateway.wiring.spec.ts` 行 1 接线用例 |
| 2 | 无关席位 | **通过** | 零信任行 8 / 9（信息只到 2 号，1 / 3 号零新增）；主装置无关玩家零请求 + 诊断逐席期望值 |
| 3 | 同族：阶段 / 白天 / 请求三态 | **通过** | `playerViewMerge.spec.ts` 同族 6 例（迟到快照不拉回、旧序号作废 / 响应不清理当前请求、同序号幂等）+ 接线用例 |
| 4 | 乱序 / 重复推送 | **通过** | `playerViewMerge.spec.ts`（乱序按序号落位、重复幂等、迟到快照只补不覆盖） |
| 5 | 说书人：Join / 刷新响应晚于推送 | **通过** | `storytellerGateway.spec.ts` 纯函数 3 例 + **接线 2 例**（推送先到、旧响应不采纳且 `refresh()` 返回当前视图） |

**对抗性复核（前台只读子代理，冻结树，2026-10-02）与本轮处置**：

- 中-1（读时推送的序号会把事件窗口水位推高、把缺口事件窗口整段截断）→ 改成**双水位**：
  `eventAt` 只由快照推进，推送只参与字段取舍；补接线用例锁死；架构 §5 同步。
- 中-2（JoinSeat 失败会留下服务端已吊销的旧凭据）→ **凭据先采纳**移到包校验之前，
  补"包不可信仍采纳新凭据"用例。
- 中-3（服务端序号回退会让视图永久卡死、复用的信息序号还会把新事实当重复丢掉）→
  加快照序号低于事件水位的**重建分支**（清空合并态、按快照为新基线、出诊断），
  补 `reset()` 单测与接线用例。
- 中-4（行 5 只有纯函数单测、网关接线没人盯）→ 两个网关加**可注入连接工厂 + 假连接接线用例**。
- 低-1（只有派生事件的提交让 `StorytellerViewChanged` 序号为 0）→ `Build` 改收全部 drafts
  （派生事件不产通知，但参与末条序号）。
- 低-2（诊断判据从"零诊断"放宽成"相对基线不变"过宽）→ 改为**逐席显式期望值**。
- 低-3（零信任只证明字段存在）→ 加"正序号 + 信息推送序号互不相同"。
- 低-4（扣帧装置无法区分"合并"与"根本不采纳快照"）→ 由新增网关接线用例补上；
  扣帧装置保留为端到端取证。

**边界与残余**：

- 白天投影 / 说书人视图这类"读时状态"的序号取自读取时的全局 head：对玩家是"新增分辨率"
  而非新能力（随时点补齐本就能采到 head），前端不渲染、不显示；按 D-0013 §5"不显示活动指示"
  的口径解释，登记为已知边界。
- 扣帧场景依赖 SignalR invoke 未超时（默认 30s）：极慢机器上扣帧阶段可能变成假红（不是假绿），
  按可重复性复跑判定。
- 服务端序号回退的**真实故障注入**未进装置（仓库内没有删除事件的路径，需要外部数据损失）；
  本轮以接线用例（假快照回退）与 `reset()` 单测为运行时证据。
