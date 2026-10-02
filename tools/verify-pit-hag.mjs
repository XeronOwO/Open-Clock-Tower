/**
 * 麻脸巫婆之夜（角色变更 + 死亡裁量）批次装置 —— 票据 docs/backlog/todo/pit-hag-character-change.md 的验收矩阵。
 *
 * 它回答：**「创造恶魔 → 当夜它就在真界面上被唤醒并行动 → 待定死亡由说书人裁定」这条链路，真机跑得通吗？**
 * 场景（固定 5 席：1 麻脸巫婆 / 2 博学者 / 3 艺术家 / 4 诺-达鲺 / 5 呆瓜）：
 *   1) 分配 → 开首夜（这五个角色都不在首夜顺序表上，自动走完）→ 开第二夜；
 *   2) 1 号在**真玩家页面**上拿到两维选择（玩家 × 角色），选 3 号变成「涡流」——一个不在场的恶魔；
 *   3) 断言说书人面板出现「麻脸巫婆之夜」控件（来源 1 号、待定 0 条），3 号牌面变成涡流；
 *   4) 4 号（诺-达鲺）击杀 5 号 → 面板出现 1 条待定死亡，且 5 号**没有死**（窗口内不直接致死）；
 *      点「阻止（免死）」→ 待定清零、5 号仍存活；
 *   5) 用「追加死亡」控件杀 5 号（归因麻脸巫婆，不触发「被恶魔杀死」类能力）；
 *   6) 3 号（当夜被创造的涡流）**真的被唤醒**并击杀 2 号 → 待定 1 条 → 点「确认死亡」→ 2 号死亡；
 *   7) 走完第二夜 → 窗口在越过最后一个恶魔行动后收口（控件消失）；
 *   8) 第三夜：麻脸巫婆把 3 号（此时已是涡流）再变成「镜像双子」——3 号阵营始终是善良，
 *      说书人面板开出「选择对立双子」裁定（候选只有邪恶玩家），选定 4 号后配对落库、裁定控件结清；
 *   9) 收包扫描：无关玩家（5 号）的全部推送里没有窗口 / 待定死亡字段（D-0012 §4.3）。
 *
 * 与主批次的分工：主批次跑五席固定花名册的通用玩法回归；本装置只跑这条能力链路。
 * 前置：Node >= 22.5（node:sqlite）、本机已构建 web/node_modules（playwright + @microsoft/signalr）。
 * 用法（在仓库根运行；默认迭代档 = 快节拍 + 不落盘截图 + 复用产物）：
 *   node tools/verify-pit-hag.mjs                                        # 迭代档
 *   node tools/verify-pit-hag.mjs --quota 2 --screenshots-all --build    # 取证档（一批一次）
 *   node tools/verify-pit-hag.mjs --port 5413 --vite-port 5293           # 自定端口
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
import { describeProfile, ensureServerArtifacts, extractProfileFlags, resolveProfile } from './lib/verify-profile.mjs'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const webRoot = path.join(repositoryRoot, 'web')
const { flags, rest } = extractProfileFlags(process.argv.slice(2))
const options = parseArguments(rest)

/** 档位（tools/lib/verify-profile.mjs）：默认迭代档；取证档显式 `--quota 2 --screenshots-all`。 */
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

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-pithag-'))
const databasePath = path.join(workspace, 'pithag.db')
const screenshotsDir = path.resolve(repositoryRoot, 'artifacts', 'web')
mkdirSync(screenshotsDir, { recursive: true })

const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`
const hubUrl = `${serverUrl}/hub/game`

/** 五个席位（与 web/src/display/labels.ts 的花名册一致）：只有麻脸巫婆与诺-达鲺在第二夜的表上。 */
const ASSIGN = ['pit-hag', 'savant', 'artist', 'no-dashii', 'klutz']
const seatOf = (slug) => ASSIGN.indexOf(slug) + 1
const PIT_HAG_SEAT = seatOf('pit-hag')
const TARGET_SEAT = seatOf('savant')
const ARTIST_SEAT = seatOf('artist')
const DEMON_SEAT = seatOf('no-dashii')
const KLUTZ_SEAT = seatOf('klutz')

const PUSH_METHODS = [
  'ReceiveOperationRequest',
  'ReceiveOperationRequestVoided',
  'ReceiveOperationRequestAnswered',
  'ReceivePhaseStarted',
  'ReceiveInformationResult',
  'ReceiveDayChanged',
]

/** 无关玩家端不该出现的词：窗口 / 待定死亡 / 裁量 / 裁定点字段（D-0012 §4.3 的信息隔离）。 */
const FORBIDDEN_PLAYER_TOKENS = [
  'pitHagNight',
  'closesAfterSlotIndex',
  'deferred',
  '待定死亡',
  '死亡裁量',
  'awaitingDecision',
  '对立双子',
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
  console.log('=== 1/8 构建并启动真宿主（独立临时库，5 席）===')
  await ensureServerArtifacts({ repositoryRoot, buildMode: config.buildMode })
  await startServer()

  console.log('=== 2/8 取票据并起 Vite ===')
  const ticket = readStorytellerTicket(databasePath)
  const seatTickets = readSeatTickets(databasePath)
  check('席位票据齐备（5 席）', seatTickets.length === 5, `数据库 ${seatTickets.length} 张`)

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

  console.log('=== 3/8 说书人 + 麻脸巫婆席（1 号）加入真浏览器 ===')
  const browser = await playwright.chromium.launch()
  const consoleErrors = []
  const storytellerPage = await newPage(browser, { width: 1600, height: 1100 }, consoleErrors)
  await storytellerPage.goto(viteUrl)
  await storytellerPage.getByPlaceholder('说书人票据').fill(ticket)
  await storytellerPage.getByRole('button', { name: '加入' }).click()
  await storytellerPage.locator('[data-testid="grimoire"]').waitFor({ timeout: 30_000 })
  check('说书人加入后看板可见（魔典主视图）', (await storytellerPage.locator('[data-testid="grimoire"]').count()) === 1)

  const pitHagPage = await newPage(browser, { width: 900, height: 1100 }, consoleErrors)
  await pitHagPage.goto(`${viteUrl}/#player`)
  await pitHagPage.getByPlaceholder('席位票据').fill(seatTickets[PIT_HAG_SEAT - 1].ticket)
  await pitHagPage.getByRole('button', { name: '加入' }).click()
  const pitHagBadge = await waitForText(pitHagPage.locator('[data-testid="player-seat"]'), String(PIT_HAG_SEAT), 30_000)
  check('麻脸巫婆席（1 号）加入玩家端', pitHagBadge.includes(String(PIT_HAG_SEAT)), pitHagBadge)

  // 3 号（会被变成涡流）与 4 号（原本的恶魔）用 Node 客户端驱动：它们是"当夜行动"的执行者。
  const artistSeat = await connectSeat(seatTickets[ARTIST_SEAT - 1])
  const demonSeat = await connectSeat(seatTickets[DEMON_SEAT - 1])
  // 5 号是无关玩家：它的全部推送要接受越权扫描。
  const klutzSeat = await connectSeat(seatTickets[KLUTZ_SEAT - 1])

  console.log('=== 4/8 分配 → 首夜（自动走完）→ 第二夜 ===')
  const assignmentSelects = storytellerPage.locator('section', { hasText: '开局分配' }).locator('select')
  for (const [index, slug] of ASSIGN.entries()) {
    await assignmentSelects.nth(index).selectOption(slug)
  }

  const assigned = await runCommand(storytellerPage, '分配', () =>
    storytellerPage.getByRole('button', { name: '提交分配' }).click(),
  )
  check('分配 5 个角色被受理', assigned.kind === 'Accepted', assigned.raw)

  const firstNight = await startNightWhenReady(storytellerPage, 1)
  check('开首夜被受理', firstNight.kind === 'Accepted', firstNight.raw)
  const secondNight = await startNightWhenReady(storytellerPage, 2)
  check('第二夜被受理（首夜自动走完后开夜）', secondNight.kind === 'Accepted', secondNight.raw)

  console.log('=== 5/8 麻脸巫婆在真界面上做两维选择：把 3 号变成涡流 ===')
  const primaryOptions = pitHagPage.locator('[data-testid="player-request-options"] [data-option-value]')
  const secondaryOptions = pitHagPage.locator(
    '[data-testid="player-request-secondary-options"] [data-option-value]',
  )
  await primaryOptions.first().waitFor({ timeout: 90_000 })
  check('第一维渲染 5 个席位选项', (await primaryOptions.count()) === 5, `渲染 ${await primaryOptions.count()} 个`)
  check(
    '第二维渲染 25 个角色选项（整张角色列表）',
    (await secondaryOptions.count()) === 25,
    `渲染 ${await secondaryOptions.count()} 个`,
  )
  const secondaryValues = await secondaryOptions.evaluateAll((nodes) =>
    nodes.map((node) => node.getAttribute('data-option-value')),
  )
  check(
    '角色表含恶魔（麻脸巫婆能创造恶魔），且不因在场而被过滤',
    secondaryValues.includes('vortox') && secondaryValues.includes('no-dashii'),
    secondaryValues.slice(0, 4).join(','),
  )

  await pitHagPage.locator(`[data-testid="player-request-options"] [data-option-value="seat:${ARTIST_SEAT}"]`).click()
  await pitHagPage
    .locator('[data-testid="player-request-secondary-options"] [data-option-value="vortox"]')
    .click()
  await screenshot(pitHagPage, 'pithag-01-two-dimensional-request')
  await pitHagPage.getByTestId('player-submit').click()

  const nightPanel = storytellerPage.getByTestId('st-pit-hag-night')
  await nightPanel.waitFor({ timeout: 30_000 })
  const sourceSeat = await waitForAttribute(nightPanel, 'data-source-seat', String(PIT_HAG_SEAT), 30_000)
  const deferredCount = await waitForAttribute(nightPanel, 'data-deferred-count', '0', 30_000)
  check('创造恶魔后说书人面板出现死亡裁量控件', sourceSeat === String(PIT_HAG_SEAT), `data-source-seat=${sourceSeat}`)
  check('刚开窗时没有待定死亡', deferredCount === '0', `data-deferred-count=${deferredCount}`)

  const artistCard = storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${ARTIST_SEAT}"]`)
  const artistCharacter = await waitForLocatorContains(artistCard, '涡流', 30_000)
  check('3 号牌面变成涡流（角色变更，阵营不变）', artistCharacter.includes('涡流'), compact(artistCharacter))
  await screenshot(storytellerPage, 'pithag-02-window-open')

  console.log('=== 6/8 待定死亡：阻止（免死）→ 追加死亡 → 当夜被创造的涡流真的被唤醒 ===')
  const demonRequests = await waitForSeatRequest(demonSeat, 90_000)
  check('4 号（诺-达鲺）当夜拿到击杀请求', demonRequests.length >= 1, `收到 ${demonRequests.length} 条`)
  await demonSeat.invoke('SubmitResponse', demonRequests[0].requestId, `seat:${KLUTZ_SEAT}`, 'pithag-kill-klutz', 1)

  const oneDeferred = await waitForAttribute(nightPanel, 'data-deferred-count', '1', 30_000)
  check('恶魔击杀在窗口内记为待定死亡（不直接致死）', oneDeferred === '1', `data-deferred-count=${oneDeferred}`)
  const klutzCard = storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${KLUTZ_SEAT}"]`)
  check('待定期间 5 号仍然存活', (await klutzCard.getAttribute('data-life')) === 'Alive', 'data-life 应为 Alive')

  const deferredEntry = await storytellerPage
    .getByTestId(`st-pit-hag-deferred-${KLUTZ_SEAT}`)
    .waitFor({ timeout: 30_000 })
    .then(() => true)
    .catch(() => false)
  check('面板列出这条待定死亡（目标席位可见）', deferredEntry, `st-pit-hag-deferred-${KLUTZ_SEAT}`)
  await screenshot(storytellerPage, 'pithag-03-deferred-death')

  const prevented = await runCommand(storytellerPage, '阻止死亡', () =>
    storytellerPage.getByTestId(`st-pit-hag-prevent-${KLUTZ_SEAT}`).click(),
  )
  check('说书人阻止这次死亡被受理', prevented.kind === 'Accepted', prevented.raw)
  const afterPrevent = await waitForAttribute(nightPanel, 'data-deferred-count', '0', 30_000)
  check('阻止后待定清零、5 号仍存活', afterPrevent === '0' && (await klutzCard.getAttribute('data-life')) === 'Alive')

  // 追加死亡：席位 + 说明 → 归因为麻脸巫婆。
  await storytellerPage.getByTestId('st-pit-hag-casualty-seat').fill(String(KLUTZ_SEAT))
  await storytellerPage.getByTestId('st-pit-hag-casualty-note').fill('批次取证：说书人追加死亡')
  const casualty = await runCommand(storytellerPage, '追加死亡', () =>
    storytellerPage.getByTestId('st-pit-hag-casualty').click(),
  )
  check('说书人追加死亡被受理', casualty.kind === 'Accepted', casualty.raw)
  const klutzLife = await waitForAttribute(klutzCard, 'data-life', 'Dead', 30_000)
  check('追加死亡生效（归因麻脸巫婆）', klutzLife === 'Dead', `data-life=${klutzLife}`)
  await screenshot(storytellerPage, 'pithag-04-casualty')

  const artistRequests = await waitForSeatRequest(artistSeat, 90_000)
  check('当夜被创造的涡流真的被唤醒（3 号拿到请求）', artistRequests.length >= 1, `收到 ${artistRequests.length} 条`)
  await artistSeat.invoke('SubmitResponse', artistRequests[0].requestId, `seat:${TARGET_SEAT}`, 'pithag-kill-clock', 1)

  const killedSeatCount = await waitForAttribute(nightPanel, 'data-deferred-count', '1', 30_000)
  check('新恶魔的击杀同样记为待定死亡', killedSeatCount === '1', `data-deferred-count=${killedSeatCount}`)
  const confirmed = await runCommand(storytellerPage, '确认死亡', () =>
    storytellerPage.getByTestId(`st-pit-hag-kill-${TARGET_SEAT}`).click(),
  )
  check('说书人确认死亡被受理', confirmed.kind === 'Accepted', confirmed.raw)

  console.log('=== 7/8 第二夜收口 ===')
  // 剩下的都是空槽位：等夜晚自动走完——窗口在越过最后一个恶魔行动之后收口，控件随之消失。
  const panelGone = await waitForCount(storytellerPage.getByTestId('st-pit-hag-night'), 0, 60_000)
  check('越过最后一个恶魔行动后窗口收口（控件消失）', panelGone, '等待控件归零')
  const clockmakerCard = storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${TARGET_SEAT}"]`)
  check('被确认的 2 号死亡、3 号存活', (await clockmakerCard.getAttribute('data-life')) === 'Dead')
  check('当夜被创造的涡流（3 号）仍存活', (await artistCard.getAttribute('data-life')) === 'Alive')
  await screenshot(storytellerPage, 'pithag-05-window-closed')

  console.log('=== 8/8 第三夜：创造镜像双子 → 「选择对立双子」配对裁定 ===')
  // 行 12 的界面级取证：同一夹具继续走第三夜。3 号阵营从未改变（一直是善良），
  // 变成镜像双子后候选只有邪恶玩家（1 号麻脸巫婆 / 4 号诺-达鲺）；新双子自己不在候选里。
  const thirdNight = await startNightWhenReady(storytellerPage, 3)
  check('开第三夜被受理', thirdNight.kind === 'Accepted', thirdNight.raw)

  await primaryOptions.first().waitFor({ timeout: 90_000 })
  await pitHagPage.locator(`[data-testid="player-request-options"] [data-option-value="seat:${ARTIST_SEAT}"]`).click()
  await pitHagPage.locator('[data-testid="player-request-secondary-options"] [data-option-value="evil-twin"]').click()
  await screenshot(pitHagPage, 'pithag-06-evil-twin-request')
  await pitHagPage.getByTestId('player-submit').click()

  const decisionPanel = storytellerPage.getByTestId('console-decision')
  await decisionPanel.waitFor({ timeout: 30_000 })
  const decisionContext = compact(await decisionPanel.locator('.context').innerText())
  check('创造镜像双子开出「选择对立双子」裁定', decisionContext.includes('对立双子'), decisionContext)
  const decisionOptions = (
    await decisionPanel.locator('.options button').evaluateAll((nodes) => nodes.map((node) => node.textContent))
  ).map((text) => compact(text))
  check(
    '候选恰为邪恶玩家（1 号 / 4 号），新双子自己不在列',
    decisionOptions.length === 2 && decisionOptions.includes('1 号玩家') && decisionOptions.includes('4 号玩家'),
    decisionOptions.join(' | '),
  )
  await screenshot(storytellerPage, 'pithag-07-pairing-decision')

  const paired = await runCommand(storytellerPage, '选择对立双子', () =>
    decisionPanel.locator('.options button', { hasText: `${DEMON_SEAT} 号玩家` }).click(),
  )
  check('说书人选定 4 号为对立双子被受理', paired.kind === 'Accepted', paired.raw)
  const twinCard = storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${ARTIST_SEAT}"]`)
  const twinCharacter = await waitForLocatorContains(twinCard, '镜像双子', 30_000)
  check('3 号牌面变成镜像双子', twinCharacter.includes('镜像双子'), compact(twinCharacter))
  const decisionCleared = await waitForCount(storytellerPage.getByTestId('console-decision'), 0, 30_000)
  check('配对后裁定控件结清', decisionCleared, '等待 console-decision 归零')
  await screenshot(storytellerPage, 'pithag-08-paired')

  const unrelatedText = JSON.stringify(klutzSeat.messages)
  const leaked = FORBIDDEN_PLAYER_TOKENS.filter((token) => unrelatedText.includes(token))
  check(
    '无关玩家（5 号）的全部推送里没有窗口 / 待定死亡字段',
    leaked.length === 0,
    leaked.join(', ') || `已扫描 ${klutzSeat.messages.length} 条`,
  )

  check('浏览器控制台没有报错', consoleErrors.length === 0, consoleErrors.slice(0, 3).join(' | '))
  await browser.close()
}

/** 点「开夜」直到被受理（上一阶段靠节拍自动走完）。 */
async function startNightWhenReady(page, nightNumber) {
  const operations = page.locator('section', { hasText: '兜底与推进' })
  await operations.locator('input[type="number"]').fill(String(nightNumber))
  // 预算按档位算，不写死次数：取证档 2s/槽 × 首夜 13 槽 ≈ 26 秒（迭代档 0.3s 只需 4 秒）。
  const deadline = Date.now() + Math.max(60_000, config.quotaSeconds * 60 * 1_000)
  let outcome = null
  while (Date.now() < deadline) {
    outcome = await runCommand(page, `开第 ${nightNumber} 夜`, () =>
      operations.getByRole('button', { name: /开夜/ }).click(),
    )
    if (outcome.kind === 'Accepted') {
      return outcome
    }

    await sleep(500)
  }

  return outcome
}

/** 等某个席位收到操作请求（Node 客户端驱动的席位）。 */
async function waitForSeatRequest(seat, timeoutMs) {
  const ok = await waitUntil(() => seat.requests.length > 0, timeoutMs)
  if (!ok) {
    throw new Error(`席位在 ${timeoutMs}ms 内没有收到操作请求`)
  }

  return seat.requests
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
    text = compact(await locator.innerText().catch(() => ''))
    if (text.includes(expected)) {
      return text
    }

    await sleep(150)
  }

  return text
}

async function waitForLocatorContains(locator, needle, timeoutMs) {
  return waitForText(locator, needle, timeoutMs)
}

async function waitForCount(locator, expected, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if ((await locator.count()) === expected) {
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
  console.log('\n=== 麻脸巫婆之夜批次（pit-hag）取证结论 ===')
  for (const result of results) {
    console.log(`${result.pass ? 'PASS' : 'FAIL'}  ${result.label}${result.detail ? ` → ${result.detail}` : ''}`)
  }

  const failed = results.filter((result) => !result.pass)
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
  const parsed = { port: 5413, vitePort: 5293 }
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
    if (condition()) {
      return true
    }

    await sleep(25)
  }

  return condition()
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
