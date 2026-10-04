/**
 * 「轮询文本读取必须有界」的最小复现（验证 tools/lib/bounded-text.mjs）——
 * 票据 docs/backlog/done/device-poll-innertext-unbounded-wait.md 的验收矩阵行 1、2。
 *
 * 为什么单独有这个脚本：装置里的轮询助手只在"元素缺失 / 晚到"的失败路径上才会咬人，
 * 正常路径的元素都在——真机装置跑绿证明不了这个修复。这里用假页面直接构造失败路径：
 *   探针 A（对照）：在场元素读到原文（证明有界读取没有把正常路径读坏）；
 *   探针 B（缺失元素单读）：readTextBounded 应立即返回 ''（约 0.5s），而不是默认的 30s 白等；
 *   探针 C（15s 档轮询缺失元素）：助手应在自己的 deadline 附近放弃（~15s），而不是被首读拖到 30s；
 *   探针 D（1.2s 晚到元素）：助手应在 deadline 内读到文本，不假红。
 *
 * 用法（仓库根）：node tools/check-bounded-text.mjs
 * 退出码：0 = 探针全过；1 = 有探针失败；2 = 缺 Playwright / Chromium。
 * 不启动宿主与 Vite：只用 Chromium 跑假页面 + 手工注入 DOM。
 */
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import path from 'node:path'
import { readTextBounded } from './lib/bounded-text.mjs'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const requireFromWeb = createRequire(path.join(repositoryRoot, 'web', 'package.json'))

let chromium = null
try {
  ;({ chromium } = requireFromWeb('playwright'))
} catch (error) {
  console.error(`缺少依赖（playwright）：${String(error)}`)
  console.error('先运行：cd web; npm install; npx playwright install chromium')
  process.exit(2)
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))
const results = []

function record(label, pass, detail) {
  results.push({ label, pass })
  console.log(`  ${pass ? '[PASS]' : '[FAIL]'} ${label} → ${detail}`)
}

/** 与装置同款的轮询助手形状：有界读取 + 自己的 deadline（超时返回最后一次读数）。 */
async function pollForText(locator, needle, timeoutMs) {
  const startedAt = Date.now()
  const deadline = startedAt + timeoutMs
  let text = ''
  while (Date.now() < deadline) {
    text = (await readTextBounded(locator)).replace(/\s+/g, ' ').trim()
    if (text.includes(needle)) {
      return { found: true, text, elapsedMs: Date.now() - startedAt }
    }

    await sleep(150)
  }

  return { found: false, text, elapsedMs: Date.now() - startedAt }
}

let browser = null
try {
  browser = await chromium.launch()
} catch (error) {
  console.error(`启动 Chromium 失败：${String(error)}`)
  process.exit(2)
}

try {
  const page = await browser.newPage()
  await page.setContent('<html><body><div id="present">在场文本</div></body></html>')

  // 探针 A：对照——在场元素读到原文。
  const tA = Date.now()
  const present = await readTextBounded(page.locator('#present'))
  const elapsedA = Date.now() - tA
  record('A 在场元素读到原文', present === '在场文本' && elapsedA < 500, `「${present}」· ${elapsedA}ms`)

  // 探针 B：缺失元素单读——旧写法在这里白等 30s。
  const tB = Date.now()
  const missing = await readTextBounded(page.locator('#missing'))
  const elapsedB = Date.now() - tB
  record(
    'B 缺失元素单读立即返回空串',
    missing === '' && elapsedB < 1_500,
    `返回「${missing}」· ${elapsedB}ms（无界写法为 30s）`,
  )

  // 探针 C：15s 档轮询缺失元素——放弃时刻应听自己的 deadline，而不是单次读取的 30s。
  const pollC = await pollForText(page.locator('#missing'), '永远不出现', 15_000)
  record(
    'C 15s 档轮询守时放弃',
    !pollC.found && pollC.elapsedMs >= 14_500 && pollC.elapsedMs <= 16_500,
    `放弃于 ${(pollC.elapsedMs / 1000).toFixed(1)}s（无界写法：首读 30s 后返回）`,
  )

  // 探针 D：元素 1.2s 后注入——deadline 内应读到，不假红。
  setTimeout(() => {
    void page
      .evaluate(() => {
        const late = document.createElement('div')
        late.id = 'late'
        late.textContent = '晚到文本'
        document.body.append(late)
      })
      .catch(() => {})
  }, 1_200)
  const pollD = await pollForText(page.locator('#late'), '晚到文本', 10_000)
  record(
    'D 晚到元素在 deadline 内读到',
    pollD.found && pollD.elapsedMs < 5_000,
    pollD.found ? `读到「${pollD.text}」于 ${(pollD.elapsedMs / 1000).toFixed(1)}s` : `未读到（${pollD.elapsedMs}ms）`,
  )
} finally {
  await browser.close()
}

const failed = results.filter((result) => !result.pass)
console.log(failed.length === 0 ? '\n探针全过：轮询读取有界，deadline 归助手自己' : `\n失败 ${failed.length} 项`)
process.exit(failed.length === 0 ? 0 : 1)
