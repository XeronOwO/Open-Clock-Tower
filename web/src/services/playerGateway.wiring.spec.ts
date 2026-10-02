import { HubConnectionState, type HubConnection } from '@microsoft/signalr'
import { describe, expect, it } from 'vitest'
import type { InformationResultDto, PlayerViewDto, ReconnectBundleDto } from '@/contracts/game'
import { PlayerGateway, type PlayerCallbacks } from '@/services/playerGateway'

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
  private readonly handlers = new Map<string, (payload: unknown) => void>()

  on(method: string, handler: (payload: unknown) => void): void {
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

  /** 模拟服务端推送。 */
  receive(method: string, payload: unknown): void {
    this.handlers.get(method)?.(payload)
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
        pendingRequest: { sequence: 5, requestId: 'r5', seat: 1, context: '请选择目标', options: [] },
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
})
