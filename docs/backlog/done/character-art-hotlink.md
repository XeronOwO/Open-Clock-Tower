# 角色图热链：席位牌显示百科角色图，失败降级为文字 + 阵营色环

- Status: Done（2026-10-04）
- Priority: Medium
- Depends on: `docs/decisions/active.md` D-0007（热链方案）、`docs/standard/rulings.md` R-0006（可用性实测，本票补证据）

## 要解决的问题

说书人主视图（魔典席位牌）目前是纯文字 + CSS 几何（`storyteller-presentation.md` §4 素材行）；
但 D-0007 已选定「运行期热链百科图片」，该决策从未落地，R-0006（热链可用性实测）也因此没有执行对象
（界面零图片）。本票按决策补功能：席位牌显示角色图；图片加载失败时**不留白、不裂图**，
退回现有的「角色名 + 阵营色环」；并补 R-0006 的实测证据。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 已知角色 | 席位牌出现角色图（`<img>` 指向百科图片）且加载成功 | `tools/check-character-art.mjs` + 主装置截图 |
| 2 | 图片失败（404 / 断网 / 主机不可达） | 撤下 `<img>`，退回角色名 + 阵营色环；不裂图、不留布局空洞 | `tools/check-character-art.mjs` 阻断分支（真实组件） |
| 3 | 未知角色（跨剧本 / 未知 slug） | 不发起图片请求，文字原样回显（不猜） | 单元 / SSR 测试 |
| 4 | 热链可用性（R-0006） | 25 张全部 200 + image/png；localhost / 局域网两种来源在真浏览器可加载；带 / 不带 Referer 行为一致 | 探针日志 + R-0006 结果 |
| 5 | 不拖红装置 | 图片失败不产生 console / pageerror（只 `requestfailed`），装置断言不受第三方图床影响 | 侦察日志 + 主装置迭代档 |
| 6 | 门禁 | `npm run gate` + 三条 dotnet + 主装置迭代档全绿 | `artifacts/web/gates.log` / 装置日志 |

## 决定与依据

- 图片来源：百科角色页首行 `[[File:…]]`（`references/wiki/*.wiki`，2026-10-01 抓取快照）逐页提取；
  地址按 MediaWiki 文件布局规则由文件名派生：`/images/<md5(文件名)[0]>/<md5(文件名)[0:2]>/<文件名>`，
  25 条 2026-10-04 逐张实测 200 + image/png；
- `referrerpolicy="no-referrer"`：不把本机 / 局域网地址带给第三方站点；实测带本站 Referer 与不带均 200，
  无防盗链、无 CORS 要求（`<img>` 显示不需要 CORS）；
- 降级：`@error` 撤下 `<img>`，保留现有文字 + 色环渲染（D-0007 的强制对冲）；席位换手时重置失败态；
- 仓库仍不出现任何官方图片文件（D-0007 / `AGENTS.md`）；只提交文件名与地址映射（元数据）；
- 未知 slug 不请求图片（与 `labels.ts`「未知取值原样回显」同姿态）；
- 接入面只做**说书人魔典席位牌**：玩家端当前没有任何角色面（own character 不下发），不属本票。

## 结果（2026-10-04 · 真浏览器探针 + 主装置取证档 + 12 装置迭代档 + 门禁）

| # | 判据 | 实测 | 结论 |
|---|---|---|---|
| 1 | 已知角色 | 探针 A：真实组件在 Chromium 里加载 `Clockmaker.png`（naturalWidth > 0）；主装置取证档截图 `17-grimoire-assigned` / `23-grimoire-narrow` 人工复核有图、布局正常 | 通过 |
| 2 | 图片失败降级 | 探针 B：阻断百科主机 → `<img>` 撤下（img=0）、角色名「钟表匠」仍在；不裂图、不留白 | 通过 |
| 3 | 未知角色不猜 | 单元测试（未知 / 空 slug → null）+ SSR 测试（不渲染 `<img>`、文字原样回显）+ 探针 C（百科请求 = 0） | 通过 |
| 4 | 热链可用性（R-0006） | 25 张逐张 `200` + `image/png`；带 / 不带本站 Referer 均 200（无防盗链）；无 CORS 头但 `<img>` 不需要；探针 D：局域网 origin（`http://192.168.77.77:5391` 仿真）+ iPhone 13 上下文加载成功。**残余：真实手机确认**（见诚实记录） | 通过（含残余） |
| 5 | 不拖红装置 | 失败图片只触发 `requestfailed`、不进 console / pageerror（`img-console-probe.log`）；12 装置迭代档全绿（accounts 36 · witch 28 · madness 28 · mathematician 38 · setup-randomizer 58 · winloss 25 · retro-info 53 · seamstress-artist 82 · death-triggers 70 · character-change 84 · pit-hag 34 · 主装置 180 判定）+ 主装置取证档 **194 通过 / 失败 0 / 跳过 0** | 通过 |
| 6 | 门禁 | `dotnet build` 0 · `dotnet test` 0（**853**）· `dotnet format` 0 · `npm run gate` 0（vitest **159**，含新增 5 条） | 通过 |

诚实记录（范围与边界）：

- 接入面只有**说书人魔典席位牌**：玩家端当前没有角色面（own character 不下发），本票未给玩家侧加图；
- 仓库仍**无任何位图**：只提交文件名与地址映射（`web/src/display/character-art.ts`，含来源与派生规则注释）；
- 图片地址按 MediaWiki 的 `md5(文件名)` 目录规则派生；百科若挪路径 → 降级为文字 + 色环（可接受，D-0007 代价 1）；
- **残余**：真实手机在局域网内打开一次角色图 URL 的确认（自动侧已用移动端仿真 + 局域网 origin 覆盖；
  无真机设备）。确认口径：打开
  `https://clocktower-wiki.gstonegames.com/images/7/74/Clockmaker.png` 能看到钟表匠立绘即可；
- 主装置取证档为**冻结工作树**上的一次完整运行（`--quota 2 --screenshots-all`，2026-10-04），
  39 张截图本次写入；人工复核 `17-grimoire-assigned`（宽）与 `23-grimoire-narrow`（窄）两张；
- 证据日志：`artifacts/web/wiki-image-probe.log`、`character-art-probe.log`、`character-art-panel-evidence.log`、
  `character-art-summary.log`、`img-console-probe.log`、`gates.log`（均 gitignored）。
