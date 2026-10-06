/**
 * 屠夫额外提名落靶 / 二次处决真机夹具 —— 票据 docs/backlog/todo/butcher-second-execution-fixture.md（E34 残余）。
 *
 * 它回答：**「当日首次处决 → 屠夫窗口 → 额外提名收票达线 → 第二次处决」这条落靶路径，
 * 在真宿主 + 真界面上一路跑得通吗？它对当日处决账与窗口账的收口对不对？**
 * 场景（6 席 = 5 固定角色 + 1 名屠夫旅行者；独立一局，不动主装置 day1 之后的夜晚剧情）：
 *   1) 配板：1 钟表匠 / 2 筑梦师 / 3 艺术家 / 4 呆瓜 / 5 诺-达希 → 开首夜 → 钟表匠裁定
 *      + 筑梦师请求强制作废 → 走完首夜；
 *   2) 开白天 → 加入屠夫（第 6 席，善良）→ 1 号提名 2 号 → 3 票达线（6 席半数）→ 计票 →
 *      首次关账：2 号死亡 + 屠夫窗口打开（R-0050），白天保持进行中；仅屠夫本人有额外提名入口，
 *      非授予席位（4 号）绕开 UI 直调 Hub 会被显式拒绝（行 1 的服务端面）；
 *   3) 屠夫额外提名 1 号 → 收票 3 票（5 席存活只需半数，不比前次多）→ 计票 →
 *      1 号进入「即将被处决」（落靶）；
 *   4) 第二次关账：1 号真实死亡、白天结束、窗口标记已用掉（当日不再开窗）；
 *      事件流复核：当日处决账两次（2 号 → 1 号）、窗口事件一次、额外提名一次、关账一次。
 *
 * 与主装置的分工（票据验收矩阵）：主装置走「额外提名 1 票不落靶 → 无二次处决」并断言窗口翻
 * Used（行 3 回归；2026-10-04 本批把该断言从"元素仍在"收紧为"状态=Used"）；本装置独立一局，
 * 只跑落靶 / 二次处决与行 1 的入口 / 越权面；行 3 的本次运行证据来自主装置，批次记录如实写范围。
 *
 * 实测（2026-10-04 默认迭代档）：24.5s / 39 项——固定开销在 4 个浏览器上下文 + 完整首夜 + 两轮收票。
 *
 * 前置：Node >= 22.5（node:sqlite）、本机已构建 web/node_modules（playwright）。
 * 用法（在仓库根运行；默认迭代档 = 快节拍 + 不落盘截图 + 复用产物）：
 *   node tools/verify-butcher.mjs                                        # 迭代档
 *   node tools/verify-butcher.mjs --quota 2 --screenshots-all --build    # 取证档（一批一次）
 *   node tools/verify-butcher.mjs --port 5418 --vite-port 5298           # 自定端口
 *
 * 外部耦合（换机器先核对 web/AGENTS.md §3.1）：宿主编译产物路径、SQLite 表 Games 的
 * SeatsJson 列形状（席位票据仍直读库；说书人身份已改走账号，见 D-0027）、
 * Events 表 Type / Payload 列形状（SeatId 序列化为 { value: N }）。
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
import { openTableAndHost, seatByAccount, seatByInviteCode } from './lib/entrance.mjs'
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

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-butcher-'))
const databasePath = path.join(workspace, 'butcher.db')
const screenshotsDir = path.resolve(repositoryRoot, 'artifacts', 'web')
mkdirSync(screenshotsDir, { recursive: true })

const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`
/** Hub 地址：桌标识在开桌之后才定得下来，所以这里是 `let`（见下面的赋值）。 */
let hubUrl = `${serverUrl}/hub/game`

/** 五席基础花名册（与集成 ButcherHostTests.FiveAssignments 同款：夜晚契约齐备、不卡建表）。 */
const ASSIGN = ['clockmaker', 'dreamer', 'artist', 'klutz', 'no-dashii']
/** 席位号从花名册顺序派生：调换 ASSIGN 顺序时断言跟着走，不靠人工同步。 */
const seatOf = (slug) => ASSIGN.indexOf(slug) + 1
const CLOCKMAKER_SEAT = seatOf('clockmaker')
const DREAMER_SEAT = seatOf('dreamer')
const ARTIST_SEAT = seatOf('artist')
const KLUTZ_SEAT = seatOf('klutz')
/** 屠夫由说书人在白天加入：追加为第 6 席（ASSIGN 之后）。 */
const BUTCHER_SEAT = ASSIGN.length + 1

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
  console.log('=== 1/6 构建并启动真宿主（独立临时库，5 席；屠夫白天追加为第 6 席）===')
  await ensureServerArtifacts({ repositoryRoot, buildMode: config.buildMode })
  await startServer()

  console.log('=== 2/6 起 Vite ===')

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

  console.log('=== 3/6 说书人开一桌并进主持台（账号身份，D-0027）+ 投票席（1 / 3 号）入座 ===')
  const browser = await playwright.chromium.launch()
  const consoleErrors = []
  const storyteller = await newPage(browser, { width: 1600, height: 1100 }, consoleErrors)
  // 说书人：注册夹具账号 → 开一桌 → 进主持台（票据退场后这是唯一路径，也是最贴近真实用法的那条）。
  const table = await openTableAndHost(storyteller, {
    frontUrl: viteUrl,
    serverUrl,
    databasePath,
    seats: ASSIGN.length,
    suffix: 'butcher',
  })
  const seatTickets = table.seatTickets
  // 桌标识属于连接（D-0027 之后不声明就被拒）：线级探针也连到这一桌。
  hubUrl = table.hubUrl
  check('席位票据齐备（5 席）', seatTickets.length === ASSIGN.length, `数据库 ${seatTickets.length} 张`)
  check('说书人加入后看板可见（魔典主视图）', (await storyteller.locator('[data-testid="grimoire"]').count()) === 1)

  const players = new Map()
  const clockmakerJoined = await joinSeatPage(players, browser, consoleErrors, table.gameId, CLOCKMAKER_SEAT)
  check(`1 号（钟表匠）玩家页加入成功`, clockmakerJoined.badgeText.includes(`${CLOCKMAKER_SEAT} 号`), clockmakerJoined.badgeText)
  const artistJoined = await joinSeatPage(players, browser, consoleErrors, table.gameId, ARTIST_SEAT)
  check(`3 号（艺术家）玩家页加入成功（无关玩家 / 公开面见证席）`, artistJoined.badgeText.includes(`${ARTIST_SEAT} 号`), artistJoined.badgeText)

  console.log('=== 4/6 配板 → 首夜（钟表匠裁定 + 筑梦师作废）→ 开白天 ===')
  const assignmentSelects = storyteller.locator('section', { hasText: '开局分配' }).locator('select')
  for (const [index, slug] of ASSIGN.entries()) {
    await assignmentSelects.nth(index).selectOption(slug)
  }

  const assigned = await runCommand(storyteller, '分配', () => storyteller.getByRole('button', { name: '提交分配' }).click())
  check('分配 5 个角色被受理', assigned.kind === 'Accepted', assigned.raw)

  const nightStarted = await runCommand(storyteller, '开夜', () => storyteller.getByRole('button', { name: /开夜/ }).click())
  check('开夜被受理（真实顺序表建表）', nightStarted.kind === 'Accepted', nightStarted.raw)

  const clockmakerDecision = await waitForDecision(storyteller, (text) => text.includes('钟表匠'), 60_000)
  check('首夜：钟表匠槽位给说书人裁定点', clockmakerDecision.length > 0, compact(clockmakerDecision).slice(0, 160))
  const clockmakerSettled = await settleFreeDecision(storyteller, '夹具：本夜最小距离 1')
  check('首夜：钟表匠裁定被受理', clockmakerSettled.kind === 'Accepted', clockmakerSettled.raw)

  // 筑梦师的请求与屠夫落靶路径无关：强制作废即可，不必在夹具里走信息裁定。
  const dreamerVoided = await forceVoidPending(storyteller, 'StorytellerForce', '夹具：与屠夫落靶路径无关')
  check('首夜：筑梦师请求被强制作废', dreamerVoided.kind === 'Accepted', dreamerVoided.raw)

  const nightFinished = await finishNightQuickly(storyteller, '首夜')
  check(
    '首夜：剩余槽位走完（可开白天）',
    nightFinished.completed === true,
    nightFinished.natural ? '自然窗口内走完' : `强推 ${nightFinished.forced} 步${nightFinished.note ? `（${nightFinished.note}）` : ''}`,
  )

  const dayStarted = await runCommand(storyteller, '开白天', () => storyteller.getByTestId('st-start-day').click())
  check('开白天被受理', dayStarted.kind === 'Accepted', dayStarted.raw)
  const dayPanel = storyteller.getByTestId('st-day')
  const dayOpen = await waitForAttribute(dayPanel, 'data-day-status', 'Open', 30_000)
  check('说书人面板进入「白天进行中」', dayOpen === 'Open', `data-day-status=${dayOpen}`)

  console.log('=== 5/6 加入屠夫 → 首次处决开窗 → 额外提名达线 → 二次处决 ===')
  // —— 屠夫加入：说书人签发第 6 席票据（签发票据本身仍被断言），新玩家登录后从大厅挑这个空席位 ——
  await storyteller.getByTestId('traveller-character').selectOption('butcher')
  await storyteller.getByTestId('traveller-alignment').selectOption('Good')
  await storyteller.getByTestId('traveller-seat').fill('')
  const butcherJoinedOutcome = await runCommand(storyteller, '加入屠夫', () => storyteller.getByTestId('traveller-join').click())
  check('加入屠夫被受理', butcherJoinedOutcome.kind === 'Accepted', butcherJoinedOutcome.raw)

  const issued = storyteller.getByTestId('traveller-issued')
  const issuedSeat = await waitForAttribute(issued, 'data-seat', String(BUTCHER_SEAT), 20_000)
  const inviteCode = (await issued.locator('.mono').innerText()).trim()
  check(
    `加入屠夫签发第 ${BUTCHER_SEAT} 席与邀请码`,
    issuedSeat === String(BUTCHER_SEAT) && inviteCode.startsWith(`${table.gameId}:`),
    `seat=${issuedSeat}；邀请码=${inviteCode.slice(0, 14)}…`,
  )

  // 屠夫是**中途到场**的旅行者：这一桌已经开局，大厅席位按钮点不动，他走"有邀请码？"那条路。
  const butcherTravellerPage = await newPage(browser, { width: 900, height: 1100 }, consoleErrors)
  await seatByInviteCode(butcherTravellerPage, {
    frontUrl: viteUrl,
    code: inviteCode,
    suffix: 'butcher-traveller',
  })
  players.set(BUTCHER_SEAT, butcherTravellerPage)
  const butcherBadgeText = (
    await butcherTravellerPage.locator('[data-testid="player-seat"]').innerText()
  ).trim()
  check(`屠夫凭邀请码加入成功（${BUTCHER_SEAT} 号）`, butcherBadgeText.includes(`${BUTCHER_SEAT} 号`), butcherBadgeText)

  // —— 首次常规提名：1 号提 2 号；1 / 3 / 6 号各举一次手 = 3 票（6 席存活恰达半数）——
  const nominationList = storyteller.getByTestId('st-day-nominations')
  const clockmakerPage = players.get(CLOCKMAKER_SEAT)
  await clockmakerPage.getByTestId('player-nominee-select').selectOption(String(DREAMER_SEAT))
  await clockmakerPage.getByTestId('player-nominate').click()
  const nominationCountOne = await waitForAttribute(nominationList, 'data-nomination-count', '1', 30_000)
  check('首次提名进入公开账目', nominationCountOne === '1', `data-nomination-count=${nominationCountOne}`)

  // 收票节拍：倒计时 2s / 逐席 0.5s（与主装置同款，缩短真机固定开销）。
  await storyteller.getByTestId('st-sweep-countdown').fill('2')
  await storyteller.getByTestId('st-sweep-interval').fill('0.5')
  const firstSweep = await runCommand(storyteller, '首次收票', () => storyteller.getByTestId('st-start-vote-sweep').click())
  check('首次提名开始收票被受理', firstSweep.kind === 'Accepted', firstSweep.raw)
  for (const seat of [CLOCKMAKER_SEAT, ARTIST_SEAT, BUTCHER_SEAT]) {
    await players.get(seat).getByTestId('player-vote-yes').click()
  }

  const firstNomination = nominationList.locator('li').first()
  const firstVotes = await waitForAttribute(firstNomination, 'data-nomination-votes', '3', 30_000)
  const firstSweepDone = await waitForAttribute(dayPanel, 'data-sweep-phase', 'AwaitingCount', 30_000)
  check(
    '首次收票冻结 3 票（6 席半数达线）',
    firstVotes === '3' && firstSweepDone === 'AwaitingCount',
    `票数=${firstVotes}；相位=${firstSweepDone}`,
  )

  const firstCount = await runCommand(storyteller, '首次计票', () => storyteller.getByTestId('st-count-votes').click())
  check('首次计票被受理', firstCount.kind === 'Accepted', firstCount.raw)
  const aboutToBeExecuted = storyteller.getByTestId('st-about-to-be-executed')
  const aboutOne = await waitForAttribute(aboutToBeExecuted, 'data-seat', String(DREAMER_SEAT), 30_000)
  check('2 号进入「即将被处决」', aboutOne === String(DREAMER_SEAT), `data-seat=${aboutOne}`)

  const firstClose = await runCommand(storyteller, '首次结束白天并处决', () => storyteller.getByTestId('st-close-day').click())
  check('首次结束白天被受理', firstClose.kind === 'Accepted', firstClose.raw)
  const dreamerLife = await waitForAttribute(cardOf(storyteller, DREAMER_SEAT), 'data-life', 'Dead', 20_000)
  const firstExecutedSeat = await waitForAttribute(storyteller.getByTestId('st-executed'), 'data-seat', String(DREAMER_SEAT), 20_000)
  check(
    '首次处决：处决事实入当日账（st-executed=2 号）且死亡另记（牌面 Dead）',
    dreamerLife === 'Dead' && firstExecutedSeat === String(DREAMER_SEAT),
    `处决=${firstExecutedSeat}；牌面=${dreamerLife}`,
  )

  // —— 行 1：窗口公开、白天保持 Open、只有屠夫本人有额外提名入口 ——
  const windowRow = storyteller.getByTestId('st-extra-nomination')
  const windowSeat = await waitForAttribute(windowRow, 'data-seat', String(BUTCHER_SEAT), 20_000)
  const windowStatus = await waitForAttribute(windowRow, 'data-status', 'Open', 20_000)
  const dayStillOpen = await waitForAttribute(dayPanel, 'data-day-status', 'Open', 20_000)
  check(
    '行 1：首次处决后屠夫窗口公开、白天保持进行中（R-0050）',
    windowSeat === String(BUTCHER_SEAT) && windowStatus === 'Open' && dayStillOpen === 'Open',
    `窗口=${windowSeat}/${windowStatus}；白天=${dayStillOpen}`,
  )
  await screenshot(storyteller, 'butcher-01-window-open')

  // 先确认无关玩家页已收到这次处决的公开事实（页面新鲜），阴性对照才不是"页面还没更新"的假绿。
  const witnessExecuted = await waitForAttribute(
    players.get(ARTIST_SEAT).getByTestId('player-executed'),
    'data-seat',
    String(DREAMER_SEAT),
    20_000,
  )
  check(
    '首次处决的公开事实已推到无关玩家（3 号看到 2 号被处决）',
    witnessExecuted === String(DREAMER_SEAT),
    `data-seat=${witnessExecuted}`,
  )

  const unrelatedEntry = await players.get(ARTIST_SEAT).getByTestId('player-extra-nomination').count()
  check('行 1 阴性对照：无关玩家（3 号）没有额外提名入口', unrelatedEntry === 0, `入口数=${unrelatedEntry}`)

  const butcherEntry = players.get(BUTCHER_SEAT).getByTestId('player-extra-nomination')
  await butcherEntry.first().waitFor({ state: 'visible', timeout: 20_000 })
  check('行 1：屠夫本人看到额外提名入口（窗口公开）', (await butcherEntry.count()) >= 1)

  // 行 1 的服务端面：非授予席位（4 号呆瓜）绕开 UI 直调 Hub 也不能发起额外提名（零信任命令面）。
  const rawKlutz = await connectRawSeat(seatTickets[KLUTZ_SEAT - 1])
  const notGranted = await rawKlutz.invoke('NominateExtra', CLOCKMAKER_SEAT, 'butcher-fixture-raw-1')
  await rawKlutz.dispose()
  check(
    '行 1：非屠夫席位直调 Hub 发起额外提名 → 显式拒绝（day.extra_nomination_not_granted）',
    notGranted.kind === 'Rejected' && notGranted.rejectionCode === 'day.extra_nomination_not_granted',
    describeOutcome(notGranted),
  )

  // —— 行 2：额外提名 → 收票达线 → 二次处决 ——
  const butcherPage = players.get(BUTCHER_SEAT)
  await butcherPage.getByTestId('player-extra-nominee-select').selectOption(String(CLOCKMAKER_SEAT))
  await butcherPage.getByTestId('player-nominate-extra').click()
  const nominationCountTwo = await waitForAttribute(nominationList, 'data-nomination-count', '2', 30_000)
  check('行 2：额外提名进入公开账目（当日提名账第 2 条）', nominationCountTwo === '2', `data-nomination-count=${nominationCountTwo}`)

  const secondSweep = await runCommand(storyteller, '额外提名收票', () => storyteller.getByTestId('st-start-vote-sweep').click())
  check('行 2：额外提名开始收票被受理', secondSweep.kind === 'Accepted', secondSweep.raw)
  for (const seat of [CLOCKMAKER_SEAT, ARTIST_SEAT, BUTCHER_SEAT]) {
    await players.get(seat).getByTestId('player-vote-yes').click()
  }

  const secondNomination = nominationList.locator('li').nth(1)
  const secondVotes = await waitForAttribute(secondNomination, 'data-nomination-votes', '3', 30_000)
  const secondSweepDone = await waitForAttribute(dayPanel, 'data-sweep-phase', 'AwaitingCount', 30_000)
  check(
    '行 2：额外提名收票冻结 3 票（5 席存活只需半数，不比前次多）',
    secondVotes === '3' && secondSweepDone === 'AwaitingCount',
    `票数=${secondVotes}；相位=${secondSweepDone}`,
  )

  const secondCount = await runCommand(storyteller, '额外提名计票', () => storyteller.getByTestId('st-count-votes').click())
  check('行 2：额外提名计票被受理', secondCount.kind === 'Accepted', secondCount.raw)
  const aboutTwo = await waitForAttribute(aboutToBeExecuted, 'data-seat', String(CLOCKMAKER_SEAT), 30_000)
  check('行 2：落靶——1 号进入「即将被处决」（二次处决待执行）', aboutTwo === String(CLOCKMAKER_SEAT), `data-seat=${aboutTwo}`)
  await screenshot(storyteller, 'butcher-02-extra-landed')

  const secondClose = await runCommand(storyteller, '二次结束白天并处决', () => storyteller.getByTestId('st-close-day').click())
  check('行 2：二次结束白天被受理', secondClose.kind === 'Accepted', secondClose.raw)

  const clockmakerLife = await waitForAttribute(cardOf(storyteller, CLOCKMAKER_SEAT), 'data-life', 'Dead', 20_000)
  check('行 2：1 号真实死亡（二次处决致死）', clockmakerLife === 'Dead', `data-life=${clockmakerLife}`)

  const dayClosedStatus = await waitForAttribute(dayPanel, 'data-day-status', 'Closed', 20_000)
  const windowUsedStatus = await waitForAttribute(windowRow, 'data-status', 'Used', 20_000)
  const aboutGone = (await aboutToBeExecuted.count()) === 0
  check(
    '行 2：二次处决后白天关账、窗口标记已用掉、无第三次处决（R-0050 第 2 / 5 条）',
    dayClosedStatus === 'Closed' && windowUsedStatus === 'Used' && aboutGone,
    `白天=${dayClosedStatus}；窗口=${windowUsedStatus}；即将被处决残留=${aboutGone ? '无' : '有'}`,
  )
  await screenshot(storyteller, 'butcher-03-day-closed')

  // —— 公开死亡面与本人视角 ——
  const witnessLife = await waitForAttribute(
    players.get(ARTIST_SEAT).locator(`[data-testid="player-lives"] li[data-seat="${CLOCKMAKER_SEAT}"]`),
    'data-life',
    'Dead',
    20_000,
  )
  const witnessAnnouncement = await players
    .get(ARTIST_SEAT)
    .locator(`[data-testid="player-life-announcements"] li[data-seat="${CLOCKMAKER_SEAT}"][data-state="Dead"]`)
    .count()
  check(
    '行 2：二次处决死亡进入无关玩家的公开面（牌面翻死亡 + 本日公告）',
    witnessLife === 'Dead' && witnessAnnouncement >= 1,
    `牌面=${witnessLife}；公告条数=${witnessAnnouncement}`,
  )
  const selfDeadBanner = await clockmakerPage.getByTestId('player-self-dead').count()
  const selfLife = await waitForAttribute(
    clockmakerPage.locator(`[data-testid="player-lives"] li[data-seat="${CLOCKMAKER_SEAT}"]`),
    'data-life',
    'Dead',
    20_000,
  )
  check(
    '行 2：被二次处决者自己的界面显式可见死亡（横幅 + 自己席位翻死亡）',
    selfDeadBanner >= 1 && selfLife === 'Dead',
    `横幅=${selfDeadBanner}；自己牌面=${selfLife}`,
  )
  await screenshot(clockmakerPage, 'butcher-04-second-target-self-dead')

  console.log('=== 6/6 事件流复核（当日处决账 / 窗口账）===')
  const dayEvents = readDayEvents(databasePath)
  // Kind：0 = ExecutionKind.Day（Web 默认 STJ 枚举序列化为数字）；只认常规处决，与处罚处决分账。
  const executedRows = dayEvents.filter(
    (row) => row.type === 'ExecutedEvent' && row.payload.dayNumber === 1 && row.payload.kind === 0,
  )
  const executedSeats = executedRows.map((row) => seatNumberOf(row.payload.seat))
  check(
    '行 2：事件流里当日两次常规处决都入账（DayNumber=1、Kind=Day：2 号 → 1 号）',
    executedSeats.join(',') === `${DREAMER_SEAT},${CLOCKMAKER_SEAT}`,
    `seats=${executedSeats.join(',')}`,
  )

  const windowRows = dayEvents.filter((row) => row.type === 'ExtraNominationWindowOpenedEvent' && row.payload.dayNumber === 1)
  const madeRows = dayEvents.filter((row) => row.type === 'ExtraNominationMadeEvent' && row.payload.dayNumber === 1)
  const closedRows = dayEvents.filter((row) => row.type === 'DayClosedEvent' && row.payload.dayNumber === 1)
  check(
    '行 1/2：窗口只开一次且被这次额外提名用掉（无第二个窗口）、当日只关账一次',
    windowRows.length === 1
      && seatNumberOf(windowRows[0].payload.seat) === BUTCHER_SEAT
      && madeRows.length === 1
      && seatNumberOf(madeRows[0].payload.nominator) === BUTCHER_SEAT
      && seatNumberOf(madeRows[0].payload.nominee) === CLOCKMAKER_SEAT
      && closedRows.length === 1,
    `窗口=${windowRows.length}；额外提名=${madeRows.length}；关账=${closedRows.length}`,
  )

  check('浏览器控制台没有报错', consoleErrors.length === 0, consoleErrors.slice(0, 3).join(' | '))
  await browser.close()
}

/** 连一个真玩家席位（真浏览器 + 真 Vite；入场走账号，见 D-0027；席位票据仍用于线级探针）。 */
async function joinSeatPage(players, browser, consoleErrors, gameId, seat) {
  const page = await newPage(browser, { width: 900, height: 1100 }, consoleErrors)
  await seatByAccount(page, { frontUrl: viteUrl, gameId, seat, suffix: `butcher-${seat}` })
  const badge = page.locator('[data-testid="player-seat"]')
  await badge.waitFor({ timeout: 30_000 })
  const badgeText = compact(await readTextBounded(badge))
  players.set(seat, page)
  return { page, badgeText }
}

/** 直连 Hub 的裸席位客户端（负向探针用：不经浏览器、不经过任何 UI 闸）。 */
async function connectRawSeat(seatTicket) {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(hubUrl)
    .configureLogging(signalR.LogLevel.None)
    .build()
  await connection.start()
  const joined = await connection.invoke('JoinSeat', seatTicket.ticket, 0)
  return {
    invoke: (method, ...args) => connection.invoke(method, joined.credential, ...args),
    dispose: () => connection.stop(),
  }
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

/** 当前槽位是否有挂起请求：有就不能强推（强推会把它按 Override 了结）。 */
async function pendingRequestVisible(page) {
  const pending = page.locator('[data-testid="console-pending"]')
  return (await pending.count()) > 0 && (await pending.first().isVisible().catch(() => false))
}

/** 说书人兜底：强推当前槽位（D-0014）；每次带原因（会随事件流记录）。 */
async function forceAdvanceSlot(page, label) {
  const box = page.locator('section', { hasText: '兜底与推进' })
  await box.locator('input[placeholder^="原因"]').fill(`夹具：${label}`)
  return runCommand(page, label, () => box.getByRole('button', { name: '强推当前槽位' }).click())
}

/**
 * 收尾一个夜晚：先给自然推进一个短窗口（保留"槽位确实会按配额自行前进"的真机观察），
 * 再用强推把剩余空槽位推完——不再白等 N × 配额。
 *
 * 完成判据用「开白天」按钮的可用性：它由视图 `planCompleted` 与夜晚阶段共同决定
 * （DayControl.canStartDay），比读数据抽屉更直接，也不依赖抽屉开合。
 */
async function finishNightQuickly(page, label) {
  const naturalWindowMs = config.slowPacer ? 2500 : 1500
  const naturalDeadline = Date.now() + naturalWindowMs
  while (Date.now() < naturalDeadline) {
    if (await nightIsDone(page)) {
      return { natural: true, forced: 0, completed: true }
    }

    await sleep(150)
  }

  let forced = 0
  while (forced < 40) {
    if (await nightIsDone(page)) {
      return { natural: false, forced, completed: true }
    }

    // 与主装置同一口径：看到挂起请求就停手，绝不越权了结。
    if (await pendingRequestVisible(page)) {
      return { natural: false, forced, completed: false, note: '仍有挂起请求（不越权强推）' }
    }

    const outcome = await forceAdvanceSlot(page, `${label} 收尾第 ${forced + 1} 步`)
    if (outcome.kind !== 'Accepted') {
      return { natural: false, forced, completed: false, note: outcome.raw }
    }

    forced += 1
  }

  return { natural: false, forced, completed: await nightIsDone(page) }
}

async function nightIsDone(page) {
  return page.getByTestId('st-start-day').isEnabled().catch(() => false)
}

/** 裁定点区块的可见文本（没有等待中的裁定点时为空串）。 */
async function readDecisionText(page) {
  const block = page.locator('[data-testid="console-decision"]')
  if ((await block.count()) === 0) {
    return ''
  }

  return compact(await readTextBounded(block.first()))
}

/** 等一个满足条件的裁定点出现（标题里带出裁定上下文）；超时返回最后一次读到的文本。 */
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

function cardOf(page, seat) {
  return page.locator(`[data-testid="grimoire-seat"][data-seat="${seat}"]`)
}

/** 读当天账相关事件（Events 表 Type / Payload；SeatId 序列化为 { value: N }）。 */
function readDayEvents(databasePathToRead) {
  const database = new DatabaseSync(databasePathToRead, { readOnly: true })
  try {
    const rows = database
      .prepare(
        "SELECT Type, Payload FROM Events WHERE Type IN ('ExecutedEvent', 'ExtraNominationWindowOpenedEvent', 'ExtraNominationMadeEvent', 'DayClosedEvent') ORDER BY Sequence",
      )
      .all()
    return rows.map((row) => ({ type: String(row.Type), payload: JSON.parse(String(row.Payload)) }))
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
  console.log('\n=== 屠夫落靶 / 二次处决真机夹具（butcher）取证结论 ===')
  for (const result of results) {
    console.log(`${result.pass ? 'PASS' : 'FAIL'}  ${result.label}${result.detail ? ` → ${result.detail}` : ''}`)
  }

  const failed = results.filter((result) => !result.pass)
  console.log(failed.length === 0 ? `全部通过（${results.length} 项）` : `失败 ${failed.length} / ${results.length}`)
}

function compact(text) {
  return String(text ?? '').replace(/\s+/g, ' ').trim()
}

function describeOutcome(outcome) {
  return `${outcome?.kind ?? '无回执'}${outcome?.rejectionCode ? ` / ${outcome.rejectionCode}` : ''}`
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

function parseArguments(argv) {
  const parsed = { port: 5418, vitePort: 5298 }
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
