# 验收批次记录：每一批判了什么、覆盖到哪里

> **目的**：规程在 `docs/acceptance/AGENTS.md`（怎么判）；本页是记录（每批判了什么、结论是什么）。
> 一批 = 一次构建 + 一次多客户端会话；一行验收只有「通过 / 不通过 / 无法判定」三种结论（规程 §5）。
> E13 及以前的判定写在各自票据里；本页从 E14 起集中记录。

## 批次 E14（2026-10-02）

五个装置同批跑完（一次构建 + 真宿主会话，全部前台）：

- 胜负链路 `tools/verify-winloss.mjs`（**本批新增**）：**20 项断言 + 3 张截图**——呆瓜被处决 →
  公开选择当场开出（候选不含已死的自己）→ 选中邪恶 → 玩家端与说书人端出现同一份结束结论 →
  结束后命令被拒 `phase.game_ended` → 旁观席位推送无越权字段；
- 主装置 `tools/verify-storyteller-panel.mjs`：**157 项断言 + 35 张截图**（默认花名册 3 席 → **5 席**：
  三席夹具在处决一人后即满足「仅剩两名存活 → 邪恶获胜」，属规则正确行为，见胜负票「同族对齐」）；
- 负向装置 `tools/verify-zero-trust.mjs`：**43 项断言**；女巫链路 `verify-witch.mjs`：**28 项**；
  处罚处决链路 `verify-madness.mjs`：**28 项**（后两者保留 4 席夹具：其流程的第二次死亡即真实结束，
  断言不依赖"结束之后还能继续"，故不改）；
- `npm run gate` 另有 89 条前端单测。

批次 E14 判出：

- **胜败判定与游戏结束票（`done/win-loss-and-game-end.md`）验收矩阵 1–11 全部通过**
  （逐行证据见票据；口径 R-0023…R-0028 已并入裁定登记表）；
- 上述五个装置同时复跑通过（E13 玩家端死亡公告面、E12 疯狂与处罚处决、E11 女巫、E10/E11 白天阶段、
  零信任、重建、健康位、魔典、玩家端新鲜度、重连补齐都在回归断言里）。

`web/src/features/storyteller/**` 与 `web/src/features/player/**` 的覆盖范围同批次 E12 / E10。

## 批次 E15（2026-10-02）

冻结版本：`main` @ `3cd6d2d`（跑批期间工作树干净、未改产品代码；唯一一次临时探针用例跑完即删，输出见票据「先红」段）。
六个装置同批跑完（一次强制构建 + 真宿主会话，全部前台，取证档）：

- 主装置 `tools/verify-storyteller-panel.mjs`：**162 项断言 + 35 张截图**（102.7s）；
- 麻脸巫婆之夜 `tools/verify-pit-hag.mjs`：**28 项 + 5 张截图**；
- 胜负链路 `tools/verify-winloss.mjs`：**20 项 + 3 张截图**；
- 女巫 `tools/verify-witch.mjs`：**28 项 + 5 张截图**；
- 处罚处决 `tools/verify-madness.mjs`：**28 项 + 5 张截图**；
- 零信任 `tools/verify-zero-trust.mjs`：**44 项**（无截图档）。

冻结版门禁复跑：`dotnet build` 0 警告 / 0 错误；`dotnet test` **465 通过 / 0 失败**（Kernel 226 · Rules 135 · Integration 81 · NormativeGates 23）；`dotnet format --verify-no-changes` 退出码 0；`npm run gate` 90 项 + typecheck / lint / build 全绿。两票相关真宿主用例过滤复跑 **6/6**（`EndedGamePendingRequestVoidTests` 4 + `WinLossHostTests` 2）。
与本批两票直接相关的 8 张截图已逐张复核（`pithag-01…05`、`winloss-01…03`），与断言一致。

批次 E15 判出：

- **结束批次作废挂起请求票（Medium）矩阵 1–8 全部通过** → 移入 `done/`。行 2（② 复判结束且请求仍 `Pending`）在当前规则面不可达，按票据既定姿态（唯一收口点 + 两段真机证据）判通过；界面级取证判定：无需单独用例（作废展示链路已由主装置覆盖，`GameEnded` 文案由前端单测锁定）。
- **麻脸巫婆票（High）判定不通过** → 移回 `todo/`：
  - **行 12「创造镜像双子」不通过**：探针运行显示「选择对立双子」裁定与 `evil-twin.pair` 均未出现（`AwaitingDecision=null`，机器照常越过槽位）；实现侧 `PitHagNightAction.BuildPostChoiceDecision` 返回 `null`，配对效果唯一来源仍是首夜 `EvilTwinNightAction`。
  - 行 3 / 4 / 13 / 15 **无法判定**（本次运行未覆盖该行关键面）：依次缺「在场仍记生效」的结算断言、「已死亡目标 + 来源自变后窗口仍有效」、「创造诺-达鲺的组合」、「含窗口的重启 / 重连重建」。
  - 其余 11 行（1 / 2 / 5–11 / 14 / 16）通过（逐行证据见票据「E15 验收判定」）。

装置脚本头注释、`backlog/README.md` 索引与相关票据指针已随移库同步。

## 批次 E16（2026-10-02）

冻结版本：`main` @ `2f8c4e6`（跑批期间工作树干净、未改产品代码；本批先给 `verify-pit-hag.mjs` 补 evil-twin 段并提交，再冻结跑批）。
六个装置同批跑完（一次强制构建 + 真宿主会话，全部前台，取证档）：

- 主装置 `tools/verify-storyteller-panel.mjs`：**162 项断言 + 35 张截图**（`--build` 强制重建）；
- 麻脸巫婆之夜 `tools/verify-pit-hag.mjs`：**34 项 + 8 张截图**（E15 的 28 项 + 本批新增第三夜「创造镜像双子 → 配对裁定」段 6 项）；
- 胜负链路 `tools/verify-winloss.mjs`：**20 项 + 3 张截图**；
- 女巫 `tools/verify-witch.mjs`：**28 项 + 5 张截图**；
- 处罚处决 `tools/verify-madness.mjs`：**28 项 + 5 张截图**；
- 零信任 `tools/verify-zero-trust.mjs`：**44 项**（无截图档）。

冻结版门禁复跑：`dotnet build` 0 警告 / 0 错误；`dotnet test` **475 通过 / 0 失败**
（Kernel 226 · Rules 139 · Integration 87 · NormativeGates 23）；`dotnet format --verify-no-changes` 退出码 0；
`npm run gate` 90 项 + typecheck / build 全绿。
本批 6 行对应用例过滤复跑：Integration `~PitHag` **7/7**、Rules `~PitHag` **14/14**。

批次 E16 判出：

- **麻脸巫婆票（High）重判行 12 / 3 / 4 / 13 / 15 / 11 全部通过**（其余 11 行 E15 结论累计有效）→ 移入 `done/`。
  行 12 首次拿到**界面级**证据：同夹具续走第三夜，麻脸巫婆把 3 号（第二夜创造的涡流）再变成镜像双子 →
  说书人面板开出「选择对立双子」裁定、候选恰为 1 / 4 号邪恶玩家 → 选定 4 号后 3 号牌面显示
  「镜像双子 爪牙 3 号 善良 存活」、4 号带「镜像双子·生效中」配对标记、裁定控件结清（`pithag-06 / 07 / 08`）。
- 本批后 `review/` 清空；`todo/` 剩 character-change-family（High）、vortox-interference-counting（Medium）、
  storyteller-annotation / terminal-hold-residue（Low）。

`pithag-06 / 07 / 08` 三张截图逐张复核（`pithag-01`–`05` 一并复核）；装置脚本头注释、`backlog/README.md` 索引
与相关票据指针随移库同步。

批次 E17 判出（2026-10-02，冻结版 `ca89e11`）：

- **角色变更族票（High）逐行判定 46 行全部通过**（舞蛇人 10 · 理发师 10 · 方古 13 · 哲学家 13；
  逐行结论与证据代号见票据「批次 E17 判定」）→ 移入 `done/`。
- 本批主证据是**新增家族装置** `tools/verify-character-change.mjs`（6 席：哲学家在真界面从镇民 / 外来者清单
  获得能力（不变身）→ 被选角色持有者醉酒、醉酒者**照常被唤醒** → 次夜在**自己的格**上代行获得的能力；
  方古首次成功杀外来者即侵染、「限一次」已用后普通死亡；理发师死亡当夜换角 + 尚未进入的格重绑）：
  取证档 **全部通过（43 项）**，截图 `cc-01…cc-10` 逐张复核（1 号牌面「获得能力」、2 号「醉酒」、
  状态账 `standing:philosopher.grant.drunk:1:2`、效果链两项终止于「来源失去能力」、新方古「邪恶 存活」、
  换角后 1 号变筑梦师 / 2 号变哲学家）。
- 回归：主装置取证档 **全部通过（判定 162 项 · 跳过 0）**；门禁 `dotnet test` **576 通过 / 0 失败**、
  `npm run gate` 92 项 + typecheck/lint/build。
- 装置夹具注意项：理发师换角应答在**迭代档**（0.3s/槽）下窗口只剩约两格，「尚未进入的格重绑」断言会时有时无；
  取证档（2s/槽）稳定通过、宿主用例亦稳定通过——先看节拍，再怀疑产品代码。
  **更正（E25 判出后）**：该"时有时无"实为**产品缺陷**的窄窗口（触发格应答晚于配额时，重进本格的第二次配额
  被幂等去重、计划永久停顿），已修复并补先红后绿回归——见 `backlog/done/sage-and-sweetheart.md`「残余补证」。
- 本批后 `review/` 清空；`todo/` 剩 `vortox-interference-counting`（Medium）、
  `storyteller-annotation` / `terminal-hold-residue`（Low）。

## 批次 E18（2026-10-03）

冻结版本：`main` @ `899eb8f`（先提交主装置涡流场景段，再冻结跑批；跑批期间工作树干净、未改产品代码）。
七装置同批跑完（一次强制构建 + 真宿主会话，全部前台，取证档）。跑批期间机器异常重启一次：主装置已完成，
六个辅助装置在同一冻结版本上重跑（全部通过，结果如下）。

- 主装置 `tools/verify-storyteller-panel.mjs`：**177 项断言 / 0 跳过**（`--quota 2 --screenshots-all --build`；
  39 张截图均为本次运行写入）；
- 胜负链路 `tools/verify-winloss.mjs`：**20 项 + 3 张截图**；
- 麻脸巫婆之夜 `tools/verify-pit-hag.mjs`：**34 项 + 8 张截图**；
- 女巫链路 `tools/verify-witch.mjs`：**28 项 + 5 张截图**；
- 处罚处决链路 `tools/verify-madness.mjs`：**28 项 + 5 张截图**；
- 零信任负向 `tools/verify-zero-trust.mjs`：**44 项**（无截图档）；
- 角色变更族 `tools/verify-character-change.mjs`：**43 项 + 10 张截图**。

冻结版门禁复跑：`dotnet build` 0 警告 / 0 错误；`dotnet test` **590 通过 / 0 失败**
（Kernel 264 · Rules 208 · Integration 95 · NormativeGates 23）；`dotnet format` 退出码 0。

批次 E18 判出：

- **涡流干扰计数票（Medium）验收矩阵 1–5 全部通过** → 移入 `done/`。行 1 / 4 首次拿到界面级证据：
  涡流存活 + 健康筑梦师结算 → 裁定点「涡流在场：信息必须为假（真角色不得出现）」、账本
  「正常生效 + 原因：涡流」+ 失效账本「2 号 dreamer 涡流」、无关玩家看不到归因；截图
  `35-vortox-dreamer-decision` / `36-storyteller-vortox-ledger` / `37-player-vortox-info` /
  `38-unrelated-player-clean` 逐张复核。行 5 由同一次运行的重启 + 修复重建段给出（前后同为 `2 号 dreamer 涡流@245`）。
- 装置补充：主装置新增 `vortox` 段（快档第 3 夜 / 取证档第 4 夜）；重建段补「重启后失效账本行与序号原样」；
  落盘断言加「本次运行写入」时间校验（提交 `899eb8f`）。
- 本批后 `review/` 清空；`todo/` 剩 `storyteller-annotation` / `terminal-hold-residue`（Low）。

## 批次 E19（2026-10-03）

冻结版本：`main` @ `fc9a41f`（跑批时工作树与该提交一致；跑批期间未改产品代码）。
本票的界面面只在"阻塞报警残留在终局"时出现，而阻塞需要数据缺陷级局面（见下），因此以真宿主用例判行、
以主装置取证档做回归：

- 主装置 `tools/verify-storyteller-panel.mjs`（取证档 `--quota 2 --screenshots-all --build`）：
  **177 项断言 / 0 跳过**（39 张截图均为本次运行写入，113.5s，退出码 0）——四夜 / 白天 / 重建 / 重连的
  通用回归（**该装置不走游戏结束**：它全程保持恶魔存活）；
- 胜负装置 `tools/verify-winloss.mjs`（同取证档）：**20 项 + 3 张截图**（退出码 0）——**真实结束批次**：
  处决呆瓜 → 公开选择 → 邪恶获胜 → 结束横幅（玩家端与说书人端同源）→ 结束后操作被 `phase.game_ended` 拒；
  截图 `winloss-01…03` 逐张复核（说书人端结束面 + 玩家端结束横幅 + 公开选择，与断言一致）；
- 真宿主用例（真宿主 + 真 SignalR + 真 SQLite）：`TerminalHoldResidueTests` **2/2**、`SlotUnblockTests` **6/6**；
- 冻结版门禁：`dotnet build` 0 警告 / 0 错误；`dotnet test` **598 通过 / 0 失败**
  （Kernel 270 · Rules 208 · Integration 97 · NormativeGates 23）；`dotnet format` 退出码 0（未改写任何文件）；
- `npm run gate` 未跑：本票无 `web/` 改动，按门禁规则跳过。
- 本票产品代码提交为 `fc9a41f`；其后一笔提交只含独立对抗性复核后的**测试与文档**增补（不改产品代码），
  故本批真机证据对产品面继续有效。

批次 E19 判出：

- **终局残留挂起票（Low）验收矩阵 1–8 全部通过** → 移入 `done/`。行 1 / 4 / 6 / 7 由真宿主用例给出
  （同批解除、只清阻塞、排序、日志），行 2 由「无阻塞不产生多余事件」用例给出，行 3 由「整条事件流重折 +
  同库重启」给出，行 8 由内核三条显式失败用例给出（逐行证据见票据「E19 验收判定」）。
- **装置不新增段的理由（诚实记录）**：阻塞报警需要「空槽位却绑着存活持有者、而这一格没有契约」这类
  数据缺陷（或提示求值落 `BlockAndAlert` 的真实局面），真机花名册走不到；主装置取证档因此只作回归，
  不作行级证据。
- 随票订正 `docs/architecture/current.md` 的 `GameEvent` 计数：25（陈旧）→ 41。
- 本批后 `review/` 清空；`todo/` 剩 `storyteller-annotation`（Low）。

## 批次 E20（2026-10-03，残段补证）

冻结版本：`main` @ `34442f2`（先提交家族装置第二局，再冻结跑批；跑批期间工作树干净、未改产品代码）。
本批没有等待判定的票据，只补 E17 残余②的界面级证据，因此只跑改动所在的家族装置：

- 角色变更族 `tools/verify-character-change.mjs`（取证档 `--quota 2 --screenshots-all --build`）：
  **59 项全部通过、退出码 0**——第一局（被获得角色**在场**）43 项回归 + 第二局（哲学家选**不在场**的钟表匠
  → 当夜钟表匠的格就地激活由他代行）16 项；截图 `cc-01…cc-14` 由本次运行写入，其中 `cc-11…cc-14`
  逐张复核（清单含钟表匠、效果链「获得能力：钟表匠（clockmaker）」且无醉酒、槽位 9/13 =
  `sv:night-1:clockmaker:decision`、账本「1 号 的 clockmaker 正常生效」+ 信息下发到 1 号玩家端）。
- 其余六装置未跑：本批没有它们的改动，也没有等待判定的票据（诚实记录，不用"全装置回归"顶替）。

批次 E20 判出：

- **E17 残余②「哲学家选不在场角色 → 同夜代行」以界面级证据闭合**（逐条见
  `done/character-change-family.md` 的「残余补证 · 哲学家选不在场角色」）；票据的 46 行判行结论不变。
- 本批后 `review/` 仍为空；`todo/` 仍只有 `storyteller-annotation`（Low）。

## 批次 E21（2026-10-03，数学家角色本体）

冻结版本：`main` @ `365c102`（先提交实现 + 装置 `5e370f7`，再补装置的涡流段 `365c102` 并冻结跑批；
跑批期间工作树干净、未改产品代码）。本批等待判定的票据一张：`mathematician`，只跑它对应的装置：

- 数学家装置 `tools/verify-mathematician.mjs`（取证档 `--quota 2 --screenshots-all --build`）：
  **38 项全部通过、退出码 0**。真宿主 + 真 Vite + 真 Chromium，6 席（1 诺-达鲺 / 2 筑梦师 / 3 数学家 /
  4 畸形秀演员（白天上报为涡流）/ 5 呆瓜 / 6 钟表匠），两夜：
  首夜——两名中毒信息角色先结算（失效账本两条 `中毒`）→ 数学家入槽时裁定提示「按失效账本推演：2」
  （计划期快照会是 0，这一行同时证明「入槽实时重建」真的生效）→ 结清「2」→ 数学家玩家端只出现
  `mathematician 2`，无关席位 SignalR 与浏览器零下发；跨黎明——开白天 / 结束白天后，在真界面把
  4 号上报为涡流（涡流在场）→ 第二夜诺-达鲺击杀筑梦师、涡流击杀已死者（无事发生）→ 数学家提示
  「推演：0」且注明「必须为假」+ R-0028 → 结清假数字「1」→ 玩家端累计两条。
  截图 `math-01…math-05` 由本次运行写入并**逐张复核**（首夜推演 2 / 「当前步骤」摘要同一份实时提示 /
  玩家端 `mathematician 2` / 失效账本两条中毒 / 第二夜推演 0 + 必须为假 + 4 号涡流在场）。
- 补强过程（诚实记录）：`5e370f7` 上先跑过一次 34 项（无涡流段）；随后补「涡流在场 → 必假注记与
  假数字下发」4 项断言（38 项）、提交为 `365c102` 并在该冻结版本重跑完整取证档——批次证据以后者为准。
- 其余七装置未跑：本批没有它们的改动，也没有等待判定的票据（诚实记录，不用"全装置回归"顶替）。
- 门禁（本轮工作树；`365c102` 只改装置脚本，C# 面与冻结版本一致）：`dotnet build` 0 警告 0 错误；
  `dotnet test OpenClockTower.slnx` **618 通过 / 0 失败**；`dotnet format` 退出码 0；`web/` 无源码改动。
- 观察（非缺陷）：全量集成套件并行负载下 `BarberHostTests.BarberExecutedDuringDay_…`（0.05s 配额夹具）
  出现过 3 次「席位 4 超时未收到请求」（单跑与两用例并行均绿）；跑批期间本机还发生一次异常重启，
  重启后全量 **618 全绿**（集成段 9s）。同轮把失败现场（阶段 / 槽位 / 待裁定 / 阻塞）写进该夹具的
  失败信息（`BarberHostTests.WaitForRequestAsync`），并在收尾清掉崩溃残留的 32 个 `%TEMP%\oct-*` 库文件。

批次 E21 判出：

- **数学家票验收矩阵 10 行全部通过**（逐行证据见 `done/mathematician.md` 的「E21 验收判定」）→ 移入 `done/`。
- 本批后 `in-progress/` 与 `review/` 为空；`todo/` 仍只有 `storyteller-annotation`（Low）。

## 批次 E22（2026-10-03，说书人注记）

冻结版本：`main` @ `9f1796d`（一次提交含实现 + 装置段落 + 文档；跑批期间工作树干净、未改产品代码）。
本批等待判定的票据一张：`storyteller-annotation`，跑它所在的通用装置：

- 主装置 `tools/verify-storyteller-panel.mjs`（取证档 `--quota 2 --screenshots-all --build`）：
  **194 项全部通过 / 0 跳过、退出码 0**（110.7s；39 张截图均为本次运行写入）。新增 `annotation` 段 16 项：
  选中席位 → 注记区初始为空 → 新增（序号 11）→ 牌面 token（截断显示 + 全文 `title`）+ 操作台全文 +
  状态账面板无该文本（D-0015 反向）→ 改（序号 12，同一 `data-note-id` 原地更新）→ 5 席玩家页零文案 / 零锚点
  + 5 席活动快照 4 次采样不变 → 删（序号 13）后 token 消失 → 再写一条留给重启断言；
  `rebuild` / `reconnect` 段新增：真宿主停机 → 损坏事件载荷 → 重启 → 修复重建后注记仍在牌面。
  新增截图 `39-grimoire-annotation` / `40-grimoire-annotation-edited` / `41-annotation-player-clean` /
  `42-grimoire-annotation-after-restart` 逐张复核（牌面 token 与操作台、玩家端干净、重启后仍在）；
  其余 35 张为既有链路回归。
- 真宿主用例 `AnnotationHostTests` **6/6**（增改删链路 + 重连包零下发 + 玩家连接零推送 + 玩家凭据被身份闸拒 +
  越界输入各拒绝码 + 文本归一化 + 幂等 + 重启后水位恢复）；内核 `SeatAnnotationTests` **12/12**
  （折叠顺序 / 标识不复用 / 顺序损坏抛错 / 不动状态账与步骤机 / 每席计数 / 文本口径）。
- 门禁（冻结版本）：`dotnet build` 0 警告 0 错误；`dotnet test OpenClockTower.slnx` **637 通过 / 0 失败**；
  `dotnet format` 退出码 0；`npm run gate` 通过（99 前端单测）。
- 结构门禁的一次真实拦截（诚实记录）：首版实现给 `GameHub` 加了 3 条命令方法后该文件 612 行、
  被 `SourceFileLengthGateTests` 当场拦下；按「超限先拆再改」把 wire→命令的翻译与参数层审计拆到
  `src/OpenClockTower.Server/GameCommandFactory.cs`（Hub 只留连接 / 凭据 / 调用 / 推送），拆分后
  `GameHub` 467 行、行为由既有 105 条集成用例与 99 条前端单测回归确认。
- 其余七装置未跑：本批没有它们的改动，也没有等待判定的票据（诚实记录，不用"全装置回归"顶替）。

批次 E22 判出：

- **说书人注记票验收矩阵 8 行全部通过**（逐行证据见 `done/storyteller-annotation.md` 的「E22 验收判定」）→ 移入 `done/`。
- 本批后 `in-progress/`、`review/`、`todo/` 均为空；`done/` 新增 `storyteller-annotation.md`。

## 批次 E23（2026-10-03，回溯型信息族）

冻结版本：`main` @ `670ce98`（先提交实现 `6bd5a49`，取证中发现装置等待窗在 2s/槽下不够，修正为 `670ce98` 后再冻结跑批；
其后 `4943ec0` 只增一条规则用例、不改产品代码，故本批真机证据对产品面继续有效）。本批等待判定的票据一张：`retrospective-info-family`。

本批改动含**白天账折叠**与**提示上下文**两条跨切面，故按「本票装置 + 通用回归 + 激活路径 + 零信任」四面对齐（四装置全部前台、一次强制构建）：

- `tools/verify-retro-info.mjs`（**本批新增**，取证档 `--quota 2 --screenshots-all --build`）：**53 项全部通过 / 0 跳过、退出码 0**。
  白天「2 号麻脸巫婆自我提名 + 1 号诺-达鲺投赞成 + 4 / 5 号投票」→ 处决 2 号 → 第二夜恶魔击杀 6 号 →
  三张裁定点依次按记录推演（**按白天账推演：是** / **按白天账推演：是** / **按当前账推演：1**，且裁定点标识逐个换新、
  不是上一条残留）→ 信息只到本人且互不串台 → 无关席位 2 / 6 号零下发（六席连接 152 帧全量扫描，无说书人字段）。
  截图 `retro-01…retro-05` 均为本次运行写入并**逐张复核**（卖花女孩裁定点 / 城镇公告员裁定点 / 神谕者裁定点 /
  3 号玩家页信息 / 6 号无关席位空态）。
- 主装置 `tools/verify-storyteller-panel.mjs`（同档）：**判定 194 项 / 0 跳过、退出码 0**（39 张截图为本次运行写入）——
  白天票面 / 裁定点实时重建 / 涡流 / 重建 / 重连 / 注记全线回归；
- 角色变更族 `tools/verify-character-change.mjs`（同档）：**59 项全部通过**——槽位激活路径回归（本轮给
  `NightSlotActivation.Plan/PlanGranted` 追加了「最近白天账」参数）；
- 零信任 `tools/verify-zero-trust.mjs`：**43 项全部通过**——白天事件新增的角色快照字段不进玩家面、不下发。

真宿主用例 `RetrospectiveInfoHostTests` **1/1**（真 SQLite + 真 SignalR：同一白天事实 → 三条提示推演「是 / 是 / 1」→
只到本人 + 无关席位零下发；玩家收到的载荷不含 `MayBeFalse` / `Note`）。
冻结版门禁：`dotnet build` 0 警告 0 错误；`dotnet test` **660 通过 / 0 失败**（用例增补后 661）；`dotnet format` 退出码 0；
`npm run gate` 通过（本轮含 `web/AGENTS.md` 指令文档增补）。

诚实记录：

- **首轮取证跑出 1 条红**：「首夜自然走完（未强推）」——2s/槽下首夜 13 格 ≈ 26s，装置原固定 12s 等待窗不够、退回强推。
  判定为**装置侧**问题并修正（等待窗按档位缩放，`670ce98`），随后同档重跑全绿；产品面无改动。
- 本批两台真机夹具里 3 / 5 号恰是诺-达鲺隔位相邻的镇民 → **常驻中毒**（动态对账）：卖花女孩与神谕者的信息按
  「能力未生效」结算（内容仍按记录推演下发、`MayBeFalse = true`），4 号城镇公告员生效。两条路径由规则用例对表覆盖
  （`Resolve_Effective_…` / `Resolve_Ineffective_…`），**不宣称**夹具覆盖了生效路径。
- 零信任本轮 43 项（E18 记录 44 项）：该装置自 `353fe84` 未再改动、本次 43 项全过无失败无跳过，差异属装置自身
  的条件计数口径，不在产品面——如实记录，不作解释性结论。

批次 E23 判出：

- **回溯型信息族票验收矩阵 13 行全部通过**（逐行证据见 `done/retrospective-info-family.md` 的「E23 验收判定」；
  行 9 / 10 为规则级证据、行 12 / 13 为组合证据，票据里已逐条注明性质）→ 移入 `done/`。
- 本批后 `in-progress/`、`review/` 清空；`todo/` 留两张本轮新登记的用户待办（`replay-auto-review` 自动化复盘 /
  `ui-layout-and-onboarding` 排版与引导），不属本批判定范围；`done/` 新增 `retrospective-info-family.md`。

## 批次 E24：死亡触发族（贤者 / 心上人）

冻结版本：`main` @ `d5cf109`（四笔提交：实现 `128ab67` → 用例 `22cfb05` → 文档 `c38b252` → 装置 `d5cf109`）。
本批等待判定的票据一张：`sage-and-sweetheart`。

本批改动含**事件触发管线的新收口路径**（触发型 / 触发格裁定结清后的续推）与**顺序表条目改换**
（`sweetheart` / `sage` 由行动格改触发格），故按「本票装置 + 通用回归 + 激活路径 + 零信任」四面对齐
（四装置全部前台、一次强制构建）：

- `tools/verify-death-triggers.mjs`（**本批新增**，取证档 `--quota 2 --screenshots-all --build`）：
  **56 项全部通过 / 0 跳过、退出码 0**。白天处决 3 号心上人 → 触发型裁定挂起（开夜被拒
  `phase.trigger_choice_pending`）→ 指定 4 号持续醉酒（跨阶段不清除、操作台可见效果来源）→
  次夜恶魔击杀 2 号贤者 → 当夜触发格裁定按击杀记录推演 → 展示两名玩家 → 信息只到本人、
  其余席位零下发（五席 126 帧全量扫描无说书人字段 + 阳性对照）。截图 `deathtrigger-01…06` 均为本次
  运行写入并**逐张复核**（处决心上人 / 触发型裁定 + 开夜被拒 / 醉酒标记与效果来源 / 贤者裁定推演 /
  2 号玩家页信息 / 5 号无关席位空态）。
- 主装置 `tools/verify-storyteller-panel.mjs`（同档）：**判定 194 项 / 0 跳过、退出码 0**；
- 角色变更族 `tools/verify-character-change.mjs`（同档）：**59 项全部通过**——顺序表改换后
  哲学家代行 / 理发师换角 / 方古侵染路径回归；
- 零信任 `tools/verify-zero-trust.mjs`：**43 项全部通过**——触发型裁定与醉酒维度不进玩家面。

真宿主用例 `DeathTriggerHostTests` **1/1**（真 SQLite + 真 SignalR：处决心上人 → 触发型裁定 → 开夜被拒 →
指定醉酒；次夜击杀贤者 → 当夜裁定 → 信息只到本人 + 无关席位零下发 + 裁定结清后夜晚继续推进到收口）。

冻结版门禁：`dotnet build` 0 警告 0 错误；`dotnet test` **703 通过 / 0 失败**；`dotnet format` 就地通过。

诚实记录：

- **提交前独立对抗性自检发现 HIGH-1 并已修复**：触发型 / 触发格裁定（贤者展示 / 心上人醉酒）结清后
  当晚不再推进——裁定 id 不属于当前槽位时内核只落裁定，而挂起期间配额已走完，结清后没有任何推进入口
  （自检真宿主探针：12.1 秒 / 约 240 次节拍 `SlotIndex` 恒 12）。修法在**提交管线**补「重进本格」
  （`SessionCommit.BuildDecisionContinuation`：有挂起不补、计划已走完不补），内核契约不变；
  回归由 Kernel 两条用例、`DeathTriggerHostTests` 第 ⑦ 断言与装置第 56 项共同固定。
- **自检同时发现 MEDIUM-2 恒真断言**（`TestServerHost.WaitForViewAsync` 超时返回最后视图 →
  其后 `Assert.NotNull` 永真），已修本票三处并整族对齐 E23 的 `RetrospectiveInfoHostTests`；
  `InformationResultDto` 的「不含 Note」断言改为键集合断言（这才是防"将来加字段"的保护）。
- 矩阵行 12（心上人离场解除）与行 16（真重启 / 重连）为**组合证据**（通用效果生命周期 / 快照往返
  加既有真重启用例），本批未跑针对性真机 / 重启用例——残余逐条登记在票据里，扩装置时优先补。
- 装置侧产品疑点三条（触发格裁定无席位归属 / 白天计划收口读数越界 2 / 1 / 候选不标生死）
  已登记为 `done/storyteller-decision-affordances.md`（当轮在 `todo/`），不属本批判定范围。
- 一次全量 `dotnet test` 曾出现 1 条未复现的红（并行构建期间跑全量），随后同命令连跑两次全绿；
  按"陈旧产物导致的假红"纪律判定为产物时序抖动，非产品缺陷。

批次 E24 判出：

- **死亡触发族票验收矩阵 17 行全部判出**（17 行通过：15 行直接证据 + 行 12 / 16 组合证据，逐行注明性质；
  逐行证据见 `done/sage-and-sweetheart.md` 的「E24 验收判定」）→ 移入 `done/`。
- 本批后 `in-progress/`、`review/` 清空；`todo/` 三张（两张既有用户待办 + 本批新登记的面板票据）；
  `done/` 新增 `sage-and-sweetheart.md`。

## 批次 E25（2026-10-03，说书人面板：裁定归属 / 读数 / 投影补口）

冻结版本：`main` @ `a365ef8`（实现 `9ddc1bb` → 用例 `91ade23` → 装置 `d79640d` → 文档 `f93c57c` → 装置补图 `a365ef8`）。
本批等待判定的票据一张：`storyteller-decision-affordances`（本轮新票，含 E17 残余③与 E21 残余）。

本批改动跨内核（裁定归属席位）、应用 / 契约（说书人投影新增三字段）、Web（面板五处呈现）与装置脚本，
故按「本票装置 + 通用回归 + 激活路径 + 同族文本口径 + 零信任」五面对齐（五装置全部前台；首装置 `--build` 一次构建）：

- `tools/verify-death-triggers.mjs`（取证档 `--quota 2 --screenshots-all --build`）：
  **70 项全部通过 / 0 跳过、退出码 0**（E24 为 56 项，本批 +14）。新增断言覆盖：心上人触发型裁定的归属
  （「归属：3 号」/ 牌面「待裁定」/ 定位按钮跳席）、候选「已死亡」标注（恰好 1 个、落在 3 号；存活候选与
  `pair:` 候选为零标签对照组）、白天收口读数（进行中 `1 / 1`、收口后不出现越界 `2 / 1`、走完 = 「已完成」）、
  贤者触发格的归属与环区常驻「本步上下文（含推演）」。截图 `deathtrigger-01…06` 为本次运行写入并逐张复核
  （01 = 处决心上人 + 归属 / 标注 / 已完成；04 = 贤者裁定 + 定位 + 推演行）。
- 角色变更族 `tools/verify-character-change.mjs`（同档，在 `a365ef8` 上重跑）：**67 项全部通过**
  （E24 为 59 项，本批 +8）——「限一次」标记（可见 / 文本精确 / title 记 3 号→5 号 / 第三夜仍在）与
  「今晚理发」标记（窗口内可见 / 文本 / title 记 4 号 / 结清后消失）；截图 `cc-06-fanggu-conversion`、
  `cc-08-barber-night-marker`（两枚标记同框）。
- 主装置 `tools/verify-storyteller-panel.mjs`（同档）：**判定 194 项 / 0 跳过、退出码 0**——通用玩法与面板回归
  （槽位读数仍兼容进行中的 `N / M`）。
- 麻脸巫婆链路 `tools/verify-pit-hag.mjs`（同档）：**34 项全部通过**——候选文本读取口径同族回归
  （该装置此前直接取 `textContent` 比对候选 preview，本批起改为先摘掉「已死亡」标签）。
- 零信任 `tools/verify-zero-trust.mjs`：**43 项全部通过**——归属 / 限一次 / 今晚理发均不进玩家面。

冻结版门禁（在 `f93c57c` 上跑）：`dotnet build` 0 警告 0 错误；`dotnet test` **704 通过 / 0 失败**；
`dotnet format` 就地通过；`npm run gate`（typecheck + eslint + 107 前端单测 + build）全绿。

诚实记录：

- 装置首轮曾假红 1 项（`verify-death-triggers` 的「候选 = 全体 5 席」精确比对）：根因是本轮把「已死亡」标签
  **嵌进了候选按钮内部**，既有读取口径直接取 `textContent` 会把标签文案混进 preview。修法在工具侧
  （先摘标签再取文本），并整族对齐 `verify-pit-hag.mjs` 的同类读取（该文件实跑 34 项全绿）；不是产品缺陷。
- 迭代档下角色变更族「换手后尚未进入的格重绑」断言曾有的时序窗口**已闭合**（E25 判出后定位为产品缺陷：
  配额输入幂等键只按「计划 + 槽位」区分，触发格应答晚于配额时重进本格的第二次配额被当成重复命令回放；
  已改为按「本次槽位进入的事件序号」区分，见 `backlog/done/sage-and-sweetheart.md`「残余补证」）；本批取证档 67 项全绿。
- 本票矩阵行 1–7 全部判出（7 行通过；逐行证据见票据「E25 验收判定」）；残余三项（多恶魔选择分支 /
  触发型裁定无挂起回退分支 / 旧事件流归属为 null 的界面回退）留在票据「残余」节，均不阻塞判行。

批次 E25 判出：

- **说书人面板票验收矩阵 7 行全部通过** → 移入 `done/`。
- 本批后 `in-progress/`、`review/` 清空；`todo/` 三张（两张既有用户待办 + 新增的初始身份随机器票）；
  `done/` 新增 `storyteller-decision-affordances.md`。

## 批次 E26（2026-10-03，限次信息族：女裁缝 / 艺术家）

冻结版本：`main` @ `2789e1b`（走查中发现并当场修复一个交付缺陷 `cf4863f`（艺术家入口与等待态的在线通道），
再提交装置与同族解析修复 `2789e1b` 后冻结跑批；跑批期间工作树干净、未改产品代码）。

本批改动跨推送面（新增本人视图变更通道）、应用 / 契约投影与 Web（面板三态），
故按「本票装置 + 通用回归 + 激活路径 + 零信任」四面对齐（四装置全部前台；首装置 `--build` 一次构建）：

- `tools/verify-seamstress-artist.mjs`（**本批新增**，取证档 `--quota 2 --screenshots-all --build`）：
  **82 项全部通过 / 0 跳过、退出码 0**。夜 1 女裁缝在玩家页选两名 / 摇头（6 组 pair + decline）→
  白天 1 艺术家提问（四答 / 要求重问 / 重连等待态）→ 夜 2 顺序表恶魔格在女裁缝格之前
  （击杀 2 号后她**再次被唤醒**）→ 裁定「是」→ 信息 + 失能标记 → 夜 3 空槽不再唤醒
  （DOM 观察器 + 视图帧双通道）+ 五席 116 帧隔离扫描。截图 `limitinfo-01…10` 均为本次运行写入并逐张复核。
- 主装置 `tools/verify-storyteller-panel.mjs`（同档）：**判定 194 项 / 0 跳过、退出码 0**；
- 角色变更族 `tools/verify-character-change.mjs`（同档）：**67 项全部通过**；
- 零信任 `tools/verify-zero-trust.mjs`：**44 项全部通过**。

真宿主用例 `SeamstressArtistHostTests` **3/3**（女裁缝用后不再唤醒 / 艺术家四答与重问 / 哲学家摇头同族；
本批扩展的在线推送断言含在其中）。冻结版门禁（跑批时）：`dotnet build` 0 警告 0 错误；
`dotnet test` **749 通过 / 0 失败**（Kernel 327 · Rules 282 · Integration 117 · NormativeGates 23）；
`dotnet format` 就地通过；`npm run gate` 全绿（typecheck + lint + 113 前端单测 + build）。
跑批后仅测试增补一条提问闸用例（行 9 的 `phase.not_open_day` / `artist.not_artist`，E19 先例）：
Integration 118、全量 **750** 通过，产品代码未变。

诚实记录：

- **E26 首跑在真机上发现交付缺陷并当场修复**：`canAskArtistQuestion` / `pendingQuestion` /
  `exhaustedAbilities` 只随 `JoinSeat` 快照更新，白天开始与提问结清都没有推送通道——艺术家的提问入口
  在**在线连接**上永不出现（装置首跑的第一条红），等待态还被面板 `v-if` 只认权限位遮住。修法（`cf4863f`）：
  新增 `ReceivePlayerViewChanged`（提问 / 结清定向、阶段边界与白天收口广播），客户端复用快照合并闸，
  面板三态改 `canAsk || pendingQuestion`；先红后绿：装置 34→82 项全绿、宿主补四条在线推送断言、
  web 接线 / 解析用例随 `npm run gate` 全绿。
- **装置侧同族修复**（`2789e1b`）：SignalR 一帧可合多条消息（服务端把推送与调用回执写进同一帧），
  `verify-seamstress-artist` / `verify-death-triggers` / `verify-retro-info` 的解析器改为逐段解析、
  回执匹配改用全量消息，修「提交生效却等不到回执」的间歇假红；三装置迭代档复跑 82 / 70 / 53 全绿。
- 女裁缝「能力未生效 / 已死亡」与艺术家醉酒 / 中毒由规则级用例给结论（行 4 / 5 / 8），本批无真机中毒夹具，
  不声称装置覆盖。

批次 E26 判出：

- **限次信息族票验收矩阵 15 行全部通过**（逐行证据见 `done/seamstress-and-artist.md` 的「E26 验收判定」）→ 移入 `done/`。
- 本批后 `in-progress/`、`review/` 清空；`todo/` 三张（`replay-auto-review` / `setup-randomizer` /
  `ui-layout-and-onboarding`）；`done/` 新增 `seamstress-and-artist.md`。

## 批次 E27（2026-10-04，初始身份随机器：按官方阵营分布自动配板）

冻结版本：`main` @ `d56f659`（本批新增装置 `tools/verify-setup-randomizer.mjs`、`proposeSetup` 包装单测与
装置登记；**未改产品代码**；跑批期间工作树只有本批新增/修改的工具、测试与文档）。

本批改动只在工具 / 测试 / 文档，故按「本票新装置 + 门禁」两面取证：

- `tools/verify-setup-randomizer.mjs`（**本批新增**，取证档 `--quota 2 --screenshots-all --build`）：
  **58 项全部通过 / 0 跳过、退出码 0**。一键配板（覆盖每席 / 角色唯一 / 净分布随在场恶魔修正 / 显式种子；
  本跑首抽方古 → 2/1/1/1，并给出「设置调整 · 方古」与「镇民取余量」披露）→ 重摇（新种子 `64ea…03e`）→
  手改到 `clockmaker / dreamer / no-dashii / mutant / klutz` → 提交走既有分配命令面（序号 9）→ 开夜（序号 11）→
  首夜 1 号钟表匠待裁定（归属 1 号 + 当前槽位高亮）+ 2 号筑梦师定向请求（窗口 8 次采样：其余席位零请求）→
  钟表匠 / 筑梦师两条信息只到本人（DOM + 五席 35 帧隔离扫描，说书人连接 26 帧阳性对照）。截图
  `setup-01…09` 均为本次运行写入并逐张复核（01 = 方古建议 + 净分布 / 钳制说明；05 = 钟表匠待裁定；
  07 = 筑梦师请求；09 = 无关席位空态）。
- **其余十一个装置本批不重跑**（诚实记录）：本批零产品代码改动，通用玩法回归与其它能力链路没有被本批触碰；
  装置脚本与 web 单测的风险由 `dotnet build/test/format` + `npm run gate` 覆盖。这与 E24–E26"改了产品代码就
  重跑主装置 + 同族装置"的口径不冲突——那些批次的改动面在本批不存在。

冻结版门禁（跑批前）：`dotnet build` 0 警告 0 错误；`dotnet test` **787 通过 / 0 失败**（Kernel 327 · Rules 314 ·
Integration 122 · NormativeGates 24）；`dotnet format` 就地通过；`npm run gate` 全绿（typecheck + lint +
**117** 前端单测 + build；本批新增 3 条 `proposeSetup` 包装用例）。

诚实记录：

- 装置首跑 58 项里 1 项**假红**：「玩家帧不含其他席位角色 slug」把玩家视图 DTO 的字段名 `klutzChoices`
  当成了角色值 `klutz`（1–4 号全命中）。修法在工具侧：按 JSON 字符串值（带引号）扫描、阳性对照同口径；
  迭代档与取证档复跑均 58 项全绿。不是产品泄漏。
- 判行时新发现一处**低危 UX 观察**并记入票据残余：手改后「净分布」行仍显示最近一次建议的分布与种子
  （不随手改重算；建议语义待定）。不影响任何矩阵行。

批次 E27 判出：

- **初始身份随机器票验收矩阵 8 行全部通过**（逐行证据见 `done/setup-randomizer.md` 的「E27 验收判定」；
  行 5 / 6 / 8 为本批装置证据，行 1–4 / 7 为规则单测 + 真宿主用例 + 帧级隔离扫描）→ 移入 `done/`。
- 本批后 `in-progress/`、`review/` 清空；`todo/` 两张（`replay-auto-review` / `ui-layout-and-onboarding`）；
  `done/` 新增 `setup-randomizer.md`；装置清单增到 12 个（`docs/acceptance/devices.md`）。

## 批次 E28（2026-10-04，自动化复盘：终局后逐步回放）

冻结版本：`main` 工作树 = 本票全部改动（`ReplayQueryService` / `ReplayProjection` / `ReplayStepCatalog`、
终局后可见契约与前端 `features/replay`、席位牌共用化、两个装置小改，以及 R-0043 / D-0020 / 术语登记）。
跑批期间工作树只有本票成果，无并行构建。

本批按「两条装置 + 三个门禁」取证：

- `tools/verify-winloss.mjs`（**本批扩展**，取证档 `--quota 2 --screenshots-all`）：**25 项全绿 / 0 跳过**——
  新增「结束批次之前玩家端没有复盘入口」「结束后入口出现、面板停在第 1 步」「下一步按原子步骤推进
  （第 3/26 步、事件序号 3）」「上一步回到第 2 步」「刷新 / 重连后按事件序号恢复位置」；截图
  `winloss-01…05` 逐张复核（04 = 复盘面板与圆盘、05 = 刷新恢复；与断言一致）。
- `tools/verify-zero-trust.mjs`（**本批扩展禁词表**）：**43 项全绿**——玩家收包扫描新增
  `replay` / `markers` / `steps`，进行中零复盘字段。
- 三个门禁：`dotnet build` 0 警告 0 错误；`dotnet test` **798 通过 / 0 失败**（Kernel 327 · Rules 314 ·
  Integration 133 · NormativeGates 24）；`dotnet format` 就地通过；`npm run gate` 全绿
  （typecheck + lint + **125** 前端单测 + build）。

诚实记录：

- 复盘步骤目录由覆盖率门禁锁死：新增 `GameEvent` 类型必须被 presenter 认领或在显式排除清单里，
  否则 `ReplayStepCatalogTests` 报红；排除项只有说书人注记（D-0019）与步骤机节拍内部事件（D-0013 / D-0014）。
- `GameSession.cs` 因本轮新增一度超过 600 行被 `SourceFileLengthGateTests` 判红：按职责把复盘读侧拆成
  `ReplayQueryService`（只读事件流、不依赖宿主内存态），门禁复跑转绿——不是调阈值，是拆文件。
- 装置首跑 4 项**假红**：面板先渲染、步骤按序号异步到达，断言读到了加载态空文案。修法：前端空白态显示
  「加载中…」、装置等待首屏步骤再判位置；复跑 25 项全绿。不是产品缺陷。

批次 E28 判出：

- **自动化复盘票：行 1 / 2 / 4 / 5 / 6 / 7 / 9 通过；行 3 与行 8 无法判定**（行 3 缺含换角 / 换手 /
  中毒 / 醉酒的逐事件族截图；行 8 缺大事件流耗时采样）→ 票据留在 `review/` 并写明缺什么
  （逐行证据见票据「实施结论」）。
- 本批后 `todo/` 两张（账号与显示名 / 排版与上手引导）、`review/` 一张（本票）。

## 批次 E29（2026-10-04，自动化复盘：收口行 3 / 行 8）

冻结版本：`main` @ `e454032`（先提交复盘呈现修正与两个装置的取证段——`fix(replay)` 自指击杀箭头、
`test(verify)` 角色变更装置复盘段 / 新规模采样装置；跑批期间工作树干净）。

本批按「本票两条装置 + 同族回归 + 零信任」取证：

- `tools/verify-character-change.mjs`（**本批扩展**，取证档 `--quota 2 --screenshots-all`）：**84 项全绿**——
  新增说书人实时面复盘段：说书人上报 6 号中毒 → 开「复盘」→ 逐步回放到醉酒（`cc-16`）/ 换角（`cc-17`）/
  恶魔击杀箭头 5 号 → 4 号（`cc-18`，SVG 连线存在）/ 换手（`cc-19`）/ 中毒（`cc-20`）五步并逐张截图，
  外加实时面口径图（`cc-15`）与自指自死步（`cc-21`）；断言含「说书人实时面：进行中的事实」、圆盘图例文案、
  标记归属与红色箭头存在。
- `tools/verify-replay-scale.mjs`（**本批新增**，取证档）：**18 项全绿**——2600 条真实命令写入 2605 步
  事件流（分页 6 页 / 492ms、序号严格递增）；服务端投影首页 / 中段 / 深页各 3 次采样 =
  66–78ms / 66–72ms / 70–74ms；浏览器首屏 91ms、第 200→201 步翻页 55ms、深页定位（第 2000 步）542ms、
  第 2000→2001 步翻页 75ms；已加载窗口严格 200 → 400 → 2000 → 2200。截图 `replay-scale-01/02` 逐张复核。
- `tools/verify-winloss.mjs`（同族回归，取证档）：**25 项全绿**——玩家复盘面（结束后入口 / 逐步回放 /
  刷新按序号恢复）未受修正影响，截图 `winloss-01…05` 本次重跑写入。
- `tools/verify-zero-trust.mjs`：**43 项全绿**。

冻结版门禁：`dotnet build` 0 警告 0 错误；`dotnet test` **800 通过 / 0 失败**（Kernel 327 · Rules 314 ·
Integration 135 · NormativeGates 24）；`dotnet format` 就地通过；`npm run gate` 全绿
（typecheck + lint + **125** 前端单测 + build）。

诚实记录：

- 装置首跑在真机上暴露**复盘呈现缺陷**：方古侵染时原方古自死（`CausedBy` = 自己）被画成
  「3 号 → 3 号」的恶魔击杀红箭头（假阳性）。先红后绿：`DemonSelfDeath_HasNoKillArrow` 先失败 →
  `StateReplayPresenter.IsDemonKill` 增加自指排除 → 转绿；装置该步复跑只剩死亡帷幕（`cc-21`）。
- 装置脚本自检时发现截图拍错步：`cc-17` 原先把截图落在「换角」的下一步（自死步）——修正为标记步本身 +
  自死步另存 `cc-21`；修正只动工具（并入提交 `e454032`），断言逻辑与产品行为未变。
- 范围说明：产品改动只在 `StateReplayPresenter`（复盘读侧投影），主装置不经过 `GetReplay`，故未重跑（E27 先例）。

批次 E29 判出：

- **自动化复盘票：行 3 / 行 8 通过（累计 9 行全部通过）** → 移入 `done/`（逐行证据见票据「实施结论」；
  残余事项 1 / 2 关闭，仅留「复盘文案仍是席位号」这一已知限制，指向 `todo/account-and-display-name.md`）。
- 本批后 `in-progress/`、`review/` 清空；`todo/` 两张（账号与显示名 / 排版与上手引导）；
  `done/` 新增 `replay-auto-review.md`；装置清单增到 13 个（`docs/acceptance/devices.md`）。

## 批次 E30（2026-10-04，账号与玩家名：M1 局内玩家名 + M2 账号系统）

冻结版本：`main` @ `451ab23`（先提交装置头部注释与选项文案收紧——`chore(tools)` / `fix(web)`；
跑批期间工作树干净，取证档只对本版本跑）。

本批按「本票装置 + 同族回归 + 零信任」取证：

- `tools/verify-accounts.mjs`（**本批新增，第 14 个装置**，取证档 `--quota 2 --screenshots-all`）：
  **29 项全绿**（37.9s，断言总数 29）——A 注册 `alice`/爱丽丝（一次性恢复码上屏）→ 凭票据认领 1 号 →
  B 注册 `bob`/鲍勃 认领 2 号（两席看到同一份公开映射）→ 游客 C 只凭票据坐 3 号（标签回退「3 号」、
  那一行不带名字、无诊断）→ A 改名「爱丽丝二世」（自己 / B 的同桌 / 说书人魔典席位牌三处同步）→
  说书人上报 1 号死亡 → 复盘步骤文案与圆盘标记都是「1 号 · 爱丽丝二世」→ 负向三条（伪造账号会话 /
  跨账号抢席 / 同账号第二席被 Hub 显式拒绝，且被拒连接拿不到连接凭据）→ 读库断言绑定是会话信息
  （`Users` 两个账号、`SeatBindings` 只有 1 / 2 号，游客 3 号无绑定）。截图 `accounts-01…06` 逐张复核
  （本次人工核了 `01` 玩家 A、`03` 游客 C、`04` 说书人魔典、`05` 复盘面板四张：席位标签、同桌名单、
  恢复码、席位牌与复盘文案与断言一致）。
- `tools/verify-zero-trust.mjs`（**本批扩展**，取证档）：**51 项全绿**（23.5s）——新增账号段：
  伪造 / 过期账号会话进不了房、账号会话不能当游戏凭据、无账号会话的第三方入座结果里没有账号凭据、
  公开席位名照常下发；既有 43 项未受影响。
- `tools/verify-storyteller-panel.mjs`（同族回归，取证档 `--quota 2 --screenshots-all`）：
  **194 判定全绿**（109.8s，跳过 0）——多客户端通用玩法、注记、首夜 / 白天 / 二夜、涡流、重建、
  重连补齐全过；玩家面新增的账号区与同桌区不影响既有断言。
- `tools/verify-winloss.mjs`（同族回归，取证档）：**25 项全绿**（34.5s）——结束批次与玩家复盘面
  （入口 / 逐步回放 / 刷新按序号恢复）在复盘文案改动后仍全过，截图 `winloss-01…05` 本次重跑写入。

冻结版门禁：`dotnet build` 0 警告 0 错误；`dotnet test` **853 通过 / 0 失败**（Kernel 327 · Rules 314 ·
Integration 188 · NormativeGates 24）；`dotnet format` 就地通过；`npm run gate` 全绿
（typecheck + lint + **142** 前端单测 + build）。

诚实记录：

- 装置首跑在真机上抓到**真缺陷**：改名推送与入座快照**同序号**（认领 / 改名是会话信息，不产生事件），
  玩家端按字段合并整视图时用 `sequence >`，整份改名推送被当旧数据丢掉。修
  `web/src/services/playerViewMerge.ts` 改用 `>=` 并补 `vitest` 回归（`d79da3e`）；改动前装置
  第 11–14 项红、改后绿。
- 装置**首跑 2 分钟**的教训：3 条红断言各自等满 20–30s 超时才是耗时来源，根因修好后同一条流水 37.4s
  （用户当场指出）。规则已写进 `AGENTS.local.md`：红断言的等待要算钱 + 新装置必须报默认档耗时。
- 取证档首跑在 `night1-dreamer-resolution` 红了：选项文案统一改写后，游客面的服务端文案「3 号玩家」
  变成「3 号」，打翻了主装置（13 个装置共享的文案耦合）。据此把口径收紧为**只有这一席已经有玩家名
  才接管选项文案**（`451ab23`）：游客面保留服务端语境，有名席位用统一口径。
- 范围说明：其余九个装置（女巫 / 麻脸巫婆 / 数学家 / 回溯信息 / 死亡触发 / 限次信息 / 角色变更 /
  初始配板 / 复盘规模）未重跑——产品改动不触及规则内核与其链路，且玩家面文案在有名字时才改写、
  游客面与 E29 一致；同族的零信任与胜负装置已重跑覆盖。

批次 E30 判出：

- **账号与玩家名票：M1 六行 + M2 八行全部通过**（逐行证据见票据「实施结论」）→ 移入 `done/`；
  `replay-auto-review` 残余事项 3（「复盘文案仍是席位号」）随之关闭。
- 残余（已写进票据，另计）：说书人数据抽屉类组件仍显示「N 号」未接姓名口径（**已由批次 E31 关闭**）；
  M2-4「新对局重新认领」只有集成用例、无真机新对局取证。
- 本批后 `in-progress/`、`review/` 清空；`done/` 新增 `account-and-display-name.md`；
  装置清单增到 14 个（`docs/acceptance/devices.md`）。

## 批次 E31（2026-10-04，排版与上手引导：版块自解释 / 信息降密度 / 抽屉姓名口径）

冻结版本：`main` @ `1a73bad`（先落 web 改动，再补装置断言与门禁登记；跑批期间工作树干净，
取证档只对本版本跑）。

本批按「本票装置 + 同族回归 + 零信任」取证：

- `tools/verify-storyteller-panel.mjs`（取证档 `--quota 2 --screenshots-all`）：**194 判定全绿**
  （跳过 0）——两端版块副标题与说明入口进入主流程后，加入 / 分配 / 首夜 / 白天 / 二三点夜 / 涡流 /
  重建 / 重连全过；整页截图 `01…41` 本次重跑写入。
- `tools/verify-accounts.mjs`（**本批扩展**，取证档）：**36 项全绿**（29 → 36）——新增
  ① 悬停显示说明气泡、② 点按（触屏路径）显示、③ `Esc` 关闭（截图 `accounts-10-explain-tip`）；
  ④ 数据抽屉 / 开局分配 / 席内注记四处「1 号 · 爱丽丝二世」（截图 `accounts-09-drawer-names`）；
  ⑤ 版面量度：玩家页内容高 **626 → 556px**、说书人页 **1664 → 1611px**（截图 `accounts-07/08`）。
- `tools/verify-zero-trust.mjs`（同族回归）：**50 项全绿**（首夜强推 3 次 → 该装置断言数按尝试次数
  动态计数，E30 那次是 4 次 / 51 项）。
- `tools/verify-winloss.mjs`（同族回归，取证档）：**25 项全绿**——玩家端结束 / 复盘面在版块重排、
  账号折叠后不受影响。
- `tools/verify-madness.mjs`（同族回归，取证档）：**28 项全绿**——处罚处决默认收起后，装置改为
  先展开再交互（`openPunishControls`）。
- `tools/verify-setup-randomizer.mjs`（同族回归，迭代档）：**58 项全绿**——开局分配面板在"可用时
  默认展开"的口径下未变；它是最依赖该面板的装置。

冻结版门禁：`dotnet build` 0 警告 0 错误；`dotnet test` **853 通过 / 0 失败**（Kernel 327 · Rules 314 ·
Integration 188 · NormativeGates 24）；`dotnet format` 就地通过；`npm run gate` 全绿
（typecheck + lint + **154** 前端单测 + build）。

诚实记录：

- 门禁与装置各抓到一次真问题：`PlayerProjectionLeakGateTests` 拦下玩家侧未登记的共享组件 `HelpTip`
  （按规则显式登记，并写明它是纯呈现组件）；主装置首跑 5 红来自两处**装置文本锚点**——
  `.caption` 必须是纯文本「槽位」、玩家信息面板必须保留可见的「信息可能是错的」（D-0002）。
  两处都改产品标记后转绿，没有放宽断言。
- 顺手修既有缺陷一处：`StorytellerPanel` 的凭据占位提示 `v-else` 误绑在复盘开关上，导致"已连接"
  的页面长期挂着一句"还没有凭据"——改为与 `GrimoireView` 配对；另把低频危险的「处罚处决」默认收起。
  两处由主装置 194 项与 `verify-madness` 28 项覆盖回归。
- 密度量度（同状态同口径）：玩家页 -11.2%、说书人页 -3.2%；主装置游玩态 -3% ～ -9.9%；
  日间抽屉展开态持平（-0.2%）；说书人"加入空态" +2.6%（标题 / 副标题的高度成本，见票据「诚实记录」）。

批次 E31 判出：

- **排版与上手引导票：矩阵 7 行全部判出（6 行通过 + 行 3「通过（有范围说明）」）** → 移入 `done/`；
  批次 E30 残余①（说书人数据抽屉组件的姓名口径）随之关闭。
- 装置清单仍是 14 个（`verify-accounts` 扩展，未新增装置）；本批后 `in-progress/`、`review/` 清空，
  `todo/` 无待办票据。
- 未做（交接提示词另计）：`verify-accounts --only/--from` 分段选择器（与其它装置同款）——
  已于 2026-10-04 落地（`docs/backlog/done/accounts-device-section-selector.md`，12 段；同轮修掉
  游客页那处让本装置白等 30s 的无界 `innerText()`，迭代档 37.3s → 7.3s）。

## 批次 E32（2026-10-04，装置读取链：守卫式读取收口 + 三装置取证档重跑）

冻结版本：`main` @ `407eea2`（先提交工具改动 `fix(tools)`，再对冻结版跑取证档；跑批期间工作树干净、
未改产品代码）。本批没有等待判定的玩法票据——主要内容是**重跑三张信息行装置的取证档**，让 E31 之后
修复的新读法与本轮守卫式有界读取进入证据链（上一票「已知缺陷与未验证」里的 Medium 项）。

- `tools/verify-retro-info.mjs`（取证档 `--quota 2 --screenshots-all --build`）：**53 项全部通过、退出码 0**（76.0s）；
- `tools/verify-seamstress-artist.mjs`（取证档）：**82 项全部通过、退出码 0**（118.3s）；
- `tools/verify-death-triggers.mjs`（取证档）：**70 项全部通过、退出码 0**（80.9s）；
- 截图 21 张（`retro-01…05` / `limitinfo-01…10` / `deathtrigger-01…06`）均为本次运行写入；
  复核 9 张关键图（每装置：裁定面 / 玩家信息面 / 无关席位干净面）——`retro-01/04/05`、
  `limitinfo-03/08/10`、`deathtrigger-02/05/06`：花名册与判定一致，玩家信息行按「中文名（slug）」+ 内容
  渲染（retro-04「卖花女孩（flowergirl）：恶魔参与了投票」、limitinfo-08「女裁缝（seamstress）：
  3 号与 4 号玩家属于同一阵营」、deathtrigger-05「贤者（sage）：…1 号 与 4 号…」），
  无关席位信息面板为空；其余 12 张为同一次运行写入，未逐张复核（如实记录）。

诚实记录：

- **本批只跑三装置**：其余装置未重跑——E31 之后产品代码没有变化，变化只在装置读取助手；
  这三张正是"读取面变化 + 旧取证档停留 E23 / E24 / E26"的交集（E20 / E27 先例：本批没有它们的改动就不跑）。
- 工具链票（`device-guarded-reads-unbounded-wait`）的验收证据是**迭代档 + 假页面探针 + 门禁**：
  探针 E 守卫后脱离 501ms（先红：旧写法 30012ms）、11 装置迭代档全绿、四条门禁全 0（vitest 154）；
  本批的三次取证档同时把该票的读取面带上真机证据链。逐条见该票「结果」节。
- 冻结版门禁在提交前已跑（`artifacts/web/gates.log`：build 0 · test 853 · format 0 · gate 0），
  跑批期间未改代码；本批记录本身是纯文档改动，按门禁规则跳过重跑。
- 本批后 `in-progress/` 清空，`todo/` 余一张（装置属性读取收口，见票据索引）。

## 批次 E33（2026-10-04，钟盘投票：R-0017 目标形态落地）

冻结版本：`main` @ `be4d403`（实现 + 装置脚本；取证完成后只补了一条旧日志回放用例与文档，
产品代码 / 装置脚本未再改动）。跑批期间未并行改代码。

- 门禁（冻结版）：`dotnet build` 0 警告 0 错误；`dotnet test` **861 通过 / 0 失败**
  （Kernel 335 · Rules 314 · Integration 188 · NormativeGates 24）；`dotnet format` 就地通过；
  `npm run gate` 全绿（typecheck + lint + **163** 前端单测 + build）；收尾
  `dotnet build-server shutdown`（常驻节点归零）。
- 装置取证档（全部 `--quota 2 --screenshots-all`，各一次完整取证档运行）：
  - 主装置 `verify-storyteller-panel.mjs`：**202 项全部通过、退出码 0**（day1 19/19；合计 116.2s；截图 39 张）；
  - `verify-winloss.mjs` **28**、`verify-witch.mjs` **28**、`verify-death-triggers.mjs` **73**、
    `verify-retro-info.mjs` **56**，全部通过、退出码 0。
- 判出：**R-0017 这张票的 7 行验收矩阵全部通过** → 票据 `clock-vote-flow` 移入 `done/`
  （R-0017 已按「落地后转 Decided」更新；设计定稿 5 条见票据，需求方如要改口径先改 R-0017 再改代码）。
- 截图复核：`32-day-dial-countdown.png`（蓝针 2 号、红针 1 号、倒计时 2）、
  `33-day-sweep-collecting.png`（当前指向 3 号、已收 2 席）；其余 37 张为同一次运行写入、未逐张复核（如实记录）。

诚实记录：

- **装置计时基线更新**：主装置迭代档本轮 44.3s（历史 27.0s——白天钟盘收票固定约 5s + 冷启动
  Release 重建），取证档 116.2s；四个辅助装置白天段各加约 4s。
- 本轮只改了 5 个装置（主装置 day1 段 + winloss / witch / death-triggers / retro-info 的白天段）：
  迭代档先全绿、再跑取证档；其余装置与白天无关，未重跑。
- 旧日志兼容（`VoteCastEvent` 无 `Sweep` 时按旧形态折叠）只有内核回放用例覆盖，没有真机旧库样本。

## 批次 E34（2026-10-04，旅行者与流放：D8 真机批次取证）

冻结版本：`main` @ `48470c3`（先提交主装置旅行者全链路——`tools/verify-storyteller-panel.mjs`
728 行增改，再对冻结版跑取证档；跑批期间工作树干净、未改产品代码：本批全部改动只在 `tools/`）。

本批按「本票装置 + 零信任回归」两面取证：

- 主装置 `tools/verify-storyteller-panel.mjs`（取证档 `--quota 2 --screenshots-all --build`）：
  **284 项全部通过 / 0 跳过、退出码 0**；54 张截图均为本次运行写入。新增覆盖：
  - `day1`：五名旅行者一次加入（席位 6..10 + 票据转交 / 公开宣告 / 阵营字样逐席零命中 /
    邪恶揭示单播）、处决 1 号后屠夫窗口 + 额外提名不落靶 + 关账；
  - 新 `traveller` 段：同日两条流放（怪咖达线受保护 / 屠夫由死者 1 号发起、不受保护死亡并
    即时公开）、移出（公开席位 10 → 9）、说书人实时面复盘四个标记（加入 / 流放 / 受保护 / 离场）；
  - `night2-3`：第二夜三个黄昏格（咖啡师「已死亡」徽标 + 效果 1 窗口 / 流莺角色信息只到本人 /
    集骨者重获窗口），第三夜两个窗口在下个黄昏收口 + 咖啡师效果 2「行动两次」窗口 + 流莺拒绝分支；
    第二夜之后移出咖啡师 / 流莺（后续夜不再入槽）。
- 零信任 `tools/verify-zero-trust.mjs`（`--quota 2`）：**51 项全部通过、退出码 0**——
  D7 的投影 / 契约变更后，玩家收包扫描与越权面未受影响。

冻结版门禁（提交前跑）：`dotnet build` 0 警告 0 错误；`dotnet test` **1076 通过 / 0 失败**
（内核 430 · 规则 390 · 集成 231 · 门禁 25）；`dotnet format` 退出 0（未重写工作树）；
`npm run gate` 退出 0。

截图复核 5 张（其余 49 张为同一次运行写入，未逐张复核，如实记录）：`night2-barista-options`
（咖啡师候选 + 「已死亡」徽标 ×2）、`night3-barista-second-action`（5 号「行动两次」窗口）、
`exile-protected`（第一条「达线但受死亡保护」）、`exile-death`（玩家端两条流放记录 + 6 号死亡
+ 当日公告）、`replay-exile`（复盘第 71 / 109 步 · 事件序号 116）——逐张与断言一致。

诚实记录（装置侧假红与口径修正，产品代码零改动）：

- 玩家端没有 `outcome` 回执区：三处玩家命令（额外提名 / 两条发起流放）从 `runCommand` 改为
  「点击 + 等公开账目变化」；装置口径错误，不是产品缺陷。
- 白天不能连开：首版把流放段放在"第 2 天"，被 `canStartDay`（开白天必须跟在夜晚之后）拒绝——
  改为**同一天继续**、`CloseDay` 移到旅行者段末尾。产品行为正确。
- 效果窗口收口后条目仍留在「效果归因链（含已终止）」（设计如此：因为什么解毒必须查得到）：
  两条收口断言从"元素消失"改为"状态 = 已终止"。
- 失效归因扫描 token `涡流在场` 误伤咖啡师给玩家的合法宣告「（即使涡流在场）」（R-0047）：
  token 收窄为带全角冒号的**说书人形状**（`涡流在场：`）——既保留对说书人泄漏的探测，
  又不误判玩家可见文案。

诚实记录（范围）：其余十二个装置未重跑——本批产品代码零改动（只改主装置脚本），D7 的投影 /
契约变更面由「主装置（多客户端全流程）+ 零信任（收包扫描）」覆盖（E27 / E31 先例）。

批次 E34 判出：

- **旅行者与流放票验收矩阵 13 行全部通过**（行 1–13；逐行证据见 `done/traveller-and-exile.md`
  「第十一批」与「验收矩阵」）→ 票据移入 `done/`。
- 残余（不阻塞判行，随票据保留）：「待保护裁定」无精确投影指示；屠夫额外提名落靶 / 二次处决
  只有内核 + 集成证据；集骨者两条登记边界（R-0054 第 6 / 10 条）；处罚处决未接死亡保护查询
  （R-0020 路径）；R-0046 / R-0044 §5 待实物回核（后于 2026-10-04 改为「实物素材可选回源、
  不阻塞」，见 `resolved/rulebook-original-crosscheck.md`）。
- 本批后 `in-progress/`、`review/` 清空；`todo/` 三张（日期偏移核查 / 夜序默认值对齐 /
  印刷规则书回核）；`done/` 新增 `traveller-and-exile.md`。

## 批次 E35（2026-10-04，夜晚顺序默认值对齐：命令层默认 Recommended）

冻结版本：`main` @ `d8b4816`（先提交命令默认值对齐 + 新集成测试 + R-0014 / 票据同步，再对冻结版
跑取证档；跑批期间工作树干净）。

本批按「主装置面板路径」取证：

- 主装置 `tools/verify-storyteller-panel.mjs`（取证档 `--quota 2 --screenshots-all --build`）：
  **284 项全部通过 / 0 跳过、退出码 0**；54 张截图均为本次运行写入（合计 173.7s）。与本票相关的：
  - `night1-clockmaker`：「首夜真实建表：14 个槽位（面板默认 Recommended 全表；含 D5 咖啡师黄昏槽）」
    ——面板不额外传口径，也从默认下拉（Recommended）开夜成功；
  - `night2-3`：「第二夜（Recommended）开夜被受理」「第三夜（Recommended）开夜被受理」，夜间
    全链路（请求 / 代填 / 强制作废 / 依赖失效）照常推进；
  - 其余 14 段（旅行者 / 涡流 / 重建 / 重连等）零失败——默认值改动未影响任何既有面板路径。

截图复核 4 张（其余 50 张为同一次运行写入，未逐张复核，如实记录）：`01-storyteller-joined`
（兜底面板口径下拉初始 = Recommended）、`03-night-started`（面板开夜后首夜 14 槽）、
`night2-barista-options`（第二夜 2/25 槽 + 咖啡师裁定点）、`14-player-phase-night-two`
（玩家端夜晚相位、无说书人字段）——逐张与断言一致。

冻结版门禁（提交前跑）：`dotnet build` 0 警告 0 错误；`dotnet test` **1078 通过 / 0 失败**
（内核 430 · 规则 390 · 集成 233 · 门禁 25）；`dotnet format` 退出 0（未重写工作树）；
`npm run gate` 退出 0（typecheck + lint + 169 测试 + 构建）。

诚实记录（范围）：其余十二个装置未重跑——本批产品改动只有命令层默认值一处（生产面板 / Hub 路径
始终显式传口径），面板全流程由主装置覆盖、命令层由集成用例覆盖；零信任投影未动。
装置 stderr 的重启窗口 `ECONNRESET` / 代理 `ECONNREFUSED` 噪声来自 `rebuild` 段**故意重启宿主**
（装置自记「重启窗口噪音=63（非预期 0）」并判过），与本批改动无关。

批次 E35 判出：

- **夜晚顺序默认值对齐票验收矩阵 3 行全部通过**（行 1 无参开夜 → Recommended 建表与槽位顺序；
  行 2 面板路径行为不变；行 3 显式 Original 仍生效并进 `StepPlan.Variant`）→ 票据移入 `done/`。
- 本批后 `review/`、`in-progress/` 清空；`todo/` 只剩印刷规则书回核一张；`done/` 新增
  `night-order-variant-default.md`。

## 批次 E36（2026-10-04，死亡保护裁定提示投影：说书人入口与裁判机器同源）

冻结版本：`main` @ `86dd663`（先提交内核 / 应用 / 契约 / 前端 / 测试 / 装置断言，再对冻结版跑取证档；
跑批期间工作树干净）。

本批按「主装置面板路径」取证：

- 主装置 `tools/verify-storyteller-panel.mjs`（取证档 `--quota 2 --screenshots-all --build`）：
  **286 项全部通过 / 0 失败 / 0 跳过、退出码 0**；54 张截图均为本次运行写入（合计 172.1s）。与本票相关的：
  - `traveller` 段：流放收票期间断言 `st-protection` 计数 = 0（不提前提问）；怪咖达线后入口出现、
    裁定后消失；屠夫（无保护来源）达线时入口计数 = 0、直接计票成立（流放死亡）。
  - 其余 15 段零失败——说书人专属提示字段未影响任何既有面板 / 玩家路径。

截图复核 4 张（其余 50 张为同一次运行写入，未逐张复核，如实记录）：`exile-dial`（收票中：无裁定入口）、
`exile-sweep-done`（达线待裁定：两键出现 + 文案「达线且待裁定，R-0048」）、`exile-protected`
（裁定后入口消失、结论「达线但受死亡保护」）、`exile-exiled`（无保护来源目标流放死亡）——逐张与断言一致。

冻结版门禁（提交前跑）：`dotnet build` 0 警告 0 错误；`dotnet test` **1084 通过 / 0 失败**
（内核 435 · 规则 390 · 集成 234 · 门禁 25）；`dotnet format` 退出 0（未重写工作树）；
`npm run gate` 退出 0（typecheck + lint + 171 测试 + 构建）。

诚实记录（范围）：其余十二个装置未重跑——本批新增字段为说书人视图专属（玩家 DTO 未动，契约由 leak gate
登记、集成用例对玩家 DTO 序列化做零命中断言）；装置 stderr 的重启窗口 `ECONNRESET` / `ECONNREFUSED`
噪声来自 `rebuild` 段故意重启宿主（装置自记「重启窗口噪音=63（非预期 0）」并判过），与本批改动无关。

批次 E36 判出：

- **死亡保护裁定提示投影票验收矩阵 4 行全部通过**（行 1 待裁定窗口 / 行 2 不提前提问 /
  行 3 判定不了（真机不可达，内核 + 前端覆盖，如实记录）/ 行 4 信息隔离）→ 票据移入 `done/`。
- 本批后 `review/`、`in-progress/` 清空；`todo/` 两张（印刷规则书回核 / 屠夫落靶夹具）；
  `done/` 新增 `day-protection-prompt-projection.md`。

## 批次 E37（2026-10-04，处罚处决接入统一死亡保护查询）

冻结版本：`main` @ `58d5aa6`（实现 + 用例 + rulings + 票据同批提交；跑批期间工作树干净）。

本批按「内核用例（先红后绿）+ 处罚处决真机链路」取证：

- 内核过滤集（`DayProtectionTests` / `AdjudicatedExecutionMachineTests`）：**先红**——实现前 2 条新用例失败
  （白天 / 夜晚的「受保护」分支仍写死亡）；实现后 **23 项全过**。
- 冻结版门禁（提交前跑）：`dotnet build` 0 警告 0 错误；`dotnet test` **1088 通过 / 0 失败**
  （内核 439 · 规则 390 · 集成 234 · 门禁 25）；`dotnet format` 退出 0（未重写工作树）。
- 真机装置 `tools/verify-madness.mjs`（取证档 `--quota 2 --screenshots-all --build`）：
  **28 项全部通过 / 0 失败、退出码 0**；5 张截图均为本次运行写入（含强制构建 4.6s）。
- 截图复核 2 张（其余 3 张为同一次运行写入，未逐张复核，如实记录）：`madness-03-mutant-night-punished`
  （夜晚处罚：4 号牌面翻死亡、事件序号 37、白天账未占上限）、`madness-04-day-punished`
  （白天处罚：2 号死亡、白天立即结束并记上限、事件序号 70）——与断言一致。

批次 E37 判出：

- **处罚处决接死亡保护票验收矩阵 5 行全部通过**：行 1 / 2 / 3 / 5 的保护短路面真机不可达
  （今天没有任何覆盖 `Execution` 的来源：`RoleContracts.DeathProtections` 只注册怪咖，且它对非流放死因
  返回 null），由内核用例判出并如实记录；行 4 由真机装置（28 项零回归）+ 集成
  `MadnessPunishmentHostTests`（3 项）+ 内核阴性对照判出。→ 票据移入 `done/`。
- 残余（随票据留档）：行 5 的记账面真机可达但既有装置未走死席处罚；女巫诅咒 / 夜杀族等致死点没有
  `DeathProtectionCause` 分类，等对应保护来源出现时另立票。
- 本批后 `review/`、`in-progress/` 清空；`todo/` 两张（印刷规则书回核 / 屠夫落靶夹具）；
  `done/` 新增 `punishment-execution-death-protection.md`。

诚实记录（范围）：其余十三个装置未重跑——改动只在内核 `AdjudicatedExecutionMachine`（只由
`PunishExecution` 命令面触达，唯一真机覆盖就是 `verify-madness`），且生产行为零改变（怪咖对处决死因
返回 null → 默认「不受保护」，与接线前一致）。

## 批次 E38（2026-10-04，屠夫额外提名落靶 / 二次处决真机夹具）

冻结版本：`main` @ `8e89928`（新装置 + 装置清单登记 + 主装置断言收紧同批提交；跑批期间工作树干净）。

本批按「新装置（落靶路径）+ 主装置（行 3 回归）」取证：

- 新装置 `tools/verify-butcher.mjs`（取证档 `--quota 2 --screenshots-all`）：**40 项全部通过 / 0 失败、
  退出码 0**、43.8s；4 张截图均为本次运行写入。
- 主装置 `tools/verify-storyteller-panel.mjs`（取证档 `--quota 2 --screenshots-all --build`）：
  **286 项全部通过 / 0 跳过、退出码 0**、171.2s（含强制构建 5.2s）；54 张截图均为本次运行写入。
- 截图复核 5 张（其余为同一次运行写入，未逐张复核，如实记录）：`butcher-01-window-open`
  （首次处决后：窗口 6 号 / Open、白天进行中、2 号牌面死亡）、`butcher-02-extra-landed`
  （额外提名计票 → 1 号进入「即将被处决」）、`butcher-03-day-closed`（二次处决后：白天已结束、
  窗口 6 号（已用掉）、1 号死亡归因 day.execution）、`butcher-04-second-target-self-dead`
  （被二次处决者本人界面的死亡横幅 + 当日公告）、`31c-day-butcher-window-used`
  （主装置：不落靶后窗口 = Used、白天仍进行中）——与断言一致。

批次 E38 判出：

- **屠夫落靶 / 二次处决夹具票验收矩阵 3 行全部通过**：行 1 / 行 2 由新装置本次运行判出
  （含入口阴性对照与非授予席位直调 Hub 拒绝 `day.extra_nomination_not_granted`）；
  行 3 由主装置本次运行判出——**本批把「窗口用掉后」的断言从「元素仍在」收紧为
  `data-status=Used`**（独立对抗性复核发现原来从未断言过窗口状态，行 3 的「Used」此前没有
  可失败证据）。→ 票据移入 `done/`。
- 残余（随票据留档）：行 2 的「不占当日提名次数」豁免本身未走（夹具里屠夫没先发起常规提名），
  由内核 `ButcherWindowTests` 既有用例覆盖，装置只判额外提名进账。
- 顺带观察（非本票范围，登记备查）：旅行者白天加入会在当日生死公告里显示「N 号 复活」
  （截图 `butcher-04`）——本批零产品代码改动，属既有行为。
- 诚实记录（范围）：其余十三个装置未重跑——本批零产品代码改动（只新增装置、收紧主装置一处断言、
  改装置计数）；行 3 的本次运行证据即主装置全量取证档。
- 本批后 `review/`、`in-progress/` 清空；`todo/` 一张（印刷规则书回核，等实物）；`done/` 新增
  `butcher-second-execution-fixture.md`。

## 批次 E39（2026-10-05，非首个夜晚获得的「首个夜晚」能力：追加一格结算）

冻结版本：`main` @ `a8b9b2c` + 本批工作树（跑批时的实现与装置分别落成提交 `28e20d1` / `1a43e30`；
本批提交在 2026-10-05 补签时被 `git rebase --gpg-sign` 重写为当前号——**内容未变，只改了签名**，
改写前的号是 `2d93c1d` / `689c0b9`；跑批期间工作树冻结、不再编辑代码）。
跑的是真宿主 + 真 Vite + 真浏览器 + 真 SQLite。

- 主装置 `tools/verify-storyteller-panel.mjs`（取证档 `--quota 2 --screenshots-all --build`）：
  **293 项全部通过 / 0 失败 / 0 跳过、退出码 0**、合计 165.9s（含强制构建）；68 张截图均为本次
  运行写入。冻结版门禁：build 0 警告 0 错误；test **1115 通过 / 0 失败**（Kernel 451 / Rules 402 /
  NormativeGates 25 / Integration 237）；format 退出码 0。
- 截图复核 2 张（其余为同一次运行写入，未逐张复核，如实记录）：
  `night2-bone-collector-window`（集骨者选中已死亡的钟表匠后：槽位计数已是 **4 / 26**——追加已落，
  效果链 `sv:night-2:bone-collector:regain:1` 带「被获得角色：钟表匠」、1 号牌面「已死亡 + 重获能力」）、
  `night2-entry-ability-appended`（追加格成为当前槽位：状态条 `sv:night-2:clockmaker@1:decision`、
  槽位 **14 / 26**、裁定点归属 1 号并给出钟表匠的信息上下文、「引擎没有给出候选选项」）——与断言一致。

批次 E39 判出：

- **「非首个夜晚获得的『首个夜晚』能力」票验收矩阵 12 行全部通过**：
  - 行 1 / 2 / 3 / 9 与行 6 / 7 / 8 由规则单测判出（`GrantedEntryAbilityTests` 11 条：
    三条来源、首夜不追加、每夜能力不追加、时机已过、未生效、契约未实现、两名持有者各一格、
    替换不重复追加、不越过黎明等待格）；
  - 行 4 / 5 / 12 由**真宿主**判出：`BoneCollectorHostTests`（死亡钟表匠 → `SlotInsertedEvent`
    落点 = 涡流之后、行动者保持死亡、裁定归属 2 号、信息只到 2 号）、`GrantedEntryAbilityHostTests`
    （第 2 夜获得钟表匠；说书人手工换角成钟表匠——角色变更四个入口共用同一条落格实现）；
  - 行 10 由内核判出（追加格 + 死亡行动者 + 无重获窗口 → `PromptSkippedEvent`）；
  - 行 11 由内核判出（折叠重放 + 快照 JSON 往返，含 `Owner` / `Character` 字段）；
  - 行 4 的**真机面**由本批主装置 night2-3 段的 7 条新断言判出（总格数 25 → 26、追加格 =
    当前槽位 `clockmaker@1`、裁定归属 1 号、已死亡的钟表匠收到信息、无关席位读不到）。
  → 票据移入 `done/`。
- 独立对抗性复核（新上下文、冻结工作树）当轮发现 **10 项（3 高 / 3 中 / 4 低）**，全部当轮修完并复跑：
  多持有者标识与顺序（标识补席位、裁定与票面统一为"后追加者先唤醒"）、角色变更路径缺运行时证据
  （补手工换角真宿主用例）、票面证据表与真实覆盖错配（逐行改写）、追加位越过黎明等待格的边界
  （实现封顶 + 裁定 + 用例）、装置只验"总格数 +1"（补当前槽位标识断言）、致死性判据重复实现
  （收敛到 `NightOrderTable.HasAction`）、快照往返未断言关键字段、复活口径未登记（写进 R-0055 第 4 条）。
- 残余（随票据留档）：① **复活**（只改生死、角色不变）**不追加**——平台的生死变化只有状态观测
  一个入口，分不清"真复活"与"改掉一次报错的死亡"（不猜，D-0015），口径见 R-0055 第 4 条；
  ② 快照 / 重建等价只证到"折叠 + JSON 往返 + 关键字段"这一层，`StepMachineStateComparer.SlotEquivalent`
  自身不比较 `Owner` / `Character`（既有欠账，未随本批扩大）。
- 诚实记录（范围）：本批只重跑主装置——本轮的改动只在"非首个夜晚获得『首个夜晚』能力"这条路径上
  改变行为，其余十四个装置没有这种场景（逐个人工核对过花名册与发牌）；主装置其余 66 张截图同次
  写入、未逐张复核。
- 本批后 `review/`、`in-progress/` 清空；`todo/` 两张（亡骨魔「保留能力」族、白天信息族剩余两名），
  `done/` 新增 `granted-entry-ability-insertion.md`。

## 批次 E40（2026-10-05，保留能力与白天信息族：三张票一次判完）

冻结版本：`main` @ `5f48a2b`（先提交装置本体与装置清单登记，再对冻结版跑取证档；跑批期间工作树干净、
未改产品代码）。跑的是真宿主 + 真 Vite + 真浏览器 + 真 SQLite，一局 6 席：1 亡骨魔 / 2 女巫 / 3 呆瓜 /
4 博学者 / 5 杂耍艺人 / 6 神谕者（说书人与三名玩家席走真浏览器，恶魔与神谕者走 Node 线级探针）。

- 新装置 `tools/verify-retention-day-info.mjs`（取证档 `--quota 2 --screenshots-all --build`）：
  **66 项全部通过 / 0 失败 / 0 跳过、退出码 0**、79s；12 张截图均为本次运行写入
  （迭代档 33s，辅助装置量级）。
- 冻结版门禁：`dotnet build` 0 警告 0 错误；`dotnet test` **1246 通过 / 0 失败**
  （内核 498 · 规则 483 · 集成 240 · 规范门禁 25）；`dotnet format --verify-no-changes` 退出 0；
  前端 `npm run gate` 退出 0。

本批一次判掉三张票——这正是"验收按批次、不按票据"的意义：三条链路的载体同在一局里。

| 票据 | 矩阵 | 本批判出的界面面 |
|---|---|---|
| 亡骨魔「死亡但保留能力」 | 7 行 | 行 4 / 7：说书人「选中毒侧」追加裁定点（两个候选各写明是哪一侧最近的镇民）、死者牌面「已死亡 + 保留能力」、中毒进状态账与效果归因链、**第 3 夜死者仍被唤醒** |
| 博学者候选辅助面 | 11 行 | 行 11：157 条候选逐条带服务端真值 / 6 个分类按钮分栏 / 两条同真时结论转红且提交禁用 / 互为反面被服务端拒绝且原因可读 / 结清后只给本人两条人话 |
| 白天信息族剩余两名 | 8 行 | 行 2 / 6 / 8：公开猜测在真玩家页面上录进当天账、随公开面下发到每一席、同日第二次入口消失、当晚报数只到本人 |

截图复核 4 张（其余 8 张为同一次运行写入，未逐张复核，如实记录）：

- `retention-08-vigormortis-side-decision`：说书人侧裁定点上下文「亡骨魔杀死了爪牙 2 号：请选择哪一侧
  最近的镇民中毒」，两个候选「4 号玩家（顺时针最近的镇民）」/「6 号玩家（逆时针最近的镇民）」，
  归属 1 号、当前槽位 `sv:night-2:vigormortis:decision`、槽位 13 / 25；
- `retention-10-dead-minion-retained`：2 号牌面「已死亡 + 保留能力」、6 号牌面「中毒」；状态账
  2 号生死=死亡（来由 `sv:night-2:vigormortis:kill`）、6 号中毒=中毒（来由
  `standing:vigormortis.retention:1:2:poison:6`）；效果归因链三条（保留能力窗口 1 → 2、中毒 1 → 6、
  击杀 1 → 2，均「生效中」）；失效账本 6 号 oracle 原因分类「中毒」；
- `retention-07-savant-two-informations`：4 号玩家端「我收到的信息」两条人话（「恶魔坐在奇数位」
  「恶魔与最近的爪牙之间隔着 1 名玩家」），无事实编码与真值；同一页可见当日公开猜测；
- `retention-12-dead-witch-woken`：第 3 夜，2 号（说书人牌面 `data-life=Dead`）的设备上收到
  「女巫选择一名玩家诅咒」的请求（候选 1–6 号），她的信息面板仍为空（无关信息零下发）。

批次 E40 判出：

- **三张票的验收矩阵全部通过**（亡骨魔 7 行 / 博学者 11 行 / 白天信息族 8 行）→ 三个文件移入 `done/`，
  票内补「界面级取证（批次 E40）」一节并更新原「装置档待补」残余。
- 顺带把「互为反面」的现状钉进证据（博学者票残余第 4 条）：界面**不预拦**（前端拿不到互斥组信息、
  按真值照常放行），服务端在提交时拒绝并给出可读原因（`savant.decision_invalid`）；
  要在界面上也变灰，得给候选加互斥组字段——需求方要就给。
- 残余（随票据留档，不阻塞判行）：博学者 28 条候选清单的取舍仍待需求方过一遍（本批只判「辅助面能用」）；
  集骨者重获已死亡的杂耍艺人（百科《集骨者》· 范例 4）专项用例未补（夜晚格与建表绑定均已就位）；
  「宽容处理」与食人族相克随跨剧本扩展票。
- 诚实记录（范围）：本批只跑新装置——三张票的载体（死亡后仍握能力的一格 + 白天两个入口）与主装置 /
  其余十四个装置的花名册没有交集，逐个人工核对过；主装置未重跑。
- 本批后 `review/`、`in-progress/`、`todo/` 全部清空；`done/` 新增三个文件；装置清单 16 个
  （新增「保留能力与白天信息族」一行，`docs/acceptance/devices.md` §1）。

## 相关阅读

- 验收规程：`docs/acceptance/AGENTS.md`
- 装置清单：`docs/acceptance/devices.md`
- 证据怎么留：`docs/evidence/verification.md`
