/**
 * 前端导航：URL 路径 → 当前面（**纯函数模块**：不读 `window`、不碰 `history`，DOM 那一侧在 `routing.ts`）。
 *
 * 地址形状在 2026-10-06 从 `#/play` 这种井号写法换成了正常路径（`/play`），原因是需求方问得直接：
 * "为什么要用 `#` 设计 path，不能用正常 path 吗"。井号唯一的优点是换面不重载文档，
 * 但那个优点换成路径之后由**点击拦截 + `history.pushState`** 同样拿到了（`routing.ts`），
 * 而正常路径对用户是看得懂、抄得走、贴得出去的地址。
 *
 * 单 SPA 多面这一点没变（D-0004 / D-0018）：四个面仍是同一份构建，由地址决定显示哪一个。
 *
 * **四个面各有各的地址，空地址是首页**：`<前缀>` = 首页 · `<前缀>play` = 加入一桌 ·
 * `<前缀>storyteller` = 主持一局。旧的 `#player` 这类井号写法仍然进得去（`legacyHashTarget`
 * 会把它们就地改写，见 `routing.ts`）——老链接不该失效，但新链接一律用正常路径。
 */
import { RUNTIME_BASE } from '@/services/runtimeBase'

/** 面：首页 / 玩家端 / 说书人端。 */
export type AppRoute = 'home' | 'player' | 'storyteller'

/**
 * 每个面的地址后缀（空串 = 该面的"根地址"）。
 *
 * 拼出来的地址按部署前缀分两种：根路径部署时是 `/play`，子路径部署（`/clocktower/`）时是
 * `/clocktower/play`——**不带末尾斜杠**，这样地址栏里是 `…/clocktower/play` 而不是 `…/play/`，
 * 与用户抄走、粘贴、分享的形态一致。
 */
const ROUTE_SEGMENTS: Record<AppRoute, string> = {
  home: 'home',
  player: 'play',
  storyteller: 'storyteller',
}

/** 拼接某一面在当前部署前缀下的**绝对路径**（给 `history.pushState` 与链接 `href` 共用）。 */
function pathOf(route: AppRoute): string {
  const prefix = RUNTIME_BASE === '/' ? '' : RUNTIME_BASE.slice(0, -1)
  return `${prefix}/${ROUTE_SEGMENTS[route]}`
}

/**
 * 某一面的**规范地址**（用户点进这一面之后地址栏里应有的形状）。
 *
 * 两个用途，都是"地址该长什么样"这一件事：顶栏链接的 `href`、以及旧井号地址改写后的落点
 * （`routing.ts`）。放在这里而不是各写一份，是因为它与 `routeState` 必须严格互逆——
 * 一份说"`/play` 是玩家端"、另一份说"玩家端该去 `/play`"，任一边改了另一边没改就会来回跳。
 */
export function pathOfRoute(route: AppRoute): string {
  return pathOf(route)
}

/**
 * 根地址 = 首页（需求方 2026-10-06 当面指出："`/clocktower` 进去是登录页，而不是变成 `/home` 页"）。
 *
 * 这一条改的是**根地址的含义**：以前空地址直接怼一张说书人登录卡，第一次打开站点的人不知道这是干什么的
 * （这正是"首页"当初被加进来的理由，只是当时没敢动根地址）。现在根地址就是门厅，
 * 说书人端有自己的地址 `/storyteller`——用户在顶栏点「主持一局」到这里。
 */
export const HOME_LINK = pathOf('home')

/** 玩家端地址（旧的 `#player` 仍然有效，由 `legacyHashTarget` 兜住）。 */
export const PLAY_LINK = pathOf('player')

/** 说书人端地址（**不再是空地址**：空地址是首页）。 */
export const STORYTELLER_LINK = pathOf('storyteller')

/**
 * 旧井号写法 → 面。
 *
 * 值 `storyteller` 与"不认识的值"落点相同，看起来冗余，但**含义不同**：
 * 前者是"这条旧地址明确指向说书人端"，后者是"历史兜底"。分开写是为了将来加面时不会误删这一条。
 */
const LEGACY_HASH_ROUTES: Record<string, AppRoute> = {
  '#player': 'player',
  '#/play': 'player',
  '#/home': 'home',
  '#/storyteller': 'storyteller',
}

/**
 * 旧井号地址要跳到哪一面；不是旧写法则返回 null。
 *
 * 判据刻意**不做子串匹配**：`#player` 在老代码里靠 `includes('player')` 命中，
 * 于是 `#player-notes` 这类无关锚点也会被当成玩家端。这里只认白名单里的确切写法。
 * @param hash 形如 `#player` 的锚点（可带尾随 `?query`，如回放位置）。
 */
export function legacyHashTarget(hash: string): AppRoute | null {
  const normalized = hash.trim().toLowerCase()
  const key = normalized.split('?')[0] ?? ''
  return LEGACY_HASH_ROUTES[key] ?? null
}

/**
 * 把地址里的路径归一化：去掉查询串与末尾斜杠，便于逐段比较。
 *
 * 导出给 `routing.ts` 用，让"当前路径"与"某一面的路径"两侧**同一把尺子**——
 * 两处各写一份的话，`/play/` 这种写法会在一边等于 `/play`、在另一边不等于，表现是点链接没反应。
 */
export function normalizePath(pathname: string): string {
  const withoutQuery = pathname.split('?')[0] ?? ''
  return withoutQuery.length > 1 && withoutQuery.endsWith('/')
    ? withoutQuery.slice(0, -1)
    : withoutQuery
}

/**
 * 解析**地址路径**到当前面。
 *
 * 判据：部署前缀剥掉之后与某一面的后缀**逐段比较**（表由 `ROUTE_SEGMENTS` 反查，不另写一份），
 * 其余统统落到**首页**——不认识的地址给门厅，比给一张"报错式"的登录卡好，
 * 也让"打开站点先看到这是什么"对任何写错的地址都成立。
 *
 * @param pathname 地址里的路径，如 `/`、`/play`、`/clocktower/storyteller`。
 */
export function routeFromPath(pathname: string): AppRoute {
  const path = normalizePath(pathname.length === 0 ? '/' : pathname)
  const prefix = RUNTIME_BASE === '/' ? '' : RUNTIME_BASE.slice(0, -1)

  // 前缀之外的路径不是本站的地址（理论到不了这里：应用本身就是从那里加载的），同样落到首页。
  if (prefix.length > 0 && !path.startsWith(prefix)) {
    return 'home'
  }

  const segment = path.slice(prefix.length)
  const matched = (Object.keys(ROUTE_SEGMENTS) as AppRoute[]).find(
    (route) => `/${ROUTE_SEGMENTS[route]}` === segment,
  )
  return matched ?? 'home'
}

/**
 * 当前位置解析成"面 + 该面在地址栏里应有的路径"。
 *
 * 两者一起返回，是因为 App 需要的正是这一对：`route` 决定渲染哪一个面，
 * `canonicalPath` 决定该面在地址栏里的形状（`/play/` 这类带末尾斜杠的写法也算同一面）。
 * 比较放在**规范化之后**，否则 `/play/` 会被判成"不在任何已知面上"。
 */
export function routeState(pathname: string): { route: AppRoute; canonicalPath: string } {
  const route = routeFromPath(pathname)
  return { route, canonicalPath: pathOf(route) }
}

/** 面 → 人话标题（顶栏显示当前位置）。界面上的词是「加入一桌 / 主持一局」，不是内部叫法（D-0027）。 */
export function routeLabel(route: AppRoute): string {
  switch (route) {
    case 'home':
      return '首页'
    case 'player':
      return '加入一桌'
    case 'storyteller':
      return '主持一局'
  }
}
