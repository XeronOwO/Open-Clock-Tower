# 授权矩阵：每个 Hub 方法谁能调、拦在哪、有没有反方向用例

- **状态**：审计第 1 步产出（2026-10-06，被审版本 `main` @ `8b6912a`）；**这是 M2 的起点**，M2 落地时在**本页原地**补用例名与改动，不另起一页。
- **用途**：回答"某个 Hub 方法**谁能调**"，以及"服务端**在哪一行真的拦**"。判定只看服务端代码——**前端不显示按钮不算鉴权**。
- **列说明**：**身份材料** = 该方法参数里能带来身份的东西 · **服务端拦截位置** = 真正判身份的那一处（Hub 层"凭据闸"或 Application 层"身份闸"，含原文引文）· **反方向用例** = 证明"不允许的人也进不来"的自动化用例名 · **缺口** = 缺用例或语义不完整的地方（对应 `web-hardening-audit.md` 的 `G-A4-*`）。

## 0 整体形态（先读这段）

单一凭据链 + 一处集中判定：**账号会话**（只存 SHA-256、固定时间比较、8 小时绝对过期、可撤销）→ **席位票据**（只定位、不授权）→ **连接级凭据**（`ConnectionRegistry`，**唯一身份来源**）。

- `GameHub` 有 **45 个客户端可调方法**（另有框架回调 `OnDisconnectedAsync`）：**41 个**第一步过 `HubActorResolver.Resolve`（命令面，随后进四道闸）/ `ResolveStoryteller`（查询面），其余 **4 个**走 Hub 内显式闸（`SetTableLock` / `ReleaseSeatBinding` / `ProposeSetup` / `GetStorytellerView`）。
  （审计原文记作"41 个公开方法"，本轮用反射点数更正为 45 —— 见 §3 读数 6。）
- 随后 `CommandGatePipeline.CheckIdentity` 按 `ActorKind` 逐命令族判定；**身份闸先于回执短路**（`CheckIdentity` 在 `receipt is not null` 之前），这条顺序没有用例锁住（见 §3）。
- 桌边界靠连接的 `?gameId=` 与 `ConnectionRegistry` 按 `(GameId, SeatId)` 分区；说书人身份靠 `Games.CreatedByAccountId`。

### 0.1 矩阵即代码（**新增方法必须在这里表态**）

上表的每一行在 `tests/OpenClockTower.Integration.Tests/AuthorizationSurfaceHostTests.cs` 里都有对应的一行（`Matrix` 表），三种身份各扫一遍；不表态就红：

| 标记 | 用例 | 扫什么 |
|---|---|---|
| **【反向·玩家】** | `PlayerConnection_MatchesAuthorizationMatrix` | 一条玩家席位连接遍历全表：说书人命令必须被身份闸拒（拒绝码逐行写死），玩家命令**不得**被身份闸拒 |
| **【反向·说书人】** | `StorytellerConnection_MatchesAuthorizationMatrix` | 一条说书人连接同理（反向） |
| **【匿名】** | `AnonymousConnection_IsRejectedOnEveryCredentialedMethod` | 一条没加入过的连接出示伪造凭据：每个收凭据的方法都必须在前门被拒 |
| **【覆盖】** | `Matrix_CoversEveryPublicGameHubMethod` | 反射枚举 `GameHub` 的公开方法，与 `Matrix` 表双向比对：**多一个方法没表态 / 表里有方法已不存在，都会红** |

- 断言只落在**身份这一维**：被允许的身份只要求"没被身份闸拒"（之后可能被阶段 / 合法性闸拒，那不是这一维的事）。这样表不会随玩法改动腐烂，代价是它**不**证明命令能成功——那是各角色自己的验收矩阵（`docs/acceptance/AGENTS.md` §4）。
- 凭据签发路径的 4 个 Join 方法（票据 / 账号会话入座）**不在【扫描】驱动面内**：它们不收连接凭据，"谁是谁"由票据与账号会话决定，各自由既有用例覆盖（各行已注明）。

## 1 `src/OpenClockTower.Server/GameHub.cs`（`/hub/game`，连接必须以 `?gameId=` 声明桌）

| 方法 | 身份材料 | 服务端拦截位置（文件 + 引文） | 允许身份 | 反方向用例 | 缺口 |
|---|---|---|---|---|---|
| `JoinSeat` | 席位票据（+ 可选账号会话走 `JoinSeatWithAccount`） | `HubJoinScope.JoinSeatCoreAsync` → `_scope.GameAsync(...)`（桌必须存在）→ `SeatJoinCoordinator.ResolveSeatAsync`：`var seatTicket = setup.Seats.FirstOrDefault(item => string.Equals(item.Ticket, ticket, StringComparison.Ordinal)); if (seatTicket is null) { … throw new HubException("会话票据无效"); }` | 持本桌任一席票据者（游客亦可） | `AccountHostTests.JoinWithInvalidAccountSession_IsRejected`（账号面）；票据面**无**"无效票据被拒"的真宿主用例；凭据签发路径，不在【扫描】内 | 票据即口令：明文存 `Games.SeatsJson`、不过期、不轮换；锁桌**不**拦此路径（`JoinBySeatAsync` 才查 `setup.IsLocked`）——**运行时已证实**（见 §3） |
| `JoinSeatWithAccount` | 席位票据 **+ 账号会话** | 同上 + `SeatJoinCoordinator.ResolveAccountSession`：`if (_sessions.TryResolve(accountSession, out var accountId)) { return accountId; } … throw new HubException("账号会话无效或已过期，请重新登录（D-0021）");` | 同上，且账号会话有效 | `AccountHostTests.Join_WithInvalidAccountSession_IsRejected`；`AccountHostTests.Claim_IsExclusivePerSeatAndPerAccount`（`"席位 1 已经由其他账号认领"`）；凭据签发路径，不在【扫描】内 | 谁持票谁能认领——票据与账号之间**没有任何绑定校验**，被盗票者先到先得，原主不会收到提示 |
| `JoinTable` | 账号会话（**必须**） | `SeatJoinCoordinator.JoinBySeatAsync`：`var accountId = ResolveAccountSession(accountSession, connectionId) ?? throw new HubException("自助入座需要先登录账号");` + 席位范围 / 锁桌 / `ClaimSeatAsync` 三条前置 | 任何登录账号，且该席未被别人占、桌未锁 | `SelfServiceJoinHostTests.SeatTakenByAnotherAccount_IsRejected`、`.SameAccountClaimingTwoSeatsInOneTable_IsRejected`、`.SeatOutOfRange_IsRejected`、`.JoiningWithoutAccount_IsRejected`、`.LockedTable_RejectsNewJoin_ButKeepsExistingPlayers`；凭据签发路径，不在【扫描】内 | 无"任意登录账号可枚举桌并逐个占席"的限速；`?gameId=` 可枚举（见 G-A4-4） |
| `JoinStorytellerWithAccount` | 账号会话 | `HubJoinFlow.JoinStorytellerAsync`：`if (setup.CreatedByAccountId is not { } owner \|\| owner != accountId) { … throw new HubException("这一桌不是你开的"); }` | **本桌开桌账号** | `MultiTableHostIsolationTests.OwnerOfOneTable_IsRejectedByAnotherTable`、`LobbyHostTests.CreateTable_ByOrdinaryPlayer_Succeeds_AndOwnerAccountOpensThatTableStorytellerConsole`（`Assert.ThrowsAsync<HubException>` 断言"路人乙"进不去）；凭据签发路径，不在【扫描】内 | 无（同局只保留一条说书人连接：`IssueForStoryteller` 先 `Revoke` 本桌旧连接——**但顶掉时不告知先进者**，见 G-A2-5） |
| `SubmitResponse` | 连接凭据 | Hub：`ResolveActor(credential)`；闸：`SubmitResponseCommand when actor.Kind == ActorKind.Player && actor.Seat is not null => null` + `CommandGatePipeline.CheckPhase`：`\|\| envelope.Actor.Seat != pending.Addressee) { return Reject("phase.no_request_for_you", …); }` | 被指名的那个席位本人 | `ZeroTrustHostTests.Row1_5_OtherSeatsPendingRequest_IsRejectedByPhaseGate`、`StepMachineHostTests.Row10_NonActorResponse_RejectedByPhaseGate`、`.Row11_DuplicateResponse_AppliesOnceAndReturnsSameResult`、**【反向·说书人】** | 无 |
| `AskArtistQuestion` | 连接凭据 | 闸：`AskArtistQuestionCommand => ArtistQuestionGate.IdentityRejection(actor)`（要求 Player + 席位） | 持席位的玩家（内核再判"是不是艺术家 / 今天用过没"） | `SeamstressArtistHostTests.AskArtistQuestion_RejectsOutsideDay_AndForNonArtist`、**【反向·说书人】** | 无（"不是艺术家"由内核拒） |
| `AskSavantQuestion` | 连接凭据 | 闸：`AskSavantQuestionCommand => SavantQuestionGate.IdentityRejection(actor)` | 持席位的玩家 | `SavantHostTests.Savant_AsksForTwoMessages_OncePerDay_OnlyToSelf`（含"只到本人"反方向）、**【反向·说书人】** | 无 |
| `MakeJugglerGuesses` | 连接凭据 | 闸：`MakeJugglerGuessesCommand => JugglerGuessGate.IdentityRejection(actor)` | 持席位的玩家 | `JugglerHostTests`（断言猜测只落本人面）、**【反向·说书人】** | 无 |
| `VoidRequest` | 连接凭据 | Hub：`ResolveActor`；闸：命中 `_ when actor.Kind == ActorKind.Storyteller => null`，玩家落到 `_ => Reject("identity.storyteller_only", …)` | 说书人 | `AnnotationHostTests.PlayerCredential_CannotWriteAnnotations`（同款默认分支）· **【反向·玩家】**（本轮补上直证） | **已补**（G-A4-6，批次 E54）：玩家调 `VoidRequest` 被拒由【反向·玩家】直接锁住 |
| `ProxyFill` | 连接凭据 | 同 `VoidRequest`（默认分支） | 说书人 | 同上 · **【反向·玩家】**（本轮补上直证） | **已补**（G-A4-6 / E54） |
| `ForceAdvance` | 连接凭据 | Hub：`ResolveActor`；闸：默认分支 `_ => Reject("identity.storyteller_only", "这条命令只有说书人可以发出", "identity")` | 说书人 | `ZeroTrustHostTests.Row4_PlayerCredential_CannotIssueStorytellerCommands`（`Assert.Equal("identity.storyteller_only", result.RejectionCode)`）· **【反向·玩家】** | 无 |
| `TakeOver` | 连接凭据 | 同 `ForceAdvance`（默认分支） | 说书人 | **【反向·玩家】**（原为缺口，本轮补） | **已补**（G-A4-6 / E54） |
| `ReleaseControl` | 连接凭据 | 同 `ForceAdvance`（默认分支） | 说书人 | **【反向·玩家】**（原为缺口，本轮补） | **已补**（G-A4-6 / E54） |
| `ResolveDecisionPoint` | 连接凭据 | 同 `ForceAdvance`（默认分支） | 说书人 | **【反向·玩家】**（原为缺口，本轮补） | **已补**（G-A4-6 / E54） |
| `ReportSeatState` | 连接凭据 | Hub 显式先过闸：`// 先过凭据闸再解析参数` → `var actor = ResolveActor(credential);`；闸：`ApplySeatStateCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null` | 说书人 | **【反向·玩家】**（原为缺口，本轮补；这是"上报任何人任何维度的角色 / 阵营"的入口） | **已补**（G-A4-6 / E54） |
| `AssignCharacters` | 连接凭据 | 闸：`AssignCharactersCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null` + `AssignmentGate.Check` | 说书人 | `NightBuildHostTests.IllegalAssignmentsAndStartNight_AreRejected`（参数面）· **【反向·玩家】**（身份面，本轮补） | **已补**（G-A4-6 / E54） |
| `JoinTraveller` | 连接凭据 | 闸：`JoinTravellerCommand or RemoveTravellerCommand => TravellerGate.IdentityRejection(…)` | 说书人 | `TravellerHostTests.JoinTraveller_Guardrails_RejectExplicitly`（含"玩家不能发"）；另有**一次性探针**证实玩家复用说书人的幂等键仍被身份闸拒（见 §3）· **【反向·玩家】** | 无越权面；残留是"回执没有归属字段"，靠身份闸在前这条**顺序**兜着（G-A4-1，已从 High 降为 Low） |
| `RemoveTraveller` | 连接凭据 | 同上 | 说书人 | `TravellerHostTests.RemoveTraveller_Guardrails_RejectExplicitly`（含"玩家身份显式拒绝"）· **【反向·玩家】** | 无 |
| `StartNight` | 连接凭据 | 闸：`StartNightCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null` | 说书人 | `NightBuildHostTests.IllegalAssignmentsAndStartNight_AreRejected`（含非法开夜）· **【反向·玩家】**（身份面，本轮补） | **已补**（G-A4-6 / E54） |
| `StartDay` | 连接凭据 | 闸：`StartDayCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null` | 说书人 | `DayPhaseHostTests.DayCommands_EnforceIdentityGateInBothDirections`（`var playerStartDay = await seat1.InvokeAsync<CommandResultDto>("StartDay", …)` 断言被拒）· **【反向·玩家】** | 无 |
| `Nominate` | 连接凭据 | 闸：`NominateCommand when actor.Kind == ActorKind.Player && actor.Seat is not null => null` + `SeatGate.CheckExists(nominate.Nominee, setup)` | 持席位的玩家（提名者＝自己，命令面无自称号） | `DayPhaseHostTests.DayCommands_EnforceIdentityGateInBothDirections`（说书人面反向）· **【反向·说书人】** | 无 |
| `NominateExtra` | 连接凭据 | 闸：`NominateExtraCommand when actor.Kind == ActorKind.Player && actor.Seat is not null => null` | 持席位的屠夫本人（内核判窗口） | `ButcherHostTests`（正面）· **【反向·说书人】**（身份面，本轮补） | **已补**（G-A4-6 / E54） |
| `CastVote` | 连接凭据 | 闸：`CastVoteCommand when actor.Kind == ActorKind.Player && actor.Seat is not null => null` | 持席位的玩家（票面按凭据推导的席位记账） | `DayPhaseHostTests.DayCommands_EnforceIdentityGateInBothDirections` · **【反向·说书人】** | 无 |
| `StartVoteSweep` | 连接凭据 | 闸：`StartVoteSweepCommand or ResumeVoteSweepCommand or CollectSeatVoteCommand => VoteSweepGate.IdentityRejection(…)`；`VoteSweepGate`：`StartVoteSweepCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null` | 说书人 | **【反向·玩家】**（原为缺口，本轮补） | **已补**（G-A4-6 / E54） |
| `ResumeVoteSweep` | 连接凭据 | 同 `StartVoteSweep`（`ResumeVoteSweepCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null`） | 说书人 | **【反向·玩家】**（原为缺口，本轮补） | **已补**（G-A4-6 / E54） |
| `CountVotes` | 连接凭据 | 闸：`CountVotesCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null` | 说书人 | **【反向·玩家】**（原为缺口，本轮补；计票直接决定生死） | **已补**（G-A4-6 / E54） |
| `CloseDay` | 连接凭据 | 闸：`CloseDayCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null` | 说书人 | **【反向·玩家】**（原为缺口，本轮补） | **已补**（G-A4-6 / E54） |
| `ProposeExile` | 连接凭据 | 闸：`ExileGate.IdentityRejection`：`ProposeExileCommand when actor.Kind == ActorKind.Player && actor.Seat is not null => null` | 持席位的玩家（含死者，R-0044） | `ExileHostTests.PlayerProjection_ExposesExilePermissionsAndCandidates`（正面）· **【反向·说书人】**（身份面，本轮补） | **已补**（G-A4-6 / E54） |
| `CastExileVote` | 连接凭据 | 闸：`CastExileVoteCommand when actor.Kind == ActorKind.Player && actor.Seat is not null => null` | 持席位的玩家 | **【反向·说书人】**（原为缺口，本轮补） | **已补**（G-A4-6 / E54） |
| `StartExileSweep` | 连接凭据 | 闸：`StartExileSweepCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null` | 说书人 | **【反向·玩家】**（原为缺口，本轮补） | **已补**（G-A4-6 / E54） |
| `ResumeExileSweep` | 连接凭据 | 闸：`ResumeExileSweepCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null` | 说书人 | **【反向·玩家】**（原为缺口，本轮补） | **已补**（G-A4-6 / E54） |
| `CountExileVotes` | 连接凭据 | 闸：`CountExileVotesCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null` | 说书人 | **【反向·玩家】**（原为缺口，本轮补） | **已补**（G-A4-6 / E54） |
| `ResolveDayProtection` | 连接凭据 | 闸：`ResolveDayProtectionCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null` | 说书人 | `DeviantHostTests.DeviantRuling_IsOnlyAcceptedAtTheDecidingMoment`（**时点**面，非身份面）· **【反向·玩家】**（身份面，本轮补） | **已补**（G-A4-6 / E54） |
| `PunishExecution` | 连接凭据 | 闸：`PunishExecutionCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null` | 说书人 | **【反向·玩家】**（原为缺口，本轮补；直接杀人） | **已补**（G-A4-6 / E54） |
| `PitHagCasualty` | 连接凭据 | 闸：`PitHagCasualtyCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null` | 说书人 | **【反向·玩家】**（原为缺口，本轮补；直接杀人） | **已补**（G-A4-6 / E54） |
| `ResolveDeferredDeath` | 连接凭据 | 闸：`ResolveDeferredDeathCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null` | 说书人 | **【反向·玩家】**（原为缺口，本轮补） | **已补**（G-A4-6 / E54） |
| `RebuildRoom` | 连接凭据 | 闸：`RebuildRoomCommand when actor.Kind is ActorKind.Storyteller or ActorKind.Host => null`，否则 `Reject("identity.storyteller_only", "只有说书人或宿主可以重建房间", "identity")` | 说书人 | **【反向·玩家】**（原为缺口，本轮补；重建会改写派生状态） | **已补**（G-A4-6 / E54） |
| `AddSeatAnnotation` | 连接凭据 | 闸：`AddSeatAnnotationCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null` | 说书人 | `AnnotationHostTests.PlayerCredential_CannotWriteAnnotations` · **【反向·玩家】** | 无 |
| `UpdateSeatAnnotation` | 连接凭据 | 闸：`UpdateSeatAnnotationCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null` | 说书人 | `AnnotationHostTests.PlayerCredential_CannotWriteAnnotations`（同族）· **【反向·玩家】** | 无 |
| `RemoveSeatAnnotation` | 连接凭据 | 闸：`RemoveSeatAnnotationCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null` | 说书人 | `AnnotationHostTests.PlayerCredential_CannotWriteAnnotations`（同族）· **【反向·玩家】** | 无 |
| `ReleaseSeatBinding` | 连接凭据 | Hub 显式闸：`_ = ResolveStorytellerActor(credential);` → `HubActorResolver.ResolveStoryteller`：`if (actor.Kind != ActorKind.Storyteller) { … throw new HubException("当前连接不是有效的说书人连接（D-0012）"); }` | **本桌**说书人 | `AccountHostTests.ReleaseSeatBinding_ClearsName_AndFreesTheSeat`（`await Assert.ThrowsAsync<HubException>(() => guest.InvokeAsync<bool>("ReleaseSeatBinding", 1));`）· **【反向·玩家】** | **无界面入口**（`web/src` 无 `ReleaseSeatBinding`），只能直调，见 G-A4-7 |
| `SetTableLock` | 连接凭据 | Hub 显式闸：`_ = ResolveStorytellerActor(credential);` | **本桌**说书人 | `SelfServiceJoinHostTests.LockedTable_RejectsNewJoin_ButKeepsExistingPlayers`（锁成功＝身份成立的闭环）+ 锁后新人被拒 · **【反向·玩家】** | **无界面入口**（`web/src` 无 `SetTableLock`），见 G-A4-7；锁**不拦**票据路径（**运行时已证实**，见 §3） |
| `ProposeSetup` | 连接凭据 | Hub 显式闸：`_ = ResolveStorytellerActor(credential);` | 本桌说书人 | `SetupProposalHostTests.ProposeSetup_RejectsPlayersAndAnonymousConnections`（玩家 + 匿名两条都断言抛错）· **【反向·玩家】** | 无 |
| `GetStorytellerView` | 连接凭据 | Hub 显式闸：`_ = ResolveStorytellerActor(credential);` | 本桌说书人 | `ZeroTrustHostTests.Row11_RejectionsAreAudited_AndCredentialsNeverLoggedInPlaintext`（玩家读说书人视图抛 `HubException`）；`Row2_ConnectionWithoutJoin_CannotInvokeCommands`（匿名连接）· **【反向·玩家】** | 无 |
| `GetReplay` | 连接凭据 | Hub：`var actor = ResolveActor(credential);` → `ReplayQueryService.ReadAsync`：`if (actor.Kind is not (ActorKind.Player or ActorKind.Storyteller)) throw Deny(…)` + `if (actor.Kind == ActorKind.Player && !replay.Ended) throw Deny(actor, replay, "本局尚未结束，复盘在结束批次之后才对局内玩家开放（R-0043）");` | 说书人随时；玩家仅本局结束后 | `ReplayHostTests.Replay_IsDeniedForPlayersBeforeEnd_AndOpensAfterEnd`（"结束前玩家零复盘数据"）· **【反向·玩家】** / **【反向·说书人】**（两类身份都**不得**被身份闸拒） | 无（复盘文案是否夹带角色 / 阵营未逐字段验证，见审计页 §5） |
| `OnDisconnectedAsync` | —（框架回调，非客户端可调） | `ConnectionRegistry.Remove(Context.ConnectionId)` + `HubGameScope.Forget(…)` | 系统 | — | 无 |

## 2 `src/OpenClockTower.Server/AccountHub.cs`（`/hub/account`，**全表匿名可达**）

| 方法 | 身份材料 | 服务端拦截位置（文件 + 引文） | 允许身份 | 反方向用例 | 缺口 |
|---|---|---|---|---|---|
| `ListTables` | 账号会话（**可为 null**） | `AccountHub.ListTables`：`var viewer = await ResolveAccountAsync(accountSession);`（无效 / 缺省 → `viewer = null`，**不抛错**） | 任何人（未登录亦可）——有意为之 | `LobbyHostTests.CreateTable_ByOrdinaryPlayer_…`：`var anonymous = await account.InvokeAsync<IReadOnlyList<LobbyTableDto>>("ListTables", null); Assert.All(anonymous, item => Assert.False(item.CreatedByMe));` | 无私有字段（`LobbyTableDto` 只有 `GameId / Name / SeatCapacity / TakenSeatCount / Started / Locked / OccupiedSeatNumbers / CreatedByMe / MySeatNumbers`）；但 `OccupiedSeatNumbers` 让**未登录**者也能读出每桌哪些席位被占，且 `GameId` 可枚举 → G-A4-4 |
| `CreateTable` | 账号会话（必须） | `LobbyService.CreateAsync`：`if (!_tableCreation.CanCreate(account)) { … return account is null ? Fail("invalid_session", …) : Fail("not_allowed", …); }`；`TableCreationPolicy.CanCreate`：`account is not null && (_allowPlayerTables \|\| _operators.IsOperator(account))` | 任何登录账号（默认放开）；收口后仅运维名单 | `LobbyHostTests.CreateTable_WhenPlayerTablesDisabled_RejectsOrdinaryPlayer_ButOperatorSucceeds`、`.CreateTable_WhenDisabled_AndNoOperatorConfigured_RejectsEveryone`、`.CreateTable_WithInvalidSession_IsRejected`、`.RejectedCreate_LeavesNothingBehind` | **无任何限速 / 配额**：默认配置下刷桌成本为零，见 G-A1-1 / G-A5-5 |
| `Register` | 无 | `AccountService.RegisterAsync`（口令策略 + 登录名唯一）；`AccountHub`：`if (!outcome.Accepted \|\| outcome.Account is null) { … return Reject(outcome); }` | 任何人 | `AccountFoundationTests.Register_RejectsInvalidInput_BeforeTouchingStore`、`.Register_ReturnsRecoveryCode_AndRejectsDuplicateUsernameCaseInsensitively` | **无限速 / 无配额**（每次注册固定两次 PBKDF2）→ G-A5-2 |
| `Login` | 口令 | `AccountService.AuthenticateAsync`；失败一律中性：`return Reject(outcome);`（`AccountFoundationTests.Authenticate_IsNeutral_ForWrongPasswordAndUnknownUser`） | 任何人（凭口令） | 同上 | **无失败次数限制 / 渐进延迟 / 锁定** → G-A1-1 |
| `Resume` | 账号会话 | `AccountHub.Resume`：`var account = await ResolveAccountAsync(accountSession); if (account is null) { … return InvalidSession(); }`；回执 `Accept(account, accountSession: null, recoveryCode: null)` | 任何持有效会话者（只回自己资料） | `AccountHostTests.Resume_ReturnsProfileWithoutCredential_AndRejectsRevokedSessions`、`.Resume_AfterPasswordReset_RejectsOldSession_AndAcceptsNewOne` | 无（方法不带任何账号标识参数，不存在"查别人"入口） |
| `Logout` | 账号会话 | `AccountHub.Logout`：`var revoked = _revocation.RevokeSession(accountSession);` → `AccountRevocationService`（撤会话 + **同批撤它建立的在线连接**；幂等） | 任何持会话者（只能撤自己那一条） | `SessionRevocationHostTests.Logout_RejectsCommandsOnLiveSeatConnection`（旧席位连接被拒）· `.Logout_RejectsQueriesOnLiveStorytellerConnection`（旧主持连接被拒）· `.Logout_OnlyRevokesConnectionsOfThatSession`（不牵连同账号另一条会话）· `.Logout_DoesNotTouchGuestConnection`（游客不受牵连）· `AccountSessionRegistryTests.Revoke_InvalidatesSession` | **已修**（M2 第一刀，G-A2-1）：撤销现在打到**已建立**的连接上，命令与推送一起停；客户端侧"主动断开 + 提示文案"留在 G-A2-7 |
| `ChangeDisplayName` | 账号会话 | `AccountHub.ChangeDisplayName`：`if (!_sessions.TryResolve(accountSession, out var accountId)) { … return InvalidSession(); }` → `_accounts.ChangeDisplayNameAsync(accountId, displayName, …)` | 任何持有效会话者（只能改自己） | `AccountHostTests.ChangeDisplayName_PropagatesToPlayerAndStorytellerViews`（正面）；**伪造会话无直证** | 缺一条"伪造会话改名被拒"的反向直证；另：改名遍历**全部桌**广播（G-A10-8） |
| `ResetPassword` | 登录名 + 恢复码 | `AccountService.ResetPasswordAsync`（恢复码校验 + 轮换）；撤销走 `AccountRevocationService.RevokeAllForAccount`（该账号**全部**会话 + 它们的在线连接） | 持正确恢复码者 | `AccountHostTests.ResetPassword_RotatesRecoveryCode_AndRevokesOldSessions`（含旧会话 / 旧口令 / 旧恢复码三条反向）· `SessionRevocationHostTests.ResetPassword_RejectsLiveSeatConnection`（在线连接被踢） | 无限速（G-A1-1）；撤销覆盖面**已修**（M2 第一刀，G-A2-1） |

## 3 审计追加的运行时读数与更正

| # | 读数 | 结果 |
|---|---|---|
| 1 | **未授权调用抽样**（部署实例，伪造凭据调 `GetStorytellerView` / `GetReplay` / `SubmitResponse` / `StartNight` / `SetTableLock` / `ReleaseSeatBinding` / `ProposeSetup` / `JoinTable` / `JoinStorytellerWithAccount`） | **逐个被拒**，文案一致（`连接凭据无效：请先用票据加入（D-0012）` / `这一桌不存在` / `账号会话无效或已过期`）——未授权入口是关着的 |
| 2 | **幂等回执能否被别的演员复用**（一次性探针：说书人 `JoinTraveller` 用键 K → 玩家用同一个 K 再调） | 玩家拿到 `identity.storyteller_only`、**没有票据**。原因：`CommandGatePipeline` 先 `CheckIdentity`、后才看 `receipt`。**静态分析判定的"玩家可偷走席位票据"被推翻**；残留是回执无归属字段 + 这条**顺序没有用例锁住**（G-A4-1，Low） |
| 3 | **锁桌是否拦票据入座**（一次性探针：`SetTableLock(true)` → 未消费票据 `JoinSeat`） | 锁桌返回 `True`，**票据照样入座成功**——锁只拦自助那条路（G-A4-2，Medium） |
| 4 | **同账号两条说书人连接** | 后进者可用，**先进者立即失效且事先没有任何提示**（G-A2-5，High） |
| 5 | **撤销覆盖面**（M2 第一刀，修复后复测） | 登出 / 口令重置后，**旧连接的命令被凭据闸拒**（`连接凭据无效`），私有推送同时停；只踢那一条会话建立的连接，同账号另一条会话与游客连接不受牵连（`SessionRevocationHostTests` 5 条 + 单测 8 条） |
| 6 | **授权面全量扫描**（M2 第二刀，G-A4-6） | 反射点数：`GameHub` **45 个客户端可调方法**（审计原文记作 41，含框架回调 `OnDisconnectedAsync` 实为 46）；表驱动扫描 **41 行 × 3 种身份**（玩家 / 说书人 / 匿名）逐个核对身份闸，另有 4 个凭据签发路径不在驱动面内（各行已注明）。**先红证明 4 处**：放宽说书人闸 → 玩家扫描红（`PunishExecution`）；放宽玩家闸 → 说书人扫描红（`Nominate`）；方法去掉凭据闸 → 玩家 + 匿名两条红（`SetTableLock`）；新增 Hub 方法不表态 → 覆盖门禁红（`ProbeSurfaceGate`）——见 `AuthorizationSurfaceHostTests` 与批次 E54 |

**M2 的施工建议（按优先级）**：① ~~G-A2-1 撤销覆盖面~~ **已完成**（M2 第一刀，批次 E53）→ ② ~~23 个说书人命令的反方向用例~~ **已完成**（M2 第二刀，G-A4-6，批次 E54；改为 41 行 × 3 身份的表驱动扫描 + 覆盖门禁）→ ③ G-A4-2 锁桌语义统一 → ④ G-A4-3 桌标识比对 → ⑤ G-A4-4 大厅字段面 → ⑥ G-A4-1 回执归属 + 锁住"身份闸先于回执"的顺序 → ⑦ G-A2-7 前端撤销性（含"登出后主动断开牌局连接 + 提示"）。

## 相关阅读

- 差距清单与严重度汇总：`docs/security/web-hardening-audit.md`
- 票据（M2 的验收就是本页 + 每条一条正 / 反用例）：`docs/backlog/in-progress/web-hardening-programme.md`
- 零信任模型（凭据链的来源）：`docs/decisions/active.md` D-0012 · 多桌与归属：D-0024 / D-0027
- 上一轮零信任工作的验收矩阵：`docs/backlog/done/zero-trust-security-model.md`
