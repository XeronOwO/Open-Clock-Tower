# 死亡保护裁定提示：只在该裁定时给出（说书人投影字段）

- Status: Review
- Priority: Medium
- Depends on: `docs/standard/rulings.md` R-0048（Decided）；`docs/backlog/done/traveller-and-exile.md`（E34 残余）

## 要解决的问题

E34 残余：说书人面板的「死亡保护裁定」入口由前端本地启发式给出（流放收票走完 + 当天未给该席裁定），
与 R-0048 的受理条件（收票收完 + 票面达线 + 目标存活 + 未裁定 + 保护来源要求裁定）不一致：

- 没有任何保护来源覆盖的目标（如屠夫）达线时也显示两个裁定键，点击必被服务端
  `day.protection_not_required` 拒绝；
- 未达线时同样先亮出按钮——「不提前提问」在界面上不成立；
- 判定不了（维度观测不齐）时界面无任何指示，说书人不知道要先补观测。

本票把入口判定移到服务端投影：`NeedsRuling` 才给按钮、`Indeterminate` 给「先补观测」提示、
其余一律不出入口；受理条件与裁判机器共用同一判定，并让玩家投影零变化。

## 实现

- 内核：新增 `DayProtectionEligibility`（受理条件评估，`DayProtectionMachine.Resolve` 复用，拒绝码与文案逐字不变）；
  新增公开 `DayProtectionPrompt`（席位 + 结论 + 说明）与 `DayProtectionPromptQuery.ForOpenExile`（纯查询）。
- 应用 / 契约：`StorytellerView.PendingProtection` → `StorytellerViewDto.PendingProtection`
  （`DayProtectionPromptDto`，说书人专属；`DayViewDto` 会进 `PlayerDayDto.publicView`，不能放那里）；
  `StorytellerViewBuilder` 用 `SessionSettlement.BuildContext` 现构上下文调用查询。
- 前端：`DayControl.vue` 改为服务端提示驱动（`NeedsRuling` → 按钮；`Indeterminate` → 补观测提示）；
  `format.ts` 增加归一化。
- 测试：内核（提示状态）、集成（真宿主正负窗口 + 玩家面零字段）、前端单测；主装置加正负断言。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 待裁定窗口 | 收票收完 + 达线 + 目标存活 + 未裁定 + 来源要求裁定 → 说书人视图给出 `NeedsRuling` 提示（席位 = 流放目标） | 内核 `DayProtectionTests.ProtectionPromptQuery_AppearsOnlyAfterReachedSweep` / `_AfterTheDecision_IsNull` + 集成 `DeviantHostTests.DeviantRuling_PromptAppearsAtTheAcceptanceWindow_ThenDisappears` + 批次 E36 主装置（怪咖达线后入口出现、裁定后消失） |
| 2 | 不提前提问 | 收票未走完 / 未达线 / 已裁定 / 无保护来源 / 目标已死 → 无提示、不出入口 | 内核 `_WithoutARulingToMake_IsNull` / `_WhenTargetIsAlreadyDead_IsNull` + 集成（未收完 / 未达线时投影为空）+ 批次 E36 主装置（收票期间无入口、屠夫达线无入口） |
| 3 | 判定不了 | 维度观测不齐 → `Indeterminate` 提示（带「先补观测」说明）、不给裁定按钮 | 内核 `_Indeterminate_IsPromptedWithObservationNote` + 前端 `format.spec.ts` 归一化用例；真机不可达（装置不制造「怪咖维度未观测」状态，如实记录） |
| 4 | 信息隔离 | 玩家投影零新增字段 | 门禁 `PlayerProjectionLeakGateTests`（契约登记说书人专属）+ 集成玩家 DTO 序列化断言 |

## 决定与依据

- R-0048 第 2 条：达线时裁定、平台不提前提问、不预缓存；结论四态不混成一个 bool（D-0015：不猜）。
- 提示放**说书人视图根**（与 `PitHagNight` / `FangGuInfection` / `BarberNight` 同族）：
  `DayViewDto` 是玩家可见的白天公开事实。
- 受理条件单点：机器与投影共用 `DayProtectionEligibility`，既有 `DayProtectionTests` 守备面保证行为与拒绝码不变。

## 验收判出（批次 E36）

（待跑；结论与逐行证据在批次完成后回填。）
