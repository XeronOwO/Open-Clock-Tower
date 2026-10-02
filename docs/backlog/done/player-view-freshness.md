# 玩家端视图新鲜度：请求了结与阶段变化的推送 / 呈现

- Status: Done
- Priority: Medium
- Depends on: 自动步骤机与操作请求（已验收，`done/operation-request-step-machine.md`）；前端形态与边界 D-0018

## 要解决的问题

2026-10-02 第一次真机验收批次（E2）跑完三客户端会话后，暴露出玩家端**不是所有玩家可见的变化都有推送 / 处理**：

1. **请求被作废后玩家界面不更新**：服务端在作废时推 `ReceiveOperationRequestVoided`
   （`src/OpenClockTower.Server/NotificationDispatcher.cs`），但玩家网关只注册了
   `ReceiveOperationRequest` / `ReceiveInformationResult`（`web/src/services/playerGateway.ts`）——
   玩家的"当前请求"面板会一直停在一条已经作废的请求上，直到手动"补齐"或重连。
2. **说书人代填后同样不更新**：`ProxyFill` 只回执给说书人，没有面向玩家的通知；玩家的请求面板同样停在旧请求。
3. **阶段变化不刷新**：`GameProjection.ForSeat` 的 `Phase` 只在 `JoinSeat` 时取一次；服务端没有"玩家视图变化"推送
   （`IGameClient` 只有请求 / 作废 / 信息三类）。E2 批次里，夜里 2 号玩家头部仍是 `NotStarted`
   （`artifacts/web/06-player-dreamer-request.png`、`08-player-dreamer-info.png`，本轮运行），
   说书人侧同一时刻显示 `FirstNight`。
4. **阶段枚举未本地化**：`FirstNight` / `NotStarted` 在说书人与玩家两端都原样显示英文
   （`web/src/display/labels.ts` 的值表没有这两个值；"未知取值原样回显"是对的，但这两个不是未知值）。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 说书人强制作废挂着请求的玩家请求 | 玩家**不刷新、不重新加入**就看到请求消失，并能看到作废原因 | 真机批次 E4：2 号请求区回空态 + 「请求已作废：说书人强制作废（批次取证：强制作废）」（截图 16）；自动作废同一通道 |
| 2 | 说书人代填 | 同上，玩家侧请求消失（可注明"由说书人代填"） | 真机批次 E4：3 号请求区回空态 + 「请求已了结：由说书人代填」（截图 15） |
| 3 | 开夜 / 阶段推进 | 玩家页头的阶段随服务端更新，不需要手动"补齐" | 真机批次 E4：未开夜「未开始」→ 首夜「首夜」→ 第二夜「夜晚」，全程未点补齐（截图 07 / 14） |
| 4 | 无关玩家 | 以上推送不得让无关玩家的设备出现任何活动指示（继续满足 D-0013 §5） | 真机批次 E4：代填窗口（1 号）与强制作废窗口（1 / 3 号）各 8 次采样的活动快照相对基线保持不变；宿主用例另证无关席位收不到定向通知 |
| 5 | 阶段显示 | 说书人 / 玩家两端把 `NotStarted` / `FirstNight` / `OtherNight` / `Day` 显示成中文；未知值仍原样回显 | 前端单测（`labels.spec.ts`）+ 真机截图 07 / 14；`Resolving` 一并补齐，未知值原样回显 |

## 收口落点（2026-10-02，批次 E4）

- **契约**：`OperationRequestAnsweredDto` / `PhaseStartedDto`（`src/OpenClockTower.Contracts`）+ TS 镜像
  `web/src/contracts/game.ts`；`IGameClient` 增 `ReceiveOperationRequestAnswered` / `ReceivePhaseStarted`。
- **推送**：`GameNotificationBuilder` 把 `OperationRequestAnsweredEvent` / `PhaseStartedEvent` 翻成通知；
  `NotificationDispatcher` 前者定向单播、后者广播给全部已绑定席位（请求 / 信息 / 作废原有通道不变，
  作废与响应补上"无在线连接"日志，重连时按事件补齐）。
- **消费**：`playerGateway` 注册三个处理器并做防御性归一化；`PlayerPanel` 在请求了结后回空态并给出
  「作废原因 / 由说书人代填」，`onPhaseStarted` 直接刷新页头（不做任何本地推断）；`display/labels.ts`
  补 `NotStarted` / `FirstNight` / `OtherNight` / `Resolving`，说书人与玩家两端共用同一张表。
- **一致性口径**：**在线推送覆盖面 = 重连白名单 `PlayerEventKind`**（五类事件各有对应通知），由
  `PlayerNotificationBuilderTests` 锁成测试；两个新契约纳入 `PlayerProjectionLeakGateTests` 扫描。

## 验收批次 E4 逐行结论（2026-10-02 真机多客户端会话）

装置：`tools/verify-storyteller-panel.mjs`（说书人 + 三席玩家，独立浏览器上下文；**91 项断言全过**，
退出码 0；16 张截图 `artifacts/web/01…16` 与运行日志 `artifacts/web/batch-run.log`；已目视复核
07 / 12 / 13 / 14 / 15 / 16）。本批次新增：第二夜"说书人代填 + 强制作废 + 阶段推送"，第三夜承接
行 5 / 6 的依赖证据（三席都活着，槽位与依赖按正常路径生效）。

| # | 结论 | 本批次证据 |
|---|---|---|
| 1 | **通过** | 说书人强制作废 2 号挂起的筑梦师请求：2 号未点补齐、未刷新，请求区回空态并显示「请求已作废：说书人强制作废（批次取证：强制作废）」（截图 16）；自动作废走同一通道，同样回空态并给出「座位依赖不再满足」 |
| 2 | **通过** | 说书人代填 3 号的诺-达鲺击杀请求：3 号请求区回空态并显示「请求已了结：由说书人代填」（截图 15） |
| 3 | **通过** | 首夜推送让三席页头由「未开始」变「首夜」、第二夜变「夜晚」，全程未点补齐（截图 07 / 14） |
| 4 | **通过** | 代填窗口（1 号）与强制作废窗口（1 / 3 号）各 8 次采样的活动快照相对基线保持不变；宿主侧另有两条用例断言无关席位收不到定向通知 |
| 5 | **通过** | 五个阶段值在两端均有中文（`labels.spec.ts` + 截图 07 / 14），未知值仍原样回显 |

**全行通过 → 本票移入 `done/`。**

## 残余事项（本票未做，矩阵未要求）

- 重连补齐的缺口事件仍只用于**证明序号连续**：玩家若在掉线期间被作废 / 被代填，重连后看到的是快照的
  正确状态（空态），但**看不到那次作废的说明**——说明只随在线推送到达。要连理由一起补，需要让前端
  消费缺口事件（`PlayerEventDto` 已带 `voidReason` / `voidNote`）；本票按"快照 + 缺口事件证明连续"的
  既有口径未做，记为后续可选增强。

## 决定与依据

- 同步口径：重连 = 快照 + 缺口事件，禁止本地推断（D-0010 / 架构 §5）；"玩家可见"的字段要么有推送，要么明确不显示。
- 信息隔离：推送仍必须是定向单播，无关玩家零消息（D-0013 §5）。
- 为什么现在才立票：步骤机票只覆盖"服务端把请求发出去"；玩家端的消费面在批次 E2 才第一次被真机检验。
