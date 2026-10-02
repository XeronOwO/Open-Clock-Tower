import { describe, expect, it } from 'vitest'
import {
  asArray,
  asCount,
  asCredential,
  asSizedText,
  clockTimeOf,
  normalizeStorytellerView,
  seatLabelOf,
  waitingSecondsTextOf,
} from '@/display/format'

describe('不可信输入规范化（架构 §4.4）', () => {
  it('非数组一律退化成空集合，不炸渲染', () => {
    expect(asArray<number>(null)).toEqual([])
    expect(asArray<number>({ length: 1 })).toEqual([])
    expect(asArray<number>([1, 2])).toEqual([1, 2])
  })

  it('席位号非法时降级为占位符', () => {
    expect(seatLabelOf(3)).toBe('3 号')
    expect(seatLabelOf(null)).toBe('—')
    expect(seatLabelOf(Number.NaN)).toBe('—')
  })

  it('等待秒数未知时不编数字', () => {
    expect(waitingSecondsTextOf(3.25)).toBe('3.3 秒')
    expect(waitingSecondsTextOf(null)).toBeNull()
    expect(waitingSecondsTextOf(Number.POSITIVE_INFINITY)).toBeNull()
  })

  it('时刻解析不了就原样回显', () => {
    expect(clockTimeOf('不是时间')).toBe('不是时间')
    expect(clockTimeOf('')).toBe('—')
    expect(clockTimeOf('2026-10-02T20:15:00+08:00')).toMatch(/\d{2}:\d{2}:\d{2}/)
  })
})

describe('说书人视图规范化', () => {
  it('空载荷退化成"未开始"的空板，而不是崩掉', () => {
    const view = normalizeStorytellerView(null)
    expect(view.phase).toBe('未开始')
    expect(view.sequence).toBe(0)
    expect(view.seats).toEqual([])
    expect(view.effects).toEqual([])
    expect(view.pending).toBeNull()
    expect(view.lastResolution).toBeNull()
    expect(view.stepDigest).toBeNull()
    expect(view.lastVoidedRequest).toBeNull()
    expect(view.awaitingDecisionOptions).toEqual([])
  })

  it('坏掉的条目被丢掉，好的条目保留', () => {
    const view = normalizeStorytellerView({
      sequence: 42,
      phase: 'Night',
      seats: [
        { seat: 3, facts: [{ dimension: 'Poison', value: 'Poisoned', reason: '投毒', causedBy: 1 }], madnesses: [] },
        { seat: '三号', facts: [] },
        null,
      ],
      effects: [{ effectId: 'e1', kind: 'Persistent', ability: 'no-dashii', source: 5, target: 3, terminated: false }],
      recentSeatChanges: [{ seat: 3, reason: '投毒', sequence: 9, recordedAt: '2026-10-02T20:15:00+08:00' }],
      abilityUses: [{ seat: 3, ability: 'dreamer', effective: false }],
      malfunctions: [{ seat: 3, ability: 'dreamer', kind: 'Poisoned' }],
      lastResolution: { seat: 3, ability: 'dreamer', effective: false, malfunction: 'Poisoned', sequence: 12 },
      awaitingDecisionId: 'dp-1',
      awaitingDecisionOptions: [{ value: 'a', preview: '选它' }, { preview: '缺值' }],
    })

    expect(view.sequence).toBe(42)
    expect(view.seats).toHaveLength(1)
    expect(view.seats[0]?.facts[0]?.causedBy).toBe(1)
    expect(view.effects).toHaveLength(1)
    expect(view.effects[0]?.terminated).toBe(false)
    expect(view.recentSeatChanges).toHaveLength(1)
    expect(view.abilityUses[0]?.effective).toBe(false)
    expect(view.malfunctions[0]?.kind).toBe('Poisoned')
    expect(view.lastResolution?.effective).toBe(false)
    expect(view.awaitingDecisionOptions).toEqual([{ value: 'a', preview: '选它' }])
  })

  it('每步摘要与最近作废按形状归一化，枚举/数字类型不猜', () => {
    const view = normalizeStorytellerView({
      stepDigest: {
        seat: 5,
        character: 'clockmaker',
        state: {
          seat: 5,
          facts: [{ dimension: 'Poison', value: 'Poisoned', reason: '常驻效果', causedBy: 1, effectId: 'e1' }],
          madnesses: [],
        },
        ability: {
          basis: 'Preview',
          ability: 'clockmaker',
          effective: false,
          malfunction: 'Poisoned',
          note: '来源中毒：能力未生效',
          sequence: null,
        },
        optionCount: 0,
        onNoOption: 'StorytellerDecides',
      },
      lastVoidedRequest: {
        requestId: 'r1',
        reason: 'DependencyViolated',
        note: '座位 2 的状态变化使请求失去意义',
      },
    })

    expect(view.stepDigest?.seat).toBe(5)
    expect(view.stepDigest?.character).toBe('clockmaker')
    expect(view.stepDigest?.state?.facts[0]?.causedBy).toBe(1)
    expect(view.stepDigest?.ability?.basis).toBe('Preview')
    expect(view.stepDigest?.ability?.effective).toBe(false)
    expect(view.stepDigest?.optionCount).toBe(0)
    expect(view.stepDigest?.onNoOption).toBe('StorytellerDecides')
    expect(view.lastVoidedRequest?.reason).toBe('DependencyViolated')
  })

  it('坏掉的新字段不编结论（缺席位号 / 缺作废原因都退化为 null）', () => {
    const view = normalizeStorytellerView({
      stepDigest: { seat: '五号' },
      lastVoidedRequest: { reason: 'DependencyViolated' },
    })

    expect(view.stepDigest).toBeNull()
    expect(view.lastVoidedRequest).toBeNull()
  })
})

describe('不可信输入的有界化（长度 / 范围，架构 §4.4）', () => {
  it('文本超长截断，而不是把面板撑爆；非字符串仍是坏载荷', () => {
    expect(asSizedText('x'.repeat(50), 10)).toBe('x'.repeat(10))
    expect(asSizedText('短', 10)).toBe('短')
    expect(asSizedText(123, 10)).toBeNull()
    expect(asSizedText('', 10)).toBeNull()
  })

  it('计数只接受非负整数：负数 / 小数 / 超大值都是坏载荷', () => {
    expect(asCount(3)).toBe(3)
    expect(asCount(0)).toBe(0)
    expect(asCount(-1)).toBeNull()
    expect(asCount(1.5)).toBeNull()
    expect(asCount(1_000_001)).toBeNull()
    expect(asCount('3')).toBeNull()
  })

  it('凭据必须是有界、无空白 / 控制字符的串（凭据不渲染，但形状要先校验）', () => {
    const valid = 'A'.repeat(43)
    expect(asCredential(valid)).toBe(valid)
    expect(asCredential('short')).toBeNull()
    expect(asCredential(`${'A'.repeat(20)} ${'B'.repeat(20)}`)).toBeNull()
    expect(asCredential('A'.repeat(513))).toBeNull()
    expect(asCredential(42)).toBeNull()
  })

  it('说书人视图里的负数 / 小数序号被当作坏载荷（退化为 0，而不是显示 -1）', () => {
    const view = normalizeStorytellerView({ sequence: -5, slotIndex: 1.5, slotCount: Number.NaN })
    expect(view.sequence).toBe(0)
    expect(view.slotIndex).toBe(0)
    expect(view.slotCount).toBe(0)
  })

  it('疯狂要求（自由文本）逐条有界化：超长截断，非字符串丢掉', () => {
    const view = normalizeStorytellerView({
      seats: [{ seat: 1, facts: [], madnesses: ['x'.repeat(300), 42, ''] }],
    })

    expect(view.seats[0]?.madnesses).toHaveLength(1)
    expect(view.seats[0]?.madnesses[0]?.length).toBe(200)
  })
})
