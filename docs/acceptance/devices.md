# 验收装置清单：十四个装置各管什么、怎么跑、换机器核对什么

> **目的**：`tools/verify-*.mjs` 的真机验收装置登记在**本页**（`web/AGENTS.md` §3.1 是字节受限的指令文件，
> 只留运行入口与外部耦合，不再随装置数量增长）。每个装置的场景与断言以**脚本头部注释**为准，本页不复制。

## 1. 装置总表

| 装置 | 脚本 | 夹具（`--assign` 派生） | 它回答的问题 |
|---|---|---|---|
| 主装置 | `tools/verify-storyteller-panel.mjs` | 默认 5 席：`clockmaker / dreamer / no-dashii / mutant / klutz`（`--seats` / `--assign` 可换） | 真宿主 + 真 Vite + 真 Chromium 的多客户端通用玩法回归：加入 → 分配 → **说书人注记**（D-0019：加 / 改 / 删 + 牌面 token + 5 席玩家零下发；重启 / 重建后仍在）→ 首夜（钟表匠裁定点 / 筑梦师请求）→ 白天（提名 / **钟盘收票** / 处决 / **五名旅行者一次加入 + 屠夫窗口额外提名**）→ **同日两条流放**（怪咖达线受保护 / 屠夫不受保护 → 流放死亡 + 即时公开；含移出与复盘四标记）→ 第二 / 三夜（**咖啡师 / 流莺 / 集骨者三个黄昏格 + 窗口在下个黄昏收口** / 代填 / 强制作废 / 依赖失效）→ 涡流干扰（涡流存活下的镇民信息结算：账本落 `Vortox` + 玩家隔离；快档第 3 夜 / 取证档第 4 夜）→ 魔典主视图 → 重建 → 重连补齐 |
| 胜负链路 | `tools/verify-winloss.mjs` | 5 席：`vortox / klutz / mutant / witch / dreamer` | 呆瓜被处决 → 公开选择当场开出（候选不含已死的自己）→ 双端同一份结束结论 → 结束后命令被拒 → **结束后玩家复盘**（入口出现 / 逐步回放 / 刷新按事件序号恢复；R-0043，2026-10-04 加） |
| 麻脸巫婆之夜 | `tools/verify-pit-hag.mjs` | 5 席：`pit-hag / savant / artist / no-dashii / klutz` | 创造恶魔 → 当夜该恶魔真的被唤醒并行动 → 恶魔击杀进待定死亡 → 说书人「阻止 / 确认 / 追加死亡」→ 窗口收口（候选文本读取口径与面板「已死亡」标签兼容，批次 E25 起） |
| 女巫链路 | `tools/verify-witch.mjs` | 4 席：`witch / clockmaker / dreamer / no-dashii`（存活 4 > 3，女巫保住能力的最小局面） | 夜晚诅咒 → 下个白天提名即死（提名仍生效；存活 ≤3 时解除）+ 三席收包扫描 |
| 处罚处决链路 | `tools/verify-madness.mjs` | 4 席：`cerenovus / clockmaker / dreamer / mutant` | 洗脑师两维选择 → 魔典疯狂要求 → 夜晚处罚（夜晚继续）→ 白天处罚（占上限 + 立即入夜） |
| 零信任负向 | `tools/verify-zero-trust.mjs` | Node SignalR 客户端扮演**篡改前端**（无浏览器） | 伪造 / 冒用 / 旧连接凭据直调 Hub、白天越权提交、非法选项、收包与审计扫描（禁词表含复盘字段 `replay` / `markers` / `steps`；R-0043，2026-10-04 加） |
| 角色变更族 | `tools/verify-character-change.mjs` | 6 席：`philosopher / dreamer / fang-gu / barber / klutz / mutant` | 哲学家在真界面上从镇民 / 外来者清单获得能力（不变身）→ 被选角色持有者醉酒、醉酒者**照常被唤醒** → 次夜在**自己的格**上代行获得的能力 → 方古首次成功杀外来者即侵染（目标变邪恶方古、原方古死亡）→「限一次」已用后普通死亡 → 理发师死亡当夜换角 + 尚未进入的格重绑；第二局：哲学家改选**不在场**的钟表匠 → 同夜钟表匠的格就地激活由他代行（账本「1 号 · clockmaker · 正常生效」+ 信息下发本人）；魔典中心两枚说书人标记——「限一次」（侵染后直至整局，第三夜仍在）与「今晚理发」（理发师之夜窗口内出现、结清后消失）；E29 补：说书人实时复盘五类标记（醉酒 / 换角 / 恶魔击杀箭头 / 换手 / 中毒，截图 cc-15…cc-20） |
| 数学家链路 | `tools/verify-mathematician.mjs` | 6 席：`no-dashii / dreamer / mathematician / mutant / klutz / clockmaker` | 两个中毒的信息角色先结算 → 数学家入槽时裁定提示按**入槽时刻**的账本推演（首夜 = 2；「当前步骤」摘要与裁定点同源）→ 数字只下发本人、无关席位零下发 → 跨黎明（开白天 / 结束白天）后第二夜推演 = 0（窗口按黎明重置） |
| 回溯型信息族 | `tools/verify-retro-info.mjs` | 6 席：`no-dashii / pit-hag / flowergirl / town-crier / oracle / mutant` | 白天「爪牙自我提名 + 恶魔举手」（钟盘收票）→ 次夜三张说书人裁定提示分别按记录推演（**按白天账推演：是 / 是**、**按当前账推演：1**，且裁定点标识逐个换新、不是上一条残留）→ 信息只到本人且互不串台 → 无关席位零下发（含每席自己那条连接的帧扫描） |
| 死亡触发族 | `tools/verify-death-triggers.mjs` | 5 席：`fang-gu / sage / sweetheart / klutz / barber` | 白天处决 3 号（心上人）→ 触发型裁定挂起（开夜被拒 `phase.trigger_choice_pending`）→ 指定 4 号持续醉酒（跨阶段不自动清除、操作台可见效果来源）→ 次夜恶魔击杀 2 号（贤者）→ 当夜触发格裁定按击杀记录推演 → 展示两名玩家 → 信息只到本人、其余席位零下发；说书人面板——两处裁定的归属席位与「定位到 N 号」、候选「已死亡」标注（含阳性 / 阴性对照）、白天收口读数封顶（不再出现 `2 / 1`）、环区常驻本步上下文（含推演） |
| 限次信息族 | `tools/verify-seamstress-artist.mjs` | 5 席：`seamstress / artist / klutz / mutant / fang-gu` | 女裁缝在**玩家页**选两名（夜 1 摇头不消耗 → 夜 2 再次被唤醒）→ 说书人裁定（信息只到本人）→ 用后不再唤醒（空槽 + 可归因跳过）；艺术家白天主动提问（四答 / 「要求重问」不消耗 / 重连后等待态仍在）→ 回答后信息 + 魔典「失去能力」标记 + 入口消失；第三夜空槽不再唤醒 + 帧级隔离扫描 |
| 初始配板链路 | `tools/verify-setup-randomizer.mjs` | 5 席：`clockmaker / dreamer / no-dashii / mutant / klutz`（随机建议不直接开夜——可能抽到未实现夜间契约的角色；手改到这套有契约的集合） | 一键配板（覆盖每席 / 角色唯一 / 净分布随在场恶魔修正 / 显式种子）→ 重摇（新种子）→ 手改 → 提交走既有分配命令面 → 开夜 → 首夜钟表匠裁定 + 筑梦师请求按配板唤醒 → 两条信息只到本人（DOM + 五席帧级隔离扫描 + 说书人连接阳性对照） |
| 复盘规模采样 | `tools/verify-replay-scale.mjs` | 5 席：`vortox / clockmaker / dreamer / mutant / klutz`（不跑夜间流程） | 事件流 ≥ 2000 事件时复盘的服务端投影与前端翻页耗时采样：真实命令面（`ReportSeatState` 交替中毒 / 健康）写流 → 服务端分页拉满核对 + 首页 / 中段 / 深页各 3 次投影采样 → 浏览器首屏 / 第 200→201 / 第 2000→2001 翻页采样 + 已加载窗口严格 200 / 页增长（票据矩阵行 8） |
| 账号与玩家名 | `tools/verify-accounts.mjs` | 4 席：3 个浏览器上下文（A 注册认领 1 号 / B 注册认领 2 号 / 游客 C 只凭票据坐 3 号）+ 1 个 Node SignalR 线级探针（不坐席） | 注册（含一次性恢复码）→ 凭票据认领席位 → 同桌所有人看到同一份「N 号 · 玩家名」→ 游客席位回退「N 号」→ 改名同步自己 / 同桌 / 说书人魔典三处 → 复盘步骤文案与圆盘标记用玩家名 → 负向：伪造账号会话 / 跨账号抢席 / 同账号第二席被显式拒绝且拿不到连接凭据；D-0021，2026-10-04 加。E31 扩展（票据 `ui-layout-and-onboarding`）：说明入口悬停 / 点按 / `Esc`、数据抽屉 + 开局分配 + 席内注记的姓名口径、版面量度（玩家页 / 说书人页内容高 + 整页截图，供前后对比） |

分工：主装置跑通用玩法回归；其余十三个各跑一条能力链路（胜负 / 角色变更 + 死亡裁量 / 白天触发 / 处罚处决 / 安全负向 / 涡流干扰 / 数学家窗口 / 回溯型信息 / 死亡触发 / 限次信息 / 初始配板 / 复盘规模采样 / 账号与玩家名）。
**结束批次只在胜负装置里跑到**：主装置全程保持恶魔存活、从不产生 `GameEndedEvent`——引"终局面 / 结束面"的真机证据只能引胜负装置（主装置那一段叫 `final`，含义是"整场收尾"，不是"游戏结束"）。
辅助装置的席位号从各自 `ASSIGN` 派生，调换花名册顺序不会打翻断言。

## 2. 档位、分段与退出码

| 项 | 规则 |
|---|---|
| 默认（迭代档） | `0.3s/槽` 快节拍 + 截图不落盘 + 复用产物；用于调试与回归 |
| 正式取证 | **必须显式** `--quota 2 --screenshots-all`（必要时加 `--build`）；一批一次、只对**冻结版本**；默认档与分段跑都不作交付证据（`docs/acceptance/AGENTS.md` §3） |
| 分段（主装置 / 账号装置） | `--list-sections` 列段名（账号装置 12 段）；`--only <段>` 执行到该段为止（前置段照跑，**只有该段的断言计入判定**）；`--from <段>` 全程执行、从该段起计入判定——只作定位，不作证据；每次运行打印**按段耗时**表 |
| 装置成本（迭代参考） | 主装置 **72.7s**（2026-10-04 旅行者段落地后；历史 27.0s——新增五名旅行者加入 + 两条流放收票 + 三个黄昏格 + 复盘扫描；同版取证档 173.5s）；**账号装置 7.3s**（2026-10-05 前 37–40s：分段耗时咬出 guest 段一处无界 `innerText()` 白等满 30s，已修）；其余辅助装置 7–13s |
| 轮询 / 守卫式读 DOM | 装置的轮询助手与守卫式助手读 DOM 一律走 `tools/lib/bounded-text.mjs`：文本（`readDecisionText` / `panelText` / `readTextOrNull` / `infoText` / `decisionId` 等）走 `readTextBounded`，属性（`waitForAttribute` / `readPlayerInformationCount` / `informationCount` / `waitForRequestPanelIdle` / 日状态 / 标记 `title` 等）走 `readAttributeBounded`（单次读取均 500ms 上限）——Playwright 默认 30s 不再参与读取，助手自己的 15s / 20s / 30s deadline 不被突破；元素预期在场的直接读取保持 auto-wait。改动该助手后跑一次假页面六探针自检 `node tools/check-bounded-text.mjs`（在场 / 缺失 / 守时 / 晚到 / 文本守卫后脱离 / 属性守卫后脱离） |
| 退出码 | `0` 全部断言通过；`1` 有断言失败（含前置段）；`2` 环境缺依赖（Playwright / Chromium 等） |
| 产物 | 截图与日志进 `artifacts/web/`（gitignored，可重生成）；装置自建临时库与工作目录由脚本收尾自清理 |

## 3. 外部耦合（换机器前先核对）

宿主编译产物路径、SQLite 表 `Games` 的 `StorytellerTicket` / `SeatsJson` 列形状、席位数量与 `--assign` 同数、
场景角色、Node ≥ 22.5（`node:sqlite`）+ `npx playwright install chromium`——逐条清单在 `web/AGENTS.md` §3.1。
账号装置另多两条：账号 Hub 入口 `/hub/account` 与其方法名（`Register` / `Login` / `Logout` /
`ChangeDisplayName` / `ResetPassword`）、账号表 `Users` 与席位绑定表 `SeatBindings` 的列形状
（装置直接读库断言「绑定是会话信息而不是事件」）；游戏 Hub 的带账号入口是
`JoinSeatWithAccount(ticket, accountSession, lastSequence)`——SignalR 不支持方法重载，改名即换契约。
运行期外链：魔典席位牌的角色图热链百科图片（D-0007 / R-0006）。图片主机不可达或路径变化时只降级为
文字 + 色环；失败图片只触发 `requestfailed`（不进 console / pageerror），不影响装置断言。
装置启动失败时会打印对应路径；退出码 `2` 先查这条。

## 4. 新增装置怎么登记

1. 新 `tools/verify-*.mjs` 完成后，在**本页** §1 加一行（脚本 / 夹具 / 回答的问题）；
2. 只有**外部耦合**有变化时才动 `web/AGENTS.md` §3.1（它受 5120 字节上限约束，不装装置细节）；
3. 脚本头部注释里的票据路径随票据移库同步更新（装置引用的票据会从 `review/` 移到 `done/`）。

## 相关阅读

- 验收规程与批次记录：`docs/acceptance/AGENTS.md`
- 装置运行入口与外部耦合：`web/AGENTS.md` §3.1
- 装置快通道（默认档 / 分段 / 断言派生）：`docs/backlog/done/device-verification-fast-lane.md`
- 证据怎么留：`docs/evidence/verification.md`
