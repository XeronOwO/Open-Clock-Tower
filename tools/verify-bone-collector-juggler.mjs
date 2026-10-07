/**
 * 集骨者重获能力的白天入口批次装置 —— 票据
 * `docs/backlog/done/bone-collector-regained-juggler-day-entry.md` 的**界面级取证**（原有残余）：
 *   · 行 4：玩家端入口（权限位）——死亡但被重获能力的杂耍艺人，在窗口存续的那个白天拿得到公开猜测入口；
 *   · 行 1 / 2：真在玩家页面上提交 → 受理、进当天账与公开面；没有窗口时照原口径拒绝；
 *   · 行 3：窗口内同一天仍然只允许一次；
 *   · 行 5：窗口到期（下个黄昏）后起算点回到原处——入口消失、提交被拒（原先只有间接覆盖）。
 *
 * 它回答：**「集骨者花掉唯一一次重获，救活已死亡杂耍艺人的能力」这条规则，在真宿主 + 真浏览器上
 * 从说书人的裁定点一路走到玩家的公开猜测面板——真的成立吗？窗口到期以后真的收得回去吗？**
 *
 * 场景（基础 5 席 + 1 名旅行者，独立一局）：
 *   1) 配板 1 亡骨魔 / 2 钟表匠 / 3 杂耍艺人 / 4 博学者 / 5 畸形秀演员 → 开首夜（只有钟表匠
 *      首夜入格，其余空槽按配额自己走完）→ 开第 1 天；
 *   2) 第 1 天：3 号（**活着**）在真玩家页面公开猜 1 条 → 说书人加入集骨者（第 6 席，旅行者）；
 *   3) 第 2 夜：3 号被恶魔击杀（真的死了）；集骨者的行动请求出现在**他自己的玩家页**上，
 *      候选里**一个已死亡席位都没有**（他还活着）→ 本人摇头不用，走完这一夜；
 *   4) 第 3 夜：集骨者的候选里出现 3 号（逐条写明「已死亡」）→ 本人选中 → 「重获能力」窗口生效、
 *      3 号在自己的格上被唤醒（报数请求真的挂起来）；
 *   5) 第 2 天：**已经死亡的 3 号**在玩家页面上重新拿到公开猜测入口（本票正题）、提交被受理、
 *      进公开面、无关席位看得到同一份；同日第二次入口消失、绕开界面再提交也被拒；
 *   6) 第 4 夜（下个黄昏）：窗口到期，3 号那一格回到空槽——说书人侧整夜不再出现报数裁定点；
 *   7) 第 3 天：入口不再出现（与第 2 天的同一界面构成对照），说书人侧「重获能力」窗口显示已终止。
 *
 * **夜间唤醒面的口径**（2026-10-05 实测，曾一度被误记为"整夜停住"）：重获一个**白天**能力的角色
 * 被激活的那一格，挂的是**说书人报数裁定点**（`JugglerNightAction` 的提示没有玩家选项、受众是说书人），
 * 所以 3 号自己的玩家页整夜保持 idle——**玩家页看不到请求不等于夜里停住**。那一格的证据要读
 * 说书人侧：魔典席位卡的「待裁定」标记 + 裁定点正文。`runNight` 会如实结清它，第 3 夜据此走完。
 * 早先那条"计划走到这里就停住"的判断来自 `BoneCollectorHostTests` 里一个**被兜底推进掩盖的假复现**
 * （真正卡住的是恶魔击杀格没人应答），现已在集成层改成完整链路。
 *
 * 与主批次的分工：`verify-retention-day-info.mjs` 跑「保留能力（亡骨魔）」那一族；本装置独立一局，
 * 只跑「重获能力（集骨者）」这一条链路，不动那个装置的夜晚剧情。
 *
 * 前置：Node >= 22.5（node:sqlite）、本机已构建 web/node_modules（playwright + @microsoft/signalr）。
 * 用法（在仓库根运行；默认迭代档 = 快节拍 + 不落盘截图 + 复用产物）：
 *   node tools/verify-bone-collector-juggler.mjs                                        # 迭代档
 *   node tools/verify-bone-collector-juggler.mjs --quota 2 --screenshots-all --build    # 取证档（一批一次）
 *   node tools/verify-bone-collector-juggler.mjs --port 5419 --vite-port 5299           # 自定端口
 *
 * 外部耦合（换机器先核对 web/AGENTS.md §3.1）：宿主编译产物路径、SQLite 表 Games 的
 * SeatsJson 列形状（席位票据仍直读库；说书人身份已改走账号，见 D-0027）、
 * SignalR 的 SubmitResponse 四参数签名。
 * 退出码：0 = 全过；1 = 有失败；2 = 环境缺依赖。
 */
import { spawn } from 'node:child_process'
import { mkdirSync, mkdtempSync, rmSync } from 'node:fs'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { readAttributeBounded, readTextBounded } from './lib/bounded-text.mjs'
import { openTableAndHost, registerProbeAccount, seatByAccount, seatByInviteCode } from './lib/entrance.mjs'
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

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-bone-juggler-'))
const databasePath = path.join(workspace, 'bone-juggler.db')
const screenshotsDir = path.resolve(repositoryRoot, 'artifacts', 'web')
mkdirSync(screenshotsDir, { recursive: true })

const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`
/** Hub 地址：桌标识在开桌之后才定得下来，所以这里是 `let`（见下面的赋值）。 */
let hubUrl = `${serverUrl}/hub/game`
/** 账号 Hub：线级探针入座前要先注册一个夹具账号（D-0037：入座必须登录）。 */
const accountHubUrl = `${serverUrl}/hub/account`

/**
 * 五席基础花名册。选角理由（每一席都写明它为什么在这里，改动前先读这段）：
 *   · 1 号亡骨魔：**其他夜晚**行动的恶魔（首夜不入格），第 2 夜杀 3 号的那一刀由它出；
 *   · 2 号钟表匠：**只首夜**行动的信息格（本装置不看他的信息，只要求那一格真的走完）；
 *   · 3 号杂耍艺人：本票的正主——先活着猜一次，再被杀死、被集骨者重获能力；
 *   · 4 号博学者：白天主动开口的信息角色，夜里不入格（本装置不开口，只占一个镇民名额）；
 *   · 5 号畸形秀演员：外来者，夜里没有行动（无关席位的收包与公开面见证席）。
 * 集骨者由说书人在第 1 天加入：追加为第 6 席（旅行者）。
 *
 * 为什么要挑「只在一夜行动」的角色：名额以外的角色一律不入格（空槽按配额走），
 * 计划里就只有本装置要处理的那几格；`dreamer` 这类「每个夜晚」的信息角色会在每一夜挂起请求，
 * 把夹具拖进"替他结清信息"的无关剧情（首版就踩过：首夜被他的请求卡住）。
 */
const ASSIGN = ['vigormortis', 'clockmaker', 'juggler', 'savant', 'mutant']
const seatOf = (slug) => ASSIGN.indexOf(slug) + 1
const DEMON_SEAT = seatOf('vigormortis')
const CLOCKMAKER_SEAT = seatOf('clockmaker')
const JUGGLER_SEAT = seatOf('juggler')
const SAVANT_SEAT = seatOf('savant')
const MUTANT_SEAT = seatOf('mutant')
/** 集骨者由说书人在白天加入：追加为第 6 席（ASSIGN 之后）。 */
const BONE_COLLECTOR_SEAT = ASSIGN.length + 1

/** 第 1 天（3 号还活着）的公开猜测：1 条——为明天的第二次猜测留出对照（那时已有"猜过一次"的账）。 */
const FIRST_DAY_GUESS = { seat: DEMON_SEAT, character: 'savant' }
/** 第 2 天（3 号已死亡、被重获能力）的公开猜测：猜的是本局真实在册的角色（畸形秀演员）。 */
const REGAINED_DAY_GUESS = { seat: MUTANT_SEAT, character: 'mutant' }

/**
 * 本装置的**固定断言数**（含末尾那条覆盖自检本身）。
 * 改动本装置、增删断言时必须同步这个数字：条件分支被静默跳过（裁定点没出现、请求没来……）
 * 会让项数变少——那必须红，而不是悄悄少判几行。
 */
const EXPECTED_CHECKS = 52
/** 说书人浏览器页：本装置只有一个，读面板 / 下命令的助手都从这里取。 */
let storytellerPage = null

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
  console.log('=== 1/10 构建并启动真宿主（独立临时库，5 席；集骨者第 1 天追加为第 6 席）===')
  const artifacts = await ensureServerArtifacts({ repositoryRoot, buildMode: config.buildMode })
  console.log(`  宿主产物：${artifacts.artifact}（${artifacts.built ? '本次构建' : '复用'}）`)
  await startServer()

  console.log('=== 2/10 起 Vite ===')

  const vite = spawn(
    process.execPath,
    [path.join(webRoot, 'node_modules', 'vite', 'bin', 'vite.js'), '--port', String(options.vitePort), '--strictPort'],
    {
      cwd: webRoot,
      env: { ...process.env, VITE_SERVER_TARGET: serverUrl },
      stdio: 'ignore',
    },
  )
  children.push(vite)
  await waitForHttp(viteUrl, 'Vite 开发服务器', 60_000)

  console.log('=== 3/10 说书人开一桌并进主持台（账号身份，D-0027）；杂耍艺人 / 畸形秀演员入座，恶魔走线级探针 ===')
  const browser = await playwright.chromium.launch()
  const consoleErrors = []
  storytellerPage = await newPage(browser, { width: 1600, height: 1100 }, consoleErrors)
  // 说书人：注册夹具账号 → 开一桌 → 进主持台（票据退场后这是唯一路径，也是最贴近真实用法的那条）。
  const table = await openTableAndHost(storytellerPage, {
    frontUrl: viteUrl,
    serverUrl,
    databasePath,
    seats: ASSIGN.length,
    suffix: 'bone',
  })
  const seatInviteCodes = table.seatInviteCodes
  // 桌标识属于连接（D-0027 之后不声明就被拒）：线级探针也连到这一桌。
  hubUrl = table.hubUrl
  check('席位邀请码齐备（5 席）', seatInviteCodes.length === ASSIGN.length, `数据库 ${seatInviteCodes.length} 张`)
  check(
    '说书人加入后看板可见（魔典主视图）',
    (await storytellerPage.locator('[data-testid="grimoire"]').count()) === 1,
  )
  await openDataDrawer(storytellerPage)

  const jugglerPage = await joinPlayerPage(browser, table.gameId, JUGGLER_SEAT, 'bone-juggler', consoleErrors)
  const mutantPage = await joinPlayerPage(browser, table.gameId, MUTANT_SEAT, 'bone-mutant', consoleErrors)
  check(
    `两个玩家席加入玩家端（${JUGGLER_SEAT} 号杂耍艺人 / ${MUTANT_SEAT} 号畸形秀演员）`,
    (await jugglerPage.locator('[data-testid="player-seat"]').count()) === 1
      && (await mutantPage.locator('[data-testid="player-seat"]').count()) === 1,
  )

  // 1 号（恶魔，出击杀）走线级探针：本装置只关心白天入口这一条链路，恶魔那两格的说书人裁定面
  // 已有别的装置覆盖（每席位只保留一条连接，见 web/AGENTS.md §3.1）。
  // 后面两条负向探针也用它——**每个有浏览器页的席位都不能再开第二条连接**（开了会把那一页的凭据顶掉，
  // 之后那一页的断言就全是假绿，本装置踩过一次：无关席位证人的公开面读数读到 null）。
  const demonSeat = await connectSeat(seatInviteCodes[DEMON_SEAT - 1])

  console.log('=== 4/10 配板 → 首夜（只有钟表匠入格）→ 第 1 天 ===')
  const assignmentSelects = storytellerPage.locator('section', { hasText: '开局分配' }).locator('select')
  for (const [index, slug] of ASSIGN.entries()) {
    await assignmentSelects.nth(index).selectOption(slug)
  }

  const assigned = await runCommand(storytellerPage, '分配', () =>
    storytellerPage.getByRole('button', { name: '提交分配' }).click(),
  )
  check(`分配 5 个角色被受理（${ASSIGN.join(' / ')}）`, assigned.kind === 'Accepted', assigned.raw)
  await screenshot(storytellerPage, 'bone-collector-00-assigned')

  const firstNight = await startNightWhenReady(storytellerPage, 1)
  check('开首夜被受理（首夜表上只有钟表匠的行动格）', firstNight.kind === 'Accepted', firstNight.raw)
  const firstNightRun = await runNight(storytellerPage, '首夜', { demonSeat })
  check(
    '首夜：唯一的信息格按自由决定结清，其余空槽按配额自己走完（全程没有强推）',
    firstNightRun.decisionCount === 1 && firstNightRun.killed === false,
    `裁定点 ${firstNightRun.decisionCount} 个；恶魔请求=${firstNightRun.killed}`,
  )

  const firstDay = await startDayWhenReady(storytellerPage)
  check('首夜走完后开第 1 天被受理', firstDay.kind === 'Accepted', firstDay.raw)
  const dayStatus = await waitForAttribute(storytellerPage.getByTestId('st-day'), 'data-day-status', 'Open', 30_000)
  check('说书人面板进入「白天进行中」', dayStatus === 'Open', `data-day-status=${dayStatus}`)

  console.log('=== 5/10 第 1 天：活着的 3 号猜一次 → 加入集骨者 → 关账 ===')
  await guessOnDay(jugglerPage, 1, FIRST_DAY_GUESS, { label: '第 1 天（本人活着）' })

  await storytellerPage.getByTestId('traveller-character').selectOption('bone-collector')
  await storytellerPage.getByTestId('traveller-alignment').selectOption('Good')
  await storytellerPage.getByTestId('traveller-seat').fill('')
  const travellerJoined = await runCommand(storytellerPage, '加入集骨者', () =>
    storytellerPage.getByTestId('traveller-join').click(),
  )
  check('第 1 天加入集骨者被受理（旅行者追加为第 6 席）', travellerJoined.kind === 'Accepted', travellerJoined.raw)

  const issued = storytellerPage.getByTestId('traveller-issued')
  const issuedSeat = await waitForAttribute(issued, 'data-seat', String(BONE_COLLECTOR_SEAT), 20_000)
  const inviteCode = compact(await readTextBounded(issued.locator('.mono')))
  check(
    `加入集骨者签发第 ${BONE_COLLECTOR_SEAT} 席与邀请码`,
    issuedSeat === String(BONE_COLLECTOR_SEAT) && inviteCode.startsWith(`${table.gameId}:`),
    `seat=${issuedSeat}；邀请码=${inviteCode.slice(0, 14)}…`,
  )

  // 集骨者是**中途到场**的旅行者：这一桌已经开局，大厅的席位按钮点不动，
  // 所以他走的是"有邀请码？"那条兜底路径——说书人把这一串交给他，他粘一次就入座。
  const boneCollectorPage = await newPage(browser, { width: 900, height: 1100 }, consoleErrors)
  await seatByInviteCode(boneCollectorPage, {
    frontUrl: viteUrl,
    code: inviteCode,
    suffix: 'bone-collector',
  })
  check(
    `集骨者凭邀请码加入成功（${BONE_COLLECTOR_SEAT} 号，与杂耍艺人同一天在局）`,
    (await boneCollectorPage.locator('[data-testid="player-seat"]').count()) === 1,
  )

  const firstDayClosed = await runCommand(storytellerPage, '结束第 1 天', () =>
    storytellerPage.getByTestId('st-close-day').click(),
  )
  check('结束第 1 天被受理（不处决任何人）', firstDayClosed.kind === 'Accepted', firstDayClosed.raw)

  console.log('=== 6/10 第 2 夜：恶魔杀死 3 号；集骨者候选里一个死者都没有 → 摇头不用 ===')
  const secondNight = await startNightWhenReady(storytellerPage, 2)
  check('开第 2 夜被受理（第 1 天已结束）', secondNight.kind === 'Accepted', secondNight.raw)
  const secondNightRun = await runNight(storytellerPage, '第 2 夜', {
    demonSeat,
    killSeat: JUGGLER_SEAT,
    boneCollectorPage,
    boneCollector: { deadCandidates: 0, choose: null },
  })
  check('第 2 夜：恶魔在自己的格上完成了击杀（选定 3 号）', secondNightRun.killed === true, `击杀=${secondNightRun.killed}`)

  const jugglerDead = await waitForAttribute(
    storytellerPage.locator(`[data-testid="grimoire-seat"][data-seat="${JUGGLER_SEAT}"]`),
    'data-life',
    'Dead',
    30_000,
  )
  check(`${JUGGLER_SEAT} 号杂耍艺人在第 2 夜被恶魔击杀（真的死了）`, jugglerDead === 'Dead', `data-life=${jugglerDead}`)

  console.log('=== 7/10 第 3 夜：集骨者选中已死亡的 3 号 → 重获能力窗口 → 他在自己的格上被唤醒 ===')
  const thirdNight = await startNightWhenReady(storytellerPage, 3)
  check('开第 3 夜被受理（第 2 天还没开过：本装置不插第 2 天）', thirdNight.kind === 'Accepted', thirdNight.raw)
  const thirdNightRun = await runNight(storytellerPage, '第 3 夜', {
    demonSeat,
    // 恶魔这一夜照常被唤醒：再点名 3 号——他已经死了，这一刀不改变任何状态（本装置只看重获窗口）。
    killSeat: JUGGLER_SEAT,
    jugglerPage,
    watchJugglerWake: true,
    boneCollectorPage,
    boneCollector: { deadCandidates: 1, choose: JUGGLER_SEAT },
  })
  check(
    '集骨者的候选逐条写明「已死亡」（本夜只有刚死的 3 号）',
    thirdNightRun.deadCandidatePreviews.length === 1
      && thirdNightRun.deadCandidatePreviews.every((preview) => preview.includes('已死亡')),
    thirdNightRun.deadCandidatePreviews.join(' | ').slice(0, 220),
  )
  await screenshot(boneCollectorPage, 'bone-collector-03-regained-choice')
  await screenshot(storytellerPage, 'bone-collector-01-regain-window')
  // 诊断：3 号那一页在夜里的请求读数（"被重新唤醒"这条断言红了的时候，先看这一行说了什么）。
  console.log(`  [诊断] 第 3 夜结束后 3 号的请求读数：${await playerRequestProbe(jugglerPage)}`)
  const regainedWindow = await waitForEffectWindow(storytellerPage, '重获能力', 30_000)
  check(
    '选中之后说书人侧出现「重获能力」窗口（效果链，与集骨者的能力同源）',
    regainedWindow.includes('重获能力'),
    regainedWindow.slice(0, 220),
  )
  // 夜间唤醒面：那一格在**说书人侧**挂出报数裁定点（受众是说书人，所以 3 号的玩家页整夜 idle）。
  // 这三条与第 9 段（到期后不再出现）构成对照——只判"第 4 夜没有"会假绿，必须同装置内两夜都判。
  check(
    '第 3 夜：死者的那一格在说书人侧被唤醒（挂出杂耍艺人的报数裁定点）',
    thirdNightRun.jugglerDecisionContext.includes('杂耍艺人'),
    thirdNightRun.jugglerDecisionContext.slice(0, 200) || '（本夜没有出现杂耍艺人的裁定点）',
  )
  check(
    `第 3 夜：裁定点在魔典上归属 ${JUGGLER_SEAT} 号席位（席位卡标出「待裁定」）`,
    thirdNightRun.jugglerSeatCardDecision === 'true',
    `data-decision=${thirdNightRun.jugglerSeatCardDecision || '（空）'}`,
  )
  check(
    '第 3 夜：报数裁定点不投给玩家页（受众是说书人，3 号那一页整夜保持 idle）',
    thirdNightRun.jugglerPending === false,
    `玩家侧 pending=${thirdNightRun.jugglerPending}`,
  )
  await screenshot(storytellerPage, 'bone-collector-01-regain-window')

  console.log('=== 8/10 第 2 天：死亡但重获能力的 3 号拿到公开猜测入口 → 再猜一次 ===')
  const secondDay = await startDayWhenReady(storytellerPage)
  check('开第 2 天被受理（第 3 夜已走完）', secondDay.kind === 'Accepted', secondDay.raw)
  const dayOpen = await waitForAttribute(storytellerPage.getByTestId('st-day'), 'data-day-status', 'Open', 30_000)
  check('说书人面板进入「白天进行中」（重获窗口存续的那个白天）', dayOpen === 'Open', `data-day-status=${dayOpen}`)

  const selfDead = await jugglerPage.getByTestId('player-self-dead').count()
  check(`${JUGGLER_SEAT} 号自己的界面仍显式可见死亡（重获的是能力，不是命）`, selfDead >= 1, `横幅=${selfDead}`)

  await guessOnDay(jugglerPage, 2, REGAINED_DAY_GUESS, {
    label: '第 2 天（本人已死亡 + 重获窗口）',
    unrelatedPage: mutantPage,
  })

  // 「同日第二次」那一条**不做界面取证**：提交成功后表单整块从 DOM 撤下，没有可点的入口；
  // 而换一个席位提交拿到的是身份闸的 `juggler.not_juggler`（不是"次数"那条）——两条都不是本装置能判的。
  // 它由内核 `JugglerGuessMachineTests.Make_StillRejectsSecondGuessInTheRegainedDay` 与真宿主
  // `BoneCollectorHostTests.BoneCollector_RegainingDayAbility_LetTheDeadJugglerGuessAgainOnThatDay`
  // 判（跑出来的是 `juggler.already_guessed`）。

  const secondDayClosed = await runCommand(storytellerPage, '结束第 2 天', () =>
    storytellerPage.getByTestId('st-close-day').click(),
  )
  check('结束第 2 天被受理（重获窗口在白天结束时仍然存续）', secondDayClosed.kind === 'Accepted', secondDayClosed.raw)

  console.log('=== 9/10 第 4 夜：下个黄昏 → 重获窗口到期，那一格回到空槽 ===')
  const fourthNight = await startNightWhenReady(storytellerPage, 4)
  check('开第 4 夜被受理（第 2 天已结束）', fourthNight.kind === 'Accepted', fourthNight.raw)
  const fourthNightRun = await runNight(storytellerPage, '第 4 夜', {
    demonSeat,
    // 恶魔照旧点名 3 号：他已经死了，这一刀不改变任何状态。
    killSeat: JUGGLER_SEAT,
    jugglerPage,
  })
  check(
    '第 4 夜：说书人侧没有再出现杂耍艺人的报数裁定点（能力随窗口收回）',
    fourthNightRun.jugglerDecisionContext === '',
    fourthNightRun.jugglerDecisionContext.slice(0, 200) || '（本夜没有杂耍艺人的裁定点）',
  )
  check(
    '第 4 夜：3 号那一页整夜没有请求（死者不再被唤醒）',
    fourthNightRun.jugglerPending === false,
    `玩家侧 pending=${fourthNightRun.jugglerPending}`,
  )
  await screenshot(storytellerPage, 'bone-collector-04-regain-expired-night')

  console.log('=== 10/10 第 3 天：窗口到期后入口不再出现（与第 2 天同一界面对照）===')
  const thirdDay = await startDayWhenReady(storytellerPage)
  check('开第 3 天被受理（第 4 夜已走完）', thirdDay.kind === 'Accepted', thirdDay.raw)
  const dayThreeOpen = await waitForAttribute(storytellerPage.getByTestId('st-day'), 'data-day-status', 'Open', 30_000)
  check('说书人面板进入「白天进行中」（窗口已到期的那个白天）', dayThreeOpen === 'Open', `data-day-status=${dayThreeOpen}`)
  const entryAfterExpiry = await jugglerPage.getByTestId('player-juggler').count()
  check(
    '第 3 天：3 号的公开猜测入口不再出现（重获能力已收回，起算点回到原处）',
    entryAfterExpiry === 0,
    `入口数=${entryAfterExpiry}`,
  )
  const expiredWindowRow = await effectWindowRow(storytellerPage, '重获能力')
  check(
    '说书人侧「重获能力」窗口显示已终止（下个黄昏收口）',
    expiredWindowRow.includes('已终止'),
    expiredWindowRow.slice(0, 220) || '（效果链里没有重获能力那一行）',
  )
  await screenshot(jugglerPage, 'bone-collector-05-entry-gone-after-expiry')

  check('浏览器控制台没有报错', consoleErrors.length === 0, consoleErrors.slice(0, 3).join(' | '))
  check(
    `装置覆盖自检：固定断言 ${EXPECTED_CHECKS} 项都命中（增删断言请同步 EXPECTED_CHECKS）`,
    results.length + 1 === EXPECTED_CHECKS,
    `实际 ${results.length + 1} 项`,
  )
  await browser.close()
}

/** 玩家席加入真浏览器：注册夹具账号 → 从大厅挑空席位（D-0027，不再填票据）→ 断言席位徽标。 */
async function joinPlayerPage(browser, gameId, seat, suffix, consoleErrors) {
  const page = await newPage(browser, { width: 900, height: 1100 }, consoleErrors)
  await seatByAccount(page, { frontUrl: viteUrl, gameId, seat, suffix })
  const badge = await waitForText(page.locator('[data-testid="player-seat"]'), String(seat), 30_000)
  if (!badge.includes(String(seat))) {
    throw new Error(`${seat} 号席位加入失败：${badge}`)
  }

  return page
}

/**
 * 玩家端的一条完整猜测链路：入口在不在 → 填表提交 → 落账 / 进公开面 → 无关席位看到同一份 → 入口消失。
 *
 * 「提交被受理」不靠回执元素（玩家页没有那种东西），靠**当天账真的多了一条**：服务端拒绝时这一条不会出现。
 * 计数读的是**当天公开面**（`publicView.jugglerGuesses.length`，只含这一天的记录）——所以两天的期望值
 * 都是 1，不是"累计条数"（首版按累计写过，第 2 天必红）。
 */
async function guessOnDay(jugglerPage, dayNumber, guess, { label, unrelatedPage = null }) {
  const form = jugglerPage.getByTestId('player-juggler')
  const visible = await form
    .waitFor({ timeout: 30_000 })
    .then(() => true)
    .catch(() => false)
  check(`${label}：玩家端出现公开猜测入口`, visible, `入口数=${await form.count()}`)
  await screenshot(jugglerPage, `bone-collector-0${dayNumber + 1}-juggler-entry`)

  const submitted = await submitJugglerGuessUi(jugglerPage, guess)
  check(`${label}：在玩家页面上填表提交（入口可用）`, submitted.kind === 'Submitted', submitted.raw)

  const count = await waitForAttribute(
    jugglerPage.getByTestId('player-juggler-guesses'),
    'data-juggler-guess-count',
    '1',
    30_000,
  )
  check(`${label}：进当天账并进入公开面（当天 1 条记录）`, count === '1', `data-juggler-guess-count=${count}`)
  if (unrelatedPage !== null) {
    const unrelated = await waitForAttribute(
      unrelatedPage.getByTestId('player-juggler-guesses'),
      'data-juggler-guess-count',
      '1',
      30_000,
    )
    check(
      `${label}：无关席位（${MUTANT_SEAT} 号畸形秀演员）看到同一份公开猜测（猜测是公开事实）`,
      unrelated === '1',
      `data-juggler-guess-count=${unrelated}`,
    )
  }

  const formGone = await waitForCount(form, 0, 30_000)
  check(`${label}：提交后入口随之消失（当天只允许一次）`, formGone, `入口数=${await form.count()}`)
}

/** 只填表、不提交（负向路径要自己点提交，见调用点）。 */
async function fillJugglerGuess(jugglerPage, guess) {
  await jugglerPage.getByTestId('player-juggler-seat-0').selectOption(String(guess.seat), { timeout: 5_000 })
  await jugglerPage.getByTestId('player-juggler-character-0').selectOption(guess.character, { timeout: 5_000 })
}

/**
 * 在**玩家页面**上填一条猜测并提交。
 *
 * 玩家页没有说书人那种「命令回执」元素（`data-testid="outcome"` 只在说书人面板上），所以这里
 * 按 DOM 收口读结果：提交后入口整块撤下 + 当天账多一条（由调用方断言）。**别在玩家页上用
 * `runCommand`**——它会一直等一个永远不出现的回执，把整轮夹具拖死（首版就在这里白等了两轮）。
 */
async function submitJugglerGuessUi(jugglerPage, guess) {
  try {
    await fillJugglerGuess(jugglerPage, guess)
    await jugglerPage.getByTestId('player-juggler-submit').click({ timeout: 5_000 })
    return { kind: 'Submitted', raw: '已提交（结果以入口撤下 / 公开账条数为准）' }
  } catch (error) {
    return { kind: 'NoEntry', raw: `界面上没有可提交的入口：${error instanceof Error ? error.message : String(error)}` }
  }
}

/**
 * 走完一个夜晚：**不强推**（与 `verify-retention-day-info.mjs` 同款口径，空槽按配额自己走）。
 *
 * 处理顺序即优先级：集骨者的玩家侧请求（应答）→ 其余裁定点（自由决定结清）→ 被重获席位的挂起
 * 请求（只读作证据）→ 恶魔的击杀请求（线级提交）→ 等槽位推进。
 */
async function runNight(page, label, night) {
  const summary = {
    deadCandidatePreviews: [],
    decisionCount: 0,
    jugglerDecisionContext: '',
    jugglerSeatCardDecision: '',
    jugglerPending: false,
    killed: false,
    pendingSeen: false,
  }
  let boneCollectorSettled = false
  let lastProgress = Date.now()
  let lastStage = ''
  const deadline = Date.now() + 240_000

  while (Date.now() < deadline) {
    // 停滞检测：状态读数（槽位 / 卡点 / 裁定点 / 请求态）变了就算"有进展"，连续 20s 不变就如实打出来
    // ——否则夹具卡住时只剩一个超时数字，定位不到是槽位没走、还是请求没人应（首版在这里白等了两轮）。
    const stage = await stageDiagnostics(page, night.jugglerPage)
    if (stage !== lastStage) {
      lastStage = stage
      lastProgress = Date.now()
    } else if (Date.now() - lastProgress > 20_000) {
      lastProgress = Date.now()
      console.log(`  [停滞] ${label}：${stage}`)
    }
    const decision = await readDecisionPanel(page)
    if (decision.context.length > 0) {
      summary.decisionCount += 1
      if (decision.context.includes('杂耍艺人')) {
        // 重获白天能力的当夜证据：那一格在**说书人侧**挂出报数裁定点，魔典上归属 3 号席位。
        // 记下来但不在这里结清（下面照常按自由决定结清）——断言在第 7 段与第 9 段做。
        summary.jugglerDecisionContext = decision.context
        summary.jugglerSeatCardDecision = (await readAttributeBounded(
          page.locator(`[data-testid="grimoire-seat"][data-seat="${JUGGLER_SEAT}"]`),
          'data-decision',
        )) ?? ''
      }

      // 其余裁定点（钟表匠 / 杂耍艺人的报数一类自由决定）：如实结清。
      const settled = await settleFreeDecision(page, '0')
      if (settled.kind !== 'Accepted') {
        console.error(`[裁定失败] ${label} 未识别的裁定点（${decision.context.slice(0, 60)}）：${settled.raw}`)
        break
      }

      continue
    }

    // 夜里的**玩家侧请求**（集骨者的选择、被重获席位的报数）都走这一条：
    // 它们不是说书人的裁定点，而是发给那一席的行动请求（说书人在自己那侧只能代填 / 作废）。
    if (night.boneCollectorPage !== undefined && !boneCollectorSettled) {
      const request = await readSeatRequest(night.boneCollectorPage)
      if (request.requestId !== '') {
        boneCollectorSettled = true
        const dead = request.options.filter((option) => option.text.includes('已死亡'))
        check(
          `${label}：集骨者的候选只含已死亡的席位（本夜 ${night.boneCollector.deadCandidates} 名）`,
          dead.length === night.boneCollector.deadCandidates,
          request.options.map((option) => option.text).join(' | ').slice(0, 260),
        )
        check(
          `${label}：摇头不用始终是候选之一（不用不算使用）`,
          request.options.some((option) => option.text.includes('摇头')),
          request.options.map((option) => option.text).join(' | ').slice(0, 260),
        )
        check(
          `${label}：请求正文写明这是「重新获得角色能力直到下个黄昏」的选择`,
          request.context.includes('重新获得角色能力'),
          request.context.slice(0, 200),
        )
        summary.deadCandidatePreviews = dead.map((option) => option.text)
        await screenshot(night.boneCollectorPage, 'bone-collector-02-bone-collector-candidates')

        const value = night.boneCollector.choose === null
          ? request.options.find((option) => option.text.includes('摇头'))?.value ?? null
          // 按**选项值**挑（`seat:N`）：界面文案会随玩家名变（D-0021 的「N 号 · 名字」），值不会。
          : request.options.find((option) => option.value === `seat:${night.boneCollector.choose}`)?.value ?? null;
        const submitted = await answerSeatRequest(night.boneCollectorPage, value)
        check(
          `${label}：集骨者${night.boneCollector.choose === null ? '摇头不用' : `选中已死亡的 ${night.boneCollector.choose} 号`}被受理`,
          submitted === true,
          `选项=${value ?? '（没找到匹配的候选）'}`,
        )
        if (night.boneCollector.choose !== null) {
          // 说书人侧的卡点应当随之清掉（请求被本人应答了）。
          const cleared = await waitForPendingGone(page, 20_000)
          check(`${label}：本人应答后说书人侧的卡点清掉`, cleared, `仍在=${!cleared}`)
        }

        continue
      }
    }

    if (night.jugglerPage !== undefined) {
      const jugglerState = await readAttributeBounded(
        night.jugglerPage.locator('[data-testid="player-request-panel"]'),
        'data-request-state',
      )
      if (night.watchJugglerWake === true) {
        // 只认**集骨者应答之后**的挂起：这一格是重获窗口把死者的空槽点活的，早于应答的挂起不算证据。
        if (boneCollectorSettled && jugglerState === 'pending') {
          summary.jugglerPending = true
        }
      } else if (jugglerState === 'pending') {
        summary.jugglerPending = true
      }
    }

    const demonRequests = night.demonSeat.takeRequests()
    if (demonRequests.length > 0) {
      // 恶魔是「每个夜晚*」：其他夜晚每夜都会被唤醒（第 2 夜真杀 3 号；第 3 夜再点名他，
      // 而他**已经死了**——击杀对死者不产生任何变化，这正是 `verify-retention-day-info.mjs`
      // 处理第 3 夜同款请求的做法，也是本装置不让恶魔这一格把夜间剧情带偏的手段）。
      if (night.killSeat === undefined) {
        check(`${label}：恶魔本夜不该被唤醒（收到请求即口径漂移）`, false, `收到 ${demonRequests.length} 条请求`)
      }

      const target = night.killSeat ?? JUGGLER_SEAT
      for (const request of demonRequests) {
        await night.demonSeat.invoke('SubmitResponse', request.requestId, `seat:${target}`, `${label}-demon-${target}`, 1)
      }

      if (night.killSeat !== undefined) {
        summary.killed = true
      }

      continue
    }

    if (await pendingRequestVisible(page)) {
      summary.pendingSeen = true
      await sleep(300)
      continue
    }

    const advanced = await waitForSlotProgress(page)
    if (advanced.kind === 'Completed') {
      break
    }

    await sleep(200)
  }

  return summary
}

/** 说书人按自由决定结清当前裁定点。 */
async function settleFreeDecision(page, content) {
  await page.getByPlaceholder('自由决定的内容（可为空）').fill(content)
  return runCommand(page, '裁定', () => page.getByRole('button', { name: '按自由决定结清' }).click())
}

/** 玩家页请求面板的读数（诊断用）：状态 / 请求号 / 面板正文开头。 */
async function playerRequestProbe(playerPage) {
  const panel = playerPage.getByTestId('player-request-panel')
  if ((await panel.count()) === 0) {
    return '（没有请求面板）'
  }

  const state = await readAttributeBounded(panel, 'data-request-state')
  const requestId = await readAttributeBounded(panel, 'data-request-id')
  const text = compact(await readTextBounded(panel))
  return `状态=${state ?? '?'} 请求号=${requestId === null || requestId === '' ? '(空)' : requestId} 正文=${text.slice(0, 160)}`
}

/** 玩家页请求面板：请求号 / 正文 / 候选（`data-option-value` 是提交时要回传的值）。 */
async function readSeatRequest(playerPage) {  const panel = playerPage.getByTestId('player-request-panel')
  const requestId = await readAttributeBounded(panel, 'data-request-id')
  if (requestId === null || requestId === '') {
    return { requestId: '', context: '', options: [] }
  }

  return {
    requestId,
    context: compact(await readTextBounded(panel.getByTestId('player-request-context'))),
    options: await panel
      .locator('[data-testid="player-request-options"] label')
      .evaluateAll((nodes) =>
        nodes.map((node) => ({
          value: node.getAttribute('data-option-value') ?? '',
          text: (node.textContent ?? '').replace(/\s+/g, ' ').trim(),
        })),
      ),
  }
}

/** 玩家页应答当前请求：选中给它的取值（null = 什么都不选）后提交；成功返回 true。 */
async function answerSeatRequest(playerPage, value) {
  if (value === null || value === undefined) {
    return false
  }

  try {
    await playerPage.locator(`[data-testid="player-request-options"] label[data-option-value="${value}"]`).click()
    await playerPage.getByTestId('player-submit').click()
  } catch (error) {
    console.error(`[玩家应答失败] ${error instanceof Error ? error.message : String(error)}`)
    return false
  }

  const deadline = Date.now() + 20_000
  while (Date.now() < deadline) {
    const panel = playerPage.getByTestId('player-request-panel')
    const state = await readAttributeBounded(panel, 'data-request-state')
    if (state === 'idle') {
      return true
    }

    await sleep(150)
  }

  return false
}

/** 等说书人侧的卡点块消失（本人应答之后）。 */
async function waitForPendingGone(page, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if (!(await pendingRequestVisible(page))) {
      return true
    }

    await sleep(200)
  }

  return false
}

/** 当前槽位是否有挂起请求：有就不能强推（强推会把它按 Override 了结）。 */
async function pendingRequestVisible(page) {
  const pending = page.locator('[data-testid="console-pending"]')
  return (await pending.count()) > 0 && (await pending.first().isVisible().catch(() => false))
}

/** 说书人侧裁定点面板：上下文文本 + 选项文案（`.options` 为空 = 自由决定类）。 */
async function readDecisionPanel(page) {
  const panel = page.getByTestId('console-decision')
  if ((await panel.count()) === 0) {
    return { context: '', options: [] }
  }

  const context = compact(await readTextBounded(panel.locator('.context')))
  const options = (
    await panel.locator('.options button').evaluateAll((nodes) =>
      nodes.map((node) => {
        // 摘掉「已死亡」标签再取文本：界面把标签嵌在按钮里，直接 textContent 会把标签文案混进候选。
        const preview = node.cloneNode(true)
        preview.querySelector('[data-testid="option-dead"]')?.remove()
        return preview.textContent ?? ''
      }),
    )
  ).map((text) => compact(text))
  return { context, options }
}

/** 连一个真 SignalR 席位：记录每一次请求（供夜间探针应答）。 */
async function connectSeat(seatInviteCode) {
  // 入座必须登录（D-0037）：每调用一次 = 新席位 → **新夹具账号**（一账号一局只坐一席，共用会被拒）。
  const probe = await registerProbeAccount(signalR, accountHubUrl, `bone-${seatInviteCode.seat}`)
  const connection = new signalR.HubConnectionBuilder().withUrl(hubUrl).configureLogging(signalR.LogLevel.None).build()
  const requests = []
  connection.on('ReceiveOperationRequest', (payload) => requests.push(payload))
  await connection.start()
  const joined = await connection.invoke('JoinByInviteCode', seatInviteCode.ticket, probe.accountSession, 0)
  return {
    takeRequests: () => requests.splice(0, requests.length),
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

/** 会话内的命令：点按钮 → 等一条新回执（序号必须比点击前大）。 */
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
    value = await readAttributeBounded(locator, name)
    if (value === expected) {
      return value
    }

    await sleep(150)
  }

  return value
}

/**
 * 等槽位自己往前走——**本装置不强推**。
 *
 * 依据：槽位按配额自行推进是产品行为（空槽照样走配额）；而"强推当前槽位"（D-0014）作用于
 * **服务端此刻的当前槽位**，不是装置做决定时看到的那个——装置决定推走第 7 槽、点击生效前
 * 第 7 槽已自行走完，这一推就落到刚进入的第 8 槽（角色行动格）上，把它按 Override 了结。
 */
async function waitForSlotProgress(page) {
  const blocked = async () => {
    if (await pendingRequestVisible(page)) {
      return '有挂起请求'
    }

    if ((await readDecisionPanel(page)).context.length > 0) {
      return '有裁定点'
    }

    return null
  }

  const first = await blocked()
  if (first !== null) {
    return { kind: 'Blocked', raw: first }
  }

  const slot = await slotState(page)
  if (slot === 'Completed') {
    return { kind: 'Completed', raw: '本计划已走完' }
  }

  await sleep(400)
  const second = await blocked()
  if (second !== null) {
    return { kind: 'Blocked', raw: `刚出现：${second}` }
  }

  return { kind: 'Waiting', raw: `等槽位按配额推进（${slot ?? '空槽'}）` }
}

/** "当前步骤"面板给出的槽位状态：`Completed`（本计划已走完）/ `ActorSlot` / null（空槽）。 */
async function slotState(page) {
  const digest = await panelText(page, '当前步骤')
  if (digest.includes('本计划已走完')) {
    return 'Completed'
  }

  return /行动者\s*\d+\s*号/.test(digest) ? 'ActorSlot' : null
}

/** 点「开夜」直到被受理（上一阶段靠节拍走完）。 */
async function startNightWhenReady(page, nightNumber, timeoutMs = 120_000) {
  const operations = page.locator('section', { hasText: '兜底与推进' })
  await operations.locator('input[type="number"]').fill(String(nightNumber))
  const button = operations.getByRole('button', { name: /开夜/ })
  const deadline = Date.now() + timeoutMs
  let outcome = null
  let lastReport = 0
  let lastDiagnostic = ''
  while (Date.now() < deadline) {
    if (await button.isDisabled().catch(() => false)) {
      if (Date.now() - lastReport > 5_000) {
        lastReport = Date.now()
        // 只在读数变了的时候打印：诊断输出刷屏会把真正的失败信息淹掉（首版踩过）。
        const diagnostic = await stageDiagnostics(page)
        if (diagnostic !== lastDiagnostic) {
          lastDiagnostic = diagnostic
          console.log(`  [诊断] 开第 ${nightNumber} 夜仍不可用：${diagnostic}`)
        }
      }

      await sleep(300)
      continue
    }

    outcome = await runCommand(page, `开第 ${nightNumber} 夜`, () => button.click())
    if (outcome.kind === 'Accepted') {
      return outcome
    }

    await sleep(500)
  }

  console.log(`  [诊断] 开第 ${nightNumber} 夜超时：${await stageDiagnostics(page)}`)
  return outcome ?? { kind: 'Timeout', raw: '开夜按钮在期限内一直不可用' }
}

/** 点「开白天」直到被受理（上一夜走完才受理）。 */
async function startDayWhenReady(page, timeoutMs = 60_000) {
  const button = page.getByTestId('st-start-day')
  const deadline = Date.now() + timeoutMs
  let outcome = null
  let lastReport = 0
  let lastDiagnostic = ''
  while (Date.now() < deadline) {
    if (await button.isDisabled().catch(() => false)) {
      if (Date.now() - lastReport > 5_000) {
        lastReport = Date.now()
        const diagnostic = await stageDiagnostics(page)
        if (diagnostic !== lastDiagnostic) {
          lastDiagnostic = diagnostic
          console.log(`  [诊断] 开白天仍不可用：${diagnostic}`)
        }
      }

      await sleep(300)
      continue
    }

    outcome = await runCommand(page, '开白天', () => button.click())
    if (outcome.kind === 'Accepted') {
      return outcome
    }

    await sleep(500)
  }

  console.log(`  [诊断] 开白天超时：${await stageDiagnostics(page)}`)
  return outcome ?? { kind: 'Timeout', raw: '开白天按钮在期限内一直不可用' }
}

/** 阶段卡住时的现场读数：槽位 / 挂起 / 裁定点 / 当前步骤；给了玩家页就带上他的请求态。 */
async function stageDiagnostics(page, playerPage = null) {
  const slots = await readSlotCounter(page)
  const pending = await pendingRequestVisible(page)
  const decision = await readDecisionPanel(page)
  const digest = compact(await panelText(page, '当前步骤'))
  const playerState = playerPage === null
    ? ''
    : ` 玩家请求=${await readAttributeBounded(playerPage.locator('[data-testid="player-request-panel"]'), 'data-request-state')}`;
  return `槽位=${slots === null ? '不可读' : `${slots.index + 1}/${slots.total}`} 挂起=${pending} `
    + `裁定点=${decision.context.slice(0, 60) || '无'} 步骤=${digest.slice(0, 160) || '不可读'}`
    + ` 操作台=${await consoleProbe(page)}${playerState}`
}

/**
 * 席位操作台的现场读数：**界面挂在哪个席位上、裁定块在不在**。
 *
 * 说书人端的裁定面板是**按选中席位渲染**的（GrimoireSeatConsole 的 `v-if="view.awaitingDecisionId"`）：
 * 装置读不到裁定点，既可能是服务端没给，也可能是界面没跟到那一席。这两者必须分得开，
 * 否则会把"界面没跟过去"误判成"服务端没给"（本装置第一次跑就卡在这里，白等了两轮）。
 */
async function consoleProbe(page) {
  const consoleSection = page.locator('[data-testid="seat-console"]')
  if ((await consoleSection.count()) === 0) {
    return '不存在'
  }

  return compact(
    await consoleSection
      .first()
      .evaluate((element) => {
        const decision = element.querySelector('[data-testid="console-decision"]')
        return `seat=${element.getAttribute('data-console-seat') ?? '?'}`
          + ` 裁定块=${decision === null ? '无' : `有(${decision.textContent?.trim().length ?? 0} 字)`}`
          + ` 卡点块=${element.querySelector('[data-testid="console-pending"]') === null ? '无' : '有'}`
          + ` 正文=${(element.textContent ?? '').replace(/\s+/g, ' ').slice(0, 140)}`
      }),
  )
}

/** "当前步骤"里的槽位计数（X / Y）→ { index（从 0 起）, total }；读不到返回 null。 */
async function readSlotCounter(page) {
  return page
    .evaluate(() => {
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
    .catch(() => null)
}

/** 展开「数据与审计」下钻面板（幂等）：状态账 / 效果链 / 当前步骤都在里面。 */
async function openDataDrawer(page) {
  const toggle = page.locator('[data-testid="data-drawer-toggle"]')
  if ((await toggle.count()) === 0) {
    return
  }

  if ((await readAttributeBounded(toggle, 'aria-expanded')) !== 'true') {
    await toggle.click()
  }
}

/** 某个 section.panel（按标题定位）的可见文本；找不到返回空串。 */
async function panelText(page, heading) {
  await openDataDrawer(page)
  const drawerBody = page.locator('[data-testid="data-drawer-body"]')
  const scope = (await drawerBody.count()) > 0 ? drawerBody : page
  const panel = scope.locator('section.panel', { hasText: heading })
  if ((await panel.count()) === 0) {
    return ''
  }

  return await readTextBounded(panel.first())
}

/** 等效果链里出现含目标窗口名的行（`data-window` 由前端按服务端给的窗口渲染）。 */
async function waitForEffectWindow(page, needle, timeoutMs) {
  await openDataDrawer(page)
  const deadline = Date.now() + timeoutMs
  let text = ''
  while (Date.now() < deadline) {
    text = compact(await readTextBounded(page.locator('[data-window="true"]').filter({ hasText: needle }).first()))
    if (text.includes(needle)) {
      return text
    }

    await sleep(200)
  }

  return text
}

/**
 * 效果链里含目标窗口名的**整行**文本（含状态列：生效中 / 已终止）。
 *
 * 与 `waitForEffectWindow` 的分工：那个读的是窗口标签那一个小 span（只判"窗口在不在"），
 * 到期这条要读**整行**才能拿到状态列（`EffectChainPanel` 的「已终止」）。
 */
async function effectWindowRow(page, needle) {
  await openDataDrawer(page)
  const cell = page.locator('[data-window="true"]').filter({ hasText: needle }).first()
  if ((await cell.count()) === 0) {
    return ''
  }

  return compact(await cell.evaluate((element) => element.closest('tr')?.textContent ?? ''))
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
    console.log(`  截图（迭代档跳过落盘）：${name}`)
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
  console.log('\n=== 集骨者重获能力的白天入口（bone-collector-juggler）取证结论 ===')
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
  const parsed = { port: 5419, vitePort: 5299 }
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
