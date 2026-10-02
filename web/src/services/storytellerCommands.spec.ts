import { describe, expect, it } from 'vitest'
import { newIdempotencyKey } from '@/services/idempotency'
import { normalizeOutcome } from '@/services/storytellerCommands'

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
    })
    expect(normalizeOutcome({ kind: 'Duplicate', sequence: 12 }).ok).toBe(true)
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
