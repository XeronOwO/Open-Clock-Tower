# 前端路由改用正常路径：地址从 `#/play` 变成 `/play`

- Status: Todo
- Priority: Medium（用户当面问"为什么要用 `#` 设计 path，不能用正常 path 吗"）
- Depends on: 无（服务端那一侧**已经就绪**，见下）

## 要解决的问题

地址栏里现在是 `http://<主机>/<前缀>/#/play` 这种形状。用户问得直接：**为什么不能用正常路径。**

## 现状事实（复核过，不是推测）

| 关注点 | 现状 | 对改造的含义 |
|---|---|---|
| 前端路由 | `web/src/display/navigation.ts`：`HOME_LINK = '#/home'` / `PLAY_LINK = '#/play'` / `STORYTELLER_LINK = '#/storyteller'`；`App.vue` 监听 `hashchange` | 改成读 `location.pathname` |
| **服务端 SPA 回退** | `Program.cs` 末尾已有 `app.MapFallbackToFile("{*path:nonfile}", "index.html")` | **正常路径今天就不会 404**，服务端与 nginx **都不用改** |
| 部署前缀 | 前端已有 `web/src/services/runtimeBase.ts`（`RUNTIME_BASE`，来自 vite 的 `BASE_URL`，已归一化） | 剥前缀不用新造轮子 |
| 旧行为（硬约束） | 空地址（空 hash）= 说书人端；`#player` = 玩家端 | 空路径天然还是说书人端 ✓；旧 hash 需要一次性重定向 |
| 依赖方 | 16 个装置约 20 处用 `#player`（`git grep -c "#player" -- tools`） | 靠重定向兜住，**装置可以一个字都不改** |

**为什么当初用 hash**：第一版这个站只有一个二选一开关（带 `player` 进玩家端，否则说书人端），
hash 最省事——井号后面的东西浏览器不发给服务器，不需要服务端配合。上一轮加导航时，
"空 hash 仍是说书人端、`#player` 仍进玩家端"被写成硬约束，于是顺着 hash 加了新前缀。

**hash 唯一站得住的理由**：点 `<a href="#/play">` 只换 hash，**文档不重载**，SignalR 长连接原封不动。
换正常路径后默认是整页跳转（重载 + 断连 + 重连），必须自己拦。

## 目标形态

- 地址：`<前缀>` = 说书人端（不变）、`<前缀>home` = 首页、`<前缀>play` = 加入一桌、`<前缀>storyteller` = 主持一局。
- 旧地址（`#player` / `#/play` / `#/home` / `#/storyteller`）**自动跳到新地址**，落点与今天一致。
- 点顶栏切面 **不重载文档**（连接不掉）。

## 要动什么

1. `display/navigation.ts`：链接常量与 `parseRoute` 改成读路径（用 `RUNTIME_BASE` 剥前缀），
   并保留一个"旧 hash → 新路径"的映射表。
2. `App.vue`：改监听 `popstate`；导航 `<a>` 加点击拦截（只接管左键无修饰键的普通点击，
   `target="_blank"` / 中键 / Ctrl 点击一律放行）。
3. 启动时若发现旧 hash：`history.replaceState` 换到新地址，**不产生历史记录**（用户按返回不该回到井号）。
4. 服务端、nginx、装置：**都不用改**。

## 验收

1. 打开 `<前缀>play` 直接进玩家端；在该页刷新仍在玩家端（这是 hash 与路径的分水岭）。
2. 打开旧的 `<前缀>#player`：自动落到 `<前缀>play`，且**地址栏里不再有井号**。
3. 空地址仍是说书人端（老红线）。
4. **切面不重载**：判据取"文档加载次数 / 连接标识在切面前后不变"（真跑一次，不能靠推理）。
5. 16 个装置跑 `#player` 的那 20 处**不改也能过**（重定向生效）。

## 残余

- 不做 SSR / 预渲染；不做"每个桌一个真路径"（那是 `entrance-redesign.md` 里"桌分享链接"的事，可以在这张票之后做）。
- 若将来要支持"直接粘某张桌的地址给朋友"，那时再给桌加真路径。

## 相关阅读

- 单 SPA 多面：`docs/decisions/active.md` D-0018
- 部署形态与前缀：`docs/operations/deploy.md` §2 / §4
- 入场重做（同一轮体验，但可独立做）：`docs/backlog/todo/entrance-redesign.md`
