/**
 * 初始身份随机器（setup-randomizer）批次装置 —— 票据 docs/backlog/done/setup-randomizer.md 的验收矩阵。
 *
 * 它回答：**「一键配板 → 重摇 / 手改 → 提交 → 开夜 → 首夜按配板唤醒」这条链路，在真的界面上一路跑得通吗？**
 * 场景（固定 5 席）：
 *   1) 说书人点「一键配板（随机）」：建议覆盖每一席、角色唯一、净分布 = 官方 5 人基线 + 在场恶魔的设置修正
 *      （R-0041 / R-0042），并给出显式种子；
 *   2) 再点「重摇」：新种子（重摇 = 新随机输入；建议不落账，提交仍走既有分配命令面）；
 *   3) 手改到 5 个有夜间契约的角色（clockmaker / dreamer / no-dashii / mutant / klutz）并提交：
 *      随机建议可能抽到未实现夜间契约的角色，直接开夜会被服务端显式拒绝（plan.contract_missing）——
 *      手改正是说书人裁量的链路（D-0002 / D-0017），本装置要证的就是它；
 *   4) 开夜：首夜按配板唤醒——1 号钟表匠进说书人裁定点（牌面高亮当前槽位、五席均无请求），
 *      2 号筑梦师收到定向请求（选项 = 除自己外的其他席位）；无关席位在窗口内持续零请求（8 次采样）；
 *   5) 两条信息只到本人：钟表匠信息只出现在 1 号、筑梦师信息只出现在 2 号（DOM + 帧两层）；
 *      五席连接的全量帧扫描：配板建议（种子 / 净分布 / 分配表）零下发、其他席位角色零泄露、
 *      操作请求只到 2 号；说书人连接做阳性对照（两个种子与全部 5 个角色确实在帧里出现过，
 *      证明"零命中"不是扫描没接通）。
 *
 * 与主批次的分工：主批次（verify-storyteller-panel.mjs）跑五席固定花名册的通用玩法回归，本装置只跑配板这一条链路。
 *
 * 前置：Node >= 22.5（node:sqlite）、本机已构建 web/node_modules（playwright）。
 * 用法（在仓库根运行；默认迭代档 = 快节拍 + 不落盘截图 + 复用产物）：
 *   node tools/verify-setup-randomizer.mjs                                        # 迭代档
 *   node tools/verify-setup-randomizer.mjs --quota 2 --screenshots-all --build    # 取证档（一批一次）
 *   node tools/verify-setup-randomizer.mjs --port 5412 --vite-port 5292           # 自定端口
 *
 * 外部耦合（换机器先核对 web/AGENTS.md §3.1）：宿主编译产物路径、SQLite 表 Games 的
 * StorytellerTicket / SeatsJson 列形状（SeatId 序列化为 { "value": N }）、SignalR 默认 JSON
 * 协议的帧形状（`{"type":1,"target":…}`，帧尾带 `\x1e` 分隔符、一帧可合多条消息）、
 * 说书人分配面板的 DOM（`st-assignment-randomize` / `st-assignment-distribution`）与
 * 说书人裁定控制台的 DOM（`console-decision`）。
 * 退出码：0 = 全过；1 = 有失败；2 = 环境缺依赖（Playwright / Chromium）。
 */
import { spawn } from 'node:child_process'
import { mkdirSync, mkdtempSync, rmSync } from 'node:fs'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { DatabaseSync } from 'node:sqlite'
import { readTextBounded } from './lib/bounded-text.mjs'
import { describeProfile, ensureServerArtifacts, extractProfileFlags, resolveProfile } from './lib/verify-profile.mjs'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const webRoot = path.join(repositoryRoot, 'web')
const { flags, rest } = extractProfileFlags(process.argv.slice(2))
const options = parseArguments(rest)

/** 档位（tools/lib/verify-profile.mjs）：默认迭代档；取证档显式 `--quota 2 --screenshots-all`。 */
const config = resolveProfile(flags, { quotaSeconds: 0.3 })

// 分段开关对本装置无意义（一条链路顺序执行）：显式查询时如实说明并退出，不假装有段可跑。
if (config.listSections) {
  console.log('本装置没有分段开关：整条链路顺序执行（前置准备只做一次）。')
  console.log('  一键配板 → 重摇 → 手改 → 提交 → 开夜 → 首夜唤醒 → DOM + 帧双层隔离扫描')
  console.log('整轮跑：node tools/verify-setup-randomizer.mjs --quota 2 --screenshots-all --build')
  process.exit(0)
}

const results = []
const children = []
let playwright = null
let browser = null

try {
  const requireFromWeb = createRequire(path.join(webRoot, 'package.json'))
  playwright = requireFromWeb('playwright')
} catch (error) {
  console.error(`缺少依赖（playwright）：${String(error)}`)
  console.error('先运行：cd web; npm install; npx playwright install chromium')
  process.exit(2)
}

console.log(`档位：${describeProfile(config)}`)

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-setup-randomizer-'))
const databasePath = path.join(workspace, 'setup-randomizer.db')
const screenshotsDir = path.resolve(repositoryRoot, 'artifacts', 'web')
mkdirSync(screenshotsDir, { recursive: true })

const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`

/**
 * 提交的配板（手改后的显式分配）：5 个已实现夜间契约的角色，与主装置默认夹具同源——
 * 首夜会被真正唤醒的链路是可验证的（钟表匠裁定点 + 筑梦师请求）。
 */
const ASSIGN = ['clockmaker', 'dreamer', 'no-dashii', 'mutant', 'klutz']
/** 席位号从花名册顺序派生：调换 ASSIGN 顺序时断言跟着走，不靠人工同步。 */
const seatOf = (slug) => ASSIGN.indexOf(slug) + 1
const CLOCKMAKER_SEAT = seatOf('clockmaker')
const DREAMER_SEAT = seatOf('dreamer')
/** 无关席位（首夜不该被唤醒、不该收到任何信息）：恶魔 + 两名无夜间行动的外来者。 */
const UNRELATED_SEATS = ASSIGN.filter((slug) => slug !== 'clockmaker' && slug !== 'dreamer').map((slug) => seatOf(slug))

/** 两条信息用唯一文本标记：验证"信息只到本人"的阳性样本。 */
const CLOCKMAKER_INFO = '批次取证-配板链路：钟表匠信息（本夜最小距离 2，说书人自由裁定）'
const DREAMER_INFO = '批次取证-配板链路：筑梦师信息（说书人自由裁定、可能错误）'

/** 官方 5 人基线（R-0041：Derived，由《洗衣妇》例反推）。 */
const FIVE_PLAYER_BASE = { Townsfolk: 3, Outsider: 0, Minion: 1, Demon: 1 }
/** S&V 四名恶魔；带 `[...]` 设置修正的两名按 R-0042 参与净分布。 */
const DEMON_SLUGS = ['fang-gu', 'vigormortis', 'no-dashii', 'vortox']
const MODIFIED_DEMONS = ['fang-gu', 'vigormortis']
const DEMON_NAMES = { 'fang-gu': '方古', vigormortis: '亡骨魔', 'no-dashii': '诺-达鲺', vortox: '涡流' }
const DEMON_ADJUSTMENTS = {
  'fang-gu': { type: 'Outsider', delta: 1 },
  vigormortis: { type: 'Outsider', delta: -1 },
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
  console.log('=== 1/8 构建并启动真宿主（独立临时库，5 席）===')
  const host = await ensureServerArtifacts({ repositoryRoot, buildMode: config.buildMode })
  console.log(`宿主编译产物：${host.artifact}（${host.built ? '本次重建' : '复用已有'}）`)
  await startServer()

  console.log('=== 2/8 取票据并起 Vite ===')
  const ticket = readStorytellerTicket(databasePath)
  const seatTickets = readSeatTickets(databasePath)
  check('席位票据齐备（5 席）', seatTickets.length === ASSIGN.length, `数据库 ${seatTickets.length} 张`)

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

  console.log('=== 3/8 说书人 + 五席玩家加入真浏览器（每席一条连接 + 帧收集器）===')
  browser = await playwright.chromium.launch()
  const consoleErrors = []

  // 说书人连接也挂帧收集器：它只用于**阳性对照**（证明配板建议的种子与五个角色确实在连接上出现过）。
  const storytellerSink = createFrameSink()
  const storytellerPage = await newPage(browser, { width: 1600, height: 1100 }, consoleErrors, storytellerSink)
  await storytellerPage.goto(viteUrl)
  await storytellerPage.getByPlaceholder('说书人票据').fill(ticket)
  await storytellerPage.getByRole('button', { name: '加入' }).click()
  await storytellerPage.locator('[data-testid="grimoire"]').waitFor({ timeout: 30_000 })
  check('说书人加入后看板可见（魔典主视图）', (await storytellerPage.locator('[data-testid="grimoire"]').count()) === 1)

  const playerPages = new Map()
  const frameSinks = new Map()
  for (const seatTicket of seatTickets) {
    const sink = createFrameSink()
    const page = await newPage(browser, { width: 900, height: 1000 }, consoleErrors, sink)
    await page.goto(`${viteUrl}/#player`)
    await page.getByPlaceholder('席位票据').fill(seatTicket.ticket)
    await page.getByRole('button', { name: '加入' }).click()
    const badge = await waitForText(page.locator('[data-testid="player-seat"]'), `${seatTicket.seat} 号`, 30_000)
    check(`玩家 ${seatTicket.seat} 号加入成功`, badge.includes(`${seatTicket.seat} 号`), compact(badge))
    playerPages.set(seatTicket.seat, page)
    frameSinks.set(seatTicket.seat, sink)
  }
  check('五席玩家各一条真连接（独立浏览器上下文）', playerPages.size === 5 && frameSinks.size === 5)

  console.log('=== 4/8 一键配板 → 重摇 → 手改 → 提交（矩阵行 5 / 6）===')
  const assignmentSelects = storytellerPage.locator('section', { hasText: '开局分配' }).locator('select')
  const randomizeButton = storytellerPage.getByTestId('st-assignment-randomize')
  const summaryBox = storytellerPage.getByTestId('st-assignment-distribution')
  const failureBox = storytellerPage.getByTestId('st-assignment-failure')
  const notesBox = storytellerPage.getByTestId('st-assignment-proposal-notes')

  const seatCount = await assignmentSelects.count()
  check('分配表覆盖服务端全部 5 席', seatCount === ASSIGN.length, `UI 席位数=${seatCount}`)

  await randomizeButton.click()
  const firstSummary = await waitForLocatorContains(summaryBox, '净分布', 30_000)
  const firstSeed = parseSeed(firstSummary)
  const firstCounts = parseDistribution(firstSummary)
  const firstValues = await readSelectValues(assignmentSelects)
  const firstDemons = firstValues.filter((slug) => DEMON_SLUGS.includes(slug))
  const firstDemon = firstDemons[0] ?? ''
  console.log(`  第一次建议：种子 ${firstSeed ?? '（不可读）'}；净分布 ${describeCounts(firstCounts)}；恶魔 ${firstDemon || '（未识别）'}`)

  check('一键配板给出建议（净分布 + 显式种子）', firstSeed !== null && firstCounts !== null, compact(firstSummary))
  check(
    '建议覆盖每一席（无「未选择」）',
    firstValues.length === ASSIGN.length && firstValues.every((value) => value.length > 0),
    firstValues.join(', '),
  )
  check(
    '逐席角色唯一（同批不重复）',
    firstValues.length === new Set(firstValues).size,
    `去重后 ${new Set(firstValues).size} / ${firstValues.length}`,
  )
  check('建议含且仅含一名恶魔（R-0041 结构不变量）', firstDemons.length === 1, firstValues.join(', '))
  const expectedFirst = expectedCountsFor(firstDemon)
  check(
    '净分布 = 官方 5 人基线 + 在场恶魔的设置修正（R-0041 / R-0042）',
    countsEqual(firstCounts, expectedFirst),
    `${describeCounts(firstCounts)} ≠ ${describeCounts(expectedFirst)}（恶魔 ${firstDemon}）`,
  )
  check(
    '净分布四项之和 = 席位数',
    firstCounts !== null && firstCounts.Townsfolk + firstCounts.Outsider + firstCounts.Minion + firstCounts.Demon === ASSIGN.length,
    describeCounts(firstCounts),
  )
  check('配板失败提示不出现（本轮建议必须成功）', (await failureBox.count()) === 0)
  if (MODIFIED_DEMONS.includes(firstDemon)) {
    const noteText = compact(await notesBox.innerText().catch(() => ''))
    check(
      `设置调整显式披露（${DEMON_NAMES[firstDemon]} 带入修正）`,
      noteText.includes('设置调整') && noteText.includes(DEMON_NAMES[firstDemon]),
      noteText,
    )
  } else {
    check(
      `无修正恶魔（${firstDemon}）时建议不附修正说明`,
      (await notesBox.count()) === 0,
      `notes 元素数=${await notesBox.count()}`,
    )
  }
  check('按钮文案变为「重摇」', compact(await randomizeButton.innerText()).includes('重摇'), compact(await randomizeButton.innerText()))
  await screenshot(storytellerPage, 'setup-01-proposal')

  // 重摇 = 再要一次建议（新种子）；建议不落账，随机只作显式输入。
  await randomizeButton.click()
  const rerolled = await waitForSeedChange(summaryBox, firstSeed, 30_000)
  const secondCounts = parseDistribution(rerolled.text)
  const secondValues = await readSelectValues(assignmentSelects)
  const secondDemon = secondValues.find((slug) => DEMON_SLUGS.includes(slug)) ?? ''
  console.log(`  重摇后：种子 ${rerolled.seed ?? '（不可读）'}；净分布 ${describeCounts(secondCounts)}；恶魔 ${secondDemon || '（未识别）'}`)
  check('重摇 = 新随机输入（种子改变）', rerolled.seed !== null && rerolled.seed !== firstSeed, `种子 ${firstSeed} → ${rerolled.seed}`)
  check(
    '重摇后仍覆盖每一席且角色唯一',
    secondValues.length === ASSIGN.length && secondValues.every((value) => value.length > 0) && new Set(secondValues).size === ASSIGN.length,
    secondValues.join(', '),
  )
  check(
    '重摇后的净分布同样随在场恶魔修正',
    countsEqual(secondCounts, expectedCountsFor(secondDemon)),
    `${describeCounts(secondCounts)}（恶魔 ${secondDemon}）`,
  )
  await screenshot(storytellerPage, 'setup-02-reroll')

  // 手改：把随机建议换成说书人显式指定的 5 个角色（表里逐席选择）。
  for (const [index, slug] of ASSIGN.entries()) {
    await assignmentSelects.nth(index).selectOption(slug)
  }
  const editedValues = await readSelectValues(assignmentSelects)
  check(
    '手改后选择表 = 说书人显式指定的 5 个角色（覆盖随机建议）',
    arraysEqual(editedValues, ASSIGN),
    editedValues.join(', '),
  )
  check('手改后前端设置修正列表撤下（五行均无 [...] 修正）', (await storytellerPage.getByTestId('st-assignment-setup-notes').count()) === 0)
  await screenshot(storytellerPage, 'setup-03-handedited')

  const assigned = await runCommand(storytellerPage, '提交分配', () =>
    storytellerPage.getByRole('button', { name: '提交分配' }).click(),
  )
  check('提交走既有分配命令面被受理（D-0017）', assigned.kind === 'Accepted', assigned.raw)

  const submittedBoard = []
  for (const [index, slug] of ASSIGN.entries()) {
    const card = cardOf(storytellerPage, index + 1)
    const value = await waitForAttribute(card, 'data-character', slug, 15_000)
    submittedBoard.push(`${index + 1}:${value}`)
  }
  check(
    '魔典逐席显示提交后的角色（逐席对账）',
    arraysEqual(submittedBoard, ASSIGN.map((slug, index) => `${index + 1}:${slug}`)),
    submittedBoard.join(' '),
  )
  await screenshot(storytellerPage, 'setup-04-assigned')

  console.log('=== 5/8 开夜 → 首夜按配板唤醒（矩阵行 8）===')
  const nightStarted = await runCommand(storytellerPage, '开夜', () =>
    storytellerPage.getByRole('button', { name: /开夜/ }).click(),
  )
  check('开夜被受理（真实顺序表建表）', nightStarted.kind === 'Accepted', nightStarted.raw)

  const phasePushes = await Promise.all(
    [...playerPages.entries()].map(async ([seat, page]) => {
      const text = await waitForText(page.locator('[data-testid="player-phase"]'), '首夜', 20_000)
      return `${seat}:${compact(text)}`
    }),
  )
  check(
    '开夜推送让五席玩家页头变「首夜」（未刷新、未点补齐）',
    phasePushes.every((entry) => entry.endsWith(':首夜')),
    phasePushes.join(' '),
  )

  const slotCounter = await waitForSlotCounter(storytellerPage, 20_000)
  check(
    '首夜真实建表：槽位计数可读且为 13 槽（与主装置同夹具）',
    slotCounter !== null && slotCounter.total === 13,
    slotCounter === null ? '槽位计数不可读' : `${slotCounter.index + 1} / ${slotCounter.total}`,
  )

  const clockmakerDecision = await waitForDecision(storytellerPage, (text) => text.includes('钟表匠'), 180_000)
  check('首夜第一个裁定点 = 钟表匠（按配板落在 1 号）', clockmakerDecision.includes('钟表匠'), compact(clockmakerDecision))
  check(
    '钟表匠裁定点没有候选选项（自由决定）',
    await storytellerPage.getByText('引擎没有给出候选选项').isVisible().catch(() => false),
  )
  const clockmakerCard = cardOf(storytellerPage, CLOCKMAKER_SEAT)
  const clockmakerCurrent = await clockmakerCard.getAttribute('data-current-slot')
  check('1 号牌面高亮为当前槽位（对应提交的 1=钟表匠）', clockmakerCurrent === 'true', `data-current-slot=${clockmakerCurrent}`)

  const statesAtClockmaker = await Promise.all(
    [...playerPages.entries()].map(async ([seat, page]) => {
      const state = await page.locator('[data-testid="player-request-panel"]').getAttribute('data-request-state')
      return `${seat}:${state}`
    }),
  )
  check(
    '钟表匠槽位期间五席玩家均无请求（顺序未越位）',
    statesAtClockmaker.every((entry) => entry.endsWith(':idle')),
    statesAtClockmaker.join(' '),
  )
  await screenshot(storytellerPage, 'setup-05-clockmaker-decision')

  const clockmakerSettled = await settleFreeDecision(storytellerPage, CLOCKMAKER_INFO)
  check('钟表匠信息裁定被受理', clockmakerSettled.kind === 'Accepted', clockmakerSettled.raw)

  const clockmakerInfoText = await waitForLocatorContains(
    playerPages.get(CLOCKMAKER_SEAT).locator('[data-testid="player-information"]'),
    CLOCKMAKER_INFO,
    30_000,
  )
  const clockmakerInfoCount = await informationCount(playerPages.get(CLOCKMAKER_SEAT))
  check(
    '1 号玩家页出现钟表匠信息（DOM 单播）',
    clockmakerInfoText.includes(CLOCKMAKER_INFO) && clockmakerInfoCount === 1,
    `count=${clockmakerInfoCount}`,
  )
  await screenshot(playerPages.get(CLOCKMAKER_SEAT), 'setup-06-clockmaker-info')

  console.log('=== 6/8 筑梦师槽位：定向请求只到 2 号，无关席位零活动 ===')
  const dreamerPage = playerPages.get(DREAMER_SEAT)
  const dreamerPanel = dreamerPage.locator('[data-testid="player-request-panel"]')
  const requestState = await waitForAttribute(dreamerPanel, 'data-request-state', 'pending', 180_000)
  check('2 号玩家收到筑梦师定向请求（服务端推送）', requestState === 'pending', `data-request-state=${requestState}`)

  const requestContext = compact(await dreamerPage.locator('[data-testid="player-request-context"]').innerText())
  check('请求上下文说明是筑梦师的选择', requestContext.includes('筑梦师'), requestContext)

  const optionValues = await dreamerPage
    .locator('[data-testid="player-request-options"] label')
    .evaluateAll((labels) => labels.map((label) => label.getAttribute('data-option-value') ?? ''))
  const expectedOptions = [...playerPages.keys()].filter((seat) => seat !== DREAMER_SEAT).map((seat) => `seat:${seat}`)
  check(
    '合法选项 = 除自己外的其他席位',
    optionValues.length === expectedOptions.length
      && expectedOptions.every((value) => optionValues.includes(value))
      && !optionValues.includes(`seat:${DREAMER_SEAT}`),
    optionValues.join(','),
  )

  const dreamerCard = cardOf(storytellerPage, DREAMER_SEAT)
  const dreamerCurrent = await dreamerCard.getAttribute('data-current-slot')
  check('2 号牌面高亮为当前槽位（对应提交的 2=筑梦师）', dreamerCurrent === 'true', `data-current-slot=${dreamerCurrent}`)

  // 反方向证据要做成"有宽度的观测"：请求窗口内连续采样，单点读取证明不了"整个窗口没有活动"。
  const observedStates = new Map([CLOCKMAKER_SEAT, ...UNRELATED_SEATS].map((seat) => [seat, new Set()]))
  for (let sample = 0; sample < 8; sample += 1) {
    for (const seat of observedStates.keys()) {
      const state = await playerPages
        .get(seat)
        .locator('[data-testid="player-request-panel"]')
        .getAttribute('data-request-state')
      observedStates.get(seat).add(state ?? '（无）')
    }

    await sleep(250)
  }

  for (const [seat, states] of observedStates) {
    check(
      `非本槽席位 ${seat} 号在请求窗口内持续零请求（8 次采样）`,
      states.size === 1 && states.has('idle'),
      `观察到：${[...states].join(',') || '（无）'}`,
    )
  }
  await screenshot(dreamerPage, 'setup-07-dreamer-request')

  // 作答 → 说书人裁定（能力中毒未生效）→ 信息只到本人。
  await dreamerPage.locator('[data-testid="player-request-options"] label[data-option-value="seat:3"] input[type=radio]').check()
  await dreamerPage.locator('[data-testid="player-submit"]').click()
  const backToIdle = await waitForAttribute(dreamerPanel, 'data-request-state', 'idle', 30_000)
  check('2 号玩家提交选择、请求区回到空态', backToIdle === 'idle', `data-request-state=${backToIdle}`)

  const dreamerDecision = await waitForDecision(
    storytellerPage,
    (text) => text.includes('筑梦师') && text.includes('未生效'),
    60_000,
  )
  check(
    '筑梦师裁定点按入槽时刻推演（中毒 → 未生效，由说书人裁定）',
    dreamerDecision.includes('筑梦师') && dreamerDecision.includes('未生效'),
    compact(dreamerDecision),
  )

  const dreamerSettled = await settleFreeDecision(storytellerPage, DREAMER_INFO)
  check('筑梦师信息裁定被受理', dreamerSettled.kind === 'Accepted', dreamerSettled.raw)

  const dreamerInfoText = await waitForLocatorContains(
    dreamerPage.locator('[data-testid="player-information"]'),
    DREAMER_INFO,
    30_000,
  )
  const dreamerInfoCount = await informationCount(dreamerPage)
  check(
    '2 号玩家页出现筑梦师信息（DOM 单播）',
    dreamerInfoText.includes(DREAMER_INFO) && dreamerInfoCount === 1,
    `count=${dreamerInfoCount}`,
  )
  await screenshot(dreamerPage, 'setup-08-dreamer-info')

  console.log('=== 7/8 信息只到本人：DOM + 帧级隔离扫描 ===')
  const clockmakerInfoPanelText = compact(await playerPages.get(CLOCKMAKER_SEAT).locator('[data-testid="player-information"]').innerText())
  const dreamerInfoPanelText = compact(await dreamerPage.locator('[data-testid="player-information"]').innerText())
  check(
    '1 号信息面板只有钟表匠那条（不含筑梦师信息）',
    clockmakerInfoPanelText.includes(CLOCKMAKER_INFO) && !clockmakerInfoPanelText.includes(DREAMER_INFO),
    clockmakerInfoPanelText,
  )
  check(
    '2 号信息面板只有筑梦师那条（不含钟表匠信息）',
    dreamerInfoPanelText.includes(DREAMER_INFO) && !dreamerInfoPanelText.includes(CLOCKMAKER_INFO),
    dreamerInfoPanelText,
  )
  for (const seat of UNRELATED_SEATS) {
    const page = playerPages.get(seat)
    const count = await informationCount(page)
    const text = compact(await infoText(page))
    check(
      `无关席位 ${seat} 号信息面板零下发（count=0 + 空态文案）`,
      count === 0 && text.includes('还没有收到信息') && !text.includes(CLOCKMAKER_INFO) && !text.includes(DREAMER_INFO),
      `count=${count}；文本=${text}`,
    )
  }
  await screenshot(playerPages.get(UNRELATED_SEATS[1]), 'setup-09-unrelated-clean')

  // —— 连接层（真 SignalR 帧）的全量扫描：每席只看自己那条连接收到的推送 ——
  const receivedBySeat = new Map()
  for (const [seat, sink] of frameSinks.entries()) {
    receivedBySeat.set(seat, sink.frames.filter((frame) => frame.parsed !== null && frame.direction === 'received'))
  }

  const deliveries = []
  const requestSeats = []
  for (const [seat, frames] of receivedBySeat.entries()) {
    for (const frame of frames) {
      for (const message of frame.messages) {
        if (message.target === 'ReceiveInformationResult') {
          deliveries.push({
            seat,
            ability: String(message.arguments?.[0]?.ability ?? ''),
            content: String(message.arguments?.[0]?.content ?? ''),
          })
        }

        if (message.target === 'ReceiveOperationRequest') {
          requestSeats.push(seat)
        }
      }
    }
  }

  const deliveryKeys = deliveries.map((entry) => `${entry.seat}:${entry.ability}`).sort()
  check(
    '信息结果只推给本人（帧）：clockmaker → 1 号、dreamer → 2 号',
    arraysEqual(deliveryKeys, [`${CLOCKMAKER_SEAT}:clockmaker`, `${DREAMER_SEAT}:dreamer`]),
    deliveryKeys.join(', ') || '（没有信息帧）',
  )
  check(
    '信息内容 = 说书人裁定原文（帧）',
    deliveries.some((entry) => entry.seat === CLOCKMAKER_SEAT && entry.content.includes(CLOCKMAKER_INFO))
      && deliveries.some((entry) => entry.seat === DREAMER_SEAT && entry.content.includes(DREAMER_INFO)),
    deliveries.map((entry) => `${entry.seat}:${compact(entry.content).slice(0, 24)}`).join(' | '),
  )
  check(
    '操作请求只推给 2 号（帧）',
    arraysEqual([...new Set(requestSeats)].sort(), [DREAMER_SEAT]),
    `收到请求的席位=${[...new Set(requestSeats)].join(',') || '（无）'}`,
  )

  // 说书人专属的配板内容零下发：建议（种子 / 净分布 / 分配表 / 说明）只在说书人连接上出现。
  const forbiddenTokens = [
    '净分布',
    '钳制',
    '设置调整',
    'ProposeSetup',
    '"seed"',
    '"assignments"',
    '"distribution"',
    '"notes"',
    firstSeed ?? '',
    rerolled.seed ?? '',
  ].filter((token) => token.length > 0)
  const leakHits = []
  let scannedFrames = 0
  let scannedMessages = 0
  for (const [seat, frames] of receivedBySeat.entries()) {
    for (const frame of frames) {
      scannedFrames += 1
      for (const message of frame.messages) {
        scannedMessages += 1
        const payload = JSON.stringify(message)
        for (const token of forbiddenTokens) {
          if (payload.includes(token)) {
            leakHits.push(`${seat} 号的 ${message.target ?? '（未识别帧）'} 含「${token}」`)
          }
        }
      }
    }
  }
  check(
    '五席连接的全部帧里没有配板建议内容（种子 / 净分布 / 分配表 / 说明）',
    scannedMessages > 0 && leakHits.length === 0,
    leakHits.slice(0, 3).join(' | ') || `已扫描 ${receivedBySeat.size} 席 / ${scannedFrames} 帧 / ${scannedMessages} 条消息`,
  )

  // 其他席位的角色 slug 零泄露（本人能力字段除外）：分配只对说书人可见（D-0012）。
  // 判据必须钉在 **JSON 字符串值**（`"klutz"`）上：玩家视图 DTO 自带同名字段 `klutzChoices`，
  // 裸 slug 会把字段名当成泄漏（本装置首跑实测：1–4 号全部假红在 klutz 上）。
  const slugLeaks = []
  for (const [seat, frames] of receivedBySeat.entries()) {
    const payload = frames
      .flatMap((frame) => frame.messages.map((message) => JSON.stringify(message)))
      .join('\n')
    const ownSlug = ASSIGN[seat - 1]
    for (const slug of ASSIGN) {
      if (slug !== ownSlug && payload.includes(`"${slug}"`)) {
        slugLeaks.push(`${seat} 号帧含他人角色 ${slug}`)
      }
    }
  }
  check(
    '玩家帧不含其他席位的角色 slug（信息隔离方向）',
    slugLeaks.length === 0,
    slugLeaks.slice(0, 3).join(' | ') || `已扫描 ${receivedBySeat.size} 席`,
  )

  // 阳性对照：同一个种子与五个角色确实在**说书人连接**的帧里出现过——否则上面的"零命中"可能只是扫描没接通。
  // 角色同样按 JSON 字符串值对照，避免字段名（klutzChoices）顶替真值。
  const storytellerFrames = storytellerSink.frames.filter((frame) => frame.parsed !== null && frame.direction === 'received')
  const storytellerPayload = storytellerFrames
    .flatMap((frame) => frame.messages.map((message) => JSON.stringify(message)))
    .join('\n')
  const positiveTokens = [firstSeed, rerolled.seed, ...ASSIGN.map((slug) => `"${slug}"`)].filter(
    (token) => typeof token === 'string' && token.length > 0,
  )
  const positiveMissing = positiveTokens.filter((token) => !storytellerPayload.includes(token))
  check(
    '阳性对照：说书人连接确实收到两个种子与全部 5 个角色（证明扫描不是空集）',
    positiveTokens.length === 7 && positiveMissing.length === 0,
    positiveMissing.length === 0 ? `说书人连接 ${storytellerFrames.length} 帧` : `缺：${positiveMissing.join(', ')}`,
  )

  check('浏览器控制台没有报错', consoleErrors.length === 0, consoleErrors.slice(0, 3).join(' | '))
}

// —— 连接层：每席只挂**它自己那条**连接的帧观察器（不另开第二条连接，见 web/AGENTS.md §3.1）——

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
 * 否则每条帧都会抛异常、被吞成"没有推送"——这会让推送扫描静默假绿。
 * 服务端还会把同一连接上先后写出的多条消息（如"视图推送 + 调用回执"）**合进一帧**：
 * 只取第一条会让回执被前面的推送遮住，表现为"提交明明生效了却等不到受理回执"。
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

async function newPage(browserInstance, viewport, consoleErrors, frameSink) {
  const context = await browserInstance.newContext({ viewport })
  const page = await context.newPage()
  page.setDefaultTimeout(30_000)
  if (frameSink !== null && frameSink !== undefined) {
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

// —— 配板建议解析（全部从真界面读，不从日志猜）——

/** `净分布：镇民 3 / 外来者 0 / 爪牙 1 / 恶魔 1（种子 …）` → 四类计数；读不到返回 null。 */
function parseDistribution(text) {
  const match = /镇民\s*(\d+)\s*\/\s*外来者\s*(\d+)\s*\/\s*爪牙\s*(\d+)\s*\/\s*恶魔\s*(\d+)/.exec(String(text ?? ''))
  if (match === null) {
    return null
  }

  return {
    Townsfolk: Number(match[1]),
    Outsider: Number(match[2]),
    Minion: Number(match[3]),
    Demon: Number(match[4]),
  }
}

/** `（种子 <32 位十六进制>）` → 种子；读不到返回 null。 */
function parseSeed(text) {
  return /（种子\s*([0-9a-f]{32})）/.exec(String(text ?? ''))?.[1] ?? null
}

/** 5 人净分布 = 基线 + 在场恶魔的设置修正（同类先加总 → 逐类钳到 0 下限 → 镇民取余量，R-0042）。 */
function expectedCountsFor(demonSlug) {
  const counts = { ...FIVE_PLAYER_BASE }
  const adjustment = DEMON_ADJUSTMENTS[demonSlug]
  if (adjustment !== undefined) {
    counts[adjustment.type] = Math.max(0, counts[adjustment.type] + adjustment.delta)
    // 镇民取余量（R-0042）：修正动的是别的类型，镇民补足到总席位数。
    counts.Townsfolk = ASSIGN.length - counts.Outsider - counts.Minion - counts.Demon
  }

  return counts
}

function countsEqual(left, right) {
  return (
    left !== null
    && right !== null
    && left.Townsfolk === right.Townsfolk
    && left.Outsider === right.Outsider
    && left.Minion === right.Minion
    && left.Demon === right.Demon
  )
}

function describeCounts(counts) {
  if (counts === null) {
    return '（不可读）'
  }

  return `镇民 ${counts.Townsfolk} / 外来者 ${counts.Outsider} / 爪牙 ${counts.Minion} / 恶魔 ${counts.Demon}`
}

async function readSelectValues(selects) {
  const count = await selects.count()
  const values = []
  for (let index = 0; index < count; index += 1) {
    values.push(await selects.nth(index).inputValue())
  }

  return values
}

async function waitForSeedChange(locator, previousSeed, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  let text = ''
  let seed = null
  while (Date.now() < deadline) {
    text = compact(await readTextBounded(locator))
    seed = parseSeed(text)
    if (seed !== null && seed !== previousSeed) {
      return { text, seed }
    }

    await sleep(150)
  }

  return { text, seed }
}

// —— 说书人侧读取与命令 ——

function cardOf(page, seat) {
  return page.locator(`[data-testid="grimoire-seat"][data-seat="${seat}"]`)
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

/** 说书人按自由决定结清当前裁定点。 */
async function settleFreeDecision(page, content) {
  await page.getByPlaceholder('自由决定的内容（可为空）').fill(content)
  return runCommand(page, '裁定', () => page.getByRole('button', { name: '按自由决定结清' }).click())
}

/** "当前步骤"里的槽位计数（X / Y）→ { index（从 0 起）, total }；读不到返回 null。 */
async function readSlotCounter(page) {
  return page.evaluate(() => {
    const cells = [...document.querySelectorAll('header.strip .cell')]
    for (const cell of cells) {
      const caption = cell.querySelector('.caption')?.textContent?.trim()
      if (caption !== '槽位') {
        continue
      }

      const match = /(\d+)\s*\/\s*(\d+)/.exec(cell.textContent ?? '')
      if (match) {
        return { index: Number.parseInt(match[1], 10) - 1, total: Number.parseInt(match[2], 10) }
      }
    }

    return null
  })
}

async function waitForSlotCounter(page, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    const counter = await readSlotCounter(page)
    if (counter !== null) {
      return counter
    }

    await sleep(150)
  }

  return null
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

// —— 玩家侧读取 ——

/** 玩家信息面板的可见文本。 */
async function infoText(page) {
  const info = page.locator('[data-testid="player-information"]')
  if ((await info.count()) === 0) {
    return ''
  }

  return await readTextBounded(info)
}

/** 玩家信息面板的信息计数（data-information-count）；面板不在时返回 -1。 */
async function informationCount(page) {
  const panel = page.locator('[data-testid="player-information"]')
  if ((await panel.count()) === 0) {
    return -1
  }

  const raw = await panel.getAttribute('data-information-count')
  return raw === null ? -1 : Number(raw)
}

// —— 通用等待 ——

async function waitForAttribute(locator, name, expected, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  let value = null
  while (Date.now() < deadline) {
    value = await locator.getAttribute(name).catch(() => null)
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

async function waitForLocatorContains(locator, needle, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  let text = ''
  while (Date.now() < deadline) {
    text = compact(await readTextBounded(locator))
    if (text.includes(needle)) {
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
  console.log('\n=== 初始身份随机器批次（setup-randomizer）取证结论 ===')
  for (const result of results) {
    console.log(`${result.pass ? 'PASS' : 'FAIL'}  ${result.label}${result.detail ? ` → ${result.detail}` : ''}`)
  }

  const failed = results.filter((result) => !result.pass)
  console.log(failed.length === 0 ? `全部通过（${results.length} 项）` : `失败 ${failed.length} / ${results.length}`)
}

function compact(text) {
  return String(text ?? '').replace(/\s+/g, ' ').trim()
}

function arraysEqual(left, right) {
  return left.length === right.length && left.every((value, index) => String(value) === String(right[index]))
}

// —— 宿主与进程 ——

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
  const parsed = { port: 5412, vitePort: 5292 }
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index]
    if (argument === '--port') {
      parsed.port = Number.parseInt(argv[index + 1] ?? '', 10)
      index += 1
    } else if (argument === '--vite-port') {
      parsed.vitePort = Number.parseInt(argv[index + 1] ?? '', 10)
      index += 1
    } else {
      throw new Error(`未知参数：${argument}`)
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
  const closeBrowser = browser !== null ? browser.close().catch(() => {}) : Promise.resolve()
  browser = null
  killChildren()
  await Promise.all([closeBrowser, waitForChildrenExit(10_000)])
  await sleep(300)
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
