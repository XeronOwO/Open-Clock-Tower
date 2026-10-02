# 补齐期间到达的信息推送会被快照覆盖丢弃

- Status: Todo
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

## 要做的事

1. 让"快照信息"与"实时推送"分轨（或按序号合并），消灭"追加后又覆盖"的双写；
2. 补并发窗口的运行时验证（先红后绿）；
3. 同族核对：`onRequest` / `onRequestVoided` / `onPhaseStarted` 是否有同型覆盖竞态。

## 验收矩阵

| # | 场景 | 期望 | 依据 |
|---|---|---|---|
| 1 | 补齐往返中到一条本席信息推送 | 推送不丢，补齐完成后两条都在且不重复 | D-0010 / D-0012 §4.3 |
| 2 | 无关席位 | 行为不变（零消息） | D-0013 §5 |

## 决定与依据

- 快照 + 补齐、禁止本地推断：D-0010 / 架构 §5
- 信息只在服务端下发方向校验：D-0012 §4.3
- 取证来源：`in-progress/player-reconnect-gap-semantics.md` 对抗性复核 F2；
  `web/src/features/player/PlayerPanel.vue` 的 `onInformation` 追加与 `join` / `resync` 覆盖
