import { describe, expect, it } from 'vitest'
import type { EffectDto, SeatStateDto, SeatStateFactDto, StorytellerViewDto } from '@/contracts/game'
import {
  ALIGNMENT_GOOD,
  DIMENSION_ALIGNMENT,
  DIMENSION_CHARACTER,
  DIMENSION_DRUNK,
  DIMENSION_LIFE,
  DIMENSION_POISON,
  DRUNK_DRUNK,
  LIFE_ALIVE,
  LIFE_DEAD,
  POISON_POISONED,
  attentionSeatOf,
  buildSeatCard,
  buildSeatMarks,
  decisionSeatOf,
  effectMarkLabel,
  factValueOf,
  ringPosition,
  seatNumbersOf,
  seatTitleOf,
} from '@/display/grimoire'

function viewOf(overrides: Partial<StorytellerViewDto> = {}): StorytellerViewDto {
  return {
    sequence: 1,
    phase: 'FirstNight',
    control: 'Automatic',
    health: { degraded: false, reason: null, since: null },
    slotIndex: 0,
    slotCount: 13,
    currentSlotId: null,
    planCompleted: false,
    pending: null,
    awaitingDecisionId: null,
    awaitingDecisionContext: null,
    awaitingDecisionOptions: [],
    blockedReason: null,
    currentSlotActor: null,
    currentSlotContext: null,
    recentSeatChanges: [],
    seats: [],
    effects: [],
    abilityUses: [],
    malfunctions: [],
    lastResolution: null,
    stepDigest: null,
    lastVoidedRequest: null,
    day: null,
    outcome: null,
    klutzChoices: [],
    pitHagNight: null,
    ...overrides,
  }
}

function fact(
  dimension: string,
  value: string,
  reason = '测试原因',
  causedBy: number | null = null,
  effectId: string | null = null,
): SeatStateFactDto {
  return { dimension, value, reason, causedBy, effectId }
}

function seatOf(seat: number, facts: SeatStateFactDto[], madnesses: string[] = []): SeatStateDto {
  return { seat, facts, madnesses }
}

function effectOf(target: number, overrides: Partial<EffectDto> = {}): EffectDto {
  return {
    effectId: 'standing:no-dashii.poison:3:1',
    kind: 'Persistent',
    ability: 'no-dashii',
    source: 3,
    target,
    sourceCharacter: 'no-dashii',
    terminated: false,
    terminationKind: null,
    terminationReason: null,
    terminationCausedBy: null,
    ...overrides,
  }
}

describe('牌面派生（未观测 ≠ 默认值）', () => {
  it('已观测席位：五个维度、疯狂要求与效果链接各就各位', () => {
    const view = viewOf({
      seats: [
        seatOf(
          1,
          [
            fact(DIMENSION_CHARACTER, 'clockmaker', '开局分配', 1),
            fact(DIMENSION_LIFE, LIFE_ALIVE, '开局分配', 1),
            fact(DIMENSION_ALIGNMENT, ALIGNMENT_GOOD, '开局分配', 1),
            fact(DIMENSION_POISON, POISON_POISONED, '常驻效果', 3, 'e1'),
          ],
          ['你要疯狂地证明你是钟表匠'],
        ),
      ],
      effects: [effectOf(1), effectOf(2, { effectId: 'e2', target: 2 })],
    })

    const card = buildSeatCard(view, 1)

    expect(card.observed).toBe(true)
    expect(card.character).toBe('clockmaker')
    expect(card.life).toBe(LIFE_ALIVE)
    expect(card.alignment).toBe(ALIGNMENT_GOOD)
    expect(card.poison).toBe(POISON_POISONED)
    expect(card.drunk).toBeNull()
    // 只收"以该席为作用对象"的效果：2 号那条不能挂到 1 号身上。
    expect(card.effects.map((effect) => effect.effectId)).toEqual(['standing:no-dashii.poison:3:1'])
    expect(card.marks.map((mark) => `${mark.kind}:${mark.label}`)).toEqual([
      'poison:中毒',
      'madness:你要疯狂地证明你是钟表匠',
      'effect:诺-达鲺',
    ])
    expect(card.marks[0]?.detail).toBe('常驻效果')
    expect(card.marks[2]?.detail).toBe('生效中')
  })

  it('未被观测的席位：全体为 null，不画成存活 / 健康', () => {
    const card = buildSeatCard(viewOf(), 4)

    expect(card.observed).toBe(false)
    expect(card.character).toBeNull()
    expect(card.life).toBeNull()
    expect(card.alignment).toBeNull()
    expect(card.poison).toBeNull()
    expect(card.marks).toEqual([])
    expect(seatTitleOf(card)).toContain('尚未观测')
  })

  it('只观测到部分维度时，其余维度保持 null（六状态正交）', () => {
    const view = viewOf({ seats: [seatOf(2, [fact(DIMENSION_LIFE, LIFE_DEAD, '说书人上报')])] })
    const card = buildSeatCard(view, 2)

    expect(card.life).toBe(LIFE_DEAD)
    expect(card.character).toBeNull()
    expect(card.drunk).toBeNull()
    expect(seatTitleOf(card)).toContain('已死亡')
  })

  it('清醒 / 健康不是标记；醉酒 / 中毒才是', () => {
    const marks = buildSeatMarks(
      [
        fact(DIMENSION_DRUNK, 'Sober'),
        fact(DIMENSION_POISON, 'Healthy'),
        fact(DIMENSION_LIFE, LIFE_ALIVE),
      ],
      [],
      [],
    )

    expect(marks).toEqual([])
    expect(factValueOf([fact(DIMENSION_DRUNK, DRUNK_DRUNK)], DIMENSION_DRUNK)).toBe(DRUNK_DRUNK)
    expect(factValueOf([], DIMENSION_DRUNK)).toBeNull()
  })

  it('已终止的效果也留在牌面上（能回答"因为什么解毒"），并标出终止', () => {
    const view = viewOf({
      seats: [seatOf(1, [fact(DIMENSION_POISON, 'Healthy', '维度解除', 3, 'e1')])],
      effects: [
        effectOf(1, {
          terminated: true,
          terminationKind: 'SourceDied',
          terminationReason: '来源死亡',
        }),
      ],
    })

    const card = buildSeatCard(view, 1)

    expect(card.marks).toEqual([{ kind: 'effect', label: '诺-达鲺', detail: '已终止' }])
  })

  it('效果简称优先用施加时的来源角色；没有来源角色时退回能力 slug（未知原样回显）', () => {
    expect(effectMarkLabel(effectOf(1, { sourceCharacter: 'no-dashii' }))).toBe('诺-达鲺')
    expect(effectMarkLabel(effectOf(1, { sourceCharacter: null }))).toBe('诺-达鲺')
    expect(
      effectMarkLabel(effectOf(1, { sourceCharacter: null, ability: 'no-dashii.poison' })),
    ).toBe('no-dashii.poison')
  })

  it('登记的提示标记优先于来源角色名：女巫的诅咒在牌面上就是「被诅咒」', () => {
    // 百科《女巫》· 2026-10-01 抓取 · 提示标记：「被诅咒」
    const curse = effectOf(4, {
      effectId: 'sv:night-1:witch:curse',
      ability: 'witch.curse',
      source: 1,
      sourceCharacter: 'witch',
    })

    expect(effectMarkLabel(curse)).toBe('被诅咒')
    expect(buildSeatMarks([], [], [curse])).toEqual([
      { kind: 'effect', label: '被诅咒', detail: '生效中' },
    ])
  })

  it('未知枚举值原样留在值里，由呈现层决定怎么显示（不吞）', () => {
    const card = buildSeatCard(
      viewOf({ seats: [seatOf(5, [fact(DIMENSION_ALIGNMENT, 'Chaotic', '说书人裁定')])] }),
      5,
    )

    expect(card.alignment).toBe('Chaotic')
    expect(card.marks).toEqual([])
  })
})

describe('席位名单与圆环布局（纯函数）', () => {
  it('名单 = 配置席位 ∪ 已观测席位，去重升序，过滤非法值', () => {
    const view = viewOf({ seats: [seatOf(5, []), seatOf(2, []), seatOf(0, []), seatOf(99, [])] })

    expect(seatNumbersOf(view, 3)).toEqual([1, 2, 3, 5, 99])
    expect(seatNumbersOf(view, 0)).toEqual([2, 5, 99])
  })

  it('名单有上限（坏载荷不能把圆环撑爆）', () => {
    const view = viewOf({
      seats: Array.from({ length: 80 }, (_, index) => seatOf(index + 1, [])),
    })

    expect(seatNumbersOf(view, 0).length).toBe(64)
  })

  it('圆环：席位 1 在正上方、顺时针均分；单席居中', () => {
    expect(ringPosition(0, 4)).toEqual({ xPercent: 50, yPercent: 12 })
    expect(ringPosition(1, 4)).toEqual({ xPercent: 88, yPercent: 50 })
    expect(ringPosition(2, 4)).toEqual({ xPercent: 50, yPercent: 88 })
    expect(ringPosition(3, 4)).toEqual({ xPercent: 12, yPercent: 50 })
    expect(ringPosition(0, 1)).toEqual({ xPercent: 50, yPercent: 50 })
  })

  it('圆环：非法下标 / 数量返回 null，不硬算一个位置', () => {
    expect(ringPosition(4, 4)).toBeNull()
    expect(ringPosition(-1, 4)).toBeNull()
    expect(ringPosition(0, 0)).toBeNull()
    expect(ringPosition(1.5, 4)).toBeNull()
  })
})

describe('注意力归属（卡点 / 裁定 / 当前槽位）', () => {
  it('没有待办时归属为空；有卡点时卡点优先', () => {
    expect(attentionSeatOf(viewOf())).toBeNull()
    expect(decisionSeatOf(viewOf())).toBeNull()

    const view = viewOf({
      currentSlotActor: 2,
      awaitingDecisionId: 'dp-1',
      pending: {
        seat: 3,
        requestId: 'r1',
        slotId: 's',
        slotIndex: 1,
        triggerReason: null,
        waitingSeconds: 12,
      },
    })

    expect(decisionSeatOf(view)).toBe(2)
    expect(attentionSeatOf(view)).toBe(3)
  })

  it('裁定归属回退到每步摘要的行动者；都没有时无法归属', () => {
    const withDigest = viewOf({
      awaitingDecisionId: 'dp-2',
      stepDigest: { seat: 5, character: null, state: null, ability: null, optionCount: null, onNoOption: null },
    })
    const withoutActor = viewOf({ awaitingDecisionId: 'dp-3' })

    expect(decisionSeatOf(withDigest)).toBe(5)
    expect(decisionSeatOf(withoutActor)).toBeNull()
    expect(attentionSeatOf(withoutActor)).toBeNull()
  })
})
