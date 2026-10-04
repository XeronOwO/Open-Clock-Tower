/**
 * 账号与局内玩家名装置（E30 / D-0021）—— 第 14 个真机装置。
 *
 * 它回答：**「注册 → 凭票据认领席位 → 同桌看得见玩家名 → 改名即时同步 → 复盘文案用玩家名」这条链路，
 * 在真界面上一路跑得通吗？**以及它的反面：伪造 / 跨账号 / 二次认领能不能被挡住？
 *
 * 场景（4 席：1 / 2 / 3 是场景席，4 号留给"线级探针"专用，浏览器不坐）：
 *   1) 玩家 A 在账号面板注册 alice / 爱丽丝 → 拿一次性恢复码 → 凭票据认领 1 号；
 *   2) 玩家 B 注册 bob / 鲍勃 → 认领 2 号：两席看到同一份公开席位名映射（「1 号 · 爱丽丝」+「2 号 · 鲍勃」）；
 *   3) 游客 C 不登录、只凭票据坐 3 号：席位标签回退「3 号」，那一行不带任何玩家名；
 *   4) A 改名「爱丽丝二世」：断言自己、B 的同桌名单、说书人魔典席位牌三处同步（**当前会红，见下**）；
 *   5) 说书人上报 1 号死亡 → 复盘步骤文案用「1 号 · 爱丽丝二世」（界面级的"复盘文案不再是席位号"证据）；
 *   6) 负向：伪造账号会话 / 跨账号认领 / 同一账号认领第二席都必须被 Hub 显式拒绝，绝不静默降级成游客；
 *   7) 线级探针（第 4 席的真 SignalR 连接）记录改名推送的**序号与载荷**：把"服务端推没推、序号是多少"
 *      从代码推演变成实测——这是第 4 步那三条红断言的根因证据（推送到了、带新名、但序号 == 入座快照序号）。
 *
 * 已知红（E30 实测，产品缺陷，装置不改产品也不放宽断言）：
 *   玩家端对"席位名变化"的推送按**事件序号**合并整视图（`web/src/services/playerViewMerge.ts`：
 *   只有 `sequence > seatNamesSequence` 才采用 seatNames）。改名属会话信息、不推进事件序号，于是这条
 *   推送（序号 0）与入座快照（序号 0）同号 → 被丢弃 → 玩家端席位名停在上一次快照，直到刷新 / 重连重取快照。
 *   说书人端不走这套按字段合并，所以同一调用里说书人视图确实更新了。
 *
 * 与其它装置的分工：零信任装置（verify-zero-trust.mjs）取证"账号会话不是游戏授权、票据才是"；
 * 本装置取证"账号链路在真界面上可用 + 认领闸挡得住 + 改名同步的实际行为"。
 *
 * 前置：Node >= 22.5（node:sqlite）、本机已构建 web/node_modules（playwright + @microsoft/signalr）。
 * 用法（在仓库根运行；默认迭代档 = 快节拍 + 不落盘截图 + 复用产物）：
 *   node tools/verify-accounts.mjs                                        # 迭代档
 *   node tools/verify-accounts.mjs --quota 2 --screenshots-all --build    # 取证档（一批一次）
 *   node tools/verify-accounts.mjs --port 5414 --vite-port 5294           # 自定端口
 *
 * 外部耦合（换机器先核对 web/AGENTS.md §3.1）：宿主编译产物路径、SQLite 表 Games 的
 * StorytellerTicket / SeatsJson 列形状、账号表 Users 与席位绑定表 SeatBindings 的列名。
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

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-accounts-'))
const databasePath = path.join(workspace, 'accounts.db')
const screenshotsDir = path.resolve(repositoryRoot, 'artifacts', 'web')
mkdirSync(screenshotsDir, { recursive: true })

const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`
const hubUrl = `${serverUrl}/hub/game`
const accountHubUrl = `${serverUrl}/hub/account`

/** 宿主日志（取证用：席位名推送到底发没发、推给了几个席位，以服务端自己的记录为准）。 */
const serverLog = []
const seatNamePushLog = () =>
  serverLog
    .join('')
    .split(/\r?\n/)
    .filter((line) => line.includes('玩家名已更新') || line.includes('已推送本人视图'))
    .slice(-4)
    .join(' ⏎ ')

/** 四个席位：1 = 账号 A，2 = 账号 B，3 = 游客 C，4 = 线级探针专用（不坐浏览器，避免抢席位）。 */
const SEAT_COUNT = 4
const SEAT_A = 1
const SEAT_B = 2
const SEAT_GUEST = 3
const SEAT_PROBE = 4

const ALICE = { username: 'alice', displayName: '爱丽丝', password: 'password-123' }
const BOB = { username: 'bob', displayName: '鲍勃', password: 'password-456' }
const RENAMED = '爱丽丝二世'

/** 玩家端推送方法（探针订阅用；`ReceivePlayerViewChanged` 是席位名 / 权力位的那条整视图通道）。 */
const PUSH_METHODS = [
  'ReceivePlayerViewChanged',
  'ReceiveOperationRequest',
  'ReceiveOperationRequestVoided',
  'ReceiveOperationRequestAnswered',
  'ReceivePhaseStarted',
  'ReceiveInformationResult',
  'ReceiveDayChanged',
  'ReceiveGameEnded',
  'ReceiveSeatNamesChanged',
]

/** 定向推送（只该到当事玩家）：游客"拿不到他人私有推送"只针对这些；阶段 / 席位名是公开信息。 */
const TARGETED_METHODS = [
  'ReceiveOperationRequest',
  'ReceiveOperationRequestVoided',
  'ReceiveOperationRequestAnswered',
  'ReceiveInformationResult',
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
  console.log('=== 1/8 构建并启动真宿主（独立临时库，3 席）===')
  await ensureServerArtifacts({ repositoryRoot, buildMode: config.buildMode })
  await startServer()

  console.log('=== 2/8 取票据并起 Vite ===')
  const storytellerTicket = readStorytellerTicket(databasePath)
  const seatTickets = readSeatTickets(databasePath)
  check(`席位票据齐备（${SEAT_COUNT} 席）`, seatTickets.length === SEAT_COUNT, `数据库 ${seatTickets.length} 张`)

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
      env: { ...process.env, VITE_SERVER_TARGET: serverUrl, VITE_SEAT_COUNT: String(SEAT_COUNT) },
      stdio: 'ignore',
    },
  )
  children.push(vite)
  await waitForHttp(viteUrl, 'Vite 开发服务器', 60_000)

  console.log('=== 3/8 说书人 + 玩家 A：注册 → 认领 1 号 ===')
  const browser = await playwright.chromium.launch()
  const consoleErrors = []
  const storytellerPage = await newPage(browser, { width: 1600, height: 1100 }, consoleErrors)
  await storytellerPage.goto(viteUrl)
  await storytellerPage.getByPlaceholder('说书人票据').fill(storytellerTicket)
  await storytellerPage.getByRole('button', { name: '加入' }).click()
  await storytellerPage.locator('[data-testid="grimoire"]').waitFor({ timeout: 30_000 })
  check('说书人加入后看板可见（魔典主视图）', (await storytellerPage.locator('[data-testid="grimoire"]').count()) === 1)

  const alicePage = await newPage(browser, { width: 900, height: 1200 }, consoleErrors)
  await alicePage.goto(`${viteUrl}/#player`)
  await registerAccount(alicePage, ALICE)
  const recoveryText = await waitForLocatorText(alicePage.getByTestId('account-recovery-code'), 30_000)
  const recoveryCode = recoveryCodeOf(recoveryText)
  check(
    'A 注册后一次性恢复码出现且非空（只展示这一次）',
    recoveryCode.length >= 8,
    `恢复码长度 ${recoveryCode.length}｜${recoveryText}`,
  )

  await joinSeat(alicePage, seatTickets[SEAT_A - 1].ticket)
  const aliceSeat = await waitForLocatorContains(
    alicePage.getByTestId('player-seat'),
    `${SEAT_A} 号 · ${ALICE.displayName}`,
    30_000,
  )
  check(`A 认领 ${SEAT_A} 号后席位标签为「${SEAT_A} 号 · ${ALICE.displayName}」`, aliceSeat.includes(`${SEAT_A} 号 · ${ALICE.displayName}`), aliceSeat)
  const aliceRoster = await waitForLocatorContains(rosterItem(alicePage, SEAT_A), ALICE.displayName, 30_000)
  check(`A 的同桌名单含 ${SEAT_A} 号且带玩家名`, aliceRoster.includes(ALICE.displayName), aliceRoster)
  // 截图拍在被断言的那一步（E29 陷阱：先 waitFor 断言元素可见，再拍）。
  await alicePage.getByTestId('player-seat').waitFor({ timeout: 15_000 })
  await screenshot(alicePage, 'accounts-01-player-a')

  console.log('=== 4/8 玩家 B：注册 → 认领 2 号（公开映射两席一致）===')
  const bobPage = await newPage(browser, { width: 900, height: 1200 }, consoleErrors)
  await bobPage.goto(`${viteUrl}/#player`)
  await registerAccount(bobPage, BOB)
  await joinSeat(bobPage, seatTickets[SEAT_B - 1].ticket)
  const bobRoster = await waitForLocatorContains(
    bobPage.getByTestId('player-roster'),
    `${SEAT_B} 号 · ${BOB.displayName}`,
    30_000,
  )
  check(
    `B 的同桌名单同时含「${SEAT_A} 号 · ${ALICE.displayName}」与「${SEAT_B} 号 · ${BOB.displayName}」（两席看到同一份公开映射）`,
    bobRoster.includes(`${SEAT_A} 号 · ${ALICE.displayName}`) && bobRoster.includes(`${SEAT_B} 号 · ${BOB.displayName}`),
    bobRoster,
  )
  await bobPage.getByTestId('player-roster').waitFor({ timeout: 15_000 })
  await screenshot(bobPage, 'accounts-02-player-b')

  console.log('=== 5/8 游客 C：不登录、只凭票据坐 3 号 ===')
  const guestPage = await newPage(browser, { width: 900, height: 1200 }, consoleErrors)
  await guestPage.goto(`${viteUrl}/#player`)
  await joinSeat(guestPage, seatTickets[SEAT_GUEST - 1].ticket)
  await waitForLocatorContains(guestPage.getByTestId('player-roster'), `${SEAT_B} 号 · ${BOB.displayName}`, 30_000)
  const guestSeatText = compact(await guestPage.getByTestId('player-seat').innerText())
  check(`游客 C 的席位标签回退为「${SEAT_GUEST} 号」（无名字）`, guestSeatText === `${SEAT_GUEST} 号`, guestSeatText)
  // 口径（PlayerPanel.vue 的 roster：有名字的席位 ∪ 自己）：自己那一行即使没有名字也列出来，
  // 因此这里断言的"无名字"是**这一行不带任何玩家名**（回退口径「N 号（你）」），不是"这一行不出现"。
  const guestRosterRow = compact(
    await guestPage
      .locator(`[data-testid="player-roster"] li[data-seat="${SEAT_GUEST}"]`)
      .innerText()
      .catch(() => ''),
  )
  check(
    `游客 C 的同桌名单里 ${SEAT_GUEST} 号那一行只有回退席位号、没有任何玩家名`,
    guestRosterRow === `${SEAT_GUEST} 号（你）`,
    `同桌 ${SEAT_GUEST} 号行「${guestRosterRow}」｜整份名单「${compact(await guestPage.getByTestId('player-roster').innerText())}」`,
  )
  check(
    `游客 C 的同桌名单里没有第二个 ${SEAT_GUEST} 号（无名字的席位不重复进映射）`,
    (await guestPage.locator(`[data-testid="player-roster"] li[data-seat="${SEAT_GUEST}"]`).count()) === 1,
    `li[data-seat="${SEAT_GUEST}"] 条数 ${await guestPage.locator(`[data-testid="player-roster"] li[data-seat="${SEAT_GUEST}"]`).count()}`,
  )
  const guestDiagnostics = compact(
    await guestPage
      .locator('[data-testid="player-diagnostics"]')
      .innerText()
      .catch(() => ''),
  )
  check(
    '游客 C 页面不白屏：席位标签在位、诊断区没有「加入失败」',
    (await guestPage.getByTestId('player-seat').count()) === 1 && !guestDiagnostics.includes('加入失败'),
    guestDiagnostics || '无诊断',
  )
  await guestPage.getByTestId('player-seat').waitFor({ timeout: 15_000 })
  await screenshot(guestPage, 'accounts-03-player-guest-c')

  // 线级探针：一条真 SignalR 连接坐在**专属的第 4 席**（游客票据），逐条记录推送的序号与载荷。
  // ⚠ 每席位只保留一条连接（ConnectionRegistry.IssueForSeat）：探针若和某个浏览器页抢同一席，两边会互相
  // 顶替（E30 实测：探针一条推送都收不到，且宿主日志里的"推送=3/3"只统计**发送尝试**、不代表送达）——
  // 所以这里给它一个浏览器不用的席位，测的才是"服务端到底推没推、序号是多少"。
  const seatProbe = await joinSeatProbe(seatTickets[SEAT_PROBE - 1].ticket)
  const probeSnapshotMark = seatProbe.inbox.length

  console.log('=== 6/8 改名：A 自己 / B 的同桌名单 / 说书人魔典三处同步 ===')
  await waitForLocatorContains(grimoireSeatName(storytellerPage, SEAT_A), ALICE.displayName, 30_000)
  await alicePage.getByTestId('account-rename-input').fill(RENAMED)
  await alicePage.getByTestId('account-rename').click()
  const aliceRenamed = await waitForLocatorContains(
    alicePage.getByTestId('player-seat'),
    `${SEAT_A} 号 · ${RENAMED}`,
    30_000,
  )
  check(`A 改名后自己的席位标签即时更新为「${SEAT_A} 号 · ${RENAMED}」`, aliceRenamed.includes(`${SEAT_A} 号 · ${RENAMED}`), aliceRenamed)
  const aliceRosterRenamed = await waitForLocatorContains(rosterItem(alicePage, SEAT_A), RENAMED, 15_000)
  check('A 自己的同桌名单同步为新名', aliceRosterRenamed.includes(RENAMED), aliceRosterRenamed)
  const bobRosterRenamed = await waitForLocatorContains(rosterItem(bobPage, SEAT_A), RENAMED, 30_000)
  check('B 的同桌名单在推送后也更新为新名（轮询等待，超时算失败）', bobRosterRenamed.includes(RENAMED), bobRosterRenamed)
  const storytellerName = await waitForLocatorText(grimoireSeatName(storytellerPage, SEAT_A), 30_000)
  check(
    `说书人魔典 ${SEAT_A} 号席位牌的玩家名为新名`,
    storytellerName === RENAMED,
    `实际「${storytellerName}」`,
  )
  await grimoireSeatName(storytellerPage, SEAT_A).waitFor({ timeout: 15_000 })
  await screenshot(storytellerPage, 'accounts-04-storyteller-grimoire')

  // 线级根因取证（把"怀疑"变成"实测"）：改名推送**确实发到了玩家连接**吗？它带的序号是多少？
  const pushedNewName = await waitForInbox(
    seatProbe,
    (message) =>
      message.method === 'ReceivePlayerViewChanged' && JSON.stringify(message.payload ?? {}).includes(RENAMED),
    20_000,
  )
  check(
    '线级取证：改名推送确实发到玩家连接、载荷里带新名（服务端一侧没有问题）',
    pushedNewName !== null,
    pushedNewName === null
      ? `20s 内没有收到带新名的整视图推送｜探针共收到 ${seatProbe.inbox.length} 条：${seatProbe.inbox.map((message) => message.method).join('|') || '无'}｜宿主日志：${seatNamePushLog() || '无相关行'}`
      : `${pushedNewName.method} 序号=${pushedNewName.sequence}｜宿主日志：${seatNamePushLog()}`,
  )
  check(
    '线级口径：改名推送带的是同一个事件序号（认领 / 改名不产生事件）——合并规则必须用 `>=` 才收得下',
    pushedNewName !== null && Number(pushedNewName.sequence) <= seatProbe.snapshotSequence,
    `推送序号=${pushedNewName?.sequence}（入座快照序号=${seatProbe.snapshotSequence}）`,
  )
  const probeTargeted = seatProbe.inbox
    .slice(probeSnapshotMark)
    .filter((message) => TARGETED_METHODS.includes(message.method))
  check(
    '席位名推送只走公开整视图：探针窗口内零定向推送（公开信息不借道个人请求通道）',
    probeTargeted.length === 0,
    probeTargeted.map((message) => message.method).join('|') || '窗口内无定向推送',
  )

  console.log('=== 7/8 复盘文案口径：说书人上报 1 号死亡 → 步骤文案用玩家名 ===')
  await storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${SEAT_A}"]`).click()
  const reportBlock = storytellerPage.locator('[data-testid="seat-console"] .report')
  await reportBlock.waitFor({ timeout: 30_000 })
  const lifeField = reportBlock.locator('label', { hasText: '生死' })
  await lifeField.locator('input[type="checkbox"]').check()
  await lifeField.locator('select').selectOption('Dead')
  await reportBlock.getByPlaceholder('变化原因（必填，会随事件流记录）').fill('账号装置取证：1 号上报死亡')
  const reported = await runCommand(storytellerPage, '上报 1 号生死', () =>
    reportBlock.getByRole('button', { name: '上报', exact: true }).click(),
  )
  check('说书人上报 1 号死亡被受理（复盘有步骤可看）', reported.kind === 'Accepted', reported.raw)

  // 机制定位（不是放宽断言）：改名属会话信息、**不推进事件序号**，而玩家端按事件序号合并整视图
  // （web/src/services/playerViewMerge.ts：只有 `sequence > seatNamesSequence` 才采用 seatNames）。
  // 刷新页面 = 客户端丢掉合并态、重新取一次快照：若这时也拿到新名，说明读模型与推送同源
  // （推丢失了也能靠重连补齐；赔付面靠的是服务端读模型，不是那一次推送）。
  await bobPage.reload()
  const bobAfterReload = await waitForLocatorContains(
    bobPage.getByTestId('player-roster'),
    `${SEAT_A} 号 · ${RENAMED}`,
    30_000,
  )
  check(
    '同源兜底：刷新重取快照后玩家端拿到同一份新名（读模型与推送同源，不依赖那一次推送）',
    bobAfterReload.includes(`${SEAT_A} 号 · ${RENAMED}`),
    `刷新后 B 的同桌名单「${bobAfterReload}」`,
  )

  await storytellerPage.getByTestId('storyteller-replay-open').click()
  await storytellerPage.getByTestId('replay-panel').waitFor({ timeout: 30_000 })
  const replaySummary = await waitForLocatorContains(
    storytellerPage.getByTestId('replay-summary'),
    `${SEAT_A} 号 · ${RENAMED}`,
    30_000,
  )
  check(
    `复盘步骤文案用「${SEAT_A} 号 · ${RENAMED}」而不是光秃秃的席位号`,
    replaySummary.includes(`${SEAT_A} 号 · ${RENAMED}`),
    replaySummary,
  )
  await storytellerPage.getByTestId('replay-summary').waitFor({ timeout: 15_000 })
  await screenshot(storytellerPage, 'accounts-05-storyteller-replay')
  await storytellerPage.getByTestId('replay-refresh').click()
  const replayRefreshed = await waitForLocatorContains(
    storytellerPage.getByTestId('replay-summary'),
    `${SEAT_A} 号 · ${RENAMED}`,
    30_000,
  )
  check('刷新复盘面板后文案口径不变（服务端口径，不是前端拼的）', replayRefreshed.includes(`${SEAT_A} 号 · ${RENAMED}`), replayRefreshed)

  console.log('=== 8/8 负向：伪造 / 跨账号 / 二次认领 + 会话信息落库 ===')
  const accountClient = await connectHub(accountHubUrl)
  const bobLogin = await accountClient.invoke('Login', BOB.username, BOB.password)
  check(
    'B 可再次登录取得新的账号会话（同一账号多会话并存，用于负向取证）',
    bobLogin.ok === true && typeof bobLogin.accountSession === 'string' && bobLogin.accountSession.length > 0,
    `code=${bobLogin.code}`,
  )

  const probe = await connectHub(hubUrl)
  const forgedTicketA = seatTickets[SEAT_A - 1].ticket

  const forged = await expectRejected(() =>
    probe.invoke('JoinSeatWithAccount', forgedTicketA, '伪造账号会话-随机串-不该被认', 0),
  )
  check(
    '伪造账号会话调 JoinSeatWithAccount 被拒（收到 Hub 错误，不得静默按游客加入）',
    forged.ok && forged.message.length > 0,
    forged.message,
  )

  const crossAccount = await expectRejected(() =>
    probe.invoke('JoinSeatWithAccount', forgedTicketA, bobLogin.accountSession, 0),
  )
  check(
    'B 的账号会话 + A 的席位票据被拒（席位已被别的账号认领）',
    crossAccount.ok && crossAccount.message.includes('其他账号'),
    crossAccount.message,
  )

  const secondSeat = await expectRejected(() =>
    probe.invoke('JoinSeatWithAccount', seatTickets[SEAT_GUEST - 1].ticket, bobLogin.accountSession, 0),
  )
  check(
    '同一账号认领第二席被拒（一账号一席）',
    secondSeat.ok && secondSeat.message.includes('已经认领'),
    secondSeat.message,
  )

  // 被拒的连接不得因此拿到任何身份：它连"我进了哪个席位"都答不出来（没有凭据可用）。
  const probeAfterReject = await expectRejected(() =>
    probe.invoke('SubmitResponse', '被拒连接凭空捏造的凭据', 'forged-request', 'seat:1', 'acc-forged-1', 0),
  )
  check(
    '被拒的连接没有拿到任何连接凭据（再造凭据仍被凭据闸拒绝）',
    probeAfterReject.ok,
    probeAfterReject.message,
  )

  const persisted = readAccountState(databasePath)
  check(
    '绑定是会话信息而非事件：Users 落库两个账号、SeatBindings 只有 1 / 2 号（游客 3 号无绑定）',
    persisted.users.sort().join(',') === `${ALICE.username},${BOB.username}`
      && persisted.bindings.join(',') === `${SEAT_A},${SEAT_B}`,
    `Users=${persisted.users.join('|')}｜SeatBindings 席位=${persisted.bindings.join('|')}`,
  )

  console.log('=== 收尾：账号面板（一次性恢复码）截图 ===')
  // 恢复码只在注册 / 重置时出现；改名不会清掉它，这里复核"同一枚恢复码仍在账号面板上"。
  const recoveryStillThere = await waitForLocatorText(alicePage.getByTestId('account-recovery-code'), 15_000)
  check(
    '账号面板仍展示注册时那枚一次性恢复码（同一枚，未轮换）',
    recoveryCodeOf(recoveryStillThere) === recoveryCode,
    `注册时「${recoveryCode}」｜现在「${recoveryCodeOf(recoveryStillThere)}」`,
  )
  await alicePage.getByTestId('player-account').scrollIntoViewIfNeeded()
  await screenshot(alicePage, 'accounts-06-account-panel-recovery-code')

  check('浏览器控制台没有报错', consoleErrors.length === 0, consoleErrors.slice(0, 3).join(' | '))

  await browser.close()
  await accountClient.stop()
  await probe.stop()
  await seatProbe.connection.stop()
}

/** A / B / C 三张玩家页共用的注册动作：账号面板在未连接时是非紧凑布局。 */
async function registerAccount(page, account) {
  await page.getByTestId('account-username').fill(account.username)
  await page.getByTestId('account-display-name').fill(account.displayName)
  await page.getByTestId('account-password').fill(account.password)
  await page.getByTestId('account-register').click()
  await waitForLocatorContains(page.getByTestId('account-profile'), account.displayName, 30_000)
}

async function joinSeat(page, ticket) {
  await page.getByPlaceholder('席位票据').fill(ticket)
  await page.getByRole('button', { name: '加入' }).click()
  await page.getByTestId('player-seat').waitFor({ timeout: 30_000 })
}

function rosterItem(page, seat) {
  return page.locator(`[data-testid="player-roster"] li[data-seat="${seat}"]`)
}

/**
 * 线级探针：一条真 SignalR 席位连接，逐条记录推送的**序号与载荷**。
 * 用途是把"服务端到底推没推、推的序号是多少"从代码推演变成实测（E30 改名链路取证）。
 */
async function joinSeatProbe(ticket) {
  const connection = await connectHub(hubUrl)
  const inbox = []
  for (const method of PUSH_METHODS) {
    // 处理器必须返回 undefined：返回任何值都会被 SignalR 当成"客户端方法的返回值"回执（web/AGENTS.md §3.1）。
    connection.on(method, (sequence, payload) => {
      inbox.push({ method, sequence, payload })
    })
  }

  const joined = await connection.invoke('JoinSeat', ticket, 0)
  const snapshotSequence = Number(joined.bundle.sequence)
  inbox.push({ method: 'JoinSeat', sequence: snapshotSequence, payload: joined })
  return { connection, credential: joined.credential, snapshotSequence, inbox }
}

/** 轮询探针收件箱直到出现满足条件的消息；超时返回 null（不猜、不吞）。 */
async function waitForInbox(probe, predicate, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    const hit = probe.inbox.find(predicate)
    if (hit !== undefined) {
      return hit
    }

    await sleep(150)
  }

  return null
}

/**
 * 魔典席位牌上的玩家名。**必须限定在 `[data-testid="grimoire"]` 之下**：复盘面板的圆盘
 * （`replay-ring`）复用同一套席位牌组件（同样带 `grimoire-seat` / `seat-display-name`），
 * 不限定范围会命中两个元素（E29 实测：严格模式直接抛错，脚本异常终止）。
 */
function grimoireSeatName(page, seat) {
  return page.locator(
    `[data-testid="grimoire"] [data-testid="grimoire-seat"][data-seat="${seat}"] [data-testid="seat-display-name"]`,
  )
}

/** 「一次性恢复码（只显示这一次，请抄下）：XXXX」→ XXXX。 */
function recoveryCodeOf(text) {
  const index = text.lastIndexOf('：')
  return (index >= 0 ? text.slice(index + 1) : text).replace(/\s+/g, '')
}

/** 账号 / 席位绑定的落库形状（会话信息；游客不产生绑定）。 */
function readAccountState(databasePathToRead) {
  const database = new DatabaseSync(databasePathToRead, { readOnly: true })
  try {
    const users = database
      .prepare('SELECT Username FROM Users ORDER BY Username')
      .all()
      .map((row) => String(row.Username))
    const bindings = database
      .prepare('SELECT Seat FROM SeatBindings ORDER BY Seat')
      .all()
      .map((row) => Number(row.Seat))
    return { users, bindings }
  } finally {
    database.close()
  }
}

/** 账号 Hub 连接（负向取证用；账号会话与游戏连接是两套凭据面）。 */
async function connectHub(url) {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(url)
    .configureLogging(signalR.LogLevel.None)
    .build()
  await connection.start()
  return connection
}

/** 期待一次调用被拒绝（HubException）：返回是否被拒 + 原因文本。 */
async function expectRejected(action) {
  try {
    await action()
    return { ok: false, message: '调用没有被拒绝' }
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error)
    return { ok: message.length > 0, message }
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

/** 轮询直到 locator 文本包含 needle；超时返回最后一次读到的文本（由断言判红）。 */
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

/** 轮询直到 locator 文本非空（"元素出现且有内容"）；超时返回空串。 */
async function waitForLocatorText(locator, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  let text = ''
  while (Date.now() < deadline) {
    text = compact(await locator.innerText().catch(() => ''))
    if (text.length > 0) {
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

/** 断言编号 = 加入顺序（每条断言都有编号与输出，收尾再逐条重放一遍）。 */
function check(label, pass, detail = '') {
  results.push({ label, pass: Boolean(pass), detail })
  console.log(`  ${pass ? '[PASS]' : '[FAIL]'} #${results.length} ${label}${detail ? ` → ${detail}` : ''}`)
}

function report() {
  console.log('\n=== 账号与局内玩家名装置（accounts）取证结论 ===')
  for (const [index, result] of results.entries()) {
    console.log(
      `#${index + 1} ${result.pass ? 'PASS' : 'FAIL'}  ${result.label}${result.detail ? ` → ${result.detail}` : ''}`,
    )
  }

  const failed = results.filter((result) => !result.pass)
  console.log(`断言总数 ${results.length}`)
  console.log(config.screenshots ? `截图：${screenshotsDir}（accounts-01…06）` : '截图：未落盘（迭代档）')
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
      GameServer__SeatCount: String(SEAT_COUNT),
      GameServer__SlotQuotaSeconds: String(config.quotaSeconds),
      GameServer__PacerIntervalMilliseconds: '200',
      DOTNET_ENVIRONMENT: 'Production',
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  })
  child.stdout.on('data', (chunk) => serverLog.push(String(chunk)))
  child.stderr.on('data', (chunk) => serverLog.push(String(chunk)))
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
  const parsed = { port: 5414, vitePort: 5294 }
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
