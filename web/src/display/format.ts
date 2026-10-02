/**
 * 把服务端下发（不可信）的数据规范成可安全渲染的形状。
 *
 * 依据架构 §4.4：前端把服务端数据当不可信输入，长度 / 类型 / 范围都要防御性处理。
 * 这里的原则是**渲染降级、判断不猜**：字段缺失或类型不对就退化成占位符或空集合，
 * 绝不因为一个坏字段让整个面板白屏，也绝不用缺失数据编出一个结论。
 */

import type {
  DayNominationDto,
  DayViewDto,
  DecisionOptionDto,
  EffectDto,
  OperationRequestVoidedDto,
  RoomHealthDto,
  SeatChangeDto,
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

/** 席位号 → 「N 号」。非法值退化成占位符。 */
export function seatLabelOf(seat: number | null | undefined): string {
  return typeof seat === 'number' && Number.isFinite(seat) ? `${seat} 号` : '—'
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
    malfunction: asText(ability['malfunction']),
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
            slotId: asText((pending as Record<string, unknown>)['slotId']) ?? '',
            slotIndex: asCount((pending as Record<string, unknown>)['slotIndex']) ?? 0,
            waitingSeconds: asNumber((pending as Record<string, unknown>)['waitingSeconds']),
          },
    awaitingDecisionId: asText(view['awaitingDecisionId']),
    awaitingDecisionContext: asText(view['awaitingDecisionContext']),
    awaitingDecisionOptions: asArray<unknown>(view['awaitingDecisionOptions'])
      .map(normalizeOption)
      .filter((option): option is DecisionOptionDto => option !== null),
    blockedReason: asText(view['blockedReason']),
    currentSlotActor: asNumber(view['currentSlotActor']),
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
            malfunction: asText((view['lastResolution'] as Record<string, unknown>)['malfunction']),
            note: asText((view['lastResolution'] as Record<string, unknown>)['note']),
            sequence: asCount((view['lastResolution'] as Record<string, unknown>)['sequence']) ?? 0,
          },
    stepDigest: normalizeStepDigest(view['stepDigest']),
    lastVoidedRequest: normalizeVoidedRequest(view['lastVoidedRequest']),
    day: normalizeDayView(view['day']),
  }
}
