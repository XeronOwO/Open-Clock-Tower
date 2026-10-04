# 账号装置分段选择器：把 37–40s 的固定开销拆开定位

- Status: Done（2026-10-04）
- Priority: Medium
- Depends on: 无（机制复用 `docs/backlog/done/device-verification-fast-lane.md` 落下的 `tools/lib/verify-sections.mjs`）

## 要解决的问题

`tools/verify-accounts.mjs` 是辅助装置里唯一超出量级的一个（迭代档 ~37s / 取证档 38.6s，其余 7–13s），
而它是十四个装置里唯一"档位开关能被解析、分段开关没人消费"的：

1. 脚本 import 了 `tools/lib/verify-profile.mjs`，`--only` / `--from` / `--list-sections` **能被解析出来**，
   却没有传给任何执行器——传 `--only drawer-names` 既不报错也不生效，静默跑完并判定全部 36 项。
   参数被静默吞掉比"不支持"更糟：日志看起来像"这次只跑了某一段"，实际没有；
2. 想复核某一段（例如新加的抽屉姓名口径 / 版面量度）时，没有"执行到这里为止 / 判到这里为止"的能力；
3. 37s 花在哪没有按段耗时，下一次"要不要再往这里加断言、该裁哪一段"只能靠猜——而猜是错的：
   见下一条；
4. **分段一落地就咬出真账**：37s 里有 **30.0s 是 guest 段的一处无界等待**。游客页在"没有诊断"时
   并不渲染 `player-diagnostics`，而 `locator.innerText()` 的默认超时是 Playwright 的 30s，
   元素缺失时白等满 30s 才 reject、被 `.catch` 吞成空串。实测空档 4316ms → 34319ms，
   段耗时把它钉在 `guest=30.4s`；修掉后整装置 **7.3s**。所谓"固定开销 = 宿主 + Vite 冷启 +
   4 个浏览器上下文"是猜的——真实固定开销只有约 3.8s（boot 1.6 + tickets 0.4 + 建 4 个上下文 ~1.8）。

其余十二个辅助装置维持现状：单次 7–13s，`device-verification-fast-lane` 残余已判"接入成本大于收益"。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | `--list-sections` | 打印段名清单后直接退出（不起宿主、不起浏览器），退出码 0 | 终端输出 |
| 2 | `--only <段>` | 执行到该段为止、只有该段计入判定；前面段照跑并记 SKIP，未选中段的**真实失败**仍计入退出码 | 一次运行输出 |
| 3 | `--from <段>` | 全程执行、从该段起计入判定 | 一次运行输出 |
| 4 | 未知段 / `--only` 与 `--from` 同传 | 显式报错退出，不静默跑全量 | 终端输出 |
| 5 | 早退不留孤儿 | `--only` 提前结束后：浏览器已关、宿主与 Vite 已停、临时库已清 | 进程与目录复核 |
| 6 | 默认全段不回归 | 仍是 36 项全绿，并新增按段耗时表 | 一次完整迭代档运行 |
| 7 | 无界等待修掉 | guest 段不再白等 30s；诊断区缺失按"无诊断"处理，不是失败 | 段耗时 `guest` 30.3s → 0.4s、整装置 37.3s → 7.3s |
| 8 | 文档同步 | 脚本用法头 + `docs/acceptance/devices.md` §2 记下分段与实测成本 | diff |

## 决定与依据

- **语义不另造**：直接用 `tools/lib/verify-sections.mjs` 的"前缀执行 + 判定过滤"
  （`--only` 执行到该段为止、`--from` 全程执行但只判该段起），与主装置同款。
  跳过中间段就没有可断言的状态——这是线性剧本的诚实语义，不是限制；
- **段边界 = 剧本自己的步骤分隔**（现 `=== N/8 ===` 段头）与两处收尾取证块，共 12 段，不重新编排剧本：
  `boot`（构建并启动真宿主）· `tickets`（取票据 + 起 Vite）· `join-a`（说书人 + A 注册认领 1 号）·
  `join-b`（B 注册认领 2 号）· `guest`（游客 C 坐 3 号 + 线级探针入座）· `rename`（改名三处同步 + 线级序号取证）·
  `replay`（复盘文案口径）· `drawer-names`（抽屉面姓名口径）· `negative`（伪造 / 跨账号 / 二次认领 + 绑定落库）·
  `account-panel`（一次性恢复码复核）· `onboarding`（说明入口悬停 / 点按 / Esc）· `layout`（版面量度 + 控制台零错误）；
- **断言报告改走共享 `createChecker` 的按段口径**：断言编号（`#N`）退场，改以"段 + 标签"定位
  （与主装置一致；编号本来就是插入序的副产品，插一条就整体位移）；
- **早退路径的进程收尾是必做项**：主装置为此把 browser 提到模块级再在 `cleanup()` 里关，
  本装置照做，并把三条 SignalR 连接一并移进 `cleanup()`——原来只在 main 正常跑完时关闭，
  `--only` 早退与异常路径都会漏（`--list-sections` 则连依赖都不加载）；
- **同族写法不进本票**：其余 12 个装置的 `waitForText` / `waitForLocatorContains` 是同一族无界等待
  （元素缺失时同样白等 30s，且会突破自己的 deadline），但一次要动 12 个装置、各需一次运行回归，
  另立 `docs/backlog/done/device-poll-innertext-unbounded-wait.md`（Low）。

## 结果（2026-10-04 · 五次真机运行 + 门禁）

| # | 判据 | 实测 | 结论 |
|---|---|---|---|
| 1 | `--list-sections` | 秒级打印 12 段后退出（未起宿主），退出码 0 | 通过 |
| 2 | `--only join-b` | 4.0s：前置段 boot / tickets / join-a 记 SKIP，只有 join-b 判定 1 项，到 `[guest]` 为止 | 通过 |
| 3 | `--from drawer-names` | 全程执行；前置 7 段记 SKIP，从 drawer-names 起判定 15 项，退出码 0 | 通过 |
| 4 | 未知段 / 互斥 | `--only nope` 退出码 1 且列出可用段；`--only boot --from tickets` 退出码 1 报互斥 | 通过 |
| 5 | 早退不留孤儿 | `--only` 早退后：临时库自清、无 Chromium / 宿主残留、5414 / 5294 无监听 | 通过 |
| 6 | 默认全段不回归 | **36 项全绿**（按段：tickets 1 · join-a 4 · join-b 1 · guest 4 · rename 7 · replay 4 · drawer-names 4 · negative 6 · account-panel 1 · onboarding 3 · layout 1） | 通过 |
| 7 | 前置段失败照记 | 共享库最小探针：前缀段失败记 `[FAIL·前置]` → 退出码 1；通过才记 SKIP → 退出码 0 | 通过 |
| 8 | 按段耗时（修后） | 整装置 **7.3s**：boot 1.6 / tickets 0.4 / join-a 1.4 / join-b 0.4 / guest 0.4（修前 30.4）/ rename 0.2 / replay 0.7 / drawer-names 0.1 / negative 0.1 / account-panel 0 / onboarding 0.1 / layout 0 | 通过 |

诚实记录（范围说明）：

- 本次**没有重跑取证档**：截图落盘路径未改动，重跑只会把 E31 冻结批次留下的
  `accounts-01…10` 证据图换成新版本；本票是装置自身的工具链改动，全部证据来自迭代档运行，
  不作玩法验收证据（口径见 `docs/acceptance/AGENTS.md` §3）；
- 断言报告口径变化：编号（`#N`）退场，`verify-accounts` 的结果改为"按段汇总 + 失败明细"，
  与主装置一致；历史票据里"第 N 项"的引用按标签仍可检索到。
