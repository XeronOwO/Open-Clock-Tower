/**
 * 信息类候选的呈现规则（博学者，R-0057-C）：真值徽章、分组分栏、搜索与组合结论。
 *
 * 边界（`web/AGENTS.md` §4：呈现层不判规则）：这里每一条结论都是「服务端声明的约束 +
 * 服务端下发的真值」的**组合显示**——真值与允许的组合都由服务端算好，服务端在提交时还会按当时的账
 * 重新核对一次；前端拦下非法组合只是省一次往返，不是权威判定。
 */
import type { DecisionOptionDto } from '@/contracts/game'

/** 服务端声明的真值组合（取值与 Kernel 的 `TruthCombinationRule` 逐项对齐）。 */
export type TruthCombinationRule =
  | 'ExactlyOneTrue'
  | 'AllFalse'
  | 'AnyCombination'
  | 'Indeterminate'

/** 一条组合结论。 */
export interface SavantCombinationVerdict {
  /** 这条组合现在能不能提交（服务端会再核对一次）。 */
  readonly ok: boolean
  /** 常驻的一行结论（说书人看的就是它）。 */
  readonly text: string
}

/** 分栏：一组候选 + 它所属的分组名。 */
export interface SavantOptionGroup {
  readonly group: string
  readonly options: DecisionOptionDto[]
}

/** 一条候选与另一槽位已选项**互为反面**：说这条路会被服务端拒绝（R-0057-C 的 C4）。 */
export interface SavantExclusionConflict {
  /** 与它冲突的那条候选（另一槽位已选的）。 */
  readonly other: DecisionOptionDto
  /** 候选上写的短提示（"与已选的一条互为反面，必然一真一假"）。 */
  readonly reason: string
}

/** 候选上写的短提示文案（服务端给的互斥组判出来的，前端只显示）。 */
export const EXCLUSION_REASON = '与已选的一条互斥（必然一真一假）'

/** 没有分组的候选归入这一栏（服务端一般都会给分组）。 */
export const UNGROUPED_LABEL = '其它'

/**
 * 真值徽章文案：只认服务端给的两个取值；未知取值原样回显（不猜、不吞）。
 */
export function truthLabelOf(truth: string | null): string | null {
  if (truth === null) {
    return null
  }

  if (truth === 'True') {
    return '真'
  }

  if (truth === 'False') {
    return '假'
  }

  return truth
}

/** 真值徽章的色调：真 / 假 / 未知（未知取值照常显示，只是不上色）。 */
export function truthToneOf(truth: string | null): 'true' | 'false' | 'unknown' {
  if (truth === 'True') {
    return 'true'
  }

  if (truth === 'False') {
    return 'false'
  }

  return 'unknown'
}

/**
 * 按服务端给的分组顺序分栏（候选顺序即服务端顺序，不重排）：
 * 说书人先看类别，再看类别里的候选——不做一条大列表。
 */
export function savantGroupsOf(options: readonly DecisionOptionDto[]): SavantOptionGroup[] {
  const groups: SavantOptionGroup[] = []
  for (const option of options) {
    const group = option.group ?? UNGROUPED_LABEL
    const existing = groups.find((entry) => entry.group === group)
    if (existing === undefined) {
      groups.push({ group, options: [option] })
      continue
    }

    existing.options.push(option)
  }

  return groups
}

/**
 * 按关键字过滤候选：匹配候选文案与编码（不区分大小写）；空关键字给全部。
 * `group` 为 null 表示不按分组过滤。
 */
export function filterSavantOptions(
  options: readonly DecisionOptionDto[],
  query: string,
  group: string | null = null,
): DecisionOptionDto[] {
  const keyword = query.trim().toLowerCase()
  return options.filter((option) => {
    if (group !== null && (option.group ?? UNGROUPED_LABEL) !== group) {
      return false
    }

    if (keyword.length === 0) {
      return true
    }

    return (
      option.preview.toLowerCase().includes(keyword) || option.value.toLowerCase().includes(keyword)
    )
  })
}

/**
 * 两条候选是不是**互斥**（R-0057-C 的 C4）：服务端下发的互斥组相同、取值不同——
 * 同一条事实的两个取值（「恶魔坐在奇数位」与「恶魔坐在偶数位」，必然一真一假），
 * 或**跨编码**的同一个事实（爪牙距离 1 与「恶魔左右相邻的席位里有爪牙」）都算。
 *
 * 互斥组相同 = 两者**至多一条为真**：不能一起给（一起给等于只给了一条信息）。
 * 与另一槽位已选项命中时返回那条冲突（供候选**预先灰掉**与结论条用），否则返回 null。
 * 服务端提交时仍会按当时的账重新核对一次——这里只是省一次注定被拒的往返。
 */
export function exclusionConflictOf(
  candidate: DecisionOptionDto,
  other: DecisionOptionDto | null,
): SavantExclusionConflict | null {
  if (
    other === null ||
    candidate.exclusionGroup === null ||
    candidate.code === null ||
    candidate.exclusionGroup !== other.exclusionGroup ||
    candidate.value === other.value
  ) {
    return null
  }

  return { other, reason: EXCLUSION_REASON }
}

/**
 * 组合结论：服务端声明的约束 × 两条候选的真值。
 *
 * 未知约束（服务端将来加了新取值）一律**不拦**——原样放行、由服务端判定，前端不替它猜。
 */
export function combinationVerdict(
  rule: string | null,
  first: DecisionOptionDto | null,
  second: DecisionOptionDto | null,
): SavantCombinationVerdict {
  if (first === null || second === null) {
    return { ok: false, text: '两条都要选：每个槽位各挑一条候选' }
  }

  if (first.value === second.value) {
    return { ok: false, text: '两条不能是同一条事实' }
  }

  const exclusion = exclusionConflictOf(second, first)
  if (exclusion !== null) {
    return {
      ok: false,
      text:
        `「${first.preview}」与「${second.preview}」互为反面，必然一真一假——等于只给了一条信息。` +
        '要这么给请走下面的自由文本兜底（服务端也会拒绝这一对）',
    }
  }

  switch (rule as TruthCombinationRule | null) {
    case 'ExactlyOneTrue':
      return first.truth === second.truth
        ? {
            ok: false,
            text: `现在两条都为${truthLabelOf(first.truth) ?? '未知'}：能力生效时必须一真一假`,
          }
        : { ok: true, text: '一真一假 ✓' }

    case 'AllFalse':
      return first.truth === 'False' && second.truth === 'False'
        ? { ok: true, text: '两条都为假 ✓（涡流在场）' }
        : { ok: false, text: '涡流在场：两条都必须为假' }

    case 'AnyCombination':
      return { ok: true, text: '任意组合都可以（能力未生效）' }

    case 'Indeterminate':
      return { ok: true, text: '能力是否生效还判不了：平台会在提交时按当时的账再判' }

    default:
      return { ok: true, text: '' }
  }
}

/** 两个槽位的答案编码：与两条信息的分隔符一致（`第一条|第二条`，R-0057 第 3 条）。 */
export function savantDecisionOf(first: DecisionOptionDto, second: DecisionOptionDto): string {
  return `${first.value}|${second.value}`
}
