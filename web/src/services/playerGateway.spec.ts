import { describe, expect, it } from 'vitest'
import type { ReconnectBundleDto } from '@/contracts/game'
import { applyBundle, normalizeBundle, normalizePlayerEvent, normalizeRequest } from '@/services/playerGateway'

/**
 * 重连补齐（架构 §5、D-0010）：快照 + 从本客户端已知序号起的**全部事件**，两者一起用。
 * 这里的用例是 2026-10-02 复核实测到的一次真实缺陷的回归：前端把 `events` 写死成空数组整体丢弃。
 */
describe('重连包规范化', () => {
  it('真的读 events，而不是丢掉', () => {
    const bundle = normalizeBundle({
      sequence: 7,
      view: { seat: 2, phase: 'FirstNight', pendingRequest: null, informationResults: [] },
      events: [
        { sequence: 6, kind: 'PhaseStarted', phase: 'FirstNight' },
        {
          sequence: 7,
          kind: 'InformationResultIssued',
          information: { ability: 'dreamer', content: '你梦到 2 号' },
        },
      ],
    })

    expect(bundle.events).toHaveLength(2)
    expect(bundle.events[1]?.information?.ability).toBe('dreamer')
  })

  it('缺序号的条目被丢掉（无序号就无法证明补齐完整）', () => {
    expect(normalizePlayerEvent({ kind: 'PhaseStarted' })).toBeNull()
    expect(normalizePlayerEvent({ sequence: 3 })).toBeNull()
    expect(normalizePlayerEvent(null)).toBeNull()
  })

  it('信息类结果只有内容，没有"可能为假"标记', () => {
    const information = normalizeBundle({
      sequence: 1,
      view: {
        seat: 1,
        phase: 'FirstNight',
        pendingRequest: null,
        informationResults: [{ ability: 'oracle', content: '2 号是邪恶' }],
      },
      events: [],
    }).view.informationResults[0]

    expect(information).toEqual({ ability: 'oracle', content: '2 号是邪恶' })
  })

  it('请求缺 requestId 时返回 null（宁可少显示，不编造请求）', () => {
    expect(normalizeRequest({ seat: 1, context: '选一个目标', options: [] })).toBeNull()
    expect(normalizeRequest({ requestId: 'r1', seat: 1, context: '', options: [{ value: 'a' }] })).toEqual({
      requestId: 'r1',
      seat: 1,
      context: '',
      options: [{ value: 'a', preview: 'a' }],
    })
  })
})

describe('重连补齐折叠', () => {
  const bundle = (sequence: number, events: ReconnectBundleDto['events']): ReconnectBundleDto => ({
    sequence,
    view: { seat: 1, phase: 'FirstNight', pendingRequest: null, informationResults: [] },
    events,
  })

  const phaseEvent = (sequence: number): ReconnectBundleDto['events'][number] => ({
    sequence,
    kind: 'PhaseStarted',
    phase: null,
    request: null,
    requestId: null,
    optionValue: null,
    voidReason: null,
    voidNote: null,
    information: null,
  })

  it('事件恰好覆盖缺口时前进序号，并接上事件里的信息', () => {
    const applied = applyBundle(
      bundle(5, [
        phaseEvent(4),
        {
          ...phaseEvent(5),
          kind: 'InformationResultIssued',
          information: { ability: 'dreamer', content: '梦到 3 号' },
        },
      ]),
      3,
    )

    expect(applied.sequence).toBe(5)
    expect(applied.diagnostic).toBe('')
    expect(applied.informationResults.map((item) => item.ability)).toEqual(['dreamer'])
  })

  it('事件条数不足 = 有缺口：不前进序号，并报出缺口', () => {
    const applied = applyBundle(bundle(5, []), 3)
    expect(applied.sequence).toBe(3)
    expect(applied.diagnostic).toContain('缺口')
  })

  it('序号不连续（重复 / 跳号）同样报诊断，不装作没事', () => {
    const duplicated = applyBundle(bundle(5, [phaseEvent(4), phaseEvent(4)]), 3)
    expect(duplicated.sequence).toBe(3)
    expect(duplicated.diagnostic).toContain('不连续')
  })

  it('序号倒退（服务端快照比本地还旧）也报诊断', () => {
    const applied = applyBundle(bundle(2, []), 9)
    expect(applied.sequence).toBe(9)
    expect(applied.diagnostic).toContain('倒退')
  })
})
