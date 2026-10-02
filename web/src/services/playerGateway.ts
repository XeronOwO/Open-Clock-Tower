/**
 * 玩家端连接：只做 JoinSeat / SubmitResponse 与推送接收（D-0013 §5）。
 *
 * 与说书人网关刻意分开：玩家连接**没有**、也不该有获取整份说书人视图的能力。
 * 零信任（D-0012）：JoinSeat 下发的**连接级凭据**只存在内存里，每条命令随参数出示；
 * 掉线重连必须重新用票据加入并换新凭据——旧连接的凭据在新连接上无效。
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
  OperationRequestAnsweredDto,
  OperationRequestDto,
  OperationRequestVoidedDto,
  PhaseStartedDto,
  PlayerEventDto,
  ReconnectBundleDto,
  PlayerViewDto,
} from '@/contracts/game'
import {
  asArray,
  asCount,
  asCredential,
  asSizedText,
  asText,
  normalizeOption,
} from '@/display/format'
import { HUB_PATH, type GatewayState } from '@/services/connectionState'

/** 玩家侧回调。 */
export interface PlayerCallbacks {
  onRequest: (request: OperationRequestDto | null) => void
  /** 请求被作废（强制作废 / 依赖失效 / 阶段推进…）：界面据此清掉当前请求并说明原因。 */
  onRequestVoided: (voided: OperationRequestVoidedDto) => void
  /** 请求已被响应（玩家本人或说书人代填）：界面据此清掉当前请求。 */
  onRequestAnswered: (answered: OperationRequestAnsweredDto) => void
  /** 阶段开始（公开信息）：页头阶段随服务端更新，不需要手动补齐。 */
  onPhaseStarted: (phase: string) => void
  onInformation: (information: InformationResultDto | null) => void
  onState: (state: GatewayState) => void
  onDiagnostic: (message: string) => void
}

/** 玩家连接网关。 */
export class PlayerGateway {
  private readonly connection: HubConnection
  private ticket = ''
  private lastSequence = 0
  /** 连接级凭据：只在内存中；票据才进 TicketStore，凭据绝不落盘。 */
  private credentialValue = ''

  constructor(private readonly callbacks: PlayerCallbacks) {
    this.connection = new HubConnectionBuilder()
      .withUrl(HUB_PATH)
      .withAutomaticReconnect([0, 1000, 3000, 5000])
      .configureLogging(LogLevel.Warning)
      .build()

    this.connection.on('ReceiveOperationRequest', (payload: unknown) => {
      callbacks.onRequest(normalizeRequest(payload))
    })
    this.connection.on('ReceiveOperationRequestVoided', (payload: unknown) => {
      const voided = normalizeVoided(payload)
      if (voided !== null) {
        callbacks.onRequestVoided(voided)
      }
    })
    this.connection.on('ReceiveOperationRequestAnswered', (payload: unknown) => {
      const answered = normalizeAnswered(payload)
      if (answered !== null) {
        callbacks.onRequestAnswered(answered)
      }
    })
    this.connection.on('ReceivePhaseStarted', (payload: unknown) => {
      const started = normalizePhaseStarted(payload)
      if (started !== null) {
        callbacks.onPhaseStarted(started.phase)
      }
    })
    this.connection.on('ReceiveInformationResult', (payload: unknown) => {
      callbacks.onInformation(normalizeInformation(payload))
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
   * 但事件必须是好数据：条目不可识别或序号越界 / 倒退 / 重复就**不装作没事**——显式失败并停在
   * 当前视图，绝不采纳这份包（禁止本地先跑再说）。
   * 零信任口径（D-0012）：加入结果里的连接级凭据是后续发命令的唯一凭据；拿不到就显式失败。
   */
  async joinSeat(ticket: string): Promise<PlayerViewDto> {
    this.ticket = ticket
    if (this.connection.state === HubConnectionState.Disconnected) {
      await this.connection.start()
    }

    this.callbacks.onState('connected')
    const joined = normalizeSeatJoin(
      await this.connection.invoke<unknown>('JoinSeat', ticket, this.lastSequence),
    )
    if (joined === null) {
      throw new Error('服务端没有下发连接凭据：加入结果不可识别（D-0012）')
    }

    if (joined.bundle.droppedEvents > 0) {
      // 归一化就丢过条目 = 这份包不可信：显式失败、停在当前视图，绝不静默前进（宁可报错）。
      throw new Error(`重连包有 ${joined.bundle.droppedEvents} 条事件条目不可识别，已停在当前视图`)
    }

    const bundle = joined.bundle
    const applied = applyBundle(bundle, this.lastSequence)
    if (applied.diagnostic.length > 0) {
      // 坏数据不采纳：显式失败并停在当前视图（watermark 不前进），由人决定重连还是重建。
      throw new Error(applied.diagnostic)
    }

    this.credentialValue = joined.credential
    this.lastSequence = applied.sequence
    this.callbacks.onRequest(bundle.view.pendingRequest)

    // 快照视图由调用方按 `bundle.view` 呈现；窗口内可见事件里的新信息随后接上。
    for (const information of applied.informationResults) {
      this.callbacks.onInformation(information)
    }

    return bundle.view
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

  /** 主动补齐：以自身序号重新加入，取回缺口事件。 */
  async resync(): Promise<PlayerViewDto> {
    return this.joinSeat(this.ticket)
  }

  async stop(): Promise<void> {
    await this.connection.stop()
    this.callbacks.onState('disconnected')
  }

  private async rejoin(): Promise<void> {
    try {
      await this.joinSeat(this.ticket)
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

/** 未知载荷 → 操作请求；缺关键字段时返回 null（宁可少显示，不编造请求）。 */
export function normalizeRequest(raw: unknown): OperationRequestDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const request = raw as Record<string, unknown>
  const requestId = asText(request['requestId'])
  if (requestId === null) {
    return null
  }

  return {
    requestId,
    seat: asCount(request['seat']) ?? 0,
    context: asSizedText(request['context'], 512) ?? '',
    options: asArray<unknown>(request['options'])
      .map(normalizeOption)
      .filter((option): option is DecisionOptionDto => option !== null),
  }
}

/** 未知载荷 → 信息类结果；**只有内容**，没有"可能为假"标记（服务端刻意不下发）。 */
export function normalizeInformation(raw: unknown): InformationResultDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const information = raw as Record<string, unknown>
  const ability = asText(information['ability'])
  if (ability === null) {
    return null
  }

  return { ability, content: asSizedText(information['content'], 4096) ?? '' }
}

/** 未知载荷 → 请求作废；缺请求标识或原因时返回 null（宁可少显示，不编造原因）。 */
export function normalizeVoided(raw: unknown): OperationRequestVoidedDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const voided = raw as Record<string, unknown>
  const requestId = asText(voided['requestId'])
  const reason = asText(voided['reason'])
  if (requestId === null || reason === null) {
    return null
  }

  return { requestId, reason, note: asSizedText(voided['note'], 512) }
}

/** 未知载荷 → 请求响应；缺请求标识 / 选项 / 来源时返回 null（表达不了来源就不编）。 */
export function normalizeAnswered(raw: unknown): OperationRequestAnsweredDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const answered = raw as Record<string, unknown>
  const requestId = asText(answered['requestId'])
  const optionValue = asText(answered['optionValue'])
  const source = asText(answered['source'])
  if (requestId === null || optionValue === null || source === null) {
    return null
  }

  return { requestId, optionValue, source, note: asSizedText(answered['note'], 512) }
}

/** 未知载荷 → 阶段开始；缺阶段名时返回 null（不知道阶段就不动页头）。 */
export function normalizePhaseStarted(raw: unknown): PhaseStartedDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const phase = asText((raw as Record<string, unknown>)['phase'])
  return phase === null ? null : { phase }
}

/** 未知载荷 → 重连包；缺序号按 0 处理（与本地序号 / 事件序号对不上时会在 applyBundle 显式诊断，而不是静默通过）。 */
export function normalizeBundle(raw: unknown): NormalizedReconnectBundle {
  const bundle = (raw ?? {}) as Record<string, unknown>
  const view = (bundle['view'] ?? {}) as Record<string, unknown>
  const rawEvents = asArray<unknown>(bundle['events'])
  const events = rawEvents
    .map(normalizePlayerEvent)
    .filter((event): event is PlayerEventDto => event !== null)

  return {
    sequence: asCount(bundle['sequence']) ?? 0,
    view: {
      seat: asCount(view['seat']) ?? 0,
      phase: asText(view['phase']) ?? '',
      pendingRequest: normalizeRequest(view['pendingRequest']),
      informationResults: asArray<unknown>(view['informationResults'])
        .map(normalizeInformation)
        .filter((information): information is InformationResultDto => information !== null),
    },
    events,
    // 被丢掉的条目不静默：加入路径据此显式失败（无序号 / 无类型的条目无法参与序号校验）。
    droppedEvents: rawEvents.length - events.length,
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

/** 应用重连包的结果：新的客户端序号、要交给界面的信息类结果、以及缺口诊断。 */
export interface AppliedBundle {
  sequence: number
  informationResults: InformationResultDto[]
  /** 非空 = 补齐不完整，界面必须让人看见（不许静默继续）。 */
  diagnostic: string
}

/**
 * 把重连包折叠成"客户端现在认哪个序号、新收到了什么"。
 *
 * 规则（架构 §5、D-0010；白名单投影见 D-0012 §4.3 / D-0013 §5）：
 * - **快照序号是新的 watermark**：服务端已读全量事件后按接收者投影，连续性由它保证；
 *   可见事件条数天然小于序号区间长度（他人事件根本不下发），所以**不校验条数**；
 * - 可见事件只要**严格递增**且落在 `(本地已知, 快照序号]` 就合法；越界 / 倒退 / 重复是坏数据
 *   → 不更新序号、报诊断（宁可报错，也不许"本地先按旧状态继续跑"）；
 * - 事件里的信息类结果按序号顺序接在快照之后。
 */
export function applyBundle(bundle: ReconnectBundleDto, knownSequence: number): AppliedBundle {
  if (bundle.sequence < knownSequence) {
    return {
      sequence: knownSequence,
      informationResults: [],
      diagnostic: `重连补齐序号倒退：本地已知 ${knownSequence}，服务端快照 ${bundle.sequence}`,
    }
  }

  const ordered = [...bundle.events].sort((left, right) => left.sequence - right.sequence)
  let previous = knownSequence
  for (const [index, event] of ordered.entries()) {
    if (event.sequence > bundle.sequence) {
      return {
        sequence: knownSequence,
        informationResults: [],
        diagnostic: `重连补齐序号越界：第 ${index + 1} 条是 ${event.sequence}，超出快照 ${bundle.sequence}`,
      }
    }

    if (event.sequence <= previous) {
      const kind = event.sequence === previous ? '重复' : '倒退'
      return {
        sequence: knownSequence,
        informationResults: [],
        diagnostic: `重连补齐序号${kind}：第 ${index + 1} 条是 ${event.sequence}，应严格大于 ${previous}`,
      }
    }

    previous = event.sequence
  }

  return {
    sequence: bundle.sequence,
    informationResults: ordered
      .map((event) => event.information)
      .filter((information): information is InformationResultDto => information !== null),
    diagnostic: '',
  }
}
