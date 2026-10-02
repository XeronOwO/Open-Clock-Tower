# 《梦殒春宵》夜晚顺序表（结算引擎的输入）

- Status: Done
- Priority: High
- Depends on: 建解决方案、项目骨架与门禁工程；百科知识基线与引用索引

## 要解决的问题

规则层 `OpenClockTower.Rules` 一直是空项目，宿主只能用一份**显式占位**的演示步骤表
（`DemoStepPlan`）证明端到端链路——占位随时可能被误当成《梦殒春宵》规则；
结算引擎（下一步）又缺一张"按什么顺序展开今晚步骤"的权威表。本票落这张表并移除占位。

## 要做的事

1. 从两页抓取快照提取《梦殒春宵》夜晚顺序（不许凭记忆，sources.md §5）：
   - 百科《梦殒春宵》· 2026-10-01 抓取 · 夜晚顺序表（原本顺序）；
   - 百科《夜晚行动顺序一览》· 2026-10-01 抓取 · 首个夜晚 / 其他夜晚（推荐顺序）。
2. 落成 `NightOrderTable`：两个阶段 × 两种口径；条目种类为黄昏 / 爪牙信息 / 恶魔信息 /
   信息类角色行动开始 / 角色行动 / 黎明；每套序列带来源注释。
3. 逐条测试锁死顺序与差异；断言 25 个角色中有夜晚行动的 21 个全覆盖
   （无夜晚行动的艺术家 / 呆瓜 / 畸形秀演员 / 博学者不在表上，依据 character-rules 对应条目）。
4. 移除生产代码里的演示占位：宿主不再自动开阶段；集成测试改用测试夹具 `TestNightPlan`。
5. 新门禁：生产代码不得再出现演示计划（先红后绿）。
6. 口径冲突登记 `docs/standard/rulings.md` R-0014（两套都入库、平台默认原本口径）。

## 验收矩阵

| # | 场景 | 期望 | 依据 | 证据 |
|---|---|---|---|---|
| 1 | 原本口径逐条 | 与《梦殒春宵》页「夜晚顺序表」逐条一致 | 页名 + 抓取日期 | 通过：`NightOrderOriginalTests`（2） |
| 2 | 推荐口径逐条 | 与《夜晚行动顺序一览》的 S&V 子序列逐条一致 | 页名 + 抓取日期 | 通过：`NightOrderRecommendedTests`（2） |
| 3 | 差异集合 | 恰好为 R-0014 登记的 4 处（首夜信息环节先后 / 麻脸巫婆位置 / 其他夜恶魔间相对顺序 / 推荐独有信息标记） | R-0014 | 通过：`NightOrderVariantDiffTests`（3） |
| 4 | 角色覆盖 | 21 个夜间角色全覆盖；4 个无夜晚行动角色不在表上 | terminology §9 / character-rules | 通过：`NightOrderTableTests`（10） |
| 5 | 结构化不变量 | 黄昏起、黎明止；首夜两条信息环节、其他夜无；同阶段角色不重复；白天 / 结算查询显式失败 | 同上 | 通过：`NightOrderTableTests` |
| 6 | 占位移除 | 生产代码无演示计划；宿主不再自动开阶段 | 架构 §2.6 | 通过：新门禁先红后绿；`BootstrapBehaviorTests` 断言引导不开阶段、席位数按配置播种、显式开阶段仍可用 |

## 落地与运行证据

| 门禁 | 结果 |
|---|---|
| `dotnet build OpenClockTower.slnx` | 9 个项目，0 警告 0 错误 |
| `dotnet test OpenClockTower.slnx` | **142/142 通过**（规则数据 17 + 门禁 11 + 内核 89 + 集成 25） |
| `dotnet format OpenClockTower.slnx --verify-no-changes` | exit 0 |
| 新门禁先红 | `dotnet test --filter PlaceholderRuleDataGateTests` → FAIL，报告指向 `src\OpenClockTower.Server\DemoStepPlan.cs` |
| 新门禁后绿 | 删除占位文件后同命令 → 通过 1/1 |
| 门禁放宽后重做先红后绿 | 复核指出精确大小写匹配可被改名绕过；改为不区分大小写 + 覆盖 `demoplan` 变体后，用未改动文件 `SeatId.cs` 做临时探针 → FAIL 报告 `src\OpenClockTower.Kernel\SeatId.cs → demoplan`；`git checkout --` 还原（`git status` / `git diff` 均为空）后 → 通过 1/1 |
| 来源一致性核对（独立于单元测试） | `pwsh -NoProfile -File tools/check-night-order.ps1` → `[OK]` 四套序列与两页快照逐条一致（exit 0） |

**占位替换的可验证性**：`DemoStepPlan.cs` 已删除；`tests` 侧改用夹具后，
SignalR 请求标识由 `demo:night-1:demo-seat-1` 变为 `test:night-1:test-seat-1`，
相关断言（单播 / 重连 / 重投 / 节奏 / 重启）全部保持通过；引导层另有 `BootstrapBehaviorTests`
断言"不自动开阶段、但显式开阶段仍成功"，使这条行为变化有**直接**的运行时证据。

## 验收批次 E2（2026-10-02，真机多客户端会话）

- 真机建表走的是**面板默认 Recommended 口径**：`开夜` 受理后说书人条显示 `1 / 13`，与
  `FirstNightRecommended` 的 13 个条目一致；钟表匠槽位（该能力没有玩家选项）先出现、其裁定期间
  三席玩家均无请求，随后才轮到筑梦师的玩家请求——与表中 `clockmaker` 在 `dreamer` 之前一致。
- 表本身仍由 `NightOrderOriginalTests` / `NightOrderRecommendedTests` / `NightOrderVariantDiffTests` /
  `NightOrderTableTests` 与 `tools/check-night-order.ps1` 逐条锁死（见上一节）。
- 证据：`tools/verify-storyteller-panel.mjs` 断言「首夜真实建表：13 个槽位」「钟表匠槽位…三席玩家均无请求」；
  截图 `artifacts/web/03-night-started.png`；运行日志 `artifacts/web/batch-run.log`。

## 独立对抗性复核（只读，冻结工作树）

第一棒复核超时未返回、被中止；改派窄范围第二棒（4 项聚焦）后取得结论：

| 复核发现 | 处置 |
|---|---|
| 高：无 | — |
| 中 M1：票据"差异集合"一行把"其他夜恶魔间相对顺序"压缩成"亡骨魔位置"，与 R-0014 用词不一致 | 已改：括号内容与 R-0014 逐字对齐 |
| 中 M2：顺序表测试的期望值是实现的手抄副本（两处同源同错会全绿），未从一手来源派生 | 已补：`tools/check-night-order.ps1` 从术语表 + 两页快照重新派生并逐条比对（exit 0），作为独立证据固定；测试仍保留硬编码夹具——`references/wiki/` 是 gitignored，无法把它做成仓库内门禁 |
| 低 L1：门禁是精确大小写字符串匹配，改名（如 `DemoPlan`）即失守 | 已改：不区分大小写并覆盖 `demoplan` 变体，重做先红后绿 |
| 低 L2：R-0014 未记录"从跨剧本总表取本剧本保序子序列"这一取证前提 | 已补：R-0014 依据段新增「取证前提」 |

复核的"已核对通过"：四套序列与两页快照逐条同序、差异集合恰为 R-0014 的 4 类、
`NightOrderVariantDiffTests` 的索引断言（3/1/5/2/7/9/13/12/14）全部命中、
`src/` 无占位残留、旧事实均为已标注取代的历史段落、README 索引与票据 `Status` 一一对应、
门禁无假红假绿路径（含 `bin`/`obj` 跳过与 `git ls-files` 结果的存在性过滤）。

**受限说明**：第二棒按指示未复跑 `dotnet`，票据里的构建 / 测试 / 先红后绿数据由本会话运行产出、复核只做静态一致性核对。

## 决定与依据

- 剧本范围与两套口径：`docs/standard/rulings.md` R-0014
- 步骤表按剧本完整顺序表展开、空槽位照样走配额：`docs/decisions/active.md` D-0013
- 规则断言必须有来源：`docs/standard/sources.md` §5
- 建表与后续消费（含说书人选择口径的入口）：`docs/backlog/done/settlement-engine.md`

## 残余

- **建表**（顺序表 + 角色分配 → `StepPlan`）与角色契约（钟表匠 / 筑梦师 / 诺-达鲺）：已在
  `docs/backlog/done/settlement-engine.md` 落地（2026-10-02）；
  说书人选择口径的入口已在面板（`web/src/features/storyteller/OperationsControl.vue` 的「口径」下拉），
  批次 E2 用默认 `Recommended` 跑通。
- 规则层其余数据（角色类型元数据、相克表）仍未开始，属后续票据。
