import { createApp } from 'vue'
import App from '@/App.vue'
import '@/styles/global.css'
import { applyLegacyHash } from '@/display/routing'
import { restore } from '@/services/accountSession'

// 旧井号地址（`#player` / `#/play` / `#/home` / `#/storyteller`）就地改写成新路径，
// **必须在挂载之前**：否则第一个渲染帧会按旧地址画错面，再被下面的订阅改回来（用户看见闪一下）。
// 就地改写不产生历史记录，用户按返回不会退回井号地址。
applyLegacyHash()

createApp(App).mount('#app')

// 挂载**之后**再去确认持久化凭据（M1 / D-0029）：先画界面再连服务端，
// 服务端不可达时既不白屏，也不会把"还没确认"错当成"未登录"——那一态由 `AccountGate` 单独表达。
void restore()
