import { describe, expect, it } from 'vitest'
import {
  asArray,
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
})
