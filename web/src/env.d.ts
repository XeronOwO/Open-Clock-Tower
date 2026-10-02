export {}

declare global {
  interface ImportMetaEnv {
    readonly VITE_SEAT_COUNT?: string
    readonly VITE_SERVER_TARGET?: string
  }

  interface ImportMeta {
    readonly env: ImportMetaEnv
  }
}
