import { describe, expect, it, vi } from 'vitest'
import type { HubConnection } from '@microsoft/signalr'
import { newIdempotencyKey } from '@/services/idempotency'
import {
  forceAdvance,
  invokeCommand,
  normalizeOutcome,
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
})
