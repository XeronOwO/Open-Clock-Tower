/**
 * 访问模式开关与离场裁定区的**渲染回归**（D-0037）：把装置要用的锚点变成会失败的断言。
 *
 * 为什么用 SSR 字符串渲染而不是真机：`TableAccessControl` 的开关要连真宿主才能点，
 * 离场裁定区要等玩家先提申请才出现——逐条安排真机夹具成本高；它们都是纯模板映射，
 * 用 Vue 自己的 `@vue/server-renderer` 渲染一次，就能把"锚点到底叫什么、什么条件下才渲染"
 * 变成可失败的断言（与 `seatDisplay.spec.ts` 同一手法）。
 *
 * 真机链路由验收装置覆盖（切换即推 / 批准离场走完整链路）。
 */
import { renderToString } from '@vue/server-renderer'
import { createSSRApp, type Component } from 'vue'
import { describe, expect, it } from 'vitest'
import { normalizeStorytellerView } from '@/display/format'
import TableAccessControl from '@/features/storyteller/TableAccessControl.vue'
import TravellerControl from '@/features/storyteller/TravellerControl.vue'

/** 渲染一个组件到 HTML 字符串；`sender` 只被事件处理函数用，渲染期不碰它。 */
function render(component: Component, props: Record<string, unknown>): Promise<string> {
  return renderToString(createSSRApp(component, props))
}

const FAKE_SENDER = { connection: null, credential: '' }

/** 用生产侧归一化器造一份视图，再覆盖需要的字段（避免手写 30 个必填字段）。 */
function viewOf(overrides: Record<string, unknown> = {}) {
  return { ...normalizeStorytellerView({}), ...overrides }
}

describe('访问模式开关（D-0037）', () => {
  it('公开桌：读数 false，按钮说的是"改成邀请制"', async () => {
    const html = await render(TableAccessControl, { inviteOnly: false, sender: FAKE_SENDER })

    expect(html).toContain('data-testid="table-access"')
    expect(html).toContain('data-invite-only="false"')
    expect(html).toContain('data-testid="table-access-toggle"')
    expect(html).toContain('改成邀请制')
    // 两条闸要说清是两件事：开局之后自助入座自动关闭，与本开关无关。
    expect(html).toContain('开局之后自助入座会自动关闭')
  })

  it('邀请制桌：读数 true，按钮说的是"改成公开桌"', async () => {
    const html = await render(TableAccessControl, { inviteOnly: true, sender: FAKE_SENDER })

    expect(html).toContain('data-invite-only="true"')
    expect(html).toContain('改成公开桌')
  })
})

describe('待批离场申请的裁定区（D-0037）', () => {
  it('有待批申请：逐行渲染席位 + 理由 + 批准 / 驳回两个按钮', async () => {
    const html = await render(TravellerControl, {
      view: viewOf({
        seatNames: [{ seat: 2, displayName: '爱丽丝' }],
        departureRequests: [
          { seat: 2, note: '家里有事' },
          { seat: 5, note: null },
        ],
      }),
      sender: FAKE_SENDER,
      gameId: 'table-x',
    })

    expect(html).toContain('data-testid="traveller-departures"')
    expect(html).toContain('data-departure-seat="2"')
    expect(html).toContain('data-departure-seat="5"')
    // 席位走统一口径（带玩家名），理由照实显示；没写理由也给一句，不留空白。
    expect(html).toContain('2 号 · 爱丽丝')
    expect(html).toContain('理由：家里有事')
    expect(html).toContain('（没有写理由）')
    // 两个按钮各一行一个：装置按 data-departure-seat 定位到行再点。
    expect(html.match(/data-testid="departure-approve"/g)).toHaveLength(2)
    expect(html.match(/data-testid="departure-reject"/g)).toHaveLength(2)
    expect(html.match(/data-testid="departure-ruling-note"/g)).toHaveLength(2)
  })

  it('没有待批申请：整块不渲染（不留空壳标题），加入 / 移出照旧在', async () => {
    const html = await render(TravellerControl, {
      view: viewOf(),
      sender: FAKE_SENDER,
      gameId: 'table-x',
    })

    expect(html).not.toContain('traveller-departures')
    expect(html).not.toContain('departure-approve')
    // 说书人**始终保留**直接移出的权限：申请区不渲染不影响它。
    expect(html).toContain('data-testid="traveller-remove"')
    expect(html).toContain('data-testid="traveller-join"')
  })
})
