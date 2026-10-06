#!/usr/bin/env node
/**
 * 入场可用性验收装置 —— 「一个没用过的人能不能自己走通」的判据。
 *
 * 它回答一个**别处证明不了**的问题：第一次打开这个站的人，知不知道该点哪。
 * 现有装置测的全是"规则对不对 / 功能通不通"（1300 个单测 · 主装置 310 项 · 其余十八个链路），
 * 没有一条断言在问界面本身是否可走通——所以界面把注册表单塞进开桌折叠区、把桌列表放在登录之前、
 * 让人抄一串 47 个字符的票据，装置照样全绿。
 *
 * 票据 docs/backlog/done/usability-acceptance-device.md。判据取自"新用户实际会遇到什么"：
 *   gate     未登录时页面上只有一张登录卡（不存在桌列表 / 席位按钮 / 票据输入框 / 开桌表单）
 *   plain    入口面上不出现开发者术语（禁词表扫描：文案会改，禁词不会）
 *   host     第一次主持：注册 → 开一桌（前置，给后两段准备一张真桌）
 *   relogin  换设备：全新浏览器上下文里只登录同一账号 → 桌还在、点一下进得去主持台
 *   sit      第一次入座：从打开站点到真的坐下，路径步数可数（超出阈值即红）
 *   faces    同一浏览器切面不掉登录（玩家面登录 → 切到说书人面 → 不用再登）
 *
 * **它不直读数据库**（与其它装置最大的不同）：身份只走账号，全程不碰 `Games.StorytellerTicket`
 * ——所以"票据整个退场"的改造不会打翻它，它反而是那次改造的判据（见 entrance-redesign.md）。
 *
 * 锚点契约（本装置定义的判据面；产品侧必须保留，或按这些名字实现）：
 *   `account-username` / `account-display-name` / `account-password` / `account-register` /
 *   `account-login` / `account-profile`            账号（登录卡与已登录资料区）
 *   `player-lobby` · `[data-table=<gameId>]` · `[data-seat=<gameId>-<n>]`   玩家面桌列表与席位按钮
 *   `my-tables` · `[data-my-table=<gameId>]` · `host-enter`                「我主持的桌」与进入按钮
 *   `open-table-name` / `open-table-seats` / `open-table-submit`           开桌表单
 *   `grimoire`（主持台）· `player-seat`（已入座徽标）· `top-nav`（顶栏）
 *   顶栏 `nav-home` / `nav-player` / `nav-storyteller`：**文案可变，锚点不变**
 *   四个面的地址：空地址 = 首页 · `/home` = 首页 · `/play` = 加入一桌 · `/storyteller` = 主持一局；
 *   旧的井号写法（`#player` 等）**是重定向入口**（gate 段有一条断言守着它）。
 *
 * 用法（在仓库根运行）：
 *   node tools/verify-entrance-usability.mjs
 *   node tools/verify-entrance-usability.mjs --list-sections
 *   node tools/verify-entrance-usability.mjs --only sit
 *   node tools/verify-entrance-usability.mjs --seats 5 --timeout 15
 *   node tools/verify-entrance-usability.mjs --quota 2 --screenshots-all    # 取证档
 *
 * 分段口径与其它装置一致：`--only X` 执行到 X 为止（前面的段作为必要前置照样跑，只有 X 的断言
 * 计入判定）；`--from X` 全程照跑、从该段起计入判定；两者互斥。
 *
 * 退出码：0 = 全部通过；1 = 有失败（含前置段）；2 = 环境问题（参数不合法 / 缺 Playwright）。
 * 前置：`cd web; npm install`（Playwright 装在 web/node_modules）。
 *
 * 收尾：自建的临时库与工作目录（`%TEMP%/oct-entrance-verify-*`）由脚本自己删——它是本装置
 * 自己的产物，与仓库、与用户的文件无关（口径同其余装置）。
 */
import { spawn } from 'node:child_process'
import { mkdirSync, mkdtempSync, rmSync } from 'node:fs'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { readAttributeBounded, readTextBounded } from './lib/bounded-text.mjs'
import { createChecker, createSectionRunner } from './lib/verify-sections.mjs'
import {
  describeProfile,
  ensureServerArtifacts,
  extractProfileFlags,
  resolveProfile,
} from './lib/verify-profile.mjs'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const webRoot = path.join(repositoryRoot, 'web')
const screenshotsDir = path.join(repositoryRoot, 'artifacts', 'web')

const SECTIONS = [
  { id: 'boot', title: '起真宿主与前端开发服务器（前置，无断言）' },
  { id: 'gate', title: '未登录时页面上只有一张登录卡（首页 / 说书人面 / 玩家面）' },
  { id: 'plain', title: '入口面上不出现开发者术语（禁词扫描）' },
  { id: 'host', title: '第一次主持：注册 → 开一桌（前置，给后两段准备一张真桌）' },
  { id: 'relogin', title: '换设备：全新浏览器里只登录同一账号，桌还在、进得去主持台' },
  { id: 'sit', title: '第一次入座：从打开站点到真的坐下（路径步数可数）' },
  { id: 'faces', title: '同一浏览器切面不掉登录（玩家面登录 → 切到说书人面）' },
]

/**
 * 入口面上的禁词表（"界面别再说黑话"的判据）。
 *
 * 口径来自票据：`席位票据` / `连接状态：未连接` / `玩家端` / `说书人端` 这类词是给开发者看的。
 * 用**禁词**而不是"断言某句中文文案存在"：文案会改，禁词不会（与各装置的 `STORYTELLER_LEAK` 同款做法）。
 * `票据` 一并禁掉：说书人票据整张票（含 `邀请码` 这个旧文案）都要从入口面退场。
 * 注意扫描面只含**入口面**（首页 / 未登录的两个面 / 顶栏）——玩家登录后若还留一个邀请码兜底入口，
 * 不在本判据内（那是"席位票据要不要退场"那张独立票的事）。
 */
const FORBIDDEN_TERMS = ['票据', '邀请码', '玩家端', '说书人端', '连接状态', '未连接']

/**
 * 从"打开站点"到"真的坐进一张桌"的步数上限（点击与输入各算一步）。
 *
 * 下限 6 = 新账号的最小自然路径：切到「加入一桌」1 + 注册 4（三项输入 + 一次点击）+ 选席位 1；
 * 已有账号只要 5。上限给到 7，是留给"登录卡上切一次注册 / 登录页签"这一步（目标形态里是两个 tab）——
 * 再多出来的每一步都是界面在绕人：多一次展开、多一次"先试试再说"、多一次没用的刷新。
 */
const MAX_SEAT_PATH_STEPS = 7

/** 退出码 2 = 参数 / 环境问题（与"断言失败"的 1 分开，一眼能分清该查哪边）。 */
let options = null
let config = null
try {
  const { flags, rest } = extractProfileFlags(process.argv.slice(2))
  config = resolveProfile(flags, {})
  options = parseArguments(rest)
} catch (error) {
  console.error(`参数错误：${error instanceof Error ? error.message : String(error)}`)
  process.exit(2)
}

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

let playwright = null
try {
  // 依赖装在 web/node_modules：脚本住在 tools/，所以要显式按 web/ 解析（Node 的默认查找不会跨目录）。
  const requireFromWeb = createRequire(path.join(webRoot, 'package.json'))
  playwright = requireFromWeb('playwright')
} catch (error) {
  console.error(`缺少 Playwright：${String(error)}`)
  console.error('先运行：cd web; npm install; npx playwright install chromium')
  process.exit(2)
}

/**
 * 界面响应窗口：一次点击 / 一跳之后，界面该把结果画出来的时间。
 * 取"单步超时"与 6 秒的较小值——**红断言不许靠等满超时收场**（`AGENTS.local.md` 验证成本纪律）：
 * 本装置的多数判据今天就是红的，每条都等满 30 秒会把一次迭代拖成几分钟。
 */
const uiWaitMs = Math.min(options.timeoutMs, 6000)

/**
 * 落地窗口：这一面上的桌列表是**异步**取的（进页面后才向账号 Hub 要）。
 * 不settle 就扫，会扫到"列表还没来"的中间态——"没有席位按钮"当场假绿（首跑实测）。
 * 1.2s 是本机实测列表到位时间的十倍余量；它只是让页面落定，不参与任何判定口径。
 */
const SETTLE_MS = 1200

console.log(`档位：${describeProfile(config)}`)
console.log(`段落选择：${runner.selectionSummary()}`)

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-entrance-verify-'))
const databasePath = path.join(workspace, 'verify.db')
const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`
const frontUrl = viteUrl
const children = []
let browser = null

/** 本次运行的标记：两个账号与桌名都带它，一眼能认出是同一次运行。 */
const stamp = Date.now().toString(36)
const hostAccount = { username: `usab-host-${stamp}`, displayName: `验收主持${stamp}`, password: `pw-host-${stamp}` }
const seatAccount = { username: `usab-seat-${stamp}`, displayName: `验收玩家${stamp}`, password: `pw-seat-${stamp}` }
const tableName = `可用性验收桌${stamp}`
/** 开桌段解出来的桌标识；后两段按它定位大厅行与"我主持的桌"行。 */
let createdGameId = null

process.on('exit', () => killChildren())

try {
  await main()
  runner.reportTimings()
  await cleanup()
  checker.report()
  process.exit(checker.results.some((result) => result.outcome === 'fail') ? 1 : 0)
} catch (error) {
  console.error(`\n[FAIL] 装置异常终止：${error instanceof Error ? error.stack : String(error)}`)
  checker.results.push({ section: runner.currentId, label: '脚本执行到底', outcome: 'fail', detail: '见上方异常' })
  await cleanup()
  checker.report()
  process.exit(1)
}

async function main() {
  console.log(`宿主：${serverUrl} · 页面：${frontUrl}`)
  console.log(`本次账号：${hostAccount.username}（主持）/ ${seatAccount.username}（入座）`)

  if (!runner.begin('boot')) return
  await ensureServerArtifacts({ repositoryRoot, buildMode: config.buildMode })
  await startServer()
  await startVite()

  browser = await playwright.chromium.launch()
  const consoleErrors = []
  /** 未登录的旁观者：首页与两个入口面都由它打开（全新上下文 = 没有会话、没有本机票据）。 */
  const bystander = await newClient(browser, consoleErrors)

  if (!runner.begin('gate')) return
  // 空地址 = 首页（门厅）；说书人面与玩家面各有各的地址。三条入口都逐面判一遍。
  await openFace(bystander.page, `${frontUrl}/`)
  const rootIsHome = await waitForCount(bystander.page.getByTestId('home-to-storyteller'), 1, uiWaitMs)
  check(
    '空地址（根路径）是首页：给出两个入口',
    rootIsHome &&
      (await bystander.page.getByTestId('home-to-player').count()) === 1 &&
      (await bystander.page.getByTestId('home-to-storyteller').count()) === 1,
    rootIsHome ? '两个入口都在' : '根路径没有渲染首页',
  )
  check(
    '首页不是登录页：上面没有账号表单 / 票据输入框 / 开桌表单',
    (await bystander.page.getByTestId('account-username').count()) === 0 &&
      (await bystander.page.getByTestId('storyteller-ticket').count()) === 0 &&
      (await bystander.page.getByTestId('open-table-submit').count()) === 0,
  )
  await inspectGate(bystander.page, '首页（/home 与空地址同一面）', `${frontUrl}/home`, 'none')
  await inspectGate(bystander.page, '说书人面（/storyteller）', `${frontUrl}/storyteller`)
  await inspectGate(bystander.page, '玩家面（/play）', `${frontUrl}/play`)

  // 旧的井号地址（#player）是用户手里与老装置里的写法：必须自己落到新地址上，且地址栏里不再有井号。
  await bystander.page.goto(`${frontUrl}/#player`, { waitUntil: 'domcontentloaded' })
  const legacySettled = await waitForCount(bystander.page.getByTestId('top-nav'), 1, uiWaitMs)
  const legacyUrl = new URL(bystander.page.url())
  check(
    '旧井号地址 #player 自动落到 /play（地址栏里不再有井号）',
    legacySettled && legacyUrl.pathname === '/play' && legacyUrl.hash === '',
    `落点=${legacyUrl.pathname}${legacyUrl.hash}`,
  )

  if (!runner.begin('plain')) return
  await scanForbiddenTerms(bystander.page, '首页（空地址）', `${frontUrl}/`)
  await scanForbiddenTerms(bystander.page, '说书人面（/storyteller）', `${frontUrl}/storyteller`)
  await scanForbiddenTerms(bystander.page, '玩家面（/play）', `${frontUrl}/play`)

  if (!runner.begin('host')) return
  const host = await newClient(browser, consoleErrors)
  await host.page.goto(`${frontUrl}/storyteller`, { waitUntil: 'domcontentloaded' })
  await revealAccountForm(host.page)
  await revealRegisterTab(host.page)
  await host.page.getByTestId('account-username').fill(hostAccount.username)
  await host.page.getByTestId('account-display-name').fill(hostAccount.displayName)
  await host.page.getByTestId('account-password').fill(hostAccount.password)
  await host.page.getByTestId('account-register').click()
  const registered = await waitForCount(host.page.getByTestId('account-profile'), 1, uiWaitMs)
  if (!registered) {
    check('注册新账号（开桌的前提）', false, '账号资料区没有出现')
    throw new Error('前置未成立：账号没注册上，后面的段无从判起')
  }

  check(`注册新账号（${hostAccount.username}）`, true, `${hostAccount.displayName}`)
  await openTable(host.page)
  await screenshot(host.page, 'entrance-opened-table')
  await host.context.close()

  if (!runner.begin('relogin')) return
  const elsewhere = await newClient(browser, consoleErrors)
  await elsewhere.page.goto(`${frontUrl}/storyteller`, { waitUntil: 'domcontentloaded' })
  await revealAccountForm(elsewhere.page)
  await elsewhere.page.getByTestId('account-username').fill(hostAccount.username)
  await elsewhere.page.getByTestId('account-password').fill(hostAccount.password)
  await elsewhere.page.getByTestId('account-login').click()
  const loggedIn = await waitForCount(elsewhere.page.getByTestId('account-profile'), 1, uiWaitMs)
  check(
    `换一台设备（全新浏览器上下文）：只凭账号登录成功（${hostAccount.username}）`,
    loggedIn,
    loggedIn ? '资料区已出现' : '登录后没有出现账号资料区',
  )

  // 换设备的全部意义在这一条：桌不跟着浏览器走，跟着账号走。
  const myTables = await waitForCount(elsewhere.page.getByTestId('my-tables'), 1, uiWaitMs)
  const myRow = createdGameId === null ? null : elsewhere.page.locator(`[data-my-table="${createdGameId}"]`)
  const rowText = myTables && myRow !== null ? compact(await readTextBounded(myRow)) : ''
  check(
    '登录后给出「我主持的桌」，里面就是刚开的那一桌（换设备后桌还在）',
    myTables && myRow !== null && (await myRow.count()) === 1,
    myTables ? rowText || '「我主持的桌」里没有这一桌' : '页面上没有「我主持的桌」',
  )
  check(
    '说书人面（/storyteller）给的是我的桌，不是玩家选席',
    myTables && (await elsewhere.page.locator('[data-seat]').count()) === 0,
    `我的桌=${myTables ? '在' : '不在'}；席位按钮=${await elsewhere.page.locator('[data-seat]').count()}`,
  )

  if (myTables && myRow !== null && (await myRow.count()) === 1) {
    await myRow.getByTestId('host-enter').click()
    const grimoire = await waitForCount(elsewhere.page.getByTestId('grimoire'), 1, uiWaitMs)
    check('点一下进得去主持台（魔典可见，不用再抄任何凭据）', grimoire, grimoire ? '魔典已渲染' : '魔典没出现')
  } else {
    check('点一下进得去主持台（魔典可见，不用再抄任何凭据）', false, '没有可点的「我主持的桌」行')
  }

  await screenshot(elsewhere.page, 'entrance-relogin')
  await elsewhere.context.close()

  if (!runner.begin('sit')) return
  // 新用户视角：从**打开站点**（空地址 = 首页）开始，只做"屏幕上看起来该做的事"，每一步都记数。
  const stranger = await newClient(browser, consoleErrors)
  const steps = createStepCounter()
  await stranger.page.goto(`${frontUrl}/`, { waitUntil: 'domcontentloaded' })
  await steps.click(stranger.page.getByTestId('nav-player'), '顶栏「加入一桌」（玩家面）')
  await waitForFace(stranger.page, 'nav-player')
  if (await revealAccountForm(stranger.page)) {
    steps.record('展开被折叠起来的登录区')
  }

  if (await revealRegisterTab(stranger.page)) {
    steps.record('登录卡上切到「注册」页签')
  }

  await steps.fill(stranger.page.getByTestId('account-username'), seatAccount.username, '登录名')
  await steps.fill(stranger.page.getByTestId('account-display-name'), seatAccount.displayName, '玩家名')
  await steps.fill(stranger.page.getByTestId('account-password'), seatAccount.password, '口令')
  await steps.click(stranger.page.getByTestId('account-register'), '注册')
  const seatRegistered = await waitForCount(stranger.page.getByTestId('account-profile'), 1, uiWaitMs)
  if (!seatRegistered) {
    check('新玩家注册（入座的前提）', false, '注册后没有出现账号资料区')
    throw new Error('前置未成立：新玩家没注册上，坐下的路径无从数起')
  }

  const lobbyRow = await waitForLobbyRow(stranger.page, createdGameId, tableName)
  check('登录后玩家面能看到在开的桌（大厅）', lobbyRow !== null, lobbyRow === null ? '大厅里没有这一桌' : '已列出')
  const seatButton = lobbyRow === null ? null : lobbyRow.locator('[data-seat]:not([disabled])').first()
  await steps.click(seatButton ?? stranger.page.locator('[data-seat]:not([disabled])').first(), '点一个空席位坐下')
  const seated = await waitForCount(stranger.page.getByTestId('player-seat'), 1, uiWaitMs)
  check('真的坐进了这一桌（本人席位徽标出现）', seated, seated ? '已入座' : '点了席位但没有入座')

  const trace = steps.trace()
  console.log(`  入座路径（${steps.count} 步，阈值 ${MAX_SEAT_PATH_STEPS}）：${trace}`)
  check(
    `从打开站点到坐下不超过 ${MAX_SEAT_PATH_STEPS} 步（本次 ${steps.count} 步）`,
    steps.count <= MAX_SEAT_PATH_STEPS,
    trace,
  )
  await screenshot(stranger.page, 'entrance-seated')

  if (!runner.begin('faces')) return
  // 同一个文档里换面（点顶栏而不是重新打开）：账号会话该跟着人走，不该跟着面走。
  // 判据取"文档加载次数"与"地址栏路径"（票 frontend-path-routing 的验收行 4）：
  // 换面若退化成整页跳转，文档重新加载、内存里的账号会话就没了——下面那条"还认得我"会跟着红。
  const beforeSwitch = await readDocumentLoads(stranger.page)
  await stranger.page.getByTestId('nav-storyteller').click()
  const switched = await waitForFace(stranger.page, 'nav-storyteller')
  const afterSwitch = await readDocumentLoads(stranger.page)
  const switcherUrl = new URL(stranger.page.url())
  check(
    '切面没有重载文档（加载次数不变）且地址栏换成目标路径',
    beforeSwitch !== null && afterSwitch === beforeSwitch && switcherUrl.pathname === '/storyteller',
    `加载次数 ${String(beforeSwitch)} → ${String(afterSwitch)}；地址=${switcherUrl.pathname}`,
  )

  const storytellerRendered = await waitForAnyTestId(
    stranger.page,
    ['my-tables', 'storyteller-ticket', 'open-table-submit'],
    uiWaitMs,
  )
  const profile = stranger.page.getByTestId('account-profile')
  const profileText = compact(await readTextBounded(profile))
  check(
    '切到说书人面还认得我：账号资料区在、用户名对得上（不用再登一次）',
    switched &&
      storytellerRendered !== null &&
      (await profile.count()) > 0 &&
      profileText.includes(seatAccount.username),
    `换面=${switched ? '已到' : '没到'}；渲染=${storytellerRendered?.id ?? '（没有认出的元素）'}；资料区=${profileText || '（空）'}`,
  )
  check(
    '切面之后没有把人退回登录卡（口令输入框不该在）',
    (await stranger.page.getByTestId('account-password').count()) === 0,
    `口令输入框=${await stranger.page.getByTestId('account-password').count()}`,
  )
  await screenshot(stranger.page, 'entrance-face-switch')

  check(
    '全程没有未预期的控制台错误',
    consoleErrors.length === 0,
    consoleErrors.slice(0, 3).join(' | ') || '零错误',
  )
}

/**
 * 本文档加载了几次（Performance API 的导航条目数）。
 *
 * 用来判"换面到底有没有整页跳转"：文档每重载一次就多一条导航条目。取不到（浏览器不支持）时返回 null，
 * 调用方据此把这条判成红——**不许**在拿不到读数时当成功。
 */
async function readDocumentLoads(page) {
  return await page
    .evaluate(() => (typeof performance?.getEntriesByType === 'function' ? performance.getEntriesByType('navigation').length : null))
    .catch(() => null)
}

/**
 * 「未登录时只有一张登录卡」逐面判定：登录卡在、别的入口面元素一个都不该在。
 * 判据用**存在性**（`count()`），不是可见性——票据输入框藏在折叠区里也算"还在"。
 *
 * `loginCard` 传 `false` 用于**首页**：首页按设计不做登录（`HomePanel.vue` 的说明），
 * 它只负责"说清这是什么 + 指两个方向"——所以在首页上"没有登录卡"才是对的。
 *
 * @param {'card'|'none'} expectation 这一面未登录时该不该有登录卡。
 */
async function inspectGate(page, label, url, expectation = 'card') {
  await openFace(page, url)
  await settle()
  const counts = {
    loginCard: await page.getByTestId('account-username').count(),
    password: await page.getByTestId('account-password').count(),
    lobby: await page.getByTestId('player-lobby').count(),
    tableRows: await page.locator('[data-table]').count(),
    seatButtons: await page.locator('[data-seat]').count(),
    storytellerTicket: await page.getByTestId('storyteller-ticket').count(),
    seatTicketInput: await page.getByPlaceholder('席位票据').count(),
    openTableForm: await page.getByTestId('open-table-submit').count(),
  }

  check(
    expectation === 'card'
      ? `未登录打开${label}：有一张登录卡（账号表单）`
      : `未登录打开${label}：刻意不做登录（首页只指方向）`,
    expectation === 'card' ? counts.loginCard === 1 && counts.password === 1 : counts.loginCard === 0,
    `账号输入框=${counts.loginCard}；口令输入框=${counts.password}`,
  )
  check(
    `未登录打开${label}：没有桌列表`,
    counts.lobby === 0 && counts.tableRows === 0,
    `大厅容器=${counts.lobby}；桌行=${counts.tableRows}`,
  )
  check(
    `未登录打开${label}：没有席位按钮`,
    counts.seatButtons === 0,
    `席位按钮=${counts.seatButtons}`,
  )
  check(
    `未登录打开${label}：没有票据输入框`,
    counts.storytellerTicket === 0 && counts.seatTicketInput === 0,
    `说书人票据框=${counts.storytellerTicket}；席位票据框=${counts.seatTicketInput}`,
  )
  check(
    `未登录打开${label}：没有开桌表单`,
    counts.openTableForm === 0,
    `开桌提交按钮=${counts.openTableForm}`,
  )
  return counts
}

/** 禁词扫描：扫这一面渲染出来的正文（含顶栏）。 */
async function scanForbiddenTerms(page, label, url) {
  await openFace(page, url)
  await settle()
  const text = compact(await readTextBounded(page.locator('body')))
  const hits = findForbiddenTerms(text)
  check(
    `${label}上不出现开发者术语`,
    hits.length === 0,
    hits.length === 0 ? `已扫 ${text.length} 字，零命中` : hits.map((hit) => `「${hit.term}」…${hit.context}…`).join(' / '),
  )
}

/** 注册完后开一桌；解出桌标识供后两段定位。 */
async function openTable(page) {
  const formReady = await waitForCount(page.getByTestId('open-table-submit'), 1, uiWaitMs)
  if (!formReady) {
    check('登录后给出开桌表单', false, '页面上没有 open-table-submit')
    throw new Error('前置未成立：找不到开桌表单')
  }

  await page.getByTestId('open-table-name').fill(tableName)
  await page.getByTestId('open-table-seats').fill(String(options.seats))
  await page.getByTestId('open-table-submit').click()

  // 受理证据按改造前后两种形态认：
  //   今天 = 一次性票据回执（`new-table-ticket`）；改造后 = 「我主持的桌」里多出来的一行。
  // 判据必须是**那一行**而不是列表容器——容器在登录后就在，拿它当证据会当场假绿。
  const deadline = Date.now() + uiWaitMs
  let evidence = null
  for (;;) {
    const row = page.locator('[data-my-table]').first()
    if ((await row.count().catch(() => 0)) > 0) {
      evidence = {
        id: 'my-tables',
        gameId: await row.getAttribute('data-my-table').catch(() => null),
        text: compact(await readTextBounded(row)),
      }
      break
    }

    const ticket = page.getByTestId('new-table-ticket')
    if ((await ticket.count().catch(() => 0)) > 0) {
      const text = compact(await readTextBounded(ticket))
      evidence = {
        id: 'new-table-ticket',
        gameId: text.includes(':') ? text.slice(0, text.indexOf(':')) : text,
        text,
      }
      break
    }

    if (Date.now() >= deadline) {
      break
    }

    await sleep(150)
  }

  createdGameId = evidence?.gameId ?? null
  check(
    '开桌被受理（有回执，或「我主持的桌」里出现了这一桌）',
    createdGameId !== null && createdGameId.length > 0,
    `证据=${evidence?.id ?? '（没有）'}；桌标识=${createdGameId ?? '（没有）'}`,
  )
  if (createdGameId === null || createdGameId.length === 0) {
    throw new Error('前置未成立：没有开成桌，后面的段无从判起')
  }
}

/**
 * 把登录卡切到「注册」页签（目标形态里登录 / 注册是两个页签，默认登录）。
 * 返回是否真的点了一下——这是用户为注册多付的一步，入座路径要把它数进去。
 */
async function revealRegisterTab(page) {
  if ((await page.getByTestId('account-display-name').count().catch(() => 0)) > 0) {
    return false
  }

  const tab = page.getByTestId('account-tab-register')
  if ((await tab.count().catch(() => 0)) === 0) {
    return false
  }

  await tab.click()
  await waitForCount(page.getByTestId('account-display-name'), 1, uiWaitMs)
  return true
}

/** 大厅里定位这一桌：优先按桌标识，退一步按桌名。列表是点开这一面之后异步取的，所以先等它出来。 */
async function waitForLobbyRow(page, gameId, name) {
  const deadline = Date.now() + uiWaitMs
  for (;;) {
    const row = await findLobbyRow(page, gameId, name)
    if (row !== null || Date.now() >= deadline) {
      return row
    }

    await sleep(150)
  }
}

async function findLobbyRow(page, gameId, name) {
  if (gameId !== null) {
    const byId = page.locator(`[data-table="${gameId}"]`)
    if ((await byId.count()) > 0) {
      return byId.first()
    }
  }

  const byName = page.locator('[data-table]').filter({ hasText: name })
  return (await byName.count()) > 0 ? byName.first() : null
}

/**
 * 把登录表单露出来：改造前它藏在说书人面的"开桌"折叠区里（`<details>` 的子节点在 DOM 里，
 * 但不可见，直接 `fill` 会卡在可见性检查上）。返回是否真的点了一下——"被折叠起来"本身就是
 * 一次多余的用户动作，入座路径要把它数进去。
 */
async function revealAccountForm(page) {
  const details = page.getByTestId('storyteller-open-table')
  if ((await details.count()) === 0) {
    return false
  }

  const opened = await details.evaluate((element) => element.hasAttribute('open')).catch(() => true)
  if (opened) {
    return false
  }

  await details.locator('summary').click()
  await waitForCount(page.getByTestId('account-username'), 1, uiWaitMs)
  return true
}

/** 打开一面（整页加载）并等顶栏画出来——扫描/判定都针对渲染后的这一面。 */
async function openFace(page, url) {
  await page.goto(url, { waitUntil: 'domcontentloaded' })
  await waitForCount(page.getByTestId('top-nav'), 1, uiWaitMs)
}

/**
 * 在同一份文档里换面之后，等顶栏把"当前面"标到目标面上（`aria-current="page"`）。
 *
 * 换面是同一个文档里的重渲染，`click()` 返回时 DOM 还没换完：不等就会读到**上一面**的元素
 * （首跑实测：点了「加入一桌」之后仍看到说书人面的开桌折叠区，于是去点它那个已经消失的 summary，
 * 白等满 30 秒超时）。
 */
async function waitForFace(page, testId) {
  const deadline = Date.now() + uiWaitMs
  for (;;) {
    if ((await readAttributeBounded(page.getByTestId(testId), 'aria-current')) === 'page') {
      return true
    }

    if (Date.now() >= deadline) {
      return false
    }

    await sleep(100)
  }
}

async function settle() {
  await sleep(SETTLE_MS)
}

/** 正文里命中的禁词与上下文（每条最多报一次，取首次出现处）。 */
function findForbiddenTerms(text) {
  const hits = []
  for (const term of FORBIDDEN_TERMS) {
    const index = text.indexOf(term)
    if (index < 0) {
      continue
    }

    const from = Math.max(0, index - 16)
    hits.push({ term, context: text.slice(from, index + term.length + 16) })
  }

  return hits
}

/** 用户动作计数：点击与输入各算一步，顺带留下可读的路径轨迹。 */
function createStepCounter() {
  const steps = []
  return {
    get count() {
      return steps.length
    },
    record(label) {
      steps.push(label)
    },
    async click(locator, label) {
      steps.push(label)
      await locator.click()
    },
    async fill(locator, value, label) {
      steps.push(label)
      await locator.fill(value)
    },
    trace() {
      return steps.map((step, index) => `${index + 1}.${step}`).join(' → ')
    },
  }
}

async function newClient(browserInstance, consoleErrors) {
  const context = await browserInstance.newContext({ viewport: { width: 1440, height: 960 } })
  const page = await context.newPage()
  page.on('console', (message) => {
    if (message.type() === 'error') {
      consoleErrors.push(message.text())
    }
  })
  page.on('pageerror', (error) => consoleErrors.push(error.message))
  return { context, page }
}

/** 等定位器数量达到期望值（超时按毫秒）；返回是否达到。 */
async function waitForCount(locator, expected, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if ((await locator.count().catch(() => 0)) >= expected) {
      return true
    }

    await sleep(150)
  }

  return (await locator.count().catch(() => 0)) >= expected
}

/** 等一组 testid 里任意一个出现；返回 { id, text }，超时返回 null。 */
async function waitForAnyTestId(page, ids, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  for (;;) {
    for (const id of ids) {
      const locator = page.getByTestId(id)
      if ((await locator.count().catch(() => 0)) > 0) {
        return { id, text: await readTextBounded(locator.first()) }
      }
    }

    if (Date.now() >= deadline) {
      return null
    }

    await sleep(150)
  }
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
    // 工作目录 = 产物目录：宿主的内容根取当前工作目录，页面（wwwroot）就在产物目录里。
    // 从仓库根启动时内容根会是仓库根，于是 / 变 404 而 /healthz 照常 200——这种"半个能跑"最难查（实测踩过）。
    cwd: path.dirname(serverExecutable),
    env: {
      ...process.env,
      ASPNETCORE_URLS: serverUrl,
      GameServer__DatabasePath: databasePath,
      GameServer__SeatCount: String(options.seats),
      GameServer__SlotQuotaSeconds: String(config.quotaSeconds),
      GameServer__PacerIntervalMilliseconds: '200',
      DOTNET_ENVIRONMENT: 'Production',
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  })
  children.push(child)
  child.stdout.on('data', (chunk) => process.stdout.write(`[host] ${String(chunk)}`))
  child.stderr.on('data', (chunk) => process.stderr.write(`[host] ${String(chunk)}`))
  await waitForHttp(`${serverUrl}/healthz`, '宿主 /healthz', 90_000)
}

async function startVite() {
  // 直接跑 Vite 的入口脚本（不经 npm、不经 shell）：**单个**进程一个 PID，收尾一次结束即可。
  // 用 `npm run dev` 会套一层 shell，杀 shell 会留下孤儿 vite（实测踩过）。
  const vite = spawn(
    process.execPath,
    [path.join(webRoot, 'node_modules', 'vite', 'bin', 'vite.js'), '--port', String(options.vitePort), '--strictPort'],
    {
      cwd: webRoot,
      env: { ...process.env, VITE_SERVER_TARGET: serverUrl },
      stdio: ['ignore', 'pipe', 'pipe'],
    },
  )
  children.push(vite)
  vite.stdout.on('data', (chunk) => process.stdout.write(`[vite] ${String(chunk)}`))
  vite.stderr.on('data', (chunk) => process.stderr.write(`[vite] ${String(chunk)}`))
  await waitForHttp(viteUrl, 'Vite 开发服务器', 60_000)
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

    await sleep(100)
  }

  throw new Error(`${label} 在 ${timeoutMs}ms 内没有就绪：${lastError}`)
}

async function screenshot(page, name) {
  if (!config.screenshots) {
    return
  }

  try {
    mkdirSync(screenshotsDir, { recursive: true })
    const file = path.join(screenshotsDir, `entrance-${name}.png`)
    await page.screenshot({ path: file, fullPage: true })
    console.log(`  截图：${file}`)
  } catch (error) {
    console.warn(`  截图失败（不影响判定）：${error instanceof Error ? error.message : String(error)}`)
  }
}

function parseArguments(argv) {
  const parsed = { port: 5397, vitePort: 5396, seats: 5, timeoutMs: 15_000 }
  for (let index = 0; index < argv.length; index += 1) {
    const flag = argv[index]
    const value = argv[index + 1]
    switch (flag) {
      case '--port':
        parsed.port = Number.parseInt(value ?? '', 10)
        index += 1
        break
      case '--vite-port':
        parsed.vitePort = Number.parseInt(value ?? '', 10)
        index += 1
        break
      case '--seats': {
        const seats = Number.parseInt(value ?? '', 10)
        if (!Number.isInteger(seats) || seats < 1 || seats > 20) {
          throw new Error(`--seats 必须是 1–20 的整数，收到：${value}`)
        }

        parsed.seats = seats
        index += 1
        break
      }
      case '--timeout': {
        const seconds = Number.parseInt(value ?? '', 10)
        if (!Number.isInteger(seconds) || seconds < 5) {
          throw new Error(`--timeout 必须是不小于 5 的整数秒，收到：${value}`)
        }

        parsed.timeoutMs = seconds * 1000
        index += 1
        break
      }
      default:
        throw new Error(`未知参数：${flag}（用法见文件头）`)
    }
  }

  return parsed
}

/**
 * 收尾：先结束自己拉起的**单个**进程（/T 会连子进程一起结束，属于递归删除红线的规避对象），
 * 等它们真的退出后再删临时工作目录——Windows 上 taskkill 是异步的，立刻删库会因句柄占用失败。
 */
async function cleanup() {
  const closeBrowser = browser !== null ? browser.close().catch(() => {}) : Promise.resolve()
  browser = null
  killChildren()
  await Promise.all([closeBrowser, waitForChildrenExit(5_000)])
  for (let attempt = 0; attempt < 5; attempt += 1) {
    try {
      rmSync(workspace, { recursive: true, force: true })
      return
    } catch {
      await sleep(300)
    }
  }

  console.warn(`临时目录未能删除：${workspace}`)
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

    await sleep(50)
  }
}

function compact(text) {
  return text.replace(/\s+/g, ' ').trim()
}

function sleep(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds))
}
