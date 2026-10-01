# 说书人上帝视角：每步状态信息、归因与最终计算结论

- Status: InProgress
- Priority: High
- Depends on: 自动步骤机与操作请求（review 中）；**结算引擎与游戏状态（未建；已立票
  `in-progress/settlement-engine.md`）**——它既是状态账的写入方，也是"能力是否生效 / 信息真假"的输入来源

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
| 1 | 某玩家中毒后其夜间能力结算 | 摘要显示"中毒"、能力**未生效**、归因到施加者 | 《重要细节》三-3 | 账本侧可查询（状态 + 归因）；"能力未生效"缺结算引擎 |
| 2 | 玩家同时中毒且醉酒 | 两种状态并存、**不相互抵消**；能力未生效 | 同上 | 账本侧可查询（两个维度各自独立留痕）；"能力未生效"缺结算引擎 |
| 3 | 中毒玩家执行信息类能力 | 显示"信息由说书人决定（可能错误）"，**不自动生成**真假结论 | D-0002 + 三-3 | 未开始（缺能力类型与结算引擎） |
| 4 | 对中毒玩家使用的能力 | 正常生效（如查中毒的恶魔仍得正确阵营） | 三-3 | 未开始（缺结算引擎） |
| 5 | 中毒来源死亡 / 能力终止 | 状态解除，摘要显示解除原因与对应事件 | 三-3 一-3 | 账本侧完成：终止 + 原因分类 + 说明；**维度解除**要由写入方再报一条状态变化（D-0015） |
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

## 尚未完成（按依赖排序）

1. **效果事件没有产生方**：已立票 `in-progress/settlement-engine.md`——契约 + 折叠 + 查询已就位，
   引擎落地后由它产出同样的事件，账本不用改。
2. **"能力是否生效"（矩阵 1 / 2 / 4）与"信息真假由说书人决定"（矩阵 3）**：并入
   `in-progress/settlement-engine.md`；账本已给出它的现成输入：`OperativeEffectsOn`、逐维度归因、效果终止原因。
3. **疯狂要求的产生方**同样在裁定点链路：`MadnessRequirementIssuedEvent` 已能被折叠，
   但"裁定点 → 结构化疯狂要求"的那一步随说书人裁定面交付。
4. **维度解除只做到账本侧**：属 `in-progress/settlement-engine.md` 第 7 条——效果终止有据可查，
   但把目标的中毒改回健康必须由写入方再报一条状态变化（D-0015 明写下来的边界，不是遗漏）。
5. **重建报告不覆盖状态账**：已立票 `todo/rebuild-state-ledger-comparison.md`
   （状态账与步骤机同源折叠，需补 `GameState` 等价比较器）。
6. **说书人面板呈现**（第 5 条）随 `web/` 前端票据；本轮只做到"视图里真的有数据"。
7. **两条账本尚未安家**：并入 `in-progress/settlement-engine.md` 第 5 条——`AbilityUseLedger` /
   `MalfunctionLedger` 已在 Kernel 里，引擎落地时把它们挂进 `GameState`，不要另起状态仓。
8. **维度 → 效果没有结构化链接**（复核 M1 / M2）：并入 `in-progress/settlement-engine.md` 第 6 条——
   状态变化事件要能携带 `EffectId`，说书人视图才能回答"这一格中毒是哪条效果造成的"、
   并提示"这一格已经过期"（来源效果已终止但维度还写着中毒）。
9. **恢复失败后视图缺降级标记**（复核 M11）：已立票 `todo/room-health-degradation-flag.md`——
   事件流损坏时房间以空账启动（记 Critical），视图必须能区分"没数据"和"数据丢了"。
10. **视图暴露的内部集合可被强转篡改**（复核 L6）：`StorytellerView.Seats` / `PersistentEffects`
    名义上只读，实际是 `List` / 数组；要改成不可变集合或只读包装。

## 决定与依据

- 说书人裁量优先：`docs/decisions/active.md` D-0002
- 状态账边界（只记事实与归因，不替引擎翻转维度）：`docs/decisions/active.md` D-0015
- 六维度独立：`docs/architecture/current.md` §2.1
- 状态账与效果归因链的折叠规则：`docs/architecture/current.md` §2.8
- 疯狂不由引擎判定：`docs/standard/rulings.md` R-0003
- 百科引用口径：`docs/standard/sources.md`；《重要细节》三-3
