/**
 * 玩家端连接：只做 JoinSeat / SubmitResponse 与推送接收（D-0013 §5）。
 *
 * 与说书人网关刻意分开：玩家连接**没有**、也不该有获取整份说书人视图的能力。
 * 零信任（D-0012）：JoinSeat 下发的**连接级凭据**只存在内存里，每条命令随参数出示；
 * 掉线重连必须重新用票据加入并换新凭据——旧连接的凭据在新连接上无效。
 *
 * 同步（D-0010 / 架构 §5，票据 player-information-resync-race）：推送与快照都是**同一份
 * 带序号的事实**，由 `PlayerViewMerge` 按序号合并后整份交给界面——网关是玩家视图的**唯一写入者**。
 * 快照序号低于本地已知不是坏包，而是"推送先到、响应后到"的正常竞态；只有事件数据本身坏
 * （越界 / 倒退 / 重复 / 不可识别）才显式失败并停在当前视图。
 */
import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
} from '@microsoft/signalr'
import type {
  DecisionOptionDto,
  InformationResultDto,
  JugglerGuessDto,
  KlutzChoiceDto,
  OperationRequestAnsweredDto,
  OperationRequestDto,
  OperationRequestVoidedDto,
  PhaseStartedDto,
  PlayerDayDto,
  PlayerEventDto,
  PlayerLifeDto,
  ReconnectBundleDto,
  ReplayViewDto,
  PlayerViewDto,
} from '@/contracts/game'
import {
  asArray,
  asBoolean,
  asCount,
  asCredential,
  asSizedText,
  asText,
  asTextArray,
  MAX_PUBLIC_LIFE_ENTRIES,
  normalizeDayView,
  normalizeGameOutcome,
  normalizeKlutzChoice,
  normalizeOption,
  normalizePlayerLife,
  normalizeSeatNames,
} from '@/display/format'
import { normalizeReplayView } from '@/display/replay'
import { HUB_PATH, type GatewayState } from '@/services/connectionState'
import { PlayerViewMerge, type PlayerPush } from '@/services/playerViewMerge'

/** 玩家侧回调。 */
export interface PlayerCallbacks {
  /** 合并后的最新视图（快照 + 推送按序号合并）；这是玩家视图的唯一写入点。 */
  onView: (view: PlayerViewDto) => void
  /** 请求被作废（强制作废 / 依赖失效 / 阶段推进…）：界面据此清掉当前请求并说明原因；先于 onView 发出。 */
  onRequestVoided: (voided: OperationRequestVoidedDto) => void
  /** 请求已被响应（玩家本人或说书人代填）：界面据此清掉当前请求；先于 onView 发出。 */
  onRequestAnswered: (answered: OperationRequestAnsweredDto) => void
  onState: (state: GatewayState) => void
  onDiagnostic: (message: string) => void
}

/** 连接工厂：默认连真宿主；测试注入假连接以验证网关的接线与顺序（不改变任何线上行为）。 */
export type GameConnectionFactory = (gameId?: string) => HubConnection

/** 默认连接：真 SignalR，自动重连与日志级别与说书人侧同口径。 */
function createPlayerConnection(gameId?: string): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(hubUrlFor(gameId))
    .withAutomaticReconnect([0, 1000, 3000, 5000])
    .configureLogging(LogLevel.Warning)
    .build()
}

/**
 * Hub 地址：声明**在哪一桌**（多桌，D-0024）。
 *
 * 不传就是"本机默认桌"——既有的票据流程与 18 个验收装置因此都不用改。
 */
export function hubUrlFor(gameId?: string): string {
  return gameId === undefined || gameId.length === 0
    ? HUB_PATH
    : `${HUB_PATH}?gameId=${encodeURIComponent(gameId)}`
}

/** 玩家连接网关。 */
export class PlayerGateway {
  private readonly connection: HubConnection
  private readonly merge = new PlayerViewMerge()
  private ticket = ''
  /** 账号会话（D-0021）：只存内存，用于认领席位 / 只凭账号重连；不落盘、不渲染。 */
  private accountSession: string | null = null
  /** 连接级凭据：只在内存中；票据才进 TicketStore，凭据绝不落盘。 */
  private credentialValue = ''

  constructor(
    private readonly callbacks: PlayerCallbacks,
    createConnection: GameConnectionFactory = () => createPlayerConnection(),
    gameId?: string,
  ) {
    // 桌在**建连接时**就定下来：`?gameId=` 属于这条连接，之后每条命令都由服务端按它路由。
    this.connection = createConnection(gameId)

    this.connection.on('ReceiveOperationRequest', (payload: unknown) => {
      const request = normalizeRequest(payload)
      if (request !== null) {
        this.dispatchPush({ kind: 'Request', sequence: request.sequence, request })
      }
    })
    this.connection.on('ReceiveOperationRequestVoided', (payload: unknown) => {
      const voided = normalizeVoided(payload)
      if (voided !== null) {
        this.dispatchPush({ kind: 'Voided', sequence: voided.sequence, voided })
      }
    })
    this.connection.on('ReceiveOperationRequestAnswered', (payload: unknown) => {
      const answered = normalizeAnswered(payload)
      if (answered !== null) {
        this.dispatchPush({ kind: 'Answered', sequence: answered.sequence, answered })
      }
    })
    this.connection.on('ReceivePhaseStarted', (payload: unknown) => {
      const started = normalizePhaseStarted(payload)
      if (started !== null) {
        this.dispatchPush({ kind: 'Phase', sequence: started.sequence, phase: started.phase })
      }
    })
    this.connection.on('ReceiveDayChanged', (payload: unknown) => {
      // 坏载荷不覆盖当前白天状态：宁可少更新一次，也不把界面清成空。
      const day = normalizePlayerDay(payload)
      if (day !== null) {
        this.dispatchPush({ kind: 'Day', sequence: day.sequence, day })
      }
    })
    this.connection.on('ReceiveInformationResult', (payload: unknown) => {
      const information = normalizeInformation(payload)
      if (information !== null) {
        this.dispatchPush({ kind: 'Information', sequence: information.sequence, information })
      }
    })
    // 游戏结束与呆瓜的公开选择都是公开广播（R-0024 / R-0027）：坏载荷不覆盖当前视图。
    this.connection.on('ReceiveGameEnded', (payload: unknown) => {
      const outcome = normalizeGameOutcome(payload)
      if (outcome !== null) {
        this.dispatchPush({ kind: 'Outcome', sequence: outcome.sequence, outcome })
      }
    })
    this.connection.on('ReceiveKlutzChoiceMade', (payload: unknown) => {
      const choice = normalizeKlutzChoice(payload)
      if (choice !== null) {
        this.dispatchPush({ kind: 'KlutzChoice', sequence: choice.sequence, choice })
      }
    })
    // 本人视图变更（艺术家提问状态 / 阶段边界）：一份**整视图**推送，按快照口径与序号合并（R-0040）——
    // 权力位 / 等待态只活在本人的投影里，没有这条通道时要等重连才更新。
    this.connection.on('ReceivePlayerViewChanged', (sequence: unknown, payload: unknown) => {
      const view = normalizePlayerView(payload)
      const viewSequence = asCount(sequence)
      if (view === null || viewSequence === null) {
        return
      }

      if (this.merge.applySnapshot(view, viewSequence)) {
        this.callbacks.onView(this.merge.snapshot())
      }
    })
    this.connection.onreconnecting(() => callbacks.onState('reconnecting'))
    this.connection.onreconnected(() => {
      callbacks.onState('connected')
      void this.rejoin()
    })
    this.connection.onclose(() => callbacks.onState('disconnected'))
  }

  get raw(): HubConnection {
    return this.connection
  }

  /** 当前连接凭据（只读；诊断与测试用，不渲染、不落盘）。 */
  get credential(): string {
    return this.credentialValue
  }

  /**
   * 加入席位并取重连包（快照 + 从本客户端已知序号起的全部**可见**事件）。
   *
   * 同步口径（架构 §5、D-0010）：**快照序号就是权威 watermark**——服务端在锁内读全量事件后
   * 按接收者投影，快照与序号同源，连续性由服务端保证。可见事件只用于带出窗口内的定向变化
   * （白名单投影，D-0012 §4.3），**不能**也用不着用条数证明序号区间完整（D-0013 §5）。
   * 事件必须是好数据：条目不可识别或序号越界 / 倒退 / 重复就**不装作没事**——显式失败并停在
   * 当前视图，绝不采纳这份包（禁止本地先跑再说）。
   * 快照比本地已知旧（推送先到、响应后到）不是坏包：交给 `PlayerViewMerge` 按序号合并，
   * 迟到的快照只补不覆盖（票据 player-information-resync-race）。
   * 送去服务端的"本地已知"是**事件窗口水位**（只由快照推进）：读时推送的序号可能领先于本席
   * 真实的事件位置，拿它当已知序号会把缺口事件窗口截断（架构 §5）。
   * 零信任口径（D-0012）：加入结果里的连接级凭据是后续发命令的唯一凭据；拿不到就显式失败。
   */
  async joinSeat(ticket: string, accountSession: string | null = null): Promise<PlayerViewDto> {
    this.ticket = ticket
    this.accountSession = accountSession
    if (this.connection.state === HubConnectionState.Disconnected) {
      await this.connection.start()
    }

    this.callbacks.onState('connected')
    const known = this.merge.eventAt
    const joined = normalizeSeatJoin(
      accountSession === null
        ? await this.connection.invoke<unknown>('JoinSeat', ticket, known)
        : await this.connection.invoke<unknown>('JoinSeatWithAccount', ticket, accountSession, known),
    )

    return this.applyJoinResult(joined, known)
  }

  /**
   * **自助入座**（D-0025）：登录后选一个空席位坐下，不需要任何票据。
   * @param seat 要坐的席位号。
   * @param accountSession 账号会话（必须；未登录时服务端会拒绝）。
   */
  async joinTable(seat: number, accountSession: string): Promise<PlayerViewDto> {
    // 自助入座不持有票据：清掉上一次的，避免后续重连（resync）拿着旧票据去试。
    this.ticket = ''
    this.accountSession = accountSession
    if (this.connection.state === HubConnectionState.Disconnected) {
      await this.connection.start()
    }

    this.callbacks.onState('connected')
    const known = this.merge.eventAt
    const joined = normalizeSeatJoin(
      await this.connection.invoke<unknown>('JoinTable', accountSession, seat, known),
    )

    return this.applyJoinResult(joined, known)
  }

  /**
   * 两条加入路径（票据 / 自助入座）的**共同后半段**。
   *
   * 放在一处是刻意的：凭据采纳、坏包拒绝、序号回退处理这些判据必须完全一致——
   * 分成两份实现，等于给其中一条路径留下"少校验一步"的后门。
   */
  private applyJoinResult(joined: ReturnType<typeof normalizeSeatJoin>, known: number): PlayerViewDto {
    if (joined === null) {
      throw new Error('服务端没有下发连接凭据：加入结果不可识别（D-0012）')
    }

    // 凭据先采纳：服务端在构造重连包**之前**就已轮换这条连接的凭据，
    // 后面即使判定"这份包不可信"，也不能把客户端留在已被服务端吊销的旧凭据上。
    this.credentialValue = joined.credential

    if (joined.bundle.droppedEvents > 0) {
      // 归一化就丢过条目 = 这份包不可信：显式失败、停在当前视图，绝不静默前进（宁可报错）。
      throw new Error(`重连包有 ${joined.bundle.droppedEvents} 条事件条目不可识别，已停在当前视图`)
    }

    const applied = applyBundle(joined.bundle, known)
    if (applied.diagnostic.length > 0) {
      // 坏数据不采纳：显式失败并停在当前视图（事件水位不前进），由人决定重连还是重建。
      throw new Error(applied.diagnostic)
    }

    if (joined.bundle.sequence < known) {
      // 快照比**事件窗口水位**还旧 = 服务端事件流回退（数据丢失 / 从旧备份恢复）：序号会被复用，
      // 必须清空合并态并以快照为新基线，否则字段闸会永久拒绝更新、信息集合还会把新事实当重复丢掉。
      this.merge.reset()
      this.callbacks.onDiagnostic(
        `服务端序号回退：快照 ${joined.bundle.sequence} < 本地已知 ${known}，已按快照重建视图`,
      )
    }

    this.merge.applySnapshot(joined.bundle.view, joined.bundle.sequence)
    const view = this.merge.snapshot()
    this.callbacks.onView(view)
    return view
  }

  /** 提交响应（幂等键由调用方持有）；没有连接凭据就不发命令。 */
  async submitResponse(
    requestId: string,
    optionValue: string,
    idempotencyKey: string,
    clientSequence: number,
  ): Promise<unknown> {
    return this.connection.invoke<unknown>(
      'SubmitResponse',
      this.requireCredential(),
      requestId,
      optionValue,
      idempotencyKey,
      clientSequence,
    )
  }

  /** 发起提名（提名者由服务端从凭据推导；客户端只给目标席位）。 */
  async nominate(nomineeSeat: number, idempotencyKey: string): Promise<unknown> {
    return this.connection.invoke<unknown>('Nominate', this.requireCredential(), nomineeSeat, idempotencyKey)
  }

  /** 在当前开放的提名上举手 / 放下（先举也算、过时不候；R-0017 目标形态）。 */
  async castVote(nominationIndex: number, voted: boolean, idempotencyKey: string): Promise<unknown> {
    return this.connection.invoke<unknown>(
      'CastVote',
      this.requireCredential(),
      nominationIndex,
      voted,
      idempotencyKey,
    )
  }

  /** 发起流放提议（发起人由服务端从凭据推导；目标必须是本局在局旅行者；R-0044 第 2 条）。 */
  async proposeExile(targetSeat: number, idempotencyKey: string): Promise<unknown> {
    return this.connection.invoke<unknown>(
      'ProposeExile',
      this.requireCredential(),
      targetSeat,
      idempotencyKey,
    )
  }

  /** 在当前开放的流放上举手 / 放下（全体在局玩家含死者；死者不耗投票标记，R-0044 第 4 条）。 */
  async castExileVote(exileIndex: number, voted: boolean, idempotencyKey: string): Promise<unknown> {
    return this.connection.invoke<unknown>(
      'CastExileVote',
      this.requireCredential(),
      exileIndex,
      voted,
      idempotencyKey,
    )
  }

  /** 屠夫本人在额外提名窗口里发起提名（R-0050；提名者由服务端从凭据推导，窗口不合法会被显式拒绝）。 */
  async nominateExtra(nomineeSeat: number, idempotencyKey: string): Promise<unknown> {
    return this.connection.invoke<unknown>(
      'NominateExtra',
      this.requireCredential(),
      nomineeSeat,
      idempotencyKey,
    )
  }

  /** 艺术家在白天向说书人提一个是 / 否问题（R-0040；幂等键由调用方持有）。 */
  async askArtistQuestion(question: string, idempotencyKey: string): Promise<unknown> {
    return this.connection.invoke<unknown>(
      'AskArtistQuestion',
      this.requireCredential(),
      question,
      idempotencyKey,
    )
  }

  /** 博学者在白天向说书人要两条信息（R-0057；内容由说书人给，命令没有参数）。 */
  async askSavantQuestion(idempotencyKey: string): Promise<unknown> {
    return this.connection.invoke<unknown>(
      'AskSavantQuestion',
      this.requireCredential(),
      idempotencyKey,
    )
  }

  /**
   * 杂耍艺人在自己的首个白天公开猜测 0–5 名玩家的角色（R-0057-B）。
   *
   * 猜测是**公开事实**：提交成功后所有玩家的白天视图都会带上它；猜对数当晚才给、只到本人。
   * 席位由凭据推导——这里不传自己的席位，前端不猜身份（D-0012）。
   */
  async makeJugglerGuesses(
    guesses: readonly JugglerGuessDto[],
    idempotencyKey: string,
  ): Promise<unknown> {
    return this.connection.invoke<unknown>(
      'MakeJugglerGuesses',
      this.requireCredential(),
      guesses,
      idempotencyKey,
    )
  }

  /**
   * 拉取一页复盘（R-0043 / D-0020）。
   *
   * 可见性闸在服务端：结束批次之前玩家调用会被显式拒绝；进行中玩家的任何收包都**不含**复盘字段，
   * 因此这里没有"提前探一探"的路径。坏数据不静默：解析不了就显式失败（宁可报错）。
   */
  async fetchReplay(afterSequence: number, pageSize: number): Promise<ReplayViewDto> {
    const replay = normalizeReplayView(
      await this.connection.invoke<unknown>(
        'GetReplay',
        this.requireCredential(),
        afterSequence,
        pageSize,
      ),
    )
    if (replay === null) {
      throw new Error('复盘数据不可识别：已停止前进（服务端数据是输入，不是保证）')
    }

    return replay
  }

  /** 主动补齐：以自身序号重新加入，取回缺口事件。 */
  async resync(): Promise<PlayerViewDto> {
    return this.joinSeat(this.ticket, this.accountSession)
  }

  /**
   * 停止连接。
   *
   * 不只 `await connection.stop()`：SignalR 在 stop 返回后到"底层连接真正关闭"之间有一个短窗口，
   * 窗口里再次 `start()` 会报
   * `Failed to start the HttpConnection before stop() was called`（实测踩到：换桌重建连接时）。
   * 所以这里等到状态确实变成 Disconnected 才返回——调用方接着建新连接就不会踩上。
   */
  async stop(): Promise<void> {
    await this.connection.stop()

    // 有界等待：正常情况下一两拍就到位；万一没到位也不把界面卡死。
    const deadline = Date.now() + 2000
    while (this.connection.state !== HubConnectionState.Disconnected && Date.now() < deadline) {
      await new Promise((resolve) => setTimeout(resolve, 20))
    }

    this.callbacks.onState('disconnected')
  }

  /** 推送统一入口：合并态是唯一写入者，说明性回调先于整份视图发出。 */
  private dispatchPush(push: PlayerPush): void {
    const pendingBefore = this.merge.pendingRequest
    if (!this.merge.applyPush(push)) {
      return
    }

    // 界面的"请求已作废 / 已了结"说明以"请求还挂在面板上"为前提，所以先发说明、再发视图。
    if (push.kind === 'Voided' && pendingBefore?.requestId === push.voided.requestId) {
      this.callbacks.onRequestVoided(push.voided)
    }

    if (push.kind === 'Answered' && pendingBefore?.requestId === push.answered.requestId) {
      this.callbacks.onRequestAnswered(push.answered)
    }

    this.callbacks.onView(this.merge.snapshot())
  }

  private async rejoin(): Promise<void> {
    try {
      await this.joinSeat(this.ticket, this.accountSession)
    } catch (error) {
      this.callbacks.onDiagnostic(`重连后重新加入失败：${error instanceof Error ? error.message : String(error)}`)
    }
  }

  /** 没有凭据就不发命令：服务端会拒绝，客户端也不该装作能发。 */
  private requireCredential(): string {
    if (this.credentialValue.length === 0) {
      throw new Error('尚未加入：没有连接凭据，不能提交命令')
    }

    return this.credentialValue
  }
}

/** 归一化后的重连包：在 wire 形状上补"被丢弃的事件条目数"（> 0 = 包不可信，加入必须显式失败）。 */
export interface NormalizedReconnectBundle extends ReconnectBundleDto {
  droppedEvents: number
}

/** 归一化后的加入结果（客户端内部形状，不是 wire 契约）。 */
export interface NormalizedSeatJoin {
  credential: string
  bundle: NormalizedReconnectBundle
}

/** 未知载荷 → 加入结果；凭据缺失 / 越界或重连包不可识别时返回 null（宁可加入失败，不带坏凭据继续）。 */
export function normalizeSeatJoin(raw: unknown): NormalizedSeatJoin | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const join = raw as Record<string, unknown>
  const credential = asCredential(join['credential'])
  const bundle = join['bundle']
  if (credential === null || bundle === null || typeof bundle !== 'object') {
    return null
  }

  return { credential, bundle: normalizeBundle(bundle) }
}

/** 未知载荷 → 操作请求；缺请求标识或序号时返回 null（表达不了先后就不合并）。 */
export function normalizeRequest(raw: unknown): OperationRequestDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const request = raw as Record<string, unknown>
  const requestId = asText(request['requestId'])
  const sequence = asCount(request['sequence'])
  if (requestId === null || sequence === null) {
    return null
  }

  return {
    sequence,
    requestId,
    seat: asCount(request['seat']) ?? 0,
    context: asSizedText(request['context'], 512) ?? '',
    options: asArray<unknown>(request['options'])
      .map(normalizeOption)
      .filter((option): option is DecisionOptionDto => option !== null),
    // 第二维是 R-0021 的两维选择（洗脑师）：缺失或损坏时降级为空数组 = 单维请求，
    // 不编造第二维、也不让界面崩（服务端数据是输入，不是保证）。
    secondaryOptions: asArray<unknown>(request['secondaryOptions'])
      .map(normalizeOption)
      .filter((option): option is DecisionOptionDto => option !== null),
  }
}

/** 未知载荷 → 信息类结果；**只有内容**（「可能为假」服务端刻意不下发），序号缺一不可。 */
export function normalizeInformation(raw: unknown): InformationResultDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const information = raw as Record<string, unknown>
  const ability = asText(information['ability'])
  const sequence = asCount(information['sequence'])
  if (ability === null || sequence === null) {
    return null
  }

  return { sequence, ability, content: asSizedText(information['content'], 4096) ?? '' }
}

/** 未知载荷 → 请求作废；缺请求标识 / 序号 / 原因时返回 null（宁可少显示，不编造原因）。 */
export function normalizeVoided(raw: unknown): OperationRequestVoidedDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const voided = raw as Record<string, unknown>
  const requestId = asText(voided['requestId'])
  const reason = asText(voided['reason'])
  const sequence = asCount(voided['sequence'])
  if (requestId === null || reason === null || sequence === null) {
    return null
  }

  return { sequence, requestId, reason, note: asSizedText(voided['note'], 512) }
}

/** 未知载荷 → 请求响应；缺请求标识 / 序号 / 选项 / 来源时返回 null（表达不了来源就不编）。 */
export function normalizeAnswered(raw: unknown): OperationRequestAnsweredDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const answered = raw as Record<string, unknown>
  const requestId = asText(answered['requestId'])
  const optionValue = asText(answered['optionValue'])
  const source = asText(answered['source'])
  const sequence = asCount(answered['sequence'])
  if (requestId === null || optionValue === null || source === null || sequence === null) {
    return null
  }

  return { sequence, requestId, optionValue, source, note: asSizedText(answered['note'], 512) }
}

/** 未知载荷 → 阶段开始；缺阶段名或序号时返回 null（不知道先后就不动页头）。 */
export function normalizePhaseStarted(raw: unknown): PhaseStartedDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const started = raw as Record<string, unknown>
  const phase = asText(started['phase'])
  const sequence = asCount(started['sequence'])
  return phase === null || sequence === null ? null : { sequence, phase }
}

/** 未知载荷 → 玩家白天投影；公开事实或序号不完整时返回 null（不编权限位、不猜先后）。 */
export function normalizePlayerDay(raw: unknown): PlayerDayDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const day = raw as Record<string, unknown>
  const publicView = normalizeDayView(day['publicView'])
  const sequence = asCount(day['sequence'])
  if (publicView === null || sequence === null) {
    return null
  }

  return {
    sequence,
    publicView,
    // 公开生死面（R-0022）：坏条目单条丢弃，整体不消失——少显示一条，不编一个状态。
    lives: asArray<unknown>(day['lives'])
      .map(normalizePlayerLife)
      .filter((entry): entry is PlayerLifeDto => entry !== null)
      .slice(0, MAX_PUBLIC_LIFE_ENTRIES),
    announcements: asArray<unknown>(day['announcements'])
      .map(normalizePlayerLife)
      .filter((entry): entry is PlayerLifeDto => entry !== null)
      .slice(0, MAX_PUBLIC_LIFE_ENTRIES),
    canNominate: asBoolean(day['canNominate']) ?? false,
    canVote: asBoolean(day['canVote']) ?? false,
    voted: asBoolean(day['voted']) ?? false,
    seatCollected: asBoolean(day['seatCollected']) ?? false,
    candidates: asArray<unknown>(day['candidates'])
      .map((candidate) => asCount(candidate))
      .filter((candidate): candidate is number => candidate !== null),
    // 旅行者与流放（D7）：权限位与候选都是服务端算好的公开使能条件；坏字段退化成 false / 空集。
    canProposeExile: asBoolean(day['canProposeExile']) ?? false,
    exileCandidates: asArray<unknown>(day['exileCandidates'])
      .map((candidate) => asCount(candidate))
      .filter((candidate): candidate is number => candidate !== null),
    canVoteExile: asBoolean(day['canVoteExile']) ?? false,
    exileVoted: asBoolean(day['exileVoted']) ?? false,
    exileSeatCollected: asBoolean(day['exileSeatCollected']) ?? false,
    canNominateExtra: asBoolean(day['canNominateExtra']) ?? false,
    extraNominationCandidates: asArray<unknown>(day['extraNominationCandidates'])
      .map((candidate) => asCount(candidate))
      .filter((candidate): candidate is number => candidate !== null),
    // 杂耍艺人的公开猜测入口（R-0057-B）：权限位由服务端算好，坏字段退化成 false。
    canMakeJugglerGuesses: asBoolean(day['canMakeJugglerGuesses']) ?? false,
  }
}

/** 未知载荷 → 重连包；缺序号按 0 处理（与本地序号 / 事件序号对不上时会在 applyBundle 显式诊断，而不是静默通过）。 */
export function normalizeBundle(raw: unknown): NormalizedReconnectBundle {
  const bundle = (raw ?? {}) as Record<string, unknown>
  const rawEvents = asArray<unknown>(bundle['events'])
  const events = rawEvents
    .map(normalizePlayerEvent)
    .filter((event): event is PlayerEventDto => event !== null)

  return {
    sequence: asCount(bundle['sequence']) ?? 0,
    // 视图解析与个人视图推送共用一份：不可识别时退回空视图（老口径：席位 0 / 阶段空串），不白屏。
    view: normalizePlayerView(bundle['view']) ?? emptyPlayerView(),
    events,
    // 被丢掉的条目不静默：加入路径据此显式失败（无序号 / 无类型的条目无法参与序号校验）。
    droppedEvents: rawEvents.length - events.length,
  }
}

/**
 * 未知载荷 → 玩家视图；缺席位 / 阶段时返回 null（表达不了"这是谁的视图"就不采纳）。
 *
 * 与 `JoinSeat` 快照里的 view 同一份解析：个人视图推送（`ReceivePlayerViewChanged`）与快照
 * 走同一个序号闸，解析口径也必须同一份（D-0014）。
 */
export function normalizePlayerView(raw: unknown): PlayerViewDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const view = raw as Record<string, unknown>
  const seat = asCount(view['seat'])
  const phase = asText(view['phase'])
  if (seat === null || phase === null) {
    return null
  }

  return {
    seat,
    phase,
    // 「我是谁」（R-0059）：有界文本——服务端数据是不可信输入，超长 / 非字符串退化成 null，不撑爆面板。
    character: asSizedText(view['character'], 64),
    alignment: asSizedText(view['alignment'], 32),
    pendingRequest: normalizeRequest(view['pendingRequest']),
    informationResults: asArray<unknown>(view['informationResults'])
      .map(normalizeInformation)
      .filter((information): information is InformationResultDto => information !== null),
    day: normalizePlayerDay(view['day']),
    outcome: normalizeGameOutcome(view['outcome']),
    klutzChoices: asArray<unknown>(view['klutzChoices'])
      .map(normalizeKlutzChoice)
      .filter((choice): choice is KlutzChoiceDto => choice !== null),
    seatNames: normalizeSeatNames(view['seatNames']),
    pendingQuestion: asSizedText(view['pendingQuestion'], 200),
    canAskArtistQuestion: asBoolean(view['canAskArtistQuestion']) ?? false,
    canAskSavantQuestion: asBoolean(view['canAskSavantQuestion']) ?? false,
    awaitingSavantQuestion: asBoolean(view['awaitingSavantQuestion']) ?? false,
    exhaustedAbilities: asTextArray(view['exhaustedAbilities']),
  }
}

/** 加入包里 view 不可识别时的降级形状（旧口径：席位 0 / 阶段空串），只作兜底、不编事实。 */
function emptyPlayerView(): PlayerViewDto {
  return {
    seat: 0,
    phase: '',
    character: null,
    alignment: null,
    pendingRequest: null,
    informationResults: [],
    day: null,
    outcome: null,
    klutzChoices: [],
    seatNames: [],
    pendingQuestion: null,
    canAskArtistQuestion: false,
    canAskSavantQuestion: false,
    awaitingSavantQuestion: false,
    exhaustedAbilities: [],
  }
}

/** 未知载荷 → 玩家可见事件；缺序号 / 缺类型的条目返回 null（由 normalizeBundle 计数，加入路径显式失败）。 */
export function normalizePlayerEvent(raw: unknown): PlayerEventDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const event = raw as Record<string, unknown>
  const sequence = asCount(event['sequence'])
  const kind = asText(event['kind'])
  if (sequence === null || kind === null) {
    return null
  }

  return {
    sequence,
    kind,
    phase: asText(event['phase']),
    request: normalizeRequest(event['request']),
    requestId: asText(event['requestId']),
    optionValue: asText(event['optionValue']),
    voidReason: asText(event['voidReason']),
    voidNote: asSizedText(event['voidNote'], 512),
    information: normalizeInformation(event['information']),
  }
}

/** 应用重连包的结果：快照序号（合并态的水位）与事件数据诊断。 */
export interface AppliedBundle {
  /** 快照序号：合并态用它决定各字段 / 信息条目的取舍。 */
  sequence: number
  /** 非空 = 补齐包的事件数据不可信，界面必须让人看见（不许静默继续）。 */
  diagnostic: string
}

/**
 * 校验重连包里的可见事件，并给出快照序号。
 *
 * 规则（架构 §5、D-0010；白名单投影见 D-0012 §4.3 / D-0013 §5）：
 * - 可见事件只要**严格递增**且落在 `(本地已知, 快照序号]` 就合法；条数天然小于序号区间长度，
 *   所以**不校验条数**；
 * - 越界 / 倒退 / 重复是坏数据 → 不采纳、报诊断（宁可报错，也不许"本地先按旧状态继续跑"）；
 * - **快照序号低于本地已知不是坏数据**：那是"推送先到、响应后到"的正常竞态
 *   （票据 player-information-resync-race）；合并态按序号逐字段取舍，迟到的快照只补不覆盖。
 */
export function applyBundle(bundle: ReconnectBundleDto, knownSequence: number): AppliedBundle {
  const ordered = [...bundle.events].sort((left, right) => left.sequence - right.sequence)
  let previous = knownSequence
  for (const [index, event] of ordered.entries()) {
    if (event.sequence > bundle.sequence) {
      return {
        sequence: bundle.sequence,
        diagnostic: `重连补齐序号越界：第 ${index + 1} 条是 ${event.sequence}，超出快照 ${bundle.sequence}`,
      }
    }

    if (event.sequence <= previous) {
      const kind = event.sequence === previous ? '重复' : '倒退'
      return {
        sequence: bundle.sequence,
        diagnostic: `重连补齐序号${kind}：第 ${index + 1} 条是 ${event.sequence}，应严格大于 ${previous}`,
      }
    }

    previous = event.sequence
  }

  return { sequence: bundle.sequence, diagnostic: '' }
}
