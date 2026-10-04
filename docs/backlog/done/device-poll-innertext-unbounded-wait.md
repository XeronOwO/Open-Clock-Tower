# 装置轮询助手的 `innerText` 无界等待：元素缺失时白等 30s

- Status: Done（2026-10-04）
- Priority: Low
- Depends on: 无（同款写法已在 `tools/verify-accounts.mjs` 内修掉，见 `done/accounts-device-section-selector.md`）

## 要解决的问题

`tools/verify-*.mjs` 里每个装置都带着自己那份轮询助手（`waitForText` / `waitForLocatorContains` /
`waitForLocatorText`），循环体是同一种写法：

```js
text = compact(await locator.innerText().catch(() => ''))
```

`locator.innerText()` 的默认超时是 Playwright 的 **30 秒**。元素**不存在**时它不会立刻抛错，
而是白等满 30s 才 reject、再被 `.catch` 吞成空串——于是助手的 `timeoutMs`（15s / 30s）只是
"循环看 deadline"的名义值，真到元素缺失时按 30s 一档走，自己的 deadline 被悄悄突破。
反过来更危险：元素**晚到**（例如 25s 才出现、deadline 30s）时，第一次读取就把 30s 吃满，
循环第一轮结束即超时 → 返回空串 → 断言假红。

首次实测（E31 之后）：账号装置的游客页并不渲染 `player-diagnostics`，
`innerText().catch(() => '')` 每次都白等 30s，把装置从 7.3s 拖到 37.3s；
分段耗时表把它钉在 `guest=30.3s` 才暴露。同轮只修了账号装置
（`readTextBounded`：`innerText({ timeout: 500 })`），其余装置未动。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 元素缺失时助手按自己的 deadline 放弃 | 15s 的助手在 ~16s 内返回，不再被拖到 30s | 最小复现：元素不存在的假页面 |
| 2 | 元素在 deadline 内晚到时能读到 | 断言不假红 | 同上，延迟注入元素 |
| 3 | 影响面清点 | 逐装置列出同名助手与调用点（当前 grep：12 个装置、14+ 处） | grep 清单 |
| 4 | 装置不回归 | 受影响装置各跑一次对应段，全绿 | 各装置一次运行 |

## 决定与依据

- **不**改 Playwright 全局默认超时：装置里 `waitFor` / `click` 等仍需默认语义，改全局是另一种风险；
- 修法取共享助手 (b)：`tools/lib/bounded-text.mjs` 的 `readTextBounded(locator, timeoutMs = 500)`——
  读不到返回 `''`、读到返回**原文**（compact / trim / 空白折叠仍归各调用点），
  Playwright 的 30s 默认超时不再参与任何轮询读取；`verify-accounts` 的本地副本一并收敛进来，
  全仓只剩一份实现；
- **整族对齐的边界**：修的是"目标可能尚不存在的轮询读取"（12 装置 19 处，含无守卫的
  `waitForSeedChange`）。`readDecisionText` / `readSlotContext` / `readTextOrNull` / `waitForHubToken` /
  `waitForGrimoireLocate` / `panelText` 这类**先 `count()` / `isVisible()` 守卫再读**的写法，
  缺失路径本来就不白等（守卫先返回空），不在本票范围——读取期间脱离的竞态是另一类问题；
- 最小复现独立成脚本 `tools/check-bounded-text.mjs`（假页面四探针），不依赖宿主与 Vite。

## 结果（2026-10-04 · 最小复现 + 12 装置各一次迭代档 + 门禁）

| # | 判据 | 实测 | 结论 |
|---|---|---|---|
| 1 | 在场对照 | 17ms 读到原文 | 通过 |
| 2 | 缺失元素单读 | 501ms 返回空串（旧写法 30s） | 通过 |
| 3 | 15s 档轮询守时 | 15.3s 放弃（旧写法：首读 30s 后返回） | 通过 |
| 4 | 晚到元素（1.2s 注入） | 1.3s 读到，未假红 | 通过 |
| 5 | 迁移面 | **12 装置 / 19 处**：witch 2、madness 2、mathematician 1、retro-info 1、seamstress-artist 1、setup-randomizer 3（含 `waitForSeedChange`）、winloss 2、pit-hag 1（委托）、death-triggers 1、character-change 1（委托）、storyteller-panel 2、accounts 2（收敛本地实现） | 通过 |
| 6 | 12 装置各跑一次（迭代档） | accounts 7.0s / storyteller-panel 34.8s / witch 11.1s / madness 10.8s / mathematician 19.8s / setup-randomizer 11.9s / winloss 11.5s / pit-hag 21.0s / character-change 35.4s 全绿；retro-info / seamstress-artist / death-triggers 首跑红——`git stash` 基线同红同指纹（E31 遗留读法，非本票回归），另行修复后 **53 / 82 / 70 全绿** | 通过（无回归） |
| 7 | 门禁 | dotnet build 0 · dotnet test 0（853：24+327+314+188）· dotnet format 0 · `npm run gate` 0（vitest 154） | 通过 |

诚实记录（范围说明）：

- 本票是装置工具链改动，证据全部来自**迭代档**运行（最小复现 + 12 装置各一次），不作玩法验收证据；
- 三个装置首跑红与修复属另一张票据 `done/player-information-row-readers-stale.md`；
  本票只证明"不是本票引入的回归"（基线 stash 复现同红），修复后同一批装置全绿；
- 日志留 `artifacts/web/`（gitignored）：`bounded-text-check.log`、`bounded-<装置>.log`、
  `bounded-<装置>-fixed.log`、`gates.log`。
