/**
 * 部署路径前缀的唯一口径（**纯函数模块**：不读环境、不依赖运行环境类型）。
 *
 * 构建期（`vite.config.ts`）与运行期（`runtimeBase.ts` → Hub 路径 / 健康检查）共用这两个函数，
 * 而不是两处各写一份归一化与拼接。一旦漂移，表现是"页面能打开、点加入却没反应"
 * （资源在一个前缀下、连接发到另一个前缀），这类故障排查成本极高，所以从结构上只留一份规则。
 *
 * 默认 `/`（根路径部署）：此时拼接结果与不带前缀的历史版本逐字节一致；
 * 只有显式指定时才带前缀，例如 `VITE_BASE_PATH=/clocktower/ npm run build`。
 */

/**
 * 把任意写法归一成 vite `base` 要的形式：**恒以 `/` 开头、恒以 `/` 结尾**。
 * 于是拼接后缀（`hub/game`、`healthz`）的调用方不必各自判断边界。
 * @param raw 原始写法（环境变量、构建期常量等）。
 */
export function resolveDeployBase(raw: string | undefined): string {
  const trimmed = (raw ?? '').trim()
  if (trimmed === '' || trimmed === '/') {
    return '/'
  }

  const leading = trimmed.startsWith('/') ? trimmed : `/${trimmed}`
  return leading.endsWith('/') ? leading : `${leading}/`
}

/**
 * 给根路径加部署前缀。
 * @param path 以 `/` 开头的根路径。
 * @param base 已归一化的前缀（`resolveDeployBase` 的结果）。
 */
export function withBase(path: string, base: string): string {
  return base === '/' ? path : `${base.slice(0, -1)}${path}`
}
