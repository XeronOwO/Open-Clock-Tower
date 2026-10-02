# 麻脸巫婆：角色变更与「创造恶魔」之夜的死亡裁量

- Status: Done
- Priority: High
- 验收：批次 E15（2026-10-05）判定不通过——行 12（创造镜像双子）未实现；行 3 / 4 / 13 / 15 缺运行证据。
  **批次 E16（2026-10-06）重判行 12 / 3 / 4 / 13 / 15 / 11：全部通过 → 移入 `done/`**；其余 11 行结论累计有效
  （详见下文两批判定与 `docs/acceptance/batches.md`）。
- Depends on: 胜负判定与游戏结束（`done/win-loss-and-game-end.md`）；结算引擎（`done/settlement-engine.md`）；镜像双子配对（同胜负票）；处罚处决命令面（`done/madness-and-adjudicated-execution.md`）
- 来源口径：`references/wiki/麻脸巫婆.wiki`、`夜晚行动顺序一览.wiki`、`重要细节.wiki`、`术语汇总.wiki`、`规则概要.wiki`、`镜像双子.wiki`、`诺-达鲺.wiki`、`贤者.wiki`、`免死.wiki`、`额外死亡.wiki`、`设计师总结的国内玩家对染的错误理解.wiki`（均为 2026-10-01 抓取）

## 要解决的问题

麻脸巫婆是首版《梦殒春宵》里唯一能把任意玩家变成任意角色的角色，至今没有契约：
她在场时开夜被显式拒绝（`plan.contract_missing`）——说书人根本开不了这一夜。

更关键的是，她的能力会逼出三件当前架构**做不到**的事（用户视角的后果按严重度排列）：

1. **创造恶魔之后的当晚无人裁量**。规则把这一晚的死亡交给说书人（可以多杀、可以让被恶魔攻击的人活下来），
   而平台现在只有恶魔能力的自动击杀：说书人既无法阻止，也无法追加，只能事后手工改生死——那会丢掉归因
   （「这人是被麻脸巫婆杀的」），也会让「被恶魔杀死才触发」的能力（贤者等）判错。
2. **当夜被创造出来的角色动不了**。建表把「不在场」固化成空槽位，麻脸巫婆今晚创造出涡流，
   涡流的槽位照样空转——而百科明说「否则，就需要唤醒这名玩家」。
   反过来，说书人在窗口里杀掉的未唤醒恶魔，本来就不该被唤醒。
3. **「恶魔角色清空」没有口径**。她把最后一名恶魔变成非恶魔角色（人还活着）时，
   现行求值器按「没有任何恶魔角色 → 不判」处理，游戏会继续，善良再也赢不了——
   这是一条角色变更族都要走的口径，必须先定下来。

## 机制清点（来源：钟楼百科 · 2026-10-01 抓取）

| # | 环节 | 规则 | 来源 |
|---|---|---|---|
| 1 | 行动时机 | 除首个夜晚外的每个夜晚；顺序表两口径都在「能造成死亡的恶魔」之前（原本：洗脑师之后、方古之前；推荐：哲学家之后、舞蛇人之前） | 《夜晚行动顺序一览》其他夜晚；`NightOrderTable` |
| 2 | 选择 | 一名玩家 + 角色列表上的一个角色；可选自己与已死亡玩家 | 《重要细节》三-1；《诺-达鲺》范例（「将死亡的诺-达鲺变成了卖花女孩」） |
| 3 | 在场即无事发生 | 所选角色已在场 → 无事发生（不是「能力未生效」） | 《麻脸巫婆》角色简介 / 运作方式 |
| 4 | 角色变更 | 所选角色不在场 → 该玩家变成该角色；**阵营不变** | 《麻脸巫婆》运作方式；《重要细节》二（「麻脸巫婆将善良的杂耍艺人变成了女巫，这位女巫仍然属于善良阵营」） |
| 5 | 新角色当晚可行动 | 「例如玩家变成了恶魔，则在恶魔行动时一并唤醒，通知角色变化并让他执行相应行动」；「一旦说书人决定杀死某个还未唤醒的恶魔，这名恶魔玩家当晚就不会被唤醒。否则，就需要唤醒这名玩家」 | 《夜晚行动顺序一览》麻脸巫婆条（含调整理由）；《麻脸巫婆》规则细节 |
| 6 | 创造恶魔 → 当晚死亡由说书人决定 | 时间范围：从「首个能够造成死亡的恶魔行动开始前」到「最后一个能够造成死亡的恶魔行动结束后」；可造成死亡、可阻止被恶魔攻击者的死亡，可多次、可在不同恶魔行动前后 | 《麻脸巫婆》规则细节 1 / 运作方式 |
| 7 | 归因 | 「说书人造成的死亡视为麻脸巫婆造成」，因此不触发贤者等「被恶魔杀死」的能力；能杀死士兵 | 《麻脸巫婆》规则细节；《贤者》范例（「因为贤者死于麻脸巫婆，而非恶魔，所以贤者不会醒来」） |
| 8 | 只加不减 | 说书人只能**造成更多死亡**或**阻止恶魔造成的死亡**；非恶魔造成的死亡无法阻止 | 《麻脸巫婆》规则细节 |
| 9 | 与来源脱钩 | 只要当晚创造了恶魔，即使麻脸巫婆之后离场 / 失去能力 / 死亡，当晚说书人仍可决定死亡 | 《麻脸巫婆》规则细节 |
| 10 | 免死 | 「麻脸巫婆在创造恶魔的夜晚，说书人能让原本被恶魔攻击且会死亡的玩家免死」 | 《免死》角色列表 |
| 11 | 创造镜像双子 | 需为新的镜像双子配对；说书人可选择恶魔或一名已死亡玩家作为对立双子 | 《镜像双子》范例 / 提示标记第 11–14 条 |
| 12 | 创造诺-达鲺 | 新诺-达鲺立即让其邻近镇民中毒（原中毒者按新位置关系解除） | 《诺-达鲺》范例 / 运作方式第 4、6 条 |
| 13 | 角色唯一 | 麻脸巫婆无法创造重复角色（在即无事）；能力造成角色变化不得违反角色唯一 | 《麻脸巫婆》角色简介；架构 §2.5「角色唯一」 |
| 14 | 从有到无 | 恶魔角色因角色变更而清空时的胜负：见「决定与依据」的新裁定 R-0029 | 《规则概要》四；《术语汇总》「恶魔玩家」；《理发师》范例 |

## 实施方案（架构审视）

### 1. 现状与缺口

| 缺口 | 现状 | 本票落点 |
|---|---|---|
| 角色契约缺失 | `NightActions` 没有 `pit-hag`；在场即开夜拒绝 | `PitHagNightAction`（提示 + 结算），注册进 `NightActions` / `DayActions` |
| 运行时角色变更 → 槽位失效 | `NightPlanBuilder` 把「不在场 / 已死亡」固化成无角色的空槽位，进入后不再求值 | 槽位保留**角色 slug**，进入时按**当前账**求值行动者（空槽位照走配额不变） |
| 当晚死亡裁量 | 恶魔击杀直接产死亡事件，没有说书人窗口 | `StepMachineState.PitHagNight`：待定死亡 + 说书人命令 + 恶魔段收口 |
| 恶魔清零口径 | `OutcomeEvaluator.DemonsAllDead` 对「没有恶魔角色」一律不判 | 用 `SeatStateChangedEvent.PreviousCharacter` 区分「从未有过恶魔」（不判）与「运行期恶魔清零」（善良获胜） |
| 创造成对角色 | 配对效果只在首夜由 `EvilTwinNightAction` 落 | 复用既有裁定点机制：`BuildPostChoiceDecision` 开「选择对立双子」裁定 |
| 缺陷② | `HandleVoid` 没有非槽位来源旁路 | 本票复核可达性（角色变更发生在请求挂起时） |

### 2. 目标域模型

| 概念 | 落点 | 理由 |
|---|---|---|
| 角色变更事实 | 既有 `SeatStateChangedEvent`（`Character` 维度，**不带 `Alignment`**） | 六维度独立：角色变、阵营不变（架构 §2.1 硬约束） |
| 变化前角色 | `SeatStateChangedEvent.PreviousCharacter`（可选，提交管线统一补全） | 让胜负求值器看见「恶魔 → 非恶魔」，不引入历史状态 |
| 槽位行动者 | `StepSlot.Character`（数据）+ 进入时求值 | 计划仍是纯数据；空槽位照走配额（D-0013 §1）不变 |
| 待定死亡 | `DeferredDeath`（账目挂在 `StepMachineState.PitHagNight`） | 与呆瓜账目同族：可重放、可审计、说书人视图可见 |
| 说书人裁定 | 新命令 `ResolveDeferredDeath`（确认 / 阻止）+ `PitHagCasualty`（追加死亡） | 与 `PunishExecution` 同一模式：说书人主动命令 + 四道闸 |
| 窗口边界 | 恶魔段最后一个槽位（建表时由 Rules 写进 `StepPlan`） | 「最后一个能造成死亡的恶魔行动结束后」，是规则原文的时间边界 |

### 3. 边界与依赖方向

- **Kernel**：`StepSlot.Character` 与进入时求值、`PitHagNight` 状态与待定死亡事件、`OutcomeEvaluator` 口径；
  不引入随机 / 时间 / IO（D-0008）。
- **Rules**：`PitHagNightAction`（提示：玩家 × 角色；结算：在场即无事、否则角色变更、创造恶魔时开窗）、
  `NightPlanBuilder` 写恶魔段边界、契约注册。
- **Application**：两条新命令 + 四道闸 + 提交管线（补 `PreviousCharacter`、窗口收口）、说书人 / 玩家投影。
- **Server / Contracts / Web**：Hub 方法、DTO 与契约镜像、说书人面板的待定死亡与追加死亡控件。
- **本票不做（明确登记）**：
  - 涡流的引擎级干扰计数（R-0004，另立票）；
  - 角色变更族其余角色（舞蛇人 / 理发师 / 方古 / 哲学家）——本票只把机制地基建好；
  - 角色变更后的**进场能力**通用结算（本票只覆盖常驻效果对账与镜像双子配对两条路径）；
  - 跨剧本角色（主谋 / 圣徒 / 僵怖）——首版剧本之外的扩展，见后续票据。

### 4. 验收矩阵（定稿）

| # | 场景 | 期望 | 依据 |
|---|---|---|---|
| 1 | 契约闸 | 麻脸巫婆在场时可开夜；角色不在夜晚顺序表时不受影响 | 架构 §2.6 同族 |
| 2 | 选择不在场角色 | 目标角色维度变化，事件带归因（`CausedBy` = 麻脸巫婆席），**阵营维度不出现** | 机制 4 |
| 3 | 选择在场角色 | 无状态变化；能力记为已使用且生效（不是「未生效」） | 机制 3 |
| 4 | 选择自己 / 已死亡玩家 | 允许；自己变成其他角色后当晚窗口仍有效 | 机制 2 / 9 |
| 5 | 创建新恶魔 | 该恶魔**当夜即可行动**（其槽位被激活，产操作请求） | 机制 5 |
| 6 | 说书人杀死未唤醒的恶魔 | 该恶魔当晚不被唤醒（槽位成为空槽位，照走配额） | 机制 5 |
| 7 | 恶魔击杀的待定 | 窗口内恶魔击杀不直接致死，只说书人可见地列为待定 | 机制 6 |
| 8 | 说书人阻止 | 阻止后目标不死（且不产生死亡事实），记录阻止原因 | 机制 6 / 10 |
| 9 | 说书人追加死亡 | 追加的死亡归因为麻脸巫婆（不触发「被恶魔杀死」类能力） | 机制 7 |
| 10 | 窗口边界 | 窗口关闭后追加死亡被拒；窗口内未裁定的待定死亡按默认生效并显式记入事件 | 机制 6 |
| 11 | 恶魔 → 非恶魔 | 场上再无恶魔角色时善良获胜（R-0029）；开局就无恶魔仍不判 | 机制 14 |
| 12 | 创造镜像双子 | 开「选择对立双子」裁定，落配对效果（阻断与触发与首夜配对同源） | 机制 11 |
| 13 | 创造诺-达鲺 | 邻近镇民中毒按新位置重算，旧中毒解除 | 机制 12 |
| 14 | 视角 | 待定死亡 / 窗口 / 归因只说书人可见；玩家端只有 R-0022 的生死公告面 | D-0012 §4.3；验收规程 §4 |
| 15 | 重连 / 重放 | 窗口与待定死亡随快照与事件重建；重放不重算 | D-0010 |
| 16 | 缺陷②复核 | 角色变更族下 `HandleVoid` 的非槽位旁路是否需要（结论写进票据） | 胜负票 F-4 |

## 已落地（全部）

| 提交 | 内容 | 证据 |
|---|---|---|
| `3625dec` | **R-0029 恶魔角色清零口径**：`SeatStateChangedEvent.PreviousCharacter` 由提交管线用提交前的账统一补全；求值器区分「本局从未有恶魔」（配置异常，不判）与「运行期恶魔清零」（善良获胜） | 内核回归 4 条；431 项测试全绿 |
| `8dda020` | **Kernel 槽位动态**：槽位记住自己对应哪个角色（`StepSlot.Character`）；进入槽位时按当前账确认行动者（已死亡 / 角色被换走 → 跳过，配额照走）；空槽位却已有存活持有者 → 显式阻塞；新事件 `SlotActivatedEvent`（只许激活尚未进入的槽位，越界 / 错位 / 重复一律抛错）；`NightSlotActivation`（Rules，**结算时**调用，时机早于推进）；`AbilityResolutionContext` 增加 `Plan` / `SlotIndex` | 内核回归 7 条（先红后绿 1 条）+ 规则 4 条；442 项测试全绿 |
| `62c2a03` | **麻脸巫婆契约**：两维原子选择（席位 × 角色，角色表不标注在场与否）；在场即无事发生（能力仍记生效）；不在场则只写角色维度（阵营不变）并带归因与变化前角色；创造出的角色今夜还有位置时激活其槽位；注册进 `NightActions` | 规则回归 7 条；449 项测试全绿，`dotnet format` 干净 |
| `ae65e88` | **死亡裁量窗口**：`PitHagNight` / `DeferredDeath` + 四个事件 + 两条说书人输入（追加死亡 / 裁定待定死亡）；收口时未裁定的按恶魔攻击的自然结果生效并**显式**记录，关闭后两条命令都被拒；`NightKill` 统一恶魔击杀出口（涡流 / 诺-达鲺 已切换）；`PitHagNightAction` 创造恶魔时开窗（关闭点 = 计划里最后一个恶魔槽位）；拆出 `PitHagNightMachine`（`StepMachine` 一度 692 行、被 600 行门禁拦下） | 内核回归 7 条 + 规则 2 条；458 项测试全绿，format 干净 |
| `557a1f7` | **命令面与说书人视图字段**：`PitHagCasualtyCommand` / `ResolveDeferredDeathCommand` + 四道闸 + 两个 Hub 方法；`StorytellerView.PitHagNight` → `StorytellerViewDto`（新契约 `PitHagNightDto` / `DeferredDeathDto`，登记为说书人专属）；前端契约镜像 + 防御性归一 | 458 项 .NET 测试全绿 + `npm run gate` 全绿（89 项） |
| `edd0273` | **缺陷② 修复**：`HandleVoid` 补上非槽位旁路（触发来源请求在计划走完后仍可作废） | 回归先红后绿；460 项测试全绿，format 干净 |
| `60a6c77` | **真宿主集成用例**：造出恶魔 → 当夜激活并行动 → 待定死亡 → 说书人阻止 / 追加死亡 → 收口默认生效 → 关闭后命令被拒；同时修掉被它逼出来的真实缺陷（账折叠器白名单未登记 `SlotActivatedEvent` 与四个窗口事件，真机一产出就抛「未知事件类型」） | 用例先红后绿；461 项测试全绿（Integration 77），format 干净 |
| `82e8957` | **说书人面板控件**：`PitHagNightPanel`（窗口来源 / 关闭点 / 待定死亡列表 + 确认·阻止两个按钮 + 追加死亡控件）挂在面板上；两条命令的前端封装与用例 | `npm run gate` 全绿（90 项 + 构建） |
| `9dfa29e` | **真机装置** `tools/verify-pit-hag.mjs`（5 席，真宿主 + 真 Chromium + Node 客户端）：两维选择 → 面板出现死亡裁量 → 待定不致死 → 「阻止」→「追加死亡」→ 当夜被创造的涡流**真的被唤醒**并击杀 → 「确认死亡」→ 窗口收口 → 收包扫描。另修主装置：重启窗口的代理错误容忍 500 → 500/502（vite 8 的代理在宿主不可达时回 502） | 迭代档 pit-hag 28 项 + 主装置 148 项全过；**取证档（`--quota 2 --screenshots-all`）28 项全过 + 5 张截图（已逐张复核）** |
| `87901f4` `e7382e7` `c972dba` | **E15 残余修复**：行 12 创造镜像双子 → 「选择对立双子」裁定 + `evil-twin.pair` 配对与双向互认（抽 `EvilTwinPairing`，候选与首夜同源）；行 3 / 4 / 11 / 13 / 15 的运行证据补齐 | 行 12 先红（16 秒内裁定点从未出现）→ 转绿（候选 1 / 4 号、效果 Source=3 / Target=4、双向互认到达）；`PitHagResidueHostTests` 5 条 + `PitHagNoDashiiCombinationTests` 1 条 + 规则 `PitHagNightActionTests` 3 条；全解决方案 475 项 + `npm run gate`（90 项）全绿 |

### 缺陷②（`HandleVoid` 的非槽位旁路）复核结论：**可达，已修**

胜负票的独立复核（其 F-4）曾把它记为低 / 存疑并自判「当前不可达」。本票复核判定**可达**：

1. 呆瓜的公开选择是**触发来源**的请求（`OperationRequestOriginKind.Trigger`），R-0027 明确它
   「常常正好开在白天关闭（计划走完）之后、下一夜开始之前」；
2. 作答路径（代填走 `HandleResponse`）有非槽位旁路，**作废路径没有**——`HandleVoid` 把
   `IsPlanCompleted` 检查放在取挂起请求**之前**；强推 `HandleForceAdvance` 同样被它挡住；
3. 后果：那种请求**答得了、撤不掉**，违反 D-0011 / D-0014「兜底入口永远开着」。

修复（提交 `edd0273`）：把 `IsPlanCompleted` 检查移到触发来源旁路之后，与作答路径对称；
回归 `TriggerRequestVoidTests` 先红后绿——触发来源 + 计划走完 → 作废成功；槽位来源 + 计划走完 → 仍被拒（不放松原有约束）。

**收口记录（2026-10-05）**：

1. **装置清单登记**：已外移到 `docs/acceptance/devices.md`（登记六个装置：主装置 / 胜负 / 麻脸巫婆 / 女巫 /
   处罚处决 / 零信任；`web/AGENTS.md` §3.1 只留运行入口与外部耦合，体积不再随装置增长）；
2. **登记残余（上报换角槽位激活）**：已移交 `in-progress/character-change-family.md`，与该族其余角色一起收口。

## E15 验收判定（2026-10-05）

冻结版本 `main` @ `3cd6d2d`；批次记录见 `docs/acceptance/batches.md`（六装置取证档全绿 + 冻结版门禁 465 / 90 全绿 + 真宿主用例复跑）。
判据：**通过** = 本次运行有与该行期望直接对应的断言；**无法判定** = 该行关键面本次没有运行覆盖（写明缺什么）；**不通过** = 运行证据显示行为与期望不符。

| # | 结论 | 本次运行的证据 |
|---|---|---|
| 1 | 通过 | 装置：开首夜 / 第二夜被受理（麻脸巫婆不在首夜表）；主装置「首夜真实建表 13 槽位」 |
| 2 | 通过 | 装置：「3 号牌面变成涡流（角色变更，阵营不变）」（截图 `pithag-02`）；规则用例 `TransformToAbsentCharacter_ChangesCharacterOnly`（`Character` / `PreviousCharacter` / `CausedBy`，`Alignment` 为 null） |
| 3 | 无法判定 | 「无状态变化」有规则用例 `TransformToCharacterInPlay_DoesNothing`；但「能力仍记『已使用且生效』」本次没有运行断言——缺一条把「选在场角色」走完结算并断言 `AbilityResolvedEvent{Effective:true}` / 使用账的用例 |
| 4 | 无法判定 | 「选自己」有规则用例 `TransformSelf_IsAllowed`；「选已死亡玩家」与「来源自变后窗口仍有效」本次无运行覆盖——缺对应真机 / 集成用例 |
| 5 | 通过 | 装置：「当夜被创造的涡流真的被唤醒（3 号拿到请求）」「新恶魔的击杀同样记为待定死亡」；真宿主用例 `PitHagCreatesDemon_AdjudicatesDeaths_AndClosesWindow`；规则 / 内核用例 `TransformToDemon_ActivatesPendingSlot`、`SlotActivated_PendingSlot_BecomesActionAndIssuesRequest` |
| 6 | 通过 | 内核用例 `SlotEntryLedgerTests.ActorDeadBeforeEntry_SkipsWithoutRequest`（进入前已死亡 → 跳过、不产请求，配额照走） |
| 7 | 通过 | 装置：「窗口内记为待定死亡（不直接致死）」「待定期间仍存活」「面板列出待定」；真宿主用例同 5 |
| 8 | 通过 | 装置：「阻止被受理 → 待定清零、仍存活」；内核用例 `ResolveDeferred_Prevented_LeavesTargetAlive`；真宿主用例同 5 |
| 9 | 通过 | 内核用例 `Casualty_KillsWithPitHagAttribution`（`CausedBy` = 麻脸巫婆席）；装置「追加死亡生效」（牌面带 `pit-hag-casualty` 标记）；真宿主用例同 5 |
| 10 | 通过 | 内核用例 `WindowClose_UnresolvedDeathsTakeEffect_AndWindowCloses`（默认生效 + 显式说明）、`AfterClose_BothCommandsAreRejected`；装置「越过最后一个恶魔行动后窗口收口」；真宿主用例（收口后 `kernel.NoPitHagNight`） |
| 11 | 通过 | 内核用例 `DemonChangedIntoNonDemon_GoodWins` / `DemonChangedIntoAnotherDemon_DoesNotEnd` / `DemonChangedIntoNonDemon_WhileAnotherDemonAlive_DoesNotEnd` / `NoDemonConfigured_WithUnrelatedCharacterChange_DoesNotEnd`。注：R-0029 仍为 `Open`（平台口径已定 + 代码注释引用 + 回归齐）；**提交管线补全 `PreviousCharacter` 缺直接运行断言**，列入残余 |
| 12 | **不通过** | 探针（临时用例，未入库）运行：`AwaitingDecision=null`、机器照常越过槽位、事件流无 `evil-twin.pair`——「选择对立双子」裁定与配对效果均未实现。代码侧：`PitHagNightAction.BuildPostChoiceDecision` 返回 `null`；配对效果唯一来源是首夜 `EvilTwinNightAction`（其注释自述「麻脸巫婆造成的新建 / 重配对不在本票」） |
| 13 | 无法判定 | 来源规则有 `NoDashiiPoisonSourceTests.CharacterChangeMovesThePoison` 等、对账机制有 `SettlementReconcilerTests`；但「创造诺-达鲺 → 邻近镇民中毒、旧中毒解除」这条组合本次没有运行覆盖 |
| 14 | 通过 | 装置：「无关玩家（5 号）全部推送里没有窗口 / 待定死亡字段」；规范门禁 `PlayerProjectionLeakGateTests`（`PitHagNightDto` / `DeferredDeathDto` 为说书人专属） |
| 15 | 无法判定 | 折叠器已支持窗口事件族（`StepMachineFolder`），内核用例用 `Apply` 折出窗口状态；但「含窗口 + 待定死亡的重启 / 重连重建、重放不重算」本次没有运行覆盖——缺从事件流重建（或真宿主重启）后的等价断言 |
| 16 | 通过 | 本票「缺陷②复核结论」可达、已修（`edd0273`）；回归 `TriggerRequestVoidTests` 两条本次全绿；结论已写进票据 |

**行 12 的先红（修复时先复现；临时用例内容：5 席 `pit-hag` / `clockmaker` / `artist` / `no-dashii` / `klutz` → 走完首夜 → 第二夜选 `seat:3|evil-twin` → 期望 `AwaitingDecision != null` 且事件流出现 `evil-twin.pair`）**：

```text
dotnet test tests/OpenClockTower.Integration.Tests --filter FullyQualifiedName~TempE15ProbeTests
→ 失败 1：期望：创造镜像双子后出现「选择对立双子」裁定；实际 AwaitingDecision=null。
  事件流末段：SlotQuotaElapsedEvent, SlotAdvancedEvent, SlotEnteredEvent, …（机器照常推进）
```

### E15 残余修复记录（2026-10-05，等待 E16 重判）

判据同 E15：**通过** = 本次运行有与该行期望直接对应的断言。以下每条都给出落点与运行证据。

- **行 12（先红 → 转绿）**：`PitHagNightAction` 创造出镜像双子时开「选择对立双子」裁定（`BuildPostChoiceDecision`；候选 = 与新双子阵营相对、除新双子外的玩家，已死亡玩家同样在列）；`Resolve` 校验裁定确为合法候选后落 `evil-twin.pair` 配对效果与双向互认，与首夜同源（共享 `EvilTwinPairing`）。边界：所选角色已在场 → 不开裁定、无事发生；裁定不在候选里 → 显式抛错（不静默落坏配对）。证据：集成 `PitHagNightHostTests.PitHagCreatesEvilTwin_OpensPairingDecision_AndPersistsPairing`（先红：16 秒内裁定点从未出现；转绿：候选只含 1 / 4 号、效果 Source=3 / Target=4、3 与 4 号各自收到互认）；规则 `PitHagNightActionTests.TransformToEvilTwin_OpensPairingDecision_AndAppliesPairing` / `TransformToEvilTwinInPlay_DoesNotOpenPairingDecision` / `TransformToEvilTwin_IllegalPairing_Throws`。
- **行 3**：集成 `PitHagResidueHostTests.PitHagPicksCharacterInPlay_NoStateChange_ButAbilityUseIsEffective`——选在场角色 → 无状态变化，但 `LastResolution` 生效、使用账 `Effective=true`（不是「未生效」）。
- **行 4**：集成 `PitHagResidueHostTests.PitHagTargetsDeadPlayer_ChangesCharacterOnly`（已死亡目标照变、生死维度不动）与 `PitHagSourceLosesAbility_WindowStaysOpen`（麻脸巫婆自变后窗口仍在、追加死亡仍受理、窗口按计划收口）。
- **行 13**：规则组合 `PitHagNoDashiiCombinationTests.PitHagCreatesNoDashii_PoisonsNeighbours_AndMovesOldPoisonOnCharacterChange`——创造诺-达鲺 → 邻近 2 / 4 号中毒；中毒的 2 号变成非镇民 → 旧中毒解除、6 号按新位置接上、4 号沿用原效果。
- **行 15**：集成 `PitHagResidueHostTests.WindowWithDeferredSurvivesRestart_AndReplayDoesNotRecompute`——真重启（同一 SQLite）：窗口与待定死亡等价恢复、事件流不追加（重放不重算）、重启后仍可继续裁定、玩家重连拿到重建后的挂起请求。
- **行 11**：集成 `PitHagResidueHostTests.CommitPipelineFillsPreviousCharacter_DemonBecomesNonDemon_GoodWins`——说书人上报只报新值，提交管线补全 `PreviousCharacter=no-dashii`（直接读回事件流断言），胜负求值据此判善良获胜（R-0029）。

提交：`87901f4`（行 12 实现 + 规则 / 集成用例）、`e7382e7`（行 13 组合）、`c972dba`（行 3 / 4 / 11 / 15）。冻结版门禁：全解决方案 475 项 + `npm run gate`（90 项）全绿。

## E16 验收判定（2026-10-06）

冻结版本 `main` @ `2f8c4e6`（跑批期间工作树干净、未改产品代码）。判据同 E15：
**通过** = 本次运行有与该行期望直接对应的断言；其余 11 行（1 / 2 / 5–10 / 14 / 16）的 E15 结论累计有效。

六装置同批取证档（`--quota 2 --screenshots-all`；真宿主 + 真 Chromium + 真 SQLite）：

| 装置 | 本次结果 |
|---|---|
| 主装置 `verify-storyteller-panel.mjs` | 162 项 · 0 失败（35 张截图） |
| 麻脸巫婆之夜 `verify-pit-hag.mjs` | **34 项 · 0 失败**（**8 张截图**：本批为行 12 补第三夜段，28 → 34） |
| 胜负链路 `verify-winloss.mjs` | 20 项 · 0 失败 |
| 女巫链路 `verify-witch.mjs` | 28 项 · 0 失败 |
| 处罚处决 `verify-madness.mjs` | 28 项 · 0 失败 |
| 零信任 `verify-zero-trust.mjs` | 44 项 · 0 失败 |

冻结版门禁：`dotnet build` 0 警告 / 0 错误；`dotnet test` **475 通过 / 0 失败**；`dotnet format --verify-no-changes` 退出码 0；
`npm run gate` **90 项** + typecheck / build 全绿。本批 6 行对应用例过滤复跑：Integration `~PitHag` **7/7**、Rules `~PitHag` **14/14**。

| # | 结论 | 本次运行的证据 |
|---|---|---|
| 3 | 通过 | 集成 `PitHagResidueHostTests.PitHagPicksCharacterInPlay_NoStateChange_ButAbilityUseIsEffective`——选在场角色 → 无状态变化，但 `LastResolution` 生效、使用账 `Effective=true`（不是「未生效」） |
| 4 | 通过 | 集成 `PitHagResidueHostTests.PitHagTargetsDeadPlayer_ChangesCharacterOnly`（已死亡目标照变、生死维度不动）+ `PitHagSourceLosesAbility_WindowStaysOpen`（来源自变后窗口仍有效、追加死亡仍受理、窗口按计划收口） |
| 11 | 通过 | 集成 `PitHagResidueHostTests.CommitPipelineFillsPreviousCharacter_DemonBecomesNonDemon_GoodWins`——说书人上报只报新值，提交管线补全 `PreviousCharacter=no-dashii`（直接读回事件流断言），胜负求值据此判善良获胜（R-0029） |
| 12 | 通过 | **装置新段（界面级）**：第三夜麻脸巫婆把 3 号（第二夜创造的涡流）再变成镜像双子 → 真玩家页面两维选择（`pithag-06`）→ 说书人面板开出「选择对立双子」裁定、候选恰为 1 / 4 号邪恶玩家（`pithag-07`）→ 选定 4 号被受理（序号 158）→ 3 号牌面「镜像双子 爪牙 3 号 **善良** 存活」、4 号带「镜像双子·生效中」配对标记（`pithag-08`）→ 裁定控件结清。集成 `PitHagNightHostTests.PitHagCreatesEvilTwin_OpensPairingDecision_AndPersistsPairing`（候选集合、`evil-twin.pair` Source=3 / Target=4、3 与 4 号各自收到互认）+ 规则 `TransformToEvilTwin_OpensPairingDecision_AndAppliesPairing` / `TransformToEvilTwinInPlay_DoesNotOpenPairingDecision` / `TransformToEvilTwin_IllegalPairing_Throws` |
| 13 | 通过 | 规则 `PitHagNoDashiiCombinationTests.PitHagCreatesNoDashii_PoisonsNeighbours_AndMovesOldPoisonOnCharacterChange`——创造诺-达鲺 → 邻近 2 / 4 号中毒；中毒的 2 号变成非镇民 → 旧中毒解除、6 号按新位置接上、4 号沿用原效果 |
| 15 | 通过 | 集成 `PitHagResidueHostTests.WindowWithDeferredSurvivesRestart_AndReplayDoesNotRecompute`——真重启（同一 SQLite）：窗口与待定死亡等价恢复、事件流不追加（重放不重算）、重启后仍可继续裁定、玩家重连拿到重建后的挂起请求 |

**行 12 的界面级取证**：`tools/verify-pit-hag.mjs` 新增第三夜段（同夹具续走，麻脸巫婆把已是涡流的 3 号再变成镜像双子），
新增 6 项断言：开第三夜被受理 / 裁定开出（上下文含「对立双子」）/ 候选恰为 1 号与 4 号 / 选定 4 号被受理 /
3 号牌面变镜像双子 / 配对后裁定控件结清；无关玩家收包白名单同步补 `awaitingDecision` 与「对立双子」。
`pithag-06 / 07 / 08` 三张截图逐张复核，`pithag-01`–`05` 一并复核（`05` 为第二夜窗口收口）。截图在 `artifacts/web/`（gitignored，可重生成）。

**残余与口径（本批未变）**：
- **R-0029 仍为 `Open`**（平台口径已定 + 代码注释引用 + 回归齐），等官方条文确证；不阻塞本票。
- 行 12 候选口径：与首夜同源 =「阵营相对、含已死亡玩家」；《镜像双子》提示里的「最后一夜可选恶魔」通过"不看生死"覆盖，
  **同阵营的善良恶魔不在候选内**（首夜同样如此，未另行核对官方条文）。

## 决定与依据

- 角色变更 = 单一维度变化（不带阵营）：百科《重要细节》二 · 2026-10-01 抓取；架构 §2.1。
- 槽位进入时求值：D-0013 §1 的「照走配额」不变，只是把「谁在行动」的求值时点推迟到进入时。
- 死亡裁量的时间边界与归因：百科《麻脸巫婆》规则细节 1 · 2026-10-01 抓取。
- 待定死亡默认生效：**平台口径**（窗口关闭时未裁定的待定死亡，按恶魔攻击的自然结果生效并显式写入事件）；
  登记为 R-0030 第 3 条。
- 新裁定（本票新增，`docs/standard/rulings.md`）：
  - **R-0029** 恶魔角色「从有到无」的胜负口径；
  - **R-0030** 麻脸巫婆之夜的死亡裁量在平台上的形态（窗口、待定、追加、默认生效）。
