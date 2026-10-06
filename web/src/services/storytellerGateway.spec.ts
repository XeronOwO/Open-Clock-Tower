import { HubConnectionState, type HubConnection } from '@microsoft/signalr'
import { describe, expect, it } from 'vitest'
import type { StorytellerViewDto } from '@/contracts/game'
import { normalizeStorytellerView } from '@/display/format'
import { StorytellerGateway, newerView } from '@/services/storytellerGateway'

/**
 * 说书人视图序号闸（票据 `player-information-resync-race` 的同族）。
 *
 * 说书人视图是整份带序号的投影：Join / 刷新响应与推送走同一条序号流。修复前
 * `join()` / `refresh()` 无条件整份替换，窗口内到达的较新推送会被迟到的旧响应拉回去。
 */
describe('说书人视图序号闸', () => {
  const view = (sequence: number, phase: string) => normalizeStorytellerView({ sequence, phase })

  it('推送先到、Join / 刷新响应后到：保持较新视图，不被旧序号拉回', () => {
    const pushed = view(7, 'Day')
    const lateResponse = view(6, 'FirstNight')

    expect(newerView(pushed, lateResponse)).toBe(pushed)
  })

  it('较新视图胜出；相同序号幂等采纳', () => {
    const current = view(6, 'FirstNight')
    const next = view(7, 'Day')

    expect(newerView(current, next)).toBe(next)
    expect(newerView(next, view(7, 'Day')).sequence).toBe(7)
  })

  it('第一次视图（还没有当前值）总是采纳，空板也不例外', () => {
    const empty = normalizeStorytellerView(null)

    expect(newerView(null, empty)).toBe(empty)
  })
})

/** 假连接：只实现说书人网关用到的那几个成员（`as unknown as HubConnection` 注入）。 */
class FakeConnection {
  /** 已连接：与真流程一致（加入前连接已 start），invoke 同步发生。 */
  state = HubConnectionState.Connected
  response: unknown = null
  readonly invocations: Array<{ method: string; args: unknown[] }> = []
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
    return Promise.resolve(this.response)
  }

  receive(method: string, payload: unknown): void {
    this.handlers.get(method)?.(payload)
  }
}

function gatewayWith(fake: FakeConnection, views: StorytellerViewDto[]): StorytellerGateway {
  return new StorytellerGateway(
    {
      onView: (next) => views.push(next),
      onState: () => {},
      onDiagnostic: () => {},
    },
    () => fake as unknown as HubConnection,
  )
}

describe('说书人网关接线：视图只进不更旧的那份', () => {
  it('推送先到、刷新响应后到：onView 只有较新那份，refresh 返回当前视图', async () => {
    const fake = new FakeConnection()
    const views: StorytellerViewDto[] = []
    const gateway = gatewayWith(fake, views)

    fake.response = { credential: 'C'.repeat(43), view: { sequence: 9, phase: 'FirstNight' } }
    await gateway.joinWithAccount('account-session')
    fake.receive('ReceiveStorytellerViewChanged', { sequence: 11, phase: 'Day' })
    // 刷新响应比推送旧（响应在飞、推送先到）：不许把面板拉回。
    fake.response = { sequence: 10, phase: 'FirstNight' }
    const returned = await gateway.refresh()

    expect(views.map((item) => item.sequence)).toEqual([9, 11])
    expect(views.at(-1)?.phase).toBe('Day')
    expect(returned.sequence).toBe(11)
  })

  it('较新的刷新响应照常采纳（不是把所有响应都丢掉）', async () => {
    const fake = new FakeConnection()
    const views: StorytellerViewDto[] = []
    const gateway = gatewayWith(fake, views)

    fake.response = { credential: 'C'.repeat(43), view: { sequence: 9, phase: 'FirstNight' } }
    await gateway.joinWithAccount('account-session')
    fake.response = { sequence: 12, phase: 'Day' }
    const returned = await gateway.refresh()

    expect(views.map((item) => item.sequence)).toEqual([9, 12])
    expect(returned.sequence).toBe(12)
  })

  it('说书人加入出示的是**账号会话**，且带上了这一桌的标识（D-0027）', async () => {
    const fake = new FakeConnection()
    const seen: Array<string | undefined> = []
    const gateway = new StorytellerGateway(
      { onView: () => {}, onState: () => {}, onDiagnostic: () => {} },
      (gameId?: string) => {
        seen.push(gameId)
        return fake as unknown as HubConnection
      },
      'table-x',
    )

    fake.response = { credential: 'C'.repeat(43), view: { sequence: 1, phase: 'FirstNight' } }
    await gateway.joinWithAccount('account-session')

    // 桌标识必须真的传到连接工厂（写成无参箭头会把 gameId 吞掉，连接就落到别处）。
    expect(seen).toEqual(['table-x'])
    expect(fake.invocations).toEqual([
      { method: 'JoinStorytellerWithAccount', args: ['account-session'] },
    ])
  })
})
