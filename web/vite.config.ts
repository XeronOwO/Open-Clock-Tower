import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

/**
 * 开发服务器把 /hub 代理到本机 ASP.NET Core 宿主（默认 5080）。
 * 这样浏览器看到的始终是同源，不引入 CORS 配置，也不需要把票据放进 URL 之外的额外信任面。
 * 部署形态见 web/AGENTS.md。
 */
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5273,
    proxy: {
      '/hub': {
        target: process.env.VITE_SERVER_TARGET ?? 'http://localhost:5080',
        changeOrigin: true,
        ws: true,
      },
      '/healthz': {
        target: process.env.VITE_SERVER_TARGET ?? 'http://localhost:5080',
        changeOrigin: true,
      },
    },
  },
})
