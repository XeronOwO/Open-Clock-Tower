/**
 * 席位牌的组件级渲染回归（D-0007 热链 + 失败降级）。
 *
 * 真机装置只跑"图能加载"的正常路径；这里用 Vue 的 SSR 渲染把**有图 / 无图 / 未观测**
 * 三种初态固定下来（运行期 `@error` 撤图由 `tools/check-character-art.mjs` 在真浏览器里证明）。
 */
import { renderToString } from '@vue/server-renderer'
import { createSSRApp } from 'vue'
import { describe, expect, it } from 'vitest'
import type { SeatCardModel } from '@/display/grimoire'
import GrimoireSeatCard from '@/features/grimoire/GrimoireSeatCard.vue'

function modelOf(character: string | null): SeatCardModel {
  return {
    seat: 1,
    displayName: null,
    observed: true,
    character,
    alignment: 'Good',
    life: 'Alive',
    drunk: null,
    poison: null,
    madnesses: [],
    facts: [],
    effects: [],
    annotations: [],
    lostAbilityMarkers: [],
    marks: [],
  }
}

function render(model: SeatCardModel): Promise<string> {
  return renderToString(
    createSSRApp(GrimoireSeatCard, {
      model,
      position: null,
      selected: false,
      isCurrentSlot: false,
      hasPending: false,
      hasDecision: false,
    }),
  )
}

describe('席位牌角色图（热链 + 降级）', () => {
  it('已知角色：渲染百科图片地址，no-referrer / lazy，角色名仍在', async () => {
    const html = await render(modelOf('clockmaker'))

    expect(html).toContain('data-testid="character-art"')
    expect(html).toContain('https://clocktower-wiki.gstonegames.com/images/7/74/Clockmaker.png')
    expect(html).toContain('referrerpolicy="no-referrer"')
    expect(html).toContain('loading="lazy"')
    expect(html).toContain('钟表匠')
  })

  it('未知角色：不渲染 <img>，文字原样回显（降级面 = 现有文字 + 色环）', async () => {
    const html = await render(modelOf('not-a-character'))

    expect(html).not.toContain('character-art')
    expect(html).toContain('not-a-character')
  })

  it('角色未观测：不渲染 <img>，显示「角色未观测」', async () => {
    const html = await render(modelOf(null))

    expect(html).not.toContain('character-art')
    expect(html).toContain('角色未观测')
  })
})
