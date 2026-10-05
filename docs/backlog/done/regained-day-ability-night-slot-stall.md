# 重获「白天能力」之后的那一夜：原判「整夜停住」被实测推翻

- Status: Done
- Priority: High（原判如此；实测该缺陷不成立）
- Depends on: `done/traveller-and-exile.md`（集骨者 R-0054）、`done/juggler-and-savant-day-abilities.md`（杂耍艺人 R-0057-B）

## 结论（2026-10-05 实测）

**那一格不会把夜卡住。** 重获白天能力的角色当夜被点活之后，进入那一格照常挂出**归属他席位的
说书人裁定点**（杂耍艺人是"比划猜对数量"）；说书人结清后计划继续推进，`PlanCompleted` 为真。

原票的三条判断逐条被推翻：

| 原票判断 | 实测 |
|---|---|
| 计划走到那一格时**既不挂说书人裁定点、也不发玩家请求** | **挂裁定点**：真宿主 `AwaitingDecisionId = sv:night-2:juggler:decision`、归属 1 号；界面面同样读得到（魔典 3 号席位卡 `data-decision=true`、裁定点正文写明"杂耍艺人获得信息……按结算时刻的角色快照推演：猜对 0 条"） |
| **整夜停住**、`PlanCompleted` 永远为假 | 结清裁定点后第 2 夜自然走完（`PlanCompleted=true`），第 2 天照常开得起来 |
| `StepSlotEntry.UnavailableReason` 与 `SlotActivationFolder.Rebind` **口径对不上**（静默跳过） | **不成立**：核对该链路事件流，`PromptSkippedEvent` 0 条、`SlotBlockedEvent` 0 条——那一格既没被跳过也没被阻塞，走的是"挂裁定点"这条正常分支 |

## 当初为什么会误判

原票的"确定性复现"来自 `BoneCollectorHostTests.BoneCollector_RegainingDayAbility_LetTheDeadJugglerGuessAgainOnThatDay`，
而那条用例当时**用 `ForceAdvance` 兜底 8 次**才走完第 2 夜。把兜底拆掉之后真相是：

- 第 2 夜真正停住的地方是**恶魔击杀格**——夹具没有连恶魔席位、没人提交 `SubmitResponse`，
  请求一直挂在 `sv:night-2:fang-gu` 上；`ForceAdvance` 每次把这个"等玩家"的状态推过去，
  于是"第 2 夜没走完"被记成了"那一格卡住"。
- 同一个混淆也在装置侧：被重获的杂耍艺人当夜的提示**受众是说书人**（`JugglerNightAction`
  的提示没有玩家选项、走自由决定），所以 3 号的玩家页整夜 `idle`——**玩家页看不到请求 ≠ 夜里停住**。
  原票把装置那一行「席位操作台 `裁定块=无 卡点块=有`」读成了停住。

## 处置

| # | 动作 | 证据 |
|---|---|---|
| 1 | 三条集骨者宿主用例**去掉强推兜底**：恶魔击杀格改由恶魔本人提交（点名已死席位，不改变任何状态） | `BoneCollectorHostTests` 三条全绿；第三个用例 18s → 3s（少掉 8 次兜底与 2s 等待） |
| 2 | 该用例补上"那一格被唤醒"的正面断言（裁定点归属席位），第 2 夜必须**自己**走完 | `BoneCollectorHostTests.BoneCollector_RegainingDayAbility_…` 的归属 1 号断言 |
| 3 | 装置补当夜唤醒面 3 条 + 到期面 4 条，并改正错误的"卡住"注释 | `tools/verify-bone-collector-juggler.mjs` 取证档 **52 项全过**（原 42 项），跑满第 4 夜与第 3 天 |

## 验收矩阵结论（原票矩阵，逐行）

| # | 场景 | 结论 | 证据 |
|---|---|---|---|
| 1 | 复现链路 | 通过（原判不成立） | 第 2 夜自然走完、`PlanCompleted=true`（`BoneCollectorHostTests`） |
| 2 | 同一夜的其余格子 | 通过 | 恶魔击杀格由本人提交后照常结算，后续格照常推进 |
| 3 | 那一位死者在当夜的角色 | 通过 | 裁定点归属 1 号（真宿主）+ 魔典席位卡「待裁定」+ 裁定点正文（装置） |
| 4 | 装置链路（跑到第 4 夜、补到期界面证据） | 通过 | 装置第 9 / 10 段：第 4 夜无报数裁定点、3 号页 `idle`；第 3 天入口消失 + 效果链「已终止（下个黄昏：窗口到期）」 |
| 5 | 同族回归（夜晚能力：卖花女孩 / 钟表匠） | 通过 | `BoneCollectorHostTests` 两条同样改成真提交后全绿 |

## 留下的教训

- **"整夜卡住"这类结论必须先排除"夹具没答请求"**：`ForceAdvance` 会把"等玩家提交"与"引擎不推进"
  推成同一个现象。夹具用兜底推进时，兜底次数与落在哪一格要如实写出来（原票只写了"永远不成立"）。
- **受众是说书人的提示在玩家页上看不到**：`ChoicePrompt.Audience` 决定投影去哪一侧；
  判"某一格有没有被唤醒"要读说书人侧（裁定点 / 魔典席位标记），不能只看玩家页。
- 这两条已写进装置头注释与 `BoneCollectorHostTests` 的相关用例注释，避免下次同款误判。

## 相关阅读

- 集骨者那一族：`done/traveller-and-exile.md`（R-0054）、`docs/standard/rulings.md` R-0055 / R-0056
- 装置与票据：`done/bone-collector-regained-juggler-day-entry.md`（本票是它的残余）
- 槽位激活口径：`docs/architecture/current.md` §结算与槽位、`docs/acceptance/devices.md`
