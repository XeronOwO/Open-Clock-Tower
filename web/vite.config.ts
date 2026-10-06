import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
// 带 `.ts` 后缀：vite 的 configLoader 在未来 native 模式下要求后缀（无后缀会告警），
// `tsconfig.node.json` 已开 `allowImportingTsExtensions`，`tsc` 也接受。
import { resolveDeployBase } from './src/services/basePath.ts'

/**
 * 部署前缀只在这里解析一次：构建基址与开发代理**同源同函数**（`src/services/basePath.ts`），
 * 不设 `VITE_BASE_PATH` 时是 `/`，行为与历史版本完全一致。
 */
const base = resolveDeployBase(process.env.VITE_BASE_PATH)
const serverTarget = process.env.VITE_SERVER_TARGET ?? 'http://localhost:5080'
/** 前缀的"去掉尾巴"写法（`/` 或 `/clocktower`）：开发代理按它剥前缀，与生产 nginx 的转发规则同构。 */
const stripPrefix = base === '/' ? '' : base.slice(0, -1)

/** 开发代理：浏览器只连 Vite，`/hub` 与 `/healthz` 由它转给本机宿主（生产由 nginx 承担同一职责）。 */
function proxyFor(path: string, websocket = false) {
  return {
    target: serverTarget,
    changeOrigin: true,
    ws: websocket,
    rewrite: (url: string) => url.replace(`${stripPrefix}${path}`, path),
  }
}

export default defineConfig({
  plugins: [vue()],
  base,
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5273,
    proxy: {
      [`${stripPrefix}/hub`]: proxyFor('/hub', true),
      [`${stripPrefix}/healthz`]: proxyFor('/healthz'),
    },
  },
})
