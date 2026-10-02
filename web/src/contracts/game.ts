/**
 * 前后端共享契约的 wire 形状（TypeScript 侧手写镜像）。
 *
 * 来源：src/OpenClockTower.Contracts/*.cs。改动契约时必须同步这里——
 * 契约测试会拿服务端实际序列化的 JSON 与之对账（tests/OpenClockTower.Integration.Tests）。
 *
 * 字段名走 camelCase：ASP.NET Core SignalR 的 JSON 协议默认 camelCase。
 * 服务端数据一律当作**不可信输入**处理（架构 §4.4）：这里的类型是断言，不是保证，
 * 呈现前必须过 display/ 的规范化函数。
 */

/** 只读数组用别名表达"客户端不改它"的意图。 */
export type ReadonlyArrayOf<T> = readonly T[]

/** 一条合法选项的线上形状。 */
export interface DecisionOptionDto {
  value: string
  preview: string
}

/** 状态账里的一条维度事实：哪个维度、当前值、怎么来的。 */
export interface SeatStateFactDto {
  dimension: string
  value: string
  reason: string
  causedBy: number | null
  effectId: string | null
}

/** 状态账里的一行：某个席位当前已知的维度与逐维度归因。 */
export interface SeatStateDto {
  seat: number
  facts: SeatStateFactDto[]
  madnesses: string[]
}

/** 一条效果的归因链。 */
export interface EffectDto {
  effectId: string
  kind: string
  ability: string
  source: number
  target: number
  sourceCharacter: string | null
  /** 契约上必有（服务端每条路径都显式赋值，见 ProjectionMapper）；缺失只可能来自篡改或服务端 bug。 */
  terminated: boolean
  terminationKind: string | null
  terminationReason: string | null
  terminationCausedBy: number | null
}

/** 能力使用账本的一条记录。 */
export interface AbilityUseDto {
  seat: number
  ability: string
  /** 契约上必有（同上）。 */
  effective: boolean
}

/** 失效账本的一条记录。 */
export interface MalfunctionDto {
  seat: number
  ability: string
  kind: string
}

/** 最近一次能力结算的结论。 */
export interface AbilityResolutionDto {
  seat: number
  ability: string
  /** 契约上必有（同上）。 */
  effective: boolean
  malfunction: string | null
  note: string | null
  sequence: number
}

/** 本槽位能力判定：已结算结论或按当前账的预览（说书人每步摘要用）。 */
export interface SlotAbilityDto {
  /** Settled（已结算）/ Preview（按当前账预览）/ Unknown（账不全，无法判定）。 */
  basis: string
  ability: string | null
  effective: boolean | null
  malfunction: string | null
  note: string | null
  sequence: number | null
}

/** 一条座位状态变化（谁因为什么原因变成了什么样）。 */
export interface SeatChangeDto {
  seat: number
  life: string | null
  character: string | null
  alignment: string | null
  drunk: string | null
  poison: string | null
  reason: string
  causedBy: number | null
  effectId: string | null
  sequence: number
  recordedAt: string
}

/** 说书人"卡点"摘要。 */
export interface PendingRequestDto {
  seat: number
  requestId: string
  slotId: string
  slotIndex: number
  waitingSeconds: number | null
}

/** 房间健康位：恢复 / 重建失败后为降级态（原因 + 发生时间）；正常时 degraded=false。 */
export interface RoomHealthDto {
  degraded: boolean
  reason: string | null
  since: string | null
}

/** 每步摘要（票据「说书人上帝视角」第 3 条）：当前槽位的行动者、状态及归因、能力判定与无选项行为。 */
export interface StepDigestDto {
  seat: number
  character: string | null
  state: SeatStateDto | null
  ability: SlotAbilityDto | null
  optionCount: number | null
  onNoOption: string | null
}

/** 推给玩家的「请求已作废」及其原因（后端同名契约）。 */
export interface OperationRequestVoidedDto {
  /** 产生这次作废的事件流序号（与快照序号比较先后，见 playerViewMerge）。 */
  sequence: number
  requestId: string
  reason: string
  note: string | null
}

/** 推给玩家的「请求已被响应」（玩家本人作答或说书人代填；后端同名契约）。 */
export interface OperationRequestAnsweredDto {
  /** 产生这次响应的事件流序号（与快照序号比较先后）。 */
  sequence: number
  requestId: string
  /** 被选中的选项值（与重连事件里的同一事实保持一致）。 */
  optionValue: string
  /** ResponseSource：Player / StorytellerProxy。 */
  source: string
  note: string | null
}

/** 推给玩家的阶段开始（公开信息：昼夜；后端同名契约）。 */
export interface PhaseStartedDto {
  /** 产生这次阶段开始的事件流序号。 */
  sequence: number
  phase: string
}

/** 白天一次提名的公开账目（提名与投票本身是桌面上的公开信息）。 */
export interface DayNominationDto {
  index: number
  nominator: number
  nominee: number
  /** Voting（投票窗口开着）/ Counted（已计票）。 */
  status: string
  /** 当前 / 最终票数。 */
  votes: number
  /** 当前 / 最终投赞成者的席位（按席位号升序）。 */
  voters: number[]
}

/** 白天公开事实（最新一天：进行中或最近结束）。 */
export interface DayViewDto {
  dayNumber: number
  /** Open（进行中）/ Closed（已结束）。 */
  status: string
  nominations: DayNominationDto[]
  aboutToBeExecuted: number | null
  executed: number | null
  openNominationIndex: number | null
}

/** 某个玩家的白天投影：公开事实 + 他现在能做什么（服务端算好，呈现层不判规则）。 */
export interface PlayerDayDto {
  /** 这份投影对应的事件流序号（推送取读取时序号；快照取快照序号）。 */
  sequence: number
  /** 公开的当天事实（字段名避开说书人专属禁词，见 PlayerProjectionLeakGateTests）。 */
  publicFacts: DayViewDto
  canNominate: boolean
  canVote: boolean
  voted: boolean
  /** 今天还没被提名过的席位（可提名目标，按席位号升序）。 */
  candidates: number[]
}

/** 说书人视图：完整看板 + 兜底所需的一切（D-0014）。 */
export interface StorytellerViewDto {
  sequence: number
  phase: string
  control: string
  health: RoomHealthDto
  slotIndex: number
  slotCount: number
  currentSlotId: string | null
  planCompleted: boolean
  pending: PendingRequestDto | null
  awaitingDecisionId: string | null
  awaitingDecisionContext: string | null
  awaitingDecisionOptions: DecisionOptionDto[] | null
  blockedReason: string | null
  currentSlotActor: number | null
  currentSlotContext: string | null
  recentSeatChanges: SeatChangeDto[]
  seats: SeatStateDto[]
  effects: EffectDto[]
  abilityUses: AbilityUseDto[]
  malfunctions: MalfunctionDto[]
  lastResolution: AbilityResolutionDto | null
  stepDigest: StepDigestDto | null
  lastVoidedRequest: OperationRequestVoidedDto | null
  /** 最新一天（进行中或最近结束）的白天账；还没有开过白天时为 null。 */
  day: DayViewDto | null
}

/** 命令回执。 */
export interface CommandResultDto {
  kind: string
  sequence: number
  rejectionCode: string | null
  rejectionMessage: string | null
  failure: string | null
  machineEquivalent: boolean | null
  snapshotEquivalent: boolean | null
  ledgerEquivalent: boolean | null
}

/** 开局分配的一项（wire 形态）。 */
export interface SeatCharacterAssignmentDto {
  seat: number
  character: string
}

/** 玩家视图：只有他自己的席位、当前大阶段与他自己的挂起请求。 */
export interface PlayerViewDto {
  seat: number
  phase: string
  pendingRequest: OperationRequestDto | null
  informationResults: InformationResultDto[]
  /** 白天投影（公开事实 + 自己能做什么）；还没有开过白天时为 null。 */
  day: PlayerDayDto | null
}

/** 推给玩家的操作请求（刻意不含槽位 / 轮次 / 进度）。 */
export interface OperationRequestDto {
  /** 这条请求状态对应的事件流序号（推送 = 背书事件；快照 = 快照序号）。 */
  sequence: number
  requestId: string
  seat: number
  context: string
  options: DecisionOptionDto[]
  /**
   * 第二维合法选项（空数组 = 单维选择）。两维时提交值必须是 `{第一维}|{第二维}`
   * （R-0021：洗脑师的一次行动是玩家 × 善良角色的原子选择，不可只完成一维）。
   */
  secondaryOptions: DecisionOptionDto[]
}

/** 玩家自己能力得到的信息类结果：**只有内容**，没有"可能为假"标记（服务端刻意不下发）。 */
export interface InformationResultDto {
  /** 产生这条信息的事件流序号：客户端按它合并去重（快照与推送谁先谁后）。 */
  sequence: number
  ability: string
  content: string
}

/** 重连包：快照 + 从该序号起的事件。 */
export interface ReconnectBundleDto {
  sequence: number
  view: PlayerViewDto
  events: PlayerEventDto[]
}

/** 玩家加入结果：连接级凭据 + 重连包（凭据只在签发它的那条连接上有效，D-0012）。 */
export interface SeatJoinDto {
  credential: string
  bundle: ReconnectBundleDto
}

/** 说书人加入结果：连接级凭据 + 首份视图。 */
export interface StorytellerJoinDto {
  credential: string
  view: StorytellerViewDto
}

/** 玩家可见事件（白名单投影）。 */
export interface PlayerEventDto {
  sequence: number
  kind: string
  phase: string | null
  request: OperationRequestDto | null
  requestId: string | null
  optionValue: string | null
  voidReason: string | null
  voidNote: string | null
  information: InformationResultDto | null
}
