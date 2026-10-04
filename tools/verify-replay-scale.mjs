/**
 * 复盘规模采样装置 —— 票据 docs/backlog/review/replay-auto-review.md 验收矩阵行 8 的真机取证。
 *
 * 它回答：**事件流 ≥ 2000 事件时，复盘的「服务端投影」与「前端翻页」各要多久，
 * 分页 / 懒加载是不是真的按页加载，而不是一次性渲染全部？**
 *
 * 场景（5 席；不跑夜间流程，只用真实命令面把事件流堆到规模）：
 *   1) 起真宿主（独立临时库）+ Node SignalR 说书人客户端；
 *   2) `AssignCharacters` 建立真实局面，再用 `ReportSeatState` 交替上报 2 号「中毒 / 健康」，
 *      每条一个唯一幂等键——写进去的是**真实事件流**（`SeatStateChangedEvent`），不是手工插库；
 *   3) 断开 Node 客户端（每席位只保留一条连接：同一票据的浏览器随后才加入）；
 *   4) 服务端采样：用说书人凭据把整条复盘分页拉完（500 / 页）核对步骤总数与序号递增，
 *      再对首页 / 中段 / 深页各采样 3 次（每次都会从序号 0 折到最新——服务端投影的真实成本）；
 *   5) 前端采样（真 Vite + 真 Chromium 的说书人端）：开面板首屏耗时 → 第 200→201 步跨页耗时
 *      → 深页（第 2000 步）定位耗时 → 第 2000→2001 步跨页耗时；每次跨页都断言已加载窗口
 *      严格按 200 / 页增长（懒加载证据）；截图 `replay-scale-01/02`；
 *   6) 耗时断言给的是宽松上限（只拦「一次性渲染全部」这类回归），真正的证据是打印出的采样数字。
 *
 * 前置：Node >= 22.5（node:sqlite）、web/node_modules（playwright + @microsoft/signalr）、Chromium。
 * 用法（在仓库根运行）：
 *   node tools/verify-replay-scale.mjs                               # 迭代档：默认 2600 条状态变化，截图不落盘
 *   node tools/verify-replay-scale.mjs --events 600                  # 小规模迭代（深页断言按规模标红）
 *   node tools/verify-replay-scale.mjs --quota 2 --screenshots-all   # 取证档（一批一次，只对冻结版本）
 *
 * 外部耦合（同 web/AGENTS.md §3.1）：宿主编译产物路径、Games 表的 StorytellerTicket / SeatsJson、
 * Node ≥ 22.5、Playwright + Chromium。
 * 退出码：0 = 全部断言通过；1 = 有失败；2 = 环境缺依赖。
 */
import { spawn } from 'node:child_process'
import { mkdirSync, mkdtempSync, rmSync } from 'node:fs'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { DatabaseSync } from 'node:sqlite'
import { describeProfile, ensureServerArtifacts, extractProfileFlags, resolveProfile } from './lib/verify-profile.mjs'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const webRoot = path.join(repositoryRoot, 'web')
const { flags, rest } = extractProfileFlags(process.argv.slice(2))
const options = parseArguments(rest)
const config = resolveProfile(flags, { quotaSeconds: 0.3 })
const results = []
const children = []
let playwright = null
let signalR = null

try {
  const requireFromWeb = createRequire(path.join(webRoot, 'package.json'))
  playwright = requireFromWeb('playwright')
  signalR = requireFromWeb('@microsoft/signalr')
} catch (error) {
  console.error(`缺少依赖（playwright / @microsoft/signalr）：${String(error)}`)
  console.error('先运行：cd web; npm install; npx playwright install chromium')
  process.exit(2)
}

console.log(`档位：${describeProfile(config)}`)
console.log(`规模：${options.events} 条状态变化（行 8 要求 ≥ 2000 步；深页采样点 ${options.deepStep}）`)

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-replay-scale-'))
const databasePath = path.join(workspace, 'replay-scale.db')
const screenshotsDir = path.resolve(repositoryRoot, 'artifacts', 'web')
mkdirSync(screenshotsDir, { recursive: true })

const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`
const hubUrl = `${serverUrl}/hub/game`

/** 5 席花名册：够建立真实局面即可，本装置不跑夜间流程。 */
const ASSIGN = ['vortox', 'clockmaker', 'dreamer', 'mutant', 'klutz']
/** 被反复上报的席位：交替「中毒 / 健康」，每条命令产出一条 `SeatStateChangedEvent`。 */
const REPORTER_SEAT = 2

process.on('exit', () => killChildren())

try {
  await main()
  await cleanup()
  report()
  process.exit(results.some((result) => !result.pass) ? 1 : 0)
} catch (error) {
  console.error(`\n[FAIL] 批次脚本异常终止：${error instanceof Error ? error.stack : String(error)}`)
  results.push({ label: '脚本执行到底', pass: false, detail: '见上方异常' })
  await cleanup()
  report()
  process.exit(1)
}

async function main() {
  console.log('=== 1/6 构建并启动真宿主（独立临时库，5 席）===')
  await ensureServerArtifacts({ repositoryRoot, buildMode: config.buildMode })
  await startServer()
  const ticket = readStorytellerTicket(databasePath)

  console.log(`=== 2/6 真实命令面写入事件流（${options.events} 条状态变化）===`)
  const storyteller = await connectStoryteller(ticket)
  check('Node 说书人客户端拿到连接级凭据', typeof storyteller.credential === 'string' && storyteller.credential.length > 0)
  const assigned = await storyteller.connection.invoke(
    'AssignCharacters',
    storyteller.credential,
    ASSIGN.map((character, index) => ({ seat: index + 1, character })),
    'scale-assign-1',
  )
  check('开局分配被受理（局面由真实命令建立）', assigned?.kind === 'Accepted', describeOutcome(assigned))

  const loadStartedAt = Date.now()
  let accepted = 0
  for (let index = 0; index < options.events; index += 1) {
    const outcome = await storyteller.connection.invoke(
      'ReportSeatState',
      storyteller.credential,
      REPORTER_SEAT,
      null,
      null,
      null,
      null,
      index % 2 === 0 ? 'Poisoned' : 'Healthy',
      `批次取证：复盘规模采样第 ${index + 1} 次中毒切换`,
      null,
      `scale-report-${index + 1}`,
    )
    if (outcome?.kind === 'Accepted') {
      accepted += 1
    }
  }

  const loadMs = Date.now() - loadStartedAt
  const perCommand = (loadMs / Math.max(1, options.events)).toFixed(2)
  console.log(`  真实命令写入 ${accepted} / ${options.events} 条，用时 ${loadMs}ms（${perCommand}ms/条）`)
  check(
    `真实命令面写入 ${options.events} 条状态变化全部被受理`,
    accepted === options.events,
    `${accepted} / ${options.events}；${loadMs}ms（${perCommand}ms/条）`,
  )

  console.log('=== 3/6 服务端投影采样（分页核对 + 首页 / 中段 / 深页各 3 次）===')
  const sequences = []
  let afterSequence = 0
  let pages = 0
  const pagingStartedAt = Date.now()
  while (true) {
    pages += 1
    const page = await storyteller.connection.invoke('GetReplay', storyteller.credential, afterSequence, 500)
    for (const step of page.steps) {
      sequences.push(step.sequence)
    }

    if (!page.hasMore) {
      break
    }

    afterSequence = page.steps[page.steps.length - 1].sequence
    if (pages > 40) {
      throw new Error('复盘分页 40 页仍未收敛：分页契约异常')
    }
  }

  const pagingMs = Date.now() - pagingStartedAt
  check(
    `复盘步骤总数 ≥ 2000（实际 ${sequences.length}；分页 ${pages} 页 / ${pagingMs}ms）`,
    sequences.length >= 2000,
    `--events ${options.events} 时实际 ${sequences.length} 步`,
  )
  const ordered = sequences.every((value, index) => index === 0 || value > sequences[index - 1])
  check(
    '服务端分页返回的步骤序号严格递增（不重不漏）',
    ordered,
    ordered ? `首 ${sequences[0]} → 末 ${sequences[sequences.length - 1]}` : '存在非递增序号',
  )

  const middleAfter = sequences[Math.min(999, Math.max(0, sequences.length - 1))] ?? 0
  const deepAfter = sequences[Math.max(0, sequences.length - 201)] ?? 0
  for (const probe of [
    { label: '首页（after=0）', after: 0 },
    { label: `中段（after=${middleAfter}）`, after: middleAfter },
    { label: `深页（after=${deepAfter}）`, after: deepAfter },
  ]) {
    const samples = []
    for (let index = 0; index < 3; index += 1) {
      samples.push(await timed(() => storyteller.connection.invoke('GetReplay', storyteller.credential, probe.after, 200)))
    }

    const stats = describeSamples(samples)
    check(`服务端投影 ${probe.label}：${stats.text}（每次 ≤ 2000ms）`, stats.max <= 2000, stats.text)
  }

  await storyteller.dispose()

  console.log('=== 4/6 前端采样：真 Vite + 真 Chromium（说书人端）===')
  await startVite()
  const browser = await playwright.chromium.launch()
  const consoleErrors = []
  const page = await newPage(browser, { width: 1600, height: 1100 }, consoleErrors)
  await page.goto(viteUrl)
  await page.getByPlaceholder('说书人票据').fill(ticket)
  await page.getByRole('button', { name: '加入' }).click()
  await page.locator('[data-testid="grimoire"]').waitFor({ timeout: 30_000 })
  check('说书人真浏览器加入成功（真 Vite + 真宿主）', (await page.locator('[data-testid="grimoire"]').count()) === 1)
  await page.getByTestId('storyteller-replay-open').waitFor({ timeout: 30_000 })

  const firstPage = await openReplayAndWait(page, 1)
  check(
    `前端首屏：打开面板拿到第一页（${Math.round(firstPage.ms)}ms；已加载窗口 200 步，不一次全渲染）`,
    firstPage.text.startsWith('第 1 / 200 '),
    firstPage.text,
  )
  check(`前端首屏耗时 ≤ 10000ms（${Math.round(firstPage.ms)}ms）`, firstPage.ms <= 10_000, `${Math.round(firstPage.ms)}ms`)
  await screenshot(page, 'replay-scale-01-first-page')

  const walk = await walkReplayTo(page, 200)
  check('走到第 200 步（已加载窗口 200 步，尚未触发下一页）', walk.text.startsWith('第 200 / 200 '), walk.text)

  const firstCross = await crossReplayPage(page)
  check(
    `前端翻页：第 200→201 步跨页（${Math.round(firstCross.ms)}ms；窗口 200 → 400）`,
    firstCross.after?.step === 201 && firstCross.after?.loaded === 400,
    `${firstCross.before?.text ?? '—'} ⇒ ${firstCross.text}`,
  )
  check(`前端跨页耗时 ≤ 3000ms（${Math.round(firstCross.ms)}ms）`, firstCross.ms <= 3000, `${Math.round(firstCross.ms)}ms`)

  const deepStep = options.deepStep
  if (sequences.length >= deepStep + 200) {
    const deepTarget = sequences[deepStep - 1]
    await page.evaluate((target) => {
      window.location.hash = `?replay=${target}`
    }, deepTarget)
    await page.getByTestId('replay-close').click()
    await page.getByTestId('replay-panel').waitFor({ state: 'detached', timeout: 10_000 })

    const deepOpen = await openReplayAndWait(page, deepStep)
    const deepProgress = parseProgress(deepOpen.text)
    check(
      `前端深页定位：按事件序号重建到第 ${deepStep} 步（${Math.round(deepOpen.ms)}ms；窗口 ${deepProgress?.loaded ?? '—'} 步）`,
      deepProgress !== null && deepProgress.step === deepStep && deepProgress.loaded === deepStep,
      deepOpen.text,
    )

    const deepCross = await crossReplayPage(page)
    check(
      `前端深页翻页：第 ${deepStep}→${deepStep + 1} 步跨页（${Math.round(deepCross.ms)}ms；窗口 → ${deepStep + 200}）`,
      deepCross.after?.step === deepStep + 1 && deepCross.after?.loaded === deepStep + 200,
      `${deepCross.before?.text ?? '—'} ⇒ ${deepCross.text}`,
    )
    check(`前端深页跨页耗时 ≤ 3000ms（${Math.round(deepCross.ms)}ms）`, deepCross.ms <= 3000, `${Math.round(deepCross.ms)}ms`)
    await screenshot(page, 'replay-scale-02-deep-page')
  } else {
    check(
      `事件规模满足深页采样（需要 ≥ ${deepStep + 200} 步，实际 ${sequences.length}）`,
      false,
      '迭代档 --events 调小时该断言按规模标红；取证档用默认 2600',
    )
  }

  console.log('=== 5/6 收口 ===')
  check('浏览器控制台没有报错', consoleErrors.length === 0, consoleErrors.slice(0, 3).join(' | '))
  await browser.close()
  console.log('=== 6/6 采样数字见上方各项断言与日志；截图目录 artifacts/web/ ===')
}

/** 打开复盘面板并等进度文本到「第 expected 步」；返回耗时与实际进度文本。 */
async function openReplayAndWait(page, expectedStep) {
  return page.evaluate(
    async ({ expected }) => {
      const read = () => {
        const progress = document.querySelector('[data-testid="replay-progress"]')
        return (progress?.textContent ?? '').replace(/\s+/g, ' ').trim()
      }

      const startedAt = performance.now()
      document.querySelector('[data-testid="storyteller-replay-open"]').click()
      const deadline = performance.now() + 60_000
      while (performance.now() < deadline) {
        if (read().startsWith(`第 ${expected} /`)) {
          return { ms: performance.now() - startedAt, text: read() }
        }

        await new Promise((resolve) => setTimeout(resolve, 5))
      }

      return { ms: performance.now() - startedAt, text: read() }
    },
    { expected: expectedStep },
  )
}

/** 在复盘面板上连点「下一步」直到进度到第 wanted 步（只走页内，不点出跨页）。 */
async function walkReplayTo(page, wanted) {
  return page.evaluate(
    async ({ target }) => {
      const read = () => {
        const progress = document.querySelector('[data-testid="replay-progress"]')
        return (progress?.textContent ?? '').replace(/\s+/g, ' ').trim()
      }
      const next = document.querySelector('[data-testid="replay-next"]')
      const deadline = performance.now() + 30_000
      while (performance.now() < deadline && !read().startsWith(`第 ${target} /`)) {
        next.click()
        await new Promise((resolve) => setTimeout(resolve, 0))
      }

      return { text: read() }
    },
    { target: wanted },
  )
}

/** 从当前步点一次「下一步」，等已加载窗口增长（= 真正拉了下一页）；返回前后快照与耗时。 */
async function crossReplayPage(page) {
  return page.evaluate(async () => {
    const read = () => {
      const progress = document.querySelector('[data-testid="replay-progress"]')
      return (progress?.textContent ?? '').replace(/\s+/g, ' ').trim()
    }
    const parse = (text) => {
      const match = /第 (\d+) \/ (\d+) 步/.exec(text)
      return match === null ? null : { step: Number(match[1]), loaded: Number(match[2]), text }
    }

    const next = document.querySelector('[data-testid="replay-next"]')
    const before = parse(read())
    const startedAt = performance.now()
    next.click()
    const deadline = performance.now() + 30_000
    while (performance.now() < deadline) {
      const after = parse(read())
      if (after !== null && before !== null && after.loaded > before.loaded) {
        return { ms: performance.now() - startedAt, before, after, text: after.text }
      }

      await new Promise((resolve) => setTimeout(resolve, 5))
    }

    return { ms: performance.now() - startedAt, before, after: parse(read()), text: read() }
  })
}

/** 页面级进度文本 → { step, loaded }；解析不了返回 null。 */
function parseProgress(text) {
  const match = /第 (\d+) \/ (\d+) 步/.exec(String(text ?? ''))
  return match === null ? null : { step: Number(match[1]), loaded: Number(match[2]) }
}

/** 计时一次异步动作，返回毫秒。 */
async function timed(action) {
  const startedAt = process.hrtime.bigint()
  await action()
  return Number(process.hrtime.bigint() - startedAt) / 1e6
}

function describeSamples(samples) {
  const sorted = [...samples].sort((left, right) => left - right)
  const min = sorted[0] ?? 0
  const median = sorted[Math.floor(sorted.length / 2)] ?? 0
  const max = sorted[sorted.length - 1] ?? 0
  return {
    min,
    median,
    max,
    text: `min ${Math.round(min)}ms / 中位 ${Math.round(median)}ms / max ${Math.round(max)}ms`,
  }
}

function describeOutcome(outcome) {
  if (outcome === null || typeof outcome !== 'object') {
    return `（无回执：${String(outcome)}）`
  }

  return `${outcome.kind ?? '?'}${outcome.message ? `：${outcome.message}` : ''}`
}

async function connectStoryteller(ticket) {
  const connection = new signalR.HubConnectionBuilder().withUrl(hubUrl).configureLogging(signalR.LogLevel.None).build()
  await connection.start()
  const joined = await connection.invoke('JoinStoryteller', ticket)
  return {
    connection,
    credential: joined?.credential,
    dispose: () => connection.stop(),
  }
}

async function newPage(browser, viewport, consoleErrors) {
  const context = await browser.newContext({ viewport })
  const page = await context.newPage()
  page.on('console', (message) => {
    if (message.type() === 'error') {
      consoleErrors.push(message.text())
    }
  })
  page.on('pageerror', (error) => consoleErrors.push(String(error)))
  return page
}

async function screenshot(page, name) {
  if (!config.screenshots) {
    return
  }

  const target = path.join(screenshotsDir, `${name}.png`)
  await page.screenshot({ path: target, fullPage: true })
  console.log(`  截图：${target}`)
}

function check(label, pass, detail = '') {
  results.push({ label, pass: Boolean(pass), detail })
  console.log(`  ${pass ? '[PASS]' : '[FAIL]'} ${label}${detail ? ` → ${detail}` : ''}`)
}

function report() {
  console.log('\n=== 复盘规模采样装置取证结论 ===')
  for (const result of results) {
    console.log(`${result.pass ? 'PASS' : 'FAIL'}  ${result.label}${result.detail ? ` → ${result.detail}` : ''}`)
  }

  const failed = results.filter((result) => !result.pass)
  console.log(failed.length === 0 ? `全部通过（${results.length} 项）` : `失败 ${failed.length} / ${results.length}`)
}

/** 起一个真宿主进程（独立临时库；本装置不跑夜间流程，席位 5 与 ASSIGN 同数）。 */
async function startServer() {
  const executableSuffix = process.platform === 'win32' ? '.exe' : ''
  const serverExecutable = path.join(
    repositoryRoot,
    'src',
    'OpenClockTower.Server',
    'bin',
    'Release',
    'net10.0',
    `OpenClockTower.Server${executableSuffix}`,
  )
  const child = spawn(serverExecutable, [], {
    cwd: repositoryRoot,
    env: {
      ...process.env,
      ASPNETCORE_URLS: serverUrl,
      GameServer__DatabasePath: databasePath,
      GameServer__SeatCount: String(ASSIGN.length),
      GameServer__SlotQuotaSeconds: String(config.quotaSeconds),
      GameServer__PacerIntervalMilliseconds: '200',
      DOTNET_ENVIRONMENT: 'Production',
    },
    stdio: 'ignore',
  })
  children.push(child)
  await waitForHttp(`${serverUrl}/healthz`, '宿主 /healthz', 90_000)
  return child
}

async function startVite() {
  const vite = spawn(
    process.execPath,
    [
      path.join(webRoot, 'node_modules', 'vite', 'bin', 'vite.js'),
      '--port',
      String(options.vitePort),
      '--strictPort',
    ],
    {
      cwd: webRoot,
      env: { ...process.env, VITE_SERVER_TARGET: serverUrl, VITE_SEAT_COUNT: String(ASSIGN.length) },
      stdio: 'ignore',
    },
  )
  children.push(vite)
  await waitForHttp(viteUrl, 'Vite 开发服务器', 60_000)
  return vite
}

function readStorytellerTicket(databasePathToRead) {
  const database = new DatabaseSync(databasePathToRead, { readOnly: true })
  try {
    const row = database.prepare('SELECT StorytellerTicket FROM Games LIMIT 1').get()
    if (row === undefined || typeof row.StorytellerTicket !== 'string') {
      throw new Error('数据库里没有说书人票据')
    }

    return row.StorytellerTicket
  } finally {
    database.close()
  }
}

function parseArguments(argv) {
  const parsed = { port: 5433, vitePort: 5313, events: 2600, deepStep: 2000 }
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index]
    if (argument === '--port') {
      parsed.port = Number.parseInt(argv[index + 1] ?? '', 10)
      index += 1
    } else if (argument === '--vite-port') {
      parsed.vitePort = Number.parseInt(argv[index + 1] ?? '', 10)
      index += 1
    } else if (argument === '--events') {
      parsed.events = Number.parseInt(argv[index + 1] ?? '', 10)
      index += 1
    } else if (argument === '--deep-step') {
      parsed.deepStep = Number.parseInt(argv[index + 1] ?? '', 10)
      index += 1
    }
  }

  for (const [name, value] of [
    ['--port', parsed.port],
    ['--vite-port', parsed.vitePort],
    ['--events', parsed.events],
    ['--deep-step', parsed.deepStep],
  ]) {
    if (!Number.isFinite(value) || value <= 0) {
      throw new Error(`${name} 非法：${value}`)
    }
  }

  return parsed
}

async function waitForHttp(url, label, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  let lastError = '未请求'
  while (Date.now() < deadline) {
    try {
      const response = await fetch(url)
      if (response.ok) {
        console.log(`  ${label} 就绪：${url}`)
        return
      }

      lastError = `HTTP ${response.status}`
    } catch (error) {
      lastError = error instanceof Error ? error.message : String(error)
    }

    await sleep(200)
  }

  throw new Error(`${label} 在 ${timeoutMs}ms 内没有就绪：${lastError}`)
}

async function cleanup() {
  killChildren()
  await waitForChildrenExit(10_000)
  await sleep(500)
  try {
    rmSync(workspace, { recursive: true, force: true })
    console.log(`临时目录已清理：${workspace}`)
  } catch (error) {
    console.warn(`临时目录未能删除：${workspace}（${error instanceof Error ? error.message : String(error)}）`)
  }
}

function killChildren() {
  for (const child of children) {
    if (child.exitCode === null && child.pid !== undefined) {
      try {
        if (process.platform === 'win32') {
          spawn('taskkill', ['/pid', String(child.pid), '/F'], { stdio: 'ignore' })
        } else {
          child.kill('SIGTERM')
        }
      } catch {
        // 收尾尽力而为：进程可能已经退出。
      }
    }
  }
}

async function waitForChildrenExit(timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if (children.every((child) => child.exitCode !== null || child.signalCode !== null)) {
      return
    }

    await sleep(100)
  }
}

function sleep(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds))
}
