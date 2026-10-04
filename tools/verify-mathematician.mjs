/**
 * 数学家（mathematician）批次装置 —— 票据 docs/backlog/done/mathematician.md 的验收矩阵。
 *
 * 它回答：**「窗口推演 + 说书人裁定 + 数字只到本人」这条链路在真界面上跑得通吗？**
 * 场景（固定 6 席：1 诺-达鲺 / 2 筑梦师 / 3 数学家 / 4 畸形秀演员（白天上报为涡流）/ 5 呆瓜 / 6 钟表匠）：
 *   1) 分配 → 常驻中毒落 2 号与 6 号；开首夜（Original）；
 *   2) 6 号钟表匠（中毒）入槽：说书人裁定 → 结算留下 `Poisoned`；
 *   3) 2 号筑梦师（中毒）：真 SignalR 席位作答 → 未生效的自由信息由说书人给出 → 留下 `Poisoned`；
 *   4) 3 号数学家入槽：裁定提示必须含「推演：2」——计划期快照会是 0，因此这一行同时证明
 *      「入槽实时重建」真的生效；结清「2」；
 *   5) 数学家玩家端（真浏览器）出现信息结果（ability=mathematician / content=2）；无关席位零下发；
 *   6) 跨黎明：开白天 → 结束白天 → **说书人把 4 号上报为涡流**（涡流在场）→ 开第二夜 →
 *      诺-达鲺击杀 2 号筑梦师、涡流击杀已死者（无事发生）→ 数学家的提示必须是「推演：0」
 *      （窗口按黎明重置）且注明「必须为假」+ R-0028；说书人给出假数字「1」；
 *   7) 全程扫描各 SignalR 席位收到的推送：玩家端不得出现失效账本 / 归因字段。
 *
 * 与主批次的分工：主批次（verify-storyteller-panel.mjs）跑五席固定花名册的通用玩法回归，
 * 本装置只跑数学家这一条能力链路。
 *
 * 前置：Node >= 22.5（node:sqlite）、本机已构建 web/node_modules（playwright + @microsoft/signalr）。
 * 用法（在仓库根运行；默认迭代档 = 快节拍 + 不落盘截图 + 复用产物）：
 *   node tools/verify-mathematician.mjs                                        # 迭代档
 *   node tools/verify-mathematician.mjs --quota 2 --screenshots-all --build    # 取证档（一批一次）
 *   node tools/verify-mathematician.mjs --port 5415 --vite-port 5295           # 自定端口
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
import { readAttributeBounded, readTextBounded } from './lib/bounded-text.mjs'
import { describeProfile, ensureServerArtifacts, extractProfileFlags, resolveProfile } from './lib/verify-profile.mjs'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const webRoot = path.join(repositoryRoot, 'web')
const { flags, rest } = extractProfileFlags(process.argv.slice(2))
const options = parseArguments(rest)

/** 档位（tools/lib/verify-profile.mjs）：默认迭代档；取证档显式 `--quota 2 --screenshots-all`。 */
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

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-mathematician-'))
const databasePath = path.join(workspace, 'mathematician.db')
const screenshotsDir = path.resolve(repositoryRoot, 'artifacts', 'web')
mkdirSync(screenshotsDir, { recursive: true })

const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`
const hubUrl = `${serverUrl}/hub/game`

/** 六个席位（与 web/src/display/labels.ts 的花名册一致）：4 号白天被上报为涡流，用于摆出「涡流在场」（R-0028）。 */
const ASSIGN = ['no-dashii', 'dreamer', 'mathematician', 'mutant', 'klutz', 'clockmaker']
/** 席位号从花名册顺序派生：调换 ASSIGN 顺序时断言跟着走，不靠人工同步。 */
const seatOf = (slug) => ASSIGN.indexOf(slug) + 1
const DEMON_SEAT = seatOf('no-dashii')
const DREAMER_SEAT = seatOf('dreamer')
const MATHEMATICIAN_SEAT = seatOf('mathematician')
const VORTOX_SEAT = seatOf('mutant')
const UNRELATED_SEAT = seatOf('klutz')
const BYSTANDER_SEAT = seatOf('klutz')
const CLOCKMAKER_SEAT = seatOf('clockmaker')

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
 * 玩家端不该出现的词：失效账本 / 归因 / 说书人说明（D-0012 §4.3）。
 * 注意**不要**把 `sv:night-` 整串列进来：计划标识会出现在操作请求标识里，那是协议管线；
 * 要抓的是失效账本与效果字段本身。
 */
const FORBIDDEN_PLAYER_TOKENS = [
  'malfunction',
  'Malfunctions',
  'MayBeFalse',
  'causedBy',
  'effectId',
  'sourceCharacter',
  'no-dashii.poison',
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
  console.log('=== 1/7 构建并启动真宿主（独立临时库，6 席）===')
  await ensureServerArtifacts({ repositoryRoot, buildMode: config.buildMode })
  await startServer()

  console.log('=== 2/7 取票据并起 Vite ===')
  const ticket = readStorytellerTicket(databasePath)
  const seatTickets = readSeatTickets(databasePath)
  check('席位票据齐备（6 席）', seatTickets.length === 6, `数据库 ${seatTickets.length} 张`)

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

  console.log('=== 3/7 说书人 + 数学家 / 无关席位加入真浏览器 ===')
  const browser = await playwright.chromium.launch()
  const consoleErrors = []
  const storytellerPage = await newPage(browser, { width: 1600, height: 1100 }, consoleErrors)
  await storytellerPage.goto(viteUrl)
  await storytellerPage.getByPlaceholder('说书人票据').fill(ticket)
  await storytellerPage.getByRole('button', { name: '加入' }).click()
  await storytellerPage.locator('[data-testid="grimoire"]').waitFor({ timeout: 30_000 })
  check('说书人加入后看板可见（魔典主视图）', (await storytellerPage.locator('[data-testid="grimoire"]').count()) === 1)

  const mathematicianPage = await newPage(browser, { width: 900, height: 1000 }, consoleErrors)
  await mathematicianPage.goto(`${viteUrl}/#player`)
  await mathematicianPage.getByPlaceholder('席位票据').fill(seatTickets[MATHEMATICIAN_SEAT - 1].ticket)
  await mathematicianPage.getByRole('button', { name: '加入' }).click()
  const mathematicianBadge = await waitForText(
    mathematicianPage.locator('[data-testid="player-seat"]'),
    String(MATHEMATICIAN_SEAT),
    30_000,
  )
  check(
    `数学家席位（${MATHEMATICIAN_SEAT} 号）加入玩家端`,
    mathematicianBadge.includes(String(MATHEMATICIAN_SEAT)),
    mathematicianBadge,
  )

  const bystanderPage = await newPage(browser, { width: 900, height: 1000 }, consoleErrors)
  await bystanderPage.goto(`${viteUrl}/#player`)
  await bystanderPage.getByPlaceholder('席位票据').fill(seatTickets[BYSTANDER_SEAT - 1].ticket)
  await bystanderPage.getByRole('button', { name: '加入' }).click()
  const bystanderBadge = await waitForText(
    bystanderPage.locator('[data-testid="player-seat"]'),
    String(BYSTANDER_SEAT),
    30_000,
  )
  check(
    `无关席位（${BYSTANDER_SEAT} 号）加入玩家端`,
    bystanderBadge.includes(String(BYSTANDER_SEAT)),
    bystanderBadge,
  )

  console.log('=== 4/7 开局分配 → 开首夜 ===')
  const assignmentSelects = storytellerPage.locator('section', { hasText: '开局分配' }).locator('select')
  for (const [index, slug] of ASSIGN.entries()) {
    await assignmentSelects.nth(index).selectOption(slug)
  }

  const assigned = await runCommand(storytellerPage, '分配', () =>
    storytellerPage.getByRole('button', { name: '提交分配' }).click(),
  )
  check('分配 6 个角色被受理', assigned.kind === 'Accepted', assigned.raw)

  // 四个真 SignalR 席位：诺-达鲺 / 涡流（第二夜击杀）/ 筑梦师（作答 + 收自己的信息）/ 无关席位（越权扫描）。
  const demonSeat = await connectSeat(seatTickets[DEMON_SEAT - 1])
  const vortoxSeat = await connectSeat(seatTickets[VORTOX_SEAT - 1])
  const dreamerSeat = await connectSeat(seatTickets[DREAMER_SEAT - 1])
  const unrelatedSeat = await connectSeat(seatTickets[UNRELATED_SEAT - 1])

  const nightStarted = await runCommand(storytellerPage, '开夜', () =>
    storytellerPage.getByRole('button', { name: /开夜/ }).click(),
  )
  check('开夜被受理（真实顺序表建表）', nightStarted.kind === 'Accepted', nightStarted.raw)

  console.log('=== 5/7 中毒信息角色结算 → 数学家推演窗口 ===')
  const clockmakerDecision = await waitForDecision(storytellerPage, (text) => text.includes('钟表匠'), 90_000)
  check('钟表匠槽位照常开出说书人裁定点（中毒不改变唤醒）', clockmakerDecision.includes('钟表匠'), compact(clockmakerDecision))
  const clockmakerSettled = await settleFreeDecision(storytellerPage, '距离 1。')
  check('钟表匠裁定结清被受理', clockmakerSettled.kind === 'Accepted', clockmakerSettled.raw)

  const dreamerAsked = await waitUntil(() => dreamerSeat.requests.length > 0, 60_000)
  check('筑梦师席位收到操作请求（中毒照常被唤醒）', dreamerAsked, `收到 ${dreamerSeat.requests.length} 条`)
  const dreamerRequest = dreamerSeat.requests[0]
  check(
    '筑梦师的合法选项 = 除自己以外的全体席位',
    JSON.stringify((dreamerRequest?.options ?? []).map((option) => option.value)) ===
      JSON.stringify(ASSIGN.map((_, index) => `seat:${index + 1}`).filter((value) => value !== `seat:${DREAMER_SEAT}`)),
    JSON.stringify((dreamerRequest?.options ?? []).map((option) => option.value)),
  )

  const dreamerAnswered = await dreamerSeat.invoke(
    'SubmitResponse',
    dreamerRequest.requestId,
    `seat:${DEMON_SEAT}`,
    'mathematician-dreamer-answer',
    1,
  )
  check('筑梦师提交被受理', dreamerAnswered?.kind === 'Accepted', describeOutcome(dreamerAnswered))

  const dreamerDecision = await waitForDecision(storytellerPage, (text) => text.includes('筑梦师'), 60_000)
  check(
    '中毒筑梦师的信息退回说书人裁定（能力未生效说明可见）',
    dreamerDecision.includes('未生效'),
    compact(dreamerDecision),
  )
  const dreamerSettled = await settleFreeDecision(storytellerPage, '筑梦师得到的信息由我说书人裁定。')
  check('筑梦师裁定结清被受理（留下第二条失效记录）', dreamerSettled.kind === 'Accepted', dreamerSettled.raw)

  const mathematicianDecision = await waitForDecision(storytellerPage, (text) => text.includes('数学家'), 60_000)
  check(
    '数学家裁定提示含「推演：2」（入槽时按账本重建，含当夜更早两条失效）',
    mathematicianDecision.includes('推演：2'),
    compact(mathematicianDecision),
  )
  await screenshot(storytellerPage, 'math-01-deduction')

  // 摘要与裁定点必须读同一份实时提示：槽位提示不回写时，这里会露出计划快照的「推演：0」。
  await setDataDrawer(storytellerPage, true)
  const currentStepText = await panelText(storytellerPage, '当前步骤')
  check(
    '「当前步骤」摘要与裁定点同一份实时提示（不是计划快照）',
    currentStepText.includes('推演：2'),
    compact(currentStepText),
  )
  await screenshot(storytellerPage, 'math-02-current-step-live')
  await setDataDrawer(storytellerPage, false)

  const mathematicianSettled = await settleFreeDecision(storytellerPage, '2')
  check('数学家裁定结清（输入 2）被受理', mathematicianSettled.kind === 'Accepted', mathematicianSettled.raw)

  console.log('=== 6/7 信息只到本人；跨黎明后窗口重置 ===')
  const informationCount = await waitForAttribute(
    mathematicianPage.locator('[data-testid="player-information"]'),
    'data-information-count',
    '1',
    30_000,
  )
  const informationText = await readPlayerInformationText(mathematicianPage)
  check(
    '数学家玩家端出现信息结果（mathematician / 2）',
    informationCount === '1'
      && informationText.includes('mathematician')
      && informationText.includes('2'),
    compact(informationText),
  )
  await screenshot(mathematicianPage, 'math-03-player-info')

  const dreamerInfo = dreamerSeat.messages.find((message) => message.method === 'ReceiveInformationResult')
  check(
    '筑梦师收到的是自己那条信息（不是数学家的数字）',
    dreamerInfo !== undefined && String(dreamerInfo.payload?.ability ?? '') !== 'mathematician',
    JSON.stringify(dreamerInfo?.payload ?? null).slice(0, 160),
  )

  await setDataDrawer(storytellerPage, true)
  const ledgerText = await malfunctionLedgerText(storytellerPage)
  check(
    '说书人失效账本记下两条：2 号 dreamer 与 6 号 clockmaker（中毒）',
    ledgerText.includes('dreamer') && ledgerText.includes('clockmaker') && ledgerText.includes('中毒'),
    compact(ledgerText),
  )
  await screenshot(storytellerPage, 'math-04-ledger')
  await setDataDrawer(storytellerPage, false)

  const unrelatedPushes = unrelatedSeat.messages.filter((message) => message.method === 'ReceiveInformationResult')
  check(
    '无关席位（SignalR）零信息下发',
    unrelatedPushes.length === 0,
    `收到 ${unrelatedPushes.length} 条`,
  )
  const numbersElsewhere = [demonSeat, vortoxSeat, dreamerSeat].flatMap((client) =>
    client.messages
      .filter((message) => message.method === 'ReceiveInformationResult')
      .filter((message) => String(message.payload?.ability ?? '') === 'mathematician'),
  )
  check(
    '数学家的数字没有下发给其他任何席位',
    numbersElsewhere.length === 0,
    `越权下发 ${numbersElsewhere.length} 条`,
  )
  const bystanderInformation = await bystanderPage
    .locator('[data-testid="player-information"]')
    .getAttribute('data-information-count')
  check('无关席位（浏览器）零信息下发', bystanderInformation === '0' || bystanderInformation === null, `count=${bystanderInformation}`)

  // 走完首夜 → 开白天 → 结束白天（黎明推进失效窗口）→ 第二夜。
  const dayStartEnabled = await waitForEnabled(storytellerPage.getByTestId('st-start-day'), 120_000)
  check('首夜走完，白天入口可用', dayStartEnabled)

  const dayStarted = await runCommand(storytellerPage, '开白天', () => storytellerPage.getByTestId('st-start-day').click())
  check('开白天被受理', dayStarted.kind === 'Accepted', dayStarted.raw)
  const dayStatus = await waitForAttribute(storytellerPage.getByTestId('st-day'), 'data-day-status', 'Open', 30_000)
  check('说书人面板进入「白天进行中」', dayStatus === 'Open', `data-day-status=${dayStatus}`)

  const dayClosed = await runCommand(storytellerPage, '结束白天', () => storytellerPage.getByTestId('st-close-day').click())
  check('结束白天被受理', dayClosed.kind === 'Accepted', dayClosed.raw)

  // 第二夜前把 4 号上报为涡流：摆出「涡流在场」的真实局面（首夜不受影响，也不会触发白天不处决的涡流胜负条件）。
  const vortoxCard = storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${VORTOX_SEAT}"]`)
  await vortoxCard.click()
  const characterLabel = storytellerPage.locator('label', { hasText: '角色' }).first()
  await characterLabel.locator('input[type="checkbox"]').check()
  await characterLabel.locator('select').selectOption('vortox')
  await storytellerPage
    .getByPlaceholder('变化原因（必填，会随事件流记录）')
    .fill('批次取证：4 号被创造为涡流（摆出「涡流在场」）')
  const reported = await runCommand(storytellerPage, '上报涡流', () =>
    storytellerPage.getByRole('button', { name: '上报', exact: true }).click(),
  )
  check('说书人把 4 号上报为涡流（涡流在场）被受理', reported.kind === 'Accepted', reported.raw)

  const nightInput = storytellerPage.locator('section', { hasText: '兜底与推进' }).locator('input[type="number"]')
  await nightInput.fill('2')
  const secondNight = await runCommand(storytellerPage, '开第二夜', () =>
    storytellerPage.getByRole('button', { name: /开夜/ }).click(),
  )
  check('开第二夜被受理', secondNight.kind === 'Accepted', secondNight.raw)

  const demonAsked = await waitUntil(() => demonSeat.requests.length > 0, 60_000)
  check('第二夜诺-达鲺收到击杀请求', demonAsked, `收到 ${demonSeat.requests.length} 条`)
  const demonRequest = demonSeat.requests[0]
  const demonAnswered = await demonSeat.invoke(
    'SubmitResponse',
    demonRequest.requestId,
    `seat:${DREAMER_SEAT}`,
    'mathematician-demon-answer',
    1,
  )
  check('诺-达鲺击杀筑梦师被受理（次夜该席不再产生失效）', demonAnswered?.kind === 'Accepted', describeOutcome(demonAnswered))

  const vortoxAsked = await waitUntil(() => vortoxSeat.requests.length > 0, 60_000)
  check('第二夜涡流收到击杀请求', vortoxAsked, `收到 ${vortoxSeat.requests.length} 条`)
  const vortoxAnswered = await vortoxSeat.invoke(
    'SubmitResponse',
    vortoxSeat.requests[0].requestId,
    `seat:${DREAMER_SEAT}`,
    'mathematician-vortox-answer',
    1,
  )
  check(
    '涡流击杀已死亡玩家被受理（不重复产出死亡事实）',
    vortoxAnswered?.kind === 'Accepted',
    describeOutcome(vortoxAnswered),
  )

  const secondDecision = await waitForDecision(storytellerPage, (text) => text.includes('数学家'), 90_000)
  check(
    '第二夜数学家提示含「推演：0」（窗口按黎明重置）且仍有涡流注记',
    secondDecision.includes('推演：0') && secondDecision.includes('必须为假'),
    compact(secondDecision),
  )
  await screenshot(storytellerPage, 'math-05-night2-window-reset')

  const secondSettled = await settleFreeDecision(storytellerPage, '1')
  check('第二夜数学家裁定结清（涡流在场，给出假数字 1）被受理', secondSettled.kind === 'Accepted', secondSettled.raw)

  const secondInfoCount = await waitForAttributeValue(
    () => readPlayerInformationCount(mathematicianPage),
    '2',
    30_000,
  )
  check(
    '数学家玩家端累计两条信息（第二夜内容 1）',
    secondInfoCount === '2' && (await readPlayerInformationText(mathematicianPage)).includes('1'),
    await readPlayerInformationText(mathematicianPage),
  )

  console.log('=== 7/7 视角隔离与收尾 ===')
  const scannedSeats = [demonSeat, vortoxSeat, dreamerSeat, unrelatedSeat]
  const receivedText = JSON.stringify(scannedSeats.map((client) => client.messages))
  const leaked = FORBIDDEN_PLAYER_TOKENS.filter((token) => receivedText.includes(token))
  check(
    '四席（诺-达鲺 + 涡流 + 筑梦师 + 无关席位）收到的全部推送里没有越权字段',
    leaked.length === 0,
    leaked.join(', ') || `已扫描 ${scannedSeats.reduce((total, client) => total + client.messages.length, 0)} 条`,
  )

  const mathematicianPageText = compact(await mathematicianPage.locator('body').innerText())
  check(
    '数学家自己的页面上没有失效账本 / 归因字段',
    FORBIDDEN_PLAYER_TOKENS.every((token) => !mathematicianPageText.includes(token)),
    FORBIDDEN_PLAYER_TOKENS.filter((token) => mathematicianPageText.includes(token)).join(', ') || '未命中',
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

async function readDecisionText(page) {
  const block = page.locator('[data-testid="console-decision"]')
  if ((await block.count()) === 0) {
    return ''
  }

  return compact(await readTextBounded(block.first()))
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

  return compact(await readTextBounded(section.first()))
}

/** 数据抽屉里「失效账本」那一块（只取账本本身：裁定提示里也会出现"失效账本"四个字）。 */
async function malfunctionLedgerText(page) {
  const drawer = page.locator('[data-testid="data-drawer-body"]')
  const block = drawer.locator('div.block', { has: page.locator('h3', { hasText: '失效账本' }) })
  if ((await block.count()) === 0) {
    return ''
  }

  return compact(await readTextBounded(block.first()))
}

async function readPlayerInformationCount(page) {  const panel = page.locator('[data-testid="player-information"]')
  if ((await panel.count()) === 0) {
    return null
  }

  return readAttributeBounded(panel, 'data-information-count')
}

async function readPlayerInformationText(page) {
  const panel = page.locator('[data-testid="player-information"]')
  if ((await panel.count()) === 0) {
    return ''
  }

  return compact(await readTextBounded(panel))
}

/** 轮询一个异步取值函数直到等于期望值（waitUntil 只收同步谓词，DOM 取值得走这里）。 */
async function waitForAttributeValue(getValue, expected, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  let value = null
  while (Date.now() < deadline) {
    value = await getValue()
    if (value === expected) {
      return value
    }

    await sleep(150)
  }

  return value
}

async function waitForAttribute(locator, name, expected, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  let value = null
  while (Date.now() < deadline) {
    value = await readAttributeBounded(locator, name)
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
    text = compact(await readTextBounded(locator))
    if (text.includes(expected)) {
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
  console.log('\n=== 数学家批次（mathematician）取证结论 ===')
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
  const parsed = { port: 5415, vitePort: 5295 }
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
