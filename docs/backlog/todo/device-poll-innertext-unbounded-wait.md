# 装置轮询助手的 `innerText` 无界等待：元素缺失时白等 30s

- Status: Todo
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
- 修法二选一：(a) 每处显式 `innerText({ timeout: 500 })`；(b) 抽 `tools/lib/` 共享助手，
  14 份拷贝一起收。倾向 (b)——但一次动 12 个装置、各需一次运行回归，所以单独排，不塞进本批；
- 严重度低：正常路径元素都在，只在慢 / 失败路径上咬人；但口径明说"红断言的等待要算钱"，
  这类白等属于纯浪费。
