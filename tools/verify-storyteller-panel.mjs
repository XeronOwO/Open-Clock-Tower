/**
 * 真机验收批次装置（说书人 + 多玩家同局）。
 *
 * 它回答：**用真服务端 + 真浏览器 + 真 SQLite 做一次多客户端会话，
 * 说书人面板与玩家端能不能真的玩通夜晚与白天？**（验收规程：docs/acceptance/AGENTS.md §3）
 *
 * 场景（花名册默认五席：clockmaker / dreamer / no-dashii / mutant / klutz）：
 *   1) 起真宿主（独立临时库）→ 读说书人票据与各席位票据 → 起 Vite → 起 Chromium；
 *   2) 说书人 + 每席一个玩家各自加入（独立浏览器上下文 = 各自设备）→ 断言加入时页头阶段是中文；
 *   3) 说书人分配三角色 → 诺-达鲺常驻中毒落在最近的两名镇民（带归因与效果链接）；
 *   4) 说书人上报 1 号醉酒 → 与中毒并存、互不抵消；
 *   5) 开夜 → 阶段推送让各席玩家页头变「首夜」（行 3）→ 钟表匠槽位没有玩家选项，直接进说书人
 *      裁定点 → 每步摘要断言行 2（中毒 + 醉酒两条原因并列 + 未生效 + 无选项行为）→ 信息只到 1 号玩家；
 *   5b) 补齐并发窗口（票据 player-information-resync-race 行 1）：扣住 1 号的补齐响应 → 钟表匠信息
 *      推送先到 → 放行响应；断言推送不丢、不重复、无坏包诊断（截图 05b）；
 *   6) 筑梦师槽位：2 号玩家收到定向请求（摘要断言行 1：中毒 + 归因 + 未生效）→ 作答 →
 *      说书人自由裁定（能力未生效）→ 信息只到 2 号玩家；期间其余玩家必须零请求、零进度；
 *   7) 第一夜 13 个槽位自行走完（服务端推送，无刷新）；
 *   8) 白天阶段：说书人开白天 → 2 号提名 1 号 → 1 / 3 号投赞成 → 计票 → 结束并处决
 *      （公开事实各端可见；处决与死亡分开记录）；
 *   9) 第二夜（Recommended）：诺-达鲺击杀请求由说书人**代填**（行 2：3 号玩家不刷新就回空态并注明代填）→
 *      筑梦师请求由说书人**强制作废**（行 1：2 号玩家不刷新就看到请求消失与原因）；
 *      两个窗口都对无关玩家做窗口采样（行 4：持续零请求、零了结说明）；
 *  10) 第二夜走完 → 第三夜：诺-达鲺击杀请求由 3 号玩家本人作答（保留提交链路覆盖）→
 *      等筑梦师请求挂起后，先把 3 号换成涡流（来源失去能力，2 号中毒解除进摘要——行 5），
 *      再报 2 号死亡（请求依赖失效自动作废、作废说明进摘要——行 6；玩家侧同样收到作废推送）→ 2 号复活；
 *  11) 魔典主视图逐行取证（说书人端主视图 = 席位圆环）：行 1 圆环牌面、行 2 牌面标记 + 操作台
 *      归因、行 4 当前槽位高亮 + 操作台内完成真实裁定、行 3 死亡帷幕与复活解除、
 *      行 6 下钻表格与牌面同源、行 8 窄视口纵向列表；行 5（视角隔离）沿用玩家端反方向断言；
 *  12) 恢复与重建：干净流重建 → 三项等价；改脏事件流里的原因文本 → 状态账报"不一致"并由重建修回；
 *      停宿主 + 弄坏事件载荷 + 重启 → 说书人视图出现降级位与原因；重建仍失败 → 保持降级、原因更新；
 *      修复载荷后重建成功 → 降级清除；玩家端全程没有健康位文案 / 锚点；
 *  13) 重连补齐（快照权威）：隐藏事件不报假缺口、watermark 随快照序号前进；非零 watermark 跨掉线窗口
 *      重连（窗口内有其他席位的隐藏状态变化）仍无假告警；
 *  14) 涡流干扰回归（票据 vortox-interference-counting 行 1 / 4）：涡流存活 + 健康的筑梦师结算信息能力 →
 *      裁定点写明「涡流在场：信息必须为假（真角色不得出现）」→ 说书人账本同时给「正常生效 + 原因：涡流」
 *      并落 dreamer / 涡流 一行 → 信息只到 2 号；全部玩家端扫描不到失效归因。
 *      快档先按角色上报把 3 号换成涡流再开夜；取证档复用第三夜依赖块已换好的状态、收尾第三夜后开第四夜；
 *  15) 全程截图（取证档 39 张，用 --screenshots-all 落盘）；断言只落在真正渲染数据的面板 / 牌面内（`data-testid` 锚点 + 单调计数）。
 *
 * 前置：Node >= 22.5（node:sqlite）、web/node_modules 已安装、本机已装 Chromium：
 *   cd web
 *   npm install
 *   npx playwright install chromium
 *
 * 用法（在仓库根运行；默认五席全分配）：
 *   node tools/verify-storyteller-panel.mjs                    # 迭代档（默认）：快节拍 + 不落盘截图 + 复用产物
 *   node tools/verify-storyteller-panel.mjs --list-sections     # 列出可用段落名
 *   node tools/verify-storyteller-panel.mjs --only day1         # 只跑到白天段，且只有该段计入判定
 *   node tools/verify-storyteller-panel.mjs --from rebuild      # 全程执行，但从重建段起才计入判定
 *   node tools/verify-storyteller-panel.mjs --quota 2 --screenshots-all --build   # 取证档（一批一次）
 *
 * 档位（详见 tools/lib/verify-profile.mjs；取证档必须显式，默认是快档）：
 *   --quota <秒>         每槽节拍（默认 0.3；取证档用 2 秒）
 *   --screenshots-all    落盘全部截图（默认不落盘；"证据截图都已落盘"断言只在落盘档判定）
 *   --no-screenshots     显式不落盘（默认行为）
 *   --build / --skip-build   强制重建 / 显式复用（默认自动：产物缺失或 src/ 源码更新时重建）
 *
 * 段落（按序执行；--only 与 --from 互斥；前面段作为必要前置照跑，但只有选中段计入判定）：
 *   boot · tickets · join · assign · opening · annotation · night1-clockmaker · night1-dreamer-request
 *   · night1-dreamer-resolution · night1-finish · day1 · night2-3 · vortox · rebuild · reconnect · final
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
import { existsSync, mkdirSync, mkdtempSync, rmSync, statSync } from 'node:fs'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { DatabaseSync } from 'node:sqlite'
import { describeProfile, ensureServerArtifacts, extractProfileFlags, resolveProfile } from './lib/verify-profile.mjs'
import { createChecker, createSectionRunner } from './lib/verify-sections.mjs'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const webRoot = path.join(repositoryRoot, 'web')

/** 两端共用的取证标记：唯一文本，用来验证"信息只到该到的人"。 */
const CLOCKMAKER_INFO = '批次取证-钟表匠信息：本夜最小距离 2（说书人自由裁定）'
const DREAMER_INFO = '批次取证-筑梦师信息：由说书人自由裁定、可能错误'
const VORTOX_INFO = '批次取证-涡流：这条信息必须为假（说书人自由裁定）'

const { flags, rest } = extractProfileFlags(process.argv.slice(2))
const options = parseArguments(rest)
const config = resolveProfile(flags, { quotaSeconds: 0.3 })

/** 段落清单：顺序即执行顺序，也是用法头里那份清单的唯一事实来源。 */
const SECTIONS = [
  { id: 'boot', title: '构建并启动真宿主（独立临时库）' },
  { id: 'tickets', title: '取票据（说书人 + 各席位）并起 Vite' },
  { id: 'join', title: '说书人与各玩家加入（每席一个独立浏览器上下文）' },
  { id: 'assign', title: '说书人分配角色（席位全分配）' },
  { id: 'opening', title: '开局状态：诺-达鲺常驻中毒 + 说书人上报醉酒' },
  { id: 'annotation', title: '说书人注记：加 / 改 / 删 + 牌面 token + 玩家零下发（D-0019）' },
  { id: 'night1-clockmaker', title: '开夜 → 钟表匠裁定点（无玩家选项）→ 1 号玩家收信息' },
  { id: 'night1-dreamer-request', title: '筑梦师槽位：2 号玩家收到定向请求（无关玩家零活动）' },
  { id: 'night1-dreamer-resolution', title: '2 号玩家作答 → 说书人自由裁定（能力未生效）→ 信息单播' },
  { id: 'night1-finish', title: '说书人结算归因（第一夜）→ 等第一夜走完' },
  { id: 'day1', title: '白天阶段：开白天 → 提名 → 投票 → 计票 → 处决' },
  { id: 'night2-3', title: '第二夜与第三夜：代填 / 强制作废 / 阶段推送 → 依赖变化' },
  { id: 'vortox', title: '涡流干扰：涡流存活下的镇民信息结算 → 账本落 Vortox + 玩家隔离' },
  { id: 'rebuild', title: '恢复与重建：状态账对比 + 降级位' },
  { id: 'reconnect', title: '重连补齐与日志面：快照权威 watermark + 隐藏事件' },
  { id: 'final', title: '整场收尾：控制台零错误 + 截图落盘' },
]

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
    console.log(`  ${section.id.padEnd(26)} ${section.title}`)
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

console.log(`档位：${describeProfile(config)}`)
console.log(`段落选择：${runner.selectionSummary()}`)

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-batch-verify-'))
const databasePath = path.join(workspace, 'verify.db')
const screenshotsDir = path.resolve(repositoryRoot, options.screenshots)
if (config.screenshots) {
  mkdirSync(screenshotsDir, { recursive: true })
}

/** 本次运行的起始时刻：落盘档用它排除"上一轮残留的同名截图"顶过落盘断言（对抗复核 M4）。 */
const runStartedAt = Date.now()

const serverUrl = `http://localhost:${options.port}`
const viteUrl = `http://localhost:${options.vitePort}`
const children = []
/** 浏览器实例（模块级：正常收尾与 --only 早退路径都要关掉，避免留下孤儿 Chromium）。 */
let browser = null
/** 宿主日志累加（重启后继续累加同一份）：末尾要检查"故意损坏"有 Critical 记录、没有未处理异常。 */
const serverLog = []

process.on('exit', () => killChildren())

try {
  await main()
  runner.reportTimings()
  await cleanup()
  checker.report()
  process.exit(checker.results.some((result) => result.outcome === 'fail') ? 1 : 0)
} catch (error) {
  console.error(`\n[FAIL] 批次脚本异常终止：${error instanceof Error ? error.stack : String(error)}`)
  checker.results.push({ section: runner.currentId, label: '脚本执行到底', outcome: 'fail', detail: '见上方异常' })
  await cleanup()
  checker.report()
  process.exit(1)
}

async function main() {
  if (!runner.begin('boot')) return
  // 刻意直接跑编译产物而不是 `dotnet run`：宿主是**单个**进程，
  // 收尾时一次结束即可，不留需要树杀的子进程（与"禁止递归删除"同一姿态）。
  await ensureServerArtifacts({ repositoryRoot, buildMode: config.buildMode })
  let server = await startServer()

  if (!runner.begin('tickets')) return
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

  if (!runner.begin('join')) return
  browser = await playwright.chromium.launch()
  const consoleErrors = []

  const storyteller = await newClient(browser, { width: 1600, height: 1100 }, consoleErrors)
  await storyteller.page.goto(viteUrl)
  await storyteller.page.getByPlaceholder('说书人票据').fill(ticket)
  await storyteller.page.getByRole('button', { name: '加入' }).click()
  const grimoire = storyteller.page.locator('[data-testid="grimoire"]')
  await grimoire.waitFor({ timeout: 30_000 })
  await screenshot(storyteller.page, '01-storyteller-joined')
  check('说书人加入后看板可见（魔典主视图）', (await grimoire.count()) === 1)
  check(
    '魔典默认不被表格抢占：数据与审计收拢',
    (await storyteller.page.locator('[data-testid="data-drawer-toggle"]').getAttribute('aria-expanded')) === 'false'
      && !(await storyteller.page.locator('[data-testid="data-drawer-body"]').isVisible()),
  )

  const players = new Map()
  // 并发窗口取证（票据 player-information-resync-race 行 1）：拦截必须装在连接建立之前——
  // Playwright 只拦截安装之后新建的 WebSocket。
  const raceSeat = options.assign.indexOf('clockmaker') + 1
  const raceHolds = new Map()
  // 各席并行创建 / 加入：每席是独立设备（独立浏览器上下文），服务端本就按多客户端并发加入设计；
  // 串行加入会白等 5 次页面往返，并行后总耗时由最慢的一席决定。
  const joined = await Promise.all(
    seatTickets.map(async (seatTicket) => {
      const client = await newClient(browser, { width: 900, height: 900 }, consoleErrors)
      let hold = null
      if (seatTicket.seat === raceSeat) {
        hold = await installJoinResponseHold(client.page)
      }

      await client.page.goto(`${viteUrl}/#player`)
      await client.page.getByPlaceholder('席位票据').fill(seatTicket.ticket)
      await client.page.getByRole('button', { name: '加入' }).click()
      const seatBadge = client.page.locator('[data-testid="player-seat"]')
      await seatBadge.waitFor({ timeout: 30_000 })
      const badgeText = (await seatBadge.innerText()).trim()
      check(`玩家 ${seatTicket.seat} 号加入成功`, badgeText.includes(`${seatTicket.seat} 号`), badgeText)

      const initialPhase = (await client.page.locator('[data-testid="player-phase"]').innerText()).trim()
      check(
        `玩家 ${seatTicket.seat} 号加入时阶段显示中文「未开始」`,
        initialPhase === '未开始',
        initialPhase,
      )

      const shellText = await client.page.locator('.shell').innerText()
      const storytellerLeak = ['状态账', '效果归因链', '账本与结算结论', '裁定点与卡点', '席位操作台'].filter(
        (heading) => shellText.includes(heading),
      )
      check(
        `玩家 ${seatTicket.seat} 号界面不含说书人面板`,
        storytellerLeak.length === 0
          && (await client.page.locator('[data-testid="grimoire"]').count()) === 0
          && (await client.page.locator('[data-testid="seat-console"]').count()) === 0,
        storytellerLeak.join(',') || shellText.replace(/\s+/g, ' ').slice(0, 120),
      )
      return { seat: seatTicket.seat, client, hold }
    }),
  )
  for (const entry of joined) {
    players.set(entry.seat, entry.client)
    if (entry.hold !== null) {
      raceHolds.set(entry.seat, entry.hold)
    }
  }

  // 场景席位：由 --assign 的顺序派生（默认 1=钟表匠 / 2=筑梦师 / 3=诺-达鲺）。
  const clockmakerSeat = options.assign.indexOf('clockmaker') + 1
  const dreamerSeat = options.assign.indexOf('dreamer') + 1
  const demonSeat = options.assign.indexOf('no-dashii') + 1

  if (!runner.begin('assign')) return
  const seatCount = await readSeatCount(storyteller.page)
  check(
    '分配表覆盖服务端全部席位，且席位数量与分配清单一致',
    seatCount === options.assign.length,
    `UI 席位数=${seatCount}，分配清单=${options.assign.length}`,
  )
  const assignmentSelects = storyteller.page
    .locator('section', { hasText: '开局分配' })
    .locator('select')
  for (const [index, slug] of options.assign.entries()) {
    await assignmentSelects.nth(index).selectOption(slug)
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

  if (!runner.begin('opening')) return
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

  // —— 魔典主视图取证（矩阵行 1 / 2）：收拢表格，只看牌面与操作台 ——
  const cardOf = (seat) =>
    storyteller.page.locator(`[data-testid="grimoire-seat"][data-seat="${seat}"]`)
  await setDataDrawer(storyteller.page, false)
  const renderedSeats = await storyteller.page
    .locator('[data-testid="grimoire-seat"]')
    .evaluateAll((nodes) =>
      nodes.map((node) => Number(node.getAttribute('data-seat'))).sort((left, right) => left - right),
    )
  const expectedSeats = Array.from({ length: options.assign.length }, (_, index) => index + 1)
  check(
    '行 1：圆环按服务端席位号逐一渲染（1..N 各一张）',
    JSON.stringify(renderedSeats) === JSON.stringify(expectedSeats),
    `牌上席位=${renderedSeats.join(',')}，期望=${expectedSeats.join(',')}`,
  )
  const clockmakerCardText = (await cardOf(clockmakerSeat).innerText()).replace(/\s+/g, ' ')
  check(
    `行 1：${clockmakerSeat} 号牌面直接显示角色（${characterNameOf(options.assign[0])}）`,
    clockmakerCardText.includes(characterNameOf(options.assign[0])),
    clockmakerCardText.slice(0, 140),
  )
  check(
    '行 2：牌面同时挂出中毒与醉酒标记（互不抵消）',
    clockmakerCardText.includes('中毒') && clockmakerCardText.includes('醉酒'),
    clockmakerCardText.slice(0, 200),
  )
  await screenshot(storyteller.page, '17-grimoire-assigned')

  // 先点一张"非默认选中"的牌，证明点选链路真的把操作台切过去；再切回 1 号看归因。
  await cardOf(dreamerSeat).click()
  const consoleSeatAfterPick = await storyteller.page
    .locator('[data-testid="seat-console"]')
    .getAttribute('data-console-seat')
  check(
    '行 2：点选席位牌 → 操作台跟随该席（data-console-seat / aria-pressed）',
    consoleSeatAfterPick === String(dreamerSeat)
      && (await cardOf(dreamerSeat).getAttribute('aria-pressed')) === 'true',
    `console-seat=${consoleSeatAfterPick}`,
  )
  await cardOf(clockmakerSeat).click()
  const consoleText = (await storyteller.page.locator('[data-testid="seat-console"]').innerText())
    .replace(/\s+/g, ' ')
  check(
    '行 2：点开席位能在操作台追到归因（3 号 / 常驻效果 / 效果链接）',
    consoleText.includes(`${demonSeat} 号`) && consoleText.includes(poisonLinkFor(clockmakerSeat)),
    consoleText.slice(0, 260),
  )
  await screenshot(storyteller.page, '18-grimoire-seat-console')
  await setDataDrawer(storyteller.page, true)
  await screenshot(storyteller.page, '02-pre-night')

  if (!runner.begin('annotation')) return
  // —— 说书人注记（D-0019）：本局级自由文本 token；只说书人可见、不进状态账 ——
  // 用末席（默认 5 号呆瓜）做载体：注记不参与任何规则判定，不影响后面的夜晚 / 白天链路。
  const noteSeat = options.assign.length
  const noteText = '批次取证-注记：18 不共边'
  const noteEditedText = '批次取证-注记：18 与 5 不共边'
  const noteRestartText = '批次取证-注记：重启后仍在'
  const annotationInput = storyteller.page.locator('[data-testid="annotation-input"]')
  const annotationTokens = cardOf(noteSeat).locator('[data-testid="seat-notes"] [data-note-id]')

  await setDataDrawer(storyteller.page, false)
  await cardOf(noteSeat).click()
  await storyteller.page.locator('[data-testid="seat-console"]').waitFor({ state: 'visible', timeout: 15_000 })
  check(
    '注记：选中席位后操作台出现注记区，且初始为空',
    (await storyteller.page.locator('[data-testid="annotation-control"]').count()) === 1
      && (await storyteller.page.locator('[data-testid="annotation-empty"]').count()) === 1,
  )

  // 行 1：新增 → 牌面出现 token（全文进 title）；状态账面板里没有这条自由文本（D-0015）。
  await annotationInput.fill(noteText)
  const noteAdded = await runCommand(storyteller.page, '添加注记', () =>
    storyteller.page.locator('[data-testid="annotation-submit"]').click(),
  )
  check('注记行 1：新增被受理', noteAdded.kind === 'Accepted', noteAdded.raw)
  await annotationTokens.first().waitFor({ state: 'visible', timeout: 15_000 })
  const noteTokenText = (await annotationTokens.first().innerText()).trim()
  const noteTokenTitle = await annotationTokens.first().getAttribute('title')
  check(
    '注记行 1：牌面出现自由文本 token，全文进 title（牌面只显示有界文本）',
    noteTokenText.length > 0 && noteTokenTitle === noteText,
    `token=${noteTokenText} title=${noteTokenTitle}`,
  )
  check(
    '注记行 1：操作台列出全文',
    (await storyteller.page.locator('[data-testid="annotation-item"]').first().innerText()).includes('不共边'),
  )

  await setDataDrawer(storyteller.page, true)
  const ledgerPanelWithNote = await panelText(storyteller.page, '状态账')
  await setDataDrawer(storyteller.page, false)
  check(
    '注记行 1：状态账面板里没有这条自由文本（不进状态账，D-0015 / D-0019）',
    !ledgerPanelWithNote.includes('不共边'),
    ledgerPanelWithNote.replace(/\s+/g, ' ').slice(0, 200),
  )
  await screenshot(storyteller.page, '39-grimoire-annotation')

  // 行 3：改 → 同一条注记原地更新（data-note-id 不变），牌面 token 跟着变。
  const noteId = await annotationTokens.first().getAttribute('data-note-id')
  await storyteller.page.locator('[data-testid="annotation-edit"]').first().click()
  await storyteller.page.locator('[data-testid="annotation-edit-input"]').fill(noteEditedText)
  const noteUpdated = await runCommand(storyteller.page, '改注记', () =>
    storyteller.page.locator('[data-testid="annotation-save"]').click(),
  )
  check('注记行 3：修改被受理', noteUpdated.kind === 'Accepted', noteUpdated.raw)
  const updatedTokenTitle = await annotationTokens.first().getAttribute('title')
  check(
    '注记行 3：同一条注记原地更新（标识不变、全文跟着变）',
    updatedTokenTitle === noteEditedText
      && (await cardOf(noteSeat)
        .locator(`[data-testid="seat-notes"] [data-note-id="${noteId}"]`)
        .count()) === 1,
    `title=${updatedTokenTitle}`,
  )
  await screenshot(storyteller.page, '40-grimoire-annotation-edited')

  // 行 4：玩家侧零下发、零活动（反方向断言）。
  const annotationLeaks = []
  for (const [seat, client] of players) {
    const shellText = await client.page.locator('.shell').innerText()
    if (shellText.includes('不共边') || shellText.includes('注记')) {
      annotationLeaks.push(`${seat} 号文案`)
    }

    if ((await client.page.locator('[data-testid="annotation-control"]').count()) > 0) {
      annotationLeaks.push(`${seat} 号锚点`)
    }
  }

  check(
    '注记行 4：玩家端没有注记字段 / 锚点（不下发，不靠前端不显示）',
    annotationLeaks.length === 0,
    annotationLeaks.join('，') || `${players.size} 席已扫描`,
  )
  await screenshot(players.get(noteSeat).page, '41-annotation-player-clean')
  await sampleUnrelatedIdle(players, [...players.keys()], '注记窗口', 2)

  // 行 3：删 → token 消失；再补一条留给"重启后仍在"（矩阵行 5）。
  const noteRemoved = await runCommand(storyteller.page, '删注记', () =>
    storyteller.page.locator('[data-testid="annotation-delete"]').first().click(),
  )
  check('注记行 3：删除被受理', noteRemoved.kind === 'Accepted', noteRemoved.raw)
  check(
    '注记行 3：删除后牌面 token 消失',
    await waitForGone(annotationTokens.first(), 15_000),
  )
  await annotationInput.fill(noteRestartText)
  const noteReAdded = await runCommand(storyteller.page, '再添加注记', () =>
    storyteller.page.locator('[data-testid="annotation-submit"]').click(),
  )
  check('注记行 5 前置：重启前再写一条注记', noteReAdded.kind === 'Accepted', noteReAdded.raw)
  await annotationTokens.first().waitFor({ state: 'visible', timeout: 15_000 })

  if (!runner.begin('night1-clockmaker')) return
  const nightStarted = await runCommand(storyteller.page, '开夜', () =>
    storyteller.page.getByRole('button', { name: /开夜/ }).click(),
  )
  check('开夜被受理（真实顺序表建表）', nightStarted.kind === 'Accepted', nightStarted.raw)

  // 行 3 / 行 5：此前实测玩家页头停在 NotStarted（英文）——必须靠推送变成中文阶段，不用点「补齐」。
  const phaseAfterNightOne = await Promise.all(
    [...players.entries()].map(async ([seat, client]) =>
      `${seat}:${await waitForText(client.page.locator('[data-testid="player-phase"]'), '首夜', 20_000)}`,
    ),
  )
  check(
    `行 3：开夜推送让 ${options.seatCount} 席玩家页头变「首夜」（未点补齐、未刷新）`,
    phaseAfterNightOne.every((entry) => entry.endsWith(':首夜')),
    phaseAfterNightOne.join(', '),
  )

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
    `钟表匠槽位（该能力没有玩家选项）期间 ${options.seatCount} 席玩家均无请求`,
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
  // 行 2：每步摘要必须给出「行动者全部生效状态及来源 + 能力是否生效 + 无合法选项时的行为」。
  const clockmakerDigest = await panelText(storyteller.page, '当前步骤')
  check(
    '行 2：钟表匠摘要同时显示中毒与醉酒（互不抵消）',
    clockmakerDigest.includes('中毒') && clockmakerDigest.includes('醉酒'),
    clockmakerDigest.replace(/\s+/g, ' ').slice(0, 240),
  )
  check(
    '行 2：钟表匠摘要把中毒归因到 3 号并带效果链接',
    clockmakerDigest.includes(`${demonSeat} 号`) && clockmakerDigest.includes(poisonLinkFor(clockmakerSeat)),
    clockmakerDigest.replace(/\s+/g, ' ').slice(0, 240),
  )
  const clockmakerDigestCompact = clockmakerDigest.replace(/\s+/g, '')
  check(
    '行 2：钟表匠摘要显示能力未生效（按当前账预览，原因并列"中毒、醉酒"）',
    clockmakerDigestCompact.includes('未正常生效')
      && clockmakerDigestCompact.includes('按当前账预览')
      && clockmakerDigestCompact.includes('原因：中毒、醉酒'),
    clockmakerDigest.replace(/\s+/g, ' ').slice(0, 240),
  )
  check(
    '行 2：钟表匠摘要写明无合法选项时的行为（由说书人自由决定）',
    clockmakerDigest.includes('由说书人自由决定'),
    clockmakerDigest.replace(/\s+/g, ' ').slice(0, 240),
  )

  // —— 矩阵行 4：当前槽位高亮 + 就在操作台完成一次真实裁定 ——
  await setDataDrawer(storyteller.page, false)
  check(
    '行 4：当前槽位行动者的牌面高亮（data-current-slot=true）',
    (await cardOf(clockmakerSeat).getAttribute('data-current-slot')) === 'true',
    `current=${await cardOf(clockmakerSeat).getAttribute('data-current-slot')}`,
  )
  check(
    '行 4：裁定点在席位操作台里就近可处理',
    await storyteller.page.locator('[data-testid="console-decision"]').isVisible(),
  )
  await screenshot(storyteller.page, '19-grimoire-current-slot')
  await setDataDrawer(storyteller.page, true)
  await screenshot(storyteller.page, '04-clockmaker-decision')

  // —— 并发窗口取证（票据 player-information-resync-race 行 1）——
  // ① 扣住这次「补齐」的 JoinSeat 响应：快照（序号 N）已生成，客户端还没应用；
  // ② 说书人完成钟表匠裁定 → 信息推送（N+1）先到玩家页，补齐仍在等响应；
  // ③ 放行响应 → 修复前信息会被 N 快照整体覆盖丢弃，修复后按序号合并保留且不重复。
  const raceHold = raceHolds.get(clockmakerSeat)
  raceHold.arm()
  await players.get(clockmakerSeat).page.getByRole('button', { name: '补齐' }).click()
  const responseHeld = await raceHold.waitForHeld(30_000)
  check('并发票行 1：补齐响应已被扣住（快照已生成、客户端尚未应用）', responseHeld)

  const clockmakerOutcome = await settleFreeDecision(storyteller.page, CLOCKMAKER_INFO)
  check('钟表匠信息裁定被受理', clockmakerOutcome.kind === 'Accepted', clockmakerOutcome.raw)
  const pushedWhileHeld = await waitForPanelContains(
    players.get(clockmakerSeat).page,
    '我收到的信息',
    CLOCKMAKER_INFO,
    30_000,
  )
  const countWhileHeld = await informationCount(players.get(clockmakerSeat).page)
  check(
    '并发票行 1：补齐返回前，窗口内到达的信息推送已按推送呈现',
    pushedWhileHeld && countWhileHeld === 1,
    `推送可见=${pushedWhileHeld}；信息计数=${countWhileHeld}`,
  )

  raceHold.release()
  const resyncDiagnostics = await waitForLocatorContains(
    players.get(clockmakerSeat).page.locator('[data-testid="player-diagnostics"]'),
    '已按自身序号重新补齐',
    30_000,
  )
  const keptRaceText = await infoText(players.get(clockmakerSeat).page)
  const keptRaceCount = await informationCount(players.get(clockmakerSeat).page)
  check(
    '并发票行 1：放行补齐响应后推送不丢、不重复（信息计数=1 且内容在列）',
    keptRaceCount === 1 && keptRaceText.includes(CLOCKMAKER_INFO),
    `信息计数=${keptRaceCount}；文本=${keptRaceText.replace(/\s+/g, ' ').slice(0, 160)}`,
  )
  check(
    '并发票行 1：补齐成功且没有"重连补齐"坏包诊断',
    resyncDiagnostics.includes('已按自身序号重新补齐') && reconnectDiagnosticIn(resyncDiagnostics).length === 0,
    resyncDiagnostics || '（无诊断）',
  )
  await screenshot(players.get(clockmakerSeat).page, '05b-resync-window-info-kept')
  check(
    '1 号玩家信息面板带"信息可能错误"提示',
    (await players.get(clockmakerSeat).page.getByText('信息可能是错的').count()) > 0,
  )
  await screenshot(players.get(clockmakerSeat).page, '05-player-clockmaker-info')

  if (!runner.begin('night1-dreamer-request')) return
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
  // 行 1：请求窗口里的每步摘要（说书人视角）。
  const dreamerDigest = await panelText(storyteller.page, '当前步骤')
  check(
    '行 1：筑梦师摘要显示中毒、归因 3 号与效果链接',
    dreamerDigest.includes('中毒')
      && dreamerDigest.includes(`${demonSeat} 号`)
      && dreamerDigest.includes(poisonLinkFor(dreamerSeat)),
    dreamerDigest.replace(/\s+/g, ' ').slice(0, 240),
  )
  check(
    '行 1：筑梦师摘要显示能力未生效（按当前账预览，原因：中毒）',
    dreamerDigest.includes('未正常生效')
      && dreamerDigest.includes('按当前账预览')
      && dreamerDigest.includes('原因：中毒'),
    dreamerDigest.replace(/\s+/g, ' ').slice(0, 240),
  )
  check(
    '行 1：筑梦师摘要给出合法选项数量',
    dreamerDigest.includes(`合法选项 ${options.assign.length - 1} 个`),
    dreamerDigest.replace(/\s+/g, ' ').slice(0, 240),
  )
  await screenshot(storyteller.page, '06-storyteller-dreamer-digest')
  await screenshot(dreamerPlayer.page, '07-player-dreamer-request')

  // 反方向证据要做成"有宽度的观测"：在请求窗口内连续采样其余玩家的请求状态。
  // 单点读取只能证明"那一刻恰好空闲"，证明不了"整个窗口里没有活动"（独立复核 2026-10-02 指出）。
  const observedStates = new Map()
  for (const [seat] of players) {
    if (seat !== dreamerSeat) {
      observedStates.set(seat, new Set())
    }
  }

  // 诊断按**完整期望值**判，而不是"相对基线不变"：基线口径会连同窗口之前的遗留噪声一起放过，
  // 反方向取证就变成空话。本轮只有 1 号自己点过「补齐」（并发票行 1 的装置动作），
  // 它的成功提示是正当的；其余无关席位必须一条诊断都没有。
  const expectedDiagnosticsOf = (seat) => (seat === raceSeat ? '已按自身序号重新补齐' : '')

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
    const seatDiagnostics = await readPlayerDiagnostics(players.get(seat).page)
    check(
      `无关玩家 ${seat} 号诊断符合期望（无异常噪声；${raceSeat} 号只有自己点补齐的成功提示）`,
      seatDiagnostics === expectedDiagnosticsOf(seat),
      `期望=${expectedDiagnosticsOf(seat) || '（无）'}；实际=${seatDiagnostics || '（无）'}`,
    )
  }
  check(
    '纯旁观玩家 3 号信息列表为空（信息单播）',
    (await infoText(players.get(demonSeat).page)).includes('还没有收到信息'),
  )
  await screenshot(players.get(demonSeat).page, '08-unrelated-player-idle')

  if (!runner.begin('night1-dreamer-resolution')) return
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
  await screenshot(dreamerPlayer.page, '09-player-dreamer-info')

  if (!runner.begin('night1-finish')) return
  await waitForPanelContains(storyteller.page, '账本与结算结论', 'dreamer', 15_000)
  const resolutionPanel = await panelText(storyteller.page, '账本与结算结论')
  check(
    '最近一次结算：筑梦师未正常生效，原因可读',
    resolutionPanel.includes('未正常生效') && resolutionPanel.includes('dreamer') && resolutionPanel.includes('中毒'),
    resolutionPanel.replace(/\s+/g, ' ').slice(0, 240),
  )
  const clockmakerMalfunctionLines = linesOf(resolutionPanel, 'clockmaker')
  check(
    '失效账本同时记录钟表匠（中毒 + 醉酒，两条原因并列）与筑梦师（中毒）',
    clockmakerMalfunctionLines.includes('中毒')
      && clockmakerMalfunctionLines.includes('醉酒')
      && resolutionPanel.includes('dreamer'),
    clockmakerMalfunctionLines.replace(/\s+/g, ' ').slice(0, 300),
  )
  const seatLedgerAfter = await panelText(storyteller.page, '状态账')
  check(
    '状态账 2 号中毒带归因 3 号与效果链接',
    linesOf(seatLedgerAfter, `${dreamerSeat} 号`).includes('中毒')
      && linesOf(seatLedgerAfter, `${dreamerSeat} 号`).includes('standing:no-dashii.poison'),
    linesOf(seatLedgerAfter, `${dreamerSeat} 号`).slice(0, 240),
  )
  await screenshot(storyteller.page, '10-storyteller-resolutions')

  const nightOneClose = await finishNightQuickly(storyteller.page, '第一夜')
  check(
    '第一夜剩余槽位走完：自然推进窗口 + 强推空槽位（服务端推送，无刷新）',
    nightOneClose.completed === true,
    nightOneClose.natural
      ? '自然窗口内走完'
      : `强推 ${nightOneClose.forced} 步${nightOneClose.note ? `（${nightOneClose.note}）` : ''}`,
  )

  if (!runner.begin('day1')) return
  // 白天是公开信息（百科《规则概要》三；在线口径 R-0017）：提名 / 票面 / 处决各端都能看到；
  // 能不能动由服务端算好的权限位决定，前端只做使能提示。
  const startDayOutcome = await runCommand(storyteller.page, '开白天', () =>
    storyteller.page.getByTestId('st-start-day').click(),
  )
  check('白天阶段：开白天被受理', startDayOutcome.kind === 'Accepted', startDayOutcome.raw)

  const dayPanel = storyteller.page.getByTestId('st-day')
  const dayOpen = await waitForAttribute(dayPanel, 'data-day-status', 'Open', 30_000)
  check('白天阶段：说书人面板进入「白天进行中」', dayOpen === 'Open', `data-day-status=${dayOpen}`)
  await screenshot(storyteller.page, '30-day-open')

  // 2 号提名 1 号（1 号被处决，不影响后续夜晚剧情需要存活的 2 / 3 号）。
  const nominatorPage = players.get(dreamerSeat).page
  await nominatorPage.getByTestId('player-nominee-select').selectOption(String(clockmakerSeat))
  await nominatorPage.getByTestId('player-nominate').click()

  const nominationList = storyteller.page.getByTestId('st-day-nominations')
  const nominationCount = await waitForAttribute(nominationList, 'data-nomination-count', '1', 30_000)
  check('白天阶段：提名进入公开账目', nominationCount === '1', `data-nomination-count=${nominationCount}`)
  await screenshot(storyteller.page, '31-day-nomination')

  // 三名存活玩家各投一票（2 号提名者也投；1 号作为被提名者可以投自己——百科《规则概要》三-2）。
  // 玩家端没有回执区：判据是公开票数随推送变化。
  for (const voteSeat of [clockmakerSeat, dreamerSeat, demonSeat]) {
    await players.get(voteSeat).page.getByTestId('player-vote-yes').click()
  }

  const firstNomination = nominationList.locator('li').first()
  const voteCount = await waitForAttribute(firstNomination, 'data-nomination-votes', '3', 30_000)
  check('白天阶段：三次投票都到服务端（公开票数 3）', voteCount === '3', `data-nomination-votes=${voteCount}`)

  const countVotesOutcome = await runCommand(storyteller.page, '计票', () =>
    storyteller.page.getByTestId('st-count-votes').click(),
  )
  check('白天阶段：计票被受理', countVotesOutcome.kind === 'Accepted', countVotesOutcome.raw)

  const aboutToBeExecuted = storyteller.page.getByTestId('st-about-to-be-executed')
  const aboutSeat = await waitForAttribute(aboutToBeExecuted, 'data-seat', String(clockmakerSeat), 30_000)
  check(
    '白天阶段：票数过半且最多 → 进入「即将被处决」',
    aboutSeat === String(clockmakerSeat),
    `data-seat=${aboutSeat}`,
  )
  await screenshot(storyteller.page, '32-day-counted')

  const closeDayOutcome = await runCommand(storyteller.page, '结束白天并处决', () =>
    storyteller.page.getByTestId('st-close-day').click(),
  )
  check('白天阶段：结束白天被受理', closeDayOutcome.kind === 'Accepted', closeDayOutcome.raw)

  const executed = storyteller.page.getByTestId('st-executed')
  const executedSeat = await waitForAttribute(executed, 'data-seat', String(clockmakerSeat), 30_000)
  const dayClosed = await waitForAttribute(dayPanel, 'data-day-status', 'Closed', 30_000)
  const executedLife = await waitForAttribute(cardOf(clockmakerSeat), 'data-life', 'Dead', 15_000)
  check(
    '白天阶段：处决被记录且死亡另行落账（处决 ≠ 死亡）',
    executedSeat === String(clockmakerSeat) && dayClosed === 'Closed' && executedLife === 'Dead',
    `处决=${executedSeat}；白天=${dayClosed}；牌面=${executedLife}`,
  )

  // 玩家侧同一条公开事实：无关玩家也能看到「1 号被处决」。
  const playerExecuted = players.get(demonSeat).page.getByTestId('player-executed')
  const playerExecutedSeat = await waitForAttribute(playerExecuted, 'data-seat', String(clockmakerSeat), 30_000)
  check(
    '白天阶段：公开事实推到无关玩家（3 号看到 1 号被处决）',
    playerExecutedSeat === String(clockmakerSeat),
    `data-seat=${playerExecutedSeat}`,
  )
  await screenshot(players.get(demonSeat).page, '33-day-executed')

  // R-0022（行 2 / 行 3）：处决致死的死亡进入公开生死面——无关玩家的牌面与本日公告都能看到；
  // 被处决者自己的界面显式可见（横幅 + 自己席位翻死亡），而不是只有说书人的账。
  const witnessLife = await waitForAttribute(
    players.get(demonSeat).page.locator(`[data-testid="player-lives"] li[data-seat="${clockmakerSeat}"]`),
    'data-life',
    'Dead',
    20_000,
  )
  const witnessAnnouncement = await players
    .get(demonSeat)
    .page.locator(`[data-testid="player-life-announcements"] li[data-seat="${clockmakerSeat}"][data-state="Dead"]`)
    .count()
  check(
    '行 2：处决死亡进入无关玩家的公开生死面（牌面翻死亡 + 本日公告）',
    witnessLife === 'Dead' && witnessAnnouncement >= 1,
    `牌面=${witnessLife}；公告条数=${witnessAnnouncement}`,
  )
  const selfDeadBanner = await players.get(clockmakerSeat).page.getByTestId('player-self-dead').count()
  const selfLife = await waitForAttribute(
    players.get(clockmakerSeat).page.locator(`[data-testid="player-lives"] li[data-seat="${clockmakerSeat}"]`),
    'data-life',
    'Dead',
    20_000,
  )
  check(
    '行 3：被处决者自己的界面显式可见死亡（横幅 + 自己席位翻死亡）',
    selfDeadBanner >= 1 && selfLife === 'Dead',
    `横幅=${selfDeadBanner}；自己牌面=${selfLife}`,
  )
  await screenshot(players.get(clockmakerSeat).page, '34-player-self-dead')

  if (!runner.begin('night2-3')) return
  const nightTwo = await runCommand(storyteller.page, '开夜2', async () => {
    await storyteller.page
      .locator('section', { hasText: '兜底与推进' })
      .locator('input[type=number]')
      .fill('2')
    await storyteller.page.getByRole('button', { name: /开夜/ }).click()
  })
  check('第二夜（Recommended）开夜被受理', nightTwo.kind === 'Accepted', nightTwo.raw)

  // 行 3 / 行 5：第二次阶段变化同样靠推送抵达（OtherNight → 中文「夜晚」，不用点「补齐」）。
  const phaseAfterNightTwo = await Promise.all(
    [...players.entries()].map(async ([seat, client]) =>
      `${seat}:${await waitForText(client.page.locator('[data-testid="player-phase"]'), '夜晚', 20_000)}`,
    ),
  )
  check(
    `行 3：第二夜推送让 ${options.seatCount} 席玩家页头变「夜晚」（未点补齐、未刷新）`,
    phaseAfterNightTwo.every((entry) => entry.endsWith(':夜晚')),
    phaseAfterNightTwo.join(', '),
  )
  await screenshot(players.get(clockmakerSeat).page, '14-player-phase-night-two')

  // 行 2：第二夜诺-达鲺击杀请求 → 说书人代填 → 3 号玩家不刷新就回空态，并注明由谁了结。
  const demonPlayer = players.get(demonSeat)
  const demonRequestPanel = demonPlayer.page.locator('[data-testid="player-request-panel"]')
  const demonAdvance = await advanceSlotsUntil(
    storyteller.page,
    async () => (await demonRequestPanel.getAttribute('data-request-state').catch(() => null)) === 'pending',
    '第二夜推进到恶魔槽位',
  )
  if (demonAdvance.blockedBy !== undefined) {
    throw new Error(`第二夜推进受阻：${demonAdvance.blockedBy}（已强推 ${demonAdvance.steps} 步）`)
  }

  const demonRequestState = await waitForAttribute(demonRequestPanel, 'data-request-state', 'pending', 180_000)
  check(
    '第二夜 3 号玩家收到诺-达鲺击杀请求',
    demonRequestState === 'pending',
    `data-request-state=${demonRequestState}`,
  )
  await screenshot(demonPlayer.page, '11-night2-demon-request')

  const proxyOutcome = await proxyFillPending(
    storyteller.page,
    `seat:${clockmakerSeat}`,
    '批次取证：说书人代填',
  )
  check('说书人代填被受理', proxyOutcome.kind === 'Accepted', proxyOutcome.raw)
  const demonBackToIdle = await waitForAttribute(demonRequestPanel, 'data-request-state', 'idle', 30_000)
  check(
    '行 2：代填后 3 号玩家请求区回到空态（未点补齐）',
    demonBackToIdle === 'idle',
    `data-request-state=${demonBackToIdle}`,
  )
  const demonSettledNote = await readSettledNote(demonPlayer.page)
  check(
    '行 2：3 号玩家看到「由说书人代填」（请求消失有交代）',
    demonSettledNote.includes('由说书人代填'),
    demonSettledNote || '（无了结说明）',
  )
  await screenshot(demonPlayer.page, '15-player-proxy-filled')
  // 行 4：代填窗口里与这条请求无关的 1 号玩家必须持续零活动（钟表匠首夜之后不再有槽位）。
  await sampleUnrelatedIdle(players, [clockmakerSeat], '代填窗口', 2)

  const dreamerNightTwo = await waitForAttribute(dreamerRequestPanel, 'data-request-state', 'pending', 180_000)
  check(
    '第二夜 2 号玩家收到筑梦师请求（中毒仍在）',
    dreamerNightTwo === 'pending',
    `data-request-state=${dreamerNightTwo}`,
  )
  const nightTwoDigestVisible = await waitForPanelContains(
    storyteller.page,
    '当前步骤',
    poisonLinkFor(dreamerSeat),
    30_000,
  )
  check(
    '第二夜筑梦师摘要仍显示中毒与效果链接',
    nightTwoDigestVisible,
    (await panelText(storyteller.page, '当前步骤')).replace(/\s+/g, ' ').slice(0, 240),
  )

  // 行 1：说书人强制作废 → 2 号玩家不刷新就看到请求消失，并读到作废原因。
  const voidOutcome = await forceVoidPending(storyteller.page, 'StorytellerForce', '批次取证：强制作废')
  check('说书人强制作废被受理', voidOutcome.kind === 'Accepted', voidOutcome.raw)
  const dreamerBackToIdle = await waitForAttribute(dreamerRequestPanel, 'data-request-state', 'idle', 30_000)
  check(
    '行 1：强制作废后 2 号玩家请求区回到空态（未点补齐）',
    dreamerBackToIdle === 'idle',
    `data-request-state=${dreamerBackToIdle}`,
  )
  const dreamerSettledNote = await readSettledNote(dreamerPlayer.page)
  check(
    '行 1：2 号玩家看到作废原因（说书人强制作废）',
    dreamerSettledNote.includes('请求已作废') && dreamerSettledNote.includes('说书人强制作废'),
    dreamerSettledNote || '（无了结说明）',
  )
  await screenshot(dreamerPlayer.page, '16-player-forced-void')
  // 行 4：作废窗口里 1 号与 3 号必须持续零活动（这是筑梦师槽位之后的最后一个行动槽）。
  await sampleUnrelatedIdle(players, [clockmakerSeat, demonSeat], '强制作废窗口', 2)

  const nightTwoClose = await finishNightQuickly(storyteller.page, '第二夜')
  check(
    '第二夜剩余槽位走完：自然推进窗口 + 强推空槽位（服务端推送，无刷新）',
    nightTwoClose.completed === true,
    nightTwoClose.natural ? '自然窗口内走完' : `强推 ${nightTwoClose.forced} 步`,
  )

  // —— 夜间死亡"黎明前不公开" + 帷幕往返（R-0022 行 1 反向 / 矩阵行 3）——
  // 用未席（默认 5 号，外来者，不影响恶魔与结束条件）；复活后状态复原，后续段不受影响。
  // 这两条语义原先只在第三夜块里，整块降级为取证档专属后会缺席——这里补一次独立往返（对抗自检 P2）。
  const probeLifeSeat = options.assign.length
  const probeLifePlayer = players.get(probeLifeSeat)
  const probeDeathOutcome = await reportSeatState(storyteller.page, {
    seat: probeLifeSeat,
    dimensionLabel: '生死',
    value: 'Dead',
    reason: '批次取证：夜里上报死亡，验证公开面未公告与帷幕',
  })
  check(`夜里上报 ${probeLifeSeat} 号死亡被受理`, probeDeathOutcome.kind === 'Accepted', probeDeathOutcome.raw)
  await setDataDrawer(storyteller.page, false)
  const deadProbeCard = (await cardOf(probeLifeSeat).innerText()).replace(/\s+/g, ' ')
  check(
    `行 3：${probeLifeSeat} 号死亡后牌面盖上帷幕（data-life=Dead）`,
    (await cardOf(probeLifeSeat).getAttribute('data-life')) === 'Dead' && deadProbeCard.includes('帷幕'),
    deadProbeCard.slice(0, 160),
  )
  const probePublicLife = await readPlayerLifeOf(probeLifePlayer.page, probeLifeSeat)
  check(
    `行 1：夜里上报的死亡在黎明前不进公开面（${probeLifeSeat} 号本人仍显示 Alive）`,
    probePublicLife === 'Alive',
    `data-life=${probePublicLife}`,
  )

  const probeReviveOutcome = await reportSeatState(storyteller.page, {
    seat: probeLifeSeat,
    dimensionLabel: '生死',
    value: 'Alive',
    reason: '批次取证：复活，验证帷幕解除与公开面恢复',
  })
  check(`上报 ${probeLifeSeat} 号复活被受理`, probeReviveOutcome.kind === 'Accepted', probeReviveOutcome.raw)
  await waitForAttribute(cardOf(probeLifeSeat), 'data-life', 'Alive', 15_000)
  const revivedProbeCard = (await cardOf(probeLifeSeat).innerText()).replace(/\s+/g, ' ')
  check(
    `行 3：复活后帷幕解除、牌面回到存活（${probeLifeSeat} 号）`,
    (await cardOf(probeLifeSeat).getAttribute('data-life')) === 'Alive' && !revivedProbeCard.includes('帷幕'),
    revivedProbeCard.slice(0, 160),
  )

  // —— 第三夜：依赖变化深度场景（取证档专属）——
  // 依据（P0-5 结构性裁剪）：自动作废的判定、依赖说明与中毒解除摘要已由真宿主集成测试等价覆盖
  // （SeatDependencyVoidTests / StepMachineHostTests 行 8 / StepDigestHostTests 行 5–6）；
  // 作废推送到玩家页与第二夜的强制作废**共用同一条前端推送链路**，触发差异（自动 vs 强制作废）
  // 由上述集成测试覆盖。迭代档跳过整个第三夜以省槽位推进时间，但显式记一条聚合 SKIP。
  // 夜间死亡"黎明前不公开"与"复活解除帷幕"两条真机语义不在此块缺席——
  // 第二夜结束后用 5 号（外来者）做独立的死亡 → 复活往返来覆盖（见上方）。
  if (!config.slowPacer) {
    check(
      '第三夜依赖变化深度场景（自动作废 / 中毒解除摘要 / 玩家侧作废推送 / 复活）',
      true,
      '取证档专属：需要 ≥1s/槽节拍；等价语义由集成测试覆盖',
      { slowPacer: true },
    )
  } else {
  // 行 5 / 6 的依赖变化放在第三夜：各席都还活着，槽位与依赖都按正常路径生效。
  const nightThree = await runCommand(storyteller.page, '开夜3', async () => {
    await storyteller.page
      .locator('section', { hasText: '兜底与推进' })
      .locator('input[type=number]')
      .fill('3')
    await storyteller.page.getByRole('button', { name: /开夜/ }).click()
  })
  check('第三夜（Recommended）开夜被受理', nightThree.kind === 'Accepted', nightThree.raw)

  // 第三夜诺-达鲺击杀请求由 3 号玩家本人作答：保留玩家提交链路的真机覆盖。
  const demonThirdAdvance = await advanceSlotsUntil(
    storyteller.page,
    async () => (await demonRequestPanel.getAttribute('data-request-state').catch(() => null)) === 'pending',
    '第三夜推进到恶魔槽位',
  )
  if (demonThirdAdvance.blockedBy !== undefined) {
    throw new Error(`第三夜推进受阻：${demonThirdAdvance.blockedBy}（已强推 ${demonThirdAdvance.steps} 步）`)
  }

  const demonNightThree = await waitForAttribute(demonRequestPanel, 'data-request-state', 'pending', 180_000)
  check(
    '第三夜 3 号玩家收到诺-达鲺击杀请求',
    demonNightThree === 'pending',
    `data-request-state=${demonNightThree}`,
  )
  await demonPlayer.page
    .locator('[data-testid="player-request-options"] label', { hasText: `${clockmakerSeat} 号玩家` })
    .locator('input[type=radio]')
    .check()
  await demonPlayer.page.locator('[data-testid="player-submit"]').click()
  const demonSubmitted = await waitForAttribute(demonRequestPanel, 'data-request-state', 'idle', 30_000)
  check(
    '第三夜 3 号玩家提交击杀目标被受理、请求区回到空态',
    demonSubmitted === 'idle',
    `data-request-state=${demonSubmitted}`,
  )

  const dreamerNightThree = await waitForAttribute(dreamerRequestPanel, 'data-request-state', 'pending', 180_000)
  check(
    '第三夜 2 号玩家收到筑梦师请求（中毒仍在）',
    dreamerNightThree === 'pending',
    `data-request-state=${dreamerNightThree}`,
  )
  const nightThreeDigestVisible = await waitForPanelContains(
    storyteller.page,
    '当前步骤',
    poisonLinkFor(dreamerSeat),
    30_000,
  )
  check(
    '第三夜筑梦师摘要仍显示中毒与效果链接',
    nightThreeDigestVisible,
    (await panelText(storyteller.page, '当前步骤')).replace(/\s+/g, ' ').slice(0, 240),
  )

  // 行 5：中毒来源死亡 → 维度解除进摘要（挂起请求还占着槽位，行动者就是 2 号）。
  // 让中毒来源**失去能力**：把诺-达鲺换成另一名恶魔（角色变化 → 能力存续判定失效 → 常驻中毒终止）。
  // 不直接上报它的死亡：唯一恶魔死亡 = 「所有恶魔均死亡 → 善良获胜」（R-0024），游戏会当场结束，
  // 本装置后续步骤（依赖作废 / 重建 / 重连）就都跑不到了。
  const demonSwap = await reportSeatState(storyteller.page, {
    seat: demonSeat,
    dimensionLabel: '角色',
    value: 'vortox',
    reason: '批次取证：诺-达鲺换成涡流，验证中毒解除进摘要',
  })
  check('第三夜上报 3 号换角被受理', demonSwap.kind === 'Accepted', demonSwap.raw)
  const releaseVisible = await waitForPanelContains(storyteller.page, '当前步骤', '解除', 30_000)
  const digestAfterRelease = await panelText(storyteller.page, '当前步骤')
  check(
    '行 5：摘要显示 2 号中毒解除（原因 / 归因 3 号 / 同一效果链接）',
    releaseVisible
      && digestAfterRelease.includes('健康')
      && digestAfterRelease.includes(`${demonSeat} 号`)
      && digestAfterRelease.includes(poisonLinkFor(dreamerSeat)),
    digestAfterRelease.replace(/\s+/g, ' ').slice(0, 300),
  )
  await screenshot(storyteller.page, '12-digest-poison-released')

  // 行 6：挂起请求的行动者死亡 → 依赖失效自动作废，摘要写明是哪一条依赖不满足；
  // 同时检查玩家侧：自动作废也必须以推送抵达 2 号（空态 + 原因），不靠"补齐"。
  const dreamerDeath = await reportSeatState(storyteller.page, {
    seat: dreamerSeat,
    dimensionLabel: '生死',
    value: 'Dead',
    reason: '批次取证：筑梦师死亡，验证挂起请求依赖失效自动作废',
  })
  check('第三夜上报 2 号死亡被受理', dreamerDeath.kind === 'Accepted', dreamerDeath.raw)
  const voidNote = `座位 ${dreamerSeat} 的状态变化使请求失去意义`
  const voidVisible = await waitForPanelContains(storyteller.page, '当前步骤', voidNote, 30_000)
  const digestAfterVoid = await panelText(storyteller.page, '当前步骤')
  check(
    '行 6：摘要写明哪条依赖不满足（生死 Dead ≠ 要求 Alive）',
    voidVisible && digestAfterVoid.includes('生死 Dead ≠ 要求 Alive'),
    digestAfterVoid.replace(/\s+/g, ' ').slice(0, 300),
  )
  const autoVoided = await waitForAttribute(dreamerRequestPanel, 'data-request-state', 'idle', 30_000)
  const autoVoidNote = await readSettledNote(dreamerPlayer.page)
  check(
    '行 6：自动作废同样以推送到达 2 号玩家（空态 + 原因「座位依赖不再满足」）',
    autoVoided === 'idle'
      && autoVoidNote.includes('请求已作废')
      && autoVoidNote.includes('座位依赖不再满足'),
    `${autoVoided} / ${autoVoidNote || '（无了结说明）'}`,
  )
  await screenshot(storyteller.page, '13-digest-request-voided')

  // R-0022 行 1 的反方向：夜里上报的死亡没到黎明——公开面不显示 2 / 3 号死亡（含本人）；
  // 说书人账与玩家牌面在这里刻意"不一致"，直到下一个黎明（未公告不算数）。
  const notYetAnnounced = []
  for (const seat of [dreamerSeat, demonSeat]) {
    const life = await players
      .get(seat)
      .page.locator(`[data-testid="player-lives"] li[data-seat="${seat}"]`)
      .first()
      .getAttribute('data-life')
    notYetAnnounced.push(`${seat}:${life}`)
  }
  check(
    '行 1：夜晚死亡在黎明前不进公开面（含本人；牌面仍显示 Alive）',
    notYetAnnounced.every((entry) => entry.endsWith(':Alive')),
    notYetAnnounced.join(', '),
  )

  // —— 矩阵行 3：复活 → 帷幕解除（真实状态上报，同一份视图推送；死亡帷幕已用白天处决的 1 号覆盖）——
  const reviveOutcome = await reportSeatState(storyteller.page, {
    seat: dreamerSeat,
    dimensionLabel: '生死',
    value: 'Alive',
    reason: '批次取证：复活 2 号，验证帷幕解除与标记同步',
  })
  check('第三夜上报 2 号复活被受理', reviveOutcome.kind === 'Accepted', reviveOutcome.raw)
  await waitForAttribute(cardOf(dreamerSeat), 'data-life', 'Alive', 15_000)
  const revivedCardText = (await cardOf(dreamerSeat).innerText()).replace(/\s+/g, ' ')
  check(
    '行 3：复活后帷幕解除、牌面回到存活（状态标记仍在同一张牌上）',
    (await cardOf(dreamerSeat).getAttribute('data-life')) === 'Alive'
      && !revivedCardText.includes('帷幕'),
    revivedCardText.slice(0, 160),
  )
  await screenshot(storyteller.page, '21-grimoire-revive')
  }

  // —— 矩阵行 3：死亡 → 帷幕（1 号在白天已被处决；不依赖第三夜，迭代档同样要跑）——
  await setDataDrawer(storyteller.page, false)
  const executedCardText = (await cardOf(clockmakerSeat).innerText()).replace(/\s+/g, ' ')
  check(
    `行 3：白天处决的 ${clockmakerSeat} 号牌面盖上帷幕（data-life=Dead）`,
    (await cardOf(clockmakerSeat).getAttribute('data-life')) === 'Dead' && executedCardText.includes('帷幕'),
    executedCardText.slice(0, 160),
  )
  await screenshot(storyteller.page, '20-grimoire-death')

  // —— 矩阵行 6：主视图与下钻表格同源（同一份视图推送）——
  await setDataDrawer(storyteller.page, true)
  const ledgerText = await panelText(storyteller.page, '状态账')
  const sameSource = options.assign.map((slug, index) =>
    linesOf(ledgerText, `${index + 1} 号`).includes(characterNameOf(slug)),
  )
  check(
    '行 6：每席牌面角色与下钻状态账里的角色一一对应（同一次视图推送）',
    sameSource.every(Boolean),
    sameSource.map((ok, index) => `${index + 1}:${ok ? 'ok' : 'x'}`).join(' '),
  )
  await screenshot(storyteller.page, '22-grimoire-data-drawer')

  // —— 矩阵行 8：窄视口退化为纵向席位列表 ——
  await storyteller.page.setViewportSize({ width: 430, height: 1200 })
  await setDataDrawer(storyteller.page, false)
  const stackedBoxes = []
  for (let seat = 1; seat <= options.assign.length; seat += 1) {
    stackedBoxes.push(await cardOf(seat).boundingBox())
  }
  check(
    '行 8：窄视口下席位牌自上而下纵向排列，角色信息不丢',
    stackedBoxes.every((box) => box !== null)
      && stackedBoxes.every((box, index) => index === 0 || box.y > stackedBoxes[index - 1].y)
      && (await cardOf(1).innerText()).includes(characterNameOf(options.assign[0])),
    stackedBoxes.map((box) => (box === null ? 'null' : Math.round(box.y))).join(','),
  )
  await screenshot(storyteller.page, '23-grimoire-narrow')
  await storyteller.page.setViewportSize({ width: 1600, height: 1100 })

  // —— 上报表单回归（对抗性复核 H-1 / M-2）：换席整表复位 + 勾选维度必须有取值 ——
  const alignmentReport = await reportSeatState(storyteller.page, {
    seat: dreamerSeat,
    dimensionLabel: '阵营',
    value: 'Evil',
    reason: '批次取证：换席复位回归（本局尾声，不影响前面玩法断言）',
  })
  check(
    '尾声：上报 2 号阵营被受理（用于验证换席复位）',
    alignmentReport.kind === 'Accepted',
    alignmentReport.raw,
  )

  await cardOf(clockmakerSeat).click()
  const alignmentLabel = storyteller.page.locator(
    '[data-testid="seat-console"] .report .dimensions label',
    { hasText: '阵营' },
  )
  const alignmentCheckbox = alignmentLabel.locator('input[type=checkbox]')
  const alignmentValue = await alignmentLabel.locator('select').inputValue()
  check(
    'H-1 回归：换席后上报表单整表复位（勾选清空、取值回到「未选择」）',
    alignmentValue === '' && !(await alignmentCheckbox.isChecked()),
    `阵营 select="${alignmentValue}"，勾选=${await alignmentCheckbox.isChecked()}`,
  )

  await alignmentCheckbox.check()
  // 先补上原因，确保走到"勾选维度但没选取值"这条分支，而不是先被"原因必填"拦下。
  await storyteller.page
    .getByPlaceholder('变化原因（必填，会随事件流记录）')
    .fill('批次取证：验证「勾选但没选取值」被本地拒绝')
  const guardOutcome = await runCommand(storyteller.page, '缺取值上报', () =>
    storyteller.page.getByRole('button', { name: '上报', exact: true }).click(),
  )
  check(
    'M-2 回归：勾选维度但没选取值 → 本地拒绝、不发命令（不再"受理但静默丢维度"）',
    guardOutcome.kind === 'Rejected'
      && guardOutcome.raw.includes('必须先选一个取值')
      && !guardOutcome.raw.includes('序号'),
    guardOutcome.raw,
  )

  // —— 第 11 步（涡流票行 1 / 4）：涡流存活时的镇民信息能力结算 ——
  // 快档没有第三夜依赖块：先按同一条角色上报把诺-达鲺换成涡流（常驻中毒随之解除），再开第 3 夜；
  // 取证档复用第三夜依赖块已经换好的 3 号（涡流）——先把第三夜收尾，再开第 4 夜。
  // 两档落在同一个场景：涡流存活 + 健康筑梦师结算信息能力 → 能力仍「正常生效」、失效账本落 Vortox。
  if (!runner.begin('vortox')) return

  if (config.slowPacer) {
    const nightThreeClose = await finishNightQuickly(storyteller.page, '第三夜')
    check(
      '涡流票：第三夜收尾（依赖深度块留下的空槽位走完）',
      nightThreeClose.completed === true,
      nightThreeClose.note ?? `强推 ${nightThreeClose.forced} 步`,
    )
  } else {
    const fastSwap = await reportSeatState(storyteller.page, {
      seat: demonSeat,
      dimensionLabel: '角色',
      value: 'vortox',
      reason: '批次取证（快档）：诺-达鲺换成涡流，供涡流干扰结算场景',
    })
    check('涡流票：快档先把 3 号换成涡流（中毒来源失去能力）', fastSwap.kind === 'Accepted', fastSwap.raw)
  }

  // 场景前提（两档同判）：3 号牌面角色确实已是涡流，而不是只拿到一条受理回执。
  const vortoxSeatCard = await waitForLocatorContains(cardOf(demonSeat), '涡流', 15_000)
  check(
    `涡流票：${demonSeat} 号牌面角色已是涡流（场景前提成立）`,
    vortoxSeatCard.includes('涡流'),
    vortoxSeatCard.replace(/\s+/g, ' ').slice(0, 120),
  )

  const vortoxNightNumber = config.slowPacer ? 4 : 3
  const vortoxNight = await runCommand(storyteller.page, `开夜${vortoxNightNumber}`, async () => {
    await storyteller.page
      .locator('section', { hasText: '兜底与推进' })
      .locator('input[type=number]')
      .fill(String(vortoxNightNumber))
    await storyteller.page.getByRole('button', { name: /开夜/ }).click()
  })
  check(
    `涡流票：第 ${vortoxNightNumber} 夜开夜被受理（涡流在场）`,
    vortoxNight.kind === 'Accepted',
    vortoxNight.raw,
  )

  // 夜晚顺序表里涡流（恶魔击杀）在筑梦师之前：先把击杀槽代填到已死的 1 号（不产生新死亡），
  // 再把节奏交给筑梦师槽——涡流在场时镇民的信息类能力照常「有请求、能结算」，只是信息必假。
  const vortoxKillAdvance = await advanceSlotsUntil(
    storyteller.page,
    async () => (await demonRequestPanel.getAttribute('data-request-state').catch(() => null)) === 'pending',
    '涡流票推进到涡流击杀槽',
  )
  if (vortoxKillAdvance.blockedBy !== undefined) {
    throw new Error(`涡流票推进受阻：${vortoxKillAdvance.blockedBy}（已强推 ${vortoxKillAdvance.steps} 步）`)
  }

  const vortoxKillPending = await waitForAttribute(demonRequestPanel, 'data-request-state', 'pending', 180_000)
  check('涡流票：3 号（涡流）的击杀槽照常发起', vortoxKillPending === 'pending', `data-request-state=${vortoxKillPending}`)
  const vortoxProxy = await proxyFillPending(
    storyteller.page,
    `seat:${clockmakerSeat}`,
    '批次取证：涡流击杀代填到已死的 1 号（不产生新死亡）',
  )
  check('涡流票：击杀代填被受理（目标已死，不产生新死亡）', vortoxProxy.kind === 'Accepted', vortoxProxy.raw)

  const vortoxDreamerPending = await waitForAttribute(dreamerRequestPanel, 'data-request-state', 'pending', 180_000)
  check(
    '涡流票：2 号（筑梦师）收到定向请求（涡流在场）',
    vortoxDreamerPending === 'pending',
    `data-request-state=${vortoxDreamerPending}`,
  )
  await dreamerPlayer.page
    .locator('[data-testid="player-request-options"] label', { hasText: `${demonSeat} 号玩家` })
    .locator('input[type=radio]')
    .check()
  await dreamerPlayer.page.locator('[data-testid="player-submit"]').click()

  const vortoxDecision = await waitForDecision(
    storyteller.page,
    (text) => text.includes('筑梦师') && text.includes('涡流在场'),
    60_000,
  )
  check(
    '行 1：筑梦师裁定点写明涡流在场、信息必须为假（真角色不得出现）',
    vortoxDecision.includes('涡流在场')
      && vortoxDecision.includes('必须为假')
      && vortoxDecision.includes('真角色不得出现'),
    vortoxDecision.slice(0, 240),
  )
  await setDataDrawer(storyteller.page, false)
  await screenshot(storyteller.page, '35-vortox-dreamer-decision')

  const vortoxOutcome = await settleFreeDecision(storyteller.page, VORTOX_INFO)
  check('行 1：涡流场景的信息内容由说书人裁定并受理', vortoxOutcome.kind === 'Accepted', vortoxOutcome.raw)
  check(
    '行 1：能力仍「正常生效」——信息送达筑梦师本人',
    await waitForPanelContains(dreamerPlayer.page, '我收到的信息', VORTOX_INFO, 30_000),
  )
  check(
    '行 1：玩家端保留「信息可能是错的」提示（平台不判真假，D-0002）',
    (await dreamerPlayer.page.getByText('信息可能是错的').count()) > 0,
  )

  // 说书人视角（行 1）：最近一次结算 = 正常生效 + 原因：涡流；失效账本落 dreamer / 涡流 一行。
  const vortoxLedgerVisible = await waitForPanelContains(storyteller.page, '账本与结算结论', '涡流', 15_000)
  const vortoxLedger = await panelText(storyteller.page, '账本与结算结论')
  const vortoxLedgerLines = vortoxLedger
    .split('\n')
    .map((line) => line.replace(/\s+/g, ' ').trim())
    .filter((line) => line.length > 0)
  // 最近一次结算块的边界：面板里「能力使用账本」之前的行才是本次结算（flex 里每个 span 在 innerText 各占一行）。
  const vortoxResolutionEnd = vortoxLedgerLines.findIndex((line) => line.includes('能力使用账本'))
  const vortoxResolutionLines =
    vortoxResolutionEnd === -1 ? vortoxLedgerLines : vortoxLedgerLines.slice(0, vortoxResolutionEnd)
  // 标签与原因各占一行、按整行等值判：'未正常生效' 含子串 '正常生效'，用 includes 会被顶绿（对抗复核 H1）。
  check(
    '行 1：最近一次结算 = 正常生效 + 原因：涡流（能力照常生效，干扰另记）',
    vortoxLedgerVisible
      && vortoxResolutionLines.includes('正常生效')
      && vortoxResolutionLines.includes('原因：涡流')
      && vortoxResolutionLines.some((line) => line.includes('dreamer')),
    vortoxResolutionLines.join(' / ').slice(0, 280),
  )
  const vortoxMalfunctionRow = vortoxMalfunctionRowOf(vortoxLedger)
  check(
    '行 1：失效账本落 dreamer / 涡流 一行（说书人专属）',
    vortoxMalfunctionRow !== undefined,
    vortoxMalfunctionRow ?? vortoxLedger.replace(/\s+/g, ' ').slice(0, 280),
  )
  await screenshot(storyteller.page, '36-storyteller-vortox-ledger')
  await screenshot(dreamerPlayer.page, '37-player-vortox-info')

  // 行 4 反方向：失效归因与涡流场景的信息都不许出现在除说书人以外的任何视图。
  const vortoxLeakTokens = ['原因：涡流', '未正常生效', '失效账本', 'malfunctions', '涡流在场']
  const vortoxLeaks = []
  for (const [seat, client] of players) {
    const shellText = (await client.page.locator('.shell').innerText()).replace(/\s+/g, ' ')
    const hits = vortoxLeakTokens.filter((token) => shellText.includes(token))
    if (hits.length > 0) {
      vortoxLeaks.push(`${seat} 号文案:${hits.join('/')}`)
    }

    if (seat !== dreamerSeat && (await infoText(client.page)).includes(VORTOX_INFO)) {
      vortoxLeaks.push(`${seat} 号收到涡流场景信息`)
    }
  }
  check(
    `行 4：${options.seatCount} 席玩家视图都看不到失效归因，且只有 ${dreamerSeat} 号收到该信息`,
    vortoxLeaks.length === 0,
    vortoxLeaks.join('，') || '已逐席扫描页面文本与信息列表',
  )
  const vortoxWitnessSeat =
    [...players.keys()].find(
      (seat) => seat !== dreamerSeat && seat !== demonSeat && seat !== clockmakerSeat,
    ) ?? options.seatCount
  await screenshot(players.get(vortoxWitnessSeat).page, '38-unrelated-player-clean')

  const vortoxNightClose = await finishNightQuickly(storyteller.page, `第 ${vortoxNightNumber} 夜`)
  check(
    `涡流票：第 ${vortoxNightNumber} 夜剩余槽位走完（不给后续重建 / 重连留半个夜）`,
    vortoxNightClose.completed === true,
    vortoxNightClose.natural ? '自然窗口内走完' : `强推 ${vortoxNightClose.forced} 步`,
  )

  // —— 第 12 步：恢复与重建（重建票行 1–3 / 健康票行 1–5）——
  // 真宿主 + 真 SQLite + 真浏览器：先判干净重建，再故意制造"内存账与事件流分叉"，
  // 最后停宿主、改库、重启，判降级位的置位 / 保持 / 清除与玩家侧不下发。
  if (!runner.begin('rebuild')) return
  const healthBanner = storyteller.page.locator('[data-testid="room-health-degraded"]')
  check('健康票行 1：正常房间不显示降级位', (await healthBanner.count()) === 0)

  // 行 1（重建票）：干净流重建 → 内存 / 快照 / 状态账三项都等价。
  const cleanRebuild = await runCommand(storyteller.page, '干净重建', () =>
    storyteller.page.getByRole('button', { name: '重建房间' }).click(),
  )
  const rebuildReport = storyteller.page.locator('[data-testid="rebuild-report"]')
  check(
    '重建票行 1：干净流重建 → 三项等价（内存 / 快照 / 状态账）',
    cleanRebuild.kind === 'Accepted'
      && (await rebuildReport.getAttribute('data-machine-equivalent')) === 'true'
      && (await rebuildReport.getAttribute('data-snapshot-equivalent')) === 'true'
      && (await rebuildReport.getAttribute('data-ledger-equivalent')) === 'true',
    `回执=${cleanRebuild.kind} ${cleanRebuild.raw}`.replace(/\s+/g, ' ').slice(0, 240),
  )
  await screenshot(storyteller.page, '24-rebuild-clean')

  // 涡流票行 5 的基线：宿主重启前记下失效账本行与最近一次结算序号，重启后逐字 / 逐号对比（对抗复核 H3）。
  const vortoxLedgerBeforeRestart = await panelText(storyteller.page, '账本与结算结论')
  const vortoxRowBeforeRestart = vortoxMalfunctionRowOf(vortoxLedgerBeforeRestart)
  const vortoxSequenceBeforeRestart = resolutionSequenceOf(vortoxLedgerBeforeRestart)

  // 行 2（重建票）：只改事件流里的原因文本 → 步骤机不受影响，状态账必须报"不一致"，并由重建修回。
  const ledgerMarker = `batch-ledger-dirty-${Date.now()}`
  tamperLatestSeatChangeReason(ledgerMarker)
  const dirtyRebuild = await runCommand(storyteller.page, '分叉重建', () =>
    storyteller.page.getByRole('button', { name: '重建房间' }).click(),
  )
  check(
    '重建票行 2：内存账与事件流分叉 → 状态账不一致（步骤机仍一致）',
    dirtyRebuild.kind === 'Accepted'
      && (await rebuildReport.getAttribute('data-machine-equivalent')) === 'true'
      && (await rebuildReport.getAttribute('data-ledger-equivalent')) === 'false',
    dirtyRebuild.raw.replace(/\s+/g, ' ').slice(0, 240),
  )
  check(
    '重建票行 2：重建把状态账修回与事件流一致',
    await waitForPanelContains(storyteller.page, '状态账', ledgerMarker, 15_000),
  )
  await screenshot(storyteller.page, '25-rebuild-ledger-repaired')

  // 行 2（健康票）：停宿主 → 弄坏首发事件载荷 → 重启：说书人刷新后必须看见降级与原因。
  const consoleErrorsBeforeRestart = consoleErrors.length
  await stopServer(server)
  const savedPayload = corruptFirstEventPayload()
  server = await startServer()
  // 宿主回来之前在途的重连请求会撞上 Vite 代理（宿主不可用 → 代理回 500）：这是装置的预期噪音。
  // 取一个窄窗口基线把它圈进来，窗口外的任何错误仍然算失败。
  // 重启噪音窗口：迭代档（快节拍）800ms 已覆盖"在途重连撞代理"的窗口；取证档保留 1500ms。
  await sleep(config.slowPacer ? 1500 : 800)
  const consoleErrorsAfterRestartWindow = consoleErrors.length
  await storyteller.page.reload()
  await healthBanner.waitFor({ state: 'visible', timeout: 30_000 })
  const degradedText = (await healthBanner.innerText()).replace(/\s+/g, ' ')
  check(
    '健康票行 2：恢复失败 → 视图显示降级 + 原因（"数据丢了"看得见）',
    degradedText.includes('恢复失败') && degradedText.includes('事件载荷损坏'),
    degradedText.slice(0, 240),
  )
  await screenshot(storyteller.page, '26-room-health-degraded')

  // 行 5（健康票）强化：降级窗口里真机重载一个玩家页——加入必须**显式失败**且文案中性
  // （不出现健康位 / 数据丢失 / 事件载荷字样），这才同时证明"服务端不下发"与"前端不外泄"。
  const probeSeat = [...players.keys()][0]
  const probePlayer = players.get(probeSeat)
  await probePlayer.page.reload()
  const degradedPlayerText = await waitForLocatorContains(probePlayer.page.locator('.shell'), '加入暂时失败', 20_000)
  const degradedPlayerLeaks = ['降级', '健康位', '数据丢失', '事件载荷'].filter((word) => degradedPlayerText.includes(word))
  check(
    `健康票行 5：降级窗口玩家 ${probeSeat} 号加入显式失败、文案中性（无健康位泄露）`,
    degradedPlayerText.includes('加入暂时失败') && degradedPlayerLeaks.length === 0,
    degradedPlayerText.replace(/\s+/g, ' ').slice(0, 200),
  )

  // 行 3（重建票）：损坏流重建 → 显式失败，不返回"等价"假结论。
  const failedRebuild = await runCommand(storyteller.page, '损坏流重建', () =>
    storyteller.page.getByRole('button', { name: '重建房间' }).click(),
  )
  check(
    '重建票行 3：损坏流重建 → 显式失败、不返回等价结论',
    failedRebuild.kind === 'Failed' && (await rebuildReport.count()) === 0,
    failedRebuild.raw.replace(/\s+/g, ' ').slice(0, 240),
  )

  // 行 4（健康票）：重建仍失败 → 降级保持、原因更新为"重建失败"（服务端推送更新，不靠手点刷新）。
  // 横幅首行是中性文案（"已降级"），失败类别只由 reason 表达——不能写死"恢复失败"（对抗复核 2026-10-02）。
  const stillDegradedText = await waitForLocatorContains(healthBanner, '重建失败', 15_000)
  check(
    '健康票行 4：重建仍失败 → 降级保持 + 原因更新为重建失败（推送到达）',
    stillDegradedText.includes('已降级')
      && stillDegradedText.includes('重建失败')
      && !stillDegradedText.includes('恢复失败'),
    stillDegradedText.slice(0, 240),
  )
  await screenshot(storyteller.page, '27-room-health-rebuild-failed')

  // 行 3（健康票）：事件流修复 → 显式重建成功 → 降级清除（房间重新可恢复）。
  restoreEventPayload(savedPayload)
  const recovered = await runCommand(storyteller.page, '修复重建', () =>
    storyteller.page.getByRole('button', { name: '重建房间' }).click(),
  )
  const healthCleared = await waitForGone(healthBanner, 15_000)
  check(
    '健康票行 3：显式重建成功 → 降级标记清除',
    recovered.kind === 'Accepted' && healthCleared,
    `回执=${recovered.kind}；降级位清除=${healthCleared}`,
  )
  await screenshot(storyteller.page, '28-room-health-cleared')

  // 修复重建那一刻，报告照实说"与重建前不一致"（内存已被清空、账为空）——这个最有误导性的组合
  // 必须被装置断言住，否则"成功重建"与"状态账 不一致"同屏出现会落在断言之外（对抗复核 2026-10-02）。
  check(
    '健康票行 3：修复重建的报告照实说"重建前内存 / 状态账不一致、快照一致"',
    (await rebuildReport.getAttribute('data-machine-equivalent')) === 'false'
      && (await rebuildReport.getAttribute('data-snapshot-equivalent')) === 'true'
      && (await rebuildReport.getAttribute('data-ledger-equivalent')) === 'false',
    recovered.raw.replace(/\s+/g, ' ').slice(0, 240),
  )

  // 涡流票行 5：宿主重启 + 修复重建之后，失效账本行与最近一次结算序号都必须原样恢复（不重算）。
  await waitForPanelContains(storyteller.page, '账本与结算结论', '涡流', 15_000)
  const ledgerAfterRecovery = await panelText(storyteller.page, '账本与结算结论')
  const recoveredMalfunctionRow = vortoxMalfunctionRowOf(ledgerAfterRecovery)
  const recoveredResolutionSequence = resolutionSequenceOf(ledgerAfterRecovery)
  check(
    '涡流票行 5：宿主重启 + 修复重建后，失效账本仍是同一行、最近结算序号不回退（随事件流恢复，不重算）',
    vortoxRowBeforeRestart !== undefined
      && recoveredMalfunctionRow === vortoxRowBeforeRestart
      && vortoxSequenceBeforeRestart !== null
      && recoveredResolutionSequence === vortoxSequenceBeforeRestart,
    `重启前=${vortoxRowBeforeRestart ?? '（无行）'}@${vortoxSequenceBeforeRestart ?? '?'}`
      + `；重启后=${recoveredMalfunctionRow ?? '（无行）'}@${recoveredResolutionSequence ?? '?'}`,
  )

  // 注记票行 5：宿主真实重启 + 修复重建之后，注记仍在牌面上（按事件流恢复，不靠内存；D-0019）。
  const noteAfterRestart = cardOf(noteSeat).locator('[data-testid="seat-notes"] [data-note-id]').first()
  await noteAfterRestart.waitFor({ state: 'visible', timeout: 20_000 })
  const noteTitleAfterRestart = await noteAfterRestart.getAttribute('title')
  check(
    '注记行 5：宿主重启 + 修复重建后注记仍在（按事件流恢复，不靠内存）',
    noteTitleAfterRestart === noteRestartText,
    `title=${noteTitleAfterRestart ?? '（无）'}`,
  )
  await screenshot(storyteller.page, '42-grimoire-annotation-after-restart')

  // 行 5（健康票）另一面：房间恢复后同一玩家页重载能正常加入，且仍然看不到任何健康位。
  const joinsBeforeReload = seatJoinLogLines(probeSeat).length
  await probePlayer.page.reload()
  await probePlayer.page
    .locator('[data-testid="player-seat"]')
    .waitFor({ state: 'visible', timeout: 20_000 })
    .catch(() => {})
  const reloadJoin = await waitForNextSeatJoin(probeSeat, joinsBeforeReload, 20_000)
  const reloadSnapshot = joinLogNumber(reloadJoin, '快照序号')
  const recoveredPlayerText = await probePlayer.page.locator('.shell').innerText()
  const recoveredPlayerLeaks = ['降级', '健康位', '数据丢失', '事件载荷'].filter((word) =>
    recoveredPlayerText.includes(word),
  )
  check(
    `健康票行 5：修复后玩家 ${probeSeat} 号能正常重连、仍看不到健康位`,
    (await probePlayer.page.locator('[data-testid="player-seat"]').count()) > 0
      && recoveredPlayerLeaks.length === 0,
    recoveredPlayerText.replace(/\s+/g, ' ').slice(0, 200),
  )

  if (!runner.begin('reconnect')) return

  // —— 重连票行 1：隐藏事件不是"缺口"，快照序号才是权威 watermark ——
  // 旧判据要求"事件条数 = 快照序号 - 本地已知"，在白名单投影下必然误报（E7 实测：应补 198 / 实际 4），
  // 并把 watermark 卡在 0。这里用宿主日志核对每次加入时客户端带回来的"本地已知"。
  const reloadDiagnostics = await readPlayerDiagnostics(probePlayer.page)
  check(
    `重连票行 1：玩家 ${probeSeat} 号全新加入（窗口内多数事件对该席不可见）无"应补 N 实际 M"假告警`,
    reloadJoin !== null && reloadSnapshot !== null && reloadSnapshot > 0
      && reconnectDiagnosticIn(reloadDiagnostics).length === 0,
    `${reloadJoin ?? '未等到加入日志'}；诊断=${reloadDiagnostics || '（无）'}`,
  )

  // 票面"可见事件与信息结果仍按快照正确呈现"：重连后自己的信息结果必须在列。
  const reconnectedInfoText = await infoText(probePlayer.page)
  check(
    `重连票行 1：重连后信息结果仍按快照呈现（${probeSeat} 号的钟表匠信息在列）`,
    reconnectedInfoText.includes(CLOCKMAKER_INFO),
    reconnectedInfoText.replace(/\s+/g, ' ').slice(0, 200),
  )
  // R-0022 行 4：公开生死面随快照恢复、不回退——处决身亡的 1 号仍在牌面上；
  // 夜里上报、尚未到黎明的 3 号仍然显示存活（未公告不算数）。
  const reconnectedLife = await readPlayerLifeOf(probePlayer.page, clockmakerSeat)
  const pendingLife = await readPlayerLifeOf(probePlayer.page, demonSeat)
  check(
    `重连票行 1（R-0022 行 4）：公开生死面随快照恢复且不回退（${clockmakerSeat} 号死亡、${demonSeat} 号夜死未公告）`,
    reconnectedLife === 'Dead' && pendingLife === 'Alive',
    `处决席=${reconnectedLife ?? '缺失'}；夜死席=${pendingLife ?? '缺失'}`,
  )
  await screenshot(probePlayer.page, '29-player-reconnect-no-gap')

  // 补齐一次：本地已知 0 → 快照序号，证明新 watermark 真的落下去（卡 0 的话这里会再次带回 0）。
  const joinsBeforeFirstResync = seatJoinLogLines(probeSeat).length
  await probePlayer.page.getByRole('button', { name: '补齐' }).click()
  const firstResyncJoin = await waitForNextSeatJoin(probeSeat, joinsBeforeFirstResync, 20_000)
  const firstResyncKnown = joinLogNumber(firstResyncJoin, '本地已知')
  const firstResyncSnapshot = joinLogNumber(firstResyncJoin, '快照序号')
  check(
    `重连票行 1：补齐把快照序号带回服务端（本地已知 ${firstResyncKnown}，快照 ${firstResyncSnapshot}）`,
    reloadSnapshot !== null && firstResyncKnown === reloadSnapshot && firstResyncSnapshot !== null,
    firstResyncJoin ?? '未等到补齐后的加入日志',
  )

  // 非零 watermark + 掉线窗口里的隐藏事件：断开 → 说书人上报其他席位（玩家白名单外）→ 重新加入。
  await probePlayer.page.getByRole('button', { name: '断开' }).click()
  await probePlayer.page.getByRole('button', { name: '加入' }).waitFor({ state: 'visible', timeout: 10_000 })
  const hiddenSeatChange = await reportSeatState(storyteller.page, {
    seat: dreamerSeat,
    dimensionLabel: '醉酒',
    value: 'Drunk',
    reason: '批次取证：重连窗口内的隐藏状态变化（其他席位，玩家白名单外）',
  })
  check(
    '重连票行 1：掉线窗口内制造隐藏事件（其他席位的状态变化）被受理',
    hiddenSeatChange.kind === 'Accepted',
    hiddenSeatChange.raw,
  )

  const joinsBeforeRejoin = seatJoinLogLines(probeSeat).length
  await probePlayer.page.getByRole('button', { name: '加入' }).click()
  await probePlayer.page
    .locator('[data-testid="player-seat"]')
    .waitFor({ state: 'visible', timeout: 20_000 })
    .catch(() => {})
  const rejoinLog = await waitForNextSeatJoin(probeSeat, joinsBeforeRejoin, 20_000)
  const rejoinKnown = joinLogNumber(rejoinLog, '本地已知')
  const rejoinSnapshot = joinLogNumber(rejoinLog, '快照序号')
  const rejoinDiagnostics = await readPlayerDiagnostics(probePlayer.page)
  check(
    `重连票行 1：本地已知 ${rejoinKnown} 跨掉线窗口重连 → 隐藏事件不报缺口、快照前进到 ${rejoinSnapshot}`,
    rejoinLog !== null
      && firstResyncSnapshot !== null
      && rejoinKnown === firstResyncSnapshot
      && rejoinSnapshot !== null
      && rejoinSnapshot > firstResyncSnapshot
      && reconnectDiagnosticIn(rejoinDiagnostics).length === 0,
    `${rejoinLog ?? '未等到重连日志'}；诊断=${rejoinDiagnostics || '（无）'}`,
  )

  // 再补齐：确认这轮重连后的 watermark 停在最新快照，而不是又卡回旧值。
  const joinsBeforeSecondResync = seatJoinLogLines(probeSeat).length
  await probePlayer.page.getByRole('button', { name: '补齐' }).click()
  const secondResyncJoin = await waitForNextSeatJoin(probeSeat, joinsBeforeSecondResync, 20_000)
  const secondResyncKnown = joinLogNumber(secondResyncJoin, '本地已知')
  check(
    `重连票行 1：重连后的 watermark 已到快照 ${rejoinSnapshot}（再次补齐带回 ${secondResyncKnown}）`,
    rejoinSnapshot !== null && secondResyncKnown === rejoinSnapshot,
    secondResyncJoin ?? '未等到再次补齐的加入日志',
  )

  // 行 5（健康票）：玩家侧没有健康位——既无锚点 / 文案，重连包契约里也没有该字段（门禁 + 集成测试）。
  const playerLeaks = []
  for (const [seat, client] of players) {
    const shellText = await client.page.locator('.shell').innerText()
    const hit = ['降级', '健康位', '数据丢失', '房间数据'].filter((word) => shellText.includes(word))
    if (hit.length > 0) {
      playerLeaks.push(`${seat} 号文案:${hit.join('/')}`)
    }

    if ((await client.page.locator('[data-testid="room-health-degraded"]').count()) > 0) {
      playerLeaks.push(`${seat} 号锚点`)
    }
  }
  check(
    '健康票行 5：玩家端没有降级位文案 / 锚点（不下发，不靠前端不显示）',
    playerLeaks.length === 0,
    playerLeaks.join('，') || `${players.size} 席已扫描`,
  )

  // —— 日志面：故意损坏必须在日志里有可定位记录（分支 / 状态 / 原因），而非静默 ——
  const serverLogText = serverLog.join('')
  check(
    '证据：故意损坏的恢复失败在宿主日志里有记录',
    serverLogText.includes('恢复失败') && serverLogText.includes('事件载荷损坏'),
    serverLogText.slice(-400),
  )
  check(
    '证据：重建失败在宿主日志里带原因与健康位上下文',
    serverLogText.includes('重建失败') && serverLogText.includes('健康位降级'),
    serverLogText.slice(-400),
  )

  if (!runner.begin('final')) return

  const serverCrash = /Unhandled exception|Application is shutting down/i.test(serverLog.join(''))
  check('宿主日志没有未处理异常', !serverCrash, serverLog.join('').slice(-400))

  // 故意杀宿主必然产生两类**装置噪音**：SignalR 断线错误（1006）与宿主不可用时 Vite 代理对 /hub 回的 5xx。
  // 判据 = 正常流程零错误 + 重启窗口内除这两类外零错误 + 窗口外零错误——不能因为预期噪音把整条检查关掉。
  // 代理错误码只认 500 / 502 这一对：vite 8 在新宿主不可达时回 502（vite 6 时代是 500），
  // 两者是同一件事（代理转不出去），所以并列容忍；其余 4xx / 5xx 仍然算非预期。
  const restartNoise = /Connection disconnected with error|WebSocket closed with status code: 1006|Failed to load resource: the server responded with a status of 50[02]|Failed to complete negotiation|Server returned an error on close/
  const noiseInWindow = consoleErrors.slice(consoleErrorsBeforeRestart, consoleErrorsAfterRestartWindow)
  const errorsOutsideWindow = consoleErrors.slice(consoleErrorsAfterRestartWindow)
  const unexpectedInWindow = noiseInWindow.filter((message) => !restartNoise.test(message))
  check(
    '所有客户端页面没有控制台错误（正常流程零错误；重启窗口只容忍断线与代理 500/502）',
    consoleErrorsBeforeRestart === 0 && unexpectedInWindow.length === 0 && errorsOutsideWindow.length === 0,
    `正常流程=${consoleErrorsBeforeRestart}；重启窗口噪音=${noiseInWindow.length}（非预期 ${unexpectedInWindow.length}）；窗口外=${errorsOutsideWindow.length}`
      + (unexpectedInWindow.length > 0 ? ` | ${unexpectedInWindow.slice(0, 2).join(' | ')}` : '')
      + (errorsOutsideWindow.length > 0 ? ` | 窗口外: ${errorsOutsideWindow.slice(0, 2).join(' | ')}` : ''),
  )

  const expectedShots = [
    '01-storyteller-joined',
    '02-pre-night',
    '03-night-started',
    '04-clockmaker-decision',
    '05-player-clockmaker-info',
    '05b-resync-window-info-kept',
    '06-storyteller-dreamer-digest',
    '07-player-dreamer-request',
    '08-unrelated-player-idle',
    '09-player-dreamer-info',
    '10-storyteller-resolutions',
    '11-night2-demon-request',
    '12-digest-poison-released',
    '13-digest-request-voided',
    '14-player-phase-night-two',
    '15-player-proxy-filled',
    '16-player-forced-void',
    '17-grimoire-assigned',
    '18-grimoire-seat-console',
    '19-grimoire-current-slot',
    '20-grimoire-death',
    '21-grimoire-revive',
    '22-grimoire-data-drawer',
    '23-grimoire-narrow',
    '24-rebuild-clean',
    '25-rebuild-ledger-repaired',
    '26-room-health-degraded',
    '27-room-health-rebuild-failed',
    '28-room-health-cleared',
    '29-player-reconnect-no-gap',
    '30-day-open',
    '31-day-nomination',
    '32-day-counted',
    '33-day-executed',
    '34-player-self-dead',
    '35-vortox-dreamer-decision',
    '36-storyteller-vortox-ledger',
    '37-player-vortox-info',
    '38-unrelated-player-clean',
  ]
  const missingShots = expectedShots.filter((name) => {
    const file = path.join(screenshotsDir, `${name}.png`)
    return !existsSync(file) || statSync(file).mtimeMs < runStartedAt
  })
  check(
    `证据截图都已落盘（${expectedShots.length} 张，且都是本次运行写入）`,
    missingShots.length === 0,
    missingShots.join(',') || screenshotsDir,
    { screenshots: true },
  )
}

/** 起一个独立浏览器上下文（= 一台设备）：页面级 console 错误统一收集。 */
async function newClient(browser, viewport, consoleErrors) {
  const context = await browser.newContext({ viewport })
  const page = await context.newPage()
  page.on('console', (message) => {
    if (message.type() === 'error') {
      // 带上来源 URL：区分"我们的链路报错"和"宿主不可用时 Vite 代理回的 500"（诊断与窗口过滤都要用）。
      const location = message.location()
      consoleErrors.push(location?.url ? `${message.text()} @${location.url}` : message.text())
    }
  })
  page.on('pageerror', (error) => consoleErrors.push(error.message))
  return { context, page }
}

/** 玩家信息面板的可见文本。 */
async function infoText(page) {
  const info = page.locator('[data-testid="player-information"]')
  if ((await info.count()) === 0) {
    return ''
  }

  return await info.innerText()
}

/** 玩家信息面板的信息计数（data-information-count）；面板不在时返回 -1。 */
async function informationCount(page) {
  const panel = page.locator('[data-testid="player-information"]')
  if ((await panel.count()) === 0) {
    return -1
  }

  const raw = await panel.getAttribute('data-information-count')
  return raw === null ? -1 : Number(raw)
}

/**
 * 扣住下一条 JoinSeat 响应帧（票据 player-information-resync-race 行 1 的并发窗口取证）。
 *
 * 平时全透传；`arm()` 后下一条含 credential / bundle 的服务端帧被扣住，`waitForHeld()` 等它到达，
 * `release()` 放行——窗口内到达的定向推送正好落在"快照已生成、客户端尚未应用"之间。
 * 必须在页面建立连接**之前**安装：Playwright 只拦截安装之后新建的 WebSocket。
 */
async function installJoinResponseHold(page) {
  const state = { armed: false, held: null }
  let held = new Promise((resolve) => {
    state.notifyHeld = resolve
  })

  await page.routeWebSocket(/\/hub/, (ws) => {
    const server = ws.connectToServer()
    ws.onMessage((message) => server.send(message))
    server.onMessage((message) => {
      const text = typeof message === 'string' ? message : message.toString('utf8')
      if (state.armed && text.includes('"credential"') && text.includes('"bundle"')) {
        state.armed = false
        state.held = { route: ws, message }
        state.notifyHeld()
        return
      }

      ws.send(message)
    })
  })

  return {
    arm: () => {
      state.armed = true
      held = new Promise((resolve) => {
        state.notifyHeld = resolve
      })
    },
    waitForHeld: async (timeoutMs) => {
      const timeout = new Promise((resolve) => setTimeout(resolve, timeoutMs))
      await Promise.race([held, timeout])
      return state.held !== null
    },
    release: () => {
      if (state.held !== null) {
        state.held.route.send(state.held.message)
        state.held = null
      }
    },
  }
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

/** 展开 / 收拢"数据与审计"下钻面板（幂等）；没有该面板（如玩家端）时什么都不做。 */
async function setDataDrawer(page, open) {
  const toggle = page.locator('[data-testid="data-drawer-toggle"]')
  if ((await toggle.count()) === 0) {
    return
  }

  if (((await toggle.getAttribute('aria-expanded')) === 'true') !== open) {
    await toggle.click()
  }
}

/** 某个 section.panel（按标题定位）的可见文本；找不到返回空串。下钻面板会自动展开。 */
async function panelText(page, heading) {
  await setDataDrawer(page, true)
  // 说书人页：只在「数据与审计」容器内找表格面板——主区还有席位操作台等同为 section.panel 的组件，
  // 用全文 hasText 会先命中它们（实测：操作台提示里出现"状态账"三个字，旧断言落到错误的面板）。
  const drawerBody = page.locator('[data-testid="data-drawer-body"]')
  const scope = (await drawerBody.count()) > 0 ? drawerBody : page
  const panel = scope.locator('section.panel', { hasText: heading })
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

    await sleep(100)
  }

  return (await panelText(page, heading)).includes(needle)
}

/** 当前步骤面板是否显示"本计划已走完"。 */
async function planCompleted(page) {
  return (await panelText(page, '当前步骤')).includes('本计划已走完')
}

/** 当前槽位是否有挂起请求：有就不能强推（强推会把它按 Override 了结）。 */
async function pendingRequestVisible(page) {
  const pending = page.locator('[data-testid="console-pending"]')
  return (await pending.count()) > 0 && (await pending.first().isVisible().catch(() => false))
}

/** 说书人兜底：强推当前槽位（D-0014）；每次带原因（会随事件流记录）。 */
async function forceAdvanceSlot(page, label) {
  const box = page.locator('section', { hasText: '兜底与推进' })
  await box.locator('input[placeholder^="原因"]').fill(`批次取证：${label}`)
  return runCommand(page, label, () => box.getByRole('button', { name: '强推当前槽位' }).click())
}

/**
 * 用"强推当前槽位"把目标之前的空槽位推走，直到条件满足（例如目标角色的请求出现）。
 *
 * 依据：槽位的"配额自行推进 / 空槽位照样走配额 / 秒回不提前推进"由真宿主集成测试
 * PacingIsolationTests 覆盖（其用例注释里的行 13/14/18）；真机这里要证的是"推进一步 → 视图/推送更新"；
 * 集成测试 StepDigestHostTests 也是"给足配额 + ForceAdvance 精确推进"的同款做法。
 * 一旦看到**任何**挂起请求就停手——不强推，避免把随后要用到的请求越权了结。
 */
async function advanceSlotsUntil(page, predicate, label, maxSteps = 30) {
  for (let step = 0; step <= maxSteps; step += 1) {
    if (await predicate()) {
      return { reached: true, steps: step }
    }

    if (await pendingRequestVisible(page)) {
      return { reached: false, steps: step, blockedBy: '遇到挂起请求（不强推）' }
    }

    if (await planCompleted(page)) {
      return { reached: false, steps: step, blockedBy: '本计划已走完' }
    }

    const outcome = await forceAdvanceSlot(page, `${label} 第 ${step + 1} 步`)
    if (outcome.kind !== 'Accepted') {
      return { reached: false, steps: step, blockedBy: `强推回执 ${outcome.kind}` }
    }
  }

  return { reached: await predicate(), steps: maxSteps, blockedBy: '达到强推步数上限' }
}

/**
 * 收尾一个夜晚：先给自然推进一个短窗口（保留"槽位确实会按配额自行前进"的真机观察，
 * 完整节奏语义在集成测试），再用强推把剩余空槽位推完——不再白等 N × 配额。
 */
async function finishNightQuickly(page, label) {
  const naturalWindowMs = config.slowPacer ? 2500 : 1500
  const deadline = Date.now() + naturalWindowMs
  while (Date.now() < deadline) {
    if (await planCompleted(page)) {
      return { natural: true, forced: 0, completed: true }
    }

    await sleep(150)
  }

  let forced = 0
  while (forced < 40) {
    if (await planCompleted(page)) {
      return { natural: false, forced, completed: true }
    }

    // 与 advanceSlotsUntil 同一口径：看到挂起请求就停手，绝不越权了结（对抗自检 P2）。
    if (await pendingRequestVisible(page)) {
      return { natural: false, forced, completed: false, note: '仍有挂起请求（不越权强推）' }
    }

    const outcome = await forceAdvanceSlot(page, `${label} 收尾第 ${forced + 1} 步`)
    if (outcome.kind !== 'Accepted') {
      return { natural: false, forced, completed: false, note: outcome.raw }
    }

    forced += 1
  }

  return { natural: false, forced, completed: await planCompleted(page) }
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

/** 等某个定位器的可见文本变成期望值；超时返回最后一次读到的文本。 */
async function waitForText(locator, expected, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  let text = ''
  while (Date.now() < deadline) {
    text = (await locator.innerText().catch(() => '')).trim()
    if (text === expected) {
      return text
    }

    await sleep(100)
  }

  return text
}

/** 等一个定位器从 DOM 消失（服务端推送驱动的清除）；超时返回是否已消失。 */
async function waitForGone(locator, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if ((await locator.count()) === 0) {
      return true
    }

    await sleep(100)
  }

  return (await locator.count()) === 0
}

/** 等一个定位器的可见文本包含目标子串（服务端推送驱动的文案更新）；超时返回最后一次文本。 */
async function waitForLocatorContains(locator, needle, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  let text = ''
  while (Date.now() < deadline) {
    text = (await locator.innerText().catch(() => '')).replace(/\s+/g, ' ')
    if (text.includes(needle)) {
      return text
    }

    await sleep(100)
  }

  return text
}

/** 宿主日志里"玩家已加入"的行（serverLog 是 stdout 块，先拼回文本再按行过滤）。 */
function seatJoinLogLines(seat) {
  return serverLog
    .join('')
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.includes(`玩家已加入：seat=${seat} `))
}

/** 等该席第 `afterCount` 条之后的下一条"玩家已加入"日志（重连的输入在服务端日志里可核对）。 */
async function waitForNextSeatJoin(seat, afterCount, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    const lines = seatJoinLogLines(seat)
    if (lines.length > afterCount) {
      return lines[lines.length - 1]
    }

    await sleep(100)
  }

  return seatJoinLogLines(seat).length > afterCount ? seatJoinLogLines(seat).at(-1) : null
}

/** 从"玩家已加入"日志行取数字字段（快照序号 / 本地已知）；取不到返回 null。 */
function joinLogNumber(line, field) {
  const matched = new RegExp(`${field}=(\\d+)`).exec(line ?? '')
  return matched === null ? null : Number(matched[1])
}

/** 玩家端诊断文本；没有诊断面板（或没有诊断）时返回空串。 */
async function readPlayerDiagnostics(page) {
  const panel = page.locator('[data-testid="player-diagnostics"]')
  if ((await panel.count()) === 0) {
    return ''
  }

  return (await panel.innerText()).replace(/\s+/g, ' ').trim()
}

/** 重连补包坏数据的指纹：applyBundle 的诊断都以"重连补齐"开头（成功提示"重新补齐"不匹配）。 */
function reconnectDiagnosticIn(text) {
  return ['重连补齐'].filter((word) => text.includes(word))
}

/** 读玩家端公开生死面上某席位的对外状态；没有该条目时返回 null（不猜，R-0022）。 */
async function readPlayerLifeOf(page, seat) {
  const entry = page.locator(`[data-testid="player-lives"] li[data-seat="${seat}"]`)
  return (await entry.count()) > 0 ? entry.first().getAttribute('data-life') : null
}

/** 玩家端"上一次请求怎么结束"的说明文本；没有这条说明时返回空串。 */
async function readSettledNote(page) {
  const note = page.locator('[data-testid="player-settled-note"]')
  if ((await note.count()) === 0) {
    return ''
  }

  return (await note.first().innerText()).trim()
}

/** 玩家端"活动快照"：请求区状态 + 了结说明。窗口内它必须保持不变。 */
async function readPlayerActivity(page) {
  const state = await page
    .locator('[data-testid="player-request-panel"]')
    .getAttribute('data-request-state')
  const note = await readSettledNote(page)
  return `${state ?? '（无）'}${note.length > 0 ? ` + 说明:${note}` : ''}`
}

/**
 * 窗口采样：这些与本次推送无关的玩家，在窗口内的**活动快照必须相对基线不变**——
 * 请求区状态与了结说明都不许动，更不许出现新的活动。
 *
 * 判据是"相对基线不变"而不是"必须为空态"：玩家面板会保留**自己**上一次请求是怎么结束的
 * （例如 3 号自己那次被代填），那是他自己的信息，不是这次推送造成的活动（E4 首跑实测踩到）。
 * 单点读取只能证明"那一刻恰好空闲"，证明不了"整个窗口里没有活动"（独立复核 2026-10-02 指出）。
 */
async function sampleUnrelatedIdle(players, seats, label, seconds) {
  // 窗口长度按档位缩放：迭代档 1 秒已覆盖数个槽位；取证档（慢节拍）保留 2 秒完整窗口。
  const windowSeconds = config.slowPacer ? seconds : Math.min(seconds, 1)
  const samples = Math.max(4, Math.round(windowSeconds * 4))
  const baseline = new Map()
  for (const seat of seats) {
    baseline.set(seat, await readPlayerActivity(players.get(seat).page))
  }

  const observed = new Map(seats.map((seat) => [seat, new Set()]))
  for (let sample = 0; sample < samples; sample += 1) {
    for (const seat of seats) {
      observed.get(seat)?.add(await readPlayerActivity(players.get(seat).page))
    }

    await sleep(250)
  }

  for (const seat of seats) {
    const states = observed.get(seat)
    const expected = baseline.get(seat)
    check(
      `行 4：${label}内无关玩家 ${seat} 号活动快照保持不变（${samples} 次采样）`,
      states.size === 1 && states.has(expected),
      `基线=${expected}，观察到：${[...states].join(' | ') || '（无）'}`,
    )
  }
}

/** 说书人代填当前挂起的请求（值必须来自该请求的合法选项集合）。 */
async function proxyFillPending(page, optionValue, note) {
  const pending = page.locator('[data-testid="console-pending"]')
  await pending.waitFor({ state: 'visible', timeout: 30_000 })
  await pending.locator('input[placeholder="代填的值（与合法选项一致）"]').fill(optionValue)
  if (note) {
    await pending.locator('input[placeholder="代填备注（可选）"]').fill(note)
  }

  return runCommand(page, '代填', () => pending.getByRole('button', { name: '代填', exact: true }).click())
}

/** 说书人强制作废当前挂起的请求。 */
async function forceVoidPending(page, reason, note) {
  const pending = page.locator('[data-testid="console-pending"]')
  await pending.waitFor({ state: 'visible', timeout: 30_000 })
  if (reason) {
    await pending.locator('select').selectOption(reason)
  }

  if (note) {
    await pending.locator('input[placeholder="作废说明（可选）"]').fill(note)
  }

  return runCommand(page, '强制作废', () =>
    pending.getByRole('button', { name: '强制作废', exact: true }).click(),
  )
}

/** 裁定点区块的可见文本（没有等待中的裁定点时为空串）。 */
async function readDecisionText(page) {
  const block = page.locator('[data-testid="console-decision"]')
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

    await sleep(100)
  }

  return text
}

/** 说书人按自由决定结清当前裁定点。 */
async function settleFreeDecision(page, content) {
  await page.getByPlaceholder('自由决定的内容（可为空）').fill(content)
  return runCommand(page, '裁定', () => page.getByRole('button', { name: '按自由决定结清' }).click())
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
  if (typeof report.causedBy === 'number') {
    await panel.locator('label.inline select').selectOption(String(report.causedBy))
  }

  await panel.getByPlaceholder('变化原因（必填，会随事件流记录）').fill(report.reason)
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

/** 失效账本表体里的涡流行：先切到「失效账本」之后再匹配，避免命中最近结算 / 能力使用账本里的同词（对抗复核 H2）。 */
function vortoxMalfunctionRowOf(panelText) {
  const lines = panelText
    .split('\n')
    .map((line) => line.replace(/\s+/g, ' ').trim())
    .filter((line) => line.length > 0)
  const start = lines.findIndex((line) => line === '失效账本')
  return lines
    .slice(start === -1 ? 0 : start + 1)
    .find((line) => line.includes('dreamer') && line.includes('涡流') && !line.includes('原因：'))
}

/** 面板里最近一次结算的序号（第一处「序号 N」；还没结算过时为 null）。 */
function resolutionSequenceOf(panelText) {
  return /序号\s*(\d+)/.exec(panelText)?.[1] ?? null
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

    await sleep(150)
  }

  return await readSlotIndex(page)
}

/**
 * 截图：只有落盘档（--screenshots-all）才真正截；迭代档跳过（省每次截图的秒级开销），
 * 但失败诊断截图（overrides.force）始终落盘——调试不能没有它。
 */
async function screenshot(page, name, overrides = {}) {
  if (!config.screenshots && overrides.force !== true) {
    console.log(`  截图（迭代档跳过落盘）：${name}`)
    return
  }

  mkdirSync(screenshotsDir, { recursive: true })
  const target = path.join(screenshotsDir, `${name}.png`)
  await page.screenshot({ path: target, fullPage: true })
  console.log(`  截图：${target}`)
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
  await screenshot(page, `diag-${label}-outcome`, { force: true }).catch(() => {})
}

/** 分配表的席位数量（UI 由 VITE_SEAT_COUNT 决定，与宿主 GameServer__SeatCount 对齐）。 */
async function readSeatCount(page) {
  return page.locator('section', { hasText: '开局分配' }).locator('tbody tr').count()
}

/** 打开本批次 SQLite 库（写用途）；调用方负责 close。 */
function openBatchDatabase() {
  return new DatabaseSync(databasePath)
}

/**
 * 把库里最新一条 SeatStateChangedEvent 的 reason 改成给定文本：只改原因、不改值，
 * 步骤机不受影响，但内存里的状态账与事件流就此分叉——用来判"重建报告必须报状态账不一致"。
 */
function tamperLatestSeatChangeReason(marker) {
  const database = openBatchDatabase()
  try {
    const row = database
      .prepare("SELECT Sequence, Payload FROM Events WHERE Type = 'SeatStateChangedEvent' ORDER BY Sequence DESC LIMIT 1")
      .get()
    if (row === undefined) {
      throw new Error('库里没有 SeatStateChangedEvent，无法制造状态账分叉')
    }

    const payload = JSON.parse(String(row.Payload))
    payload.reason = marker
    database.prepare('UPDATE Events SET Payload = ? WHERE Sequence = ?').run(JSON.stringify(payload), row.Sequence)
    return Number(row.Sequence)
  } finally {
    database.close()
  }
}

/** 弄坏首发事件载荷（重启恢复必然失败），返回原始内容用于修复。 */
function corruptFirstEventPayload() {
  const database = openBatchDatabase()
  try {
    const row = database.prepare('SELECT Sequence, Payload FROM Events ORDER BY Sequence ASC LIMIT 1').get()
    if (row === undefined) {
      throw new Error('事件表为空，无法制造恢复失败')
    }

    database.prepare('UPDATE Events SET Payload = ? WHERE Sequence = ?').run('{ this is not valid json', row.Sequence)
    return { sequence: Number(row.Sequence), payload: String(row.Payload) }
  } finally {
    database.close()
  }
}

/** 修复首发事件载荷（事件流恢复完整，显式重建才有机会成功）。 */
function restoreEventPayload(saved) {
  const database = openBatchDatabase()
  try {
    database.prepare('UPDATE Events SET Payload = ? WHERE Sequence = ?').run(saved.payload, saved.sequence)
  } finally {
    database.close()
  }
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

    await sleep(100)
  }

  throw new Error(`${label} 在 ${timeoutMs}ms 内没有就绪：${lastError}`)
}

/** 启动真宿主（沿用同一临时库与端口）；返回子进程，日志累加到模块级 serverLog。 */
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
      GameServer__SeatCount: String(options.seatCount),
      GameServer__SlotQuotaSeconds: String(config.quotaSeconds),
      GameServer__PacerIntervalMilliseconds: '200',
      DOTNET_ENVIRONMENT: 'Production',
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  })
  children.push(child)
  child.stdout.on('data', (chunk) => serverLog.push(String(chunk)))
  child.stderr.on('data', (chunk) => serverLog.push(String(chunk)))
  await waitForHttp(`${serverUrl}/healthz`, '宿主 /healthz', 90_000)
  return child
}

/** 停掉一个宿主子进程并等它真的退出：Windows 上 taskkill 是异步的，不等它就改库会踩句柄冲突。 */
async function stopServer(child) {
  if (child.exitCode === null && child.signalCode === null) {
    if (process.platform === 'win32') {
      spawn('taskkill', ['/pid', String(child.pid), '/F'], { stdio: 'ignore' })
    } else {
      child.kill('SIGTERM')
    }
  }

  const deadline = Date.now() + 10_000
  while (Date.now() < deadline && child.exitCode === null && child.signalCode === null) {
    await sleep(100)
  }

  if (child.exitCode === null && child.signalCode === null) {
    throw new Error(`宿主进程未能在 10s 内退出：pid=${child.pid}`)
  }
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
    // 而"有角色"还要求该角色的夜间契约已实现（plan.contract_missing）。默认五席：
    // 白天处决 + 夜晚击杀各带走一人——**三席夹具会在第一次死亡后当场满足「仅剩两名存活 → 邪恶获胜」**
    // （规则正确行为），第二夜就再也开不起来。两名外来者（畸形秀演员 / 呆瓜）不在夜晚顺序表上、
    // 也不会被诺-达鲺毒到（它只毒邻近镇民），所以首夜 13 个槽位与中毒归因面都不变。
    seatCount: undefined,
    assign: ['clockmaker', 'dreamer', 'no-dashii', 'mutant', 'klutz'],
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
    throw new Error('本批次场景需要 --assign 同时含 clockmaker / dreamer / no-dashii（这三个角色的能力链路是场景主干）')
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
  // 浏览器关闭与子进程退出互不依赖：并行等，省下一次串行等待。
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

function sleep(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds))
}
