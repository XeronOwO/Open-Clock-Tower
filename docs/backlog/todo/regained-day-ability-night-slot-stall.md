# 重获「白天能力」之后的那一夜：死者的行动格被点活却整夜停住

- Status: Todo
- Priority: High
- Depends on: `done/traveller-and-exile.md`（集骨者 R-0054）、`done/juggler-and-savant-day-abilities.md`（杂耍艺人 R-0057-B）

## 要解决的问题

集骨者重获一名**已死亡**玩家的能力之后，如果那个能力是**白天**能力（杂耍艺人的公开猜测，
或任何"不在夜晚顺序表上、夜里不行动"的能力），当夜的计划里那一格会被

`NightSlotActivation.PlanRegained` 正确地**点活**（空槽 → 行动者 = 死者、归属 = 那个角色，
`SlotActivatedEvent` 确实落了账），但计划走到那一格时**既不挂说书人裁定点、也不发玩家请求**，
夜间就此停住：`PlanCompleted` 永远为假，"开白天"按钮一直不可用——**这一夜过不去**。

首版 30 个角色里只有杂耍艺人属于这一类，所以触发面很窄；但触发之后是**卡住整局**，不是少一条信息。

### 复现（真宿主，确定性）

`tests/OpenClockTower.Integration.Tests/BoneCollectorHostTests.cs` 的
`BoneCollector_RegainingDayAbility_LetTheDeadJugglerGuessAgainOnThatDay` 走过这条链路：
1 号杂耍艺人第 1 天公开猜过一次 → 当天被说书人上报死亡 → 集骨者（第 6 席）在第 2 夜选中 1 号重获能力
→ 第 2 夜的 `PlanCompleted` 永远不成立。

### 实测证据（2026-10-05）

| 观测 | 值 |
|---|---|
| 那一格建表时的形状 | `Empty`（角色 = juggler，无行动者） |
| `SlotActivatedEvent` | **1 条**（确实点活了） |
| 计划走到那一格时的状态 | `CurrentSlotId = juggler`、`PendingRequest = 无`、`AwaitingDecision = 无`（实测取 `GameSession.GetStorytellerView()`） |
| 界面侧（装置 `tools/verify-bone-collector-juggler.mjs`） | 席位操作台 `seat=6 裁定块=无 卡点块=有`（卡点停在集骨者那一格之前），夜里没有任何裁定点 |
| 对照：**夜晚**能力的重获 | 正常——`BoneCollectorHostTests.BoneCollector_RegainsDeadPlayersAbility_UntilNextDusk`（卖花女孩）与 `…RegainingFirstNightAbility…`（钟表匠，走 R-0055 追加格）都判过 |

## 初步定位（未定论，动手前先证伪）

`SlotActivationFolder.Rebind`（`src/OpenClockTower.Kernel/SlotActivationFolder.cs`）在**普通行动槽**那一支
用 `slot.Character`（= 被重获的角色）作为 `StepSlot.Action(...)` 的最后一个实参——即 `Owner`；
而 `StepSlotEntry.UnavailableReason`（`src/OpenClockTower.Kernel/StepSlotEntry.cs`）判"死者能不能被唤醒"
读的是 `slot.Owner == entry.CharacterValue`。两者对**重获窗口**这种"死者持有自己本该有的能力"的场景
可能对不上：一旦 `Owner` 变成"行动者的角色"而不是"被授予的能力角色"，这一格就会被判成
「行动者已经死亡：本步不唤醒（配额照走）」——**静默跳过**，正好表现为"整夜停住"。

要证伪 / 坐实这一点，最短路径是把 `StepSlotEntry.UnavailableReason` 的判定条件打点出来
（或在 `SlotActivatedEvent` 折叠后的槽位上直接读 `Owner` / `Character`），确认是哪一支返回了非 null。

## 决定与依据

- 规则依据：百科《集骨者》· 2026-10-04 抓取 · 角色简介 2（重获后限次能力可再用，**即使先前已经用过**）、
  规则细节 2（重获**夜晚会行动**的能力时说书人必须主动唤醒）；R-0054 第 6 条（当夜落格）。
- 平台口径：重获白天能力**不需要**当夜唤醒任何人（他的能力在白天生效）；
  这一格最差也应当像"死者无行动"那样**显式跳过并继续推进**（`PromptSkippedEvent`，配额照走），
  绝不能把整夜卡死。修的时候两条都要满足：① 夜里不停住；② 如果确实唤醒了，提示要按当前账重建。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 上表的复现链路 | 第 2 夜正常走完（`PlanCompleted` 为真） | `BoneCollectorHostTests` 该用例改为断言走完 |
| 2 | 同一夜的其余格子 | 照常被唤醒 / 照常跳过，不受这一格影响 | 同一用例的后续断言（恶魔击杀格） |
| 3 | 那一位死者在当夜的角色 | 要么被唤醒并给出提示、要么显式记一条跳过（可归因，不静默） | 事件流里的 `PromptSkippedEvent` 或裁定点 |
| 4 | 装置链路 | `tools/verify-bone-collector-juggler.mjs` 能继续跑到第 4 夜，把"窗口到期"的界面证据补上 | 该装置新加的段（本票修好后补） |
| 5 | 同族回归 | 夜晚能力（卖花女孩 / 钟表匠）的重获不受影响 | `BoneCollectorHostTests` 既有两条用例 |

## 相关阅读

- 集骨者那一族：`done/traveller-and-exile.md`（R-0054）、`docs/standard/rulings.md` R-0055 / R-0056
- 装置与票据：`review/bone-collector-regained-juggler-day-entry.md`（本票是它的残余）
- 槽位激活口径：`docs/architecture/current.md` §结算与槽位、`docs/acceptance/devices.md`
