# 装置守卫式读取未收口：条件读取仍可能吃满 30s

- Status: Done（2026-10-05）
- Priority: Low
- Depends on: 无（是 `done/device-poll-innertext-unbounded-wait.md` 写明的边界项；同族的**属性**读取另立 `todo/device-attribute-poll-unbounded-wait.md`，本轮不混做）

## 要解决的问题

上一票把**轮询助手**（`waitForText` / `waitForLocatorContains` / `waitForLocatorText` / `waitForSeedChange`）
的文本读取统一改成了 `readTextBounded`（单次读取 500ms 上限）；但各装置还有一批**先守卫后读取**的助手：

```js
const block = page.locator('[data-testid="console-decision"]')
if ((await block.count()) === 0) {           // 守卫：不存在就走空路径（这条没问题）
  return ''
}

return compact(await block.first().innerText())   // 无界读：默认 30s
```

守卫只保证"读之前那一瞬间"元素存在。Vue 每次推送都会重建面板，元素完全可能在 `count()` 与
`innerText()` 之间脱离。此时 `innerText()` 不会立即报错，而是等满 Playwright 默认 **30 秒**再抛错
（或被 `.catch` 吞成空串）——助手自己的 15s / 20s / 30s deadline 被悄悄突破，装置白等 30s，
甚至把"读取失败"当成"界面为空"给出假红或假绿。

受影响助手（本轮清点，11 个装置）：

| 助手 | 装置 |
|---|---|
| `readDecisionText` | witch / madness / mathematician / setup-randomizer / winloss / retro-info / seamstress-artist / death-triggers / character-change / storyteller-panel |
| `panelText` | witch / madness / mathematician / winloss / retro-info / character-change / storyteller-panel |
| `readSlotContext` / `readDecisionSeatHint` | death-triggers / seamstress-artist |
| `readTextOrNull` | death-triggers / retro-info / seamstress-artist |
| `waitForGrimoireLocate` / `decisionId` | death-triggers |
| `waitForHubToken`（文本部分） | character-change |

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 守卫后元素脱离（文本） | 读取有界返回空串（≤1s），不再被拖到 30s | `tools/check-bounded-text.mjs` 新增探针 E |
| 2 | 先红：同一场景跑旧写法 | 裸 `innerText()` 等满 ~30s 才返回/抛错 | 一次性探针日志 `artifacts/web/guarded-reads-before-fix.log` |
| 3 | 迁移面 | 上表助手内不再有裸 `innerText()`；元素预期在场的直接读取不动 | grep 清单 + diff |
| 4 | 装置不回归 | 受影响装置各跑一次迭代档全绿 | 各装置运行日志 |
| 5 | 门禁 | dotnet build / test / format + `npm run gate` + 探针脚本全绿 | `artifacts/web/gates.log` |

## 决定与依据

- 修法与上一票同源：**守卫保留**（元素缺失时仍 0ms 返回空路径），把守卫之后的**读取**换成
  `readTextBounded`；读取返回原文，`compact` / `trim` / `null` 与空串契约全部留在各助手内部，不改变调用方语义；
- **整族对齐的边界**：只收口"守卫 / 轮询语境下、读不到属于正常路径"的文本读取。
  元素预期在场的直接读取（席位牌 `cardOf(seat).innerText()`、整页 `body.innerText()` 等）保持
  Playwright 的 auto-wait 语义——那里的"等元素出现"是想要的，不在本票范围；
- 同族的**属性**读取（`waitForAttribute` × 11 装置、`read*Count`、日状态、标记 `title`）本轮只登记
  `todo/device-attribute-poll-unbounded-wait.md`，不动代码：两类原语的收口各自成票，避免一次大扫描
  跨文件混做（上一票的教训：迁移一次只做一类，diff 才审得清）。

## 结果（2026-10-05 · 探针 + 11 装置迭代档 + 门禁）

| # | 判据 | 实测 | 结论 |
|---|---|---|---|
| 1 | 守卫后脱离（新写法） | 探针 E：守卫 `count=1` → 元素移除 → `readTextBounded` 返回 '' · **501ms** | 通过 |
| 2 | 先红（旧写法同一场景） | 裸 `innerText().catch` 返回 '' · **30012ms**（一次性探针复现旧写法语义，与提交版本无关；日志 `artifacts/web/guarded-reads-before-fix.log`） | 通过（缺陷可见） |
| 3 | 迁移面 | **11 装置 / 40 处**（readDecisionText 10 · panelText 7 · readTextOrNull 3 · decisionId 3 · readPlayerInformationText 3 · readSlotContext 2 · readDecisionSeatHint 2 · infoText 2 · waitForGrimoireLocate / waitForHubToken（文本）/ malfunctionLedgerText / readPlayerDiagnostics / readSettledNote / replayMarkerText / 换手诊断读 / accounts 游客诊断 各 1）；审计脚本：函数内「守卫 12 行内 + 裸 `innerText()`（非 body）」= **0** | 通过 |
| 4 | 装置不回归（迭代档） | accounts 36 项 / 6.7s · witch 28 / 11.1s · madness 28 / 10.8s · mathematician 38 / 19.7s · setup-randomizer 58 / 12.4s · winloss 25 / 12.4s · retro-info 53 / 19.8s · seamstress-artist 82 / 29.1s · death-triggers 70 / 21.7s · character-change 84 / 36.5s（诊断读修复后复跑）· storyteller-panel 判定 180（跳过 2）/ 36.2s —— **11/11 全绿** | 通过 |
| 5 | 探针脚本 | `node tools/check-bounded-text.mjs` A–E 五探针全过：在场 15ms · 缺失 500ms · 15s 守时 15.3s · 晚到 1.3s · 守卫后脱离 501ms | 通过 |
| 6 | 门禁 | `dotnet build` 0 · `dotnet test` 0（**853** = 24+327+314+188）· `dotnet format` 0 · `npm run gate` 0（vitest 154） | 通过 |

诚实记录（范围与边界）：

- 本票只收口**具名助手与同形守卫读**；同族**属性**读取（`waitForAttribute` × 11 装置、`read*Count`、
  日状态、标记 `title`）留在 `todo/device-attribute-poll-unbounded-wait.md`，本轮未动代码；
- 仍保留 auto-wait 语义的读取不在本票：元素预期在场的场景直读（席位牌 / 整页 `body` / 请求上下文等）、
  `waitForOutcome` 在状态就绪后的 `box.innerText()` 诊断读、`setup-randomizer` 的 notesBox catch 读——
  这些路径没有"先守卫再轮询"的约定；若要继续收口零白等，另票审计；
- 本票是装置工具链改动：证据全部是**迭代档 + 假页面探针**，不作玩法验收证据；三张信息行装置
  （retro-info / seamstress-artist / death-triggers）的**取证档**重跑在批次 E32 执行；
- 顺手修正：4 处过期票据指针（`tools/verify-retro-info.mjs`、`verify-death-triggers.mjs`、
  `verify-mathematician.mjs`、`tools/lib/verify-profile.mjs` 头部的 in-progress/todo → done）；
  `docs/acceptance/devices.md` §2 口径行扩到守卫式读取与五探针；
- 日志：`artifacts/web/guarded-*.log`、`guarded-summary.log`、`guarded-reads-before-fix.log`、
  `bounded-text-check.log`、`gates.log`（均 gitignored）。
