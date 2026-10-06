import { describe, expect, it } from 'vitest'
import { HOME_LINK, PLAY_LINK, parseRoute, routeLabel, STORYTELLER_LINK } from '@/display/navigation'

/**
 * 导航解析：四个面 + **两条兼容红线**。
 *
 * 这两条红线是真机装置与用户链接依赖的行为，改错任何一条都会让它们静默失灵
 * （装置会在"等说书人面板出现"那一步超时，而不是给出一条看得懂的失败）。
 */
describe('URL hash → 面', () => {
  it('兼容红线一：空 hash（容器访问根路径）仍然是说书人端', () => {
    expect(parseRoute('')).toBe('storyteller')
    expect(parseRoute('#')).toBe('storyteller')
    expect(parseRoute('#/')).toBe('storyteller')
  })

  it('兼容红线二：旧的 #player 仍然进玩家端', () => {
    expect(parseRoute('#player')).toBe('player')
    expect(parseRoute('#Player')).toBe('player')
  })

  it('新写法：#/play 与 #/home', () => {
    expect(parseRoute(PLAY_LINK)).toBe('player')
    expect(parseRoute(HOME_LINK)).toBe('home')
  })

  it('说书人端的显式地址', () => {
    expect(parseRoute(STORYTELLER_LINK)).toBe('storyteller')
  })

  it('不认识的值退回说书人端（历史行为，不是报错页）', () => {
    expect(parseRoute('#whatever')).toBe('storyteller')
  })

  it('每个面都有可读标题', () => {
    expect(routeLabel('home')).toBe('首页')
    expect(routeLabel('player')).toBe('玩家端')
    expect(routeLabel('storyteller')).toBe('说书人端')
  })
})
