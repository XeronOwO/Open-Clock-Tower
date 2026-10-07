import { describe, expect, it, vi } from 'vitest'
import type { HubConnection } from '@microsoft/signalr'
import type { SetupProposalDto } from '@/contracts/game'
import { newIdempotencyKey } from '@/services/idempotency'
import {
  closeDay,
  countExileVotes,
  countVotes,
  forceAdvance,
  invokeCommand,
  joinTraveller,
  localFailure,
  localSuccess,
  normalizeOutcome,
  pitHagCasualty,
  proposeSetup,
  removeTraveller,
  resolveDayProtection,
  resolveDeferredDeath,
  resolveTravellerDeparture,
  resumeExileSweep,
  resumeVoteSweep,
  setTableInviteOnly,
  startDay,
  startExileSweep,
  startVoteSweep,
  type CommandSender,
} from '@/services/storytellerCommands'

describe('幂等键', () => {
  it('带前缀且每次不同（重试必须复用同一个键，由调用方持有）', () => {
    const first = newIdempotencyKey('assign')
    const second = newIdempotencyKey('assign')
    expect(first.startsWith('assign:')).toBe(true)
    expect(first).not.toBe(second)
  })
})

describe('命令回执规范化', () => {
  it('Accepted / Duplicate 视为成功并带序号', () => {
    expect(normalizeOutcome({ kind: 'Accepted', sequence: 12 })).toEqual({
      ok: true,
      kind: 'Accepted',
      sequence: 12,
      message: '',
      rebuild: null,
      issuedSeat: null,
      issuedSeatTicket: null,
    })
    expect(normalizeOutcome({ kind: 'Duplicate', sequence: 12 }).ok).toBe(true)
  })

  it('加入旅行者的回执带签发席位与票据；普通命令为 null（D1）', () => {
    const joined = normalizeOutcome({
      kind: 'Accepted',
      sequence: 20,
      issuedSeat: 16,
      issuedSeatTicket: 'T'.repeat(43),
    })
    expect(joined.issuedSeat).toBe(16)
    expect(joined.issuedSeatTicket).toBe('T'.repeat(43))

    const plain = normalizeOutcome({ kind: 'Accepted', sequence: 21 })
    expect(plain.issuedSeat).toBeNull()
    expect(plain.issuedSeatTicket).toBeNull()
  })

  it('重建回执带三项等价结论；非重建命令不带报告', () => {
    const rebuilt = normalizeOutcome({
      kind: 'Accepted',
      sequence: 40,
      machineEquivalent: true,
      snapshotEquivalent: false,
      ledgerEquivalent: false,
    })
    expect(rebuilt.rebuild).toEqual({
      machineEquivalent: true,
      snapshotEquivalent: false,
      ledgerEquivalent: false,
    })
    // 无快照时该项为 null（服务端刻意不发），但报告仍存在——不能把"没快照"吞成"没有报告"。
    const noSnapshot = normalizeOutcome({
      kind: 'Accepted',
      sequence: 41,
      machineEquivalent: true,
      ledgerEquivalent: true,
    })
    expect(noSnapshot.rebuild).toEqual({
      machineEquivalent: true,
      snapshotEquivalent: null,
      ledgerEquivalent: true,
    })
    expect(normalizeOutcome({ kind: 'Accepted', sequence: 42 }).rebuild).toBeNull()
  })

  it('Rejected 拼出拒绝码与说明', () => {
    const outcome = normalizeOutcome({
      kind: 'Rejected',
      sequence: 3,
      rejectionCode: 'plan.contract_missing',
      rejectionMessage: '角色 dreamer 的夜间行动契约还没有实现',
    })
    expect(outcome.ok).toBe(false)
    expect(outcome.message).toContain('plan.contract_missing')
    expect(outcome.message).toContain('dreamer')
  })

  it('形状不可识别时按失败处理，绝不猜成成功', () => {
    expect(normalizeOutcome(null).ok).toBe(false)
    expect(normalizeOutcome({ kind: 'Failed', failure: '折账失败' }).message).toBe('折账失败')
  })
})

describe('命令必须出示连接凭据（D-0012）', () => {
  const credential = 'C'.repeat(43)

  it('凭据作为第一个参数发出：连接与凭据成对，不允许"只给连接"', async () => {
    const invoke = vi.fn(async () => ({ kind: 'Accepted', sequence: 7 }))
    const sender: CommandSender = { connection: { invoke } as unknown as HubConnection, credential }

    const outcome = await forceAdvance(sender, '测试：强推', 'key-1')

    expect(outcome.ok).toBe(true)
    expect(invoke).toHaveBeenCalledWith('ForceAdvance', credential, '测试：强推', 'key-1')
  })

  it('没有凭据就不发命令：本地先拒绝，不把无效请求打到服务端', async () => {
    const invoke = vi.fn()
    const sender: CommandSender = { connection: { invoke } as unknown as HubConnection, credential: '' }

    const outcome = await invokeCommand(sender, 'ForceAdvance', 'x')

    expect(outcome.ok).toBe(false)
    expect(invoke).not.toHaveBeenCalled()
  })

  it('白天命令按 Hub 方法名与参数顺序发出（开白天 / 开始收票 / 继续 / 计票 / 结束并处决）', async () => {
    const invoke = vi.fn(async () => ({ kind: 'Accepted', sequence: 9 }))
    const sender: CommandSender = { connection: { invoke } as unknown as HubConnection, credential }

    await startDay(sender, 'key-day')
    expect(invoke).toHaveBeenCalledWith('StartDay', credential, 'key-day')

    await startVoteSweep(sender, 2, 3000, 1000, 'key-sweep')
    expect(invoke).toHaveBeenCalledWith('StartVoteSweep', credential, 2, 3000, 1000, 'key-sweep')

    await resumeVoteSweep(sender, 2, 'key-resume')
    expect(invoke).toHaveBeenCalledWith('ResumeVoteSweep', credential, 2, 'key-resume')

    await countVotes(sender, 2, 'key-count')
    expect(invoke).toHaveBeenCalledWith('CountVotes', credential, 2, 'key-count')

    await closeDay(sender, 'key-close')
    expect(invoke).toHaveBeenCalledWith('CloseDay', credential, 'key-close')
  })

  it('麻脸巫婆之夜的两条命令按 Hub 方法名与参数顺序发出（R-0030）', async () => {
    const invoke = vi.fn(async () => ({ kind: 'Accepted', sequence: 11 }))
    const sender: CommandSender = { connection: { invoke } as unknown as HubConnection, credential }

    await pitHagCasualty(sender, 3, '平衡局面', 'key-casualty')
    expect(invoke).toHaveBeenCalledWith('PitHagCasualty', credential, 3, '平衡局面', 'key-casualty')

    await resolveDeferredDeath(sender, 4, false, null, 'key-resolve')
    expect(invoke).toHaveBeenCalledWith('ResolveDeferredDeath', credential, 4, false, null, 'key-resolve')
  })

  it('旅行者与流放命令按 Hub 方法名与参数顺序发出（票据 traveller-and-exile · D7）', async () => {
    const invoke = vi.fn(async () => ({ kind: 'Accepted', sequence: 13 }))
    const sender: CommandSender = { connection: { invoke } as unknown as HubConnection, credential }

    // 追加席位（seat = null）：服务端签发新席位与新票据，回执字段由 normalizeOutcome 带出。
    await joinTraveller(sender, null, 'bone-collector', 'Good', null, 'key-join')
    expect(invoke).toHaveBeenCalledWith(
      'JoinTraveller',
      credential,
      null,
      'bone-collector',
      'Good',
      null,
      'key-join',
    )

    // 指定席位 + 邪恶揭示名单：数组原样透传（平台只转达，不替说书人拍板）。
    await joinTraveller(sender, 16, 'deviant', 'Evil', [1, 2], 'key-join-2')
    expect(invoke).toHaveBeenCalledWith('JoinTraveller', credential, 16, 'deviant', 'Evil', [1, 2], 'key-join-2')

    await removeTraveller(sender, 16, '玩家离席', 'key-leave')
    expect(invoke).toHaveBeenCalledWith('RemoveTraveller', credential, 16, '玩家离席', 'key-leave')

    // 离场改成「玩家发起 → 说书人裁定」（D-0037）：裁定命令与直接移出是两条并存的通道。
    await resolveTravellerDeparture(sender, 16, true, '家里有事', 'key-depart-approve')
    expect(invoke).toHaveBeenCalledWith(
      'ResolveTravellerDeparture',
      credential,
      16,
      true,
      '家里有事',
      'key-depart-approve',
    )

    await resolveTravellerDeparture(sender, 16, false, null, 'key-depart-reject')
    expect(invoke).toHaveBeenCalledWith(
      'ResolveTravellerDeparture',
      credential,
      16,
      false,
      null,
      'key-depart-reject',
    )

    await resolveDayProtection(sender, 5, true, '有趣', 'key-protect')
    expect(invoke).toHaveBeenCalledWith('ResolveDayProtection', credential, 5, true, '有趣', 'key-protect')

    await startExileSweep(sender, 1, 3000, 1000, 'key-exile-sweep')
    expect(invoke).toHaveBeenCalledWith('StartExileSweep', credential, 1, 3000, 1000, 'key-exile-sweep')

    await resumeExileSweep(sender, 1, 'key-exile-resume')
    expect(invoke).toHaveBeenCalledWith('ResumeExileSweep', credential, 1, 'key-exile-resume')

    await countExileVotes(sender, 1, 'key-exile-count')
    expect(invoke).toHaveBeenCalledWith('CountExileVotes', credential, 1, 'key-exile-count')
  })
})

describe('桌的访问模式不是命令（D-0037）', () => {
  const credential = 'C'.repeat(43)

  it('按 Hub 方法名与参数顺序发出，并把服务端确认的新值交还界面', async () => {
    // 假服务端照实回新值（真实契约也是"回新值本身"），两次调用因此拿到各自的结果。
    const invoke = vi.fn(async (_method: string, _credential: string, inviteOnly: boolean) => inviteOnly)
    const sender: CommandSender = { connection: { invoke } as unknown as HubConnection, credential }

    expect(await setTableInviteOnly(sender, true)).toBe(true)
    expect(invoke).toHaveBeenCalledWith('SetTableInviteOnly', credential, true)

    // 关掉也一样：回执是布尔本身，不走命令回执的规范化（形状对不上）。
    expect(await setTableInviteOnly(sender, false)).toBe(false)
    expect(invoke).toHaveBeenLastCalledWith('SetTableInviteOnly', credential, false)
  })

  it('没有凭据就不发；传输异常 / 回执不是布尔都收敛成 null（界面据此说"没确认"）', async () => {
    const unauthorized = vi.fn()
    expect(
      await setTableInviteOnly(
        { connection: { invoke: unauthorized } as unknown as HubConnection, credential: '' },
        true,
      ),
    ).toBeNull()
    expect(unauthorized).not.toHaveBeenCalled()

    const broken = vi.fn(async () => {
      throw new Error('connection lost')
    })
    expect(
      await setTableInviteOnly({ connection: { invoke: broken } as unknown as HubConnection, credential }, true),
    ).toBeNull()

    // 旧服务端 / 坏回执：不是布尔就不采纳——绝不把"没听清"当成"已切换"。
    const shaped = vi.fn(async () => ({ kind: 'Accepted' }))
    expect(
      await setTableInviteOnly({ connection: { invoke: shaped } as unknown as HubConnection, credential }, true),
    ).toBeNull()
  })
})

describe('本地合成回执（非命令方法也走同一套展示路径）', () => {
  it('成功 / 失败两种形态字段齐备且 ok 与 kind 一致', () => {
    const ok = localSuccess('本桌已改为邀请制')
    expect(ok.ok).toBe(true)
    expect(ok.kind).toBe('Accepted')
    expect(ok.message).toBe('本桌已改为邀请制')
    expect(ok.rebuild).toBeNull()
    expect(ok.issuedSeat).toBeNull()
    expect(ok.issuedSeatTicket).toBeNull()

    const bad = localFailure('没能切换访问模式', 'Failed')
    expect(bad.ok).toBe(false)
    expect(bad.kind).toBe('Failed')
    expect(bad.sequence).toBeNull()
  })
})

describe('配板建议是只读查询（R-0041 / R-0042）', () => {
  const credential = 'C'.repeat(43)

  it('按 Hub 方法名与参数顺序发出，并把服务端建议原样交还（UI 不加工）', async () => {
    const proposal: SetupProposalDto = {
      ok: true,
      seed: 'a'.repeat(32),
      nonTravellerCount: 2,
      travellerCount: 0,
      assignments: [
        { seat: 1, character: 'clockmaker' },
        { seat: 2, character: 'dreamer' },
      ],
      distribution: [{ type: 'Townsfolk', count: 3 }],
      notes: ['设置调整 · 方古：外来者 +1（缺额由镇民补偿）'],
      failureCode: null,
      failureMessage: null,
    }
    const invoke = vi.fn(async () => proposal)
    const sender: CommandSender = { connection: { invoke } as unknown as HubConnection, credential }

    const result = await proposeSetup(sender, null)

    expect(result).toBe(proposal)
    expect(invoke).toHaveBeenCalledWith('ProposeSetup', credential, null, null)
  })

  it('没有凭据就不查：本地先拒绝，UI 拿到的失败形态字段齐备', async () => {
    const invoke = vi.fn()
    const sender: CommandSender = { connection: { invoke } as unknown as HubConnection, credential: '' }

    const result = await proposeSetup(sender, 'seed-1')

    expect(result.ok).toBe(false)
    expect(result.failureCode).toBe('setup.no_credential')
    expect(result.seed).toBe('')
    expect(result.assignments).toEqual([])
    expect(result.distribution).toEqual([])
    expect(result.notes).toEqual([])
    expect(invoke).not.toHaveBeenCalled()
  })

  it('传输异常收敛成建议的失败形态，不把异常抛给 UI', async () => {
    const invoke = vi.fn(async () => {
      throw new Error('connection lost')
    })
    const sender: CommandSender = { connection: { invoke } as unknown as HubConnection, credential }

    const result = await proposeSetup(sender, null)

    expect(result.ok).toBe(false)
    expect(result.failureCode).toBe('setup.transport')
    expect(result.failureMessage).toContain('connection lost')
    expect(result.seed).toBe('')
    expect(result.assignments).toEqual([])
  })
})
