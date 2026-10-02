/**
 * 玩家端连接：只做 JoinSeat / SubmitResponse 与推送接收（D-0013 §5）。
 *
 * 与说书人网关刻意分开：玩家连接**没有**、也不该有获取整份说书人视图的能力。
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
  OperationRequestDto,
  PlayerEventDto,
  ReconnectBundleDto,
  PlayerViewDto,
} from '@/contracts/game'
import { asArray, asNumber, asText, normalizeOption } from '@/display/format'
import { HUB_PATH, type GatewayState } from '@/services/connectionState'

/** 玩家侧回调。 */
export interface PlayerCallbacks {
  onRequest: (request: OperationRequestDto | null) => void
  onInformation: (information: InformationResultDto | null) => void
  onState: (state: GatewayState) => void
  onDiagnostic: (message: string) => void
}

/** 玩家连接网关。 */
export class PlayerGateway {
  private readonly connection: HubConnection
  private ticket = ''
  private lastSequence = 0

  constructor(private readonly callbacks: PlayerCallbacks) {
    this.connection = new HubConnectionBuilder()
      .withUrl(HUB_PATH)
      .withAutomaticReconnect([0, 1000, 3000, 5000])
      .configureLogging(LogLevel.Warning)
      .build()

    this.connection.on('ReceiveOperationRequest', (payload: unknown) => {
      callbacks.onRequest(normalizeRequest(payload))
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

  /**
   * 加入席位并取重连包（快照 + 从本客户端已知序号起的全部事件）。
   *
   * 同步口径（架构 §5、D-0010）：**快照 + 缺口事件一起用**，不许只取快照把事件丢掉。
   * 事件的作用是证明"从我的序号到快照序号之间没有缺口"：序号不连续就说明补齐不完整，
   * 那时**不装作没事**——报诊断并停在当前视图，由人决定重连还是重建（禁止本地先跑再说）。
   */
  async joinSeat(ticket: string): Promise<PlayerViewDto> {
    this.ticket = ticket
    if (this.connection.state === HubConnectionState.Disconnected) {
      await this.connection.start()
    }

    this.callbacks.onState('connected')
    const raw = await this.connection.invoke<unknown>('JoinSeat', ticket, this.lastSequence)
    const bundle = normalizeBundle(raw)
    const applied = applyBundle(bundle, this.lastSequence)
    this.lastSequence = applied.sequence
    // 空诊断 = "补齐完整"，不是一条消息：原样转发会让界面多出一个空条目（2026-10-02 批次实机发现）。
    if (applied.diagnostic.length > 0) {
      this.callbacks.onDiagnostic(applied.diagnostic)
    }
    this.callbacks.onRequest(bundle.view.pendingRequest)

    // 快照里的信息类结果按顺序重放；缺口事件里的新信息随后接上（按序号去重）。
    for (const information of applied.informationResults) {
      this.callbacks.onInformation(information)
    }

    return bundle.view
  }

  /** 提交响应（幂等键由调用方持有）。 */
  submitResponse(
    requestId: string,
    optionValue: string,
    idempotencyKey: string,
    clientSequence: number,
  ): Promise<unknown> {
    return this.connection.invoke<unknown>(
      'SubmitResponse',
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
    seat: asNumber(request['seat']) ?? 0,
    context: asText(request['context']) ?? '',
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

  return { ability, content: asText(information['content']) ?? '' }
}

/** 未知载荷 → 重连包；缺序号按 0 处理（那会让缺口校验显式失败，而不是静默放行）。 */
export function normalizeBundle(raw: unknown): ReconnectBundleDto {
  const bundle = (raw ?? {}) as Record<string, unknown>
  const view = (bundle['view'] ?? {}) as Record<string, unknown>

  return {
    sequence: asNumber(bundle['sequence']) ?? 0,
    view: {
      seat: asNumber(view['seat']) ?? 0,
      phase: asText(view['phase']) ?? '',
      pendingRequest: normalizeRequest(view['pendingRequest']),
      informationResults: asArray<unknown>(view['informationResults'])
        .map(normalizeInformation)
        .filter((information): information is InformationResultDto => information !== null),
    },
    events: asArray<unknown>(bundle['events'])
      .map(normalizePlayerEvent)
      .filter((event): event is PlayerEventDto => event !== null),
  }
}

/** 未知载荷 → 玩家可见事件；缺序号的条目被丢掉（无序号就无法证明补齐完整）。 */
export function normalizePlayerEvent(raw: unknown): PlayerEventDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const event = raw as Record<string, unknown>
  const sequence = asNumber(event['sequence'])
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
    voidNote: asText(event['voidNote']),
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
 * 规则（架构 §5）：
 * - 快照序号是新的起点；事件必须**恰好**覆盖 `已知序号+1 … 快照序号` 这一段；
 * - 序号出现缺口或倒退 → 不更新序号、报诊断（宁可报错，也不许"本地先按旧状态继续跑"）；
 * - 事件里的信息类结果按序号顺序接在快照之后。
 */
export function applyBundle(bundle: ReconnectBundleDto, knownSequence: number): AppliedBundle {
  const expected = bundle.sequence - knownSequence
  if (expected < 0) {
    return {
      sequence: knownSequence,
      informationResults: [],
      diagnostic: `重连补齐序号倒退：本地已知 ${knownSequence}，服务端快照 ${bundle.sequence}`,
    }
  }

  if (bundle.events.length !== expected) {
    return {
      sequence: knownSequence,
      informationResults: [],
      diagnostic:
        `重连补齐有缺口：本地已知 ${knownSequence}，快照 ${bundle.sequence} `
        + `应补 ${expected} 条事件，实际收到 ${bundle.events.length} 条`,
    }
  }

  const ordered = [...bundle.events].sort((left, right) => left.sequence - right.sequence)
  for (const [index, event] of ordered.entries()) {
    if (event.sequence !== knownSequence + index + 1) {
      return {
        sequence: knownSequence,
        informationResults: [],
        diagnostic: `重连补齐序号不连续：第 ${index + 1} 条是 ${event.sequence}，应为 ${knownSequence + index + 1}`,
      }
    }
  }

  return {
    sequence: bundle.sequence,
    informationResults: ordered
      .map((event) => event.information)
      .filter((information): information is InformationResultDto => information !== null),
    diagnostic: '',
  }
}
