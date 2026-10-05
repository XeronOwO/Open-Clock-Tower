import { describe, expect, it } from 'vitest'
import {
  ROSTER,
  alignmentLabelOf,
  characterLabelOf,
  characterNameOf,
  characterTypeOf,
  dimensionLabelOf,
  effectMarkNameOf,
  labelOf,
  setupModifiersOf,
  typeLabelOf,
  voidReasonLabelOf,
} from '@/display/labels'

describe('花名册（docs/standard/terminology.md §9）', () => {
  it('是 30 人（25 非旅行者 + 5 旅行者），且 slug 唯一', () => {
    expect(ROSTER).toHaveLength(30)
    expect(new Set(ROSTER.map((profile) => profile.slug)).size).toBe(30)
  })

  it('已实现契约的角色在册且类型正确（含旅行者）', () => {
    expect(characterNameOf('clockmaker')).toBe('钟表匠')
    expect(characterNameOf('dreamer')).toBe('筑梦师')
    expect(characterNameOf('no-dashii')).toBe('诺-达鲺')
    expect(characterTypeOf('no-dashii')).toBe('恶魔')
    expect(characterTypeOf('clockmaker')).toBe('镇民')
    expect(characterNameOf('deviant')).toBe('怪咖')
    expect(characterTypeOf('deviant')).toBe('旅行者')
    expect(characterNameOf('butcher')).toBe('屠夫')
  })

  it('角色类型枚举名有中文，未知类型原样回显（配板净分布要用）', () => {
    expect(typeLabelOf('Townsfolk')).toBe('镇民')
    expect(typeLabelOf('Demon')).toBe('恶魔')
    expect(typeLabelOf('Traveller')).toBe('旅行者')
    expect(typeLabelOf('Unknown')).toBe('Unknown')
    expect(typeLabelOf(null)).toBe('—')
  })
})

describe('文案映射', () => {
  it('翻已知枚举值', () => {
    expect(labelOf('Alive')).toBe('存活')
    expect(labelOf('Dead')).toBe('死亡')
    expect(labelOf('Poisoned')).toBe('中毒')
    expect(labelOf('Healthy')).toBe('健康')
    expect(labelOf('Sober')).toBe('清醒')
    expect(labelOf('Evil')).toBe('邪恶')
    expect(labelOf('SourceDied')).toBe('来源死亡')
    expect(labelOf('NoLongerApplies')).toBe('条件不再满足')
    expect(labelOf('Open')).toBe('未定（R-0004）')
  })

  it('每步摘要依据与无选项行为有中文', () => {
    expect(labelOf('Settled')).toBe('已结算')
    expect(labelOf('Preview')).toBe('按当前账预览')
    expect(labelOf('Unknown')).toBe('无法判定')
    expect(labelOf('StorytellerDecides')).toBe('由说书人自由决定')
    expect(labelOf('BlockAndAlert')).toBe('阻塞并报警，等说书人处理')
  })

  it('游戏阶段（含未开夜占位）有中文，未知值仍原样回显', () => {
    expect(labelOf('NotStarted')).toBe('未开始')
    expect(labelOf('FirstNight')).toBe('首夜')
    expect(labelOf('OtherNight')).toBe('夜晚')
    expect(labelOf('Day')).toBe('白天')
    expect(labelOf('Resolving')).toBe('结算中')
    expect(labelOf('SomethingNew')).toBe('SomethingNew')
  })

  it('作废原因与同名控制模式分开映射，未知原因原样回显', () => {
    expect(voidReasonLabelOf('DependencyViolated')).toBe('座位依赖不再满足')
    expect(voidReasonLabelOf('StorytellerTakeover')).toBe('强推 / 接管切步了结')
    expect(voidReasonLabelOf('GameEnded')).toBe('本局已结束')
    expect(labelOf('StorytellerTakeover')).toBe('说书人接管')
    expect(voidReasonLabelOf('SomethingNew')).toBe('SomethingNew')
    expect(voidReasonLabelOf(null)).toBe('—')
  })

  it('未知值原样回显，不吞成空白', () => {
    expect(labelOf('SomethingNew')).toBe('SomethingNew')
    expect(labelOf(null)).toBe('—')
    expect(labelOf(undefined)).toBe('—')
    expect(labelOf('')).toBe('—')
  })

  it('提示标记名按能力登记：女巫的诅咒是「被诅咒」，未登记的能力不编名字', () => {
    expect(effectMarkNameOf('witch.curse')).toBe('被诅咒')
    expect(effectMarkNameOf('philosopher.grant.drunk')).toBe('醉酒')
    expect(effectMarkNameOf('philosopher.grant')).toBe('获得能力')
    expect(effectMarkNameOf('no-dashii.poison')).toBeNull()
    expect(effectMarkNameOf(null)).toBeNull()
  })

  it('维度名与角色名有中文，未知 slug 不回显空', () => {
    expect(dimensionLabelOf('Poison')).toBe('中毒')
    expect(dimensionLabelOf('Unknown')).toBe('Unknown')
    expect(characterLabelOf('vortox')).toBe('涡流（vortox）')
    expect(characterLabelOf('not-a-character')).toBe('not-a-character')
    expect(characterNameOf(null)).toBe('—')
    expect(characterTypeOf('not-a-character')).toBe('')
  })
})

describe('阵型修正提示（setup-modifier；rulings.md R-0042）', () => {
  it('方古 / 亡骨魔在已选分配里 → 各提示自己的方括号文案', () => {
    const notes = setupModifiersOf(['fang-gu', 'clockmaker', 'vigormortis'])
    expect(notes.map((profile) => profile.slug)).toEqual(['fang-gu', 'vigormortis'])
    expect(notes[0]?.setupModifier).toContain('[+1 外来者]')
    expect(notes[1]?.setupModifier).toContain('[-1 外来者]')
  })

  it('没有带修正的角色（含未知 slug）→ 不编提示', () => {
    expect(setupModifiersOf(['clockmaker', 'not-a-character'])).toEqual([])
    expect(setupModifiersOf([])).toEqual([])
  })
})

describe('本人角色与阵营（rulings.md R-0059）', () => {
  it('角色走花名册中文名；未观测显示占位符，不猜', () => {
    expect(characterLabelOf('vortox')).toBe('涡流（vortox）')
    expect(characterLabelOf(null)).toBe('—')
    expect(characterLabelOf('not-a-character')).toBe('not-a-character')
  })

  it('阵营独立成句（善良阵营 / 邪恶阵营）；未知取值原样回显、不吞', () => {
    expect(alignmentLabelOf('Good')).toBe('善良阵营')
    expect(alignmentLabelOf('Evil')).toBe('邪恶阵营')
    expect(alignmentLabelOf('Weird')).toBe('Weird')
    expect(alignmentLabelOf(null)).toBe('—')
  })
})
