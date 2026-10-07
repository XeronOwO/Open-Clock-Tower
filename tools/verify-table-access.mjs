/**
 * 桌的访问模式与旅行者离场装置（E60 / D-0037）—— 第 24 个真机装置。
 *
 * 它回答两个**别处证明不了**的问题：
 *
 *   1) **说书人改了「这一桌怎么进人」之后，别人屏幕上看到的东西对不对？**（验收矩阵行 7 / 8 / 4 / 2）
 *      说书人在主持台把桌切成邀请制，**在场玩家那一页不刷新、不重连**就该变；大厅里这一桌照样列出、
 *      标「邀请制」、席位点不动；持邀请码的人仍然进得来；切回公开桌，空席位重新点得动。
 *   2) **旅行者离场那条往返走不走得通？**（行 9 / 10 / 11）
 *      说书人在「旅行者」区块把旅行者加入（席位留空 = 服务端**追加新席位**并签发邀请码）→ 那名玩家
 *      凭码入座 → 本人提出离场申请 → 说书人页面出现待批行 → 批准 → 本人看到「已离场」；
 *      第二条申请走驳回，本人看到「驳回」且**没有**离场。
 *
 * 为什么"不刷新不重连"必须单独判（而不是"等了 8 秒之后值对上了就算"）：这条能力的**全部意义**就是
 * 推送——若界面是靠刷新 / 重连后重取快照才对上的，功能等于没做。所以本装置在拨开关**之前**先给
 * 那一页装上三个**装置自用**的观测点（不进产品代码）：
 *   · 文档标记：整页刷新会把 `window` 上的标记清掉（`addInitScript` 之外的地方写，刷新即失）；
 *   · 导航计数：`framenavigated` / `load` 事件各数一次，导航过就不是"没刷新"；
 *   · hub WebSocket 条数：SignalR 的自动重连（`withAutomaticReconnect([0, 1000, 3000, 5000])`）必然
 *     **新建**一条 WebSocket，条数变了就是重连过（Vite 的 HMR 也是 WebSocket，按 `/hub/` 过滤掉）。
 * 三者同时成立才算"没刷新、没重连"，读数原样进报告（这一条的失败明细里能看到是哪一个不成立）。
 *
 * 开场与入座照抄既有做法（`tools/lib/entrance.mjs`）：说书人 = 注册账号 → 开一桌 → 进主持台；
 * 自助入座走 `seatByAccount`，凭邀请码入座（新账号那条）走 `seatByInviteCode`；席位票据仍直读库的
 * `Games.SeatsJson`（邀请码 = `桌标识:席位票据`，装置从库里取那一串，再与说书人面板上显示的**对读**）。
 * 只有一处例外：本装置需要一个**"注册完就停在大厅"**的玩家（大厅读数只有停在大厅的人才看得到），
 * 既有 helper 全都直接入座，所以复用 `entrance.mjs` 导出的 `registerOnGate`。
 *
 * 场景（5 席 + 服务端为旅行者追加的 2 个席位）：
 *   1 号 = 玩家 A（公开桌自助入座；行 7 的"在场玩家"就是它）
 *   3 号 = 大厅观察员 B（注册后**不入座**：行 8 的大厅读数与行 4 的持码入座都由它走）
 *   4 号 = 迟到者 D（切回公开桌之后从大厅点得动并坐下——行 2 的反方向对照）
 *   6 号 = 旅行者 T1（说书人追加席位 + 签发邀请码；走"申请 → 批准"）
 *   7 号 = 旅行者 T2（同上；走"申请 → 驳回"）。**必须是另一个旅行者角色**：角色唯一，
 *          同一个角色给第二席会被服务端按 `legality.character_duplicated` 拒掉。
 *
 * 首跑咬出并已修掉的一条真缺陷（记在这里，免得被当成"已知边界"）：本人提出的申请若**没写理由**，
 * `PendingDepartureNote` 为 null，而界面当时拿"非 null"当"有待批申请"的判据——于是这条**默认路径**下
 * 本人页面上整块离场区直接消失，申请却已经送到了说书人。修法是把两件事拆开
 * （`PlayerView.HasPendingDeparture` 回答"有没有"，理由是另一件事），本装置对 T1 / T2 两条路径都判等待态。
 *
 * 用法（在仓库根运行；默认迭代档 = 快节拍 + 不落盘截图 + 复用产物）：
 *   node tools/verify-table-access.mjs                                      # 迭代档（全部段）
 *   node tools/verify-table-access.mjs --list-sections                      # 只列段名，不起宿主
 *   node tools/verify-table-access.mjs --only lobby                         # 执行到该段为止，且只判该段
 *   node tools/verify-table-access.mjs --from traveller                     # 全程执行，但从该段起才计入判定
 *   node tools/verify-table-access.mjs --quota 2 --screenshots-all --build  # 取证档（一批一次）
 *   node tools/verify-table-access.mjs --port 5423 --vite-port 5303         # 自定端口
 *
 * 段落（前缀执行 + 判定过滤：`--only` 与 `--from` 互斥；前面的段是必要前置、照跑但只有选中段计入判定）：
 *   boot · front · open · seat · access · lobby · invite · public · traveller · reject · wrap
 *
 * 外部耦合（换机器先核对 `docs/acceptance/devices.md` §3）：
 *   宿主编译产物路径 · SQLite `Games.SeatsJson`（席位票据的读法收在 `tools/lib/entrance.mjs`）·
 *   **`Games.IsInviteOnly`**（访问模式那一列，D-0037 由 `IsLocked` 原地改名而来——装置顺带读它，
 *   证明"切换是落库的事实"而不只是界面上的读数）· 入场锚点（`entrance.mjs` 头部清单，含本批新增的
 *   `player-table-access` / `table-access` / `traveller-departures` 那一组）。
 *
 * 退出码：0 = 全过；1 = 有失败；2 = 环境缺依赖（Playwright）。
 */
import { spawn } from 'node:child_process'
import { mkdirSync, mkdtempSync, rmSync } from 'node:fs'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { DatabaseSync } from 'node:sqlite'
import { readAttributeBounded, readTextBounded } from './lib/bounded-text.mjs'
import { issueInviteCode, openTableAndHost, registerOnGate, seatByAccount, seatByInviteCode } from './lib/entrance.mjs'
import { describeProfile, ensureServerArtifacts, extractProfileFlags, resolveProfile } from './lib/verify-profile.mjs'
import { createChecker, createSectionRunner } from './lib/verify-sections.mjs'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const webRoot = path.join(repositoryRoot, 'web')
const { flags, rest } = extractProfileFlags(process.argv.slice(2))
const options = parseArguments(rest)

/**
 * 档位（tools/lib/verify-profile.mjs）：默认迭代档（快节拍 0.3s + 不落盘截图 + 复用产物）；
 * 取证档显式传 `--quota 2 --screenshots-all`（必要时加 `--build`）。
 * 本装置**不开夜、不推进槽位**，所以节拍对它只是一次服务端往返的时间上限。
 */
const config = resolveProfile(flags, { quotaSeconds: 0.3 })

/** 段落清单：顺序即执行顺序，也是用法头里那份清单的唯一事实来源。 */
const SECTIONS = [
  { id: 'boot', title: '构建并启动真宿主（独立临时库）' },
  { id: 'front', title: '起 Vite（/hub 代理到真宿主）' },
  { id: 'open', title: '说书人开桌（5 席）并进主持台' },
  { id: 'seat', title: '玩家 A 从大厅自助入座 1 号 + 读出「公开桌」初值' },
  { id: 'access', title: '矩阵行 7：说书人切邀请制 → 在场玩家不刷新不重连就变；再切回公开' },
  { id: 'lobby', title: '矩阵行 8：邀请制桌在大厅仍然列出、标「邀请制」、席位点不动' },
  { id: 'invite', title: '矩阵行 4 正向：持邀请码者进得来（大厅「有邀请码？」）' },
  { id: 'public', title: '行 2 反方向对照：切回公开桌 → 另一个空席位在大厅点得动并坐下' },
  { id: 'traveller', title: '矩阵行 9 / 10：旅行者加入 → 申请离场 → 说书人批准 → 本人看到已离场' },
  { id: 'reject', title: '矩阵行 11：旅行者再申请 → 说书人驳回 → 本人看到驳回、没有离场' },
  { id: 'wrap', title: '收尾：浏览器控制台零错误' },
]

const runner = createSectionRunner(SECTIONS, { only: config.only, from: config.from })
const checker = createChecker({
  sections: SECTIONS,
  isJudged: (id) => runner.isJudged(id),
  currentSection: () => runner.currentId,
  slowPacer: config.slowPacer,
  screenshots: config.screenshots,
})
const check = checker.check

if (config.listSections) {
  console.log('可用段落（按执行顺序；--only 与 --from 互斥）：')
  for (const section of SECTIONS) {
    console.log(`  ${section.id.padEnd(10)} ${section.title}`)
  }

  process.exit(0)
}

/** 一次服务端往返的动作超时（注册 / 开桌 / 入座，本机实测 < 1s）。 */
const ACTION_TIMEOUT_MS = 20_000

/**
 * 断言前的有界轮询窗口：**红断言不许靠等满超时收场**（本机踩过"元素缺失白等 30s"）——
 * 8 秒够一次推送落地，又短到迭代期跑得起。超时返回最后一次读数，由 check 判红并打印它。
 */
const WINDOW_MS = 8_000

/** 席位与夹具：席位数 = 开桌时定的 5，旅行者那两席由服务端追加（6 / 7）。 */
const SEAT_COUNT = 5
const SEAT_ALICE = 1
const SEAT_OBSERVER = 3
const SEAT_LATE = 4
const SEAT_TRAVELLER = 6
const SEAT_REJECTED = 7

/** 两位旅行者的角色 slug（`web/src/display/labels.ts` 的花名册；角色唯一，必须不同）。 */
const CHARACTER_APPROVED = 'deviant'
const CHARACTER_REJECTED = 'bone-collector'

/**
 * "注册完就停在大厅"的两个夹具账号（B 与 D）。名字由本装置自己起：
 * `entrance.mjs` 的 `fixtureAccount` 是模块私有的，而这两个账号的用途是**不入座**，不属于那套。
 * 每次运行都是全新的临时库，所以固定名字不会撞。
 */
const LOBBY_OBSERVER = { username: 'ta-lobby-b', displayName: '夹具大厅客', password: 'fixture-pw-ta-lobby' }
const LATE_PLAYER = { username: 'ta-late-d', displayName: '夹具迟到者', password: 'fixture-pw-ta-late' }

/** 第一份离场申请写的理由（要能在两个页面上原样读到，才叫"理由真的过去了"）。 */
const DEPARTURE_NOTE = '装置取证：这名旅行者要离场'

/** 玩家人手一句的探针标记：整页刷新会把它清掉。 */
const LIVE_MARK = '__octTableAccessLiveMark'

const VIEWPORT_PLAYER = { width: 900, height: 1200 }
const VIEWPORT_STORYTELLER = { width: 1600, height: 1100 }

let playwright = null
try {
  // 依赖装在 web/node_modules：脚本住在 tools/，所以要显式按 web/ 解析（Node 的默认查找不会跨目录）。
  const requireFromWeb = createRequire(path.join(webRoot, 'package.json'))
  playwright = requireFromWeb('playwright')
} catch (error) {
  console.error(`缺少依赖（playwright）：${String(error)}`)
  console.error('先运行：cd web; npm install; npx playwright install chromium')
  process.exit(2)
}

console.log(`档位：${describeProfile(config)}`)
console.log(`段落选择：${runner.selectionSummary()}`)

const children = []
/** 浏览器与说书人那一页（模块级：正常收尾、`--only` 早退与异常路径都要关掉，不留孤儿）。 */
let browser = null
let storytellerPage = null

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-table-access-'))
const databasePath = path.join(workspace, 'table-access.db')
const screenshotsDir = path.resolve(repositoryRoot, 'artifacts', 'web')
mkdirSync(screenshotsDir, { recursive: true })

const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`

process.on('exit', () => killChildren())

try {
  await main()
  runner.reportTimings()
  await cleanup()
  checker.report()
  console.log(config.screenshots ? `截图：${screenshotsDir}（tableaccess-*）` : '截图：未落盘（迭代档）')
  process.exit(checker.results.some((result) => result.outcome === 'fail') ? 1 : 0)
} catch (error) {
  console.error(`\n[FAIL] 批次脚本异常终止：${error instanceof Error ? error.stack : String(error)}`)
  checker.results.push({ section: runner.currentId, label: '脚本执行到底', outcome: 'fail', detail: '见上方异常' })
  await cleanup()
  checker.report()
  process.exit(1)
}

async function main() {
  if (!runner.begin('boot')) return
  await ensureServerArtifacts({ repositoryRoot, buildMode: config.buildMode })
  await startServer()

  if (!runner.begin('front')) return
  const vite = spawn(
    process.execPath,
    [path.join(webRoot, 'node_modules', 'vite', 'bin', 'vite.js'), '--port', String(options.vitePort), '--strictPort'],
    { cwd: webRoot, env: { ...process.env, VITE_SERVER_TARGET: serverUrl }, stdio: 'ignore' },
  )
  children.push(vite)
  await waitForHttp(viteUrl, 'Vite 开发服务器', 60_000)

  if (!runner.begin('open')) return
  browser = await playwright.chromium.launch()
  const consoleErrors = []
  const host = await newPage(browser, VIEWPORT_STORYTELLER, consoleErrors)
  storytellerPage = host.page
  const table = await openTableAndHost(storytellerPage, {
    frontUrl: viteUrl,
    databasePath,
    seats: SEAT_COUNT,
    suffix: 'ta',
  })
  check(`席位票据齐备（${SEAT_COUNT} 席）`, table.seatTickets.length === SEAT_COUNT, `数据库 ${table.seatTickets.length} 张`)
  const grimoireCount = await storytellerPage.getByTestId('grimoire').count()
  check('说书人进主持台后看板可见（魔典主视图）', grimoireCount === 1, `grimoire=${grimoireCount}`)

  if (!runner.begin('seat')) return
  // A：公开桌的自助入座（既有主路径）——行 7 的"在场玩家"就是这一页。
  // 它的页面多装一个 WebSocket 探针（`socketProbe`），后面判"不刷新不重连"要用。
  const alice = await newPage(browser, VIEWPORT_PLAYER, consoleErrors, { socketProbe: true })
  const aliceAccount = await seatByAccount(alice.page, {
    frontUrl: viteUrl,
    gameId: table.gameId,
    seat: SEAT_ALICE,
    suffix: 'ta-a',
  })
  const aliceSeat = await waitForLocatorContains(
    alice.page.getByTestId('player-seat'),
    `${SEAT_ALICE} 号 · ${aliceAccount.displayName}`,
    ACTION_TIMEOUT_MS,
  )
  check(
    `A 从大厅自助入座 ${SEAT_ALICE} 号（公开桌主路径，行 2 的正向）`,
    aliceSeat.includes(`${SEAT_ALICE} 号 · ${aliceAccount.displayName}`),
    `席位标签「${aliceSeat}」`,
  )
  // 初值必须是"公开"：玩家页从**大厅列表**补全这条初始条件（不是等推送、也不是猜一个 false 写上去）。
  const aliceInitial = await waitForAttribute(
    alice.page.getByTestId('player-table-access'),
    'data-invite-only',
    'false',
    WINDOW_MS,
  )
  check(
    '入座后本桌访问模式的初值读作「公开桌」（data-invite-only="false"，初值来自大厅列表）',
    aliceInitial === 'false',
    `player-table-access[data-invite-only]=${readable(aliceInitial)}｜文案「${await playerAccessText(alice.page)}」`,
  )
  await screenshot(alice.page, 'tableaccess-01-player-public')

  if (!runner.begin('access')) return
  // ---- 矩阵行 7 的第一半：公开 → 邀请制 ----
  const armInvite = await armLiveProbe(alice.page, alice.nav)
  const hostInvite = await toggleTableAccess('true', '邀请制')
  check(
    '说书人拨到邀请制：主持台读数由 false 变 true（服务端确认后才写回界面）',
    hostInvite === 'true',
    `table-access[data-invite-only]=${readable(hostInvite)}｜文案「${await tableAccessText()}」`,
  )
  const aliceInvite = await waitForAttribute(
    alice.page.getByTestId('player-table-access'),
    'data-invite-only',
    'true',
    WINDOW_MS,
  )
  check(
    `矩阵行 7：在场玩家的读数在 ${WINDOW_MS / 1000}s 内由 false 变 true（不刷新、不重连）`,
    aliceInvite === 'true',
    `player-table-access[data-invite-only]=${readable(aliceInvite)}｜文案「${await playerAccessText(alice.page)}」`,
  )
  const liveInvite = await readLiveProbe(alice.page, alice.nav, armInvite)
  check(
    '这一变是推送来的，不是刷新的：文档标记还在（没换文档）+ 零导航 + hub WebSocket 数不变（没重连）',
    isLiveChange(liveInvite),
    describeLive('公开→邀请制', liveInvite),
  )
  check(
    '切到邀请制是**落库的事实**（Games.IsInviteOnly=1，D-0037 由 IsLocked 改名的那一列）',
    readInviteOnlyColumn(table.gameId) === 1,
    `Games.IsInviteOnly=${inviteOnlyReading(table.gameId)}`,
  )
  await screenshot(alice.page, 'tableaccess-02-player-invite-only')

  // ---- 矩阵行 7 的第二半：邀请制 → 公开（反方向，同样不许靠刷新）----
  const armPublic = await armLiveProbe(alice.page, alice.nav)
  const hostPublic = await toggleTableAccess('false', '公开桌')
  check(
    '说书人拨回公开桌：主持台读数由 true 变回 false',
    hostPublic === 'false',
    `table-access[data-invite-only]=${readable(hostPublic)}｜文案「${await tableAccessText()}」`,
  )
  const alicePublic = await waitForAttribute(
    alice.page.getByTestId('player-table-access'),
    'data-invite-only',
    'false',
    WINDOW_MS,
  )
  check(
    `矩阵行 7 反方向：在场玩家的读数变回 false（同样不刷新、不重连）`,
    alicePublic === 'false',
    `player-table-access[data-invite-only]=${readable(alicePublic)}｜文案「${await playerAccessText(alice.page)}」`,
  )
  const livePublic = await readLiveProbe(alice.page, alice.nav, armPublic)
  check(
    '反方向也是推送来的：文档标记还在 + 零导航 + hub WebSocket 数不变',
    isLiveChange(livePublic),
    describeLive('邀请制→公开', livePublic),
  )
  check(
    '拨回公开桌同样落库（Games.IsInviteOnly=0）',
    readInviteOnlyColumn(table.gameId) === 0,
    `Games.IsInviteOnly=${inviteOnlyReading(table.gameId)}`,
  )

  if (!runner.begin('lobby')) return
  // 大厅观察员：登录但**不入座**——"邀请制桌在大厅里长什么样"只有停在大厅的人才看得到。
  const observer = await newPage(browser, VIEWPORT_PLAYER, consoleErrors)
  await observer.page.goto(`${viteUrl}/play`, { waitUntil: 'domcontentloaded' })
  await registerOnGate(observer.page, LOBBY_OBSERVER)
  const observerRow = observer.page.locator(`[data-table="${table.gameId}"]`)
  await requireLocator(observerRow, 1, `大厅里这一桌的行 [data-table="${table.gameId}"]`)

  // 再切到邀请制（说书人第三次拨开关）：大厅读数需要一个邀请制桌当对象。
  const hostInviteAgain = await toggleTableAccess('true', '邀请制')
  check(
    '说书人再次切成邀请制（大厅读数的前置：开关来回拨仍然生效）',
    hostInviteAgain === 'true',
    `table-access[data-invite-only]=${readable(hostInviteAgain)}`,
  )
  // 大厅是"读一次的快照 + 一个「刷新列表」按钮"（访问模式不在大厅的推送面上），
  // 所以这里点一次刷新按钮再读——判的是"大厅如实呈现"，不是"大厅也实时推送"（后者本批没做）。
  await observer.page.getByRole('button', { name: '刷新列表' }).click()

  const observerRowInvite = await waitForAttribute(observerRow, 'data-invite-only', 'true', WINDOW_MS)
  const observerRowText = compact(await readTextBounded(observerRow))
  const observerSeats = await readSeatButtons(observerRow, table.gameId, SEAT_COUNT)
  check(
    '矩阵行 8：邀请制桌在大厅里**仍然列出**（行还在，席位数与本桌一致）',
    observerRowInvite === 'true' && observerSeats.every((entry) => entry.present),
    `行 data-invite-only=${readable(observerRowInvite)}｜席位按钮 ${observerSeats.filter((entry) => entry.present).length}/${SEAT_COUNT} 在位`,
  )
  check(
    '矩阵行 8：这一行标「邀请制」（如实呈现，不隐藏、不含糊）',
    observerRowText.includes('邀请制'),
    `行文本「${observerRowText}」`,
  )
  check(
    '矩阵行 8：席位按钮点不动（全部 disabled；空席位写明要邀请码，已占席位写明有人了）',
    observerSeats.every((entry) => entry.present && entry.disabled !== null)
      && observerSeats.every((entry) => entry.title.includes('邀请码') || entry.title.includes('已经有人了'))
      // 至少有一个**空**席位把原因指向邀请码：否则"全部点不动"可能只是因为席位都被占了（假绿）。
      && observerSeats.some((entry) => entry.title.includes('邀请码')),
    describeSeats(observerSeats),
  )
  await screenshot(observer.page, 'tableaccess-03-lobby-invite-only')

  if (!runner.begin('invite')) return
  // 矩阵行 4 的正向判据：邀请制桌里，**持邀请码的人进得来**。
  // 码由说书人**当场签发**（D-0038）：库里只有哈希，读不回来——面板那一下是唯一的来源。
  const observerCode = await issueInviteCode(storytellerPage, SEAT_OBSERVER)
  await joinByInviteCodeFromLobby(observer.page, observerCode)
  const observerSeat = await waitForLocatorContains(observer.page.getByTestId('player-seat'), `${SEAT_OBSERVER} 号`, WINDOW_MS)
  check(
    `矩阵行 4 正向：持邀请码者在大厅「有邀请码？」里入座成功（${SEAT_OBSERVER} 号，码由说书人当场签发）`,
    observerSeat.includes(`${SEAT_OBSERVER} 号`),
    `席位标签「${observerSeat}」｜用的码「${observerCode}」`,
  )
  const observerAccess = await waitForAttribute(
    observer.page.getByTestId('player-table-access'),
    'data-invite-only',
    'true',
    WINDOW_MS,
  )
  check(
    '凭邀请码进来的人看到的访问模式也是「邀请制」（初值取大厅那一行，不是猜的）',
    observerAccess === 'true',
    `player-table-access[data-invite-only]=${readable(observerAccess)}｜文案「${await playerAccessText(observer.page)}」`,
  )
  await screenshot(observer.page, 'tableaccess-04-invite-code-seat')

  if (!runner.begin('public')) return
  // 行 2 的反方向对照：切回公开桌之后，**另一个空席位在大厅点得动**（同一批按钮，前一刻是点不动的）。
  const hostBackPublic = await toggleTableAccess('false', '公开桌')
  check(
    '说书人切回公开桌（反方向对照的前置）',
    hostBackPublic === 'false',
    `table-access[data-invite-only]=${readable(hostBackPublic)}`,
  )
  const late = await newPage(browser, VIEWPORT_PLAYER, consoleErrors)
  await late.page.goto(`${viteUrl}/play`, { waitUntil: 'domcontentloaded' })
  await registerOnGate(late.page, LATE_PLAYER)
  const lateRow = late.page.locator(`[data-table="${table.gameId}"]`)
  await requireLocator(lateRow, 1, `大厅里这一桌的行 [data-table="${table.gameId}"]`)
  const lateRowInvite = await waitForAttribute(lateRow, 'data-invite-only', 'false', WINDOW_MS)
  check(
    '大厅那一行的读数也回到「公开」（data-invite-only="false"）',
    lateRowInvite === 'false',
    `行 data-invite-only=${readable(lateRowInvite)}｜行文本「${compact(await readTextBounded(lateRow))}」`,
  )
  const lateSeats = await readSeatButtons(lateRow, table.gameId, SEAT_COUNT)
  const lateTarget = lateSeats.find((entry) => entry.seat === SEAT_LATE)
  check(
    `行 2 反方向对照：${SEAT_LATE} 号席位在大厅点得动（按钮在位、没有 disabled 属性）`,
    lateTarget !== undefined && lateTarget.present && lateTarget.disabled === null,
    describeSeats(lateSeats),
  )
  await lateRow.locator(`[data-seat="${table.gameId}-${SEAT_LATE}"]`).click()
  const lateSeat = await waitForLocatorContains(late.page.getByTestId('player-seat'), `${SEAT_LATE} 号`, WINDOW_MS)
  check(
    `行 2 反方向对照：点下去真的坐上了 ${SEAT_LATE} 号（player-seat 出现）`,
    lateSeat.includes(`${SEAT_LATE} 号`),
    `席位标签「${lateSeat}」`,
  )
  await screenshot(late.page, 'tableaccess-05-public-seat')

  if (!runner.begin('traveller')) return
  // ---- 矩阵行 9 / 10：旅行者加入（服务端追加席位 + 签发邀请码）→ 申请 → 批准 ----
  const issued = await addTraveller(CHARACTER_APPROVED, SEAT_TRAVELLER)
  check(
    `说书人在「旅行者」区块加入旅行者：服务端追加 ${SEAT_TRAVELLER} 号席并签发邀请码（面板上转交那一串）`,
    issued.seat === String(SEAT_TRAVELLER) && issued.code.length > 0,
    `traveller-issued[data-seat]=${readable(issued.seat)}｜转交的码「${issued.code}」`,
  )
  // D-0038：面板转交的那一串**不会落在库里**——把"库里没有可用明文"变成一次真机读数。
  const travellerSecret = issued.code.slice(issued.code.indexOf(':') + 1)
  const stored = readInvitationStorage(table.gameId)
  check(
    '库里读不到这一串明文（席位名单与凭据表里都只有席位号与哈希，D-0038）',
    travellerSecret.length > 0 && !stored.includes(travellerSecret),
    `面板「${issued.code}」｜库里两列的原文都不含它（长度 ${stored.length}）`,
  )

  const traveller = await newPage(browser, VIEWPORT_PLAYER, consoleErrors)
  await seatByInviteCode(traveller.page, { frontUrl: viteUrl, code: issued.code, suffix: 'ta-t1' })
  const travellerSeat = await waitForLocatorContains(
    traveller.page.getByTestId('player-seat'),
    `${SEAT_TRAVELLER} 号`,
    WINDOW_MS,
  )
  check(
    `旅行者凭这串码入座 ${SEAT_TRAVELLER} 号（新账号 + 邀请码那条既有路径）`,
    travellerSeat.includes(`${SEAT_TRAVELLER} 号`),
    `席位标签「${travellerSeat}」`,
  )
  const requestVisible = await waitForCount(traveller.page.getByTestId('departure-request'), 1, WINDOW_MS)
  check(
    '这一席拿到「申请离场」入口（权限位只给在局的旅行者，普通角色拿不到）',
    requestVisible,
    `departure-request 条数=${await traveller.page.getByTestId('departure-request').count()}`,
  )
  await traveller.page.getByTestId('departure-note').fill(DEPARTURE_NOTE)
  await traveller.page.getByTestId('departure-request').click()
  const pendingText = await waitForLocatorContains(
    traveller.page.getByTestId('departure-pending'),
    '等说书人裁定',
    WINDOW_MS,
  )
  check(
    '矩阵行 9：本人页面出现等待态 departure-pending（并带自己写的理由）',
    pendingText.includes('等说书人裁定') && pendingText.includes(DEPARTURE_NOTE),
    `等待态文案「${pendingText}」`,
  )
  await screenshot(traveller.page, 'tableaccess-06-traveller-pending')

  const approveRow = storytellerPage.locator(
    `[data-testid="traveller-departures"] [data-departure-seat="${SEAT_TRAVELLER}"]`,
  )
  const approveRowSeen = await waitForCount(approveRow, 1, WINDOW_MS)
  const approveRowText = approveRowSeen ? compact(await readTextBounded(approveRow)) : ''
  check(
    `矩阵行 9：说书人页面上出现待批行 [data-departure-seat="${SEAT_TRAVELLER}"]（在 traveller-departures 里，带理由）`,
    approveRowSeen && approveRowText.includes(DEPARTURE_NOTE),
    approveRowSeen ? `待批行「${approveRowText}」` : `${WINDOW_MS / 1000}s 内没有出现待批行（traveller-departures 条数=${await storytellerPage.getByTestId('traveller-departures').count()}）`,
  )
  await screenshot(storytellerPage, 'tableaccess-07-storyteller-departures')
  // 裁定按钮**逐行重复**：必须先按 `data-departure-seat` 定位到行，再点行内那一个。
  await approveRow.getByTestId('departure-approve').click()
  const departedText = await waitForLocatorContains(traveller.page.getByTestId('player-departed'), '已经离场', WINDOW_MS)
  check(
    '矩阵行 10：说书人点「批准」→ 旅行者页面出现 player-departed（座位已从本局移除）',
    departedText.includes('已经离场'),
    `已离场文案「${departedText}」`,
  )
  await screenshot(traveller.page, 'tableaccess-08-player-departed')
  const requestGone = await waitForCount(traveller.page.getByTestId('departure-request'), 0, WINDOW_MS)
  check(
    '批准之后本人不能再申请（申请入口撤下：已经离场的席位没有可再申请的离场）',
    requestGone,
    `departure-request 条数=${await traveller.page.getByTestId('departure-request').count()}`,
  )

  if (!runner.begin('reject')) return
  // ---- 矩阵行 11：第二条申请走驳回（同一席位不能再申请，所以第二条由另一位旅行者提出）----
  const issuedRejected = await addTraveller(CHARACTER_REJECTED, SEAT_REJECTED)
  check(
    `说书人再加入第二名旅行者（另一个角色）：追加 ${SEAT_REJECTED} 号席并签发邀请码`,
    issuedRejected.seat === String(SEAT_REJECTED) && issuedRejected.code.length > 0,
    `traveller-issued[data-seat]=${readable(issuedRejected.seat)}｜转交的码「${issuedRejected.code}」`,
  )
  // D-0038：**重新签发即轮换**——同一席再签一枚，上一枚当场失效（码发错人时的收场动作）。
  const rotatedRejected = await issueInviteCode(storytellerPage, SEAT_REJECTED)
  check(
    `重新签发 ${SEAT_REJECTED} 号席的邀请码：新的一枚与上一枚不同（轮换，旧的当场作废）`,
    rotatedRejected !== issuedRejected.code && rotatedRejected.startsWith(`${table.gameId}:`),
    `上一枚「${issuedRejected.code}」｜新的一枚「${rotatedRejected}」`,
  )
  const rejected = await newPage(browser, VIEWPORT_PLAYER, consoleErrors)
  await seatByInviteCode(rejected.page, { frontUrl: viteUrl, code: rotatedRejected, suffix: 'ta-t2' })
  await requireLocator(
    rejected.page.getByTestId('departure-request'),
    1,
    `第二名旅行者的「申请离场」入口（${SEAT_REJECTED} 号）`,
  )
  // 这一条**不写理由**（`departure-note` 是可选字段，这是默认路径）。
  // 首跑时它在本人页面上看不到等待态——`PendingDepartureNote` 为 null，而界面拿"非 null"当待批判据，
  // 于是整块离场区直接消失。那是一条真缺陷，已修（本人视图加了 `HasPendingDeparture`）：
  // 现在**两条路径都要看到等待态**，这里连同说书人那一行的「（没有写理由）」一起判。
  await rejected.page.getByTestId('departure-request').click()

  const rejectedPendingSeen = await waitForCount(
    rejected.page.getByTestId('departure-pending'),
    1,
    WINDOW_MS,
  )
  check(
    '矩阵行 11 前置：**没写理由**的申请同样让本人在页面上看到等待态（默认路径，曾经看不到）',
    rejectedPendingSeen,
    `departure-pending 条数=${await rejected.page.getByTestId('departure-pending').count()}`,
  )

  const rejectRow = storytellerPage.locator(
    `[data-testid="traveller-departures"] [data-departure-seat="${SEAT_REJECTED}"]`,
  )
  const rejectRowSeen = await waitForCount(rejectRow, 1, WINDOW_MS)
  const rejectRowText = rejectRowSeen ? compact(await readTextBounded(rejectRow)) : ''
  check(
    `矩阵行 11：第二条申请到达说书人页面（[data-departure-seat="${SEAT_REJECTED}"] 行），没写理由时如实写明`,
    rejectRowSeen && rejectRowText.includes('没有写理由'),
    rejectRowSeen ? `待批行「${rejectRowText}」` : `${WINDOW_MS / 1000}s 内没有出现待批行（traveller-departures 条数=${await storytellerPage.getByTestId('traveller-departures').count()}）`,
  )
  await screenshot(storytellerPage, 'tableaccess-09-storyteller-second-request')
  await rejectRow.getByTestId('departure-reject').click()
  const rulingApproved = await waitForAttribute(
    rejected.page.getByTestId('departure-ruling'),
    'data-approved',
    'false',
    WINDOW_MS,
  )
  const rulingText = compact(await readTextBounded(rejected.page.getByTestId('departure-ruling')))
  check(
    '矩阵行 11：说书人点「驳回」→ 本人页面出现 departure-ruling 且 data-approved="false"',
    rulingApproved === 'false' && rulingText.includes('驳回'),
    `departure-ruling[data-approved]=${readable(rulingApproved)}｜文案「${rulingText}」`,
  )
  // 反例观察窗：驳回之后**不该**出现离场。给推送留一个落地窗口再读，读到就是真红（不是"还没到"）。
  await sleep(400)
  const rejectedDeparted = await rejected.page.getByTestId('player-departed').count()
  check(
    '驳回不离场：本人页面上**没有** player-departed（本局继续，席位还在）',
    rejectedDeparted === 0,
    `player-departed 条数=${rejectedDeparted}`,
  )
  const rejectedCanAskAgain = await waitForCount(rejected.page.getByTestId('departure-request'), 1, WINDOW_MS)
  check(
    '驳回结清了那条申请：申请入口回到可点（本局继续，之后还能再申请）',
    rejectedCanAskAgain,
    `departure-request 条数=${await rejected.page.getByTestId('departure-request').count()}`,
  )
  await screenshot(rejected.page, 'tableaccess-10-departure-rejected')

  if (!runner.begin('wrap')) return
  check('浏览器控制台没有报错', consoleErrors.length === 0, consoleErrors.slice(0, 3).join(' | ') || '零错误')
}

/**
 * 说书人拨一次访问模式开关，等主持台读数落到期望值。
 *
 * 读数取 `table-access[data-invite-only]`：它是**服务端确认之后**由子组件写回父组件的值
 * （`update:inviteOnly`），所以这一条既是"按钮点了"，也是"服务端认了这个新值"。
 *
 * @returns {Promise<string|null>} 读到的值；超时返回最后一次读数（由断言判红并打印）。
 */
async function toggleTableAccess(expected, label) {
  await storytellerPage.getByTestId('table-access-toggle').click()
  const value = await waitForAttribute(storytellerPage.getByTestId('table-access'), 'data-invite-only', expected, WINDOW_MS)
  if (value !== expected) {
    console.log(`  [诊断] 拨到「${label}」没有生效：期望 ${expected}，读到 ${readable(value)}`)
  }

  return value
}

/** 主持台访问模式那一行的文案（读数的一部分：`invite-only` 与人话要对得上）。 */
function tableAccessText() {
  return readTextBounded(storytellerPage.getByTestId('table-access-summary')).then(compact)
}

/** 玩家页访问模式那一行的文案。 */
function playerAccessText(page) {
  return readTextBounded(page.getByTestId('player-table-access')).then(compact)
}

/**
 * 说书人在「旅行者」区块加入一名旅行者：**席位留空** = 服务端追加新席位并签发新票据，
 * 票据就是面板上「新席位 N 的邀请码」那一串（`桌标识:席位票据`）。
 *
 * @returns {Promise<{seat: string|null, code: string, text: string}>} `data-seat` 读数、转交的邀请码、整行文本。
 */
async function addTraveller(character, expectedSeat) {
  await storytellerPage.getByTestId('traveller-character').selectOption(character)
  await storytellerPage.getByTestId('traveller-join').click()
  const issued = storytellerPage.getByTestId('traveller-issued')
  const seat = await waitForAttribute(issued, 'data-seat', String(expectedSeat), WINDOW_MS)
  const text = compact(await readTextBounded(issued))
  // 邀请码是这一行的最后一个"词"（模板里就是 `<span class="mono">{{ inviteCode }}</span>`）；
  // 取不到空串也算读数——后面那条"与库里的票据对读"会把空串判红。
  return { seat, code: text.length === 0 ? '' : (text.split(/\s+/).pop() ?? ''), text }
}

/**
 * 在**已经登录、停在大厅**的那一页里凭邀请码入座（矩阵行 4 的正向）。
 *
 * 为什么不复用 `seatByInviteCode`：那个 helper 的契约是"打开登录卡 → 注册 / 登录 → 粘码"，
 * 而这里的玩家**已经在自己的大厅里**（同一页、没有整页刷新）——这正是"大厅点不动时还有一条路"的
 * 真实形态；拿 helper 去点登录页签只会等不到元素。两条路用的是同一组锚点
 * （`seat-invite` / `seat-invite-code` / `seat-invite-join` / `player-seat`）。
 */
async function joinByInviteCodeFromLobby(page, code) {
  const invite = page.getByTestId('seat-invite')
  await requireLocator(invite, 1, '大厅里的「有邀请码？」折叠块（seat-invite）')
  if ((await readAttributeBounded(invite, 'open')) === null) {
    await invite.locator('summary').click()
  }

  await page.getByTestId('seat-invite-code').fill(code)
  await page.getByTestId('seat-invite-join').click()
}

/**
 * 大厅里一行桌的席位按钮读数：**在不在 / 点不点得动 / 悬停说什么**。
 *
 * `disabled` 用属性在场与否判（Vue 的 `:disabled` 落到属性上）：读到 `''` = 点了不动，
 * 读到 `null` = 点得动（也可能是按钮根本不在——所以 `present` 必须一起看，别把"读不到"当空值）。
 */
async function readSeatButtons(row, gameId, seatCount) {
  const entries = []
  for (let seat = 1; seat <= seatCount; seat += 1) {
    const button = row.locator(`[data-seat="${gameId}-${seat}"]`)
    entries.push({
      seat,
      present: (await button.count().catch(() => 0)) === 1,
      disabled: await readAttributeBounded(button, 'disabled'),
      title: (await readAttributeBounded(button, 'title')) ?? '',
    })
  }

  return entries
}

/** 席位读数 → 一行可读文本（断言失败时它就是"当时的读数"）。 */
function describeSeats(entries) {
  return entries
    .map(
      (entry) =>
        `${entry.seat} 号 ${
          entry.present ? (entry.disabled === null ? '点得动' : '点不动(disabled)') : '按钮缺失'
        }（${entry.title || '没有提示'}）`,
    )
    .join(' · ')
}

/**
 * 给一个浏览器上下文装"这个页面开过几条 WebSocket"的观测点（**装置自用，不进产品代码**）。
 *
 * 为什么数 WebSocket：SignalR 的自动重连会**新建**一条 WebSocket，于是"推送把界面改了"与
 * "重连之后重新取快照把界面改了"在读数上分得开。Vite 的 HMR 也是 WebSocket，读取时按 `/hub/` 过滤。
 */
function installSocketProbe(context) {
  return context.addInitScript(() => {
    window.__octSocketUrls = []
    const Native = window.WebSocket
    window.WebSocket = class extends Native {
      // 用 `...args` 原样转发而不是补一个 `protocols`：signalr 在浏览器里是 `new WebSocket(url)` 一个参数，
      // 显式补 `undefined` 当第二个参数会被 Chrome 拒（源码里就有一句"Chrome is not happy about that"）。
      constructor(...args) {
        super(...args)
        window.__octSocketUrls.push(String(args[0]))
      }
    }
  })
}

/** 这一页开过的 **hub** WebSocket 条数（读不到记 0；判据里另有"至少一条"的要求，不会因此假绿）。 */
function hubSocketCount(page) {
  return page
    .evaluate(() => (window.__octSocketUrls ?? []).filter((url) => String(url).includes('/hub/')).length)
    .catch(() => 0)
}

/** 拨开关**之前**记下"这一刻的活体状态"：文档标记 + 导航次数 + hub 连接数。 */
async function armLiveProbe(page, nav) {
  await page.evaluate((mark) => {
    window[mark] = 'kept'
  }, LIVE_MARK)
  return { frames: nav.frames, loads: nav.loads, sockets: await hubSocketCount(page) }
}

/** 与 `armLiveProbe` 对读：标记还在 = 文档没被换掉；导航增量为 0 = 没跳转；hub 连接数不变 = 没重连。 */
async function readLiveProbe(page, nav, baseline) {
  const mark = await page
    .evaluate((name) => window[name] ?? null, LIVE_MARK)
    .catch(() => null)
  return {
    mark,
    frames: nav.frames - baseline.frames,
    loads: nav.loads - baseline.loads,
    sockets: await hubSocketCount(page),
    socketsBefore: baseline.sockets,
  }
}

/**
 * "没刷新、没重连"的判据：三件事同时成立。
 * 最后那条 `sockets >= 1` 是**防假绿**：若这一页压根没走 WebSocket 传输，前三条会平凡成立，
 * 那时"没重连"其实没有被观测到——宁可判红并打印读数，也不给一个空转的绿灯。
 */
function isLiveChange(probe) {
  return (
    probe.mark === 'kept'
    && probe.frames === 0
    && probe.loads === 0
    && probe.sockets === probe.socketsBefore
    && probe.sockets >= 1
  )
}

function describeLive(label, probe) {
  return `${label}：文档标记=${probe.mark ?? '丢了（文档被换过）'}｜新增导航 framed=${probe.frames} load=${probe.loads}｜hub 连接 ${probe.socketsBefore}→${probe.sockets}`
}

/** 库里与邀请码有关的两列**原文**：席位名单（只剩席位号）+ 凭据表（只有哈希）。 */
function readInvitationStorage(gameId) {
  const database = new DatabaseSync(databasePath)
  try {
    const seats = database.prepare('SELECT SeatsJson FROM Games WHERE GameId = ?').get(gameId)?.SeatsJson ?? ''
    const hashes = database.prepare('SELECT Hash FROM SeatInvitations WHERE GameId = ?').all(gameId)
    return `${seats}\n${hashes.map((row) => String(row.Hash)).join('\n')}`
  } finally {
    database.close()
  }
}

/**
 * 库里这一桌的访问模式（`Games.IsInviteOnly`，D-0037 由 `IsLocked` 原地改名的那一列）。
 *
 * 为什么要读它：界面读数只证明"这一刻屏幕上是对的"，这一列证明**切换是落库的事实**
 * （重启之后还是同一形态）。读不到返回 null，由断言判红。
 */
function readInviteOnlyColumn(gameId) {
  const database = new DatabaseSync(databasePath, { readOnly: true })
  try {
    const row = database.prepare('SELECT IsInviteOnly FROM Games WHERE GameId = ?').get(gameId)
    return row === undefined ? null : Number(row.IsInviteOnly)
  } finally {
    database.close()
  }
}

/** 库读数的可读文本（`null` = 读不到）。 */
function inviteOnlyReading(gameId) {
  const value = readInviteOnlyColumn(gameId)
  return value === null ? '读不到' : String(value)
}

/** `null` → 「读不到」，其余原样（`''` 也要看得见，它是 disabled 属性的读数）。 */
function readable(value) {
  return value === null ? '读不到' : `"${value}"`
}

async function newPage(browserInstance, viewport, consoleErrors, options = {}) {
  const context = await browserInstance.newContext({ viewport })
  if (options.socketProbe === true) {
    await installSocketProbe(context)
  }

  const page = await context.newPage()
  /** 导航计数（判"没刷新"用）：整页导航与 `load` 各数一次。 */
  const nav = { frames: 0, loads: 0 }
  page.on('framenavigated', () => {
    nav.frames += 1
  })
  page.on('load', () => {
    nav.loads += 1
  })
  page.on('console', (message) => {
    if (message.type() === 'error') {
      consoleErrors.push(message.text())
    }
  })
  page.on('pageerror', (error) => consoleErrors.push(String(error)))
  return { page, nav }
}

/**
 * 有界轮询：等属性落到期望值。
 * @returns {Promise<string|null>} 命中即返回；超时返回最后一次读数（`null` = 元素或属性都读不到）。
 */
async function waitForAttribute(locator, name, expected, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  for (;;) {
    const value = await readAttributeBounded(locator, name)
    if (value === expected || Date.now() >= deadline) {
      return value
    }

    await sleep(120)
  }
}

/** 有界轮询：等定位器数量达到 expected（"出现了"与"确实没有"两种判定共用）；超时返回是否达到。 */
async function waitForCount(locator, expected, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  for (;;) {
    if ((await locator.count().catch(() => 0)) === expected) {
      return true
    }

    if (Date.now() >= deadline) {
      return false
    }

    await sleep(120)
  }
}

/** 动作前置守卫：等元素到位；等不到立刻抛错并带读数（不让后续 `fill` / `click` 白等 Playwright 的 30s）。 */
async function requireLocator(locator, expected, label) {
  if (await waitForCount(locator, expected, ACTION_TIMEOUT_MS)) {
    return
  }

  throw new Error(`等不到元素（${label}）：期望 ${expected} 个，实际 ${await locator.count().catch(() => 0)}`)
}

/** 轮询直到 locator 文本包含 needle；超时返回最后一次读到的文本（由断言判红）。 */
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
      GameServer__SeatCount: String(SEAT_COUNT),
      GameServer__SlotQuotaSeconds: String(config.quotaSeconds),
      GameServer__PacerIntervalMilliseconds: '200',
      DOTNET_ENVIRONMENT: 'Production',
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  })
  // 宿主日志只在异常时才有用：留着尾部几行，诊断时打印（本装置不吃它做判定）。
  const log = []
  child.stdout.on('data', (chunk) => log.push(String(chunk)))
  child.stderr.on('data', (chunk) => log.push(String(chunk)))
  children.push(child)
  try {
    await waitForHttp(`${serverUrl}/healthz`, '宿主 /healthz', 90_000)
  } catch (error) {
    // 起不来的原因在宿主日志里（迁移失败 / 端口被占 / 库路径不可写）：尾部几行原样打出来再抛。
    console.error(`  宿主日志尾部：\n${log.join('').split(/\r?\n/).slice(-12).join('\n')}`)
    throw error
  }

  return child
}

function parseArguments(argv) {
  const parsed = { port: 5423, vitePort: 5303 }
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

/** 收尾：关浏览器 → 停宿主 / Vite → 删本装置自己的临时库目录（正常收尾与异常路径共用，幂等）。 */
async function cleanup() {
  if (browser !== null) {
    const closing = browser
    browser = null
    storytellerPage = null
    await closing.close().catch(() => {})
  }

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

/** 等自己起的子进程真的退出（宿主写完了库才删目录，不然 Windows 上删不掉）。 */
async function waitForChildrenExit(timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if (children.every((child) => child.exitCode !== null || child.pid === undefined)) {
      return
    }

    await sleep(150)
  }
}

function sleep(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds))
}
