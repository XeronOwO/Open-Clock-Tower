/**
 * 角色变更族（理发师 / 方古 / 哲学家）批次装置 —— 票据 docs/backlog/done/character-change-family.md 的验收矩阵。
 *
 * **第一局**回答：**「获得能力 → 被选角色持有者醉酒 → 代行获得的能力」「首次成功杀外来者 → 侵染」
 * 「理发师死亡 → 当夜换角 + 尚未进入的格重绑」这三条链路，在同一局真机上跑得通吗？**
 * 场景（固定 6 席：1 哲学家 / 2 筑梦师 / 3 方古 / 4 理发师 / 5 呆瓜 / 6 畸形秀演员）：
 *   1) 分配 → 开首夜；1 号在**真玩家页面**上拿到镇民 / 外来者清单（16 项 + 摇头）→ 选「筑梦师」；
 *   2) 断言：1 号**不变身**、2 号牌面出现「醉酒」标记、1 号牌面出现「获得能力」标记、
 *      效果链里 `philosopher.grant` 带被获得的角色、`philosopher.grant.drunk` 落在 2 号；
 *   3) 醉酒的筑梦师**照常拿到请求**（不因醉酒而不被唤醒）；作答后信息退回说书人裁定；
 *   4) 第二夜：1 号的格上执行的是**获得的能力**（筑梦师的提示：不含自己、没有摇头），说书人强制作废后继续；
 *   5) 3 号方古击杀 5 号呆瓜（外来者）→ 5 号变成**邪恶方古**且**不死亡**、3 号死亡（原方古）；
 *      同一次视图推送里魔典中心出现「限一次」标记（`hub-once-marker`）：文本 = 限一次、
 *      title 归属 = 3 号方古 → 5 号（方古侵染的**整局事实**，R-0034；只说书人可见）；
 *   6) 第三夜：「限一次」已用 → 新方古（5 号）击杀 4 号理发师 → **普通死亡**（标记仍在：
 *      整局事实不因换夜消失）；
 *   7) 同夜理发师格：存活恶魔（5 号）拿到「玩家对 / 不交换」请求 → 选 `pair:1+2` → 只写角色维度地互换；
 *      窗口内魔典中心挂着「今晚理发」标记（`hub-barber-night`：文本 = 今晚理发、title 记以理发师身份
 *      死亡的 4 号），**结清后消失**（R-0033 的待处理事实收口，不残留到下一夜）；
 *   8) 换手后**尚未进入**的筑梦师格重绑给新持有者（1 号又拿到筑梦师提示）→ 强制作废；
 *   9) 1 号失去角色能力 → 「获得能力」事实与醉酒一并终止（来源失去能力）；
 *  10) 视角隔离：1 号自己的页面上没有说书人词汇；2 / 4 / 6 号的全部推送无越权字段。
 *  11) 说书人实时复盘（复盘票据矩阵行 3，E29 补）：说书人上报 6 号中毒 → 开「复盘」→ 逐步回放到
 *      醉酒 / 换角 / 恶魔击杀箭头 / 换手 / 中毒 五步，逐张截图并断言标记文案与红色箭头连线
 *      （截图 cc-15…cc-20；中毒维度的输入事件与诺-达鲺常驻中毒同类型：SeatStateChangedEvent）。
 *
 * **第二局**（E17 残余②，R-0036 第 4 条的「被选角色不在场」路径）：6 席花名册不变，但 1 号哲学家改选
 * **不在场**的钟表匠——顺序表上钟表匠的格在哲学家之后、且这一格没有行动者，因此**当夜**就地激活由他代行；
 * 断言：不变身 / 没有醉酒对象（效果链无 `philosopher.grant.drunk`）/ 同夜开出钟表匠的说书人裁定点 /
 * 结清后账本记「1 号 · clockmaker · 正常生效」/ 信息结果下发到 1 号玩家端。
 * 第二局另起真宿主（独立临时库）与第二套 Vite，端口 = 第一局 +1。
 *
 * 与主批次的分工：主批次跑五席固定花名册的通用玩法回归；本装置只跑这三条能力链路。
 * 前置：Node >= 22.5（node:sqlite）、本机已构建 web/node_modules（playwright + @microsoft/signalr）。
 * 用法（在仓库根运行；默认迭代档 = 快节拍 + 不落盘截图 + 复用产物；两局都会跑）：
 *   node tools/verify-character-change.mjs                                        # 迭代档
 *   node tools/verify-character-change.mjs --quota 2 --screenshots-all --build    # 取证档（一批一次）
 *   node tools/verify-character-change.mjs --port 5500 --vite-port 5400           # 自定端口（第二局 +1）
 *
 * 外部耦合（换机器先核对 web/AGENTS.md §3.1）：宿主编译产物路径、SQLite 表 Games 的
 * StorytellerTicket / SeatsJson 列形状、说书人魔典中心的两枚标记锚点
 * （`hub-once-marker` 文本 = 「限一次」/ `hub-barber-night` 文本 = 「今晚理发」；title 里带席位归属）。
 * 退出码：0 = 全过；1 = 有失败；2 = 环境缺依赖。
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

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-charchange-'))
const databasePath = path.join(workspace, 'character-change.db')
const screenshotsDir = path.resolve(repositoryRoot, 'artifacts', 'web')
mkdirSync(screenshotsDir, { recursive: true })

const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`
const hubUrl = `${serverUrl}/hub/game`

/** 六个席位（与 web/src/display/labels.ts 的花名册一致）：三条链路各要的角色都在。 */
const ASSIGN = ['philosopher', 'dreamer', 'fang-gu', 'barber', 'klutz', 'mutant']
const seatOf = (slug) => ASSIGN.indexOf(slug) + 1
const PHILOSOPHER_SEAT = seatOf('philosopher')
const DREAMER_SEAT = seatOf('dreamer')
const FANG_GU_SEAT = seatOf('fang-gu')
const BARBER_SEAT = seatOf('barber')
const KLUTZ_SEAT = seatOf('klutz')
const MUTANT_SEAT = seatOf('mutant')

const PUSH_METHODS = [
  'ReceiveOperationRequest',
  'ReceiveOperationRequestVoided',
  'ReceiveOperationRequestAnswered',
  'ReceivePhaseStarted',
  'ReceiveInformationResult',
  'ReceiveDayChanged',
]

/** 无关玩家端不该出现的词：获得能力事实、理发师事实与裁定点字段（D-0012 §4.3 的信息隔离）。
 *  注意：说书人**自己填**的作废说明会原样下发（自由文本，属说书人裁量 D-0002），
 *  因此本批取证一律用中性文案，不把"醉酒"这类事实写进说明里。 */
const FORBIDDEN_PLAYER_TOKENS = [
  'philosopher.grant',
  'grantedCharacter',
  '获得能力',
  'barberNight',
  '今晚理发',
  'deferred',
  '待定死亡',
  'awaitingDecision',
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
  await runPresentGrantScene()
  await runAbsentGrantScene()
}

/** 第一局：被获得角色**在场**（筑梦师）——代行落在哲学家自己的格上（R-0036 第 4 条后半）。 */
async function runPresentGrantScene() {
  console.log('=== 1/14 构建并启动真宿主（独立临时库，6 席）===')
  await ensureServerArtifacts({ repositoryRoot, buildMode: config.buildMode })
  await startServer({ serverUrl, databasePath, seatCount: ASSIGN.length })

  console.log('=== 2/14 取票据并起 Vite ===')
  const ticket = readStorytellerTicket(databasePath)
  const seatTickets = readSeatTickets(databasePath)
  check('席位票据齐备（6 席）', seatTickets.length === 6, `数据库 ${seatTickets.length} 张`)

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

  console.log('=== 3/14 说书人 + 哲学家（1 号）/ 方古（3 号）/ 呆瓜（5 号）加入真浏览器 ===')
  const browser = await playwright.chromium.launch()
  const consoleErrors = []
  const storytellerPage = await newPage(browser, { width: 1600, height: 1100 }, consoleErrors)
  await storytellerPage.goto(viteUrl)
  await storytellerPage.getByPlaceholder('说书人票据').fill(ticket)
  await storytellerPage.getByRole('button', { name: '加入' }).click()
  await storytellerPage.locator('[data-testid="grimoire"]').waitFor({ timeout: 30_000 })
  check('说书人加入后看板可见（魔典主视图）', (await storytellerPage.locator('[data-testid="grimoire"]').count()) === 1)

  const joinAs = async (page, seatTicket, seat, label) => {
    await page.goto(`${viteUrl}/#player`)
    await page.getByPlaceholder('席位票据').fill(seatTicket)
    await page.getByRole('button', { name: '加入' }).click()
    const badge = await waitForText(page.locator('[data-testid="player-seat"]'), String(seat), 30_000)
    check(`${label}加入玩家端`, badge.includes(String(seat)), badge)
  }

  const philosopherPage = await newPage(browser, { width: 900, height: 1100 }, consoleErrors)
  await joinAs(philosopherPage, seatTickets[PHILOSOPHER_SEAT - 1].ticket, PHILOSOPHER_SEAT, '哲学家席（1 号）')
  const fangGuPage = await newPage(browser, { width: 900, height: 1100 }, consoleErrors)
  await joinAs(fangGuPage, seatTickets[FANG_GU_SEAT - 1].ticket, FANG_GU_SEAT, '方古席（3 号）')
  const klutzPage = await newPage(browser, { width: 900, height: 1100 }, consoleErrors)
  await joinAs(klutzPage, seatTickets[KLUTZ_SEAT - 1].ticket, KLUTZ_SEAT, '呆瓜席（5 号）')

  // 无关玩家（2 号筑梦师 / 4 号理发师 / 6 号畸形秀演员）用 Node 客户端驱动：它们的全部推送进越权扫描。
  const unrelatedSeats = [
    await connectSeat(seatTickets[DREAMER_SEAT - 1]),
    await connectSeat(seatTickets[BARBER_SEAT - 1]),
    await connectSeat(seatTickets[MUTANT_SEAT - 1]),
  ]

  console.log('=== 4/14 分配 → 开首夜 ===')
  const assignmentSelects = storytellerPage.locator('section', { hasText: '开局分配' }).locator('select')
  for (const [index, slug] of ASSIGN.entries()) {
    await assignmentSelects.nth(index).selectOption(slug)
  }

  const assigned = await runCommand(storytellerPage, '分配', () =>
    storytellerPage.getByRole('button', { name: '提交分配' }).click(),
  )
  check('分配 6 个角色被受理', assigned.kind === 'Accepted', assigned.raw)

  const firstNight = await startNightWhenReady(storytellerPage, 1)
  check('开首夜被受理', firstNight.kind === 'Accepted', firstNight.raw)

  console.log('=== 5/14 首夜 · 哲学家在真界面上选要获得的能力（镇民 / 外来者清单）===')
  const philosopherOptions = philosopherPage.locator('[data-testid="player-request-options"] [data-option-value]')
  await philosopherOptions.first().waitFor({ timeout: 90_000 })
  const optionValues = await philosopherOptions.evaluateAll((nodes) =>
    nodes.map((node) => node.getAttribute('data-option-value')),
  )
  check(
    '提示清单 = 16 个镇民 / 外来者 + 摇头（1 项）',
    optionValues.length === 17,
    `渲染 ${optionValues.length} 项：${optionValues.slice(0, 5).join(',')}…`,
  )
  check(
    '清单含筑梦师 / 呆瓜，不含哲学家自己与爪牙 / 恶魔',
    optionValues.includes('dreamer') &&
      optionValues.includes('klutz') &&
      !optionValues.includes('philosopher') &&
      !optionValues.includes('vortox') &&
      !optionValues.includes('pit-hag'),
    optionValues.join(','),
  )
  check(
    '清单末尾是摇头（decline，即"本夜不使用能力"）',
    optionValues[optionValues.length - 1] === 'decline',
    optionValues[optionValues.length - 1] ?? '（无）',
  )
  await screenshot(philosopherPage, 'cc-01-philosopher-grant-request')

  await philosopherPage.locator('[data-testid="player-request-options"] [data-option-value="dreamer"]').click()
  await philosopherPage.getByTestId('player-submit').click()

  const philosopherCard = storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${PHILOSOPHER_SEAT}"]`)
  const dreamerCard = storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${DREAMER_SEAT}"]`)
  const stillPhilosopher = await waitForAttribute(philosopherCard, 'data-character', 'philosopher', 30_000)
  check('哲学家**不变身**（角色维度仍是 philosopher）', stillPhilosopher === 'philosopher', `data-character=${stillPhilosopher}`)

  const drunkMark = await waitForLocatorContains(dreamerCard, '醉酒', 30_000)
  check('被选角色（2 号筑梦师）的持有者醉酒（牌面出现「醉酒」标记）', drunkMark.includes('醉酒'), compact(drunkMark))
  const grantMark = await waitForLocatorContains(philosopherCard, '获得能力', 30_000)
  check('哲学家自己的牌面出现「获得能力」标记（本次授予）', grantMark.includes('获得能力'), compact(grantMark))
  await screenshot(storytellerPage, 'cc-02-drunk-mark')

  await setDataDrawer(storytellerPage, true)
  const grantChain = await panelText(storytellerPage, '效果归因链')
  check(
    '效果链：获得能力事实带被获得的角色（获得能力：筑梦师）',
    grantChain.includes('philosopher.grant') &&
      grantChain.includes('获得能力') &&
      grantChain.includes('筑梦师'),
    compact(grantChain),
  )
  check(
    '效果链：常驻醉酒落在被选角色的持有者（2 号）身上',
    grantChain.includes('philosopher.grant.drunk') && grantChain.includes('2 号'),
    compact(grantChain),
  )
  await screenshot(storytellerPage, 'cc-03-effect-chain-grant')
  await setDataDrawer(storytellerPage, false)

  console.log('=== 6/14 首夜 · 醉酒的筑梦师照常被唤醒（信息由说书人裁定）===')
  const dreamerRequests = await waitForSeatRequest(unrelatedSeats[0], 90_000)
  check('醉酒的筑梦师照常拿到请求（不因醉酒而不被唤醒）', dreamerRequests.length >= 1, `收到 ${dreamerRequests.length} 条`)
  const drunkAnswer = await unrelatedSeats[0].invoke(
    'SubmitResponse',
    dreamerRequests[0].requestId,
    `seat:${FANG_GU_SEAT}`,
    'cc-drunk-dream',
    1,
  )
  check('醉酒者的作答被受理（能力照常使用、只是不生效）', drunkAnswer?.kind === 'Accepted', `kind=${drunkAnswer?.kind ?? '（无回执）'}`)
  const drunkDecision = await waitForDecision(storytellerPage, (text) => text.includes('未生效'), 60_000)
  check('醉酒的筑梦师：信息退回由说书人裁定', drunkDecision.includes('未生效'), compact(drunkDecision))
  const settledDrunk = await settleFreeDecision(storytellerPage, '批次取证：本条信息由说书人裁定')
  check('该裁定被受理', settledDrunk.kind === 'Accepted', settledDrunk.raw)

  console.log('=== 7/14 第二夜 · 哲学家在**自己的格**上代行获得的能力；方古侵染外来者 ===')
  const secondNight = await startNightWhenReady(storytellerPage, 2)
  check('开第二夜被受理', secondNight.kind === 'Accepted', secondNight.raw)

  const delegatedOptions = philosopherPage.locator('[data-testid="player-request-options"] [data-option-value]')
  await delegatedOptions.first().waitFor({ timeout: 90_000 })
  const delegatedValues = await delegatedOptions.evaluateAll((nodes) =>
    nodes.map((node) => node.getAttribute('data-option-value')),
  )
  check(
    '哲学家的格上执行的是获得的能力（筑梦师的提示：5 项、不含自己、没有摇头）',
    delegatedValues.length === 5 &&
      !delegatedValues.includes('seat:1') &&
      !delegatedValues.includes('decline'),
    delegatedValues.join(','),
  )
  await screenshot(philosopherPage, 'cc-04-delegated-dreamer-prompt')
  const delegatedVoid = await forceVoidPending(
    storytellerPage,
    'StorytellerForce',
    '批次取证：哲学家的代行槽位（本批只验证提示与链路）',
  )
  check('代行槽位的请求可被说书人强制作废（夜继续走）', delegatedVoid.kind === 'Accepted', delegatedVoid.raw)

  const fangGuOptions = fangGuPage.locator('[data-testid="player-request-options"] [data-option-value]')
  await fangGuOptions.first().waitFor({ timeout: 90_000 })
  const fangGuValues = await fangGuOptions.evaluateAll((nodes) =>
    nodes.map((node) => node.getAttribute('data-option-value')),
  )
  check(
    '方古的提示 = 全体席位（含自己与已死亡玩家）',
    JSON.stringify(fangGuValues) === JSON.stringify(['seat:1', 'seat:2', 'seat:3', 'seat:4', 'seat:5', 'seat:6']),
    fangGuValues.join(','),
  )
  await screenshot(fangGuPage, 'cc-05-fanggu-kill-request')
  await fangGuPage.locator(`[data-testid="player-request-options"] [data-option-value="seat:${KLUTZ_SEAT}"]`).click()
  await fangGuPage.getByTestId('player-submit').click()

  const klutzCard = storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${KLUTZ_SEAT}"]`)
  const converted = await waitForAttribute(klutzCard, 'data-character', 'fang-gu', 30_000)
  check('首次成功杀死外来者 → 5 号变成方古（角色维度变化）', converted === 'fang-gu', `data-character=${converted}`)
  check(
    '被攻击者**不死亡**（是侵染，不是击杀）',
    (await klutzCard.getAttribute('data-life')) === 'Alive',
    `data-life=${await klutzCard.getAttribute('data-life')}`,
  )
  const newDemonText = compact(await klutzCard.innerText())
  check('新方古是邪恶阵营（牌面阵营文案）', newDemonText.includes('邪恶'), newDemonText)
  const originalDead = await waitForAttribute(
    storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${FANG_GU_SEAT}"]`),
    'data-life',
    'Dead',
    30_000,
  )
  check('原方古死亡（3 号），角色标记仍留在牌面上', originalDead === 'Dead', `data-life=${originalDead}`)

  // 魔典中心的「限一次」标记：方古侵染是整局事实（R-0034），说书人一眼看到它有没有用掉。
  const onceMarker = await waitForHubToken(storytellerPage, 'hub-once-marker', 30_000)
  check(
    '方古侵染成功后：魔典中心出现「限一次」标记（hub-once-marker 可见）',
    onceMarker.visible,
    `visible=${onceMarker.visible}；文本=${onceMarker.text}`,
  )
  check('「限一次」标记文本 = 限一次', onceMarker.text === '限一次', `文本=${onceMarker.text}`)
  check(
    `「限一次」标记归属正确（title 记 ${FANG_GU_SEAT} 号 → ${KLUTZ_SEAT} 号）`,
    onceMarker.title.includes(`${FANG_GU_SEAT} 号`) && onceMarker.title.includes(`${KLUTZ_SEAT} 号`),
    `title=${onceMarker.title}`,
  )
  await screenshot(storytellerPage, 'cc-06-fanggu-conversion')

  // 第二夜剩下的格：醉酒的筑梦师同样照常被唤醒（它的请求挂起会让夜停在这里）。
  const dreamerNightTwo = await waitUntil(() => unrelatedSeats[0].requests.length >= 2, 90_000)
  check('醉酒的筑梦师第二夜同样照常被唤醒', dreamerNightTwo, `累计 ${unrelatedSeats[0].requests.length} 条请求`)
  const nightTwoVoid = await forceVoidPending(
    storytellerPage,
    'StorytellerForce',
    '批次取证：越过该槽位（第二夜）',
  )
  check('第二夜的筑梦师槽位可被强制作废（夜继续走）', nightTwoVoid.kind === 'Accepted', nightTwoVoid.raw)

  console.log('=== 8/14 第三夜 · 「限一次」已用 → 击杀理发师（普通死亡）→ 理发师格开换角请求 ===')
  const thirdNight = await startNightWhenReady(storytellerPage, 3)
  check('开第三夜被受理', thirdNight.kind === 'Accepted', thirdNight.raw)

  await philosopherPage.locator('[data-testid="player-request-options"] [data-option-value]').first().waitFor({ timeout: 90_000 })
  const thirdVoid = await forceVoidPending(
    storytellerPage,
    'StorytellerForce',
    '批次取证：哲学家的代行槽位（第三夜）',
  )
  check('第三夜的代行槽位请求同样可被强制作废', thirdVoid.kind === 'Accepted', thirdVoid.raw)

  // 第三夜方古的格绑给**存活**的新方古（5 号）：击杀请求发到 5 号的页面，不是已死亡的 3 号。
  await klutzPage
    .locator(`[data-testid="player-request-options"] [data-option-value="seat:${BARBER_SEAT}"]`)
    .waitFor({ timeout: 90_000 })
  await klutzPage.locator(`[data-testid="player-request-options"] [data-option-value="seat:${BARBER_SEAT}"]`).click()
  await screenshot(klutzPage, 'cc-07-new-fanggu-kill')
  await klutzPage.getByTestId('player-submit').click()

  const barberCard = storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${BARBER_SEAT}"]`)
  const barberDead = await waitForAttribute(barberCard, 'data-life', 'Dead', 30_000)
  check('「限一次」已用 → 外来者（4 号理发师）正常死亡', barberDead === 'Dead', `data-life=${barberDead}`)
  check(
    '理发师没有被侵染（角色仍是 barber）',
    (await barberCard.getAttribute('data-character')) === 'barber',
    `data-character=${await barberCard.getAttribute('data-character')}`,
  )

  // 整局事实的另一半：换到第三夜之后「限一次」标记仍在（它记的是"用过没有"，不是"本夜发生"）。
  const onceMarkerLaterNight = await waitForHubToken(storytellerPage, 'hub-once-marker', 15_000)
  check(
    '「限一次」是整局事实：第三夜标记仍在（不随换夜 / 换角消失）',
    onceMarkerLaterNight.visible && onceMarkerLaterNight.text === '限一次',
    `visible=${onceMarkerLaterNight.visible}；文本=${onceMarkerLaterNight.text}`,
  )

  // 同夜的理发师格：恶魔（此刻存活的方古 = 5 号）拿到「玩家对 / 不交换」原子请求。
  const swapOptions = klutzPage.locator('[data-testid="player-request-options"] [data-option-value^="pair:"]')
  await swapOptions.first().waitFor({ timeout: 90_000 })

  // 「今晚理发」待处理事实（R-0033）：请求挂起 = 事实开着，此刻魔典中心必须挂着这枚标记
  // （放在请求观测之后断言：请求还在，事实就一定没被收口，不受节拍竞态影响）。
  const barberNightOpen = await waitForHubToken(storytellerPage, 'hub-barber-night', 30_000)
  check(
    '理发师之夜的窗口内：魔典中心出现「今晚理发」标记（hub-barber-night 可见）',
    barberNightOpen.visible,
    `visible=${barberNightOpen.visible}；文本=${barberNightOpen.text}`,
  )
  check('「今晚理发」标记文本 = 今晚理发', barberNightOpen.text === '今晚理发', `文本=${barberNightOpen.text}`)
  check(
    `「今晚理发」标记归属正确（title 记以理发师身份死亡的 ${BARBER_SEAT} 号）`,
    barberNightOpen.title.includes(`${BARBER_SEAT} 号`),
    `title=${barberNightOpen.title}`,
  )
  await screenshot(storytellerPage, 'cc-08-barber-night-marker')

  const swapValues = await klutzPage
    .locator('[data-testid="player-request-options"] [data-option-value]')
    .evaluateAll((nodes) => nodes.map((node) => node.getAttribute('data-option-value')))
  check(
    '理发师格：换角请求给到存活的恶魔，且含「不交换」',
    swapValues.includes('decline') && swapValues.some((value) => value.startsWith('pair:')),
    swapValues.slice(0, 4).join(','),
  )
  await screenshot(klutzPage, 'cc-08-barber-swap-request')
  await klutzPage
    .locator(
      `[data-testid="player-request-options"] [data-option-value="pair:${PHILOSOPHER_SEAT}+${DREAMER_SEAT}"]`,
    )
    .click()
  await klutzPage.getByTestId('player-submit').click()

  const swappedPhilosopher = await waitForAttribute(philosopherCard, 'data-character', 'dreamer', 30_000)
  check('交换只写角色维度：1 号变成筑梦师', swappedPhilosopher === 'dreamer', `data-character=${swappedPhilosopher}`)
  const swappedDreamer = await waitForAttribute(dreamerCard, 'data-character', 'philosopher', 30_000)
  check('2 号变成哲学家（阵营不变）', swappedDreamer === 'philosopher', `data-character=${swappedDreamer}`)

  // 收口（恶魔按「玩家对」结清）后事实关闭：标记在同一份视图推送里消失，不残留到下一夜。
  const barberNightClosed = await waitForHubTokenGone(storytellerPage, 'hub-barber-night', 30_000)
  check(
    '换角结清后：「今晚理发」标记消失（事实收口，不残留）',
    barberNightClosed,
    barberNightClosed ? '已消失' : '标记仍在',
  )
  await screenshot(storytellerPage, 'cc-09-swap-applied')

  // 换手后尚未进入的筑梦师格重绑给新持有者（1 号）：它在真界面上拿到筑梦师的请求（R-0032）。
  // 记录「有没有收到」而不是把没收到当成脚本异常：这是要被 E17 逐行判定的行为证据。
  // 注意：本装置的 `waitUntil` 只接受同步谓词（传 Promise 会抛错），页面读取必须自己轮询。
  let reboundValues = []
  let reboundArrived = false
  const reboundDeadline = Date.now() + 45_000
  while (Date.now() < reboundDeadline && !reboundArrived) {
    const values = await philosopherPage
      .locator('[data-testid="player-request-options"] [data-option-value]')
      .evaluateAll((nodes) => nodes.map((node) => node.getAttribute('data-option-value')))
    if (values.length > 0) {
      reboundValues = values
      reboundArrived = true
      break
    }

    await sleep(200)
  }
  const swappedPending =
    (await storytellerPage.locator('[data-testid="console-pending"]').count()) > 0
      ? compact(await readTextBounded(storytellerPage.locator('[data-testid="console-pending"]').first()))
      : '（说书人挂起面：当前没有挂起请求）'
  check(
    '换手后尚未进入的筑梦师格重绑给新持有者（1 号收到筑梦师提示）',
    reboundArrived,
    reboundArrived ? `提示选项：${reboundValues.join(',')}` : `1 号页面上没有新请求；${swappedPending}`,
  )
  if (reboundArrived) {
    check(
      '重绑后的提示是筑梦师的（5 项、不含自己）',
      reboundValues.length === 5 && !reboundValues.includes('seat:1'),
      reboundValues.join(','),
    )
    const reboundVoid = await forceVoidPending(
      storytellerPage,
      'StorytellerForce',
      '批次取证：越过该槽位（换手后重绑）',
    )
    check('重绑后的请求可被强制作废（夜继续走）', reboundVoid.kind === 'Accepted', reboundVoid.raw)
  }

  await setDataDrawer(storytellerPage, true)
  const terminatedChain = await panelText(storytellerPage, '效果归因链')
  check(
    '哲学家失去角色能力 → 获得能力事实与醉酒一并终止（来源失去能力）',
    terminatedChain.includes('philosopher.grant') &&
      terminatedChain.includes('已终止') &&
      terminatedChain.includes('来源失去能力'),
    compact(terminatedChain),
  )
  await screenshot(storytellerPage, 'cc-10-grant-terminated')
  await setDataDrawer(storytellerPage, false)

  console.log('=== 9/14 视角隔离：玩家端没有这些说书人专属事实 ===')
  const philosopherPageText = compact(await philosopherPage.locator('body').innerText())
  check(
    '哲学家自己的页面上没有能力事实字段（获得能力 / philosopher.grant）',
    !philosopherPageText.includes('获得能力') && !philosopherPageText.includes('philosopher.grant'),
    compact(philosopherPageText),
  )

  const receivedText = JSON.stringify(unrelatedSeats.map((client) => client.messages))
  const leaked = FORBIDDEN_PLAYER_TOKENS.filter((token) => receivedText.includes(token))
  check(
    '三席无关玩家（2 / 4 / 6 号）收到的全部推送里没有越权字段',
    leaked.length === 0,
    leaked.join(', ') ||
      `已扫描 ${unrelatedSeats.reduce((total, client) => total + client.messages.length, 0)} 条推送`,
  )

  console.log('=== 10/14 收口：说书人实时复盘（五类标记）→ 浏览器零错误 ===')
  // —— 说书人实时复盘（复盘票据矩阵行 3 的真机取证）：游戏仍在进行，说书人面按 D-0020 是实时面 ——
  // 中毒维度的输入事件由说书人上报产出 `SeatStateChangedEvent`（Poison 维度）；诺-达鲺的常驻中毒
  // 经 `DimensionEffectReconciler` 产出的也是同一事件类型、同一个 presenter 与同一套标记文案。
  const poisonReport = await reportSeatState(storytellerPage, {
    seat: MUTANT_SEAT,
    dimensionLabel: '中毒',
    value: 'Poisoned',
    reason: '批次取证：说书人裁定 6 号中毒（复盘中毒标记的输入事件）',
  })
  check('说书人上报 6 号中毒被受理（中毒标记的事件输入）', poisonReport.kind === 'Accepted', poisonReport.raw)

  await storytellerPage.getByTestId('storyteller-replay-open').click()
  await storytellerPage.getByTestId('replay-panel').waitFor({ timeout: 30_000 })
  const replayFirstProgress = await waitForText(storytellerPage.getByTestId('replay-progress'), '第 1 /', 30_000)
  const replayScope = compact(await storytellerPage.getByTestId('replay-scope').innerText())
  check('说书人可开实时面复盘（游戏未结束：口径 = 说书人实时面）', replayScope.includes('说书人实时面'), replayScope)
  check('复盘面板停在首步（窗口按页给、不一次全渲染）', replayFirstProgress.startsWith('第 1 /'), compact(replayFirstProgress))
  await screenshot(storytellerPage, 'cc-15-replay-storyteller-live')

  // 五类标记按事件流自然顺序逐一回放取证。只匹配当前步骤的标记列表（不看牌面与摘要），
  // 避免把牌面上的同名词误判成本步的复盘标记。
  const replayTargets = [
    { label: '醉酒', name: 'cc-16-replay-drunk' },
    { label: '换角', name: 'cc-17-replay-character-change' },
    // 恶魔击杀取「第三夜新方古（5 号）击杀理发师（4 号）」这一步；侵染时原方古自死不画箭头。
    { label: '恶魔击杀', name: 'cc-18-replay-kill-arrow', require: `${KLUTZ_SEAT} 号 → ${BARBER_SEAT} 号` },
    { label: '换手', name: 'cc-19-replay-role-rebind' },
    { label: '中毒', name: 'cc-20-replay-poison' },
  ]
  for (const target of replayTargets) {
    const found = await scanReplayForMarker(storytellerPage, target.label, { require: target.require })
    check(`复盘逐步回放到「${target.label}」标记步骤`, found.found, found.progress)
    if (!found.found) {
      continue
    }

    const legend = compact(await storytellerPage.getByTestId('replay-markers').innerText())
    check(`「${target.label}」进入圆盘图例（与实时魔典同口径）`, legend.includes(target.label), legend)
    if (target.label === '换角' || target.label === '换手') {
      check(`「${target.label}」标记带归属文案（谁 → 谁）`, found.markers.includes('→'), found.markers)
    }

    if (target.label === '恶魔击杀') {
      const arrowLines = await storytellerPage.locator('[data-testid="replay-ring"] svg line').count()
      check('恶魔击杀 = 圆盘上的红色箭头（SVG 连线存在）', arrowLines >= 1, `line=${arrowLines}`)
    }

    await screenshot(storytellerPage, target.name)

    if (target.label === '换角') {
      // 紧接的下一步是侵染的另一半事实——原方古（3 号）自死：只画死亡帷幕，不画击杀箭头
      // （CausedBy = 自己；自指归因不是「被恶魔击杀」，判据修正的实机回归）。这一步另存一张截图。
      await storytellerPage.getByTestId('replay-next').click()
      await sleep(150)
      const selfDeathMarkers = await replayMarkerText(storytellerPage)
      check(
        '侵染的下一步（原方古自死）只有死亡帷幕、没有击杀箭头',
        selfDeathMarkers.includes('死亡') && !selfDeathMarkers.includes('恶魔击杀'),
        selfDeathMarkers,
      )
      await screenshot(storytellerPage, 'cc-21-replay-self-death-no-arrow')
    }
  }

  check('浏览器控制台没有报错', consoleErrors.length === 0, consoleErrors.slice(0, 3).join(' | '))
  await browser.close()
}

/**
 * 第二局（E17 残余②，R-0036 第 4 条的另一条路径）：被获得角色**不在场**时，当夜那一格就地激活由他代行。
 * 与第一局只差一处：1 号选的钟表匠不在 6 席花名册里（它的格没有行动者），因此落格落在**它的格**上。
 */
async function runAbsentGrantScene() {
  const absentServerUrl = `http://localhost:${options.port + 1}`
  const absentViteUrl = `http://localhost:${options.vitePort + 1}`
  const absentDatabasePath = path.join(workspace, 'philosopher-absent.db')

  console.log('=== 11/14 第二局：起第二个真宿主（独立临时库）+ 第二套 Vite ===')
  // 宿主产物第一局已经备好（ensureServerArtifacts 跑在更前面），这里只起进程。
  await startServer({ serverUrl: absentServerUrl, databasePath: absentDatabasePath, seatCount: ASSIGN.length })
  const ticket = readStorytellerTicket(absentDatabasePath)
  const seatTickets = readSeatTickets(absentDatabasePath)
  check('第二局：席位票据齐备（6 席）', seatTickets.length === 6, `数据库 ${seatTickets.length} 张`)

  const vite = spawn(
    process.execPath,
    [
      path.join(webRoot, 'node_modules', 'vite', 'bin', 'vite.js'),
      '--port',
      String(options.vitePort + 1),
      '--strictPort',
    ],
    {
      cwd: webRoot,
      env: { ...process.env, VITE_SERVER_TARGET: absentServerUrl, VITE_SEAT_COUNT: String(ASSIGN.length) },
      stdio: 'ignore',
    },
  )
  children.push(vite)
  await waitForHttp(absentViteUrl, '第二套 Vite 开发服务器', 60_000)

  console.log('=== 12/14 第二局：说书人 + 哲学家（1 号）加入 → 分配 → 开首夜 ===')
  const browser = await playwright.chromium.launch()
  const consoleErrors = []
  const storytellerPage = await newPage(browser, { width: 1600, height: 1100 }, consoleErrors)
  await storytellerPage.goto(absentViteUrl)
  await storytellerPage.getByPlaceholder('说书人票据').fill(ticket)
  await storytellerPage.getByRole('button', { name: '加入' }).click()
  await storytellerPage.locator('[data-testid="grimoire"]').waitFor({ timeout: 30_000 })
  check('第二局：说书人加入后看板可见（魔典主视图）', (await storytellerPage.locator('[data-testid="grimoire"]').count()) === 1)

  const philosopherPage = await newPage(browser, { width: 900, height: 1100 }, consoleErrors)
  await philosopherPage.goto(`${absentViteUrl}/#player`)
  await philosopherPage.getByPlaceholder('席位票据').fill(seatTickets[PHILOSOPHER_SEAT - 1].ticket)
  await philosopherPage.getByRole('button', { name: '加入' }).click()
  const absentBadge = await waitForText(
    philosopherPage.locator('[data-testid="player-seat"]'),
    String(PHILOSOPHER_SEAT),
    30_000,
  )
  check('第二局：哲学家席（1 号）加入玩家端', absentBadge.includes(String(PHILOSOPHER_SEAT)), absentBadge)

  const assignmentSelects = storytellerPage.locator('section', { hasText: '开局分配' }).locator('select')
  for (const [index, slug] of ASSIGN.entries()) {
    await assignmentSelects.nth(index).selectOption(slug)
  }

  const assigned = await runCommand(storytellerPage, '分配', () =>
    storytellerPage.getByRole('button', { name: '提交分配' }).click(),
  )
  check('第二局：分配 6 个角色被受理', assigned.kind === 'Accepted', assigned.raw)
  const firstNight = await startNightWhenReady(storytellerPage, 1)
  check('第二局：开首夜被受理', firstNight.kind === 'Accepted', firstNight.raw)

  console.log('=== 13/14 第二局：哲学家选**不在场**的钟表匠（不变身、没有醉酒对象）===')
  const absentOptions = philosopherPage.locator('[data-testid="player-request-options"] [data-option-value]')
  await absentOptions.first().waitFor({ timeout: 90_000 })
  const absentValues = await absentOptions.evaluateAll((nodes) =>
    nodes.map((node) => node.getAttribute('data-option-value')),
  )
  check(
    '第二局：清单含钟表匠——它不在场上（6 席花名册里没有 clockmaker）',
    absentValues.includes('clockmaker'),
    `${absentValues.length} 项`,
  )
  await screenshot(philosopherPage, 'cc-11-absent-grant-choice')

  await philosopherPage.locator('[data-testid="player-request-options"] [data-option-value="clockmaker"]').click()
  await philosopherPage.getByTestId('player-submit').click()

  const philosopherCard = storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${PHILOSOPHER_SEAT}"]`)
  const stillPhilosopher = await waitForAttribute(philosopherCard, 'data-character', 'philosopher', 30_000)
  check('第二局：哲学家**不变身**（角色维度仍是 philosopher）', stillPhilosopher === 'philosopher', `data-character=${stillPhilosopher}`)
  const absentGrantMark = await waitForLocatorContains(philosopherCard, '获得能力', 30_000)
  check('第二局：牌面出现「获得能力」标记（不变身地获得）', absentGrantMark.includes('获得能力'), compact(absentGrantMark))

  const absentSeatCards = storytellerPage.locator('[data-testid="grimoire-seat"]')
  const absentSeatTexts = await absentSeatCards.evaluateAll((nodes) => nodes.map((node) => node.innerText))
  const drunkSeats = absentSeatTexts.filter((text) => text.includes('醉酒'))
  check(
    '第二局：被选角色不在场 → 没有醉酒对象（6 席牌面都没有「醉酒」标记）',
    drunkSeats.length === 0,
    drunkSeats.length === 0 ? `已扫描 ${absentSeatTexts.length} 席` : drunkSeats.map(compact).join(' | '),
  )

  await setDataDrawer(storytellerPage, true)
  const absentChain = await panelText(storytellerPage, '效果归因链')
  check(
    '第二局：效果链的获得能力事实带「钟表匠」',
    absentChain.includes('philosopher.grant') && absentChain.includes('钟表匠'),
    compact(absentChain),
  )
  check(
    '第二局：效果链里没有常驻醉酒（philosopher.grant.drunk）',
    !absentChain.includes('philosopher.grant.drunk'),
    compact(absentChain),
  )
  await screenshot(storytellerPage, 'cc-12-absent-grant-chain')
  await setDataDrawer(storytellerPage, false)

  console.log('=== 14/14 第二局：同一夜钟表匠的格就地激活由他代行 → 裁定 → 账本与玩家端 ===')
  const absentDecision = await waitForDecision(storytellerPage, (text) => text.includes('钟表匠'), 90_000)
  check(
    '第二局：同一夜（本局从未开第二夜）钟表匠的格就地激活为代行槽位',
    absentDecision.includes('钟表匠获得信息'),
    compact(absentDecision),
  )
  await screenshot(storytellerPage, 'cc-13-absent-delegated-decision')

  const absentSettled = await settleFreeDecision(storytellerPage, '最近距离 3')
  check('第二局：钟表匠的信息由说书人裁定并受理', absentSettled.kind === 'Accepted', absentSettled.raw)

  await setDataDrawer(storytellerPage, true)
  const ledgerSection = storytellerPage.locator('[data-testid="data-drawer-body"] section.panel', {
    hasText: '账本与结算结论',
  })
  const absentLedger = await waitForText(ledgerSection, 'clockmaker', 30_000)
  check(
    '第二局：账本记行动者哲学家（1 号）· 能力钟表匠 · 正常生效',
    absentLedger.includes('1 号') && absentLedger.includes('clockmaker') && absentLedger.includes('正常生效'),
    compact(absentLedger),
  )
  await screenshot(storytellerPage, 'cc-14-absent-ledger')
  await setDataDrawer(storytellerPage, false)

  const absentInfo = await waitForText(
    philosopherPage.locator('[data-testid="player-information"]'),
    '最近距离 3',
    30_000,
  )
  check(
    '第二局：信息结果下发到哲学家本人（能力名 clockmaker）',
    absentInfo.includes('clockmaker') && absentInfo.includes('最近距离 3'),
    compact(absentInfo),
  )
  check('第二局：浏览器控制台没有报错', consoleErrors.length === 0, consoleErrors.slice(0, 3).join(' | '))
  await browser.close()
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

/** 说书人上报座位状态：先点选该席的牌（操作台按席位就近），再只报本次观测到的维度。 */
async function reportSeatState(page, report) {
  await page.locator(`[data-testid="grimoire-seat"][data-seat="${report.seat}"]`).click()
  const panel = page.locator('[data-testid="seat-console"]')
  await panel.waitFor({ state: 'visible', timeout: 10_000 })

  const checkboxes = panel.locator('.dimensions input[type=checkbox]')
  for (let index = 0; index < (await checkboxes.count()); index += 1) {
    await checkboxes.nth(index).uncheck().catch(() => {})
  }

  const dimension = panel.locator('.dimensions label', { hasText: report.dimensionLabel })
  await dimension.locator('input[type=checkbox]').check()
  await dimension.locator('select').selectOption(report.value)

  await panel.getByPlaceholder('变化原因（必填，会随事件流记录）').fill(report.reason)
  return runCommand(page, `上报-${report.dimensionLabel}`, () =>
    page.getByRole('button', { name: '上报', exact: true }).click(),
  )
}

/** 当前步骤的标记列表文本（无标记时为空串）。 */
async function replayMarkerText(page) {
  const list = page.getByTestId('replay-marker-list')
  if ((await list.count()) === 0) {
    return ''
  }

  return compact(await readTextBounded(list))
}

/**
 * 在复盘面板上逐步向前扫描，直到当前步骤的标记列表里出现目标文案（`require` 再要求一段标记文本）。
 * 只读 `replay-marker-list`（当前步的标记），不读牌面 / 摘要，避免同名词误判；
 * 每步点一次「下一步」并让出一轮事件循环等 Vue 渲染，超时由 maxSteps 兜底。
 */
async function scanReplayForMarker(page, label, { require: requireText, maxSteps = 2000 } = {}) {
  return page.evaluate(
    async ({ wanted, required, limit }) => {
      const next = document.querySelector('[data-testid="replay-next"]')
      const progress = document.querySelector('[data-testid="replay-progress"]')
      if (next === null || progress === null) {
        return { found: false, progress: '（复盘面板未渲染）', markers: '' }
      }

      const readMarkers = () => {
        const list = document.querySelector('[data-testid="replay-marker-list"]')
        return (list?.textContent ?? '').replace(/\s+/g, ' ').trim()
      }
      const readProgress = () => (progress.textContent ?? '').replace(/\s+/g, ' ').trim()
      const matches = (markers) =>
        markers.includes(wanted) && (required === null || markers.includes(required))

      for (let index = 0; index < limit; index += 1) {
        const markers = readMarkers()
        if (matches(markers)) {
          return { found: true, progress: readProgress(), markers }
        }

        next.click()
        await new Promise((resolve) => setTimeout(resolve, 0))
      }

      return { found: false, progress: readProgress(), markers: readMarkers() }
    },
    { wanted: label, required: requireText ?? null, limit: maxSteps },
  )
}

async function readDecisionText(page) {
  const block = page.locator('[data-testid="console-decision"]')
  if ((await block.count()) === 0) {
    return ''
  }

  return compact(await readTextBounded(block.first()))
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

  return compact(await readTextBounded(section.first()))
}

/**
 * 魔典中心的标记（`hub-once-marker` = 限一次 / `hub-barber-night` = 今晚理发）：等它出现，
 * 读出可见性、文本与 title（title 里带席位归属）。标记是 `v-if` 渲染的，视图推送未到就 count = 0，
 * 所以这里轮询而不是读一次；超时返回最后一次快照（调用方据此报红并打印快照）。
 */
async function waitForHubToken(page, testId, timeoutMs = 30_000) {
  const token = page.locator(`[data-testid="${testId}"]`).first()
  const deadline = Date.now() + timeoutMs
  let snapshot = { visible: false, text: '', title: '' }
  while (Date.now() < deadline) {
    if (await token.isVisible().catch(() => false)) {
      snapshot = {
        visible: true,
        text: compact(await readTextBounded(token)),
        title: (await token.getAttribute('title').catch(() => null)) ?? '',
      }
      if (snapshot.text.length > 0) {
        return snapshot
      }
    }

    await sleep(150)
  }

  return snapshot
}

/** 等某个魔典中心标记消失（事实被收口 / 本夜过时不候）；超时返回 false。 */
async function waitForHubTokenGone(page, testId, timeoutMs = 30_000) {
  try {
    await page.locator(`[data-testid="${testId}"]`).waitFor({ state: 'detached', timeout: timeoutMs })
    return true
  } catch {
    return false
  }
}

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
    text = compact(await readTextBounded(locator))
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
  console.log('\n=== 角色变更族批次（理发师 / 方古 / 哲学家）取证结论 ===')
  for (const result of results) {
    console.log(`${result.pass ? 'PASS' : 'FAIL'}  ${result.label}${result.detail ? ` → ${result.detail}` : ''}`)
  }

  const failed = results.filter((result) => !result.pass)
  console.log(failed.length === 0 ? `全部通过（${results.length} 项）` : `失败 ${failed.length} / ${results.length}`)
}

function compact(text) {
  return String(text ?? '').replace(/\s+/g, ' ').trim()
}

/** 起一个真宿主进程（同一份 Release 产物；第二局换独立临时库与端口）。 */
async function startServer({ serverUrl, databasePath, seatCount }) {
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
      GameServer__SeatCount: String(seatCount),
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

  // 第二局用「第一局端口 +1」（见文件头）：两对端口交叉相等时，两套宿主 / Vite 会互相抢端口。
  if (parsed.vitePort === parsed.port + 1 || parsed.vitePort + 1 === parsed.port) {
    throw new Error(`端口组合与「第二局 = 第一局 +1」冲突：--port ${parsed.port} / --vite-port ${parsed.vitePort}`)
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


