# 内核领域模型与六状态不变量

- Status: Done
- Priority: High
- Depends on: 建解决方案、项目骨架与门禁工程

## 要解决的问题

规则内核最重要的一条不变量——**状态属于玩家，不属于角色**——现在只写在文档里，
没有任何代码或测试在守它。而这条一旦被破坏，后果是隐蔽且连锁的：
一次"角色变了顺手清个醉酒"的写法，会让某局游戏在三十步之后给出错误信息，
而现场没人能定位到是哪一步坏的。

## 要做的事

在 `OpenClockTower.Kernel` 里建立领域模型，**只做模型与不变量，不做结算**：

1. **六个正交状态**：`Character` / `Alignment` / `Life` / `Drunk` / `Poisoned`，加上
   只由裁定写入的 `MadnessRequirement`（依据 `rulings.md` R-0003：引擎不判定疯狂）。
   它们**之间不得存在任何联合约束**。
2. **效果生命周期**：区分 `InstantaneousEffect` 与 `PersistentEffect`，
   实现"来源失效即终止、来源恢复即继续"的语义。
3. **两本账**（本项目最易做错的地方）：
   - `AbilityUseLedger`：使用过 ≠ 生效过。醉酒/中毒期间使用一次性能力 = 已浪费。
   - `MalfunctionLedger`：记录每次"能力未正常生效"及其原因分类 `MalfunctionKind`。
     口径未定前，未核对的路径标 `Open` 并引用 `rulings.md` R-0004。
4. **裁定点契约** `DecisionPoint`：含 `Id` / `Context` / `Options` / `Preview` /
   **`OnNoOption`**（依据 `rulings.md` R-0009：无合法选项时必须显式声明行为）。
5. 把这五条全部写成**会失败的门禁测试**。

## 验收矩阵

| # | 场景 | 期望 | 依据 |
|---|---|---|---|
| 1 | 玩家醉酒 → 改变角色 | **仍然醉酒** | 百科《重要细节》三："因其他角色能力而醉酒的玩家角色发生了变化，不会让玩家因此解除醉酒" |
| 2 | 玩家中毒 → 改变阵营 | 仍然中毒 | 同上（"状态与玩家绑定，而不与角色绑定"） |
| 3 | 持续型效果的来源死亡 | 效果**立即终止** | 百科《重要细节》三-3 |
| 4 | 持续型效果的来源醉酒后恢复清醒 | 效果**继续生效**（不是重新施加） | 同上 |
| 5 | 一次性能力在中毒期间被使用 | 记为**已使用且已浪费**，恢复后不可再用 | 百科《重要细节》三-3 |
| 6 | 一次性能力在中毒期间被使用 | `AbilityUseLedger` 显示 used=true、effective=false | 同上 |
| 7 | 即时型效果已生效后来源中毒 | **不回滚** | 百科《重要细节》三-3 |
| 8 | 一次失败的能力结算 | `MalfunctionLedger` 有一条带原因分类的记录 | `rulings.md` R-0004 |
| 9 | 构造一个"无合法选项"的裁定点 | 返回 `OnNoOption` 声明的行为，**不抛异常** | `rulings.md` R-0009 |
| 10 | 手工加入一条"角色变更时清除醉酒"的联合约束 | 不变量门禁测试**失败** | 本票据核心 |

> 第 10 行必须**先见红**：它是这整个票据存在的理由。

## 决定与依据

- 六状态正交与其百科出处：`docs/architecture/current.md` §2.1
- 效果生命周期与两本账：`docs/architecture/current.md` §2.2
- 内核确定性（禁止时间/随机/IO）：`docs/decisions/active.md` D-0008
- 疯狂不作为引擎状态：`docs/standard/rulings.md` R-0003
- 「能力未正常生效」口径未定：`docs/standard/rulings.md` R-0004

## 备注

本票据**不实现任何角色**。舞蛇人、洗脑师、麻脸巫婆等具体能力在后续票据里做，
它们必须建立在本票据的账本与裁定点契约之上。

## 已落地（2026-10-01）

`Kernel` 新增 16 个领域类型（一文件一顶层类型）：

- 身份与六状态：`SeatId`；`MadnessRequirement`（只由裁定写入、以裁定点溯源，R-0003）。
- 效果生命周期：`EffectId` / `InstantaneousEffect`（已生效不回滚）/ `PersistentEffect`
  （来源醉酒中毒→挂起、恢复→继续；来源死亡→**不可逆终止**）。
- 两本账：`AbilityId` / `AbilityUse` + `AbilityUseLedger`（used ≠ effective）；
  `MalfunctionKind` / `Malfunction` + `MalfunctionLedger`（含 `Open` 待核对清单，R-0004）。
- 裁定点契约：`DecisionPointId` / `DecisionOption` / `NoOptionBehavior` / `DecisionPointOutcome` /
  `DecisionPoint`（`OnNoOption` 必填；无合法选项**不抛异常**，R-0009）。

测试与门禁：

- `tests/OpenClockTower.Kernel.Tests` 新增 5 个测试文件并扩展 `SeatStateTests`（共 35 条，含新 `SeatFixture` 收敛重复构造）。
- 新增规范门禁 `tests/OpenClockTower.NormativeGates.Tests/SeatStateIndependenceGateTests`：
  `SeatState` 五维必须保持纯自动属性——自定义访问器正是"把两个维度耦合起来"的入口。

文档：`terminology.md` 登记 `seat` / `effect` / `ability` / `madness-requirement` / 两本账等词；
`architecture/current.md` §6 与本索引同步。

## 验收记录（代理运行，2026-10-01）

| # | 场景 | 期望 | 实测 | 结论 |
|---|---|---|---|---|
| 1 | 玩家醉酒 → 改变角色 | 仍然醉酒 | `SeatStateTests.ChangingCharacter_DoesNotClearDrunk` 通过；全量 `dotnet test` 42/42 全绿 | 通过 |
| 2 | 玩家中毒 → 改变阵营 | 仍然中毒 | `SeatStateTests.ChangingAlignment_DoesNotClearPoison` 通过；另有五维逐一改动的 Theory 5/5 | 通过 |
| 3 | 持续型效果的来源死亡 | 立即终止 | `EffectLifecycleTests.PersistentEffect_IsTerminatedBySourceDeath_AndStaysTerminated`：死亡→不生效；终止后来源重新存活也不恢复（《重要细节》二-7） | 通过 |
| 4 | 来源醉酒后恢复清醒 | 继续生效（不是重新施加） | `PersistentEffect_Resumes_WhenSourceSobernsUp_WithoutBeingReapplied`：同一实例 Id 不变、未终止，`IsOperative` 由 false 回到 true | 通过 |
| 5 | 一次性能力中毒期间被使用 | 已浪费，恢复后不可再用 | `AbilityUseLedgerTests.WastedOncePerGameAbility_CannotBeUsedAgain`：`WasUsed=true`、`WasEffective=false` | 通过 |
| 6 | 同上 | used=true、effective=false | `WastedUse_IsRecordedAsUsedButNotEffective`：两条查询分别为 true / false | 通过 |
| 7 | 即时型效果已生效后来源中毒 | 不回滚 | `InstantaneousEffect_IsNotRolledBack_WhenSourceIsImpaired`：对照同来源持续型（挂起），即时型 `RemainsInEffect()` 恒真 | 通过 |
| 8 | 一次失败的能力结算 | 一条带原因分类的记录 | `MalfunctionLedgerTests.FailedAbility_IsRecordedWithItsReason`（Kind=Poisoned）；`UnverifiedPath…` 另证 `Open` 进待核对清单 | 通过 |
| 9 | 无合法选项的裁定点 | 返回声明的行为，不抛异常 | `DecisionPointTests.NoLegalOption_ReturnsDeclaredBehavior_WithoutThrowing`（Skip / StorytellerDecides / BlockAndAlert 三态）+ 未知值按阻塞（不静默、不抛异常） | 通过 |
| 10 | 手工加入"角色变更清醉酒"联合约束 | 不变量门禁测试失败 | **先红**：`dotnet test` → `ChangingCharacter_DoesNotClearDrunk`、`ChangingOneDimension…(Character)`、`SeatStateIndependenceGateTests.SeatState_DeclaresEveryDimensionAsAPlainAutoProperty` 共 3 条 FAIL；**后绿**：`git checkout -- …/SeatState.cs` 还原（与 HEAD 零 diff）后复跑 42/42 全绿 | 通过 |

> 第 10 行是**先红后绿**探针：红、绿均为一次真实 `dotnet test` 运行；探针在同一会话内还原并复验。
> 其余每一行的"实测"都来自真实测试运行（不是纸面推演）。

**复算方式**：

```powershell
dotnet build OpenClockTower.slnx                                # 期望 0 警告 0 错误
dotnet test  OpenClockTower.slnx                                # 期望 42 通过：Kernel 35 + Gates 6 + Integration 1
dotnet format OpenClockTower.slnx --verify-no-changes           # 期望 exit 0
```

## 过程中的关键设计决定

1. **「终止」与「挂起」必须分开**：百科《重要细节》二-3 / 三-3 说来源死亡、中毒、醉酒时
   持续型效果都会"终止"，但只有中毒/醉酒会"恢复后继续生效"；死亡是终局——
   同页二-7：复活视为获得新角色，"和原角色能力有关的所有持续性效果也会立即终止"。
   因此 `PersistentEffect` 用两个独立事实表达：`IsTerminated`（不可逆）与来源当前是否清醒健康存活。
2. **疯狂不进 `SeatState`**：R-0003 要求引擎不判定疯狂，故单独建模为 `MadnessRequirement`（以裁定点溯源）；
   并用反射测试锁住"`SeatState` 恰好五个可观测维度"，防止将来被顺手塞进去。

## 过程中发现并修掉的文档欠账

- 架构页 §6 模块清单写的还是「`Kernel.Tests` 六状态不变量 6 条」「规范门禁 4 条」，
  与本轮实测不符（测试 35 条、门禁测试方法 6 个）——本轮同步为实测值。

## 遗留

- 本票据不做结算、不实现角色；两本账与裁定点的**真实写入方**（结算引擎）在未来票据里，
  本票据只交付契约与不变量。
- `MadnessRequirement` 不携带真实时间（D-0008）：它以产生它的裁定点溯源，
  逻辑时刻将随步骤机进事件流（见 `docs/backlog/todo/operation-request-step-machine.md`）。
- `MalfunctionKind.Open` 的待核对清单由 R-0004 收口；`Barista`（咖啡师）是否可用，取决于 R-0007 对旅行者的范围决策。
- 架构页门禁清单未列「机器绝对路径」门禁（`RepositoryGateTests` 确实存在），其见红记录亦未见档——
  属既有欠账，本轮未补做，已在此登记。
