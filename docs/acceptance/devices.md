# 验收装置清单：八个装置各管什么、怎么跑、换机器核对什么

> **目的**：`tools/verify-*.mjs` 的真机验收装置登记在**本页**（`web/AGENTS.md` §3.1 是字节受限的指令文件，
> 只留运行入口与外部耦合，不再随装置数量增长）。每个装置的场景与断言以**脚本头部注释**为准，本页不复制。

## 1. 装置总表

| 装置 | 脚本 | 夹具（`--assign` 派生） | 它回答的问题 |
|---|---|---|---|
| 主装置 | `tools/verify-storyteller-panel.mjs` | 默认 5 席：`clockmaker / dreamer / no-dashii / mutant / klutz`（`--seats` / `--assign` 可换） | 真宿主 + 真 Vite + 真 Chromium 的多客户端通用玩法回归：加入 → 分配 → 首夜（钟表匠裁定点 / 筑梦师请求）→ 白天（提名 / 投票 / 处决）→ 第二 / 三夜（代填 / 强制作废 / 依赖失效）→ 涡流干扰（涡流存活下的镇民信息结算：账本落 `Vortox` + 玩家隔离；快档第 3 夜 / 取证档第 4 夜）→ 魔典主视图 → 重建 → 重连补齐 |
| 胜负链路 | `tools/verify-winloss.mjs` | 5 席：`vortox / klutz / mutant / witch / dreamer` | 呆瓜被处决 → 公开选择当场开出（候选不含已死的自己）→ 双端同一份结束结论 → 结束后命令被拒 |
| 麻脸巫婆之夜 | `tools/verify-pit-hag.mjs` | 5 席：`pit-hag / savant / artist / no-dashii / klutz` | 创造恶魔 → 当夜该恶魔真的被唤醒并行动 → 恶魔击杀进待定死亡 → 说书人「阻止 / 确认 / 追加死亡」→ 窗口收口 |
| 女巫链路 | `tools/verify-witch.mjs` | 4 席：`witch / clockmaker / dreamer / no-dashii`（存活 4 > 3，女巫保住能力的最小局面） | 夜晚诅咒 → 下个白天提名即死（提名仍生效；存活 ≤3 时解除）+ 三席收包扫描 |
| 处罚处决链路 | `tools/verify-madness.mjs` | 4 席：`cerenovus / clockmaker / dreamer / mutant` | 洗脑师两维选择 → 魔典疯狂要求 → 夜晚处罚（夜晚继续）→ 白天处罚（占上限 + 立即入夜） |
| 零信任负向 | `tools/verify-zero-trust.mjs` | Node SignalR 客户端扮演**篡改前端**（无浏览器） | 伪造 / 冒用 / 旧连接凭据直调 Hub、白天越权提交、非法选项、收包与审计扫描 |
| 角色变更族 | `tools/verify-character-change.mjs` | 6 席：`philosopher / dreamer / fang-gu / barber / klutz / mutant` | 哲学家在真界面上从镇民 / 外来者清单获得能力（不变身）→ 被选角色持有者醉酒、醉酒者**照常被唤醒** → 次夜在**自己的格**上代行获得的能力 → 方古首次成功杀外来者即侵染（目标变邪恶方古、原方古死亡）→「限一次」已用后普通死亡 → 理发师死亡当夜换角 + 尚未进入的格重绑；第二局：哲学家改选**不在场**的钟表匠 → 同夜钟表匠的格就地激活由他代行（账本「1 号 · clockmaker · 正常生效」+ 信息下发本人） |
| 数学家链路 | `tools/verify-mathematician.mjs` | 6 席：`no-dashii / dreamer / mathematician / mutant / klutz / clockmaker` | 两个中毒的信息角色先结算 → 数学家入槽时裁定提示按**入槽时刻**的账本推演（首夜 = 2；「当前步骤」摘要与裁定点同源）→ 数字只下发本人、无关席位零下发 → 跨黎明（开白天 / 结束白天）后第二夜推演 = 0（窗口按黎明重置） |

分工：主装置跑通用玩法回归；其余七个各跑一条能力链路（胜负 / 角色变更 + 死亡裁量 / 白天触发 / 处罚处决 / 安全负向 / 涡流干扰 / 数学家窗口）。
**结束批次只在胜负装置里跑到**：主装置全程保持恶魔存活、从不产生 `GameEndedEvent`——引"终局面 / 结束面"的真机证据只能引胜负装置（主装置那一段叫 `final`，含义是"整场收尾"，不是"游戏结束"）。
辅助装置的席位号从各自 `ASSIGN` 派生，调换花名册顺序不会打翻断言。

## 2. 档位、分段与退出码

| 项 | 规则 |
|---|---|
| 默认（迭代档） | `0.3s/槽` 快节拍 + 截图不落盘 + 复用产物；用于调试与回归 |
| 正式取证 | **必须显式** `--quota 2 --screenshots-all`（必要时加 `--build`）；一批一次、只对**冻结版本**；默认档与分段跑都不作交付证据（`docs/acceptance/AGENTS.md` §3） |
| 主装置分段 | `--list-sections` 列段名；`--only <段>` 执行到该段为止（前置段照跑，**只有该段的断言计入判定**）；`--from <段>` 全程执行、从该段起计入判定——只作定位，不作证据 |
| 退出码 | `0` 全部断言通过；`1` 有断言失败（含前置段）；`2` 环境缺依赖（Playwright / Chromium 等） |
| 产物 | 截图与日志进 `artifacts/web/`（gitignored，可重生成）；装置自建临时库与工作目录由脚本收尾自清理 |

## 3. 外部耦合（换机器前先核对）

宿主编译产物路径、SQLite 表 `Games` 的 `StorytellerTicket` / `SeatsJson` 列形状、席位数量与 `--assign` 同数、
场景角色、Node ≥ 22.5（`node:sqlite`）+ `npx playwright install chromium`——逐条清单在 `web/AGENTS.md` §3.1。
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
