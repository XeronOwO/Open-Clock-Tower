# 旅行者与流放流程（首版纳入）

- Status: In progress
- Priority: High（需求方 2026-10-04 明确纳入首版；范围决策 D-0022）
- Depends on: `docs/decisions/active.md` D-0022（范围）、`rulings.md` R-0007 / R-0044 / R-0045 / R-0046 / R-0047（2026-10-04 机制清点收口）；无代码依赖，但会触碰胜败 / 白天 / 投影 / 复盘

## 要解决的问题

首版《梦殒春宵》目前不支持旅行者：剧本页列有 5 名旅行者（怪咖 / 集骨者 / 咖啡师 / 流莺 / 屠夫），
但界面没有加入旅行者的路径，白天也没有流放流程；需求方已确认纳入 MVP（D-0022）。本票要做：
旅行者加入 / 离开 + 流放流程 + 5 名角色能力 + 与之相关的胜败 / 投影 / 复盘口径。

## 机制清点（2026-10-04 完成）

### 来源补抓与工具漂移修复

- `tools/fetch-wiki.ps1` 页表补入 5 名旅行者（怪咖 / 集骨者 / 咖啡师 / 流莺 / 屠夫）——咖啡师此前
  已单独补抓、却漏在页表里（页表与索引漂移），本次一并修掉；
- 重跑抓取：`references/wiki-index.json` **88 → 92 页**，新增恰为怪咖 / 集骨者 / 流莺 / 屠夫；
  既有 88 页 sha256 逐字节零漂移、零丢失（全部 `fetchedAt = 2026-10-04`）。

### 六项待核对 → 结论

| # | 待核对 | 结论 | 登记 |
|---|---|---|---|
| 1 | 流放成功线在奇数人数下的取整 | 分母 = **在局玩家总数**（含生死、含旅行者）；阈值 `ceil(总数 / 2)`（推导口径，与 R-0017「≥ 一半」同读法） | R-0044 §5 |
| 2 | 流放表决的票面公开面 | 与 R-0017 提名投票**同一公开面**：发起 / 举手 / 当前席位 / 逐席冻结结论 / 票数全体可见 | R-0044 §7 |
| 3 | 旅行者死亡的公开面与触发 | 真实死亡：进公开生死面、照常获得投票标记、计入「死亡玩家」统计；白天流放即时公开、夜死走黎明公告；S&V 死亡触发族全是**自身**触发，旅行者角色没有死亡触发能力 → 不新增触发面 | R-0045 |
| 4 | 15+ 人开局带旅行者的初始分配 | 配板按**非旅行者人数**取 R-0041 的行；非旅行者上限 15，超出者必须为旅行者（16 人 = 15 人行 + 1 名旅行者） | R-0046 |
| 5 | 咖啡师的具体口径 | 免疫窗口 = **挂起不解除**（标记照记、暂不生效、到期照常移除、窗口结束恢复）；窗口内必定正确信息（含涡流）；两种效果都不写失效账本 | R-0047、R-0004 |
| 6 | 与既有流程的接缝 | 流放**不是提名 / 不是投票 / 不是处决**（三分流）；任意时刻、任意玩家（含死者）可发起；每名旅行者每天一次；死者表决不耗投票标记；能力不得影响流放流程，但**防死能力仍有效** | R-0044、R-0045 |

### 5 名角色的机制要点（实现依据；逐条来源见对应百科页）

| 角色 | 英文名（slug 待登记） | 能力要点 | 平台落点 |
|---|---|---|---|
| 怪咖 | Deviant（deviant） | 说书人判定「今天是否有趣」；有趣 → 当天不能被流放（免死） | 白天裁定点 + 流放死亡保护 |
| 集骨者 | Bone Collector（bone-collector） | 每局限一次，黄昏选择一名**死亡**玩家：重获其能力至下个黄昏；用后集骨者失去能力；集骨者死亡则终止；「重获」要求先失去（亡骨魔 / 僵怖仍保有能力的例外不重获）；被选者不被告知 | 能力授予 / 回收 + 夜序（其他夜晚·黄昏） |
| 咖啡师 | Barista（barista） | 每晚（黄昏）说书人二选一：① 目标清醒健康 + 必定正确信息至下个黄昏；② 目标能力可生效两次（含已用过的「每局限一次」）。受影响玩家得知是哪个效果 | 黄昏夜序（每晚）+ 裁定点 + 信息覆盖 + 免疫账本（R-0047） |
| 流莺 | Harlot（harlot） | 每夜（其他夜晚·黄昏）选一名**存活**玩家：对方同意 → 流莺得知其**角色**（不含阵营）；说书人可裁定两人当夜同死（黎明公告） | 裁定点（同意 / 拒绝）+ 私密信息 + 说书人额外死亡 |
| 屠夫 | Butcher（butcher） | 每个白天首次处决后，可再发起一次额外提名（可提名当日已被提名者、不占自己的提名次数），仍需正常票数；当日没有任何处决则不能用 | 白天流程（处决后的额外提名窗口） |

说明：

- 黄昏行动清单（《夜晚行动顺序一览》· 2026-10-04 抓取）：首个夜晚 = 咖啡师；其他夜晚 = 咖啡师 + 流莺 + 集骨者
  ——**流莺 / 集骨者的首个夜晚不行动**，以该夜序表为准（《旅行者》页的「可用夜晚能力」是通述，不逐角色）；
- 怪咖的「有趣」判定与「疯狂」同源（《疯狂规则如何运作？——疯狂的小精灵》以怪咖为判例），属说书人主观裁定，平台只承载裁定点；
- 屠夫与流放零联动（《屠夫》范例：「流放不是处决」）。

## 设计定稿（2026-10-04，待实施）

分层落点照既有四层：Kernel（纯迁移 + 事件 + 折叠）· Rules（花名册 / 夜序 / 能力 / 事实端口）·
Application（命令面 / 闸 / 投影 / 节拍器 / 会话）· Contracts + web（契约与两端呈现）。

### D1 旅行者角色与座次

- 花名册：`SectsAndVioletsRoster` 增加 5 名 `CharacterType.Traveller`；术语表 §9 登记 slug
  （deviant / bone-collector / barista / harlot / butcher）；`character-rules.md` 同步。
- **加入 = 新席位**：`GameSetup.Seats` 追加 `SeatTicket`（`IGameCatalog.SaveAsync` 覆盖保存，
  `GameSession` 需失效 `_setup` 缓存）；同一提交落 `TravellerJoinedEvent`（席位、角色、私密阵营、
  公开宣告「谁 + 角色 + 能力」）；邪恶旅行者的「得知一名存活恶魔」走私密信息面。
- **离开**：席位票据与座位号保留，内核账记离场事实（`DepartedSeats` 或等价账）；**离场者不计入任何
  「人数」口径**（流放分母、胜负、投票、收票顺序）。弃用「删席位」路径——它会把票据 / 重连 / 复盘
  三处语义一起搅动。
- 「**在局座次**」成为统一构造：Application 从账 + 会话座次派生，所有需要座次的调用点改读它。

### D2 流放流程（内核 + 收票复用）

- 新白天子状态 `ExileRecord`（发起人 / 目标 / 状态 / 收票 / 结论），挂 `DayRecord.Exiles`；
  `OpenExile` = 进行中的一条；每名旅行者每天一次（成败都算），同日可多次（顺序进行）。
- **收票复用**：把「逐席冻结」的推进抽成共享内核（顺序校验 + 冻结 + 恢复），提名与流放各持一份
  `VoteSweepState`；**事件类型分开**（`ExileSweepStartedEvent` / `ExileSeatVoteCollectedEvent` /
  `ExileSweepResumedEvent` / `ExileVoteCastEvent` / `ExileVoteCountedEvent`），提名事件形状不动
  （旧日志回放零影响）。
- 命令面：`ProposeExileCommand`（任何在局玩家，含死者；随时可提，含提名进行中）、
  `StartExileSweepCommand`（说书人）、`CollectExileSeatVoteCommand`（系统节拍器）、
  `ResumeExileSweepCommand`、`CastExileVoteCommand`、`CountExileVotesCommand`。
- **同一时间只走一条收票**（含提名）：提案可随时登记，钟盘串行；提名可在流放结清后继续。
- 判定：达线 = 支持数 × 2 ≥ **在局玩家总数**；不与他项比较；死者表决**不查也不耗**票权
  （`SpentVoteTokens` 只约束提名投票）。
- 收口：达线且未被保护 → 死亡（`reason = day.exile`）；未达线 / 被保护 → 存活（分别记失败原因）。
- 节奏与中断沿用 R-0017：`VoteSweepPacer` 增加「当前开放收票」的统一读取口，或抽出
  `BallotSweepPacer`；幂等键加入 ballot 标识。

**D2 实施口径（2026-10-04 实施前定案；细节以本块为准）**：

- **钟盘串行**：钟盘 =「未收完的那一条收票」。同一时刻至多一条未收完的收票；开始 / 继续收票时
  另一条未收完 → 显式拒绝 `day.ballot_in_progress`。提案（提名 / 流放）不受钟盘占用限制、随时登记；
  收票已收完但未计票的选票**不占**钟盘（计票可延后）；`CloseDay` / 强推要求所有未计票的选票都已结清
  （提名 `day.nomination_not_counted`、流放 `day.exile_not_counted`）。
- **分母与阈值**：收票席位快照 = 开始收票时的在局座次（R-0044 §6 分母时点）；达线 = 赞成票 × 2
  ≥ 快照席位数；不与当日其他结果比较（与提名的「严格最多」无关）。
- **表决资格**：全体在局玩家（按开始时的快照名单），含死者；死者**不查也不耗**投票标记。
- **收口**：达线且目标存活 → 即时死亡（`reason = day.exile`，进公开生死面、照常获得投票标记）；
  未达线 → 存活并记结论；死亡保护（怪咖）随 D3 接进同一收口点。
- **限额与顺序**：每名旅行者每个白天至多被提议一次（成败都算）；同一天可多次流放，顺序进行
  （同一天至多一条未结清的流放记录）。

### D3 免死（怪咖）与死亡收口

- 统一「死亡保护」查询：流放收口与 `CloseDay` 处决收口都先问保护（现 `CloseDay` 的注释写明
  「本票没有免死角色」——这正是要改的点）。
- 怪咖裁定点：白天作用域的说书人裁定「今天是否有趣」，达线时触发一次并记入事件流；结论为
  「有趣」→ 不产生死亡、不触发死亡触发能力（R-0045 §3）。保护不拦流放流程本身（票照收、数照记）。

**D3 实施口径（2026-10-04 实施前定案；细节以本块为准）**：

- **裁定时机（「实施时定」#3 定案 = 达线时）**：只有「这一票真的会决定怪咖的生死」时才需要裁定——
  一条流放满足「目标是该席位、收票已收完、票面达线（赞成 × 2 ≥ 收票席位数）、目标存活、
  且该席位的流放死亡保护尚未裁定」时，说书人的裁定才被接受。平台**不提前提问、不预缓存**；
  「当日缓存」= 日账里该席位当天的裁定本身（每席位每天至多一条，重放稳定）。
- **保护查询（统一收口）**：内核新增「死亡保护」契约族：`DeathProtectionCause`（死因）/
  `DeathProtectionOutcome`（`Protected` / `NotProtected` / `NeedsRuling` 待裁定 / `Indeterminate` 判定不了）/
  `DeathProtectionAssessment` / `IDeathProtectionSource` + `SettlementContext.DeathProtections` + 聚合查询。
  流放计票与 `CloseDay` 处决收口**都先问查询**（按死因）；`NeedsRuling` / `Indeterminate` 一律**显式拒绝**，
  不猜、不静默死亡。
- **两条收口**：
  - 流放计票达线且目标存活：受保护 → `ExileConclusion.Protected`，不产生死亡事件、不触发死亡触发能力；
    不受保护 → `day.exile` 死亡（现状）；待裁定 → `day.exile_protection_required`；
    判定不了 → `day.exile_protection_indeterminate`（补观测后再计票）。
  - `CloseDay` 处决收口（目标存活）：受保护 → 只记「被处决」、不产生死亡；待裁定 / 判定不了 → 显式拒绝。
    今日没有覆盖处决路径的保护来源 → 行为与现状一致（注释从「本票没有免死角色」改为查询口径）。
- **怪咖裁定命令**：新命令 `ResolveDayProtectionCommand { Seat, Protected, Note }`（说书人 / 宿主）→
  事件 `DayProtectionDecidedEvent`，折进 `DayRecord.ProtectionDecisions`。裁定只在该席位
  「流放已收完且达线」时被接受；已裁定 → `day.protection_already_decided`；目标生死未观测 →
  `day.protection_life_unknown`；席位不在局 → `day.protection_seat_unknown`；其余 → `day.protection_not_required`。
- **怪咖保护范围（新登记 R-0048）**：只作用于**流放致死**（能力文本「当天你不能被流放」+ 两则范例；
  《免死》分类页的通述不扩张角色自身范围）；死亡 / 醉酒 / 中毒 → 能力不生效（不受保护、不需要裁定）；
  维度未观测齐 → `Indeterminate`（显式拒绝，不猜）。
- **不拦流程**：保护不改变收票、票数、公开面与额度（R-0044 第 7 / 9 条）；同日后续流放照常。
- **D7 边界（预期）**：两端入口、`DayViewDto` 流放字段与复盘圆盘标记仍属 D7；本批只落命令面 / 事件 /
  日账与门禁所需接线，不做 UI。

**D3 期间发现（D4 已收口）**：提名 / 处决路径的旅行者接缝已按 `rulings.md` R-0049（Decided，2026-10-04）
收口——旅行者可被提名、计票不落靶、两条处决路径拒绝旅行者；上方「D4 实施口径」与 R-0049 是唯一口径，
旧「未收口」描述作废。

### D4 屠夫窗口

- `CloseDay` 拆两段：处决当前「即将被处决」者 → 若存在**可用屠夫**（存活、能力生效、当日已有处决、
  窗口未用）→ 产出 `ButcherWindowOpenedEvent`（白天保持 Open、标记额外提名可用）；否则 `DayClosedEvent`。
- 额外提名走新命令（不占当日提名次数、可提名当日已被提名者）；其收票 / 计票 / 处决走原链路，
  结清后 `CloseDay` 直接关闭（不再开第二个窗口）。当日无任何处决 → 不触发。
- 依据：《处决》· 第 5 步（「触发屠夫的能力，或立即宣布白天阶段结束」）。

**D4 实施口径（2026-10-04 实施前定案；细节以本块为准；裁定见 `rulings.md` R-0049 / R-0050）**：

- **窗口状态机**：`DayRecord.ExtraNomination`（授予席位 + `Open` / `Used`）；事件
  `ExtraNominationWindowOpenedEvent`（首次处决后打开）与 `ExtraNominationMadeEvent`（额外提名，窗口转 `Used`）。
  没有登记来源 = 无窗口（行为与 D4 前一致）；来源判定不了（生死 / 醉酒 / 中毒观测不齐、多个屠夫席位）→
  `day.extra_nomination_indeterminate` 显式拒绝，不猜。
- **两次处决的账**：`DayRecord.Executed` / `ExecutedKind` 改为 `Executions` 列表（保留派生读取面兼容），
  `ExecutedEvent` 折叠允许「首次」与「窗口已用后的第二次」；无窗口的第二次 / 第三次一律按损坏流抛错。
  第一次处决必须清空 `AboutToBeExecuted`（D4 前白天随即关闭不显形，D4 起白天保持 Open，必须显式清）。
- **槽位推进**：`DayStepMachine` 的 `CloseDayInput` 只在产出 `DayClosedEvent` 时推进槽位；产出窗口打开事件时
  白天计划保持 `DayWindow` 未完成（同一槽位继续额外提名 / 二次关闭）。
- **额外提名命令**：`NominateExtraCommand`（玩家，席位由凭据推导）→ `ExtraNominationMachine.Nominate`：
  窗口开着、发起人 = 授予席位、白天开着、同一时间至多一项提名；豁免《屠夫》明文允许的「每人每天一次」
  「每人每天被提名一次」两条当日限制，其余照旧。首次处决后提名阶段即结束：窗口期内常规提名一律拒绝
  （`day.nomination_window_closed`），只有屠夫的额外提名这一个入口（《处决》· 第 5 步）。
- **票数口径**：额外提名的「落靶」只看「赞成 × 2 ≥ 存活玩家数」且 ≥1 票（与普通提名同一阈值口径），
  不与其他已计票提名比较；R-0049 的旅行者收口同样适用（旅行者照样不落靶）。
- **边界**：流放不是处决（不触发）；处罚处决不触发窗口（《处决》「非常规处决不影响游戏流程」 +
  《洗脑师》《畸形秀演员》「直接进入夜晚」，R-0020 不变），但处罚处决同样不得杀旅行者（R-0049 第 4 条）。
- **R-0049 收口（随 D4 一并落）**：旅行者可提名；计票不落靶（票数照记、参与「最多票」比较）；
  `CloseDay` 与处罚处决两条路径拒绝旅行者目标；事实缺失显式拒绝。
- **D7 边界**：窗口的控制台 / 玩家端入口、`DayViewDto` 字段与复盘圆盘标记属 D7；本批不做 UI。

### D5 夜序（黄昏）与 5 名角色

- `NightOrderTable` 两口径同改：首个夜晚黄昏 = 咖啡师；其他夜晚 = 咖啡师 → 流莺 → 集骨者
  （插入 Dusk 步之后；不在场 / 已死亡自动落空槽，现有建表机制直接复用）。
- 咖啡师：黄昏槽 + 说书人二选一（目标 + 效果）+ 效果落账（免疫窗口按 R-0047；双次行动按当夜 / 当日）。
- 流莺：黄昏槽 + 目标同意 / 拒绝输入 + 私密信息 + 说书人「两人同死」裁定（黎明公告）。
- 集骨者：黄昏槽 + 选一名死亡玩家 +「重获能力」标记（下个黄昏移除、集骨者死亡终止、用后失去自身能力）；
  与「能力存续」家族（`IAbilityPresence`）和夜计划的接线在实施时细化。
- 怪咖 / 屠夫：无夜晚行动（白天面）。
- `tools/check-night-order.ps1` 目前只解析黄昏行、**不解析行内「（官员、窃贼…）」旅行者清单**：
  本票要么扩展它解析 dusk 行动清单（登记术语表 §9 后能自动核对），要么在装置/文档里显式记录
  「黄昏行动不由该脚本覆盖」。二选一，落地时写进本票。

### D6 配板与人数

- `SetupProposalQuery` 输入改为**非旅行者人数**；非旅行者 >15 显式失败（R-0046）；旅行者人数在
  说书人面显式表达；求解只覆盖非旅行者部分。
- 「在局座次」口径统一后，收票顺序 / 分母 / 胜负一律读它。

### D7 投影与两端

- Contracts：`DayViewDto` 增 `Exiles`（提议 / 收票 / 结论）与 `OpenExile`；钟盘收票视图沿用
  `DayVoteSweepDto` 形状，外层用 ballot 标识区分提名 / 流放。
- 公开面：流放发起 / 举手 / 当前席位 / 逐席结论 / 票数全体可见（R-0044 §7）；旅行者的**角色 + 能力公开、
  阵营不公开**；说书人专属字段不进玩家投影。
- web：说书人控制台（加入 / 离开 / 发起流放 / 流放钟盘）、玩家端（发起流放 + 举手 + 钟盘复用
  `VoteDial.vue`）、复盘把加入 / 离开 / 流放呈现为原子步骤。

### D8 验证顺序

- 内核用例（流放阈值 / 分母 / 票权 / 免死 / 屠夫窗口 / 胜负计数 / 集骨者重获 / 咖啡师窗口）→
  集成（加入 / 离开 / 提名中流放 / 同日多流放 / 死亡公告 / 配板）→ 前端单测 → 门禁三件套 +
  `npm run gate` → 装置迭代档 → 批次 E34 取证（`--quota 2 --screenshots-all`）。

### 实施时定（悬而未决，不许静默）

1. 离场用「账上集合」还是「座次表过滤」的最终形态（倾向账上集合）；
   — **第二批已收口**：账上集合 `GameState.DepartedSeats` + 唯一派生入口 `InGameSeats.Derive`
   （座次表过滤读账；见下方第二批记录）。
2. 提名 / 流放钟盘串行时的显式拒绝码与提示文案；
   — **D2 已定案**：钟盘 =「未收完的那一条收票」，冲突一律 `day.ballot_in_progress`（提示里点名
   当前占着钟盘的是第几项提名 / 第几条流放）；关账时未计票一律 `day.nomination_not_counted` /
   `day.exile_not_counted`（见上方 D2 实施口径）。
3. 怪咖裁定点的提问时机（达线时 vs 当日一次性）与当日缓存；
   — **D3 已定案**：达线时裁定（收票已收完 + 票面达线 + 目标存活 + 尚未裁定）；「当日缓存」= 日账里
   该席位当天的裁定本身，不提前提问（见上方「D3 实施口径」）。
4. 集骨者「重获能力」与 `IAbilityPresence` / 夜计划的接线方式；
5. `MalfunctionKind.Barista` 去留（R-0047 §5）。

## 实施进度（2026-10-04，第一批：D1 数据切片 + D6）

已落地（代码 + 测试 + 文档同一提交）：

- **D1（数据部分）**：`CharacterType.Traveller`；花名册增 5 名旅行者（deviant / bone-collector /
  barista / harlot / butcher），`AsSetupScript` 仍只出四类型池；术语表 §9 收录 5 人（咖啡师从
  「非首版」移入正表）、`character-rules.md` 增「旅行者（5）」节（2026-10-04 快照）；
  前端镜像 `labels.ts` 同步 30 人 + 旅行者类型标签（`RosterMirrorGateTests` 两侧对账）、
  `character-art.ts` 同步 5 张图，并按 R-0006 全量探活 30/30 通过（`200` + `image/png`）。
- **D6**：`SetupProposalQuery` 输入改为**非旅行者人数**（`ProposeSetup` 的显式参数，null = 全部席位）；
  分布表按它取行、求解只覆盖非旅行者；非旅行者 >15 显式失败（`setup.player_count_unsupported`，
  R-0046）；非旅行者人数越界显式失败（`setup.non_traveller_count_invalid`）；结果 / DTO 回显
  非旅行者 / 旅行者人数。
- **护栏**：旅行者不得走开局分配（`legality.character_not_assignable`）——初始阵营不能由类型推导
  （说书人私下裁定），杜绝分派层抛穿。

验证证据（2026-10-04，冻结工作树）：

- `dotnet build` 0 警告 0 错误；`dotnet test` **866 通过 / 0 失败**
  （门禁 24 / 内核 335 / 规则 316 / 集成 191）；`dotnet format` 0（未重写工作树）；
- `npm run gate` 0（typecheck + vitest **163** + vite build）；
- 装置迭代档 `node tools/verify-setup-randomizer.mjs --build`：**58/58 通过**
  （真宿主 + 真前端 + 5 席；配板建议 → 手改 → 提交 → 开夜的既有链路未断）；
- R-0006 热链全量复核：30/30 `200` + `image/png`（含 5 名旅行者；`artifacts/web/wiki-image-probe.log`）；
- 新增用例逐条在跑（过滤运行确认）：`Roster_ContainsTheFiveTravellers`、
  `ComposedBag_NeverContainsTravellers`、`ProposeSetup_WithTravellers_CoversNonTravellersOnly`、
  `ProposeSetup_OverFifteenNonTravellers_FailsExplicitly`、`ProposeSetup_NonTravellerCountBeyondSeats_FailsExplicitly`、
  `IllegalAssignmentsAndStartNight_AreRejected`（含旅行者分支）。

实现口径澄清（与设计定稿一致，写在这里供复核）：

1. 「旅行者人数在开局配置显式表达」本批以 `ProposeSetup` 的**显式参数**承载（非旅行者人数），
   暂不落 `GameSetup`；持久化席位模型（加入 = 追加席位 / 离场账）随 D1「加入 / 离开」切片一起定。
2. 配板建议把非旅行者角色绑在**低号席**（D1：旅行者以「追加席位」进入 = 高号席）；
   建议只是建议，说书人提交分配时可改绑。
3. 本批只覆盖「开局声明 + 配板」：16 席 = 15 人行 + 1 名旅行者的**第 16 席**还不能真正入局
   （加入流程未实现），因此 16 席局在加入切片落地前不能开夜——这是预期边界，不是缺陷。

## 实施进度（2026-10-04，第二批：D1 加入 / 离开与「在局座次」）

已落地（代码 + 测试同一提交；口径按设计定稿 D1，未改任何已登记裁定）：

- **命令面（Server + Application）**：`JoinTravellerCommand`（`Seat` 可空：null = 服务端追加新席位并
  签发新票据；指定席位 = 落在本局**尚未分配**的席位，如 15+ 开局提前占好的高号席）、
  `RemoveTravellerCommand`；Hub 方法 `JoinTraveller` / `RemoveTraveller`（说书人 / 宿主，任意时刻——
  含开局前与阶段中）。角色由说书人按玩家自选结果录入、阵营由说书人私下裁定；邪恶旅行者的揭示目标
  由说书人指定（一名或全部存活恶魔，百科口径允许二选一）。
- **内核事件与账**：`TravellerJoinedEvent`（事实 + 公开宣告；六维度初始条件由同批
  `SeatStateChangedEvent` 落地，与方古「事实 + 配套账事件」同款）与 `TravellerDepartedEvent`
  （席位账移除、`GameState.DepartedSeats` 登记、以该席位为来源 / 目标的持续型效果与它下达的疯狂要求
  以新 `EffectTerminationKind.SeatLeftGame` 终止）；公开生死面同步撤下该席位。
- **在局座次**：`InGameSeats.Derive(setup, state)` = 会话席位名单 − 离场账（R-0044 第 6 条），统一改造
  调用点：结算上下文（`SessionSettlement`）、胜负求值（`SessionCommit.EvaluateOutcome`）、投影座位
  （`GameSession.SeatList`）、开夜 / 开白天建表（`GameCommandDispatcher`）。
- **胜负口径**：`IWinConditionFacts.IsTraveller` + `OutcomeEvaluator.TwoPlayersAlive` 排除旅行者
  （R-0045 第 4 条：「除旅行者外仅剩两名存活」）。
- **私密揭示**：邪恶旅行者的 `InformationResultIssuedEvent` 只投影给本人（收件人白名单）；校验目标必须
  是**在局存活恶魔**；其他玩家只收到公开事实（席位 + 角色，无阵营）。
- **公开与复盘**：`PlayerEventKind.TravellerJoined` / `TravellerDeparted`（重连补齐的公开面）+ 视图刷新
  推送；`TravellerReplayPresenter` 把加入 / 离场登记为原子复盘步骤（D-0020 覆盖率门禁绿）。
  D7 仍需补：说书人控制台与玩家端的加入 / 离场入口与公告呈现、复盘圆盘标记。
- **票据签发**：追加席位走 `GameSetupFactory.AppendSeat`（席位号 = 当前最大 + 1，票据格式与开局一致）；
  `GameSession` 在同一把锁里「先存目录 → 再提交事件」，提交失败按补偿回滚目录；重复投递（同幂等键）
  按首次落库的 `TravellerJoinedEvent` 回填同一张票，不生成第二张。

验证证据（2026-10-04，冻结工作树）：

- `dotnet build` 0 警告 0 错误；`dotnet test` **885 通过 / 0 失败**（门禁 24 / 内核 342 / 规则 316 /
  集成 203）；`dotnet format` 退出 0；
- 新增用例（逐条在跑）：`TravellerSeatLedgerTests`（加入事实 vs 账、离场终止源 / 目标效果与疯狂要求、
  重复离场显式抛错、加入 / 离场不启动步骤机）、
  `OutcomeEvaluatorTests.TwoPlayersAlive_DoesNotCountTravellers`、
  `PublicLifeBoardFolderTests.TravellerDeparture_RemovesTheSeatFromThePublicBoard`、
  `GameStateComparerTests.DepartedSeats_AreComparedInOrder`、`InGameSeatsTests` 三条、
  `TravellerHostTests` 九条（16 席加入后可开夜、阶段中加入 / 离场保住步骤机、追加席位票据可加入且
  重复投递同票、邪恶揭示只到本人、离场后票据可重连且重建等价、加入 / 离场 / 揭示三组护栏拒绝）。

实施口径澄清（供复核）：

1. 「加入」支持两种落点（既有未分配席位 / 追加新席位 + 新票据）：16 席开局的第 16 席在创建时就已存在
   席位与票据，追加路径只服务真正的新到场玩家；两种落点共用同一个 `TravellerJoinedEvent`。
2. 离场用**账上集合**（`GameState.DepartedSeats`）：座次表过滤读它（`InGameSeats` 是唯一的派生入口），
   「离场者不计任何人数口径」不靠各处自觉。
3. 邪恶揭示由说书人给席位列表（一名或全部），平台只校验「在局 + 存活 + 恶魔」并转达——与 D-0002
   「平台不替说书人拍板」一致。

顺带记录（本轮的门禁处理）：`GameHub` 触到 600 行门禁——把 `ReleaseSeatBinding` 的编排拆进
`SeatJoinCoordinator.ReleaseBindingAsync`（行为不变：同样的存在性校验与日志，席位名读模型照旧更新），
Hub 只保留凭据闸与推送。这是门禁要求的「先拆再改」，不是顺手的重构。

顺带记录（本轮发现的接缝，留给 D5 / 加入切片）：

- 既有角色契约里「不能选旅行者」的目标排除（如筑梦师）——**第三批已收口**：全族复核见第三批记录，
  只有筑梦师需要改；女裁缝「可以选择旅行者」与诺-达鲺「跳过非镇民」经代码核对本就正确。
- `CommandGatePipeline` 触到 600 行门禁：本轮把「开局分配」「说书人注记」两个命令族的合法性拆成
  `AssignmentGate` / `AnnotationGate`，并抽 `SeatGate` / `GateRejections` 共用（行为不变，同一批用例守）。
  这是门禁要求的「先拆再改」，不是顺手的重构。

## 实施进度（2026-10-04，第三批：D2 流放流程）

已落地（代码 + 测试同一提交；口径按上方「D2 实施口径」，未改任何已登记裁定）：

- **内核**：`ExileRecord` / `ExileStatus` / `ExileConclusion` 与 `DayRecord.Exiles` / `OpenExile`；
  六条输入（`ProposeExile` / `CastExileVote` / `StartExileSweep` / `CollectExileSeatVote` /
  `ResumeExileSweep` / `CountExileVotes`）与六个事件；`ExileMachine` 负责提议资格、阈值
  （赞成票 × 2 ≥ 收票开始时在局座次快照）、死者不查不耗票权、达线且目标存活 → 即时死亡
  （`reason = day.exile`，R-0045）；`BallotSweep` 抽出提名 / 流放共用的「开始校验 / 严格顺序冻结 /
  继续」原语，提名事件形状未动（旧日志回放零影响）；`ExileLedgerFolder` 独立成册，折叠对损坏流显式抛错。
- **钟盘串行**（「实施时定」#2 定案）：`DayRecord.ActiveBallot` 是「当前未收完的那条收票」的唯一读取口；
  开始 / 继续另一条收票显式拒绝 `day.ballot_in_progress`；收票已收完但未计票的选票不占钟盘；
  `CloseDay` / 强推要求提名与流放都已计票（`day.nomination_not_counted` / `day.exile_not_counted`）。
- **Application / Server**：六个命令 + `ExileGate`（身份 / 形状，与内核同尺）+ `KernelInputMapper` /
  `GameCommandDispatcher` / `PendingChoiceGate` 接线 + Hub 五个公开方法；`SessionTrackers` 的收票锚点
  带上选票身份（族 + 序号），`VoteSweepPacer` 统一读取 `ActiveBallot` 并把到点输入翻译成提名 / 流放
  两种命令（幂等键前缀 `vote-seat` / `exile-seat`）；`GameNotificationBuilder` 把流放事件纳入白天刷新；
  `DayReplayPresenter` 认领六个事件（D-0020 覆盖率门禁绿）。
- **接缝收口**：离场闸拦住「流放未结清 / 钟盘收票进行中」的移出（`legality.traveller_exile_unsettled` /
  `legality.traveller_on_the_dial`）；`DayActions` 把怪咖 / 屠夫登记为白天相关但**未覆盖**——契约未实现前
  带它们的局开白天显式拒绝 `legality.day_contract_missing`（不静默跳过；D3 / D4 落地时翻覆盖）。
- **门禁拆类**：`GameCommandDispatcher` 与 `GameHub` 触到 600 行门禁，按「先拆再改」拆出
  `AnnotationCommandDispatch` / `HubActorResolver`（行为不变，同一批用例守）。
- **目标排除复核（D1 留下的接缝）**：筑梦师候选集合排除旅行者（《筑梦师》规则细节 4，
  `DreamerNightAction` + `InfoResolutionTests.DreamerPrompt_ExcludesTravellerSeats`）；全族复核
  女裁缝（可选择旅行者）/ 神谕者（计邪恶旅行者）/ 诺-达鲺（按角色类型跳过非镇民）/ 卖花女孩
  （流放不算投票）均核对无误，无需改动（逐条来源见 `character-rules.md`）。

验证证据（2026-10-04，冻结工作树）：

- `dotnet build` 0 警告 0 错误；`dotnet test` **919 通过 / 0 失败**
  （门禁 24 / 内核 367 / 规则 319 / 集成 209）；`dotnet format` 退出 0（未重写工作树）；
- 新增用例（逐类在跑）：`ExileMachineTests`（提议资格 / 钟盘串行双向 / 阈值边界 7 点 / 死者票权 /
  收票名册与离场 / 目标生死未观测 / 同日多条顺序进行 / 关日与强推闸）、
  `ExileLedgerTests`（五类损坏流显式失败 + 比较器覆盖流放账）、`DayActionsTests`（怪咖 / 屠夫进白天相关名单）、
  `InfoResolutionTests.DreamerPrompt_ExcludesTravellerSeats`、`ExileHostTests` 六条（提名中流放 + 串行拒绝 /
  未实现白天契约显式拒绝 / 死者不耗票且随后提名仍投得出去 / 离场不计分母 + 目标不得中途离场 /
  重启恢复后继续收票 / 节拍器自动收第 1 席）。

未做（属 D7 / D8）：`DayViewDto` / 玩家投影的流放字段与两端入口、复盘圆盘标记、真机批次取证。

## 实施进度（2026-10-04，第四批：D3 免死收口）

已落地（代码 + 测试 + 文档同一提交；口径按上方「D3 实施口径」与 R-0048，未改任何已登记裁定）：

- **内核契约族**：`DeathProtectionCause` / `DeathProtectionOutcome`（四态：受保护 / 不受保护 / 待裁定 /
  判定不了）/ `DeathProtectionAssessment` / `DeathProtectionContext` / `IDeathProtectionSource` +
  `SettlementContext.DeathProtections` + 聚合查询 `DeathProtectionQuery`（受保护 > 判定不了 > 待裁定 >
  不受保护；没有来源时行为与保护机制引入前一致）。
- **两条收口**：`ExileMachine.CountVotes` 在达线且目标存活时先问保护——受保护 → `ExileConclusion.Protected`
  （不产生死亡、不触发死亡触发）；待裁定 → `day.exile_protection_required`；判定不了 →
  `day.exile_protection_indeterminate`。`DayMachine.CloseDay` 的处决收口同样先问保护（受保护只记「被处决」、
  不产生死亡；待裁定 / 判定不了显式拒绝）——「本票没有免死角色」的注释随之换成查询口径。
- **怪咖裁定**：`ResolveDayProtectionInput` / `DayProtectionMachine` / `DayProtectionDecidedEvent` +
  `DayRecord.ProtectionDecisions`（每席位每天至多一条）；受理条件 = 该席位流放「收完且达线」+ 目标存活
  且未裁定（达线时裁定、不提前问），拒绝码 `day.protection_seat_unknown` / `day.protection_life_unknown` /
  `day.protection_already_decided` / `day.protection_not_required` / `day.protection_indeterminate`；
  `DayStepMachine` / `DayLedgerFolder` / `StepMachineFolder` / `GameStateMachine` 接线，
  `StepMachineStateComparer` 同步比对裁定账。
- **规则层**：`DeviantProtectionSource`（只覆盖流放致死；死亡 / 醉酒 / 中毒 → 不生效；无裁定 → 待裁定；
  维度不齐 → 判定不了）+ `RoleContracts.DeathProtections` + `SessionSettlement` 接线；
  `DayActions` 把怪咖翻进已覆盖——带怪咖的局现在可以开白天（原先 `legality.day_contract_missing`）。
- **命令面**：`ResolveDayProtectionCommand` + `ExileGate`（身份 / 形状）+ 分派 + Hub `ResolveDayProtection` +
  白天刷新通知 + 复盘步骤（含「达线但受死亡保护」结论文案）。
- **接缝登记（新发现）**：提名 / 处决路径目前不排除旅行者（可提名、可被计票送上「即将被处决」、
  可被 `CloseDay` 以 `day.execution` 杀死），与《术语汇总》处决条「杀死非旅行者」冲突——登记 R-0049（Open）
  与上方「D3 期间发现」，随 D4 一并处理。

验证证据（2026-10-04，冻结工作树）：

- `dotnet build` 0 警告 0 错误；`dotnet test` **941 通过 / 0 失败**
  （门禁 24 / 内核 375 / 规则 329 / 集成 213）；`dotnet format` 退出 0；
- 新增用例（逐类在跑）：`DayProtectionTests` 八条（保护四态聚合 / 流放计票两条分支 / 裁定受理与全部拒绝路径 /
  处决收口三分支 / 裁定账折叠损坏与重复拒绝）、`DeviantProtectionSourceTests` 十条（范围 / 裁定 / 酒毒与死亡 /
  观测不齐）、`DeviantHostTests` 四条（计票被拒 → 裁定 → 受保护存活 / 不受保护死亡 / 受理时机与重复裁定 /
  重启恢复后裁定仍可计票），以及 `ExileHostTests` 的「怪咖白天契约已覆盖」翻转、`DayActionsTests` 覆盖名单更新。

未做（属 D7 / D8）：`DayViewDto` / 玩家投影的保护字段与两端入口、复盘圆盘标记、真机批次取证；
处罚处决路径（洗脑师 / 畸形秀演员）今日未接保护查询——没有来源覆盖处决，行为与既有实现一致，
D4 / 未来免死角色需要时再收。

## 实施进度（2026-10-04，第五批：D4 屠夫窗口 + R-0049 收口）

已落地（代码 + 测试 + 文档同一提交；口径按上方「D4 实施口径」与 R-0049 / R-0050，未改任何未登记口径）：

- **R-0049 定案（Decided）**：以印刷规则书提取文本（2026-10-04 抓取 · Travelers / 词汇表）为准——
  「Travelers are exiled, not executed」「处决 = 杀死非旅行者」。旅行者照常可被提名、票数照记并参与
  「当天最多票」比较，但**永不进入「即将被处决」**；`CloseDay` 与处罚处决两条路径都拒绝旅行者目标，
  角色事实缺失 / 未观测显式拒绝（不猜）；旧日志 / 损坏流的旅行者目标由强推兜底关闭（不产生处决）。
- **内核（D4）**：`DayRecord.Executions`（执行列表，派生 `Executed` / `ExecutedKind` 兼容既有读取面）+
  `ExtraNomination`（窗口 `Open` / `Used`）；事件 `ExtraNominationWindowOpenedEvent` / `ExtraNominationMadeEvent`；
  `CloseDay` 拆进 `DayCloseMachine`：首次处决后按规则层来源开窗、白天保持 Open，第二次 CloseDay 执行后
  直接关账；`DayStepMachine` 只在产出 `DayClosedEvent` 时推进槽位；`CountVotes` 对额外提名只按
  「≥ 存活一半 + ≥1 票」落靶（不要求超过前票），并对旅行者目标收口；`AdjudicatedExecutionMachine`
  拒绝旅行者目标；`StepMachineStateComparer` 与折叠损坏流全接线。
- **规则层**：`ButcherExtraNominationSource`（在局 + 存活 + 能力生效；观测不齐 / 多屠夫席位 → 判定不了）+
  `RoleContracts.ExtraNominations` + `SessionSettlement` 接线；`DayActions` 把屠夫翻进已覆盖——带屠夫的局
  可以开白天；`WitchCurseTrigger` 对额外提名一视同仁（额外提名也是提名）。
- **命令面**：`NominateExtraCommand` + 玩家闸 / 合法性闸 + 分派 + `GameCommandFactory.NominateExtra` +
  Hub `NominateExtra` + 白天刷新通知 + 复盘两步（开窗 / 额外提名）。
- **文档**：`rulings.md` R-0049 转 Decided、新增 R-0050；`character-rules.md` 补旅行者接缝与屠夫平台口径；
  本票据「D4 实施口径」与验收矩阵行 9 / 新行 13。

验证证据（2026-10-04，冻结工作树）：

- `dotnet build` **0 警告 0 错误**；`dotnet test` **975 通过 / 0 失败**
  （门禁 24 / 内核 396 / 规则 339 / 集成 216；基线 941 → +34）；`dotnet format` 退出 0；
- 新增用例（逐类在跑）：内核 `ButcherWindowTests` 十四条（开窗三态 / 无来源与不可用照常关账 / 窗口边界 /
  额外提名两条豁免 / 只按半数落靶 / 二次处决与不再开窗 / 四项折叠损坏流）、`TravellerExecutionTests` 七条
  （提名照常 / 计票不落靶 / 高票仍压后续提名 / 旧流拒绝 / 处罚拒绝 / 事实缺失两态）；
  规则 `ButcherExtraNominationSourceTests` 九条（可用 / 失效三态 / 观测不齐 / 隐藏席位 / 多屠夫席位）、
  `WitchAbilitiesTests` 增补「额外提名触发诅咒」一条；集成 `ButcherHostTests` 两条（真宿主开窗 → 额外提名
  3 票与首轮持平仍落靶 → 二次处决 → 关账；无首处决不开窗）、`TravellerNominationHostTests` 一条
  （旅行者被提名 → 计票不落靶 → 关账无死亡）。
- 顺带修正：`DayPhaseFixture` 默认补非旅行者角色 + 角色事实端口（R-0049 起计票 / 关账要判旅行者）。

未做（属 D7 / D8）：窗口的控制台 / 玩家端入口、`DayViewDto` 的窗口字段与复盘圆盘标记、真机批次取证；
处罚处决仍未接死亡保护查询（无来源覆盖处决，行为与既有实现一致）。

## 验收矩阵

（维度细化；证据列在实现时逐行落）

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 加入 | 说书人任意时刻（含 15+ 开局）加入 1 名旅行者：玩家自选角色、说书人私下定阵营；其他玩家看到「谁 + 角色 + 能力」、看不到阵营；邪恶旅行者得知一名存活恶魔、不告知爪牙 | 集成 + 真机 |
| 2 | 离开 | 说书人可把旅行者移出游戏（移除角色与生命标记）；此后不计任何人数（流放分母、胜负、投票） | 集成 |
| 3 | 流放成立 | 任意玩家（含死者）任意白天时刻（含提名进行中）发起；不计提名；全员（含死者）逐席表决、死者不耗标记；支持 ≥ `ceil(在局总数 / 2)` → 死亡 | 集成 + 真机 |
| 4 | 流放边界 | 每名旅行者每日一次（成败均消耗）；同日多名旅行者多次流放；不占当日处决；与提名 / 处决并行不互斥 | 集成 |
| 5 | 能力不进流放 | 投票 / 处决类能力（卖花女孩 / 屠夫 / 涡流 / 女巫诅咒等）对流放零影响；管家类限制不影响流放表决；**防死能力仍有效**（怪咖有趣时不死亡、且不触发死亡触发） | 内核（`DayProtectionTests` / `DeviantProtectionSourceTests`）+ 集成（`DeviantHostTests`） |
| 6 | 阈值与分母 | 分母含死者与旅行者、不含离场者；奇数上取整；边界值（恰好一半 / 差一票）逐点判出 | 内核用例 |
| 7 | 胜败 | 旅行者不计入「仅有两名玩家存活」；流放不计入涡流「白天被处决」；其余胜败条件不受影响 | 内核用例 |
| 8 | 死亡面 | 流放死亡即时公开 + 照常获得投票标记；夜死走黎明公告；计入神谕者类死亡统计；无新增死亡触发 | 集成 + 投影用例 |
| 9 | 5 名角色 | 怪咖（裁定保护）/ 集骨者（重获与终止）/ 咖啡师（两效果 + 免疫窗口）/ 流莺（同意 + 共死）/ 屠夫（额外提名）逐条链路 | 怪咖：内核 `DayProtectionTests` + 规则 `DeviantProtectionSourceTests` + 集成 `DeviantHostTests`（D3）；屠夫：内核 `ButcherWindowTests` + 规则 `ButcherExtraNominationSourceTests` + 集成 `ButcherHostTests`（D4）；集骨者 / 咖啡师 / 流莺属 D5；真机装置属 D8 |
| 10 | 信息隔离 | 玩家端不出现旅行者阵营 / 说书人字段；复盘与实时投影一致 | 零信任门禁 + 装置 |
| 11 | 15+ 配板 | 16 人 = 15 人行 + 1 名旅行者；非旅行者人数 > 15（旅行者数不足）显式失败；旅行者人数在开局配置显式表达 | 内核 / 集成 |
| 12 | 复盘 | 加入 / 离开 / 流放 / 角色行动按原子步骤可见；进行中零泄露（R-0043 口径） | 投影用例 + 装置 |
| 13 | 旅行者-处决接缝 | 旅行者可被提名、票数照记并参与「当天最多票」比较，但永不进入「即将被处决」；`CloseDay` 与处罚处决都拒绝旅行者目标；事实缺失显式拒绝 | 内核 `TravellerExecutionTests` + 集成 `TravellerNominationHostTests`（R-0049，D4） |

## 决定与依据

- 范围：`docs/decisions/active.md` D-0022（需求方 2026-10-04 确认纳入首版）；
- 规则来源（均为钟楼百科 · 2026-10-04 抓取，哈希见 `references/wiki-index.json`）：《旅行者》
  《重要细节》一-4、《梦殒春宵》旅行者区与夜序表、5 个角色页、《投票》《提名》《处决》《术语汇总》
  《规则概要》《免死》《额外死亡》《死亡触发能力》《设计师总结的国内玩家对染的错误理解》；
- 规则口径：R-0007（范围）+ R-0044（流放语义）/ R-0045（死亡后果）/ R-0046（配板边界）/
  R-0047（咖啡师免疫）+ R-0004（数学家计数）+ R-0048（怪咖免死范围与收口）+
  R-0049（旅行者与处决路径接缝）+ R-0050（屠夫额外提名窗口）；
- 已知接缝：R-0049（Decided，D4 收口：旅行者可提名、计票不落靶、两条处决路径拒绝旅行者）；
- 相克面（2026-10-04）：5 名 S&V 旅行者在《相克规则》快照中 0 命中，不新增相克组合（R-0002 第 4 条）；
- 不做：实验性旅行者（黑帮 / 笑匠 / 侏儒等）；旅行者↔非旅行者转换（默认摇头口径）；
- 架构方案：见本文「设计定稿」（D1–D8 + 「实施时定」5 条）；实现按该节执行——要改口径，
  先改设计定稿与裁定，再改代码；
- 建议顺序：来源（已完成）→ 细则（已完成）→ 数据 / 内核 → 白天流放流程 → 5 名角色能力 →
  投影 / 复盘 → 真机与批次取证。
