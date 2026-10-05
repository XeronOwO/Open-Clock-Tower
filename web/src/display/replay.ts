/**
 * 复盘的派生计算（纯函数）：把服务端按事件序号下发的步骤折成"某一刻的盘面"。
 *
 * 边界（D-0020 / web/AGENTS.md §4）：
 * - 增量是服务端投影的事实，前端只做**按序号合并**，不推断任何规则；
 * - 未观测 ≠ 默认值：缺维度保持 null，绝不画成"存活 / 健康"；
 * - 服务端数据是不可信输入：坏步骤让整页显式失败（宁可报错，不静默跳步）。
 */
import type {
  ReplayMarkerDto,
  ReplaySeatDeltaDto,
  ReplayStepDto,
  ReplayViewDto,
  SeatDisplayNameDto,
  SeatStateFactDto,
} from '@/contracts/game'
import {
  asArray,
  asBoolean,
  asCount,
  asSizedText,
  asText,
  normalizeSeatNames,
  seatDisplayOf,
} from '@/display/format'
import { buildSeatMarks, type SeatCardModel } from '@/display/grimoire'

/** 单维度的已知值 + 归因（复盘增量按事件序号覆盖）。 */
export interface ReplayFact {
  value: string
  reason: string
  causedBy: number | null
}

/** 某一刻的席位盘面：五个可观测维度的已知事实。 */
export interface ReplaySeatBoard {
  seat: number
  character: ReplayFact | null
  alignment: ReplayFact | null
  life: ReplayFact | null
  drunk: ReplayFact | null
  poison: ReplayFact | null
}

/** 某一刻的整盘：席位号 → 盘面。 */
export type ReplayBoard = ReadonlyMap<number, ReplaySeatBoard>

const STEP_KIND_LABELS: Record<string, string> = {
  Phase: '阶段',
  Slot: '槽位',
  Request: '玩家选择',
  Decision: '说书人裁定',
  Ability: '能力结算',
  Information: '信息',
  State: '状态变化',
  Effect: '效果',
  Day: '白天',
  Trigger: '触发',
  Control: '控制',
  Outcome: '终局',
}

/** 复盘标记的文案口径（术语表 §7：击杀箭头 / 换手 / 换角；状态名与实时魔典一致）。 */
const MARKER_LABELS: Record<string, string> = {
  'kill-arrow': '恶魔击杀',
  shroud: '死亡',
  'character-change': '换角',
  'role-rebind': '换手',
  poisoned: '中毒',
  drunk: '醉酒',
  // 旅行者与窗口（票据 traveller-and-exile · D7）：加入 / 离场 / 流放 / 保护 / 屠夫窗口 / 两种效果窗口。
  'traveller-joined': '旅行者加入',
  'traveller-departed': '旅行者离场',
  exile: '流放',
  protected: '受死亡保护',
  'extra-nomination': '额外提名窗口',
  'regained-ability': '重获能力',
  // 亡骨魔「保留能力」（R-0056）：死者从未失去能力，与「重获能力」区分开——那个到下个黄昏到期，这个不。
  'retained-ability': '保留能力',
  'effect-window': '效果窗口',
}

/** 步骤族文案（未知取值原样回显，不猜）。 */
export function stepKindLabelOf(kind: string): string {
  return STEP_KIND_LABELS[kind] ?? kind
}

/** 标记类别文案（未知取值原样回显）。 */
export function markerLabelOf(kind: string): string {
  return MARKER_LABELS[kind] ?? kind
}

/** 标记的一句话说明：有向标记写「A 号 → B 号」，其余写席位 + 补充文本；席位走 D-0021 统一口径。 */
export function markerTextOf(
  marker: ReplayMarkerDto,
  seatNames: readonly SeatDisplayNameDto[] = [],
): string {
  const head =
    marker.from !== null && marker.to !== null
      ? `${seatDisplayOf(marker.from, seatNames)} → ${seatDisplayOf(marker.to, seatNames)}`
      : marker.seat !== null
        ? seatDisplayOf(marker.seat, seatNames)
        : ''
  return [head, marker.text ?? ''].filter((part) => part.length > 0).join(' · ')
}

/** 未知载荷 → 复盘视图；任一步骤不可识别时返回 null（不静默跳步）。 */
export function normalizeReplayView(raw: unknown): ReplayViewDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const view = raw as Record<string, unknown>
  const sequence = asCount(view['sequence'])
  if (sequence === null) {
    return null
  }

  const parsed = asArray<unknown>(view['steps']).map(normalizeReplayStep)
  if (parsed.some((step) => step === null)) {
    return null
  }

  return {
    sequence,
    ended: asBoolean(view['ended']) ?? false,
    hasMore: asBoolean(view['hasMore']) ?? false,
    steps: parsed as ReplayStepDto[],
    seatNames: normalizeSeatNames(view['seatNames']),
  }
}

/** 未知载荷 → 复盘步骤；缺序号 / 类型 / 文案时返回 null（表达不了就整页失败）。 */
export function normalizeReplayStep(raw: unknown): ReplayStepDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const step = raw as Record<string, unknown>
  const sequence = asCount(step['sequence'])
  const kind = asText(step['kind'])
  const summary = asSizedText(step['summary'], 512)
  if (sequence === null || kind === null || summary === null) {
    return null
  }

  return {
    sequence,
    kind,
    phase: asText(step['phase']),
    summary,
    detail: asSizedText(step['detail'], 2048),
    // 坏条目单条降级（少显示一条增量 / 标记），不炸整步；步骤本身坏了才整页失败。
    seats: asArray<unknown>(step['seats'])
      .map(normalizeSeatDelta)
      .filter((delta): delta is ReplaySeatDeltaDto => delta !== null),
    markers: asArray<unknown>(step['markers'])
      .map(normalizeMarker)
      .filter((marker): marker is ReplayMarkerDto => marker !== null),
  }
}

/** 未知载荷 → 席位增量；缺席位号时返回 null。 */
export function normalizeSeatDelta(raw: unknown): ReplaySeatDeltaDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const delta = raw as Record<string, unknown>
  const seat = asCount(delta['seat'])
  if (seat === null) {
    return null
  }

  return {
    seat,
    life: asText(delta['life']),
    character: asText(delta['character']),
    previousCharacter: asText(delta['previousCharacter']),
    alignment: asText(delta['alignment']),
    drunk: asText(delta['drunk']),
    poison: asText(delta['poison']),
    reason: asSizedText(delta['reason'], 512),
    causedBy: asCount(delta['causedBy']),
  }
}

/** 未知载荷 → 复盘标记；缺类别时返回 null。 */
export function normalizeMarker(raw: unknown): ReplayMarkerDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const marker = raw as Record<string, unknown>
  const kind = asText(marker['kind'])
  if (kind === null) {
    return null
  }

  return {
    kind,
    seat: asCount(marker['seat']),
    from: asCount(marker['from']),
    to: asCount(marker['to']),
    text: asSizedText(marker['text'], 256),
  }
}

/**
 * 折出第 <paramref name="index"/> 步之后的盘面（含该步）。
 * 从第 0 步线性重放增量——即使从末尾回退到开头也不会丢状态（票据矩阵行 7 的"按序号重建"）。
 */
export function boardAt(steps: readonly ReplayStepDto[], index: number): ReplayBoard {
  const board = new Map<number, ReplaySeatBoard>()

  for (let stepIndex = 0; stepIndex <= index && stepIndex < steps.length; stepIndex++) {
    const step = steps[stepIndex]
    if (step === undefined) {
      continue
    }

    for (const delta of step.seats) {
      const current = board.get(delta.seat) ?? emptyBoard(delta.seat)
      board.set(delta.seat, {
        seat: delta.seat,
        character: mergedFact(current.character, delta.character, delta),
        alignment: mergedFact(current.alignment, delta.alignment, delta),
        life: mergedFact(current.life, delta.life, delta),
        drunk: mergedFact(current.drunk, delta.drunk, delta),
        poison: mergedFact(current.poison, delta.poison, delta),
      })
    }
  }

  return board
}

/** 盘面上出现过的席位号（升序，封顶 64 与实时魔典同口径）；一个都没有时退回本人席位。 */
export function seatNumbersOf(steps: readonly ReplayStepDto[], fallbackSeat: number | null): number[] {
  let max = fallbackSeat ?? 0
  const consider = (seat: number | null): void => {
    if (seat !== null && seat > max) {
      max = seat
    }
  }

  for (const step of steps) {
    for (const delta of step.seats) {
      consider(delta.seat)
    }

    for (const marker of step.markers) {
      consider(marker.seat)
      consider(marker.from)
      consider(marker.to)
    }
  }

  return Array.from({ length: Math.min(max, 64) }, (_, index) => index + 1)
}

/** 由盘面构造一个席位牌模型（复用实时魔典的 SeatCardModel 与标记口径，票据矩阵行 9）。 */
export function seatCardOf(
  board: ReplayBoard,
  seat: number,
  displayName: string | null = null,
): SeatCardModel {
  const entry = board.get(seat)
  const facts: SeatStateFactDto[] = []
  const push = (dimension: string, fact: ReplayFact | null | undefined): void => {
    if (fact !== null && fact !== undefined) {
      facts.push({
        dimension,
        value: fact.value,
        reason: fact.reason,
        causedBy: fact.causedBy,
        effectId: null,
      })
    }
  }

  push('Life', entry?.life)
  push('Character', entry?.character)
  push('Alignment', entry?.alignment)
  push('Drunk', entry?.drunk)
  push('Poison', entry?.poison)

  return {
    seat,
    displayName,
    observed: entry !== undefined,
    character: entry?.character?.value ?? null,
    alignment: entry?.alignment?.value ?? null,
    life: entry?.life?.value ?? null,
    drunk: entry?.drunk?.value ?? null,
    poison: entry?.poison?.value ?? null,
    madnesses: [],
    facts,
    // 复盘 v1 的牌面只呈现五维与状态标记；效果链与说书人注记不进复盘（D-0019 / 矩阵行 3 的口径）。
    effects: [],
    annotations: [],
    lostAbilityMarkers: [],
    marks: buildSeatMarks(facts, [], []),
  }
}

function emptyBoard(seat: number): ReplaySeatBoard {
  return { seat, character: null, alignment: null, life: null, drunk: null, poison: null }
}

/** 增量里非 null 的维度覆盖旧值；null = 本次未观测，保留旧值（六维度独立）。 */
function mergedFact(
  current: ReplayFact | null,
  value: string | null,
  delta: ReplaySeatDeltaDto,
): ReplayFact | null {
  return value === null ? current : { value, reason: delta.reason ?? '', causedBy: delta.causedBy }
}
