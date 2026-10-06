import { createApp } from 'vue'
import App from '@/App.vue'
import '@/styles/global.css'
import { applyLegacyHash } from '@/display/routing'

// 旧井号地址（`#player` / `#/play` / `#/home` / `#/storyteller`）就地改写成新路径，
// **必须在挂载之前**：否则第一个渲染帧会按旧地址画错面，再被下面的订阅改回来（用户看见闪一下）。
// 就地改写不产生历史记录，用户按返回不会退回井号地址。
applyLegacyHash()

createApp(App).mount('#app')
