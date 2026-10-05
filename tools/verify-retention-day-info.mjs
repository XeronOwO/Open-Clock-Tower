/**
 * 保留能力与白天信息族批次装置 —— 三张票据的界面级（装置）取证：
 *   · 亡骨魔「死亡但保留能力」（docs/backlog/done/vigormortis-death-ability-retention.md 的行 4 / 7 界面面）；
 *   · 博学者候选辅助面（docs/backlog/done/savant-information-picker.md 的行 11）；
 *   · 杂耍艺人公开猜测与当晚报数（docs/backlog/done/juggler-and-savant-day-abilities.md 的行 2 / 6 / 8 界面面）。
 *
 * 它回答：**「死亡但保留能力的爪牙仍在自己的格上被唤醒」「博学者的两条信息在说书人界面上靠候选
 * 辅助面选出来」「杂耍艺人的公开猜测录得进、当晚报数只到本人」这三条界面链路，真机跑得通吗？**
 *
 * 场景（固定 6 席：1 亡骨魔 / 2 女巫 / 3 呆瓜 / 4 博学者 / 5 杂耍艺人 / 6 神谕者）：
 *   1) 分配 → 开首夜（这六个角色都不在首夜顺序表上，槽位自动走完）→ 开第 1 天；
 *   2) 5 号在**真玩家页面**上公开猜 3 条（1 真 2 假）→ 公开面在每一席都出现（公开事实）、
 *      同日第二次的入口随之消失（首个白天只猜一次）；
 *   3) 4 号开口要两条信息 → 说书人裁定点渲染**候选辅助面**：真值徽章 / 分类分栏 / 同真伪时
 *      常驻结论转红且提交禁用 → 改成一真一假后提交 → 4 号收到两条人话，无关席位零下发；
 *   4) 关白天 → 第 2 夜：2 号（女巫）先诅咒 3 号；1 号（亡骨魔）杀 2 号 → 说书人**追加裁定点**
 *      选中毒侧（顺时针 / 逆时针）→ 2 号死亡但「保留能力」（效果窗口）、6 号（逆时针最近镇民）中毒；
 *      5 号的夜间格给出猜对数（说书人自由给出）→ 信息只到本人；
 *   5) 第 3 夜：2 号**已经死亡**，在自己的格上仍被唤醒（真浏览器收到请求）——「保留能力」的界面证据；
 *   6) 收包扫描：两个 Node 席位（1 号恶魔 / 6 号神谕者）的全部推送里没有保留能力 / 中毒标记与
 *      博学者的编码 / 真值；神谕者收到的信息只有它自己那条（没有杂耍艺人的报数）。
 *
 * 与主批次的分工：主批次跑五席固定花名册的通用玩法回归；本装置只跑这三条能力链路。
 * 前置：Node >= 22.5（node:sqlite）、本机已构建 web/node_modules（playwright + @microsoft/signalr）。
 * 用法（在仓库根运行；默认迭代档 = 快节拍 + 不落盘截图 + 复用产物）：
 *   node tools/verify-retention-day-info.mjs                                        # 迭代档
 *   node tools/verify-retention-day-info.mjs --quota 2 --screenshots-all --build    # 取证档（一批一次）
 *   node tools/verify-retention-day-info.mjs --port 5415 --vite-port 5295           # 自定端口
 *
 * 外部耦合（换机器先核对 web/AGENTS.md §3.1）：宿主编译产物路径、SQLite 表 Games 的
 * StorytellerTicket / SeatsJson 列形状、SignalR 的 SubmitResponse 四参数签名。
 * 退出码：0 = 全过；1 = 有失败；2 = 环境缺依赖。
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

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-retention-'))
const databasePath = path.join(workspace, 'retention.db')
const screenshotsDir = path.resolve(repositoryRoot, 'artifacts', 'web')
mkdirSync(screenshotsDir, { recursive: true })

const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`
const hubUrl = `${serverUrl}/hub/game`

/**
 * 六个席位（与 web/src/display/labels.ts 的花名册一致）。
 *
 * 选角理由：亡骨魔「其他夜晚」行动（首夜不入格，第 2 夜才有击杀格）；女巫同为「其他夜晚」的爪牙
 * （她是被亡骨魔杀死后**保留能力**的那一个）；呆瓜无夜晚行动（外来者，也是逆时针侧够不到的位置，
 * 让「选侧」两个候选落到不同的人身上）；博学者 / 杂耍艺人是白天族的两名镇民；神谕者「其他夜晚」
 * 且是**逆时针方向最近的镇民**——说书人选逆时针即由他中毒，博学者因此保持健康（C1 一真一假照旧成立）。
 */
const ASSIGN = ['vigormortis', 'witch', 'klutz', 'savant', 'juggler', 'oracle']
const seatOf = (slug) => ASSIGN.indexOf(slug) + 1
const DEMON_SEAT = seatOf('vigormortis')
const WITCH_SEAT = seatOf('witch')
const KLUTZ_SEAT = seatOf('klutz')
const SAVANT_SEAT = seatOf('savant')
const JUGGLER_SEAT = seatOf('juggler')
const ORACLE_SEAT = seatOf('oracle')

/** 杂耍艺人的三条公开猜测：4 号 = 博学者（**猜对 1 条**）、1 号 ≠ 诺-达鲺、6 号 ≠ 钟表匠。 */
const JUGGLER_GUESSES = [
  { seat: SAVANT_SEAT, character: 'savant' },
  { seat: DEMON_SEAT, character: 'no-dashii' },
  { seat: ORACLE_SEAT, character: 'clockmaker' },
]
const EXPECTED_CORRECT = 1

/** 说书人浏览器页：本装置只有一个，读面板 / 下命令的助手都从这里取。 */
let storytellerPage = null

/**
 * 本装置的**固定断言数**（含末尾那条覆盖自检本身）。
 * 改动本装置、增删断言时必须同步这个数字：条件分支被静默跳过（"互为反面"的夹具没找到、
 * 女巫请求没出现……）会让项数变少——那必须红，而不是悄悄少判几行。
 */
const EXPECTED_CHECKS = 74

const PUSH_METHODS = [
  'ReceiveOperationRequest',
  'ReceiveOperationRequestVoided',
  'ReceiveOperationRequestAnswered',
  'ReceivePhaseStarted',
  'ReceiveInformationResult',
  'ReceiveDayChanged',
]

/**
 * 无关玩家端不该出现的词（D-0012 §4.3 的信息隔离）。
 * 注意**不含**角色 slug：杂耍艺人的猜测本身是公开事实，被猜角色的 slug 会随公开面下发（那是对的）。
 * 这里扫的是说书人专属面：保留能力窗口、亡骨魔击杀事实、博学者的事实编码与组合约束。
 */
const FORBIDDEN_PLAYER_TOKENS = [
  'retained-ability',
  'RetainedAbility',
  '保留能力',
  'vigormortis.retention',
  'VigormortisKill',
  'DeferredRetention',
  'fact:',
  'ExactlyOneTrue',
  'truthRule',
  'truthNote',
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
  console.log('=== 1/8 构建并启动真宿主（独立临时库，6 席）===')
  const artifacts = await ensureServerArtifacts({ repositoryRoot, buildMode: config.buildMode })
  console.log(`  宿主产物：${artifacts.artifact}（${artifacts.built ? '本次构建' : '复用'}）`)
  await startServer()

  console.log('=== 2/8 取票据并起 Vite ===')
  const ticket = readStorytellerTicket(databasePath)
  const seatTickets = readSeatTickets(databasePath)
  check('席位票据齐备（6 席）', seatTickets.length === ASSIGN.length, `数据库 ${seatTickets.length} 张`)

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

  console.log('=== 3/8 说书人 + 女巫 / 博学者 / 杂耍艺人加入真浏览器；恶魔与神谕者走线级探针 ===')
  const browser = await playwright.chromium.launch()
  const consoleErrors = []
  storytellerPage = await newPage(browser, { width: 1600, height: 1100 }, consoleErrors)
  await storytellerPage.goto(viteUrl)
  await storytellerPage.getByPlaceholder('说书人票据').fill(ticket)
  await storytellerPage.getByRole('button', { name: '加入' }).click()
  await storytellerPage.locator('[data-testid="grimoire"]').waitFor({ timeout: 30_000 })
  check(
    '说书人加入后看板可见（魔典主视图）',
    (await storytellerPage.locator('[data-testid="grimoire"]').count()) === 1,
  )
  await openDataDrawer(storytellerPage)

  const witchPage = await joinPlayerPage(browser, seatTickets[WITCH_SEAT - 1], WITCH_SEAT, consoleErrors)
  const savantPage = await joinPlayerPage(browser, seatTickets[SAVANT_SEAT - 1], SAVANT_SEAT, consoleErrors)
  const jugglerPage = await joinPlayerPage(browser, seatTickets[JUGGLER_SEAT - 1], JUGGLER_SEAT, consoleErrors)
  check(
    `三个玩家席加入玩家端（${WITCH_SEAT} 号女巫 / ${SAVANT_SEAT} 号博学者 / ${JUGGLER_SEAT} 号杂耍艺人）`,
    (await witchPage.locator('[data-testid="player-seat"]').count()) === 1
      && (await savantPage.locator('[data-testid="player-seat"]').count()) === 1
      && (await jugglerPage.locator('[data-testid="player-seat"]').count()) === 1,
  )

  // 1 号（亡骨魔）与 6 号（神谕者）用 Node 客户端驱动：前者是本局唯一的夜间击杀执行者，
  // 后者是"无关席位 + 收包扫描"的载体（每席位只保留一条连接，见 web/AGENTS.md §3.1）。
  const demonSeat = await connectSeat(seatTickets[DEMON_SEAT - 1])
  const oracleSeat = await connectSeat(seatTickets[ORACLE_SEAT - 1])

  console.log('=== 4/8 分配 → 首夜（女巫之外都是空槽）→ 第 1 天 ===')
  const assignmentSelects = storytellerPage.locator('section', { hasText: '开局分配' }).locator('select')
  for (const [index, slug] of ASSIGN.entries()) {
    await assignmentSelects.nth(index).selectOption(slug)
  }

  const assigned = await runCommand(storytellerPage, '分配', () =>
    storytellerPage.getByRole('button', { name: '提交分配' }).click(),
  )
  check(`分配 6 个角色被受理（${ASSIGN.join(' / ')}）`, assigned.kind === 'Accepted', assigned.raw)

  const firstNight = await startNightWhenReady(storytellerPage, 1)
  check('开首夜被受理（首夜表里只有女巫）', firstNight.kind === 'Accepted', firstNight.raw)
  await driveFirstNight(storytellerPage, witchPage, demonSeat)

  const firstDay = await startDayWhenReady(storytellerPage)
  check('首夜走完后开第 1 天被受理', firstDay.kind === 'Accepted', firstDay.raw)
  const dayStatus = await waitForAttribute(storytellerPage.getByTestId('st-day'), 'data-day-status', 'Open', 30_000)
  check('说书人面板进入「白天进行中」', dayStatus === 'Open', `data-day-status=${dayStatus}`)

  console.log('=== 5/8 第 1 天：杂耍艺人公开猜测 + 博学者候选辅助面 ===')
  await driveJugglerDayGuesses(jugglerPage, witchPage)
  await driveSavantPickers(savantPage, witchPage)

  const closed = await runCommand(storytellerPage, '结束白天', () =>
    storytellerPage.getByTestId('st-close-day').click(),
  )
  check('结束第 1 天被受理', closed.kind === 'Accepted', closed.raw)

  console.log('=== 6/8 第 2 夜：女巫 → 亡骨魔杀爪牙 → 说书人选中毒侧 → 杂耍艺人报数 ===')
  const secondNight = await startNightWhenReady(storytellerPage, 2)
  check('开第 2 夜被受理（第 1 天已结束）', secondNight.kind === 'Accepted', secondNight.raw)
  await driveSecondNight(storytellerPage, witchPage, savantPage, jugglerPage, demonSeat)

  console.log('=== 7/8 第 2 天（不做任何动作，只为让第 3 夜的「昨天」有一份白天账）===')
  const secondDay = await startDayWhenReady(storytellerPage)
  check('第 2 天开白天被受理（这一天不猜、不提名）', secondDay.kind === 'Accepted', secondDay.raw)
  const secondDayClosed = await runCommand(storytellerPage, '结束第 2 天', () =>
    storytellerPage.getByTestId('st-close-day').click(),
  )
  check('第 2 天直接结束被受理', secondDayClosed.kind === 'Accepted', secondDayClosed.raw)

  console.log('=== 8/8 第 3 夜：已死亡的爪牙仍被唤醒 + 收包扫描 ===')
  const thirdNight = await startNightWhenReady(storytellerPage, 3)
  check('开第 3 夜被受理（第 2 天已结束）', thirdNight.kind === 'Accepted', thirdNight.raw)

  // 探针放在**开夜之后**：黄昏收口（DuskExpiry）是在开夜命令里跑的，开夜前读到的"还在"
  // 证明不了"这一夜开始时还在"。属性本身还有下游兜底——第 3 夜死者的格被唤醒，窗口若被收掉就不会绑格。
  const retentionAlive = await waitForEffectWindow(storytellerPage, '保留能力', 20_000)
  check(
    '开第 3 夜（黄昏收口）之后「保留能力」窗口仍在生效（不随黄昏到期）',
    retentionAlive.includes('保留能力'),
    retentionAlive.slice(0, 200),
  )
  await driveThirdNight(storytellerPage, witchPage, savantPage, demonSeat)

  // 扫描面：无关席位（6 号神谕者）的全部推送，加上当事恶魔（1 号）**除自己请求正文以外**的推送
  // ——请求正文是发给他的界面内容（亡骨魔的能力原文里本来就有「保留能力」四个字），不算投影泄漏。
  const scannedMessages = [
    ...oracleSeat.messages,
    ...demonSeat.messages.filter((message) => message.method !== 'ReceiveOperationRequest'),
  ]
  const unrelatedText = JSON.stringify(scannedMessages)
  const leaked = FORBIDDEN_PLAYER_TOKENS.filter((token) => unrelatedText.includes(token))
  // 阳性对照：扫描面必须真的有帧（本装置实测两席合计 18 条）。否则「一条都没收到」也会让下面的
  // 「没有泄漏」为真——那正是最典型的假绿。
  check(
    '收包扫描的样本不是空集（阳性对照，实测两席合计 18 条）',
    scannedMessages.length >= 4,
    `已扫描 ${scannedMessages.length} 条`,
  )
  check(
    `无关玩家（${DEMON_SEAT} 号恶魔 / ${ORACLE_SEAT} 号神谕者）的推送里没有保留能力与博学者字段`,
    leaked.length === 0 && scannedMessages.length >= 4,
    leaked.join(', ') || `已扫描 ${scannedMessages.length} 条`,
  )

  const oracleInformations = oracleSeat.messages.filter((message) => message.method === 'ReceiveInformationResult')
  const oracleAbilities = oracleInformations.map((message) => String(message.payload?.ability ?? '?'))
  check(
    '神谕者收到了自己的信息（阳性对照：她的信息通道是通的）',
    oracleAbilities.includes('oracle'),
    oracleAbilities.join(', ') || '（无信息）',
  )
  check(
    '神谕者只收到自己那条信息（没有杂耍艺人的猜对数）',
    oracleAbilities.includes('oracle') && !oracleAbilities.includes('juggler'),
    oracleAbilities.join(', '),
  )
  check(
    '女巫（当事玩家）的信息面板始终为空（她不是信息角色）',
    (await informationCount(witchPage)) === 0,
    `data-information-count=${await informationCount(witchPage)}`,
  )

  check('浏览器控制台没有报错', consoleErrors.length === 0, consoleErrors.slice(0, 3).join(' | '))
  check(
    `装置覆盖自检：固定断言 ${EXPECTED_CHECKS} 项都命中（增删断言请同步 EXPECTED_CHECKS）`,
    results.length + 1 === EXPECTED_CHECKS,
    `实际 ${results.length + 1} 项`,
  )
  await browser.close()
}

/** 玩家席加入真浏览器：填票据 → 断言席位徽标。 */
async function joinPlayerPage(browser, seatTicket, seat, consoleErrors) {
  const page = await newPage(browser, { width: 900, height: 1100 }, consoleErrors)
  await page.goto(`${viteUrl}/#player`)
  await page.getByPlaceholder('席位票据').fill(seatTicket.ticket)
  await page.getByRole('button', { name: '加入' }).click()
  const badge = await waitForText(page.locator('[data-testid="player-seat"]'), String(seat), 30_000)
  if (!badge.includes(String(seat))) {
    throw new Error(`${seat} 号席位加入失败：${badge}`)
  }

  return page
}

/** 杂耍艺人的白天：真玩家页面上填 3 条猜测 → 公开面（各席可见）→ 入口随之消失。 */
async function driveJugglerDayGuesses(jugglerPage, witchPage) {
  const form = jugglerPage.getByTestId('player-juggler')
  const formVisible = await form
    .waitFor({ timeout: 30_000 })
    .then(() => true)
    .catch(() => false)
  check(`杂耍艺人（${JUGGLER_SEAT} 号）首个白天出现公开猜测入口`, formVisible, `入口数=${await form.count()}`)

  for (const [index, guess] of JUGGLER_GUESSES.entries()) {
    await jugglerPage.getByTestId(`player-juggler-seat-${index}`).selectOption(String(guess.seat))
    await jugglerPage.getByTestId(`player-juggler-character-${index}`).selectOption(guess.character)
  }

  const seatOptions = await jugglerPage
    .getByTestId('player-juggler-seat-0')
    .locator('option')
    .evaluateAll((nodes) => nodes.map((node) => node.textContent?.trim() ?? ''))
  check(
    '猜测的席位候选 = 本局全体席位（0–5 条、可猜已死亡玩家）',
    seatOptions.length === ASSIGN.length + 1,
    seatOptions.join(' / '),
  )

  await screenshot(jugglerPage, 'retention-01-juggler-guess-form')
  await jugglerPage.getByTestId('player-juggler-submit').click()

  const ownCount = await waitForAttribute(
    jugglerPage.getByTestId('player-juggler-guesses'),
    'data-juggler-guess-count',
    '1',
    30_000,
  )
  const ownText = compact(await readTextBounded(jugglerPage.getByTestId('player-juggler-guesses')))
  check('公开猜测落进当天账并进入公开面（1 条记录 = 杂耍艺人）', ownCount === '1', `data-juggler-guess-count=${ownCount}`)
  const listed = ['博学者', '诺-达鲺', '钟表匠'].filter((name) => ownText.includes(name))
  check('公开面逐条列出三条猜测（角色中文名）', listed.length === 3, `${listed.join('/')} ← ${ownText.slice(0, 200)}`)
  await screenshot(jugglerPage, 'retention-02-juggler-public-guesses')

  const unrelatedCount = await waitForAttribute(
    witchPage.getByTestId('player-juggler-guesses'),
    'data-juggler-guess-count',
    '1',
    30_000,
  )
  check(
    `无关席位（${WITCH_SEAT} 号女巫）看到同一份公开猜测（猜测是公开事实）`,
    unrelatedCount === '1',
    `data-juggler-guess-count=${unrelatedCount}`,
  )

  const formGone = await waitForCount(jugglerPage.getByTestId('player-juggler'), 0, 30_000)
  check('同日第二次入口消失（首个白天只允许公开猜一次）', formGone, `入口数=${await form.count()}`)
}

/** 博学者的白天：玩家开口 → 说书人候选辅助面（真值 / 分栏 / 非法组合禁用）→ 结清 → 只到本人。 */
async function driveSavantPickers(savantPage, witchPage) {
  const entry = savantPage.getByTestId('player-savant-question')
  const idle = await waitForAttribute(entry, 'data-question-state', 'idle', 30_000)
  check(`博学者（${SAVANT_SEAT} 号）白天出现「要两条信息」入口`, idle === 'idle', `data-question-state=${idle}`)
  await screenshot(savantPage, 'retention-03-savant-entry')

  await savantPage.getByTestId('player-savant-question-submit').click()
  const waiting = await waitForAttribute(entry, 'data-question-state', 'waiting', 30_000)
  check('开口后进入等待态（等说书人给两条）', waiting === 'waiting', `data-question-state=${waiting}`)

  const picker = storytellerPage.getByTestId('savant-picker')
  const pickerVisible = await picker
    .waitFor({ timeout: 60_000 })
    .then(() => true)
    .catch(() => false)
  check('说书人侧渲染候选辅助面（信息类裁定点走两槽位面板）', pickerVisible)

  const decision = await readDecisionPanel(storytellerPage)
  check(
    '裁定点上下文说明两条一真一假与归属',
    decision.context.includes('博学者') && decision.context.includes('一条正确') && decision.context.includes('一条错误'),
    decision.context.slice(0, 200),
  )
  const ruleNote = compact(await readTextBounded(picker.getByTestId('savant-rule-note')))
  check('辅助面常驻服务端给的组合约束说明', ruleNote.length > 0, ruleNote.slice(0, 160))

  const candidates = await picker.locator('[data-testid^="savant-option-"]').evaluateAll((nodes) =>
    nodes.map((node) => ({
      value: (node.getAttribute('data-testid') ?? '').replace('savant-option-', ''),
      truth: node.getAttribute('data-truth') ?? '',
      group: node.getAttribute('data-group') ?? '',
    })),
  )
  check(
    '候选逐条带服务端真值（真 / 假，不是让说书人自己记）',
    // 下界取实际量级（本局实测 157 条）：候选缩水一半必须红，而不是被 `>= 10` 兜住。
    candidates.length >= 150
      && candidates.every((entryItem) => entryItem.truth === 'True' || entryItem.truth === 'False'),
    `${candidates.length} 条候选；真值集合=${[...new Set(candidates.map((item) => item.truth))].join('/')}`,
  )
  const groups = [...new Set(candidates.map((item) => item.group).filter((group) => group.length > 0))]
  const groupButtons = await picker.locator('[data-testid^="savant-group-"]').count()
  check(
    '候选按类别分栏（不是一条大列表）',
    // 五组（座位关系 / 阵营与人数 / 昨晚与今天 / 状态读数 / 点名）+ 「全部」按钮 = 6 个按钮。
    groups.length >= 5 && groupButtons >= 6,
    `${groups.join('/')}；按钮 ${groupButtons}`,
  )

  const trueOptions = candidates.filter((item) => item.truth === 'True')
  const falseOptions = candidates.filter((item) => item.truth === 'False')
  check(
    '候选里真假都有（假信息同样可选）',
    trueOptions.length >= 2 && falseOptions.length >= 1,
    `真 ${trueOptions.length} / 假 ${falseOptions.length}`,
  )

  await picker.locator(`[data-testid="savant-option-${trueOptions[0].value}"]`).click()
  await picker.locator(`[data-testid="savant-option-${trueOptions[1].value}"]`).click()
  const badVerdict = await waitForAttribute(picker.getByTestId('savant-verdict'), 'data-verdict-ok', 'false', 10_000)
  const badText = compact(await readTextBounded(picker.getByTestId('savant-verdict')))
  const badDisabled = await picker.getByTestId('savant-submit').isDisabled()
  check('两条同真：常驻结论转红并写明原因', badVerdict === 'false' && badText.includes('一真一假'), badText)
  check('两条同真：提交按钮当场禁用（非法组合拦在前端）', badDisabled === true, `disabled=${badDisabled}`)
  await screenshot(storytellerPage, 'retention-04-savant-illegal-combination')

  // 行 9（R-0057-C）：挂起期间账变了 → 以**提交时刻**重新求值与校验。
  // 挑一条"当前为假、但可以当场变真"的状态读数（「场上有玩家中毒」），与一条为真的座位事实配成
  // 一真一假（前端按**快照**真值放行）；随后在挂起期间上报 3 号中毒——提交时账上已是"两条都为真"。
  const mutableFact = candidates.find(
    (item) => item.group === '状态读数' && item.truth === 'False' && item.value.includes('poison'),
  )
  if (mutableFact === undefined) {
    check(
      '挂起期间账变（行 9）的夹具：候选里有「场上有玩家中毒」这条可翻转的状态读数',
      false,
      '没找到可翻转的事实，本行判不了',
    )
  } else {
    const fillSlots = async () => {
      await picker.getByTestId('savant-slot-1').getByRole('button', { name: '第一条' }).click()
      await picker.locator(`[data-testid="savant-option-${trueOptions[0].value}"]`).click()
      await picker.getByTestId('savant-slot-2').getByRole('button', { name: '第二条' }).click()
      await picker.locator(`[data-testid="savant-option-${mutableFact.value}"]`).click()
    }

    await fillSlots()
    const freshVerdict = await waitForAttribute(picker.getByTestId('savant-verdict'), 'data-verdict-ok', 'true', 10_000)
    check(
      '挂起期间账变（行 9）：按提交时刻前的快照真值选出的一真一假先被前端放行',
      freshVerdict === 'true',
      compact(await readTextBounded(picker.getByTestId('savant-verdict'))),
    )

    const poisonReport = await reportSeatState(storytellerPage, {
      seat: KLUTZ_SEAT,
      dimensionLabel: '中毒',
      value: 'Poisoned',
      reason: '批次取证：挂起期间账变了（R-0057-C 提交时刻校验）',
    })
    check('挂起期间账变（行 9）：说书人上报 3 号中毒被受理（账真的变了）', poisonReport.kind === 'Accepted', poisonReport.raw)

    // 上报会切走操作台的选中席位：重新填一遍两个槽位（前端手上的**快照真值**仍是旧的——正是本行要证的点）。
    await fillSlots()
    await screenshot(storytellerPage, 'retention-05-savant-stale-snapshot')
    const staleRejected = await runCommand(storytellerPage, '账变后提交', () =>
      picker.getByTestId('savant-submit').click(),
    )
    check(
      '挂起期间账变（行 9）：提交时按当时的账重算——快照里的一真一假已成两条都为真，服务端拒绝',
      staleRejected.kind !== 'Accepted' && staleRejected.raw.includes('都为真'),
      staleRejected.raw.slice(0, 220),
    )
  }

  // 「互为反面」（奇 / 偶这类同族两条）必然一真一假——前端拿不到互斥组信息，因此照常放行；
  // 服务端在提交时按当时的账拒绝。这一段把"界面不预拦、平台兜得住"的现状钉在证据里（票据「残余」第 4 条）。
  const familyOf = (value) => value.slice(0, value.lastIndexOf(':'))
  const mirror = falseOptions.find((item) => familyOf(item.value) === familyOf(trueOptions[0].value))
  const independent = falseOptions.find((item) => familyOf(item.value) !== familyOf(trueOptions[0].value))
  if (mirror !== undefined) {
    await picker.getByTestId('savant-slot-2').getByRole('button', { name: '第二条' }).click()
    await picker.locator(`[data-testid="savant-option-${mirror.value}"]`).click()
    const mirrorVerdict = await waitForAttribute(picker.getByTestId('savant-verdict'), 'data-verdict-ok', 'true', 10_000)
    check(
      '互为反面的两条：前端不知互斥、按真值照常放行（残余第 4 条的现状）',
      mirrorVerdict === 'true',
      compact(await readTextBounded(picker.getByTestId('savant-verdict'))),
    )
    const rejected = await runCommand(storytellerPage, '提交互为反面的组合', () =>
      picker.getByTestId('savant-submit').click(),
    )
    check(
      '互为反面的两条：服务端当场拒绝并给出可读原因（平台防呆）',
      rejected.kind !== 'Accepted' && rejected.raw.includes('互为反面'),
      rejected.raw.slice(0, 200),
    )
    await screenshot(storytellerPage, 'retention-06-savant-mirror-rejected')
  }

  await picker.getByTestId('savant-slot-2').getByRole('button', { name: '第二条' }).click()
  await picker.locator(`[data-testid="savant-option-${independent.value}"]`).click()
  const okVerdict = await waitForAttribute(picker.getByTestId('savant-verdict'), 'data-verdict-ok', 'true', 10_000)
  const okText = compact(await readTextBounded(picker.getByTestId('savant-verdict')))
  const okDisabled = await picker.getByTestId('savant-submit').isDisabled()
  check('改成一真一假：结论转为可提交', okVerdict === 'true' && okText.includes('一真一假'), okText)
  check('改成一真一假：提交按钮恢复可用', okDisabled === false, `disabled=${okDisabled}`)

  const slotFirst = await readAttributeBounded(picker.getByTestId('savant-slot-1'), 'data-slot-value')
  const slotSecond = await readAttributeBounded(picker.getByTestId('savant-slot-2'), 'data-slot-value')
  check(
    '两个槽位各自记下所选事实的编码（提交的就是这两条）',
    slotFirst === trueOptions[0].value && slotSecond === independent.value,
    `${slotFirst} | ${slotSecond}`,
  )
  await screenshot(storytellerPage, 'retention-07-savant-legal-combination')

  const settled = await runCommand(storytellerPage, '博学者按候选结清', () =>
    picker.getByTestId('savant-submit').click(),
  )
  check('说书人按候选结清被受理', settled.kind === 'Accepted', settled.raw)

  const infoPanel = savantPage.getByTestId('player-information')
  const infoCount = await waitForAttribute(infoPanel, 'data-information-count', '2', 30_000)
  const infoText = compact(await readTextBounded(infoPanel))
  check('博学者收到两条信息（只到本人）', infoCount === '2', `data-information-count=${infoCount}`)
  check(
    '两条信息都归在博学者名下、只看到人话文案（不含事实编码与真值）',
    infoCount === '2' && infoText.includes('博学者') && !infoText.includes('fact:') && !infoText.includes('True'),
    infoText.slice(0, 220),
  )
  await screenshot(savantPage, 'retention-08-savant-two-informations')

  const entryGone = await waitForCount(entry, 0, 30_000)
  check('结清后入口整块撤下（今天已经要过，不重复开口）', entryGone, `入口数=${await entry.count()}`)
  check(
    `无关席位（${WITCH_SEAT} 号女巫）的信息面板为空（两条信息零下发）`,
    (await informationCount(witchPage)) === 0,
    `data-information-count=${await informationCount(witchPage)}`,
  )
}

/**
 * 首夜：女巫的能力是「每个夜晚」（**没有星号**），所以首夜就有行动格、真的会发请求——
 * 她之外的角色（亡骨魔 / 杂耍艺人 / 神谕者都是「每个夜晚*」）首夜只走空槽。
 * 这里如实把她叫起来（流程本身也是"首夜真的有人在行动"的运行时证据）。
 */
async function driveFirstNight(page, witchPage, demonSeat) {
  const witchRequest = witchPage.locator('[data-testid="player-request-panel"]')
  let cursedFirstNight = false
  let idleConfirmed = false
  const deadline = Date.now() + 180_000

  while (Date.now() < deadline) {
    const decision = await readDecisionPanel(page)
    if (decision.context.length > 0) {
      const outcome = await settleFreeDecision(page, '0')
      if (outcome.kind !== 'Accepted') {
        break
      }

      continue
    }

    const witchState = await readAttributeBounded(witchRequest, 'data-request-state')
    if (witchState === 'pending') {
      if (!cursedFirstNight) {
        check(
          `女巫（${WITCH_SEAT} 号）首夜照常被唤醒（她是「每个夜晚」的爪牙）`,
          witchState === 'pending',
          `data-request-state=${witchState}`,
        )
        await answerSeatRequest(witchPage, KLUTZ_SEAT)
        cursedFirstNight = true
      }

      await sleep(200)
      continue
    }

    if (cursedFirstNight && !idleConfirmed) {
      const back = await waitForAttribute(witchRequest, 'data-request-state', 'idle', 30_000)
      check('首夜女巫诅咒提交后请求区回到空态', back === 'idle', `data-request-state=${back}`)
      idleConfirmed = true
    }

    const demonRequests = demonSeat.takeRequests()
    if (demonRequests.length > 0) {
      // 亡骨魔（「每个夜晚*」）首夜不该有行动格；真收到请求说明口径变了——如实提交，不猜。
      check('亡骨魔（「每个夜晚*」）首夜不入行动格', false, `首夜收到 ${demonRequests.length} 条恶魔请求`)
      for (const request of demonRequests) {
        await demonSeat.invoke('SubmitResponse', request.requestId, `seat:${KLUTZ_SEAT}`, 'retention-first-night', 1)
      }

      continue
    }

    if (await pendingRequestVisible(page)) {
      await sleep(300)
      continue
    }

    const advanced = await waitForSlotProgress(page)
    if (advanced.kind === 'Completed') {
      break
    }

    await sleep(200)
  }

  check('首夜计划走完（女巫之外全是空槽）', await planCompleted(page), `计划走完=${await planCompleted(page)}`)
}

/** 第 2 夜：女巫诅咒 → 亡骨魔杀爪牙 → 说书人选中毒侧 → 杂耍艺人报数。 */
async function driveSecondNight(page, witchPage, savantPage, jugglerPage, demonSeat) {
  const witchRequest = witchPage.locator('[data-testid="player-request-panel"]')
  const unhandled = []
  let cursedKlutz = false
  let witchIdleConfirmed = false
  let demonKilledWitch = false
  let sideChosen = false
  let jugglerCountGiven = false
  let lastTrace = ''
  const deadline = Date.now() + 300_000

  while (Date.now() < deadline) {
    const trace = await nightTrace(page, witchPage, demonSeat)
    if (trace !== lastTrace) {
      lastTrace = trace
      console.log(`  [时序] ${trace}`)
    }

    const decision = await readDecisionPanel(page)
    if (decision.context.includes('哪一侧最近的镇民中毒')) {
      if (!sideChosen) {
        const clockwise = decision.options.some((option) => option.includes('顺时针'))
        const counter = decision.options.some((option) => option.includes('逆时针'))
        check(
          '亡骨魔杀死爪牙后，说书人侧开出「选中毒侧」的追加裁定点',
          decision.context.includes(String(WITCH_SEAT)) && clockwise && counter,
          `${decision.context.slice(0, 80)} ← ${decision.options.join(' | ')}`,
        )
        check(
          '两个候选各自写明是哪一侧最近的镇民（不是让说书人自己数）',
          decision.options.some((option) => option.includes(String(ORACLE_SEAT))) && decision.options.some((option) => option.includes(String(SAVANT_SEAT))),
          decision.options.join(' | '),
        )
        await screenshot(page, 'retention-09-vigormortis-side-decision')
        const outcome = await runCommand(page, '选定中毒侧（逆时针）', () =>
          page.getByTestId('console-decision').locator('.options button', { hasText: '逆时针' }).click(),
        )
        check('说书人选逆时针侧被受理', outcome.kind === 'Accepted', outcome.raw)
        sideChosen = true
        continue
      }

      await sleep(200)
      continue
    }

    if (decision.context.includes('杂耍艺人')) {
      if (!jugglerCountGiven) {
        check(
          '杂耍艺人的夜间格按结算时刻快照推演猜对数（平台给推演值）',
          decision.context.includes(`猜对 ${EXPECTED_CORRECT} 条`),
          decision.context.slice(0, 220),
        )
        check(
          '推演值旁边写明是「按结算时刻的角色快照」算的',
          decision.context.includes('结算时刻'),
          decision.context.slice(0, 220),
        )
        await screenshot(page, 'retention-10-juggler-night-decision')
      }

      const outcome = await settleFreeDecision(page, String(EXPECTED_CORRECT))
      check('说书人给出猜对数量被受理（数字仍由他说书）', outcome.kind === 'Accepted', outcome.raw)
      jugglerCountGiven = true
      continue
    }

    if (decision.context.length > 0) {
      // 其余自由决定类裁定点（本局的神谕者）：如实结清，不让它挡住本夜；形状收进清单待判。
      if (!unhandled.includes(decision.context)) {
        unhandled.push(decision.context)
      }

      const outcome = await settleFreeDecision(page, '0')
      if (outcome.kind !== 'Accepted') {
        console.error(`[裁定失败] 未识别的自由裁定点：${outcome.raw}`)
        break
      }

      continue
    }

    const witchState = await readAttributeBounded(witchRequest, 'data-request-state')
    if (witchState === 'pending') {
      if (!cursedKlutz) {
        check(
          `女巫（${WITCH_SEAT} 号）在第 2 夜被唤醒（每夜能力照常行动）`,
          witchState === 'pending',
          `data-request-state=${witchState}`,
        )
        await answerSeatRequest(witchPage, KLUTZ_SEAT)
        cursedKlutz = true
      }

      await sleep(200)
      continue
    }

    if (cursedKlutz && !witchIdleConfirmed) {
      const back = await waitForAttribute(witchRequest, 'data-request-state', 'idle', 30_000)
      check('女巫诅咒提交后请求区回到空态', back === 'idle', `data-request-state=${back}`)
      witchIdleConfirmed = true
    }

    const demonRequests = demonSeat.takeRequests()
    if (demonRequests.length > 0) {
      for (const request of demonRequests) {
        console.log(
          `  [恶魔请求] ${compact(request.context ?? request.requestId).slice(0, 120)} → 提交 seat:${WITCH_SEAT}`,
        )
        await demonSeat.invoke('SubmitResponse', request.requestId, `seat:${WITCH_SEAT}`, 'retention-kill-witch', 1)
      }

      demonKilledWitch = true
      continue
    }

    if (await pendingRequestVisible(page)) {
      await sleep(300)
      continue
    }

    const advanced = await waitForSlotProgress(page)
    if (advanced.kind === 'Completed') {
      break
    }

    await sleep(200)
  }

  check(
    '第 2 夜两名夜间角色都在自己的格上完成了行动（爪牙段与恶魔段都到了）',
    cursedKlutz && demonKilledWitch,
    `诅咒=${cursedKlutz}；击杀=${demonKilledWitch}`,
  )

  const witchCard = page.locator(`[data-testid="grimoire-seat"][data-seat="${WITCH_SEAT}"]`)
  const witchLife = await waitForAttribute(witchCard, 'data-life', 'Dead', 30_000)
  check(`亡骨魔的击杀生效：${WITCH_SEAT} 号（爪牙）死亡`, witchLife === 'Dead', `data-life=${witchLife}`)
  const retentionWindow = await waitForEffectWindow(page, '保留能力', 30_000)
  check('死者带着「保留能力」窗口（死亡不等于失去能力）', retentionWindow.includes('保留能力'), retentionWindow.slice(0, 200))
  const poisonLedger = linesOf(await panelText(page, '状态账'), `${ORACLE_SEAT} 号`)
  check(
    `说书人选定的逆时针侧最近镇民（${ORACLE_SEAT} 号）中毒（状态账）`,
    poisonLedger.includes('中毒'),
    poisonLedger.slice(0, 220) || '（状态账里没有该席位行）',
  )
  await screenshot(page, 'retention-11-dead-minion-retained')

  const jugglerInfo = await waitForAttribute(jugglerPage.getByTestId('player-information'), 'data-information-count', '1', 30_000)
  const jugglerText = compact(await readTextBounded(jugglerPage.getByTestId('player-information')))
  check('杂耍艺人当晚收到猜对数（只到本人）', jugglerInfo === '1', `data-information-count=${jugglerInfo}`)
  check(
    '报数内容就是说书人给的数量（平台只做推演与记录）',
    jugglerText.includes(String(EXPECTED_CORRECT)),
    jugglerText.slice(0, 200),
  )
  check(
    `无关席位（${SAVANT_SEAT} 号博学者）的信息面板只有自己那两条（报数没串台）`,
    (await informationCount(savantPage)) === 2,
    `data-information-count=${await informationCount(savantPage)}`,
  )
  await screenshot(jugglerPage, 'retention-12-juggler-night-count-player')

  const unexpected = unhandled.filter((context) => !(context.includes('神谕者') || context.includes('oracle')))
  check(
    '本夜其余的说书人裁定点都是预期内的回溯型信息（神谕者）',
    unexpected.length === 0,
    unexpected.join(' || ') || `神谕者裁定点 ${unhandled.length - unexpected.length} 次`,
  )
}

/** 第 3 夜：已经死亡的爪牙在自己的格上仍被唤醒（真浏览器收到请求）。 */
async function driveThirdNight(page, witchPage, savantPage, demonSeat) {
  const witchRequest = witchPage.locator('[data-testid="player-request-panel"]')
  const witchCard = page.locator(`[data-testid="grimoire-seat"][data-seat="${WITCH_SEAT}"]`)
  let deadMinionWoken = false
  let idleConfirmed = false
  let demonActed = false
  const deadline = Date.now() + 300_000

  while (Date.now() < deadline) {
    const decision = await readDecisionPanel(page)
    if (decision.context.includes('杂耍艺人')) {
      // 第 2 天没有猜测 → 本夜不该唤醒他（空选项 + Skip，R-0057-B）。真出现就如实结清并判红。
      check('第 3 夜：昨天（第 2 天）没有公开猜测，杂耍艺人不再被唤醒', false, decision.context.slice(0, 160))
      await settleFreeDecision(page, String(EXPECTED_CORRECT))
      continue
    }

    if (decision.context.length > 0) {
      const outcome = await settleFreeDecision(page, '0')
      if (outcome.kind !== 'Accepted') {
        break
      }

      continue
    }

    if ((await readAttributeBounded(witchRequest, 'data-request-state')) === 'pending') {
      if (!deadMinionWoken) {
        const life = await readAttributeBounded(witchCard, 'data-life')
        check(
          '第 3 夜：已经死亡（data-life=Dead）的女巫在自己的格上仍被唤醒',
          life === 'Dead',
          `data-life=${life}`,
        )
        await screenshot(witchPage, 'retention-13-dead-witch-woken')
        await answerSeatRequest(witchPage, KLUTZ_SEAT)
        deadMinionWoken = true
      }

      await sleep(200)
      continue
    }

    if (deadMinionWoken && !idleConfirmed) {
      const back = await waitForAttribute(witchRequest, 'data-request-state', 'idle', 30_000)
      check('死者提交后请求区回到空态（请求真的发到了她的设备）', back === 'idle', `data-request-state=${back}`)
      idleConfirmed = true
    }

    const demonRequests = demonSeat.takeRequests()
    if (demonRequests.length > 0) {
      // 杀一个已经死亡的玩家：能力用过，但不产生新的死亡（R-0056 第 1 条）。
      for (const request of demonRequests) {
        await demonSeat.invoke('SubmitResponse', request.requestId, `seat:${KLUTZ_SEAT}`, 'retention-kill-dead', 1)
      }

      demonActed = true
      continue
    }

    if (await pendingRequestVisible(page)) {
      await sleep(300)
      continue
    }

    const advanced = await waitForSlotProgress(page)
    if (advanced.kind === 'Completed') {
      break
    }

    await sleep(200)
  }

  check('保留能力的死亡爪牙确实在自己的行动格上被唤醒', deadMinionWoken, `唤醒=${deadMinionWoken}`)
  check('亡骨魔第 3 夜的击杀格也照常到达（杀已死者不产生新死亡）', demonActed, `恶魔行动=${demonActed}`)
  const life = await readAttributeBounded(witchCard, 'data-life')
  check('第 3 夜结束时她仍是死亡状态（唤醒不改变生死）', life === 'Dead', `data-life=${life}`)
  check(
    `博学者（${SAVANT_SEAT} 号）的信息面板没有多出别人的信息`,
    (await informationCount(savantPage)) === 2,
    `data-information-count=${await informationCount(savantPage)}`,
  )
}

/** 点玩家端请求里的某个席位并提交（浏览器席位）。 */
async function answerSeatRequest(page, seat) {
  await page
    .locator('[data-testid="player-request-options"] label', { hasText: `${seat} 号玩家` })
    .locator('input[type=radio]')
    .check()
  await page.getByTestId('player-submit').click()
}

/** 说书人侧裁定点面板：上下文文本 + 选项文案（`.options` 为空 = 自由决定类）。 */
async function readDecisionPanel(page) {
  const panel = page.getByTestId('console-decision')
  if ((await panel.count()) === 0) {
    return { context: '', options: [] }
  }

  const context = compact(await readTextBounded(panel.locator('.context')))
  const options = (
    await panel.locator('.options button').evaluateAll((nodes) =>
      nodes.map((node) => {
        // 摘掉「已死亡」标签再取文本：界面把标签嵌在按钮里，直接 textContent 会把标签文案混进候选。
        const preview = node.cloneNode(true)
        preview.querySelector('[data-testid="option-dead"]')?.remove()
        return preview.textContent ?? ''
      }),
    )
  ).map((text) => compact(text))
  return { context, options }
}

/** 说书人上报座位状态：先点选该席的牌（操作台按席位就近），再只报本次观测到的维度。 */
async function reportSeatState(page, report) {
  await page.locator(`[data-testid="grimoire-seat"][data-seat="${report.seat}"]`).click()
  const panel = page.locator('[data-testid="seat-console"]')
  await panel.waitFor({ state: 'visible', timeout: 10_000 })

  const checkboxes = panel.locator('.dimensions input[type=checkbox]')
  for (let index = 0; index < (await checkboxes.count()); index += 1) {
    await checkboxes.nth(index).uncheck().catch(() => {})
  }

  const dimension = panel.locator('.dimensions label', { hasText: report.dimensionLabel })
  await dimension.locator('input[type=checkbox]').check()
  await dimension.locator('select').selectOption(report.value)
  await panel.getByPlaceholder('变化原因（必填，会随事件流记录）').fill(report.reason)
  return runCommand(page, `上报-${report.dimensionLabel}`, () =>
    page.getByRole('button', { name: '上报', exact: true }).click(),
  )
}

/** 说书人按自由决定结清当前裁定点（无候选的信息类：数字 / 内容由他说书）。 */
async function settleFreeDecision(page, content) {
  await page.getByPlaceholder('自由决定的内容（可为空）').fill(content)
  return runCommand(page, '裁定', () => page.getByRole('button', { name: '按自由决定结清' }).click())
}

/**
 * 等槽位自己往前走——**本装置不强推**。
 *
 * 依据：槽位按配额自行推进是产品行为（空槽照样走配额）；而"强推当前槽位"（D-0014）作用于
 * **服务端此刻的当前槽位**，不是装置做决定时看到的那个——装置决定推走第 7 槽、点击生效前
 * 第 7 槽已自行走完，这一推就落到刚进入的第 8 槽（角色行动格）上，把它按 Override 了结。
 * 实测两次因此把女巫的请求推没了（装置随后整夜看不到那条请求 → 假红）。
 *
 * 所以这里只**等**：挂起请求 / 裁定点 / 角色行动格都交给它自己走，空槽按配额走完
 * （迭代档 0.3s/槽、取证档 2s/槽）。
 */
async function waitForSlotProgress(page) {
  const blocked = async () => {
    if (await pendingRequestVisible(page)) {
      return '有挂起请求'
    }

    if ((await readDecisionPanel(page)).context.length > 0) {
      return '有裁定点'
    }

    return null
  }

  const first = await blocked()
  if (first !== null) {
    return { kind: 'Blocked', raw: first }
  }

  const slot = await slotState(page)
  if (slot === 'Completed') {
    return { kind: 'Completed', raw: '本计划已走完' }
  }

  await sleep(400)
  const second = await blocked()
  if (second !== null) {
    return { kind: 'Blocked', raw: `刚出现：${second}` }
  }

  return { kind: 'Waiting', raw: `等槽位按配额推进（${slot ?? '空槽'}）` }
}

/** "当前步骤"面板给出的槽位状态：`Completed`（本计划已走完）/ `ActorSlot`（这一格是角色行动）/ null（空槽）。 */
async function slotState(page) {
  const digest = await panelText(page, '当前步骤')
  if (digest.includes('本计划已走完')) {
    return 'Completed'
  }

  return /行动者\s*\d+\s*号/.test(digest) ? 'ActorSlot' : null
}

/** 点「开夜」直到被受理（上一阶段靠节拍 / 强推走完）。 */
async function startNightWhenReady(page, nightNumber, timeoutMs = 120_000) {
  const operations = page.locator('section', { hasText: '兜底与推进' })
  await operations.locator('input[type="number"]').fill(String(nightNumber))
  const button = operations.getByRole('button', { name: /开夜/ })
  const deadline = Date.now() + timeoutMs
  let outcome = null
  let lastReport = 0
  while (Date.now() < deadline) {
    if (await button.isDisabled().catch(() => false)) {
      if (Date.now() - lastReport > 5_000) {
        lastReport = Date.now()
        console.log(`  [诊断] 开第 ${nightNumber} 夜仍不可用：${await stageDiagnostics(page)}`)
      }

      await sleep(300)
      continue
    }

    outcome = await runCommand(page, `开第 ${nightNumber} 夜`, () => button.click())
    if (outcome.kind === 'Accepted') {
      return outcome
    }

    await sleep(500)
  }

  console.log(`  [诊断] 开第 ${nightNumber} 夜超时：${await stageDiagnostics(page)}`)
  return outcome ?? { kind: 'Timeout', raw: '开夜按钮在期限内一直不可用' }
}

/** 点「开白天」直到被受理（上一夜走完才受理）。 */
async function startDayWhenReady(page, timeoutMs = 60_000) {
  const button = page.getByTestId('st-start-day')
  const deadline = Date.now() + timeoutMs
  let outcome = null
  let lastReport = 0
  while (Date.now() < deadline) {
    if (await button.isDisabled().catch(() => false)) {
      if (Date.now() - lastReport > 5_000) {
        lastReport = Date.now()
        console.log(`  [诊断] 开白天仍不可用：${await stageDiagnostics(page)}`)
      }

      await sleep(300)
      continue
    }

    outcome = await runCommand(page, '开白天', () => button.click())
    if (outcome.kind === 'Accepted') {
      return outcome
    }

    await sleep(500)
  }

  console.log(`  [诊断] 开白天超时：${await stageDiagnostics(page)}`)
  return outcome ?? { kind: 'Timeout', raw: '开白天按钮在期限内一直不可用' }
}

/** 夜里的时序读数：槽位 / 卡点 / 裁定点 / 女巫请求态 / 恶魔请求数（只在状态变化时打印）。 */
async function nightTrace(page, witchPage, demonSeat) {
  const slots = await readSlotCounter(page)
  const pending = await pendingRequestVisible(page)
  const decision = await readDecisionPanel(page)
  const witchState = await readAttributeBounded(
    witchPage.locator('[data-testid="player-request-panel"]'),
    'data-request-state',
  )
  const demonRequests = demonSeat.messages.filter((message) => message.method === 'ReceiveOperationRequest').length
  return `槽位=${slots === null ? '?' : `${slots.index + 1}/${slots.total}`} 格=${(await slotState(page)) ?? '空'} `
    + `卡点=${pending} 裁定点=${decision.context.slice(0, 16) || '无'} 女巫=${witchState ?? 'null'} 恶魔请求=${demonRequests}`
}

/** 阶段卡住时的现场读数：槽位计数 / 当前步骤 / 卡点 / 裁定点。 */
async function stageDiagnostics(page) {
  const slots = await readSlotCounter(page)
  const pending = await pendingRequestVisible(page)
  const decision = await readDecisionPanel(page)
  const digest = compact(await panelText(page, '当前步骤'))
  return `槽位=${slots === null ? '不可读' : `${slots.index + 1}/${slots.total}`} 挂起=${pending} `
    + `裁定点=${decision.context.slice(0, 60) || '无'} 步骤=${digest.slice(0, 160) || '不可读'}`
}

/** "当前步骤"里的槽位计数（X / Y）→ { index（从 0 起）, total }；读不到返回 null。 */
async function readSlotCounter(page) {
  return page
    .evaluate(() => {
      const cells = [...document.querySelectorAll('header.strip .cell')]
      for (const cell of cells) {
        if (cell.querySelector('.caption')?.textContent?.trim() !== '槽位') {
          continue
        }

        const match = /(\d+)\s*\/\s*(\d+)/.exec(cell.textContent ?? '')
        if (match) {
          return { index: Number.parseInt(match[1], 10) - 1, total: Number.parseInt(match[2], 10) }
        }
      }

      return null
    })
    .catch(() => null)
}

/** 当前槽位是否有挂起请求：有就不能强推（强推会把它按 Override 了结）。 */
async function pendingRequestVisible(page) {
  const pending = page.locator('[data-testid="console-pending"]')
  return (await pending.count()) > 0 && (await pending.first().isVisible().catch(() => false))
}

/** 当前步骤面板是否显示"本计划已走完"。 */
async function planCompleted(page) {
  return (await panelText(page, '当前步骤')).includes('本计划已走完')
}

/** 展开「数据与审计」下钻面板（幂等）：状态账 / 效果链 / 当前步骤都在里面。 */
async function openDataDrawer(page) {
  const toggle = page.locator('[data-testid="data-drawer-toggle"]')
  if ((await toggle.count()) === 0) {
    return
  }

  if ((await readAttributeBounded(toggle, 'aria-expanded')) !== 'true') {
    await toggle.click()
  }
}

/** 某个 section.panel（按标题定位）的可见文本；找不到返回空串。 */
async function panelText(page, heading) {
  await openDataDrawer(page)
  const drawerBody = page.locator('[data-testid="data-drawer-body"]')
  const scope = (await drawerBody.count()) > 0 ? drawerBody : page
  const panel = scope.locator('section.panel', { hasText: heading })
  if ((await panel.count()) === 0) {
    return ''
  }

  return await readTextBounded(panel.first())
}

/** 等效果链里出现含目标窗口名的行（`data-window` 由前端按服务端给的窗口渲染）。 */
async function waitForEffectWindow(page, needle, timeoutMs) {
  await openDataDrawer(page)
  const deadline = Date.now() + timeoutMs
  let text = ''
  while (Date.now() < deadline) {
    text = compact(await readTextBounded(page.locator('[data-window="true"]').filter({ hasText: needle }).first()))
    if (text.includes(needle)) {
      return text
    }

    await sleep(200)
  }

  return text
}

/** 玩家信息面板的信息计数（data-information-count）；面板不在时返回 -1。 */
async function informationCount(page) {
  const panel = page.locator('[data-testid="player-information"]')
  if ((await panel.count()) === 0) {
    return -1
  }

  const raw = await readAttributeBounded(panel, 'data-information-count')
  return raw === null ? -1 : Number(raw)
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
    messages,
    /** 取走"还没处理过"的请求（跨夜复用同一个探针，取走即不再重复处理）。 */
    takeRequests: () => requests.splice(0, requests.length),
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

async function waitForCount(locator, expected, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if ((await locator.count()) === expected) {
      return true
    }

    await sleep(200)
  }

  return false
}

async function screenshot(page, name) {
  if (!config.screenshots) {
    console.log(`  截图（迭代档跳过落盘）：${name}`)
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
  console.log('\n=== 保留能力与白天信息族批次（retention-day-info）取证结论 ===')
  for (const result of results) {
    console.log(`${result.pass ? 'PASS' : 'FAIL'}  ${result.label}${result.detail ? ` → ${result.detail}` : ''}`)
  }

  const failed = results.filter((result) => !result.pass)
  console.log(failed.length === 0 ? `全部通过（${results.length} 项）` : `失败 ${failed.length} / ${results.length}`)
}

function compact(text) {
  return String(text ?? '').replace(/\s+/g, ' ').trim()
}

/** 取面板文本里含某个席位的行（把断言钉在"真正渲染数据的面板"内）。 */
function linesOf(text, needle) {
  return text
    .split('\n')
    .filter((line) => line.includes(needle))
    .join(' / ')
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
