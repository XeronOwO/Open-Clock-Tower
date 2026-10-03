/**
 * 回溯型信息族（卖花女孩 / 城镇公告员 / 神谕者）批次装置 —— 票据
 * docs/backlog/in-progress/retrospective-info-family.md 的验收矩阵行 1、5、6、8。
 *
 * 它回答：**「读白天账推演 + 说书人裁定 + 信息只到本人」这条链路在真界面上跑得通吗？**
 * 三个角色都是「除首夜外每夜」的**记录读者**：无玩家选项 → 说书人裁定点 → 信息只发本人。
 *
 * 场景（固定 6 席：1 诺-达鲺（恶魔）/ 2 麻脸巫婆（爪牙）/ 3 卖花女孩 / 4 城镇公告员 /
 * 5 神谕者 / 6 畸形秀演员（善良陪跑））：
 *   1) 分配 → 开首夜（Original，1 号口径）：六席里没有任何首夜行动者 → 整夜无裁定点、恶魔也不被唤醒；
 *   2) 开白天 1：2 号（爪牙）在自己页面上提名 2 号自己；1 号（恶魔）在自己页面上投赞成、4 / 5 号也投
 *      赞成（3 票 × 2 = 存活 6 席，达到计票阈值）；说书人计票 → 结束白天并处决 → 2 号「死亡且邪恶」；
 *   3) 开第二夜（Original，2 号）：1 号恶魔收到操作请求 → 在页面上选 6 号（善良陪跑，不影响推演）；
 *   4) 依次断言三个裁定点（真界面文本）并结清：
 *      - 卖花女孩：白天账里有「恶魔举手」⇒ 提示必须含「推演：是」；给出「恶魔参与了投票」；
 *      - 城镇公告员：白天账里有「爪牙发起提名」⇒ 提示必须含「推演：是」；给出「有爪牙发起了提名」；
 *      - 神谕者：当前账「死亡且邪恶」1 席（2 号被处决）⇒ 提示必须含「推演：1」；给出「1」。
 *      每个裁定点都顺带断言**该提示不是上一个裁定点的残留**（提示里的角色名自证归属 +
 *      裁定点标识必须换新），否则「读到上一个槽位的文本」会假绿；
 *   5) 信息只到本人：3 / 4 / 5 号玩家页各出现自己的那条（ability slug + 内容，按行读 DOM），
 *      且**互不串台**（3 号页没有另两条内容、4 / 5 号同理）；
 *   6) 反方向零下发（D-0012 §4.3 信息隔离）：无关席位 2 号与 6 号的信息面板零下发（count=0 + 空态文案），
 *      六席各自那条连接上收到的全部推送里没有别的能力 slug、没有说书人视角的「推演」行。
 *
 * 一处必须写明的连接口径（本装置踩过的坑）：**服务端每个席位只保留一条连接**
 * （ConnectionRegistry.IssueForSeat：同席新连接会立即吊销旧连接的凭据）。因此本装置**不给任何席位
 * 另开第二条连接**——提名 / 投票 / 作答全部走玩家页自己的连接，推送扫描则挂在**玩家页自己的
 * WebSocket 帧**上（page.on('websocket')）。另开 SignalR 客户端会把浏览器页的凭据挤掉，页面从此
 * 收不到任何推送，而那些断言仍然"绿"——这是假绿，不是能力通过。
 *
 * 与主批次的分工：主批次（verify-storyteller-panel.mjs）跑五席固定花名册的通用玩法回归，
 * 本装置只跑这一族能力链路。
 *
 * 前置：Node >= 22.5（node:sqlite）、本机已构建 web/node_modules（playwright）。
 * 用法（在仓库根运行；默认迭代档 = 快节奏 + 不落盘截图 + 复用产物）：
 *   node tools/verify-retro-info.mjs                                        # 迭代档
 *   node tools/verify-retro-info.mjs --quota 2 --screenshots-all --build    # 取证档（一批一次）
 *   node tools/verify-retro-info.mjs --port 5416 --vite-port 5296           # 自定端口
 *
 * 外部耦合（换机器先核对 web/AGENTS.md §3.1）：宿主编译产物路径、SQLite 表 Games 的
 * StorytellerTicket / SeatsJson 列形状、SignalR 默认 JSON 协议的帧形状（`{"type":1,"target":…}`）。
 * 退出码：0 = 全过；1 = 有失败；2 = 环境缺依赖。
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

/** 档位（tools/lib/verify-profile.mjs）：默认迭代档；取证档显式 `--quota 2 --screenshots-all`。 */
const config = resolveProfile(flags, { quotaSeconds: 0.3 })
const results = []
const children = []
let playwright = null

try {
  const requireFromWeb = createRequire(path.join(webRoot, 'package.json'))
  playwright = requireFromWeb('playwright')
} catch (error) {
  console.error(`缺少依赖（playwright）：${String(error)}`)
  console.error('先运行：cd web; npm install; npx playwright install chromium')
  process.exit(2)
}

console.log(`档位：${describeProfile(config)}`)

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-retro-info-'))
const databasePath = path.join(workspace, 'retro-info.db')
const screenshotsDir = path.resolve(repositoryRoot, 'artifacts', 'web')
mkdirSync(screenshotsDir, { recursive: true })

const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`

/** 六个席位：2 号是爪牙（白天提名自己并因此被处决），3 / 4 / 5 号是三个信息角色，6 号是善良陪跑。 */
const ASSIGN = ['no-dashii', 'pit-hag', 'flowergirl', 'town-crier', 'oracle', 'mutant']
/** 席位号从花名册顺序派生：调换 ASSIGN 顺序时断言跟着走，不靠人工同步。 */
const seatOf = (slug) => ASSIGN.indexOf(slug) + 1
const DEMON_SEAT = seatOf('no-dashii')
const MINION_SEAT = seatOf('pit-hag')
const FLOWERGIRL_SEAT = seatOf('flowergirl')
const TOWN_CRIER_SEAT = seatOf('town-crier')
const ORACLE_SEAT = seatOf('oracle')
const BYSTANDER_SEAT = seatOf('mutant')
/** 反方向的无关席位：2 号（被处决的爪牙）与 6 号（善良陪跑）。 */
const UNRELATED_SEATS = [MINION_SEAT, BYSTANDER_SEAT]

/** 三个信息角色在信息结果里的 ability slug（与契约 AbilityId 同字面）。 */
const FLOWERGIRL_CONTENT = '恶魔参与了投票'
const TOWN_CRIER_CONTENT = '有爪牙发起了提名'
const ORACLE_CONTENT = '1'
/** 三个能力 slug：既用于「只推给本人」，也用于越权扫描。 */
const INFO_ABILITIES = ['flowergirl', 'town-crier', 'oracle']
/** 「不是自己的那条内容」——串台探针：三条内容互不包含（'1' 太泛，只用于本人那一行）。 */
const OTHER_CONTENTS = new Map([
  ['flowergirl', [TOWN_CRIER_CONTENT, ORACLE_CONTENT]],
  ['town-crier', [FLOWERGIRL_CONTENT]],
  ['oracle', [FLOWERGIRL_CONTENT, TOWN_CRIER_CONTENT]],
])

/** 信号帧的默认 JSON 协议：调用（广播）帧形如 `{"type":1,"target":"ReceiveX","arguments":[{…}]}`。 */
const INVOCATION_TYPE = 1

/**
 * 玩家端不该出现的词：说书人裁定提示的推演行 / 失效标记（D-0012 §4.3）。
 * 推演原文形如「按白天账推演：是」「按当前账推演：1」——那是**说书人视角**的提示，
 * 一旦出现在玩家连接或玩家页面上，等于把"平台已经替你算好了"泄露给玩家。
 */
const FORBIDDEN_PLAYER_TOKENS = [
  '推演',
  'MayBeFalse',
  'malfunction',
  'Malfunctions',
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
  await ensureServerArtifacts({ repositoryRoot, buildMode: config.buildMode })
  await startServer()

  console.log('=== 2/8 取票据并起 Vite ===')
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

  console.log('=== 3/8 说书人 + 六席玩家页加入真浏览器（一席一条连接）===')
  const browser = await playwright.chromium.launch()
  const consoleErrors = []
  const storytellerPage = await newPage(browser, { width: 1600, height: 1100 }, consoleErrors, null)
  await storytellerPage.goto(viteUrl)
  await storytellerPage.getByPlaceholder('说书人票据').fill(ticket)
  await storytellerPage.getByRole('button', { name: '加入' }).click()
  await storytellerPage.locator('[data-testid="grimoire"]').waitFor({ timeout: 30_000 })
  check('说书人加入后看板可见（魔典主视图）', (await storytellerPage.locator('[data-testid="grimoire"]').count()) === 1)

  /**
   * 六席各开**一个**浏览器上下文（服务端每席只认一条连接，见文件头「连接口径」）：
   * 三个信息角色 + 恶魔要真点界面上按钮与选项，两个无关席位提供反方向的零下发证据。
   */
  const playerPages = new Map()
  const frameSinks = new Map()
  const joinOrder = [DEMON_SEAT, ...UNRELATED_SEATS, FLOWERGIRL_SEAT, TOWN_CRIER_SEAT, ORACLE_SEAT]
  for (const seat of joinOrder) {
    const sink = createFrameSink()
    frameSinks.set(seat, sink)
    const page = await newPage(browser, { width: 900, height: 1000 }, consoleErrors, sink)
    await page.goto(`${viteUrl}/#player`)
    await page.getByPlaceholder('席位票据').fill(seatTickets[seat - 1].ticket)
    await page.getByRole('button', { name: '加入' }).click()
    const badge = await waitForText(page.locator('[data-testid="player-seat"]'), String(seat), 30_000)
    const role = UNRELATED_SEATS.includes(seat) ? '无关席位' : '信息角色'
    check(`${seat} 号（${ASSIGN[seat - 1]}，${role}）加入玩家端`, badge.includes(String(seat)), badge)
    playerPages.set(seat, page)
  }

  const infoPages = new Map([FLOWERGIRL_SEAT, TOWN_CRIER_SEAT, ORACLE_SEAT].map((seat) => [seat, playerPages.get(seat)]))
  const unrelatedPages = new Map(UNRELATED_SEATS.map((seat) => [seat, playerPages.get(seat)]))

  console.log('=== 4/8 开局分配 → 开首夜（Original，1 号）===')
  const assignmentSelects = storytellerPage.locator('section', { hasText: '开局分配' }).locator('select')
  for (const [index, slug] of ASSIGN.entries()) {
    await assignmentSelects.nth(index).selectOption(slug)
  }

  const assigned = await runCommand(storytellerPage, '分配', () =>
    storytellerPage.getByRole('button', { name: '提交分配' }).click(),
  )
  check('分配 6 个角色被受理', assigned.kind === 'Accepted', assigned.raw)

  const nightStarted = await runCommand(storytellerPage, '开首夜', () => startNight(storytellerPage, 1))
  check('开首夜被受理（Original 顺序表建表）', nightStarted.kind === 'Accepted', nightStarted.raw)

  const nightOneComplete = await finishNightQuickly(storytellerPage, '首夜')
  check(
    '首夜：六席无首夜行动者 → 整夜自然走完（未强推）',
    nightOneComplete.completed === true && nightOneComplete.natural === true,
    nightOneComplete.natural
      ? `自然窗口（${config.slowPacer ? 12 : 8}s）内走完`
      : `强推 ${nightOneComplete.forced} 步${nightOneComplete.note ? `（${nightOneComplete.note}）` : ''}`,
  )
  const nightOneDecisionId = await decisionId(storytellerPage)
  check(
    '首夜：没有任何说书人裁定点（三者在顺序表上只属「其他夜晚」）',
    nightOneDecisionId === null,
    `裁定点=${nightOneDecisionId}`,
  )
  const nightOneDigest = await panelText(storytellerPage, '当前步骤')
  check(
    '首夜：说书人「当前步骤」摘要确认为「本计划已走完」',
    nightOneDigest.includes('本计划已走完'),
    compact(nightOneDigest).slice(0, 160),
  )

  // 顺带证明"推送真的会到达玩家页"：阶段推送在连接上被看到（防止"零推送"式的假绿）。
  const phasePushes = await waitForFrames(frameSinks.get(DEMON_SEAT), (frames) =>
    frames.some((frame) => frame.parsed?.target === 'ReceivePhaseStarted'),
  )
  check(
    '1 号页在自己的连接上收到阶段推送（推送扫描确有信号，不是空集假绿）',
    phasePushes,
    `已捕获 ${describeFrameCounts(frameSinks)}`,
  )

  console.log('=== 5/8 白天 1：爪牙提名自己 → 恶魔与 4 / 5 号投赞成 → 计票 → 处决 ===')
  const dayStarted = await runCommand(storytellerPage, '开白天', () => storytellerPage.getByTestId('st-start-day').click())
  check('开白天被受理', dayStarted.kind === 'Accepted', dayStarted.raw)
  const dayStatus = await waitForAttribute(storytellerPage.getByTestId('st-day'), 'data-day-status', 'Open', 30_000)
  check('说书人面板进入「白天进行中」', dayStatus === 'Open', `data-day-status=${dayStatus}`)

  // 2 号（爪牙）在**自己的页面**上提名 2 号自己：提名者当时的角色快照 = 爪牙 ⇒ 城镇公告员推演「是」。
  const minionPage = playerPages.get(MINION_SEAT)
  await minionPage.getByTestId('player-nominee-select').selectOption(String(MINION_SEAT))
  await minionPage.getByTestId('player-nominate').click()

  const nominationList = storytellerPage.getByTestId('st-day-nominations')
  const nominationCount = await waitForAttribute(nominationList, 'data-nomination-count', '1', 30_000)
  check('爪牙自我提名进入公开账目（1 条）', nominationCount === '1', `data-nomination-count=${nominationCount}`)

  // 投赞成的三票：1 号（恶魔，快照 = 恶魔 ⇒ 卖花女孩推演「是」）+ 4 号 + 5 号。
  // 3 票 × 2 = 6 = 存活席位数，达到计票阈值（DayMachine：votes * 2 >= alive）。
  for (const voterSeat of [DEMON_SEAT, TOWN_CRIER_SEAT, ORACLE_SEAT]) {
    await castYesVote(playerPages.get(voterSeat), voterSeat)
  }

  const firstNomination = nominationList.locator('li').first()
  const voteCount = await waitForAttribute(firstNomination, 'data-nomination-votes', '3', 30_000)
  check('三次「投赞成」都到服务端（公开票数 3）', voteCount === '3', `data-nomination-votes=${voteCount}`)

  const counted = await runCommand(storytellerPage, '计票', () => storytellerPage.getByTestId('st-count-votes').click())
  check('计票被受理', counted.kind === 'Accepted', counted.raw)

  const aboutSeat = await waitForAttribute(
    storytellerPage.getByTestId('st-about-to-be-executed'),
    'data-seat',
    String(MINION_SEAT),
    30_000,
  )
  check('票数达标 → 进入「即将被处决」', aboutSeat === String(MINION_SEAT), `data-seat=${aboutSeat}`)

  const closed = await runCommand(storytellerPage, '结束白天并处决', () => storytellerPage.getByTestId('st-close-day').click())
  check('结束白天被受理', closed.kind === 'Accepted', closed.raw)
  const executedSeat = await waitForAttribute(
    storytellerPage.getByTestId('st-executed'),
    'data-seat',
    String(MINION_SEAT),
    30_000,
  )
  const executedLife = await waitForAttribute(
    storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${MINION_SEAT}"]`),
    'data-life',
    'Dead',
    15_000,
  )
  check(
    '爪牙被处决：处决记录 + 席位死亡（神谕者读数因此 = 1）',
    executedSeat === String(MINION_SEAT) && executedLife === 'Dead',
    `处决=${executedSeat}；牌面=${executedLife}`,
  )

  console.log('=== 6/8 第二夜（Original，2 号）：恶魔选 6 号 → 三个裁定点依次推演 ===')
  const nightTwo = await runCommand(storytellerPage, '开第二夜', () => startNight(storytellerPage, 2))
  check('开第二夜被受理', nightTwo.kind === 'Accepted', nightTwo.raw)

  const demonPage = playerPages.get(DEMON_SEAT)
  const demonAsked = await waitForFrames(frameSinks.get(DEMON_SEAT), (frames) =>
    frames.some((frame) => frame.parsed?.target === 'ReceiveOperationRequest'),
  )
  check('第二夜恶魔（1 号）页在自己连接上收到操作请求', demonAsked, `已捕获 ${describeFrameCounts(frameSinks)}`)

  const demonRequest = await waitForRequestPanel(demonPage, 60_000)
  check(
    `恶魔的请求面板开出「选一名玩家」的玩家端选项（含 ${BYSTANDER_SEAT} 号）`,
    demonRequest.options.includes(`seat:${BYSTANDER_SEAT}`),
    `选项=${demonRequest.options.join(', ')}；上下文=${demonRequest.context.slice(0, 120)}`,
  )
  await demonPage
    .getByTestId('player-request-options')
    .locator(`label.option[data-option-value="seat:${BYSTANDER_SEAT}"]`)
    .click()
  const submitAccepted = await submitRequestAndAwaitOutcome(demonPage, frameSinks.get(DEMON_SEAT))
  const requestIdle = await waitForRequestPanelIdle(demonPage, 30_000)
  check(
    `恶魔在页面上选 ${BYSTANDER_SEAT} 号（善良陪跑）并提交：服务端回执 Accepted + 请求区回到空态`,
    submitAccepted && requestIdle,
    `回执=${submitAccepted}；data-request-state=${await demonPage.getByTestId('player-request-panel').getAttribute('data-request-state')}`,
  )

  // —— 裁定点 1：卖花女孩（恶魔白天投过赞成）——
  const flowergirlDecision = await waitForDecision(
    storytellerPage,
    (text) => text.includes('卖花女孩') && text.includes('推演：'),
    90_000,
  )
  check(
    '卖花女孩裁定提示含「推演：是」（白天账里恶魔投过赞成；R-0037 按举手动作的角色快照）',
    flowergirlDecision.includes('卖花女孩') && flowergirlDecision.includes('推演：是'),
    compact(flowergirlDecision),
  )
  await screenshot(storytellerPage, 'retro-01-flowergirl-decision')
  const flowergirlDecisionId = await decisionId(storytellerPage)

  const flowergirlSettled = await settleFreeDecision(storytellerPage, FLOWERGIRL_CONTENT)
  check('卖花女孩裁定结清被受理', flowergirlSettled.kind === 'Accepted', flowergirlSettled.raw)

  // —— 裁定点 2：城镇公告员（爪牙白天发起提名）——
  const townCrierDecision = await waitForDecision(
    storytellerPage,
    (text) => text.includes('城镇公告员') && text.includes('推演：'),
    90_000,
  )
  const townCrierDecisionId = await decisionId(storytellerPage)
  check(
    '城镇公告员裁定提示含「推演：是」（白天账里爪牙发起过提名）',
    townCrierDecision.includes('城镇公告员') && townCrierDecision.includes('推演：是'),
    compact(townCrierDecision),
  )
  check(
    '城镇公告员的提示是**新的**裁定点（不是卖花女孩那条的残留）',
    townCrierDecision.includes('城镇公告员')
      && !townCrierDecision.includes('卖花女孩')
      && !townCrierDecision.includes(FLOWERGIRL_CONTENT)
      && townCrierDecisionId !== null
      && townCrierDecisionId !== flowergirlDecisionId,
    `id=${townCrierDecisionId}（卖花女孩 id=${flowergirlDecisionId}）`,
  )
  await screenshot(storytellerPage, 'retro-02-towncrier-decision')

  const townCrierSettled = await settleFreeDecision(storytellerPage, TOWN_CRIER_CONTENT)
  check('城镇公告员裁定结清被受理', townCrierSettled.kind === 'Accepted', townCrierSettled.raw)

  // —— 裁定点 3：神谕者（当前账「死亡且邪恶」1 席：2 号被处决）——
  const oracleDecision = await waitForDecision(
    storytellerPage,
    (text) => text.includes('神谕者') && text.includes('推演：'),
    90_000,
  )
  const oracleDecisionId = await decisionId(storytellerPage)
  check(
    '神谕者裁定提示含「推演：1」（2 号死亡且邪恶；含当夜死者、按当前阵营）',
    oracleDecision.includes('神谕者') && oracleDecision.includes('推演：1'),
    compact(oracleDecision),
  )
  check(
    '神谕者的提示是**新的**裁定点（不是城镇公告员那条的残留）',
    oracleDecision.includes('神谕者')
      && !oracleDecision.includes('城镇公告员')
      && !oracleDecision.includes(TOWN_CRIER_CONTENT)
      && oracleDecisionId !== null
      && oracleDecisionId !== townCrierDecisionId,
    `id=${oracleDecisionId}（城镇公告员 id=${townCrierDecisionId}）`,
  )
  await screenshot(storytellerPage, 'retro-03-oracle-decision')

  const oracleSettled = await settleFreeDecision(storytellerPage, ORACLE_CONTENT)
  check('神谕者裁定结清被受理（输入 1）', oracleSettled.kind === 'Accepted', oracleSettled.raw)

  console.log('=== 7/8 信息只到本人：3 / 4 / 5 号页各一条，互不串台 ===')
  const expectedInformation = [
    [FLOWERGIRL_SEAT, 'flowergirl', FLOWERGIRL_CONTENT],
    [TOWN_CRIER_SEAT, 'town-crier', TOWN_CRIER_CONTENT],
    [ORACLE_SEAT, 'oracle', ORACLE_CONTENT],
  ]
  const informationRows = new Map()
  for (const [seat, ability, content] of expectedInformation) {
    const page = infoPages.get(seat)
    const count = await waitForAttributeValue(() => readPlayerInformationCount(page), '1', 30_000)
    const rows = await readPlayerInformationRows(page)
    informationRows.set(seat, rows)
    check(
      `${seat} 号玩家页出现一条信息结果（${ability} / ${content}）`,
      count === '1' && rows.length === 1 && rows[0].ability === ability && rows[0].content === content,
      `count=${count}；行=${JSON.stringify(rows)}`,
    )
  }
  await screenshot(infoPages.get(FLOWERGIRL_SEAT), 'retro-04-player-flowergirl-info')

  for (const [seat, ability] of expectedInformation.map(([seat, ability]) => [seat, ability])) {
    const rows = informationRows.get(seat)
    const leaked = OTHER_CONTENTS.get(ability).filter((content) => rows.some((row) => row.content.includes(content)))
    check(
      `${seat} 号页没有别人的那条内容（${ability} 的行只有自己的）`,
      rows.length > 0 && rows.every((row) => row.ability === ability) && leaked.length === 0,
      leaked.join(' | ') || `行=${JSON.stringify(rows)}`,
    )
  }

  console.log('=== 8/8 反方向：无关席位零下发 ===')
  for (const seat of UNRELATED_SEATS) {
    const count = await readPlayerInformationCount(unrelatedPages.get(seat))
    const text = await readPlayerInformationText(unrelatedPages.get(seat))
    check(
      `无关席位（${seat} 号）信息面板零下发（count=0 + 空态文案）`,
      count === '0' && text.includes('还没有收到信息') && !text.includes(FLOWERGIRL_CONTENT) && !text.includes(TOWN_CRIER_CONTENT),
      `count=${count}；文本=${compact(text)}`,
    )
  }
  await screenshot(unrelatedPages.get(BYSTANDER_SEAT), 'retro-05-unrelated-player-clean')

  // —— 连接层（真 SignalR 帧）的全量扫描：每席只看自己那条连接收到的推送 ——
  const expectedRecipients = { flowergirl: FLOWERGIRL_SEAT, 'town-crier': TOWN_CRIER_SEAT, oracle: ORACLE_SEAT }
  const receivedBySeat = new Map()
  for (const [seat, sink] of frameSinks.entries()) {
    receivedBySeat.set(seat, sink.frames.filter((frame) => frame.parsed !== null))
  }

  const misdelivered = []
  const pushedAbilities = []
  for (const [seat, frames] of receivedBySeat.entries()) {
    for (const frame of frames.filter((entry) => entry.parsed.target === 'ReceiveInformationResult')) {
      const ability = String(frame.parsed.arguments?.[0]?.ability ?? '')
      pushedAbilities.push(`${seat}:${ability}`)
      if (expectedRecipients[ability] !== seat) {
        misdelivered.push(`${seat} 号收到 ${ability}`)
      }
    }
  }
  check(
    '信息结果只推给本人（六席连接各自的推送全量扫描）',
    misdelivered.length === 0
      && pushedAbilities.includes(`${FLOWERGIRL_SEAT}:flowergirl`)
      && pushedAbilities.includes(`${TOWN_CRIER_SEAT}:town-crier`)
      && pushedAbilities.includes(`${ORACLE_SEAT}:oracle`),
    misdelivered.join(' | ') || `下发=${pushedAbilities.join(', ')}`,
  )

  const unrelatedPushes = UNRELATED_SEATS.flatMap((seat) =>
    receivedBySeat
      .get(seat)
      .filter((frame) => frame.parsed.target === 'ReceiveInformationResult')
      .map((frame) => `${seat}:${String(frame.parsed.arguments?.[0]?.ability ?? '')}`),
  )
  check('无关席位（2 / 6 号）连接上零信息下发', unrelatedPushes.length === 0, unrelatedPushes.join(', ') || '零条')

  const crossSeatLeak = []
  for (const [seat, frames] of receivedBySeat.entries()) {
    const owned = Object.entries(expectedRecipients)
      .filter(([, owner]) => owner === seat)
      .map(([ability]) => ability)
    for (const frame of frames) {
      for (const ability of INFO_ABILITIES.filter((candidate) => !owned.includes(candidate))) {
        if (JSON.stringify(frame.parsed.arguments ?? null).includes(ability)) {
          crossSeatLeak.push(`${seat} 号收到含 ${ability} 的 ${frame.parsed.target}`)
        }
      }
    }
  }
  check(
    '三个能力 slug 没有出现在其他任何席位的任何推送里',
    crossSeatLeak.length === 0,
    crossSeatLeak.join(' | ') || '未命中',
  )

  const allFrames = receivedBySeat.size === 0
    ? ''
    : JSON.stringify([...receivedBySeat.values()].flat().map((frame) => frame.payload))
  const leakedTokens = FORBIDDEN_PLAYER_TOKENS.filter((token) => allFrames.includes(token))
  check(
    '六席连接收到的全部帧里没有说书人视角的推演行 / 失效字段',
    allFrames.length > 0 && leakedTokens.length === 0,
    leakedTokens.join(', ') || `已扫描 ${receivedBySeat.size} 席 / ${[...receivedBySeat.values()].reduce((total, frames) => total + frames.length, 0)} 帧`,
  )

  for (const [seat, page] of playerPages.entries()) {
    const pageText = compact(await page.locator('body').innerText())
    const hits = FORBIDDEN_PLAYER_TOKENS.filter((token) => pageText.includes(token))
    check(`${seat} 号页面文本没有说书人视角的推演行`, hits.length === 0, hits.join(', ') || '未命中')
  }

  check('浏览器控制台没有报错', consoleErrors.length === 0, consoleErrors.slice(0, 3).join(' | '))
  await browser.close()
}

// —— 连接层：每席只挂**它自己那条**连接的帧观察器（不再另开第二条连接，见文件头「连接口径」）——

/** 一席的连接帧收集器：received 记全部收到的帧，invocations 记发出去的调用。 */
function createFrameSink() {
  return { frames: [], invocations: [] }
}

/** 把 page 的 WebSocket 帧接到 sink 上（SignalR 默认 JSON 协议，文本帧）。 */
function attachFrameSink(page, sink) {
  page.on('websocket', (socket) => {
    socket.on('framereceived', (frame) => recordFrame(sink, 'received', frame.payload))
    socket.on('framesent', (frame) => {
      const entry = recordFrame(sink, 'sent', frame.payload)
      if (entry !== null && entry.parsed !== null) {
        sink.invocations.push(entry)
      }
    })
  })
}

function recordFrame(sink, direction, rawPayload) {
  const payload = typeof rawPayload === 'string' ? rawPayload : '<binary>'
  const messages = parseSignalRMessages(payload)
  const entry = { direction, payload, parsed: messages[0] ?? null, messages }
  sink.frames.push(entry)
  return entry
}

/**
 * 解析一帧 SignalR（默认 JSON 协议）里的**全部**消息。
 *
 * 帧尾带 `\x1e` 记录分隔符（SignalR 的 TextMessageFormat）：**不能**直接 JSON.parse，
 * 否则每条帧都会抛异常、被吞成"没有推送"——这会让所有推送扫描静默假绿（本装置踩过）。
 * 服务端还会把同一连接上先后写出的多条消息（如"视图推送 + 调用回执"）**合进一帧**：
 * 只取第一条会让回执被前面的推送遮住，表现为"提交明明生效了却等不到受理回执"
 * （限次信息族装置首跑实测；本装置同款修复）。
 */
function parseSignalRMessages(payload) {
  const messages = []
  for (const part of payload.split('\u001e')) {
    const trimmed = part.trim()
    if (!trimmed.startsWith('{')) {
      continue
    }

    try {
      const candidate = JSON.parse(trimmed)
      if (candidate !== null && typeof candidate === 'object') {
        messages.push(candidate)
      }
    } catch {
      // 半截 / 多段内容：跳过这一段，不吞掉整帧。
    }
  }

  return messages
}

/** 等连接帧里出现满足条件的帧（防"推送扫描是空集"式假绿）。 */
async function waitForFrames(sink, predicate, timeoutMs = 60_000) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if (predicate(sink.frames)) {
      return true
    }

    await sleep(200)
  }

  return predicate(sink.frames)
}

function describeFrameCounts(frameSinks) {
  return [...frameSinks.entries()]
    .map(([seat, sink]) => `${seat} 号 ${sink.frames.length} 帧`)
    .join('，')
}

/** 一席帧的明细（排障用）：方向 + target（或原始帧前 90 字）。 */
function describeFrames(sink) {
  return sink.frames
    .map((frame) => `${frame.direction === 'sent' ? '>' : '<'} ${frame.parsed?.target ?? compact(frame.payload).slice(0, 90)}`)
    .join(' || ')
}

/** 等恶魔页开出请求面板，返回上下文与选项（全部从真界面读）。 */
async function waitForRequestPanel(page, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  let context = ''
  let options = []
  while (Date.now() < deadline) {
    context = (await readTextOrNull(page, 'player-request-context')) ?? ''
    options = await page
      .getByTestId('player-request-options')
      .locator('label.option')
      .evaluateAll((labels) => labels.map((label) => label.getAttribute('data-option-value') ?? ''))
      .catch(() => [])
    if (context.length > 0 && options.length > 0) {
      return { context, options }
    }

    await sleep(200)
  }

  return { context, options }
}

async function newPage(browser, viewport, consoleErrors, frameSink) {
  const context = await browser.newContext({ viewport })
  const page = await context.newPage()
  page.setDefaultTimeout(30_000)
  if (frameSink !== null) {
    attachFrameSink(page, frameSink)
  }

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

/**
 * 开会话：在「兜底与推进」里选口径（回溯型信息族只在 OtherNight 有行动格，因此两夜都走 Original）
 * 并点「开夜」。口径与夜序都从真界面下发，命令面无隐藏默认。
 */
async function startNight(page, nightNumber) {
  const box = page.locator('section', { hasText: '兜底与推进' })
  await box.locator('input[type="number"]').fill(String(nightNumber))
  await box.locator('select').selectOption('Original')
  await page.getByRole('button', { name: /开夜/ }).click()
}

/** 说书人按自由决定结清当前裁定点。 */
async function settleFreeDecision(page, content) {
  await page.getByPlaceholder('自由决定的内容（可为空）').fill(content)
  return runCommand(page, '裁定', () => page.getByRole('button', { name: '按自由决定结清' }).click())
}

/** 当前待裁定的裁定点标识；null = 现在没有裁定点（也用于证明"这是新的裁定点"）。 */
async function decisionId(page) {
  const block = page.locator('[data-testid="console-decision"]')
  if ((await block.count()) === 0) {
    return null
  }

  const text = compact(await block.first().innerText())
  const matched = text.match(/[A-Za-z0-9_.:-]+/g) ?? []
  return matched.find((token) => token.includes(':')) ?? text.slice(0, 120)
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

/** 数据抽屉里某一段的文本（当前步骤 / 状态账 / …）。抽屉是懒渲染的：先展开再读。 */
async function panelText(page, heading) {
  const toggle = page.locator('[data-testid="data-drawer-toggle"]')
  if ((await toggle.getAttribute('aria-expanded')) !== 'true') {
    await toggle.click()
    await sleep(200)
  }

  const drawer = page.locator('[data-testid="data-drawer-body"]')
  const section = drawer.locator('section.panel', { hasText: heading })
  if ((await section.count()) === 0) {
    return ''
  }

  return compact(await section.first().innerText())
}

/**
 * 玩家端「投赞成」：等按钮真的可点再点。
 *
 * 判据全在界面上：按钮出现 = 服务端下发的 `canNominate === false`（这只在「有一项提名在投票中」
 * 成立，见 DayProjection.canNominate）；按钮可用 = `canVote && !voted`。不成立就抛错并附上
 * 玩家页的真实状态，而不是干等 30 秒后只报一句超时。
 */
async function castYesVote(page, seat) {
  try {
    await page.getByTestId('player-vote-yes').click({ timeout: 30_000 })
  } catch (error) {
    const body = compact(await page.locator('body').innerText()).slice(0, 400)
    console.error(`[诊断] ${seat} 号玩家页全文：${body}`)
    throw new Error(
      `${seat} 号的「投赞成」不可点：${await describePlayerDay(page)}`
        + `（原始错误：${error instanceof Error ? error.message.split('\n')[0] : String(error)}）`,
    )
  }
}

/** 玩家页白天区的真实状态（失败诊断用）：属性 + 关键文本，全部从 DOM 读。 */
async function describePlayerDay(page) {
  const day = page.getByTestId('player-day')
  if ((await day.count()) === 0) {
    return JSON.stringify({ 存在: false, 页面: compact(await page.locator('body').innerText()).slice(0, 200) })
  }

  return JSON.stringify({
    存在: true,
    status: await day.getAttribute('data-day-status'),
    dayNumber: await day.getAttribute('data-day-number'),
    提名入口: await page.getByTestId('player-nominee-select').count(),
    投票按钮: await page.getByTestId('player-vote-yes').count(),
    投票按钮可用: await page.getByTestId('player-vote-yes').isEnabled().catch(() => null),
    投票态: await readTextOrNull(page, 'player-vote-state'),
    等待态: await readTextOrNull(page, 'player-day-waiting'),
    已结束: await readTextOrNull(page, 'player-day-closed'),
    提名条数: await page.getByTestId('player-day-nominations').getAttribute('data-nomination-count').catch(() => null),
  })
}

async function readTextOrNull(page, testId) {
  const locator = page.getByTestId(testId)
  if ((await locator.count()) === 0) {
    return null
  }

  return compact(await locator.first().innerText())
}

/**
 * 夜晚是否已经收口：看**界面自己**的判据——「开白天」按钮的使能位就是服务端的
 * `planCompleted && phase ∈ {FirstNight, OtherNight}`（见 DayControl.vue 的 canStartDay）。
 *
 * 刻意不读数据抽屉里的「当前步骤」摘要当轮询条件：抽屉是展开态渲染，轮询它读到一次假值，
 * 就会把"计划还在走"当成"没走完"，接着拿强推去推一个已经收口的计划——服务端会以
 * `本计划已走完` 拒绝（StepMachine.PlanAlreadyCompleted）。界面的使能位是更硬的判据。
 */
async function nightSettled(page) {
  return page
    .getByTestId('st-start-day')
    .isEnabled()
    .catch(() => false)
}

/** 当前槽位是否有挂起请求：有就不能强推（强推会把它按 Override 了结）。 */
async function pendingRequestVisible(page) {
  const pending = page.locator('[data-testid="console-pending"]')
  return (await pending.count()) > 0 && (await pending.first().isVisible().catch(() => false))
}

/** 说书人兜底：强推当前槽位（D-0014）；每次带原因（会随事件流记录）。 */
async function forceAdvanceSlot(page, label) {
  const box = page.locator('section', { hasText: '兜底与推进' })
  await box.locator('input[placeholder^="原因"]').fill(`批次取证：${label}`)
  return runCommand(page, label, () => box.getByRole('button', { name: '强推当前槽位' }).click())
}

/**
 * 收尾一个夜晚：先观察一个足够长的窗口（= 整夜槽位数 × 配额 + 余量，保留"槽位确实会按配额自行前进"
 * 的真机观察，完整节奏语义在集成测试），没收口再用强推把剩余空槽位推完——不白等 N × 配额。
 * 看到**任何**挂起请求就立刻停手并如实报出来（绝不越权了结随后的请求 / 裁定点）。
 *
 * `natural` 在「窗口内没有任何一次强推」时为真：迭代档 0.3s/槽 下整夜 ≤ 8s；
 * 取证档 2s/槽 下窗口按配额缩放（首夜 13 格 ≈ 26s）——固定 12s 会在取证档误把
 * "还没走完"判成"走不完"（2026-10-03 取证档实测踩到，故按配额算窗）。
 */
async function finishNightQuickly(page, label) {
  const naturalWindowMs = Math.max(8_000, Math.round(config.quotaSeconds * 16_000) + 6_000)
  const deadline = Date.now() + naturalWindowMs
  while (Date.now() < deadline) {
    if (await nightSettled(page)) {
      return { natural: true, forced: 0, completed: true }
    }

    await sleep(150)
  }

  let forced = 0
  while (forced < 40) {
    if (await nightSettled(page)) {
      return { natural: false, forced, completed: true }
    }

    if (await pendingRequestVisible(page)) {
      return { natural: false, forced, completed: false, note: '仍有挂起请求（不越权强推）' }
    }

    const outcome = await forceAdvanceSlot(page, `${label} 收尾第 ${forced + 1} 步`)
    if (outcome.kind !== 'Accepted') {
      return { natural: false, forced, completed: false, note: `强推回执 ${outcome.kind}：${outcome.raw}` }
    }

    forced += 1
  }

  return { natural: false, forced, completed: await nightSettled(page), note: '达到强推步数上限' }
}

async function readPlayerInformationCount(page) {
  const panel = page.locator('[data-testid="player-information"]')
  if ((await panel.count()) === 0) {
    return null
  }

  return panel.getAttribute('data-information-count')
}

/** 玩家页信息面板的每一行（真 DOM：ability slug + 说书人给的内容）。 */
async function readPlayerInformationRows(page) {
  const panel = page.locator('[data-testid="player-information"]')
  if ((await panel.count()) === 0) {
    return []
  }

  return panel.locator('li').evaluateAll((items) =>
    items.map((item) => ({
      index: item.getAttribute('data-information-index'),
      ability: (item.querySelector('.mono')?.textContent ?? '').trim(),
      content: (item.querySelectorAll('span')[1]?.textContent ?? '').trim(),
    })),
  )
}

async function readPlayerInformationText(page) {
  const panel = page.locator('[data-testid="player-information"]')
  if ((await panel.count()) === 0) {
    return ''
  }

  return compact(await panel.innerText())
}

/** 等玩家端请求区回到空态（data-request-state=idle：提交被受理后的界面事实）。 */
async function waitForRequestPanelIdle(page, timeoutMs) {
  const panel = page.getByTestId('player-request-panel')
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if ((await panel.getAttribute('data-request-state')) === 'idle') {
      return true
    }

    await sleep(150)
  }

  return (await panel.getAttribute('data-request-state')) === 'idle'
}

/**
 * 点「提交」并等服务器对**这一次调用**的回执（SignalR 调用帧 → `{"type":3,"invocationId":…}`）。
 * 判据取回执本身而不是页面文案：提交被受理时页面不显示任何提示（PlayerPanel.submit 成功分支只清空请求），
 * 干等文案会误判成失败。
 */
async function submitRequestAndAwaitOutcome(page, sink) {
  const before = sink.invocations.length
  await page.getByTestId('player-submit').click()
  const deadline = Date.now() + 30_000
  while (Date.now() < deadline) {
    const invocation = sink.invocations
      .slice(before)
      .flatMap((frame) => frame.messages)
      .find((message) => message.target === 'SubmitResponse')
    if (invocation !== undefined) {
      const invocationId = String(invocation.invocationId ?? '')
      // 回执可能与推送同帧到达：必须在**全部消息**里找，不能只看每条帧的第一条。
      const completion = sink.frames
        .flatMap((frame) => frame.messages)
        .find((message) => message.type === 3 && String(message.invocationId ?? '') === invocationId)
      if (completion !== undefined) {
        return completion.result?.kind === 'Accepted'
      }
    }

    await sleep(100)
  }

  return false
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
  console.log('\n=== 回溯型信息族批次（retro-info）取证结论 ===')
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
  const parsed = { port: 5416, vitePort: 5296 }
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
