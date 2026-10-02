/**
 * 说书人命令入口：只做「参数拼装 + InvokeAsync + 回执规范化」，不做任何领域判断。
 *
 * 与 GameHub 的方法签名逐条对应（src/OpenClockTower.Server/GameHub.cs）。
 * 每条命令调用都带幂等键；幂等键由调用方持有，重试复用同一个键。
 */
import type { HubConnection } from '@microsoft/signalr'
import { asNumber, asText } from '@/display/format'

/** 命令回执的规范化结果：服务端拒绝 / 抛错都收敛成这里的一种形态。 */
export interface CommandOutcome {
  ok: boolean
  /** Accepted / Rejected / Duplicate / Failed；本地异常时为 Transport。 */
  kind: string
  sequence: number | null
  /** 人话说明（拒绝码 + 说明 / 异常消息）。 */
  message: string
}

/** 未知响应 → 回执；服务端字段缺失时降级，不编造"成功"。 */
export function normalizeOutcome(raw: unknown): CommandOutcome {
  if (raw === null || typeof raw !== 'object') {
    return { ok: false, kind: 'Failed', sequence: null, message: '回执形状不可识别' }
  }

  const result = raw as Record<string, unknown>
  const kind = asText(result['kind']) ?? 'Failed'
  const sequence = asNumber(result['sequence'])
  const rejectionCode = asText(result['rejectionCode'])
  const rejectionMessage = asText(result['rejectionMessage'])
  const failure = asText(result['failure'])
  const parts = [rejectionCode, rejectionMessage, failure].filter(
    (part): part is string => part !== null,
  )

  return {
    ok: kind === 'Accepted' || kind === 'Duplicate',
    kind,
    sequence,
    message: parts.length > 0 ? parts.join('：') : '',
  }
}

/** 发一条命令并规范化回执；传输层异常不吞，收敛成 Transport 回执。 */
export async function invokeCommand(
  connection: HubConnection,
  method: string,
  ...args: readonly unknown[]
): Promise<CommandOutcome> {
  try {
    const raw = await connection.invoke<unknown>(method, ...args)
    return normalizeOutcome(raw)
  } catch (error) {
    return {
      ok: false,
      kind: 'Transport',
      sequence: null,
      message: error instanceof Error ? error.message : String(error),
    }
  }
}

/** 分配角色：服务端会按会话席位名单与首版花名册重新校验。 */
export function assignCharacters(
  connection: HubConnection,
  assignments: readonly { seat: number; character: string }[],
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(connection, 'AssignCharacters', assignments, idempotencyKey)
}

/** 开夜（口径是引擎输入，R-0014）。 */
export function startNight(
  connection: HubConnection,
  nightNumber: number,
  variant: string,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(connection, 'StartNight', nightNumber, variant, idempotencyKey)
}

/** 代填挂起请求。 */
export function proxyFill(
  connection: HubConnection,
  requestId: string,
  optionValue: string,
  note: string | null,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(connection, 'ProxyFill', requestId, optionValue, note, idempotencyKey)
}

/** 强制作废挂起请求（原因必须是服务端认识的枚举名）。 */
export function voidRequest(
  connection: HubConnection,
  requestId: string,
  reason: string,
  note: string | null,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(connection, 'VoidRequest', requestId, reason, note, idempotencyKey)
}

/** 强推当前槽位（D-0014 兜底）。 */
export function forceAdvance(
  connection: HubConnection,
  reason: string,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(connection, 'ForceAdvance', reason, idempotencyKey)
}

/** 接管自动化。 */
export function takeOver(
  connection: HubConnection,
  reason: string,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(connection, 'TakeOver', reason, idempotencyKey)
}

/** 交还自动化。 */
export function releaseControl(
  connection: HubConnection,
  reason: string,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(connection, 'ReleaseControl', reason, idempotencyKey)
}

/** 了结裁定点（R-0009 自由决定）。 */
export function resolveDecisionPoint(
  connection: HubConnection,
  decisionPointId: string,
  decision: string | null,
  note: string | null,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(connection, 'ResolveDecisionPoint', decisionPointId, decision, note, idempotencyKey)
}

/**
 * 上报座位状态变化。
 * 只上报本次观测到的维度；不给的维度不参与判定，也不会进状态账。
 */
export function reportSeatState(
  connection: HubConnection,
  report: {
    seat: number
    life: string | null
    character: string | null
    alignment: string | null
    drunk: string | null
    poison: string | null
    reason: string
    causedBySeat: number | null
  },
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(
    connection,
    'ReportSeatState',
    report.seat,
    report.life,
    report.character,
    report.alignment,
    report.drunk,
    report.poison,
    report.reason,
    report.causedBySeat,
    idempotencyKey,
  )
}

/** 按事件日志重建房间（D-0014 恢复）。 */
export function rebuildRoom(
  connection: HubConnection,
  reason: string,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(connection, 'RebuildRoom', reason, idempotencyKey)
}
