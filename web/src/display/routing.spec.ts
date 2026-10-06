import { describe, expect, it } from 'vitest'
import { applyLegacyHash, decideLinkNavigation, subscribeRoute, type LinkIntent } from '@/display/routing'

/**
 * 路由的 DOM 那一侧：**旧地址改写**与**面变化订阅**。
 *
 * 这里用替身（假的 `location` / `history`）而不是真 DOM：判据是"地址被改成了什么、订阅被通知了几次"，
 * 与浏览器无关；同理 vitest 的默认环境是 node、装 jsdom 只为了测这两个循环没有意义。
 *
 * 真正的端到端判据（点顶栏**文档不重载**、旧地址落在正确的面）由真机装置给出——
 * 这一层只保证地址搬得对。
 */
interface FakeWindow {
  location: {
    origin: string
    pathname: string
    search: string
    hash: string
    href: string
    replace: (url: string) => void
  }
  history: { pushState: (data: unknown, title: string, url?: string | URL | null) => void }
  addEventListener: (type: string, listener: () => void) => void
  removeEventListener: (type: string, listener: () => void) => void
  dispatchEvent: (event: Event) => boolean
}

/** 假窗口：`location` 跟着 `history.pushState` / `location.replace` 真的变，否则测不出"搬地址"这件事。 */
function fakeWindow(initialPath = '/', initialHash = ''): FakeWindow {
  const listeners = new Map<string, Set<() => void>>()
  const origin = 'http://localhost:5273'
  /** 与浏览器同口径：接受绝对地址，也接受相对路径（按当前位置解析，井号一并丢掉）。 */
  const moveTo = (url: string): void => {
    const parsed = new URL(url, `${origin}${location.pathname}${location.search}${location.hash}`)
    location.pathname = parsed.pathname
    location.search = parsed.search
    location.hash = parsed.hash
    location.href = parsed.href
  }

  /** `location.replace('/play')` 会连井号一起去掉；替身必须同口径，否则测不出"地址栏里不再有井号"。 */
  const replace = (url: string): void => {
    moveTo(url)
    if (!url.includes('#')) {
      location.hash = ''
      location.href = `${origin}${location.pathname}${location.search}`
    }
  }

  const location: FakeWindow['location'] = {
    origin,
    pathname: initialPath,
    search: '',
    hash: initialHash,
    href: `${origin}${initialPath}${initialHash}`,
    replace,
  }

  return {
    location,
    history: {
      pushState: (_data, _title, url) => {
        if (url === undefined || url === null) {
          return
        }

        moveTo(String(url))
      },
    },
    addEventListener: (type, listener) => {
      const bucket = listeners.get(type) ?? new Set()
      bucket.add(listener)
      listeners.set(type, bucket)
    },
    removeEventListener: (type, listener) => {
      listeners.get(type)?.delete(listener)
    },
    dispatchEvent: (event) => {
      for (const listener of listeners.get(event.type) ?? []) {
        listener()
      }

      return true
    },
  }
}

function asWindow(window: FakeWindow): Window {
  return window as unknown as Window
}

function asLocation(window: FakeWindow): Location {
  return window.location as unknown as Location
}

describe('旧井号地址改写（在挂载前调用）', () => {
  it('根的旧写法：#player → /play，且不再带井号', () => {
    const window = fakeWindow('/', '#player')
    expect(applyLegacyHash(asLocation(window))).toBe(true)
    expect(window.location.pathname).toBe('/play')
    expect(window.location.hash).toBe('')
  })

  it('上一版的井号路径同样改写', () => {
    const window = fakeWindow('/', '#/home')
    expect(applyLegacyHash(asLocation(window))).toBe(true)
    expect(window.location.pathname).toBe('/home')
  })

  it('回放位置（带查询串的井号）也改写，且不把查询串带进地址栏', () => {
    const window = fakeWindow('/', '#player?replay=12')
    expect(applyLegacyHash(asLocation(window))).toBe(true)
    expect(window.location.pathname).toBe('/play')
    expect(window.location.search).toBe('')
  })

  it('已经在目标路径上、只是还挂着旧井号：去掉井号，不多推一次地址', () => {
    const window = fakeWindow('/play', '#player')
    expect(applyLegacyHash(asLocation(window))).toBe(true)
    expect(window.location.pathname).toBe('/play')
    expect(window.location.hash).toBe('')
  })

  it('说书人端的旧写法落在 `/` 上时不做整页跳转（别名已经是这一面）', () => {
    const window = fakeWindow('/', '#/storyteller')
    expect(applyLegacyHash(asLocation(window))).toBe(true)
    expect(window.location.pathname).toBe('/')
    expect(window.location.hash).toBe('')
  })

  it('新地址原样不动（不是旧井号就什么都不做）', () => {
    for (const [path, hash] of [['/', ''], ['/play', ''], ['/home', ''], ['/storyteller', '']] as const) {
      const window = fakeWindow(path, hash)
      expect(applyLegacyHash(asLocation(window))).toBe(false)
      expect(window.location.pathname).toBe(path)
    }
  })

  it('回放位置挂在井号上时不算旧地址，绝不能被抹掉', () => {
    const window = fakeWindow('/play', '?replay=7')
    expect(applyLegacyHash(asLocation(window))).toBe(false)
    expect(window.location.hash).toBe('?replay=7')
  })
})

describe('面变化订阅', () => {
  it('订阅时先播报一次当前面（initial=true），挂载时不会出现"空白面"', () => {
    const seen: [string, boolean][] = []
    const window = fakeWindow('/play')
    const off = subscribeRoute((route, initial) => seen.push([route, initial]), asWindow(window))
    off()
    expect(seen).toEqual([['player', true]])
  })

  it('前进 / 后退（popstate）与同文档改井号（hashchange）都播报', () => {
    const seen: string[] = []
    const window = fakeWindow('/')
    const off = subscribeRoute((route) => seen.push(route), asWindow(window))

    window.location.pathname = '/home'
    window.dispatchEvent(new Event('popstate'))
    window.location.pathname = '/play'
    window.dispatchEvent(new Event('hashchange'))
    off()

    expect(seen).toEqual(['storyteller', 'home', 'player'])
  })

  it('面没变就不重复播报（两个事件同时到达时不会白渲染一次）', () => {
    const seen: string[] = []
    const window = fakeWindow('/play')
    const off = subscribeRoute((route) => seen.push(route), asWindow(window))

    window.dispatchEvent(new Event('popstate'))
    window.dispatchEvent(new Event('hashchange'))
    off()

    expect(seen).toEqual(['player'])
  })

  it('同文档导航到旧井号地址：地址被改写成新路径，面跟着变', () => {
    const seen: string[] = []
    const window = fakeWindow('/play')
    const off = subscribeRoute((route) => seen.push(route), asWindow(window))

    // 浏览器对"只改井号"的导航不重载文档：地址变成 /play#... 后触发 hashchange。
    window.location.hash = '#player'
    window.dispatchEvent(new Event('hashchange'))
    off()

    expect(window.location.pathname).toBe('/play')
    expect(window.location.hash).toBe('')
    expect(seen).toEqual(['player'])
  })

  it('取消订阅之后不再播报（组件卸载后不该再改状态）', () => {
    const seen: string[] = []
    const window = fakeWindow('/')
    const off = subscribeRoute((route) => seen.push(route), asWindow(window))
    off()

    window.location.pathname = '/home'
    window.dispatchEvent(new Event('popstate'))

    expect(seen).toEqual(['storyteller'])
  })
})

describe('站内链接点击的处置（纯判定）', () => {
  const here = 'http://localhost:5273/'

  /** 默认是"在这个站点的根地址上、用普通左键点一个站内链接"。 */
  function intent(overrides: Partial<LinkIntent> = {}): LinkIntent {
    return { href: `${here}play`, target: '', download: false, currentUrl: here, plainLeftClick: true, ...overrides }
  }

  it('普通左键点站内链接：接管并换成目标路径（不重载文档）', () => {
    expect(decideLinkNavigation(intent())).toEqual({ action: 'push', path: '/play' })
    expect(decideLinkNavigation(intent({ href: `${here}home` }))).toEqual({ action: 'push', path: '/home' })
  })

  it('中键 / 修饰键点击一律放行给浏览器（用户要开新标签页）', () => {
    expect(decideLinkNavigation(intent({ plainLeftClick: false }))).toEqual({ action: 'pass' })
  })

  it('target 不是当前窗口、以及带 download 的链接放行', () => {
    expect(decideLinkNavigation(intent({ target: '_blank' }))).toEqual({ action: 'pass' })
    expect(decideLinkNavigation(intent({ download: true }))).toEqual({ action: 'pass' })
    expect(decideLinkNavigation(intent({ target: '_self' }))).toEqual({ action: 'push', path: '/play' })
  })

  it('外站链接放行（用户是想离开本站，不该被 SPA 吞掉）', () => {
    expect(decideLinkNavigation(intent({ href: 'https://example.com/play' }))).toEqual({ action: 'pass' })
  })

  it('重复点当前面：放行给浏览器（不堆历史记录）', () => {
    expect(decideLinkNavigation(intent({ href: `${here}play`, currentUrl: `${here}play` }))).toEqual({ action: 'pass' })
    expect(decideLinkNavigation(intent({ href: here, currentUrl: here }))).toEqual({ action: 'pass' })
  })

  it('切换到目标面时，与目标面无关的查询串 / 井号（如回放位置）被清掉', () => {
    const decision = decideLinkNavigation(
      intent({ href: `${here}home`, currentUrl: `${here}play?replay=12#?replay=12` }),
    )
    expect(decision).toEqual({ action: 'push', path: '/home' })
  })

  it('同一面点来点去不重复跳；从别的面切过去才接管，且落到该面的规范地址', () => {
    // 顶栏「主持一局」的 href 是 `/storyteller`，而 `/` 也是说书人端——两者是同一个面。
    expect(decideLinkNavigation(intent({ href: `${here}storyteller` }))).toEqual({ action: 'pass' })
    expect(decideLinkNavigation(intent({ href: here, currentUrl: `${here}storyteller` }))).toEqual({
      action: 'pass',
    })
    // 从玩家面切过去要接管；说书人端的规范地址只有一个（`/` 与 `/storyteller` 都归到它）。
    expect(decideLinkNavigation(intent({ href: here, currentUrl: `${here}play` }))).toEqual({
      action: 'push',
      path: '/storyteller',
    })
    expect(
      decideLinkNavigation(intent({ href: `${here}storyteller`, currentUrl: `${here}play` })),
    ).toEqual({ action: 'push', path: '/storyteller' })
  })
})
