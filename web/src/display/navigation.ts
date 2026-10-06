/**
 * 前端导航：URL hash → 当前面。
 *
 * 现状是"一个二选一开关"（带 `player` 进玩家端、其余一律说书人端），于是**没有导航**：
 * 玩家想切到说书人端只能手改地址栏，也没有首页。这里把它拆成四个面，但仍然保持单 SPA
 * （D-0004 / D-0018）。
 *
 * **兼容是硬约束**：18 个验收装置与用户手里的链接依赖既有行为——
 * 容器里的空 hash（`http://host/`）必须仍然是**说书人端**，`#player` 必须仍然是玩家端。
 * 所以旧写法一律保留，新面只用新前缀（`#/play`、`#/home`）。
 */

/** 面：首页 / 玩家端 / 说书人端。 */
export type AppRoute = 'home' | 'player' | 'storyteller'

/** 首页地址（说书人端保持"空 hash = 它"，以兼容既有装置与链接）。 */
export const HOME_LINK = '#/home'

/** 玩家端地址（旧的 `#player` 仍然有效，见下面的解析）。 */
export const PLAY_LINK = '#/play'

/** 说书人端地址。 */
export const STORYTELLER_LINK = '#/storyteller'

/**
 * 解析 URL hash 到当前面。
 *
 * 判据按优先级：新前缀 → 旧写法（`player` 关键词）→ **其余一律说书人端**（历史行为）。
 * 最后那条是刻意的：容器访问根路径（空 hash）必须落到说书人登录框，
 * 否则既有装置会在"等说书人面板出现"这一步卡死。
 */
export function parseRoute(hash: string): AppRoute {
  const normalized = hash.trim().toLowerCase()

  if (normalized.startsWith('#/play') || normalized.includes('player')) {
    return 'player'
  }

  if (normalized.startsWith('#/home')) {
    return 'home'
  }

  return 'storyteller'
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
