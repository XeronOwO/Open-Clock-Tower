import type { DecisionOptionDto } from '@/contracts/game'
import {
  EXCLUSION_REASON,
  combinationVerdict,
  exclusionConflictOf,
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
  code: string | null = null,
  exclusionGroup: string | null = null,
): DecisionOptionDto {
  return { value, preview, truth, group, code, exclusionGroup, tags: [] }
}

const T = option('fact:a', '恶魔坐在奇数位', 'True')
const F = option('fact:b', '恶魔坐在偶数位', 'False')

/** 服务端下发的真·奇偶一对：同编码 `demon-seat-parity`、同互斥组、取值不同。 */
const ODD = option('fact:demon-seat-parity:odd', '恶魔坐在奇数位', 'True', '座位关系', 'demon-seat-parity', 'demon-seat-parity')
const EVEN = option('fact:demon-seat-parity:even', '恶魔坐在偶数位', 'False', '座位关系', 'demon-seat-parity', 'demon-seat-parity')
/** 同一组事实的其它取值（如两个爪牙距离）不是反面对：服务端不给互斥组。 */
const DIST_2 = option('fact:demon-minion-distance:2', '恶魔与最近的爪牙相距 2', 'True', '座位关系', 'demon-minion-distance', null)
const DIST_3 = option('fact:demon-minion-distance:3', '恶魔与最近的爪牙相距 3', 'False', '座位关系', 'demon-minion-distance', null)
/** 跨编码的同一个事实：爪牙距离 1 与「恶魔旁边有爪牙」同互斥组（服务端给的组相同、编码不同）。 */
const DIST_1 = option('fact:demon-minion-distance:1', '恶魔与最近的爪牙相邻（距离 1）', 'True', '座位关系', 'demon-minion-distance', 'demon-minion-adjacency')
const BESIDE = option('fact:minion-beside-demon', '恶魔左右相邻的席位里有爪牙', 'True', '座位关系', 'minion-beside-demon', 'demon-minion-adjacency')

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

describe('exclusionConflictOf（互为反面：C4 防呆）', () => {
  it('同编码、同互斥组、取值不同 = 互为反面，给出可读理由', () => {
    const conflict = exclusionConflictOf(EVEN, ODD)
    expect(conflict?.other.value).toBe(ODD.value)
    expect(conflict?.reason).toBe(EXCLUSION_REASON)
    expect(exclusionConflictOf(ODD, EVEN)?.reason).toBe(EXCLUSION_REASON)
  })

  it('同一条候选（取值相同）不算反面对：那是"写两遍"，另有一条结论', () => {
    expect(exclusionConflictOf(ODD, ODD)).toBeNull()
  })

  it('另一个槽位没选 / 服务端没给互斥组 ⇒ 不拦', () => {
    expect(exclusionConflictOf(EVEN, null)).toBeNull()
    expect(exclusionConflictOf(DIST_3, DIST_2)).toBeNull()
    expect(exclusionConflictOf(T, ODD)).toBeNull()
  })

  it('跨编码但同互斥组也算互斥：爪牙距离 1 与「旁边有爪牙」不能一起给', () => {
    expect(exclusionConflictOf(BESIDE, DIST_1)?.reason).toBe(EXCLUSION_REASON)
    expect(exclusionConflictOf(DIST_1, BESIDE)?.reason).toBe(EXCLUSION_REASON)
  })

  it('互斥组不同 ⇒ 不拦（同组的其它取值不是反面）', () => {
    const otherGroup = option('fact:x', '场上有玩家中毒', 'False', '状态读数', 'poisoned-present', 'poisoned-present')
    expect(exclusionConflictOf(DIST_2, otherGroup)).toBeNull()
  })

  it('缺事实编码的旧服务端数据宁可放行，也不误灰（服务端提交时仍会拒绝）', () => {
    const legacy = option('fact:x', '恶魔坐在奇数位', 'True', '座位关系', null, 'demon-seat-parity')
    expect(exclusionConflictOf(legacy, ODD)).toBeNull()
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

  it('互为反面的一对不给提交，并指路自由文本兜底（真值上它确实"一真一假"）', () => {
    const mirror = combinationVerdict('ExactlyOneTrue', ODD, EVEN)
    expect(mirror.ok).toBe(false)
    expect(mirror.text).toContain('互为反面')
    expect(mirror.text).toContain('自由文本')
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
