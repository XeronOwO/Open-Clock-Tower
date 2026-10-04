/**
 * 胜败判定与游戏结束批次装置 —— 票据 docs/backlog/done/win-loss-and-game-end.md 的验收矩阵。
 *
 * 它回答：**「呆瓜死亡后的公开选择 → 胜负判定 → 结束面」这条链路，在真界面上一路跑得通吗？**
 * 场景（固定 5 席：1 涡流 / 2 呆瓜 / 3 畸形秀演员 / 4 女巫 / 5 筑梦师）：
 *   1) 分配 → 开首夜（女巫与筑梦师的请求由说书人强制作废）→ 开白天；
 *   2) 2 号（呆瓜）自我提名，3 / 4 号投票，说书人计票 → 3 票过半 → 结束白天处决 2 号；
 *   3) 白天死亡即时公告 → 2 号自己的页面上当场出现**公开选择**（候选不含已死的自己）；
 *   4) 选中 1 号（邪恶的涡流）→ 呆瓜阵营（善良）落败 → 邪恶获胜：
 *      2 号页面出现结束横幅与公开选择记录；说书人面板出现同一份结束结论；
 *   5) 结束后再点「开夜」被拒（`phase.game_ended`）——结束态真的冻结了操作面。
 *
 * 与主批次的分工：主批次跑五席固定花名册的通用玩法回归；本装置只跑这条胜负链路。
 * 涡流「黄昏无人被处决」与镜像双子阻断由集成测试覆盖（同一套投影与横幅）。
 *
 * 前置：Node >= 22.5（node:sqlite）、本机已构建 web/node_modules（playwright + @microsoft/signalr）。
 * 用法（在仓库根运行；默认迭代档 = 快节拍 + 不落盘截图 + 复用产物）：
 *   node tools/verify-winloss.mjs                                        # 迭代档
 *   node tools/verify-winloss.mjs --quota 2 --screenshots-all --build    # 取证档（一批一次）
 *   node tools/verify-winloss.mjs --port 5414 --vite-port 5294           # 自定端口
 *
 * 外部耦合（换机器先核对 web/AGENTS.md §3.1）：宿主编译产物路径、SQLite 表 Games 的
 * StorytellerTicket / SeatsJson 列形状。退出码：0 = 全过；1 = 有失败；2 = 环境缺依赖。
 */
import { spawn } from 'node:child_process'
import { mkdirSync, mkdtempSync, rmSync } from 'node:fs'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { DatabaseSync } from 'node:sqlite'
import { readTextBounded } from './lib/bounded-text.mjs'
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

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-winloss-'))
const databasePath = path.join(workspace, 'winloss.db')
const screenshotsDir = path.resolve(repositoryRoot, 'artifacts', 'web')
mkdirSync(screenshotsDir, { recursive: true })

const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`
const hubUrl = `${serverUrl}/hub/game`

/** 五个席位（与 web/src/display/labels.ts 的花名册一致）：呆瓜要被处决，因此不能再放第二个恶魔。 */
const ASSIGN = ['vortox', 'klutz', 'mutant', 'witch', 'dreamer']
/** 席位号从花名册顺序派生：调换 ASSIGN 顺序时断言跟着走，不靠人工同步。 */
const seatOf = (slug) => ASSIGN.indexOf(slug) + 1
const VORTOX_SEAT = seatOf('vortox')
const KLUTZ_SEAT = seatOf('klutz')
const WITCH_SEAT = seatOf('witch')

/** 玩家客户端可能收到的全部推送（扫描越权字段用）。 */
const PUSH_METHODS = [
  'ReceiveOperationRequest',
  'ReceiveOperationRequestVoided',
  'ReceiveOperationRequestAnswered',
  'ReceivePhaseStarted',
  'ReceiveInformationResult',
  'ReceiveDayChanged',
  'ReceiveGameEnded',
  'ReceiveKlutzChoiceMade',
]

/** 无关玩家端不该出现的词：效果链 / 归因 / 说书人专属字段（D-0012 §4.3 的信息隔离），
 *  以及复盘字段（R-0043：复盘只在结束批次之后由玩家主动查询，任何推送都不含它）。 */
const FORBIDDEN_PLAYER_TOKENS = [
  'effects',
  'effectId',
  'causedBy',
  'termination',
  'klutz:2',
  'witch.curse',
  'replay',
  'markers',
  'steps',
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
  console.log('=== 1/7 构建并启动真宿主（独立临时库，5 席）===')
  await ensureServerArtifacts({ repositoryRoot, buildMode: config.buildMode })
  await startServer()

  console.log('=== 2/7 取票据并起 Vite ===')
  const ticket = readStorytellerTicket(databasePath)
  const seatTickets = readSeatTickets(databasePath)
  check(`席位票据齐备（${ASSIGN.length} 席）`, seatTickets.length === ASSIGN.length, `数据库 ${seatTickets.length} 张`)

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

  console.log('=== 3/7 说书人 + 呆瓜席（2 号）加入真浏览器 ===')
  const browser = await playwright.chromium.launch()
  const consoleErrors = []
  const storytellerPage = await newPage(browser, { width: 1600, height: 1100 }, consoleErrors)
  await storytellerPage.goto(viteUrl)
  await storytellerPage.getByPlaceholder('说书人票据').fill(ticket)
  await storytellerPage.getByRole('button', { name: '加入' }).click()
  await storytellerPage.locator('[data-testid="grimoire"]').waitFor({ timeout: 30_000 })
  check('说书人加入后看板可见（魔典主视图）', (await storytellerPage.locator('[data-testid="grimoire"]').count()) === 1)

  const klutzPage = await newPage(browser, { width: 900, height: 1100 }, consoleErrors)
  await klutzPage.goto(`${viteUrl}/#player`)
  await klutzPage.getByPlaceholder('席位票据').fill(seatTickets[KLUTZ_SEAT - 1].ticket)
  await klutzPage.getByRole('button', { name: '加入' }).click()
  const klutzBadge = await waitForText(klutzPage.locator('[data-testid="player-seat"]'), '2', 30_000)
  check('呆瓜席（2 号）加入玩家端', klutzBadge.includes('2'), klutzBadge)

  // R-0043 反方向：结束批次之前，玩家端连复盘入口都不该出现（更不会有复盘数据）。
  check(
    '结束批次之前：玩家端没有复盘入口（进行中零复盘面）',
    (await klutzPage.locator('[data-testid="player-replay-entry"]').count()) === 0,
  )

  // 投票用真 SignalR 席位客户端（3 / 4 号）；1 号（涡流）与 5 号（筑梦师）本场景不需要动作。
  const voterSeats = [await connectSeat(seatTickets[2]), await connectSeat(seatTickets[WITCH_SEAT - 1])]

  console.log('=== 4/7 分配 → 开首夜 → 过夜（女巫 / 筑梦师的请求由说书人作废）===')
  const assignmentSelects = storytellerPage.locator('section', { hasText: '开局分配' }).locator('select')
  for (const [index, slug] of ASSIGN.entries()) {
    await assignmentSelects.nth(index).selectOption(slug)
  }

  const assigned = await runCommand(storytellerPage, '分配', () =>
    storytellerPage.getByRole('button', { name: '提交分配' }).click(),
  )
  check('分配 5 个角色被受理', assigned.kind === 'Accepted', assigned.raw)

  const nightStarted = await runCommand(storytellerPage, '开夜', () =>
    storytellerPage.getByRole('button', { name: /开夜/ }).click(),
  )
  check('开夜被受理（涡流 / 女巫 / 筑梦师契约齐备）', nightStarted.kind === 'Accepted', nightStarted.raw)

  const witchVoided = await forceVoidPending(
    storytellerPage,
    'StorytellerForce',
    '批次取证：与胜负链路无关的女巫槽位',
  )
  check('女巫请求被说书人强制作废', witchVoided.kind === 'Accepted', witchVoided.raw)
  const dreamerVoided = await forceVoidPending(
    storytellerPage,
    'StorytellerForce',
    '批次取证：与胜负链路无关的筑梦师槽位',
  )
  check('筑梦师请求被说书人强制作废', dreamerVoided.kind === 'Accepted', dreamerVoided.raw)

  const dayStarted = await runCommand(storytellerPage, '开白天', async () => {
    await waitForEnabled(storytellerPage.getByTestId('st-start-day'), 120_000)
    await storytellerPage.getByTestId('st-start-day').click()
  })
  check('白天阶段：开白天被受理', dayStarted.kind === 'Accepted', dayStarted.raw)

  console.log('=== 5/7 处决呆瓜 → 公开选择 → 选中邪恶 → 游戏结束 ===')
  // 2 号自我提名（R-0018 允许）；3 / 4 号投票 → 3 票 ≥ 5 名存活的一半。
  // 玩家端没有命令回执区：判据取**公开账目**（提名条数 / 票数），与主装置同一口径。
  await klutzPage.getByTestId('player-nominee-select').selectOption(String(KLUTZ_SEAT))
  await klutzPage.getByTestId('player-nominate').click()

  const nominationList = storytellerPage.getByTestId('st-day-nominations')
  const nominationCount = await waitForAttribute(nominationList, 'data-nomination-count', '1', 30_000)
  check('呆瓜自我提名进入公开账目', nominationCount === '1', `data-nomination-count=${nominationCount}`)

  await klutzPage.getByTestId('player-vote-yes').click()
  for (const [index, voter] of voterSeats.entries()) {
    const voted = await voter.invoke('CastVote', 1, true, `test-winloss-vote-${index + 3}`)
    check(`旁观席位投票（${index + 3} 号）`, voted.kind === 'Accepted', JSON.stringify(voted))
  }

  const voteCount = await waitForAttribute(
    nominationList.locator('li').first(),
    'data-nomination-votes',
    '3',
    30_000,
  )
  check('呆瓜自投 + 两席旁观投票都到服务端（公开票数 3）', voteCount === '3', `data-nomination-votes=${voteCount}`)

  const counted = await runCommand(storytellerPage, '计票', () =>
    storytellerPage.getByTestId('st-count-votes').click(),
  )
  check('计票被受理（3 票过半）', counted.kind === 'Accepted', counted.raw)

  const dayClosed = await runCommand(storytellerPage, '结束白天（处决呆瓜）', () =>
    storytellerPage.getByTestId('st-close-day').click(),
  )
  check('结束白天：呆瓜被处决', dayClosed.kind === 'Accepted', dayClosed.raw)

  // 白天死亡即时公告 → 呆瓜自己的页面上当场出现公开选择；候选不含已死的自己。
  const requestOptions = klutzPage.locator('[data-testid="player-request-options"] [data-option-value]')
  await requestOptions.first().waitFor({ timeout: 60_000 })
  const optionValues = await requestOptions.evaluateAll((nodes) =>
    nodes.map((node) => node.getAttribute('data-option-value')),
  )
  const requestContext = compact(await klutzPage.getByTestId('player-request-context').innerText())
  check(
    '呆瓜拿到死亡选择请求，候选只含存活席位（不含 2 号自己）',
    optionValues.length === 4 && !optionValues.includes(`seat:${KLUTZ_SEAT}`) && requestContext.includes('呆瓜'),
    `${optionValues.join(',')}｜${requestContext}`,
  )
  await screenshot(klutzPage, 'winloss-01-klutz-choice')

  await klutzPage.locator('[data-testid="player-request-options"] [data-option-value="seat:1"]').click()
  await klutzPage.getByTestId('player-submit').click()

  const playerWinner = await waitForAttribute(
    klutzPage.getByTestId('player-outcome'),
    'data-outcome-winner',
    'Evil',
    60_000,
  )
  const playerDetail = compact(await klutzPage.getByTestId('player-outcome-detail').innerText())
  const publicChoice = compact(await klutzPage.getByTestId('player-klutz-choices').innerText())
  check(
    '选中邪恶的涡流 → 呆瓜阵营落败：玩家端出现结束横幅（邪恶获胜 + 原因）与公开选择记录',
    playerWinner === 'Evil' && playerDetail.includes('呆瓜') && publicChoice.includes('1 号'),
    `winner=${playerWinner}｜${playerDetail}｜${publicChoice}`,
  )
  await screenshot(klutzPage, 'winloss-02-klutz-picked-evil')

  const storytellerWinner = await waitForAttribute(
    storytellerPage.getByTestId('storyteller-outcome'),
    'data-outcome-winner',
    'Evil',
    60_000,
  )
  check('说书人面板出现同一份结束结论', storytellerWinner === 'Evil', `data-outcome-winner=${storytellerWinner}`)
  await screenshot(storytellerPage, 'winloss-03-storyteller-ended')

  console.log('=== 6/7 结束后：玩家端复盘入口 → 逐步回放 → 刷新按序号恢复 ===')
  const replayOpenButton = klutzPage.getByTestId('player-replay-open')
  await replayOpenButton.waitFor({ timeout: 30_000 })
  await replayOpenButton.click()
  await klutzPage.getByTestId('replay-panel').waitFor({ timeout: 30_000 })
  // 面板先出现、步骤按序号异步到达：等首屏加载完成再判位置（加载态显示「加载中…」）。
  const firstProgress = await waitForText(klutzPage.getByTestId('replay-progress'), '第 1 /', 30_000)
  const firstSummary = compact(await klutzPage.getByTestId('replay-summary').innerText())
  check(
    '结束批次之后：玩家端复盘入口出现，面板停在第 1 步',
    firstProgress.includes('第 1 /') && firstSummary.length > 0,
    `${firstProgress}｜${firstSummary}`,
  )

  await klutzPage.getByTestId('replay-next').click()
  await klutzPage.getByTestId('replay-next').click()
  const thirdProgress = compact(await klutzPage.getByTestId('replay-progress').innerText())
  check('「下一步」按原子步骤推进（不跳步、不合并）', thirdProgress.includes('第 3 /'), thirdProgress)

  await klutzPage.getByTestId('replay-prev').click()
  const secondProgress = compact(await klutzPage.getByTestId('replay-progress').innerText())
  const positionSequence = /事件序号 (\d+)/.exec(secondProgress)?.[1] ?? ''
  check(
    '「上一步」回到第 2 步，进度以事件序号为准',
    secondProgress.includes('第 2 /') && positionSequence.length > 0,
    secondProgress,
  )
  await screenshot(klutzPage, 'winloss-04-player-replay')

  // 刷新 / 重连：位置来自 URL 里的序号；重连后自动重开复盘并回到同一步（票据矩阵行 7）。
  await klutzPage.reload()
  await klutzPage.getByTestId('replay-panel').waitFor({ timeout: 30_000 })
  const restoredProgress = await waitForText(klutzPage.getByTestId('replay-progress'), '第 2 /', 30_000)
  check(
    '刷新 / 重连后回放位置按事件序号恢复（不丢）',
    restoredProgress.includes('第 2 /') && restoredProgress.includes(`事件序号 ${positionSequence}`),
    `刷新前 ${secondProgress}｜刷新后 ${restoredProgress}`,
  )
  await screenshot(klutzPage, 'winloss-05-player-replay-restored')

  console.log('=== 7/7 结束后操作面被冻结 + 收包扫描 ===')
  const afterEnd = await voterSeats[0].invoke('Nominate', 1, 'test-winloss-after-end')
  check(
    '结束后玩家操作被拒（phase.game_ended）',
    afterEnd.kind === 'Rejected' && afterEnd.rejectionCode === 'phase.game_ended',
    JSON.stringify(afterEnd),
  )

  const voterText = JSON.stringify(voterSeats.map((client) => client.messages))
  const leaked = FORBIDDEN_PLAYER_TOKENS.filter((token) => voterText.includes(token))
  check(
    '旁观席位的全部推送里没有越权字段',
    leaked.length === 0,
    leaked.join(', ') || `已扫描 ${voterSeats.reduce((total, client) => total + client.messages.length, 0)} 条`,
  )

  check('浏览器控制台没有报错', consoleErrors.length === 0, consoleErrors.slice(0, 3).join(' | '))
  await browser.close()
  for (const voter of voterSeats) {
    await voter.dispose()
  }
}
/** 连一个真 SignalR 席位：记录每一次请求与每一条推送（供越权扫描）。 */
async function connectSeat(seatTicket) {
  const connection = new signalR.HubConnectionBuilder().withUrl(hubUrl).configureLogging(signalR.LogLevel.None).build()
  const requests = []
  const messages = []
  connection.on('ReceiveOperationRequest', (payload) => {
    requests.push(payload)
    messages.push({ method: 'ReceiveOperationRequest', payload })
  })
  for (const method of PUSH_METHODS.filter((candidate) => candidate !== 'ReceiveOperationRequest')) {
    connection.on(method, (payload) => messages.push({ method, payload }))
  }

  await connection.start()
  const joined = await connection.invoke('JoinSeat', seatTicket.ticket, 0)
  return {
    requests,
    messages,
    invoke: (method, ...args) => connection.invoke(method, joined.credential, ...args),
    dispose: () => connection.stop(),
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

async function setDataDrawer(page, open) {
  const toggle = page.locator('[data-testid="data-drawer-toggle"]')
  if ((await toggle.getAttribute('aria-expanded')) === String(open)) {
    return
  }

  await toggle.click()
  await sleep(150)
}

/** 数据抽屉里某一段的文本（当前步骤 / 状态账 / 最近状态变化 / 效果归因链 / 两本账）。 */
async function panelText(page, heading) {
  const drawer = page.locator('[data-testid="data-drawer-body"]')
  const section = drawer.locator('section.panel', { hasText: heading })
  if ((await section.count()) === 0) {
    return ''
  }

  return compact(await section.first().innerText())
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
    text = compact(await readTextBounded(locator))
    if (text.includes(expected)) {
      return text
    }

    await sleep(150)
  }

  return text
}

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

async function waitForEnabled(locator, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if (await locator.isEnabled().catch(() => false)) {
      return true
    }

    await sleep(200)
  }

  return false
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
  console.log('\n=== 胜败判定与游戏结束批次（winloss）取证结论 ===')
  for (const result of results) {
    console.log(`${result.pass ? 'PASS' : 'FAIL'}  ${result.label}${result.detail ? ` → ${result.detail}` : ''}`)
  }

  const failed = results.filter((result) => !result.pass)
  console.log(failed.length === 0 ? `全部通过（${results.length} 项）` : `失败 ${failed.length} / ${results.length}`)
}

function describeOutcome(outcome) {
  return `${outcome?.kind ?? '无回执'}${outcome?.rejectionCode ? ` / ${outcome.rejectionCode}` : ''}`
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
  const parsed = { port: 5411, vitePort: 5291 }
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

async function waitUntil(condition, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if (await syncCondition(condition)) {
      return true
    }

    await sleep(25)
  }

  return syncCondition(condition)
}

// 同步谓词守卫：waitUntil 不 await 谓词，Promise 恒真会让旧实现静默假绿（agent-reference.md §8）。
function syncCondition(condition) {
  const result = condition()
  if (result !== null && typeof result === 'object' && typeof result.then === 'function') {
    throw new Error(
      'waitUntil 只接受同步谓词：返回 Promise 会恒真（假绿）。请自己写轮询，或改用 locator.waitFor / waitForAttribute / waitForLocatorContains。',
    )
  }

  return result
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
