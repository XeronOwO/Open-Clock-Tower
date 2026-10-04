# 待办（Backlog）

一个工作项 = 一个文件。**移动票据到另一个状态目录，就是状态流转**。

```text
todo/  →  in-progress/  →  review/  →  done/
                              ↓
                          future/     延后 / 低优先级 / 未来架构
                          resolved/   已决策，无需改代码
                          watchlist/  可观测性 / 架构看护项
```

## 规则

- **一个工作项 = 一个文件。**
- **下面的索引是指针表，不是第二份摘要。** 一行的组成是
  `- [标题](路径) — **优先级** — 一句话`：那一句说的是这个工作项**是什么**，
  永远不是它的历史。所有事实（决定、日期、计数、阶段进度、结论）都活在票据文件里。
  一行的**章节名就是它的状态**，所以行里不重复写状态。
- 优先级取自票据自己的 `- Priority:` 字段（第一个词）。闭环的存档记录不写优先级。
- 索引里出现"超长行、优先级与票据不一致、票据漏登记、挂在错误章节下"，都算缺陷。
- **代码完成即移入 `review/`**，review 是等待**验收批次**的等待态，不是"看起来还行"。
  代码审查通过和测试全绿都**不算**验收。
- 验收通过的记录把票据移到 `done/`；某一行不通过就移回 `todo/`，并在票据里标出被拒的行；
  缺依赖则留在 `review/` 并写明缺的是什么——**等待是诚实的状态，含糊不是**。

## 状态目录

| 目录 | 含义 |
|---|---|
| `todo/` | 未开始 |
| `in-progress/` | 正在做 |
| `review/` | 代码完成，等待验收批次 |
| `done/` | 已落地 / 已关闭 |
| `future/` | 延后或未来架构 |
| `resolved/` | 已决策，无需改代码 |
| `watchlist/` | 可维护性 / 架构看护 |

## 票据模板

```markdown
# 标题

- Status: Todo
- Priority: High
- Depends on: <票据或"无">

## 要解决的问题
（用户视角：现在发生了什么坏事）

## 验收矩阵
| # | 场景 | 期望 | 证据 |
|---|---|---|---|

## 决定与依据
（规则的指引用 页名 + 抓取日期；不确定的登记 rulings.md 编号）
```

## 索引

### Todo

- [夜晚顺序默认值对齐：命令层默认改为官方魔典顺序](todo/night-order-variant-default.md) — **Low** — 面板已默认 Recommended（= 官方魔典顺序，R-0014 已定案）；只剩命令层 `StartNightCommand.Variant` 默认值仍是 Original
- [印刷规则书原件回核：R-0005 / R-0012 / R-0013 / R-0018 引文（R-0029 顺带）](todo/rulebook-original-crosscheck.md) — **Medium** — 三条裁定依第三方逐字提取文本收口，官方原件不在手；拿到实物后回核引文
- [文档日期偏移核查](todo/doc-date-offset-audit.md) — **Low** — 多处记录写 2026-10-05（E31/E32 等），git 时间戳与外部服务器时间均为 2026-10-04；需统一核查

### In progress

### Review

### Done

- [旅行者与流放流程（首版纳入）](done/traveller-and-exile.md) — **High** — 《梦殒春宵》5 名旅行者（怪咖 / 集骨者 / 咖啡师 / 流莺 / 屠夫）+ 流放流程 + 加入 / 离开；范围决策 D-0022；批次 E34 判出矩阵 13 行全过（主装置取证档 284 项 + 零信任 51；冻结版门禁 1076 通过）

- [钟盘投票形态：设计与实现](done/clock-vote-flow.md) — R-0017 目标形态落地：内核逐席严格时点收票 + 控制面时间轴 + 两端钟盘；批次 E33 判出 7 行验收矩阵全过（主装置取证档 202 项；winloss 28 / witch 28 / death-triggers 73 / retro-info 56；门禁 861 + 前端 163）

- [角色图热链：席位牌显示百科角色图，失败降级为文字 + 阵营色环](done/character-art-hotlink.md) — **Medium** — 席位牌接入 25 人百科角色图（运行期热链、仓库无位图）；失败撤图降级、未知不请求；R-0006 实测收口为 Decided；主装置取证档 194 通过、12 装置迭代档全绿

- [装置属性读取未收口：轮询/守卫式 `getAttribute` 仍可能吃满 30s](done/device-attribute-poll-unbounded-wait.md) — **Low** — 12 个装置的 `waitForAttribute` / `read*Count` / 日状态 / 标记 `title` 等 37 处守卫式属性读取统一改走 `readAttributeBounded`（单次 500ms），元素脱离不再白等 30s

- [装置守卫式读取未收口：条件读取仍可能吃满 30s](done/device-guarded-reads-unbounded-wait.md) — **Low** — 11 个装置的 `readDecisionText` / `panelText` / `readSlotContext` / `readTextOrNull` 等守卫式文本读取统一改走 `readTextBounded`（40 处），读取期间脱离不再吃 30s

- [装置轮询读文本必须有界：元素缺失时不再白等 30s](done/device-poll-innertext-unbounded-wait.md) — **Low** — 十二个装置的 `waitForText` / `waitForLocatorContains` / `waitForLocatorText` / `waitForSeedChange` 统一改走 `tools/lib/bounded-text.mjs` 的有界读取（单次 500ms），并落一个假页面最小复现脚本 `tools/check-bounded-text.mjs`

- [玩家信息行读法跟进 E31：三装置改读可见标签](done/player-information-row-readers-stale.md) — **Medium** — `verify-retro-info` / `verify-death-triggers` / `verify-seamstress-artist` 从已退役的 `.mono` / `span[1]` 结构改读「`<strong>` 中文名（slug）标签 + 内容」；修复前该三装置在 `e6917db` 基线已红，修复后 53 / 82 / 70 全绿

- [账号装置分段选择器：把 37–40s 的固定开销拆开定位](done/accounts-device-section-selector.md) — **Medium** — 账号装置接上 `--only` / `--from` / `--list-sections`（12 段、每次运行打印按段耗时）；分段当场咬出 guest 段一处无界 `innerText()` 白等满 30s——迭代档 **37.3s → 7.3s**，36 项回归全绿（同族 12 个装置的同款写法另立 Low 票）

- [排版与上手引导优化：让版块自解释、信息降密度](done/ui-layout-and-onboarding.md) — **Medium** — 两端版块标题 + 一句副标题、`?` 说明入口（悬停 / 点按 / Esc）、通俗文案与「席位 + 角色 + 状态 + 归因」拼接句、低频区块折叠；同批收口 E30 残余①（说书人数据抽屉八个组件接入玩家名口径）；批次 E31 矩阵 7 行全部判出（主装置 194 + accounts 36 + 零信任 50 + 胜负 25 + 疯狂 28；冻结版门禁 853 通过；玩家页内容高 -11.2%）

- [账号与显示名：让局内每个人知道对面是谁](done/account-and-display-name.md) — **Medium** — 账号（注册 / 登录 / 跨局身份 / 恢复码找回）与席位级公开玩家名「1 号 · 玩家名」；玩家名以账号为准、只作公开呈现，不参与授权（D-0021）；批次 E30 M1 六行 + M2 八行全部通过（新装置 `verify-accounts` 29 项 + 零信任 51 + 主装置 194 + 胜负 25；冻结版门禁 853 通过，含同序号推送丢名字的真缺陷修正）

- [自动化复盘：在圆盘上逐步回放每一个原子步骤](done/replay-auto-review.md) — **High** — 结束批次之后对局内玩家开放的逐步回放：圆盘上按原子步骤可视化（恶魔击杀红箭头 / 死亡标记 / 换角等）+ 上部步骤说明，进行中零泄露（零信任）

- [初始身份随机器：按官方阵营分布自动配板](done/setup-randomizer.md) — **High** — 给定人数按官方「镇民 / 外来者 / 爪牙 / 恶魔」分布表生成配板；阵型修正参与净分布、按官方口径钳制并显式披露；随机只作显式输入、可重放；说书人可重摇 / 手改；批次 E27 矩阵 8 行全部通过（本票装置 58 项 + 真宿主 4 条 + 规则单测；冻结版门禁 787 通过）
- [限次信息族：女裁缝 / 艺术家](done/seamstress-and-artist.md) — **High** — S&V 两名限次信息角色：女裁缝每夜可选两名玩家得知是否同阵营、用后不再唤醒（摇头不消耗、空槽可归因）；艺术家白天主动提问、四种回答、「要求重问」不消耗、用后失去能力——共用「使用即消耗 + 失去能力标记」基建；批次 E26 矩阵 15 行全部通过（本票装置 82 项 + 主装置 194 + 角色变更族 67 + 零信任 44 + 真宿主 4 条；E26 首跑发现并修复在线入口 / 等待态缺陷）
- [说书人面板：裁定归属、读数与投影补口](done/storyteller-decision-affordances.md) — **Medium** — 触发格 / 触发型裁定的归属席位（内核 `AttributionSeat` → 说书人投影）、白天收口读数越界（2 / 1）、候选「已死亡」标注、限一次与今晚理发事实进说书人投影、本夜推演常驻；批次 E25 五装置全绿（本票 70 / 角色变更 67 / 主 194 / 麻脸巫婆 34 / 零信任 43）

- [死亡触发族：贤者 / 心上人](done/sage-and-sweetheart.md) — **High** — 贤者被恶魔击杀 → 当夜展示两名玩家（信息只到本人，R-0038）；心上人死亡即由说书人指定一名玩家持续醉酒（触发型裁定点 + 持续效果，R-0039）；批次 E24 矩阵 17 行全部判出（本票装置 56 项 + 主装置 194 项 + 角色变更族 59 项 + 零信任 43 项 + 真宿主 1 条；全量 703 通过）

- [回溯型信息族：卖花女孩 / 城镇公告员 / 神谕者](done/retrospective-info-family.md) — **High** — 三个「读记录」的信息角色：白天事实按**动作时刻的角色快照**入账（R-0037），除首夜外每夜由说书人给出「是 / 否 / 数字」，信息只到本人；批次 E23 矩阵 13 行全部通过（本票装置 53 项 + 主装置 194 项 + 角色变更族 59 项 + 零信任 43 项 + 真宿主 1 条）

- [说书人注记：魔典上的自由文本提示标记](done/storyteller-annotation.md) — **Low** — 魔典上的自由文本 token：本局级、进事件流、独立注记账（D-0019）+ 席位锚定 + 有界化呈现；批次 E22 矩阵 8 行全部通过（主装置 194 项 + 真宿主 6 条 + 内核 12 条）

- [数学家：窗口取值与说书人裁定面](done/mathematician.md) — **Medium** — R-0004 的失效窗口（黎明水位 + 首夜口径）+ 入槽实时重建说书人裁定提示 + 数字只下发给本人；批次 E21 矩阵 10 行全部通过（装置 38 项 + 真宿主两夜 + 内核/规则单测，全量 618 通过）

- [终局残留挂起：阻塞报警的收口](done/terminal-hold-residue.md) — **Low** — 阻塞报警（`Block`）随终局快照残留（仅说书人视图可见）：内核补「只清阻塞」原语 `SlotUnblockedEvent` 并在结束批次收口；批次 E19 矩阵 1–8 全部通过（真宿主 2 条 + 内核 5 条 + 主装置 177 项回归）

- [涡流干扰计数：R-0004 的引擎级口径（数学家）](done/vortox-interference-counting.md) — **Medium** — 涡流在场的「能力未正常生效」落失效账本（`MalfunctionKind.Vortox`）+ R-0004 逐条口径闭合（按玩家去重、组合原因逐条并列）；批次 E18 逐行判定 1–5 全部通过（主装置 177 项 + 涡流段截图 35–38）

- [角色变更族其余角色：舞蛇人 / 理发师 / 方古 / 哲学家（含上报路径槽位激活）](done/character-change-family.md) — **High** — 麻脸巫婆票之后的同族四角色实现 + 说书人手工上报换角不触发槽位激活的残余收口；批次 E17 逐行判定 46 行全部通过

- [麻脸巫婆：角色变更与「创造恶魔」之夜的死亡裁量](done/pit-hag-character-change.md) — **High** — S&V 唯一能把任意玩家变成任意角色的角色：角色变更（阵营不变、角色唯一）+ 新角色当夜可行动 + 创造恶魔之夜的死亡裁量 + 运行期「恶魔角色清零」口径（R-0029）+ 创造镜像双子配对（配对裁定与首夜同源）；两批 16 行全部通过，行 12 的界面级取证由本票装置第三夜段覆盖
- [结束批次作废挂起请求：终局事件流不留死信](done/ended-game-pending-request-void.md) — **Medium** — 结束批次把挂起请求作废（原因 `GameEnded`）、把挂起裁定点以「本局已结束」收口；收口事件排在 `GameEndedEvent` 之前
- [胜败判定与游戏结束（涡流 / 镜像双子 / 呆瓜）](done/win-loss-and-game-end.md) — **High** — 内核胜负引擎与结束态（一局只结束一次、结束后一切命令被拒）+ 常规两条条件 + 三个角色的胜负条件（镜像双子配对与阻断、涡流黄昏、呆瓜公开选择）与真机可达性；批次 E14 装置 20 项 + 3 图（主装置 157 / 疯狂 28 / 女巫 28 / 零信任 43 全过）
- [装置验证快通道：默认迭代档 + 分段选择器 + 断言从花名册派生](done/device-verification-fast-lane.md) — **High** — 五装置统一档位（默认 0.3s/槽 + 不落盘截图 + 复用产物，取证显式 `--quota 2 --screenshots-all`）、主装置 `--only/--from` 分段、断言从花名册派生；主装置迭代 27.0s / 取证 83.4s，四辅助装置 7.1–12.5s
- [玩家端的死亡公告面缺失](done/player-death-announcement.md) — **Medium** — 公开生死面（生命标记等价物）+ 本日生死公告：夜晚净变化到黎明公开、白天变化即时公开、不含死因；批次 E13 装置 143 / 43 / 28 / 28 项
- [疯狂与裁定式处决：洗脑师 + 畸形秀演员](done/madness-and-adjudicated-execution.md) — **High** — 洗脑师夜晚签发疯狂要求（玩家 + 善良角色、目标知情、按"白天 + 夜晚"存续）；畸形秀演员与处罚处决共用一条说书人主动处决命令面（白天占当日上限并立即入夜、夜晚不占次日上限）；批次 E12 装置 27 项 + 5 图
- [女巫：夜晚诅咒 → 白天提名咒杀](done/witch-curse.md) — 夜晚施加的无维度持续型效果在下个白天触发（提名即死，提名仍生效）；顺带打通「事件触发契约」与「能力存续契约」；批次 E11 行 1–11 全过（主装置 139 / 零信任 42 / 女巫装置 26 项断言）
- [补齐期间到达的信息推送会被快照覆盖丢弃](done/player-information-resync-race.md) — 推送携带事件序号 + 客户端单一写入者按序号合并（含阶段 / 白天 / 请求三态与说书人整视图）；批次 E10 行 1–5 全过
- [白天阶段：提名 / 投票 / 处决](done/day-phase.md) — 开白天 / 提名 / 投票 / 计票 / 处决 + 阶段闸与公开投影；解开了零信任票行 5
- [零信任安全模型](done/zero-trust-security-model.md) — 鉴权两级凭据 + 操作四道闸 + 服务端强制投影；批次 E9 行 1–11 全部有运行时证据
- [玩家重连「补齐缺口」判据与白名单投影不一致](done/player-reconnect-gap-semantics.md) — 可见事件条数不可能覆盖全局序号区间；改为快照权威判据，坏包不采纳、不前进 watermark
- [重建报告对比状态账](done/rebuild-state-ledger-comparison.md) — 重建报告对状态账五账做等价对比，与步骤机 / 快照并列成三项结论
- [恢复失败后的房间健康位](done/room-health-degradation-flag.md) — 区分「空账」与「数据丢了」；降级位置位后显式重建才清除，玩家侧不下发
- [界面游戏化：魔典式小镇视图](done/grimoire-view.md) — 说书人看板改为以席位为中心的魔典（圆环 / 帷幕 / 标记 / 就近操作 / 数据下钻）；批次 E6 行 1–8 全通过
- [玩家端视图新鲜度：请求了结与阶段变化的推送 / 呈现](done/player-view-freshness.md) — 作废 / 代填 / 阶段变化在线到达玩家界面（含阶段中文化）；批次 E4 行 1–5 全通过
- [说书人上帝视角：每步状态信息、归因与最终计算结论](done/storyteller-step-insights.md) — 说书人每步摘要：行动者全部已观测状态及归因、能力生效判定、无选项行为、解除与作废说明；批次 E3 行 1–6 全通过
- [自动步骤机与操作请求](done/operation-request-step-machine.md) — 轮到你时服务端主动推送请求；无超时，说书人可接管/强推/重建；批次 E2 判行 16 / 17 通过
- [《梦殒春宵》夜晚顺序表（结算引擎的输入）](done/sects-and-violets-night-order.md) — 规则层第一块数据：两套口径 + 逐条来源 + 占位移除；批次 E2 真机建表 13 槽位
- [结算引擎与能力生效判定](done/settlement-engine.md) — 逐步结算 + 能力生效判定 + 信息结果 + 维度 → 效果链接与解除；批次 E2 补呈现面证据
- [建解决方案、项目骨架与门禁工程](done/solution-and-gates-bootstrap.md) — 7 个项目 + 4 条规范门禁，三条提交门禁全绿且逐条见红验证
- [百科知识基线与引用索引](done/wiki-knowledge-baseline.md) — 显式 79 页抓取脚本与 SHA256 索引，25 角色类型与 slug 核对完毕，规则细节与范例清单落库
- [内核领域模型与六状态不变量](done/kernel-domain-model.md) — 六状态正交数据模型 + 效果生命周期 + 两本账 + 裁定点契约；10 行验收矩阵逐行留证

### Future

- [跨剧本扩展：主谋 / 圣徒 / 僵怖 / 小怪宝与多恶魔、善良恶魔剧本](future/cross-script-extension.md) — **Low** — 首版剧本之外的扩展：范围、依赖与开工前必须回答的问题先登记为真

### Resolved

- [自我提名口径取证](resolved/self-nomination-source.md) — 已收口（2026-10-04）— R-0018 依印刷规则书 FAQ 转 `Decided`（允许自我提名）；现有实现与回归用例保留，无需改代码
- [R-0041 的 6 人分布行取证](resolved/setup-distribution-6p-source.md) — 已收口（2026-10-04）— 需求方拍板 6 人行按 3 / 1 / 1 / 1 采纳，R-0041 转 `Decided`；实物素材出现时按复核路径可选回填
- [R-0017 在线投票口径收口](resolved/online-vote-ruling-followup.md) — 已收口（2026-10-04）— 需求方给出目标形态「钟盘投票」；实现改造另立 `done/clock-vote-flow.md`

### Watchlist

- [结算结论的三态表达：把「未知」写进契约](watchlist/settlement-conclusion-three-state.md) — **Low** — 三处 `bool` 表达不了"服务端没给结论"，前端只能退化成 false
