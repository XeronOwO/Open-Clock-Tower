# 亡骨魔：夜间击杀 + 爪牙「保留能力」+ 集骨者「先失去」前置

- Status: Review
- Priority: High
- Depends on: 无（同族：`docs/backlog/done/granted-entry-ability-insertion.md` 已落地，两者的
  「持有能力者的入格放行」判据同源；跨剧本的「假死」僵怖见 `future/cross-script-extension.md`）

## 要解决的问题

亡骨魔在首版花名册里（`SectsAndVioletsRoster`），却在夜晚顺序表上有格、没有契约——
**抽到亡骨魔当恶魔的对局开不了夜**：`NightPlanBuilder` 按 `plan.contract_missing` 显式拒绝
（`docs/architecture/current.md` §2.6「能力边界」；`done/settlement-engine.md` 残余 1）。
四个恶魔里少一个＝四分之一的配板开不了局。

同时它带着两条平台还没有的机制：

1. **爪牙死后保留能力**（「被你杀死的爪牙保留他的能力」）：死亡不再等于失去能力，
   而平台的 `AbilityPresentOn` 目前只认「存活」或「生效中的重获窗口」；
   保留能力的爪牙**仍要在夜晚行动**（角色简介 1），而建表把「已死亡的持有者」一律算空槽位。
2. **集骨者的「先失去」前置**（`docs/standard/rulings.md` R-0054 第 10 条）：
   重获（regain）以"先失去"为前提；被亡骨魔杀死并保留能力的爪牙没有失去能力，
   集骨者**不该**让他"再次获得"。这条判据在亡骨魔落地前无处可依，落地后必须一起补上
   （依据：百科《集骨者》· 规则细节 1——「因为这些玩家虽然'死亡'但仍然具有能力，集骨者不会让
   这些玩家再次获得能力……regain 意味着这项能力需要先失去之后，才能被'重获'」）。

## 依据（百科《亡骨魔》· 2026-10-01 抓取）

- 角色能力——「每个夜晚*，你要选择一名玩家：他死亡。」「被你杀死的爪牙保留他的能力，
  且与他邻近的两名镇民之一中毒。[-1外来者]」；
- 角色简介 1——「虽然爪牙会死亡，但只要**亡骨魔还存活**，该爪牙就能保留能力，且**仍能在夜晚行动**」；
- 角色简介 2 / 3——「所有被亡骨魔杀死的爪牙都会保留能力并使一名镇民中毒」；
  中毒目标取「顺时针或逆时针最近的镇民」，跳过非镇民，由说书人选择哪一侧；爪牙已死亡也照中毒；
  亡骨魔死亡或失去能力 → 中毒者恢复健康；
- 角色简介 4——「如果死亡的爪牙变成非爪牙角色，便不再使相应的镇民中毒，且不再保留能力。
  如果死亡的爪牙醉酒或中毒，也会失去能力直到再次恢复清醒和健康」；
- 提示标记「保留能力」——移除时机「亡骨魔死亡或离场，或被标记的玩家角色不再是爪牙角色时」；
- 旁证：百科《死后能力保留》· 能力简介——这类能力「生效与否不关注玩家的生死状态」，
  但醉酒 / 中毒仍然让它失效；
- 设置调整 `[-1 外来者]` **已实现**（`SectsAndVioletsRoster` / R-0042），本票不碰配板。

## 方案（机制清点后定稿）

**机制清点结论**（读源码得到的现状）：

| 要接的点 | 现状 | 缺口 |
|---|---|---|
| 夜间契约 | `NightActions.Catalog` 无 `vigormortis`；`NightPlanBuilder` 建到恶魔格时按 `plan.contract_missing` 拒绝 | 新契约 |
| 击杀出口 | `NightKill.Resolve` 统一出口（窗口期外即时死亡、窗口期内待定死亡）；恶魔夜序在爪牙段**之后** | 复用 |
| 「能力在场」 | `GameState.AbilityPresentOn`：存活 → true，死亡 → 只看 `RegainedAbilityOn` | 加「保留能力」一路 |
| 行动格入格放行 | `StepSlotEntry.UnavailableReason` 已按 `AbilityPresentOn` 放行死者 | 复用 |
| **建表绑定** | `NightPlanBuilder.BuildCharacterSlot` 只把**存活**持有者绑成行动格，死者一律空槽 | 保留能力的死者必须绑格 |
| 窗口族 | `EffectWindowKind`：`AfflictionImmunity` / `SecondAction` / `RegainedAbility`；`DuskExpiry.ExpireAll` 在**每个黄昏**收掉全部窗口 | 保留能力**不随黄昏**到期，需分族 |
| 常驻效果 | `IStandingEffectSource` + `SettlementReconciler`（按期望集补 / 终止）；期望项只有 `Dimension` | 期望项要能表达**窗口** |
| 中毒 | `NoDashiiPoisonSource` 是最接近的同族（按席位环算邻近、按来源状态挂起） | 亡骨魔是**说书人选一侧** |
| 死亡 → 终止 | `GameStateMachine.LosesAbility`：来源死亡 → 其名下效果与疯狂要求终止 | 保留能力 = **没有失去**，不得终止 |
| 集骨者「先失去」 | `BoneCollectorNightAction` 候选 = 全部已死亡席位，未问「是否仍保有」 | 显式拒绝 / 跳过 |

**落地形状**：

1. **内核记录**：新事件 `VigormortisKillRecordedEvent` + `GameState.VigormortisKills`
   （`VigormortisKill { Demon, Minion, Side }`）——记录「哪位亡骨魔杀死了哪位爪牙」与
   **说书人选的中毒侧**；目标侧（邻近镇民）按百科提示标记第 23 条**动态重算**
   （"一旦标记了此中毒标记的玩家不再是邻近的镇民玩家之一，就需要移动中毒标记至同一侧"）。
2. **两条持续型效果**（同一能力 `vigormortis.retention` 名下，由常驻来源统一维持 / 终止）：
   - `Window = RetainedAbility` 落在**爪牙**身上（`GameState.RetainedAbilityOn`）；
   - `Dimension = Poison` 落在**该侧最近的镇民**身上（标识含目标：换人就换标识 → 旧的终止、新的落地，
     与 `NoDashiiPoisonSource` 同款）。
   保留能力窗口在**击杀当场**由契约落下（排在死亡事件之前）——死亡折叠时据此判定"没有失去能力"，
   它名下既有的持续型效果与疯狂要求才不会被误终止。
3. **能力在场**：`AbilityPresentOn` = 存活 → true；死亡 → 重获窗口**或**保留能力窗口；
   两路都判定不了 → null（不猜）。女巫 / 诺-达鲺 / 涡流三处原本直接读 `RegainedAbilityOn` 的
   存续判定**整族改为读这一份口径**（同一个"死亡不一定失去能力"）。
4. **建表**：`BuildCharacterSlot` 的"行动者"从「存活持有者」放宽到「存活持有者，或死亡但
   `AbilityPresentOn == true` 的持有者」；后者沿用集骨者重获格的依赖口径（不锁生死、只锁角色）。
   同一角色出现**多名**死亡且保留能力的持有者 → 显式拒绝（不猜）。
5. **窗口分族**：`DuskExpiry` 改为只收「下个黄昏到期」的窗口（咖啡师两效果 + 集骨者重获），
   保留能力窗口不进这一族。
6. **死亡不再等于失去能力**：`LosesAbility` 的死亡分支按 `RetainedAbilityOn` 放行；
   保留能力终止（亡骨魔死亡 / 离场 / 失去能力 / 该爪牙不再是爪牙）时，它名下窗口期效果与
   疯狂要求**一并终止**（与 R-0054 第 3 条的重获级联同一处实现）。
7. **集骨者「先失去」前置**：候选与结算都跳过 / 拒绝仍保有能力的死亡席位（R-0054 第 10 条落地注记）。
8. **麻脸巫婆之夜**：击杀按统一出口记为**待定死亡**；「保留能力」载荷随待定死亡一起记
   （`DeferredRetention`），说书人确认时按同一条路径落格——确认与阻止的结果因此与普通夜晚一致。
9. **投影**：两条效果只进说书人视图（既有 `EffectDto`，无新 DTO）；复盘步骤认领新事件。

## 自检表（开工前逐条问过）

| 问题 | 结论 |
|---|---|
| 这条规则有来源吗 | 有：百科《亡骨魔》· 2026-10-01 抓取（规则细节 1–24 + 简介 + 运作方式）、
《死后能力保留》· 同批抓取、《集骨者》规则细节 1；摘要在 `docs/standard/character-rules.md` §亡骨魔 |
| 哪些是规则、哪些是我们的选择 | 规则：击杀 / 保留能力 / 中毒 / 移除时机；选择：状态怎么表达（记录 + 窗口 + 维度）、
中毒侧怎么存（存侧、目标动态重算）、麻脸巫婆之夜的载荷形状 → 登记 R-0056 |
| 状态属于谁 | 击杀记录属状态账（`GameState`）；两侧效果由常驻来源独占写（D-0015 单一写入方） |
| 会耦合六维度吗 | 不会：死亡不改醉酒 / 中毒 / 阵营；保留能力窗口只回答"能力在不在" |
| 确定性 | 无时间 / 随机 / IO；席位环按升序取，说书人选侧是**输入**不是随机 |
| 家族对齐 | 女巫 / 诺-达鲺 / 涡流 / 数学家 / 集骨者五处同族判定逐个核过（见「残余」） |
| 结构门禁 | 新类均 < 600 行、一文件一顶层类型；新增事件必须被复盘目录认领 |

## 验收矩阵（逐行判定）

判定依据：本轮代码与测试均已在同一冻结版本上跑绿——`dotnet build` **0 警告 / 0 错误**、
`dotnet test OpenClockTower.slnx` **1165 通过 / 0 失败**（内核 466 / 规则 436 / 集成 238 / 规范门禁 25）、
`dotnet format` 退出 0、前端 `npm run gate` 退出 0（类型检查 + 单测 + 构建）。下表「证据」列写的是
**这一次**运行的运行时证据，不是推测。

| # | 场景 | 期望 | 证据 | 判定 |
|---|---|---|---|---|
| 1 | 带亡骨魔的局开夜 | 不再 `plan.contract_missing`；恶魔格正常开请求 | `VigormortisHostTests`：六席局 `StartNight(1)` 受理、首夜自然走完、第 2 夜 `sv:night-2:vigormortis` 请求到达；`NightPlanBuilderTests.FangGuSlot_IsActionSlot_OnlyOnOtherNights` 同族口径 | 通过 |
| 2 | 亡骨魔杀爪牙 | 爪牙死亡 + 「保留能力」效果落账 + 邻近镇民中毒（说书人选侧） | `VigormortisHostTests`：裁定点选 `clockwise` 后 `Ability=vigormortis.retention`(Target=2, Window=RetainedAbility) 与 `Dimension=Poison`(Target=3) 同时未终止；`VigormortisNightActionTests.KillMinion_PlacesRetentionBeforeDeath_AndRecordsSide`（事件顺序）+ `VigormortisRetentionSourceTests.ValidKill_ExpectsWindowAndPoison` | 通过 |
| 3 | 保留能力的死亡爪牙在自己的行动格 | 被唤醒并结算（不是"死者不唤醒"） | `VigormortisHostTests`：第 3 夜死者（`Life=Dead`）收到 `sv:night-3:witch` 并成功提交；`NightPlanBuilderTests.RetainedDeadMinion_IsWokenWithUnlockedLifeDependency`（依赖不锁生死） | 通过 |
| 4 | 亡骨魔死亡 / 离场 / 爪牙不再是爪牙 | 效果终止、中毒解除、该爪牙不再被唤醒 | 真宿主：白天 3 报亡骨魔死亡后两条效果均 `IsTerminated`；内核 `RetainedAbilityTests.AbilityPresentOn_TracksTheRetainedWindow` / `LosingRetention_CascadesToTheMinionEffectsAndMadness` / `DemonDeparture_TerminatesRetentionAndCascades`；来源 `MinionNoLongerMinion_MeansNoExpectations` / `TerminatedWindow_IsNotRestored` | 通过 |
| 5 | 保留能力的爪牙中毒 / 醉酒 | 能力失效（生死不参与判定） | 内核 `RetainedAbilityTests.RetainedMinion_IsStillBlockedByPoisonAndDrunk`（生效判定：保留窗口放行生死，中毒 / 醉酒照旧否掉） | 通过 |
| 6 | 集骨者试图"重获"保留能力的爪牙 | 显式拒绝 / 跳过，并说明「先失去」前置；不落重获窗口 | 规则 `BoneCollectorNightActionTests.Prompt_SkipsSeatsThatNeverLostTheirAbility`（候选排除 + 提示写明「先失去」）与 `Grant_ToRetainedSeat_Throws`（结算显式失败） | 通过 |
| 7 | 无关玩家视角 | 保留能力 / 中毒标记只说书人可见 | 真宿主：旁观席（4 号）与当事席（2 号）的 `PlayerView.InformationResults` 为空、无待响应请求；标记只出现在说书人视图的 `PersistentEffects`。结构性依据：`PlayerView` 没有效果通道，DTO 名单由 `PlayerProjectionLeakGateTests` 扫 | 通过 |

**界面级（装置）证据**：本票的载体是「说书人裁定点 + 玩家被唤醒」，装置档（真浏览器 + 多客户端）
尚未覆盖到这一族——见「残余」。

## 残余（不在本票）

- **装置档待补**：主装置 `verify-storyteller-panel.mjs` 的夹具不含亡骨魔；说书人「选侧」裁定点的
  界面呈现、以及死亡玩家在自己的设备上收到请求这一路，目前只有真宿主（无浏览器）证据。
  下一次验收批次补一段/一台装置（候选：主装置加 `vigormortis` 段，或新装置 `verify-vigormortis.mjs`）。
- 「假死」僵怖（Zombuul）：**不在《梦殒春宵》**，随跨剧本扩展票；R-0054 第 10 条对该角色继续有效。
- 主谋的相克规则（「如果亡骨魔死亡，被亡骨魔杀死的主谋依然保留能力」，百科 · 规则细节 1）：
  主谋不在首版剧本，随跨剧本扩展票；本票按「标记随亡骨魔失效」实现。
- 「保留能力」影响其它读取"能力在场"的角色：**已整族核对并逐个表态**（R-0056 第 10 条）——
  女巫 / 诺-达鲺 / 涡流改读统一口径（死而保有能力者继续算"在场"）；数学家读失效账本、屠夫只作用于
  旅行者、艺术家的"已用过的提问可以再用"是重获语义，三者都不受影响。
