/**
 * 魔典主视图的派生计算（纯函数）。
 *
 * 输入是 `normalizeStorytellerView` 之后的整份视图；输出只描述"这一席在牌面上怎么画"。
 * 边界（`docs/architecture/storyteller-presentation.md` §3）：
 * - **不推断规则**：只读服务端已下发的事实与效果；
 * - **未观测 ≠ 默认值**：缺维度就是 null，绝不画成"存活 / 健康"；
 * - 服务端数据是不可信输入：坏值只降级该格，不炸渲染（架构 §4.4）。
 */
import type {
  EffectDto,
  LostAbilityMarkerDto,
  SeatAnnotationDto,
  SeatStateFactDto,
  StorytellerViewDto,
} from '@/contracts/game'
import { characterLabelOf, characterNameOf, effectMarkNameOf } from '@/display/labels'
import { displayNameOf } from '@/display/format'

/** 六维度里可在牌面上呈现的五个；疯狂要求另列（`SeatStateDto.madnesses`）。 */
export const DIMENSION_LIFE = 'Life'
export const DIMENSION_CHARACTER = 'Character'
export const DIMENSION_ALIGNMENT = 'Alignment'
export const DIMENSION_DRUNK = 'Drunk'
export const DIMENSION_POISON = 'Poison'

export const LIFE_ALIVE = 'Alive'
export const LIFE_DEAD = 'Dead'
export const ALIGNMENT_GOOD = 'Good'
export const ALIGNMENT_EVIL = 'Evil'
export const DRUNK_DRUNK = 'Drunk'
export const POISON_POISONED = 'Poisoned'

/** 牌面上的一个标记：中毒 / 醉酒 / 疯狂要求 / 效果链接 / 失去能力（R-0040）。 */
export interface SeatMark {
  kind: 'poison' | 'drunk' | 'madness' | 'effect' | 'exhausted'
  label: string
  /** 归因补充（原因 / 生效状态 / 来源）；没有时为 null。 */
  detail: string | null
}

/** 一个席位的牌面模型：全部为 null 表示"该维度还没被观测到"。 */
export interface SeatCardModel {
  seat: number
  /** 玩家名（D-0021）：账号认领后非 null；游客席位为 null（呈现层回退席位号）。 */
  displayName: string | null
  /** 状态账里有没有这一席（没有 = 还没观测到任何东西）。 */
  observed: boolean
  character: string | null
  alignment: string | null
  life: string | null
  drunk: string | null
  poison: string | null
  madnesses: readonly string[]
  facts: readonly SeatStateFactDto[]
  /** 以该席为作用对象的效果（含已终止——"因为什么解毒"要查得到）。 */
  effects: readonly EffectDto[]
  /** 挂在该席上的说书人注记（D-0019）：自由文本提示标记，只说书人可见。 */
  annotations: readonly SeatAnnotationDto[]
  /** 该席用尽的限次能力（「失去能力」标记，R-0040；由能力使用账本派生）。 */
  lostAbilityMarkers: readonly LostAbilityMarkerDto[]
  marks: readonly SeatMark[]
}

/** 在事实列表里找某个维度的事实；未观测返回 null。 */
export function factOf(
  facts: readonly SeatStateFactDto[],
  dimension: string,
): SeatStateFactDto | null {
  return facts.find((fact) => fact.dimension === dimension) ?? null
}

/** 某个维度的当前值；未观测返回 null。 */
export function factValueOf(
  facts: readonly SeatStateFactDto[],
  dimension: string,
): string | null {
  return factOf(facts, dimension)?.value ?? null
}

/**
 * 效果链在牌面上的简称：**登记的提示标记名优先**（百科口径，如女巫的「被诅咒」），
 * 未登记的能力退回施加时的来源角色（中文名），再退回能力 slug。
 */
export function effectMarkLabel(effect: EffectDto): string {
  return effectMarkNameOf(effect.ability) ?? characterNameOf(effect.sourceCharacter ?? effect.ability)
}

/** 效果状态：生效中 / 已终止（终止原因交给操作台的下钻查看）。 */
export function effectMarkDetail(effect: EffectDto): string {
  return effect.terminated ? '已终止' : '生效中'
}

/** 由事实与效果派生牌面上的标记；顺序稳定，便于断言与人工核对。 */
export function buildSeatMarks(
  facts: readonly SeatStateFactDto[],
  madnesses: readonly string[],
  effects: readonly EffectDto[],
): SeatMark[] {
  const marks: SeatMark[] = []

  const poison = factOf(facts, DIMENSION_POISON)
  if (poison !== null && poison.value === POISON_POISONED) {
    marks.push({ kind: 'poison', label: '中毒', detail: poison.reason })
  }

  const drunk = factOf(facts, DIMENSION_DRUNK)
  if (drunk !== null && drunk.value === DRUNK_DRUNK) {
    marks.push({ kind: 'drunk', label: '醉酒', detail: drunk.reason })
  }

  for (const madness of madnesses) {
    marks.push({ kind: 'madness', label: madness, detail: null })
  }

  for (const effect of effects) {
    marks.push({ kind: 'effect', label: effectMarkLabel(effect), detail: effectMarkDetail(effect) })
  }

  return marks
}

/** 构造一个席位的牌面模型；该席未被观测时给出"未观测"的空模型，不编造默认值。 */
export function buildSeatCard(view: StorytellerViewDto, seat: number): SeatCardModel {
  const entry = view.seats.find((candidate) => candidate.seat === seat) ?? null
  const facts = entry?.facts ?? []
  const madnesses = entry?.madnesses ?? []
  const effects = view.effects.filter((effect) => effect.target === seat)
  const annotations = view.annotations.filter((annotation) => annotation.seat === seat)
  const lostAbilityMarkers = view.lostAbilityMarkers.filter((marker) => marker.seat === seat)

  const marks = buildSeatMarks(facts, madnesses, effects)
  for (const marker of lostAbilityMarkers) {
    marks.push({ kind: 'exhausted', label: '失去能力', detail: marker.note })
  }

  return {
    seat,
    displayName: displayNameOf(seat, view.seatNames),
    observed: entry !== null,
    character: factValueOf(facts, DIMENSION_CHARACTER),
    alignment: factValueOf(facts, DIMENSION_ALIGNMENT),
    life: factValueOf(facts, DIMENSION_LIFE),
    drunk: factValueOf(facts, DIMENSION_DRUNK),
    poison: factValueOf(facts, DIMENSION_POISON),
    madnesses,
    facts,
    effects,
    annotations,
    lostAbilityMarkers,
    marks,
  }
}

/**
 * 席位名单 = 会话配置席位 ∪ 状态账里已观测到的席位（去重、升序）。
 * 上限 64：BotC 一局最多二十来人，超出的只可能是坏载荷（架构 §4.4 的有界化）。
 */
export function seatNumbersOf(view: StorytellerViewDto, seatCount: number, cap = 64): number[] {
  const seats = new Set<number>()
  const configured = Number.isInteger(seatCount) && seatCount > 0 ? seatCount : 0
  for (let seat = 1; seat <= configured; seat += 1) {
    seats.add(seat)
  }

  for (const entry of view.seats) {
    seats.add(entry.seat)
  }

  return [...seats]
    .filter((seat) => Number.isInteger(seat) && seat > 0)
    .sort((left, right) => left - right)
    .slice(0, cap)
}

/**
 * 待裁定归属的席位：**服务端给的归属席位优先**（触发格 / 触发型裁定没有行动者，
 * 只有内核知道"谁在等"）；退回当前槽位行动者、每步摘要的行动者；都无法归属时为 null。
 */
export function decisionSeatOf(view: StorytellerViewDto): number | null {
  if (view.awaitingDecisionId === null) {
    return null
  }

  return view.awaitingDecisionSeat ?? view.currentSlotActor ?? view.stepDigest?.seat ?? null
}

/**
 * 状态条的槽位读数：计划已走完显示「已完成」、未建计划显示「—」、否则封顶在 1..slotCount。
 *
 * `slotIndex == slotCount` 是内核"计划已走完"的正常表示，直接 `+1` 会读出「2 / 1」。
 */
export function slotCounterTextOf(view: StorytellerViewDto): string {
  if (view.slotCount <= 0) {
    return '—'
  }

  if (view.planCompleted || view.slotIndex >= view.slotCount) {
    return '已完成'
  }

  return `${Math.max(view.slotIndex, 0) + 1} / ${view.slotCount}`
}

/** 环心读数：与状态条同口径（`已完成` / `第 N / M 步` / 尚未建计划）。 */
export function slotProgressTextOf(view: StorytellerViewDto): string {
  if (view.slotCount <= 0) {
    return '尚未建计划'
  }

  if (view.planCompleted || view.slotIndex >= view.slotCount) {
    return '已完成'
  }

  return `第 ${Math.max(view.slotIndex, 0) + 1} / ${view.slotCount} 步`
}

/**
 * 选项值里的席位：只认规则层 `seat:N` 的编码（`web/AGENTS.md` §4 允许的映射，
 * 不是规则推断）；不是席位选择的选项（是 / 否、角色 slug、玩家对等）返回 null。
 */
export function seatOfOptionValue(value: string): number | null {
  const prefix = 'seat:'
  if (!value.startsWith(prefix)) {
    return null
  }

  const digits = value.slice(prefix.length)
  if (!/^[0-9]+$/.test(digits)) {
    return null
  }

  const seat = Number.parseInt(digits, 10)
  return Number.isInteger(seat) && seat >= 1 && seat <= 1_000 ? seat : null
}

/**
 * 该选项指向的席位是否**已死亡**（按状态账 `Life = Dead`）。
 * 非席位选项、该席未观测一律 false——"未观测 ≠ 默认值"，绝不把未知标成已死亡。
 */
export function optionSeatIsDead(view: StorytellerViewDto, value: string): boolean {
  const seat = seatOfOptionValue(value)
  if (seat === null) {
    return false
  }

  const entry = view.seats.find((candidate) => candidate.seat === seat)
  return entry?.facts.some((fact) => fact.dimension === DIMENSION_LIFE && fact.value === LIFE_DEAD) ?? false
}

/** 说书人现在最需要处理的席位：卡点优先，其次待裁定，再次当前行动者。 */
export function attentionSeatOf(view: StorytellerViewDto): number | null {
  return view.pending?.seat ?? decisionSeatOf(view) ?? view.currentSlotActor ?? null
}

/** 圆环上的一个位置（百分比；`null` = 该下标不在圆环上）。 */
export interface RingPosition {
  xPercent: number
  yPercent: number
}

/**
 * 席位牌在圆环上的位置：席位 1 从正上方起、顺时针均匀分布。
 * 单席时居中；半径 38% 是留给牌宽的余量（牌以 `translate(-50%, -50%)` 挂在这个点上）。
 */
export function ringPosition(index: number, count: number): RingPosition | null {
  if (!Number.isInteger(index) || !Number.isInteger(count) || count <= 0 || index < 0 || index >= count) {
    return null
  }

  if (count === 1) {
    return { xPercent: 50, yPercent: 50 }
  }

  const angle = (index / count) * Math.PI * 2 - Math.PI / 2
  const radius = 38
  const round = (value: number) => Math.round(value * 1000) / 1000
  return {
    xPercent: round(50 + radius * Math.cos(angle)),
    yPercent: round(50 + radius * Math.sin(angle)),
  }
}

/** 牌面的可访问名称 / 悬停标题：席位 + 角色 + 生死 + 标记。 */
export function seatTitleOf(model: SeatCardModel): string {
  const parts = [`${model.seat} 号`]
  if (model.character !== null) {
    parts.push(characterLabelOf(model.character))
  } else if (!model.observed) {
    parts.push('尚未观测')
  }

  if (model.life === LIFE_DEAD) {
    parts.push('已死亡')
  } else if (model.life === LIFE_ALIVE) {
    parts.push('存活')
  }

  for (const mark of model.marks) {
    parts.push(mark.detail === null ? mark.label : `${mark.label}（${mark.detail}）`)
  }

  // 注记（D-0019）也算席位的可读状态：全文进 title / 无障碍名称，牌面只显示截断 token。
  for (const annotation of model.annotations) {
    parts.push(`注记：${annotation.text}`)
  }

  return parts.join('，')
}
