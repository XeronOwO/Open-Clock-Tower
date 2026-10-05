import { describe, expect, it } from 'vitest'
import {
  MAX_ANNOTATION_LENGTH,
  annotationTokenTextOf,
  asArray,
  asCount,
  asCredential,
  asSeatNumber,
  asSizedText,
  clockTimeOf,
  displayNameOf,
  normalizeBarberNight,
  normalizeDayProtectionPrompt,
  normalizeDayView,
  normalizeFangGuInfection,
  normalizeRoomHealth,
  normalizeSeatAnnotation,
  normalizeStorytellerView,
  optionDisplayOf,
  replaceControlCharacters,
  seatDisplayOf,
  seatTextOf,
  waitingSecondsTextOf,
} from '@/display/format'
import type { DecisionOptionDto, SeatDisplayNameDto } from '@/contracts/game'

/** 选项夹具：真值 / 分组 / 徽章是信息类候选才有的元数据，普通候选一律为空。 */
function optionOf(value: string, preview: string): DecisionOptionDto {
  return { value, preview, truth: null, group: null, tags: [] }
}

describe('不可信输入规范化（架构 §4.4）', () => {
  it('非数组一律退化成空集合，不炸渲染', () => {
    expect(asArray<number>(null)).toEqual([])
    expect(asArray<number>({ length: 1 })).toEqual([])
    expect(asArray<number>([1, 2])).toEqual([1, 2])
  })

  it('席位号必须是 1..1000 的正整数：0 / 负数 / 小数 / 超界都退化成 null', () => {
    expect(asSeatNumber(3)).toBe(3)
    expect(asSeatNumber(0)).toBeNull()
    expect(asSeatNumber(-1)).toBeNull()
    expect(asSeatNumber(1.5)).toBeNull()
    expect(asSeatNumber(1_001)).toBeNull()
    expect(asSeatNumber('3')).toBeNull()
  })

  it('可空席位：缺值与坏值退化成占位符，合法值走统一口径（含玩家名）', () => {
    const names: SeatDisplayNameDto[] = [{ seat: 2, displayName: '小明' }]

    expect(seatTextOf(3, names)).toBe('3 号')
    expect(seatTextOf(2, names)).toBe('2 号 · 小明')
    expect(seatTextOf(null, names)).toBe('—')
    expect(seatTextOf(undefined, names)).toBe('—')
    expect(seatTextOf(Number.NaN, names)).toBe('—')
  })

  it('席位显示文本带玩家名（D-0021）：有名字「N 号 · 玩家名」，没名字回退「N 号」', () => {
    const names: SeatDisplayNameDto[] = [{ seat: 2, displayName: '小明' }]

    expect(displayNameOf(2, names)).toBe('小明')
    expect(displayNameOf(3, names)).toBeNull()
    expect(seatDisplayOf(2, names)).toBe('2 号 · 小明')
    expect(seatDisplayOf(3, names)).toBe('3 号')
  })

  it('选项文案按值格式本地化（D-0021）：有名字才改写，没名字保留服务端原文', () => {
    const names: SeatDisplayNameDto[] = [{ seat: 2, displayName: '小明' }]

    expect(optionDisplayOf(optionOf('seat:2', '2 号玩家'), names)).toBe('2 号 · 小明')
    expect(optionDisplayOf(optionOf('pair:2+5', '2 号 + 5 号'), names)).toBe('2 号 + 5 号')
    expect(optionDisplayOf(optionOf('seat:3', '3 号玩家'), names)).toBe('3 号玩家')
    expect(optionDisplayOf(optionOf('pair:3+4', '3 号 + 4 号'), names)).toBe('3 号 + 4 号')
    expect(optionDisplayOf(optionOf('clockmaker', '钟表匠'), names)).toBe('钟表匠')
    expect(optionDisplayOf(optionOf('seat:x', '坏值原样'), names)).toBe('坏值原样')
  })

  it('两名玩家都有名字时，配对选项同样换成统一席位口径', () => {
    const names: SeatDisplayNameDto[] = [
      { seat: 2, displayName: '小明' },
      { seat: 5, displayName: '小红' },
    ]

    expect(optionDisplayOf(optionOf('pair:2+5', '2 号 + 5 号'), names)).toBe(
      '2 号 · 小明 + 5 号 · 小红',
    )
  })

  it('等待秒数未知时不编数字', () => {
    expect(waitingSecondsTextOf(3.25)).toBe('3.3 秒')
    expect(waitingSecondsTextOf(null)).toBeNull()
    expect(waitingSecondsTextOf(Number.POSITIVE_INFINITY)).toBeNull()
  })

  it('时刻解析不了就原样回显', () => {
    expect(clockTimeOf('不是时间')).toBe('不是时间')
    expect(clockTimeOf('')).toBe('—')
    expect(clockTimeOf('2026-10-02T20:15:00+08:00')).toMatch(/\d{2}:\d{2}:\d{2}/)
  })
})

describe('说书人视图规范化', () => {
  it('空载荷退化成"未开始"的空板，而不是崩掉', () => {
    const view = normalizeStorytellerView(null)
    expect(view.phase).toBe('未开始')
    expect(view.sequence).toBe(0)
    expect(view.seats).toEqual([])
    expect(view.effects).toEqual([])
    expect(view.pending).toBeNull()
    expect(view.lastResolution).toBeNull()
    expect(view.stepDigest).toBeNull()
    expect(view.lastVoidedRequest).toBeNull()
    expect(view.awaitingDecisionOptions).toEqual([])
    expect(view.awaitingDecisionSeat).toBeNull()
    expect(view.fangGuInfection).toBeNull()
    expect(view.barberNight).toBeNull()
    expect(view.health).toEqual({ degraded: false, reason: null, since: null })
  })

  it('房间健康位：坏字段按正常保守渲染，降级字段原样透传', () => {
    expect(normalizeRoomHealth(null)).toEqual({ degraded: false, reason: null, since: null })
    expect(normalizeRoomHealth({ degraded: 'yes', reason: 42 })).toEqual({
      degraded: false,
      reason: null,
      since: null,
    })

    const view = normalizeStorytellerView({
      health: {
        degraded: true,
        reason: '恢复失败：事件载荷损坏：SeatStateChangedEvent',
        since: '2026-10-02T20:15:00+08:00',
      },
    })
    expect(view.health.degraded).toBe(true)
    expect(view.health.reason).toContain('事件载荷损坏')
    expect(view.health.since).toBe('2026-10-02T20:15:00+08:00')
  })

  it('坏掉的条目被丢掉，好的条目保留', () => {
    const view = normalizeStorytellerView({
      sequence: 42,
      phase: 'Night',
      seats: [
        { seat: 3, facts: [{ dimension: 'Poison', value: 'Poisoned', reason: '投毒', causedBy: 1 }], madnesses: [] },
        { seat: '三号', facts: [] },
        null,
      ],
      effects: [{ effectId: 'e1', kind: 'Persistent', ability: 'no-dashii', source: 5, target: 3, terminated: false }],
      recentSeatChanges: [{ seat: 3, reason: '投毒', sequence: 9, recordedAt: '2026-10-02T20:15:00+08:00' }],
      abilityUses: [{ seat: 3, ability: 'dreamer', effective: false }],
      malfunctions: [{ seat: 3, ability: 'dreamer', kind: 'Poisoned' }],
      lastResolution: { seat: 3, ability: 'dreamer', effective: false, malfunctions: ['Poisoned'], sequence: 12 },
      awaitingDecisionId: 'dp-1',
      awaitingDecisionOptions: [{ value: 'a', preview: '选它' }, { preview: '缺值' }],
    })

    expect(view.sequence).toBe(42)
    expect(view.seats).toHaveLength(1)
    expect(view.seats[0]?.facts[0]?.causedBy).toBe(1)
    expect(view.effects).toHaveLength(1)
    expect(view.effects[0]?.terminated).toBe(false)
    expect(view.recentSeatChanges).toHaveLength(1)
    expect(view.abilityUses[0]?.effective).toBe(false)
    expect(view.malfunctions[0]?.kind).toBe('Poisoned')
    expect(view.lastResolution?.effective).toBe(false)
    expect(view.lastResolution?.malfunctions).toEqual(['Poisoned'])
    expect(view.awaitingDecisionOptions).toEqual([
      { value: 'a', preview: '选它', truth: null, group: null, tags: [] },
    ])
  })

  it('归属席位与两枚整局事实（限一次 / 今晚理发）按形状归一化', () => {
    const view = normalizeStorytellerView({
      awaitingDecisionId: 'dp-9',
      awaitingDecisionSeat: 3,
      fangGuInfection: { seat: 3, source: 1, note: '方古侵染' },
      barberNight: { source: 1, note: '理发师死亡' },
    })

    expect(view.awaitingDecisionSeat).toBe(3)
    expect(view.fangGuInfection).toEqual({ seat: 3, source: 1, note: '方古侵染' })
    expect(view.barberNight).toEqual({ source: 1, note: '理发师死亡' })
  })

  it('限一次 / 今晚理发：坏形状退化成"没有事实"，不编数据', () => {
    expect(normalizeFangGuInfection({ seat: 0, source: 1 })).toBeNull()
    expect(normalizeFangGuInfection({ seat: 3 })).toBeNull()
    expect(normalizeFangGuInfection(null)).toBeNull()
    expect(normalizeBarberNight({ source: 0 })).toBeNull()
    expect(normalizeBarberNight({ source: '一号' })).toBeNull()

    const view = normalizeStorytellerView({
      awaitingDecisionSeat: '三号',
      fangGuInfection: { seat: 0, source: 1 },
      barberNight: null,
    })
    expect(view.awaitingDecisionSeat).toBeNull()
    expect(view.fangGuInfection).toBeNull()
    expect(view.barberNight).toBeNull()

    // 归属链上的席位字段必须拒绝 0：否则会短路前端的回退链（「定位到 0 号」）。
    const zeroSeats = normalizeStorytellerView({ awaitingDecisionSeat: 0, currentSlotActor: 0 })
    expect(zeroSeats.awaitingDecisionSeat).toBeNull()
    expect(zeroSeats.currentSlotActor).toBeNull()
  })

  it('每步摘要与最近作废按形状归一化，枚举/数字类型不猜', () => {
    const view = normalizeStorytellerView({
      stepDigest: {
        seat: 5,
        character: 'clockmaker',
        state: {
          seat: 5,
          facts: [{ dimension: 'Poison', value: 'Poisoned', reason: '常驻效果', causedBy: 1, effectId: 'e1' }],
          madnesses: [],
        },
        ability: {
          basis: 'Preview',
          ability: 'clockmaker',
          effective: false,
          malfunctions: ['Poisoned'],
          note: '来源中毒：能力未生效',
          sequence: null,
        },
        optionCount: 0,
        onNoOption: 'StorytellerDecides',
      },
      lastVoidedRequest: {
        sequence: 8,
        requestId: 'r1',
        reason: 'DependencyViolated',
        note: '座位 2 的状态变化使请求失去意义',
      },
    })

    expect(view.stepDigest?.seat).toBe(5)
    expect(view.stepDigest?.character).toBe('clockmaker')
    expect(view.stepDigest?.state?.facts[0]?.causedBy).toBe(1)
    expect(view.stepDigest?.ability?.basis).toBe('Preview')
    expect(view.stepDigest?.ability?.effective).toBe(false)
    expect(view.stepDigest?.ability?.malfunctions).toEqual(['Poisoned'])
    expect(view.stepDigest?.optionCount).toBe(0)
    expect(view.stepDigest?.onNoOption).toBe('StorytellerDecides')
    expect(view.lastVoidedRequest?.reason).toBe('DependencyViolated')
    expect(view.lastVoidedRequest?.sequence).toBe(8)
  })

  it('坏掉的新字段不编结论（缺席位号 / 缺作废原因 / 缺序号都退化为 null）', () => {
    const view = normalizeStorytellerView({
      stepDigest: { seat: '五号' },
      lastVoidedRequest: { reason: 'DependencyViolated' },
    })

    expect(view.stepDigest).toBeNull()
    expect(view.lastVoidedRequest).toBeNull()

    // 缺序号 = 没法与快照比较先后，按坏载荷丢弃，不猜一个序号。
    const missingSequence = normalizeStorytellerView({
      lastVoidedRequest: { requestId: 'r1', reason: 'DependencyViolated' },
    })
    expect(missingSequence.lastVoidedRequest).toBeNull()
  })

  it('失效原因只收字符串数组：标量 / 混入非字符串都退化，不编原因', () => {
    const view = normalizeStorytellerView({
      lastResolution: {
        seat: 1,
        ability: 'clockmaker',
        effective: true,
        malfunctions: ['Vortox', 42, null],
        sequence: 3,
      },
      stepDigest: {
        seat: 1,
        ability: { basis: 'Settled', ability: 'clockmaker', effective: true, malfunctions: 'Vortox' },
      },
    })

    expect(view.lastResolution?.malfunctions).toEqual(['Vortox'])
    expect(view.stepDigest?.ability?.malfunctions).toEqual([])
  })
})

describe('不可信输入的有界化（长度 / 范围，架构 §4.4）', () => {
  it('文本超长截断，而不是把面板撑爆；非字符串仍是坏载荷', () => {
    expect(asSizedText('x'.repeat(50), 10)).toBe('x'.repeat(10))
    expect(asSizedText('短', 10)).toBe('短')
    expect(asSizedText(123, 10)).toBeNull()
    expect(asSizedText('', 10)).toBeNull()
  })

  it('计数只接受非负整数：负数 / 小数 / 超大值都是坏载荷', () => {
    expect(asCount(3)).toBe(3)
    expect(asCount(0)).toBe(0)
    expect(asCount(-1)).toBeNull()
    expect(asCount(1.5)).toBeNull()
    expect(asCount(1_000_001)).toBeNull()
    expect(asCount('3')).toBeNull()
  })

  it('凭据必须是有界、无空白 / 控制字符的串（凭据不渲染，但形状要先校验）', () => {
    const valid = 'A'.repeat(43)
    expect(asCredential(valid)).toBe(valid)
    expect(asCredential('short')).toBeNull()
    expect(asCredential(`${'A'.repeat(20)} ${'B'.repeat(20)}`)).toBeNull()
    expect(asCredential('A'.repeat(513))).toBeNull()
    expect(asCredential(42)).toBeNull()
  })

  it('说书人视图里的负数 / 小数序号被当作坏载荷（退化为 0，而不是显示 -1）', () => {
    const view = normalizeStorytellerView({ sequence: -5, slotIndex: 1.5, slotCount: Number.NaN })
    expect(view.sequence).toBe(0)
    expect(view.slotIndex).toBe(0)
    expect(view.slotCount).toBe(0)
  })

  it('疯狂要求（自由文本）逐条有界化：超长截断，非字符串丢掉', () => {
    const view = normalizeStorytellerView({
      seats: [{ seat: 1, facts: [], madnesses: ['x'.repeat(300), 42, ''] }],
    })

    expect(view.seats[0]?.madnesses).toHaveLength(1)
    expect(view.seats[0]?.madnesses[0]?.length).toBe(200)
  })
})

describe('失去能力标记的归一化（R-0040）', () => {
  it('缺省退化成空集合；坏条目只丢自己，不炸渲染', () => {
    expect(normalizeStorytellerView(null).lostAbilityMarkers).toEqual([])

    const view = normalizeStorytellerView({
      lostAbilityMarkers: [
        { seat: 2, ability: 'seamstress', note: '女裁缝（2 号）：能力已用尽' },
        { seat: 0, ability: 'artist', note: '坏席位' },
        { seat: 3, ability: '', note: '坏能力' },
        'not-an-object',
      ],
    })

    expect(view.lostAbilityMarkers).toEqual([
      { seat: 2, ability: 'seamstress', note: '女裁缝（2 号）：能力已用尽' },
    ])
  })
})

describe('说书人注记的归一化（D-0019）', () => {
  it('缺省退化成空集合；坏条目只丢自己，不炸渲染', () => {
    expect(normalizeStorytellerView(null).annotations).toEqual([])

    const view = normalizeStorytellerView({
      annotations: [
        { id: 1, seat: 2, text: '甲' },
        { id: 'x', seat: 2, text: '乙' },
        { id: 2, seat: 0, text: '丙' },
        { id: 3, seat: 2, text: '' },
      ],
    })

    expect(view.annotations).toEqual([{ id: 1, seat: 2, text: '甲' }])
  })

  it('文本超长按上限截断（服务端已限一次，这里仍防一手）', () => {
    const note = normalizeSeatAnnotation({
      id: 1,
      seat: 1,
      text: '甲'.repeat(MAX_ANNOTATION_LENGTH + 10),
    })

    expect(note?.text.length).toBe(MAX_ANNOTATION_LENGTH)
  })

  it('牌面 token 文案：控制字符 / 换行折成空格，超长截断并带省略号', () => {
    expect(annotationTokenTextOf('甲\n\n乙')).toBe('甲 乙')
    expect(annotationTokenTextOf('短')).toBe('短')
    expect(annotationTokenTextOf('很长'.repeat(20))).toBe(`${'很长'.repeat(8)}…`)
  })

  it('控制字符逐字符折成空格（刻意不用控制字符正则）', () => {
    expect(replaceControlCharacters('甲\u0000乙\u007f丙')).toBe('甲 乙 丙')
  })
})

describe('白天流放 / 保护 / 额外提名窗口的归一化（票据 traveller-and-exile · D7）', () => {
  it('流放账、保护裁定与窗口按形状归一化；坏条目只丢自己', () => {
    const view = normalizeDayView({
      dayNumber: 2,
      status: 'Open',
      nominations: [],
      exiles: [
        {
          index: 1,
          proposer: 1,
          target: 3,
          status: 'Voting',
          votes: 2,
          voters: [1, 2],
          handsRaised: [2],
          sweep: null,
          conclusion: null,
        },
        { index: 'x', proposer: 1, target: 3, status: 'Voting' },
      ],
      openExileIndex: 1,
      protections: [{ seat: 3, protected: true }, { seat: 3, protected: 'yes' }],
      extraNomination: { seat: 4, status: 'Open' },
    })

    expect(view?.exiles).toEqual([
      {
        index: 1,
        proposer: 1,
        target: 3,
        status: 'Voting',
        votes: 2,
        voters: [1, 2],
        handsRaised: [2],
        sweep: null,
        conclusion: null,
      },
    ])
    expect(view?.openExileIndex).toBe(1)
    expect(view?.protections).toEqual([{ seat: 3, protected: true }])
    expect(view?.extraNomination).toEqual({ seat: 4, status: 'Open' })
  })

  it('旧服务端形状（还没有这些字段）退化成空集合 / null，不编造', () => {
    const view = normalizeDayView({ dayNumber: 1, status: 'Closed', nominations: [] })

    expect(view?.exiles).toEqual([])
    expect(view?.openExileIndex).toBeNull()
    expect(view?.protections).toEqual([])
    expect(view?.extraNomination).toBeNull()
  })
})

describe('死亡保护裁定提示的归一化（R-0048）', () => {
  it('两态提示按形状归一化；席位 / 结论不合法时退化成"没有入口"', () => {
    expect(normalizeDayProtectionPrompt(null)).toBeNull()
    expect(normalizeDayProtectionPrompt('x')).toBeNull()
    expect(normalizeDayProtectionPrompt({ seat: 2, outcome: 'NeedsRuling' })).toEqual({
      seat: 2,
      outcome: 'NeedsRuling',
      note: '',
    })
    expect(
      normalizeDayProtectionPrompt({ seat: 5, outcome: 'Indeterminate', note: '先补观测' }),
    ).toEqual({ seat: 5, outcome: 'Indeterminate', note: '先补观测' })

    // 结论不在两态内 / 席位非法：宁可不给入口，也不让说书人对坏数据做裁定。
    expect(normalizeDayProtectionPrompt({ seat: 2, outcome: 'Protected' })).toBeNull()
    expect(normalizeDayProtectionPrompt({ seat: 0, outcome: 'NeedsRuling' })).toBeNull()
    expect(normalizeDayProtectionPrompt({ seat: '2', outcome: 'NeedsRuling' })).toBeNull()
  })

  it('说书人视图缺省该字段为 null（旧服务端形状不编入口）', () => {
    expect(normalizeStorytellerView({}).pendingProtection).toBeNull()
    expect(
      normalizeStorytellerView({
        pendingProtection: { seat: 3, outcome: 'NeedsRuling', note: '怪咖还没裁定' },
      }).pendingProtection,
    ).toEqual({ seat: 3, outcome: 'NeedsRuling', note: '怪咖还没裁定' })
  })
})
