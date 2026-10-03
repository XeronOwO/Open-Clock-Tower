/**
 * 死亡触发族（贤者 / 心上人）批次装置 —— 票据
 * docs/backlog/in-progress/sage-and-sweetheart.md 的验收矩阵行 1 / 9 / 14 / 15（界面级），
 * 平台口径见 docs/standard/rulings.md R-0038（贤者判据与当晚展示）/ R-0039（心上人即时醉酒）。
 *
 * 它回答：**「死亡时立即产生效果 / 当晚开裁定点 + 推进被挡 + 信息只到本人」这条链路
 * 在真界面上跑得通吗？**
 *
 * 固定 5 席夹具（1 方古恶魔 / 2 贤者 / 3 心上人 / 4 呆瓜 / 5 理发师；五席都没有首夜行动格，
 * 首夜靠配额自然收口）。两个场景串成一条链路：
 *
 *   准备  分配 5 席 → 开首夜 1（Original，五席无首夜行动格 → 整夜无裁定点）→
 *         开白天 1：3 号（心上人）自我提名 → 1 / 2 / 4 / 5 投赞成（4 票 × 2 ≥ 存活 5 席）→
 *         计票 → 结束白天并处决 3 号；
 *   场景 A（心上人）说书人端出现**触发型裁定**（无槽位承载；提示含「心上人」与「醉酒」）→
 *         此时候裁定未了结，开夜被拒（`phase.trigger_choice_pending`）→
 *         说书人点「4 号玩家」提交 `seat:4` → 4 号牌面出现「醉酒」标记、操作台出现来源 = 心上人的
 *         生效中效果，其他席位不误标；醉酒跨到次夜仍在（不自动清除）；
 *   场景 B（贤者）  开第二夜（Original）→ 1 号恶魔页收到操作请求 → 页面上击杀 `seat:2` →
 *         2 号席位死亡 → 当夜「贤者」触发格开裁定（提示含「按击杀记录推演」与真恶魔「1 号」）→
 *         说书人点「1 号 + 4 号」提交 `pair:1+4` → 2 号玩家页出现那条信息（含「1 号」「4 号」），
 *         3 / 4 / 5 号页零下发，五席连接上零说书人字段（「推演」/ MayBeFalse / note）。
 *
 * 两处必须写明的连接口径（装置踩过的坑）：
 *   1) **服务端每个席位只保留一条连接**（ConnectionRegistry.IssueForSeat：同席新连接会立即吊销旧凭据）。
 *      因此本装置**不给任何席位另开第二条连接**——提名 / 投票 / 作答全部走玩家页自己的连接，
 *      推送扫描挂在**玩家页自己的 WebSocket 帧**上（page.on('websocket')）。另开 SignalR 客户端会把
 *      浏览器页的凭据挤掉，页面从此收不到推送，而那些断言仍然"绿"——那是假绿，不是能力通过。
 *   2) SignalR 默认 JSON 协议的**帧尾带 `\x1e` 记录分隔符**：直接 JSON.parse 每条帧都会抛异常、
 *      被吞成「没有推送」→ 推送扫描静默假绿。必须按 `\x1e` 切段、逐段解析（parseSignalRMessages）——
 *      一帧可能合了"推送 + 调用回执"多条消息，只取第一条会让回执被遮住。
 *
 * 一处断言口径的说明（说书人字段扫描）：`ReceiveOperationRequestAnswered` 帧里的 `note` 是**玩家自己**
 * 作答时填的备注（PlayerPanel 从不填，一向为空），不是说书人专属字段；因此 `note` 这一项对该方法豁免，
 * 「推演」「MayBeFalse」则对所有收到的帧一律零容忍。
 *
 * 与主批次的分工：主批次（verify-storyteller-panel.mjs）跑五席固定花名册的通用玩法回归，
 * 本装置只跑死亡触发族这一条链路（无分段开关：场景 A 是场景 B 的前置，拆开跑没有意义）。
 *
 * 前置：Node >= 22.5（node:sqlite）、本机已构建 web/node_modules（playwright）。
 * 用法（在仓库根运行；默认迭代档 = 快节奏 + 不落盘截图 + 复用产物）：
 *   node tools/verify-death-triggers.mjs                                        # 迭代档
 *   node tools/verify-death-triggers.mjs --quota 2 --screenshots-all --build    # 取证档（一批一次）
 *   node tools/verify-death-triggers.mjs --port 5417 --vite-port 5297           # 自定端口
 *   node tools/verify-death-triggers.mjs --list-sections                        # 打印两个场景与前置
 *
 * 断言清单（分组；总数以实际输出为准）：
 *   装备：5 席票据齐备 / 说书人加入 / 五席各一条连接加入 / 分配受理 / 开首夜受理 /
 *         首夜自然走完（未强推）/ 首夜零裁定点 / 席位连接上确实收到阶段推送（扫描非空集）；
 *   白天：开白天受理 + 进入 Open / **槽位读数 = 「1 / 1」（白天计划恰好 1 个 DayWindow 槽位）** /
 *         3 号自我提名进账 / 四次赞成到服务端 / 计票受理 / 即将被处决 = 3 号 / 结束白天受理 /
 *         处决记录 + 席位死亡 / **收口后槽位读数封顶**（不出现越界的「2 / 1」，走完即「已完成」）；
 *   心上人：触发型裁定出现（提示含「心上人」「醉酒」）/ 裁定点标识 = `sweetheart:3`（按席位派生，
 *         无槽位段）/ **裁定块给出归属席位**（`console-decision-seat` 含「3 号」）/
 *         **圆环上该席带待裁定高亮**（`data-decision="true"` + 牌面「待裁定」）**、环头出现
 *         「定位到 3 号（待裁定）」按钮**（`attentionSeat` 读服务端归属席位；点它把操作台切回该席）/
 *         该裁定不由槽位承载（状态条「计划」= 已走完、槽位标识为空）/ 候选 = 全体 5 席 /
 *         **只有已死亡的 3 号候选带「已死亡」标签**（`option-dead`；阳性对照：存活候选不带一例）/
 *         开夜被拒 `phase.trigger_choice_pending` / 提交 `seat:4` 受理 / 结清后裁定块消失 /
 *         4 号牌面出现「醉酒」（mark-drunk）/ 其他席位不误标 / 4 号操作台出现来源 = 心上人的生效中效果 /
 *         醉酒跨阶段不自动清除；
 *   贤者：裁定结清后可开第二夜 / 恶魔页收到操作请求 / 请求面板给出「选一名玩家」（含 2 号）/
 *         页面选 2 号提交回执 Accepted + 请求区回空态 / 2 号席位死亡 / 当夜贤者格开裁定
 *         （提示含「按击杀记录推演」与真恶魔「1 号」）/ **裁定块归属席位含「2 号」+ 该席待裁定高亮 +
 *         环头「定位到 2 号（待裁定）」按钮**（触发格裁定靠服务端归属席位定位）/
 *         **环区常驻「本步上下文」非空且含推演行**（`grimoire-slot-context`：触发格没有槽位提示，
 *         挂起时取裁定上下文）/
 *         是新的裁定点（非心上人残留）/ 由槽位承载（对照：计划进行中 + 当前槽位标识非空）/
 *         候选 = 除贤者外 6 组 pair 且不含 2 号 / **非 `seat:N` 的 pair 候选一律不带「已死亡」标签** /
 *         提交 `pair:1+4` 受理；
 *   隔离：2 号页出现那条信息（ability=sage，内容含「1 号」「4 号」）/ 3 / 4 / 5 号页零该信息 /
 *         信息结果只推给 2 号（五席连接全量扫描）/ 五席连接零说书人字段（推演 / MayBeFalse / note）/
 *         裁定结清后夜晚继续自动推进到收口（独立复核 H-1 的界面级回归）/
 *         阳性对照：说书人连接确实收到含「推演」的视图帧 / 贤者信息帧本身不含说书人字段 /
 *         五席页面文本没有推演行 / 浏览器控制台无报错。
 *
 * 外部耦合（换机器先核对 web/AGENTS.md §3.1）：宿主编译产物路径（脚本启动时打印）、
 * SQLite 表 Games 的 StorytellerTicket / SeatsJson 列形状（SeatId 序列化为 { "value": N }）、
 * SignalR 默认 JSON 协议的帧形状（`{"type":1,"target":…}`）、说书人裁定控制台的 DOM
 * （`[data-testid="console-decision"]` 的选项按钮文本 = 选项 preview）、座位牌标记类名 `mark-drunk`、
 * 说书人面板的归属 / 标签锚点（`console-decision-seat` = 「归属：N 号」、选项按钮内的 `option-dead`
 * 标签文本 = 「已死亡」（**读候选 preview 时要把它摘掉**，否则「候选 = 全体 N 席」会假红）、
 * 座位牌 `data-decision` = 待裁定高亮、环头 `.ring-head button` 的
 * 「定位到 N 号（待裁定）」文案；**该按钮只在选中席 ≠ 归属席时渲染**——初始选中态跟随归属席，
 * 故断言前必须先点开别的席位）、状态条槽位读数文案（进行中 = `N / M`、走完 = 「已完成」）、
 * 环区常驻上下文行 `grimoire-slot-context`（文本前缀「本步上下文：」）。
 * 退出码：0 = 全过；1 = 有失败；2 = 环境缺依赖（playwright / node:sqlite）。
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

// 分段开关对本装置无意义（场景 A 是场景 B 的前置）：显式查询时如实说明并退出，不假装有段可跑。
if (config.listSections) {
  console.log('本装置没有分段开关：两个场景在一条链路上顺序执行（前置准备只做一次）。')
  console.log('  准备  ：分配 5 席 → 开首夜 1（自然走完）→ 白天 1：处决 3 号心上人')
  console.log('  心上人：触发型裁定挂起 → 开夜被拒 phase.trigger_choice_pending → 指定 4 号醉酒')
  console.log('  贤者  ：开第二夜 → 恶魔击杀 2 号 → 当夜裁定 → pair:1+4 → 信息只到本人')
  console.log('整轮跑：node tools/verify-death-triggers.mjs --build')
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

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-death-triggers-'))
const databasePath = path.join(workspace, 'death-triggers.db')
const screenshotsDir = path.resolve(repositoryRoot, 'artifacts', 'web')
mkdirSync(screenshotsDir, { recursive: true })

const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`

/** 五席夹具：1 方古（恶魔，次夜击杀）/ 2 贤者 / 3 心上人（白天被处决）/ 4 呆瓜 / 5 理发师。 */
const ASSIGN = ['fang-gu', 'sage', 'sweetheart', 'klutz', 'barber']
/** 席位号从花名册顺序派生：调换 ASSIGN 顺序时断言跟着走，不靠人工同步。 */
const seatOf = (slug) => ASSIGN.indexOf(slug) + 1
const DEMON_SEAT = seatOf('fang-gu')
const SAGE_SEAT = seatOf('sage')
const SWEETHEART_SEAT = seatOf('sweetheart')
const KLUTZ_SEAT = seatOf('klutz')
const BARBER_SEAT = seatOf('barber')
/** 反方向的无关席位（信息零下发）：已死亡的心上人 + 两个陪跑席。 */
const UNRELATED_SEATS = [SWEETHEART_SEAT, KLUTZ_SEAT, BARBER_SEAT]
/** 心上人裁定的醉酒目标（R-0039 第 3 条：任一席位，含已死亡玩家）。 */
const DRUNK_TARGET_SEAT = KLUTZ_SEAT
/** 贤者的展示组合：真恶魔（1 号）+ 一名陪跑（4 号）——与 R-0038 第 3 条的推演一致。 */
const SAGE_SHOW_PREVIEW = `${DEMON_SEAT} 号 + ${DRUNK_TARGET_SEAT} 号`

/**
 * 玩家端不该出现的词：说书人裁定提示的推演行 / 失效标记（D-0012 §4.3）。
 * 推演原文形如「按击杀记录推演：杀死他的是 1 号」——那是**说书人视角**的提示，
 * 一旦出现在玩家连接或玩家页面上，等于把"平台已经替你算好了"泄露给玩家。
 */
const FORBIDDEN_PLAYER_TOKENS = ['推演', 'MayBeFalse', 'malfunction', 'Malfunctions']

/**
 * 帧扫描里的说书人字段。`ReceiveOperationRequestAnswered` 的 `note` 是玩家自己作答时填的备注
 * （界面从不填、一向为空），不算说书人字段，故豁免；其余一律零容忍。
 */
const FORBIDDEN_FRAME_PATTERNS = [
  { label: '推演行', test: (payload) => payload.includes('推演') },
  { label: 'MayBeFalse', test: (payload) => /maybefalse/i.test(payload) },
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

  // 说书人连接也挂帧收集器：它只用于**阳性对照**（证明「推演」这类词确实会在连接上出现，
  // 玩家侧的零命中不是"扫描根本没接通"式的假绿）。
  const storytellerSink = createFrameSink()
  const storytellerPage = await newPage(browser, { width: 1600, height: 1100 }, consoleErrors, storytellerSink)
  await storytellerPage.goto(viteUrl)
  await storytellerPage.getByPlaceholder('说书人票据').fill(ticket)
  await storytellerPage.getByRole('button', { name: '加入' }).click()
  await storytellerPage.locator('[data-testid="grimoire"]').waitFor({ timeout: 30_000 })
  check('说书人加入后看板可见（魔典主视图）', (await storytellerPage.locator('[data-testid="grimoire"]').count()) === 1)

  /**
   * 五席各开**一个**浏览器上下文（服务端每席只认一条连接，见文件头「连接口径」）：
   * 恶魔要真点界面击杀、心上人要在自己页面上提名、贤者要收信息、两席提供反方向的零下发证据。
   */
  const playerPages = new Map()
  const frameSinks = new Map()
  for (const seat of [DEMON_SEAT, SWEETHEART_SEAT, KLUTZ_SEAT, BARBER_SEAT, SAGE_SEAT]) {
    const sink = createFrameSink()
    frameSinks.set(seat, sink)
    const page = await newPage(browser, { width: 900, height: 1000 }, consoleErrors, sink)
    await page.goto(`${viteUrl}/#player`)
    await page.getByPlaceholder('席位票据').fill(seatTickets[seat - 1].ticket)
    await page.getByRole('button', { name: '加入' }).click()
    const badge = await waitForText(page.locator('[data-testid="player-seat"]'), String(seat), 30_000)
    check(`${seat} 号（${ASSIGN[seat - 1]}）加入玩家端`, badge.includes(String(seat)), badge)
    playerPages.set(seat, page)
  }

  console.log('=== 4/8 开局分配 → 开首夜（Original，1 号）===')
  const assignmentSelects = storytellerPage.locator('section', { hasText: '开局分配' }).locator('select')
  for (const [index, slug] of ASSIGN.entries()) {
    await assignmentSelects.nth(index).selectOption(slug)
  }

  const assigned = await runCommand(storytellerPage, '分配', () =>
    storytellerPage.getByRole('button', { name: '提交分配' }).click(),
  )
  check('分配 5 个角色被受理', assigned.kind === 'Accepted', assigned.raw)

  const nightStarted = await runCommand(storytellerPage, '开首夜', () => startNight(storytellerPage, 1))
  check('开首夜被受理（Original 顺序表建表）', nightStarted.kind === 'Accepted', nightStarted.raw)

  const nightOneComplete = await finishNightQuickly(storytellerPage, '首夜')
  check(
    '首夜：五席无首夜行动者 → 整夜自然走完（未强推）',
    nightOneComplete.completed === true && nightOneComplete.natural === true,
    nightOneComplete.natural
      ? `自然窗口（${config.slowPacer ? 12 : 8}s）内走完`
      : `强推 ${nightOneComplete.forced} 步${nightOneComplete.note ? `（${nightOneComplete.note}）` : ''}`,
  )
  const nightOneDecisionId = await decisionId(storytellerPage)
  check('首夜：没有任何说书人裁定点', nightOneDecisionId === null, `裁定点=${nightOneDecisionId}`)

  // 顺带证明"推送真的会到达玩家页"：阶段推送在连接上被看到（防止"零推送"式的假绿）。
  const phasePushes = await waitForFrames(frameSinks.get(DEMON_SEAT), (frames) =>
    frames.some((frame) => frame.parsed?.target === 'ReceivePhaseStarted'),
  )
  check(
    '1 号页在自己的连接上收到阶段推送（推送扫描确有信号，不是空集假绿）',
    phasePushes,
    `已捕获 ${describeFrameCounts(frameSinks)}`,
  )

  console.log('=== 5/8 白天 1：心上人自我提名 → 四票赞成 → 计票 → 处决 3 号 ===')
  const dayStarted = await runCommand(storytellerPage, '开白天', () => storytellerPage.getByTestId('st-start-day').click())
  check('开白天被受理', dayStarted.kind === 'Accepted', dayStarted.raw)
  const dayStatus = await waitForAttribute(storytellerPage.getByTestId('st-day'), 'data-day-status', 'Open', 30_000)
  check('说书人面板进入「白天进行中」', dayStatus === 'Open', `data-day-status=${dayStatus}`)

  // 白天的计划形状由内核定死：恰好一个 DayWindow 槽位（DayStepMachine.StartDay 的校验）。
  // 读数从 1 起算，这一条把"白天只有 1 个槽位"钉在真界面上——收口那两条才有对照。
  const dayRunningStrip = await waitForStatusCellText(storytellerPage, '槽位', '1 / 1', 20_000)
  const dayRunningSlotText = (dayRunningStrip ?? (await statusCells(storytellerPage)))['槽位']?.text ?? ''
  check(
    '白天进行中：状态条槽位读数 = 「1 / 1」（白天计划恰好 1 个槽位）',
    dayRunningSlotText.includes('1 / 1'),
    `槽位读数=${dayRunningSlotText}`,
  )

  // 3 号（心上人）在**自己的页面**上提名 3 号自己：R-0039 的触发点是"作为心上人死亡"，死因不限。
  const sweetheartPage = playerPages.get(SWEETHEART_SEAT)
  await sweetheartPage.getByTestId('player-nominee-select').selectOption(String(SWEETHEART_SEAT))
  await sweetheartPage.getByTestId('player-nominate').click()

  const nominationList = storytellerPage.getByTestId('st-day-nominations')
  const nominationCount = await waitForAttribute(nominationList, 'data-nomination-count', '1', 30_000)
  check('心上人自我提名进入公开账目（1 条）', nominationCount === '1', `data-nomination-count=${nominationCount}`)

  // 四票赞成（1 / 2 / 4 / 5）：4 票 × 2 = 8 ≥ 存活 5 席，达到计票阈值（DayMachine：votes * 2 >= alive）。
  for (const voterSeat of [DEMON_SEAT, SAGE_SEAT, KLUTZ_SEAT, BARBER_SEAT]) {
    await castYesVote(playerPages.get(voterSeat), voterSeat)
  }

  const firstNomination = nominationList.locator('li').first()
  const voteCount = await waitForAttribute(firstNomination, 'data-nomination-votes', '4', 30_000)
  check('四次「投赞成」都到服务端（公开票数 4）', voteCount === '4', `data-nomination-votes=${voteCount}`)

  const counted = await runCommand(storytellerPage, '计票', () => storytellerPage.getByTestId('st-count-votes').click())
  check('计票被受理', counted.kind === 'Accepted', counted.raw)

  const aboutSeat = await waitForAttribute(
    storytellerPage.getByTestId('st-about-to-be-executed'),
    'data-seat',
    String(SWEETHEART_SEAT),
    30_000,
  )
  check('票数达标 → 进入「即将被处决」', aboutSeat === String(SWEETHEART_SEAT), `data-seat=${aboutSeat}`)

  const closed = await runCommand(storytellerPage, '结束白天并处决', () => storytellerPage.getByTestId('st-close-day').click())
  check('结束白天被受理', closed.kind === 'Accepted', closed.raw)
  const executedSeat = await waitForAttribute(
    storytellerPage.getByTestId('st-executed'),
    'data-seat',
    String(SWEETHEART_SEAT),
    30_000,
  )
  const executedLife = await waitForAttribute(
    storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${SWEETHEART_SEAT}"]`),
    'data-life',
    'Dead',
    15_000,
  )
  check(
    '心上人被处决：处决记录 + 席位死亡',
    executedSeat === String(SWEETHEART_SEAT) && executedLife === 'Dead',
    `处决=${executedSeat}；牌面=${executedLife}`,
  )

  // 白天收口：`slotIndex == slotCount` 是内核"计划已走完"的正常表示（结束白天 = 走完这个 1 槽计划）。
  // 状态条曾把它直接 `+1` 读成越界的「2 / 1」（slotCounterTextOf）；现在封顶为「已完成」。
  const dayClosedStrip = await waitForStatusCellText(storytellerPage, '计划', '已走完', 30_000)
  const dayClosedSlotText = (dayClosedStrip ?? (await statusCells(storytellerPage)))['槽位']?.text ?? ''
  check(
    '白天收口：槽位读数不出现越界的「2 / 1」',
    !dayClosedSlotText.includes('2 / 1'),
    `槽位读数=${dayClosedSlotText}`,
  )
  check(
    '白天收口：计划已走完 → 槽位读数 = 「已完成」（封顶；白天只有 1 个槽位）',
    dayClosedSlotText.includes('已完成') || /(^|\s)1 \/ 1(\s|$)/.test(dayClosedSlotText),
    `槽位读数=${dayClosedSlotText}`,
  )
  await screenshot(storytellerPage, 'deathtrigger-01-day-executed-sweetheart')

  console.log('=== 6/8 场景 A（心上人）：触发型裁定 → 推进被拒 → 指定 4 号醉酒 ===')
  const stingDecision = await waitForDecision(
    storytellerPage,
    (text) => text.includes('心上人') && text.includes('醉酒'),
    90_000,
  )
  check(
    '说书人端出现触发型裁定：提示含「心上人」「醉酒」与死亡席位',
    stingDecision.includes('心上人') && stingDecision.includes('醉酒') && stingDecision.includes(`${SWEETHEART_SEAT} 号`),
    compact(stingDecision),
  )
  const stingDecisionId = await decisionId(storytellerPage)
  check(
    '裁定点标识按席位派生（sweetheart:3）、不带槽位段 —— 触发型裁定',
    /^sweetheart:3$/.test(String(stingDecisionId)),
    `id=${stingDecisionId}`,
  )

  // 归属席位：触发型裁定没有行动者，只有内核知道"谁在等"——服务端给的 awaitingDecisionSeat
  // 落到界面上有两处：操作台的 console-decision-seat 文案、圆环头部（attentionSeat）的定位入口。
  const stingSeatHint = await waitForDecisionSeatHint(storytellerPage, SWEETHEART_SEAT, 20_000)
  check(
    `裁定块给出归属席位：console-decision-seat 含「${SWEETHEART_SEAT} 号」`,
    stingSeatHint.includes(`${SWEETHEART_SEAT} 号`),
    `归属文本=${stingSeatHint || '（未渲染）'}`,
  )

  const sweetheartCard = storytellerPage.locator(
    `[data-testid="grimoire-seat"][data-seat="${SWEETHEART_SEAT}"]`,
  )
  const stingHighlight = await waitForAttribute(sweetheartCard, 'data-decision', 'true', 15_000)
  const stingCardText = compact(await sweetheartCard.innerText())
  check(
    `圆环上待裁定席位带高亮：${SWEETHEART_SEAT} 号 data-decision=true 且牌面出现「待裁定」`,
    stingHighlight === 'true' && stingCardText.includes('待裁定'),
    `data-decision=${stingHighlight}；牌面=${stingCardText.slice(0, 120)}`,
  )

  // 环头「定位到 N 号（待裁定）」按钮**只在选中席 ≠ 归属席时渲染**（GrimoireView：selected 默认
  // 跟随 attentionSeat）。先点开 1 号把选中态挪走，按钮才是这条链路的可见证据；再点它切回 3 号，
  // 证明它接的确实是服务端给的归属席位，而不是"当前行动者"。
  await storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${DEMON_SEAT}"]`).click()
  const stingLocateText = await waitForGrimoireLocate(storytellerPage, SWEETHEART_SEAT, 20_000)
  check(
    `圆环头部出现「定位到 ${SWEETHEART_SEAT} 号（待裁定）」按钮（attentionSeat 读服务端归属席位）`,
    stingLocateText.includes(`定位到 ${SWEETHEART_SEAT} 号`) && stingLocateText.includes('待裁定'),
    `按钮=${stingLocateText || '（未渲染）'}`,
  )
  await storytellerPage
    .locator('[data-testid="grimoire"] .ring-head button')
    .filter({ hasText: `定位到 ${SWEETHEART_SEAT} 号` })
    .click()
  const locatedConsoleSeat = await waitForAttribute(
    storytellerPage.locator('[data-testid="seat-console"]'),
    'data-console-seat',
    String(SWEETHEART_SEAT),
    15_000,
  )
  check(
    `点「定位到 ${SWEETHEART_SEAT} 号」把操作台切到该席`,
    locatedConsoleSeat === String(SWEETHEART_SEAT),
    `data-console-seat=${locatedConsoleSeat}`,
  )

  const stingOptions = await readDecisionOptions(storytellerPage)
  check(
    `候选 = 全体 ${ASSIGN.length} 席（含已死亡的心上人自己；R-0039 第 3 条）`,
    stingOptions.length === ASSIGN.length
      && stingOptions.includes(`${DRUNK_TARGET_SEAT} 号玩家`)
      && stingOptions.includes(`${SWEETHEART_SEAT} 号玩家`),
    `选项=${stingOptions.join(', ')}`,
  )

  // 候选的生死标注：只对 `seat:N` 选项按状态账 Life=Dead 打标，不影响候选集合（R-0039）。
  const stingOptionTags = await readDecisionOptionTags(storytellerPage)
  const deadTaggedOptions = stingOptionTags.filter((option) => option.deadLabel !== null)
  check(
    `候选里只有已死亡的 ${SWEETHEART_SEAT} 号候选带「已死亡」标签（option-dead）`,
    deadTaggedOptions.length === 1
      && deadTaggedOptions[0].text.includes(`${SWEETHEART_SEAT} 号玩家`)
      && deadTaggedOptions[0].deadLabel === '已死亡',
    `带标签=${deadTaggedOptions.map((option) => `${option.text}（标签=${option.deadLabel}）`).join(' | ') || '（无）'}`
      + `；共 ${stingOptionTags.length} 个候选`,
  )
  const aliveStingOption = stingOptionTags.find((option) => option.text.includes(`${DEMON_SEAT} 号玩家`))
  check(
    `阳性对照：存活候选（${DEMON_SEAT} 号玩家）不带「已死亡」标签`,
    aliveStingOption !== undefined && aliveStingOption.deadLabel === null,
    aliveStingOption === undefined
      ? '（没找到该候选）'
      : `候选=${aliveStingOption.text}；标签=${aliveStingOption.deadLabel ?? '无'}`,
  )
  const stingStrip = await statusCells(storytellerPage)
  check(
    '该裁定不由槽位承载：状态条「计划」= 已走完、当前槽位标识为空',
    (stingStrip['计划']?.text ?? '').includes('已走完') && (stingStrip['槽位']?.mono ?? '') === '',
    JSON.stringify(stingStrip),
  )
  const blockedNight = await runCommand(storytellerPage, '裁定挂起时开夜', () => startNight(storytellerPage, 2))
  check(
    '裁定未了结 → 开夜被拒（phase.trigger_choice_pending；R-0039 第 6 条）',
    blockedNight.kind === 'Rejected' && blockedNight.raw.includes('phase.trigger_choice_pending'),
    blockedNight.raw,
  )
  await screenshot(storytellerPage, 'deathtrigger-02-night-blocked-by-trigger-choice')

  const stingSettled = await runCommand(storytellerPage, '心上人裁定（4 号醉酒）', () =>
    clickDecisionOption(storytellerPage, `${DRUNK_TARGET_SEAT} 号玩家`),
  )
  check(
    `说书人点「${DRUNK_TARGET_SEAT} 号玩家」提交 seat:${DRUNK_TARGET_SEAT} 被受理`,
    stingSettled.kind === 'Accepted',
    stingSettled.raw,
  )
  const stingCleared = await waitForDecisionCleared(storytellerPage, 30_000)
  check('结清后待裁定块消失（裁定点被消费，不是残留）', stingCleared, stingCleared ? '已消失' : '待裁定块仍在')

  const drunkMark = await waitForSeatMark(storytellerPage, DRUNK_TARGET_SEAT, 'mark-drunk', '醉酒')
  check(
    `说书人面板 ${DRUNK_TARGET_SEAT} 号牌面出现「醉酒」标记（效果落账 + 维度对账）`,
    drunkMark.found,
    drunkMark.detail,
  )
  const wronglyDrunk = []
  for (const seat of [DEMON_SEAT, SAGE_SEAT, BARBER_SEAT]) {
    const marks = await readSeatMarks(storytellerPage, seat)
    if (marks.some((mark) => mark.className.includes('mark-drunk') || mark.label.includes('醉酒'))) {
      wronglyDrunk.push(`${seat} 号`)
    }
  }
  check('其他席位（1 / 2 / 5 号）牌面没有醉酒标记（效果只落在裁定目标上）', wronglyDrunk.length === 0, wronglyDrunk.join(', ') || '未误标')

  await storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${DRUNK_TARGET_SEAT}"]`).click()
  const targetConsole = compact(await storytellerPage.getByTestId('seat-console').innerText())
  check(
    `${DRUNK_TARGET_SEAT} 号操作台出现来源 = 心上人的「生效中」效果（醉酒的来由在界面上看得见）`,
    targetConsole.includes('心上人') && targetConsole.includes('生效中') && targetConsole.includes('醉酒'),
    targetConsole.slice(0, 220),
  )
  await screenshot(storytellerPage, 'deathtrigger-03-seat-drunk-marker')

  console.log('=== 7/8 场景 B（贤者）：第二夜恶魔击杀贤者 → 当夜裁定 → 展示两名玩家 ===')
  const nightTwo = await runCommand(storytellerPage, '开第二夜', () => startNight(storytellerPage, 2))
  check('裁定结清后开第二夜被受理', nightTwo.kind === 'Accepted', nightTwo.raw)

  const drunkAcrossNight = await waitForSeatMark(storytellerPage, DRUNK_TARGET_SEAT, 'mark-drunk', '醉酒')
  check(
    `醉酒跨阶段不自动清除（次夜里 ${DRUNK_TARGET_SEAT} 号仍标醉酒；R-0039 第 4 条）`,
    drunkAcrossNight.found,
    drunkAcrossNight.detail,
  )

  const demonPage = playerPages.get(DEMON_SEAT)
  const demonAsked = await waitForFrames(frameSinks.get(DEMON_SEAT), (frames) =>
    frames.some((frame) => frame.parsed?.target === 'ReceiveOperationRequest'),
  )
  check('第二夜恶魔（1 号）页在自己连接上收到操作请求', demonAsked, `已捕获 ${describeFrameCounts(frameSinks)}`)

  const demonRequest = await waitForRequestPanel(demonPage, 60_000)
  check(
    `恶魔的请求面板开出「选一名玩家」的玩家端选项（含 ${SAGE_SEAT} 号贤者）`,
    demonRequest.options.includes(`seat:${SAGE_SEAT}`),
    `选项=${demonRequest.options.join(', ')}；上下文=${demonRequest.context.slice(0, 120)}`,
  )
  await demonPage
    .getByTestId('player-request-options')
    .locator(`label.option[data-option-value="seat:${SAGE_SEAT}"]`)
    .click()
  const submitAccepted = await submitRequestAndAwaitOutcome(demonPage, frameSinks.get(DEMON_SEAT))
  const requestIdle = await waitForRequestPanelIdle(demonPage, 30_000)
  check(
    `恶魔在页面上选 ${SAGE_SEAT} 号（贤者）并提交：服务端回执 Accepted + 请求区回到空态`,
    submitAccepted && requestIdle,
    `回执=${submitAccepted}；data-request-state=${await demonPage.getByTestId('player-request-panel').getAttribute('data-request-state')}`,
  )

  const sageLife = await waitForAttribute(
    storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${SAGE_SEAT}"]`),
    'data-life',
    'Dead',
    30_000,
  )
  check('2 号席位死亡（恶魔击杀落账，贤者事实的触发前提）', sageLife === 'Dead', `data-life=${sageLife}`)

  const sageDecision = await waitForDecision(
    storytellerPage,
    (text) => text.includes('贤者') && text.includes('推演'),
    90_000,
  )
  check(
    '当夜贤者触发格开裁定：提示含「按击杀记录推演」与真恶魔「1 号」',
    sageDecision.includes('贤者') && sageDecision.includes('按击杀记录推演') && sageDecision.includes(`${DEMON_SEAT} 号`),
    compact(sageDecision),
  )
  const sageDecisionId = await decisionId(storytellerPage)
  check(
    '贤者的提示是**新的**裁定点（不是心上人那条的残留）',
    sageDecision.includes('贤者')
      && !sageDecision.includes('心上人')
      && sageDecisionId !== null
      && sageDecisionId !== stingDecisionId,
    `id=${sageDecisionId}（心上人 id=${stingDecisionId}）`,
  )

  // 触发格裁定同样靠服务端归属席位定位：这一格没有行动者（2 号已死），归属 = 2 号贤者本人。
  const sageSeatHint = await waitForDecisionSeatHint(storytellerPage, SAGE_SEAT, 20_000)
  check(
    `贤者触发格裁定块给出归属席位：console-decision-seat 含「${SAGE_SEAT} 号」`,
    sageSeatHint.includes(`${SAGE_SEAT} 号`),
    `归属文本=${sageSeatHint || '（未渲染）'}`,
  )
  // 环区常驻「本步上下文」：触发格没有槽位提示，挂起时取**裁定上下文**
  // （GrimoireView.slotContextText = awaitingDecisionContext ?? currentSlotContext）——贤者的推演行
  // 因此在圆环头部就能看到，不必翻裁定块或数据抽屉。
  const sageRingContext = await waitForSlotContext(storytellerPage, '推演', 20_000)
  check(
    '贤者触发格挂起时：环区「本步上下文」非空且含推演行（grimoire-slot-context）',
    sageRingContext.includes('本步上下文：') && sageRingContext.includes('推演'),
    `环区上下文=${sageRingContext || '（未渲染）'}`,
  )
  const sageHighlight = await waitForAttribute(
    storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${SAGE_SEAT}"]`),
    'data-decision',
    'true',
    15_000,
  )
  check(
    `圆环上待裁定席位带高亮：${SAGE_SEAT} 号 data-decision=true`,
    sageHighlight === 'true',
    `data-decision=${sageHighlight}`,
  )
  await storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${DEMON_SEAT}"]`).click()
  const sageLocateText = await waitForGrimoireLocate(storytellerPage, SAGE_SEAT, 20_000)
  check(
    `圆环头部出现「定位到 ${SAGE_SEAT} 号（待裁定）」按钮`,
    sageLocateText.includes(`定位到 ${SAGE_SEAT} 号`) && sageLocateText.includes('待裁定'),
    `按钮=${sageLocateText || '（未渲染）'}`,
  )

  const sageStrip = await statusCells(storytellerPage)
  check(
    '贤者裁定由槽位承载（对照心上人）：状态条「计划」= 进行中、当前槽位标识非空',
    (sageStrip['计划']?.text ?? '').includes('进行中') && (sageStrip['槽位']?.mono ?? '').length > 0,
    JSON.stringify(sageStrip),
  )
  const sageOptions = await readDecisionOptions(storytellerPage)
  check(
    '候选 = 除贤者外的任意两名（6 组 pair，且不含贤者本人 2 号）',
    sageOptions.length === 6
      && sageOptions.includes(SAGE_SHOW_PREVIEW)
      && sageOptions.every((option) => !option.includes(`${SAGE_SEAT} 号`)),
    `选项=${sageOptions.join(', ')}`,
  )

  // 负方向对照：生死标注只认 `seat:N` 选项的形状。贤者的候选全是 `pair:A+B`（含已死亡的 3 号），
  // 一个标签都不该出现——否则等于对所有候选「凡含已死席位就标红」，那是另一条规则。
  const sageOptionTags = await readDecisionOptionTags(storytellerPage)
  const sageDeadTagged = sageOptionTags.filter((option) => option.deadLabel !== null)
  check(
    '非 seat:N 的 pair 候选一律不带「已死亡」标签（标注只认席位选项形状）',
    sageOptionTags.length === sageOptions.length && sageDeadTagged.length === 0,
    `候选 ${sageOptionTags.length} 个；带标签=${sageDeadTagged.map((option) => option.text).join(' | ') || '（无）'}`,
  )
  await screenshot(storytellerPage, 'deathtrigger-04-sage-decision')

  const sageSettled = await runCommand(storytellerPage, '贤者展示两名玩家', () =>
    clickDecisionOption(storytellerPage, SAGE_SHOW_PREVIEW),
  )
  check(`说书人点「${SAGE_SHOW_PREVIEW}」提交 pair:1+4 被受理`, sageSettled.kind === 'Accepted', sageSettled.raw)

  console.log('=== 8/8 信息只到本人 + 反方向零下发 + 帧扫描零说书人字段 ===')
  const sagePage = playerPages.get(SAGE_SEAT)
  const sageInfoCount = await waitForAttributeValue(() => readPlayerInformationCount(sagePage), '1', 30_000)
  const sageRows = await readPlayerInformationRows(sagePage)
  check(
    `${SAGE_SEAT} 号玩家页出现一条信息结果（ability=sage，内容含「${DEMON_SEAT} 号」「${DRUNK_TARGET_SEAT} 号」）`,
    sageInfoCount === '1'
      && sageRows.length === 1
      && sageRows[0].ability === 'sage'
      && sageRows[0].content.includes(`${DEMON_SEAT} 号`)
      && sageRows[0].content.includes(`${DRUNK_TARGET_SEAT} 号`),
    `count=${sageInfoCount}；行=${JSON.stringify(sageRows)}`,
  )
  await screenshot(sagePage, 'deathtrigger-05-sage-player-info')

  for (const seat of UNRELATED_SEATS) {
    const page = playerPages.get(seat)
    const count = await readPlayerInformationCount(page)
    const text = await readPlayerInformationText(page)
    check(
      `无关席位（${seat} 号）信息面板零下发（count=0 + 空态文案）`,
      count === '0' && text.includes('还没有收到信息') && !text.includes('得知两名玩家'),
      `count=${count}；文本=${compact(text)}`,
    )
  }
  await screenshot(playerPages.get(BARBER_SEAT), 'deathtrigger-06-unrelated-player-clean')

  // —— 连接层（真 SignalR 帧）的全量扫描：每席只看自己那条连接收到的推送 ——
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
      if (seat !== SAGE_SEAT) {
        misdelivered.push(`${seat} 号收到 ${ability}`)
      }
    }
  }
  check(
    '信息结果只推给贤者本人（五席连接各自的推送全量扫描）',
    misdelivered.length === 0 && pushedAbilities.includes(`${SAGE_SEAT}:sage`),
    misdelivered.join(' | ') || `下发=${pushedAbilities.join(', ')}`,
  )

  const leakHits = []
  let scannedFrames = 0
  for (const [seat, frames] of receivedBySeat.entries()) {
    for (const frame of frames) {
      scannedFrames += 1
      for (const pattern of FORBIDDEN_FRAME_PATTERNS) {
        if (pattern.exemptTargets?.includes(frame.parsed.target) === true) {
          continue
        }

        if (pattern.test(frame.payload)) {
          leakHits.push(`${seat} 号的 ${frame.parsed.target} 含${pattern.label}`)
        }
      }
    }
  }
  check(
    '五席连接收到的全部帧里没有说书人字段（推演 / MayBeFalse / note）',
    scannedFrames > 0 && leakHits.length === 0,
    leakHits.slice(0, 3).join(' | ') || `已扫描 ${receivedBySeat.size} 席 / ${scannedFrames} 帧`,
  )

  // 阳性对照：同一个词确实会在说书人连接上出现——否则上面的"零命中"可能只是扫描没接通。
  const storytellerFrames = storytellerSink.frames.filter((frame) => frame.parsed !== null)
  check(
    '阳性对照：说书人连接确实收到含「推演」的视图帧（证明扫描不是空集）',
    storytellerFrames.some((frame) => frame.payload.includes('推演')),
    `说书人连接 ${storytellerFrames.length} 帧`,
  )

  const sageInfoFrames = receivedBySeat
    .get(SAGE_SEAT)
    .filter((frame) => frame.parsed.target === 'ReceiveInformationResult')
  const sageInfoPayload = sageInfoFrames.map((frame) => frame.payload).join('')
  check(
    '贤者那条信息帧本身只有 ability + content（无 MayBeFalse / note / 推演）',
    sageInfoFrames.length >= 1
      && !/maybefalse/i.test(sageInfoPayload)
      && !/"note"\s*:/i.test(sageInfoPayload)
      && !sageInfoPayload.includes('推演'),
    compact(sageInfoPayload).slice(0, 180),
  )

  // H-1 回归（界面级）：裁定结清后夜晚必须继续自动推进到收口——修复前触发型 / 触发格裁定
  // 结清后没有任何推进入口，状态条会一直停在「计划 进行中 / 槽位 13 / 21」，只能由说书人逐格强推。
  const nightSettled = await waitForStatusCellText(storytellerPage, '计划', '已走完', 30_000)
  check(
    '贤者裁定结清后夜晚继续自动推进到收口（独立复核 H-1 的界面级回归）',
    nightSettled !== null,
    nightSettled === null ? '状态条「计划」在等待窗内没有变成「已走完」' : JSON.stringify(nightSettled['计划'] ?? {}),
  )

  for (const [seat, page] of playerPages.entries()) {
    const pageText = compact(await page.locator('body').innerText())
    const hits = FORBIDDEN_PLAYER_TOKENS.filter((token) => pageText.includes(token))
    check(`${seat} 号页面文本没有说书人视角的推演行 / 失效标记`, hits.length === 0, hits.join(', ') || '未命中')
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
 * 开会话：在「兜底与推进」里选口径（两夜都走 Original：触发格在 Original 表上就在恶魔之后）
 * 并点「开夜」。口径与夜序都从真界面下发，命令面无隐藏默认。
 */
async function startNight(page, nightNumber) {
  const box = page.locator('section', { hasText: '兜底与推进' })
  await box.locator('input[type="number"]').fill(String(nightNumber))
  await box.locator('select').selectOption('Original')
  await page.getByRole('button', { name: /开夜/ }).click()
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

/**
 * 裁定候选按钮：`{ text, deadLabel }`——`text` = 选项 preview、`deadLabel` = 按钮内 `option-dead`
 * 标签的文案（没有标签时为 null）。
 *
 * 读文本时必须**把死亡标签摘掉**：界面把它嵌在按钮里
 * （`<button>{{preview}}<span data-testid="option-dead">已死亡</span></button>`），直接取
 * `textContent` 会把标签文案混进 preview，让"候选 = 全体 N 席"这类按候选集合比对的断言假红
 * （2026-10-05 本轮实测踩到）。
 */
async function readDecisionOptionTags(page) {
  const block = page.locator('[data-testid="console-decision"]')
  if ((await block.count()) === 0) {
    return []
  }

  return block.locator('.options button').evaluateAll((buttons) =>
    buttons.map((button) => {
      const tag = button.querySelector('[data-testid="option-dead"]')
      const preview = button.cloneNode(true)
      preview.querySelector('[data-testid="option-dead"]')?.remove()
      return {
        text: (preview.textContent ?? '').replace(/\s+/g, ' ').trim(),
        deadLabel: tag?.textContent?.trim() ?? null,
      }
    }),
  )
}

/** 裁定点的候选按钮文本（= 选项 preview；界面不给 data 属性，只能按可见文本点）。 */
async function readDecisionOptions(page) {
  return (await readDecisionOptionTags(page)).map((option) => option.text)
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

  return compact(await hint.first().innerText())
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

  return compact(await line.first().innerText())
}

/** 等环区上下文行出现且含 token（值随视图推送到达）；超时返回最后一次读到的文本。 */
async function waitForSlotContext(page, token, timeoutMs = 20_000) {
  const deadline = Date.now() + timeoutMs
  let text = ''
  while (Date.now() < deadline) {
    text = await readSlotContext(page)
    if (text.includes(token)) {
      return text
    }

    await sleep(150)
  }

  return text
}

/**
 * 圆环头部的「定位到 N 号（…）」按钮文案；超时返回 ''。
 *
 * 必须限定在 `.grimoire .ring-head` 里：席位操作台的裁定块也有同名按钮（那是"当前选中席 ≠ 归属席"
 * 时的第二个入口）。该按钮只在选中席 ≠ 归属席时渲染——初始选中态跟随归属席，所以要断言它，
 * 得先把选中态点到别的席位上。
 */
async function waitForGrimoireLocate(page, seat, timeoutMs = 20_000) {
  const button = page
    .locator('[data-testid="grimoire"] .ring-head button')
    .filter({ hasText: `定位到 ${seat} 号` })
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if ((await button.count()) > 0) {
      return compact(await button.first().innerText())
    }

    await sleep(150)
  }

  return ''
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

/**
 * 顶部状态条的单元格（按 caption 取）：说书人一眼看到「计划走完没有 / 当前槽位是谁」。
 * 触发型裁定（心上人）出现时**没有当前槽位**——这是「不由槽位承载」在界面上的判据。
 */
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
  console.log('\n=== 死亡触发族批次（death-triggers）取证结论 ===')
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
  const parsed = { port: 5417, vitePort: 5297 }
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
