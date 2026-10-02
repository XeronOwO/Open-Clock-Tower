# 说书人上帝视角：每步状态信息、归因与最终计算结论

- Status: Review
- Priority: High
- Depends on: 自动步骤机与操作请求（review 中）；**结算引擎与游戏状态（已落地，
  `review/settlement-engine.md`）**——它既是状态账的写入方，也是"能力是否生效 / 信息真假"的输入来源

## 要解决的问题

说书人是上帝视角：他需要在**每一步**看到"这一步为什么是这样"，而不是自己翻日志与事件流：

- 当前步骤的玩家身上发生了什么状态变化（中毒 / 醉酒 / 死亡 / 角色变更…）；
- 变化**由谁、因什么能力或效果**导致；解除状态也一样要能归因（例如"投毒者死亡，因此解毒"）；
- 该玩家的**最终计算结果**：例如同时中毒与醉酒时，能力失效、信息类结果由说书人裁量。

（需求方补充要求。规则口径必须先核对，见下。）

## 规则口径（2026-10-01 已核对，不许凭记忆）

百科《重要细节》三-3：

- "在任意时间点，一名玩家一定会处于清醒或醉酒两种状态之一，也一定会处于中毒或健康两种状态之一"；
- "**醉酒与中毒状态不会互相抵消**"——同时中毒并醉酒 = 同时具有两种状态；
- "醉酒或中毒的玩家会失去能力"（每局限一次的能力会被浪费）；
- "醉酒或中毒的玩家，其角色能力**可能**会获取到错误的信息"——**可以**给错误信息、通常也应该给，
  但这是**说书人裁量**（D-0002：平台不替说书人拍板）；
- "对醉酒或中毒的玩家所使用的能力仍然会正常生效"（别人查他，结果正确）。

因此"最终计算结果"在本平台上的正确形态是**两件事分开呈现**：
`能力是否正常生效（可计算）` + `信息真假（说书人裁定，平台只提示"可能错误"）`。

## 要做的事

1. **状态账（GameState）**：六个维度（角色 / 阵营 / 生死 / 醉酒 / 中毒 / 疯狂）成为可查询的当前态，
   写入方是结算引擎；每次变化产出 `SeatStateChangedEvent`（含 `Reason` / `CausedBy`）。
2. **效果归因链**：持续型 / 即时型效果记录"谁施加、作用于谁、何时终止、因何终止"，
   能回答"当前步骤玩家中毒是谁造成的""因为什么解毒"。
3. **每步摘要（StepDigest）**：说书人面板显示当前槽位的完整上下文：
   行动者、请求上下文、其身上全部生效状态及来源、能力是否生效、无合法选项时的行为。
4. **最终计算结果**：按能力类型给出"生效 / 失效（中毒、醉酒、死亡）"；信息类给出
   "说书人自行决定（提示：可能错误）"，**禁止引擎自动判定信息真假**。
5. 说书人面板呈现（随 `web/` 前端票据一起做）。

## 验收矩阵

| # | 场景 | 期望 | 依据 | 现状 |
|---|---|---|---|---|
| 1 | 某玩家中毒后其夜间能力结算 | 摘要显示"中毒"、能力**未生效**、归因到施加者 | 《重要细节》三-3 | 引擎侧已就位（`AbilityResolvedEvent` 生效判定 + 逐维度归因；真宿主链路见 `review/settlement-engine.md` 矩阵 1）；面板摘要未做 |
| 2 | 玩家同时中毒且醉酒 | 两种状态并存、**不相互抵消**；能力未生效 | 同上 | 引擎侧已就位（两维各自留痕；同时中毒且醉酒记 `Open`，R-0004 待核对）；面板摘要未做 |
| 3 | 中毒玩家执行信息类能力 | 显示"信息由说书人决定（可能错误）"，**不自动生成**真假结论 | D-0002 + 三-3 | 引擎侧已就位（`InformationResultIssuedEvent` + 裁定点上下文）；面板与玩家端展示未做 |
| 4 | 对中毒玩家使用的能力 | 正常生效（如查中毒的恶魔仍得正确阵营） | 三-3 | 引擎侧已就位（真宿主链路见 `review/settlement-engine.md` 矩阵 4） |
| 5 | 中毒来源死亡 / 能力终止 | 状态解除，摘要显示解除原因与对应事件 | 三-3 一-3 | 已闭环：终止 + 原因分类 + **维度解除事件**（`DimensionEffectReconciler`，带 `EffectId` 归因） |
| 6 | 上游状态变化使请求失去意义 | 与既有自动作废联动，摘要写明是哪一条依赖不满足 | `review/operation-request-step-machine.md` 第 5 条 | 已交付（自动作废 + 作废说明，见该票据行 8） |

## 已落地的第一片（2026-10-01，随「自动步骤机与操作请求」）

- `SeatStateChangedEvent`（内核）：**谁、因何原因、变成什么样**；只记录本次真正观测到的维度
  （可空维度，避免"只看到生死却被迫填角色"造成误判）。
- 说书人视图新增：`CurrentSlotActor`（当前步骤的玩家）、`CurrentSlotContext`（为什么需要这个选择）、
  `RecentSeatChanges`（最近的状态变化及原因 / 归因）。

## 已落地的第二片（2026-10-02，状态账 + 效果归因链）

**内核**

- `GameState`：`Seats`（按席位号升序）/ `PersistentEffects` / `InstantaneousEffects`；
  纯数据，不依赖字典枚举顺序（D-0008）。
- `SeatStateEntry`：五个维度各一条 `StateFact<T>`（值 + 原因 + 导致方），疯狂要求单独成列
  （R-0003：引擎不判定疯狂）；维度为 null = **尚未观测**，不是默认值。
- `PersistentEffect` 补齐归因：`Ability`（哪个能力）、`Target`（作用于谁）、
  `Termination`（原因分类 + 说明 + 导致方）；无参 `Terminate()` 被删除并由门禁锁死。
- `InstantaneousEffect` 同样补齐 `Ability` / `Target`（已生效即不回滚，故没有终止字段）。
- 新事件：`PersistentEffectAppliedEvent` / `PersistentEffectTerminatedEvent` /
  `InstantaneousEffectAppliedEvent` / `MadnessRequirementIssuedEvent`；
  `SeatStateChangedEvent` 扩到五个维度（阵营、醉酒、中毒也带上）。
- `GameStateMachine`（折叠，纯计算），规则见 `docs/architecture/current.md` §2.8。要点——
  来源死亡 / 来源换角色 → 其持续型效果**立即终止且不可逆**；来源醉酒中毒 → **只挂起不终止**，
  恢复后继续生效（同一效果，不是重新施加）；顺序损坏（终止不存在 / 重复终止 / 重复施加 /
  空状态变化）**显式抛错**。

**应用层与宿主**

- `GameSession` 在**提交 / 恢复 / 重建**三处把同一批事件同时折进步骤机与状态账——
  同源派生，不允许只折一个。
- `StorytellerView` 新增 `Seats` / `PersistentEffects` / `InstantaneousEffects`；
  `StorytellerViewDto` + `SeatStateDto` / `SeatStateFactDto` / `EffectDto` 走同一套映射。
- `GameHub.ReportSeatState` 支持五维度上报（生死 / 角色 / 阵营 / 醉酒 / 中毒），非法枚举当场拒绝。
- 玩家投影与玩家事件**没有动**：状态账只出现在说书人视角（D-0012 §4.3）。

**门禁与测试（本轮真实输出）**

| 门禁 | 结果 |
|---|---|
| `dotnet build OpenClockTower.slnx` | 0 警告 0 错误 |
| `dotnet test OpenClockTower.slnx` | 123/123 通过（内核 89 + 门禁 10 + 集成 24） |
| `dotnet format OpenClockTower.slnx --verify-no-changes` | exit 0 |
| 新门禁先红 · 效果终止带原因 | 临时植入 `Terminate( )` → `EffectTerminationGateTests` FAIL，报告指向 `src\OpenClockTower.Kernel\PersistentEffect.cs → Terminate()`；删除后复绿（改成正则后连"空白绕过"也拦得住） |
| 新门禁先红 · 单文件 ≤ 600 行 | 临时把 `StepMachineStateComparer.cs` 填到 616 行 → `SourceFileLengthGateTests` FAIL 并点名该文件；还原后复绿 |

**运行时证据（真宿主 + 真 SignalR + 真 SQLite）**：`StateLedgerHostTests`

1. 说书人上报 3 号的五个维度（生死 / 角色 / 阵营 / 醉酒 / 中毒，归因到 1 号）→
   说书人视图 `Seats` 里 3 号出现 5 条事实，中毒那条带原因与 `CausedBy=1`；
   同一轮**反方向**断言：1 号重连包的 JSON 里没有 `Poison` / `Facts` / `CausedBy`（信息隔离）。
2. 换一个宿主进程、同一个库重启后，状态账按事件流重放恢复，2 号的醉酒事实仍在、归因不变。
3. 内核侧 20 条新用例覆盖：逐维度归因不串味、未观测即未知（`IsOperative` 返回 null 且不猜）、
   来源死亡 / 换角色终止、来源醉酒挂起与恢复、即时型不回滚、显式终止、四类损坏流显式抛错。

### 独立对抗性复核（2026-10-02）

按项目要求，把冻结版本交给独立子代理做**只读**对抗性复核，其结论是"不接受为完整交付"，
报 3 高 / 11 中 / 9 低。本轮已修掉的（每条都补了回归或用例）：

| 复核发现 | 处置 |
|---|---|
| 高 H1：来源角色**从未被观测过**时，"换角色"判定静默漏判，早该失效的效果继续被算成生效 | 效果改为记录**施加时的来源角色**（`PersistentEffect.SourceCharacter`），判据不再依赖"上一次观测值"；新增两条回归：首次观测到就是新角色 → 终止，观测到与记录一致 → 不终止 |
| 高 H2："醉酒 / 中毒时效果是终止还是挂起"是百科同段自相矛盾，未登记裁定 | 新增 `rulings.md` **R-0012**（Contested，取挂起），并在 `PersistentEffect`、`EffectLifecycleTests` 注释里引用裁定号 |
| 高 H3：折账发生在 `CommitAsync` **之后**，折失败会留下"事件已落库、账没折"，重投还被当成 Duplicate 掩盖分叉 | 改为**先折副本、再提交**：折不动 = 整条命令失败且不落库；提交成功后才把结果赋回 |
| 中 M4：疯狂要求重复签发被静默追加，与效果路径的失败姿态不一致 | 改为显式抛错（按 `IssuedBy + Seat + ProveToBe` 去重），补回归用例 |
| 中 M5：14 个"与账无关"的直通分支没有任何账级断言 | 新增内核用例：跑一条完整步骤机事件流（阶段 / 槽位 / 请求 / 推进 / 接管 / 交还）折进账，断言三个集合为空 |
| 中 M7：`GameSession` 789 行、`StepMachine` 612 行越过 600 行硬阈值，且仓库没有这条门禁 | 拆出 4 个内聚类型（`SessionTrackers` / `PlayerEventProjection` / `KernelInputMapper` / `SeatDependencyCheck`）→ 581 / 587 行；新增第 10 条门禁「单文件 ≤ 600 行」并先红后绿 |
| 中 M8：状态账"不下发玩家"只有一条运行时断言，没有门禁 | 扩展 `PlayerProjectionLeakGateTests` 令牌表（`Ledger` / `Facts` / `CausedBy` / `Effects` / `Termination` / `Madness` / `Seats`），玩家面向的 6 个文件全部纳入 |
| 中 M9 与低 L1 / L2 / L3：引用区号错（二-3 被写成三-3、流放条写成 一-3）、五 / 六维度措辞不一、事件条数过期 | 逐条改正：`PersistentEffect` 与 `EffectLifecycleTests` 的区号、`rulings.md` R-0007 改 一-4、术语与架构统一成"五个可观测维度 + 疯狂要求另列"、事件数按实测改为 19 |
| 中 M10：即时型"不回滚"是从沉默反推出来的规则断言 | 新增 `rulings.md` **R-0013**（Open，暂取不回滚），`InstantaneousEffect` 注释引用裁定号 |
| 低 L4：终止门禁用纯文本匹配，`Terminate( )` 可绕过 | 改成正则 `Terminate\s*\(\s*\)`，并用"带空格"的违规重做先红后绿 |
| 低 L5：`ReportSeatState` 接受数字枚举名（`"0"` → Alive） | 拒绝数字开头的取值，只认枚举名 |
| 低 L7：重连包读取没进会话锁，并发下可能拼出撕裂包 | 重连包读取纳入同一把锁 |
| 低 L8：若干次要分支没有覆盖 | 补：同一条事件里"死亡 + 换角色"的优先序、`LiveEffectsOn` 的终止过滤、疯狂重复签发 |
| 低 L9：恢复只捕 `InvalidOperationException`，其它异常会把旧状态继续当现状服务 | 改为捕全部非取消异常，并停在空状态 |

**复核中"已核对通过"的项**：五维正交与逐维度归因、来源死亡终止且不可逆、醉酒中毒挂起与恢复、
即时型不回滚、四类损坏流显式抛错、未知事件抛错、空流 = 空账、未观测不猜、
确定性（无字典枚举 / 时间 / 随机 / IO，`Fold ≡ 逐条 Apply` 有用例）、
信息隔离三条出口逐条走查、一文件一顶层类型、内核纯净。

## 已落地的第三片（2026-10-02，`web/` 起步 + 说书人上帝视角面板 + 真机会话）

**形态**（D-0018）：单 SPA 两套视图；说书人端默认入口，玩家端 `#player`。

- `web/`：Vue 3 + TS + Vite；连接层（`StorytellerGateway` / `PlayerGateway`）只做"连上、
  收视图、发命令"；`display/labels.ts` 提供角色与枚举中文文案（来源术语表 §9），
  `display/format.ts` 把服务端数据当不可信输入做防御性规范化（坏字段只降级该行，不白屏）。
- 面板呈现：每步摘要（行动者 / 槽位上下文 / 非行动槽位说明）、状态账（五维度 + 疯狂要求另列，
  逐条带来由、归因与**效果链接**）、最近状态变化时间线（原因 / 归因 / `EffectId` / 时刻）、
  效果归因链（含终止原因）、两本账与最近一次结算结论、裁定点（合法选项 + 自由决定）与
  卡点（代填 / 作废）、开局分配 / 状态上报 / 开夜 / 强推 / 接管 / 交还 / 重建。
- 门禁：`PlayerProjectionLeakGateTests` 新增一条——玩家端 `web/src/features/player` 与共享
  `web/src/services` 里出现说书人专属字段名（`seats` / `effects` / `causedBy` / `slotIndex` …）即失败。

**运行时证据（2026-10-02，真服务端 + 真浏览器 + 真 SQLite）**：`tools/verify-storyteller-panel.mjs`
（21 项断言全通过，退出码 0；截图落在 `artifacts/web/`，本轮已目视复核）

| 步骤 | 结果 |
|---|---|
| 真宿主 + Vite + Chromium，说书人票据从 SQLite 读出并加入 | 看板可见（截图 `01-joined`） |
| 面板分配 3 席（钟表匠 / 筑梦师 / 诺-达鲺） | 受理，状态账逐席出现角色（`02-assigned`） |
| 面板开夜（真实顺序表建表，13 槽位） | 受理，`FirstNight` 进入（`03-night-started`） |
| 服务端节拍器推送使槽位**自行**前进（无刷新） | 槽位 0 → 1（`04-advanced`） |
| 面板上报"2 号中毒，归因 3 号" | 受理；状态账出现 `中毒 | 归因 3 号 | standing:no-dashii.poison:3:2`；最近变化带原因与归因（`05-reported`） |
| 面板上报"1 号醉酒" | 受理；与 2 号中毒**并存**（两条独立事实，互不覆盖） |
| 效果链 | 两条 `standing:no-dashii.poison:3:N` 常驻效果，来源角色诺-达鲺、**生效中** |
| 控制台 / 宿主日志 | 无控制台错误、无未处理异常 |

**同轮修掉的缺陷（真机验证发现，不在纸面推演里）**

1. **预阶段状态观测不可写**：`ApplySeatStateCommand` 落在阶段闸 default 分支被
   `phase.not_started` 拒绝——说书人无法在开局设置阶段写观测（而开局分配可以）。
   修法：阶段闸与身份闸为它单开一条预阶段通路（`GameCommandDispatcher.DispatchPrePhaseSeatState`），
   事件形状与运行期一致；补两条集成回归（`PrePhaseSeatObservationTests`）。
   同轮确认口径：建表要求每一席都有角色（`plan.seat_unassigned`），"只分配一半"不是合法开局。
2. **门禁枚举类型写死 `.cs`**：`RepositoryLayout.EnumerateSourceFiles` 用 `*.cs` 找前端文件必然是空集，
   等于"扫了个空"的假绿。修法：新增 `EnumerateFiles(searchPattern, ...)`，`EnumerateSourceFiles` 转发到它。
3. **面板回执区随视图推送闪烁**：回执只在"有结果"时渲染，视图推送重渲染时位置漂移。
   修法：回执区常驻并带单调 `data-outcome-serial`（供真机验证判定"这是新回执"）。

### 独立对抗性复核（2026-10-02，第二次；只读子代理，限 25 文件 / 12 分钟）

结论"不接受为完整交付"，报 2 高 / 3 中 / 2 低。**本轮全部处理**（每条都有对应改动或票据）：

| 复核发现 | 处置 |
|---|---|
| 高 H1：玩家端把重连包的补齐事件写死成空数组整体丢弃 | 真消费：`normalizePlayerEvent` + `applyBundle`（缺口/倒退/不连续都出诊断且不前进序号），补 `playerGateway.spec.ts` 6 条用例 |
| 高 H2：契约镜像幽灵字段 `InformationResultDto.sequence`，并被当 Vue 列表 key（同能力两条信息同 key） | 删字段、改用索引 key；新增门禁 **`ContractMirrorGateTests`**（逐字段对账形状，已先红后绿：植入 `sequence` → FAIL 并点名） |
| 中 M3：呈现层用 `?? false` 把"缺数据"落成"服务端结论" | 复核建议改 `bool?`；实测生产者 `ProjectionMapper` 每条路径都显式赋值，改契约会连带 8 处既有断言且违背 wire 必填口径。**本轮不改契约**，改记为 `watchlist/settlement-conclusion-three-state.md`（三态口径另立票），并在 `format.ts` 注明这一格只在坏载荷下退化为 false |
| 中 M4：预阶段观测是绕过内核映射的第二条写账通路，四闸无席位合法性校验 | `CommandGatePipeline.CheckLegality` 增加 `ApplySeatStateCommand → CheckSeatExists`（与开局分配同一把尺子：`legality.seat_unknown`） |
| 中 M5：取证脚本两条自我欺骗断言（整页 innerText 会被常驻表单标签命中；无条件 `pass:true` 计入 21 项） | 断言改为落在 `状态账` / `最近状态变化` / `效果归因链` 面板内，并新增"五张截图都落盘"的真实断言；无条件的"截图目录"条目删除 |
| 低 L6：隔离门禁只扫 `features/player` + `services`，共享层未扫 | 新增门禁 **`PlayerSources_ImportOnlyPlayerSideModules`**：玩家的每条 import 必须落在显式允许清单里（`@/display/**`、`@/services/player*` 等），且不得出现说书人专属类型名。先红后绿时**发现原实现是假绿**（`SourceText` 按 C# 写、会把 `.vue` 模板里成对的 `{ }` 当块注释吞掉整个 `<script>`），已改为只取 `<script setup>` 块 + 只去块注释 |
| 低 L7：脚本硬耦合 `bin/Release/net10.0` 与 `Games.StorytellerTicket` | 在 `web/AGENTS.md` §3.1 显式列出外部耦合与失败表现（不假装可移植） |

**复核同时确认通过的**：玩家视图字段面与 `PlayerViewDto` 逐字段一致且无进度字段、
`connectionState.ts` 无夹带、其余 13 个契约 DTO 字段名与可空性一致、
玩家不能上报状态（身份闸）、门禁不会扫空（`Assert.NotEmpty`）、脚本无"等不到就放过"、
收尾只杀自拉起的单进程（不带 `/T`）。

## 验收矩阵逐行结论（2026-10-02 真机会话 E1）

| # | 结论 | 本次证据 / 缺什么 |
|---|---|---|
| 1 | **部分通过** | 引擎侧：诺-达鲺常驻中毒对 1 / 2 号生效并给出效果链接（截图 `05-reported`）；面板"每步摘要"已显示状态账、效果链与最近变化，但尚未把"能力是否生效"按槽位直接写在摘要行——待补 |
| 2 | **部分通过** | 引擎侧两维独立：1 号状态账同时有"醉酒（说书人裁定：本夜醉酒）"与"中毒（持续型效果）"，互不覆盖；"同时中毒**且**醉酒对同一席位的能力结算"仍记 `Open`（R-0004） |
| 3 | **无法判定** | 面板尚未呈现信息类结果与"信息可能错误、由说书人决定"的提示；玩家端界面见票据 4 |
| 4 | **部分通过** | 常驻中毒对目标席位的状态账写入正常；"被使用的能力正常生效（如查中毒恶魔仍得正确阵营）"需要一次能力结算，等更多角色契约 |
| 5 | **通过（账本级）** | 效果终止带原因与**维度解除事件**已闭环（`DimensionEffectReconciler`，上一片证据）；面板已呈现效果链生效状态 |
| 6 | **本会话未覆盖** | 上游状态变化 → 请求自动作废与作废说明：需要存在挂起请求的槽位；本会话只展示了"当前没有卡住的请求"与作废原因分类的下拉 |

## 尚未完成（按依赖排序）

> 2026-10-02 第二次更新：`web/` 面板已落地，下列 1 / 2 / 4 / 6 / 7 / 8 的**引擎侧与面板侧**
> 大部分已就位（生效判定、信息结果、两本账、维度链接与解除都在事件流、状态账与面板里）；
> 本票剩余的是**呈现细化**与**玩家端**（见 11 / 12）。

1. **效果事件没有产生方**：已落地（`review/settlement-engine.md`）——面板的效果链已按会话实测显示常驻效果与来源角色。
2. **"能力是否生效"（矩阵 1 / 2 / 4）**：引擎侧已落地、面板有"最近一次结算"与失效账本；
   剩余：把结论直接写进**当前槽位的每步摘要行**（票据第 3 条的原始诉求），见 11。
   **"信息真假由说书人决定"（矩阵 3）**：引擎侧已落地（`InformationResultIssuedEvent` + 裁定点上下文）；
   面板与玩家端呈现见 11 / 12。
3. **疯狂要求的产生方**同样在裁定点链路：`MadnessRequirementIssuedEvent` 已能被折叠，
   但"裁定点 → 结构化疯狂要求"的那一步随说书人裁定面交付。
4. **维度解除只做到账本侧**：属 `review/settlement-engine.md` 第 7 条——效果终止有据可查，
   但把目标的中毒改回健康必须由写入方再报一条状态变化（D-0015 明写下来的边界，不是遗漏）。
5. **重建报告不覆盖状态账**：已立票 `todo/rebuild-state-ledger-comparison.md`
   （状态账与步骤机同源折叠，需补 `GameState` 等价比较器）。
6. **说书人面板呈现**：已落地本轮（见"已落地的第三片"与本文件验收矩阵）。
7. **两条账本尚未安家**：已落地——`AbilityUseLedger` / `MalfunctionLedger` 挂在 `GameState` 上，
   面板"账本与结算结论"一栏实时显示（空则显示空态）。
8. **维度 → 效果没有结构化链接**（复核 M1 / M2）：已落地——状态账每格带 `EffectId`，
   会话实测显示 `standing:no-dashii.poison:3:2`，面板同时给出"（链接由 无迁移而来）"。
9. **恢复失败后视图缺降级标记**（复核 M11）：已立票 `todo/room-health-degradation-flag.md`——
   事件流损坏时房间以空账启动（记 Critical），视图必须能区分"没数据"和"数据丢了"。
10. **视图暴露的内部集合可被强转篡改**（复核 L6）：`StorytellerView.Seats` / `PersistentEffects`
    名义上只读，实际是 `List` / 数组；要改成不可变集合或只读包装。
11. **每步摘要还需收口**（本轮残余）：`StepDigest` 目前只呈现行动者与槽位上下文；
    要按票据第 3 条补上"本槽位行动者身上全部生效状态及来源 + 能力是否生效 + 无合法选项时的行为"。
12. **玩家端界面**：`web/` 已有玩家视图骨架（`#player`：加入、收请求、提交、看自己的信息类结果），
    但它没有进本轮验证矩阵，也没有与说书人端同批复验——矩阵 3 与步骤机票行 16 依赖它。

## 决定与依据

- 说书人裁量优先：`docs/decisions/active.md` D-0002
- 状态账边界（只记事实与归因，不替引擎翻转维度）：`docs/decisions/active.md` D-0015
- 六维度独立：`docs/architecture/current.md` §2.1
- 状态账与效果归因链的折叠规则：`docs/architecture/current.md` §2.8
- 疯狂不由引擎判定：`docs/standard/rulings.md` R-0003
- `web/` 形态与前端边界：`docs/decisions/active.md` D-0018、`web/AGENTS.md`
- 百科引用口径：`docs/standard/sources.md`；《重要细节》三-3
