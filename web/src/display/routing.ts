/**
 * 前端路由的 **DOM 那一侧**（解析规则在 `navigation.ts`，那边是纯函数）。
 *
 * 换成正常路径之后，浏览器默认的 `<a href="/play">` 是**整页跳转**：文档重载、SignalR 长连接断掉、
 * 账号会话（只活在内存里，D-0021）一起没了。所以这里做三件事，缺一不可：
 *
 * 1. **点击拦截**：只接管"左键 + 无修饰键 + 同一站点 + 没有 `target`"的普通点击，
 *    换成 `history.pushState` —— 文档不重载，连接与会话原封不动。
 *    中键 / Ctrl / Cmd / Shift / Alt 点击、`target="_blank"`、外站链接一律放行给浏览器。
 * 2. **监听地址变化**：`popstate`（前进 / 后退）与 `hashchange`（旧井号地址被同文档导航过来）。
 * 3. **旧井号地址改写**：`#player` 这类旧地址一次性换成新路径，见 `applyLegacyHash`。
 *
 * 这里不读取也不推导"当前是哪个面"——那是 `navigation.ts` 的事，本模块只搬地址。
 */
import { legacyHashTarget, normalizePath, pathOfRoute, routeState, type AppRoute } from '@/display/navigation'

/** 监听项的卸载函数。 */
export type Unsubscribe = () => void

/**
 * 程序化改地址后补发的事件名。
 *
 * 刻意**不用 `popstate`**：那是浏览器自己的事件，借它当内部信号会在日后接入真路由库时
 * 变成"谁在发这个事件"的谜题（真 `popstate` 与自造 `popstate` 混在一起，没法区分）。
 */
const LOCATION_CHANGED_EVENT = 'oct:locationchange'

/**
 * 旧井号地址 → 新路径，**不产生历史记录**（`Location.replace` 替换当前历史项）。
 *
 * 这里用的是 `location.replace`（整页跳转），不是 `history.replaceState`：地址改写的对象是
 * **路径**，而路径一变，文档里那些**相对地址**（`assets/index-*.js` 这类）就会按新路径解析，
 * 不重载就等于拿旧解析结果跑在新地址上。整页跳转把这一整类问题一起消掉，
 * 代价只是"用旧链接进来多加载一次"——旧链接只该出现在收藏夹与老装置里，不该出现在主路径上。
 *
 * `replace` 而不是 `assign`：用户按返回不该退回到那个井号地址。
 *
 * 另外它让"先改地址、再渲染"成为顺序确定的动作：调用方（`main.ts`）在挂载前调用它，
 * `#player` 于是从第一个渲染帧起就是玩家端，不会先闪一下说书人端。
 *
 * @param location 当前地址（默认真的地址栏；测试可注入替身）。
 * @returns 改写了就返回 `true`；不是旧地址、或已经在目标路径上则返回 `false`。
 */
export function applyLegacyHash(location: Location = window.location): boolean {
  const route = legacyHashTarget(location.hash)
  if (route === null) {
    return false
  }

  // 落点取"这一面的规范地址"：`#player` 落到 `/play`、`#/storyteller` 落到 `/storyteller`。
  const canonical = pathOfRoute(route)
  // 当前地址已经**就是**这一面时（比如带着井号的 `/play`），只在原地把井号去掉：
  // 整页跳转一次只为改一个井号不值得。
  // 判据用**面**而不是路径——路径可以有多条等价写法（`/play/` 带末尾斜杠），面只有一个。
  const sameRoute = routeState(location.pathname).route === route
  const destination = sameRoute ? location.pathname : canonical

  // 这个比较就是旧代码里的 `page.goto` 陷阱：**同一个 URL 再 replace 一次是空操作**；
  // 反过来说，判据里必须把"地址里还挂着旧井号"也算成"需要改写"（否则 `#player` 永远留在地址栏里）。
  if (`${location.pathname}${location.search}${location.hash}` === destination) {
    return false
  }

  // 传**路径**而不是整条地址：`Location.replace` 接受相对 URL 并按当前位置解析，
  // 于是这里不需要知道站点挂在根路径还是子路径（那由 `navigation.ts` 统一给出）。
  location.replace(destination)
  return true
}

/**
 * 订阅"地址对应的面"变化，返回取消订阅的函数。
 *
 * 回调签名是 `(route, initial)`：`initial` 为 `true` 表示这是订阅时立刻播报的当前值，
 * 调用方据此区分"首次对齐"与"用户真的换了面"。
 *
 * 三个事件都听：`popstate` 管前进 / 后退，`hashchange` 管"同一个文档里地址被改成旧井号写法"
 * （比如装置从 `/play` 同文档导航到 `/#player`），`oct:locationchange` 是本站自己点链接时补发的
 * （`pushState` 不触发 `popstate`）。面没变就不播报，所以三个事件同时到达也不会白渲染。
 *
 * @param listener 面变化时的回调。
 * @param target 监听对象（默认浏览器窗口；测试可注入替身）。
 */
export function subscribeRoute(
  listener: (route: AppRoute, initial: boolean) => void,
  target: Window = window,
): Unsubscribe {
  let current: AppRoute | null = null

  const notify = (initial: boolean): void => {
    const { route } = routeState(target.location.pathname)
    if (initial || route !== current) {
      current = route
      listener(route, initial)
    }
  }

  const onLocationChange = (): void => {
    // 同文档导航到旧井号地址时，先把地址改写成新路径，再播报面。
    applyLegacyHash(target.location)
    notify(false)
  }

  target.addEventListener('popstate', onLocationChange)
  target.addEventListener('hashchange', onLocationChange)
  target.addEventListener(LOCATION_CHANGED_EVENT, onLocationChange)
  notify(true)

  return () => {
    target.removeEventListener('popstate', onLocationChange)
    target.removeEventListener('hashchange', onLocationChange)
    target.removeEventListener(LOCATION_CHANGED_EVENT, onLocationChange)
  }
}

/** 一次链接点击里，判定真正用到的那些事实（DOM 与事件的提取在 `interceptLinkClick`）。 */
export interface LinkIntent {
  /** 链接的绝对地址（`HTMLAnchorElement.href`；相对写法已被浏览器解析过）。 */
  href: string
  /** `target` 属性；空串表示没写，`_self` 与空串等价。 */
  target: string
  /** 是否带 `download` 属性。 */
  download: boolean
  /** 当前地址（用来判"是不是同一站点"）。 */
  currentUrl: string
  /** 点击是不是"普通左键"（无修饰键、未被别人处理过）。 */
  plainLeftClick: boolean
}

/** 一次链接点击的处置：`push` = 接管并改地址，`pass` = 交给浏览器。 */
export type LinkDecision = { action: 'push'; path: string } | { action: 'pass' }

/**
 * 判定一次站内链接点击该不该被接管（**纯函数**：不读 `window`、不碰 `history`）。
 *
 * 放行（`pass`）的情形：非左键 / 带修饰键、`target` 不是当前窗口、带 `download`、跨站点。
 * 地址本来就对（路径、查询串、井号三者都一样）时也放行——重复点同一面不该堆历史记录。
 *
 * 接管时会顺带**清掉与目标面无关的查询串 / 井号**（比如回放位置）：换面之后那些状态不再成立，
 * 留在地址栏里就会在下一次刷新时被当成"要看这一面的回放"。
 */
export function decideLinkNavigation(intent: LinkIntent): LinkDecision {
  if (!intent.plainLeftClick || intent.download) {
    return { action: 'pass' }
  }

  // 新窗口 / 新标签页是用户的明确意图，浏览器怎么做就怎么做。
  if (intent.target !== '' && intent.target !== '_self') {
    return { action: 'pass' }
  }

  const destination = parseInSiteUrl(intent.href, intent.currentUrl)
  if (destination === null) {
    return { action: 'pass' }
  }

  const current = new URL(intent.currentUrl)
  const sameRoute = routeState(destination.pathname).route === routeState(current.pathname).route
  const sameAddress =
    normalizePath(destination.pathname) === normalizePath(current.pathname) &&
    destination.search === current.search &&
    destination.hash === current.hash
  // 同一面不重复跳（`/` 与 `/storyteller` 都是说书人端，点来点去不该堆历史记录）；
  // 地址逐字相同更没什么可做的。
  if (sameRoute || sameAddress) {
    return { action: 'pass' }
  }

  const { canonicalPath } = routeState(destination.pathname)
  return { action: 'push', path: `${canonicalPath}${destination.search}${destination.hash}` }
}

/**
 * 整页接管站内链接点击：命中就 `pushState` 换地址并补发地址变化事件，不重载文档。
 *
 * 判定本身在 `decideLinkNavigation`（纯函数，单测在那边）；这里只负责从事件里把事实取出来、
 * 以及真的改地址。改完**不自己回调调用方**——补发一个事件，让订阅者照常按地址重算。
 *
 * @param event 点击事件。
 * @param target 判定与改地址用的窗口（默认浏览器窗口；测试可注入替身）。
 * @returns 是否已接管（接管了就已经 `preventDefault`）。
 */
export function interceptLinkClick(event: MouseEvent, target: Window = window): boolean {
  const anchor = anchorOf(event.target)
  if (anchor === null) {
    return false
  }

  const decision = decideLinkNavigation({
    href: anchor.href,
    target: anchor.target,
    download: anchor.hasAttribute('download'),
    currentUrl: target.location.href,
    plainLeftClick: isPlainLeftClick(event),
  })
  if (decision.action === 'pass') {
    return false
  }

  target.history.pushState(null, '', decision.path)
  event.preventDefault()

  // `pushState` **不会**触发 `popstate`（浏览器只把它当"写了一条历史记录"），所以这里补发一次。
  target.dispatchEvent(new Event(LOCATION_CHANGED_EVENT))
  return true
}

/** 是否"普通左键点击"（用户没有要求开新窗口 / 新标签页）。 */
function isPlainLeftClick(event: PlainClick): boolean {
  return (
    event.button === 0 &&
    !event.defaultPrevented &&
    !event.metaKey &&
    !event.ctrlKey &&
    !event.shiftKey &&
    !event.altKey
  )
}

/**
 * 判定"是不是普通点击"真正用到的那几个字段。
 *
 * 按结构取字段而不是要求整个 `MouseEvent`：这样这条判定能在 Node 环境（vitest 默认环境，没有 DOM）
 * 里被直接测到，而不必为了四个布尔值搭一个假 DOM。
 */
interface PlainClick {
  button: number
  defaultPrevented: boolean
  metaKey: boolean
  ctrlKey: boolean
  shiftKey: boolean
  altKey: boolean
}

/** 从事件目标往上找最近的 `<a href>`；纯元素（SVG 图标等）也能正确冒泡到它。 */
function anchorOf(node: EventTarget | null): HTMLAnchorElement | null {
  return node instanceof Element ? node.closest('a[href]') : null
}

/**
 * 把链接目标解析成"站内"地址：跨站点返回 null（交给浏览器，是用户想离开本站）。
 *
 * 相对链接（`assets/…`、`foo`）也在这里被解析成绝对地址——但同时返回 `origin`，
 * 判定用的是解析后的 `origin` 与 `pathname`，所以相对链接不会因为当前路径深度而误判。
 */
function parseInSiteUrl(href: string, currentUrl: string): URL | null {
  try {
    const destination = new URL(href, currentUrl)
    const here = new URL(currentUrl)
    return destination.origin === here.origin ? destination : null
  } catch {
    return null
  }
}
