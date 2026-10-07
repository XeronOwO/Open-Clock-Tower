/**
 * 整局端到端验收装置（full-game）—— `docs/acceptance/AGENTS.md` 开篇那句问话的正解：
 * **说书人和玩家能不能真的玩完一局？**
 *
 * 为什么必须单独有一个装置：主装置 `verify-storyteller-panel.mjs` 全程保持恶魔存活、从不产生
 * `GameEndedEvent`（分工见 `docs/acceptance/devices.md` §1）；胜负装置 `verify-winloss.mjs` 从
 * 「呆瓜被处决」一跳进结束面，夜里靠强制作废收场。于是"一局能不能从头玩到尾"从来没有被**一次性**
 * 回答过——本装置就是那一次会话：一次构建 + 一次多客户端联机，从配板玩到分出胜负。
 *
 * ## 它证明什么
 * - **没有任何夹具特权**：不调强推、不调代填、不发作废，槽位按配额自己往前走；夜里每一步都是
 *   玩家在自己的设备上作答、说书人在自己的面板上结清裁定点（两夜都以「本计划已走完」收尾）。
 * - **走法全部按规则成立**：谁死、谁被处决，都由真界面上的操作产生，不是脚本直接改账。
 *
 * ## 夹具（5 席）
 *   1 钟表匠 / 2 畸形秀演员 / 3 诺-达鲺（恶魔）/ 4 呆瓜 / 5 筑梦师
 *
 *   第 1 夜  钟表匠裁定点（说书人给信息）+ 筑梦师自己选人（说书人给信息）；恶魔首夜不行动
 *            （百科《夜晚行动顺序一览》「除首个夜晚外」唤醒恶魔）
 *   第 1 天  5 号提名 1 号 → 3 票达线（5 席存活需 3 票）→ 处决 1 号
 *   第 2 夜  筑梦师再选一次 + 恶魔自己选 2 号 → 2 号死亡（黎明公告）
 *   第 2 天  4 号提名 5 号 → 2 票达线（3 席存活需 2 票）→ 处决 5 号
 *            → 场上只剩 3 号（恶魔）与 4 号 → **仅剩两名存活 → 邪恶获胜**（R-0045 第 4 条）
 *
 *   注：3 号两侧最近的镇民是 1 / 5 号，诺-达鲺的常驻中毒因此落在两名信息角色身上——那是规则的
 *   正常结果（玩家看不到"你的能力没生效"，说书人照常裁定信息）。本装置如实记录，不绕开它。
 *
 * 前置：Node >= 22.5（node:sqlite）、本机已构建 web/node_modules（playwright）。
 * 用法（在仓库根运行；默认迭代档 = 快节拍 + 不落盘截图 + 复用产物）：
 *   node tools/verify-full-game.mjs                                      # 迭代档
 *   node tools/verify-full-game.mjs --quota 2 --screenshots-all --build  # 取证档（一批一次）
 *   node tools/verify-full-game.mjs --port 5422 --vite-port 5302          # 自定端口
 *
 * 外部耦合（换机器先核对 web/AGENTS.md §3.1）：宿主编译产物路径、SQLite 表 Games 的
 * SeatsJson 列形状（席位票据仍直读库；说书人身份已改走账号，见 D-0027）。
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
import { openTableAndHost, seatByAccount } from './lib/entrance.mjs'
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

try {
  const requireFromWeb = createRequire(path.join(webRoot, 'package.json'))
  playwright = requireFromWeb('playwright')
} catch (error) {
  console.error(`缺少依赖（playwright）：${String(error)}`)
  console.error('先运行：cd web; npm install; npx playwright install chromium')
  process.exit(2)
}

console.log(`档位：${describeProfile(config)}`)

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-fullgame-'))
const databasePath = path.join(workspace, 'fullgame.db')
const screenshotsDir = path.resolve(repositoryRoot, 'artifacts', 'web')
mkdirSync(screenshotsDir, { recursive: true })

const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`

/** 五席花名册：恶魔坐中间，两名**外来者**当邻居，两名镇民（信息角色）落在外侧。 */
const ASSIGN = ['clockmaker', 'mutant', 'no-dashii', 'klutz', 'dreamer']
const seatOf = (slug) => ASSIGN.indexOf(slug) + 1
const CLOCKMAKER_SEAT = seatOf('clockmaker')
const MUTANT_SEAT = seatOf('mutant')
const DEMON_SEAT = seatOf('no-dashii')
const KLUTZ_SEAT = seatOf('klutz')
const DREAMER_SEAT = seatOf('dreamer')

/** 说书人自由裁定的两句文案：会原样进对应玩家的信息面板（用来判"信息只到本人"）。 */
const CLOCKMAKER_INFO = '本局取证：钟表匠读数'
const DREAMER_INFO = '本局取证：筑梦师读到的角色'

/** 本局剧本：两次白天谁提名谁、谁来投票；第 2 夜恶魔杀谁。全部是真界面上的真人操作。 */
const SCRIPT = {
  dayOne: {
    nominator: DREAMER_SEAT,
    nominee: CLOCKMAKER_SEAT,
    voters: [MUTANT_SEAT, DEMON_SEAT, KLUTZ_SEAT],
    expectedVotes: 3,
  },
  nightTwoVictim: MUTANT_SEAT,
  dayTwo: {
    nominator: KLUTZ_SEAT,
    nominee: DREAMER_SEAT,
    voters: [DEMON_SEAT, KLUTZ_SEAT],
    expectedVotes: 2,
  },
}

/**
 * 本装置发给说书人的**命令种类**。收尾断言它 ⊆ 白名单——这是"没有夹具特权"的机读证据：
 * 强推 / 代填 / 作废 / 报告状态都不在里面（本装置压根没有这些代码路径）。
 */
const storytellerCommands = []
const ALLOWED_COMMANDS = new Set(['分配', '开夜', '裁定', '开白天', '开始收票', '计票', '结束白天'])

/** 夜里被装置作答过的请求上下文（用来判"请求都在预期集合里"）。 */
const observedRequestContexts = []

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

  console.log('=== 2/8 起 Vite ===')

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
      env: { ...process.env, VITE_SERVER_TARGET: serverUrl },
      stdio: 'ignore',
    },
  )
  children.push(vite)
  await waitForHttp(viteUrl, 'Vite 开发服务器', 60_000)

  console.log('=== 3/8 说书人开一桌并进主持台（账号身份，D-0027）+ 五名玩家各用一台设备入座 ===')
  const browser = await playwright.chromium.launch()
  const consoleErrors = []
  const storyteller = await newPage(browser, { width: 1600, height: 1100 }, consoleErrors)
  // 说书人：注册夹具账号 → 开一桌 → 进主持台（票据退场后这是唯一路径，也是最贴近真实用法的那条）。
  const table = await openTableAndHost(storyteller, {
    frontUrl: viteUrl,
    serverUrl,
    databasePath,
    seats: ASSIGN.length,
    suffix: 'fullgame',
  })
  const seatInviteCodes = table.seatInviteCodes
  check('席位邀请码齐备（5 席）', seatInviteCodes.length === ASSIGN.length, `数据库 ${seatInviteCodes.length} 张`)
  check('说书人加入后看板可见（魔典主视图）', (await storyteller.locator('[data-testid="grimoire"]').count()) === 1)
  // 全程把数据抽屉留在展开态：面板读数（当前步骤 / 状态账）都在抽屉里。
  await setDataDrawer(storyteller, true)

  const players = new Map()
  for (const row of seatInviteCodes) {
    const joined = await joinSeatPage(players, browser, consoleErrors, table.gameId, row.seat)
    check(`玩家 ${row.seat} 号加入成功`, joined.badgeText.includes(`${row.seat} 号`), joined.badgeText)
  }

  // —— 行 1：配板 ——
  console.log('=== 4/8 配板：说书人在真界面上分配五席 ===')
  const assignmentSelects = storyteller.locator('section', { hasText: '开局分配' }).locator('select')
  const selectCount = await assignmentSelects.count()
  check('分配表覆盖服务端全部席位', selectCount === ASSIGN.length, `UI 席位数=${selectCount}`)
  for (const [index, slug] of ASSIGN.entries()) {
    await assignmentSelects.nth(index).selectOption(slug)
  }

  const assigned = await runCommand(storyteller, '分配', () =>
    storyteller.getByRole('button', { name: '提交分配' }).click(),
  )
  check('行 1：配板被受理（每席都有角色）', assigned.kind === 'Accepted', assigned.raw)

  const ownCharacters = []
  for (const [index, slug] of ASSIGN.entries()) {
    const seat = index + 1
    const panel = players.get(seat).locator('[data-testid="player-own-character"]')
    const seen = await waitForAttribute(panel, 'data-character', slug, 20_000)
    ownCharacters.push(`${seat}号=${seen ?? '（无）'}`)
  }

  check(
    '行 1：配板之后五席各自的设备上都显示**本人**角色（与配板一致，R-0059）',
    ownCharacters.every((entry, index) => entry === `${index + 1}号=${ASSIGN[index]}`),
    ownCharacters.join('，'),
  )
  await screenshot(players.get(DREAMER_SEAT), 'fullgame-01-own-character')

  // —— 行 2 / 行 3：第 1 夜 ——
  console.log('=== 5/8 第 1 夜：玩家自己选人 + 说书人裁定，槽位按配额自己走完 ===')
  const nightOneStarted = await runCommand(storyteller, '开夜', () =>
    storyteller.getByRole('button', { name: /开夜/ }).click(),
  )
  check('行 2：开夜被受理（真实顺序表建表）', nightOneStarted.kind === 'Accepted', nightOneStarted.raw)

  const nightOne = await driveNight(storyteller, players, { victim: null }, '第 1 夜')
  check(
    '行 3：第 1 夜走完——没有强推 / 代填 / 作废，槽位按配额自然走到计划终点',
    nightOne.completed && nightOne.decisionFailures.length === 0,
    nightOne.decisionFailures.join('；') || nightOne.detail,
  )
  check(
    '行 3：夜里出现过筑梦师的**玩家请求**（玩家在自己的设备上选人）',
    nightOne.playerAnswers.some((answer) => answer.includes('筑梦师')),
    nightOne.playerAnswers.join('；') || '（一次玩家作答都没有）',
  )
  check(
    '行 3：夜里出现过说书人裁定点（钟表匠 + 筑梦师各一次）',
    nightOne.decisions.length >= 2,
    `${nightOne.decisions.length} 次：${nightOne.decisions.map((entry) => `${entry.text}（${entry.mode}）`).join('；')}`,
  )
  check(
    '行 3：夜里的请求都在预期集合里（没有计划外的能力被唤醒）',
    nightOne.unknownContexts.length === 0,
    nightOne.unknownContexts.join('；') || '（无）',
  )
  check(
    '行 3：槽位确实按配额流逝（整夜耗时 ≥ 槽位数 × 配额 × 0.5，不是被瞬间推完）',
    nightOne.slots !== null && nightOne.seconds >= nightOne.slots * config.quotaSeconds * 0.5,
    `槽位=${nightOne.slots ?? '（读不到）'}；耗时=${nightOne.seconds.toFixed(1)}s；配额=${config.quotaSeconds}s`,
  )
  check(
    '行 3：说书人抽屉里同时读到「本计划已走完」（与按钮口径互为二次确认）',
    await planCompleted(storyteller),
    compact(await panelText(storyteller, '当前步骤')).slice(0, 120),
  )

  // —— 行 4：首夜信息只到本人 ——
  const infoCounts = []
  for (const seat of [CLOCKMAKER_SEAT, MUTANT_SEAT, DEMON_SEAT, KLUTZ_SEAT, DREAMER_SEAT]) {
    infoCounts.push({ seat, count: await informationCount(players.get(seat)) })
  }

  const countOf = (seat) => infoCounts.find((entry) => entry.seat === seat)?.count ?? -1
  check(
    `行 4：钟表匠（${CLOCKMAKER_SEAT} 号）与筑梦师（${DREAMER_SEAT} 号）各自收到本人信息`,
    countOf(CLOCKMAKER_SEAT) >= 1 && countOf(DREAMER_SEAT) >= 1,
    infoCounts.map((entry) => `${entry.seat}号=${entry.count}`).join('，'),
  )
  check(
    '行 4：无关三席（畸形秀演员 / 恶魔 / 呆瓜）一条信息都没有',
    countOf(MUTANT_SEAT) === 0 && countOf(DEMON_SEAT) === 0 && countOf(KLUTZ_SEAT) === 0,
    infoCounts.map((entry) => `${entry.seat}号=${entry.count}`).join('，'),
  )
  check(
    '行 4：两名信息角色之间不串台（各自只收到自己那句裁定原文）',
    !(await infoText(players.get(CLOCKMAKER_SEAT))).includes(DREAMER_INFO)
      && !(await infoText(players.get(DREAMER_SEAT))).includes(CLOCKMAKER_INFO),
    '两份信息面板已互查',
  )
  // 正面断言：面板里确实有**自己的能力条目**——没有这一条，上面那句"不含对方的原文"在空面板上恒真。
  const clockmakerInfoText = await infoText(players.get(CLOCKMAKER_SEAT))
  const dreamerInfoText = await infoText(players.get(DREAMER_SEAT))
  check(
    '行 4：两名信息角色的面板里各有**自己的**能力条目（不是空面板）',
    clockmakerInfoText.includes('钟表匠') && dreamerInfoText.includes('筑梦师'),
    `钟表匠面板=${compact(clockmakerInfoText).slice(0, 60)}；筑梦师面板=${compact(dreamerInfoText).slice(0, 60)}`,
  )
  // 说书人**自己填的**那句话必须原样到达本人；走引擎候选分支时不适用（模式如实打印在明细里）。
  const clockmakerFreeText = nightOne.decisions.some(
    (entry) => entry.mode === 'freeText' && entry.target === 'clockmaker',
  )
  const dreamerFreeText = nightOne.decisions.some(
    (entry) => entry.mode === 'freeText' && entry.target === 'dreamer',
  )
  check(
    '行 4：说书人自由裁定的原文原样到达本人面板（候选分支不适用）',
    (!clockmakerFreeText || clockmakerInfoText.includes(CLOCKMAKER_INFO))
      && (!dreamerFreeText || dreamerInfoText.includes(DREAMER_INFO)),
    `钟表匠=${clockmakerFreeText ? '自由文本' : '引擎候选'}；筑梦师=${dreamerFreeText ? '自由文本' : '引擎候选'}`,
  )
  await screenshot(players.get(CLOCKMAKER_SEAT), 'fullgame-02-clockmaker-info')

  // —— 行 5 / 行 6 / 行 7：第 1 个白天 ——
  const dayOne = await playDay(storyteller, players, {
    dayNumber: 1,
    label: '第 1 天',
    script: SCRIPT.dayOne,
  })
  check('行 5：第 1 天开白天被受理', dayOne.started === 'Accepted', dayOne.startedRaw)
  check(
    '行 5：首夜无人死亡 → 白天公告里没有夜死（恶魔首夜不行动）',
    dayOne.announcementSeats.length === 0,
    `公告席位=${dayOne.announcementSeats.join(',') || '（无）'}`,
  )
  check(
    '行 6：提名进公开账目 → 收票冻结出 3 票 → 达线者进入「即将被处决」',
    dayOne.nominations === 1 && dayOne.votes === SCRIPT.dayOne.expectedVotes && dayOne.aboutToBeExecuted === SCRIPT.dayOne.nominee,
    `提名=${dayOne.nominations}；票数=${dayOne.votes}；即将被处决=${dayOne.aboutToBeExecuted}`,
  )
  check(
    `行 7：${SCRIPT.dayOne.nominee} 号被处决后真实死亡（说书人牌面 + 本日处决账）`,
    dayOne.executedSeat === SCRIPT.dayOne.nominee && dayOne.cardLife === 'Dead',
    `处决=${dayOne.executedSeat}；牌面=${dayOne.cardLife}`,
  )
  check(
    '行 7：处决的公开事实推到无关玩家的设备（自己那一席也翻死亡）',
    dayOne.witnessExecuted === SCRIPT.dayOne.nominee && dayOne.selfDeadBanner >= 1,
    `旁观者看到=${dayOne.witnessExecuted}；本人横幅=${dayOne.selfDeadBanner}`,
  )
  await screenshot(players.get(CLOCKMAKER_SEAT), 'fullgame-03-executed-self-dead')

  // —— 行 8：第 2 夜（恶魔第一次行动）——
  console.log('=== 6/8 第 2 夜：恶魔自己作答击杀，黎明公告 ===')
  const nightNumberInput = storyteller.locator('section', { hasText: '兜底与推进' }).locator('input[type=number]')
  await nightNumberInput.fill('2')
  const nightNumberReadBack = await nightNumberInput.inputValue()
  check('行 8：开夜前把「第几夜」填成 2 并回读确认（避免静默重开第 1 夜的表）', nightNumberReadBack === '2', `input=${nightNumberReadBack}`)

  const nightTwoStarted = await runCommand(storyteller, '开夜', () =>
    storyteller.getByRole('button', { name: /开夜/ }).click(),
  )
  check('行 8：第 2 夜开夜被受理', nightTwoStarted.kind === 'Accepted', nightTwoStarted.raw)

  const nightTwo = await driveNight(storyteller, players, { victim: SCRIPT.nightTwoVictim }, '第 2 夜')
  check(
    '行 8：第 2 夜走完（同样没有强推 / 代填，且裁定点没有一次被拒）',
    nightTwo.completed && nightTwo.decisionFailures.length === 0,
    nightTwo.decisionFailures.join('；') || nightTwo.detail,
  )
  check(
    `行 8：恶魔（${DEMON_SEAT} 号）在自己的设备上完成了击杀选择`,
    nightTwo.playerAnswers.some((answer) => answer.includes('诺-达鲺')),
    nightTwo.playerAnswers.join('；') || '（没有恶魔作答）',
  )
  check(
    '行 8：第 2 夜也出现过说书人裁定点（筑梦师第二次被唤醒）与玩家请求',
    nightTwo.decisions.length >= 1 && nightTwo.playerAnswers.some((answer) => answer.includes('筑梦师')),
    `裁定 ${nightTwo.decisions.length} 次：${nightTwo.decisions.map((entry) => `${entry.text}（${entry.mode}）`).join('；')}`
      + `；玩家作答 ${nightTwo.playerAnswers.length} 次`,
  )
  check(
    '行 8：第 2 夜同样只有预期内的请求',
    nightTwo.unknownContexts.length === 0,
    nightTwo.unknownContexts.join('；') || '（无）',
  )
  check(
    '行 8：第 2 夜的说书人抽屉里也读到「本计划已走完」',
    await planCompleted(storyteller),
    compact(await panelText(storyteller, '当前步骤')).slice(0, 120),
  )

  // —— 行 9 / 行 10：第 2 个白天 → 分出胜负 ——
  console.log('=== 7/8 第 2 天：提名处决 → 仅剩两名存活 → 邪恶获胜 ===')
  const dayTwo = await playDay(storyteller, players, {
    dayNumber: 2,
    label: '第 2 天',
    script: SCRIPT.dayTwo,
  })
  check('行 5（第 2 天）：第 2 天开白天被受理', dayTwo.started === 'Accepted', dayTwo.startedRaw)
  check(
    '行 5（第 2 天）：黎明公告里出现夜死（2 号），且公告文本不含角色名（死因不外泄）',
    dayTwo.announcementSeats.includes(SCRIPT.nightTwoVictim) && dayTwo.announcementText.includes('死亡'),
    `公告席位=${dayTwo.announcementSeats.join(',')}；公告文本=${compact(dayTwo.announcementText).slice(0, 120)}`,
  )
  check(
    '行 5（第 2 天）：公告文本里没有任何角色 slug（死因 / 凶手不外泄）',
    ASSIGN.every((slug) => !dayTwo.announcementText.includes(slug)) && !dayTwo.announcementText.includes('诺-达鲺'),
    compact(dayTwo.announcementText).slice(0, 120),
  )
  check(
    '行 6（第 2 天）：第二次提名收票冻结 2 票（3 席存活需 2 票）→ 达线',
    dayTwo.nominations === 1 && dayTwo.votes === SCRIPT.dayTwo.expectedVotes,
    `提名=${dayTwo.nominations}；票数=${dayTwo.votes}`,
  )

  const playerOutcomeWinner = await waitForAttribute(
    players.get(KLUTZ_SEAT).getByTestId('player-outcome'),
    'data-outcome-winner',
    'Evil',
    30_000,
  )
  const storytellerOutcomeWinner = await waitForAttribute(
    storyteller.getByTestId('storyteller-outcome'),
    'data-outcome-winner',
    'Evil',
    30_000,
  )
  const playerOutcomeDetail = compact(
    await readTextBounded(players.get(KLUTZ_SEAT).getByTestId('player-outcome-detail')),
  )
  check(
    '行 9：分出胜负——玩家端与说书人端出现**同一份**结束结论（邪恶获胜 · 仅剩两名存活）',
    playerOutcomeWinner === 'Evil'
      && storytellerOutcomeWinner === 'Evil'
      && playerOutcomeDetail.includes('仅剩 2 名玩家存活'),
    `玩家=${playerOutcomeWinner}；说书人=${storytellerOutcomeWinner}；说明=${playerOutcomeDetail.slice(0, 160)}`,
  )
  await screenshot(players.get(KLUTZ_SEAT), 'fullgame-04-player-outcome')
  await screenshot(storyteller, 'fullgame-05-storyteller-outcome')

  // —— 行 10：结束后冻结 ——
  const afterEnd = await runCommand(storyteller, '开夜', () => {
    return (async () => {
      await storyteller.locator('section', { hasText: '兜底与推进' }).locator('input[type=number]').fill('3')
      return storyteller.getByRole('button', { name: /开夜/ }).click()
    })()
  })
  check(
    '行 10：结束之后再开夜被拒（phase.game_ended）——结束态真的冻结了操作面',
    afterEnd.kind === 'Rejected' && afterEnd.raw.includes('phase.game_ended'),
    afterEnd.raw.slice(0, 160),
  )

  // —— 行 11：复盘 ——
  const replayOpened = await openReplay(players.get(KLUTZ_SEAT))
  check(
    '行 11：结束批次之后玩家能打开复盘并逐步回放',
    replayOpened.ok,
    replayOpened.detail,
  )
  await screenshot(players.get(KLUTZ_SEAT), 'fullgame-06-replay')

  // —— 行 12：夹具纪律 ——
  // 先说清这条证据的**两层**：① 装置自己贴的命令标签（下一条断言）；② 服务端事件流（接下来三条）。
  // 只有 ② 才是"没有夹具特权"的硬证据——`runCommand` 记的是脚本的意图，不是服务端的账。
  const usedCommands = [...new Set(storytellerCommands)]
  const offScript = usedCommands.filter((command) => !ALLOWED_COMMANDS.has(command))
  check(
    '行 12：装置只点过白名单里的说书人命令按钮（脚本意图自检）',
    offScript.length === 0,
    `用过：${usedCommands.join(',')}${offScript.length > 0 ? `；越界：${offScript.join(',')}` : ''}`,
  )

  const ledger = readLedgerSummary(databasePath)
  check(
    '行 12：事件流里没有作废（`OperationRequestVoidedEvent`）——读账，不是自报',
    ledger.voided === 0,
    `作废事件=${ledger.voided}；事件总数=${ledger.rows}`,
  )
  check(
    '行 12：事件流里没有代填（`OperationRequestAnsweredEvent` 的 source = StorytellerProxy）',
    ledger.proxyFilled === 0,
    `代填=${ledger.proxyFilled}`,
  )
  check(
    '行 12：事件流里没有强推 / 接管类事件（`*Force*` / `*Takeover*` / `*Override*`）',
    ledger.forced === 0,
    `强推类事件=${ledger.forced}${ledger.unexpectedTypes.length > 0 ? `：${ledger.unexpectedTypes.join(',')}` : ''}`,
  )
  check(
    '行 12：事件流读到并打印全量种类（供下一次收紧白名单，不猜种类名）',
    ledger.types.length >= 20,
    `${ledger.types.length} 种：${ledger.types.join(',')}`,
  )

  check('浏览器控制台没有报错', consoleErrors.length === 0, consoleErrors.slice(0, 3).join(' | '))

  await browser.close()
}

/**
 * 「夜里由真人玩」的驱动器：轮询说书人裁定点与各玩家设备上的挂起请求，按剧本作答。
 *
 * 它**不做强推、不做代填、不发作废**——这正是本装置要证明的事：一局可以在没有任何夹具特权、
 * 也不依赖"测试专用命令"的情况下走完。完成判据是说书人面板上的「本计划已走完」。
 */
async function driveNight(storyteller, players, plan, label) {
  const decisions = []
  const decisionFailures = []
  const playerAnswers = []
  const unknownContexts = []
  // 配额决定夜晚长度：首夜 14 个槽位、其他夜晚 25 个（本局花名册在两种口径下都是这个数）；
  // 取证档 2s/槽，所以给足余量，完成判据仍是"开白天按钮转为可用"而不是等满这个上限。
  const deadline = Date.now() + Math.max(90_000, 20_000 * ASSIGN.length * config.quotaSeconds)
  const startedAt = Date.now()
  const slots = (await readSlotCounter(storyteller))?.total ?? null
  let lastDecisionText = ''
  let sameDecisionRejections = 0
  while (Date.now() < deadline) {
    if (await nightIsDone(storyteller)) {
      const seconds = (Date.now() - startedAt) / 1000
      return {
        completed: true,
        detail: `${label}：裁定 ${decisions.length} 次 / 玩家作答 ${playerAnswers.length} 次`
          + `（开白天转为可用 = 计划走完；槽位 ${slots ?? '?'} 个 · 耗时 ${seconds.toFixed(1)}s）`,
        decisions,
        decisionFailures,
        playerAnswers,
        unknownContexts,
        slots,
        seconds,
      }
    }

    const decisionText = await readDecisionText(storyteller)
    if (decisionText.length > 0) {
      // 「这句裁定是谁的」必须在**未截断**的原文上判：截断后的展示文本可能正好切在角色名中间
      // （本装置首版就踩过——`钟表匠` 被切成 `钟表`，条件断言于是恒真）。
      const target = decisionText.includes('钟表匠') ? 'clockmaker' : decisionText.includes('筑梦师') ? 'dreamer' : 'other'
      const settled = await settleDecision(storyteller, target === 'clockmaker' ? CLOCKMAKER_INFO : DREAMER_INFO)
      if (settled.kind === 'Accepted') {
        decisions.push({ target, text: compact(decisionText).slice(0, 48), mode: settled.mode })
        sameDecisionRejections = 0
      } else {
        decisionFailures.push(`${compact(decisionText).slice(0, 40)} → ${settled.kind}`)
        // 同一条裁定连续被拒三次就停手：不靠等满 deadline 收场（AGENTS.local.md「红断言的等待要算钱」）。
        sameDecisionRejections = decisionText === lastDecisionText ? sameDecisionRejections + 1 : 1
        if (sameDecisionRejections >= 3) {
          return {
            completed: false,
            detail: `${label}：同一条裁定点连续被拒 3 次，停手`,
            decisions,
            decisionFailures,
            playerAnswers,
            unknownContexts,
            slots,
            seconds: (Date.now() - startedAt) / 1000,
          }
        }
      }

      lastDecisionText = decisionText
      await sleep(200)
      continue
    }

    const pending = await findPendingPlayer(players)
    if (pending !== null) {
      const { seat, page } = pending
      const context = compact(await readTextBounded(page.locator('[data-testid="player-request-context"]')))
      observedRequestContexts.push(`${seat} 号：${context}`)
      const choice = chooseOption(context, plan)
      if (choice === null) {
        unknownContexts.push(`${seat} 号：${context}`)
      }

      const optionValue = choice ?? (await firstOptionValue(page))
      await page
        .locator(`[data-testid="player-request-options"] label[data-option-value="${optionValue}"] input[type=radio]`)
        .check()
      await page.getByTestId('player-submit').click()
      await waitForAttribute(
        page.locator('[data-testid="player-request-panel"]'),
        'data-request-state',
        'idle',
        30_000,
      )
      playerAnswers.push(`${seat} 号：${context.slice(0, 32)} → ${optionValue}`)
      await sleep(200)
      continue
    }

    await sleep(200)
  }

  return {
    completed: false,
    detail: `${label}：等待超时（裁定 ${decisions.length} 次 / 玩家作答 ${playerAnswers.length} 次）`,
    decisions,
    decisionFailures,
    playerAnswers,
    unknownContexts,
    slots,
    seconds: (Date.now() - startedAt) / 1000,
  }
}

/** 按请求上下文挑合法作答：筑梦师选恶魔、恶魔选剧本里的目标；其余一律返回 null（记成计划外）。 */
function chooseOption(context, plan) {
  if (context.includes('筑梦师')) {
    return `seat:${DEMON_SEAT}`
  }

  if (context.includes('诺-达鲺') && plan.victim !== null) {
    return `seat:${plan.victim}`
  }

  return null
}

/** 计划外的请求：不猜——取第一个合法选项让局面继续，同时把上下文记进红断言。 */
async function firstOptionValue(page) {
  const labels = page.locator('[data-testid="player-request-options"] label')
  return (await labels.first().getAttribute('data-option-value')) ?? ''
}

/** 找出第一个挂着请求的玩家设备；没有返回 null。 */
async function findPendingPlayer(players) {
  for (const [seat, page] of players) {
    const state = await readAttributeBounded(page.locator('[data-testid="player-request-panel"]'), 'data-request-state')
    if (state === 'pending') {
      return { seat, page }
    }
  }

  return null
}

/**
 * 结清当前裁定点：引擎给了候选就点第一个候选，没有候选就按自由决定结清。
 * 两条路径都是说书人在真界面上的正常操作（`GrimoireSeatConsole` 的裁定区）；
 * 返回值带上 `mode`——"这句话是不是说书人自己填的"决定了下游能不能判"原文到达本人"。
 */
async function settleDecision(storyteller, content) {
  const block = storyteller.locator('[data-testid="console-decision"]').first()
  const candidates = block.locator('.options button')
  if ((await candidates.count()) > 0) {
    return { ...(await runCommand(storyteller, '裁定', () => candidates.first().click())), mode: 'candidate' }
  }

  await block.getByPlaceholder('自由决定的内容（可为空）').fill(content)
  return {
    ...(await runCommand(storyteller, '裁定', () => block.getByRole('button', { name: '按自由决定结清' }).click())),
    mode: 'freeText',
  }
}

/** 走完一个白天：开白天 → 提名 → 收票 → 计票 → 结束白天，返回这一天的读数。 */
async function playDay(storyteller, players, { dayNumber, label, script }) {
  const started = await runCommand(storyteller, '开白天', () => storyteller.getByTestId('st-start-day').click())
  const dayPanel = storyteller.getByTestId('st-day')
  const dayStatus = await waitForAttribute(dayPanel, 'data-day-status', 'Open', 30_000)

  // 说书人先用一句公开的死亡公告见证席判"夜死到黎明才说"：读的是无关玩家的公告列表。
  // 见证席固定用呆瓜那一席（两次白天的被处决者都不是它，所以它一直是旁观者）。
  const witness = players.get(KLUTZ_SEAT)
  const announcementText = await readTextBounded(witness.getByTestId('player-life-announcements'))
  const announcementSeats = await witness
    .locator('[data-testid="player-life-announcements"] li[data-state="Dead"]')
    .evaluateAll((nodes) => nodes.map((node) => Number(node.getAttribute('data-seat'))))
  await screenshot(players.get(DREAMER_SEAT), `fullgame-day${dayNumber}-open`)

  // 提名：提名者与目标都来自剧本（都是真人设备上的操作）。
  const nominatorPage = players.get(script.nominator)
  await nominatorPage.getByTestId('player-nominee-select').selectOption(String(script.nominee))
  await nominatorPage.getByTestId('player-nominate').click()
  const nominationList = storyteller.getByTestId('st-day-nominations')
  const nominations = Number(
    (await waitForAttribute(nominationList, 'data-nomination-count', '1', 30_000)) ?? '0',
  )

  // 收票：说书人设节拍 → 开始收票 → 剧本里的投票者各举一次手。
  await storyteller.getByTestId('st-sweep-countdown').fill('2')
  await storyteller.getByTestId('st-sweep-interval').fill('1')
  const sweep = await runCommand(storyteller, '开始收票', () => storyteller.getByTestId('st-start-vote-sweep').click())
  for (const seat of script.voters) {
    await players.get(seat).getByTestId('player-vote-yes').click()
  }

  const votes = Number(
    (await waitForAttribute(
      nominationList.locator('li').first(),
      'data-nomination-votes',
      String(script.expectedVotes),
      30_000,
    )) ?? '0',
  )
  await waitForAttribute(dayPanel, 'data-sweep-phase', 'AwaitingCount', 30_000)

  const counted = await runCommand(storyteller, '计票', () => storyteller.getByTestId('st-count-votes').click())
  const aboutToBeExecuted = Number(
    (await waitForAttribute(
      storyteller.getByTestId('st-about-to-be-executed'),
      'data-seat',
      String(script.nominee),
      30_000,
    )) ?? '0',
  )

  const closed = await runCommand(storyteller, '结束白天', () => storyteller.getByTestId('st-close-day').click())
  const executedSeat = Number(
    (await waitForAttribute(storyteller.getByTestId('st-executed'), 'data-seat', String(script.nominee), 30_000)) ?? '0',
  )
  const cardLife = await waitForAttribute(
    storyteller.locator(`[data-testid="grimoire-seat"][data-seat="${script.nominee}"]`),
    'data-life',
    'Dead',
    20_000,
  )

  // 反方向：处决的公开事实要推到**其他人**的设备上；本人设备出现死亡横幅。
  const witnessPage = players.get(script.nominator)
  const witnessExecuted = Number(
    (await waitForAttribute(
      witnessPage.getByTestId('player-executed'),
      'data-seat',
      String(script.nominee),
      20_000,
    )) ?? 0,
  )
  const selfDeadBanner = await players.get(script.nominee).getByTestId('player-self-dead').count()

  // 三条白天命令都必须被受理：只判"最终票数对"会让"计票其实被拒、数字是上一次的"混过去。
  check(
    `行 6（${label}）：开始收票 / 计票 / 结束白天三条命令都被受理`,
    sweep.kind === 'Accepted' && counted.kind === 'Accepted' && closed.kind === 'Accepted',
    `收票=${sweep.kind}；计票=${counted.kind}；结束白天=${closed.kind}`,
  )

  return {
    label,
    started: started.kind,
    startedRaw: started.raw,
    dayStatus,
    sweep: sweep.kind,
    counted: counted.kind,
    closed: closed.kind,
    announcementSeats,
    announcementText,
    nominations,
    votes,
    aboutToBeExecuted,
    executedSeat,
    cardLife,
    witnessExecuted,
    selfDeadBanner,
  }
}

/** 玩家端复盘：入口 → 面板 → 逐步回放。返回是否真的能走步。 */
async function openReplay(page) {
  const entry = page.getByTestId('player-replay-open')
  await entry.waitFor({ state: 'visible', timeout: 30_000 })
  await entry.click()
  await page.getByTestId('replay-panel').waitFor({ state: 'visible', timeout: 30_000 })

  // 面板先渲染「加载中…」再填内容：必须等首屏到位再判，否则读到的是一句加载提示（本装置首跑踩到）。
  const progress = page.getByTestId('replay-progress')
  const first = await waitForText(progress, '第 1 /', 30_000)
  await page.getByTestId('replay-next').click()
  const second = await waitForText(progress, '第 2 /', 30_000)
  await page.getByTestId('replay-next').click()
  const third = await waitForText(progress, '第 3 /', 30_000)
  const summary = compact(await readTextBounded(page.getByTestId('replay-summary')))
  return {
    ok: first.includes('第 1 /') && second.includes('第 2 /') && third.includes('第 3 /') && summary.length > 0,
    detail: `起始=${first}；两步后=${third}；摘要=${summary.slice(0, 80)}`,
  }
}

/** 轮询等一段文本出现目标子串；超时返回最后一次读到的原文。 */
async function waitForText(locator, needle, timeoutMs) {
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

/** 连一个真玩家席位（真浏览器 + 真 Vite；入场走账号，见 D-0027）。 */
async function joinSeatPage(players, browser, consoleErrors, gameId, seat) {
  const page = await newPage(browser, { width: 900, height: 1100 }, consoleErrors)
  await seatByAccount(page, { frontUrl: viteUrl, gameId, seat, suffix: `fullgame-${seat}` })
  const badge = page.locator('[data-testid="player-seat"]')
  await badge.waitFor({ timeout: 30_000 })
  const badgeText = compact(await readTextBounded(badge))
  players.set(seat, page)
  return { page, badgeText }
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

/** 玩家端信息面板的计数（data-information-count）；面板不在时返回 -1。 */
async function informationCount(page) {
  const panel = page.locator('[data-testid="player-information"]')
  if ((await panel.count()) === 0) {
    return -1
  }

  const raw = await readAttributeBounded(panel, 'data-information-count')
  return raw === null ? -1 : Number(raw)
}

async function infoText(page) {
  const info = page.locator('[data-testid="player-information"]')
  if ((await info.count()) === 0) {
    return ''
  }

  return await readTextBounded(info)
}

/** 说书人面「当前步骤」抽屉里的话：走完的判据是「本计划已走完」。 */
async function planCompleted(page) {
  return (await panelText(page, '当前步骤')).includes('本计划已走完')
}

/**
 * 夜晚是否走完：**「开白天」按钮转为可用**。
 *
 * 与既有装置同口径（`verify-butcher.mjs` 的 `nightIsDone`）：比读数据抽屉更直接，也不依赖抽屉开合——
 * 抽屉读数只作二次说明，不作判据（复核指出：把整夜推进押在抽屉上，一处 DOM 改动就是 90s 空转）。
 */
async function nightIsDone(page) {
  return page.getByTestId('st-start-day').isEnabled().catch(() => false)
}

/** 状态条上的「槽位 k / N」读数；读不到返回 null。 */
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

/** 裁定点区块的可见文本（没有等待中的裁定点时为空串）。 */
async function readDecisionText(page) {
  const block = page.locator('[data-testid="console-decision"]')
  if ((await block.count()) === 0) {
    return ''
  }

  return compact(await readTextBounded(block.first()))
}

async function setDataDrawer(page, open) {
  const toggle = page.locator('[data-testid="data-drawer-toggle"]')
  if ((await toggle.count()) === 0) {
    return
  }

  if (((await readAttributeBounded(toggle, 'aria-expanded')) === 'true') !== open) {
    await toggle.click()
  }
}

/** 某个 section.panel（按标题定位）的可见文本；找不到返回空串。 */
async function panelText(page, heading) {
  await setDataDrawer(page, true)
  const drawerBody = page.locator('[data-testid="data-drawer-body"]')
  const scope = (await drawerBody.count()) > 0 ? drawerBody : page
  const panel = scope.locator('section.panel', { hasText: heading })
  if ((await panel.count()) === 0) {
    return ''
  }

  return await readTextBounded(panel.first())
}

/** 说书人面板上的命令：点按钮 → 等一条新回执（序号必须比点击前大）。 */
async function runCommand(page, label, click) {
  storytellerCommands.push(label)
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
  console.log('\n=== 整局端到端（full-game）取证结论 ===')
  for (const result of results) {
    console.log(`${result.pass ? 'PASS' : 'FAIL'}  ${result.label}${result.detail ? ` → ${result.detail}` : ''}`)
  }

  const failed = results.filter((result) => !result.pass)
  console.log(
    failed.length === 0
      ? `全部通过（判定 ${results.length} 项）`
      : `失败 ${failed.length} 项（判定 ${results.length} 项）`,
  )
  if (observedRequestContexts.length > 0) {
    console.log('\n夜里被作答过的请求上下文：')
    for (const context of observedRequestContexts) {
      console.log(`  ${context}`)
    }
  }
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

/**
 * 读事件流的摘要：全量种类 + 三类"夹具特权"计数（作废 / 代填 / 强推-接管）。
 *
 * 为什么必须读账：`runCommand` 记的是**脚本贴的标签**（脚本的意图），不是服务端受理了什么；
 * "全程没有强推 / 代填 / 作废"只有从事件流里读出来才算证据（复核指出这条）。
 * 种类名不预猜：把 `DISTINCT Type` 原样打印出来供下一次收严（猜错就变成恒真的假绿）。
 */
function readLedgerSummary(databasePathToRead) {
  const database = new DatabaseSync(databasePathToRead, { readOnly: true })
  try {
    const rows = database.prepare('SELECT Type, Payload FROM Events ORDER BY Sequence').all()
    const types = [...new Set(rows.map((row) => String(row.Type)))].sort()
    let voided = 0
    let proxyFilled = 0
    let forced = 0
    for (const row of rows) {
      const type = String(row.Type)
      if (type === 'OperationRequestVoidedEvent') {
        voided += 1
      }

      if (/Force|Takeover|Override/.test(type)) {
        forced += 1
      }

      if (type === 'OperationRequestAnsweredEvent') {
        const payload = JSON.parse(String(row.Payload))
        const source = payload?.answer?.source ?? payload?.answer?.Source
        if (source === 1 || source === 'StorytellerProxy') {
          proxyFilled += 1
        }
      }
    }

    return {
      types,
      rows: rows.length,
      voided,
      proxyFilled,
      forced,
      unexpectedTypes: types.filter((type) => /Void|Force|Takeover|Override/.test(type)),
    }
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
  const parsed = { port: 5422, vitePort: 5302 }
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
