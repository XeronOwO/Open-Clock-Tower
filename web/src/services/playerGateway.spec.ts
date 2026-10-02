import { describe, expect, it } from 'vitest'
import type { ReconnectBundleDto } from '@/contracts/game'
import {
  applyBundle,
  normalizeAnswered,
  normalizeBundle,
  normalizeInformation,
  normalizePhaseStarted,
  normalizePlayerDay,
  normalizePlayerEvent,
  normalizeRequest,
  normalizeSeatJoin,
  normalizeVoided,
} from '@/services/playerGateway'

/**
 * 重连补齐（架构 §5、D-0010）：快照 + 从本客户端已知序号起的**全部可见事件**（白名单投影，
 * D-0012 §4.3），快照序号才是权威 watermark。这里的用例是 2026-10-02 复核实测到的一次真实缺陷
 * 的回归：前端把 `events` 写死成空数组整体丢弃。
 *
 * 票据 `player-information-resync-race` 之后，推送与快照都带事件序号：快照序号低于本地已知
 * 不再是坏包（推送先到、响应后到），而由 `PlayerViewMerge` 按序号合并；坏事件数据仍显式失败。
 */
describe('重连包规范化', () => {
  it('真的读 events，而不是丢掉', () => {
    const bundle = normalizeBundle({
      sequence: 7,
      view: { seat: 2, phase: 'FirstNight', pendingRequest: null, informationResults: [], day: null },
      events: [
        { sequence: 6, kind: 'PhaseStarted', phase: 'FirstNight' },
        {
          sequence: 7,
          kind: 'InformationResultIssued',
          information: { sequence: 7, ability: 'dreamer', content: '你梦到 2 号' },
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
      view: { seat: 1, phase: 'FirstNight', pendingRequest: null, informationResults: [], day: null },
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
      sequence: 6,
      view: {
        seat: 1,
        phase: 'FirstNight',
        pendingRequest: null,
        informationResults: [{ sequence: 6, ability: 'oracle', content: '2 号是邪恶' }],
        day: null,
      },
      events: [],
    }).view.informationResults[0]

    expect(information).toEqual({ sequence: 6, ability: 'oracle', content: '2 号是邪恶' })
  })

  it('请求缺 requestId 或序号时返回 null（宁可少显示，不编造请求）', () => {
    expect(normalizeRequest({ seat: 1, context: '选一个目标', options: [] })).toBeNull()
    expect(normalizeRequest({ requestId: 'r1', seat: 1, context: '', options: [{ value: 'a' }] })).toBeNull()
    expect(normalizeRequest({ sequence: 3, requestId: 'r1', seat: 1, context: '', options: [{ value: 'a' }] })).toEqual({
      sequence: 3,
      requestId: 'r1',
      seat: 1,
      context: '',
      options: [{ value: 'a', preview: 'a' }],
      secondaryOptions: [],
    })
  })
})

describe('事件序号是合并判据：缺序号 = 坏载荷', () => {
  it('信息 / 作废 / 响应 / 阶段 / 请求：序号缺一不可', () => {
    expect(normalizeInformation({ ability: 'oracle', content: 'x' })).toBeNull()
    expect(normalizeInformation({ sequence: 6, ability: 'oracle', content: 'x' })).toEqual({
      sequence: 6,
      ability: 'oracle',
      content: 'x',
    })

    expect(normalizeVoided({ requestId: 'r1', reason: 'StorytellerForce', note: '测试' })).toBeNull()
    expect(normalizeVoided({ sequence: 6, requestId: 'r1', reason: 'StorytellerForce', note: '测试' })).toEqual({
      sequence: 6,
      requestId: 'r1',
      reason: 'StorytellerForce',
      note: '测试',
    })

    expect(normalizeAnswered({ requestId: 'r1', optionValue: 'seat:2', source: 'StorytellerProxy' })).toBeNull()
    expect(
      normalizeAnswered({ sequence: 6, requestId: 'r1', optionValue: 'seat:2', source: 'StorytellerProxy' }),
    ).toEqual({ sequence: 6, requestId: 'r1', optionValue: 'seat:2', source: 'StorytellerProxy', note: null })

    expect(normalizePhaseStarted({ phase: 'FirstNight' })).toBeNull()
    expect(normalizePhaseStarted({ sequence: 6, phase: 'FirstNight' })).toEqual({
      sequence: 6,
      phase: 'FirstNight',
    })
  })
})

describe('白天投影规范化', () => {
  it('公开事实 / 序号 / 权限位保留；缺关键字段的历史数据不编造', () => {
    const day = normalizePlayerDay({
      sequence: 9,
      publicFacts: {
        dayNumber: 1,
        status: 'Open',
        nominations: [
          { index: 1, nominator: 1, nominee: 2, status: 'Voting', votes: 1, voters: [3] },
        ],
        aboutToBeExecuted: null,
        executed: null,
        openNominationIndex: 1,
      },
      canNominate: false,
      canVote: true,
      voted: false,
      candidates: [3],
    })

    expect(day?.sequence).toBe(9)
    expect(day?.publicFacts.dayNumber).toBe(1)
    expect(day?.publicFacts.nominations[0]?.voters).toEqual([3])
    expect(day?.canVote).toBe(true)
    expect(day?.candidates).toEqual([3])

    // 公开事实缺天数 / 状态，或缺序号 → 整份白天投影不可识别，宁可少显示。
    expect(normalizePlayerDay({ sequence: 9, publicFacts: { status: 'Open' } })).toBeNull()
    expect(normalizePlayerDay({ publicFacts: { dayNumber: 1, status: 'Open' } })).toBeNull()
    expect(normalizePlayerDay(null)).toBeNull()
  })
})

describe('重连补齐折叠（快照权威）', () => {
  const bundle = (sequence: number, events: ReconnectBundleDto['events']): ReconnectBundleDto => ({
    sequence,
    view: { seat: 1, phase: 'FirstNight', pendingRequest: null, informationResults: [], day: null },
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

  it('可见事件是白名单子集：条数不可能覆盖序号区间，快照序号照常成为水位', () => {
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

  it('乱序到达的可见事件按序号重排后再校验', () => {
    const applied = applyBundle(bundle(5, [phaseEvent(5), phaseEvent(4)]), 3)

    expect(applied.sequence).toBe(5)
    expect(applied.diagnostic).toBe('')
  })

  it('事件序号等于本地已知（重复投递）→ 显式诊断，不采纳', () => {
    const applied = applyBundle(bundle(5, [phaseEvent(3)]), 3)

    expect(applied.diagnostic).toContain('重复')
  })

  it('事件序号早于本地已知、或落后于前一条 → 显式诊断', () => {
    const stale = applyBundle(bundle(5, [phaseEvent(2)]), 3)
    expect(stale.diagnostic).toContain('倒退')

    const regressed = applyBundle(bundle(9, [phaseEvent(6), phaseEvent(4)]), 5)
    expect(regressed.diagnostic).toContain('倒退')
  })

  it('事件序号越界（超出快照）→ 显式诊断', () => {
    const applied = applyBundle(bundle(5, [phaseEvent(6)]), 3)

    expect(applied.diagnostic).toContain('越界')
  })

  it('快照序号低于本地已知（推送先到、响应后到）→ 不是坏包；由合并态按字段序号取舍', () => {
    const applied = applyBundle(bundle(2, []), 9)

    expect(applied.sequence).toBe(2)
    expect(applied.diagnostic).toBe('')
  })

  it('迟到的响应里若夹带事件 → 仍按坏数据处理（倒退 / 重复）', () => {
    const applied = applyBundle(bundle(2, [phaseEvent(1)]), 9)

    expect(applied.diagnostic).toContain('倒退')
  })
})

describe('加入结果：连接级凭据（D-0012）', () => {
  const credential = 'C'.repeat(43)
  const bundle = {
    sequence: 0,
    view: { seat: 1, phase: 'FirstNight', pendingRequest: null, informationResults: [], day: null },
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
