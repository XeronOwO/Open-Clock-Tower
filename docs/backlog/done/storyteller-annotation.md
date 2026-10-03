# 说书人注记：魔典上的自由文本提示标记

- Status: Done（批次 E22 矩阵 8 行全部通过，见「E22 验收判定」）
- Priority: Low
- Depends on: 魔典主视图（`done/grimoire-view.md`）；状态账边界 D-0015；**注记语义 D-0019（2026-10-03 定）**

## 要解决的问题

官方魔典允许说书人在席位旁挂**自由文本**提示标记（示例：「18 不共边」）。2026-10-02 需求方确认：
魔典首版只做**由事实派生**的提示标记（疯狂要求、生效中的效果链接），自由文本注记**另立本票**——
它不是游戏状态事实，混进状态账会违反 D-0015（账本只记事实与归因）。

## 已定（2026-10-03，开工前回答；选项与代价见 D-0019）

| # | 待定问题 | 结论 |
|---|---|---|
| 1 | 归属 | 属于**这一局**（房间级）；只说书人可写、可看，玩家**零下发**（D-0012 §4.3）。要让玩家看见注记属「信息结果」能力，不在本票 |
| 2 | 持久化 | 进**事件流**：增 / 改 / 删各一条说书人专属事件，折叠成**独立注记账**（不与状态账合流）——重启 / 重连 / 换设备都还在；将来撤销 = 截断重放时注记一并回退 |
| 3 | 边界 | 不参与任何规则判定、不进 `GameState`（D-0015）、不进玩家投影；增删改都留痕（事件流即审计），删除写删除事件而不是抹掉历史 |
| 4 | 呈现 | 席位锚定 token；有界化 = 服务端归一化 + 长度上限（120 字符）+ 每席条数上限（5 条），牌面截断显示、全文进 `title` 与操作台；补齐走说书人视图的序号流（D-0010） |

## 验收矩阵

| # | 场景 | 期望 | 依据 |
|---|---|---|---|
| 1 | 说书人给某席加一条注记 | 该席牌面出现自由文本 token；状态账（席位 / 效果）与步骤机**不受影响**（反方向断言） | D-0015 / D-0019 |
| 2 | 注记含超长 / 换行 / 控制字符 | 服务端归一化（换行折成空格）并按上限拒绝；牌面显示有界（截断 + 全文 `title`），不撑爆牌面；坏载荷只降级该 token | 架构 §4.4 / D-0019 |
| 3 | 编辑 / 删除一条注记 | 编辑原地更新、删除后 token 消失；两者各留一条事件（审计可回看） | D-0019 |
| 4 | 无关玩家视图 | 零下发、零活动（反方向断言）；玩家侧代码不出现注记字段 | D-0012 §4.3 / D-0013 §5 |
| 5 | 重连 / 重启 | 按事件流恢复：重新加入 / 服务端重启后注记仍在，序号连续 | D-0010 / D-0019 |
| 6 | 身份不符（玩家凭据 / 宿主之外） | 身份闸拒绝（`identity.storyteller_only`），审计留痕 | D-0012 §4.2 |
| 7 | 越界输入（幽灵席位 / 不存在的注记 id / 超过每席上限） | 合法性闸拒绝，不改状态 | D-0012 §4.2 |
| 8 | 重复投递同一命令（同幂等键） | 只生效一次，第二次返回首次回执 | D-0010 |

## 决定与依据

- 2026-10-02 需求方决定：首版不做，另立本票（魔典先交付派生标记）；
- **2026-10-03 需求方选择「本局级 · 进事件流」**：语义、替代方案与代价见 `docs/decisions/active.md` D-0019；
- D-0015：状态账只记事实与归因，自由文本不能混入——注记走**独立注记账**（同一事件流、不同派生视图）；
- D-0012 §4.3：玩家端零下发、零活动；D-0010：注记随说书人视图的序号流补齐，重连 / 重启按事件流恢复；
- 术语：提示标记 `reminder-token`（`docs/standard/terminology.md`）。

## E22 验收判定（2026-10-03，冻结版本 `main` @ `9f1796d`）

批次记录见 `docs/acceptance/batches.md`。本批装置：主装置 `tools/verify-storyteller-panel.mjs`
取证档（`--quota 2 --screenshots-all --build`）**194 项全部通过 / 0 跳过**（39 张截图本次写入、
其中新增 4 张逐张复核）；真宿主用例 `AnnotationHostTests` 6 条 + 内核 `SeatAnnotationTests` 12 条；
门禁 `dotnet build` 0 警告 0 错误、`dotnet test` **637 通过 / 0 失败**、`dotnet format` 退出码 0、
`npm run gate` 通过（99 前端单测）。逐行结论：

| # | 结论 | 证据（本次运行） |
|---|---|---|
| 1 | 通过 | 装置 `annotation` 段：新增被受理（序号 11）→ 5 号牌面出现 token（截断显示、全文进 `title`），截图 `39-grimoire-annotation`；同一段断言状态账面板里没有这条自由文本（D-0015 反向）；内核 `SeatAnnotationTests.AnnotationEvents_DoNotTouchTheStateLedgerOrTheStepMachine`（注记事件既不进 `GameState`、也不把步骤机从"未开始"变"已开始"）；真宿主 `AnnotationHostTests.AddUpdateRemove_…` |
| 2 | 通过 | 真宿主 `MultilineText_IsCollapsedBeforeStoring`（`甲\r\n\r\n乙\t丙` → `甲 乙 丙`）与 `InvalidTextSeatAndCapacity_AreRejected`（空 / 121 字符 / 控制字符分别给 `legality.annotation_empty` / `_too_long` / `_control`）；内核 `TryNormalize_*`；前端 `format.spec` 的 `annotationTokenTextOf`（控制字符折空格、超长截断 + 省略号）——牌面只显示有界文本、坏载荷只降级该 token |
| 3 | 通过 | 装置：改（序号 12）后**同一 `data-note-id`** 原地更新（截图 `40-grimoire-annotation-edited`）、删（序号 13）后 token 消失；两者各留一条事件（事件序号连续），已删除的标识再改被拒 `legality.annotation_unknown`；内核 `Fold_AddUpdateRemove_…` / `RemovedId_IsNotReissued` |
| 4 | 通过 | 装置：5 席玩家页零文案 / 零锚点 + 5 席活动快照 4 次采样保持不变（零活动），截图 `41-annotation-player-clean`；真宿主 `AnnotationHostTests`（重连包 JSON 无 `Annotation` / 注记文本；玩家连接 300ms 内零推送）；门禁 `PlayerProjectionLeakGateTests` 把 `SeatAnnotationDto` 登记进说书人专属契约与类型清单（契约覆盖自检一起变绿） |
| 5 | 通过 | 装置 `rebuild` / `reconnect` 段：真宿主停机 → 损坏事件载荷 → 重启 → 修复重建后注记仍在牌面（截图 `42-grimoire-annotation-after-restart`）；真宿主 `Notes_SurviveHostRestart_AndKeepTheIssuedWatermark`（重启后 id=1 仍在，新注记拿到 id=2——签发水位随事件流恢复，不复用旧标识） |
| 6 | 通过 | 真宿主 `PlayerCredential_CannotWriteAnnotations`：玩家凭据发注记命令 → `Rejected` + `identity.storyteller_only`（身份闸 + 审计，不触达 Kernel） |
| 7 | 通过 | 真宿主 `InvalidTextSeatAndCapacity_AreRejected`：幽灵席位 `legality.seat_unknown`；第 6 条 `legality.annotation_limit`；越界输入不改状态（被拒后视图仍是 5 条）；改 / 删不存在的 id 按行 3 的 `legality.annotation_unknown` |
| 8 | 通过 | 真宿主 `SameIdempotencyKey_IsAppliedOnce`：同一幂等键第二次返回 `Duplicate`、视图仍只有 1 条 |

**残余**：无阻塞项。E17 残余③（面板常驻显示等）与本票无关；「给玩家看某条注记」若将来要做，
走既有「信息结果」能力另立票（D-0019 推论）。
