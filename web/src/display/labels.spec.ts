import { describe, expect, it } from 'vitest'
import {
  ROSTER,
  characterLabelOf,
  characterNameOf,
  characterTypeOf,
  dimensionLabelOf,
  labelOf,
} from '@/display/labels'

describe('花名册（docs/standard/terminology.md §9）', () => {
  it('是 25 人，且 slug 唯一', () => {
    expect(ROSTER).toHaveLength(25)
    expect(new Set(ROSTER.map((profile) => profile.slug)).size).toBe(25)
  })

  it('三个已实现契约的角色在册且类型正确', () => {
    expect(characterNameOf('clockmaker')).toBe('钟表匠')
    expect(characterNameOf('dreamer')).toBe('筑梦师')
    expect(characterNameOf('no-dashii')).toBe('诺-达鲺')
    expect(characterTypeOf('no-dashii')).toBe('恶魔')
    expect(characterTypeOf('clockmaker')).toBe('镇民')
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
    expect(labelOf('Open')).toBe('未定（R-0004）')
  })

  it('未知值原样回显，不吞成空白', () => {
    expect(labelOf('SomethingNew')).toBe('SomethingNew')
    expect(labelOf(null)).toBe('—')
    expect(labelOf(undefined)).toBe('—')
    expect(labelOf('')).toBe('—')
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
