# 集骨者重获能力：死亡杂耍艺人的白天入口（首个白天重新起算）

- Status: Done
- Priority: Medium
- Depends on: 无（同族已落地：`done/traveller-and-exile.md`（集骨者 R-0054）、
  `done/juggler-and-savant-day-abilities.md`（杂耍艺人 R-0057-B）、`done/seamstress-and-artist.md`（艺术家 R-0040））

## 要解决的问题

集骨者（旅行者）可以把一名**已死亡**玩家的角色能力「重获」到身上，持续到下一个黄昏
（百科《集骨者》· 2026-10-04 抓取 · 角色能力）。规则明确：重获之后**已经用过的「每局限一次」能力还能再用一次**
（同页 · 角色简介 2——「『首个夜晚』『每局游戏限一次』类能力可在黄昏前再次使用，**即使先前已经用过**」）。

首版 30 个角色里，白天能力的重获路径**三缺一**：

| 能力 | 用度记在哪 | 重获时能不能再用 | 证据 |
|---|---|---|---|
| 艺术家（提问） | 能力使用账本（整局） | ✅ 能 | `ArtistQuestionMachine.Ask`：`RegainedAbilityOn == true && uses < 2` 时放行 |
| 博学者（要两条信息） | 按白天记账（阶段边界清零） | ✅ 能 | 白天入口只要求「能力在场」；重获窗口把该席位按「握有角色能力」处理 |
| **杂耍艺人（公开猜测）** | **按「这一次持有」的起算日记账** | ❌ **不能** | 见下 |

杂耍艺人的「用过没有」是**从起算日往后扫**（`guess.DayNumber >= tenureStart`），而起算日只认
「变成杂耍艺人」那条角色变化记录。死者重获能力**不产生角色变化**，于是起算日还是他当年拿到角色的那一天：

- 第 1 天猜过 → 第 N 天（重获窗口内）提交被拒：`juggler.not_first_day`
  （`tenureStart` 仍 = 第 1 天 ≠ 今天）；
- 就算把起算日放宽，`already_guessed` 也会拦——第 1 天那条记录仍在 `DayNumber >= tenureStart` 的范围里。

**玩家视角的后果**：集骨者花掉唯一一次重获机会救活了死亡杂耍艺人的能力，说书人也照规则唤醒了他，
但那名玩家在界面上**拿不到「公开猜测」入口**、真去提交也会被拒——平台把一个规则允许的用法静默判成非法
（不是"少一点信息"，是**这条规则在平台上不成立**）。

## 决定与依据

- **依据**：
  1. 百科《集骨者》· 2026-10-04 抓取 · 角色简介 2——重获后限次能力可再用，即使先前已用过；
  2. 同页 · 规则细节 2——重获**非强制能力**（艺术家 / 博学者一类）时说书人无需提示可否使用，
     重获**夜晚会行动**的能力时必须主动唤醒；杂耍艺人属"白天主动使用"，玩家自己就能发起；
  3. R-0057-B 第 4 条（「首个白天」的起算）与 R-0054 第 4 条（重获的用度口径）——两条在这里相接；
  4. 同族先例：`ArtistQuestionMachine` 的同一处放宽（`RegainedAbilityOn` + 上限 2）。
- **口径（已登记进 `rulings.md` R-0057-B 第 4 条）**：重获窗口存续期间，**这一次持有从当天重新起算**——
  被重获的杂耍艺人在窗口存续的那个白天可以再公开猜测一次；窗口内仍然**只有一次**
  （同一天第二次照旧 `juggler.already_guessed`）；窗口到期（下个黄昏 / 集骨者死亡或离场）后回到原起算点。
- **没有新裁定编号**：这是 R-0057-B 第 4 条与 R-0054 第 4 条的交叉，不是新的项目取舍。

## 落地记录（2026-10-05）

- 内核 `JugglerGuessMachine.Make`：新增一次 `RegainedAbilityOn` 判定——窗口生效（`== true`；
  判定不了按原口径拒绝，不猜）时把 `tenureStart` 取为**今天**，一处同时解决 `not_first_day` 与
  `already_guessed` 两条拒绝路径；判定不了（`null`）不放行。
- 应用层 `DayProjection.CanMakeJugglerGuesses`：同一处放宽，否则玩家页**连入口都不给**
  （投影只给权限位，真正的合法性仍由内核按同一份账再判一次）。
- 测试：内核 `JugglerGuessMachineTests` 两条（窗口内可再猜一次 / 窗口内仍只一次）+
  集成 `DayProjectionTests.JugglerGuesses_DeadButRegainedJuggler_GetsTheEntryOnTheRegainedDay`
  （同一份账下"有窗口给入口、没窗口不给"）。
- 证据：`dotnet build` 0 警告 0 错误；`dotnet test OpenClockTower.slnx` **1250 通过 / 0 失败**
  （内核 500 / 规则 484 / 集成 241 / 规范门禁 25）。

## 验收矩阵

| # | 场景 | 期望 | 证据 | 判定 |
|---|---|---|---|---|
| 1 | 死亡杂耍艺人被集骨者重获能力，窗口存续的那个白天再来猜 | 受理；猜测进当天账并公开 | 内核 `JugglerGuessMachineTests.Make_AllowsDeadButRegainedJuggler_WhenAlreadyGuessedOnce`（第 1 天猜过、第 2 天窗口内受理）+ 集成 `BoneCollectorHostTests.BoneCollector_RegainingDayAbility_LetTheDeadJugglerGuessAgainOnThatDay` 同一断言（真宿主命令面） | 通过 |
| 2 | 同一份账**没有**重获窗口 | 照原口径拒绝（`juggler.not_first_day`） | 同一用例的前半段断言 + 内核 `Make_RejectsOutsideTheFirstDay` | 通过 |
| 3 | 重获窗口内同一天猜第二次 | 拒绝（`juggler.already_guessed`）——放宽的是起算点，不是次数 | 内核 `Make_StillRejectsSecondGuessInTheRegainedDay` + 真宿主同一断言 | 通过 |
| 4 | 玩家端入口（权限位） | 有窗口 → 给入口；没窗口 → 不给 | 集成 `DayProjectionTests.JugglerGuesses_DeadButRegainedJuggler_GetsTheEntryOnTheRegainedDay`；**界面面**由新装置 `tools/verify-bone-collector-juggler.mjs` 判（死亡玩家的入口重新出现、在页面上填表提交、进公开面、无关席位看得到同一份） | 通过（批次 E42 取证档） |
| 5 | 窗口到期（下个黄昏）之后 | 起算点回到原处：`not_first_day` 再次生效 | 内核 `Make_RejectsAgainAfterTheWindowExpires`（窗口开了又关）+ 集成 `DayProjectionTests.JugglerGuesses_WindowExpiresAtDusk_EntryDisappearsOnTheNextDay`；真宿主 `BoneCollectorHostTests` 断言窗口在下个黄昏终止；**界面面**由装置第 9 / 10 段判（批次 E43：第 4 夜不再唤醒、第 3 天入口消失 + 效果链「已终止」） | 通过 |
| 6 | 无关玩家视角 | 入口只发给本人；猜测仍是**公开**事实（与 R-0057-B 一致，不因重获改变） | 装置断言无关席位的入口数为 0、且看得到同一份公开猜测；字段未变、投影未新增下发面 | 通过（批次 E42 取证档） |
| 7 | 集骨者的候选面 | 候选只含已死亡的席位、逐条写明「已死亡」，摇头始终可选 | 装置 `tools/verify-bone-collector-juggler.mjs`：第 2 夜 0 名（3 号还活着）、第 3 夜 1 名（刚死的 3 号）；请求正文写明「重新获得角色能力直到下个黄昏」 | 通过（批次 E42 取证档） |

## 残余 / 风险

- **装置级（界面）证据已补**（2026-10-05，批次 E42）：新装置 `tools/verify-bone-collector-juggler.mjs`
  独立一局（5 席 + 集骨者第 6 席）：第 1 天活着的杂耍艺人猜一次 → 第 2 夜恶魔杀他 →
  第 3 夜集骨者选中他重获能力 → 第 2 天（窗口存续）**已经死亡的他重新拿到公开猜测入口**、
  在页面上填表提交、进当天账与公开面、无关席位看得到同一份、同日第二次被拒。
- **到期那一夜已并入本装置**（2026-10-05，批次 E43）：装置跑到第 4 夜（下个黄昏）与第 3 天——
  第 4 夜整夜不再出现报数裁定点、3 号页 `idle`；第 3 天入口不再出现、效果链显示「已终止（下个黄昏：
  窗口到期）」。到期至此三层齐备（内核 / 集成 / 界面），取证档 42 → **52 项全过**。
  原先那句"重获白天能力之后当夜那一格会把整夜卡住"经实测**不成立**，证伪过程见
  `done/regained-day-ability-night-slot-stall.md`。
- **规则细节 2 的后半句**（重获**夜晚会行动**的能力时说书人必须主动唤醒）由集骨者的落格路径覆盖
  （`NightSlotActivation.PlanRegained` + `GrantedEntryAbilityTests`），本票只补**白天**这一侧。
- **「重获窗口 + 咖啡师行动两次」叠加**：杂耍艺人没有"每局限一次"的账本（用度按持有期起算），
  窗口内本来就只有一次猜测机会，因此没有第二次可放宽——这是口径，不是缺口。
