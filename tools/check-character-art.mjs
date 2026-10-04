/**
 * 「角色图热链 + 失败降级」的最小复现（D-0007 的实现 / R-0006 的浏览器侧行为）——
 * 票据 docs/backlog/in-progress/character-art-hotlink.md 验收矩阵行 1–4。
 *
 * 为什么单独有这个脚本：真机装置只跑"图能加载"的正常路径；"图加载失败会不会裂图 / 留白"
 * 只有把**真实组件**挂进真浏览器、人为阻断图片请求才能证明。这里用 Vite 程序化起一个
 * 只挂 `GrimoireSeatCard` 的临时页面（不启动宿主、不碰数据库、不读写业务数据）：
 *   探针 A（正常路径）：已知角色 → `[data-testid="character-art"]` 出现且 naturalWidth > 0；
 *   探针 B（降级）：同一角色 + 阻断百科图片主机 → `<img>` 撤下、角色名仍在（文字 + 色环兜底）；
 *   探针 C（不猜）：未知角色 → 不渲染 `<img>`，且不向百科发起任何请求；
 *   探针 D（局域网 + 移动端仿真）：页面 origin 换成一个局域网 IP（请求经本机代理，不暴露端口）、
 *     上下文用 iPhone 13 设备参数 → 角色图同样加载（R-0006 的"局域网 / 移动端"自动侧证据；
 *     真机（真实手机）仍由使用者在局域网内打开一次确认）。
 *
 * 用法（仓库根）：node tools/check-character-art.mjs
 * 退出码：0 = 探针全过（D 无可用局域网地址时跳过不算失败）；1 = 有探针失败；2 = 缺依赖或外网不可达。
 */
import { createRequire } from 'node:module'
import { fileURLToPath, pathToFileURL } from 'node:url'
import path from 'node:path'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const webRoot = path.join(repositoryRoot, 'web')
const requireFromWeb = createRequire(path.join(webRoot, 'package.json'))

let chromium = null
let devices = null
let createServer = null
try {
  ;({ chromium, devices } = requireFromWeb('playwright'))
  const vitePath = requireFromWeb.resolve('vite')
  ;({ createServer } = await import(pathToFileURL(vitePath).href))
} catch (error) {
  console.error(`缺少依赖（playwright / vite）：${String(error)}`)
  console.error('先运行：cd web; npm install')
  process.exit(2)
}

const results = []
function record(label, pass, detail) {
  results.push({ label, pass })
  console.log(`  ${pass ? '[PASS]' : '[FAIL]'} ${label} → ${detail}`)
}

const PROBE_ENTRY = 'virtual:character-art-probe-entry'
const PROBE_ENTRY_URL = `/@id/__x00__${PROBE_ENTRY}`
const WIKI_IMAGE = 'https://clocktower-wiki.gstonegames.com/images/7/74/Clockmaker.png'
/** 局域网 origin 仿真：浏览器认为页面来自这个地址（实际请求由本机代理转发）。 */
const LAN_ORIGIN = 'http://192.168.77.77:5391'

// 外网预检：探针 A 依赖百科可达；不可达按"缺外部依赖"退出，不把网络问题当产品缺陷。
try {
  const response = await fetch(WIKI_IMAGE, { method: 'HEAD' })
  if (!response.ok) {
    throw new Error(`HTTP ${response.status}`)
  }
} catch (error) {
  console.error(`百科图片不可达（探针 A 的外部依赖）：${String(error)}`)
  process.exit(2)
}

const entrySource = `
import { createApp, h } from 'vue'
import GrimoireSeatCard from '/src/features/grimoire/GrimoireSeatCard.vue'

const params = new URLSearchParams(location.search)
const slug = params.get('slug')
const model = {
  seat: 1,
  displayName: null,
  observed: true,
  character: slug,
  alignment: 'Good',
  life: 'Alive',
  drunk: null,
  poison: null,
  madnesses: [],
  facts: [],
  effects: [],
  annotations: [],
  lostAbilityMarkers: [],
  marks: [],
}

createApp({
  render: () =>
    h(GrimoireSeatCard, {
      model,
      position: null,
      selected: false,
      isCurrentSlot: false,
      hasPending: false,
      hasDecision: false,
    }),
}).mount('#app')
`

const probePlugin = {
  name: 'character-art-probe',
  resolveId(id) {
    return id === PROBE_ENTRY ? `\0${PROBE_ENTRY}` : null
  },
  load(id) {
    return id === `\0${PROBE_ENTRY}` ? entrySource : null
  },
  configureServer(server) {
    server.middlewares.use((request, response, next) => {
      if (!(request.url ?? '').startsWith('/__probe.html')) {
        return next()
      }

      response.setHeader('Content-Type', 'text/html; charset=utf-8')
      response.end(
        `<!doctype html><html lang="zh"><body><div id="app"></div>`
          + `<script type="module" src="${PROBE_ENTRY_URL}"></script></body></html>`,
      )
    })
  },
}

const server = await createServer({
  root: webRoot,
  logLevel: 'warn',
  plugins: [probePlugin],
  server: { host: '127.0.0.1', port: 5391, strictPort: false },
})

/** 等角色图真实解码（naturalWidth > 0）。 */
async function waitForArtLoaded(page) {
  await page.waitForFunction(
    () => {
      const image = document.querySelector('[data-testid="character-art"]')
      return image instanceof HTMLImageElement && image.complete && image.naturalWidth > 0
    },
    null,
    { timeout: 20_000 },
  )
}

let browser = null
try {
  await server.listen()
  const base = server.resolvedUrls?.local?.[0] ?? 'http://127.0.0.1:5391/'
  browser = await chromium.launch()

  // 探针 A：正常路径——真实组件 + 真网络。
  const pageA = await browser.newPage()
  await pageA.goto(`${base}__probe.html?slug=clockmaker`, { waitUntil: 'domcontentloaded' })
  const artA = pageA.locator('[data-testid="character-art"]')
  await artA.waitFor({ state: 'attached', timeout: 20_000 })
  await waitForArtLoaded(pageA)
  const srcA = await artA.getAttribute('src')
  record('A 已知角色：角色图真实加载', srcA === WIKI_IMAGE, `src=${srcA}`)

  // 探针 B：阻断百科图片 → 真实组件的 @error 撤图，文字兜底。
  const pageB = await browser.newPage()
  await pageB.route(/clocktower-wiki\.gstonegames\.com/, (route) => route.abort())
  await pageB.goto(`${base}__probe.html?slug=clockmaker`, { waitUntil: 'domcontentloaded' })
  await pageB.waitForTimeout(1_500)
  const countB = await pageB.locator('[data-testid="character-art"]').count()
  const roleB = (await pageB.locator('.role').innerText()).trim()
  record(
    'B 图片失败：撤下 <img>、角色名兜底',
    countB === 0 && roleB === '钟表匠',
    `img=${countB}・role=${roleB}`,
  )

  // 探针 C：未知角色 → 不渲染 <img>，也不发请求。
  const pageC = await browser.newPage()
  const wikiRequestsC = []
  pageC.on('request', (request) => {
    if (request.url().includes('clocktower-wiki.gstonegames.com')) {
      wikiRequestsC.push(request.url())
    }
  })
  await pageC.goto(`${base}__probe.html?slug=not-a-character`, { waitUntil: 'domcontentloaded' })
  await pageC.waitForTimeout(1_000)
  const countC = await pageC.locator('[data-testid="character-art"]').count()
  const roleC = (await pageC.locator('.role').innerText()).trim()
  record(
    'C 未知角色：不渲染 <img>、不发图片请求',
    countC === 0 && roleC === 'not-a-character' && wikiRequestsC.length === 0,
    `img=${countC}・role=${roleC}・百科请求=${wikiRequestsC.length}`,
  )

  // 探针 D：局域网 origin（仿真）+ 移动端上下文（iPhone 13）→ 图片同样加载。
  // 页面与模块请求经 route 代理回本机 127.0.0.1:5391；不向局域网暴露任何端口。
  const mobile = await browser.newContext({ ...devices['iPhone 13'] })
  const pageD = await mobile.newPage()
  await pageD.route(`${LAN_ORIGIN}/**`, async (route) => {
    const url = new URL(route.request().url())
    const response = await route.fetch({ url: `http://127.0.0.1:5391${url.pathname}${url.search}` })
    await route.fulfill({ response })
  })
  await pageD.goto(`${LAN_ORIGIN}/__probe.html?slug=clockmaker`, { waitUntil: 'domcontentloaded' })
  const artD = pageD.locator('[data-testid="character-art"]')
  await artD.waitFor({ state: 'attached', timeout: 20_000 })
  await waitForArtLoaded(pageD)
  const srcD = await artD.getAttribute('src')
  record(
    'D 局域网 origin + 移动端仿真：角色图同样加载',
    srcD === WIKI_IMAGE,
    `origin=${LAN_ORIGIN}・ua=iPhone 13・src=${srcD}`,
  )
  await mobile.close()
} finally {
  if (browser !== null) {
    await browser.close()
  }
  await server.close()
}

const failed = results.filter((result) => !result.pass)
console.log(failed.length === 0 ? '\n探针全过：热链能显示，失败能降级，未知不猜，局域网 / 移动端同样加载' : `\n失败 ${failed.length} 项`)
process.exit(failed.length === 0 ? 0 : 1)
