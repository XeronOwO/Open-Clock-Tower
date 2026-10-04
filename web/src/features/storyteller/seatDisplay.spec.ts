/**
 * 席位口径的组件级渲染回归（E30 残余①：说书人数据抽屉类组件曾只显示「N 号」）。
 *
 * 为什么用 SSR 字符串渲染而不是真机：这八个组件里 `PitHagNightPanel` / `StepDigest` 等在
 * 真机装置里需要特定窗口 / 槽位才出现，逐条安排真机夹具成本高；它们的席位文案是纯模板映射，
 * 用 Vue 自己的 `@vue/server-renderer`（`vue` 的既带依赖，见 package-lock）渲染一次，
 * 就能把"到底拼的是哪份口径"变成可失败的断言。
 *
 * 真机链路由 `tools/verify-accounts.mjs` 的抽屉段覆盖（有名字 / 无名字两种席位）。
 */
import { renderToString } from '@vue/server-renderer'
import { createSSRApp, type Component } from 'vue'
import { describe, expect, it } from 'vitest'
import type { StorytellerViewDto } from '@/contracts/game'
import { normalizeStorytellerView } from '@/display/format'
import AssignmentControl from '@/features/storyteller/AssignmentControl.vue'
import EffectChainPanel from '@/features/storyteller/EffectChainPanel.vue'
import GrimoireAnnotationControl from '@/features/storyteller/GrimoireAnnotationControl.vue'
import LedgerPanel from '@/features/storyteller/LedgerPanel.vue'
import PitHagNightPanel from '@/features/storyteller/PitHagNightPanel.vue'
import SeatChangeTimeline from '@/features/storyteller/SeatChangeTimeline.vue'
import SeatLedgerPanel from '@/features/storyteller/SeatLedgerPanel.vue'
import StepDigest from '@/features/storyteller/StepDigest.vue'

const NAMED = '1 号 · 爱丽丝'

/** 用生产侧归一化器造一份最小视图，再覆盖需要的字段（避免手写 30 个必填字段）。 */
function viewOf(overrides: Partial<StorytellerViewDto> = {}): StorytellerViewDto {
  return { ...normalizeStorytellerView({}), ...overrides }
}

/** 渲染一个组件到 HTML 字符串；需要 `sender` 的组件单独传假对象（渲染期不碰它）。 */
function render(component: Component, props: Record<string, unknown>): Promise<string> {
  return renderToString(createSSRApp(component, props))
}

const FAKE_SENDER = { connection: null, credential: '' }

function namedView(overrides: Partial<StorytellerViewDto> = {}): StorytellerViewDto {
  return viewOf({
    slotCount: 4,
    seatNames: [{ seat: 1, displayName: '爱丽丝' }],
    seats: [
      {
        seat: 1,
        facts: [{ dimension: 'Life', value: 'Dead', reason: '测试上报', causedBy: null, effectId: null }],
        madnesses: [],
      },
    ],
    ...overrides,
  })
}

describe('说书人抽屉 / 面板按统一席位口径渲染玩家名（E30 残余①）', () => {
  it('状态账 / 最近变化 / 效果链 / 两本账：每张表都用「N 号 · 玩家名」', async () => {
    const view = namedView({
      effects: [
        {
          effectId: 'standing:no-dashii.poison:1:2',
          kind: 'Persistent',
          ability: 'no-dashii',
          source: 1,
          target: 2,
          sourceCharacter: 'no-dashii',
          grantedCharacter: null,
          window: null,
          terminated: false,
          terminationKind: null,
          terminationReason: null,
          terminationCausedBy: null,
        },
      ],
      recentSeatChanges: [
        {
          seat: 1,
          life: 'Dead',
          character: null,
          alignment: null,
          drunk: null,
          poison: null,
          reason: '测试上报',
          causedBy: null,
          effectId: null,
          sequence: 3,
          recordedAt: '2026-10-05T10:00:00Z',
        },
      ],
      abilityUses: [{ seat: 1, ability: 'clockmaker', effective: true }],
      malfunctions: [{ seat: 1, ability: 'dreamer', kind: 'Poisoned' }],
      lastResolution: { seat: 1, ability: 'dreamer', effective: false, malfunctions: [], note: null, sequence: 4 },
    })

    expect(await render(SeatLedgerPanel, { view })).toContain(NAMED)
    expect(await render(SeatChangeTimeline, { view })).toContain(NAMED)
    expect(await render(EffectChainPanel, { view })).toContain(NAMED)
    expect(await render(LedgerPanel, { view })).toContain(NAMED)
  })

  it('当前步骤 / 开局分配 / 席内注记：行动者与目标席位名一致', async () => {
    const view = namedView({
      currentSlotActor: 1,
      stepDigest: {
        seat: 1,
        character: 'clockmaker',
        state: null,
        ability: null,
        optionCount: null,
        onNoOption: null,
      },
    })

    expect(await render(StepDigest, { view })).toContain(NAMED)
    expect(await render(AssignmentControl, { view, sender: FAKE_SENDER, seatCount: 2 })).toContain(NAMED)
    expect(await render(GrimoireAnnotationControl, { view, sender: FAKE_SENDER, seat: 1 })).toContain(NAMED)
  })

  it('麻脸巫婆之夜：创造者与待定死亡的来源 / 目标都用同一口径', async () => {
    const view = namedView({
      pitHagNight: {
        source: 1,
        closesAfterSlotIndex: 3,
        deferred: [
          { target: 2, source: 1, ability: 'fang-gu', note: '测试击杀', transformation: false },
        ],
      },
    })

    const html = await render(PitHagNightPanel, { view, sender: FAKE_SENDER })

    expect(html).toContain(NAMED)
    expect(html).toContain('2 号')
  })

  it('游客席位回退「N 号」；注记目标缺失时退占位符', async () => {
    const guest = viewOf({
      seats: [
        {
          seat: 1,
          facts: [{ dimension: 'Life', value: 'Alive', reason: '开局分配', causedBy: null, effectId: null }],
          madnesses: [],
        },
      ],
    })

    const ledger = await render(SeatLedgerPanel, { view: guest })

    expect(ledger).toContain('1 号')
    expect(ledger).not.toContain('·')
    expect(await render(GrimoireAnnotationControl, { view: guest, sender: FAKE_SENDER, seat: null })).toContain('—')
  })
})
