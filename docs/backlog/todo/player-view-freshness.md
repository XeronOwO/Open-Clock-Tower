# 玩家端视图新鲜度：请求了结与阶段变化的推送 / 呈现

- Status: Todo
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
| 1 | 说书人强制作废挂着请求的玩家请求 | 玩家**不刷新、不重新加入**就看到请求消失，并能看到作废原因 | 待真机批次 |
| 2 | 说书人代填 | 同上，玩家侧请求消失（可注明"由说书人代填"） | 待真机批次 |
| 3 | 开夜 / 阶段推进 | 玩家页头的阶段随服务端更新，不需要手动"补齐" | 待真机批次 |
| 4 | 无关玩家 | 以上推送不得让无关玩家的设备出现任何活动指示（继续满足 D-0013 §5） | 待真机批次（复用批次装置的窗口采样断言） |
| 5 | 阶段显示 | 说书人 / 玩家两端把 `NotStarted` / `FirstNight` / `OtherNight` / `Day` 显示成中文；未知值仍原样回显 | 前端单测 + 截图 |

## 决定与依据

- 同步口径：重连 = 快照 + 缺口事件，禁止本地推断（D-0010 / 架构 §5）；"玩家可见"的字段要么有推送，要么明确不显示。
- 信息隔离：推送仍必须是定向单播，无关玩家零消息（D-0013 §5）。
- 为什么现在才立票：步骤机票只覆盖"服务端把请求发出去"；玩家端的消费面在批次 E2 才第一次被真机检验。
