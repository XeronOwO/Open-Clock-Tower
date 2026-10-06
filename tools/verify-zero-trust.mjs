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
 *         8（收包不含越权信息）、9 的在线面（无关玩家零活动）、11（拒绝审计）、
 *         账号面（D-0021 / D-0027：伪造 / 已登出的账号会话进不去，旧协议 `JoinStoryteller` 已删除）。
 * 行 6 的"僧侣"依赖尚未落地的角色，已在集成测试里用等价反例覆盖。
 *
 * 说书人进场走**真实用法**（D-0027）：账号 Hub 注册夹具账号 → 由它开一桌 → 游戏连接声明该桌的
 * `?gameId=` 并出示账号会话。票据已整个退场，宿主也不再自动建默认桌。
 *
 * 前置：Node >= 22.5（node:sqlite，读席位票据）、本机已构建。
 * 用法（在仓库根运行；默认迭代档 = 快节拍 + 复用产物）：
 *   node tools/verify-zero-trust.mjs                  # 迭代档
 *   node tools/verify-zero-trust.mjs --quota 2 --build  # 取证档（慢节拍 + 强制构建）
 *   node tools/verify-zero-trust.mjs --port 5397      # 自定端口
 *
 * 外部耦合（换机器先核对 web/AGENTS.md §3.1）：宿主编译产物路径、SQLite 表 Games 的
 * SeatsJson 列形状（席位票据仍直读库；说书人身份已改走账号，见 D-0027）。
 * 退出码：0 = 全过；1 = 有失败；2 = 环境缺依赖。
 */
import { spawn } from 'node:child_process'
import { mkdtempSync, rmSync } from 'node:fs'
import { createRequire } from 'node:module'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { readSeatTickets } from './lib/entrance.mjs'
import { describeProfile, ensureServerArtifacts, extractProfileFlags, resolveProfile } from './lib/verify-profile.mjs'

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const webRoot = path.join(repositoryRoot, 'web')
const { flags, rest } = extractProfileFlags(process.argv.slice(2))
const options = parseArguments(rest)

/**
 * 档位（tools/lib/verify-profile.mjs）：默认迭代档（快节拍 + 复用产物）；本装置无浏览器截图，
 * 取证档显式传 `--quota 2 --build`（`--screenshots-all` 传了也无副作用）。
 */
const config = resolveProfile(flags, { quotaSeconds: 0.3 })
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

console.log(`档位：${describeProfile(config)}`)

const workspace = mkdtempSync(path.join(tmpdir(), 'oct-zero-trust-'))
const databasePath = path.join(workspace, 'verify.db')
const serverUrl = `http://localhost:${options.port}`
const accountHubUrl = `${serverUrl}/hub/account`

/**
 * 游戏 Hub 地址：`?gameId=` 是连接的一部分（D-0027：不声明桌的连接一律被拒），而桌标识要开完桌
 * 才知道——所以这里是 `let`，开桌之后覆盖成带桌标识的那条（那之前没有任何游戏连接可用）。
 */
let hubUrl = `${serverUrl}/hub/game`

/**
 * 夹具说书人账号（D-0027）：说书人票据退场之后，"是不是说书人"只由"是不是开这一桌的账号"回答。
 * 登录名总长受服务端 `UsernameText.MaxLength` = 24 字符约束（前缀 13 字符 + 后缀 ≤ 11）。
 */
const FIXTURE_HOST = {
  username: 'fixture-host-zero-trust',
  displayName: '夹具说书人zero-trust',
  password: 'fixture-pw-zero-trust',
  tableName: '零信任取证桌',
}

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
  // 复盘（R-0043）：终局后才由玩家主动查询；进行中任何收包里不该出现复盘字段。
  'replay',
  'markers',
  'steps',
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
  await ensureServerArtifacts({ repositoryRoot, buildMode: config.buildMode })
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
      GameServer__SlotQuotaSeconds: String(config.quotaSeconds),
      GameServer__PacerIntervalMilliseconds: '200',
      DOTNET_ENVIRONMENT: 'Production',
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  })
  children.push(server)
  server.stdout.on('data', (chunk) => serverLog.push(String(chunk)))
  server.stderr.on('data', (chunk) => serverLog.push(String(chunk)))
  await waitForHttp(`${serverUrl}/healthz`, '宿主 /healthz', 90_000)

  console.log('=== 2/6 开一桌：夹具账号 + 三席票据（D-0027：宿主不再自动建默认桌）===')
  // 说书人票据已整个退场：装置不再"从库里掏凭据"，而是走真实用法——注册夹具账号 → 由它开一桌。
  const accountHub = await connectTo(accountHubUrl)
  const host = await registerFixtureHost(accountHub)
  const created = await accountHub.invoke('CreateTable', host.session, FIXTURE_HOST.tableName, 3)
  if (created?.ok !== true) {
    throw new Error(`开桌失败：${created?.code ?? '未知'} ${created?.message ?? ''}`)
  }

  const gameId = String(created.gameId ?? '')
  if (gameId.length === 0) {
    throw new Error('开桌回执没有桌标识（D-0027：说书人面凭桌标识进主持台）')
  }

  // 账号会话在服务端不绑连接（8 小时到期，见 AccountSessionRegistry），开桌这条连接用完即关。
  await accountHub.stop()
  hubUrl = `${serverUrl}/hub/game?gameId=${encodeURIComponent(gameId)}`
  console.log(`  夹具账号 ${host.username} 已开桌：game=${gameId}（${created.seatCount} 席）；游戏 Hub ${hubUrl}`)

  const seatTickets = readSeatTickets(databasePath, gameId)
  check('席位票据齐备（3 席）', seatTickets.length === 3, `实际 ${seatTickets.length} 张（game=${gameId}）`)

  console.log('=== 3/6 篡改客户端：加入、分配、开夜、裁定 ===')
  const storyteller = await joinAsStoryteller(host.session)
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

  console.log('=== 5.8/6 账号会话负向（D-0021 / D-0027）：账号会话不是游戏凭据，席位票据才是入座授权 ===')
  // 账号会话与连接凭据是**两套**凭据面（D-0012 / D-0021）：这一段取证"账号会话既不能当连接凭据，
  // 也不能被伪造 / 过期后蒙混过关"，反方向取证"席位票据仍然是唯一的入座授权"（D-0027 之后
  // 说书人这一侧不看票据了——看的是"你是不是开这一桌的账号"，见下面两条负向）。
  const accounts = await connectTo(accountHubUrl)
  const registered = await accounts.invoke('Register', 'zt-account', '零信任玩家名', 'zt-account-password-1')
  const accountSession = registered.accountSession
  const seat1Ticket = seatTickets.find((seatTicket) => seatTicket.seat === 1)

  // 伪造（或已过期）的账号会话：必须显式拒绝，不得静默降级成游客入座。
  const forgedJoin = await expectRejected(
    () => anonymous.invoke('JoinSeatWithAccount', seat1Ticket.ticket, '伪造账号会话-不存在的随机串', 0),
    '账号会话无效',
  )
  check('行 账号：伪造 / 过期账号会话调 JoinSeatWithAccount 被拒（不静默降级成游客）', forgedJoin.ok, forgedJoin.message)

  // 被拒的连接不得因此进房：1 号的原连接凭据仍然有效（没被顶替），被拒的连接也拿不到任何凭据。
  const afterForged = await player1.connection.invoke(
    'ForceAdvance',
    player1.credential,
    '零信任取证：伪造会话尝试之后确认 1 号原连接仍然有效',
    'zt-after-forged-1',
  )
  check(
    '行 账号：伪造尝试没有顶替 1 号原连接、也没有给被拒连接签发凭据',
    afterForged.kind === 'Rejected' && afterForged.rejectionCode === 'identity.storyteller_only',
    describeOutcome(afterForged),
  )

  // 账号会话不能当连接级凭据用：拿它去调一条游戏命令，必须被凭据闸拒。
  const sessionAsCredential = await expectRejected(
    () =>
      anonymous.invoke(
        'SubmitResponse',
        accountSession,
        'zt-request',
        'seat:1',
        'zt-account-as-credential-1',
        0,
      ),
    '连接凭据无效',
  )
  check(
    '行 账号：账号会话不能当连接级凭据用（拿它调游戏命令被凭据闸拒绝）',
    sessionAsCredential.ok,
    sessionAsCredential.message,
  )

  // 已登出（会话已失效）的账号会话同样进不了房。
  const loggedOut = await accounts.invoke('Logout', accountSession)
  const revokedJoin = await expectRejected(
    () => anonymous.invoke('JoinSeatWithAccount', seat1Ticket.ticket, accountSession, 0),
    '账号会话无效',
  )
  check(
    '行 账号：已登出的账号会话进不了房（登出即失效，不靠客户端自觉）',
    loggedOut.ok === true && revokedJoin.ok,
    `登出 code=${loggedOut.code}；入座被拒：${revokedJoin.message}`,
  )

  // 说书人这一侧（D-0027）：入场不看票据、只看"你是不是开这一桌的账号"，所以负向也钉在这条机制上。
  // 下面两条都走 anonymous 这条**没有任何身份**的游戏连接——它的其余用途到此已经全部用完。
  const forgedHostJoin = await expectRejected(
    () => anonymous.invoke('JoinStorytellerWithAccount', '伪造账号会话-不存在的随机串'),
    '账号会话无效',
  )
  check(
    '行 账号 / D-0027：伪造 / 过期账号会话调 JoinStorytellerWithAccount 被拒（不静默降级、不签发凭据）',
    forgedHostJoin.ok,
    forgedHostJoin.message,
  )

  // 旧协议入口 `JoinStoryteller` 已整个删除。这一条**不押服务端的错误文案**（"方法不存在"怎么呈现由
  // 框架决定，写死文案会让装置跟着框架版本红），只钉实质：直调它**拿不到任何连接凭据**。
  const legacyHostJoin = await anonymous
    .invoke('JoinStoryteller', '旧协议说书人票据-已退场')
    .then((result) => ({ rejected: false, result }))
    .catch((error) => ({ rejected: true, message: error instanceof Error ? error.message : String(error) }))
  const legacyCredential = legacyHostJoin.rejected ? undefined : legacyHostJoin.result?.credential
  check(
    'D-0027：旧协议 JoinStoryteller 已删除（直调它拿不到任何连接凭据：票据退场后没有这条入口）',
    typeof legacyCredential !== 'string' || legacyCredential.length === 0,
    legacyHostJoin.rejected
      ? `被拒：${legacyHostJoin.message}`
      : `未抛错，但回执里没有凭据：${JSON.stringify(legacyHostJoin.result ?? null)}`,
  )

  // 正向对照：带**有效**账号会话 + 票据才能入座并拿到玩家名——证明上面的拒绝不是"路径没实现"。
  const relogin = await accounts.invoke('Login', 'zt-account', 'zt-account-password-1')
  const accountSeat = await joinSeatWithAccount(seat1Ticket.ticket, relogin.accountSession)
  const ownName = (accountSeat.view.seatNames ?? []).find((entry) => entry.seat === 1)?.displayName ?? ''
  check(
    '行 账号：有效账号会话 + 票据入座成功，且玩家名进入公开席位名投影（正向对照）',
    accountSeat.view.seat === 1 && ownName === '零信任玩家名',
    `seat=${accountSeat.view.seat} seatNames=${JSON.stringify(accountSeat.view.seatNames)}`,
  )

  // 反方向：票据换不来账号身份——拿席位票据去改玩家名（账号 Hub 的账号会话面）必须被拒。
  const ticketAsSession = await accounts.invoke('ChangeDisplayName', seat1Ticket.ticket, '票据冒充账号会话')
  check(
    '行 账号：席位票据不能当账号会话用（拿它去改玩家名被账号 Hub 拒绝）',
    ticketAsSession.ok === false && ticketAsSession.code === 'invalid_session',
    `ok=${ticketAsSession.ok} code=${ticketAsSession.code} message=${ticketAsSession.message}`,
  )

  // 同一张票据、但没有账号会话的第三方：入座成功也只是**游客连接**——入座结果里不含任何账号会话
  // （拿不到账号凭据），窗口内零定向推送。席位名是**公开映射**（D-0021「姓名是公开信息」），
  // 第三方照样看得见 1 号的名字：它改变的是"看得见"，不是"拿得到"——所以这里断言凭据与推送面，不断言"看不见名字"。
  const guestThird = await joinAsSeat(seat1Ticket)
  const guestMark = guestThird.inbox.length
  await sleep(600)
  const guestTargeted = guestThird.inbox
    .slice(guestMark)
    .filter((message) => TARGETED_METHODS.includes(message.method))
    .map((message) => message.method)
  const joinPayload = JSON.stringify(guestThird.inbox.find((message) => message.method === 'JoinSeat')?.payload ?? {})
  check(
    '行 账号：没有账号会话的第三方入座结果里没有账号凭据、窗口内零定向推送（票据 ≠ 账号身份）',
    guestThird.view.seat === 1
      && relogin.accountSession.length > 0
      && !joinPayload.includes(relogin.accountSession)
      && guestTargeted.length === 0,
    `seat=${guestThird.view.seat}；入座结果含账号会话=${joinPayload.includes(relogin.accountSession)}`
      + `；窗口内定向推送=${guestTargeted.join('|') || '无'}；公开席位名=${JSON.stringify(guestThird.view.seatNames)}`,
  )
  await guestThird.connection.stop()
  await accountSeat.connection.stop()
  await accounts.stop()

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
  return { connection, credential: joined.credential, view: joined.bundle.view, inbox }
}

/** 带账号会话的入座（D-0021）：票据用于首次认领；返回视图供"玩家名是否进投影"取证。 */
async function joinSeatWithAccount(ticket, accountSession) {
  const connection = await connect()
  const inbox = []
  for (const method of PUSH_METHODS) {
    connection.on(method, (payload) => {
      inbox.push({ method, payload })
    })
  }

  const joined = await connection.invoke('JoinSeatWithAccount', ticket, accountSession, 0)
  inbox.push({ method: 'JoinSeatWithAccount', payload: joined })
  return { connection, credential: joined.credential, view: joined.bundle.view, inbox }
}

/**
 * 注册夹具说书人账号（D-0027）：注册即登录，回执里带 `accountSession`；
 * 不 ok 就当场抛（`invalid_username` 这类配置错误不该以"后面某条断言红了"的形式出现）。
 */
async function registerFixtureHost(connection) {
  const account = await connection.invoke(
    'Register',
    FIXTURE_HOST.username,
    FIXTURE_HOST.displayName,
    FIXTURE_HOST.password,
  )
  if (account?.ok !== true || typeof account.accountSession !== 'string' || account.accountSession.length === 0) {
    throw new Error(`夹具账号注册失败：${account?.code ?? '未知'} ${account?.message ?? ''}`)
  }

  return { ...FIXTURE_HOST, session: account.accountSession }
}

/** 说书人加入（D-0027）：连接已声明桌标识，入场出示**账号会话**——只有开这一桌的账号进得来。 */
async function joinAsStoryteller(accountSession) {
  const connection = await connect()
  const joined = await connection.invoke('JoinStorytellerWithAccount', accountSession)
  return { connection, credential: joined.credential }
}

/** 连到指定 Hub（游戏 / 账号是两条独立的连接与凭据面）。 */
async function connectTo(url) {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(url)
    .configureLogging(signalR.LogLevel.Error)
    .build()
  await connection.start()
  return connection
}

async function connect() {
  return connectTo(hubUrl)
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

function parseArguments(argv) {
  const parsed = { port: 5397 }
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index]
    if (argument === '--port') {
      parsed.port = Number.parseInt(argv[index + 1] ?? '', 10)
      index += 1
    }
  }

  if (!Number.isFinite(parsed.port) || parsed.port <= 0) {
    throw new Error(`--port 非法：${parsed.port}`)
  }

  return parsed
}
