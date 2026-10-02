# 验收批次记录：每一批判了什么、覆盖到哪里

> **目的**：规程在 `docs/acceptance/AGENTS.md`（怎么判）；本页是记录（每批判了什么、结论是什么）。
> 一批 = 一次构建 + 一次多客户端会话；一行验收只有「通过 / 不通过 / 无法判定」三种结论（规程 §5）。
> E13 及以前的判定写在各自票据里；本页从 E14 起集中记录。

## 批次 E14（2026-10-04）

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

## 批次 E15（2026-10-05）

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

## 批次 E16（2026-10-06）

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
- 本批后 `review/` 清空；`todo/` 剩 `vortox-interference-counting`（Medium）、
  `storyteller-annotation` / `terminal-hold-residue`（Low）。

## 相关阅读

- 验收规程：`docs/acceptance/AGENTS.md`
- 装置清单：`docs/acceptance/devices.md`
- 证据怎么留：`docs/evidence/verification.md`
