# 装置验证快通道：默认迭代档 + 分段选择器 + 断言从花名册派生

- Status: Done
- Priority: High（P0 · 用户指定：完成本项前不开新能力票据）
- 来源：2026-10-02 用户当场指出的效率问题（本轮主装置连跑 3 次，单次 210–314 秒）

## 结果（2026-10-02 · 一次真机运行 + 门禁）

| # | 判据 | 实测 | 结论 |
|---|---|---|---|
| 1 | 迭代档单次 ≤ 30 秒 | 主装置默认档 **27.5 秒**（判定 148 项 + 按设计跳过 2 项） | 通过 |
| 2 | 取证档单次 ≤ 90 秒 | `--quota 2 --screenshots-all` **84.2 秒**（判定 162 项 + 35 张截图落盘） | 通过 |
| 3 | 分段选择器 | `--only boot` 2.4s；`--only day1` 14.0s（只判该段 11 项）；`--from rebuild` 27.6s（只判 rebuild 起 23 项）；段名在用法头 / 段头 / 报告三处可见 | 通过 |
| 4 | 断言从花名册派生 | 4 席变体（`--assign clockmaker,dreamer,no-dashii,mutant`）真机全绿 136 项；辅助装置席位号从 `ASSIGN` 派生 | 通过 |
| 5 | 五装置快档 | winloss 12.3s（20 项）/ madness 11.9s（28 项）/ witch 12.5s（28 项）/ zero-trust 7.1s（43 项） | 通过 |

对比（同一批语义）：主装置 210–314 秒 → 迭代 27.5 秒 / 取证 84.2 秒；winloss 117 秒（默认）/
24.7 秒（旧快通道）→ 12.3 秒。

## 落地

- **档位**（`tools/lib/verify-profile.mjs`）：默认迭代档（0.3s/槽 · 截图不落盘 · 复用产物）；
  取证档显式 `--quota 2 --screenshots-all`（必要时 `--build`）；构建"自动" = 产物缺失、
  或 `src/` 源码比产物新才重建；`--skip-build` / `--no-screenshots` 保留兼容，互斥开关显式报错。
- **分段**（`tools/lib/verify-sections.mjs` + 主装置 14 段）：`--only <段>` / `--from <段>` / `--list-sections`；
  语义 = 前缀执行 + 判定过滤（`--only` 执行到该段为止、`--from` 全程执行但只判该段起）；
  未选中段的断言记 SKIP 并按段汇总——不会"跳过了却显示全绿"。
- **P0-4 派生**：主装置"三席"文本改为 `${options.seatCount}`；辅助装置 `CERENOVUS_SEAT`
  等从 `ASSIGN` 派生（调换花名册顺序不再打翻断言）。
- **P0-5 结构性裁剪**：
  - 第三夜"依赖变化深度场景"（自动作废 / 中毒解除摘要 / 玩家侧作废推送 / 复活）
    降级为**取证档专属**——依据：等价语义已有真宿主集成测试（`SeatDependencyVoidTests`、
    `StepMachineHostTests` 行 8、`StepDigestHostTests` 行 5–6），作废推送链路已由第二夜的
    强制作废在真机覆盖；死亡帷幕改用白天处决的 1 号，迭代档保留该 UI 覆盖。
  - 尾部槽位等待改"自然推进窗口 + 强推空槽位"（`finishNightQuickly` / `advanceSlotsUntil`）——
    依据：节奏语义由 `PacingIsolationTests` 行 13/14/18 覆盖，集成测试 `StepDigestHostTests`
    也是"给足配额 + ForceAdvance 精确推进"的同款做法；强推前检查挂起请求，不越权了结。
  - 工程优化：多席并行加入、阶段推送并行等待、轮询粒度 100–150ms、收尾并行关闭浏览器。
- **先红证据**：旧版对 `--skip-build` / `--only` / `--no-screenshots` 直接抛"未知参数"退出码 1
  （`git show HEAD:tools/verify-storyteller-panel.mjs` 导出后实跑）；新版边界校验
  （未知段 / `--only`×`--from` / `--quota abc` / `--build`×`--skip-build`）逐条报错。

## 独立对抗性自检与修复（2026-10-02）

由独立上下文子代理对冻结工作树做对抗性复核，发现 3 处实质缺陷，全部在本轮修掉：

| 严重度 | 发现 | 修复 |
|---|---|---|
| P1 | `--only/--from` 时，未选中前置段的**真实失败**被记成 SKIP、静默成"全部通过" | `tools/lib/verify-sections.mjs`：未选中段的失败照记 `fail`（日志 `[FAIL·前置]`）并计入退出码；只有通过才记 SKIP。单元探针 + `--only day1` 复验 |
| P2 | `finishNightQuickly` 强推循环缺"挂起请求"守卫，可能越权了结请求 | 与 `advanceSlotsUntil` 统一口径：见挂起请求即停手、判失败（不越权） |
| P2 | 第三夜降级后，"夜里死亡黎明前不公开"与"复活解除帷幕"两条真机语义没有等价覆盖 | 第二夜结束后新增 5 号（外来者）死亡 → 复活往返（5 条断言，两档都跑）；第三夜注释写明"作废推送与强制作废共用同一条前端链路"的依据边界 |

P3 一并处理：源文件新鲜度检测补 `json/resx/razor`；winloss 席位断言改 `ASSIGN.length` 派生；
"PacingIsolationTests 行 13/14/18"改注为"其用例注释里的行号"（避免误读为文件行号）。

## 决定与依据

- 装置是**验收**工具（`docs/acceptance/AGENTS.md`：一批 = 一次构建 + 一次多客户端会话）；
  本票把"调试回路"从装置里剥出来，而不是削弱验收强度；
- **正式取证必须显式**：默认档是快档（不落盘截图），交付证据只能来自
  `--quota 2 --screenshots-all` 的一次完整运行；分段跑与默认档跑都不作证据；
- 与集成测试的分工已写进 `docs/acceptance/AGENTS.md` §3；装置用法同步到 `web/AGENTS.md` §3.1
  与 `AGENTS.local.md`「验证成本纪律」。

## 残余

- 取证档余量 5.8 秒（84.2 / 90）：再往真机装置加断言前要重新评估；
- 迭代档不跑第三夜深度场景（取证档跑）；迭代时想要更强的回归，用 `--quota 1` 以上的节拍；
- 辅助装置暂无分段选择器（单次 7–13 秒，接入成本大于收益），需要时按主装置同款接入
  `tools/lib/verify-sections.mjs`（账号装置 2026-10-04 已接入：`done/accounts-device-section-selector.md`）；
- 截图证据卫生：`artifacts/web/` 不轮转、落盘断言只查文件名存在性（不查 mtime / 哈希）——
  旧图可能冒充当轮；
- `--skip-build` 只做存在性检查、不看产物新鲜度（显式复用的语义如此；改过 C# 后必须先构建）。
