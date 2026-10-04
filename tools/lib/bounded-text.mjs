/**
 * 装置**轮询 / 守卫式**读取的**有界**文本助手 —— 票据 docs/backlog/done/device-poll-innertext-unbounded-wait.md
 * 与 docs/backlog/in-progress/device-guarded-reads-unbounded-wait.md。
 *
 * 为什么必须有界：Playwright 的 `locator.innerText()` 默认等 30 秒。轮询助手在元素缺失时
 * 每个周期都会白等满 30 秒再被 `.catch` 吞成空串——助手自己的 deadline（15s / 30s）被悄悄突破；
 * 元素**晚到**时更糟：首读就吃满 30 秒，循环第一轮即判超时 → 断言假红。
 * 实测：账号装置 guest 段 30.4s → 0.4s，整装置 37.3s → 7.3s（见 done/accounts-device-section-selector.md）。
 *
 * 口径：读不到（不存在 / 读取期间脱离 / 严格模式冲突）返回 ''，读到返回**原文**——
 * 文本归一化（compact / trim / 空白折叠）留给各调用点，助手不做隐藏转换。
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
