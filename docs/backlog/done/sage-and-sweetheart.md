# 死亡触发族：贤者 / 心上人

- Status: Done（E24 判定通过；判定记录见文末「E24 验收判定」）
- Priority: High
- Depends on: 触发管线（`IEventTrigger` 已就位：理发师 / 呆瓜 / 女巫 / 洗脑师）；
  死亡事件的来源归因（`SeatStateChangedEvent.CausedBy` / `EffectId`）；持续效果账与维度对账；
  夜晚顺序表（两套口径均已含 `sweetheart` / `sage` 条目，本票由 `Action` 改为 `Trigger`）

## 要解决的问题

S&V 剩余 7 个未实现角色里的两名**死亡触发**角色，入场即被显式阻塞：

1. 夜晚顺序表的「其他夜晚」两套口径里都有 `sweetheart` / `sage` 条目（`Action`），
   但没有对应契约——角色在场时开夜被 `plan.contract_missing` 显式拒绝；
2. 死亡触发族目前只有两种形态：理发师「死亡记事实 → 当夜触发格交互」（R-0033）
   与呆瓜「公告时开触发型请求」（R-0027）。**缺**「死亡时立即产生信息 / 效果」的路径；
3. 贤者要读「杀死他的那个恶魔」——需要把死亡归因（CausedBy + 死亡时刻的角色）翻译成
   「恶魔击杀」判据，并与处决 / 女巫诅咒 / 麻脸巫婆追加死亡区分开；
4. 心上人的醉酒在**死亡时**就落成，且来源（心上人）已经死亡——现有持续效果的生命周期
   （来源死亡即终止、来源死亡即不生效）都不适用，需要一条「既成事实」口径。

## 机制清点

| 角色 | 触发时点 | 读取的事实 | 平台推演口径 | 来源 |
|---|---|---|---|---|
| 贤者 `sage` | 被恶魔杀死的那一刻（信息在当晚展示） | 死亡时刻击杀者的角色（`CausedBy` 是否为恶魔角色） | 死亡批记「恶魔击杀」事实；当夜 `sage` 触发格开说书人裁定点（选择展示的两名玩家）；平台推演「真恶魔 = 击杀者」并只作注记；信息只到本人 | 百科《贤者》· 2026-10-01 抓取 · 角色能力 / 角色简介 / 运作方式 / 范例；《死亡触发能力》· 能力简介 |
| 心上人 `sweetheart` | 死亡那一刻（立即） | 死亡时刻该席位是否心上人；死亡时醉酒 / 中毒 | 死亡批内开**触发型说书人裁定点**（选择任一玩家）；裁定后该玩家获得持续醉酒（`SourceStateIndependent`，来源换角时终止）；能力未生效则显式跳过 | 百科《心上人》· 2026-10-01 抓取 · 角色能力 / 角色简介 / 运作方式 / 提示标记；《死亡触发能力》· 能力简介 |

两者共同点：都必须作为本角色死亡才触发（死后才变成该角色不算）；死亡时醉酒 / 中毒 →
能力不生效，按既有生效判定口径处理（贤者照发信息但标「可能为假」；心上人不开裁定、显式跳过）。

## 验收矩阵

| # | 场景 | 期望 | 依据 |
|---|---|---|---|
| 1 | 贤者被恶魔夜杀 | 当夜 `sage` 触发格开裁定点（注记「杀死者 = M 号」）→ 说书人展示两名玩家 → 信息只到贤者本人 | 《贤者》角色能力 / 运作方式 |
| 2 | 贤者死于处决 | 不触发（无裁定点、无信息事件） | 《贤者》角色简介 2 |
| 3 | 贤者死亡不是恶魔造成（女巫诅咒 / 处罚处决 / 麻脸巫婆追加死亡） | 不触发；留可归因的跳过记录 | 《贤者》范例 3（麻脸巫婆）；女巫 / 处罚处决的死亡归因 |
| 4 | 贤者能力未生效（死亡时醉酒 / 中毒） | 照常开裁定；信息照发、标 `MayBeFalse` + 注记，内容由说书人裁量 | 《贤者》范例 2；《重要细节》三-3 |
| 5 | 涡流在场 | 信息标 `MayBeFalse` + 「必须为假」注记；裁定内容仍由说书人给 | R-0028 |
| 6 | 贤者展示范围 | 说书人可选择任意两名其他玩家（含已死亡者）；选项编码稳定、可重放 | 《贤者》运作方式 |
| 7 | 贤者：过时不候 | 贤者格已走过 / 当夜无贤者格 → 显式跳过（可归因），不顺延 | R-0033 同族 |
| 8 | 心上人夜杀 | 死亡批内开触发型裁定点（候选 = 全体席位）→ 裁定后目标持续醉酒（效果账 + 维度对账） | 《心上人》角色能力 / 提示标记 |
| 9 | 心上人白天被处决 | 同样立即触发（不等夜晚；与夜晚死亡行为一致） | 《死亡触发能力》能力简介 |
| 10 | 心上人死亡时能力未生效（醉酒 / 中毒） | 不开裁定；写显式跳过记录 | 《重要细节》三-3（能力生效通例） |
| 11 | 醉酒持续 | 跨黎明、多阶段不自动清除；目标信息 / 能力按醉酒结算 | 《心上人》角色简介 1 |
| 12 | 心上人离场（换角） | 醉酒立即解除（来源失去能力 → 效果终止 → 维度解除） | 提示标记「移除时机：心上人离场时」 |
| 13 | 幂等 / 重放 | 同一名心上人 / 贤者只触发一次；事件流重折不重复开裁定、不重复记事实 | 触发器硬约定 |
| 14 | 触发型裁定点挂起 | 推进类命令被拒（`phase.trigger_choice_pending` 同族）；强推 / 收口走显式记录 | D-0011 / R-0027 同族 |
| 15 | 信息隔离 | 无关玩家收发包零下发、零字段；醉酒不上玩家面；贤者信息不含说书人字段 | D-0012 / 《重要细节》三-1 |
| 16 | 重启 / 重建 / 重连 | 事实、裁定点、效果、维度按事件流原样恢复；重建比较器等价 | D-0010 / D-0014 |
| 17 | 相关族回归 | 理发师 / 呆瓜 / 女巫 / 洗脑师链路与全量门禁不破 | 整族对齐 |

## 决定与依据

- **贤者「被恶魔杀死」判据**：死亡事件的 `CausedBy` 席位在**死亡时刻**是恶魔角色
  （按死亡批内事件重建死亡时刻角色，与理发师 `DiedAsBarber` 同族；处决死亡的 `CausedBy`
  为 null、女巫诅咒为女巫、麻脸巫婆追加死亡为麻脸巫婆，自然排除）。
  依据：百科《贤者》范例 3——「因为贤者死于麻脸巫婆，而非恶魔，所以贤者不会醒来」。
- **贤者交互承载**：死亡批记事实（跨阶段保留，与 `BarberNight` 同模板），当夜 `sage`
  触发格开裁定点、说书人选择展示的两名玩家；信息结果只发给贤者本人。
  依据：《贤者》角色能力「**在当晚**你会被唤醒」；《夜晚行动顺序一览》其他夜晚 sage 条目；
  《死亡触发能力》「涉及交互的效果等到夜晚」。
- **心上人交互承载（平台口径，登记）**：死亡批内**立即**开触发型裁定点（无槽位来源），
  裁定后立即施加持续醉酒。依据：《死亡触发能力》「死亡时立即触发」；《心上人》提示标记
  「放置时机：当心上人死亡时」；该能力不对受影响的玩家征求选择，不属于「等夜晚的交互」。
- **心上人效果生命周期**：来源 = 心上人席位、`SourceCharacter = sweetheart`、
  `SourceStateIndependent = true`、`Dimension = Drunk`；**来源死亡不终止**（效果在死亡之后
  落账，死亡事件先于效果事件），来源换角（离场）走 `SourceLostAbility` 自然终止。
  依据：《心上人》提示标记「移除时机：心上人离场时」；R-0031 同族（既成事实类效果）。
- **触发型裁定点（基建）**：给裁定点补上与 `OperationRequestOrigin` 对称的来源表达
  （槽位来源 / 触发来源），使「谁在等说书人」可识别、可投影、可挡推进；心上人是首个用例。
- **顺序表承载**：`sweetheart` / `sage` 由 `Action` 改 `Trigger`（`CharacterTrigger`）——
  死亡触发能力不属于持有者本人的行动，角色不在场 / 已死亡也保留该格；表内容与位置不变
  （D-0013 §1 的完整顺序表保真），只改平台承载方式。依据：`NightOrderEntryKind.CharacterTrigger`
  的既有定义（理发师先例）。
- **能力未生效口径**：沿用「照常推演 + 显式标注」；心上人不产生效果并留跳过记录。
- **说书人自由裁量**：贤者展示的两名玩家、心上人的醉酒目标都由说书人裁定；平台只给
  推演提示与合法选项，不替说书人拍板（D-0002）。

## 实现（本轮）

- **内核——触发型裁定点**：`DecisionPointRaisedEvent` 补来源表达（`SlotId` / `TriggerAbility`
  恰好一个非空）、`StepMachineState.AwaitingDecisionTriggerAbility` 与折叠 / 比较器 / 推进清空；
  `phase.trigger_choice_pending` 闸扩展到触发型裁定（未了结时推进类命令被拒）。
- **内核——贤者事实**：`SageNight` + `SageNightOpened` / `Closed` / `SkippedEvent`：死亡批开启、
  当夜格消费、夜末「过时不候」显式清空、跨阶段携带显式失败；比较器同步。
- **内核——心上人跳过账**：`SweetheartSkipRecord` + `SweetheartDeathSkippedEvent`
  （触发器的幂等依据，跨阶段保留）。
- **Rules——顺序表**：`sweetheart` / `sage` 由 `Action` 改 `Trigger`（`CharacterTrigger`；
  表内容与位置不变，只是平台承载方式）。
- **Rules——贤者**：`SageNightTrigger`（判据：死亡时刻角色 + `CausedBy` 席位在死亡时刻是恶魔角色；
  处决 / 女巫诅咒 / 处罚处决 / 麻脸巫婆追加死亡不触发）、`SageInteraction`（`pair:` 两维原子选择 →
  信息结果只发本人）、`SageAbility`。
- **Rules——心上人**：`SweetheartDeathTrigger`（死亡批内**立即**开触发型裁定；结清后施加
  `SourceStateIndependent` 的持续醉酒；未生效 / 未观测 / 未裁定显式跳过；跨事件防裁定点覆盖）、
  `SweetheartAbility`。
- **共用重构**：玩家对编码抽出 `PlayerPairChoice`（理发师与贤者共用）；「死亡时刻角色」重建抽到
  `DeathTriggerReadings`（三族共用，理发师行为不变）。
- **文档**：R-0038 / R-0039 登记；架构 §2.3 / §2.6 / §2.7 / §2.9 同步（事件计数 41 → 48）。
- **测试**：Kernel `SageNightTests` / `SweetheartDeathTests`；Rules `SageNightTriggerTests` /
  `SweetheartDeathTriggerTests`；Integration `DeathTriggerHostTests`（真宿主：处决心上人 → 触发型裁定
  → 推进命令被拒 → 指定醉酒；次夜击杀贤者 → 当夜裁定 → 信息只到本人 + 无关席位零下发）；
  顺序表断言与 `DayActions` 覆盖名单同步。
- **装置**：`tools/verify-death-triggers.mjs`（真机两链：触发型裁定挂起 + 开夜被拒 + 指定醉酒 +
  跨阶段保留；当夜展示 + 信息只到本人 + 五席 99 帧零说书人字段 + 阳性对照）；迭代档 **55 项全过**、
  退出码 0；已登记 `docs/acceptance/devices.md`；产品侧疑点（触发格裁定无席位归属 / 白天读数越界 /
  候选不标生死）登记为 `todo/storyteller-decision-affordances.md`。

## 残余（开工前登记，收尾时逐条更新）

- 心上人醉酒目标允许选已死亡玩家（百科「任一玩家」无存活限制）；该选择对局面无实际影响，
  但保留说书人权利。
- 贤者展示「任意两名玩家」不做「必须含真恶魔」的硬校验（信息内容由说书人给出，D-0002），
  只在提示里写明推演；能力生效时是否合规由说书人自检。
- 相克条目（陌客可能被当作杀死贤者的恶魔）涉及非首版角色，不在本票范围（R-0002 双角色口径）。
- 装置未覆盖的边界：处决 / 非恶魔死因、涡流在场、死亡时醉酒 / 中毒、心上人离场解除、
  重放 / 重连——由 Rules 用例与 `DeathTriggerHostTests` / `DeathTriggerStateJsonTests` 组合覆盖，
  判定时逐行注明证据性质；将来扩装置时优先补「离场解除」与「选已死亡玩家」。
- 界面侧：触发格 / 触发型裁定在说书人圆环上暂无席位归属、白天计划收口后读数越界（2 / 1）、
  裁定候选不标生死——登记为 `todo/storyteller-decision-affordances.md`，随面板迭代同批处理。

## E24 验收判定（冻结版本 `d5cf109`）

本票装置 `tools/verify-death-triggers.mjs`（**本批新增**）取证档 `--quota 2 --screenshots-all --build`：
**56 项全部通过 / 0 跳过、退出码 0**；截图 `deathtrigger-01…06` 为本次运行写入并**逐张复核**
（处决心上人 / 触发型裁定 + 开夜被拒 / 醉酒标记与效果来源 / 贤者裁定按击杀记录推演 /
2 号玩家页信息 / 5 号无关席位空态）。

矩阵逐行（证据性质逐条注明）：

| # | 判定 | 证据 |
|---|---|---|
| 1 | 通过 | 装置（贤者裁定 + 信息只到本人）+ `DeathTriggerHostTests` |
| 2 | 通过（规则级） | `SageNightTriggerTests` 处决路径 |
| 3 | 通过（规则级） | 同上（女巫 / 处罚处决 / 麻脸巫婆三条死因各一例） |
| 4 | 通过（规则级） | `Resolve_IneffectiveSage_MarksMayBeFalse` |
| 5 | 通过（规则级） | `Resolve_WithVortox_MarksMustBeFalse` |
| 6 | 通过 | 装置（6 组 pair、不含贤者）+ 规则用例（候选含已死亡者） |
| 7 | 通过（内核级） | `SageNightTests`（夜末「过时不候」收口；跨阶段携带显式失败） |
| 8 | 通过（组合证据） | 规则用例覆盖夜杀批；装置覆盖白天处决批（同一触发器路径） |
| 9 | 通过 | 装置 + `DeathTriggerHostTests`（处决 → 触发型裁定 → 指定醉酒） |
| 10 | 通过（规则级） | `Skips_WhenIneffective` |
| 11 | 通过 | 装置（跨阶段仍标醉酒）+ `DeathTriggerHostTests` |
| 12 | 通过（组合证据） | 效果终止走 E13 的通用生命周期：`EffectAttributionTests.SourceCharacterChange_TerminatesItsEffects`（来源换角 → `SourceLostAbility`）+ `DimensionEffectReconcilerTests`（终止 → 维度解除）+ 本票 `SweetheartDeathTriggerTests` 断言效果 `SourceCharacter = sweetheart`；**未跑**针对性真机离场用例——见残余 |
| 13 | 通过 | Kernel 幂等用例（重复开启 / 关闭抛错、跳过账）+ `DeathTriggerStateJsonTests` |
| 14 | 通过 | 装置（`phase.trigger_choice_pending` 拒绝被界面读出）+ `DeathTriggerHostTests` |
| 15 | 通过 | 装置（五席 126 帧零说书人字段 + 无关席位零下发）+ `DeathTriggerHostTests` |
| 16 | 通过（组合证据） | `DeathTriggerStateJsonTests`（含新字段非空的快照往返）+ 比较器等价 + 既有真重启用例 `PitHagResidueHostTests`（同一序列化 / 重建路径）；**未跑**含新字段非空的真重启——见残余 |
| 17 | 通过 | 主装置 194 / 角色变更族 59 / 零信任 43 / 全量 703 通过 |

回归面：主装置 `verify-storyteller-panel.mjs` **194 项**、角色变更族 `verify-character-change.mjs` **59 项**、
零信任 `verify-zero-trust.mjs` **43 项**（三项全过、退出码 0）；
冻结版门禁：`dotnet build` 0 警告 0 错误、`dotnet test` **703 通过 / 0 失败**、`dotnet format` 就地通过。

诚实记录（本批真实发生的事）：

- **独立对抗性自检发现 HIGH-1 并已修复**：触发型 / 触发格裁定（贤者展示、心上人醉酒）结清后当晚不再推进——
  裁定 id 不属于当前槽位时内核只落裁定，而挂起期间配额已走完，结清后没有任何推进入口
  （自检真宿主探针：12.1 秒 / 约 240 次节拍 `SlotIndex` 恒 12）。修法：**提交管线**在触发管线之后补一步
  「重进本格」复位配额（`SessionCommit.BuildDecisionContinuation`：有挂起一律不补，避免清掉未了结请求；
  计划已走完不补）；内核契约保持不变（理发师路径与既有用例不受影响）。回归证据：
  `TriggerSourcedDecisionResolution_LeavesContinuationToCommitPipeline` /
  `..._WithPendingRequest_KeepsRequest`（Kernel）、`DeathTriggerHostTests` 第 ⑦ 断言（真宿主夜晚收口）、
  装置第 56 项「贤者裁定结清后夜晚继续自动推进到收口」。
- **自检同时发现 MEDIUM-2 恒真断言并已修**：`TestServerHost.WaitForViewAsync` 超时返回最后视图，
  其后跟 `Assert.NotNull` 永真——本票三处改用条件断言；`InformationResultDto` 序列化「不含 Note」同属恒真，
  改为键集合断言（`["ability","content","sequence"]`，Web defaults 口径）。**整族对齐**：E23 的
  `RetrospectiveInfoHostTests` 同病三处 + `AssertInfo` 一并修正。
- **架构门禁真实拦截两次**：`StepMachineFolder.cs` 加完两族账后 713 行超限 → 拆出 `DeathTriggerFolder.cs`
  （472 + 172 行）；`GameSession.cs` 因续推调用一度 605 行 → 两步合成
  `SessionCommit.AppendDerivedWithContinuation` 一次调用（546 行）。
- 装置未覆盖的边界（残余已逐条登记）：处决 / 非恶魔死因的**真机**路径、涡流在场、死亡时醉酒 / 中毒、
  心上人离场解除（行 12）、真重启 / 重连（行 16）、裁定候选选已死亡玩家。
