/**
 * 说书人面板的真机冒烟与取证脚本（本机脚本，不参与 dotnet 门禁）。
 *
 * 它回答一个问题：**用真服务端 + 真浏览器做一次真实会话，说书人面板能不能真的用？**
 * 步骤：起真宿主（独立临时库）→ 读说书人票据 → 起 Vite → 用 Chromium 加入 →
 * 分配角色 → 开夜 → 等 SignalR 推送让"当前步骤"自己前进 → 上报状态变化 →
 * 断言面板上确实出现了状态账归因、效果链、最近变化 → 全程截图。
 *
 * 前置：Node >= 22.5（需要 node:sqlite）、web/node_modules 已安装、本机已装 Chromium：
 *   cd web
 *   npm install
 *   npx playwright install chromium
 *
 * 用法（在仓库根运行）：
 *   node tools/verify-storyteller-panel.mjs
 *   node tools/verify-storyteller-panel.mjs --port 5399 --seats 5 \
 *     --assign clockmaker,dreamer,no-dashii --screenshots artifacts/web
 *
 * 退出码：0 = 全部断言通过；1 = 有断言失败；2 = 环境缺依赖（Playwright / 浏览器）。
 */
import { spawn } from 'node:child_process'
import { existsSync, mkdirSync, mkdtempSync, rmSync } from 'node:fs'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { DatabaseSync } from 'node:sqlite'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const webRoot = path.join(repositoryRoot, 'web')

const options = parseArguments(process.argv.slice(2))
const results = []
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

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-panel-verify-'))
const databasePath = path.join(workspace, 'verify.db')
const screenshotsDir = path.resolve(repositoryRoot, options.screenshots)
mkdirSync(screenshotsDir, { recursive: true })

const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`
const children = []

process.on('exit', () => cleanupChildren())

try {
  await main()
  report()
  process.exit(results.some((result) => !result.pass) ? 1 : 0)
} catch (error) {
  console.error(`\n[FAIL] 验证脚本异常终止：${error instanceof Error ? error.stack : String(error)}`)
  results.push({ label: '脚本执行到底', pass: false, detail: '见上方异常' })
  cleanupChildren()
  report()
  process.exit(1)
}

async function main() {
  console.log('=== 1/7 构建并启动真宿主（独立临时库）===')
  // 刻意直接跑编译产物而不是 `dotnet run`：宿主是**单个**进程，
  // 收尾时一次结束即可，不留需要树杀的子进程（与"禁止递归删除"同一姿态）。
  await runProcess('dotnet', ['build', 'src/OpenClockTower.Server', '-c', 'Release'], repositoryRoot)
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
  const server = spawn(serverExecutable, [], {
    cwd: repositoryRoot,
    env: {
      ...process.env,
      ASPNETCORE_URLS: `http://localhost:${options.port}`,
      GameServer__DatabasePath: databasePath,
      GameServer__SeatCount: String(options.seatCount),
      GameServer__SlotQuotaSeconds: options.quotaSeconds,
      GameServer__PacerIntervalMilliseconds: '200',
      DOTNET_ENVIRONMENT: 'Production',
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  })
  children.push(server)
  const serverLog = []
  server.stdout.on('data', (chunk) => serverLog.push(String(chunk)))
  server.stderr.on('data', (chunk) => serverLog.push(String(chunk)))
  await waitForHttp(`${serverUrl}/healthz`, '宿主 /healthz', 90_000)

  console.log('=== 2/7 取说书人票据并起 Vite ===')
  const ticket = readStorytellerTicket(databasePath)
  console.log(`说书人票据：${ticket.slice(0, 12)}…`)
  // 直接跑 Vite 的入口脚本（不经 npm、不经 shell）：**单个**进程一个 PID，
  // 收尾一次结束即可。用 `npm run dev` 会套一层 shell，杀 shell 会留下孤儿 vite（实测踩过）。
  const vite = spawn(
    process.execPath,
    [path.join(webRoot, 'node_modules', 'vite', 'bin', 'vite.js'), '--port', String(options.vitePort), '--strictPort'],
    {
      cwd: webRoot,
      env: { ...process.env, VITE_SERVER_TARGET: serverUrl, VITE_SEAT_COUNT: String(options.seatCount) },
      stdio: ['ignore', 'pipe', 'pipe'],
    },
  )
  children.push(vite)
  vite.stdout.on('data', (chunk) => process.stdout.write(`[vite] ${String(chunk)}`))
  vite.stderr.on('data', (chunk) => process.stderr.write(`[vite] ${String(chunk)}`))
  await waitForHttp(viteUrl, 'Vite 开发服务器', 60_000)

  const browser = await playwright.chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1600, height: 1100 } })
  const consoleErrors = []
  page.on('console', (message) => {
    if (message.type() === 'error') {
      consoleErrors.push(message.text())
    }
  })
  page.on('pageerror', (error) => consoleErrors.push(error.message))

  console.log('=== 3/7 说书人加入（真票据 + 真 SignalR）===')
  await page.goto(viteUrl)
  await page.getByPlaceholder('说书人票据').fill(ticket)
  await page.getByRole('button', { name: '加入' }).click()
  await page.getByText('当前步骤').waitFor({ timeout: 30_000 })
  await screenshot(page, '01-joined')
  check('说书人加入后看板可见', (await page.getByText('当前步骤').count()) === 1)

  console.log('=== 4/7 分配角色（席位全分配：NightPlanBuilder 要求每席都有角色）===')
  const seatCount = await readSeatCount(page)
  check(
    '分配表覆盖服务端全部席位，且席位数量与分配清单一致',
    seatCount === options.assign.length,
    `UI 席位数=${seatCount}，分配清单=${options.assign.length}`,
  )
  for (const [index, slug] of options.assign.entries()) {
    await page.locator('select').nth(index).selectOption(slug)
    console.log(`  席位 ${index + 1} → ${slug}`)
  }
  const assigned = await runCommand(page, '分配', () => page.getByRole('button', { name: '提交分配' }).click())
  check(`分配 ${options.assign.length} 个角色被受理`, assigned.kind === 'Accepted', assigned.raw)
  for (const slug of options.assign) {
    const characterName = characterNameOf(slug)
    check(`状态账里出现角色 ${slug}（${characterName}）`, (await page.getByText(characterName).count()) > 0)
  }
  await screenshot(page, '02-assigned')

  console.log('=== 5/7 开夜（真实顺序表建表）===')
  const nightStarted = await runCommand(page, '开夜', () => page.getByRole('button', { name: /开夜/ }).click())
  check('开夜被受理', nightStarted.kind === 'Accepted', nightStarted.raw)
  await screenshot(page, '03-night-started')
  console.log('=== 6/7 等推送让槽位自己前进（证明实时刷新，不是刷新页面）===')
  const slotAfterStart = await readSlotIndex(page)
  const advanced = await waitForSlotAdvance(page, slotAfterStart, 60_000)
  check(
    `槽位由 ${slotAfterStart ?? '未知'} 前进（无页面刷新）`,
    slotAfterStart !== null && advanced !== null && advanced > slotAfterStart,
    `当前槽位：${advanced}`,
  )
  await screenshot(page, '04-advanced')

  console.log('=== 7/7 上报状态变化并验证归因 / 效果链 ===')
  for (const report of options.reports) {
    console.log(`  上报：${JSON.stringify(report)}`)
    await page.locator('section.panel', { hasText: '上报座位状态' }).locator('select').first().selectOption(String(report.seat))
    await page.locator('.dimensions label', { hasText: report.dimensionLabel }).locator('input[type=checkbox]').check()
    await page.locator('.dimensions label', { hasText: report.dimensionLabel }).locator('select').selectOption(report.value)
    await page.getByPlaceholder('变化原因（必填，会随事件流记录）').fill(report.reason)
    if (typeof report.causedBy === 'number') {
      await page.locator('section.panel', { hasText: '上报座位状态' }).locator('select').nth(1).selectOption(String(report.causedBy))
    }

    const reported = await runCommand(page, `上报-${report.reason}`, () =>
      page.getByRole('button', { name: '上报', exact: true }).click(),
    )
    check(`上报「${report.reason}」被受理`, reported.kind === 'Accepted', reported.raw)
    check(
      `最近变化里出现该原因`,
      (await page.locator('section.panel', { hasText: '最近状态变化' }).getByText(report.reason).count()) > 0,
    )
    if (typeof report.causedBy === 'number') {
      check(
        `最近变化里带归因到 ${report.causedBy} 号`,
        (await page.locator('section.panel', { hasText: '最近状态变化' }).getByText(`${report.causedBy} 号`).count()) > 0,
      )
    }
  }

  await screenshot(page, '05-reported')

  // 断言只落在**真正渲染数据的面板**里：整页 innerText 会被常驻的表单标签（"中毒 / 醉酒"复选框）
  // 误判成通过（复核 2026-10-02 指出过这一点）。
  const ledgerText = await page.locator('section.panel', { hasText: '状态账' }).innerText()
  check(
    '状态账里 2 号出现中毒维度',
    /2 号[^\n]*\n?[^\n]*中毒/.test(ledgerText) && ledgerText.includes('中毒'),
    ledgerText.split('\n').filter((line) => line.includes('2 号')).join(' / ').slice(0, 200),
  )
  check(
    '状态账里 1 号出现醉酒维度（与中毒并存、互不抵消）',
    ledgerText.includes('醉酒'),
    ledgerText.split('\n').filter((line) => line.includes('1 号')).join(' / ').slice(0, 200),
  )
  check(
    '状态账带效果链接（这一格中毒是哪条效果造成的）',
    /standing:no-dashii\.poison/.test(ledgerText),
    ledgerText.match(/standing:[^\s]*/)?.[0] ?? '（没有效果链接）',
  )

  const effectChain = page.locator('section.panel', { hasText: '效果归因链' })
  const effectChainText = await effectChain.innerText()
  check('效果归因链里出现诺-达鲺的能力', effectChainText.includes('no-dashii'), effectChainText.slice(0, 200))
  check(
    '效果归因链给出了生效状态',
    effectChainText.includes('生效中') || effectChainText.includes('已终止'),
    effectChainText.slice(0, 200),
  )
  check('页面没有控制台错误', consoleErrors.length === 0, consoleErrors.slice(0, 3).join(' | '))

  await browser.close()

  const serverCrash = /Unhandled exception|Application is shutting down|fail:/i.test(serverLog.join(''))
  check('宿主日志没有未处理异常', !serverCrash, serverLog.join('').slice(-400))

  const expectedShots = ['01-joined', '02-assigned', '03-night-started', '04-advanced', '05-reported']
  const missingShots = expectedShots.filter((name) => !existsSync(path.join(screenshotsDir, `${name}.png`)))
  check('五张证据截图都已落盘', missingShots.length === 0, missingShots.join(',') || screenshotsDir)
}

/** 轮询"当前步骤"里的槽位显示（X / Y），返回下标（从 0 起）。 */
async function readSlotIndex(page) {
  return page.evaluate(() => {
    const cells = [...document.querySelectorAll('header.strip .cell')]
    for (const cell of cells) {
      const caption = cell.querySelector('.caption')?.textContent?.trim()
      if (caption !== '槽位') {
        continue
      }

      const match = /(\d+)\s*\/\s*(\d+)/.exec(cell.textContent ?? '')
      if (match) {
        return Number.parseInt(match[1], 10) - 1
      }
    }

    return null
  })
}

/** 等槽位下标前进（或计划走完）。 */
async function waitForSlotAdvance(page, before, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    const current = await readSlotIndex(page)
    if (current !== null && before !== null && current > before) {
      return current
    }

    await new Promise((resolve) => setTimeout(resolve, 300))
  }

  return await readSlotIndex(page)
}

async function screenshot(page, name) {
  const target = path.join(screenshotsDir, `${name}.png`)
  await page.screenshot({ path: target, fullPage: true })
  console.log(`  截图：${target}`)
}

function check(label, pass, detail = '') {
  results.push({ label, pass: Boolean(pass), detail })
  console.log(`  ${pass ? '[PASS]' : '[FAIL]'} ${label}${detail ? ` → ${detail}` : ''}`)
}

/**
 * 读回执区的"新鲜度"基线：单调递增的回执序号。
 * 不能用 kind 当判据——连续两条命令都可能返回 Accepted（实测踩过）。
 */
async function readOutcomeSerial(page) {
  return page
    .locator('[data-testid="outcome"]')
    .evaluate((element) => Number(element.getAttribute('data-outcome-serial') ?? '0'))
    .catch(() => 0)
}

/** 等一条**新**回执出现，返回 { kind, raw }；kind 是服务端枚举名（Accepted / Rejected / …）。 */
async function waitForOutcome(page, baselineSerial = 0, timeoutMs = 30_000) {
  const box = page.locator('[data-testid="outcome"]')
  await box.waitFor({ state: 'visible', timeout: timeoutMs })
  const kind = await pollOutcomeKind(box, baselineSerial, timeoutMs)
  const raw = (await box.innerText()).replace(/\s+/g, ' ').trim()
  return { kind, raw }
}

/** 轮询回执区：等回执序号大于基线，返回这条回执的枚举名。 */
async function pollOutcomeKind(box, baselineSerial, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    const state = await box
      .evaluate((element) => ({
        serial: Number(element.getAttribute('data-outcome-serial') ?? '0'),
        kind: element.querySelector('[data-outcome-marker]')?.getAttribute('data-outcome-marker') ?? null,
      }))
      .catch(() => ({ serial: 0, kind: null }))
    if (state.kind !== null && state.serial > baselineSerial) {
      return state.kind
    }

    await new Promise((resolve) => setTimeout(resolve, 100))
  }

  throw new Error(`命令回执在超时前没有出现（基线序号=${baselineSerial}）`)
}

/** 发命令的统一步骤：先记回执序号基线，再点击，再等一条序号更大的新回执。 */
async function runCommand(page, label, click) {
  const baseline = await readOutcomeSerial(page)
  await click()
  try {
    return await waitForOutcome(page, baseline)
  } catch (error) {
    await dumpOutcomeDiagnostics(page, label, error)
    throw error
  }
}

/** 回执没出现时的诊断快照：DOM 原样 + 截图。 */
async function dumpOutcomeDiagnostics(page, label, error) {
  const html = await page
    .locator('[data-testid="outcome"]')
    .evaluate((element) => element.outerHTML)
    .catch(() => '（回执区不存在）')
  console.log(`  [诊断] ${label} 回执未出现：${error.message}`)
  console.log(`  [诊断] 回执区 HTML：${html.replace(/\s+/g, ' ').slice(0, 400)}`)
  await screenshot(page, `diag-${label}-outcome`).catch(() => {})
}

/** 分配表的席位数量（UI 由 VITE_SEAT_COUNT 决定，与宿主 GameServer__SeatCount 对齐）。 */
async function readSeatCount(page) {
  return page.locator('section', { hasText: '开局分配' }).locator('tbody tr').count()
}

function report() {
  const failed = results.filter((result) => !result.pass)
  console.log('\n=== 结果 ===')
  for (const result of results) {
    console.log(`${result.pass ? 'PASS' : 'FAIL'}  ${result.label}${result.detail ? ` → ${result.detail}` : ''}`)
  }

  console.log(failed.length === 0 ? `全部通过（${results.length} 项）` : `失败 ${failed.length} / ${results.length}`)
}

function readStorytellerTicket(databasePath) {
  const database = new DatabaseSync(databasePath, { readOnly: true })
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

    await new Promise((resolve) => setTimeout(resolve, 500))
  }

  throw new Error(`${label} 在 ${timeoutMs}ms 内没有就绪：${lastError}`)
}

function characterNameOf(slug) {
  const roster = {
    clockmaker: '钟表匠',
    dreamer: '筑梦师',
    'snake-charmer': '舞蛇人',
    mathematician: '数学家',
    flowergirl: '卖花女孩',
    'town-crier': '城镇公告员',
    oracle: '神谕者',
    savant: '博学者',
    seamstress: '女裁缝',
    philosopher: '哲学家',
    artist: '艺术家',
    juggler: '杂耍艺人',
    sage: '贤者',
    mutant: '畸形秀演员',
    sweetheart: '心上人',
    barber: '理发师',
    klutz: '呆瓜',
    'evil-twin': '镜像双子',
    witch: '女巫',
    cerenovus: '洗脑师',
    'pit-hag': '麻脸巫婆',
    'fang-gu': '方古',
    vigormortis: '亡骨魔',
    'no-dashii': '诺-达鲺',
    vortox: '涡流',
  }

  return roster[slug] ?? slug
}

function parseArguments(argv) {
  const parsed = {
    port: 5399,
    vitePort: 5398,
    // 席位数量默认跟着分配清单走：NightPlanBuilder 要求**每一席都有角色**（plan.seat_unassigned），
    // 而"有角色"还要求该角色的夜间契约已实现（plan.contract_missing）——本轮只有三个契约，
    // 因此默认 3 席全分配。要验证更多席位，用 --assign 传同样数量的角色。
    seatCount: undefined,
    quotaSeconds: '6',
    assign: ['clockmaker', 'dreamer', 'no-dashii'],
    reports: [
      // 两条独立上报：一条带归因（来源 3 号），一条用来说"同时中毒且醉酒不互抵消"（票据矩阵 2）。
      { seat: 2, dimensionLabel: '中毒', value: 'Poisoned', reason: '诺-达鲺的常驻中毒', causedBy: 3 },
      { seat: 1, dimensionLabel: '醉酒', value: 'Drunk', reason: '说书人裁定：本夜醉酒', causedBy: undefined },
    ],
    screenshots: 'artifacts/web',
  }

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
      case '--seats':
        parsed.seatCount = Number.parseInt(value ?? '', 10)
        index += 1
        break
      case '--quota':
        parsed.quotaSeconds = value ?? parsed.quotaSeconds
        index += 1
        break
      case '--assign':
        parsed.assign = (value ?? '').split(',').map((slug) => slug.trim()).filter(Boolean)
        index += 1
        break
      case '--screenshots':
        parsed.screenshots = value ?? parsed.screenshots
        index += 1
        break
      case '--report':
        parsed.reports = [parseReport(value ?? '')]
        index += 1
        break
      default:
        throw new Error(`未知参数：${flag}`)
    }
  }

  return {
    ...parsed,
    seatCount: Number.isFinite(parsed.seatCount) && parsed.seatCount > 0 ? parsed.seatCount : parsed.assign.length,
  }
}

/** --report 形状：seat:dimension=Value[,causedBy=N][,reason=文本] */
function parseReport(raw) {
  const [head, ...tail] = raw.split(',')
  const [seatPart, dimensionPart] = (head ?? '').split(':')
  const [dimension, value] = (dimensionPart ?? '').split('=')
  const report = {
    seat: Number.parseInt(seatPart ?? '', 10),
    dimension: dimension ?? 'poison',
    value: value ?? 'Poisoned',
    reason: '说书人上报',
    causedBy: undefined,
  }

  const dimensionLabels = {
    poison: '中毒',
    drunk: '醉酒',
    life: '生死',
    alignment: '阵营',
    character: '角色',
  }

  report.dimensionLabel = dimensionLabels[report.dimension] ?? '中毒'
  for (const piece of tail) {
    const [key, pieceValue] = piece.split('=')
    if (key === 'causedBy') {
      report.causedBy = Number.parseInt(pieceValue ?? '', 10)
    } else if (key === 'reason') {
      report.reason = pieceValue ?? report.reason
    } else if (key === 'value') {
      report.value = pieceValue ?? report.value
    }
  }

  return report
}

function cleanupChildren() {
  for (const child of children) {
    if (child.exitCode === null && child.pid !== undefined) {
      try {
        // 只结束自己拉起的**单个**进程（/T 会连子进程一起结束，属于该红线的规避对象）。
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

  try {
    rmSync(workspace, { recursive: true, force: true })
  } catch {
    // 临时库可能还被子进程握着；失败不影响结论，路径会打印出来。
    console.warn(`临时目录未能删除：${workspace}`)
  }
}

/** 跑一个前置命令（构建等），失败即抛。 */
function runProcess(command, args, cwd) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { cwd, stdio: 'inherit' })
    child.on('error', reject)
    child.on('exit', (code) => {
      if (code === 0) {
        resolve()
        return
      }

      reject(new Error(`${command} ${args.join(' ')} 退出码 ${code}`))
    })
  })
}
