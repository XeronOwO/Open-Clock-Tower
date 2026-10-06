/**
 * 复盘规模采样装置 —— 票据 docs/backlog/done/replay-auto-review.md 验收矩阵行 8 的真机取证。
 *
 * 它回答：**事件流 ≥ 2000 事件时，复盘的「服务端投影」与「前端翻页」各要多久，
 * 分页 / 懒加载是不是真的按页加载，而不是一次性渲染全部？**
 *
 * 场景（5 席；不跑夜间流程，只用真实命令面把事件流堆到规模）：
 *   1) 起真宿主（独立临时库）→ 夹具账号在账号 Hub 注册并开一桌（D-0027：说书人票据退场、
 *      宿主不再自动建默认桌，"说书人"就是开这一桌的那个账号）→ Node SignalR 客户端带着账号会话进主持台；
 *   2) `AssignCharacters` 建立真实局面，再用 `ReportSeatState` 交替上报 2 号「中毒 / 健康」，
 *      每条一个唯一幂等键——写进去的是**真实事件流**（`SeatStateChangedEvent`），不是手工插库；
 *   3) 断开 Node 客户端（同一桌同一时刻只保留一条有效说书人连接：同一账号的浏览器随后才加入）；
 *   4) 服务端采样：用说书人凭据把整条复盘分页拉完（500 / 页）核对步骤总数与序号递增，
 *      再对首页 / 中段 / 深页各采样 3 次（每次都会从序号 0 折到最新——服务端投影的真实成本）；
 *   5) 前端采样（真 Vite + 真 Chromium 的说书人端）：开面板首屏耗时 → 第 200→201 步跨页耗时
 *      → 深页（第 2000 步）定位耗时 → 第 2000→2001 步跨页耗时；每次跨页都断言已加载窗口
 *      严格按 200 / 页增长（懒加载证据）；截图 `replay-scale-01/02`；
 *   6) 耗时断言给的是宽松上限（只拦「一次性渲染全部」这类回归），真正的证据是打印出的采样数字。
 *
 * 前置：web/node_modules（playwright + @microsoft/signalr）、Chromium；装置**不直读库**
 * （说书人票据已随 D-0027 退场，席位票据只归"玩家入座"那类装置读）。
 * 用法（在仓库根运行）：
 *   node tools/verify-replay-scale.mjs                               # 迭代档：默认 2600 条状态变化，截图不落盘
 *   node tools/verify-replay-scale.mjs --events 600                  # 小规模迭代（深页断言按规模标红）
 *   node tools/verify-replay-scale.mjs --quota 2 --screenshots-all   # 取证档（一批一次，只对冻结版本）
 *
 * 外部耦合（同 web/AGENTS.md §3.1）：宿主编译产物路径、Playwright + Chromium、账号 Hub 的
 * `Register` / `CreateTable` 回执形状（D-0027：说书人身份改走账号，故不再耦合 StorytellerTicket）。
 * 退出码：0 = 全部断言通过；1 = 有失败；2 = 环境缺依赖。
 */
import { spawn } from 'node:child_process'
import { mkdirSync, mkdtempSync, rmSync } from 'node:fs'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
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
const accountHubUrl = `${serverUrl}/hub/account`
const viteUrl = `http://localhost:${options.vitePort}`
/** 游戏 Hub 地址：桌标识要开完桌才知道（D-0027：不声明 `?gameId=` 的连接一律被拒），所以是 `let`。 */
let hubUrl = `${serverUrl}/hub/game`

/** 5 席花名册：够建立真实局面即可，本装置不跑夜间流程。 */
const ASSIGN = ['vortox', 'clockmaker', 'dreamer', 'mutant', 'klutz']
/** 被反复上报的席位：交替「中毒 / 健康」，每条命令产出一条 `SeatStateChangedEvent`。 */
const REPORTER_SEAT = 2

/**
 * 夹具说书人账号（D-0027）：登录名总长受服务端 `UsernameText.MaxLength` = 24 字符约束，
 * 所以前缀 `fixture-host-`（13 字符）配一个 ≤ 11 字符的后缀；越界只回 `invalid_username`，
 * 在装置侧很难查，注册回执不 ok 时这里会连 code / message 一起抛出来。
 */
const FIXTURE_HOST = {
  username: 'fixture-host-replay',
  displayName: '夹具说书人replay',
  password: 'fixture-pw-replay',
  tableName: '复盘规模采样桌',
}

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
  console.log('=== 1/6 构建并启动真宿主（独立临时库，5 席）+ 夹具账号开一桌（D-0027）===')
  await ensureServerArtifacts({ repositoryRoot, buildMode: config.buildMode })
  await startServer()

  // 说书人票据已整个退场、宿主也不再自动建默认桌（D-0027）：装置改走真实用法的那条路——
  // 账号 Hub 注册一个夹具账号（注册即登录）→ 由它开一桌 → 这一桌归它，它就是这一桌的说书人。
  const accountHub = await connectAccountHub()
  const host = await registerFixtureHost(accountHub)
  const created = await accountHub.invoke('CreateTable', host.session, FIXTURE_HOST.tableName, ASSIGN.length)
  if (created?.ok !== true) {
    throw new Error(`开桌失败：${created?.code ?? '未知'} ${created?.message ?? ''}`)
  }

  const gameId = String(created.gameId ?? '')
  if (gameId.length === 0) {
    throw new Error('开桌回执没有桌标识（D-0027：说书人面凭桌标识进主持台）')
  }

  // 账号会话在服务端不绑连接（8 小时到期，见 AccountSessionRegistry），所以这条连接用完即关；
  // 下面所有游戏连接都必须声明桌标识。
  await accountHub.stop()
  hubUrl = `${serverUrl}/hub/game?gameId=${encodeURIComponent(gameId)}`
  console.log(`  夹具账号 ${host.username} 已开桌：game=${gameId}（${created.seatCount} 席）；游戏 Hub ${hubUrl}`)

  console.log(`=== 2/6 真实命令面写入事件流（${options.events} 条状态变化）===`)
  const storyteller = await connectStoryteller(host.session)
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
  // 说书人面走**同一个账号**（D-0027）：Node 客户端已断开，这里登录夹具账号 →「我主持的桌」→ 进主持台。
  await enterHostPanel(page, host, gameId)
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

/** 连账号 Hub：注册与开桌都是**账号面**的事（不过游戏命令的四道闸），走这条独立连接。 */
async function connectAccountHub() {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(accountHubUrl)
    .configureLogging(signalR.LogLevel.None)
    .build()
  await connection.start()
  return connection
}

/** 注册夹具账号：注册即登录，回执里带 `accountSession`（D-0021）；不 ok 就当场抛，不带着半个身份往下跑。 */
async function registerFixtureHost(connection) {
  const account = await connection.invoke(
    'Register',
    FIXTURE_HOST.username,
    FIXTURE_HOST.displayName,
    FIXTURE_HOST.password,
  )
  if (account?.ok !== true || typeof account.accountSession !== 'string' || account.accountSession.length === 0) {
    throw new Error(`夹具账号注册失败：${account?.code ?? '未知'} ${account?.message ?? ''}`)
  }

  return { ...FIXTURE_HOST, session: account.accountSession }
}

/**
 * 浏览器侧进主持台（D-0027 的真实用法）：登录**同一个夹具账号** →「我主持的桌」里点「进主持台」。
 * 界面锚点与 `tools/lib/entrance.mjs` 一致（那边是"注册 + 开桌"一条龙，这里桌已由 Node 侧开好）。
 */
async function enterHostPanel(page, host, gameId) {
  await page.goto(viteUrl)
  const loginTab = page.getByTestId('account-tab-login')
  if ((await loginTab.count()) > 0) {
    await loginTab.click()
  }

  await page.getByTestId('account-username').fill(host.username)
  await page.getByTestId('account-password').fill(host.password)
  await page.getByTestId('account-login').click()
  await page.getByTestId('account-profile').waitFor({ timeout: 30_000 })
  const row = page.locator(`[data-my-table="${gameId}"]`)
  await row.waitFor({ timeout: 30_000 })
  await row.getByTestId('host-enter').click()
  await page.locator('[data-testid="grimoire"]').waitFor({ timeout: 30_000 })
}

/** 说书人客户端（D-0027）：连接声明桌标识，入场出示**账号会话**——只有开这一桌的账号进得来。 */
async function connectStoryteller(accountSession) {
  const connection = new signalR.HubConnectionBuilder().withUrl(hubUrl).configureLogging(signalR.LogLevel.None).build()
  await connection.start()
  const joined = await connection.invoke('JoinStorytellerWithAccount', accountSession)
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
      env: { ...process.env, VITE_SERVER_TARGET: serverUrl },
      stdio: 'ignore',
    },
  )
  children.push(vite)
  await waitForHttp(viteUrl, 'Vite 开发服务器', 60_000)
  return vite
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
