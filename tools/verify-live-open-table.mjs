#!/usr/bin/env node
/**
 * 部署后真机验收装置（D-0026 / D-0027）—— 「登录即可开桌，开完自己主持」的真实部署判据。
 *
 * 它回答一个**别处证明不了**的问题：把包真的装到服务器上之后，一个全新普通账号
 * 能不能自己开一桌并主持起来。其它装置都在本机临时库上跑（自包含、可重复），
 * 而"权限到底放开了没有""部署形态下页面与 Hub 通不通"只能在真部署上验：
 * 真 nginx 前缀 · 真宿主 · 真持久库 · 真浏览器。
 *
 * 覆盖的链路（一段一条判据）：
 *   home      首页在部署前缀下打得开，两个入口都在
 *   register  说书人面注册一个**全新随机账号** → 服务端给出"能开桌"的能力位
 *   open      开一桌 → 桌标识拿到手，「我主持的桌」里出现这一桌（**回执里没有凭据**，D-0027）
 *   lobby     未登录的玩家面只有登录卡；同一浏览器切过去能看到这一桌（账号会话跨面）
 *   host      从「我主持的桌」点进主持台：魔典可见、席位数与开桌时一致
 *   setup     一键配板覆盖每一席 → 提交分配被受理
 *   night     开夜被受理 → 槽位由 0 前进（说书人真的主持起来了）
 *   invite    说书人签发邀请码 → **覆盖即轮换**（旧的一枚当场作废）→ 旅行者（另一台设备、另一个账号）
 *             凭新的一枚入座（D-0038 的凭据路径在**部署形态**下的判据；库里只有哈希，读不回来）
 *   reconnect 刷新页面 → **自动接回主持台**（M1：不重新登录、不点「进主持台」）；新标签页仍要重新
 *             登录（关标签页即清），登录后那张桌还在、点一下接回主持台（桌跟着账号走）
 *
 * 用法（在仓库根运行）：
 *   node tools/verify-live-open-table.mjs --base-url http://<主机>/<前缀>/
 *   node tools/verify-live-open-table.mjs --base-url ... --seats 5      # 开桌席位数（默认 7）
 *   node tools/verify-live-open-table.mjs --base-url ... --timeout 60   # 单步超时（秒，默认 30）
 *   node tools/verify-live-open-table.mjs --list-sections               # 只列段名，不跑
 *   node tools/verify-live-open-table.mjs --base-url ... --only night   # 执行到该段为止，且只判该段
 *   node tools/verify-live-open-table.mjs --base-url ... --legacy-code '桌标识:席位邀请码'
 *                                                                       # 迁移现场：升级前那一版签发的
 *                                                                       # 旧码必须进不来（§6.6 / §9.3）
 *
 * 分段口径与其它装置一致：`--only X` 执行到 X 为止（前面的段作为必要前置照样跑，只有 X 的断言计入判定）；
 * `--from X` 全程照跑、从该段起计入判定；两者互斥。**远程一次红跑很贵**（每次都在真服务器上留数据），
 * 所以迭代期优先用 `--only` 定点，并靠收尾打印的清理 SQL 收干净。
 *
 * **它会真的在目标服务器上留下**：两个测试账号（开桌的那个 + 旅行者）+ 一张测试桌（外加这一桌的事件）。
 * 装置不能替你连 SSH，所以收尾会把这批数据的**清理 SQL** 原样打印出来，由部署者执行
 * （**先停服务再删**：开着的桌活在宿主内存里，不停服务就删会被写回来）。
 * 账号名 / 桌名都带本次运行的时间戳，一眼能认出是验收残留。
 *
 * 退出码：0 = 全部通过；1 = 有失败；2 = 环境问题（参数不合法 / 目标站点探活不通 / 缺 Playwright）。
 * 前置：`cd web; npm install`（Playwright 装在 web/node_modules）。
 */
import { mkdirSync } from 'node:fs'
import { createRequire } from 'node:module'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { readTextBounded } from './lib/bounded-text.mjs'
import { issueInviteCode } from './lib/entrance.mjs'
import { createChecker, createSectionRunner } from './lib/verify-sections.mjs'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const webRoot = path.join(repositoryRoot, 'web')
const screenshotsDir = path.join(repositoryRoot, 'artifacts', 'web')

const SECTIONS = [
  { id: 'probe', title: '探活：目标站点可达（地址 / 前缀写错时以退出码 2 收场）' },
  { id: 'home', title: '首页在部署前缀下打得开（两个入口都在）' },
  { id: 'register', title: '说书人面：注册一个全新账号 → 服务端给出"能开桌"能力位' },
  { id: 'open', title: '开一桌：桌标识拿到手，「我主持的桌」里出现这一桌（回执里没有凭据）' },
  { id: 'lobby', title: '未登录的玩家面只有登录卡；同一浏览器切过去能看到这一桌（会话跨面）' },
  { id: 'host', title: '点「进主持台」：魔典可见、席位数与开桌一致' },
  { id: 'setup', title: '一键配板 + 提交分配被受理' },
  { id: 'night', title: '开夜被受理 → 槽位由 0 前进（真的主持起来了）' },
  {
    id: 'invite',
    title: '邀请码：说书人签发 → 覆盖即轮换（旧的一枚当场作废）→ 旅行者凭新的一枚入座（D-0038）',
  },
  { id: 'reconnect', title: '刷新后自动接回这张桌（M1 行 1）+ 新标签页必须重新登录（关标签页即清）' },
]

/** 退出码 2 = 参数 / 环境问题（与"断言失败"的 1 分开，部署者一眼能分清该查哪边）。 */
let options = null
try {
  options = parseArguments(process.argv.slice(2))
} catch (error) {
  console.error(`参数错误：${error instanceof Error ? error.message : String(error)}`)
  process.exit(2)
}

if (options.listSections) {
  console.log('可用段落（按执行顺序；--only 与 --from 互斥）：')
  for (const section of SECTIONS) {
    console.log(`  ${section.id.padEnd(12)} ${section.title}`)
  }

  process.exit(0)
}

const runner = createSectionRunner(SECTIONS, { only: options.only, from: options.from })
const checker = createChecker({
  sections: SECTIONS,
  isJudged: runner.isJudged,
  currentSection: () => runner.currentId,
})
const check = checker.check

if (options.baseUrl === null) {
  console.error('用法：node tools/verify-live-open-table.mjs --base-url http://<主机>/<前缀>/ [--seats 7] [--timeout 30]')
  process.exit(2)
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

/** 本次运行的标记：账号与桌名都带它，服务器上一眼能认出是验收残留。 */
const stamp = Date.now().toString(36)
const username = `e2e-open-${stamp}`
const displayName = `开桌验收${stamp}`
const password = `pw-${stamp}-oct`
const tableName = `e2e验收桌${stamp}`

/** 装置自己产生的数据，收尾用它打印清理 SQL（账号是**两个**：开桌的说书人 + 凭码入座的旅行者）。 */
const created = { username, gameId: null, tableName, accounts: [username] }

let browser = null

try {
  await main()
  runner.reportTimings()
  await cleanup()
  checker.report()
  reportLeftovers()
  process.exit(checker.results.some((result) => result.outcome === 'fail') ? 1 : 0)
} catch (error) {
  console.error(`\n[FAIL] 装置异常终止：${error instanceof Error ? error.stack : String(error)}`)
  checker.results.push({ section: runner.currentId, label: '脚本执行到底', outcome: 'fail', detail: '见上方异常' })
  await cleanup()
  checker.report()
  reportLeftovers()
  process.exit(1)
}

async function main() {
  console.log(`目标：${options.baseUrl}`)
  console.log(`本次账号：${username}（随机新账号，不在任何运维名单里）`)

  if (!runner.begin('probe')) return
  // 先探活再起浏览器：地址 / 子路径前缀写错时给**退出码 2**（"环境不对"），
  // 不要让它混进后面那条 `page.goto` 抛异常 → 退出码 1（"产品红"）里——那会让人查错方向。
  const health = await probeHealth()
  if (!health.ok) {
    console.error(`\n[环境] 目标站点探活不通过：${options.baseUrl}healthz → ${health.detail}`)
    console.error('  地址或子路径前缀写错了？装置以退出码 2 收场（与"断言失败"的 1 区分开）。')
    await cleanup()
    process.exit(2)
  }

  console.log(`  探活通过：${options.baseUrl}healthz → ${health.detail}`)

  browser = await playwright.chromium.launch()
  const context = await browser.newContext({ viewport: { width: 1440, height: 960 } })
  const page = await context.newPage()
  const consoleErrors = []
  page.on('console', (message) => {
    if (message.type() === 'error') {
      consoleErrors.push(message.text())
    }
  })
  page.on('pageerror', (error) => consoleErrors.push(error.message))

  if (!runner.begin('home')) return
  await openAt(page, `${options.baseUrl}home`)
  const homeReady = await waitForCount(page.getByTestId('home-to-storyteller'), 1, options.timeoutMs)
  check('首页在部署前缀下打得开（真 nginx + 真宿主）', homeReady, options.baseUrl)
  check(
    '首页给出玩家端与说书人端两个入口',
    (await page.getByTestId('home-to-player').count()) === 1,
    `入口数=${await page.getByTestId('home-to-player').count()}`,
  )

  if (!runner.begin('register')) return
  await openAt(page, `${options.baseUrl}storyteller`)
  await revealRegisterTab(page)
  await page.getByTestId('account-username').fill(username)
  await page.getByTestId('account-display-name').fill(displayName)
  await page.getByTestId('account-password').fill(password)
  await page.getByTestId('account-register').click()

  const registered = await waitForCount(page.getByTestId('account-profile'), 1, options.timeoutMs)
  const profileText = registered ? compact(await readTextBounded(page.getByTestId('account-profile'))) : ''
  check(`全新账号注册并登录成功（${username}）`, registered, profileText || '资料区没有出现')

  // 一次性恢复码是**只回一次**的秘密（服务端只存哈希）：界面上不显示就等于永久丢失，
  // 忘了口令再也找不回这个账号。开桌区对所有玩家开放之后，这条路径就是普通人的注册路径。
  const recoveryShown = (await page.getByTestId('account-recovery-code').count()) > 0
  const recoveryText = recoveryShown ? compact(await readTextBounded(page.getByTestId('account-recovery-code'))) : ''
  check('注册回执里的一次性恢复码当场可见（丢了再也找不回）', recoveryShown && recoveryText.length > 0, recoveryText || '没有显示恢复码')

  // 行为判据，不解析文案：不能开桌时界面上会挂出"被挡住"的提示、按钮也会禁用。
  const blocked = await page.getByTestId('open-table-blocked').count()
  const submitDisabled = await page.getByTestId('open-table-submit').isDisabled()
  check(
    '服务端给出"这个账号能开桌"的能力位（默认放开自助开桌）',
    blocked === 0 && !submitDisabled,
    `挡板提示=${blocked}；按钮禁用=${submitDisabled}`,
  )

  if (!runner.begin('open')) return
  await page.getByTestId('open-table-name').fill(tableName)
  await page.getByTestId('open-table-seats').fill(String(options.seats))
  await page.getByTestId('open-table-submit').click()

  // D-0027：受理证据是「我主持的桌」里多出来的那一行，**不是**一串要被抄下来的凭据。
  const row = page.locator('[data-my-table]').first()
  const listedOwn = await waitForCount(row, 1, options.timeoutMs)
  check('开桌成功：这一桌出现在「我主持的桌」里', listedOwn, listedOwn ? compact(await readTextBounded(row)) : '列表里没有这一桌')

  created.gameId = listedOwn ? await row.getAttribute('data-my-table') : null
  check(
    '开桌回执里没有任何凭据（票据已随 D-0027 退场：进主持台只认开桌账号）',
    (await page.getByTestId('new-table-ticket').count()) === 0 && created.gameId !== null,
    `票据元素=${await page.getByTestId('new-table-ticket').count()}；桌标识=${created.gameId ?? '（没有）'}`,
  )
  await screenshot(page, 'open-table')

  if (!runner.begin('lobby')) return
  // 未登录的玩家面：只有一张登录卡（D-0027 的第一条判据，在**部署形态**下同样要成立）。
  const stranger = await context.newPage()
  stranger.on('pageerror', (error) => consoleErrors.push(error.message))
  await openAt(stranger, `${options.baseUrl}play`)
  const strangerLobby = await stranger.getByTestId('player-lobby').count()
  const strangerSeats = await stranger.locator('[data-seat]').count()
  check(
    '未登录的玩家面只有登录卡（没有桌列表、没有席位按钮）',
    strangerLobby === 0 && strangerSeats === 0,
    `大厅容器=${strangerLobby}；席位按钮=${strangerSeats}`,
  )
  await stranger.close()

  // 同一个浏览器切到玩家面：账号会话跨面共享（D-0027），所以大厅里看得到刚开的那一桌。
  await page.getByTestId('nav-player').click()
  const newTableRow = page.locator(`[data-table="${created.gameId}"]`)
  const listed = await waitForCount(newTableRow, 1, options.timeoutMs)
  const rowText = listed ? compact(await readTextBounded(newTableRow)) : ''
  check('同一浏览器切到玩家面（不用再登一次）：大厅里看得到这张新桌', listed, rowText || '大厅里没有这一桌')
  check(
    `新桌人数与席位对得上（0 / ${options.seats}）`,
    rowText.includes(`0 / ${options.seats}`),
    rowText,
  )
  await screenshot(page, 'lobby-with-new-table')

  if (!runner.begin('host')) return
  // 回说书人面，从「我主持的桌」点进主持台。
  await page.getByTestId('nav-storyteller').click()
  const backRow = page.locator(`[data-my-table="${created.gameId}"]`)
  await waitForCount(backRow, 1, options.timeoutMs)
  await backRow.getByTestId('host-enter').click()
  const grimoire = await waitForCount(page.getByTestId('grimoire'), 1, options.timeoutMs)
  check('从「我主持的桌」进主持台（魔典可见）', grimoire, grimoire ? '魔典已渲染' : '魔典没出现')
  const seatRows = await page.locator('section', { hasText: '开局分配' }).locator('tbody tr').count()
  check(`主持台的席位数与开桌时一致（${options.seats}）`, seatRows === options.seats, `UI 席位数=${seatRows}`)
  await screenshot(page, 'storyteller-console')

  if (!runner.begin('setup')) return
  const selects = page.locator('section', { hasText: '开局分配' }).locator('select')
  if (!(await selects.first().isVisible())) {
    await page.getByTestId('st-assignment-toggle').click()
  }

  await page.getByTestId('st-assignment-randomize').click()
  const proposed = await waitForCount(page.getByTestId('st-assignment-distribution'), 1, options.timeoutMs)
  const values = await selects.evaluateAll((nodes) => nodes.map((node) => node.value))
  check(
    '一键配板覆盖每一席（无「未选择」）',
    proposed && values.length === options.seats && values.every((value) => value.length > 0),
    values.join(', ') || '没有建议',
  )

  const assigned = await runCommand(page, () => page.getByRole('button', { name: '提交分配' }).click())
  check('提交分配被受理', assigned.kind === 'Accepted', assigned.raw)
  await screenshot(page, 'assigned')

  if (!runner.begin('night')) return
  const nightStarted = await runCommand(page, () => page.getByRole('button', { name: /开夜/ }).click())
  check('开夜被受理（说书人真的主持起来了）', nightStarted.kind === 'Accepted', nightStarted.raw)

  const slotBefore = await readSlotCounter(page)
  const advanced = await waitForSlotAdvance(page, slotBefore?.index ?? null, options.timeoutMs)
  check(
    '开夜后槽位由 0 前进（阶段真的在跑，不是只回了一条受理）',
    slotBefore !== null && advanced !== null && advanced > slotBefore.index,
    slotBefore === null ? '槽位计数不可读' : `开夜前 ${slotBefore.index} → 现在 ${advanced}`,
  )
  await screenshot(page, 'night-started')

  if (!runner.begin('invite')) return
  // D-0038：邀请码是**签发出来的**——库里只有哈希，明文只在签发那一次出现（"直读库里的票据"那条路已退场）。
  // 这一段必须在**部署形态**下跑：真 nginx 前缀 + 真宿主 + 真持久库，"装上去还成立吗"只有这里回答得了。
  const inviteSeat = options.seats
  const firstCode = await issueInviteCode(page, inviteSeat)
  check(
    `说书人面板为 ${inviteSeat} 号席签发邀请码（形态「桌标识:席位邀请码」）`,
    firstCode.startsWith(`${created.gameId}:`),
    `码指纹 ${briefCode(firstCode)}`,
  )

  // 覆盖即轮换：同一席再签一枚，上一枚当场作废（码发错人时的收场动作）。
  const secondCode = await issueInviteCode(page, inviteSeat)
  check(
    '同一席重新签发即轮换：新的一枚与上一枚不同（旧的当场作废）',
    secondCode !== firstCode && secondCode.startsWith(`${created.gameId}:`),
    `旧 ${briefCode(firstCode)} → 新 ${briefCode(secondCode)}`,
  )
  await screenshot(page, 'invite-issued')

  // 旅行者是**另一台设备**：另一个浏览器上下文 + 另一个账号（一账号一局只坐一席，也不能共用说书人那一个）。
  const travellerContext = await browser.newContext({ viewport: { width: 1280, height: 900 } })
  const travellerPage = await travellerContext.newPage()
  travellerPage.on('pageerror', (error) => consoleErrors.push(error.message))
  const traveller = `e2e-trav-${stamp}`
  created.accounts.push(traveller)
  await openAt(travellerPage, `${options.baseUrl}play`)
  await revealRegisterTab(travellerPage)
  await travellerPage.getByTestId('account-username').fill(traveller)
  await travellerPage.getByTestId('account-display-name').fill(`旅行者${stamp}`)
  await travellerPage.getByTestId('account-password').fill(password)
  await travellerPage.getByTestId('account-register').click()
  const travellerReady = await waitForCount(travellerPage.getByTestId('account-profile'), 1, options.timeoutMs)
  check(
    `旅行者（另一台设备）注册并登录成功（${traveller}）`,
    travellerReady,
    travellerReady ? '资料区已出现' : '没有登录成功',
  )

  if (options.legacyCode !== null) {
    // 结构 v4 的迁移现场（部署文档 §6.6 / §9.3）：升级前那一版把邀请码**明文**存在 `Games.SeatsJson` 里，
    // 迁移把它们抹掉之后服务端只剩一张哈希表，而表里没有它——于是这枚"当年有效"的码必须进不来。
    const legacy = await joinWithCode(travellerPage, options.legacyCode)
    check(
      '升级前那一版签发的旧邀请码进不来（v4 迁移已把它从库里抹掉）',
      !legacy.seated && legacy.notice.length > 0,
      legacy.seated ? '竟然坐进去了——迁移没生效？' : legacy.notice || joinDiagnostic(legacy),
    )
  } else {
    console.log('  （未给 --legacy-code：跳过「升级前的旧码进不来」这一条）')
  }

  const stale = await joinWithCode(travellerPage, firstCode)
  check(
    '被轮换掉的那一枚进不来（当场被拒，席位没有被占）',
    !stale.seated && stale.notice.length > 0,
    stale.seated ? '竟然坐进去了' : stale.notice || joinDiagnostic(stale),
  )

  const fresh = await joinWithCode(travellerPage, secondCode)
  const seatTag = fresh.seated ? compact(await readTextBounded(travellerPage.getByTestId('player-seat'))) : ''
  check(
    `新签发的那一枚进得来：旅行者坐到 ${inviteSeat} 号席`,
    fresh.seated && seatTag.includes(`${inviteSeat} 号`),
    fresh.seated ? `席位标签「${seatTag}」` : fresh.notice || joinDiagnostic(fresh),
  )
  await screenshot(travellerPage, 'invite-traveller-seated')
  await travellerContext.close()

  if (!runner.begin('reconnect')) return
  // M1（D-0029）：刷新即**自动接回主持台**——凭据进 `sessionStorage`，前端启动时向服务端确认
  // （`Resume`）并按位置重新进桌：不填登录卡、不点「进主持台」。这正是这一轮要证明的"刷新不掉登录"。
  // "桌跟着账号走"（D-0027）没有变：位置只是"我刚才在哪"，能不能进去仍由服务端按开桌账号判定。
  await page.goto(`${options.baseUrl}storyteller`, { waitUntil: 'domcontentloaded' })
  await page.reload({ waitUntil: 'domcontentloaded' })

  // 刷新后的**第一眼**不许是登录卡：要么"正在恢复登录状态…"，要么已经回到主持台。
  // 这条比"最终回到了主持台"更贴近用户看到的东西——先闪一张"先登录"再跳回来，正是要消掉的跳变。
  const firstPaint = await Promise.race([
    page
      .getByTestId('account-gate')
      .waitFor({ timeout: options.timeoutMs })
      .then(() => 'gate')
      .catch(() => 'none'),
    page
      .getByTestId('account-restoring')
      .waitFor({ timeout: options.timeoutMs })
      .then(() => 'restoring')
      .catch(() => 'none'),
    page
      .getByTestId('grimoire')
      .waitFor({ timeout: options.timeoutMs })
      .then(() => 'grimoire')
      .catch(() => 'none'),
  ])
  check(
    'M1 行 1：刷新后第一眼不是登录卡（正在恢复登录态或已直接回到主持台）',
    firstPaint === 'restoring' || firstPaint === 'grimoire',
    `刷新后第一眼=${firstPaint}`,
  )

  const backInConsole = await waitForCount(page.getByTestId('grimoire'), 1, options.timeoutMs)
  check(
    'M1 行 1：刷新后自动接回主持台（不重新登录、不点「进主持台」）',
    backInConsole,
    backInConsole ? '魔典已渲染' : '没有自动接回',
  )
  const seatRowsAfterReload = await page.locator('section', { hasText: '开局分配' }).locator('tbody tr').count()
  check(
    `接回的是同一张桌（席位数仍是 ${options.seats}）`,
    seatRowsAfterReload === options.seats,
    `UI 席位数=${seatRowsAfterReload}`,
  )
  await screenshot(page, 'reconnected')

  // M1 行 2 / 行 3：首页要认出回来的人——显示登录身份，并给一张「回到我那一桌」直达卡；
  // 点它接回主持台（位置记着"我正在主持哪一桌"，能不能进去仍由服务端按开桌账号判定）。
  await page.getByTestId('nav-home').click()
  let homeIdentity = false
  try {
    await page.getByTestId('home-profile').waitFor({ timeout: options.timeoutMs })
    await page.getByTestId('home-back-to-table').waitFor({ timeout: options.timeoutMs })
    homeIdentity = true
  } catch {
    homeIdentity = false
  }

  const homeText = homeIdentity ? compact(await readTextBounded(page.getByTestId('home-identity'))) : ''
  check(
    'M1 行 2：首页显示登录身份 + 「回到我那一桌」入口',
    homeIdentity && homeText.includes(username),
    homeText.slice(0, 160) || '首页没有身份块',
  )

  let homeBackToConsole = false
  if (homeIdentity) {
    await page.getByTestId('home-back-to-table').click()
    homeBackToConsole = await waitForCount(page.getByTestId('grimoire'), 1, options.timeoutMs)
  }

  check(
    'M1 行 3：从首页点「回到我那一桌」接回主持台（离开再回来落点不变）',
    homeBackToConsole,
    homeBackToConsole ? '魔典已渲染' : '没有接回',
  )
  await screenshot(page, 'home-identity')

  // 反方向 + 换设备（新标签页 = 关掉原标签页重开）：`sessionStorage` 每个标签页一份，
  // 所以这里**必须重新登录**；登录之后这一桌仍在「我主持的桌」里，点一下就能接回。
  // M1 的"关标签页即清"与 D-0027 的"桌跟着账号走"在这里同时成立。
  const freshTab = await context.newPage()
  freshTab.on('pageerror', (error) => consoleErrors.push(error.message))
  await openAt(freshTab, `${options.baseUrl}storyteller`)
  const freshGate = await waitForCount(freshTab.getByTestId('account-gate'), 1, options.timeoutMs)
  check(
    'M1 行 1 反方向：新标签页必须重新登录（凭据不跨标签页、不长期驻留设备）',
    freshGate,
    freshGate ? '新标签页落在登录卡' : '新标签页没出现登录卡——凭据泄漏到共享存储了？',
  )

  let freshBack = false
  if (freshGate) {
    await freshTab.getByTestId('account-username').fill(username)
    await freshTab.getByTestId('account-password').fill(password)
    await freshTab.getByTestId('account-login').click()
    await waitForCount(freshTab.getByTestId('account-profile'), 1, options.timeoutMs)

    const freshRow = freshTab.locator(`[data-my-table="${created.gameId}"]`)
    const freshListed = await waitForCount(freshRow, 1, options.timeoutMs)
    check('换设备回来：那一桌仍在「我主持的桌」里（桌不跟着浏览器走）', freshListed, freshListed ? '在列表里' : '列表里没有它')
    if (freshListed) {
      await freshRow.getByTestId('host-enter').click()
      freshBack = await waitForCount(freshTab.getByTestId('grimoire'), 1, options.timeoutMs)
    }
  }

  check('换设备回来：点一下就接回主持台（不用再抄任何凭据）', freshBack, freshBack ? '魔典已渲染' : '没有接回')
  await screenshot(freshTab, 'reconnected-fresh-tab')
  await freshTab.close()

  check('全过程没有未预期的控制台错误', consoleErrors.length === 0, consoleErrors.slice(0, 3).join(' | ') || '零错误')
}

/** 登录卡上的「注册」页签（新用户要先切过去）。 */
async function revealRegisterTab(page) {
  const tab = page.getByTestId('account-tab-register')
  if ((await tab.count()) > 0) {
    await tab.click()
  }

  await waitForCount(page.getByTestId('account-display-name'), 1, options.timeoutMs)
}

/**
 * 在玩家面**用一枚邀请码试着入座**，把成败如实报回来（不抛错）。
 *
 * 正路与拒绝路径都要用它：`lib/entrance.mjs` 的 `seatByInviteCode` 只服务前者（失败即抛），
 * 而"旧的一枚进不来"这类**否定判据**必须读到界面上那句拒绝理由才算数——
 * 只判"没有 seat 元素"会把连接错误、页面没加载也算成通过。
 *
 * @returns {Promise<{seated: boolean, notice: string}>} `notice` 是界面给出的那句话（成功时为空）。
 */
async function joinWithCode(page, code) {
  const invite = page.getByTestId('seat-invite')
  if ((await invite.count()) === 0) {
    throw new Error('玩家面上没有邀请码入口（seat-invite）——目标站点的界面版本不对？')
  }

  const opened = await invite.evaluate((element) => element.hasAttribute('open')).catch(() => false)
  if (!opened) {
    await invite.locator('summary').click()
  }

  await page.getByTestId('seat-invite-code').fill(code)
  await page.getByTestId('seat-invite-join').click()

  // 红的时候要当场留下够查的东西（与 runCommand 同一个姿态）：把"界面在这 30 秒里到底停在哪一步"
  // 记成一条轨迹，结尾连同 DOM 概览一起带回去——否则只剩一句"既没入座也没理由"，查不动。
  //
  // ⚠️ 轮询里的读取**必须走有界读**（`readTextBounded`，见 `lib/bounded-text.mjs`）：入座成功后
  // `seat-invite` 整块会从 DOM 里消失，那时无界的 `innerText()` 会白等满 Playwright 的 30 秒默认
  // 超时，把整个判定窗口吃光——真机实测踩到：服务端 73ms 就回了"已入座"、界面也切过去了，
  // 装置却报"新签的那一枚进不来"（这条陷阱在 done/device-poll-innertext-unbounded-wait.md 里
  // 已经记过一次，本处是同一个坑的第四例）。
  const trace = []
  const deadline = Date.now() + options.timeoutMs
  while (Date.now() < deadline) {
    if ((await page.getByTestId('player-seat').count()) > 0) {
      return { seated: true, notice: '', trace }
    }

    const notice = compact(await readTextBounded(page.getByTestId('seat-invite-notice'), 500))
    const step = notice.length > 0 ? notice.slice(0, 80) : '（没有任何提示）'
    if (trace.at(-1) !== step) {
      trace.push(step)
    }

    // 成功那一刻也会先写一句「已凭邀请码入座」——它不是拒绝，继续等席位视图。
    if (notice.length > 0 && !notice.includes('已凭邀请码入座')) {
      return { seated: false, notice, trace }
    }

    await sleep(150)
  }

  const diag = {
    lobby: (await page.getByTestId('player-lobby').count()) > 0,
    invite: (await page.getByTestId('seat-invite').count()) > 0,
    seat: (await page.getByTestId('player-seat').count()) > 0,
    gate: (await page.getByTestId('account-gate').count()) > 0,
  }

  return { seated: false, notice: '', trace, diag }
}

/** 邀请码只留**短指纹**进日志（桌标识 + 尾巴 6 位）：明文是凭据，而且轮换前后靠尾巴才分得清。 */
function briefCode(code) {
  const separator = code.indexOf(':')
  if (separator < 0) {
    return `${code.slice(0, 6)}…`
  }

  return `${code.slice(0, separator + 1)}…${code.slice(-6)}`
}

/** 超时那一档的现场描述：界面轨迹 + DOM 概览（红的判据必须自带可查的东西）。 */
function joinDiagnostic(outcome) {
  const trace = outcome.trace.length > 0 ? outcome.trace.join(' → ') : '（一次状态都没读到）'
  const diag = outcome.diag === undefined || outcome.diag === null ? '（DOM 概览读不到）' : JSON.stringify(outcome.diag)
  return `超时：界面轨迹 ${trace}｜DOM ${diag}`
}

/** 收尾：只关浏览器。服务器的数据由部署者按报告里的 SQL 清理。 */
async function cleanup() {
  if (browser !== null) {
    await browser.close().catch(() => {})
  }
}

function reportLeftovers() {
  console.log('\n=== 本次在目标服务器上留下的数据（请按需清理）===')
  console.log(`  测试账号：${created.accounts.join('、')}（Users 表）`)
  console.log(`  测试桌　：${created.gameId ?? '（没有开成）'}（Games 表，桌名「${created.tableName}」）`)
  if (created.gameId !== null) {
    console.log('  在目标机上执行（把 <DB> 换成库文件路径；先确认这个桌标识不是你要保留的）：')
    console.log('    systemctl stop clocktower            # 必做：开着的桌活在宿主内存里，不停服务就删会被写回来')
    console.log(
      `    sqlite3 <DB> "DELETE FROM SeatInvitations WHERE GameId='${created.gameId}';` +
        ` DELETE FROM SeatBindings WHERE GameId='${created.gameId}';` +
        ` DELETE FROM Events WHERE GameId='${created.gameId}';` +
        ` DELETE FROM Snapshots WHERE GameId='${created.gameId}';` +
        ` DELETE FROM Receipts WHERE GameId='${created.gameId}';` +
        ` DELETE FROM Games WHERE GameId='${created.gameId}';` +
        ` DELETE FROM Users WHERE Username IN (${created.accounts.map((name) => `'${name}'`).join(', ')});"`,
    )
    console.log('    systemctl start clocktower')
  }
}

function parseArguments(argv) {
  const parsed = {
    baseUrl: null,
    seats: 7,
    timeoutMs: 30_000,
    only: [],
    from: undefined,
    listSections: false,
    legacyCode: null,
  }
  for (let index = 0; index < argv.length; index += 1) {
    const flag = argv[index]
    const value = argv[index + 1]
    switch (flag) {
      case '--base-url':
        parsed.baseUrl = normalizeBaseUrl(value ?? '')
        index += 1
        break
      case '--only':
        if (value === undefined) {
          throw new Error('--only 需要一个段名（用 --list-sections 看有哪些）')
        }

        parsed.only.push(value)
        index += 1
        break
      case '--from':
        if (value === undefined) {
          throw new Error('--from 需要一个段名（用 --list-sections 看有哪些）')
        }

        parsed.from = value
        index += 1
        break
      case '--list-sections':
        parsed.listSections = true
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
      case '--legacy-code': {
        // 迁移现场用（§6.6 / §9.3）：升级前那一版签发的码，升级后必须进不来。
        if (value === undefined || !value.includes(':')) {
          throw new Error('--legacy-code 需要「桌标识:席位邀请码」形态的一串')
        }

        parsed.legacyCode = value
        index += 1
        break
      }
      default:
        throw new Error(`未知参数：${flag}（用法见文件头）`)
    }
  }

  if (parsed.only.length > 0 && parsed.from !== undefined) {
    throw new Error('--only 与 --from 互斥：前者执行到该段为止，后者全程执行')
  }

  return parsed
}

/** 部署地址一律补成"以 / 结尾"，否则拼 hash 会拼到最后一个路径段上。 */
function normalizeBaseUrl(raw) {
  const trimmed = raw.trim()
  if (trimmed.length === 0) {
    return null
  }

  if (!/^https?:\/\//.test(trimmed)) {
    throw new Error(`--base-url 必须以 http:// 或 https:// 开头，收到：${raw}`)
  }

  return trimmed.endsWith('/') ? trimmed : `${trimmed}/`
}

/**
 * 打开一个地址并**强制整页重载**：同一个 SPA 的不同 hash 属于同文档导航，
 * 组件不会重新挂载——重载一次才能保证每一步都是"新打开这一页"的真实行为。
 */
async function openAt(page, url) {
  await page.goto(url, { waitUntil: 'domcontentloaded' })
  await page.reload({ waitUntil: 'domcontentloaded' })
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

/** 发命令：先记回执序号基线，再点击，再等一条序号更大的新回执；超时先留证据再抛。 */
async function runCommand(page, click) {
  const baseline = await readOutcomeSerial(page)
  await click()
  try {
    return await waitForOutcome(page, baseline, options.timeoutMs)
  } catch (error) {
    // 远端跑一次贵（真服务器 + 真数据），所以红的时候必须当场留下够查的东西：
    // 回执区原样 DOM + 一张截图，而不是只留一句"回执没出现"。
    console.log(`  [诊断] 回执未出现：${error instanceof Error ? error.message : String(error)}`)
    const html = await page
      .locator('[data-testid="outcome"]')
      .evaluate((element) => element.outerHTML)
      .catch(() => '（回执区不存在）')
    console.log(`  [诊断] 回执区 HTML：${compact(html).slice(0, 400)}`)
    await screenshot(page, 'diag-outcome-missing')
    throw error
  }
}

/** 探活：目标站点的健康检查（地址 / 前缀不对时给得出可读原因，供退出码 2 用）。 */
async function probeHealth() {
  try {
    const response = await fetch(`${options.baseUrl}healthz`, { signal: AbortSignal.timeout(10_000) })
    if (!response.ok) {
      return { ok: false, detail: `HTTP ${response.status}` }
    }

    const body = await response.json().catch(() => null)
    return { ok: true, detail: body === null ? '（响应不是 JSON）' : JSON.stringify(body) }
  } catch (error) {
    return { ok: false, detail: error instanceof Error ? error.message : String(error) }
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
    const state = await box
      .evaluate((element) => ({
        serial: Number(element.getAttribute('data-outcome-serial') ?? '0'),
        kind: element.querySelector('[data-outcome-marker]')?.getAttribute('data-outcome-marker') ?? null,
      }))
      .catch(() => ({ serial: 0, kind: null }))
    if (state.kind !== null && state.serial > baselineSerial) {
      const raw = compact(await readTextBounded(box))
      return { kind: state.kind, raw }
    }

    await sleep(100)
  }

  throw new Error(`命令回执在超时前没有出现（基线序号=${baselineSerial}）`)
}

/** 状态条上的槽位计数（X / Y）→ { index（从 0 起）, total }；读不到返回 null。 */
async function readSlotCounter(page) {
  return page.evaluate(() => {
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
}

/** 等槽位下标超过基线（服务端推送驱动，不刷新页面）；超时返回最后一次读到的下标。 */
async function waitForSlotAdvance(page, baselineIndex, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  let index = baselineIndex
  while (Date.now() < deadline) {
    index = (await readSlotCounter(page))?.index ?? index
    if (baselineIndex !== null && index !== null && index > baselineIndex) {
      return index
    }

    await sleep(200)
  }

  return index
}

async function screenshot(page, name) {
  try {
    mkdirSync(screenshotsDir, { recursive: true })
    const file = path.join(screenshotsDir, `live-${name}.png`)
    await page.screenshot({ path: file })
    console.log(`  截图：${file}`)
  } catch (error) {
    console.warn(`  截图失败（不影响判定）：${error instanceof Error ? error.message : String(error)}`)
  }
}

function compact(text) {
  return text.replace(/\s+/g, ' ').trim()
}

function sleep(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds))
}
