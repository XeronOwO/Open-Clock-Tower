# 集成套件在集合并行下随机红：句柄已释放 / 库锁 / 认领竞态伪装成领域拒绝

- Status: Todo
- Priority: **High**（门禁的可信度：它既产假红，也会把真红淹进噪声里——"全绿才可提交"这条纪律目前不成立）
- Depends on: 无
- 咬出它的一轮：批次 **E64**（部署实例升结构 v4 · 真机验收补邀请码段）。那一轮改动只在 `tools/*.mjs` 与
  `docs/`，**没有碰任何 C# 与前端**，而全量门禁随机红。

## 要解决的问题

`dotnet test OpenClockTower.slnx` 的**集成项目**在默认并行下会随机红一条，**每次红的不是同一条**，
症状全是宿主生命周期 / SQLite 句柄与锁的争用：

| 运行 | 结果 | 红的那一条 | 消息 |
|---|---|---|---|
| 全量（第 1 次） | 433 / 434 | `ZeroTrustHostTests.Row3_ConcurrentJoins_LeaveExactlyOneValidCredential` | `HubException: 这个账号在本局已经认领了席位 1` |
| 集成全量（第 1 次） | 433 / 434 | `BarberHostTests.BarberExecutedDuringDay_OpensSwapAtNight_AndRebindsLaterSlot` | `ObjectDisposedException: Cannot access a disposed object. Object name: 'SQLitePCL.sqlite3'` |
| 集成全量（第 2 次） | 433 / 434 | `TransportHardeningHostTests.TransportLimits_AreExplicitValues_NotFrameworkDefaults` | `Microsoft.Data.Sqlite.SqliteException: SQLite Error 5: 'database is locked'` |
| 集成全量（第 3 次） | **434 / 434** | — | — |
| 全量（第 2 次） | 433 / 434 | `ZeroTrustHostTests.Row3_ConcurrentJoins_…` | 同上（认领竞态） |
| 全量（第 3 次） | 433 / 434 | `AnnotationHostTests.MultilineText_IsCollapsedBeforeStoring` | `ObjectDisposedException: SQLitePCL.sqlite3` |
| `Row3_…` 单跑（隔离，8 次） | **8 / 8 全绿** | — | 每次 ~1s |
| 集成全量（**关掉集合并行**） | **434 / 434 全绿** | — | 3m54s（并行 33–41s） |

并行 5 次里 4 次红、串行 1 次全绿——**根因在"集合并行"这一层**，不在被红的那条用例里。

## 已经排除的

- **残留进程**：先 `dotnet build-server shutdown` 关掉 12 个 MSBuild 复用节点、复核 `dotnet` 进程数为 0，
  再跑仍然红（不是构建节点抢资源）；
- **被测代码**：那一轮的改动只有 `tools/*.mjs` 与 `docs/`（`git status` 可查），C# 与前端一个字节没动；
  红的四条用例彼此不相干（零信任并发 / 理发师换角色 / 注释文本折叠 / 传输上限常量）；
- **共享库文件**：每台宿主各自一份 `%TEMP%\oct-test-{guid}.db`（`TestServerHost` 构造时现造），
  不是多台宿主踩同一个库；
- **单条不稳**：那条并发用例隔离跑 8/8 全绿，说明红要"整机负载 + 同进程多宿主"才出现。

## 线索（给接手的人）

1. **两类症状都是"谁在宿主 dispose 之后还用连接"的形状**：`ObjectDisposedException: SQLitePCL.sqlite3`
   直接指向"连接/`sqlite3` 句柄已释放仍被使用"。常驻服务（`GameBootstrapHostedService` / 节拍器 /
   空闲桌清扫）在 `DisposeAsync` 之后是否还持有 `DbContext`，是第一个要查的地方。
2. **`database is locked` 要查 `busy_timeout`**：应用默认 5000ms（`GameServer__Sqlite__BusyTimeoutMilliseconds`），
   测试宿主是否显式覆盖成 0 / 很小时最可能被反复撞上。
3. **第三条是"伪装成领域拒绝"的那种**：`Row3_…` 期望"同账号同席的并发认领收敛成幂等、只剩最后签发的凭据有效"，
   却收到 `SeatBindingService.ClaimAsync` 前置检查分支的拒绝（`这个账号在本局已经认领了席位 1`）——
   也就是 `FindBySeatAsync` 与 `FindByAccountAsync` 在并发下读到了**不一致的快照**。
   这条即使修好前两条也要单独看：它是"读倾斜"，与句柄释放不是同一个机制。
4. **别把"串行全绿"当修好**：串行 3m54s vs 并行 35s，代价太大；默认口径要的是"并行下也不随机红"。

## 下一步（按代价从低到高）

1. 把"临时串行跑法"记进 `docs/acceptance/AGENTS.md`（`dotnet test … --settings <关掉 ParallelizeTestCollections>`），
   供"怀疑随机红"时对照用——这**不是**默认口径；
2. 查常驻服务与 `DisposeAsync` 的时序（线索 1），拿到"谁在释放之后用连接"的最小复现；
3. 核对测试宿主的 `busy_timeout`（线索 2）；
4. 若确认是"并行 + 单机磁盘"的固有争用，给集成集合加**资源闸**（例如按 SQLite 文件/磁盘串行化的
   collection fixture），而不是让它随机红。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 并行连跑 10 次集成全量 | 10 / 10 全绿 | 运行记录（附耗时） |
| 2 | 每条历史随机红都能归因 | 说得出机制（谁释放后还在用 / 哪两张快照不一致），并有最小复现 | 最小复现脚本或集成用例 |
| 3 | 门禁可信 | `dotnet test OpenClockTower.slnx` 连续 5 次全绿 | 运行记录 |
