import type { DecisionOptionDto } from '@/contracts/game'
import {
  combinationVerdict,
  filterSavantOptions,
  savantDecisionOf,
  savantGroupsOf,
  truthLabelOf,
  truthToneOf,
} from '@/display/savantFacts'
import { describe, expect, it } from 'vitest'

function option(
  value: string,
  preview: string,
  truth: string | null,
  group: string | null = '座位关系',
): DecisionOptionDto {
  return { value, preview, truth, group, tags: [] }
}

const T = option('fact:a', '恶魔坐在奇数位', 'True')
const F = option('fact:b', '恶魔坐在偶数位', 'False')

describe('truthLabelOf / truthToneOf', () => {
  it('只认服务端给的两个取值，未知取值原样回显', () => {
    expect(truthLabelOf('True')).toBe('真')
    expect(truthLabelOf('False')).toBe('假')
    expect(truthLabelOf(null)).toBeNull()
    expect(truthLabelOf('Maybe')).toBe('Maybe')
  })

  it('色调按真值分三档（未知不上色，但照常显示）', () => {
    expect(truthToneOf('True')).toBe('true')
    expect(truthToneOf('False')).toBe('false')
    expect(truthToneOf(null)).toBe('unknown')
    expect(truthToneOf('Maybe')).toBe('unknown')
  })
})

describe('savantGroupsOf', () => {
  it('按服务端给的分组顺序分栏，候选顺序不重排', () => {
    const groups = savantGroupsOf([
      option('a', '甲', 'True', '座位关系'),
      option('b', '乙', 'False', '阵营与人数'),
      option('c', '丙', 'True', '座位关系'),
    ])

    expect(groups.map((entry) => entry.group)).toEqual(['座位关系', '阵营与人数'])
    expect(groups[0]?.options.map((entry) => entry.value)).toEqual(['a', 'c'])
  })

  it('没有分组的候选归入「其它」', () => {
    expect(savantGroupsOf([option('a', '甲', 'True', null)])[0]?.group).toBe('其它')
  })
})

describe('filterSavantOptions', () => {
  const options = [
    option('fact:demon-seat-parity:odd', '恶魔坐在奇数位', 'True', '座位关系'),
    option('fact:role-in-play:juggler', '角色「杂耍艺人」在场', 'False', '点名'),
  ]

  it('空关键字给全部；按分组过滤', () => {
    expect(filterSavantOptions(options, '')).toHaveLength(2)
    expect(filterSavantOptions(options, '', '点名').map((entry) => entry.value)).toEqual([
      'fact:role-in-play:juggler',
    ])
  })

  it('匹配文案与编码，不区分大小写', () => {
    expect(filterSavantOptions(options, '奇数').map((entry) => entry.value)).toEqual([
      'fact:demon-seat-parity:odd',
    ])
    expect(filterSavantOptions(options, 'JUGGLER').map((entry) => entry.value)).toEqual([
      'fact:role-in-play:juggler',
    ])
    expect(filterSavantOptions(options, '没有这一条')).toHaveLength(0)
  })
})

describe('combinationVerdict', () => {
  it('两个槽位没选全时不给提交', () => {
    expect(combinationVerdict('ExactlyOneTrue', T, null).ok).toBe(false)
    expect(combinationVerdict('ExactlyOneTrue', null, null).text).toContain('两条都要选')
  })

  it('同一条事实写两遍不给提交', () => {
    expect(combinationVerdict('ExactlyOneTrue', T, T).ok).toBe(false)
  })

  it('能力生效：一真一假可以，双真 / 双假都不行', () => {
    expect(combinationVerdict('ExactlyOneTrue', T, F).ok).toBe(true)
    expect(combinationVerdict('ExactlyOneTrue', T, F).text).toContain('一真一假')

    const bothTrue = combinationVerdict('ExactlyOneTrue', T, option('c', '丙', 'True'))
    expect(bothTrue.ok).toBe(false)
    expect(bothTrue.text).toContain('都为真')

    const bothFalse = combinationVerdict(
      'ExactlyOneTrue',
      F,
      option('d', '丁', 'False', '点名'),
    )
    expect(bothFalse.ok).toBe(false)
    expect(bothFalse.text).toContain('都为假')
  })

  it('涡流在场：两条都为假才放行', () => {
    expect(combinationVerdict('AllFalse', F, option('d', '丁', 'False')).ok).toBe(true)
    const mixed = combinationVerdict('AllFalse', T, F)
    expect(mixed.ok).toBe(false)
    expect(mixed.text).toContain('涡流')
  })

  it('能力未生效：任意组合都放行（含双真）', () => {
    expect(combinationVerdict('AnyCombination', T, option('c', '丙', 'True')).ok).toBe(true)
  })

  it('判不了 / 未知约束：一律不拦，交给服务端', () => {
    expect(combinationVerdict('Indeterminate', T, F).ok).toBe(true)
    expect(combinationVerdict('SomethingNew', T, F)).toEqual({ ok: true, text: '' })
    expect(combinationVerdict(null, T, F)).toEqual({ ok: true, text: '' })
  })
})

describe('savantDecisionOf', () => {
  it('答案是两条候选值，用与两条信息同一分隔符拼起来', () => {
    expect(savantDecisionOf(T, F)).toBe('fact:a|fact:b')
  })
})
