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
 *   4) A 改名「爱丽丝二世」：断言自己、B 的同桌名单、说书人魔典席位牌三处同步；
 *   5) 说书人上报 1 号死亡 → 复盘步骤文案用「1 号 · 爱丽丝二世」（界面级的"复盘文案不再是席位号"证据）；
 *   6) 负向：伪造账号会话 / 跨账号认领 / 同一账号认领第二席都必须被 Hub 显式拒绝，绝不静默降级成游客；
 *   7) 线级探针（第 4 席的真 SignalR 连接）记录改名推送的**序号与载荷**：把"服务端推没推、序号是多少"
 *      从代码推演变成实测——这条推送与入座快照**同序号**（认领 / 改名是会话信息，不产生事件），
 *      所以玩家端按字段合并整视图时必须用 `sequence >=`（`web/src/services/playerViewMerge.ts`）。
 *      E30 首跑用 `>` 时第 4 步那三条断言真红过（推送到了、带新名、却被当旧数据丢弃），修好后全绿；
 *      这三条 + 探针就是这条口径的回归闸，改动合并规则时别再退回 `>`。
 *   8) 上手引导（E31，票据 ui-layout-and-onboarding 矩阵行 2）：B 页的「?」说明入口——悬停显示、
 *      点按（触屏路径）显示、`Esc` 关闭；文案来自 `display/help.ts` 登记表（截图 accounts-10）。
 *   9) 抽屉面姓名口径（E31，E30 残余①）：数据抽屉（状态账 / 最近状态变化）+ 开局分配 + 席内注记
 *      四处都显示「1 号 · 爱丽丝二世」（截图 accounts-09）。
 *  10) 版面量度（E31 矩阵行 3）：固定状态下的整页截图 + `.shell` 内容高度（玩家页 / 说书人页），
 *      供"前后对比"引用；数值只记录、不断言（截图 accounts-07/08）。
 *
 * 与其它装置的分工：零信任装置（verify-zero-trust.mjs）取证"账号会话不是游戏授权、票据才是"；
 * 本装置取证"账号链路在真界面上可用 + 认领闸挡得住 + 改名同步的实际行为"。
 *
 * 前置：Node >= 22.5（node:sqlite）、本机已构建 web/node_modules（playwright + @microsoft/signalr）。
 * 用法（在仓库根运行；默认迭代档 = 快节拍 + 不落盘截图 + 复用产物）：
 *   node tools/verify-accounts.mjs                                        # 迭代档（全部段）
 *   node tools/verify-accounts.mjs --list-sections                        # 只列段名，不起宿主
 *   node tools/verify-accounts.mjs --only drawer-names                    # 执行到该段为止，且只判该段
 *   node tools/verify-accounts.mjs --from replay                          # 全程执行，但从该段起才计入判定
 *   node tools/verify-accounts.mjs --quota 2 --screenshots-all --build    # 取证档（一批一次）
 *   node tools/verify-accounts.mjs --port 5414 --vite-port 5294           # 自定端口
 *
 * 段落（前缀执行 + 判定过滤：`--only` 与 `--from` 互斥，前面的段是必要前置、照跑但只有选中段计入判定）：
 *   boot · tickets · join-a · join-b · guest · rename · replay · drawer-names
 *   · negative · account-panel · onboarding · layout
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
import { readAttributeBounded, readTextBounded } from './lib/bounded-text.mjs'
import { describeProfile, ensureServerArtifacts, extractProfileFlags, resolveProfile } from './lib/verify-profile.mjs'
import { createChecker, createSectionRunner } from './lib/verify-sections.mjs'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const webRoot = path.join(repositoryRoot, 'web')
const { flags, rest } = extractProfileFlags(process.argv.slice(2))
const options = parseArguments(rest)

/**
 * 档位（tools/lib/verify-profile.mjs）：默认迭代档（快节拍 0.3s + 不落盘截图 + 复用产物）；
 * 取证档显式传 `--quota 2 --screenshots-all`（必要时加 `--build`）。
 */
const config = resolveProfile(flags, { quotaSeconds: 0.3 })

/** 段落清单：顺序即执行顺序，也是用法头里那份清单的唯一事实来源。 */
const SECTIONS = [
  { id: 'boot', title: '构建并启动真宿主（独立临时库）' },
  { id: 'tickets', title: '取票据（说书人 + 各席位）并起 Vite' },
  { id: 'join-a', title: '说书人 + 玩家 A：注册 → 认领 1 号' },
  { id: 'join-b', title: '玩家 B：注册 → 认领 2 号（公开映射两席一致）' },
  { id: 'guest', title: '游客 C：不登录只凭票据坐 3 号 + 线级探针入座' },
  { id: 'rename', title: '改名：A 自己 / B 同桌 / 说书人魔典三处同步 + 线级序号取证' },
  { id: 'replay', title: '复盘文案口径：上报 1 号死亡 → 步骤文案与刷新' },
  { id: 'drawer-names', title: '抽屉面姓名口径：状态账 / 最近状态变化 / 开局分配 / 席内注记' },
  { id: 'negative', title: '负向：伪造 / 跨账号 / 二次认领 + 会话信息落库' },
  { id: 'account-panel', title: '收尾：账号面板一次性恢复码' },
  { id: 'onboarding', title: '收尾：说明入口（悬停 / 点按 / Esc）' },
  { id: 'layout', title: '版面量度（内容高 + 整页截图）+ 控制台零错误' },
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
    console.log(`  ${section.id.padEnd(14)} ${section.title}`)
  }

  process.exit(0)
}

const children = []
/** 浏览器实例与三条 SignalR 连接（模块级：正常收尾、`--only` 早退与异常路径都要关掉，不留孤儿）。 */
let browser = null
let accountClient = null
let probe = null
let seatProbe = null
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
console.log(`段落选择：${runner.selectionSummary()}`)

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
  runner.reportTimings()
  await cleanup()
  checker.report()
  console.log(config.screenshots ? `截图：${screenshotsDir}（accounts-*）` : '截图：未落盘（迭代档）')
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

  if (!runner.begin('tickets')) return
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

  if (!runner.begin('join-a')) return
  browser = await playwright.chromium.launch()
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

  if (!runner.begin('join-b')) return
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

  if (!runner.begin('guest')) return
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
  // 诊断区只在有内容时渲染，所以这里必须**非等待**读取：`innerText()` 在元素缺失时会白等满
  // Playwright 默认的 30s 超时（2026-10-05 实测：光是这一处就把本装置从 ~7s 拖到 ~37s，
  // 分段耗时把 guest 段钉在 30.3s 才暴露出来）。缺失 = 没有诊断，不是失败。
  const diagnosticsBox = guestPage.locator('[data-testid="player-diagnostics"]')
  const guestDiagnostics =
    (await diagnosticsBox.count()) === 0 ? '' : compact(await readTextBounded(diagnosticsBox.first()))
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
  seatProbe = await joinSeatProbe(seatTickets[SEAT_PROBE - 1].ticket)
  const probeSnapshotMark = seatProbe.inbox.length

  if (!runner.begin('rename')) return
  await waitForLocatorContains(grimoireSeatName(storytellerPage, SEAT_A), ALICE.displayName, 30_000)
  // 账号区登录后默认收成一行摘要（票据 ui-layout-and-onboarding）：先点开「管理账号」再改名。
  await alicePage.getByTestId('account-fold-toggle').click()
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

  if (!runner.begin('replay')) return
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

  if (!runner.begin('drawer-names')) return
  // 抽屉面姓名口径（E30 残余①，票据 ui-layout-and-onboarding 同批收口）：八个组件接入后逐面复核
  // **真机**上的四处（其余面板需要夜间 / 窗口夹具，由组件级渲染回归 `seatDisplay.spec.ts` 覆盖）。
  const drawerToggle = storytellerPage.getByTestId('data-drawer-toggle')
  if ((await drawerToggle.getAttribute('aria-expanded')) !== 'true') {
    await drawerToggle.click()
  }

  const ledgerText = await waitForLocatorContains(
    storytellerPage.locator('[data-testid="data-drawer-body"] section', { hasText: '状态账' }).first(),
    `${SEAT_A} 号 · ${RENAMED}`,
    15_000,
  )
  check(
    `状态账面板用「${SEAT_A} 号 · ${RENAMED}」而不是光秃秃的席位号`,
    ledgerText.includes(`${SEAT_A} 号 · ${RENAMED}`),
    ledgerText.slice(0, 160),
  )

  const timelineText = compact(
    await storytellerPage
      .locator('[data-testid="data-drawer-body"] section', { hasText: '最近状态变化' })
      .first()
      .innerText(),
  )
  check(
    `最近状态变化面板用「${SEAT_A} 号 · ${RENAMED}」`,
    timelineText.includes(`${SEAT_A} 号 · ${RENAMED}`),
    timelineText.slice(0, 160),
  )

  const assignmentText = compact(
    await storytellerPage.locator('section', { hasText: '开局分配' }).first().innerText(),
  )
  check(
    `开局分配表用「${SEAT_A} 号 · ${RENAMED}」`,
    assignmentText.includes(`${SEAT_A} 号 · ${RENAMED}`),
    assignmentText.slice(0, 160),
  )

  const annotationText = compact(
    await storytellerPage.locator('[data-testid="annotation-control"] .line').first().innerText(),
  )
  check(
    `席内注记区用「${SEAT_A} 号 · ${RENAMED}」`,
    annotationText.includes(`${SEAT_A} 号 · ${RENAMED}`),
    annotationText.slice(0, 160),
  )
  await screenshot(storytellerPage, 'accounts-09-drawer-names')

  if (!runner.begin('negative')) return
  accountClient = await connectHub(accountHubUrl)
  const bobLogin = await accountClient.invoke('Login', BOB.username, BOB.password)
  check(
    'B 可再次登录取得新的账号会话（同一账号多会话并存，用于负向取证）',
    bobLogin.ok === true && typeof bobLogin.accountSession === 'string' && bobLogin.accountSession.length > 0,
    `code=${bobLogin.code}`,
  )

  probe = await connectHub(hubUrl)
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

  if (!runner.begin('account-panel')) return
  // 恢复码只在注册 / 重置时出现；改名不会清掉它，这里复核"同一枚恢复码仍在账号面板上"。
  const recoveryStillThere = await waitForLocatorText(alicePage.getByTestId('account-recovery-code'), 15_000)
  check(
    '账号面板仍展示注册时那枚一次性恢复码（同一枚，未轮换）',
    recoveryCodeOf(recoveryStillThere) === recoveryCode,
    `注册时「${recoveryCode}」｜现在「${recoveryCodeOf(recoveryStillThere)}」`,
  )
  await alicePage.getByTestId('player-account').scrollIntoViewIfNeeded()
  await screenshot(alicePage, 'accounts-06-account-panel-recovery-code')

  if (!runner.begin('onboarding')) return
  // 上手引导的说明入口（票据 ui-layout-and-onboarding 矩阵行 2）：悬停（键鼠）与点按（触屏路径）都能打开，
  // 文案来自 `display/help.ts` 登记表；Esc 关闭。截图 `accounts-10-explain-tip` 留证。
  const helpButton = bobPage.getByTestId('player-roster').getByRole('button', { name: '说明：玩家名' })
  await helpButton.hover()
  const hoverBubble = await waitForLocatorText(bobPage.getByRole('tooltip'), 10_000)
  check(
    '说明入口：悬停显示登记表文案（「玩家名」条目）',
    hoverBubble.includes('玩家名') && hoverBubble.includes('不参与授权'),
    hoverBubble,
  )
  await bobPage.mouse.move(0, 0)
  await helpButton.click()
  const tapBubble = await waitForLocatorText(bobPage.getByRole('tooltip'), 10_000)
  check('说明入口：点按（触屏路径）同样能打开', tapBubble.includes('不参与授权'), tapBubble)
  await screenshot(bobPage, 'accounts-10-explain-tip')
  await bobPage.keyboard.press('Escape')
  check('说明入口：Esc 关闭气泡', (await bobPage.getByRole('tooltip').count()) === 0, '气泡已撤下')

  if (!runner.begin('layout')) return
  // 版面量度（票据 `ui-layout-and-onboarding` 矩阵行 3）：固定状态下的整页截图 + 内容高度，
  // 供"前后对比"引用。量的是 `.shell` 的内容底边——`documentElement.scrollHeight` 会被视口高度钳制；
  // 说书人页量之前先收起数据抽屉（展开与否是本地呈现态，不能混进量度）。
  // B 页的账号区保持默认态，正是要量的那个状态；数值只记录、不断言。
  const drawer = storytellerPage.getByTestId('data-drawer-toggle')
  if ((await drawer.count()) > 0 && (await readAttributeBounded(drawer, 'aria-expanded')) === 'true') {
    await drawer.click()
  }
  const contentHeightOf = (page) =>
    page.evaluate(() => {
      const shell = document.querySelector('.shell')
      return shell === null ? 0 : Math.round(shell.getBoundingClientRect().bottom + window.scrollY)
    })
  const playerScroll = await contentHeightOf(bobPage)
  const storytellerScroll = await contentHeightOf(storytellerPage)
  console.log(`  版面量度：玩家页（B，账号默认态）内容高=${playerScroll}px；说书人页（抽屉收起）内容高=${storytellerScroll}px`)
  await screenshot(bobPage, 'accounts-07-layout-player')
  await screenshot(storytellerPage, 'accounts-08-layout-storyteller')

  check('浏览器控制台没有报错', consoleErrors.length === 0, consoleErrors.slice(0, 3).join(' | '))
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
    text = compact(await readTextBounded(locator))
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
    text = compact(await readTextBounded(locator))
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

/** 关掉浏览器与三条 SignalR 连接：正常收尾、`--only` 早退与异常路径共用，幂等。 */
async function closeBrowser() {
  if (browser === null) {
    return
  }

  const closing = browser
  browser = null
  await closing.close().catch(() => {})
}

async function stopConnections() {
  const connections = [accountClient, probe, seatProbe?.connection].filter((connection) => connection != null)
  accountClient = null
  probe = null
  seatProbe = null
  await Promise.all(connections.map((connection) => connection.stop().catch(() => {})))
}

async function cleanup() {
  await closeBrowser()
  await stopConnections()
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
