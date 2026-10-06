#!/usr/bin/env node
/**
 * 部署后真机验收装置（D-0026）—— 「登录即可开桌，开完自己主持」的真实部署判据。
 *
 * 它回答一个**别处证明不了**的问题：把包真的装到服务器上之后，一个全新普通账号
 * 能不能自己开一桌并主持起来。其它装置都在本机临时库上跑（自包含、可重复），
 * 而"权限到底放开了没有""部署形态下页面与 Hub 通不通"只能在真部署上验：
 * 真 nginx 前缀 · 真宿主 · 真持久库 · 真浏览器。
 *
 * 覆盖的链路（一段一条判据）：
 *   home      首页在部署前缀下打得开，两个入口都在
 *   register  说书人端开桌区注册一个**全新随机账号** → 服务端给出"能开桌"的能力位
 *   open      开一桌 → 拿到桌标识与说书人票据，票据**自动填进**输入框
 *   lobby     玩家端大厅里能看到这一桌（公开可见），且默认桌还在（多桌并存）
 *   host      点「加入」进主持台：魔典可见、席位数与开桌时一致
 *   setup     一键配板覆盖每一席 → 提交分配被受理
 *   night     开夜被受理 → 槽位由 0 前进（说书人真的主持起来了）
 *
 * 用法（在仓库根运行）：
 *   node tools/verify-live-open-table.mjs --base-url http://<主机>/<前缀>/
 *   node tools/verify-live-open-table.mjs --base-url ... --seats 5      # 开桌席位数（默认 7）
 *   node tools/verify-live-open-table.mjs --base-url ... --timeout 60   # 单步超时（秒，默认 30）
 *   node tools/verify-live-open-table.mjs --list-sections               # 只列段名，不跑
 *   node tools/verify-live-open-table.mjs --base-url ... --only night   # 执行到该段为止，且只判该段
 *
 * 分段口径与其它装置一致：`--only X` 执行到 X 为止（前面的段作为必要前置照样跑，只有 X 的断言计入判定）；
 * `--from X` 全程照跑、从该段起计入判定；两者互斥。**远程一次红跑很贵**（每次都在真服务器上留数据），
 * 所以迭代期优先用 `--only` 定点，并靠收尾打印的清理 SQL 收干净。
 *
 * **它会真的在目标服务器上留下**：一个测试账号 + 一张测试桌（外加这一桌的事件）。
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
import { createChecker, createSectionRunner } from './lib/verify-sections.mjs'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const webRoot = path.join(repositoryRoot, 'web')
const screenshotsDir = path.join(repositoryRoot, 'artifacts', 'web')

const SECTIONS = [
  { id: 'probe', title: '探活：目标站点可达（地址 / 前缀写错时以退出码 2 收场）' },
  { id: 'home', title: '首页在部署前缀下打得开（两个入口都在）' },
  { id: 'register', title: '说书人端：注册一个全新账号 → 服务端给出"能开桌"能力位' },
  { id: 'open', title: '开一桌：拿到桌标识与说书人票据，票据自动填进输入框' },
  { id: 'lobby', title: '玩家端大厅看得到这一桌，且默认桌还在（多桌并存）' },
  { id: 'host', title: '点「加入」进主持台：魔典可见、席位数与开桌一致' },
  { id: 'setup', title: '一键配板 + 提交分配被受理' },
  { id: 'night', title: '开夜被受理 → 槽位由 0 前进（真的主持起来了）' },
  { id: 'reconnect', title: '刷新页面后凭本机票据自动接回主持台（身份跟着桌走）' },
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

/** 装置自己产生的数据，收尾用它打印清理 SQL。 */
const created = { username, gameId: null, tableName }

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
  await openAt(page, `${options.baseUrl}#/home`)
  const homeReady = await waitForCount(page.getByTestId('home-to-storyteller'), 1, options.timeoutMs)
  check('首页在部署前缀下打得开（真 nginx + 真宿主）', homeReady, options.baseUrl)
  check(
    '首页给出玩家端与说书人端两个入口',
    (await page.getByTestId('home-to-player').count()) === 1,
    `入口数=${await page.getByTestId('home-to-player').count()}`,
  )

  if (!runner.begin('register')) return
  await openAt(page, `${options.baseUrl}#/storyteller`)
  await page.getByTestId('storyteller-open-table').locator('summary').click()
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
  const noticeText = (await page.getByTestId('open-table-notice').count()) > 0
    ? compact(await readTextBounded(page.getByTestId('open-table-notice')))
    : ''
  check(
    '服务端给出"这个账号能开桌"的能力位（默认放开自助开桌）',
    blocked === 0 && !submitDisabled,
    `挡板提示=${blocked}；按钮禁用=${submitDisabled}；提示=${noticeText || '（无）'}`,
  )

  if (!runner.begin('open')) return
  await page.getByTestId('open-table-name').fill(tableName)
  await page.getByTestId('open-table-seats').fill(String(options.seats))
  await page.getByTestId('open-table-submit').click()

  const ticketVisible = await waitForCount(page.getByTestId('new-table-ticket'), 1, options.timeoutMs)
  const ticket = ticketVisible ? compact(await readTextBounded(page.getByTestId('new-table-ticket'))) : ''
  check('开桌成功并回给开桌者一张说书人票据', /^[a-z0-9]+:storyteller-[0-9a-f]{32}$/.test(ticket), ticket || '票据没出现')

  created.gameId = ticket.includes(':') ? ticket.slice(0, ticket.indexOf(':')) : null
  const filled = await page.getByTestId('storyteller-ticket').inputValue()
  check('票据自动填进主持台的输入框（省掉手抄一串）', filled === ticket && ticket.length > 0, `输入框=${filled || '（空）'}`)
  await screenshot(page, 'open-table')

  if (!runner.begin('lobby')) return
  const lobby = await context.newPage()
  lobby.on('pageerror', (error) => consoleErrors.push(error.message))
  await openAt(lobby, `${options.baseUrl}#/play`)
  const newTableRow = lobby.locator(`[data-table="${created.gameId}"]`)
  const listed = await waitForCount(newTableRow, 1, options.timeoutMs)
  const rowText = listed ? compact(await readTextBounded(newTableRow)) : ''
  check('玩家端大厅里能看到这张新桌（公开信息，未登录也能看）', listed, rowText || '大厅里没有这一桌')
  check(
    `新桌人数与席位对得上（0 / ${options.seats}）`,
    rowText.includes(`0 / ${options.seats}`),
    rowText,
  )
  check(
    '默认桌仍在列表里（多桌并存，新桌没顶掉旧桌）',
    (await lobby.locator('[data-table="default"]').count()) === 1,
    `桌数=${await lobby.locator('[data-table]').count()}`,
  )
  await screenshot(lobby, 'lobby-with-new-table')
  await lobby.close()

  if (!runner.begin('host')) return
  await page.getByRole('button', { name: '加入' }).click()
  const grimoire = await waitForCount(page.getByTestId('grimoire'), 1, options.timeoutMs)
  check('凭这张票据进了这一桌的主持台（魔典可见）', grimoire, grimoire ? '魔典已渲染' : '魔典没出现')
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

  if (!runner.begin('reconnect')) return
  // 票据存在本机（TicketStore）：重开这一页应当自己接回主持台，不需要再登录、再抄票据。
  // 这条同时说明"说书人身份跟着**桌**走"——它不依赖账号会话（账号会话只存内存，刷新即失效）。
  await openAt(page, `${options.baseUrl}#/storyteller`)
  const backInConsole = await waitForCount(page.getByTestId('grimoire'), 1, options.timeoutMs)
  check('刷新页面后凭本机票据自动接回主持台（无需重新登录）', backInConsole, backInConsole ? '魔典已渲染' : '没有自动接回')
  const seatRowsAfterReload = await page.locator('section', { hasText: '开局分配' }).locator('tbody tr').count()
  check(
    `接回的是同一张桌（席位数仍是 ${options.seats}）`,
    seatRowsAfterReload === options.seats,
    `UI 席位数=${seatRowsAfterReload}`,
  )
  await screenshot(page, 'reconnected')

  check('全过程没有未预期的控制台错误', consoleErrors.length === 0, consoleErrors.slice(0, 3).join(' | ') || '零错误')
}

/** 收尾：只关浏览器。服务器的数据由部署者按报告里的 SQL 清理。 */
async function cleanup() {
  if (browser !== null) {
    await browser.close().catch(() => {})
  }
}

function reportLeftovers() {
  console.log('\n=== 本次在目标服务器上留下的数据（请按需清理）===')
  console.log(`  测试账号：${created.username}（Users 表）`)
  console.log(`  测试桌　：${created.gameId ?? '（没有开成）'}（Games 表，桌名「${created.tableName}」）`)
  if (created.gameId !== null) {
    console.log('  在目标机上执行（把 <DB> 换成库文件路径；先确认这个桌标识不是你要保留的）：')
    console.log('    systemctl stop clocktower            # 必做：开着的桌活在宿主内存里，不停服务就删会被写回来')
    console.log(
      `    sqlite3 <DB> "DELETE FROM SeatBindings WHERE GameId='${created.gameId}';` +
        ` DELETE FROM Events WHERE GameId='${created.gameId}';` +
        ` DELETE FROM Snapshots WHERE GameId='${created.gameId}';` +
        ` DELETE FROM Receipts WHERE GameId='${created.gameId}';` +
        ` DELETE FROM Games WHERE GameId='${created.gameId}';` +
        ` DELETE FROM Users WHERE Username='${created.username}';"`,
    )
    console.log('    systemctl start clocktower')
  }
}

function parseArguments(argv) {
  const parsed = { baseUrl: null, seats: 7, timeoutMs: 30_000, only: [], from: undefined, listSections: false }
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
