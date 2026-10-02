/**
 * 真机验收批次装置（说书人 + 多玩家同局）。
 *
 * 它回答：**用真服务端 + 真浏览器 + 真 SQLite 做一次多客户端会话，
 * 说书人面板与玩家端能不能真的玩通一个夜晚？**（验收规程：docs/acceptance/AGENTS.md §3）
 *
 * 场景（花名册固定为 clockmaker / dreamer / no-dashii——当前已实现契约的三名角色）：
 *   1) 起真宿主（独立临时库）→ 读说书人票据与各席位票据 → 起 Vite → 起 Chromium；
 *   2) 说书人 + 每席一个玩家各自加入（独立浏览器上下文 = 各自设备）；
 *   3) 说书人分配三角色 → 诺-达鲺常驻中毒落在最近的两名镇民（带归因与效果链接）；
 *   4) 说书人上报 1 号醉酒 → 与中毒并存、互不抵消；
 *   5) 开夜 → 钟表匠槽位没有玩家选项，直接进说书人裁定点 → 信息只到 1 号玩家；
 *   6) 筑梦师槽位：2 号玩家收到定向请求 → 作答 → 说书人自由裁定（能力未生效）→
 *      信息只到 2 号玩家；期间其余玩家必须零请求、零进度；
 *   7) 说书人上报 3 号（诺-达鲺）死亡 → 常驻中毒终止、维度解除并归因；
 *   8) 全程截图；断言只落在真正渲染数据的面板内（`data-testid` 锚点 + 单调计数）。
 *
 * 前置：Node >= 22.5（node:sqlite）、web/node_modules 已安装、本机已装 Chromium：
 *   cd web
 *   npm install
 *   npx playwright install chromium
 *
 * 用法（在仓库根运行；默认三席全分配）：
 *   node tools/verify-storyteller-panel.mjs
 *   node tools/verify-storyteller-panel.mjs --port 5399 --vite-port 5398 --quota 2 \
 *     --screenshots artifacts/web
 *
 * 外部耦合（换机器前先核对，见 web/AGENTS.md §3.1）：
 *   - 宿主编译产物路径 src/OpenClockTower.Server/bin/Release/net10.0/OpenClockTower.Server[.exe]；
 *   - SQLite 表 Games、列 StorytellerTicket / SeatsJson（SeatId 是 record struct，
 *     Web 序列化形状为 { "value": 1 }）；
 *   - 席位数量 = --seats = --assign 数量（建表要求每一席都有角色）；
 *   - 场景要求 --assign 含 clockmaker / dreamer / no-dashii。
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

/** 两端共用的取证标记：唯一文本，用来验证"信息只到该到的人"。 */
const CLOCKMAKER_INFO = '批次取证-钟表匠信息：本夜最小距离 2（说书人自由裁定）'
const DREAMER_INFO = '批次取证-筑梦师信息：由说书人自由裁定、可能错误'

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

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-batch-verify-'))
const databasePath = path.join(workspace, 'verify.db')
const screenshotsDir = path.resolve(repositoryRoot, options.screenshots)
mkdirSync(screenshotsDir, { recursive: true })

const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`
const children = []

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
  console.log('=== 1/9 构建并启动真宿主（独立临时库）===')
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
      ASPNETCORE_URLS: serverUrl,
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

  console.log('=== 2/9 取票据（说书人 + 各席位）并起 Vite ===')
  const ticket = readStorytellerTicket(databasePath)
  console.log(`说书人票据：${ticket.slice(0, 12)}…`)
  const seatTickets = readSeatTickets(databasePath)
  check(
    '席位票据齐备且与席位数量一致',
    seatTickets.length === options.seatCount,
    `数据库 ${seatTickets.length} 张，配置 ${options.seatCount} 席`,
  )
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

  console.log('=== 3/9 说书人与各玩家加入（每席一个独立浏览器上下文）===')
  const browser = await playwright.chromium.launch()
  const consoleErrors = []

  const storyteller = await newClient(browser, { width: 1600, height: 1100 }, consoleErrors)
  await storyteller.page.goto(viteUrl)
  await storyteller.page.getByPlaceholder('说书人票据').fill(ticket)
  await storyteller.page.getByRole('button', { name: '加入' }).click()
  await storyteller.page.getByText('当前步骤').waitFor({ timeout: 30_000 })
  await screenshot(storyteller.page, '01-storyteller-joined')
  check('说书人加入后看板可见', (await storyteller.page.getByText('当前步骤').count()) === 1)

  const players = new Map()
  for (const seatTicket of seatTickets) {
    const client = await newClient(browser, { width: 900, height: 900 }, consoleErrors)
    await client.page.goto(`${viteUrl}/#player`)
    await client.page.getByPlaceholder('席位票据').fill(seatTicket.ticket)
    await client.page.getByRole('button', { name: '加入' }).click()
    const seatBadge = client.page.locator('[data-testid="player-seat"]')
    await seatBadge.waitFor({ timeout: 30_000 })
    const badgeText = (await seatBadge.innerText()).trim()
    check(`玩家 ${seatTicket.seat} 号加入成功`, badgeText.includes(`${seatTicket.seat} 号`), badgeText)

    const shellText = await client.page.locator('.shell').innerText()
    check(
      `玩家 ${seatTicket.seat} 号界面不含说书人面板`,
      !['状态账', '效果归因链', '账本与结算结论', '裁定点与卡点'].some((heading) => shellText.includes(heading)),
      shellText.replace(/\s+/g, ' ').slice(0, 120),
    )
    players.set(seatTicket.seat, client)
  }

  // 场景席位：由 --assign 的顺序派生（默认 1=钟表匠 / 2=筑梦师 / 3=诺-达鲺）。
  const clockmakerSeat = options.assign.indexOf('clockmaker') + 1
  const dreamerSeat = options.assign.indexOf('dreamer') + 1
  const demonSeat = options.assign.indexOf('no-dashii') + 1

  console.log('=== 4/9 说书人分配角色（席位全分配）===')
  const seatCount = await readSeatCount(storyteller.page)
  check(
    '分配表覆盖服务端全部席位，且席位数量与分配清单一致',
    seatCount === options.assign.length,
    `UI 席位数=${seatCount}，分配清单=${options.assign.length}`,
  )
  for (const [index, slug] of options.assign.entries()) {
    await storyteller.page.locator('select').nth(index).selectOption(slug)
    console.log(`  席位 ${index + 1} → ${slug}`)
  }
  const assigned = await runCommand(storyteller.page, '分配', () =>
    storyteller.page.getByRole('button', { name: '提交分配' }).click(),
  )
  check(`分配 ${options.assign.length} 个角色被受理`, assigned.kind === 'Accepted', assigned.raw)
  for (const slug of options.assign) {
    check(
      `状态账里出现角色 ${slug}（${characterNameOf(slug)}）`,
      (await storyteller.page.getByText(characterNameOf(slug)).count()) > 0,
    )
  }

  console.log('=== 5/9 开局状态：诺-达鲺常驻中毒 + 说书人上报醉酒 ===')
  // 视图是推送更新的：先等效果链 / 状态账把分配后的对账结果渲染出来，再断言。
  // 判据用**两条不同的效果标识**（来源：诺-达鲺所在席位；目标：最近的两名镇民），
  // 而不是"某个字符串出现两次"——后者在同一条效果被重复渲染时也会成立（独立复核 2026-10-02）。
  const poisonLinkFor = (target) => `standing:no-dashii.poison:${demonSeat}:${target}`
  const effectsVisible = await waitForPanelContains(
    storyteller.page,
    '效果归因链',
    poisonLinkFor(clockmakerSeat),
    15_000,
  )
  const effectChainText = await panelText(storyteller.page, '效果归因链')
  check(
    `效果链出现两条常驻中毒，分别落在 ${clockmakerSeat} / ${dreamerSeat} 号（生效中）`,
    effectsVisible
      && effectChainText.includes(poisonLinkFor(clockmakerSeat))
      && effectChainText.includes(poisonLinkFor(dreamerSeat))
      && effectChainText.includes('生效中'),
    effectChainText.replace(/\s+/g, ' ').slice(0, 200),
  )
  check(
    '常驻中毒带施加时来源角色（诺-达鲺）',
    effectChainText.includes('诺-达鲺'),
    effectChainText.replace(/\s+/g, ' ').slice(0, 200),
  )

  await waitForPanelContains(storyteller.page, '状态账', '中毒', 15_000)
  const ledgerAfterAssign = await panelText(storyteller.page, '状态账')
  check(
    '状态账里 1 / 2 号已被常驻中毒（归因 3 号）',
    linesOf(ledgerAfterAssign, `${clockmakerSeat} 号`).includes('中毒')
      && linesOf(ledgerAfterAssign, `${dreamerSeat} 号`).includes('中毒')
      && linesOf(ledgerAfterAssign, `${dreamerSeat} 号`).includes(`${demonSeat} 号`),
    linesOf(ledgerAfterAssign, `${dreamerSeat} 号`).slice(0, 200),
  )

  const drunkOutcome = await reportSeatState(storyteller.page, {
    seat: clockmakerSeat,
    dimensionLabel: '醉酒',
    value: 'Drunk',
    reason: '批次取证：说书人裁定本夜醉酒',
  })
  check('上报 1 号醉酒被受理', drunkOutcome.kind === 'Accepted', drunkOutcome.raw)
  await waitForPanelContains(storyteller.page, '状态账', '醉酒', 15_000)
  const ledgerAfterDrunk = await panelText(storyteller.page, '状态账')
  const clockmakerLines = linesOf(ledgerAfterDrunk, `${clockmakerSeat} 号`)
  check(
    '状态账里 1 号中毒与醉酒并存（互不抵消）',
    clockmakerLines.includes('中毒') && clockmakerLines.includes('醉酒'),
    clockmakerLines.slice(0, 240),
  )
  await screenshot(storyteller.page, '02-pre-night')

  console.log('=== 6/9 开夜 → 钟表匠裁定点（无玩家选项）→ 1 号玩家收信息 ===')
  const nightStarted = await runCommand(storyteller.page, '开夜', () =>
    storyteller.page.getByRole('button', { name: /开夜/ }).click(),
  )
  check('开夜被受理（真实顺序表建表）', nightStarted.kind === 'Accepted', nightStarted.raw)

  const slotAfterStart = await waitForSlotIndex(storyteller.page, 15_000)
  const slotCounter = await readSlotCounter(storyteller.page)
  check(
    '首夜真实建表：13 个槽位（面板默认 Recommended 全表）',
    slotCounter !== null && slotCounter.total === 13,
    slotCounter === null ? '槽位计数不可读' : `${slotCounter.index + 1} / ${slotCounter.total}`,
  )
  const advanced = await waitForSlotAdvance(storyteller.page, slotAfterStart, 60_000)
  check(
    `槽位由 ${slotAfterStart ?? '未知'} 前进（服务端推送，无页面刷新）`,
    slotAfterStart !== null && advanced !== null && advanced > slotAfterStart,
    `当前槽位：${advanced}`,
  )
  await screenshot(storyteller.page, '03-night-started')

  const clockmakerDecision = await waitForDecision(
    storyteller.page,
    (text) => text.includes('钟表匠'),
    180_000,
  )
  const statesAtClockmaker = []
  for (const [seat, client] of players) {
    statesAtClockmaker.push(
      `${seat}:${await client.page
        .locator('[data-testid="player-request-panel"]')
        .getAttribute('data-request-state')}`,
    )
  }
  check(
    '钟表匠槽位（该能力没有玩家选项）期间三席玩家均无请求',
    statesAtClockmaker.every((entry) => entry.endsWith(':idle')),
    statesAtClockmaker.join(', '),
  )
  check(
    '钟表匠槽位直接进说书人裁定点（该能力没有玩家选项）',
    clockmakerDecision.includes('钟表匠'),
    clockmakerDecision.slice(0, 200),
  )
  check(
    '钟表匠裁定点没有候选选项（自由决定）',
    await storyteller.page.getByText('引擎没有给出候选选项').isVisible().catch(() => false),
  )
  await screenshot(storyteller.page, '04-clockmaker-decision')

  const clockmakerOutcome = await settleFreeDecision(storyteller.page, CLOCKMAKER_INFO)
  check('钟表匠信息裁定被受理', clockmakerOutcome.kind === 'Accepted', clockmakerOutcome.raw)
  check(
    '1 号玩家收到钟表匠信息（内容 = 说书人裁定原文）',
    await waitForPanelContains(players.get(clockmakerSeat).page, '我收到的信息', CLOCKMAKER_INFO, 30_000),
  )
  check(
    '1 号玩家信息面板带"信息可能错误"提示',
    (await players.get(clockmakerSeat).page.getByText('信息可能是错的').count()) > 0,
  )
  await screenshot(players.get(clockmakerSeat).page, '05-player-clockmaker-info')

  console.log('=== 7/9 筑梦师槽位：2 号玩家收到定向请求（无关玩家零活动）===')
  const dreamerPlayer = players.get(dreamerSeat)
  const dreamerRequestPanel = dreamerPlayer.page.locator('[data-testid="player-request-panel"]')
  const requestState = await waitForAttribute(dreamerRequestPanel, 'data-request-state', 'pending', 180_000)
  check('2 号玩家收到定向操作请求（服务端推送）', requestState === 'pending', `data-request-state=${requestState}`)

  const requestContext = await dreamerPlayer.page.locator('[data-testid="player-request-context"]').innerText()
  check('请求上下文含筑梦师选择说明', requestContext.includes('筑梦师'), requestContext.slice(0, 160))

  const optionValues = await dreamerPlayer.page
    .locator('[data-testid="player-request-options"] label')
    .evaluateAll((nodes) => nodes.map((node) => node.getAttribute('data-option-value')))
  check(
    '合法选项 = 除自己外的其他席位',
    optionValues.includes(`seat:${clockmakerSeat}`)
      && optionValues.includes(`seat:${demonSeat}`)
      && !optionValues.includes(`seat:${dreamerSeat}`),
    optionValues.join(','),
  )

  const pendingLeak = await progressLeakOf(dreamerPlayer.page)
  check(
    '玩家端请求态界面无进度语义（槽位 / 进度 / 轮次 / 计数 / 谁在思考）',
    pendingLeak.hits.length === 0,
    pendingLeak.hits.join(',') || pendingLeak.text.replace(/\s+/g, ' ').slice(0, 200),
  )
  await screenshot(dreamerPlayer.page, '06-player-dreamer-request')

  // 反方向证据要做成"有宽度的观测"：在请求窗口内连续采样其余玩家的请求状态。
  // 单点读取只能证明"那一刻恰好空闲"，证明不了"整个窗口里没有活动"（独立复核 2026-10-02 指出）。
  const observedStates = new Map()
  for (const [seat] of players) {
    if (seat !== dreamerSeat) {
      observedStates.set(seat, new Set())
    }
  }

  for (let sample = 0; sample < 8; sample += 1) {
    for (const [seat] of players) {
      if (seat === dreamerSeat) {
        continue
      }

      const state = await players
        .get(seat)
        .page.locator('[data-testid="player-request-panel"]')
        .getAttribute('data-request-state')
      observedStates.get(seat)?.add(state ?? '（无）')
    }

    await sleep(250)
  }

  const stillPending = await dreamerRequestPanel.getAttribute('data-request-state')
  check(
    '采样窗口内请求一直挂在 2 号玩家（未被静默推进）',
    stillPending === 'pending',
    `data-request-state=${stillPending}`,
  )
  for (const [seat, states] of observedStates) {
    check(
      `无关玩家 ${seat} 号在请求窗口内持续零请求（8 次采样）`,
      states.size === 1 && states.has('idle'),
      `观察到：${[...states].join(',') || '（无）'}`,
    )
    check(
      `无关玩家 ${seat} 号没有收到 2 号的信息`,
      !(await infoText(players.get(seat).page)).includes(DREAMER_INFO),
    )
    check(
      `无关玩家 ${seat} 号零诊断（不吞异常，也不刷无意义噪声）`,
      (await players.get(seat).page.locator('[data-testid="player-diagnostics"]').count()) === 0,
    )
  }
  check(
    '纯旁观玩家 3 号信息列表为空（信息单播）',
    (await infoText(players.get(demonSeat).page)).includes('还没有收到信息'),
  )
  await screenshot(players.get(demonSeat).page, '07-unrelated-player-idle')

  console.log('=== 8/9 2 号玩家作答 → 说书人自由裁定（能力未生效）→ 信息单播 ===')
  await dreamerPlayer.page
    .locator('[data-testid="player-request-options"] label', { hasText: `${demonSeat} 号玩家` })
    .locator('input[type=radio]')
    .check()
  await dreamerPlayer.page.locator('[data-testid="player-submit"]').click()
  const backToIdle = await waitForAttribute(dreamerRequestPanel, 'data-request-state', 'idle', 30_000)
  check('2 号玩家提交被受理、请求区回到空态', backToIdle === 'idle', `data-request-state=${backToIdle}`)

  const idleLeak = await progressLeakOf(dreamerPlayer.page)
  check(
    '玩家端空态界面同样无进度语义（两个窗口都查）',
    idleLeak.hits.length === 0,
    idleLeak.hits.join(',') || idleLeak.text.replace(/\s+/g, ' ').slice(0, 200),
  )

  const dreamerDecision = await waitForDecision(
    storyteller.page,
    (text) => text.includes('筑梦师') && text.includes('未生效'),
    60_000,
  )
  check(
    '筑梦师能力未生效 → 信息由说书人裁定（可能错误，不自动判定真假）',
    dreamerDecision.includes('未生效')
      && dreamerDecision.includes('由你说书人裁定')
      && dreamerDecision.includes('可以是错的'),
    dreamerDecision.slice(0, 240),
  )
  check(
    '筑梦师裁定点没有引擎候选（自由文本）',
    await storyteller.page.getByText('引擎没有给出候选选项').isVisible().catch(() => false),
  )

  const dreamerOutcome = await settleFreeDecision(storyteller.page, DREAMER_INFO)
  check('筑梦师信息裁定被受理', dreamerOutcome.kind === 'Accepted', dreamerOutcome.raw)
  check(
    '2 号玩家收到信息（内容 = 说书人裁定原文）',
    await waitForPanelContains(dreamerPlayer.page, '我收到的信息', DREAMER_INFO, 30_000),
  )
  check(
    '2 号玩家信息面板带"信息可能错误"提示',
    (await dreamerPlayer.page.getByText('信息可能是错的').count()) > 0,
  )
  check(
    '1 号玩家没有收到 2 号的裁定内容',
    !(await infoText(players.get(clockmakerSeat).page)).includes(DREAMER_INFO),
  )
  check(
    '2 号玩家没有收到 1 号的裁定内容',
    !(await infoText(dreamerPlayer.page)).includes(CLOCKMAKER_INFO),
  )
  await screenshot(dreamerPlayer.page, '08-player-dreamer-info')

  console.log('=== 9/9 说书人结算归因 → 上报 3 号死亡 → 常驻中毒解除 ===')
  await waitForPanelContains(storyteller.page, '账本与结算结论', 'dreamer', 15_000)
  const resolutionPanel = await panelText(storyteller.page, '账本与结算结论')
  check(
    '最近一次结算：筑梦师未正常生效，原因可读',
    resolutionPanel.includes('未正常生效') && resolutionPanel.includes('dreamer') && resolutionPanel.includes('中毒'),
    resolutionPanel.replace(/\s+/g, ' ').slice(0, 240),
  )
  check(
    '失效账本同时记录钟表匠（中毒 + 醉酒 = 未定 R-0004）与筑梦师（中毒）',
    resolutionPanel.includes('clockmaker')
      && resolutionPanel.includes('未定（R-0004）')
      && resolutionPanel.includes('dreamer'),
    resolutionPanel.replace(/\s+/g, ' ').slice(0, 300),
  )
  const seatLedgerAfter = await panelText(storyteller.page, '状态账')
  check(
    '状态账 2 号中毒带归因 3 号与效果链接',
    linesOf(seatLedgerAfter, `${dreamerSeat} 号`).includes('中毒')
      && linesOf(seatLedgerAfter, `${dreamerSeat} 号`).includes('standing:no-dashii.poison'),
    linesOf(seatLedgerAfter, `${dreamerSeat} 号`).slice(0, 240),
  )
  await screenshot(storyteller.page, '09-storyteller-resolutions')

  const deathOutcome = await reportSeatState(storyteller.page, {
    seat: demonSeat,
    dimensionLabel: '生死',
    value: 'Dead',
    reason: '批次取证：诺-达鲺死亡，验证常驻中毒解除',
  })
  check('上报 3 号死亡被受理', deathOutcome.kind === 'Accepted', deathOutcome.raw)
  check(
    '效果链显示常驻中毒已终止（来源死亡）',
    await waitForPanelContains(storyteller.page, '效果归因链', '已终止', 30_000)
      && await waitForPanelContains(storyteller.page, '效果归因链', '来源死亡', 30_000),
    (await panelText(storyteller.page, '效果归因链')).replace(/\s+/g, ' ').slice(0, 300),
  )
  await waitForPanelContains(storyteller.page, '状态账', '健康', 15_000)
  const ledgerAfterRelease = await panelText(storyteller.page, '状态账')
  check(
    '1 / 2 号中毒解除为健康（维度解除事件）',
    linesOf(ledgerAfterRelease, `${clockmakerSeat} 号`).includes('健康')
      && linesOf(ledgerAfterRelease, `${dreamerSeat} 号`).includes('健康'),
    linesOf(ledgerAfterRelease, `${dreamerSeat} 号`).slice(0, 240),
  )
  await screenshot(storyteller.page, '10-poison-released')

  await browser.close()

  const serverCrash = /Unhandled exception|Application is shutting down/i.test(serverLog.join(''))
  check('宿主日志没有未处理异常', !serverCrash, serverLog.join('').slice(-400))
  check('所有客户端页面没有控制台错误', consoleErrors.length === 0, consoleErrors.slice(0, 3).join(' | '))

  const expectedShots = [
    '01-storyteller-joined',
    '02-pre-night',
    '03-night-started',
    '04-clockmaker-decision',
    '05-player-clockmaker-info',
    '06-player-dreamer-request',
    '07-unrelated-player-idle',
    '08-player-dreamer-info',
    '09-storyteller-resolutions',
    '10-poison-released',
  ]
  const missingShots = expectedShots.filter((name) => !existsSync(path.join(screenshotsDir, `${name}.png`)))
  check('十张证据截图都已落盘', missingShots.length === 0, missingShots.join(',') || screenshotsDir)
}

/** 起一个独立浏览器上下文（= 一台设备）：页面级 console 错误统一收集。 */
async function newClient(browser, viewport, consoleErrors) {
  const context = await browser.newContext({ viewport })
  const page = await context.newPage()
  page.on('console', (message) => {
    if (message.type() === 'error') {
      consoleErrors.push(message.text())
    }
  })
  page.on('pageerror', (error) => consoleErrors.push(error.message))
  return { context, page }
}

/** 轮询玩家信息面板的可见文本。 */
async function infoText(page) {
  const info = page.locator('[data-testid="player-information"]')
  if ((await info.count()) === 0) {
    return ''
  }

  return await info.innerText()
}

/**
 * 玩家端"进度语义"检查：返回命中的令牌（空数组 = 干净）。
 * 空态与请求态两个窗口都要查——只查一个窗口是时序侥幸（独立复核 2026-10-02）。
 * 注意：空态占位文案里**本来就有**"轮到谁 / 还有几步"这两个词（它是在声明"不会有"），
 * 所以判据不能包含这两个词，只能查真正的泄露面：槽位 / 进度 / 轮次 / 谁在思考 / N / M 计数。
 */
async function progressLeakOf(page) {
  const text = await page.locator('.shell').innerText()
  const hits = ['槽位', '进度', '轮次', '谁在思考'].filter((token) => text.includes(token))
  const counter = /\d+\s*\/\s*\d+/.exec(text)
  return { hits: counter ? [...hits, counter[0]] : hits, text }
}

/** 某个 section.panel（按标题定位）的可见文本；找不到返回空串。 */
async function panelText(page, heading) {
  const panel = page.locator('section.panel', { hasText: heading })
  if ((await panel.count()) === 0) {
    return ''
  }

  return await panel.first().innerText()
}

/** 等某个 section.panel 的文本出现目标子串（服务端推送驱动的变化）。 */
async function waitForPanelContains(page, heading, needle, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if ((await panelText(page, heading)).includes(needle)) {
      return true
    }

    await sleep(150)
  }

  return (await panelText(page, heading)).includes(needle)
}

/** 等某个定位器上的属性变成期望值；超时返回最后一次读到的值。 */
async function waitForAttribute(locator, name, expected, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  let value = null
  while (Date.now() < deadline) {
    value = await locator.getAttribute(name).catch(() => null)
    if (value === expected) {
      return value
    }

    await sleep(100)
  }

  return value
}

/** 裁定点区块的可见文本（没有等待中的裁定点时为空串）。 */
async function readDecisionText(page) {
  const block = page.locator('section.panel', { hasText: '裁定点与卡点' }).locator('.block.decision')
  if ((await block.count()) === 0) {
    return ''
  }

  return (await block.first().innerText()).replace(/\s+/g, ' ').trim()
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

/** 说书人按自由决定结清当前裁定点。 */
async function settleFreeDecision(page, content) {
  await page.getByPlaceholder('自由决定的内容（可为空）').fill(content)
  return runCommand(page, '裁定', () => page.getByRole('button', { name: '按自由决定结清' }).click())
}

/** 说书人上报座位状态：先清空既有勾选，再只报本次观测到的维度（避免把上一次的选择带过去）。 */
async function reportSeatState(page, report) {
  const panel = page.locator('section.panel', { hasText: '上报座位状态' })
  await panel.locator('select').first().selectOption(String(report.seat))

  const checkboxes = panel.locator('.dimensions input[type=checkbox]')
  for (let index = 0; index < (await checkboxes.count()); index += 1) {
    await checkboxes.nth(index).uncheck().catch(() => {})
  }

  const dimension = panel.locator('.dimensions label', { hasText: report.dimensionLabel })
  await dimension.locator('input[type=checkbox]').check()
  await dimension.locator('select').selectOption(report.value)
  if (typeof report.causedBy === 'number') {
    await panel.locator('select').nth(1).selectOption(String(report.causedBy))
  }

  await page.getByPlaceholder('变化原因（必填，会随事件流记录）').fill(report.reason)
  return runCommand(page, `上报-${report.dimensionLabel}`, () =>
    page.getByRole('button', { name: '上报', exact: true }).click(),
  )
}

/** 取面板文本里含某个席位的行（用于把断言钉在"真正渲染数据的面板"内）。 */
function linesOf(text, needle) {
  return text
    .split('\n')
    .filter((line) => line.includes(needle))
    .join(' / ')
}

/** 等"当前步骤"的槽位计数渲染出来（视图推送先到）；超时返回 null。 */
async function waitForSlotIndex(page, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    const index = await readSlotIndex(page)
    if (index !== null) {
      return index
    }

    await sleep(150)
  }

  return null
}

/** "当前步骤"里的槽位计数（X / Y）→ { index（从 0 起）, total }；读不到返回 null。 */
async function readSlotCounter(page) {
  return page.evaluate(() => {
    const cells = [...document.querySelectorAll('header.strip .cell')]
    for (const cell of cells) {
      const caption = cell.querySelector('.caption')?.textContent?.trim()
      if (caption !== '槽位') {
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

/** 槽位下标（从 0 起）；读不到返回 null。 */
async function readSlotIndex(page) {
  return (await readSlotCounter(page))?.index ?? null
}

/** 等槽位下标前进（或计划走完）。 */
async function waitForSlotAdvance(page, before, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    const current = await readSlotIndex(page)
    if (current !== null && before !== null && current > before) {
      return current
    }

    await sleep(300)
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

    await sleep(100)
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

/** 读各席位票据：SeatId 是 record struct，Web 序列化形状为 { "value": N }（两种形状都认）。 */
function readSeatTickets(databasePath) {
  const database = new DatabaseSync(databasePath, { readOnly: true })
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

    await sleep(500)
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
    // 节拍器槽位配额：默认 2 秒，让 13 个槽位能在一次批次里走完；节奏规则本身由内核用例锁死。
    quotaSeconds: '2',
    assign: ['clockmaker', 'dreamer', 'no-dashii'],
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
        parsed.assign = (value ?? '')
          .split(',')
          .map((slug) => slug.trim())
          .filter(Boolean)
        index += 1
        break
      case '--screenshots':
        parsed.screenshots = value ?? parsed.screenshots
        index += 1
        break
      default:
        throw new Error(`未知参数：${flag}`)
    }
  }

  if (!['clockmaker', 'dreamer', 'no-dashii'].every((slug) => parsed.assign.includes(slug))) {
    throw new Error('本批次场景需要 --assign 同时含 clockmaker / dreamer / no-dashii（当前已实现契约的三名角色）')
  }

  if (Number.isFinite(parsed.seatCount) && parsed.seatCount !== parsed.assign.length) {
    throw new Error(`--seats 必须与 --assign 同数（建表要求每一席都有角色）：${parsed.seatCount} ≠ ${parsed.assign.length}`)
  }

  return { ...parsed, seatCount: parsed.assign.length }
}

/**
 * 收尾：先结束自己拉起的**单个**进程（/T 会连子进程一起结束，属于该红线的规避对象），
 * 等它们真的退出后再删临时工作目录——Windows 上 taskkill 是异步的，立刻删库会因句柄占用失败
 * （2026-10-02 实测留下 oct-batch-verify-* 残骸，所以这里等退出 + 重试）。
 */
async function cleanup() {
  killChildren()
  await waitForChildrenExit(5_000)
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

    await sleep(100)
  }
}

function sleep(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds))
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
