/**
 * 与真实宿主 /hub/game 的连接（真 SignalR）。
 *
 * 同步口径（架构 §5、D-0010）：重连 = 票据校验 → 重新取整份快照；
 * **不允许**用本地缓存"接着跑"。因此这里的姿态是：
 * - 断电 / 掉线后重连成功 → 重新 Join 并重新拉一次完整视图，不做本地增量猜测；
 * - 任何推送到达 → 整份替换视图，不让前端自己合并出服务端没有的状态；
 * - 视图带序号：Join / 刷新响应与推送同属一条序号流，**只接受序号不更旧的视图**
 *   （票据 player-information-resync-race：此前推送先到、join 响应后到会把视图拉回旧状态）。
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
import type { ReplayViewDto, StorytellerJoinDto, StorytellerViewDto } from '@/contracts/game'
import { asCredential, normalizeStorytellerView } from '@/display/format'
import { normalizeReplayView } from '@/display/replay'
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

/** 连接工厂：默认连真宿主；测试注入假连接以验证"序号闸"真的接在 Join / 刷新响应上。 */
export type StorytellerConnectionFactory = () => HubConnection

/** 默认连接：真 SignalR（与玩家侧同口径的自动重连与日志级别）。 */
function createStorytellerConnection(): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(HUB_PATH)
    .withAutomaticReconnect([0, 1000, 3000, 5000])
    .configureLogging(LogLevel.Warning)
    .build()
}

/**
 * 说书人连接网关：只负责"连上、收视图、发命令"三件事，不持有任何领域判断。
 */
export class StorytellerGateway {
  private readonly connection: HubConnection
  private ticket = ''
  /** 连接级凭据：只在内存中；票据才进 TicketStore，凭据绝不落盘。 */
  private credentialValue = ''
  /** 最近一次采纳的视图（含序号）：所有写入都经过它，旧序号只丢不覆盖。 */
  private current: StorytellerViewDto | null = null

  constructor(
    private readonly callbacks: GatewayCallbacks,
    createConnection: StorytellerConnectionFactory = createStorytellerConnection,
  ) {
    this.connection = createConnection()

    this.connection.on('ReceiveStorytellerViewChanged', (payload: unknown) => {
      this.applyView(normalizeStorytellerView(payload))
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
    this.applyView(joined.view)
    return this.current ?? joined.view
  }

  /** 主动拉取整份视图（刷新按钮 / 重连补齐）；没有凭据就不发。 */
  async refresh(): Promise<StorytellerViewDto> {
    if (this.credentialValue.length === 0) {
      throw new Error('尚未加入：没有连接凭据，不能刷新视图')
    }

    const view = normalizeStorytellerView(
      await this.connection.invoke<unknown>('GetStorytellerView', this.credentialValue),
    )
    this.applyView(view)
    return this.current ?? view
  }

  /**
   * 拉取一页复盘（D-0020）：说书人随时可看（实时面）；玩家面只在结束批次之后开放，闸在服务端。
   * 分页按事件序号推进；坏数据不静默（解析不了就显式失败）。
   */
  async fetchReplay(afterSequence: number, pageSize: number): Promise<ReplayViewDto> {
    if (this.credentialValue.length === 0) {
      throw new Error('尚未加入：没有连接凭据，不能读取复盘')
    }

    const replay = normalizeReplayView(
      await this.connection.invoke<unknown>(
        'GetReplay',
        this.credentialValue,
        afterSequence,
        pageSize,
      ),
    )
    if (replay === null) {
      throw new Error('复盘数据不可识别：已停止前进（服务端数据是输入，不是保证）')
    }

    return replay
  }

  /** 断开（保留票据，便于重连）。 */
  async stop(): Promise<void> {
    await this.connection.stop()
    this.callbacks.onState('disconnected')
  }

  /** 视图唯一写入点：序号不更旧的才采纳（相等幂等，更旧丢弃——迟到响应不许把面板拉回去）。 */
  private applyView(view: StorytellerViewDto): void {
    const next = newerView(this.current, view)
    if (next === this.current) {
      return
    }

    this.current = next
    this.callbacks.onView(next)
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

/**
 * 说书人视图的序号闸：推送与 Join / 刷新响应同属一条序号流，只接受序号不更旧的视图。
 * 此前 Join 响应会无条件整份替换，把窗口内到达的较新推送拉回旧状态（票据 player-information-resync-race）。
 */
export function newerView(
  current: StorytellerViewDto | null,
  incoming: StorytellerViewDto,
): StorytellerViewDto {
  return current !== null && incoming.sequence < current.sequence ? current : incoming
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
