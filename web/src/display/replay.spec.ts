import { describe, expect, it } from 'vitest'
import type {
  ReplayMarkerDto,
  ReplaySeatDeltaDto,
  ReplayStepDto,
  SeatDisplayNameDto,
} from '@/contracts/game'
import { seatDisplayOf } from '@/display/format'
import {
  boardAt,
  markerLabelOf,
  markerTextOf,
  normalizeReplayView,
  seatCardOf,
  seatNumbersOf,
  stepKindLabelOf,
} from '@/display/replay'

function step(
  sequence: number,
  seats: ReplaySeatDeltaDto[] = [],
  markers: ReplayMarkerDto[] = [],
): ReplayStepDto {
  return {
    sequence,
    kind: 'State',
    phase: 'FirstNight',
    summary: `步骤 ${sequence}`,
    detail: null,
    seats,
    markers,
  }
}

function delta(seat: number, patch: Partial<ReplaySeatDeltaDto> = {}): ReplaySeatDeltaDto {
  return {
    seat,
    life: null,
    character: null,
    previousCharacter: null,
    alignment: null,
    drunk: null,
    poison: null,
    reason: null,
    causedBy: null,
    ...patch,
  }
}

describe('复盘盘面折叠（D-0020：按事件序号合并服务端增量）', () => {
  it('未观测的维度保持旧值；本次观测到的维度覆盖', () => {
    const steps = [
      step(1, [delta(1, { character: 'clockmaker', life: 'Alive', reason: '开局分配' })]),
      step(2, [delta(1, { life: 'Dead', reason: '恶魔击杀', causedBy: 2 })]),
    ]

    const board = boardAt(steps, 1)
    expect(board.get(1)?.character?.value).toBe('clockmaker')
    expect(board.get(1)?.life?.value).toBe('Dead')
    expect(board.get(1)?.life?.causedBy).toBe(2)
    // 第 0 步只含开局分配：死亡还没发生（回退到开头不丢状态）。
    expect(boardAt(steps, 0).get(1)?.life?.value).toBe('Alive')
  })

  it('换角同时保留前角色与当前角色', () => {
    const steps = [
      step(1, [delta(1, { character: 'clockmaker', previousCharacter: null })]),
      step(2, [delta(1, { character: 'dreamer', previousCharacter: 'clockmaker' })]),
    ]

    const board = boardAt(steps, 1)
    expect(board.get(1)?.character?.value).toBe('dreamer')
    // 增量字段本身也带前角色，供步骤文案使用。
    expect(steps[1]?.seats[0]?.previousCharacter).toBe('clockmaker')
  })

  it('中毒 / 醉酒由事实派生牌面标记（复用实时魔典口径）', () => {
    const steps = [step(1, [delta(1, { poison: 'Poisoned', drunk: 'Drunk', reason: '测试' })])]
    const card = seatCardOf(boardAt(steps, 0), 1)

    expect(card.observed).toBe(true)
    expect(card.poison).toBe('Poisoned')
    expect(card.marks.map((mark) => mark.kind)).toEqual(['poison', 'drunk'])
  })

  it('未观测席位是"未观测"，不是默认存活', () => {
    const card = seatCardOf(boardAt([], 0), 3)
    expect(card.observed).toBe(false)
    expect(card.life).toBeNull()
    expect(card.marks).toEqual([])
  })

  it('席位号从增量与标记推导，封顶 64', () => {
    const steps = [step(1, [delta(5, {})], [{ kind: 'shroud', seat: 3, from: null, to: null, text: null }])]
    expect(seatNumbersOf(steps, null)).toEqual([1, 2, 3, 4, 5])
    expect(seatNumbersOf([], 2)).toEqual([1, 2])
  })
})

describe('复盘归一化与文案（服务端数据是不可信输入）', () => {
  it('合法视图解析；任一步骤坏掉则整页失败（不静默跳步）', () => {
    const view = normalizeReplayView({
      sequence: 9,
      ended: true,
      hasMore: false,
      steps: [step(1), step(2)],
    })
    expect(view?.steps).toHaveLength(2)
    expect(view?.ended).toBe(true)

    expect(normalizeReplayView({ sequence: 9, steps: [{ sequence: 'x' }] })).toBeNull()
    expect(normalizeReplayView('not-an-object')).toBeNull()
  })

  it('未知取值原样回显，不猜', () => {
    expect(stepKindLabelOf('Mystery')).toBe('Mystery')
    expect(markerLabelOf('mystery-marker')).toBe('mystery-marker')
  })

  it('击杀箭头写成「A 号 → B 号」', () => {
    const marker: ReplayMarkerDto = { kind: 'kill-arrow', seat: null, from: 2, to: 5, text: null }
    expect(markerTextOf(marker)).toBe('2 号 → 5 号')
    expect(markerLabelOf(marker.kind)).toBe('恶魔击杀')
  })

  it('D7 新增标记有中文文案（加入 / 离场 / 流放 / 保护 / 屠夫窗口 / 两种效果窗口）', () => {
    expect(markerLabelOf('traveller-joined')).toBe('旅行者加入')
    expect(markerLabelOf('traveller-departed')).toBe('旅行者离场')
    expect(markerLabelOf('exile')).toBe('流放')
    expect(markerLabelOf('protected')).toBe('受死亡保护')
    expect(markerLabelOf('extra-nomination')).toBe('额外提名窗口')
    expect(markerLabelOf('regained-ability')).toBe('重获能力')
    expect(markerLabelOf('retained-ability')).toBe('保留能力')
    expect(markerLabelOf('effect-window')).toBe('效果窗口')
  })

  it('有玩家名时标记与文案走同一口径；没名字的席位回退席位号（D-0021）', () => {
    const names: SeatDisplayNameDto[] = [
      { seat: 2, displayName: '小明' },
      { seat: 5, displayName: '小红' },
    ]
    const arrow: ReplayMarkerDto = { kind: 'kill-arrow', seat: null, from: 2, to: 5, text: null }
    expect(markerTextOf(arrow, names)).toBe('2 号 · 小明 → 5 号 · 小红')

    const single: ReplayMarkerDto = { kind: 'poisoned', seat: 3, from: null, to: null, text: '中毒' }
    expect(markerTextOf(single, names)).toBe('3 号 · 中毒')
    expect(seatDisplayOf(2, names)).toBe('2 号 · 小明')
    expect(seatDisplayOf(4, names)).toBe('4 号')
  })

  it('复盘视图解析带上公开席位名（缺字段时退化成空表）', () => {
    const withNames = normalizeReplayView({
      sequence: 9,
      ended: true,
      hasMore: false,
      steps: [step(1)],
      seatNames: [{ seat: 1, displayName: '爱丽丝' }, { seat: 'x', displayName: '坏数据' }],
    })
    expect(withNames?.seatNames).toEqual([{ seat: 1, displayName: '爱丽丝' }])

    const without = normalizeReplayView({ sequence: 9, ended: true, hasMore: false, steps: [step(1)] })
    expect(without?.seatNames).toEqual([])
  })
})
