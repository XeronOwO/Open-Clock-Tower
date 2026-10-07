/**
 * 装置**轮询 / 守卫式**读取的**有界**助手（文本 + 属性）——票据
 * docs/backlog/done/device-poll-innertext-unbounded-wait.md、
 * docs/backlog/done/device-guarded-reads-unbounded-wait.md 与
 * docs/backlog/done/device-attribute-poll-unbounded-wait.md。
 *
 * 为什么必须有界：Playwright 的 `locator.innerText()` / `locator.getAttribute()` 默认等 30 秒
 * （读取前先等元素附着）。轮询助手在元素缺失时每个周期都会白等满 30 秒再被 `.catch` 吞成空值——
 * 助手自己的 deadline（15s / 30s）被悄悄突破；元素**晚到**时更糟：首读就吃满 30 秒，
 * 循环第一轮即判超时 → 断言假红。
 * 实测：账号装置 guest 段（该段的 id 已随 D-0037 正名为 `invite`）30.4s → 0.4s，整装置 37.3s → 7.3s
 * （见 done/accounts-device-section-selector.md）。
 *
 * 口径：读不到（不存在 / 读取期间脱离 / 严格模式冲突）返回调用方约定的空值——文本 `''`、
 * 属性 `null`；读到返回**原文**。文本归一化（compact / trim / 空白折叠）与 `null` → `''`
 * 之类的转换留给各调用点，助手不做隐藏转换。
 *
 * 边界：本模块只服务"读不到属于正常路径"的轮询 / 守卫读。元素预期在场的直接读取
 * （席位牌正文、整页 body、显式 `waitFor` 之后的读取等）保持 Playwright 的 auto-wait 语义。
 */

/**
 * 有界读取 locator 的可见文本；读不到返回 ''。
 * 调用方负责在外层循环里用自己的 deadline 决定"何时放弃、放弃算不算红"。
 *
 * @param {import('playwright').Locator} locator 目标定位器
 * @param {number} timeoutMs 单次读取上限：够一次已附着元素的读取，又远小于外层 15s / 30s 的 deadline
 */
export function readTextBounded(locator, timeoutMs = 500) {
  return locator.innerText({ timeout: timeoutMs }).catch(() => '')
}

/**
 * 有界读取 locator 的属性值；读不到返回 null（属性本身不存在时也是 null，与 Playwright 口径一致）。
 *
 * 装置里的守卫式读法（`count()` 看到元素后读 `data-*`）与轮询读法
 * （`waitForAttribute` / `waitForRequestPanelIdle` / 采样谓词）都走这里：
 * 守卫只保证"读之前那一瞬间"元素存在，Vue 推送重建面板时元素可能在守卫与读取之间脱离，
 * 无界读会白等满 30 秒再被 `.catch` 吞掉——读到 `null` 只是"这一刻读不到"，由调用方按语境决定算空值还是算红。
 *
 * @param {import('playwright').Locator} locator 目标定位器
 * @param {string} name 属性名（如 `data-day-status` / `data-information-count` / `title` / `aria-expanded`）
 * @param {number} timeoutMs 单次读取上限：够一次已附着元素的读取，又远小于外层 15s / 30s 的 deadline
 */
export function readAttributeBounded(locator, name, timeoutMs = 500) {
  return locator.getAttribute(name, { timeout: timeoutMs }).catch(() => null)
}
