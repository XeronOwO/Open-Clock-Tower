/**
 * 限次信息族（女裁缝 / 艺术家）批次装置 —— 票据
 * docs/backlog/done/seamstress-and-artist.md 的界面级验收行，平台口径见 docs/standard/rulings.md R-0040。
 *
 * 它回答：**「每局限一次的信息能力 + 失去能力标记」这条链路在真界面上走得完吗？**
 *
 * 固定 5 席夹具（1 女裁缝 / 2 艺术家 / 3 呆瓜 / 4 畸形秀演员 / 5 方古），一条链路串两个场景：
 *   准备  ：分配 → 开首夜 1（Original；女裁缝在首夜的第 11 格，其余角色无首夜动作）；
 *   夜 1  ：女裁缝的**玩家页**收到「选两名 / 摇头」请求（`pair:` 6 组 + `decline`，不含自己）→
 *           点「摇头」提交 → 无裁定点、无信息、无标记 → 首夜自然收口（摇头不消耗，R-0040）；
 *   白天 1：只有 2 号（艺术家）出现提问入口 → 提一个问题 → 说书人端裁定块读到**问题全文**且归属 = 2 号、
 *           候选 = 是 / 不是 / 我不知道 / 要求重问（不消耗能力）→ 挂起时「结束白天」被拒
 *           （`phase.artist_question_pending`）→ **重连（刷新 = 快照恢复）后等待态仍带问题全文** →
 *           点「要求重问」→ 不记账、不落标记、输入框回来 → 再问一次 → 回答「不是」→
 *           2 号玩家页出现信息（ability=artist，内容「不是」）+ 说书人牌面「失去能力」标记 + 提问入口消失；
 *   夜 2  ：顺序表里方古的格（第 7 位）在女裁缝的格（第 18 位）之前——先应掉恶魔击杀（目标 2 号），
 *           再证女裁缝**再次被唤醒**（摇头不消耗的运行时证据）→ 玩家页选 `pair:3+4` →
 *           说书人裁定「是」（上下文含「按当前账推演：两人属于同一阵营」）→ 信息只到 1 号 + 她的失能标记；
 *   夜 3  ：方古击杀已死亡的 2 号（无事发生）后，计划推进到她的格——**不再唤醒**：
 *           她的格走空槽（无玩家请求）、槽上下文留下可归因跳过「不再被唤醒」，配额照走。
 *
 * 两处必须写明的连接口径（与死亡触发族装置同源）：
 *   1) **服务端每个席位只保留一条连接**（ConnectionRegistry.IssueForSeat）：本装置不给任何席位另开
 *      第二条连接；白天 1 的「重连」用 **page.reload() 重新 Join**——那正是 D-0014 的重连路径，
 *      不是第二条并发连接。另开 SignalR 客户端会把浏览器页的凭据挤掉，断言会假绿。
 *   2) SignalR 默认 JSON 协议的**帧尾带 `\x1e` 记录分隔符**：必须按 `\x1e` 切段再解析，
 *      否则每条帧都会抛异常、被吞成「没有推送」→ 推送扫描静默假绿。
 *
 * 夜 3 的「不再唤醒」证据取两条独立通道（防轮询窗口错过一闪而过的空槽）：
 *   - 页面侧 **MutationObserver** 收集 `grimoire-slot-context` 文本（跳过的原因）；
 *   - 服务端**视图帧**里同样出现过「不再被唤醒」（说书人连接）。
 *   再加上 1 号玩家页请求态观察器（整夜只允许出现 idle）与「计划照配额推进到恶魔格」作旁证。
 *
 * 一处断言口径的说明（说书人字段扫描）：`ReceiveOperationRequestAnswered` 帧里的 `note` 是**玩家自己**
 * 作答时填的备注（PlayerPanel 从不填，一向为空），不算说书人字段，故豁免；「推演」「MayBeFalse」
 * 「失去能力」则对所有收到的帧一律零容忍。
 *
 * 与主批次的分工：主批次（verify-storyteller-panel.mjs）跑通用玩法回归，本装置只跑限次信息族这一条链路
 * （无分段开关：夜 1 / 白天 1 / 夜 2 / 夜 3 是同一条会话的前后置，拆开跑没有意义）。
 *
 * 前置：Node >= 22.5（node:sqlite）、本机已构建 web/node_modules（playwright）。
 * 用法（在仓库根运行；默认迭代档 = 快节奏 + 不落盘截图 + 复用产物）：
 *   node tools/verify-seamstress-artist.mjs                                        # 迭代档
 *   node tools/verify-seamstress-artist.mjs --quota 2 --screenshots-all --build    # 取证档（一批一次）
 *   node tools/verify-seamstress-artist.mjs --port 5421 --vite-port 5301           # 自定端口
 *   node tools/verify-seamstress-artist.mjs --list-sections                        # 打印链路与用途
 *
 * 断言清单（分组；总数以实际输出为准）：
 *   装备：5 席票据齐备 / 说书人加入 / 五席各一条连接加入 / 分配受理 / 开首夜受理；
 *   夜 1 ：1 号页收到请求（上下文含「女裁缝」）/ 候选 = 除自己外 6 组 pair 且含 decline /
 *         摇头提交被受理 + 请求区回空态 / 首夜自然收口（未强推）/ 摇头后零裁定点、零标记、零信息；
 *   白天 1：开白天受理 + 白天进行中 / 入口只对 2 号（其余四席零入口）/
 *         提问后本人页出现「已提问，等待回答」态且带问题全文（三态面板的等待态）/
 *         说书人端读到问题全文 + 归属 = 2 号 + 候选恰好四答 /
 *         无关席位页面零问题文本 / 挂起时结束白天被拒 `phase.artist_question_pending` /
 *         重连（快照）后等待态与问题全文仍在 / 「要求重问」受理 + 裁定块消失 + 输入框回来 +
 *         零标记零信息 / 第二次提问开出新裁定点（问题全文更新）/ 回答「不是」受理 + 裁定块消失 /
 *         2 号玩家页信息（艺术家，内容「不是」）/ 2 号牌面「失去能力」标记（title 记能力已用尽）/
 *         回答后提问入口消失 / 其他席位零失能标记；
 *   夜 2 ：开夜受理 / 恶魔击杀 2 号被受理（顺序表位置在女裁缝之前）+ 2 号席位死亡 /
 *         女裁缝**再次被唤醒**且候选 = 同一 6 组 pair + decline（摇头不消耗）/
 *         选 `pair:3+4` 提交受理 / 裁定归属 = 1 号、上下文含推演行、候选 = 是 / 否 /
 *         裁定「是」受理 / 1 号玩家页信息（女裁缝，3 号与 4 号属于同一阵营）/
 *         1 号牌面「失去能力」标记 / 无关席位零信息 / 第二夜自然收口；
 *   夜 3 ：开夜受理 / 恶魔击杀已死 2 号受理（无事发生）/ 计划推进到她的格；
 *         空槽不再唤醒——槽上下文（DOM 观察器 + 说书人视图帧）含「不再被唤醒」/
 *         1 号玩家页整夜只出现 idle 请求态（零唤醒）/ 信息不增且标记仍在；
 *   隔离：信息只推给本人（女裁缝→1 号 / 艺术家→2 号）/ 问题全文只进本人与说书人连接 /
 *         玩家连接零说书人字段（推演 / MayBeFalse / 失能标记）/ 阳性对照（说书人连接确有「推演」「失去能力」）/
 *         五席页面零说书人文本、零他人问题、零「失去能力」文案 / 浏览器控制台无报错。
 *
 * 外部耦合（换机器先核对 web/AGENTS.md §3.1）：宿主编译产物路径（脚本启动时打印）、
 * SQLite 表 Games 的 StorytellerTicket / SeatsJson 列形状（SeatId 序列化为 { "value": N }）、
 * SignalR 默认 JSON 协议的帧形状（`{"type":1,"target":…}`）、玩家请求面板 DOM
 * （`player-request-panel` / `player-request-context` / `label.option[data-option-value]` / `player-submit`）、
 * 玩家提问面板 DOM（`player-artist-question` 的 `data-question-state`、`player-artist-question-pending`、
 * `player-artist-question-submit`）、说书人裁定控制台 DOM（`console-decision` 的选项按钮文本 = 选项 preview、
 * `console-decision-seat` = 「归属：N 号」）、座位牌标记类名 `mark-exhausted`（文案「失去能力」）、
 * 状态条 `header.strip .cell`（「计划」/「槽位」）、`st-start-day` / `st-close-day` 按钮使能位。
 * 退出码：0 = 全过；1 = 有失败；2 = 环境缺依赖（playwright / node:sqlite）。
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

// 分段开关对本装置无意义：夜 1 → 白天 1 → 夜 2 → 夜 3 是同一条会话的前后置。
if (config.listSections) {
  console.log('本装置没有分段开关：两个场景在一条会话上顺序执行（前置准备只做一次）。')
  console.log('  准备  ：分配 5 席 → 开首夜 1（女裁缝第 11 格）→ 女裁缝玩家页摇头不用')
  console.log('  艺术家：白天 1 提问 → 四答 / 要求重问 → 重连等待态 → 再问 → 回答 + 失去能力标记')
  console.log('  女裁缝：第二夜再次被唤醒 → 选两名 → 裁定 → 信息只到本人 + 失去能力标记')
  console.log('  夜 3  ：空槽不再唤醒（可归因跳过）+ 隔离扫描')
  console.log('整轮跑：node tools/verify-seamstress-artist.mjs --build')
  process.exit(0)
}

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

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-seamstress-artist-'))
const databasePath = path.join(workspace, 'seamstress-artist.db')
const screenshotsDir = path.resolve(repositoryRoot, 'artifacts', 'web')
mkdirSync(screenshotsDir, { recursive: true })

const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`

/**
 * 五席夹具：1 女裁缝 / 2 艺术家 / 3 呆瓜 / 4 畸形秀演员 / 5 方古。
 * 3 / 4 是外来者（不会开夜、不会被常驻中毒影响），方古除首夜外每夜开击杀格（顺序表第 7 位，
 * 在女裁缝第 18 位之前）；除方古外没有其他夜间行动者，链路只被这两处交互驱动。
 */
const ASSIGN = ['seamstress', 'artist', 'klutz', 'mutant', 'fang-gu']
/** 席位号从花名册顺序派生：调换 ASSIGN 顺序时断言跟着走，不靠人工同步。 */
const seatOf = (slug) => ASSIGN.indexOf(slug) + 1
const SEAMSTRESS_SEAT = seatOf('seamstress')
const ARTIST_SEAT = seatOf('artist')
const KLUTZ_SEAT = seatOf('klutz')
const MUTANT_SEAT = seatOf('mutant')
const DEMON_SEAT = seatOf('fang-gu')
const SEATS = [SEAMSTRESS_SEAT, ARTIST_SEAT, KLUTZ_SEAT, MUTANT_SEAT, DEMON_SEAT]
/** 反方向的无关席位（信息零下发 / 零标记）：两个陪跑外来者 + 恶魔本人。 */
const UNRELATED_SEATS = [KLUTZ_SEAT, MUTANT_SEAT, DEMON_SEAT]
/** 女裁缝第二夜选择的玩家对（同阵营：两名外来者，说书人裁定「是」与推演一致）。 */
const PAIR_FIRST = KLUTZ_SEAT
const PAIR_SECOND = MUTANT_SEAT
const PAIR_VALUE = `pair:${PAIR_FIRST}+${PAIR_SECOND}`
const PAIR_PREVIEW = `${PAIR_FIRST} 号与 ${PAIR_SECOND} 号`

/** 艺术家两次提问的全文（一次重问不消耗、一次结清消耗；两问都不该进无关玩家）。 */
const QUESTION_ONE = '1 号是爪牙吗？'
const QUESTION_TWO = '3 号是爪牙吗？'
const ARTIST_OPTION_TEXTS = ['是', '不是', '我不知道', '要求重问（不消耗能力）']

/**
 * 玩家端不该出现的词：说书人裁定提示的推演行 / 失效标记 / 失能标记（D-0012 §4.3）。
 * 推演原文形如「按当前账推演：两人属于同一阵营」——那是**说书人视角**的提示，
 * 一旦出现在玩家连接或玩家页面上，等于把"平台已经替你算好了"泄露给玩家。
 */
const FORBIDDEN_PLAYER_TOKENS = ['推演', 'MayBeFalse', 'malfunction', 'Malfunctions', '失去能力']

/**
 * 帧扫描里的说书人字段。`ReceiveOperationRequestAnswered` 的 `note` 是玩家自己作答时填的备注
 * （界面从不填、一向为空），不算说书人字段，故豁免；其余一律零容忍。
 */
const FORBIDDEN_FRAME_PATTERNS = [
  { label: '推演行', test: (payload) => payload.includes('推演') },
  { label: 'MayBeFalse', test: (payload) => /maybefalse/i.test(payload) },
  { label: '失能标记文案', test: (payload) => payload.includes('失去能力') },
  {
    label: 'note 字段',
    test: (payload) => /"note"\s*:/i.test(payload),
    exemptTargets: ['ReceiveOperationRequestAnswered'],
  },
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

  console.log('=== 3/8 说书人 + 五席玩家页加入真浏览器（一席一条连接）===')
  const browser = await playwright.chromium.launch()
  const consoleErrors = []

  // 说书人连接也挂帧收集器：它用于**阳性对照**（证明「推演」「失去能力」确实会在连接上出现，
  // 玩家侧的零命中不是"扫描根本没接通"式的假绿）。
  const storytellerSink = createFrameSink()
  const storytellerPage = await newPage(browser, { width: 1600, height: 1100 }, consoleErrors, storytellerSink)
  await storytellerPage.goto(viteUrl)
  await storytellerPage.getByPlaceholder('说书人票据').fill(ticket)
  await storytellerPage.getByRole('button', { name: '加入' }).click()
  await storytellerPage.locator('[data-testid="grimoire"]').waitFor({ timeout: 30_000 })
  check('说书人加入后看板可见（魔典主视图）', (await storytellerPage.locator('[data-testid="grimoire"]').count()) === 1)

  const playerPages = new Map()
  const frameSinks = new Map()
  for (const seat of SEATS) {
    const sink = createFrameSink()
    frameSinks.set(seat, sink)
    const page = await newPage(browser, { width: 900, height: 1000 }, consoleErrors, sink)
    await page.goto(`${viteUrl}/#player`)
    await joinSeat(page, seat, seatTickets)
    playerPages.set(seat, page)
  }

  console.log('=== 4/8 分配 → 首夜 1：女裁缝请求（6 组 pair + 摇头）→ 摇头不用 ===')
  const assignmentSelects = storytellerPage.locator('section', { hasText: '开局分配' }).locator('select')
  for (const [index, slug] of ASSIGN.entries()) {
    await assignmentSelects.nth(index).selectOption(slug)
  }

  const assigned = await runCommand(storytellerPage, '分配', () =>
    storytellerPage.getByRole('button', { name: '提交分配' }).click(),
  )
  check('分配 5 个角色被受理', assigned.kind === 'Accepted', assigned.raw)

  const nightOne = await runCommand(storytellerPage, '开首夜', () => startNight(storytellerPage, 1))
  check('开首夜被受理（Original 顺序表建表）', nightOne.kind === 'Accepted', nightOne.raw)

  const seamstressPage = playerPages.get(SEAMSTRESS_SEAT)
  const firstRequest = await waitForRequestOptions(seamstressPage, 120_000)
  check(
    `首夜 ${SEAMSTRESS_SEAT} 号（女裁缝）在自己的页面收到「选两名 / 摇头」请求`,
    firstRequest.context.includes('女裁缝') && firstRequest.options.length > 0,
    `上下文=${firstRequest.context.slice(0, 160)}；选项数=${firstRequest.options.length}`,
  )
  const firstPairs = firstRequest.options.filter((option) => option.value.startsWith('pair:'))
  check(
    `候选 = 除自己外的 6 组 pair（不含 ${SEAMSTRESS_SEAT} 号）`,
    firstPairs.length === 6 && firstPairs.every((option) => !pairHasSeat(option.value, SEAMSTRESS_SEAT)),
    `pair 候选=${firstPairs.map((option) => option.value).join(', ')}`,
  )
  check(
    '候选同时包含「摇头：本夜不使用能力」（decline）',
    firstRequest.options.some((option) => option.value === 'decline'),
    `选项=${firstRequest.options.map((option) => option.value).join(', ')}`,
  )
  await screenshot(seamstressPage, 'limitinfo-01-seamstress-night1-request')

  // 摇头：不记账、不落标记、不产生信息；之后夜晚仍会被唤醒（第二夜再证）。
  await seamstressPage
    .getByTestId('player-request-options')
    .locator('label.option[data-option-value="decline"]')
    .click()
  const declineAccepted = await submitRequestAndAwaitOutcome(seamstressPage, frameSinks.get(SEAMSTRESS_SEAT))
  const declineIdle = await waitForRequestPanelIdle(seamstressPage, 30_000)
  check(
    '摇头提交被受理且请求区回到空态',
    declineAccepted && declineIdle,
    `回执=${declineAccepted}；data-request-state=${await seamstressPage.getByTestId('player-request-panel').getAttribute('data-request-state')}`,
  )

  const nightOneResult = await finishNightQuickly(storytellerPage, '首夜')
  check(
    '首夜自然走完（摇头后不产生任何裁定点，未强推）',
    nightOneResult.completed === true && nightOneResult.natural === true,
    nightOneResult.natural
      ? `自然窗口（${config.slowPacer ? 38 : 8}s）内走完`
      : `强推 ${nightOneResult.forced} 步${nightOneResult.note ? `（${nightOneResult.note}）` : ''}`,
  )
  check('摇头后没有待裁定（不消耗、不记账的界面事实）', (await decisionId(storytellerPage)) === null, `裁定点=${await decisionId(storytellerPage)}`)
  check(
    `摇头后 ${SEAMSTRESS_SEAT} 号牌面无「失去能力」标记`,
    (await countSeatMarksOfKind(storytellerPage, SEAMSTRESS_SEAT, 'mark-exhausted')) === 0,
    JSON.stringify(await readSeatMarks(storytellerPage, SEAMSTRESS_SEAT)),
  )
  check(
    `摇头后 ${SEAMSTRESS_SEAT} 号玩家页信息面板为空（摇头不产生信息）`,
    (await readPlayerInformationCount(seamstressPage)) === '0',
    `count=${await readPlayerInformationCount(seamstressPage)}`,
  )

  console.log('=== 5/8 白天 1：艺术家提问 → 四答 / 挂起挡收口 / 重连等待态 / 要求重问 ===')
  const dayOne = await runCommand(storytellerPage, '开白天 1', () => storytellerPage.getByTestId('st-start-day').click())
  check('开白天被受理', dayOne.kind === 'Accepted', dayOne.raw)
  const dayStatus = await waitForAttribute(storytellerPage.getByTestId('st-day'), 'data-day-status', 'Open', 30_000)
  check('说书人面板进入「白天进行中」', dayStatus === 'Open', `data-day-status=${dayStatus}`)

  const artistPage = playerPages.get(ARTIST_SEAT)
  const artistIdle = await waitForArtistPanelState(artistPage, 'idle', 30_000)
  check(
    `白天：${ARTIST_SEAT} 号（艺术家）出现提问入口（idle 态）`,
    artistIdle !== null,
    JSON.stringify(artistIdle),
  )
  for (const seat of SEATS.filter((candidate) => candidate !== ARTIST_SEAT)) {
    const hasPanel = (await playerPages.get(seat).locator('[data-testid="player-artist-question"]').count()) > 0
    check(`${seat} 号玩家页没有提问入口（权限位只对本人）`, !hasPanel, `面板数=${hasPanel ? 1 : 0}`)
  }

  await askArtistQuestion(artistPage, QUESTION_ONE)
  const artistWaiting = await waitForArtistPanelState(artistPage, 'waiting', 30_000)
  check(
    '提问后本人页出现「已提问，等待说书人回答」态且带问题全文（三态面板的等待态）',
    artistWaiting !== null && artistWaiting.pending.includes(QUESTION_ONE),
    JSON.stringify(artistWaiting),
  )
  await screenshot(artistPage, 'limitinfo-02-artist-pending')

  const firstQuestionDecision = await waitForDecision(
    storytellerPage,
    (text) => text.includes(QUESTION_ONE),
    60_000,
  )
  check('说书人端裁定块读到问题全文（问题私密：本人 + 说书人）', firstQuestionDecision.includes(QUESTION_ONE), compact(firstQuestionDecision).slice(0, 220))
  const firstQuestionSeat = await waitForDecisionSeatHint(storytellerPage, ARTIST_SEAT, 20_000)
  check(
    `提问裁定归属 = ${ARTIST_SEAT} 号（本人）`,
    firstQuestionSeat.includes(`${ARTIST_SEAT} 号`),
    `归属文本=${firstQuestionSeat || '（未渲染）'}`,
  )
  const questionOptions = await readDecisionOptions(storytellerPage)
  check(
    '裁定候选恰好四答：是 / 不是 / 我不知道 / 要求重问（不消耗能力）',
    questionOptions.length === ARTIST_OPTION_TEXTS.length
      && ARTIST_OPTION_TEXTS.every((text) => questionOptions.includes(text)),
    `选项=${questionOptions.join(' | ')}`,
  )
  await screenshot(storytellerPage, 'limitinfo-03-artist-decision-four-options')

  // 无关玩家零下发（页面级）：问题全文不进其他席位。
  for (const seat of SEATS.filter((candidate) => candidate !== ARTIST_SEAT)) {
    const pageText = compact(await playerPages.get(seat).locator('body').innerText())
    check(`无关席位（${seat} 号）页面零问题全文`, !pageText.includes(QUESTION_ONE), pageText.slice(0, 120))
  }

  // 挂起裁定挡推进（R-0040 / D-0011）：白天收口被拒。
  const blockedClose = await runCommand(storytellerPage, '挂起时结束白天', () =>
    storytellerPage.getByTestId('st-close-day').click(),
  )
  check(
    '提问未结清 → 结束白天被拒（phase.artist_question_pending）',
    blockedClose.kind === 'Rejected' && blockedClose.raw.includes('phase.artist_question_pending'),
    blockedClose.raw,
  )

  // 重连（D-0014）：刷新 = 重新 Join，快照必须恢复进行中问题并在界面上可见。
  await rejoinSeat(artistPage, ARTIST_SEAT, seatTickets)
  const artistWaitingAfterReload = await waitForArtistPanelState(artistPage, 'waiting', 30_000)
  check(
    '重连（快照恢复）后：等待态仍在且带问题全文',
    artistWaitingAfterReload !== null && artistWaitingAfterReload.pending.includes(QUESTION_ONE),
    JSON.stringify(artistWaitingAfterReload),
  )
  await screenshot(artistPage, 'limitinfo-04-artist-pending-after-reload')

  // 「要求重问」：不记账、不落标记、可再问（R-0040 第 4 条）。
  const retried = await runCommand(storytellerPage, '要求重问', () =>
    clickDecisionOption(storytellerPage, '要求重问（不消耗能力）'),
  )
  check('「要求重问」被受理', retried.kind === 'Accepted', retried.raw)
  const retryCleared = await waitForDecisionCleared(storytellerPage, 30_000)
  check('重问后裁定块消失（进行中问题被清空）', retryCleared, retryCleared ? '已消失' : '裁定块仍在')
  const artistIdleAgain = await waitForArtistPanelState(artistPage, 'idle', 30_000)
  check('重问后本人页回到可提问态（输入框回来）', artistIdleAgain !== null, JSON.stringify(artistIdleAgain))
  check(
    `重问不落标记：${ARTIST_SEAT} 号牌面仍无「失去能力」`,
    (await countSeatMarksOfKind(storytellerPage, ARTIST_SEAT, 'mark-exhausted')) === 0,
    JSON.stringify(await readSeatMarks(storytellerPage, ARTIST_SEAT)),
  )
  check(
    '重问不产生信息：2 号玩家页信息面板仍为空',
    (await readPlayerInformationCount(artistPage)) === '0',
    `count=${await readPlayerInformationCount(artistPage)}`,
  )

  console.log('=== 6/8 再问一次并回答「不是」→ 信息 + 失能标记 + 入口消失 ===')
  await askArtistQuestion(artistPage, QUESTION_TWO)
  const secondQuestionDecision = await waitForDecision(
    storytellerPage,
    (text) => text.includes(QUESTION_TWO),
    60_000,
  )
  check(
    '第二次提问开出一条新的裁定点（问题全文更新，不是上一条残留）',
    secondQuestionDecision.includes(QUESTION_TWO) && !secondQuestionDecision.includes(QUESTION_ONE),
    compact(secondQuestionDecision).slice(0, 220),
  )
  const answered = await runCommand(storytellerPage, '回答「不是」', () => clickDecisionOption(storytellerPage, '不是'))
  check('回答「不是」被受理', answered.kind === 'Accepted', answered.raw)
  const answerCleared = await waitForDecisionCleared(storytellerPage, 30_000)
  check('裁定结清后裁定块消失', answerCleared, answerCleared ? '已消失' : '裁定块仍在')

  const artistInfoCount = await waitForAttributeValue(() => readPlayerInformationCount(artistPage), '1', 30_000)
  const artistRows = await readPlayerInformationRows(artistPage)
  check(
    `${ARTIST_SEAT} 号玩家页收到一条信息（艺术家，内容「不是」）`,
    artistInfoCount === '1'
      && artistRows.length === 1
      && artistRows[0].label === '艺术家（artist）'
      && artistRows[0].content === '不是',
    `count=${artistInfoCount}；行=${JSON.stringify(artistRows)}`,
  )
  const artistMark = await waitForSeatMark(storytellerPage, ARTIST_SEAT, 'mark-exhausted', '失去能力')
  const artistMarkTitle = (await readSeatMarks(storytellerPage, ARTIST_SEAT))
    .find((mark) => mark.className.includes('mark-exhausted'))?.title ?? ''
  check(
    `${ARTIST_SEAT} 号牌面出现「失去能力」标记且 title 记能力已用尽`,
    artistMark.found && artistMarkTitle.includes('能力已用尽'),
    artistMark.detail,
  )
  const artistPanelGone = await waitForArtistPanelDetached(artistPage, 30_000)
  check('回答消耗后：本人提问入口消失（每局限一次）', artistPanelGone, artistPanelGone ? '已撤下' : '面板仍在')
  const otherSeatMarks = []
  for (const seat of UNRELATED_SEATS.concat([SEAMSTRESS_SEAT])) {
    const marks = await readSeatMarks(storytellerPage, seat)
    if (marks.some((mark) => mark.className.includes('mark-exhausted'))) {
      otherSeatMarks.push(`${seat} 号`)
    }
  }
  check('其他席位（1 / 3 / 4 / 5 号）牌面无「失去能力」标记', otherSeatMarks.length === 0, otherSeatMarks.join(', ') || '未误标')
  await screenshot(artistPage, 'limitinfo-05-artist-answered-info')
  await screenshot(storytellerPage, 'limitinfo-06-grimoire-exhausted-marker')

  const dayOneClosed = await runCommand(storytellerPage, '结束白天 1', () =>
    storytellerPage.getByTestId('st-close-day').click(),
  )
  check('回答结清后白天正常收口', dayOneClosed.kind === 'Accepted', dayOneClosed.raw)

  console.log('=== 7/8 第二夜：恶魔击杀（顺序表在女裁缝之前）→ 女裁缝再被唤醒 → 选两名 → 裁定「是」 ===')
  const nightTwo = await runCommand(storytellerPage, '开第二夜', () => startNight(storytellerPage, 2))
  check('开第二夜被受理', nightTwo.kind === 'Accepted', nightTwo.raw)

  const demonPage = playerPages.get(DEMON_SEAT)
  const nightWindowMs = Math.max(60_000, Math.round(config.quotaSeconds * 30_000) + 20_000)
  const nightTwoDeadline = Date.now() + nightWindowMs
  let nightTwoSettled = false
  let demonKillHandled = false
  let seamstressSecondRequest = null
  let seamstressDecisionHandled = false
  while (Date.now() < nightTwoDeadline) {
    if (await nightSettled(storytellerPage)) {
      nightTwoSettled = true
      break
    }

    // 顺序表里方古（第 7 格）在女裁缝（第 18 格）之前：先应掉击杀请求，计划才会推进到她的格。
    if (!demonKillHandled) {
      const demonRequest = await readRequestOptions(demonPage)
      if (demonRequest.options.some((option) => option.value === `seat:${ARTIST_SEAT}`)) {
        await demonPage
          .getByTestId('player-request-options')
          .locator(`label.option[data-option-value="seat:${ARTIST_SEAT}"]`)
          .click()
        const killAccepted = await submitRequestAndAwaitOutcome(demonPage, frameSinks.get(DEMON_SEAT))
        check(
          `第二夜恶魔击杀 ${ARTIST_SEAT} 号被受理（顺序表：恶魔格在女裁缝之前）`,
          killAccepted,
          `回执=${killAccepted}`,
        )
        const artistLife = await waitForAttribute(
          storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${ARTIST_SEAT}"]`),
          'data-life',
          'Dead',
          30_000,
        )
        check(`${ARTIST_SEAT} 号席位死亡（击杀落账；不触发方古侵染——目标是镇民）`, artistLife === 'Dead', `data-life=${artistLife}`)
        demonKillHandled = true
      }
    }

    if (seamstressSecondRequest === null) {
      const secondRequest = await readRequestOptions(seamstressPage)
      if (secondRequest.options.some((option) => option.value === 'decline')) {
        seamstressSecondRequest = secondRequest
        const pairs = secondRequest.options.filter((option) => option.value.startsWith('pair:'))
        const samePairs = pairs.length === 6
          && pairs.every((option) => !pairHasSeat(option.value, SEAMSTRESS_SEAT))
        check(
          '第二夜女裁缝再次被唤醒（摇头不消耗 → 仍开选择；R-0040）',
          samePairs,
          `pair 候选=${pairs.map((option) => option.value).join(', ')}`,
        )
        await seamstressPage
          .getByTestId('player-request-options')
          .locator(`label.option[data-option-value="${PAIR_VALUE}"]`)
          .click()
        const pairAccepted = await submitRequestAndAwaitOutcome(seamstressPage, frameSinks.get(SEAMSTRESS_SEAT))
        check(`女裁缝在页面上选「${PAIR_PREVIEW}」并提交被受理`, pairAccepted, `回执=${pairAccepted}`)
      }
    }

    if (seamstressSecondRequest !== null && !seamstressDecisionHandled) {
      const decisionText = await readDecisionText(storytellerPage)
      if (decisionText.includes(`女裁缝选择了 ${PAIR_PREVIEW}`)) {
        const decisionSeat = await waitForDecisionSeatHint(storytellerPage, SEAMSTRESS_SEAT, 20_000)
        const options = await readDecisionOptions(storytellerPage)
        check(
          `女裁缝裁定归属 = ${SEAMSTRESS_SEAT} 号，上下文按当前账推演两人同阵营`,
          decisionSeat.includes(`${SEAMSTRESS_SEAT} 号`)
            && decisionText.includes('按当前账推演：两人属于同一阵营'),
          `归属=${decisionSeat || '（未渲染）'}；上下文=${compact(decisionText).slice(0, 220)}`,
        )
        check(
          '女裁缝裁定的候选 = 是 / 否（两名玩家同阵营的信息）',
          options.length === 2 && options.includes('是：两人属于同一阵营') && options.includes('否：两人不属于同一阵营'),
          `选项=${options.join(' | ')}`,
        )
        await screenshot(storytellerPage, 'limitinfo-07-seamstress-night2-decision')
        const saidYes = await runCommand(storytellerPage, '女裁缝裁定「是」', () =>
          clickDecisionOption(storytellerPage, '是：两人属于同一阵营'),
        )
        check('女裁缝裁定「是」被受理', saidYes.kind === 'Accepted', saidYes.raw)
        seamstressDecisionHandled = true
      }
    }

    await sleep(150)
  }

  check('第二夜自然收口（恶魔格 + 女裁缝格都处理完）', nightTwoSettled, nightTwoSettled ? '计划已走完' : '等待窗内未收口')
  check(
    '第二夜的三个交互都被处理（恶魔击杀 / 女裁缝请求 / 女裁缝裁定）',
    demonKillHandled && seamstressSecondRequest !== null && seamstressDecisionHandled,
    JSON.stringify({ demonKillHandled, seamstressRequest: seamstressSecondRequest !== null, seamstressDecisionHandled }),
  )

  const seamstressInfoCount = await waitForAttributeValue(() => readPlayerInformationCount(seamstressPage), '1', 30_000)
  const seamstressRows = await readPlayerInformationRows(seamstressPage)
  check(
    `${SEAMSTRESS_SEAT} 号玩家页收到一条信息（女裁缝，内容 = ${PAIR_PREVIEW} 属于同一阵营）`,
    seamstressInfoCount === '1'
      && seamstressRows.length === 1
      && seamstressRows[0].label === '女裁缝（seamstress）'
      && seamstressRows[0].content.includes(PAIR_PREVIEW)
      && seamstressRows[0].content.includes('属于同一阵营'),
    `count=${seamstressInfoCount}；行=${JSON.stringify(seamstressRows)}`,
  )
  const seamstressMark = await waitForSeatMark(storytellerPage, SEAMSTRESS_SEAT, 'mark-exhausted', '失去能力')
  check(
    `${SEAMSTRESS_SEAT} 号牌面出现「失去能力」标记（用后不再唤醒）`,
    seamstressMark.found,
    seamstressMark.detail,
  )
  for (const seat of UNRELATED_SEATS) {
    const count = await readPlayerInformationCount(playerPages.get(seat))
    const rows = await readPlayerInformationRows(playerPages.get(seat))
    check(
      `无关席位（${seat} 号）信息零下发`,
      count === '0' && rows.length === 0,
      `count=${count}；行=${JSON.stringify(rows)}`,
    )
  }
  const artistRowsAfterNight = await readPlayerInformationRows(artistPage)
  check(
    `${ARTIST_SEAT} 号（已死亡）信息里没有别人的女裁缝信息`,
    artistRowsAfterNight.every((row) => row.label !== '女裁缝（seamstress）'),
    JSON.stringify(artistRowsAfterNight),
  )
  await screenshot(seamstressPage, 'limitinfo-08-seamstress-player-info')
  await screenshot(storytellerPage, 'limitinfo-09-grimoire-two-markers')

  console.log('=== 8/8 第三夜：她的格走空槽、不再唤醒（可归因跳过）+ 隔离扫描 ===')
  const dayTwo = await runCommand(storytellerPage, '开白天 2', () => storytellerPage.getByTestId('st-start-day').click())
  check('开白天 2 被受理', dayTwo.kind === 'Accepted', dayTwo.raw)
  const dayTwoClosed = await runCommand(storytellerPage, '结束白天 2', () =>
    storytellerPage.getByTestId('st-close-day').click(),
  )
  check('结束白天 2 被受理', dayTwoClosed.kind === 'Accepted', dayTwoClosed.raw)

  await installNightThreeObservers(storytellerPage, seamstressPage)
  const nightThree = await runCommand(storytellerPage, '开第三夜', () => startNight(storytellerPage, 3))
  check('开第三夜被受理', nightThree.kind === 'Accepted', nightThree.raw)

  // 恶魔格（第 7 位）仍在她的格之前：先应掉击杀请求（打已死亡的 2 号，无事发生），计划才会推进到她的格。
  const nightThreeDemonRequest = await waitForRequestOptions(demonPage, nightWindowMs)
  const nightThreeKillAvailable = nightThreeDemonRequest.options.some(
    (option) => option.value === `seat:${ARTIST_SEAT}`,
  )
  check(
    '第三夜恶魔击杀请求出现且可选已死亡的 2 号（顺序表位置在女裁缝之前）',
    nightThreeKillAvailable,
    `上下文=${nightThreeDemonRequest.context.slice(0, 120)}；选项=${nightThreeDemonRequest.options.map((option) => option.value).join(', ')}`,
  )
  if (nightThreeKillAvailable) {
    await demonPage
      .getByTestId('player-request-options')
      .locator(`label.option[data-option-value="seat:${ARTIST_SEAT}"]`)
      .click()
    const accepted = await submitRequestAndAwaitOutcome(demonPage, frameSinks.get(DEMON_SEAT))
    check('第三夜恶魔击杀已死者（2 号）被受理：无事发生（不侵染、不再死亡）', accepted, `回执=${accepted}`)
  }

  const skipSeenInDom = await waitForObservedSlotContext(storytellerPage, '不再被唤醒', nightWindowMs)
  const skipSeenInFrame = await waitForFramePayload(storytellerSink, '不再被唤醒', 5_000)
  check(
    '第三夜：她的格不再唤醒，槽上下文留下可归因跳过（含「不再被唤醒」）',
    skipSeenInDom || skipSeenInFrame,
    `DOM 观察器=${skipSeenInDom}；说书人视图帧=${skipSeenInFrame}`,
  )
  await screenshot(storytellerPage, 'limitinfo-10-night3-slot-context')

  const nightThreeObservations = await readNightThreeObservers(storytellerPage, seamstressPage)
  const requestStates = nightThreeObservations.requestStates
  check(
    `第三夜：观察窗内 ${SEAMSTRESS_SEAT} 号玩家页从未出现请求（不再唤醒）`,
    requestStates.length > 0 && requestStates.every((state) => state.startsWith('idle')),
    `记录 ${requestStates.length} 条：${[...new Set(requestStates)].join(' | ').slice(0, 200)}`,
  )
  check(
    `第三夜：信息不增（仍 1 条）且「失去能力」标记仍在`,
    (await readPlayerInformationCount(seamstressPage)) === '1'
      && (await countSeatMarksOfKind(storytellerPage, SEAMSTRESS_SEAT, 'mark-exhausted')) === 1,
    `信息数=${await readPlayerInformationCount(seamstressPage)}；标记数=${await countSeatMarksOfKind(storytellerPage, SEAMSTRESS_SEAT, 'mark-exhausted')}`,
  )

  console.log('=== 隔离扫描：帧级零下发 + 页面级零说书人字段 ===')
  const receivedBySeat = new Map()
  for (const [seat, sink] of frameSinks.entries()) {
    receivedBySeat.set(
      seat,
      sink.frames.filter((frame) => frame.direction === 'received' && frame.messages.length > 0),
    )
  }

  // 信息只推给本人：女裁缝 → 1 号；艺术家 → 2 号。
  const pushedAbilities = []
  const misdelivered = []
  for (const [seat, frames] of receivedBySeat.entries()) {
    for (const frame of frames) {
      for (const message of frame.messages) {
        if (message.target !== 'ReceiveInformationResult') {
          continue
        }

        const ability = String(message.arguments?.[0]?.ability ?? '')
        pushedAbilities.push(`${seat}:${ability}`)
        if (
          (ability === 'seamstress' && seat !== SEAMSTRESS_SEAT)
          || (ability === 'artist' && seat !== ARTIST_SEAT)
        ) {
          misdelivered.push(`${seat} 号收到 ${ability}`)
        }
      }
    }
  }
  check(
    '信息结果只推给本人（女裁缝 → 1 号 / 艺术家 → 2 号；五席连接全量扫描）',
    misdelivered.length === 0
      && pushedAbilities.includes(`${SEAMSTRESS_SEAT}:seamstress`)
      && pushedAbilities.includes(`${ARTIST_SEAT}:artist`),
    misdelivered.join(' | ') || `下发=${pushedAbilities.join(', ')}`,
  )

  // 问题全文只进本人与说书人连接。
  const questionLeaks = []
  for (const [seat, frames] of receivedBySeat.entries()) {
    if (seat === ARTIST_SEAT) {
      continue
    }

    for (const frame of frames) {
      if (frame.payload.includes(QUESTION_ONE) || frame.payload.includes(QUESTION_TWO)) {
        questionLeaks.push(`${seat} 号的 ${frame.parsed?.target ?? '（未识别帧）'}`)
      }
    }
  }
  check(
    '问题全文只进本人与说书人连接（无关席位零下发）',
    questionLeaks.length === 0,
    questionLeaks.slice(0, 3).join(' | ') || `已扫描 ${receivedBySeat.size} 席`,
  )
  check(
    '阳性对照：艺术家本人连接上确实出现过问题全文（扫描不是空集）',
    frameSinks.get(ARTIST_SEAT).frames.some((frame) => frame.payload.includes(QUESTION_ONE)),
    `2 号连接 ${frameSinks.get(ARTIST_SEAT).frames.length} 帧`,
  )

  // 说书人字段 / 失能标记不进玩家连接：按**消息**扫（一帧可能合了多条，豁免与命中都必须落到消息上）。
  const leakHits = []
  let scannedFrames = 0
  for (const [seat, frames] of receivedBySeat.entries()) {
    for (const frame of frames) {
      scannedFrames += 1
      for (const message of frame.messages) {
        const text = JSON.stringify(message)
        for (const pattern of FORBIDDEN_FRAME_PATTERNS) {
          if (pattern.exemptTargets?.includes(message.target) === true) {
            continue
          }

          if (pattern.test(text)) {
            leakHits.push(`${seat} 号的 ${message.target ?? '（无 target）'} 含${pattern.label}`)
          }
        }
      }
    }
  }
  check(
    '玩家连接收到的全部帧里没有说书人字段与失能标记（推演 / MayBeFalse / note / 失去能力）',
    scannedFrames > 0 && leakHits.length === 0,
    leakHits.slice(0, 3).join(' | ') || `已扫描 ${receivedBySeat.size} 席 / ${scannedFrames} 帧`,
  )

  // 阳性对照：同一个词确实会在说书人连接上出现——否则上面的"零命中"可能只是扫描没接通。
  const storytellerFrames = storytellerSink.frames.filter((frame) => frame.parsed !== null)
  check(
    '阳性对照：说书人连接确实收到含「推演」与「失去能力」的视图帧（证明扫描不是空集）',
    storytellerFrames.some((frame) => frame.payload.includes('推演'))
      && storytellerFrames.some((frame) => frame.payload.includes('失去能力')),
    `说书人连接 ${storytellerFrames.length} 帧`,
  )

  for (const [seat, page] of playerPages.entries()) {
    const pageText = compact(await page.locator('body').innerText())
    const hits = FORBIDDEN_PLAYER_TOKENS.filter((token) => pageText.includes(token))
    const questionHits = seat === ARTIST_SEAT
      ? []
      : [QUESTION_ONE, QUESTION_TWO].filter((question) => pageText.includes(question))
    check(
      `${seat} 号页面文本没有说书人视角字段 / 失能标记 / 他人问题`,
      hits.length === 0 && questionHits.length === 0,
      [...hits, ...questionHits].join(', ') || '未命中',
    )
  }

  check('浏览器控制台没有报错', consoleErrors.length === 0, consoleErrors.slice(0, 3).join(' | '))
  await browser.close()
}

// —— 连接层：每席只挂**它自己那条**连接的帧观察器（不另开第二条连接，见文件头「连接口径」）——

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
 * 否则每条帧都会抛异常、被吞成"没有推送"——这会让所有推送扫描静默假绿。
 * 更关键的是：服务端会把同一连接上先后写出的多条消息（如"视图推送 + 调用回执"）**合进一帧**，
 * 只取第一条会让回执被前面的推送遮住，表现为"提交明明生效了却等不到受理回执"（本装置首跑实测踩到，
 * 两条提交各踩一次、且换着出现）。
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

/** 等某条连接的帧里出现一段文本（阳性对照 / 空槽跳过的第二通道）。 */
async function waitForFramePayload(sink, token, timeoutMs) {
  return waitForFrames(sink, (frames) => frames.some((frame) => frame.payload.includes(token)), timeoutMs)
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
 * 开会话：在「兜底与推进」里选口径（全程 Original）并点「开夜」。
 * 口径与夜序都从真界面下发，命令面无隐藏默认。
 */
async function startNight(page, nightNumber) {
  const box = page.locator('section', { hasText: '兜底与推进' })
  await box.locator('input[type="number"]').fill(String(nightNumber))
  await box.locator('select').selectOption('Original')
  await page.getByRole('button', { name: /开夜/ }).click()
}

/** 当前待裁定的裁定点标识；null = 现在没有裁定点。 */
async function decisionId(page) {
  const block = page.locator('[data-testid="console-decision"]')
  if ((await block.count()) === 0) {
    return null
  }

  const text = compact(await readTextBounded(block.first()))
  const matched = text.match(/[A-Za-z0-9_.:-]+/g) ?? []
  return matched.find((token) => token.includes(':')) ?? text.slice(0, 120)
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

/** 裁定候选的按钮文本（= 选项 preview；界面不给 data 属性，只能按可见文本点）。 */
async function readDecisionOptions(page) {
  const block = page.locator('[data-testid="console-decision"]')
  if ((await block.count()) === 0) {
    return []
  }

  return block.locator('.options button').evaluateAll((buttons) =>
    buttons.map((button) => {
      const preview = button.cloneNode(true)
      preview.querySelector('[data-testid="option-dead"]')?.remove()
      return (preview.textContent ?? '').replace(/\s+/g, ' ').trim()
    }),
  )
}

/** 在裁定点上按选项 preview 点一下（说书人的裁定值就由这次点击下发）。 */
async function clickDecisionOption(page, preview) {
  await page
    .locator('[data-testid="console-decision"] .options button')
    .filter({ hasText: preview })
    .first()
    .click({ timeout: 30_000 })
}

/** 裁定块的归属席位文案（`console-decision-seat`，形如「归属：3 号」）；没有该元素时返回 ''。 */
async function readDecisionSeatHint(page) {
  const hint = page.locator('[data-testid="console-decision-seat"]')
  if ((await hint.count()) === 0) {
    return ''
  }

  return compact(await readTextBounded(hint.first()))
}

/** 等归属文案出现且含某个席位号（视图推送与裁定块同一次到达，这里只兜渲染时序）；超时返回最后读到的文本。 */
async function waitForDecisionSeatHint(page, seat, timeoutMs = 20_000) {
  const deadline = Date.now() + timeoutMs
  let text = ''
  while (Date.now() < deadline) {
    text = await readDecisionSeatHint(page)
    if (text.includes(`${seat} 号`)) {
      return text
    }

    await sleep(150)
  }

  return text
}

/** 环区常驻「本步上下文」行的文本（`grimoire-slot-context`）；没有该元素时返回 ''。 */
async function readSlotContext(page) {
  const line = page.locator('[data-testid="grimoire-slot-context"]')
  if ((await line.count()) === 0) {
    return ''
  }

  return compact(await readTextBounded(line.first()))
}

/** 等待裁定块消失（裁定被消费后 v-if 撤掉）；超时返回 false。 */
async function waitForDecisionCleared(page, timeoutMs) {
  try {
    await page.locator('[data-testid="console-decision"]').waitFor({ state: 'detached', timeout: timeoutMs })
    return true
  } catch {
    return false
  }
}

/** 顶部状态条的单元格（按 caption 取）：说书人一眼看到「计划走完没有 / 当前槽位是谁」。 */
async function statusCells(page) {
  const cells = await page.locator('header.strip .cell').evaluateAll((nodes) =>
    nodes.map((node) => ({
      caption: (node.querySelector('.caption')?.textContent ?? '').trim(),
      text: (node.textContent ?? '').replace(/\s+/g, ' ').trim(),
      mono: (node.querySelector('.mono')?.textContent ?? '').trim(),
    })),
  )
  const byCaption = {}
  for (const cell of cells) {
    if (cell.caption.length > 0) {
      byCaption[cell.caption] = cell
    }
  }

  return byCaption
}

/** 等状态条某个 caption 的文本包含 token（如「计划」→「已走完」）；超时返回 null。 */
async function waitForStatusCellText(page, caption, token, timeoutMs = 30_000) {
  const deadline = Date.now() + timeoutMs
  let cells = {}
  while (Date.now() < deadline) {
    cells = await statusCells(page)
    if ((cells[caption]?.text ?? '').includes(token)) {
      return cells
    }

    await sleep(150)
  }

  return null
}

/** 一个席位牌上的标记（label + 类名 + title）；「无标记」占位也一并读出，由调用方过滤。 */
async function readSeatMarks(page, seat) {
  return page
    .locator(`[data-testid="grimoire-seat"][data-seat="${seat}"] .mark`)
    .evaluateAll((nodes) =>
      nodes.map((node) => ({
        label: (node.textContent ?? '').trim(),
        className: typeof node.className === 'string' ? node.className : '',
        title: node.getAttribute('title') ?? '',
      })),
    )
}

/** 某席上指定类名的标记数量（`mark-exhausted` 这类派生标记的判据）。 */
async function countSeatMarksOfKind(page, seat, classToken) {
  const marks = await readSeatMarks(page, seat)
  return marks.filter((mark) => mark.className.includes(classToken)).length
}

/** 等某席出现指定标记（类名 + 文案），返回 { found, detail }。 */
async function waitForSeatMark(page, seat, classToken, labelToken, timeoutMs = 30_000) {
  const deadline = Date.now() + timeoutMs
  let marks = []
  while (Date.now() < deadline) {
    marks = await readSeatMarks(page, seat)
    if (marks.some((mark) => mark.className.includes(classToken) && mark.label.includes(labelToken))) {
      return { found: true, detail: JSON.stringify(marks) }
    }

    await sleep(150)
  }

  return { found: false, detail: JSON.stringify(marks) }
}

/**
 * 夜晚是否已经收口：看**界面自己**的判据——「开白天」按钮的使能位就是服务端的
 * `planCompleted && phase ∈ {FirstNight, OtherNight}`（见 DayControl.vue 的 canStartDay）。
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
 * 收尾一个夜晚：先观察一个足够长的窗口（= 整夜槽位数 × 配额 + 余量），没收口再用强推把剩余空槽位推完。
 * 看到**任何**挂起请求就立刻停手并如实报出来（绝不越权了结随后的请求 / 裁定点）。
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

/** 玩家页信息面板的每一行（真 DOM：能力中文标签 + 说书人给的内容）。 */
async function readPlayerInformationRows(page) {
  const panel = page.locator('[data-testid="player-information"]')
  if ((await panel.count()) === 0) {
    return []
  }

  return panel.locator('li').evaluateAll((items) =>
    items.map((item) => {
      const label = (item.querySelector('strong')?.textContent ?? '').trim()
      const text = (item.textContent ?? '').trim()
      return {
        index: item.getAttribute('data-information-index'),
        label,
        content: text.startsWith(label) ? text.slice(label.length).replace(/^[：:]\s*/, '').trim() : text,
      }
    }),
  )
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

/** 玩家请求面板的当前上下文与选项（值 + 可见文案）；空态返回空集。 */
async function readRequestOptions(page) {
  const context = (await readTextOrNull(page, 'player-request-context')) ?? ''
  const options = await page
    .getByTestId('player-request-options')
    .locator('label.option')
    .evaluateAll((labels) =>
      labels.map((label) => ({
        value: label.getAttribute('data-option-value') ?? '',
        text: (label.textContent ?? '').replace(/\s+/g, ' ').trim(),
      })),
    )
    .catch(() => [])
  return { context, options }
}

/** 等请求面板开出（上下文 + 选项都到位）；超时返回最后一次读数。 */
async function waitForRequestOptions(page, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  let last = { context: '', options: [] }
  while (Date.now() < deadline) {
    last = await readRequestOptions(page)
    if (last.context.length > 0 && last.options.length > 0) {
      return last
    }

    await sleep(200)
  }

  return last
}

/** `pair:A+B` 里是否含某个席位（候选集合的排除断言用；形状不对返回 false）。 */
function pairHasSeat(value, seat) {
  const matched = /^pair:(\d+)\+(\d+)$/.exec(value)
  if (matched === null) {
    return false
  }

  return Number(matched[1]) === seat || Number(matched[2]) === seat
}

/** 艺术家提问面板的呈现态：{ state, pending }；没有面板（不是本人 / 已用尽）时返回 null。 */
async function readArtistPanel(page) {
  const panel = page.locator('[data-testid="player-artist-question"]')
  if ((await panel.count()) === 0) {
    return null
  }

  return {
    state: (await panel.getAttribute('data-question-state')) ?? '',
    pending: (await readTextOrNull(page, 'player-artist-question-pending')) ?? '',
  }
}

/** 等提问面板到达指定呈现态（idle / waiting）；超时返回最后一次读数（可能是 null）。 */
async function waitForArtistPanelState(page, expectedState, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  let panel = null
  while (Date.now() < deadline) {
    panel = await readArtistPanel(page)
    if (panel !== null && panel.state === expectedState) {
      return panel
    }

    await sleep(150)
  }

  return panel
}

/** 等提问入口被撤下（回答消耗后 canAsk=false，v-if 撤掉整块）；超时返回 false。 */
async function waitForArtistPanelDetached(page, timeoutMs) {
  try {
    await page.locator('[data-testid="player-artist-question"]').waitFor({ state: 'detached', timeout: timeoutMs })
    return true
  } catch {
    return false
  }
}

/** 在本人页面上提出一个问题（真界面：填文本 → 点提交）。 */
async function askArtistQuestion(page, question) {
  const section = page.locator('[data-testid="player-artist-question"]')
  await section.locator('input').fill(question)
  await page.getByTestId('player-artist-question-submit').click()
}

/** 加入一席（首次进入 / 刷新后重连同一条路径：票据 → 加入 → 席位徽章）。 */
async function joinSeat(page, seat, seatTickets) {
  const input = page.getByPlaceholder('席位票据')
  await input.waitFor({ timeout: 30_000 })
  if ((await input.inputValue()).trim().length === 0) {
    await input.fill(seatTickets[seat - 1].ticket)
  }

  await page.getByRole('button', { name: '加入' }).click()
  const badge = await waitForText(page.locator('[data-testid="player-seat"]'), String(seat), 30_000)
  check(`${seat} 号（${ASSIGN[seat - 1]}）加入玩家端`, badge.includes(String(seat)), badge)
}

/**
 * 重连：刷新页面 = 重新 Join（D-0014 的重连路径，不是第二条并发连接）。
 *
 * 两种真实情况都要兜住：面板记住上次票据时**自动加入**（加入按钮在飞行中处于禁用态，
 * 点它会超时——本装置首跑踩到），或停在登录态等手工加入。因此只看两条稳定判据：
 * 席位徽章出现（自动加入完成）或加入按钮**可得可点**（手工路径）。
 */
async function rejoinSeat(page, seat, seatTickets) {
  await page.reload()
  const badge = page.locator('[data-testid="player-seat"]')
  const joinButton = page.getByRole('button', { name: '加入' })

  const deadline = Date.now() + 30_000
  while (Date.now() < deadline) {
    if ((await badge.count()) > 0) {
      return waitForText(badge, String(seat), 10_000)
    }

    if ((await joinButton.count()) > 0 && (await joinButton.isEnabled().catch(() => false))) {
      const input = page.getByPlaceholder('席位票据')
      if ((await input.inputValue()).trim().length === 0) {
        await input.fill(seatTickets[seat - 1].ticket)
      }

      await joinButton.click()
      break
    }

    await sleep(100)
  }

  return waitForText(badge, String(seat), 30_000)
}

/**
 * 第三夜观察器（页面侧）：装 MutationObserver，收集空槽上下文与请求态——
 * 轮询窗口可能错过一闪而过的空槽，观察器不会。
 */
async function installNightThreeObservers(storytellerPage, seamstressPage) {
  await storytellerPage.evaluate(() => {
    window.__slotContextLog = []
    const record = () => {
      const line = document.querySelector('[data-testid="grimoire-slot-context"]')
      if (line === null) {
        return
      }

      const text = (line.textContent ?? '').replace(/\s+/g, ' ').trim()
      if (text.length > 0 && !window.__slotContextLog.includes(text)) {
        window.__slotContextLog.push(text)
      }
    }
    record()
    new MutationObserver(record).observe(document.body, {
      subtree: true,
      childList: true,
      attributes: true,
      characterData: true,
    })
  })

  await seamstressPage.evaluate(() => {
    window.__requestStateLog = []
    const record = () => {
      const panel = document.querySelector('[data-testid="player-request-panel"]')
      if (panel === null) {
        return
      }

      window.__requestStateLog.push(
        `${panel.getAttribute('data-request-state') ?? ''}:${panel.getAttribute('data-request-id') ?? ''}`,
      )
    }
    record()
    new MutationObserver(record).observe(document.body, {
      subtree: true,
      childList: true,
      attributes: true,
      characterData: true,
    })
  })
}

async function readNightThreeObservers(storytellerPage, seamstressPage) {
  return {
    slotContexts: await storytellerPage.evaluate(() => window.__slotContextLog ?? []),
    requestStates: await seamstressPage.evaluate(() => window.__requestStateLog ?? []),
  }
}

/** 等观察器收集到含 token 的槽上下文（说书人页）；超时返回 false。 */
async function waitForObservedSlotContext(storytellerPage, token, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  let log = []
  while (Date.now() < deadline) {
    log = await storytellerPage.evaluate(() => window.__slotContextLog ?? [])
    if (log.some((text) => text.includes(token))) {
      return true
    }

    await sleep(150)
  }

  return false
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
    const invocation = sink.invocations.slice(before).find((frame) => frame.parsed?.target === 'SubmitResponse')
    if (invocation !== undefined) {
      const invocationId = String(invocation.parsed.invocationId ?? '')
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

  // 超时现场证据：把窗口内的调用与回执原样打出来（不猜，看到什么记什么）。
  const sent = sink.invocations.slice(before).map((frame) => `${frame.parsed?.target}:${frame.parsed?.invocationId}`)
  const completions = sink.frames
    .flatMap((frame) => frame.messages)
    .filter((message) => message.type === 3)
    .slice(-4)
    .map((message) => `${message.invocationId}:${JSON.stringify(message.result ?? message.error)}`)
  console.error(
    `[诊断] 没有等到 SubmitResponse 的受理回执；本窗调用=${sent.join(', ') || '（无）'}；`
      + `最近回执=${completions.join(' ‖ ') || '（无）'}`,
  )
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
    text = compact(await readTextBounded(locator))
    if (text.includes(expected)) {
      return text
    }

    await sleep(150)
  }

  return text
}

async function readTextOrNull(page, testId) {
  const locator = page.getByTestId(testId)
  if ((await locator.count()) === 0) {
    return null
  }

  return compact(await readTextBounded(locator.first()))
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
  console.log('\n=== 限次信息族批次（seamstress-artist）取证结论 ===')
  for (const result of results) {
    console.log(`${result.pass ? 'PASS' : 'FAIL'}  ${result.label}${result.detail ? ` → ${result.detail}` : ''}`)
  }

  const failed = results.filter((result) => !result.pass)
  console.log(failed.length === 0 ? `全部通过（${results.length} 项）` : `失败 ${failed.length} / ${results.length}`)
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
  const parsed = { port: 5421, vitePort: 5301 }
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
