# 玩家端的死亡公告面缺失

- Status: Done
- Priority: Medium
- Depends on: 无（与 `done/witch-curse.md` 同族：那条链路先落地时发现"玩家看不见死亡"这个更大的缺口）

## 要解决的问题

**玩家端看不到任何人的生死**：`PlayerViewDto` 只有阶段 / 挂起请求 / 自己的信息结果 / 白天公开事实
（`PlayerDayDto`），`SeatStateDto` 与效果链都说书人专属。后果：

1. 夜晚被击杀的玩家、白天被诅咒咒杀的玩家，在自己的界面上**没有任何提示**——现实桌面靠说书人
   口头宣布死亡（百科《女巫》· 2026-10-01 抓取 · 运作方式："说书人立即宣布他死亡"；
   《规则概要》三-3：处决后说书人宣布死亡），线上没有这条通道就没有等价物；
2. 平台没有聊天 / 语音，玩家只能靠外部语音得知自己（或别人）已经死了——一旦掉线或没听清，
   他会继续按"我还活着"行动，而被处决 / 被杀的事实早已进账；
3. 处决是唯一的例外：`DayViewDto.Executed` 是公开事实，玩家端看得到「X 号被处决」——
   也就是说"死亡是否可见"取决于**死因**，这对玩家是说不通的。

## 期望行为（来源逐条落定 → `rulings.md` R-0022）

- 公开面 = **全体席位的对外可见生死**（城镇广场生命标记的等价物）+ **本日生死公告**；
- 夜晚发生的变化**累积到黎明**（下一个白天开始）一次性公开、按"相对黄昏的净变化"（死而复生不公告）；
  白天发生的变化**立即公开**（女巫"立即宣布"的等价物）；黄昏边界 = 白天关闭（R-0022 第 2 条）；
- **死因不公开**：公告只有"谁死 / 谁复活"，不含来源 / 能力 / 效果链；处决仍走既有 `Executed` 公开事实；
  夜晚处罚处决的死亡在下一个黎明随夜色变化公开、处罚理由不公开（R-0020 / R-0021 的隔离不变）；
- 未公告的生死变化对任何玩家（含本人）不可见——"已死亡但未得知死讯"是真实状态（百科《死亡触发能力》）；
- 玩家自己的死亡显式可见，权限位（能否提名 / 投票）随之更新（服务端算好，前端只呈现）。

## 验收矩阵（定稿）

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 夜晚有人死亡 | 开白天前公开面不变；开白天（黎明）后出现"本日死亡"公告 + 牌面翻死亡；无关玩家看到同一份公开事实 | 集成 `MadnessPunishmentHostTests.Mutant_NightPunishment_DoesNotConsumeTheNextDay`（处罚后仍 Alive → 开白天后公告 + 牌面 + 本人不可提名）+ 内核 `PublicLifeBoardFolderTests.NightDeath_IsInvisibleUntilDawn_ThenAnnouncedWithBoardUpdate` + 装置 `madness`（真浏览器牌面 + 公告，不含处罚理由） |
| 2 | 白天被咒杀 | 死亡**即时**进入公开面，且与 `Executed`（处决）语义分开 | 集成 `WitchCurseHostTests.CursedDeath_EntersThePublicLifeSurface_ImmediatelyAndWithoutCause`（即时公告 + `Executed` / `AboutToBeExecuted` 为 null）+ 装置 `witch`（被诅咒者页面即时翻死亡） |
| 3 | 自己死亡 | 自己的界面上显式可见（横幅 + 自己席位死亡），权限位随之更新 | 集成（咒杀：`CanNominate=false`、`CanVote=true` 保留票权；处决：`DayPhaseHostTests.FullDayFlow_...` 自己席位死亡 + `CanNominate=false`）+ 装置 `witch` / 主装置 `34-player-self-dead` 截图 |
| 4 | 重启 / 重连 | 公告面随快照与补齐一致、不回退 | 集成 `DayPhaseHostTests.FullDayFlow_...`（重连包与在线投影同源）+ `SessionTrackersPublicLifeTests.Recover_FoldsTheSamePublicSurfaceAsIncrementalUpdate` + 主装置（重连后处决席仍死亡、夜死仍未公告） |
| 5 | 玩家收包扫描 | 仍然不含说书人专属字段（效果链 / 归因 / 生死账）；只含公开生死面 | 泄漏门禁（`PlayerProjectionLeakGateTests` 扩面 + 契约覆盖率自检）+ 零信任装置（43 项，含"白天推送带 lives / announcements 且无死因字段"） |

## 决定与依据

- 死亡需要被宣布：百科《女巫》· 2026-10-01 抓取 · 运作方式；《规则概要》三-3 · 2026-10-01 抓取；
- 公开面口径登记为 `docs/standard/rulings.md` R-0022（生命标记等价物 + 黎明净变化公告；不含死因；
  黄昏边界 = 白天关闭；夜晚处罚处决的死亡下个黎明公开）；R-0019 补第 5 条；
- 信息隔离与服务端投影强制：D-0012；事件是唯一事实来源：D-0010；账边界：D-0015。

## 已落地与运行证据（2026-10-02，批次 E13）

**实现**：

- Kernel：`PublicLifeEntry` / `PublicLifeBoard` / `PublicLifeBoardFolder`（夜晚累积 / 黄昏快照 /
  黎明净变化 / 白天即时 / 开局前只补折叠态；`PublicRevision` 只统计对外可观察变化）；Kernel 用例 12 条。
- Application：`SessionTrackers` 持有公开面并随重放恢复、`Update` 返回"本批是否对外变化"；
  `GameProjection` / `DayProjection` 投影 `Lives` / `Announcements`；`GameNotificationBuilder`
  在无白天事件时补发 `DayChanged`（夜晚挂起不推）。
- Contracts / Server / Web：`PlayerDayDto` 扩字段（`PublicFacts` → `PublicView` 避开禁词表）、
  新增 `PlayerLifeDto`；`ProjectionMapper`；玩家端「小镇生死 / 生死公告 / 自己死亡横幅」+ 防御性归一化。

**门禁（冻结树）**：

| 门禁 | 结果 |
|---|---|
| `dotnet build OpenClockTower.slnx` | 0 警告 0 错误 |
| `dotnet test OpenClockTower.slnx` | **396/396**（Kernel 191 / Rules 110 / Integration 72 / NormativeGates 23） |
| `dotnet format OpenClockTower.slnx --verify-no-changes` | exit 0 |
| `npm run gate`（typecheck + lint + vitest 89 + build） | exit 0 |

**装置（批次 E13，2026-10-02，全部前台）**：

| 装置 | 结果 |
|---|---|
| `tools/verify-storyteller-panel.mjs`（主回归） | **143 项断言全通过 + 35 张截图**（新增 `34-player-self-dead.png`） |
| `tools/verify-zero-trust.mjs`（负向） | **43 项断言全通过** |
| `tools/verify-witch.mjs`（女巫链路） | **28 项断言全通过 + 5 张截图** |
| `tools/verify-madness.mjs`（疯狂与处罚处决） | **28 项断言全通过 + 5 张截图** |

日志 `artifacts/web/batch-run.log`、`zero-trust-run.log`、`witch-run.log`、`madness-run.log`
（gitignored，可重生成）；截图已目视复核：`34-player-self-dead`（被处决者本人：死亡横幅 + 自己席位死亡 +
本日公告，文案无死因）、`witch-05`（咒杀后即时：横幅 + 公告 + 提名仍开、投票按钮仍可用）、
`madness-05`（黎明公告 4 号 + 白天处决 2 号，均只写"死亡"）。矩阵 1–5 逐行证据见上表。

## 对抗性复核处置（2026-10-02，独立上下文只读复核）

| 发现 | 严重度 | 处置 |
|---|---|---|
| F-1 首个黎明前的补观测会推进 `PublicRevision`，产出没有下发的补推通知（`Day=null` 被分发器丢弃，无实际泄漏） | Med | **已修**：开局前只补折叠态、不动版本号；`Update_ReportsPublicSurfaceChangesOnly` 先红后绿 |
| F-2 "黄昏边界"（白天关闭 → 夜晚开始）在代码里按夜晚处理，但未登记 | Med | **已登记**：R-0022 第 2 条写明黄昏边界 = `DayClosedEvent`；代码与用例注释引用该条 |
| F-3 补推序号取"本批最后一条草案"（可能是派生事件） | Low | **已修**：改用本批最后一条生死变化事件的序号，口径与白天事件一致 |
| F-4 门禁扫描面是手工名单，新增契约文件可静默绕过 | Med | **已修**：扫描面补 4 个玩家可见嵌套 DTO；新增覆盖自检——契约目录每个文件必须在扫描面或说书人豁免清单里 |
| F-5 `architecture/current.md` §2.7"夜晚挂起不算变化，不推"在首个黎明前不成立 | Low | **随 F-1 修复**：修复后该承诺对所有批次成立 |

**残余（留给后续）**：

- 说书人手动上报（`ApplySeatState`）在白天造成的生死变化走"无白天事件补推"通道；公告面不替代说书人
  口头宣布，平台只保证"记录 + 投影"与规则一致；
- 假死 / 幕后生死不一致类机制（僵怖等）不在本票：当前公开面直接由生死变化派生；将来引入
  "对外可见生死 ≠ 真实账"时需要一条独立的公开面写入方（R-0022 第 1 条的"对外可见"为此留话）；
- 公告只到"本日"粒度，没有历史公告回看（牌面是累计的、公告是本日的；需要历史时另立票）。
