import { describe, expect, it, vi } from 'vitest'
import type { HubConnection } from '@microsoft/signalr'
import type { SetupProposalDto } from '@/contracts/game'
import { newIdempotencyKey } from '@/services/idempotency'
import {
  closeDay,
  countVotes,
  forceAdvance,
  invokeCommand,
  normalizeOutcome,
  pitHagCasualty,
  proposeSetup,
  resolveDeferredDeath,
  resumeVoteSweep,
  startDay,
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
    })
    expect(normalizeOutcome({ kind: 'Duplicate', sequence: 12 }).ok).toBe(true)
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
})

describe('配板建议是只读查询（R-0041 / R-0042）', () => {
  const credential = 'C'.repeat(43)

  it('按 Hub 方法名与参数顺序发出，并把服务端建议原样交还（UI 不加工）', async () => {
    const proposal: SetupProposalDto = {
      ok: true,
      seed: 'a'.repeat(32),
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
    expect(invoke).toHaveBeenCalledWith('ProposeSetup', credential, null)
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
