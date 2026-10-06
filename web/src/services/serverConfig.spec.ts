import { describe, expect, it } from 'vitest'
import { readSeatCount } from '@/services/serverConfig'

/**
 * 席位数量出口（部署形态：服务端是唯一来源）。
 *
 * 前端此前把席位数量写死在构建期（`VITE_SEAT_COUNT`，默认 5），而它是**服务端配置**：
 * 两边分叉时服务端配 6 席、面板只画 5 席，说书人给不出第 6 席的角色，那名玩家永远进不了局。
 * 本用例锁住"取不到就明说取不到（null），绝不猜一个值"。
 */
function fakeFetch(payload: unknown, init: { ok?: boolean; throws?: boolean } = {}): typeof fetch {
  return (async () => {
    if (init.throws === true) {
      throw new Error('网络不可达')
    }

    return {
      ok: init.ok ?? true,
      json: async () => payload,
    } as unknown as Response
  }) as unknown as typeof fetch
}

describe('服务端席位数量出口', () => {
  it('服务端给出整数席位时原样采纳', async () => {
    expect(await readSeatCount(fakeFetch({ status: 'ok', game: 'default', seatCount: 7 }))).toBe(7)
  })

  it('字段缺失 / 类型不对 / 越界一律返回 null，不猜值', async () => {
    expect(await readSeatCount(fakeFetch({ status: 'ok' }))).toBeNull()
    expect(await readSeatCount(fakeFetch({ seatCount: '6' }))).toBeNull()
    expect(await readSeatCount(fakeFetch({ seatCount: 5.5 }))).toBeNull()
    expect(await readSeatCount(fakeFetch({ seatCount: 0 }))).toBeNull()
    expect(await readSeatCount(fakeFetch({ seatCount: -3 }))).toBeNull()
    expect(await readSeatCount(fakeFetch({ seatCount: 999 }))).toBeNull()
  })

  it('HTTP 不成功或请求抛错时返回 null，不把异常抛给面板', async () => {
    expect(await readSeatCount(fakeFetch({ seatCount: 5 }, { ok: false }))).toBeNull()
    expect(await readSeatCount(fakeFetch(null, { throws: true }))).toBeNull()
  })
})
