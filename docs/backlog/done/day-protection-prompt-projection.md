# 死亡保护裁定提示：只在该裁定时给出（说书人投影字段）

- Status: Done
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

## 验收判出（批次 E36 · 2026-10-04）

冻结版本 `main` @ `86dd663`（跑批期间工作树干净）；主装置
`node tools/verify-storyteller-panel.mjs --quota 2 --screenshots-all --build`：**286 项通过 / 0 失败 /
0 跳过、退出码 0**（172.1s，54 张截图本次运行写入）。矩阵 4 行判出：

- 行 1 **通过**：断言「达线后出现死亡保护裁定入口（待裁定）」（`exile-sweep-done` 截图可见
  「受保护（怪咖有趣）/ 不受保护」两键 + 文案「达线且待裁定，R-0048」）；裁定后断言入口消失
  （`exile-protected` 截图）。
- 行 2 **通过**：收票期间断言入口计数 = 0（`exile-dial` 截图同证：收票 2/7 席时无两键）；
  屠夫（无保护来源）达线断言入口计数 = 0、直接计票成立（`exile-exiled` 截图：流放成立、目标死亡）。
- 行 3 **通过（真机不可达）**：内核 `ProtectionPromptQuery_Indeterminate_IsPromptedWithObservationNote` +
  前端归一化用例；装置不制造「怪咖维度未观测」状态，未做组件级用例——如实记录。
- 行 4 **通过**：门禁 `PlayerProjectionLeakGateTests` + 集成玩家 DTO 序列化零命中断言。

诚实记录（范围）：其余十二个装置未重跑——新增字段为说书人视图专属（玩家 DTO 未动，契约由门禁登记）；
装置 stderr 的重启窗口 `ECONNRESET` / `ECONNREFUSED` 噪声来自 `rebuild` 段故意重启宿主
（装置自记「重启窗口噪音=63（非预期 0）」并判过），与本票无关。批次记录见 `docs/acceptance/batches.md` 批次 E36。
