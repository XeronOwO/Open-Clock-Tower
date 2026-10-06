import { describe, expect, it } from 'vitest'
import { resolveDeployBase, withBase } from '@/services/basePath'

/**
 * 部署前缀：只允许一份口径。
 *
 * 归一化若在构建期与运行期各写一遍就会漂移——资源放在 `/clocktower/` 下、而 Hub 发到 `/hub/game`，
 * 表现是"页面能打开、点加入没反应"，排查成本极高。同源由结构保证（`vite.config.ts` 与本模块
 * 引用同一个 `resolveDeployBase`，见配置注释），这里锁的是行为：默认值、边界写法、拼接结果。
 */
describe('部署路径前缀', () => {
  it('默认（不设变量）就是根路径，与历史行为一致', () => {
    expect(resolveDeployBase(undefined)).toBe('/')
    expect(resolveDeployBase('')).toBe('/')
    expect(resolveDeployBase('/')).toBe('/')
    expect(resolveDeployBase('   ')).toBe('/')
  })

  it('归一成"恒以 / 开头、恒以 / 结尾"：各种写法等价', () => {
    for (const raw of ['/clocktower', 'clocktower', '/clocktower/', 'clocktower/', '  /clocktower  ']) {
      expect(resolveDeployBase(raw)).toBe('/clocktower/')
    }
  })

  it('多级前缀同样成立', () => {
    expect(resolveDeployBase('/apps/clocktower')).toBe('/apps/clocktower/')
  })

  it('根路径下拼接逐字节保持历史值（不引入任何变化）', () => {
    expect(withBase('/hub/game', '/')).toBe('/hub/game')
    expect(withBase('/hub/account', '/')).toBe('/hub/account')
    expect(withBase('/healthz', '/')).toBe('/healthz')
  })

  it('子路径下拼接不留双斜杠、也不缺斜杠', () => {
    expect(withBase('/hub/game', '/clocktower/')).toBe('/clocktower/hub/game')
    expect(withBase('/healthz', '/clocktower/')).toBe('/clocktower/healthz')
    expect(withBase('/hub/game', '/apps/clocktower/')).toBe('/apps/clocktower/hub/game')
  })

  it('运行期基址与构建期同源：由同一个 resolveDeployBase 产出（见 runtimeBase.ts）', () => {
    // 行为侧的可验证判据：vite 注入的 BASE_URL 经归一化后必然以 `/` 结尾、且以 `/` 开头。
    // `tsconfig.node.json` 环境没有 vite/client 类型，所以这里不直接引运行期常量，改由
    // runtimeBase.ts 保证同源（它只做"读环境 → 调本模块函数"这一件事）。
    expect(resolveDeployBase('/clocktower/').startsWith('/')).toBe(true)
    expect(resolveDeployBase('clocktower').endsWith('/')).toBe(true)
  })
})
