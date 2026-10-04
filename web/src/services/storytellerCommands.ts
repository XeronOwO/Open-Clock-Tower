/**
 * 说书人命令入口：只做「参数拼装 + InvokeAsync + 回执规范化」，不做任何领域判断。
 *
 * 与 GameHub 的方法签名逐条对应（src/OpenClockTower.Server/GameHub.cs）。
 * 每条命令调用都带幂等键；幂等键由调用方持有，重试复用同一个键。
 * 配板建议是**只读查询**（`ProposeSetup`，R-0041 / R-0042）：同样在这里收口——
 * 传输异常收敛成 `ok:false` 的建议形态，UI 不需要处理两种失败。
 *
 * 零信任口径（D-0012）：命令的**第一个参数**永远是当前连接的凭据；
 * 连接与凭据必须成对出现——所以这里用 `CommandSender` 把两者绑在一起，
 * 不提供"只给连接"的发命令入口。
 */
import type { HubConnection } from '@microsoft/signalr'
import type { SetupProposalDto } from '@/contracts/game'
import { asBoolean, asNumber, asText } from '@/display/format'

/** 一条命令的两个必要条件：连接 + 该连接的凭据（D-0012）。 */
export interface CommandSender {
  connection: HubConnection
  credential: string
}

/** 重建对比报告：内存状态 / 持久化快照 / 状态账三项等价结论（仅重建命令）。 */
export interface RebuildReport {
  machineEquivalent: boolean | null
  snapshotEquivalent: boolean | null
  ledgerEquivalent: boolean | null
}

/** 命令回执的规范化结果：服务端拒绝 / 抛错都收敛成这里的一种形态。 */
export interface CommandOutcome {
  ok: boolean
  /** Accepted / Rejected / Duplicate / Failed；本地异常时为 Transport。 */
  kind: string
  sequence: number | null
  /** 人话说明（拒绝码 + 说明 / 异常消息）。 */
  message: string
  /** 重建对比报告；非重建命令为 null。 */
  rebuild: RebuildReport | null
  /** 加入旅行者且由服务端分配席位时签发的席位号；其它命令为 null（D1）。 */
  issuedSeat: number | null
  /** 签发的席位票据（只回给出命令的说书人，不进事件流 / 任何投影）；其它命令为 null。 */
  issuedSeatTicket: string | null
}

/** 未知响应 → 回执；服务端字段缺失时降级，不编造"成功"。 */
export function normalizeOutcome(raw: unknown): CommandOutcome {
  if (raw === null || typeof raw !== 'object') {
    return {
      ok: false,
      kind: 'Failed',
      sequence: null,
      message: '回执形状不可识别',
      rebuild: null,
      issuedSeat: null,
      issuedSeatTicket: null,
    }
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

  // 三项旗标在非重建命令上都是 null（服务端刻意不发）；任一为布尔才视为一份重建报告。
  const machineEquivalent = asBoolean(result['machineEquivalent'])
  const snapshotEquivalent = asBoolean(result['snapshotEquivalent'])
  const ledgerEquivalent = asBoolean(result['ledgerEquivalent'])
  const rebuild =
    machineEquivalent === null && snapshotEquivalent === null && ledgerEquivalent === null
      ? null
      : { machineEquivalent, snapshotEquivalent, ledgerEquivalent }

  return {
    ok: kind === 'Accepted' || kind === 'Duplicate',
    kind,
    sequence,
    message: parts.length > 0 ? parts.join('：') : '',
    rebuild,
    issuedSeat: asNumber(result['issuedSeat']),
    issuedSeatTicket: asText(result['issuedSeatTicket']),
  }
}

/**
 * 本地合成的失败回执：客户端在发命令之前就拒绝（没选目标、没有凭据等）。
 * 统一在这里构造——`CommandOutcome` 增字段时只改一处，各组件不必各自补齐。
 */
export function localFailure(message: string, kind: 'Rejected' | 'Failed' = 'Rejected'): CommandOutcome {
  return {
    ok: false,
    kind,
    sequence: null,
    message,
    rebuild: null,
    issuedSeat: null,
    issuedSeatTicket: null,
  }
}

/** 发一条命令（凭据永远随方法参数先出示）并规范化回执；传输层异常不吞，收敛成 Transport 回执。 */export async function invokeCommand(
  sender: CommandSender,
  method: string,
  ...args: readonly unknown[]
): Promise<CommandOutcome> {
  if (sender.credential.length === 0) {
    return {
      ok: false,
      kind: 'Rejected',
      sequence: null,
      message: '尚未加入：没有连接凭据',
      rebuild: null,
      issuedSeat: null,
      issuedSeatTicket: null,
    }
  }

  try {
    const raw = await sender.connection.invoke<unknown>(method, sender.credential, ...args)
    return normalizeOutcome(raw)
  } catch (error) {
    return {
      ok: false,
      kind: 'Transport',
      sequence: null,
      message: error instanceof Error ? error.message : String(error),
      rebuild: null,
      issuedSeat: null,
      issuedSeatTicket: null,
    }
  }
}

/** 分配角色：服务端会按会话席位名单与首版花名册重新校验。 */
export function assignCharacters(
  sender: CommandSender,
  assignments: readonly { seat: number; character: string }[],
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'AssignCharacters', assignments, idempotencyKey)
}

/**
 * 查询配板建议（只读、不落账；R-0041 / R-0042 / R-0046）：种子由服务端生成并回传。
 * 建议只是建议——说书人可重摇 / 手改，提交仍走 {@link assignCharacters}（D-0017）。
 * `nonTravellerCount` = 配板覆盖的**非旅行者人数**（旅行者是叠加角色、不占类型名额）；null = 全部席位。
 */
export async function proposeSetup(
  sender: CommandSender,
  seed: string | null,
  nonTravellerCount: number | null = null,
): Promise<SetupProposalDto> {
  if (sender.credential.length === 0) {
    return failedProposal('setup.no_credential', '尚未加入：没有连接凭据')
  }

  try {
    return await sender.connection.invoke<SetupProposalDto>(
      'ProposeSetup',
      sender.credential,
      seed,
      nonTravellerCount,
    )
  } catch (error) {
    return failedProposal('setup.transport', error instanceof Error ? error.message : String(error))
  }
}

/** 建议查询的失败形态：字段齐备，UI 不必区分 null / undefined。 */
function failedProposal(code: string, message: string): SetupProposalDto {
  return {
    ok: false,
    seed: '',
    nonTravellerCount: 0,
    travellerCount: 0,
    assignments: [],
    distribution: [],
    notes: [],
    failureCode: code,
    failureMessage: message,
  }
}

/** 开夜（口径是引擎输入，R-0014）。 */
export function startNight(
  sender: CommandSender,
  nightNumber: number,
  variant: string,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'StartNight', nightNumber, variant, idempotencyKey)
}

/** 开白天（天数由服务端推导，客户端不提供计划）。 */
export function startDay(sender: CommandSender, idempotencyKey: string): Promise<CommandOutcome> {
  return invokeCommand(sender, 'StartDay', idempotencyKey)
}

/** 开始钟盘收票：倒计时 + 分针逐席旋转（R-0017 目标形态）。 */
export function startVoteSweep(
  sender: CommandSender,
  nominationIndex: number,
  countdownMilliseconds: number,
  intervalMilliseconds: number,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(
    sender,
    'StartVoteSweep',
    nominationIndex,
    countdownMilliseconds,
    intervalMilliseconds,
    idempotencyKey,
  )
}

/** 继续中断的钟盘收票（重新起倒计时，从下一未收席位接着收）。 */
export function resumeVoteSweep(
  sender: CommandSender,
  nominationIndex: number,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'ResumeVoteSweep', nominationIndex, idempotencyKey)
}

/** 收票全部完成后计票（票面 = 逐席冻结结论，R-0017 目标形态）。 */
export function countVotes(
  sender: CommandSender,
  nominationIndex: number,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'CountVotes', nominationIndex, idempotencyKey)
}

/** 结束白天：处决当前「即将被处决」者（如果有），然后关闭白天。 */
export function closeDay(sender: CommandSender, idempotencyKey: string): Promise<CommandOutcome> {
  return invokeCommand(sender, 'CloseDay', idempotencyKey)
}

/**
 * 处罚处决（说书人主动处决）：洗脑师 / 畸形秀演员的"疯狂"后果（R-0020）。
 * 白天形态占用当天处决上限并立即收口白天；夜晚形态不占任何白天的上限。
 * `source` 只接受服务端枚举名（Cerenovus / Mutant），由说书人显式选择。
 */
export function punishExecution(
  sender: CommandSender,
  seat: number,
  source: string,
  note: string | null,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'PunishExecution', seat, source, note, idempotencyKey)
}

/**
 * 麻脸巫婆之夜的**追加死亡**（R-0030 第 4 条）：说书人让某名玩家死亡，归因为麻脸巫婆。
 * 窗口是否存在、目标是否已死由服务端判定（`kernel.NoPitHagNight` / `kernel.UnexpectedInput`）。
 */
export function pitHagCasualty(
  sender: CommandSender,
  seat: number,
  note: string | null,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'PitHagCasualty', seat, note, idempotencyKey)
}

/**
 * 裁定一条**待定死亡**（R-0030 第 2 条）：`killed` = true 确认死亡（归因为发起击杀的恶魔），
 * false 阻止死亡（免死）——「说书人能让原本被恶魔攻击且会死亡的玩家免死」（百科《免死》）。
 */
export function resolveDeferredDeath(
  sender: CommandSender,
  seat: number,
  killed: boolean,
  note: string | null,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'ResolveDeferredDeath', seat, killed, note, idempotencyKey)
}

/** 代填挂起请求。 */
export function proxyFill(
  sender: CommandSender,
  requestId: string,
  optionValue: string,
  note: string | null,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'ProxyFill', requestId, optionValue, note, idempotencyKey)
}

/** 强制作废挂起请求（原因必须是服务端认识的枚举名）。 */
export function voidRequest(
  sender: CommandSender,
  requestId: string,
  reason: string,
  note: string | null,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'VoidRequest', requestId, reason, note, idempotencyKey)
}

/** 强推当前槽位（D-0014 兜底）。 */
export function forceAdvance(
  sender: CommandSender,
  reason: string,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'ForceAdvance', reason, idempotencyKey)
}

/** 接管自动化。 */
export function takeOver(
  sender: CommandSender,
  reason: string,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'TakeOver', reason, idempotencyKey)
}

/** 交还自动化。 */
export function releaseControl(
  sender: CommandSender,
  reason: string,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'ReleaseControl', reason, idempotencyKey)
}

/** 了结裁定点（R-0009 自由决定）。 */
export function resolveDecisionPoint(
  sender: CommandSender,
  decisionPointId: string,
  decision: string | null,
  note: string | null,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'ResolveDecisionPoint', decisionPointId, decision, note, idempotencyKey)
}

/**
 * 上报座位状态变化。
 * 只上报本次观测到的维度；不给的维度不参与判定，也不会进状态账。
 */
export function reportSeatState(
  sender: CommandSender,
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
    sender,
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

/**
 * 给某席加一条自由文本注记（D-0019）。
 * 文本的归一化与长度上限由服务端强制（`SeatAnnotationText`）；玩家侧没有这条命令的入口。
 */
export function addSeatAnnotation(
  sender: CommandSender,
  seat: number,
  text: string,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'AddSeatAnnotation', seat, text, idempotencyKey)
}

/** 改一条注记的文本（标识与席位不变；不存在的标识由服务端拒绝）。 */
export function updateSeatAnnotation(
  sender: CommandSender,
  annotationId: number,
  text: string,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'UpdateSeatAnnotation', annotationId, text, idempotencyKey)
}

/** 删一条注记（写删除事件，不抹历史）。 */
export function removeSeatAnnotation(
  sender: CommandSender,
  annotationId: number,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'RemoveSeatAnnotation', annotationId, idempotencyKey)
}

/** 按事件日志重建房间（D-0014 恢复）。 */
export function rebuildRoom(
  sender: CommandSender,
  reason: string,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'RebuildRoom', reason, idempotencyKey)
}

// ===== 旅行者与流放（票据 traveller-and-exile；D7 补齐说书人控制台入口）=====

/**
 * 加入一名旅行者（说书人 / 宿主；任意时刻可用，含开局前与阶段中）。
 *
 * `seat` = null 时由服务端**追加新席位**并签发新票据（回执里的 `issuedSeat` / `issuedSeatTicket`）；
 * 指定席位 = 落在本局**尚未分配**的席位（如 15+ 开局提前占好的高号席）。
 * `alignment` 是说书人私下裁定的阵营（Good / Evil），不进任何公开投影；
 * `revealDemonSeats` = 邪恶旅行者要被告知的存活恶魔席位（一名或全部；善良必须为空）。
 */
export function joinTraveller(
  sender: CommandSender,
  seat: number | null,
  character: string,
  alignment: string,
  revealDemonSeats: readonly number[] | null,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'JoinTraveller', seat, character, alignment, revealDemonSeats, idempotencyKey)
}

/** 移出一名旅行者：席位与票据保留，离场后不计入任何人数口径（R-0044 第 6 条）。 */
export function removeTraveller(
  sender: CommandSender,
  seat: number,
  note: string | null,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'RemoveTraveller', seat, note, idempotencyKey)
}

/** 裁定某席位「今天的死亡保护」（R-0048；怪咖：有趣 → 受保护）。只在流放达线待裁定时受理。 */
export function resolveDayProtection(
  sender: CommandSender,
  seat: number,
  isProtected: boolean,
  note: string | null,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'ResolveDayProtection', seat, isProtected, note, idempotencyKey)
}

/** 开始流放收票（R-0044 第 10 条沿用 R-0017 钟盘：倒计时 + 分针逐席旋转）。 */
export function startExileSweep(
  sender: CommandSender,
  exileIndex: number,
  countdownMilliseconds: number,
  intervalMilliseconds: number,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(
    sender,
    'StartExileSweep',
    exileIndex,
    countdownMilliseconds,
    intervalMilliseconds,
    idempotencyKey,
  )
}

/** 继续中断的流放收票（重新起倒计时，从下一未收席位接着收）。 */
export function resumeExileSweep(
  sender: CommandSender,
  exileIndex: number,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'ResumeExileSweep', exileIndex, idempotencyKey)
}

/** 流放收票全部完成后计票（达线 = 赞成 × 2 ≥ 收票开始时的在局人数；R-0044 第 5 条）。 */
export function countExileVotes(
  sender: CommandSender,
  exileIndex: number,
  idempotencyKey: string,
): Promise<CommandOutcome> {
  return invokeCommand(sender, 'CountExileVotes', exileIndex, idempotencyKey)
}
