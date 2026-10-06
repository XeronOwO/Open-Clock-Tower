import { describe, expect, it } from 'vitest'
import {
  HOME_LINK,
  legacyHashTarget,
  normalizePath,
  PLAY_LINK,
  routeFromPath,
  routeLabel,
  routeState,
  STORYTELLER_LINK,
} from '@/display/navigation'

/**
 * 导航解析：四个面 + **两条兼容红线**。
 *
 * 这两条红线是真机装置与用户链接依赖的行为，改错任何一条都会让它们静默失灵
 * （装置会在"等说书人面板出现"那一步超时，而不是给出一条看得懂的失败）。
 *
 * 本文件只测**纯函数**（不读 `window`）；地址怎么被改写在 `routing.spec.ts`，
 * "点一下真的不重载"由真机装置判（`verify-entrance-usability.mjs` 的换面段）。
 */
describe('地址路径 → 面', () => {
  it('空地址是首页（门厅：先说清这是什么）', () => {
    expect(routeFromPath('/')).toBe('home')
    expect(routeFromPath('')).toBe('home')
  })

  it('四个面的地址', () => {
    expect(HOME_LINK).toBe('/home')
    expect(PLAY_LINK).toBe('/play')
    expect(STORYTELLER_LINK).toBe('/storyteller')
    expect(routeFromPath('/home')).toBe('home')
    expect(routeFromPath('/play')).toBe('player')
    expect(routeFromPath('/storyteller')).toBe('storyteller')
  })

  it('末尾斜杠、查询串都不影响判定（用户手打的地址什么形状都有）', () => {
    expect(routeFromPath('/play/')).toBe('player')
    expect(routeFromPath('/play?replay=3')).toBe('player')
    expect(routeFromPath('/home/')).toBe('home')
  })

  it('不认识的地址也落到首页（给门厅，比给一张报错式的登录卡好）', () => {
    expect(routeFromPath('/whatever')).toBe('home')
    expect(routeFromPath('/player')).toBe('home')
  })

  it('路径归一化：只去查询串与末尾斜杠，根路径不动', () => {
    expect(normalizePath('/play/')).toBe('/play')
    expect(normalizePath('/play?replay=3')).toBe('/play')
    expect(normalizePath('/')).toBe('/')
  })

  it('每一面都给出"地址栏里应有的路径"（顶栏高亮与旧地址落点都用它）', () => {
    expect(routeState('/')).toEqual({ route: 'home', canonicalPath: '/home' })
    expect(routeState('/home')).toEqual({ route: 'home', canonicalPath: '/home' })
    expect(routeState('/play')).toEqual({ route: 'player', canonicalPath: '/play' })
    expect(routeState('/storyteller')).toEqual({ route: 'storyteller', canonicalPath: '/storyteller' })
  })
})

describe('旧井号地址 → 面（兼容红线二）', () => {
  it('旧的 #player 仍然进玩家端', () => {
    expect(legacyHashTarget('#player')).toBe('player')
    expect(legacyHashTarget('#Player')).toBe('player')
  })

  it('上一版的井号路径写法同样认', () => {
    expect(legacyHashTarget('#/play')).toBe('player')
    expect(legacyHashTarget('#/home')).toBe('home')
    expect(legacyHashTarget('#/storyteller')).toBe('storyteller')
  })

  it('回放位置挂在井号查询串上，不能因为带问号就不认', () => {
    expect(legacyHashTarget('#player?replay=12')).toBe('player')
  })

  it('只认白名单里的确切写法：历史实现的子串匹配会把无关锚点也当成玩家端', () => {
    expect(legacyHashTarget('#player-notes')).toBeNull()
    expect(legacyHashTarget('#anything')).toBeNull()
    expect(legacyHashTarget('')).toBeNull()
    expect(legacyHashTarget('#')).toBeNull()
  })

  it('井号写法一律映射到四个面之一，没有"未知面"', () => {
    for (const hash of ['#player', '#/play', '#/home', '#/storyteller']) {
      expect(routeLabel(legacyHashTarget(hash) ?? 'storyteller')).not.toBe('')
    }
  })
})

describe('面的可读标题', () => {
  it('用界面口径（加入一桌 / 主持一局，D-0027），不是内部叫法', () => {
    expect(routeLabel('home')).toBe('首页')
    expect(routeLabel('player')).toBe('加入一桌')
    expect(routeLabel('storyteller')).toBe('主持一局')
  })
})
