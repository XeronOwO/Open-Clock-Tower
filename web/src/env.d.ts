export {}

declare global {
  interface ImportMetaEnv {
    /**
     * 部署路径前缀（vite `base` 的读取口）。构建时用 `VITE_BASE_PATH=/clocktower/ npm run build` 指定；
     * 不设时为 `/`（根路径部署）。
     */
    readonly VITE_BASE_PATH?: string
    readonly VITE_SERVER_TARGET?: string
  }

  interface ImportMeta {
    readonly env: ImportMetaEnv
  }
}
