/**
 * 与真实宿主 /hub/game 的连接（真 SignalR）。
 *
 * 同步口径（架构 §5、D-0010）：重连 = 票据校验 → 重新取整份快照；
 * **不允许**用本地缓存"接着跑"。因此这里的姿态是：
 * - 断电 / 掉线后重连成功 → 重新 Join 并重新拉一次完整视图，不做本地增量猜测；
 * - 任何推送到达 → 整份替换视图，不让前端自己合并出服务端没有的状态。
 * 命令的幂等键由调用方持有，重试复用同一个键。
 *
 * 零信任口径（D-0012）：JoinStoryteller 下发的**连接级凭据**只存在内存里，
 * 每条命令（含刷新视图）随参数出示；掉线重连必须重新出示票据换新凭据。
 */
import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
} from '@microsoft/signalr'
import type { StorytellerJoinDto, StorytellerViewDto } from '@/contracts/game'
import { asCredential, normalizeStorytellerView } from '@/display/format'
import { HUB_PATH, type GatewayState } from '@/services/connectionState'

export type { GatewayState } from '@/services/connectionState'

/** Hub 入口相对路径：与 Vite 代理 / 生产同源部署一致。 */
export { HUB_PATH } from '@/services/connectionState'

/** 掉线重连与推送到达时的回调集合。 */
export interface GatewayCallbacks {
  /** 视图整份替换。 */
  onView: (view: StorytellerViewDto) => void
  /** 连接状态变化。 */
  onState: (state: GatewayState) => void
  /** 需要人看懂的诊断信息（不吞异常）。 */
  onDiagnostic: (message: string) => void
}

/**
 * 说书人连接网关：只负责"连上、收视图、发命令"三件事，不持有任何领域判断。
 */
export class StorytellerGateway {
  private readonly connection: HubConnection
  private ticket = ''
  /** 连接级凭据：只在内存中；票据才进 TicketStore，凭据绝不落盘。 */
  private credentialValue = ''

  constructor(private readonly callbacks: GatewayCallbacks) {
    this.connection = new HubConnectionBuilder()
      .withUrl(HUB_PATH)
      .withAutomaticReconnect([0, 1000, 3000, 5000])
      .configureLogging(LogLevel.Warning)
      .build()

    this.connection.on('ReceiveStorytellerViewChanged', (payload: unknown) => {
      callbacks.onView(normalizeStorytellerView(payload))
    })

    // 重连成功 = 换了一条连接：身份绑定与视图都要重新建立，绝不沿用旧连接的状态。
    this.connection.onreconnected(() => {
      callbacks.onState('connected')
      void this.rejoin()
    })
    this.connection.onreconnecting(() => callbacks.onState('reconnecting'))
    this.connection.onclose(() => callbacks.onState('disconnected'))
  }

  /** 原始连接：命令客户端用 InvokeAsync 发命令。 */
  get raw(): HubConnection {
    return this.connection
  }

  /** 当前连接凭据（只读；命令发送方持有它，不渲染、不落盘）。 */
  get credential(): string {
    return this.credentialValue
  }

  get state(): GatewayState {
    return mapState(this.connection.state)
  }

  /** 连接并加入说书人席位，返回首次视图；拿不到连接凭据就显式失败。 */
  async join(ticket: string): Promise<StorytellerViewDto> {
    this.ticket = ticket
    this.callbacks.onState(this.state)
    if (this.connection.state === HubConnectionState.Disconnected) {
      await this.connection.start()
    }

    this.callbacks.onState('connected')
    const joined = normalizeStorytellerJoin(
      await this.connection.invoke<unknown>('JoinStoryteller', ticket),
    )
    if (joined === null) {
      throw new Error('服务端没有下发连接凭据：加入结果不可识别（D-0012）')
    }

    this.credentialValue = joined.credential
    this.callbacks.onView(joined.view)
    return joined.view
  }

  /** 主动拉取整份视图（刷新按钮 / 重连补齐）；没有凭据就不发。 */
  async refresh(): Promise<StorytellerViewDto> {
    if (this.credentialValue.length === 0) {
      throw new Error('尚未加入：没有连接凭据，不能刷新视图')
    }

    const view = normalizeStorytellerView(
      await this.connection.invoke<unknown>('GetStorytellerView', this.credentialValue),
    )
    this.callbacks.onView(view)
    return view
  }

  /** 断开（保留票据，便于重连）。 */
  async stop(): Promise<void> {
    await this.connection.stop()
    this.callbacks.onState('disconnected')
  }

  private async rejoin(): Promise<void> {
    try {
      await this.join(this.ticket)
    } catch (error) {
      this.callbacks.onDiagnostic(`重连后重新加入失败：${describe(error)}`)
    }
  }
}

/** 未知载荷 → 加入结果；凭据缺失 / 越界或视图不可识别时返回 null。 */
export function normalizeStorytellerJoin(raw: unknown): StorytellerJoinDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const join = raw as Record<string, unknown>
  const credential = asCredential(join['credential'])
  const view = join['view']
  if (credential === null || view === null || typeof view !== 'object') {
    return null
  }

  return { credential, view: normalizeStorytellerView(view) }
}

function mapState(state: HubConnectionState): GatewayState {
  switch (state) {
    case HubConnectionState.Connected:
      return 'connected'
    case HubConnectionState.Connecting:
      return 'connecting'
    case HubConnectionState.Reconnecting:
      return 'reconnecting'
    default:
      return 'disconnected'
  }
}

/** 把未知异常转成一句人话（不吞异常、不猜原因）。 */
export function describe(error: unknown): string {
  if (error instanceof Error) {
    return error.message
  }

  return String(error)
}
