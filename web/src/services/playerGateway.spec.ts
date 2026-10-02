import { describe, expect, it } from 'vitest'
import type { ReconnectBundleDto } from '@/contracts/game'
import {
  applyBundle,
  normalizeAnswered,
  normalizeBundle,
  normalizePhaseStarted,
  normalizePlayerEvent,
  normalizeRequest,
  normalizeSeatJoin,
  normalizeVoided,
} from '@/services/playerGateway'

/**
 * 重连补齐（架构 §5、D-0010）：快照 + 从本客户端已知序号起的**全部可见事件**（白名单投影，
 * D-0012 §4.3），快照序号才是权威 watermark。这里的用例是 2026-10-02 复核实测到的一次真实缺陷
 * 的回归：前端把 `events` 写死成空数组整体丢弃。
 */
describe('重连包规范化', () => {
  it('真的读 events，而不是丢掉', () => {
    const bundle = normalizeBundle({
      sequence: 7,
      view: { seat: 2, phase: 'FirstNight', pendingRequest: null, informationResults: [] },
      events: [
        { sequence: 6, kind: 'PhaseStarted', phase: 'FirstNight' },
        {
          sequence: 7,
          kind: 'InformationResultIssued',
          information: { ability: 'dreamer', content: '你梦到 2 号' },
        },
      ],
    })

    expect(bundle.events).toHaveLength(2)
    expect(bundle.events[1]?.information?.ability).toBe('dreamer')
    expect(bundle.droppedEvents).toBe(0)
  })

  it('缺序号的条目被丢掉（无序号就无法证明补齐完整）', () => {
    expect(normalizePlayerEvent({ kind: 'PhaseStarted' })).toBeNull()
    expect(normalizePlayerEvent({ sequence: 3 })).toBeNull()
    expect(normalizePlayerEvent(null)).toBeNull()
  })

  it('归一化丢条目不静默：droppedEvents 计数交给加入路径显式失败', () => {
    const bundle = normalizeBundle({
      sequence: 7,
      view: { seat: 1, phase: 'FirstNight', pendingRequest: null, informationResults: [] },
      events: [
        { sequence: 6, kind: 'PhaseStarted' },
        { kind: 'PhaseStarted' },
        { sequence: 8 },
        null,
      ],
    })

    expect(bundle.events).toHaveLength(1)
    expect(bundle.droppedEvents).toBe(3)
  })

  it('信息类结果只有内容，没有"可能为假"标记', () => {
    const information = normalizeBundle({
      sequence: 1,
      view: {
        seat: 1,
        phase: 'FirstNight',
        pendingRequest: null,
        informationResults: [{ ability: 'oracle', content: '2 号是邪恶' }],
      },
      events: [],
    }).view.informationResults[0]

    expect(information).toEqual({ ability: 'oracle', content: '2 号是邪恶' })
  })

  it('请求缺 requestId 时返回 null（宁可少显示，不编造请求）', () => {
    expect(normalizeRequest({ seat: 1, context: '选一个目标', options: [] })).toBeNull()
    expect(normalizeRequest({ requestId: 'r1', seat: 1, context: '', options: [{ value: 'a' }] })).toEqual({
      requestId: 'r1',
      seat: 1,
      context: '',
      options: [{ value: 'a', preview: 'a' }],
    })
  })
})

describe('重连补齐折叠（快照权威）', () => {
  const bundle = (sequence: number, events: ReconnectBundleDto['events']): ReconnectBundleDto => ({
    sequence,
    view: { seat: 1, phase: 'FirstNight', pendingRequest: null, informationResults: [] },
    events,
  })

  const phaseEvent = (sequence: number): ReconnectBundleDto['events'][number] => ({
    sequence,
    kind: 'PhaseStarted',
    phase: null,
    request: null,
    requestId: null,
    optionValue: null,
    voidReason: null,
    voidNote: null,
    information: null,
  })

  it('可见事件是白名单子集：条数不可能覆盖序号区间，快照序号照常成为新 watermark', () => {
    // 批次 E7 实测：本地已知 0、快照 198，但掉线期间的 194 条事件对这名玩家不可见（白名单投影）。
    const applied = applyBundle(bundle(198, [phaseEvent(7), phaseEvent(42)]), 0)

    expect(applied.sequence).toBe(198)
    expect(applied.diagnostic).toBe('')
  })

  it('掉线期间没有任何可见事件：照快照前进，不报假缺口', () => {
    const applied = applyBundle(bundle(5, []), 3)

    expect(applied.sequence).toBe(5)
    expect(applied.diagnostic).toBe('')
  })

  it('可见事件之间可以有隐藏事件：序号严格递增即可，不要求逐条连续', () => {
    const applied = applyBundle(bundle(9, [phaseEvent(5), phaseEvent(9)]), 3)

    expect(applied.sequence).toBe(9)
    expect(applied.diagnostic).toBe('')
  })

  it('事件恰好覆盖窗口时前进序号，并接上事件里的信息', () => {
    const applied = applyBundle(
      bundle(5, [
        phaseEvent(4),
        {
          ...phaseEvent(5),
          kind: 'InformationResultIssued',
          information: { ability: 'dreamer', content: '梦到 3 号' },
        },
      ]),
      3,
    )

    expect(applied.sequence).toBe(5)
    expect(applied.diagnostic).toBe('')
    expect(applied.informationResults.map((item) => item.ability)).toEqual(['dreamer'])
  })

  it('乱序到达的可见事件按序号重排后再校验', () => {
    const applied = applyBundle(bundle(5, [phaseEvent(5), phaseEvent(4)]), 3)

    expect(applied.sequence).toBe(5)
    expect(applied.diagnostic).toBe('')
  })

  it('事件序号等于本地已知（重复投递）→ 显式诊断，不前进 watermark', () => {
    const applied = applyBundle(bundle(5, [phaseEvent(3)]), 3)

    expect(applied.sequence).toBe(3)
    expect(applied.diagnostic).toContain('重复')
  })

  it('事件序号早于本地已知、或落后于前一条 → 显式诊断，不前进 watermark', () => {
    const stale = applyBundle(bundle(5, [phaseEvent(2)]), 3)
    expect(stale.sequence).toBe(3)
    expect(stale.diagnostic).toContain('倒退')

    const regressed = applyBundle(bundle(9, [phaseEvent(6), phaseEvent(4)]), 5)
    expect(regressed.sequence).toBe(5)
    expect(regressed.diagnostic).toContain('倒退')
  })

  it('事件序号越界（超出快照）→ 显式诊断，不前进 watermark', () => {
    const applied = applyBundle(bundle(5, [phaseEvent(6)]), 3)

    expect(applied.sequence).toBe(3)
    expect(applied.diagnostic).toContain('越界')
  })

  it('快照序号倒退（服务端比本地还旧）→ 显式诊断，不前进 watermark', () => {
    const applied = applyBundle(bundle(2, []), 9)

    expect(applied.sequence).toBe(9)
    expect(applied.diagnostic).toContain('倒退')
  })

  it('快照序号不变且没有可见事件 = 幂等补齐，既不前进也不报诊断', () => {
    const applied = applyBundle(bundle(7, []), 7)

    expect(applied.sequence).toBe(7)
    expect(applied.diagnostic).toBe('')
  })
})

describe('在线推送载荷规范化', () => {
  it('请求作废：缺请求标识或原因视为坏载荷（不编造原因）', () => {
    expect(normalizeVoided({ requestId: 'r1', reason: 'StorytellerForce', note: '测试' })).toEqual({
      requestId: 'r1',
      reason: 'StorytellerForce',
      note: '测试',
    })
    expect(normalizeVoided({ requestId: 'r1', reason: 'StorytellerForce' })).toEqual({
      requestId: 'r1',
      reason: 'StorytellerForce',
      note: null,
    })
    expect(normalizeVoided({ reason: 'StorytellerForce' })).toBeNull()
    expect(normalizeVoided({ requestId: 'r1' })).toBeNull()
    expect(normalizeVoided(null)).toBeNull()
  })

  it('请求响应：请求标识 / 选项 / 来源三者缺一不可', () => {
    expect(
      normalizeAnswered({ requestId: 'r1', optionValue: 'seat:2', source: 'StorytellerProxy', note: '代填' }),
    ).toEqual({ requestId: 'r1', optionValue: 'seat:2', source: 'StorytellerProxy', note: '代填' })
    expect(normalizeAnswered({ requestId: 'r1', optionValue: 'seat:2' })).toBeNull()
    expect(normalizeAnswered({ requestId: 'r1', source: 'Player' })).toBeNull()
    expect(normalizeAnswered('garbage')).toBeNull()
  })

  it('阶段开始：缺阶段名视为坏载荷（不知道阶段就不动页头）', () => {
    expect(normalizePhaseStarted({ phase: 'FirstNight' })).toEqual({ phase: 'FirstNight' })
    expect(normalizePhaseStarted({ phase: 42 })).toBeNull()
    expect(normalizePhaseStarted(undefined)).toBeNull()
  })
})

describe('加入结果：连接级凭据（D-0012）', () => {
  const credential = 'C'.repeat(43)
  const bundle = {
    sequence: 0,
    view: { seat: 1, phase: 'FirstNight', pendingRequest: null, informationResults: [] },
    events: [],
  }

  it('凭据与重连包都在，才算可识别的加入结果', () => {
    expect(normalizeSeatJoin({ credential, bundle })?.credential).toBe(credential)
    expect(normalizeSeatJoin({ credential, bundle })?.bundle.view.seat).toBe(1)
  })

  it('凭据缺失 / 越界 / 带空白，或重连包缺失 → 坏载荷（宁可加入失败，不带坏凭据继续）', () => {
    expect(normalizeSeatJoin({ credential: '', bundle })).toBeNull()
    expect(normalizeSeatJoin({ credential: 'short', bundle })).toBeNull()
    expect(normalizeSeatJoin({ credential, bundle: null })).toBeNull()
    expect(normalizeSeatJoin({ credential })).toBeNull()
    expect(normalizeSeatJoin(null)).toBeNull()
  })
})
