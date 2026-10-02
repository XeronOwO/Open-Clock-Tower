/**
 * 女巫（witch）批次装置 —— 票据 docs/backlog/done/witch-curse.md 的验收矩阵。
 *
 * 它回答：**「夜晚施加的诅咒在下个白天触发」这条链路，在真的界面上一路跑得通吗？**
 * 场景（固定 4 席，存活 4 > 3 —— 女巫保住能力的最小局面）：
 *   1) 分配 1=女巫 / 2=钟表匠 / 3=筑梦师 / 4=诺-达鲺 → 开首夜；
 *   2) 1 号（女巫）在真玩家页面上收到选择请求，选 4 号 → 魔典牌面出现「被诅咒」提示标记；
 *   3) 说书人结清钟表匠裁定点、强制作废筑梦师的请求，把这一夜走完 → 开白天；
 *   4) 4 号（被诅咒者）在自己的页面上提名 1 号 → 提名成立、他当场死亡、存活掉到 3；
 *      女巫因此立即失去能力，诅咒在同一批提交里解除（百科《女巫》角色简介 / 范例 4）；
 *   5) 全程扫描三个 SignalR 席位（女巫 + 两名无关玩家）收到的每一条消息：玩家端不得出现
 *      诅咒 / 效果链 / 归因字段（被诅咒席 4 号走真浏览器，另行断言页面文本与积分）。
 *
 * 与主批次的分工：主批次（verify-storyteller-panel.mjs）跑三席固定花名册的通用玩法回归，
 * 本装置只跑女巫这一条能力链路，且**必须**是 4 席（三席局里女巫按规则根本没有诅咒）。
 *
 * 前置：Node >= 22.5（node:sqlite）、本机已构建 web/node_modules（playwright + @microsoft/signalr）。
 * 用法（在仓库根运行）：
 *   node tools/verify-witch.mjs
 *   node tools/verify-witch.mjs --port 5411 --vite-port 5291 --quota 1
 *
 * 外部耦合（换机器先核对 web/AGENTS.md §3.1）：宿主编译产物路径、SQLite 表 Games 的
 * StorytellerTicket / SeatsJson 列形状。退出码：0 = 全过；1 = 有失败；2 = 环境缺依赖。
 */
import { spawn } from 'node:child_process'
import { mkdirSync, mkdtempSync, rmSync } from 'node:fs'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { DatabaseSync } from 'node:sqlite'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const webRoot = path.join(repositoryRoot, 'web')
const options = parseArguments(process.argv.slice(2))
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

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-witch-'))
const databasePath = path.join(workspace, 'witch.db')
const screenshotsDir = path.resolve(repositoryRoot, 'artifacts', 'web')
mkdirSync(screenshotsDir, { recursive: true })

const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`
const hubUrl = `${serverUrl}/hub/game`

/** 四个席位（与 web/src/display/labels.ts 的花名册一致）。 */
const ASSIGN = ['witch', 'clockmaker', 'dreamer', 'no-dashii']
const WITCH_SEAT = 1
const CURSED_SEAT = 4

/** 玩家客户端可能收到的全部推送（扫描越权字段用）。 */
const PUSH_METHODS = [
  'ReceiveOperationRequest',
  'ReceiveOperationRequestVoided',
  'ReceiveOperationRequestAnswered',
  'ReceivePhaseStarted',
  'ReceiveInformationResult',
  'ReceiveDayChanged',
]

/**
 * 玩家端不该出现的词：诅咒 / 效果链 / 归因（D-0012 §4.3 的信息隔离）。
 * 注意**不要**把 `sv:night-` 整串列进来：计划标识会出现在操作请求标识里（`sv:night-1:witch`），
 * 那是协议管线（阶段本身就是公开信息），不是诅咒泄漏——要抓的是效果标识 `witch:curse` 本身。
 */
const FORBIDDEN_PLAYER_TOKENS = [
  'witch.curse',
  'witch:curse',
  '被诅咒',
  'effects',
  'effectId',
  'causedBy',
  'sourceCharacter',
  'terminated',
  'termination',
  'madnesses',
]

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
  console.log('=== 1/7 构建并启动真宿主（独立临时库，4 席）===')
  await runProcess('dotnet', ['build', 'src/OpenClockTower.Server', '-c', 'Release'], repositoryRoot)
  await startServer()

  console.log('=== 2/7 取票据并起 Vite ===')
  const ticket = readStorytellerTicket(databasePath)
  const seatTickets = readSeatTickets(databasePath)
  check('席位票据齐备（4 席）', seatTickets.length === 4, `数据库 ${seatTickets.length} 张`)

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

  console.log('=== 3/7 说书人 + 被诅咒席（4 号）加入真浏览器 ===')
  const browser = await playwright.chromium.launch()
  const consoleErrors = []
  const storytellerPage = await newPage(browser, { width: 1600, height: 1100 }, consoleErrors)
  await storytellerPage.goto(viteUrl)
  await storytellerPage.getByPlaceholder('说书人票据').fill(ticket)
  await storytellerPage.getByRole('button', { name: '加入' }).click()
  await storytellerPage.locator('[data-testid="grimoire"]').waitFor({ timeout: 30_000 })
  check('说书人加入后看板可见（魔典主视图）', (await storytellerPage.locator('[data-testid="grimoire"]').count()) === 1)

  const cursedPage = await newPage(browser, { width: 900, height: 1000 }, consoleErrors)
  await cursedPage.goto(`${viteUrl}/#player`)
  await cursedPage.getByPlaceholder('席位票据').fill(seatTickets[CURSED_SEAT - 1].ticket)
  await cursedPage.getByRole('button', { name: '加入' }).click()
  const cursedSeatBadge = await waitForText(cursedPage.locator('[data-testid="player-seat"]'), '4', 30_000)
  check('被诅咒席（4 号）加入玩家端', cursedSeatBadge.includes('4'), cursedSeatBadge)

  console.log('=== 4/7 开局分配 → 开首夜 ===')
  const assignmentSelects = storytellerPage.locator('section', { hasText: '开局分配' }).locator('select')
  for (const [index, slug] of ASSIGN.entries()) {
    await assignmentSelects.nth(index).selectOption(slug)
  }

  const assigned = await runCommand(storytellerPage, '分配', () =>
    storytellerPage.getByRole('button', { name: '提交分配' }).click(),
  )
  check('分配 4 个角色被受理', assigned.kind === 'Accepted', assigned.raw)

  const witchSeat = await connectSeat(seatTickets[WITCH_SEAT - 1])
  // 另外两席（钟表匠 / 筑梦师）是**无关玩家**：它们收到的每一条推送同样进越权扫描。
  const unrelatedSeats = [
    await connectSeat(seatTickets[1]),
    await connectSeat(seatTickets[2]),
  ]
  const nightStarted = await runCommand(storytellerPage, '开夜', () =>
    storytellerPage.getByRole('button', { name: /开夜/ }).click(),
  )
  check('开夜被受理（真实顺序表建表）', nightStarted.kind === 'Accepted', nightStarted.raw)

  console.log('=== 5/7 女巫选人 → 魔典出现「被诅咒」提示标记 ===')
  const requestArrived = await waitUntil(() => witchSeat.requests.length > 0, 60_000)
  check('女巫席位收到操作请求', requestArrived, `收到 ${witchSeat.requests.length} 条`)
  const request = witchSeat.requests[0]
  check('请求上下文说明是女巫的选择', typeof request?.context === 'string' && request.context.includes('女巫'), request?.context ?? '（无）')
  check(
    '合法选项 = 全体席位（含自己）',
    JSON.stringify((request?.options ?? []).map((option) => option.value)) ===
      JSON.stringify(['seat:1', 'seat:2', 'seat:3', 'seat:4']),
    JSON.stringify((request?.options ?? []).map((option) => option.value)),
  )

  const answered = await witchSeat.invoke('SubmitResponse', request.requestId, `seat:${CURSED_SEAT}`, 'witch-answer', 1)
  check('女巫选择 4 号被受理', answered?.kind === 'Accepted', describeOutcome(answered))

  const cursedCard = storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${CURSED_SEAT}"]`)
  const curseMarkText = await waitForLocatorContains(cursedCard, '被诅咒', 30_000)
  check('魔典牌面出现「被诅咒」提示标记', curseMarkText.includes('被诅咒'), compact(curseMarkText))
  check(
    '被诅咒者此刻仍然存活（诅咒还没触发）',
    (await cursedCard.getAttribute('data-life')) === 'Alive',
    `data-life=${await cursedCard.getAttribute('data-life')}`,
  )
  await screenshot(storytellerPage, 'witch-01-curse-mark')

  await setDataDrawer(storytellerPage, true)
  const effectChain = await panelText(storytellerPage, '效果归因链')
  check(
    '效果链里看得见这条诅咒（生效中）',
    effectChain.includes('witch.curse') && effectChain.includes('生效中'),
    compact(effectChain),
  )
  await screenshot(storytellerPage, 'witch-02-effect-chain')
  await setDataDrawer(storytellerPage, false)

  console.log('=== 6/7 走完首夜 → 开白天 → 被诅咒者提名即死 ===')
  const decided = await waitForDecision(storytellerPage, (text) => text.includes('钟表匠'), 60_000)
  check('钟表匠槽位给说书人裁定点', decided.includes('钟表匠'), compact(decided))
  const settled = await settleFreeDecision(storytellerPage, '批次取证-钟表匠信息：本夜最小距离 2')
  check('钟表匠裁定被受理', settled.kind === 'Accepted', settled.raw)

  const voided = await forceVoidPending(storytellerPage, 'StorytellerForce', '批次取证：与女巫无关的槽位')
  check('筑梦师请求被说书人强制作废', voided.kind === 'Accepted', voided.raw)

  const dayStarted = await runCommand(storytellerPage, '开白天', async () => {
    await waitForEnabled(storytellerPage.getByTestId('st-start-day'), 120_000)
    await storytellerPage.getByTestId('st-start-day').click()
  })
  check('白天阶段：开白天被受理', dayStarted.kind === 'Accepted', dayStarted.raw)
  const dayPanel = storytellerPage.getByTestId('st-day')
  const dayStatus = await waitForAttribute(dayPanel, 'data-day-status', 'Open', 30_000)
  check('说书人面板进入「白天进行中」', dayStatus === 'Open', `data-day-status=${dayStatus}`)

  // 被诅咒者在自己的页面上提名 → 提名成立，他当场死亡（票面/提名仍在）。
  await cursedPage.getByTestId('player-nominee-select').selectOption(String(WITCH_SEAT))
  await cursedPage.getByTestId('player-nominate').click()

  const nominations = storytellerPage.getByTestId('st-day-nominations')
  const nominationCount = await waitForAttribute(nominations, 'data-nomination-count', '1', 30_000)
  check('提名进入公开账目（白天公开事实）', nominationCount === '1', `data-nomination-count=${nominationCount}`)
  const deadLife = await waitForAttribute(cursedCard, 'data-life', 'Dead', 30_000)
  check('被诅咒者提名后死亡（魔典牌面翻成已死亡）', deadLife === 'Dead', `data-life=${deadLife}`)
  await screenshot(storytellerPage, 'witch-03-nominated-and-dead')

  const dayPanelText = compact(await dayPanel.innerText())
  check(
    '提名仍然生效：投票窗口还开着、还没有「即将被处决」',
    dayPanelText.includes('投票') && !dayPanelText.includes('即将被处决'),
    compact(dayPanelText),
  )

  await setDataDrawer(storytellerPage, true)
  const afterChain = await panelText(storytellerPage, '效果归因链')
  check(
    '存活降到 3 → 女巫失去能力 → 诅咒已终止（条件不再满足）',
    afterChain.includes('witch.curse') && afterChain.includes('已终止') && afterChain.includes('条件不再满足'),
    compact(afterChain),
  )
  const changes = await panelText(storytellerPage, '最近状态变化')
  check('死亡事实带得出归因（女巫的诅咒）', changes.includes('女巫'), compact(changes))
  await screenshot(storytellerPage, 'witch-04-effect-chain-terminated')
  await setDataDrawer(storytellerPage, false)

  console.log('=== 7/7 视角隔离：玩家端没有这条说书人专属事实 ===')
  const cursedPageText = compact(await cursedPage.locator('body').innerText())
  check(
    '被诅咒者自己的页面上没有诅咒 / 效果链字样',
    !cursedPageText.includes('被诅咒') && !cursedPageText.includes('witch.curse'),
    compact(cursedPageText),
  )
  check(
    '被诅咒者页面上看得到提名这一公开事实',
    (await cursedPage.locator('[data-testid="player-day-nominations"]').count()) > 0,
  )

  // R-0022：白天咒杀**即时**进入公开生死面——被诅咒者自己的界面显式可见（横幅 + 自己席位翻死亡 +
  // 本日死亡公告），且公告只含"谁死了"，不含死因 / 来源 / 效果。
  const selfDeadBanner = await cursedPage.getByTestId('player-self-dead').count()
  const selfLife = await waitForAttribute(
    cursedPage.locator(`[data-testid="player-lives"] li[data-seat="${CURSED_SEAT}"]`),
    'data-life',
    'Dead',
    30_000,
  )
  const announcedDeath = await cursedPage
    .locator(`[data-testid="player-life-announcements"] li[data-seat="${CURSED_SEAT}"][data-state="Dead"]`)
    .count()
  check(
    'R-0022：自己的死亡显式可见（横幅 + 自己席位翻死亡 + 本日死亡公告）',
    selfDeadBanner >= 1 && selfLife === 'Dead' && announcedDeath >= 1,
    `横幅=${selfDeadBanner}；牌面=${selfLife}；公告=${announcedDeath}`,
  )
  const announcementText = compact(
    await cursedPage.locator('[data-testid="player-life-announcements"]').innerText(),
  )
  check(
    'R-0022：公告只含"谁死了"，不含死因 / 来源',
    announcementText.includes(`${CURSED_SEAT} 号`)
      && announcementText.includes('死亡')
      && !announcementText.includes('女巫')
      && !announcementText.includes('witch.curse'),
    announcementText,
  )

  await screenshot(cursedPage, 'witch-05-cursed-player-view')

  const scannedSeats = [witchSeat, ...unrelatedSeats]
  const receivedText = JSON.stringify(scannedSeats.map((client) => client.messages))
  const leaked = FORBIDDEN_PLAYER_TOKENS.filter((token) => receivedText.includes(token))
  check(
    '三席（女巫 + 两名无关玩家）收到的全部推送里没有越权字段',
    leaked.length === 0,
    leaked.join(', ') || `已扫描 ${scannedSeats.reduce((total, client) => total + client.messages.length, 0)} 条`,
  )

  check('浏览器控制台没有报错', consoleErrors.length === 0, consoleErrors.slice(0, 3).join(' | '))
  await browser.close()
}

/** 连一个真 SignalR 席位：记录每一次请求与每一条推送（供越权扫描）。 */
async function connectSeat(seatTicket) {
  const connection = new signalR.HubConnectionBuilder().withUrl(hubUrl).configureLogging(signalR.LogLevel.None).build()
  const requests = []
  const messages = []
  connection.on('ReceiveOperationRequest', (payload) => {
    requests.push(payload)
    messages.push({ method: 'ReceiveOperationRequest', payload })
  })
  for (const method of PUSH_METHODS.filter((candidate) => candidate !== 'ReceiveOperationRequest')) {
    connection.on(method, (payload) => messages.push({ method, payload }))
  }

  await connection.start()
  const joined = await connection.invoke('JoinSeat', seatTicket.ticket, 0)
  return {
    requests,
    messages,
    invoke: (method, ...args) => connection.invoke(method, joined.credential, ...args),
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

/** 说书人面板上的命令：点按钮 → 等一条新回执（序号必须比点击前大）。 */
async function runCommand(page, label, click) {
  const baseline = await readOutcomeSerial(page)
  await click()
  try {
    return await waitForOutcome(page, baseline, 60_000)
  } catch (error) {
    console.error(`[命令失败] ${label}：${error instanceof Error ? error.message : String(error)}`)
    throw error
  }
}

async function readOutcomeSerial(page) {
  return page
    .locator('[data-testid="outcome"]')
    .evaluate((element) => Number(element.getAttribute('data-outcome-serial') ?? '0'))
    .catch(() => 0)
}

async function waitForOutcome(page, baselineSerial, timeoutMs) {
  const box = page.locator('[data-testid="outcome"]')
  await box.waitFor({ state: 'visible', timeout: timeoutMs })
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    const state = await box.evaluate((element) => ({
      serial: Number(element.getAttribute('data-outcome-serial') ?? '0'),
      kind: element.querySelector('[data-outcome-marker]')?.getAttribute('data-outcome-marker') ?? null,
    }))
    if (state.serial > baselineSerial && state.kind !== null) {
      return { kind: state.kind, raw: compact(await box.innerText()) }
    }

    await sleep(100)
  }

  throw new Error(`没有等到新的命令回执（基线 ${baselineSerial}）`)
}

/** 说书人按自由决定结清当前裁定点。 */
async function settleFreeDecision(page, content) {
  await page.getByPlaceholder('自由决定的内容（可为空）').fill(content)
  return runCommand(page, '裁定', () => page.getByRole('button', { name: '按自由决定结清' }).click())
}

/** 说书人强制作废当前挂起的请求。 */
async function forceVoidPending(page, reason, note) {
  const pending = page.locator('[data-testid="console-pending"]')
  await pending.waitFor({ state: 'visible', timeout: 60_000 })
  await pending.locator('select').selectOption(reason)
  if (note) {
    await pending.locator('input[placeholder="作废说明（可选）"]').fill(note)
  }

  return runCommand(page, '强制作废', () => pending.getByRole('button', { name: '强制作废', exact: true }).click())
}

async function readDecisionText(page) {
  const block = page.locator('[data-testid="console-decision"]')
  if ((await block.count()) === 0) {
    return ''
  }

  return compact(await block.first().innerText())
}

async function waitForDecision(page, predicate, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  let text = ''
  while (Date.now() < deadline) {
    text = await readDecisionText(page)
    if (text.length > 0 && predicate(text)) {
      return text
    }

    await sleep(200)
  }

  return text
}

async function setDataDrawer(page, open) {
  const toggle = page.locator('[data-testid="data-drawer-toggle"]')
  if ((await toggle.getAttribute('aria-expanded')) === String(open)) {
    return
  }

  await toggle.click()
  await sleep(150)
}

/** 数据抽屉里某一段的文本（当前步骤 / 状态账 / 最近状态变化 / 效果归因链 / 两本账）。 */
async function panelText(page, heading) {
  const drawer = page.locator('[data-testid="data-drawer-body"]')
  const section = drawer.locator('section.panel', { hasText: heading })
  if ((await section.count()) === 0) {
    return ''
  }

  return compact(await section.first().innerText())
}

async function waitForAttribute(locator, name, expected, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  let value = null
  while (Date.now() < deadline) {
    value = await locator.getAttribute(name)
    if (value === expected) {
      return value
    }

    await sleep(150)
  }

  return value
}

async function waitForText(locator, expected, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  let text = ''
  while (Date.now() < deadline) {
    text = compact(await locator.innerText().catch(() => ''))
    if (text.includes(expected)) {
      return text
    }

    await sleep(150)
  }

  return text
}

async function waitForLocatorContains(locator, needle, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  let text = ''
  while (Date.now() < deadline) {
    text = compact(await locator.innerText().catch(() => ''))
    if (text.includes(needle)) {
      return text
    }

    await sleep(150)
  }

  return text
}

async function waitForEnabled(locator, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if (await locator.isEnabled().catch(() => false)) {
      return true
    }

    await sleep(200)
  }

  return false
}

async function screenshot(page, name) {
  const target = path.join(screenshotsDir, `${name}.png`)
  await page.screenshot({ path: target, fullPage: true })
  console.log(`  截图：${target}`)
}

function check(label, pass, detail = '') {
  results.push({ label, pass: Boolean(pass), detail })
  console.log(`  ${pass ? '[PASS]' : '[FAIL]'} ${label}${detail ? ` → ${detail}` : ''}`)
}

function report() {
  console.log('\n=== 女巫批次（witch）取证结论 ===')
  for (const result of results) {
    console.log(`${result.pass ? 'PASS' : 'FAIL'}  ${result.label}${result.detail ? ` → ${result.detail}` : ''}`)
  }

  const failed = results.filter((result) => !result.pass)
  console.log(failed.length === 0 ? `全部通过（${results.length} 项）` : `失败 ${failed.length} / ${results.length}`)
}

function describeOutcome(outcome) {
  return `${outcome?.kind ?? '无回执'}${outcome?.rejectionCode ? ` / ${outcome.rejectionCode}` : ''}`
}

function compact(text) {
  return String(text ?? '').replace(/\s+/g, ' ').trim()
}

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
      GameServer__SlotQuotaSeconds: String(options.quotaSeconds),
      GameServer__PacerIntervalMilliseconds: '200',
      DOTNET_ENVIRONMENT: 'Production',
    },
    stdio: 'ignore',
  })
  children.push(child)
  await waitForHttp(`${serverUrl}/healthz`, '宿主 /healthz', 90_000)
  return child
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

/** 读各席位票据：SeatId 是 record struct，Web 序列化形状为 { "value": N }（两种形状都认）。 */
function readSeatTickets(databasePathToRead) {
  const database = new DatabaseSync(databasePathToRead, { readOnly: true })
  try {
    const row = database.prepare('SELECT SeatsJson FROM Games LIMIT 1').get()
    if (row === undefined || typeof row.SeatsJson !== 'string') {
      throw new Error('数据库里没有席位票据（Games.SeatsJson）')
    }

    const parsed = JSON.parse(row.SeatsJson)
    if (!Array.isArray(parsed) || parsed.length === 0) {
      throw new Error('席位票据 JSON 形状不可识别')
    }

    return parsed
      .map((item) => ({ seat: seatNumberOf(item?.seat), ticket: String(item?.ticket ?? '') }))
      .filter((item) => Number.isFinite(item.seat) && item.ticket.length > 0)
      .sort((left, right) => left.seat - right.seat)
  } finally {
    database.close()
  }
}

function seatNumberOf(raw) {
  if (typeof raw === 'number') {
    return raw
  }

  if (raw !== null && typeof raw === 'object' && typeof raw.value === 'number') {
    return raw.value
  }

  return Number.parseInt(String(raw ?? ''), 10)
}

function parseArguments(argv) {
  const parsed = { port: 5411, vitePort: 5291, quotaSeconds: 1 }
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index]
    if (argument === '--port') {
      parsed.port = Number.parseInt(argv[index + 1] ?? '', 10)
      index += 1
    } else if (argument === '--vite-port') {
      parsed.vitePort = Number.parseInt(argv[index + 1] ?? '', 10)
      index += 1
    } else if (argument === '--quota') {
      parsed.quotaSeconds = Number.parseFloat(argv[index + 1] ?? '')
      index += 1
    }
  }

  for (const [name, value] of [
    ['--port', parsed.port],
    ['--vite-port', parsed.vitePort],
  ]) {
    if (!Number.isFinite(value) || value <= 0) {
      throw new Error(`${name} 非法：${value}`)
    }
  }

  if (!Number.isFinite(parsed.quotaSeconds) || parsed.quotaSeconds <= 0) {
    throw new Error(`--quota 非法：${parsed.quotaSeconds}`)
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

async function waitUntil(condition, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if (condition()) {
      return true
    }

    await sleep(25)
  }

  return condition()
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

function runProcess(command, args, cwd) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { cwd, stdio: 'inherit' })
    child.on('error', reject)
    child.on('exit', (code) => {
      if (code === 0) {
        resolve()
        return
      }

      reject(new Error(`${command} 退出码 ${code}`))
    })
  })
}
