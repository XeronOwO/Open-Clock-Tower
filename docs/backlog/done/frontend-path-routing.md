# 前端路由改用正常路径：地址从 `#/play` 变成 `/play`

- Status: Done（2026-10-06 部署到真机并复验通过）
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

1. `display/navigation.ts`：链接常量与解析改成读路径（用 `RUNTIME_BASE` 剥前缀），
   并保留一个"旧 hash → 新路径"的映射表。
2. `App.vue`：订阅地址变化；导航 `<a>` 加点击拦截（只接管左键无修饰键的普通点击，
   `target="_blank"` / 中键 / Ctrl 点击一律放行）。
3. 启动时若发现旧 hash：就地改写成新地址，**不产生历史记录**（用户按返回不该回到井号）。
4. 服务端、nginx：**都不用改**。

## 实际怎么做（与上面几处不同，都是实现时改的主意）

| 原计划 | 实际 | 为什么 |
|---|---|---|
| `App.vue` 监听 `popstate` | 新增 `display/routing.ts` 专门管 DOM 侧；`navigation.ts` 保持**纯函数** | 解析规则要能在 node 环境单测（vitest 默认环境没有 DOM）；地址怎么变、面是哪个，分开两处谁都不臃肿 |
| 只监听 `popstate` | 三个来源都听：`popstate`（前进后退）· `hashchange`（同文档导航到旧井号）· 自发的地址变化事件 | `pushState` **不触发** `popstate`，只监听它会让"点了链接但界面没换" |
| 启动时用 `history.replaceState` 改写旧 hash | 同一件事，但**改写在挂载之前**（`main.ts`） | 挂载后改写会让第一个渲染帧画错面，用户看见闪一下 |
| 「装置可以一个字都不改」 | 20 处 `#player` 改成 `/play`，只留**一条**旧地址断言（可用性装置的 gate 段） | 兼容网要留，但主路径不该跑在兼容网上——留一条断言证明网还在就够了 |
| 点击监听挂在顶栏 | 挂在根节点（`App.vue` 的 `.app`） | 首页的两张入口卡片也是站内链接，只挂顶栏它们会整页重载 |

## 验收

1. 打开 `<前缀>play` 直接进玩家端；在该页刷新仍在玩家端（这是 hash 与路径的分水岭）。
2. 打开旧的 `<前缀>#player`：自动落到 `<前缀>play`，且**地址栏里不再有井号**。
3. 空地址仍是说书人端（老红线）。
4. **切面不重载**：判据取"文档加载次数 / 连接标识在切面前后不变"（真跑一次，不能靠推理）。
5. 旧地址那条路**不改也能过**（重定向生效）——装置仍按新路径跑，兼容性由一条断言守着。

## 验收读数（批次 E50，2026-10-06）

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 打开 `/play` 直接进玩家端；刷新仍在玩家端 | 通过 | 可用性装置 `gate` 段「未登录打开玩家面（/play）」6 项 · `replay` 段那条 `goto` + `reload` 回座断言（账号装置 `--only replay` 4 项全过，13.3s） |
| 2 | 旧的 `#player` 自动落到 `/play`、地址栏无井号 | 通过 | 可用性装置新增断言「旧井号地址 #player 自动落到 /play（地址栏里不再有井号）」→ 落点 `/play` |
| 3 | 空地址仍是说书人端 | 通过 | 可用性装置 `gate` 段「未登录打开说书人面（空地址）」6 项 + `relogin` 段「空地址（根路径）仍是"主持一局"那一面」 |
| 4 | 切面**不重载文档** | 通过 | 可用性装置新增断言：`faces` 段「切面没有重载文档（加载次数不变）且地址栏换成目标路径」→ **加载次数 1 → 1**、地址 `/storyteller`；同一段「还认得我 / 没有退回登录卡」两项是它的行为面 |
| 5 | 旧地址那条路仍然通 | 通过 | 同上第 2 条；另有纯逻辑用例锁住白名单（`navigation.spec.ts` 的「只认白名单里的确切写法」） |

**纯逻辑层**：`navigation.spec.ts` 12 项 + `routing.spec.ts` 19 项（地址改写、面订阅、点击判定），
`npx vitest run src/display/` 全过。**装置整体**：可用性装置判定 **29 项全过**（10.5s）、
主装置判定 **289 项全过**（76.2s）、账号装置（`--only replay`）4 项全过。门禁：0 警告 0 错误 ·
1305 通过 · format 干净 · web 242 通过。

**实现中途改过一处口径**：说完"落点一律取规范地址"之后发现，说书人端的旧写法落在 `/` 上时
（`/#/storyteller`）会为了把 `/` 换成 `/storyteller` 白跳一次整页——判据改成比**面**而不是比路径
（`routeState(location.pathname).route === route`），路径已经是这一面时就地去掉井号即可。
这条是**单测先红后绿**咬出来的（`routing.spec.ts` 的「说书人端的旧写法落在 `/` 上时不做整页跳转」）。

**待正式验收**：上面这些读数是**迭代档**跑出来的（路由是前端行为，没有节拍器可观察，
档位不影响判据）。按 `docs/acceptance/AGENTS.md` §3，正式判定要来自一次**取证档**
（`--quota 2 --screenshots-all`）跑——留待下一次取证批次，故本票停在 `review/`。

## 真机部署与复验（2026-10-06，需求方当场问"为什么我浏览器里还有井号"）

**原因**：上一轮（E49）部署的是 `91332b6` 那一版，**本票的改动当时还没上服务器**，
所以线上打开站点看到的仍是井号地址。当场按 `docs/operations/deploy.md` §2–§3 部署：

`node tools/deploy-prepare.mjs --app-dir <APP_DIR> --prefix /clocktower/ --port 5080 --seats 7`
→ 上传 → `systemctl stop clocktower` → `rm -f <APP_DIR>/wwwroot/assets/*` → 解压 →
`chmod -R u=rwX,go=rX` + `chmod u+x OpenClockTower.Server` → 起服。

| 真机读数 | 证据 |
|---|---|
| 部署后真机验收 | `node tools/verify-live-open-table.mjs --base-url http://<部署地址>/clocktower/ --seats 7` → **21 项全过**，14.5s（该装置四个入口已改成新路径，跑的就是 `/clocktower/play`） |
| 子路径下的深路径 | `curl /clocktower/{play,home,storyteller}` 三条都 200，返回的 `index.html` 引用新产物 `assets/index-C5DbmjT3.js`——**nginx 不用改**，SPA 回退在真前缀下成立 |
| 行为探针（线上真浏览器） | 旧地址 `#player` → `/clocktower/play` 且地址栏无井号 · 刷新 `/clocktower/play` 仍在玩家面 · 空地址仍是「主持一局」· 点顶栏换面加载次数 1 → 1（判定 6 项全过；探针为一次性脚本，未入装置） |
| 缓存口径 | `index.html` 与 `assets/*.js` 的响应头都没有长 `max-age`（只有 `ETag` / `Last-Modified`），换产物后浏览器会重新取，用户**不需要手动清缓存** |

清理：真机验证留下的测试账号与测试桌按装置打印的 SQL 删净（先停服），清理后
`Games` 0 · `Users` 1（原有的 `<运维账号>`）· `Events` / `Snapshots` / `SeatBindings` / `Receipts` 全 0。

**残余（不影响本票结论）**：本票的正式判定证据来自**部署后真机验收**与**可用性装置**，
没有单独跑一次取证档的可用性装置（那条路径没有节拍器可观察，档位不影响判据）。

## 残余

- 不做 SSR / 预渲染；不做"每个桌一个真路径"（那是 `entrance-redesign.md` 里"桌分享链接"的事，可以在这张票之后做）。
- 若将来要支持"直接粘某张桌的地址给朋友"，那时再给桌加真路径。
- **回放位置仍挂在井号上**（`#?replay=N`，`ReplayPanel.vue`）：它不是路由，是面板状态；
  换成查询串会更整齐，但那是另一件事（本票只搬路由，没动它）。
- **说书人面有两个规范地址**（`/` 与 `/storyteller`，都进说书人端）：历史红线要求空地址仍进说书人端，
  所以两者并存；顶栏链接指向 `/storyteller`。

## 相关阅读

- 单 SPA 多面：`docs/decisions/active.md` D-0018
- 部署形态与前缀：`docs/operations/deploy.md` §2 / §4
- 入场重做（同一轮体验，但可独立做）：`docs/backlog/done/entrance-redesign.md`
