import { fileURLToPath, URL } from 'node:url'
import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  // 组件级渲染回归（如 `features/storyteller/seatDisplay.spec.ts`）需要 SFC 编译；
  // 生产构建的插件与这里同源（`@vitejs/plugin-vue` 已是 devDependency）。
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    environment: 'node',
    include: ['src/**/*.spec.ts'],
  },
})
