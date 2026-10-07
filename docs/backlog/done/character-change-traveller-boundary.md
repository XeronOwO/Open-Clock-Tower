# 角色转换越过旅行者边界：麻脸巫婆能把玩家变成旅行者（同族：理发师的混合玩家对）

- Status: Done
- Priority: **Medium**（规则违反：能力真能把旅行者与非旅行者互转，不只是界面瑕疵）
- Depends on: 无
- 冻结版本：`main` @ `c2889c4`（本票的代码与装置提交；结案记录在其后一次 docs 提交里）
- 原票名：`pit-hag-character-option-count-stale.md`（判断被本票推翻，见下"为什么原票判成陈旧断言"）

## 要解决的问题

麻脸巫婆的两维请求里，**第二维列的是整张花名册**（30 条 = 25 名剧本角色 + 5 名旅行者）：

```csharp
internal static IReadOnlyList<CharacterId> SelectableCharacters() => SectsAndVioletsRoster.All;
```

于是她可以「把任何玩家变成旅行者」（如屠夫），也可以把**旅行者玩家**变成普通角色。
理发师（`barber`）同病：交换角色的玩家对只排除了"另一名恶魔"，没排除"一侧旅行者、一侧非旅行者"。
两者都是规则不允许的**角色转换**。

## 判定与依据

**旅行者与非旅行者不能在游戏过程中互转。** 这不是平台取舍，是百科的口径：

| # | 依据（钟楼百科，2026-10-04 抓取） | 关键原文 |
|---|---|---|
| 1 | 《哪些是“可以但不建议”》· 基础规则部分「旅行者的角色转换」 | 「……另外，在这一规则下，**麻脸巫婆也仍无法将玩家变成旅行者，因为旅行者角色不满足“不在场”的定义**，详情参考术语汇总。」 |
| 2 | 《术语汇总》·「不在场」/「角色列表」/「旅行者列表」 | 「**不在场**：指在当前游戏中不存在，但出现在**角色列表**上的角色。」「**角色列表**：一叠罗列出对应剧本中可能出现的所有角色及角色能力的纸张。」旅行者在**旅行者列表**上 |
| 3 | 《旅行者》· 旅行者运作方式 | 「一般来说，旅行者角色不能在游戏过程中变成非旅行者角色，非旅行者角色也不能在游戏过程中变成旅行者角色。如果有玩家尝试用自己的能力执行这样的操作，那么**对他摇头示意让他们重新进行选择**。」（同页"你也可以允许"属需开局前告知的家规，默认口径不采用） |
| 4 | 《麻脸巫婆》· 角色能力 / 运作方式 | 「选择一名玩家和一个角色，如果该角色**不在场**，他变成该角色」；「让她指向一名玩家和**角色列表**上的一个角色图标」 |

已登记为裁定 **R-0060**（`docs/standard/rulings.md`）。

### 为什么原票判成「陈旧断言」

原票的机制解释是对的（候选表来自 `SelectableCharacters()` → 整张花名册 → 30 条），
归因也做了（在本批之前的 HEAD 上同样红 ⇒ **不是本批引入的回归**）——但**只查到了"为什么是 30"，
没查"30 是不是对的"**。装置的读数（30）一直是正确的，它咬出的正是这个产品缺陷。
教训与 E61 那次同源：**装置的读数与产品的行为一致，不等于产品的行为正确**；
规则类的红要先回百科，再决定改哪一边。

## 验收矩阵

| # | 场景 | 期望 | 证据（本次运行） |
|---|---|---|---|
| 1 | 麻脸巫婆候选角色表 | 恰好是**角色列表**（25 条：镇民 / 外来者 / 爪牙 / 恶魔），**一个旅行者都没有** | 单测 `PitHagNightActionTests.PromptCharacters_ExcludeTravellers`（先红后绿）；装置 `verify-pit-hag`：「第二维 = 角色列表上的全部角色（一个不少、顺序不变）→ 渲染 25 个 / 角色列表 25 个」＋「第二维一个旅行者都没有 → 花名册旅行者 deviant,bone-collector,barista,harlot,butcher；渲染 25 个」 |
| 2 | 麻脸巫婆目标维（席位） | 旅行者席位不进候选（旅行者不能变成非旅行者） | 单测 `PromptSeats_ExcludeTravellers`（旅行者席 3 号不在列） |
| 3 | 越界答案（迟到的 / 被篡改的） | 显式抛错、不写状态：选旅行者角色 → `TransformIntoTraveller_Throws`；把旅行者当目标 → `TransformTravellerTarget_Throws` | 两条单测（`InvalidOperationException`） |
| 4 | 理发师玩家对 | 混合对不进候选；**同为旅行者 / 同为非旅行者照旧**；迟到的混合答案抛错 | 单测 `SlotEntryWithFact_TravellerInPlay_OmitsMixedPairs`（候选恰为 `pair:1+3 / pair:1+5 / pair:3+5 / decline`）· `TwoTravellers_StillSwappableWithEachOther`（含 `pair:2+4`）· `AnswerMixedTravellerPair_Throws` |
| 5 | 同族逐条核对 | 舞蛇人（只在目标是恶魔时交换，旅行者不可能是恶魔）· 方古（外来者 → 方古，按类型）· 哲学家 / 洗脑师（镇民 / 外来者，按类型）· 筑梦师（本就排除旅行者）**都不受影响** | 代码逐条核对（理由记在 R-0060 第 4 条）＋ 全量单测 1472 通过（含筑梦师既有用例） |
| 6 | 回归面 | 三条真机链路全绿 | 主装置**取证档 310 项全过**（跳过 0）· `verify-character-change` 89 项 · `verify-pit-hag` 35 项 |

**先红后绿**：新增 7 条用例在改动前**全红**（`失败: 7，通过: 30`，其中 `第二维`一条实测渲染 30 条、
含 `deviant / bone-collector / barista …`；`第一维`实测多出旅行者席 `seat:3`），修好后同 7 条转绿。

## 改了什么

1. `SectsAndVioletsRoster.CharacterList`：补上花名册缺的概念——**角色列表**（四类型，不含旅行者），
   并把"不在场"的定义写在注释里。这是"能力可以指向的角色"的唯一出处。
2. `PitHagAbility.SelectableCharacters()` 改读 `CharacterList`；`PitHagNightAction` 的目标维排除旅行者席位，
   `ParseChoice` 对两维各自的越界显式抛错。
3. `BarberSwapInteraction`：候选对与迟到答案都按"同一侧"收口（`TravellerBoundary.IsSameSide`）。
4. `TravellerBoundary`（新文件）：把旅行者这条线收成一处，筑梦师的原私有实现并入（行为不变）。
5. 装置侧：新增 `tools/lib/roster.mjs`（从 `web/src/display/labels.ts` 读花名册——服务端权威数据在 C#，
   装置读不了；`labels.ts` 是 `RosterMirrorGateTests` 逐条对账过的镜像）；`verify-pit-hag` 的候选条数
   断言改成**从花名册派生 + 旅行者阴性**；主装置里手抄的那份花名册一并退场（对照实测 30 条、0 处不一致）。

## 残余（不许消失）

1. **目标维没有真机读数**：「旅行者席位不在第一维候选」只有单测与结算侧抛错兜住——两个相关装置
   （`verify-pit-hag` / `verify-character-change`）的夹具里都没有在场旅行者。下次做"在场旅行者 +
   麻脸巫婆"的夹具时补一行。
2. **说书人手工上报换角不受本约束**（R-0060 第 5 条）：那是《旅行者》页明确允许的家规形态，
   由说书人按下并自担"开局前告知"的义务；平台不在这一层替他说不。
3. **开局分配下拉仍列着 5 名旅行者**（服务端会拒），另立票 `docs/backlog/todo/assignment-offers-travellers.md`（Low）。
4. `GameStateComparer` 的 `Activity` / `VigormortisKills` 历史缺口与本票无关，仍记在 E60 的残余里。

## 相关阅读

- 裁定：`docs/standard/rulings.md` R-0060（含四页百科引文与同族逐条结论）
- 花名册与镜像门禁：`src/OpenClockTower.Rules/SectsAndVioletsRoster.cs` ·
  `web/src/display/labels.ts` · `tests/OpenClockTower.NormativeGates.Tests/RosterMirrorGateTests.cs`
- 装置：`tools/verify-pit-hag.mjs`（段 `5/8`）· `tools/lib/roster.mjs`
- 批次：`docs/acceptance/batches.md` 的 E62；咬出它的读数是 E60（"装置总跑咬出的两处历史红"）
