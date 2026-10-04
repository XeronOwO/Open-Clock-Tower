/**
 * 把服务端下发（不可信）的数据规范成可安全渲染的形状。
 *
 * 依据架构 §4.4：前端把服务端数据当不可信输入，长度 / 类型 / 范围都要防御性处理。
 * 这里的原则是**渲染降级、判断不猜**：字段缺失或类型不对就退化成占位符或空集合，
 * 绝不因为一个坏字段让整个面板白屏，也绝不用缺失数据编出一个结论。
 */

import type {
  BarberNightDto,
  DayNominationDto,
  DayViewDto,
  DecisionOptionDto,
  DeferredDeathDto,
  EffectDto,
  FangGuInfectionDto,
  GameOutcomeDto,
  KlutzChoiceDto,
  LostAbilityMarkerDto,
  OperationRequestVoidedDto,
  PitHagNightDto,
  PlayerLifeDto,
  RoomHealthDto,
  SeatAnnotationDto,
  SeatChangeDto,
  SeatDisplayNameDto,
  SeatStateDto,
  SeatStateFactDto,
  SlotAbilityDto,
  StepDigestDto,
  StorytellerViewDto,
} from '@/contracts/game'

/** 安全取字符串：非字符串或空串退化成 null。 */
export function asText(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null
}

/** 安全取数字：只接受有限数字。 */
export function asNumber(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null
}

/** 安全取布尔。 */
export function asBoolean(value: unknown): boolean | null {
  return typeof value === 'boolean' ? value : null
}

/** 安全取数组：不是数组就退化成空集合（面板显示"暂无"，而不是崩掉）。 */
export function asArray<T>(value: unknown): readonly T[] {
  return Array.isArray(value) ? (value as readonly T[]) : []
}

/** 安全取"有界文本"：类型 / 非空 / 长度上限；超长截断，绝不把面板撑爆。 */
export function asSizedText(value: unknown, maxLength: number): string | null {
  const text = asText(value)
  if (text === null) {
    return null
  }

  return text.length <= maxLength ? text : text.slice(0, maxLength)
}

/** 安全取字符串数组：非字符串项丢掉；不是数组就退化成空集合（R-0004 的失效分类可并列多条）。 */
export function asTextArray(value: unknown): string[] {
  return asArray<unknown>(value)
    .map((item) => asText(item))
    .filter((item): item is string => item !== null)
}

/**
 * 安全取连接级凭据：服务端数据是**不可信输入**，凭据必须是有界、无空白 / 控制字符的串。
 * 凭据只用于发命令，绝不渲染、绝不写日志、绝不落盘（D-0012）。
 */
export function asCredential(value: unknown): string | null {
  if (typeof value !== 'string' || value.length < 16 || value.length > 512) {
    return null
  }

  return /^[A-Za-z0-9_-]+$/.test(value) ? value : null
}

/** 安全取计数（非负整数）：席位 / 序号 / 下标必须落在这个形状里，浮点与负数一律视为坏载荷。 */
export function asCount(value: unknown, max = 1_000_000): number | null {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0 && value <= max
    ? value
    : null
}

/**
 * 安全取席位号：正整数且在 1..max 内，否则 null。
 * 0 / 负数 / 小数 / 超界都是坏载荷——归属席位这类字段拿到 0 会短路前端回退链（「定位到 0 号」）。
 */
export function asSeatNumber(value: unknown, max = 1_000): number | null {
  const number = asCount(value, max)
  return number !== null && number >= 1 ? number : null
}

/** 席位号 → 「N 号」。非法值退化成占位符。 */
export function seatLabelOf(seat: number | null | undefined): string {
  return typeof seat === 'number' && Number.isFinite(seat) ? `${seat} 号` : '—'
}

/** 单条注记的文本上限（与服务端 `SeatAnnotationText.MaxLength` 同一口径，D-0019）。 */
export const MAX_ANNOTATION_LENGTH = 120

/** 每席注记条数上限（与服务端 `SeatAnnotationText.MaxPerSeat` 同一口径）。 */
export const MAX_ANNOTATIONS_PER_SEAT = 5

/** 说书人视图里注记条数的防御上限：坏载荷不撑爆面板（每席上限 × 座位数的同阶）。 */
export const MAX_ANNOTATIONS = 64

/** 牌面 token 显示的字符上限：超长截断，全文仍留在 title 与操作台。 */
export const ANNOTATION_TOKEN_LENGTH = 16

/**
 * 一条注记：id / 席位必须是有界正整数、文本非空；形状不对就丢掉这一条（宁可少一条，不猜）。
 * 文本按 `MAX_ANNOTATION_LENGTH` 截断——服务端本就不会超，这里是"不可信输入"的第二道防线。
 */
export function normalizeSeatAnnotation(raw: unknown): SeatAnnotationDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const annotation = raw as Record<string, unknown>
  const id = asCount(annotation['id'], 100_000)
  const seat = asCount(annotation['seat'], 1_000)
  const text = asSizedText(annotation['text'], MAX_ANNOTATION_LENGTH)
  if (id === null || id < 1 || seat === null || seat < 1 || text === null) {
    return null
  }

  return { id, seat, text }
}

/**
 * 控制字符（含换行 / 制表）折成空格。
 * 刻意不用控制字符正则：ESLint 的 `no-control-regex` 会拦下这类字面量，逐字符判定等价且更清楚。
 */
export function replaceControlCharacters(text: string): string {
  let result = ''
  for (const character of text) {
    const code = character.codePointAt(0) ?? 0
    result += code < 0x20 || code === 0x7f ? ' ' : character
  }

  return result
}

/**
 * 牌面 token 的文案：控制字符（含换行）与连续空白折成单个空格，再截断到上限。
 * 服务端已归一化过一次，这里仍按不可信输入处理（架构 §4.4）。
 */
export function annotationTokenTextOf(
  text: string,
  maxLength: number = ANNOTATION_TOKEN_LENGTH,
): string {
  const cleaned = replaceControlCharacters(text).replace(/\s+/g, ' ').trim()
  return cleaned.length <= maxLength ? cleaned : `${cleaned.slice(0, maxLength)}…`
}

/** 归因方 → 「N 号」；无人可归因时 null（调用方决定怎么显示）。 */
export function causedByLabelOf(causedBy: number | null | undefined): string | null {
  return typeof causedBy === 'number' && Number.isFinite(causedBy) ? `${causedBy} 号` : null
}

/** 时刻 → 本地 HH:mm:ss；解析不了就原样回显。 */
export function clockTimeOf(recordedAt: string | null | undefined): string {
  if (typeof recordedAt !== 'string' || recordedAt.length === 0) {
    return '—'
  }

  const parsed = new Date(recordedAt)
  if (Number.isNaN(parsed.getTime())) {
    return recordedAt
  }

  return parsed.toLocaleTimeString('zh-CN', { hour12: false })
}

/** 秒数 → 「N 秒」；未知时 null。 */
export function waitingSecondsTextOf(waitingSeconds: number | null | undefined): string | null {
  const seconds = asNumber(waitingSeconds)
  return seconds === null ? null : `${seconds.toFixed(1)} 秒`
}

/** 归一化一条维度事实。 */
export function normalizeFact(raw: unknown): SeatStateFactDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const fact = raw as Record<string, unknown>
  const dimension = asText(fact['dimension'])
  const value = asText(fact['value'])
  const reason = asText(fact['reason'])
  if (dimension === null || value === null || reason === null) {
    return null
  }

  return {
    dimension,
    value,
    reason,
    causedBy: asCount(fact['causedBy']),
    effectId: asText(fact['effectId']),
  }
}

/** 归一化状态账的一行。 */
export function normalizeSeatState(raw: unknown): SeatStateDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const entry = raw as Record<string, unknown>
  const seat = asCount(entry['seat'])
  if (seat === null) {
    return null
  }

  return {
    seat,
    facts: asArray<unknown>(entry['facts'])
      .map(normalizeFact)
      .filter((fact): fact is SeatStateFactDto => fact !== null),
    // 疯狂要求是自由文本，必须有界：超长只截断显示，不把牌面撑爆（架构 §4.4 的有界化）。
    madnesses: asArray<unknown>(entry['madnesses'])
      .map((raw) => asSizedText(raw, 200))
      .filter((text): text is string => text !== null),
  }
}

/** 归一化本槽位能力判定；缺 basis 视为坏载荷（表达不了"依据"就不编结论）。 */
export function normalizeSlotAbility(raw: unknown): SlotAbilityDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const ability = raw as Record<string, unknown>
  const basis = asText(ability['basis'])
  if (basis === null) {
    return null
  }

  return {
    basis,
    ability: asText(ability['ability']),
    effective: asBoolean(ability['effective']),
    malfunctions: asTextArray(ability['malfunctions']),
    note: asText(ability['note']),
    sequence: asNumber(ability['sequence']),
  }
}

/** 归一化每步摘要；缺席位号视为坏载荷。 */
export function normalizeStepDigest(raw: unknown): StepDigestDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const digest = raw as Record<string, unknown>
  const seat = asCount(digest['seat'])
  if (seat === null) {
    return null
  }

  return {
    seat,
    character: asText(digest['character']),
    state:
      digest['state'] === null || typeof digest['state'] !== 'object'
        ? null
        : normalizeSeatState(digest['state']),
    ability: normalizeSlotAbility(digest['ability']),
    optionCount: asCount(digest['optionCount']),
    onNoOption: asText(digest['onNoOption']),
  }
}

/** 归一化最近一次请求作废；缺请求标识 / 序号 / 原因视为坏载荷。 */
export function normalizeVoidedRequest(raw: unknown): OperationRequestVoidedDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const voided = raw as Record<string, unknown>
  const requestId = asText(voided['requestId'])
  const reason = asText(voided['reason'])
  const sequence = asCount(voided['sequence'])
  if (requestId === null || reason === null || sequence === null) {
    return null
  }

  return { sequence, requestId, reason, note: asSizedText(voided['note'], 512) }
}

/** 归一化一条效果归因。 */
export function normalizeEffect(raw: unknown): EffectDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const effect = raw as Record<string, unknown>
  const effectId = asText(effect['effectId'])
  const ability = asText(effect['ability'])
  const source = asCount(effect['source'])
  const target = asCount(effect['target'])
  if (effectId === null || ability === null || source === null || target === null) {
    return null
  }

  return {
    effectId,
    kind: asText(effect['kind']) ?? '未知类型',
    ability,
    source,
    target,
    sourceCharacter: asText(effect['sourceCharacter']),
    grantedCharacter: asText(effect['grantedCharacter']),
    // 契约上 Terminated 必有（服务端每条路径都显式赋值）；缺失只可能是篡改或服务端 bug，
    // 那时按 false 渲染是**在坏载荷上保守**，不是把"未知"说成结论（生产者不会漏）。
    terminated: asBoolean(effect['terminated']) ?? false,
    terminationKind: asText(effect['terminationKind']),
    terminationReason: asText(effect['terminationReason']),
    terminationCausedBy: asNumber(effect['terminationCausedBy']),
  }
}

/** 归一化一条座位状态变化。 */
export function normalizeSeatChange(raw: unknown): SeatChangeDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const change = raw as Record<string, unknown>
  const seat = asCount(change['seat'])
  const reason = asSizedText(change['reason'], 512)
  const sequence = asCount(change['sequence'])
  if (seat === null || reason === null || sequence === null) {
    return null
  }

  return {
    seat,
    life: asText(change['life']),
    character: asText(change['character']),
    alignment: asText(change['alignment']),
    drunk: asText(change['drunk']),
    poison: asText(change['poison']),
    reason,
    causedBy: asNumber(change['causedBy']),
    effectId: asText(change['effectId']),
    sequence,
    recordedAt: asText(change['recordedAt']) ?? '',
  }
}

/**
 * 归一化房间健康位：契约上 `degraded` 必有（服务端每条路径都显式赋值，见 `ProjectionMapper`）；
 * 缺失只可能来自篡改或服务端 bug，那时按"正常"渲染是**在坏载荷上保守**，不是把"未知"说成结论。
 */
export function normalizeRoomHealth(raw: unknown): RoomHealthDto {
  if (raw === null || typeof raw !== 'object') {
    return { degraded: false, reason: null, since: null }
  }

  const health = raw as Record<string, unknown>
  return {
    degraded: asBoolean(health['degraded']) ?? false,
    reason: asSizedText(health['reason'], 512),
    since: asText(health['since']),
  }
}

/** 归一化一条合法选项。 */
export function normalizeOption(raw: unknown): DecisionOptionDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const option = raw as Record<string, unknown>
  const value = asText(option['value'])
  if (value === null) {
    return null
  }

  return { value, preview: asSizedText(option['preview'], 512) ?? value }
}

/** 归一化一次白天提名；缺关键字段时返回 null（宁可少显示，不编造票数）。 */
export function normalizeDayNomination(raw: unknown): DayNominationDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const nomination = raw as Record<string, unknown>
  const index = asCount(nomination['index'])
  const nominator = asCount(nomination['nominator'])
  const nominee = asCount(nomination['nominee'])
  const status = asText(nomination['status'])
  if (index === null || nominator === null || nominee === null || status === null) {
    return null
  }

  return {
    index,
    nominator,
    nominee,
    status,
    votes: asCount(nomination['votes']) ?? 0,
    voters: asArray<unknown>(nomination['voters'])
      .map((voter) => asCount(voter))
      .filter((voter): voter is number => voter !== null),
  }
}

/** 归一化白天公开事实；缺天数 / 状态时返回 null（不编造"某一天"）。 */
export function normalizeDayView(raw: unknown): DayViewDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const day = raw as Record<string, unknown>
  const dayNumber = asCount(day['dayNumber'])
  const status = asText(day['status'])
  if (dayNumber === null || status === null) {
    return null
  }

  return {
    dayNumber,
    status,
    nominations: asArray<unknown>(day['nominations'])
      .map(normalizeDayNomination)
      .filter((nomination): nomination is DayNominationDto => nomination !== null),
    aboutToBeExecuted: asCount(day['aboutToBeExecuted']),
    executed: asCount(day['executed']),
    openNominationIndex: asCount(day['openNominationIndex']),
  }
}

/** 公开生死面条目的条数上限：一桌人就这么多，超出的一律丢弃（坏数据不撑爆面板）。 */
export const MAX_PUBLIC_LIFE_ENTRIES = 64

/**
 * 归一化一条公开生死事实（牌面 / 公告同形）；缺席位或状态时返回 null
 * （宁可少一条，不编一个状态——服务端数据是输入，不是保证，web/AGENTS §4）。
 */
export function normalizePlayerLife(raw: unknown): PlayerLifeDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const entry = raw as Record<string, unknown>
  const seat = asCount(entry['seat'], 1_000)
  const state = asSizedText(entry['state'], 32)
  return seat === null || seat < 1 || state === null ? null : { seat, state }
}

/** 席位名条目的条数上限：一桌人就这么多，超出的一律丢弃（坏数据不撑爆面板）。 */
export const MAX_SEAT_NAMES = 64

/**
 * 归一化一条「席位 → 玩家名」（D-0021）：席位必须是 1..1000 的正整数、名字是有界文本；
 * 坏条目直接丢弃（宁可少一条，不编一个人名）。
 */
export function normalizeSeatName(raw: unknown): SeatDisplayNameDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const entry = raw as Record<string, unknown>
  const seat = asCount(entry['seat'], 1_000)
  const displayName = asSizedText(entry['displayName'], 64)
  return seat === null || seat < 1 || displayName === null ? null : { seat, displayName }
}

/** 归一化整份「席位 → 玩家名」映射：同一席位只留第一条，条数封顶。 */
export function normalizeSeatNames(raw: unknown): SeatDisplayNameDto[] {
  const seen = new Set<number>()
  const result: SeatDisplayNameDto[] = []
  for (const item of asArray<unknown>(raw)) {
    const name = normalizeSeatName(item)
    if (name === null || seen.has(name.seat)) {
      continue
    }

    seen.add(name.seat)
    result.push(name)
    if (result.length >= MAX_SEAT_NAMES) {
      break
    }
  }

  return result
}

/**
 * 席位对应的玩家名（D-0021）；这一席没有名字（游客 / 未认领）时返回 null。
 * 呈现层据此回退席位号，不编一个人名。
 */
export function displayNameOf(
  seat: number,
  seatNames: readonly SeatDisplayNameDto[],
): string | null {
  return seatNames.find((item) => item.seat === seat)?.displayName ?? null
}

/**
 * 席位显示文本（D-0021 的**唯一口径**）：「N 号 · 玩家名」；这一席没有名字时回退「N 号」。
 * 席位牌 / 提名 / 投票 / 归属 / 复盘共用这一份拼接口径（票据矩阵行 5）。
 */
export function seatDisplayOf(seat: number, seatNames: readonly SeatDisplayNameDto[]): string {
  const name = displayNameOf(seat, seatNames)
  return name === null ? `${seat} 号` : `${seat} 号 · ${name}`
}

/**
 * 选项文案本地化（D-0021）：选项值形如 `seat:N` / `pair:A+B` 且**这一席已经有玩家名**时，
 * 用统一席位口径（「N 号 · 玩家名」）取代服务端原文；没名字一律保留服务端原文
 * （服务端那句带语境，如「3 号玩家」，比光秃秃的「3 号」更有用——游客面上不动它）。
 */
export function optionDisplayOf(
  option: DecisionOptionDto,
  seatNames: readonly SeatDisplayNameDto[],
): string {
  const value = option.value
  if (value.startsWith('seat:')) {
    const seat = Number.parseInt(value.slice('seat:'.length), 10)
    if (Number.isInteger(seat) && seat > 0 && displayNameOf(seat, seatNames) !== null) {
      return seatDisplayOf(seat, seatNames)
    }

    return option.preview
  }

  if (value.startsWith('pair:')) {
    const parts = value.slice('pair:'.length).split('+')
    const first = Number.parseInt(parts[0] ?? '', 10)
    const second = Number.parseInt(parts[1] ?? '', 10)
    if (
      parts.length === 2
      && Number.isInteger(first)
      && Number.isInteger(second)
      && first > 0
      && second > 0
      && displayNameOf(first, seatNames) !== null
      && displayNameOf(second, seatNames) !== null
    ) {
      return `${seatDisplayOf(first, seatNames)} + ${seatDisplayOf(second, seatNames)}`
    }
  }

  return option.preview
}

/** 归一化整个说书人视图。任何缺失都退化成空集合 / null，不编造状态。 */
export function normalizeStorytellerView(raw: unknown): StorytellerViewDto {
  const view = (raw ?? {}) as Record<string, unknown>
  const pending = view['pending']

  return {
    sequence: asCount(view['sequence']) ?? 0,
    phase: asText(view['phase']) ?? '未开始',
    control: asText(view['control']) ?? '未知',
    health: normalizeRoomHealth(view['health']),
    slotIndex: asCount(view['slotIndex']) ?? 0,
    slotCount: asCount(view['slotCount']) ?? 0,
    currentSlotId: asText(view['currentSlotId']),
    planCompleted: asBoolean(view['planCompleted']) ?? false,
    pending:
      pending === null || typeof pending !== 'object'
        ? null
        : {
            seat: asCount((pending as Record<string, unknown>)['seat']) ?? 0,
            requestId: asText((pending as Record<string, unknown>)['requestId']) ?? '',
            slotId: asText((pending as Record<string, unknown>)['slotId']),
            slotIndex: asCount((pending as Record<string, unknown>)['slotIndex']),
            triggerReason: asSizedText(
              (pending as Record<string, unknown>)['triggerReason'],
              512,
            ),
            waitingSeconds: asNumber((pending as Record<string, unknown>)['waitingSeconds']),
          },
    awaitingDecisionId: asText(view['awaitingDecisionId']),
    awaitingDecisionContext: asText(view['awaitingDecisionContext']),
    awaitingDecisionOptions: asArray<unknown>(view['awaitingDecisionOptions'])
      .map(normalizeOption)
      .filter((option): option is DecisionOptionDto => option !== null),
    awaitingDecisionSeat: asSeatNumber(view['awaitingDecisionSeat']),
    blockedReason: asText(view['blockedReason']),
    currentSlotActor: asSeatNumber(view['currentSlotActor']),
    currentSlotContext: asText(view['currentSlotContext']),
    recentSeatChanges: asArray<unknown>(view['recentSeatChanges'])
      .map(normalizeSeatChange)
      .filter((change): change is SeatChangeDto => change !== null),
    seats: asArray<unknown>(view['seats'])
      .map(normalizeSeatState)
      .filter((seat): seat is SeatStateDto => seat !== null),
    effects: asArray<unknown>(view['effects'])
      .map(normalizeEffect)
      .filter((effect): effect is EffectDto => effect !== null),
    abilityUses: asArray<unknown>(view['abilityUses'])
      .map((rawUse) => {
        if (rawUse === null || typeof rawUse !== 'object') {
          return null
        }

        const use = rawUse as Record<string, unknown>
        const seat = asNumber(use['seat'])
        const ability = asText(use['ability'])
        if (seat === null || ability === null) {
          return null
        }

        return { seat, ability, effective: asBoolean(use['effective']) ?? false }
      })
      .filter((use): use is { seat: number; ability: string; effective: boolean } => use !== null),
    malfunctions: asArray<unknown>(view['malfunctions'])
      .map((rawMalfunction) => {
        if (rawMalfunction === null || typeof rawMalfunction !== 'object') {
          return null
        }

        const malfunction = rawMalfunction as Record<string, unknown>
        const seat = asNumber(malfunction['seat'])
        const ability = asText(malfunction['ability'])
        const kind = asText(malfunction['kind'])
        if (seat === null || ability === null || kind === null) {
          return null
        }

        return { seat, ability, kind }
      })
      .filter(
        (malfunction): malfunction is { seat: number; ability: string; kind: string } =>
          malfunction !== null,
      ),
    lastResolution:
      view['lastResolution'] === null || typeof view['lastResolution'] !== 'object'
        ? null
        : {
            seat: asCount((view['lastResolution'] as Record<string, unknown>)['seat']) ?? 0,
            ability: asText((view['lastResolution'] as Record<string, unknown>)['ability']) ?? '',
            effective:
              asBoolean((view['lastResolution'] as Record<string, unknown>)['effective']) ?? false,
            malfunctions: asTextArray(
              (view['lastResolution'] as Record<string, unknown>)['malfunctions'],
            ),
            note: asText((view['lastResolution'] as Record<string, unknown>)['note']),
            sequence: asCount((view['lastResolution'] as Record<string, unknown>)['sequence']) ?? 0,
          },
    stepDigest: normalizeStepDigest(view['stepDigest']),
    lastVoidedRequest: normalizeVoidedRequest(view['lastVoidedRequest']),
    day: normalizeDayView(view['day']),
    outcome: normalizeGameOutcome(view['outcome']),
    klutzChoices: asArray<unknown>(view['klutzChoices'])
      .map(normalizeKlutzChoice)
      .filter((choice): choice is KlutzChoiceDto => choice !== null),
    seatNames: normalizeSeatNames(view['seatNames']),
    pitHagNight: normalizePitHagNight(view['pitHagNight']),
    fangGuInfection: normalizeFangGuInfection(view['fangGuInfection']),
    barberNight: normalizeBarberNight(view['barberNight']),
    annotations: asArray<unknown>(view['annotations'])
      .map(normalizeSeatAnnotation)
      .filter((annotation): annotation is SeatAnnotationDto => annotation !== null)
      .slice(0, MAX_ANNOTATIONS),
    lostAbilityMarkers: asArray<unknown>(view['lostAbilityMarkers'])
      .map(normalizeLostAbilityMarker)
      .filter((marker): marker is LostAbilityMarkerDto => marker !== null)
      .slice(0, MAX_LOST_ABILITY_MARKERS),
  }
}

/** 「失去能力」标记的条数上限：一桌人就这么多，超出的一律丢弃（坏数据不撑爆面板）。 */
export const MAX_LOST_ABILITY_MARKERS = 64

/**
 * 归一化一条「失去能力」标记（R-0040）；缺席位或能力时返回 null
 * （宁可少一条，不编一个状态——服务端数据是输入，不是保证）。
 */
export function normalizeLostAbilityMarker(raw: unknown): LostAbilityMarkerDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const entry = raw as Record<string, unknown>
  const seat = asCount(entry['seat'], 1_000)
  const ability = asSizedText(entry['ability'], 64)
  if (seat === null || seat < 1 || ability === null) {
    return null
  }

  return { seat, ability, note: asSizedText(entry['note'], 512) ?? '' }
}

/** 待定死亡的条数上限：一桌人就这么多，超出的一律丢弃（坏数据不撑爆面板）。 */
export const MAX_DEFERRED_DEATHS = 32

/**
 * 归一化一条待定死亡（R-0030）；缺席位或来源时返回 null
 * （宁可少一条，不编一个状态——服务端数据是输入，不是保证）。
 */
export function normalizeDeferredDeath(raw: unknown): DeferredDeathDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const entry = raw as Record<string, unknown>
  const target = asCount(entry['target'], 1_000)
  const source = asCount(entry['source'], 1_000)
  if (target === null || source === null || target < 1 || source < 1) {
    return null
  }

  return {
    target,
    source,
    ability: asText(entry['ability']) ?? '未知能力',
    note: asSizedText(entry['note'], 512) ?? '',
    // 缺字段 = 普通死亡（服务端旧版本或坏数据都不猜成"转化"）。
    transformation: entry['transformation'] === true,
  }
}

/**
 * 归一化麻脸巫婆之夜的死亡裁量窗口（R-0030）：形状不对就当成"今晚没有窗口"——
 * 宁可不给裁定面，也不让说书人对着一份坏数据做裁定。
 */
export function normalizePitHagNight(raw: unknown): PitHagNightDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const night = raw as Record<string, unknown>
  const source = asCount(night['source'], 1_000)
  const closesAfterSlotIndex = asCount(night['closesAfterSlotIndex'], 10_000)
  if (source === null || closesAfterSlotIndex === null) {
    return null
  }

  return {
    source,
    closesAfterSlotIndex,
    deferred: asArray<unknown>(night['deferred'])
      .slice(0, MAX_DEFERRED_DEATHS)
      .map(normalizeDeferredDeath)
      .filter((deferred): deferred is DeferredDeathDto => deferred !== null),
  }
}

/**
 * 归一化方古的「限一次」整局事实（R-0034）：形状不对就当成"还没用掉"——
 * 宁可不显示魔典中心标记，也不让说书人对着坏数据做判断。
 */
export function normalizeFangGuInfection(raw: unknown): FangGuInfectionDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const infection = raw as Record<string, unknown>
  const seat = asCount(infection['seat'], 1_000)
  const source = asCount(infection['source'], 1_000)
  if (seat === null || source === null || seat < 1 || source < 1) {
    return null
  }

  return { seat, source, note: asSizedText(infection['note'], 512) ?? '' }
}

/** 归一化「今晚理发」待处理事实（R-0033）；缺来源或来源非法时视为没有待处理事实。 */
export function normalizeBarberNight(raw: unknown): BarberNightDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const night = raw as Record<string, unknown>
  const source = asCount(night['source'], 1_000)
  if (source === null || source < 1) {
    return null
  }

  return { source, note: asSizedText(night['note'], 512) ?? '' }
}

/** 归一化胜负结论；缺序号 / 胜方 / 条件时返回 null（宁可少显示，不编一个结论）。 */
export function normalizeGameOutcome(raw: unknown): GameOutcomeDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const outcome = raw as Record<string, unknown>
  const sequence = asCount(outcome['sequence'])
  const winner = asText(outcome['winner'])
  const condition = asText(outcome['condition'])
  if (sequence === null || winner === null || condition === null) {
    return null
  }

  return { sequence, winner, condition, detail: asSizedText(outcome['detail'], 512) ?? '' }
}

/** 归一化呆瓜的公开选择记录；缺序号或席位时返回 null。 */
export function normalizeKlutzChoice(raw: unknown): KlutzChoiceDto | null {
  if (raw === null || typeof raw !== 'object') {
    return null
  }

  const choice = raw as Record<string, unknown>
  const sequence = asCount(choice['sequence'])
  const seat = asCount(choice['seat'])
  if (sequence === null || seat === null) {
    return null
  }

  const target = asCount(choice['target'])
  return {
    sequence,
    seat,
    target,
    made: asBoolean(choice['made']) ?? target !== null,
    detail: asSizedText(choice['detail'], 512) ?? '',
  }
}
