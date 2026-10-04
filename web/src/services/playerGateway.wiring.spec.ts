import { HubConnectionState, type HubConnection } from '@microsoft/signalr'
import { describe, expect, it } from 'vitest'
import type { InformationResultDto, PlayerViewDto, ReconnectBundleDto } from '@/contracts/game'
import { PlayerGateway, normalizeRequest, type PlayerCallbacks } from '@/services/playerGateway'

/**
 * 网关接线（票据 `player-information-resync-race` 行 1 / 行 3 / 行 4）：
 * `playerViewMerge.spec.ts` 证明的是纯函数，这里证明**网关真的把推送与快照接在合并态上**——
 * 用假连接确定性地制造"补齐响应还在飞、推送先到"。
 */

/** 假连接：只实现网关用到的那几个成员（`as unknown as HubConnection` 注入）。 */
class FakeConnection {
  /** 已连接（真流程里加入时连接已 start 过）：invoke 因此在调用栈内同步发生，测试能确定地扣住它。 */
  state = HubConnectionState.Connected
  response: unknown = null
  /** true = 下一次 invoke 不立刻落地，由测试决定何时放行（模拟"响应在飞"）。 */
  holdNext = false
  readonly invocations: Array<{ method: string; args: unknown[] }> = []
  private pending: ((value: unknown) => void) | null = null
  private readonly handlers = new Map<string, (...args: unknown[]) => void>()

  on(method: string, handler: (...args: unknown[]) => void): void {
    this.handlers.set(method, handler)
  }

  onreconnecting(_handler: (error?: Error) => void): void {}

  onreconnected(_handler: (connectionId?: string) => void): void {}

  onclose(_handler: (error?: Error) => void): void {}

  async start(): Promise<void> {
    this.state = HubConnectionState.Connected
  }

  async stop(): Promise<void> {
    this.state = HubConnectionState.Disconnected
  }

  invoke(method: string, ...args: unknown[]): Promise<unknown> {
    this.invocations.push({ method, args })
    if (!this.holdNext) {
      return Promise.resolve(this.response)
    }

    this.holdNext = false
    return new Promise((resolve) => {
      this.pending = resolve
    })
  }

  /** 模拟服务端推送（可带多个参数，如 PlayerViewChanged 的序号 + 视图）。 */
  receive(method: string, ...args: unknown[]): void {
    this.handlers.get(method)?.(...args)
  }

  /** 放行被扣住的响应。 */
  release(value: unknown): void {
    const resolve = this.pending
    this.pending = null
    resolve?.(value)
  }
}

const credential = (fill: string): string => fill.repeat(43)

const view = (overrides: Partial<PlayerViewDto> = {}): PlayerViewDto => ({
  seat: 1,
  phase: 'FirstNight',
  pendingRequest: null,
  informationResults: [],
  day: null,
  outcome: null,
  klutzChoices: [],
  seatNames: [],
  pendingQuestion: null,
  canAskArtistQuestion: false,
  exhaustedAbilities: [],
  ...overrides,
})

const information = (sequence: number, ability: string, content = `${ability} 的信息`): InformationResultDto => ({
  sequence,
  ability,
  content,
})

const joinResult = (
  sequence: number,
  viewValue: PlayerViewDto,
  events: ReconnectBundleDto['events'] = [],
): unknown => ({
  credential: credential('C'),
  bundle: { sequence, view: viewValue, events },
})

/** 回调记录器：视图 / 说明 / 诊断按发生顺序记下来，顺序本身也是断言对象。 */
function recorder(): {
  callbacks: PlayerCallbacks
  views: PlayerViewDto[]
  diagnostics: string[]
  settled: string[]
} {
  const views: PlayerViewDto[] = []
  const diagnostics: string[] = []
  const settled: string[] = []
  return {
    views,
    diagnostics,
    settled,
    callbacks: {
      onView: (next) => {
        views.push(next)
        settled.push('view')
      },
      onRequestVoided: () => settled.push('voided'),
      onRequestAnswered: () => settled.push('answered'),
      onState: () => {},
      onDiagnostic: (message) => diagnostics.push(message),
    },
  }
}

function gatewayWith(fake: FakeConnection, callbacks: PlayerCallbacks): PlayerGateway {
  return new PlayerGateway(callbacks, () => fake as unknown as HubConnection)
}

describe('玩家网关接线：补齐窗口', () => {
  it('补齐响应还在飞时到达的推送：放行后两条信息都在、不重复（票据行 1）', async () => {
    const fake = new FakeConnection()
    const record = recorder()
    const gateway = gatewayWith(fake, record.callbacks)

    fake.holdNext = true
    const joining = gateway.joinSeat('ticket-1')
    // 响应在飞：推送先到（旧语义下它会被随后落地的快照整体覆盖）。
    fake.receive('ReceiveInformationResult', { sequence: 6, ability: 'dreamer', content: '窗口内到达' })
    fake.release(joinResult(5, view({ informationResults: [information(5, 'clockmaker', '快照里已有')] })))
    await joining

    const last = record.views.at(-1)
    expect(last?.informationResults.map((item) => item.sequence)).toEqual([5, 6])
    expect(last?.informationResults.map((item) => item.content)).toEqual(['快照里已有', '窗口内到达'])
    expect(gateway.credential).toBe(credential('C'))
  })

  it('推送不推高送去 JoinSeat 的已知序号：事件窗口不被截断（架构 §5）', async () => {
    const fake = new FakeConnection()
    const record = recorder()
    const gateway = gatewayWith(fake, record.callbacks)

    fake.response = joinResult(10, view())
    await gateway.joinSeat('ticket-1')
    fake.receive('ReceiveInformationResult', { sequence: 12, ability: 'oracle' })
    await gateway.resync()

    expect(fake.invocations[0]?.args[1]).toBe(0)
    expect(fake.invocations.at(-1)?.args[1]).toBe(10)
  })

  it('重连包不可信被拒时仍采纳新凭据（不留在服务端已吊销的旧凭据上）', async () => {
    const fake = new FakeConnection()
    const record = recorder()
    const gateway = gatewayWith(fake, record.callbacks)

    fake.response = {
      credential: credential('N'),
      // 缺序号的事件条目 → droppedEvents > 0 → 这份包不可信。
      bundle: { sequence: 3, view: view(), events: [{ kind: 'PhaseStarted' }] },
    }

    await expect(gateway.joinSeat('ticket-1')).rejects.toThrow(/不可识别/)
    expect(gateway.credential).toBe(credential('N'))
  })

  it('服务端序号回退：清空合并态、按快照重建、出诊断（复用的信息序号不被吞掉）', async () => {
    const fake = new FakeConnection()
    const record = recorder()
    const gateway = gatewayWith(fake, record.callbacks)

    fake.response = joinResult(
      12,
      view({ phase: 'Day', informationResults: [information(11, 'oracle', '回退前的旧事实')] }),
    )
    await gateway.joinSeat('ticket-1')

    fake.response = joinResult(
      10,
      view({ phase: 'FirstNight', informationResults: [information(11, 'dreamer', '回退后的新事实')] }),
    )
    await gateway.joinSeat('ticket-1')

    const last = record.views.at(-1)
    expect(last?.phase).toBe('FirstNight')
    expect(last?.informationResults.map((item) => item.content)).toEqual(['回退后的新事实'])
    expect(record.diagnostics.some((message) => message.includes('序号回退'))).toBe(true)
  })

  it('作废推送：先发说明、再发视图（界面靠"请求还挂着"显示原因）', async () => {
    const fake = new FakeConnection()
    const record = recorder()
    const gateway = gatewayWith(fake, record.callbacks)

    fake.response = joinResult(
      5,
      view({
        pendingRequest: { sequence: 5, requestId: 'r5', seat: 1, context: '请选择目标', options: [], secondaryOptions: [] },
      }),
    )
    await gateway.joinSeat('ticket-1')
    record.settled.length = 0

    fake.receive('ReceiveOperationRequestVoided', {
      sequence: 6,
      requestId: 'r5',
      reason: 'StorytellerForce',
      note: null,
    })

    expect(record.settled).toEqual(['voided', 'view'])
    expect(record.views.at(-1)?.pendingRequest).toBeNull()
  })

  it('本人视图推送：按快照口径合并（艺术家入口出现 / 等待态可见 / 用尽撤下）', async () => {
    const fake = new FakeConnection()
    const record = recorder()
    const gateway = gatewayWith(fake, record.callbacks)

    fake.response = joinResult(5, view({ phase: 'FirstNight', canAskArtistQuestion: false }))
    await gateway.joinSeat('ticket-1')

    // 白天开始：服务端补推一份本人视图 → 提问入口出现（修复前该位只在快照里更新，入口永不出现）。
    fake.receive('ReceivePlayerViewChanged', 51, view({ phase: 'Day', canAskArtistQuestion: true }))
    expect(record.views.at(-1)?.canAskArtistQuestion).toBe(true)

    // 提问后：权限位关闭，但等待态带着问题全文（面板靠 pendingQuestion 保持可见，R-0040）。
    fake.receive(
      'ReceivePlayerViewChanged',
      52,
      view({ phase: 'Day', canAskArtistQuestion: false, pendingQuestion: '1 号是爪牙吗？' }),
    )
    expect(record.views.at(-1)?.pendingQuestion).toBe('1 号是爪牙吗？')
    expect(record.views.at(-1)?.canAskArtistQuestion).toBe(false)

    // 回答结清：问题清空、用尽能力下发（面板撤下）。
    fake.receive(
      'ReceivePlayerViewChanged',
      53,
      view({ phase: 'Day', canAskArtistQuestion: false, pendingQuestion: null, exhaustedAbilities: ['artist'] }),
    )
    expect(record.views.at(-1)?.exhaustedAbilities).toEqual(['artist'])
  })

  it('本人视图推送：旧序号被字段闸挡下，缺序号 / 不可识别视图不采纳（宁可少更新一次）', async () => {
    const fake = new FakeConnection()
    const record = recorder()
    const gateway = gatewayWith(fake, record.callbacks)

    fake.response = joinResult(50, view({ phase: 'Day', canAskArtistQuestion: true }))
    await gateway.joinSeat('ticket-1')
    const before = record.views.length

    // 迟到的旧视图：序号低于字段水位，不覆盖。
    fake.receive('ReceivePlayerViewChanged', 40, view({ phase: 'Day', canAskArtistQuestion: false }))
    expect(record.views.length).toBe(before)

    // 缺序号的推送：表达不了先后就不合并。
    fake.receive('ReceivePlayerViewChanged', null, view({ phase: 'Day', canAskArtistQuestion: false }))
    expect(record.views.length).toBe(before)

    // 不可识别的视图（缺席位 / 阶段）：不采纳。
    fake.receive('ReceivePlayerViewChanged', 60, { phase: 'Day' })
    expect(record.views.length).toBe(before)
    expect(record.views.at(-1)?.canAskArtistQuestion).toBe(true)
  })
})

describe('账号加入路径（D-0021）', () => {
  it('带账号会话走 JoinSeatWithAccount；补齐重连继续带同一会话', async () => {
    const fake = new FakeConnection()
    const record = recorder()
    const gateway = gatewayWith(fake, record.callbacks)

    fake.response = joinResult(3, view())
    await gateway.joinSeat('ticket-1', 'session-9')

    expect(fake.invocations[0]).toEqual({
      method: 'JoinSeatWithAccount',
      args: ['ticket-1', 'session-9', 0],
    })

    await gateway.resync()

    expect(fake.invocations.at(-1)).toEqual({
      method: 'JoinSeatWithAccount',
      args: ['ticket-1', 'session-9', 3],
    })
  })

  it('不带账号会话仍是原来的 JoinSeat（游客路径不受影响）', async () => {
    const fake = new FakeConnection()
    const record = recorder()
    const gateway = gatewayWith(fake, record.callbacks)

    fake.response = joinResult(1, view())
    await gateway.joinSeat('ticket-1')

    expect(fake.invocations[0]).toEqual({ method: 'JoinSeat', args: ['ticket-1', 0] })
  })

  it('只凭账号（票据留空）也能加入：空票据原样下发，服务端按绑定解出席位', async () => {
    const fake = new FakeConnection()
    const record = recorder()
    const gateway = gatewayWith(fake, record.callbacks)

    fake.response = joinResult(0, view())
    await gateway.joinSeat('', 'session-9')

    expect(fake.invocations[0]).toEqual({
      method: 'JoinSeatWithAccount',
      args: ['', 'session-9', 0],
    })
  })
})

describe('normalizeRequest 的两维选择（R-0021）', () => {
  it('保留第二维；字段缺失或损坏时降级为空数组，不编造第二维', () => {
    const twoDimensional = normalizeRequest({
      sequence: 7,
      requestId: 'r7',
      seat: 1,
      context: '洗脑师选择一名玩家和一个善良角色',
      options: [{ value: 'seat:1', preview: '1 号玩家' }],
      secondaryOptions: [{ value: 'clockmaker', preview: '钟表匠' }],
    })
    expect(twoDimensional?.secondaryOptions).toEqual([{ value: 'clockmaker', preview: '钟表匠' }])

    const missing = normalizeRequest({
      sequence: 8,
      requestId: 'r8',
      seat: 1,
      context: '单维请求',
      options: [],
    })
    expect(missing?.secondaryOptions).toEqual([])

    const broken = normalizeRequest({
      sequence: 9,
      requestId: 'r9',
      seat: 1,
      context: '第二维损坏',
      options: [],
      secondaryOptions: [{ value: 42, preview: null }],
    })
    expect(broken?.secondaryOptions).toEqual([])
  })
})
