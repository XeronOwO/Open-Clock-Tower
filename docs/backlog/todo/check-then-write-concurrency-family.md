# 「先读再写」的并发家族：整行改写会把口令重置静默回滚

- Status: Todo
- Priority: **High**（第一条是账号安全动作被静默回滚；其余三条同形不同后果）
- Depends on: 无
- 咬出它的一轮：批次 **E65**（集成套件并行随机红的根因与收口）——那一轮的独立对抗性复核
  在核对"还有没有第二处『两次独立读再写』"时找出来的。**它们不是那次随机红的成因**，
  是同一类形状（读一份快照 → 按它写 → 中间可能被人改过）的其他落点。

## 要解决的问题

存储层多处是"读一份快照 → 改 → 写回"，而**并发写之间没有任何检测**。后果从"静默丢更新"
到"抛未预期异常"不等，按严重度排：

### 1（High）`EfAccountStore.TryUpdateAsync` 读整行 → 写整行，含口令与恢复码哈希

`src/OpenClockTower.Server/EfAccountStore.cs` 的 `TryUpdateAsync` 把整行读出来、
把 `UsernameKey / Username / DisplayName / PasswordHash / RecoveryCodeHash` **五个字段一起写回**。
`AccountService.ChangeDisplayNameAsync` 与 `ResetPasswordAsync` 是两个独立入口：

1. 改名请求读到 `{口令哈希=旧, 恢复码哈希=旧}`；
2. 重置口令请求读到同一条，写回 `{口令哈希=新, 恢复码哈希=新}`（并把新恢复码明文回给用户一次）；
3. 改名请求随后写回——**把口令与恢复码一起退回旧值**。

用户看到的是"口令重置了、恢复码也发我了"，而库里是旧的：新口令登不进去、新恢复码也无效。
改名本身是一次无害操作，却能把一次账号安全动作回滚掉。

**修法（择一，优先第一条）**：① 改成按列更新（只写这次真的要改的列）——最直接，
改名就只碰 `DisplayName`；② 给 `Users` 行加并发令牌（`RowVersion` / 版本列），撞了就重试或让上层拒绝。
②更通用但要动结构（v5 迁移），①能立刻消掉这条。

### 2（Medium）席位邀请码的 `ReplaceAsync` 不是原子 upsert

`src/OpenClockTower.Server/EfSeatInvitationStore.cs`：先 `FirstOrDefaultAsync` 再 `Add` / 改。
同一席位**首次**并发签发时两边都读到空 → 一方撞主键 `(GameId, Seat)` → `DbUpdateException`
**没人接**，于是"两个说书人同时点签发"会有一个收到未预期异常。
修法：撞约束后重试一次（改成 upsert 语义），或像席位绑定那样接住约束类失败再收敛。

### 3（Medium）`EfSeatBindingStore.TryReleaseAsync` 的"读 → 删"没有并发保护

同席并发双解除时，后到的 `DELETE` 影响 0 行 → EF 默认的"受影响行数"并发检查会抛
`DbUpdateConcurrencyException`（**按 EF 默认行为推断，未跑探针**）。
修法：把"删掉 0 行"当成 `return false`（语义上就是"没有这条绑定"），而不是异常。

### 4（Low）随机标识撞号被当成 upsert 覆盖

`EfGameCatalog.SaveAsync` + `LobbyService.CreateAsync`：先生成随机桌标识、`FindAsync` 确认不存在、
再 `SaveAsync`。撞号（概率极低）时是**覆盖**已有那一桌，而不是拒绝。
修法：写入时按"已存在 → 换一个标识重试"处理。

## 已经确认**不是**本族的（复核过，不要重复怀疑）

- `AccountService.RegisterAsync` → `EfAccountStore.TryCreateAsync`：插入 + 接住 `DbUpdateException` +
  复核 → 返回 `null`，正确。
- `ConnectionRegistry` / `AccountSessionRegistry`：所有变更路径都在**同一把锁**里
  （签发是"读旧绑定 → 作废 → 写新绑定"的复合操作，拆开就会留下两条都有效的凭据——2026-10-02 复核过）。
- `SeatInvitationService.RedeemAsync`：只读。
- `EfGameStore` 的"取最大序号 → 追加"：被 `GameSession._gate` + 进程级单实例锁包住，当前安全
  （依赖的是锁，不是数据库约束——这点记在这里，别把锁删了还以为有约束兜底）。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 改名与口令重置并发 | 两个动作都生效（口令是新值、命名是新名） | 集成用例：两个 `AccountService` 调用交错（或直接对同一行两次 `TryUpdateAsync`），断言两列都是新值 |
| 2 | 同席并发首次签发邀请码 | 两条都拿到一枚有效码，旧的那枚失效，无异常 | 集成用例 + 库里只剩一行 |
| 3 | 同席并发双解除 | 一个 `true` 一个 `false`，无异常 | 服务层用例 |
| 4 | 各修法同族对齐 | 存储层不再有"整行写回"的更新入口 | 代码复核（`TryUpdateAsync` 的写回列清单） |

## 相关阅读

- 同形但已修的落点：`docs/backlog/done/integration-suite-parallel-flakes.md`（读倾斜的收敛）
- 并发与撤销面：`docs/decisions/active.md` D-0021 / D-0038 · `docs/security/authorization-matrix.md`
