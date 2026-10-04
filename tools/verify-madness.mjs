/**
 * 疯狂与裁定式处决（洗脑师 + 畸形秀演员）批次装置 —— 票据 docs/backlog/done/madness-and-adjudicated-execution.md 的验收矩阵。
 *
 * 它回答：**「夜晚签发疯狂要求 → 说书人处罚处决」这条链路，在真界面上一路跑得通吗？**
 * 场景（固定 4 席：1 洗脑师 / 2 钟表匠 / 3 筑梦师 / 4 畸形秀演员）：
 *   1) 分配 → 开首夜；1 号在真玩家页面上拿到**两维选择**（玩家 + 善良角色），选 2 号证明「博学者」；
 *   2) 魔典 2 号牌面出现疯狂要求（「博学者」）；4 号（畸形秀演员）在**夜晚**被处罚处决——
 *      夜晚不被中断（R-0020 的明文例外）；
 *   3) 走完首夜 → 开白天：`st-executed` 为空（夜晚处罚**没有**占掉当天上限）；
 *   4) 白天处罚 2 号（洗脑师）→ 死亡 + 当天处决上限被占用 + 白天立即结束（直接进入夜晚）；
 *   5) 收包扫描：2 号（目标）收到私密告知（含「洗脑师」「博学者」）；3、4 号（无关玩家）收到的
 *      每一条推送里没有要求 / 效果 / 归因字段；洗脑师自己的页面也没有任何说书人专属字样。
 *
 * 与主批次的分工：主批次跑五席固定花名册的通用玩法回归；本装置只跑这条能力链路。
 * 夜晚顺序表要求角色契约齐备——4 席选的都是在场不会卡建表的角色（畸形秀演员不在夜晚顺序表上）。
 *
 * 前置：Node >= 22.5（node:sqlite）、本机已构建 web/node_modules（playwright + @microsoft/signalr）。
 * 用法（在仓库根运行；默认迭代档 = 快节拍 + 不落盘截图 + 复用产物）：
 *   node tools/verify-madness.mjs                                        # 迭代档
 *   node tools/verify-madness.mjs --quota 2 --screenshots-all --build    # 取证档（一批一次）
 *   node tools/verify-madness.mjs --port 5412 --vite-port 5292           # 自定端口
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
import { describeProfile, ensureServerArtifacts, extractProfileFlags, resolveProfile } from './lib/verify-profile.mjs'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const webRoot = path.join(repositoryRoot, 'web')
const { flags, rest } = extractProfileFlags(process.argv.slice(2))
const options = parseArguments(rest)

/**
 * 档位（tools/lib/verify-profile.mjs）：默认迭代档（快节拍 0.3s + 不落盘截图 + 复用产物）；
 * 取证档显式传 `--quota 2 --screenshots-all`（必要时加 `--build`）。
 */
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

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-madness-'))
const databasePath = path.join(workspace, 'madness.db')
const screenshotsDir = path.resolve(repositoryRoot, 'artifacts', 'web')
mkdirSync(screenshotsDir, { recursive: true })

const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`
const hubUrl = `${serverUrl}/hub/game`

/** 四个席位（与 web/src/display/labels.ts 的花名册一致）。 */
const ASSIGN = ['cerenovus', 'clockmaker', 'dreamer', 'mutant']
/** 席位号从花名册顺序派生：调换 ASSIGN 顺序时断言跟着走，不靠人工同步。 */
const seatOf = (slug) => ASSIGN.indexOf(slug) + 1
const CERENOVUS_SEAT = seatOf('cerenovus')
const TARGET_SEAT = seatOf('clockmaker')
const MUTANT_SEAT = seatOf('mutant')

/** 玩家客户端可能收到的全部推送（扫描越权字段用）。 */
const PUSH_METHODS = [
  'ReceiveOperationRequest',
  'ReceiveOperationRequestVoided',
  'ReceiveOperationRequestAnswered',
  'ReceivePhaseStarted',
  'ReceiveInformationResult',
  'ReceiveDayChanged',
]

/** 无关玩家端不该出现的词：要求本身 / 效果链 / 归因（D-0012 §4.3 的信息隔离）。 */
const FORBIDDEN_PLAYER_TOKENS = [
  'madnesses',
  'cerenovus.madness',
  'sv:night-1:cerenovus:madness',
  'effects',
  'effectId',
  'causedBy',
  'termination',
  '博学者',
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
  await ensureServerArtifacts({ repositoryRoot, buildMode: config.buildMode })
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

  console.log('=== 3/7 说书人 + 洗脑师席（1 号）加入真浏览器 ===')
  const browser = await playwright.chromium.launch()
  const consoleErrors = []
  const storytellerPage = await newPage(browser, { width: 1600, height: 1100 }, consoleErrors)
  await storytellerPage.goto(viteUrl)
  await storytellerPage.getByPlaceholder('说书人票据').fill(ticket)
  await storytellerPage.getByRole('button', { name: '加入' }).click()
  await storytellerPage.locator('[data-testid="grimoire"]').waitFor({ timeout: 30_000 })
  check('说书人加入后看板可见（魔典主视图）', (await storytellerPage.locator('[data-testid="grimoire"]').count()) === 1)

  const cerenovusPage = await newPage(browser, { width: 900, height: 1100 }, consoleErrors)
  await cerenovusPage.goto(`${viteUrl}/#player`)
  await cerenovusPage.getByPlaceholder('席位票据').fill(seatTickets[CERENOVUS_SEAT - 1].ticket)
  await cerenovusPage.getByRole('button', { name: '加入' }).click()
  const cerenovusBadge = await waitForText(cerenovusPage.locator('[data-testid="player-seat"]'), '1', 30_000)
  check('洗脑师席（1 号）加入玩家端', cerenovusBadge.includes('1'), cerenovusBadge)

  // 2 号是目标（收私密告知），3 / 4 号是无关玩家（收包扫描）。
  const targetSeat = await connectSeat(seatTickets[TARGET_SEAT - 1])
  const unrelatedSeats = [
    await connectSeat(seatTickets[2]),
    await connectSeat(seatTickets[MUTANT_SEAT - 1]),
  ]

  console.log('=== 4/7 分配 → 开首夜 → 洗脑师两维选择（真界面）===')
  const assignmentSelects = storytellerPage.locator('section', { hasText: '开局分配' }).locator('select')
  for (const [index, slug] of ASSIGN.entries()) {
    await assignmentSelects.nth(index).selectOption(slug)
  }

  const assigned = await runCommand(storytellerPage, '分配', () =>
    storytellerPage.getByRole('button', { name: '提交分配' }).click(),
  )
  check('分配 4 个角色被受理', assigned.kind === 'Accepted', assigned.raw)

  const nightStarted = await runCommand(storytellerPage, '开夜', () =>
    storytellerPage.getByRole('button', { name: /开夜/ }).click(),
  )
  check('开夜被受理（洗脑师契约已实现，建表不再拒绝）', nightStarted.kind === 'Accepted', nightStarted.raw)

  // 洗脑师页面上出现两维选择：第一维是席位，第二维是善良角色。
  const primaryOptions = cerenovusPage.locator('[data-testid="player-request-options"] [data-option-value]')
  const secondaryOptions = cerenovusPage.locator(
    '[data-testid="player-request-secondary-options"] [data-option-value]',
  )
  await primaryOptions.first().waitFor({ timeout: 60_000 })
  check('第一维渲染 4 个席位选项', (await primaryOptions.count()) === 4, `渲染 ${await primaryOptions.count()} 个`)
  check(
    '第二维渲染 17 个善良角色选项（镇民 + 外来者）',
    (await secondaryOptions.count()) === 17,
    `渲染 ${await secondaryOptions.count()} 个`,
  )
  const secondaryValues = await secondaryOptions.evaluateAll((nodes) =>
    nodes.map((node) => node.getAttribute('data-option-value')),
  )
  check('第二维含镇民与外来者、不含爪牙 / 恶魔', secondaryValues.includes('savant') && secondaryValues.includes('mutant') && !secondaryValues.includes('witch') && !secondaryValues.includes('no-dashii'), secondaryValues.slice(0, 4).join(','))

  await cerenovusPage.locator('[data-testid="player-request-options"] [data-option-value="seat:2"]').click()
  await cerenovusPage
    .locator('[data-testid="player-request-secondary-options"] [data-option-value="savant"]')
    .click()
  await screenshot(cerenovusPage, 'madness-01-two-dimensional-request')
  await cerenovusPage.getByTestId('player-submit').click()

  const targetCard = storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${TARGET_SEAT}"]`)
  const madnessMark = await waitForLocatorContains(targetCard, '博学者', 30_000)
  check('魔典 2 号牌面出现疯狂要求（要证明的角色）', madnessMark.includes('博学者'), compact(madnessMark))
  await screenshot(storytellerPage, 'madness-02-madness-mark')

  console.log('=== 5/7 夜晚处罚畸形秀演员（4 号）：夜晚继续、不占次日上限 ===')
  await storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${MUTANT_SEAT}"]`).click()
  await openPunishControls(storytellerPage)
  await storytellerPage.getByTestId('console-punish-source').selectOption('Mutant')
  const nightPunished = await runCommand(storytellerPage, '夜晚处罚处决', () =>
    storytellerPage.getByTestId('console-punish').click(),
  )
  check('畸形秀演员的夜晚处罚被受理', nightPunished.kind === 'Accepted', nightPunished.raw)

  const mutantCard = storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${MUTANT_SEAT}"]`)
  const mutantLife = await waitForAttribute(mutantCard, 'data-life', 'Dead', 30_000)
  check('4 号被处罚处决（牌面翻死亡）', mutantLife === 'Dead', `data-life=${mutantLife}`)
  await screenshot(storytellerPage, 'madness-03-mutant-night-punished')

  // 走完首夜：钟表匠（2 号）的裁定点由说书人结清，筑梦师（3 号）的请求由说书人强制作废。
  const decided = await waitForDecision(storytellerPage, (text) => text.includes('钟表匠'), 60_000)
  check('钟表匠槽位给说书人裁定点', decided.includes('钟表匠'), compact(decided))
  const settled = await settleFreeDecision(storytellerPage, '批次取证-钟表匠信息：本夜最小距离 2')
  check('钟表匠裁定被受理', settled.kind === 'Accepted', settled.raw)
  const voided = await forceVoidPending(storytellerPage, 'StorytellerForce', '批次取证：与疯狂链路无关的槽位')
  check('筑梦师请求被说书人强制作废', voided.kind === 'Accepted', voided.raw)

  const dayStarted = await runCommand(storytellerPage, '开白天', async () => {
    await waitForEnabled(storytellerPage.getByTestId('st-start-day'), 120_000)
    await storytellerPage.getByTestId('st-start-day').click()
  })
  check('白天阶段：开白天被受理', dayStarted.kind === 'Accepted', dayStarted.raw)
  const dayPanel = storytellerPage.getByTestId('st-day')
  const dayStatus = await waitForAttribute(dayPanel, 'data-day-status', 'Open', 30_000)
  check('说书人面板进入「白天进行中」', dayStatus === 'Open', `data-day-status=${dayStatus}`)
  check(
    '夜晚处罚没有占掉当天上限（白天账里还没有处决）',
    (await storytellerPage.getByTestId('st-executed').count()) === 0,
  )

  // R-0022：夜晚处罚的死亡在**下一个黎明**（开白天）进入公开面——1 号自己的页面上同时出现
  // 4 号的死亡公告与牌面翻死亡；公告只含"谁死了"，夜晚处罚的理由（疯狂要求）仍然不可见。
  const punishedLife = await waitForAttribute(
    cerenovusPage.locator(`[data-testid="player-lives"] li[data-seat="${MUTANT_SEAT}"]`),
    'data-life',
    'Dead',
    30_000,
  )
  const punishedAnnouncement = await cerenovusPage
    .locator(`[data-testid="player-life-announcements"] li[data-seat="${MUTANT_SEAT}"][data-state="Dead"]`)
    .count()
  const dawnAnnouncementText = compact(
    await cerenovusPage.locator('[data-testid="player-life-announcements"]').innerText(),
  )
  check(
    'R-0022：夜间处罚的死亡在黎明进入公开面（牌面 + 本日公告），不含处罚理由',
    punishedLife === 'Dead'
      && punishedAnnouncement >= 1
      && dawnAnnouncementText.includes(`${MUTANT_SEAT} 号`)
      && !dawnAnnouncementText.includes('畸形秀演员')
      && !dawnAnnouncementText.includes('疯狂'),
    `牌面=${punishedLife}；公告条数=${punishedAnnouncement}｜${dawnAnnouncementText}`,
  )

  console.log('=== 6/7 白天处罚洗脑师目标（2 号）：占上限、立即入夜 ===')
  await storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${TARGET_SEAT}"]`).click()
  await openPunishControls(storytellerPage)
  // 处罚来源不随选席复位：换成 2 号后必须显式改回洗脑师。
  await storytellerPage.getByTestId('console-punish-source').selectOption('Cerenovus')
  const consoleMadness = await waitForLocatorContains(
    storytellerPage.getByTestId('console-madnesses'),
    '博学者',
    30_000,
  )
  check('席位操作台看得到这条疯狂要求', consoleMadness.includes('博学者'), compact(consoleMadness))

  const dayPunished = await runCommand(storytellerPage, '白天处罚处决', () =>
    storytellerPage.getByTestId('console-punish').click(),
  )
  check('洗脑师的白天处罚被受理', dayPunished.kind === 'Accepted', dayPunished.raw)

  const targetLife = await waitForAttribute(targetCard, 'data-life', 'Dead', 30_000)
  check('2 号被处罚处决（牌面翻死亡）', targetLife === 'Dead', `data-life=${targetLife}`)
  const closedStatus = await waitForAttribute(dayPanel, 'data-day-status', 'Closed', 30_000)
  check('白天立即结束（直接进入夜晚）', closedStatus === 'Closed', `data-day-status=${closedStatus}`)
  const executedSeat = await waitForAttribute(storytellerPage.getByTestId('st-executed'), 'data-seat', '2', 30_000)
  check('当天处决上限被这次处罚占用', executedSeat === '2', `data-seat=${executedSeat}`)
  await screenshot(storytellerPage, 'madness-04-day-punished')

  console.log('=== 7/7 视角与收包扫描 ===')
  const targetMessages = JSON.stringify(targetSeat.messages)
  check(
    '目标（2 号）收到私密告知：能力来源与要证明的角色',
    targetMessages.includes('洗脑师') && targetMessages.includes('博学者'),
    '已扫目标收包',
  )
  check(
    '目标收包里没有要求字段 / 要求标识',
    !targetMessages.includes('madnesses') && !targetMessages.includes('sv:night-1:cerenovus:madness'),
    '已扫目标收包',
  )

  const unrelatedText = JSON.stringify(unrelatedSeats.map((client) => client.messages))
  const leaked = FORBIDDEN_PLAYER_TOKENS.filter((token) => unrelatedText.includes(token))
  check(
    '两席无关玩家（3 / 4 号）的全部推送里没有越权字段',
    leaked.length === 0,
    leaked.join(', ') || `已扫描 ${unrelatedSeats.reduce((total, client) => total + client.messages.length, 0)} 条`,
  )

  const cerenovusPageText = compact(await cerenovusPage.locator('body').innerText())
  check(
    '洗脑师自己的页面上没有要求 / 效果链字样',
    !cerenovusPageText.includes('madnesses') &&
      !cerenovusPageText.includes('博学者') &&
      !cerenovusPageText.includes('已终止'),
    compact(cerenovusPageText),
  )
  await screenshot(cerenovusPage, 'madness-05-cerenovus-view')

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

/** 处罚处决默认收起（票据 ui-layout-and-onboarding 降密度）：点开才出现来源 / 说明 / 执行。 */
async function openPunishControls(page) {
  const toggle = page.getByTestId('console-punish-toggle')
  if ((await toggle.count()) > 0 && (await toggle.getAttribute('aria-expanded')) !== 'true') {
    await toggle.click()
  }
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
  console.log('\n=== 疯狂与处罚处决批次（madness）取证结论 ===')
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
  const parsed = { port: 5411, vitePort: 5291 }
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index]
    if (argument === '--port') {
      parsed.port = Number.parseInt(argv[index + 1] ?? '', 10)
      index += 1
    } else if (argument === '--vite-port') {
      parsed.vitePort = Number.parseInt(argv[index + 1] ?? '', 10)
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
    if (await syncCondition(condition)) {
      return true
    }

    await sleep(25)
  }

  return syncCondition(condition)
}

// 同步谓词守卫：waitUntil 不 await 谓词，Promise 恒真会让旧实现静默假绿（agent-reference.md §8）。
function syncCondition(condition) {
  const result = condition()
  if (result !== null && typeof result === 'object' && typeof result.then === 'function') {
    throw new Error(
      'waitUntil 只接受同步谓词：返回 Promise 会恒真（假绿）。请自己写轮询，或改用 locator.waitFor / waitForAttribute / waitForLocatorContains。',
    )
  }

  return result
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
