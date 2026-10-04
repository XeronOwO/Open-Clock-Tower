import { describe, expect, it } from 'vitest'
import type { InformationResultDto, OperationRequestDto, PlayerDayDto, PlayerViewDto } from '@/contracts/game'
import { PlayerViewMerge, type PlayerPush } from '@/services/playerViewMerge'

/**
 * 补齐往返窗口的确定性交错（票据 `player-information-resync-race` 行 1 与同族）。
 *
 * 服务端在锁内建快照（序号 N）后，N+1 的推送可能先到、快照响应后到。修复前推送不带序号，
 * 客户端只能"追加后再被快照整体覆盖"，这条信息静默丢失；修复后所有推送带序号，
 * 合并态按序号逐字段 / 逐条目取舍——**先红后绿**的第一步就是让这些用例先在旧语义下红一次。
 */

const information = (sequence: number, ability: string, content = `${ability} 的信息`): InformationResultDto => ({
  sequence,
  ability,
  content,
})

const request = (sequence: number): OperationRequestDto => ({
  sequence,
  requestId: `r${sequence}`,
  seat: 1,
  context: '请选择目标',
  options: [{ value: 'a', preview: '甲' }],
  secondaryOptions: [],
})

const day = (dayNumber: number, sequence: number): PlayerDayDto => ({
  sequence,
  publicView: {
    dayNumber,
    status: 'Open',
    nominations: [],
    exiles: [],
    openExileIndex: null,
    protections: [],
    extraNomination: null,
    aboutToBeExecuted: null,
    executed: null,
    openNominationIndex: null,
  },
  lives: [],
  announcements: [],
  canNominate: true,
  canVote: true,
  voted: false,
  seatCollected: false,
  candidates: [],
  canProposeExile: false,
  exileCandidates: [],
  canVoteExile: false,
  exileVoted: false,
  exileSeatCollected: false,
  canNominateExtra: false,
  extraNominationCandidates: [],
})

function snapshotView(overrides: Partial<PlayerViewDto> = {}): PlayerViewDto {
  return {
    seat: 1,
    phase: 'FirstNight',
    pendingRequest: null,
    informationResults: [],
    day: null,
    outcome: null,
    klutzChoices: [],
    seatNames: [],
    pendingQuestion: null,
    canAskArtistQuestion: false,
    exhaustedAbilities: [],
    ...overrides,
  }
}

const phase = (sequence: number, value: string): PlayerPush => ({ kind: 'Phase', sequence, phase: value })
const infoPush = (sequence: number, ability: string, content?: string): PlayerPush => ({
  kind: 'Information',
  sequence,
  information: information(sequence, ability, content),
})

describe('补齐往返窗口：推送与快照按序号合并', () => {
  it('补齐响应晚于推送到达：推送不丢、两条都在且不重复（票据行 1）', () => {
    const merge = new PlayerViewMerge()
    merge.applyPush(infoPush(6, 'dreamer', '窗口内到达的推送'))
    merge.applySnapshot(
      snapshotView({ informationResults: [information(5, 'clockmaker', '快照里已有')] }),
      5,
    )

    expect(merge.snapshot().informationResults.map((item) => item.sequence)).toEqual([5, 6])
    expect(merge.snapshot().informationResults.map((item) => item.content)).toEqual([
      '快照里已有',
      '窗口内到达的推送',
    ])
  })

  it('推送早已包含在快照里：只保留一份（不重复）', () => {
    const merge = new PlayerViewMerge()
    merge.applySnapshot(snapshotView({ informationResults: [information(5, 'clockmaker')] }), 5)
    merge.applyPush(infoPush(5, 'clockmaker'))

    expect(merge.snapshot().informationResults.map((item) => item.sequence)).toEqual([5])
  })

  it('同序号的整视图推送仍会更新席位名（认领 / 改名不产生事件，D-0021）', () => {
    const merge = new PlayerViewMerge()
    merge.applySnapshot(snapshotView({ seatNames: [{ seat: 1, displayName: '爱丽丝' }] }), 7)

    const changed = merge.applySnapshot(
      snapshotView({ seatNames: [{ seat: 1, displayName: '爱丽丝二世' }] }),
      7,
    )

    expect(changed).toBe(true)
    expect(merge.snapshot().seatNames).toEqual([{ seat: 1, displayName: '爱丽丝二世' }])
  })

  it('同一条推送重复到达：按序号幂等（不重复）', () => {
    const merge = new PlayerViewMerge()
    merge.applyPush(infoPush(6, 'dreamer'))
    merge.applyPush(infoPush(6, 'dreamer'))

    expect(merge.snapshot().informationResults.map((item) => item.sequence)).toEqual([6])
  })

  it('乱序到达的推送：按序号落位，仍完整有序', () => {
    const merge = new PlayerViewMerge()
    merge.applyPush(infoPush(7, 'oracle'))
    merge.applyPush(infoPush(6, 'dreamer'))

    expect(merge.snapshot().informationResults.map((item) => item.sequence)).toEqual([6, 7])
  })

  it('迟到快照只补不覆盖：补上缺口、保留更新的推送', () => {
    const merge = new PlayerViewMerge()
    merge.applyPush(infoPush(7, 'oracle'))
    merge.applySnapshot(
      snapshotView({ informationResults: [information(5, 'clockmaker'), information(6, 'dreamer')] }),
      6,
    )

    expect(merge.snapshot().informationResults.map((item) => item.sequence)).toEqual([5, 6, 7])
  })

  it('事件窗口水位只由快照推进：读时推送不把 JoinSeat 的已知序号推高', () => {
    const merge = new PlayerViewMerge()
    merge.applySnapshot(snapshotView(), 5)
    merge.applyPush(infoPush(9, 'oracle'))

    // 推送 9 落在字段 / 信息取舍里，但事件窗口水位仍是 5：
    // 否则下一条 JoinSeat(known=9) 会把 (5, 9] 的可见事件整段截断（架构 §5 的"快照 + 缺口事件"）。
    expect(merge.snapshot().informationResults.map((item) => item.sequence)).toEqual([9])
    expect(merge.eventAt).toBe(5)
  })

  it('迟到的快照不回退事件窗口水位', () => {
    const merge = new PlayerViewMerge()
    merge.applySnapshot(snapshotView(), 9)
    merge.applySnapshot(snapshotView(), 4)

    expect(merge.eventAt).toBe(9)
  })

  it('reset 清空合并态：服务端序号回退后，新快照成为新基线（复用的信息序号不被当成重复）', () => {
    const merge = new PlayerViewMerge()
    merge.applySnapshot(snapshotView({ phase: 'Day', informationResults: [information(11, 'oracle')] }), 11)

    merge.reset()
    merge.applySnapshot(snapshotView({ phase: 'FirstNight', informationResults: [information(11, 'dreamer')] }), 11)

    expect(merge.eventAt).toBe(11)
    expect(merge.snapshot().phase).toBe('FirstNight')
    expect(merge.snapshot().informationResults.map((item) => item.ability)).toEqual(['dreamer'])
  })
})

describe('同族：阶段 / 白天 / 请求三态不被迟到快照拉回', () => {
  it('阶段：推送先到、快照后到 → 页头保持较新阶段', () => {
    const merge = new PlayerViewMerge()
    merge.applyPush(phase(6, 'Day'))
    merge.applySnapshot(snapshotView({ phase: 'FirstNight' }), 5)

    expect(merge.snapshot().phase).toBe('Day')
  })

  it('旧阶段推送后到：丢弃、不回退', () => {
    const merge = new PlayerViewMerge()
    merge.applySnapshot(snapshotView({ phase: 'Day' }), 7)
    merge.applyPush(phase(5, 'FirstNight'))

    expect(merge.snapshot().phase).toBe('Day')
  })

  it('白天：较新的投影胜出；迟到快照不清空、不拉回', () => {
    const merge = new PlayerViewMerge()
    merge.applyPush({ kind: 'Day', sequence: 6, day: day(2, 6) })
    merge.applySnapshot(snapshotView({ day: day(1, 5) }), 5)

    expect(merge.snapshot().day?.publicView.dayNumber).toBe(2)
  })

  it('请求：较新的状态胜出；迟到快照不让已了结的请求复活', () => {
    const merge = new PlayerViewMerge()
    merge.applySnapshot(snapshotView({ pendingRequest: request(5) }), 5)
    merge.applyPush({ kind: 'Voided', sequence: 6, voided: { sequence: 6, requestId: 'r5', reason: 'StorytellerForce', note: null } })

    expect(merge.snapshot().pendingRequest).toBeNull()

    // 更晚的响应让面板重新挂起另一条请求；此时迟到快照（序号 5）不许把 r5 拉回来。
    merge.applyPush({ kind: 'Request', sequence: 7, request: request(7) })
    merge.applySnapshot(snapshotView({ pendingRequest: request(5) }), 5)

    expect(merge.snapshot().pendingRequest?.requestId).toBe('r7')
  })

  it('旧序号的作废 / 响应：不清理当前请求（序号才是判据，不是 requestId 相等）', () => {
    const merge = new PlayerViewMerge()
    merge.applySnapshot(snapshotView({ pendingRequest: request(7) }), 7)

    merge.applyPush({ kind: 'Voided', sequence: 6, voided: { sequence: 6, requestId: 'r7', reason: 'StorytellerForce', note: null } })
    expect(merge.snapshot().pendingRequest?.requestId).toBe('r7')

    merge.applyPush({ kind: 'Answered', sequence: 6, answered: { sequence: 6, requestId: 'r7', optionValue: 'a', source: 'Player', note: null } })
    expect(merge.snapshot().pendingRequest?.requestId).toBe('r7')
  })

  it('快照与推送同序号：幂等，不改变视图内容', () => {
    const merge = new PlayerViewMerge()
    merge.applySnapshot(snapshotView({ phase: 'Day', informationResults: [information(7, 'oracle')] }), 7)
    merge.applyPush(phase(7, 'Day'))
    merge.applyPush(infoPush(7, 'oracle'))

    expect(merge.snapshot().phase).toBe('Day')
    expect(merge.snapshot().informationResults.map((item) => item.sequence)).toEqual([7])
  })
})
