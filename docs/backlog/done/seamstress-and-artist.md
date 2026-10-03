# 限次信息族：女裁缝 / 艺术家

- Status: Done（E26 判定通过；判定记录见文末「E26 验收判定」）
- Priority: High
- Depends on: 能力使用账本（`AbilityUseLedger`，哲学家的「每局限一次」先例 R-0036）；
  玩家对编码（`PlayerPairChoice`：理发师 R-0033 / 贤者 R-0038）；
  裁定点归属席位（`AttributionSeat`，R-0038 / R-0039）；
  信息结果与「可能为假」口径（百科《重要细节》三-1 / 三-3）；
  说书人整局事实投影先例（`FangGuInfection` / `BarberNight`，E25 面板）

## 要解决的问题

S&V 剩余 5 个未实现角色里的两名「获取信息 + 限次能力」角色，入场即被显式阻塞：

1. **女裁缝**在夜晚顺序表上（首夜与其他夜晚两套口径都有 `seamstress` 条目），但没有行动契约——
   角色在场时开夜被 `plan.contract_missing` 显式拒绝；「每局只能得到一次信息、用后从夜晚顺序表移除标记」
   这条限次约束没有承载。
2. **艺术家**在白天名单上（`DayActions` 的 `day_contract_missing`）——但平台当前没有
   「玩家在白天主动找说书人私下问答」的通道：操作请求是服务端推给玩家的、裁定点只有「槽位 / 触发」
   两种来源，白天窗口下从未开过裁定点。
3. 两者的**「失去能力」提示标记**（百科原件里放在角色标记旁的一次性标记）没有落点：
   说书人魔典上无标记、投影无字段、玩家重连后无从恢复「我还能不能用」。

## 机制清点

| 角色 | 能力时机 | 交互形态 | 平台推演口径 | 来源 |
|---|---|---|---|---|
| 女裁缝 `seamstress` | 每夜行动格（首夜 + 其他夜晚）；每局限一次 | 摇头不用 / 选**两名**其他玩家（可含已死）→ 说书人裁决「是 / 否（同阵营）」 | `pair:` 玩家对 + `decline`；裁定点归属 = 女裁缝；信息只到本人；使用即记账（含未生效）、放「失去能力」标记，**之后不再被唤醒**（空槽照走配额） | 百科《女裁缝》· 2026-10-01 抓取 · 角色简介 1–3 / 运作方式 1–6 |
| 艺术家 `artist` | 白天任意时刻，**玩家主动发起**；每局限一次 | 自由文本「是 / 否」问题 → 说书人答「是 / 不是 / 我不知道」（或「要求重问」） | 玩家命令 → 开裁定点（归属 = 艺术家）→ 回答走信息结果只到本人；前三答记帐并放「失去能力」标记；「要求重问」不记账、不落标记 | 百科《艺术家》· 2026-10-01 抓取 · 规则细节 1 / 角色简介 1–3 / 运作方式 1–11 |

共同点：使用即消耗（无论能力是否生效，百科《重要细节》三-3「使用机会被浪费」）；
信息内容由说书人给出、能力未生效时标「可能为假」（三-1）；标记只说书人可见（D-0012）。

## 验收矩阵

| # | 场景 | 期望 | 依据 |
|---|---|---|---|
| 1 | 女裁缝夜选两名玩家 | 裁定点归属 = 女裁缝、候选为除自己外的玩家对（含已死）；裁定后信息「是 / 否」只到本人 | 《女裁缝》运作方式 3–6 |
| 2 | 女裁缝摇头不用 | 不记使用、不落标记；之后夜晚仍被唤醒、仍可再选 | 《女裁缝》运作方式 4–5 |
| 3 | 女裁缝用后不再唤醒 | 用过（含醉酒 / 中毒时）之后，后续夜晚她的格不再开出选择，留一条可归因的跳过记录、配额照走 | 《女裁缝》运作方式 6；R-0036 第 2 条同族 |
| 4 | 女裁缝能力未生效 | 照常开格、照常裁定、信息照发并标「可能为假」；使用被浪费（标记落下） | 《重要细节》三-1 / 三-3 |
| 5 | 女裁缝已死亡 | 不唤醒（空槽）；复活 / 换角后按进入时求值 | D-0013 §1 |
| 6 | 艺术家白天提问 | 白天任意时刻本人可发起；问题私密（无关玩家零下发）；说书人端读到问题全文与归属 | 《艺术家》运作方式 2、4–7 |
| 7 | 艺术家四种回答 | 「是 / 不是 / 我不知道」结清并下发信息 + 记账 + 落标记；「要求重问」不记账、不落标记、可重新提问 | 《艺术家》规则细节 1 / 运作方式 8–11 |
| 8 | 艺术家醉酒 / 中毒 | 「无论是否醉酒中毒都放标记」：使用照常消耗、标记落下；信息按三-1 标注 | 《艺术家》运作方式 11；《重要细节》三-1 |
| 9 | 非法提问 | 不是白天 / 不是艺术家 / 已用过 / 已有未结问题 / 空文本或超长文本 → 显式拒绝，不产生事件 | 四道闸口径 |
| 10 | 挂起裁定挡推进 | 艺术家问题挂起时，白天推进类命令被拒（`phase.*_pending` 同族），说书人强推 / 收口是兜底 | D-0011 / R-0038 同族 |
| 11 | 失去能力标记 | 说书人视图出现标记（席位 + 能力），重放 / 重启后按账本原样恢复；玩家面不含 | 《女裁缝》运作方式 6；《艺术家》运作方式 9；D-0010 / D-0012 |
| 12 | 玩家重连 | 进行中问题、本人已用尽能力由快照恢复；收口 / 重问后状态清空 | D-0014；投标 §「断线重连」 |
| 13 | 幂等 / 重放 | 同一席位同一能力只记一次使用；事件流重折不重复开裁定、不重复记账 | 触发器 / 账本硬约定 |
| 14 | 相关族回归 | 哲学家「获得能力」、方古「限一次」、理发师 / 贤者的 `pair:` 选择、夜晚表四套口径不破 | 整族对齐 |
| 15 | 信息隔离 | 问题文本、回答、标记、归属均不进无关玩家收发包；零信任装置复跑 | D-0012 |

## 决定与依据

- **女裁缝选择形态**：复用 `PlayerPairChoice` 的 `pair:小+大`（升序规范化）+ `decline`（摇头）。
  候选 = 除自己外的任意两名**不同**玩家，可含已死。依据：《女裁缝》运作方式 3–5；
  理发师 / 贤者的原子选择先例（R-0033 / R-0038）。
- **女裁缝「用后不再唤醒」的平台承载**：建表期按 `AbilityUseLedger` 判定——已用（无论是否生效）
  → 该夜她的格走 `NoActionSlot`（空选项 + `Skip` + 可归因跳过记录），配额照走。
  依据：《女裁缝》运作方式 6「从夜晚顺序表上移除她的夜晚标记」；R-0036 第 2 条同族；
  D-0013 §1（空槽走配额，避免时序泄漏）。
- **「失去能力」标记 = 说书人投影的派生字段**：从 `AbilityUseLedger` × **限次能力集合**
  （本票登记 `seamstress` / `artist`）派生，按（席位，能力）去重；**不新增第二份事实**
  （D-0010：事件是唯一事实来源；账本已经是「用过没有」的权威）。呈现为魔典上角色标记旁的提示标记。
- **艺术家提问的命令面**：玩家主动命令（`AskArtistQuestionCommand`），白天窗口内、本人、一次；
  问题文本进事件流（`ArtistQuestionAskedEvent`）、只说书人 + 本人可见。
  依据：《艺术家》运作方式 4「是否提问由艺术家决定，而非说书人」；三-5「不是公开交流」。
- **艺术家裁定的承载与归属**：开 `DecisionPointRaisedEvent`（归属席位 = 艺术家；
  由白天窗口槽位承载、挂着不推进）；选项四种：`是` / `不是` / `我不知道` / `要求重问`。
  依据：《艺术家》运作方式 7–8；`AttributionSeat` 先例（R-0038 / R-0039）。
- **「要求重问」不消耗（平台口径，登记）**：规则允许说书人在无法用三种答案回答时要求重新提问
  （运作方式 8）；平台把它做成第四个裁定选项：清空进行中问题、不记使用、不落标记、允许重新提问。
  这条是**实质性的**（问题不合规时玩家不该被消耗），不是口头便利。
- **回答与消耗口径**：`是` / `不是` / `我不知道` 三者结清时：信息结果只到本人、记一次使用、
  落「失去能力」标记——无论能力是否生效（运作方式 9、11；三-3）。
- **挂起裁定挡推进**：与触发型裁定同姿态——未结清的白天裁定存在时，推进类命令（提名 / 投票 /
  计票 / 结束白天 / 开夜 / 强推之外的推进）被拒；说书人强推 / 收口是兜底（D-0011 / D-0014）。
- **玩家投影补两字段**：进行中问题（本人）、已用尽能力（本人）——由快照权威恢复，
  不靠前端本地记忆（D-0014）。
- **相克与非首版条目不做**：食人族等非首版角色、旅行者（首版没有）不在本票范围（R-0002 双角色口径）。

## 实现（本轮）

- **内核——限次记账边界**：`IAbilityResolution.CountsAsUse`（默认 true；摇头 / 不用返回 false，
  R-0036 / R-0040）——`AbilitySettlement` 只在计入时产出 `AbilityResolvedEvent`。
  **同轮修复**：旧实现无条件记账，哲学家的「摇头不用」被记成一次使用、下一夜不再被唤醒
  （与 `PhilosopherNightAction` 的声明和 R-0036 第 2 条矛盾）；回归见 `OnceAbilityUseTests` /
  `SeamstressArtistHostTests.PhilosopherDecline_DoesNotConsumeOncePerGame`。
- **内核——艺术家提问**：`ArtistQuestion`（进行中问题）+ `ArtistQuestionAskedEvent` /
  `ArtistQuestionClosedEvent`（`Answered` / `Returned` / `Abandoned`）+ `ArtistQuestionFolder`
  （折叠 / 阶段边界显式失败）+ `ArtistQuestionMachine`（Ask / Resolve、四种回答的结清、强推作废）；
  规则语义由注入的 `IArtistQuestionSource` 给出（内核不认角色 slug，与处罚处决同构）。
- **Rules——女裁缝**：`SeamstressNightAction`（INightAction + IAbilityResolution）：
  `pair:` 玩家对（除自己、含已死）+ `decline`；裁定「是 / 否」（含按当前账的推演与涡流必假注记）；
  信息只到本人；`CountsAsUse` 对摇头为 false。
- **Rules——建表**：`NightPlanBuilder` 对「已用尽的限次行动」走 `NoActionSlot`
  （「从夜晚顺序表上移除她的夜晚标记」，配额照走；`NoActionSlot` 参数化以支持女裁缝）。
- **Rules——艺术家**：`ArtistQuestionSource`（四种回答、回答文案、生效与涡流干扰、
  「要求重问」不消耗）；`RoleContracts.ArtistQuestions` 注册；`DayActions` 覆盖 artist。
- **应用 / 契约**：`AskArtistQuestionCommand`（玩家主动、席位由凭据推导）+ Hub 命令 +
  四道闸（身份 / 白天 / 挂起挡推进）；说书人视图新增 `LostAbilityMarkers`（由能力使用账本派生，
  不新增事实）；玩家视图新增 `PendingQuestion` / `CanAskArtistQuestion` / `ExhaustedAbilities`
  （都只对本人生效）。
- **Web**：魔典牌面「失去能力」标记（`mark-exhausted`）；玩家端「向说书人提问」面板
  （提问 / 等待回答 / 已用尽三态）；`playerViewMerge` 按序号合并三个新字段。
- **测试**：Kernel `ArtistQuestionMachineTests`（14 例）/ `OnceAbilityUseTests`（2 例）；
  Rules `SeamstressNightActionTests`（8 例）/ `ArtistQuestionSourceTests`（8 例）/
  `NightPlanBuilderTests` 女裁缝 +3；Integration `SeamstressArtistHostTests`（3 条真宿主链路）。
- **独立对抗性复核（前台只读子代理）**：M-1「提问后换角 → 结清死锁」已修（问题快照**提问时刻的角色**，
  与槽位结算按 `Owner` 取契约同姿态）；三条 minor 已修：`CanAskArtistQuestion` 加「无未结问题」条件、
  无关席位零下发的对抗性断言、两处恒真断言与一条弱断言；门禁复跑 748 通过 / 0 失败。
- **文档**：R-0040 登记；术语表「失去能力标记」；架构 §2.6 能力边界与白天边界、事件计数 48 → 50。
- **结构**：`CommandGatePipeline` 借本轮拆出 `ArtistQuestionGate` / `PendingChoiceGate`
  （600 行门禁，575 行）；`NoActionSlot` 参数化以支持女裁缝。

## 残余（E26 收尾后）

- **已消化（E26 首跑发现并当场修复，`cf4863f`）**：艺术家的提问入口 / 等待态原来只随 `JoinSeat`
  快照更新、没有任何在线推送通道——白天开始后入口在在线连接上永不出现，等待态还被面板 `v-if`
  的权限位门遮住。修法：新增「本人视图变更」推送（`ReceivePlayerViewChanged`），面板三态改
  `canAsk || pendingQuestion`；E26 装置与宿主用例已把四态锁进回归。
- 艺术家「要求重问」后的界面提示：重连场景下本地提示可能丢失，兜底是重新提问（服务端不拒绝）。
- 问题文本口径已定稿：trim 后非空、≤ 200 字符、拒绝控制字符（`artist.question_control`）。
- 女裁缝「若两名候选中有首版外角色」的相克条目不做（R-0002）。
- `CommandGatePipeline` 借本轮拆出 `ArtistQuestionGate` / `PendingChoiceGate`（575 行）；合法性闸
  （`CheckLegality` 一族）仍留在主类，若继续增长应在下一轮整族拆出。
- 已知边界（非本票引入，登记备查）：说书人对**其他**裁定点提交 `decision = null` 时仍会被受理
  （`DecisionPointResolvedEvent.Decision` 可空，历史上用于强推；内核只对艺术家问题显式拒绝空裁定）。
  若要收紧，应作为独立票据处理（影响所有裁定来源）。
- 装置侧同族修复（非本票引入，`2789e1b`）：`verify-death-triggers` / `verify-retro-info` 的
  SignalR 帧解析改为逐段解析、调用回执改用全量消息匹配（修「一帧合多条消息」导致的间歇假红）。

## E26 验收判定（2026-10-03，冻结版 `2789e1b`）

本批等待判定的票据一张（本票）。改动含**推送面**（艺术家提问状态的在线通道——E26 首跑发现并当场修复，
`cf4863f`）、应用 / 契约投影与 Web（面板三态），故按「本票装置 + 通用回归 + 激活路径 + 零信任」四面对齐
（四装置全部前台；首装置 `--build` 一次构建）：

- `tools/verify-seamstress-artist.mjs`（**本批新增**，取证档 `--quota 2 --screenshots-all --build`）：
  **82 项全部通过 / 0 跳过、退出码 0**。5 席（1 女裁缝 / 2 艺术家 / 3 呆瓜 / 4 畸形秀演员 / 5 方古）一条链路：
  夜 1 女裁缝在**玩家页**收到「选两名 / 摇头」（6 组 pair + decline、不含自己）→ 摇头提交（零裁定 / 零标记 /
  零信息）→ 白天 1 只有 2 号出现提问入口（idle）→ 提问后本人页等待态 + 说书人端问题全文 / 归属 2 号 / 恰好四答 →
  挂起时「结束白天」被拒 `phase.artist_question_pending` → **重连（刷新 = 快照）后等待态仍带问题全文** →
  「要求重问」不消耗、不落标记、输入框回来 → 再问并回答「不是」（信息 + `mark-exhausted` + 入口撤下）→
  夜 2 顺序表恶魔格（第 7 位）在女裁缝格（第 18 位）之前：击杀 2 号后女裁缝**再次被唤醒**（摇头不消耗）→
  选 `pair:3+4` → 裁定归属 1 号 + 推演行 + 是 / 否 → 信息只到 1 号 + 她的失能标记 → 夜 3 她的格走空槽：
  1 号页整夜零请求、槽上下文与说书人视图帧均含「不再被唤醒」、信息不增且标记仍在 →
  五席连接 116 帧零说书人字段（推演 / MayBeFalse / note / 失去能力）+ 信息只推本人 + 问题全文只进本人与说书人 +
  阳性对照。截图 `limitinfo-01…10` 均为本次运行写入并**逐张复核**（01 请求候选 / 03 四答裁定 / 06 艺术家失能标记 /
  07 女裁缝裁定与推演 / 09 两枚标记 / 10 夜 3 空槽上下文）。
- 主装置 `tools/verify-storyteller-panel.mjs`（同档）：**判定 194 项 / 0 跳过、退出码 0**；
- 角色变更族 `tools/verify-character-change.mjs`（同档）：**67 项全部通过**——哲学家代行 / 理发师换角 / 方古侵染回归；
- 零信任 `tools/verify-zero-trust.mjs`：**44 项全部通过**——伪造 / 冒用 / 越权 / 收包扫描。

真宿主用例 `SeamstressArtistHostTests` **4/4**（真 SQLite + 真 SignalR：女裁缝用后不再唤醒的事件流证据、
艺术家回答消耗 / 要求重问不消耗 / 挂起挡收口 / 失能标记；本批扩展的**在线推送断言**：白天开始入口下发、
提问后等待态下发、重问后入口回来、回答后用尽下发；跑批后追加仅测试的提问闸证据 `phase.not_open_day` + `artist.not_artist`）。

内核 / 规则证据：`ArtistQuestionMachineTests` 14 例 + `OnceAbilityUseTests` 2 例 + `SeamstressNightActionTests` 8 例 +
`ArtistQuestionSourceTests` 8 例 + `NightPlanBuilderTests` 女裁缝 3 例。

冻结版门禁（跑批时）：`dotnet build` 0 警告 0 错误；`dotnet test` **749 通过 / 0 失败**
（Kernel 327 · Rules 282 · Integration 117 · NormativeGates 23）；`dotnet format` 就地通过；
`npm run gate` 全绿（typecheck + lint + 113 前端单测 + build）。
跑批后仅测试增补一条（提问闸）→ Integration 118、全量 **750** 通过；产品代码未变（E19 先例）。

诚实记录：

- **E26 首跑在真机上发现交付缺陷并当场修复**：`canAskArtistQuestion` / `pendingQuestion` /
  `exhaustedAbilities` 只随 `JoinSeat` 快照更新，而白天开始与提问结清都没有推送通道——艺术家的提问入口
  在**在线连接**上永不出现（装置首跑的第一条红），等待态还叠加了面板 `v-if` 只认权限位的门。
  修法（`cf4863f`）：新增「本人视图变更」推送（`ReceivePlayerViewChanged`：提问 / 结清定向到本人，
  阶段边界与白天收口广播），客户端复用快照合并闸，面板三态改 `canAsk || pendingQuestion`；
  先红后绿：装置 34→82 项全绿、宿主用例补四条在线推送断言、web 接线 / 解析用例随 `npm run gate` 全绿。
- 装置侧同族修复（`2789e1b`）：SignalR 一帧可合多条消息（服务端把推送与调用回执写进同一帧），
  `verify-seamstress-artist` / `verify-death-triggers` / `verify-retro-info` 的解析器改为逐段解析、
  回执匹配改用全量消息，修掉「提交生效却等不到回执」的间歇假红（三装置迭代档复跑 82 / 70 / 53 全绿）。
- 本批未直接跑到的面（如实登记）：女裁缝「能力未生效」与「已死亡」、艺术家醉酒 / 中毒由规则级用例给结论
  （见逐行）；无真机中毒夹具，不声称装置覆盖。

### 逐行判定

| # | 结论 | 证据（本批运行） |
|---|---|---|
| 1 | 通过 | 装置 01 / 07 / 08（候选 6 组 pair 不含自己、裁定归属 1 号、是 / 否、信息只到本人）；宿主 `Seamstress_UsesOnce_ThenNeverWakesAgain`；Rules `Prompt_OffersPlayerPairsAndDecline` / `Prompt_OffersPairsRegardlessOfLife` / `Resolve_Yes_IssuesInformationToSelf` |
| 2 | 通过 | 装置：夜 1 摇头零裁定 / 零标记 / 零信息 + **夜 2 再次收到同一候选**；宿主 `PhilosopherDecline_DoesNotConsumeOncePerGame`（同族记账）+ 女裁缝宿主用例的摇头路径；Rules `Decline_SkipsDecisionAndResolve`；内核 `OnceAbilityUseTests` |
| 3 | 通过 | 装置夜 3（1 号页零请求 + 槽上下文 / 视图帧含「不再被唤醒」+ 信息不增 + 标记仍在，截图 10）；宿主 `Seamstress_UsesOnce_ThenNeverWakesAgain`（第二夜无请求、有可归因跳过事件）；Rules `SeamstressUsed_NoLongerWakes` |
| 4 | 通过（规则级） | Rules `Resolve_Ineffective_MarksMayBeFalse` / `Resolve_WithVortox_MarksMustBeFalse`；本批无中毒真机夹具（如实标注） |
| 5 | 通过（规则级） | Rules `SeamstressDead_IsEmptySlot`（不唤醒、空槽走配额）+ `NightSlotActivationTests` 同族的「进入时求值」口径（D-0013 §1） |
| 6 | 通过 | 装置 02 / 03（入口只对 2 号、等待态、说书人端问题全文 + 归属 2 号、无关席位页面零问题文本）；宿主 `Artist_AnswerConsumes_ReturnDoesNot` |
| 7 | 通过 | 装置 03 / 04 / 05 / 06（四答候选；要求重问不消耗 / 不落标记 / 可再问；回答「不是」→ 信息 + 标记 + 入口撤下）；宿主同用例（含在线推送四态）；Rules `Prompt_OffersFourAnswers` / `Resolve_Returned_DoesNotConsume` / `Resolve_Yes` / `Resolve_Unknown` |
| 8 | 通过（规则级） | Rules `Resolve_Ineffective_MarksMayBeFalse`（无论是否生效都消耗 + 信息按三-1 标注） |
| 9 | 通过 | 宿主 `AskArtistQuestion_RejectsOutsideDay_AndForNonArtist`（`phase.not_open_day` + `artist.not_artist` + 同日受理正对照）+ `Artist_AnswerConsumes_ReturnDoesNot`（`artist.already_used`）；内核 `ArtistQuestionMachineTests`（`artist.question_control` / `question_pending` 等）；装置：用尽后入口不再出现 |
| 10 | 通过 | 装置与宿主均断言 `phase.artist_question_pending`（界面回执 + 服务端拒绝码） |
| 11 | 通过 | 装置 06 / 09（两枚标记、title 记「能力已用尽」、夜 3 仍在）+ 帧 / 页面级零「失去能力」；宿主 `LostAbilityMarkers`（席位 + 能力）；`PlayerProjectionLeakGate` 收录 `LostAbilityMarkerDto` |
| 12 | 通过 | 装置 04（提问挂起中刷新重连 → 等待态 + 问题全文由快照恢复）；宿主 `PendingQuestion` 快照断言；内核 `ArtistQuestionFolder` 阶段边界显式失败 |
| 13 | 通过（内核级） | 内核 `OnceAbilityUseTests`（同一席位同一能力只记一次）+ `ArtistQuestionMachineTests`（重复 / 越界 / 空裁定显式拒绝）；重放口径由事件流折叠覆盖 |
| 14 | 通过 | 本批四装置回归（主 194 / 角色变更 67 / 零信任 44 / 本票 82）+ 同族装置解析修复后复跑（death-triggers 70 / retro-info 53）+ 全量 750 用例 |
| 15 | 通过 | 装置五席 116 帧零说书人字段 + 信息只推本人 + 问题全文只进本人与说书人 + 阳性对照；零信任装置 44 项 |
