# 涡流干扰计数：R-0004 的引擎级口径（数学家）

- Status: Done（批次 E18 逐行判定 1–5 全部通过，见「批次 E18 判定」）
- Priority: Medium
- Depends on: 涡流的「信息必假」约束（`done/win-loss-and-game-end.md`，R-0028）；失效账本与 `MalfunctionKind`（`done/settlement-engine.md`）；裁定 R-0004（已闭合）
- 来源：胜负票残余「涡流的信息必假只做到打标 + 归因」（`done/win-loss-and-game-end.md`）；`docs/standard/rulings.md` R-0004 / R-0028；百科《数学家》《涡流》《获取信息》《相克规则》《占卜师》《重要细节》《更换选择目标》（均 2026-10-01 抓取）

## 要解决的问题

涡流存活时，镇民通过能力获取的信息必假（R-0028）。此前平台只做「打标 + 归因」，**没有失效记录**：
数学家类「统计能力未正常生效」的能力实现时，"因涡流未正常生效"无处可数；R-0004 的逐条
`MalfunctionKind` 对表也一直挂着 Open。

## 决定与依据（R-0004 已闭合）

1. **计玩家，不计次数**：数学家的数字 = 窗口内出现过 ≥1 次失效的**玩家数**（同一玩家当天最多一枚标记）；
   窗口 = 上一个黎明到数学家被唤醒；数学家自身不计。
   依据《数学家》角色能力 / 角色简介 / 提示标记；《更换选择目标》与数学家的交互；《梦殒春宵》夜晚顺序表。
2. **涡流计入**：涡流存活时，镇民的信息类能力**照常「生效」**（平台不判信息真假，D-0002），
   但留下 `MalfunctionKind.Vortox` 记录。依据《数学家》角色简介 + 范例（五名玩家得到错误信息 + 醉酒女巫 =
   六名玩家能力未正常生效）、《涡流》角色简介（哪怕醉酒中毒也必假）、《获取信息》错误信息的格式第 3 条
   （看的是**玩家本人的角色**）。
3. **组合路径逐条并列、互不顶替**：中毒 + 醉酒、涡流叠加中毒……一次结算可命中多条，账本逐条记录；
   数字仍按第 1 条只算该玩家一次。依据《重要细节》三-3「醉酒与中毒状态不会互相抵消」与 R-0004 的「不硬塞、不抵消」。
4. **不计入**：相克规则（《相克规则》开头）、能力自身设定（《占卜师》规则细节）；咖啡师 / 说书人裁定
   两条路径尚未核对，**在补齐前不计入、也不得由引擎自行写这两类记录**。
   `MalfunctionCounting.CountsForMathematician` 是这张表的正向可执行表达（默认拒绝，不核对完不静默放行）。
5. **平台边界**：信息内容仍由说书人裁定（D-0002 / R-0028）；中毒 / 醉酒下的信息「可能错也可能对」，
   账本记的是机制事实，数字最终由说书人给出（《数学家》运作方式：说书人比划数字）。

## 落地

- 裁定与文档：`docs/standard/rulings.md` R-0004 改 Decided（含逐条对表）；`docs/architecture/current.md` §2.2、
  `docs/standard/character-rules.md`（数学家提示标记四条）、`docs/standard/terminology.md` 同步。
- 内核：`AbilityOutcome` / `AbilityResolvedEvent` 的失效分类改为**列表**；`AbilityEffectivenessEvaluator`
  对「中毒 + 醉酒」并列两条（不再记 Open）；`MalfunctionLedger.CountedSeats` + `MalfunctionCounting`。
- 规则层：`IAbilityResolution.InterferenceMalfunctions` 钩子；`VortoxInterference.MalfunctionsFor`
  （涡流存活 + 行动者**本人**角色是镇民）；钟表匠 / 筑梦师接入。
  `AbilityResolutionContext` 新增 `ActorOwnCharacter`（行动者本人角色；代行槽位上不等于契约检索键
  `ActorCharacter`，R-0036）。
- 投影与前端：说书人视图 / DTO 的 `Malfunctions` 字符串数组；`format.ts` / `LedgerPanel.vue` / `StepDigest.vue`
  并列显示多条原因；玩家投影不含失效面。
- 装置：`tools/verify-storyteller-panel.mjs` 两条旧口径断言（`未定（R-0004）`）改为断言并列原因串
  （`原因：中毒、醉酒`），避免被 note 文案顶成假绿；六个 `verify-*.mjs` 的 `waitUntil` 同步谓词守卫与
  `docs/development/agent-reference.md` §8 一并落地。
- 装置（E18 补，提交 `899eb8f`）：主装置新增 `vortox` 段——快档先按角色上报把 3 号换成涡流再开第 3 夜，
  取证档复用第三夜依赖块换好的状态、收尾后开第 4 夜；断言行 1（裁定点「信息必须为假」、账本
  「正常生效 + 原因：涡流」、失效账本 `dreamer / 涡流` 行）与行 4（全席页面扫描 + 信息单播）；重建段补行 5
  （重启 + 修复重建后失效账本行与最近结算序号原样）；落盘断言加「本次运行写入」时间校验；截图 35–38。

## 验收矩阵（定稿）

结论列按 `docs/acceptance/AGENTS.md` §5：只有批次运行的证据才算「通过」；下表「本轮证据」是测试侧证据，不顶替验收。

| # | 场景 | 期望 | 本轮证据（测试侧，不是验收） | 批次结论 |
|---|---|---|---|---|
| 1 | 涡流存活时镇民信息能力结算 | 账本落 `Vortox` 记录；能力本身仍 `Effective`；面板显示「原因：涡流」 | `VortoxMalfunctionHostTests.InfoAbilityUnderLivingVortox_LeavesStorytellerOnlyRecord`（真宿主 + 真 SQLite；先红后绿见下）；`WinConditionAbilitiesTests.VortoxInPlay_InformationAbilitiesDeclareVortoxMalfunction` | **通过**（E18 行 1：截图 35 / 36 / 37） |
| 2 | 组合路径（中毒 + 醉酒 / 涡流叠加状态） | 逐条记录、互不顶替；数字按玩家去重 | `AbilityEffectivenessEvaluatorTests.PoisonedAndDrunk_IsIneffectiveWithBothCauses`；`AbilityLedgerTests.MultipleCauses_LeaveOneEntryEach`；`MalfunctionLedgerTests.CountedSeats_...` / `CountsForMathematician_MatchesRuling` | **通过**（E18 行 2：主装置 177 项含钟表匠「中毒、醉酒」并列；新增涡流场景同屏给「正常生效 + 原因：涡流」） |
| 3 | 信息内容 | 仍由说书人裁定，平台不判真假 | 既有裁定点路径未改：`SettlementHostTests.PoisonedDreamer_...`（信息由说书人给、只下发当事人） | **通过**（E18 行 3：裁定点退化为自由填写，平台不生成、不判真假） |
| 4 | 视角 | 计数与失效归因只说书人可见 | `VortoxMalfunctionHostTests` 玩家投影序列化无失效面；`PlayerProjectionLeakGateTests`；零信任装置 `FORBIDDEN_PLAYER_KEYS` 含 `malfunctions` | **通过**（E18 行 4：5 席页面扫描 + 截图 38；零信任 44 项含 `malfunctions` 键扫描） |
| 5 | 重放 | 失效记录随事件流恢复，不重算 | `VortoxMalfunctionHostTests`（换宿主进程、同库重启后 `Vortox` 仍在）；`SettlementHostTests.PoisonedAndDrunk_...`（重启后两条原因都在） | **通过**（E18 行 5：重启 + 修复重建前后同为 `2 号 dreamer 涡流@245`） |

## 已落地与运行证据

- 先红后绿（新机制的守规验证）：先让钟表匠的 `InterferenceMalfunctions` 临时返回 `[]` →
  `dotnet test tests/OpenClockTower.Integration.Tests --filter VortoxMalfunctionHostTests` →
  **先红**：`Expected: ["Vortox"] / Actual: []`；恢复实现后同一命令 **后绿**：1/1 通过。
- `dotnet build OpenClockTower.slnx`：0 警告 0 错误。
- `dotnet test OpenClockTower.slnx`：**590 通过 / 0 失败**（内核 264 / 规则 208 / 集成 95 / 规范门禁 23）。
- `npm run gate`（web）：typecheck + lint + 93 单测 + 构建全绿。
- 主装置迭代档 + 强制重构建：`node tools/verify-storyteller-panel.mjs --build` →
  **148 项通过 / 2 项取证档专属跳过**，含改动后的两条失效账本断言。
- 独立对抗性自检（冻结工作树、另一上下文）：发现 3 中 4 低，本轮已全部处理——
  计数谓词改正向列举并 pin 全枚举、`ActorOwnCharacter` 语义修正 + 结算层用例、装置断言改为匹配并列原因串、
  文档措辞与前端渲染 key 修正。
- `waitUntil` 守卫探针：含该助手的 6 个文件全部「同步谓词通过 / 异步谓词抛错」；
  `verify-storyteller-panel.mjs` 本就没有该助手（自有等待器），不是漏改。

## 批次 E18 判定（2026-10-03，冻结版 `899eb8f`）

**本批取证构成**（按 `docs/acceptance/AGENTS.md` §3–§5；跑批期间工作树干净、未改产品代码）：

| 取证 | 命令 | 结果 |
|---|---|---|
| 主装置（本票主证据） | `node tools/verify-storyteller-panel.mjs --quota 2 --screenshots-all --build` | **全部通过（判定 177 项 · 跳过 0）**；39 张截图均为本次运行写入 |
| 胜负链路 | `node tools/verify-winloss.mjs --quota 2 --screenshots-all` | 全部通过（20 项） |
| 麻脸巫婆之夜 | `node tools/verify-pit-hag.mjs --quota 2 --screenshots-all` | 全部通过（34 项） |
| 女巫链路 | `node tools/verify-witch.mjs --quota 2 --screenshots-all` | 全部通过（28 项） |
| 处罚处决链路 | `node tools/verify-madness.mjs --quota 2 --screenshots-all` | 全部通过（28 项） |
| 零信任负向 | `node tools/verify-zero-trust.mjs --quota 2` | 全部通过（44 项；`FORBIDDEN_PLAYER_KEYS` 含 `malfunctions`） |
| 角色变更族 | `node tools/verify-character-change.mjs --quota 2 --screenshots-all` | 全部通过（43 项） |
| 门禁（冻结版） | `dotnet build` / `dotnet test OpenClockTower.slnx` / `dotnet format` | 0 警告 0 错误 · **590 通过 / 0 失败** · 退出码 0 |

本票 5 行逐行判定：

| # | 结论 | 本批证据 |
|---|---|---|
| 1 | **通过** | 主装置取证档第 4 夜（迭代档为第 3 夜）真实流程：涡流击杀槽代填 → 2 号收到筑梦师请求 → 裁定点写明「涡流在场：筑梦师（2 号）的信息必须为假（R-0028）……真角色不得出现在其中」（截图 `35-vortox-dreamer-decision`）→ 说书人自由裁定被受理 → 信息送达 2 号本人且带「信息可能是错的」提示 → 账本「最近一次结算：正常生效 / 2 号 的 dreamer / 原因：涡流 / 序号 245」+ 失效账本「2 号 dreamer 涡流」行（截图 `36-storyteller-vortox-ledger`）。 |
| 2 | **通过** | 同一次运行里：钟表匠首夜「中毒、醉酒」两原因并列（失效账本逐条），本批新增场景给第三条并列来源 `涡流`——同一结算里 `正常生效` 与 `原因：涡流` 同屏，互不顶替；按玩家去重由 `MalfunctionLedgerTests` 覆盖。 |
| 3 | **通过** | 涡流场景下信息内容没有引擎候选：裁定点要求说书人自行填写（「请填写要传达的两名角色或整句信息（真角色不得出现在其中）」），平台只记 `MayBeFalse` + 注记，不生成内容、不判真假（截图 35 / 37）。 |
| 4 | **通过** | 主装置 5 席页面扫描：无关席收不到涡流场景信息（`我收到的信息` 仍为「还没有收到信息」），全部玩家页无 `原因：涡流 / 未正常生效 / 失效账本 / malfunctions / 涡流在场`（截图 `38-unrelated-player-clean`；当事席 `37-player-vortox-info` 也只有信息本身）；协议层由零信任装置 44 项收包扫描 + 宿主 `PlayerProjectionLeakGateTests` 覆盖。 |
| 5 | **通过** | 主装置干净重建三项等价后，停宿主 + 损坏载荷 + 重启 + 修复重建：重启前后失效账本同为「2 号 dreamer 涡流」、最近结算序号同为 245（断言「随事件流恢复，不重算」）；宿主级由 `VortoxMalfunctionHostTests`（换进程、同库重启）覆盖。 |

截图复核：`35` / `36` / `37` / `38` 逐张人工复核，与断言一致（裁定点文案、账本两处、当事席信息、无关席空态）。

## 残余与后续（登记，不改判行结论）

1. **数学家的窗口取值与自身排除**：`CountedSeats` 只做全账按玩家去重；「上一个黎明到被唤醒」的窗口需要失效
   记录带白天号、数学家自身不计要在取值时排除其席位——随数学家角色实现（另票）。
2. **咖啡师 / 说书人裁定**两条路径仍待核对（R-0004 表内标注；补齐前不计入、引擎不得自行写入）。
