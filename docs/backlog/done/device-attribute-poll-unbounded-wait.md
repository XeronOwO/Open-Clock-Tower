# 装置属性读取未收口：轮询/守卫式 `getAttribute` 仍可能吃满 30s

- Status: Done（2026-10-04）
- Priority: Low
- Depends on: 无（同族文本读取见 `done/device-poll-innertext-unbounded-wait.md` 与 `done/device-guarded-reads-unbounded-wait.md`）

## 要解决的问题

`innerText()` 一族收口后，装置里还有另一类同款读取：**属性**。`locator.getAttribute(name)` 的默认超时
同样是 Playwright 的 30 秒；元素在守卫 / 轮询之间脱离时会白等，或被 `.catch(() => null)` 吞成静默空值，
把助手自己的 deadline 悄悄突破——根因与文本族完全相同。

已清点的同族调用点：

- `waitForAttribute(locator, name, expected, timeoutMs)`：**11 个装置**
  （witch / madness / mathematician / setup-randomizer / winloss / retro-info / seamstress-artist /
  death-triggers / character-change / storyteller-panel / pit-hag）的轮询循环里
  `value = await locator.getAttribute(name)`；
- 守卫式 `read*Count`：mathematician / retro-info / death-triggers / seamstress-artist / storyteller-panel /
  setup-randomizer 的 `readPlayerInformationCount` / `readInformationCount`（`count()` 守卫后取
  `data-information-count`）；
- 日状态读取：retro-info / death-triggers 的 `readDayState`（`data-day-status` / `data-day-number` /
  `data-nomination-count`，末者已带 `.catch(() => null)` 但仍是 30s）；
- 标记 `title` 读取：character-change 的 `waitForHubToken`（`token.getAttribute('title')`）。

## 验收矩阵（开工时按实际清点修订）

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 元素脱离时读属性有界 | 新助手 ≤1s 返回 null，不再等 30s | 假页面探针 |
| 2 | 迁移面 | 上列轮询 / 守卫式属性读取不再有裸 `getAttribute` | grep 清单 |
| 3 | 装置不回归 | 受影响装置各一次迭代档全绿 | 各装置运行日志 |

## 决定与依据

- 在 `tools/lib/bounded-text.mjs` 增加
  `readAttributeBounded(locator, name, timeoutMs = 500)`（`getAttribute(name, { timeout }).catch(() => null)`），
  与 `readTextBounded` 同一处收口；
- 开工清点结论：`inputValue` 直读均在显式 `waitFor` / `count` 之后；`isEnabled` 轮询（4 处）目标是常驻的
  `st-start-day`（`isEnabled` 只等附着、不等 enabled）；`textContent` 只在 `page.evaluate` 页面上下文内——
  均无 30s 白等路径，保持 auto-wait 并登记在诚实记录；
- 同形扩面：票面清单之外把同类守卫 / 轮询属性读一并纳入（`madness` 处罚开合、`accounts` 抽屉开合、
  `readArtistPanel`、`readPlayerLifeOf`、3 处 `advanceSlotsUntil` 谓词），见结果表行 2；
- 迁移面审计：11 项旧写法检查全 0，剩余 53 条直读逐条分类（`artifacts/web/attribute-migration-audit.log`）。

## 结果（2026-10-04 · 探针 + 审计 + 12 装置迭代档 + 门禁）

| # | 判据 | 实测 | 结论 |
|---|---|---|---|
| 1 | 元素脱离时读属性有界 | 探针 F：守卫 count=1 → 元素移除 → `readAttributeBounded` 返回 null · **512ms**；同场景裸 `getAttribute`（先红探针，测的是 Playwright 原始语义，与提交版本无关）**30011ms**（`artifacts/web/attribute-guarded-reads-before-fix.log`） | 通过 |
| 2 | 迁移面 | **12 装置 / 37 处**：`waitForAttribute` 11 · `readPlayerInformationCount` 4 · `informationCount` 2 · `waitForRequestPanelIdle` 6 · `describePlayerDay` 日状态 / 提名 6 · `waitForHubToken` title 1 · `readPlayerLifeOf` 1 · `readArtistPanel` 1 · `advanceSlotsUntil` 谓词 3 · madness 处罚开关 1 · accounts 抽屉 1；审计 11 项旧写法检查全 0（`artifacts/web/attribute-migration-audit.log`） | 通过 |
| 3 | 装置不回归（迭代档） | witch 28/11.8s · madness 28/11.2s · mathematician 38/20.3s · setup-randomizer 58/12.4s · winloss 25/12.3s · retro-info 53/19.9s · seamstress-artist 82/29.1s · death-triggers 70/21.8s · character-change 84/37.4s · storyteller-panel 判定 180（跳过 2）/36.1s · pit-hag 34/21.1s · accounts 判定 36/7.5s —— **12/12 全绿（exit=0）** | 通过 |
| 4 | 探针脚本 | `node tools/check-bounded-text.mjs` A–F 全过：在场 17ms · 缺失 509ms · 15s 守时 15.3s · 晚到 1.3s · 文本守卫后脱离 508ms · 属性守卫后脱离 512ms（`artifacts/web/bounded-check.log`） | 通过 |
| 5 | 门禁 | `dotnet build` 0 · `dotnet test` 0（**853** = 327+314+24+188）· `dotnet format` 0 · `npm run gate` 0（vitest 154）（`artifacts/web/gates.log`） | 通过 |

诚实记录（范围与边界）：

- 同形扩面（票面清单之外，审计后一并纳入）：`madness` 的处罚开合守卫、`accounts` 的数据抽屉开合守卫、
  `seamstress-artist` 的 `readArtistPanel`、`storyteller-panel` 的 `readPlayerLifeOf` 与 3 处 `advanceSlotsUntil` 谓词；
- 保持 auto-wait 的边界（本轮清点后确认无 30s 白等，未改）：元素预期在场的直接读取（席位牌 / 重建报告 /
  注记 token（显式 `waitFor` 之后）/ 抽屉开关 / `readPlayerActivity` 与 `setup-randomizer` 的请求态采样 /
  失败路径诊断字符串）；`waitForEnabled` ×4（常驻 `st-start-day`，`isEnabled` 只等附着不等 enabled）；
  `inputValue`（均在显式 wait 之后）；`page.evaluate` 内的 `textContent` / `getAttribute`（页面上下文，无附着等待）；
- 本票是装置工具链改动：证据全部是**迭代档 + 假页面探针 + 迁移面审计**，不作玩法验收证据；
- 一次性先红脚本 `artifacts/web/attribute-before-fix-probe.mjs` 跑完即删（显式单文件删除并回看确认）；
  证据日志保留在 `artifacts/web/`（gitignored）：`attribute-guarded-reads-before-fix.log`、`bounded-check.log`、
  `attribute-migration-audit.log`、`attribute-*.log` ×12、`attribute-summary.log`、`gates.log`。
