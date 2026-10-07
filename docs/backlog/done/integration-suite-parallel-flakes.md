# 集成套件在集合并行下随机红：两类机制都已查清（读倾斜 / 进程级清池）

- Status: **Done**（2026-10-08，批次 E65）
- Priority: ~~**High**~~ → 已清零（门禁可信度：既产假红，也把真红淹进噪声）
- Depends on: 无
- 咬出它的一轮：批次 **E64**（部署实例升结构 v4 · 真机验收补邀请码段）。那一轮改动只在
  `tools/*.mjs` 与 `docs/`，**没有碰任何 C# 与前端**，而全量门禁随机红。

## 要解决的问题

`dotnet test` 的集成项目在默认并行（集合级）下随机红一条，**每次红的不是同一条**。
它有两个**互不相干**的根因，此前被笼统记成"句柄 / 库锁 / 认领竞态"一堆症状：

| 症状（历史红的那一条） | 机制 |
|---|---|
| `ZeroTrustHostTests.Row3_ConcurrentJoins_LeaveExactlyOneValidCredential` → `HubException: 这个账号在本局已经认领了席位 1` | **读倾斜**：`SeatBindingService.ClaimAsync` 的两次读之间被别人的写入穿过 |
| `ObjectDisposedException: SQLitePCL.sqlite3`（Barber / Annotation / StepMachine / SeatInvitation / PitHagResidue / PlayerPushFreshness / TakeoverAndRecovery / PlayerOwnCharacter …） | **进程级清池**：`SqliteConnection.ClearAllPools()` 动的**全进程所有库**的池，而并行时别的用例正在用它们 |
| `SQLite Error 5: 'database is locked'`（TransportHardening / DatabaseMaintenance / 临时探针） | 同上（清池路径自己也会抛：栈见下） |

## 机制一：席位认领的读倾斜（产品缺陷）

`ClaimAsync` 先按席位读、再按账号读，两次读各自一个快照：

1. `FindBySeatAsync(game, seat)` → 那一刻这一席还没人（**旧快照**）；
2. `FindByAccountAsync(game, account)` → 那一刻**自己刚才那次写入已经落库**（**新快照**）；
3. 于是走进"这个账号在本局已经认领了席位 N"分支 → **把幂等那一路当成冲突拒绝**。

四条同账号同席的并发加入里，只要有一条卡在"写入落在两次读之间"，它就会被 Hub 抛回客户端。
它与开头那条"同账号同席 → 幂等接受"的快路径是**同一件事**，结论却相反——这才是它伪装成
领域拒绝的原因（看起来像"业务上不允许"，实际是读到了半成品快照）。

**修法**（三条同族，一起收）：

1. `mine.Seat == seat` → 幂等接受（与快路径同结论）；
2. `mine.Seat != seat` 时**先确认那一行现在仍然在**（按它再读一次席位表）才拒绝——那条绑定也可能
   刚被并发解除（说书人移人 / 账号注销），拿一份过期读去拒绝是同一个病的另一面；
3. `TryBind` 失败后的收敛补全：先按席位读、读不到再按账号读，两次都解不出原因时才用 `conflict`
   （这个码在 `SeatBindingOutcome` 里声明过却从没被产生过）。为了让第 3 条真的可达，
   `EfSeatBindingStore.TryBindAsync` 补上**约束类失败**的判定（`SqliteConstraintViolations`）：
   复核读不到占用、但错误码是 SQLITE_CONSTRAINT（19）时按业务返回 `false`，
   而不是把一个未预期异常抛给 Hub。

- **最小复现（已落成用例）**：`SeatBindingTests` 三条确定性用例 + 各自的假存储
  （`TornSeatReadBindingStore` 席位读恒空 / `StaleAccountReadBindingStore` 账号读看到已解除的那一行 /
  `VanishingOccupancyBindingStore` 写入被挡且两次复核都读不到）。
  **先红后绿**：把这两段收敛逻辑临时退回"不收敛"的写法，后两条当场红。
- 拒绝文案带上两个席位号（"…已经认领了席位 7，不能再认领席位 2"），日志里能直接看出是哪种冲突。

## 机制二：进程级清池会动到别人的池（测试侧）

`TestServerHost.DisposeAsync` 与另外 13 处用例收尾都在用 `SqliteConnection.ClearAllPools()`。
它是**进程级**的：按库清池只动一个连接串的池（同一支探针的读数证明**池键就是连接串字面量**），
反向就是"它一次动**所有**库的池"。集成用例是几十台宿主同进程并行，于是任何一处收尾都会
动到别人的池。

清池路径自己也可能抛——实测栈：

```text
Microsoft.Data.Sqlite.SqliteException : SQLite Error 5: 'database is locked'
   at Microsoft.Data.Sqlite.SqliteConnection.Deactivate()
   at Microsoft.Data.Sqlite.SqliteConnectionInternal.Deactivate()
   at Microsoft.Data.Sqlite.SqliteConnectionPool.Return(SqliteConnectionInternal connection)
   at Microsoft.Data.Sqlite.SqliteConnectionPool.ReclaimLeakedConnections()
   at Microsoft.Data.Sqlite.SqliteConnectionPool.Clear()
   at Microsoft.Data.Sqlite.SqliteConnectionFactory.ClearPools()
   at Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
```

受害方的栈更能说明"别人正在用的句柄被拆了"：那些 `ObjectDisposedException: SQLitePCL.sqlite3`
**不是抛在收尾那边，而是抛在受害者自己的操作中途**——
`sqlite3_prepare_v2`（`SqliteConnectionPragmas.ApplyJournalModeAsync` 里跑 PRAGMA）、
`Microsoft.Data.Sqlite.SqliteConnection.Open()`、`DbContext.SaveChangesAsync`、
`SqliteConnection.BackupDatabase`。

**边界说明（不许写成已证）**：栈里的 `ReclaimLeakedConnections()` 说明清池会回收它**认为泄漏**
（未归还）的连接；"被回收的正是别的线程**手里正在用**的那条"是**推断**，
一次两线程的最小探针（400 次清池 + 另一线程 8500 次开合用循环）**没能单独复现**它——
它只在整解决方案并行（4 个测试项目同时压、几十台宿主）下出现。修法不依赖这条强机制：
"进程级作用域"本身就足以判它出局。

- **最小复现（形状）**：一个临时探针——一条线程反复"开连接 → 查一句 → 关"，
  另一条线程循环 500 次 `ClearAllPools()`，每次 `Task.Delay(1)`。
  **单跑绿**（3 支探针全过）、放进整解决方案并行就红（就是上面那段栈）。
  探针本身已删（留着就违反下面那条门禁），形状留在这里。
- **修法**：改成**按库清池**。落点：
  1. 测试侧唯一出口 `TestDatabaseFiles.ReleasePool(库路径)`，`Delete` / `DeleteOrFail`
     **先释放再删**——"删库"这件事自动带上正确的池作用域；
  2. 产品侧新增 `SqliteConnectionStrings.ForPath`（`Program` 与测试共用同一处事实）：
     连接串也是**分池键**，差一个字符就会清到一个空池；
  3. `MaintenanceCli` 的那句**直接删掉**：它自己的上下文是 `Pooling=False`（连接从不入池），
     那句清的是**别的**（宿主的）池——注释写着"别把句柄留在池里"，实际起作用的是 `Pooling=False`
     本身，清池是多余的且作用域错。集成用例是在**进程内**调这条维护命令的，所以这句曾是真隐患。
- **门禁**：`NormativeGates.Tests/SqlitePoolScopeGateTests` 禁掉 `src/` 与 `tests/` 里的
  `ClearAllPools(` 调用（先过 `SourceText.StripCommentsAndLiterals`，注释里写这个名字不算违规）。
  先红后绿：加门禁时它**只**点名了那支临时探针，探针删除后 1/1 绿。

## 验收矩阵

| # | 场景 | 期望 | 结果 |
|---|---|---|---|
| 1 | 并行连跑 10 次集成全量 | 10 / 10 全绿 | **10 / 10**（28–39s / 次，`--no-build`，冻结产物） |
| 2 | 每条历史随机红都能归因 | 说得出机制 + 有最小复现 | 两类机制各给出栈与确定性用例（见上） |
| 3 | `dotnet test OpenClockTower.slnx` 连续 5 次全绿 | 5 / 5 | **5 / 5**（30–44s / 次） |
| 4 | 同类形状整族对齐 | 没有第二处"两次独立读**共同决定一个拒绝结论**" | 复核过 `SeatInvitationService`（兑换只读）· `AccountService.RegisterAsync`（唯一索引返回 null）· `ConnectionRegistry` / `AccountSessionRegistry`（同一把锁）——**只有席位认领这一处**；另有 4 处"同形不同族"的 check-then-write 已立票 `todo/check-then-write-concurrency-family.md` |

修前/修后的对照读数（同一台机器、同一口径）：

| 阶段 | 口径 | 读数 |
|---|---|---|
| 修前 | 集成项目单独并行 3 次 | 3 / 3 **绿**（负载不够，复现不出） |
| 修前 | 整解决方案并行 3 次 | **2 / 3 红**：两次都是 `ZeroTrustHostTests.Row3_…` |
| 只修机制一 | 整解决方案并行 5 次 | **5 / 5 红**（每次 1–3 条）：句柄已释放 ×6 · 库被锁 ×2 · `JoinByInviteCode` 的 HubException ×1 |
| 两类都修 | 整解决方案并行 5 次 · 集成单独并行 10 次 | **5 / 5 绿** · **10 / 10 绿** |

- **独立对抗性复核**（提交前，新上下文）：11 条意见，本轮修掉 9 条——其中两条是**本轮自己的错**：
  `MaintenanceCli` 清的是别人的池（见上）、以及把"回收正在用的连接"写成了已证机制（已降级为推断）。
  其余是约束类判定补口、过期读的拒绝面、门禁/文档计数与结论强度；剩 2 条（极窄窗口的 `conflict`
  与同形不同族的落点）按下文列进残余。
- 门禁：build **0 警告 0 错误** · `dotnet format` 就地通过 · 规范门禁 **42/42** ·
  集成 **442**（含本轮新增 **8** 条：读倾斜 1 · 过期读 1 · 占用退场收敛 1 · 约束判定 5）· 前端未改（未跑）。
  本轮改动一度把 `TestServerHost.cs` 顶到 602 行、被架构门禁（≤600 行）拦下——删掉与
  `TestDatabaseFiles.ReleasePool` 重复的解释后通过。

## 结论与残余

- **两类随机红的机制都查清并修掉了**，默认并行口径（不关集合并行）连跑 15 次未复现
  （整方案 5 次 + 集成 10 次）。**这是读数不是证明**：样本量决定不了"不存在残存随机性"，
  残余风险 = 下面那些没被覆盖的交错。
- **"单跑集成项目全绿"不能用来否证并行随机红**：负载是这类竞态的一部分
  （修前单跑 3/3 绿、整方案 2/3 红）。复现口径已写进 `docs/development/agent-reference.md` §1。
- 残余（都不影响本票判据，另行处理）：
  1. 同账号同席在**极窄**窗口仍可能吃到一次 `conflict` 拒绝（`TryBind` 看到占用 → 冲突行在两次重读
     之间被释放）。`conflict` 的语义就是"请重试"，但服务端没有自旋重试；
  2. `EfSeatBindingStore.TryReleaseAsync` 的"读 → 删"在并发双解除下会抛 `DbUpdateConcurrencyException`
     （按 EF 默认受影响行数检查推断，未跑探针）——已并入 `todo/check-then-write-concurrency-family.md`；
  3. "一账号一席"依赖的唯一索引 `IX_SeatBindings_GameId_AccountId` 只有元数据面的自愈覆盖，
     行为面用例目前只参数化了 `IX_Users_UsernameKey`（同上票）。
