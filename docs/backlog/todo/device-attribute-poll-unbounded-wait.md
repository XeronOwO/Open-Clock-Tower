# 装置属性读取未收口：轮询/守卫式 `getAttribute` 仍可能吃满 30s

- Status: Todo
- Priority: Low
- Depends on: 无（同族文本读取见 `done/device-poll-innertext-unbounded-wait.md` 与 `in-progress/device-guarded-reads-unbounded-wait.md`）

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

- 预计在 `tools/lib/bounded-text.mjs` 增加
  `readAttributeBounded(locator, name, timeoutMs = 500)`（`getAttribute(name, { timeout }).catch(() => null)`），
  与 `readTextBounded` 同一处收口；
- 开工时再清点是否还有别的属性原语（`inputValue` / `isEnabled` 等）的轮询读；
- 未开工：本轮只登记，避免与 `in-progress/device-guarded-reads-unbounded-wait.md` 的文本迁移混在一起。
