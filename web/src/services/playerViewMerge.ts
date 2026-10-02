/**
 * 玩家视图合并：把「快照 + 带序号的在线推送」折成一份单调不回退的视图。
 *
 * 背景（票据 `docs/backlog/todo/player-information-resync-race.md`）：
 * 玩家视图此前有两个写入者——在线推送的追加、`JoinSeat` 快照的整体覆盖——且推送不带序号，
 * 客户端无法判断推送与快照的先后。服务端在锁内建快照（序号 N）后，N+1 的推送可能先到、
 * 随后被 N 快照覆盖，信息静默丢失（可恢复，但属于静默丢失）。
 *
 * 本模块是**纯逻辑**（无连接、无 IO），由 `PlayerGateway` 独占持有：推送与快照都只进这里，
 * 合并结果再整份交给界面——把"两个写入者"收敛成一个。合并规则（架构 §5；先红后绿见票据）：
 *
 * - **信息结果按序号并集**：每条信息携带自己的事件序号（快照里也带），按序号去重排序；
 *   快照是某个序号之前的全量事实，推送是此刻到达的单条事实，谁到得早都不影响最终集合；
 * - **阶段 / 白天 / 请求按"字段序号"取新**：每份快照与推送都带它被表达时的序号，
 *   只接受序号更大的那份；序号更小的（迟到的响应、倒灌的旧推送）直接丢弃；
 * - **两个水位，用途不同**：
 *   - 字段序号是"这份状态被表达时的序号"，可以来自推送，用于决定字段取舍；
 *   - `eventAt` 是**事件窗口水位**，只由快照推进，用于向服务端索要缺口事件。推送**不**推高它：
 *     白天投影这类"读时状态"的序号取自读取时的全局 head，可能领先于本席真实的事件位置，
 *     拿它当 `JoinSeat` 的已知序号会把缺口事件窗口整段截断（架构 §5 的"快照 + 从该序号起的全部事件"）。
 *
 * 快照序号低于本地已知**不是坏数据**：那是"推送先到、响应后到"的正常竞态
 * （`applyBundle` 只对事件越界 / 倒退 / 重复报诊断）；而快照序号低于**事件窗口水位**意味着服务端
 * 事件流回退（数据丢失 / 从旧备份恢复）——那要显式重建合并态，见 `reset()`。
 */
import type {
  InformationResultDto,
  OperationRequestAnsweredDto,
  OperationRequestDto,
  OperationRequestVoidedDto,
  PlayerDayDto,
  PlayerViewDto,
} from '@/contracts/game'

/** 一条在线推送：带背书事件序号（或读时状态序号），客户端据此与快照比较先后。 */
export type PlayerPush =
  | { kind: 'Phase'; sequence: number; phase: string }
  | { kind: 'Information'; sequence: number; information: InformationResultDto }
  | { kind: 'Request'; sequence: number; request: OperationRequestDto }
  | { kind: 'Voided'; sequence: number; voided: OperationRequestVoidedDto }
  | { kind: 'Answered'; sequence: number; answered: OperationRequestAnsweredDto }
  | { kind: 'Day'; sequence: number; day: PlayerDayDto | null }

/** 单席位玩家视图的合并态（一个 `PlayerGateway` 一个实例）。 */
export class PlayerViewMerge {
  private seat = 0
  private phase = ''
  /** 各字段最近一次被表达的序号；`-1` = 还没有应用过任何快照 / 推送（序号非负）。 */
  private phaseSequence = -1
  private pending: OperationRequestDto | null = null
  private pendingSequence = -1
  private day: PlayerDayDto | null = null
  private daySequence = -1
  /** 已收到的信息结果：序号 → 条目（同一序号只可能有一条事实，天然去重）。 */
  private readonly information = new Map<number, InformationResultDto>()
  /** 事件窗口水位：只由快照推进（见文件头"两个水位"）。 */
  private eventSequence = 0

  /** 事件窗口水位：主动补齐时按它向服务端索要缺口事件。 */
  get eventAt(): number {
    return this.eventSequence
  }

  /** 当前挂起请求（合并前的原值，供网关判断"这次作废 / 响应是不是界面在等的那一条"）。 */
  get pendingRequest(): OperationRequestDto | null {
    return this.pending
  }

  /** 应用一份快照；返回 true = 视图发生了变化。 */
  applySnapshot(view: PlayerViewDto, sequence: number): boolean {
    let changed = view.seat !== this.seat
    this.seat = view.seat

    if (sequence > this.phaseSequence) {
      this.phase = view.phase
      this.phaseSequence = sequence
      changed = true
    }

    if (sequence > this.pendingSequence) {
      this.pending = view.pendingRequest
      this.pendingSequence = sequence
      changed = true
    }

    if (sequence > this.daySequence) {
      this.day = view.day
      this.daySequence = sequence
      changed = true
    }

    for (const item of view.informationResults) {
      if (!this.information.has(item.sequence)) {
        this.information.set(item.sequence, item)
        changed = true
      }
    }

    this.eventSequence = Math.max(this.eventSequence, sequence)
    return changed
  }

  /**
   * 服务端事件流回退（快照序号低于事件窗口水位）时重建合并态：清空全部字段与信息集合，
   * 让下一份快照成为新基线。**必须清空信息集合**：序号在回退后会被复用，
   * 留着旧键会把"新事实"当成重复而静默丢掉（得到的是错误视图，不是旧视图）。
   */
  reset(): void {
    this.phase = ''
    this.phaseSequence = -1
    this.pending = null
    this.pendingSequence = -1
    this.day = null
    this.daySequence = -1
    this.information.clear()
    this.eventSequence = 0
  }

  /** 应用一条推送；返回 true = 视图发生了变化。 */
  applyPush(push: PlayerPush): boolean {
    let changed = false

    switch (push.kind) {
      case 'Phase':
        if (push.sequence > this.phaseSequence) {
          this.phase = push.phase
          this.phaseSequence = push.sequence
          changed = true
        }

        break

      case 'Day':
        if (push.sequence > this.daySequence) {
          this.day = push.day
          this.daySequence = push.sequence
          changed = true
        }

        break

      case 'Request':
        if (push.sequence > this.pendingSequence) {
          this.pending = push.request
          this.pendingSequence = push.sequence
          changed = true
        }

        break

      // 作废 / 响应都表示"服务端此刻已经没有挂起请求"：序号更大就清空。
      // （只有活着的挂起请求会被作废 / 响应，所以序号更大的这类事件必然让状态变空。）
      case 'Voided':
      case 'Answered':
        if (push.sequence > this.pendingSequence) {
          changed = this.pending !== null
          this.pending = null
          this.pendingSequence = push.sequence
        }

        break

      case 'Information':
        if (!this.information.has(push.sequence)) {
          this.information.set(push.sequence, push.information)
          changed = true
        }

        break
    }

    // 刻意不推进 eventSequence：推送的序号可能领先于本席真实的事件位置（见文件头"两个水位"）。
    return changed
  }

  /** 当前合并结果：交给界面的唯一一份视图（信息按发生序号升序）。 */
  snapshot(): PlayerViewDto {
    return {
      seat: this.seat,
      phase: this.phase,
      pendingRequest: this.pending,
      informationResults: [...this.information.values()].sort(
        (left, right) => left.sequence - right.sequence,
      ),
      day: this.day,
    }
  }
}
