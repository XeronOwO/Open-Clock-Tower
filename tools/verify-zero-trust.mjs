/**
 * 零信任负向取证装置（票据 docs/backlog/done/zero-trust-security-model.md 矩阵）。
 *
 * 它回答：**客户端被完全攻陷之后，能不能作弊或窃取他人信息？**
 * 与传统批次（verify-storyteller-panel.mjs）的分工：
 *   - 主批次跑真浏览器 + 真 UI，断言"该看见的看见、不该看见的不活动"；
 *   - 本装置用 Node SignalR 客户端扮演**篡改后的前端**：直接调 Hub、伪造 / 冒用凭据、
 *     记录每个玩家客户端收到的**全部消息**并扫描越权字段。
 *
 * 覆盖行：2（未加入的连接）、3（旧连接凭据）、1（他人请求归属）、4（玩家调说书人命令）、
 *         5（白天提交夜间行动：完成首夜 → 开白天 → 提交被阶段闸拒绝）、6（非法选项）、
 *         8（收包不含越权信息）、9 的在线面（无关玩家零活动）、11（拒绝审计）。
 * 行 6 的"僧侣"依赖尚未落地的角色，已在集成测试里用等价反例覆盖。
 *
 * 前置：Node >= 22.5（node:sqlite）、本机已构建；脚本自己会跑一次 Release 构建。
 * 用法（在仓库根运行）：
 *   node tools/verify-zero-trust.mjs
 *   node tools/verify-zero-trust.mjs --port 5397 --quota 0.5
 *
 * 外部耦合（换机器先核对 web/AGENTS.md §3.1）：宿主编译产物路径、SQLite 表 Games
 * 的 StorytellerTicket / SeatsJson 列形状。退出码：0 = 全过；1 = 有失败；2 = 环境缺依赖。
 */
import { spawn } from 'node:child_process'
import { mkdtempSync, rmSync } from 'node:fs'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { DatabaseSync } from 'node:sqlite'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const webRoot = path.join(repositoryRoot, 'web')
const options = parseArguments(process.argv.slice(2))
const results = []
const children = []
let signalR = null

try {
  const requireFromWeb = createRequire(path.join(webRoot, 'package.json'))
  signalR = requireFromWeb('@microsoft/signalr')
} catch (error) {
  console.error(`缺少 @microsoft/signalr：${String(error)}`)
  console.error('先运行：cd web; npm install')
  process.exit(2)
}

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-zero-trust-'))
const databasePath = path.join(workspace, 'verify.db')
const serverUrl = `http://localhost:${options.port}`
const hubUrl = `${serverUrl}/hub/game`

/** 越权字段名（说书人专属；扫描玩家客户端收到的**全部消息**）：一个精确键名命中就是泄密。 */
const FORBIDDEN_PLAYER_KEYS = [
  'seats',
  'effects',
  'facts',
  'causedBy',
  'abilityUses',
  'malfunctions',
  'lastResolution',
  'terminationKind',
  'terminationReason',
  'madnesses',
  'awaitingDecisionId',
  'awaitingDecisionContext',
  'awaitingDecisionOptions',
  'slotIndex',
  'slotCount',
  'currentSlotId',
  'stepDigest',
  'recentSeatChanges',
  'currentSlotActor',
  'planCompleted',
  'control',
  'storytellerTicket',
]

const PUSH_METHODS = [
  'ReceiveOperationRequest',
  'ReceiveOperationRequestVoided',
  'ReceiveOperationRequestAnswered',
  'ReceivePhaseStarted',
  'ReceiveInformationResult',
  'ReceiveDayChanged',
]

/** 定向推送（只该到当事玩家）：行 9 的"无关玩家零活动"只针对这些；阶段是公开信息，不算活动。 */
const TARGETED_METHODS = [
  'ReceiveOperationRequest',
  'ReceiveOperationRequestVoided',
  'ReceiveOperationRequestAnswered',
  'ReceiveInformationResult',
]

process.on('exit', () => {
  for (const child of children) {
    if (child.exitCode === null && child.signalCode === null) {
      child.kill()
    }
  }
})

try {
  await main()
  await cleanup()
  report()
  process.exit(results.some((result) => !result.pass) ? 1 : 0)
} catch (error) {
  console.error(`\n[FAIL] 脚本异常终止：${error instanceof Error ? error.stack : String(error)}`)
  results.push({ label: '脚本执行到底', pass: false, detail: '见上方异常' })
  await cleanup()
  report()
  process.exit(1)
}

async function main() {
  console.log('=== 1/6 构建并启动真宿主（独立临时库）===')
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
  const serverLog = []
  const server = spawn(serverExecutable, [], {
    cwd: repositoryRoot,
    env: {
      ...process.env,
      ASPNETCORE_URLS: serverUrl,
      GameServer__DatabasePath: databasePath,
      GameServer__SeatCount: '3',
      GameServer__SlotQuotaSeconds: String(options.quotaSeconds),
      GameServer__PacerIntervalMilliseconds: '200',
      DOTNET_ENVIRONMENT: 'Production',
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  })
  children.push(server)
  server.stdout.on('data', (chunk) => serverLog.push(String(chunk)))
  server.stderr.on('data', (chunk) => serverLog.push(String(chunk)))
  await waitForHttp(`${serverUrl}/healthz`, '宿主 /healthz', 90_000)

  console.log('=== 2/6 取票据：说书人 + 三席 ===')
  const storytellerTicket = readStorytellerTicket(databasePath)
  const seatTickets = readSeatTickets(databasePath)
  check('席位票据齐备（3 席）', seatTickets.length === 3, `实际 ${seatTickets.length} 张`)

  console.log('=== 3/6 篡改客户端：加入、分配、开夜、裁定 ===')
  const storyteller = await joinAsStoryteller(storytellerTicket)
  check('说书人加入并拿到连接凭据', storyteller.credential.length >= 16)

  const players = new Map()
  for (const seatTicket of seatTickets) {
    players.set(seatTicket.seat, await joinAsSeat(seatTicket))
  }
  check(
    '各席加入并各自拿到独立凭据',
    new Set([...players.values()].map((player) => player.credential)).size === 3,
  )

  // 行 2：不持票据的连接伪造凭据 → 拒绝（且不改任何状态）。
  const anonymous = await connect()
  const anonymousRejected = await expectRejected(
    () => anonymous.invoke('GetStorytellerView', '伪造凭据-不存在的连接'),
    '连接凭据无效',
  )
  check('行 2：未加入的连接调接口被拒绝（凭据闸）', anonymousRejected.ok, anonymousRejected.message)

  const roles = ['clockmaker', 'dreamer', 'no-dashii']
  const assigned = await storyteller.connection.invoke(
    'AssignCharacters',
    storyteller.credential,
    seatTickets.map((seatTicket, index) => ({ seat: seatTicket.seat, character: roles[index] })),
    'zt-assign-1',
  )
  check('开局分配被受理', assigned.kind === 'Accepted', describeOutcome(assigned))

  const started = await storyteller.connection.invoke(
    'StartNight',
    storyteller.credential,
    1,
    'Recommended',
    'zt-night-1',
  )
  check('开夜（Recommended）被受理', started.kind === 'Accepted', describeOutcome(started))

  const decision = await waitForStoryteller(
    storyteller,
    (view) => typeof view.awaitingDecisionId === 'string' && view.awaitingDecisionId.length > 0,
    30_000,
  )
  check('到达钟表匠裁定点（说书人专属）', decision !== null)

  const resolved = await storyteller.connection.invoke(
    'ResolveDecisionPoint',
    storyteller.credential,
    decision.awaitingDecisionId,
    '零信任取证：距离 1。',
    null,
    'zt-decision-1',
  )
  check('裁定被受理', resolved.kind === 'Accepted', describeOutcome(resolved))

  const dreamer = playerOf(players, 2)
  const gotRequest = await waitUntil(
    () => dreamer.inbox.some((message) => message.method === 'ReceiveOperationRequest'),
    30_000,
  )
  check('行 8：筑梦师请求按单播到达 2 号客户端', gotRequest)
  const dreamerRequest = dreamer.inbox.find((message) => message.method === 'ReceiveOperationRequest')?.payload

  // 窗口采样：从"筑梦师请求已到达"这一刻起，1 / 3 号不得再收到任何定向推送。
  // 1 号此前收到的是**它自己（钟表匠）的信息结果**——那是设计，不是噪声。
  const quietMarks = inboxMarks(players)
  await sleep(600)
  const quietViolations = [1, 3]
    .map((seat) => ({ seat, methods: newTargetedMethods(playerOf(players, seat), quietMarks.get(seat)) }))
    .filter((item) => item.methods.length > 0)
  check(
    '行 9：无关玩家（1 / 3 号）窗口内零定向推送',
    quietViolations.length === 0,
    quietViolations.map((item) => `${item.seat}号=${item.methods.join('|')}`).join(' ') || '窗口内无新增',
  )

  console.log('=== 4/6 收包扫描：玩家收到的全部消息不得含越权字段或他人角色（行 8）===')
  const slugBySeat = new Map(seatTickets.map((seatTicket, index) => [seatTicket.seat, roles[index]]))
  for (const [seat, player] of players) {
    const text = JSON.stringify(player.inbox)
    const hits = FORBIDDEN_PLAYER_KEYS.filter((key) => text.includes(`"${key}"`))
    check(`行 8：${seat} 号客户端收包无越权字段`, hits.length === 0, hits.join('、'))

    const leakedRoles = [...slugBySeat.entries()]
      .filter(([otherSeat, slug]) => otherSeat !== seat && text.includes(slug))
      .map(([otherSeat, slug]) => `${otherSeat}号=${slug}`)
    check(`行 8：${seat} 号收包不含其他席位的角色`, leakedRoles.length === 0, leakedRoles.join('、'))
  }

  console.log('=== 5/6 篡改客户端直调 Hub（行 1 / 3 / 4 / 6 / 10）===')
  const player1 = playerOf(players, 1)
  const player3 = playerOf(players, 3)

  // 行 4 / 10：玩家凭据调说书人命令 → 身份闸拒绝（Application 层，带拒绝码）。
  const forceAdvance = await player1.connection.invoke(
    'ForceAdvance',
    player1.credential,
    '玩家自称说书人',
    'zt-force-1',
  )
  check(
    '行 4 / 10：玩家凭据调说书人命令被身份闸拒绝（命令面无自称身份的参数）',
    forceAdvance.kind === 'Rejected' && forceAdvance.rejectionCode === 'identity.storyteller_only',
    describeOutcome(forceAdvance),
  )

  // 行 10：用自己凭据尝试改动**他人状态**（说书人专用命令；命令面没有"自称身份"的参数可伪造）。
  const reportSeat = await player1.connection.invoke(
    'ReportSeatState',
    player1.credential,
    2,
    'Dead',
    null,
    null,
    null,
    null,
    '零信任取证：玩家越权上报他人状态',
    null,
    'zt-report-1',
  )
  check(
    '行 10：玩家尝试上报他人状态被身份闸拒绝',
    reportSeat.kind === 'Rejected' && reportSeat.rejectionCode === 'identity.storyteller_only',
    describeOutcome(reportSeat),
  )

  // 行 1 / 5：别的席位的请求不是你的 → 阶段闸拒绝（不泄露他人请求内容）。
  const crossSeat = await player3.connection.invoke(
    'SubmitResponse',
    player3.credential,
    dreamerRequest.requestId,
    dreamerRequest.options[0].value,
    'zt-cross-1',
    0,
  )
  check(
    '行 1 / 5：跨席位提交被阶段闸拒绝（拒绝码不区分"没有 / 已了结 / 不是你的"）',
    crossSeat.kind === 'Rejected' && crossSeat.rejectionCode === 'phase.no_request_for_you',
    describeOutcome(crossSeat),
  )
  check(
    '行 1：拒绝回执不泄露他人座位号',
    typeof crossSeat.rejectionMessage === 'string' && !crossSeat.rejectionMessage.includes('号'),
    crossSeat.rejectionMessage ?? '',
  )
  check(
    '行 1：拒绝回执不带全局事件序号（防差分探测房间活动）',
    crossSeat.sequence === 0,
    `sequence=${crossSeat.sequence}`,
  )

  // 行 6：不在服务端合法集合里的选项 → 合法性闸拒绝。
  const illegal = await dreamer.connection.invoke(
    'SubmitResponse',
    dreamer.credential,
    dreamerRequest.requestId,
    'seat:99',
    'zt-illegal-1',
    0,
  )
  check(
    '行 6：非法选项被合法性闸拒绝',
    illegal.kind === 'Rejected' && illegal.rejectionCode === 'legality.option_not_legal',
    describeOutcome(illegal),
  )

  // 行 3：同席重连换新凭据；旧连接立即失效，旧凭据在新连接上不被接受。
  const seat3Ticket = seatTickets.find((seatTicket) => seatTicket.seat === 3)
  const staleCredential = player3.credential
  const reconnected = await joinAsSeat(seat3Ticket)
  check('行 3：同席重连签发新凭据', reconnected.credential !== staleCredential)
  const superseded = await expectRejected(
    () =>
      player3.connection.invoke(
        'SubmitResponse',
        staleCredential,
        dreamerRequest.requestId,
        dreamerRequest.options[0].value,
        'zt-stale-1',
        0,
      ),
    '连接凭据无效',
  )
  check('行 3：被顶替的旧连接立即失效', superseded.ok, superseded.message)
  const staleOnNewConnection = await expectRejected(
    () => reconnected.connection.invoke('GetStorytellerView', staleCredential),
    '连接凭据无效',
  )
  check('行 3：旧凭据在新连接上被拒绝', staleOnNewConnection.ok, staleOnNewConnection.message)

  // 合法提交把本夜推完：信息只到 2 号（行 8 的单播面、行 9 的无关玩家零活动）。
  const legalSubmit = await dreamer.connection.invoke(
    'SubmitResponse',
    dreamer.credential,
    dreamerRequest.requestId,
    dreamerRequest.options[0].value,
    'zt-answer-1',
    0,
  )
  check('筑梦师合法提交被受理', legalSubmit.kind === 'Accepted', describeOutcome(legalSubmit))
  const infoDecision = await waitForStoryteller(
    storyteller,
    (view) => typeof view.awaitingDecisionId === 'string' && view.awaitingDecisionId.length > 0,
    30_000,
  )
  check('到达信息类裁定点', infoDecision !== null)

  // 第二次裁定之前打点：只比较**这次裁定之后**的新消息——1 号拥有自己的钟表匠信息，不能混算。
  const infoMarks = inboxMarks(players)
  const infoResolved = await storyteller.connection.invoke(
    'ResolveDecisionPoint',
    storyteller.credential,
    infoDecision.awaitingDecisionId,
    '零信任取证-筑梦师信息',
    null,
    'zt-decision-2',
  )
  check('信息类裁定被受理', infoResolved.kind === 'Accepted', describeOutcome(infoResolved))

  const gotInformation = await waitUntil(
    () => newTargetedMethods(dreamer, infoMarks.get(2)).includes('ReceiveInformationResult'),
    15_000,
  )
  const othersGotInformation = [1, 3].some((seat) =>
    newTargetedMethods(playerOf(players, seat), infoMarks.get(seat)).includes('ReceiveInformationResult'),
  )
  check(
    '行 8 / 9：筑梦师信息只到 2 号，1 / 3 号零新增',
    gotInformation && !othersGotInformation,
    `2号收到=${gotInformation}；1号新增=${newTargetedMethods(playerOf(players, 1), infoMarks.get(1)).join('|') || '无'}；`
      + `3号新增=${newTargetedMethods(playerOf(players, 3), infoMarks.get(3)).join('|') || '无'}`,
  )

  // 票据 player-information-resync-race：推送必须带事件序号，客户端才可能按序号合并。
  // 这里从**真实收包**取证（含阶段与白天广播），缺一即视为合并判据不成立。
  const sequencedPushes = [...players.values()]
    .flatMap((player) => player.inbox)
    .filter((message) => PUSH_METHODS.includes(message.method))
  const badSequences = sequencedPushes.filter(
    (message) => !Number.isInteger(message.payload?.sequence) || message.payload.sequence <= 0,
  )
  // 信息推送的序号就是它的事件序号：同一次运行内必须互不相同
  // （恒定 0 / 重复都说明"按序号合并"的口径没真的落实，光判"字段存在"挡不住）。
  const infoSequences = sequencedPushes
    .filter((message) => message.method === 'ReceiveInformationResult')
    .map((message) => message.payload.sequence)
  check(
    '行 8：玩家推送携带正的事件序号，且信息推送序号互不相同（客户端按序号合并的判据）',
    sequencedPushes.length > 0
      && badSequences.length === 0
      && infoSequences.length > 0
      && new Set(infoSequences).size === infoSequences.length,
    `收到推送 ${sequencedPushes.length} 条；坏序号 ${badSequences.length} 条；信息推送 ${infoSequences.length} 条`
      + (badSequences.length > 0 ? `：${badSequences.map((message) => message.method).join('|')}` : ''),
  )

  console.log('=== 5.5/6 行 5 原场景：完成首夜 → 开白天 → 白天提交夜间行动被阶段闸拒绝 ===')
  // 本装置只关心"游戏进入白天"；白天玩法面由主批次（真浏览器）覆盖。
  for (let attempt = 0; attempt < 20; attempt += 1) {
    const view = await storyteller.connection.invoke('GetStorytellerView', storyteller.credential)
    if (view.planCompleted) {
      break
    }

    const forced = await storyteller.connection.invoke(
      'ForceAdvance',
      storyteller.credential,
      '零信任取证：走完首夜',
      `zt-night-force-${attempt}`,
    )
    check(`行 5：首夜强推（第 ${attempt + 1} 次）被受理`, forced.kind === 'Accepted', describeOutcome(forced))
  }

  const beforeDay = await storyteller.connection.invoke('GetStorytellerView', storyteller.credential)
  check('行 5：首夜已走完（可以开白天）', beforeDay.planCompleted === true, `planCompleted=${beforeDay.planCompleted}`)

  const startedDay = await storyteller.connection.invoke('StartDay', storyteller.credential, 'zt-day-1')
  check('行 5：开白天被受理', startedDay.kind === 'Accepted', describeOutcome(startedDay))

  const dayView = await storyteller.connection.invoke('GetStorytellerView', storyteller.credential)
  check(
    '行 5：阶段为白天、白天账开着',
    dayView.phase === 'Day' && dayView.day?.status === 'Open',
    `phase=${dayView.phase} day=${dayView.day?.status}`,
  )

  // 玩家在白天伪造一个"夜间行动"（当下没有挂起请求）→ 期望阶段闸拒绝、状态不变。
  // 用 1 号连接（3 号连接已在行 3 的重连用例里被新连接顶替，旧凭据已失效）。
  const dayNightAction = await player1.connection.invoke(
    'SubmitResponse',
    player1.credential,
    'forged-night-request',
    'seat:2',
    'zt-day-submit-1',
    0,
  )
  check(
    '行 5：白天提交夜间行动被阶段闸拒绝',
    dayNightAction.kind === 'Rejected' && dayNightAction.rejectionCode === 'phase.no_request_for_you',
    describeOutcome(dayNightAction),
  )
  check(
    '行 5：白天拒绝回执不带全局事件序号（状态不变）',
    dayNightAction.sequence === 0,
    `sequence=${dayNightAction.sequence}`,
  )
  const afterDayAction = await storyteller.connection.invoke('GetStorytellerView', storyteller.credential)
  check(
    '行 5：拒绝后白天仍开着、没有被推进',
    afterDayAction.phase === 'Day' && afterDayAction.day?.status === 'Open',
    `phase=${afterDayAction.phase} day=${afterDayAction.day?.status}`,
  )

  // 行 8 / R-0022：白天投影必须带**公开生死面**，且只有"谁是什么状态"——
  // 不含死因 / 归因 / 效果链（那些仍只在说书人视图里）。扫描所有仍收得到白天推送的客户端。
  await sleep(400)
  const dayPushTexts = [...players.values(), reconnected]
    .map((client) => ({
      client,
      text: JSON.stringify(client.inbox.filter((message) => message.method === 'ReceiveDayChanged')),
    }))
    .filter((entry) => entry.text !== '[]')
  check(
    '行 8：白天推送带公开生死面（lives / announcements），不含死因 / 归因 / 效果字段',
    dayPushTexts.length > 0
      && dayPushTexts.every(({ text }) =>
        text.includes('"lives"')
        && text.includes('"announcements"')
        && !text.includes('"causedBy"')
        && !text.includes('"reason"')
        && !text.includes('"effects"')),
    dayPushTexts.map(({ text }) => text.slice(0, 160)).join(' | ') || '没有客户端收到白天推送',
  )

  console.log('=== 6/6 审计：拒绝有记录，凭据明文不在日志里（行 11）===')
  const logText = serverLog.join('')
  check('行 11：凭据闸拒绝有审计记录', logText.includes('凭据闸'), '')
  check(
    '行 11：身份 / 合法性拒绝有带 code 的审计记录',
    logText.includes('identity.storyteller_only') && logText.includes('legality.option_not_legal'),
  )
  const leaked = [storyteller, ...players.values(), reconnected]
    .map((client) => client.credential)
    .filter((credential) => credential.length > 0 && logText.includes(credential))
  check('行 11：服务端日志不含任何凭据明文（只有短指纹）', leaked.length === 0, `${leaked.length} 条泄露`)
}

/** 记录"玩家客户端收到的全部消息"：join 结果 + 五类推送，一个都不漏。 */
async function joinAsSeat(seatTicket) {
  const connection = await connect()
  const inbox = []
  for (const method of PUSH_METHODS) {
    // 处理器必须返回 undefined：返回任何值都会被 SignalR 当成"客户端方法的返回值"，
    // 客户端会回一条 completion，服务端并不期待——实测会刷协议错误并干扰后续消息处理。
    connection.on(method, (payload) => {
      inbox.push({ method, payload })
    })
  }

  const joined = await connection.invoke('JoinSeat', seatTicket.ticket, 0)
  inbox.push({ method: 'JoinSeat', payload: joined })
  return { connection, credential: joined.credential, inbox }
}

async function joinAsStoryteller(ticket) {
  const connection = await connect()
  const joined = await connection.invoke('JoinStoryteller', ticket)
  return { connection, credential: joined.credential }
}

async function connect() {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(hubUrl)
    .configureLogging(signalR.LogLevel.Error)
    .build()
  await connection.start()
  return connection
}

/** 轮询说书人视图直到条件成立；超时返回 null（不猜、不吞）。 */
async function waitForStoryteller(storyteller, predicate, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    const view = await storyteller.connection.invoke('GetStorytellerView', storyteller.credential)
    if (predicate(view)) {
      return view
    }

    await sleep(50)
  }

  return null
}

function playerOf(players, seat) {
  const player = players.get(seat)
  if (player === undefined) {
    throw new Error(`缺少 ${seat} 号玩家的客户端`)
  }

  return player
}

/** 给每个玩家的收件箱打一个长度标记：之后只看"标记之后新增的消息"。 */
function inboxMarks(players) {
  return new Map([...players.entries()].map(([seat, player]) => [seat, player.inbox.length]))
}

/** 标记之后新增的定向推送方法名。 */
function newTargetedMethods(player, mark) {
  return player.inbox
    .slice(mark ?? 0)
    .filter((message) => TARGETED_METHODS.includes(message.method))
    .map((message) => message.method)
}

/** 期待一次调用被拒绝（HubException）；拒绝原因必须包含期望片段（否则视为没拒绝到位）。 */
async function expectRejected(action, expectedFragment = '') {
  try {
    await action()
    return { ok: false, message: '调用没有被拒绝' }
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error)
    return {
      ok: expectedFragment.length === 0 || message.includes(expectedFragment),
      message,
    }
  }
}

function describeOutcome(outcome) {
  return `${outcome.kind}${outcome.rejectionCode ? ` / ${outcome.rejectionCode}` : ''}`
}

function check(label, pass, detail = '') {
  results.push({ label, pass: Boolean(pass), detail })
  console.log(`  ${pass ? '[PASS]' : '[FAIL]'} ${label}${detail ? ` → ${detail}` : ''}`)
}

function report() {
  console.log('\n=== 零信任负向取证结论 ===')
  for (const result of results) {
    console.log(`${result.pass ? 'PASS' : 'FAIL'}  ${result.label}${result.detail ? ` → ${result.detail}` : ''}`)
  }

  const failed = results.filter((result) => !result.pass)
  console.log(failed.length === 0 ? `全部通过（${results.length} 项）` : `失败 ${failed.length} / ${results.length}`)
}

async function cleanup() {
  for (const child of children) {
    if (child.exitCode === null && child.signalCode === null) {
      child.kill()
    }
  }

  await sleep(200)
  rmSync(workspace, { recursive: true, force: true })
}

function sleep(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds))
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

function runProcess(command, args, cwd) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { cwd, stdio: 'inherit' })
    child.on('exit', (code) => (code === 0 ? resolve() : reject(new Error(`${command} 退出码 ${code}`))))
    child.on('error', reject)
  })
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
  const parsed = { port: 5397, quotaSeconds: 0.5 }
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index]
    if (argument === '--port') {
      parsed.port = Number.parseInt(argv[index + 1] ?? '', 10)
      index += 1
    } else if (argument === '--quota') {
      parsed.quotaSeconds = Number.parseFloat(argv[index + 1] ?? '')
      index += 1
    }
  }

  if (!Number.isFinite(parsed.port) || parsed.port <= 0) {
    throw new Error(`--port 非法：${parsed.port}`)
  }

  if (!Number.isFinite(parsed.quotaSeconds) || parsed.quotaSeconds <= 0) {
    throw new Error(`--quota 非法：${parsed.quotaSeconds}`)
  }

  return parsed
}
