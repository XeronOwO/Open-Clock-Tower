# 公网就绪审计：能跑与敢放公网之间差什么

- **状态**：第 1 步（只读审计）**已完成**，2026-10-06。差距清单是第 2 步（M2–M6）的输入，不是"以后再说"的备忘。
  **修复进度（滚动）**：第 2 步已开工——**G-A2-1（撤销覆盖面，Critical）已修**、**G-A4-6（授权面反方向用例）已修**，见各条目的修复记录；
  本页的条数统计与严重度汇总仍是 **2026-10-06 的基线读数**，不随修复回填，"已修"逐条打在 §2 的条目上。
- **被审版本**：`main` @ `8b6912a`（工作树干净；审计全程只读，未改任何产品代码）。
- **一句话结论**：**核心玩法与凭据链是扎实的**（口令加盐慢哈希、凭据只存哈希、固定时间比较、逐命令服务端鉴权、投影隔离有正反用例），
  但**当前状态不得对外发布**——`Critical` 未清零：全站明文 HTTP、明文链路上传全部身份材料、三个账号入口与开桌/入座零限速。
- **编号约定**：差距号沿用维度前缀（`G-A<n>-<k>`），便于回溯到各维度结论；跨维度同源条目**已合并**并在条目内注明原编号。

## 1 方法与读数来源

审计按"运行证据为准"做，不靠纸面推演。四类读数：

| # | 读数类型 | 怎么取的 | 覆盖 | 局限 |
|---|---|---|---|---|
| R1 | **部署实例只读探测** | 从本机对部署地址做 `curl -I` / `GET` / `OPTIONS`，以及 **SignalR over WebSocket 一次性探针**（仓库外临时脚本，跑完删除） | 响应头、协议、明文面、未授权调用、登录耗时与限速 | 只做只读与必然被拒的调用，未做任何写操作 |
| R2 | **真宿主集成测试** | `dotnet test OpenClockTower.slnx`（`TestServerHost` 起真宿主 + 真 SignalR + 真 SQLite）；另跑一组**一次性探针用例**（跑完删除），故意用 `Assert.Fail` 把读数打进出错信息 | 撤销语义、并发进主持台、锁桌与票据、幂等键跨演员复用 | 进程内宿主，不是独立部署进程 |
| R3 | **服务端只读检查** | SSH 只读命令：`systemctl show` / `cat` 单元与 nginx 片段 / `ss -lntp` / `df` / `journalctl` / `sqlite3` 的 `PRAGMA` 与 `index_list` | 运行身份、监听面、TLS、库权限与索引、日志与磁盘 | 未在服务器上跑探针，未做重启演练 |
| R4 | **依赖与合规扫描** | `dotnet list OpenClockTower.slnx package --vulnerable --include-transitive` · `npm audit`（含 `--omit=dev`）· GitHub 内容 API 查 `.github/workflows` | 已知漏洞、是否有 CI | 漏洞库口径随时间变化 |

**R2 的全量结果**：三条 .NET 门禁中的 `dotnet test OpenClockTower.slnx` **全绿**（退出码 0）。首次运行曾因 NuGet 漏洞数据下载超时（`NU1900`）还原失败，重跑即绿——属网络抖动，不是产品缺陷，但记录在案。

**R1 的关键读数**（2026-10-06，目标为部署地址）：

```
curl -sSI http://<部署主机>/clocktower/
  → HTTP/1.1 200 OK · Server: nginx/1.26.3 · 无任何安全响应头
curl -sS -I https://<部署主机>/clocktower/
  → curl: (35) schannel: failed to receive handshake, SSL/TLS connection failed   （没有 TLS 监听）
curl -sSI http://<部署主机>/clocktower      （少一个尾斜杠）
  → 301 · Location: http://<部署主机>/clocktower/                                 （明文跳明文）
curl -sS http://<部署主机>/clocktower/healthz
  → 200 · {"status":"ok","seatCount":7}                                           （匿名可读，无安全头）
```

**R3 的关键读数**：

| 项 | 读数 |
|---|---|
| TLS / 证书 | `ss -lntp` 只有 `:22` `:80` `127.0.0.1:5080`；`nginx -T` 无 `ssl` 生效项；`/etc/letsencrypt` 不存在，`certbot` 未安装 |
| 安全响应头 | `grep -rn add_header /etc/nginx/` **零命中**；应用侧 `Program.cs` 管线只有 `UseDefaultFiles` + `UseStaticFiles` |
| 运行身份 | 单元文件**无 `User=`**；`systemctl show -p User -p Group` 为空；服务进程属主 `root`；`<应用目录>/data` 为 `root:root 755`、`oct.db` 为 `644` |
| 反向代理真实 IP | nginx 已设 `X-Real-IP` / `X-Forwarded-For`，但应用**没有** `UseForwardedHeaders`，日志里也没有客户端 IP |
| 库 | `PRAGMA journal_mode = wal` · `busy_timeout = 0` · `page_size = 4096` · `page_count = 25`（约 100 KB）· `integrity_check = ok` |
| 索引 | `IX_Users_UsernameKey`（唯一）与 `IX_SeatBindings_GameId_AccountId`（唯一）**都存在**——"丢索引"尚未发生 |
| 数据量 | `Users = 2` · `Games = 0` · `Events = 0`（审计时无在开的桌） |
| 磁盘 | 根分区 80 G，已用 58 G（**72%**），无水位告警 |
| 日志 | 应用只写 Console → journald；`journalctl --disk-usage` 约 406 MB；nginx 日志有 logrotate（daily / rotate 10） |
| 重启语义 | `Restart=always` · `RestartSec=3` · `NRestarts=0`；服务反复重启的痕迹在 journal 里可见（当天 21:24–21:26 三次） |
| 备份 | `<应用目录>/data` 下**没有** `backup-*`；`/root` 下只有一份人工快照与一个发布包；`crontab` 无任何本项目的备份任务 |
| 开桌授权 | 单元文件未设 `GameServer__AllowPlayerTables` → 取默认 **true**：任何登录账号都能开桌；`GameServer__AdminUsernames=<运维账号>` |
| CI | 仓库内无 `.github/`；GitHub 内容 API 查 `.github/workflows` → `404`（平台侧也没有流水线） |
| 依赖漏洞 | NuGet 9 个工程（含传递依赖）**无已知漏洞**；`npm audit`（含 dev）**0 项** |

> 术语：本页"部署实例"指当前那台公网机器；机器地址、路径、口令一律不写进仓库（见根 `AGENTS.md`）。

---

## 2 差距清单

严重度口径：`Critical` = 放公网即出事（越权、明文口令、无鉴权入口）· `High` = 会被刷 / 会丢数据 / 体验致命 ·
`Medium` = 该有但可排队 · `Low` = 打磨。里程碑口径见票据 §2：M2 授权越权 · M3 传输与部署安全 ·
M4 滥用与风控 · M5 数据层与运维 · M6 开源与合规。

### A1 账号与口令

#### G-A1-1 三个账号入口零限速、零锁定，且每次尝试都强制服务端跑满一次 PBKDF2｜**Critical**｜M4（反代兜底挂 M3）
- **现状证据**：`src/OpenClockTower.Server/AccountHub.cs` 全文无任何限速 / 锁定 / 失败计数字段；`src/OpenClockTower.Server/Program.cs` 在 `MapHub<AccountHub>` 之前没有任何限流中间件；全仓检索 `RateLimiter` / `RateLimit` / `Throttle` **零命中**。
- **运行时读数**（R1）：对部署实例连打 **12 次不存在账号的失败登录**，12 次全部返回 `invalid_credentials`，**无一次被拒、无延迟增长**；计时各 8 次：不存在账号中位 **55 ms**、真实账号 + 错误口令中位 **55.8 ms**（每次都在服务端跑满 21 万轮 PBKDF2）。
- **影响**：口令爆破不受限（单连接约 18 次/秒，并发连接线性放大；无锁定、无退避）；同时是**CPU 放大攻击**——每次失败尝试都烧一次慢哈希，攻击者用很低成本就能把服务端 CPU 打满，正常玩家进不来。注册与恢复码重置同样零限速（见 G-A1-4）。
- **修法**：`AddRateLimiter` 按"IP + 登录名"做固定窗口 + 失败计数渐进延迟/锁定；反代层加 `limit_req` 作第二道（M3）；限速口径写进决策记录。
- **怎么验证修好了**：一条**会红**的用例——连续失败 N 次后第 N+1 次被拒（429 或专用错误码），且正确口令不受影响、不误伤同 IP 的其他账号。

#### G-A1-2 口令哈希迭代数 21 万，只有当前口径的三分之一；且没有门禁锁住这个参数（合并原 G-A1-3）｜**High**｜M2
- **现状证据**：`src/OpenClockTower.Application/Pbkdf2PasswordHasher.cs` 的 `private const int Iterations = 210_000;`（`git log` 显示该数字自落地起从未变过）；`Verify` 从哈希串里读迭代数后直接 `Rfc2898DeriveBytes.Pbkdf2(...)`——**能验旧串，但全代码没有任何把旧串按新参数重新哈希的路径**，`IPasswordHasher` 也没有 `NeedsUpgrade` 这类第三态，所以"升参数"只做了一半。
- **口径来源**：[OWASP Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)（2026-10-06 抓取）原文：`PBKDF2-HMAC-SHA256: 600,000 iterations (recommended)`；同页 FIPS-140 段落亦写 `600,000 or more`。
- **影响**：GPU/ASIC 离线破解成本比达标参数低约 3 倍；更麻烦的是**参数能被顺手改坏而没有任何测试变红**（改成 1 也全绿）。
- **修法**：抬到当前推荐值（或换 Argon2id）；补 `NeedsUpgrade` 语义——登录成功且参数低于目标时**重新哈希并写回**；把"参数取值 + 依据来源 + 复查节奏"写进决策记录（D-0021 只写了"PBKDF2（随机盐 + 迭代 + 固定时间比较）"，没写目标值）。
- **怎么验证修好了**：一条断言参数不低于目标值的门禁（改小即红）；一条"旧参数串登录成功后库里变成新参数"的集成用例。

#### G-A1-4 恢复码：熵与轮换达标，但同样零尝试限制（合并 A1-9 的测试缺口）｜**Medium**｜M4
- **现状证据**：恢复码 128 位、只存哈希、一次性、用过即轮换、固定时间比较——这几项**已达标**（`AccountService` 路径）。缺口在：`ResetPassword` 没有任何尝试次数限制（与 G-A1-1 同源）；`Users.RecoveryCodeHash` 非空这一事实在库里可读，等于"这个账号从未重置过口令"对外可推断。
- **影响**：恢复码是账号的第二把钥匙，猜它不受限；`RecoveryCodeHash` 的可读性属信息泄露（低危）。
- **修法**：给重置入口单独的成功/失败配额与渐进延迟；日志里只记短指纹（已有）。
- **怎么验证修好了**：连续错误恢复码 N 次后被拒的会红用例。

#### G-A1-5 登录名允许同形字与任意 Unicode、不拒保留名，注册接口是唯一的用户名枚举预言机｜**High**｜M2
- **现状证据**：`src/OpenClockTower.Application/UsernameText.cs` 的 `TryNormalize` 只拒控制字符与空白、限长 2–24，其余任意 Unicode 放行；比较键是 `ToLowerInvariant()`（西里尔 `аdmin` 与拉丁 `admin` 不同键，零宽字符 U+200B 也被放行）；`AccountService.RegisterAsync` 冲突时返回 `username_taken`「这个登录名已经被占用」给匿名调用者；`AdminUsernames` 名单里的 `admin` 之类可以被陌生人先注册走。
- **影响**：冒充与钓鱼面（视觉同形名出现在同桌名单与大厅里）；保留名可被抢注，与运维名单语义相撞；注册接口可被用来批量探测"哪些登录名存在"。
- **修法**：登录名限定字符面（ASCII 字母数字 + `-`/`_`）或做 Unicode 归一化 + 同形字折叠；拒保留名与已配置的运维名单；注册冲突改成中性文案（"这个登录名不可用"）或加注册限速（G-A1-1）后再评估。
- **怎么验证修好了**：同形字与零宽字符被拒、保留名被拒、批量枚举被限速的用例各一条。

#### G-A1-6 账号没有任何注销 / 删号路径｜**Medium**｜M5（隐私说明依赖它）
- **现状证据**：`IAccountStore` 无删除方法、`AccountHub` 无入口、库层无外键级联（见 G-A6-5）；`AdminDirectory` 只用于开桌兜底，不提供删号。登录名**刻意不可改**（与运维名单口径一致，不是缺陷）。
- **影响**：用户要"带走/删除我的数据"时无路可走；开源给陌生人部署时这是合规硬伤（配合 G-A8-4 隐私说明）。
- **修法**：设计注销语义（账号行删除 + 席位绑定解除 + 事件流里玩家名的处理口径），端到端可用。
- **怎么验证修好了**：注销后旧凭据全部失效、席位释放、公开面不再出现该玩家名的集成用例。

#### G-A1-7 口令策略只有"8–128 + 拒控制字符"，没有弱口令拦截｜**Medium**｜M4
- **现状证据**：`src/OpenClockTower.Application/PasswordPolicy.cs` 只做长度与字符面检查，`12345678` 是合法口令；没有强度提示、没有"与登录名/玩家名相似即拒"。
- **影响**：爆破面被用户自己放大（配合零限速更糟）。
- **修法**：最小改动是拒一份常见弱口令表 + 拒与登录名相同/包含；要做强度提示就放在前端（服务端口径仍是唯一判定）。
- **怎么验证修好了**：弱口令表内的口令被拒的用例。

#### G-A1-8 `Verify` 对哈希串里的参数与长度不设上界（深度防御）｜**Low**｜M5
- **现状证据**：`Pbkdf2PasswordHasher.Verify` 把串里的 `iterations` 与 `expected.Length` 直接当参数用，没有上限校验——畸形串可让单次请求烧掉任意 CPU / 内存。
- **影响**：需要先有写库能力才能触发，属深度防御缺失。
- **修法**：验证前夹紧 `iterations` 上限（如 ≤ 2,000,000）与派生键长度上限（如 16–64 字节）。
- **怎么验证修好了**：畸形串（超大迭代数 / 超长键）被拒且不消耗 CPU 的用例。

#### G-A1-9 账号相关测试在结构上盖不到"口令强度、限速/锁定、账号删除"（合并进 G-A1-1 / G-A1-2 / G-A1-6 的验证条件）｜**Medium**｜M2
- **现状证据**：`tests/OpenClockTower.NormativeGates.Tests/CredentialSecurityGateTests.cs` 只覆盖"凭据"，检索 `PasswordHasher` **零命中**；限速、删除、保留名、弱口令、验证失败不写库这五类**都没有反方向用例**。
- **修法**：M2/M4 落地时把上面每条差距的"怎么验证修好了"写成会红的用例，而不是只改代码。

### A2 会话与状态保持

#### G-A2-1 撤销只作用到"下一次进门"：登出 / 改口令后，**已建立的游戏连接与主持权继续有效**｜**Critical**｜M2｜**已修（M2 第一刀，2026-10-06）**
- **现状证据**：`src/OpenClockTower.Server/HubActorResolver.cs` 是"凭据 → 身份"的唯一入口，只调 `ConnectionRegistry.Validate(presented, connectionId)`，**全程不出现 `AccountSessionRegistry`**；账号会话只在三个 Join 入口被读。而 `AccountHub.Logout` 全文只有 `_sessions.Revoke(accountSession)`、`ResetPassword` 只有 `_sessions.RevokeAllForAccount(...)`，**都不触碰 `ConnectionRegistry`**（其 `Revoke` 是私有）。既有 `AccountHostTests` 的登出用例只从 `Resume` 入口验撤销——**没有一条用例把"撤销后的旧凭据"送进 `GameHub`**。
- **运行时读数**（R2，一次性探针）：注册账号 → `JoinTable` 入座拿到连接凭据 →（登出前）凭据有效（`SubmitResponse` 返回业务拒绝 `legality.request_not_current`，说明凭据本身被受理）→ `Logout` 回执 `ok`、`Resume` 回执 `invalid_session`（账号会话确实撤销了）→ **同一条连接、同一凭据再调 `SubmitResponse`，仍然返回 `legality.request_not_current`——凭据依旧有效**。
- **影响**：用户点"登出"或"我怀疑账号泄露，改口令"，**已经进入牌局的那条连接不会被踢**：那个席位可以继续行动，说书人连接可以继续主持，直到刷新页面 / 关标签页 / 断线为止（刷新会走 `Resume`，那时才被拒）。这是"改口令 = 全场踢下线"这条最基本预期的缺口。
- **修法**：让连接级身份带上"它属于哪个账号会话"，撤销账号会话时同批撤销该账号的连接凭据与主持权（`ConnectionRegistry` 按 `AccountId` 撤销）；口径写进 D-0029 的后续版本。
- **怎么验证修好了**：会用红的用例——登出 / 口令重置后，**旧连接**发命令被拒（`HubException`），而不是只在 `Resume` 被拒。
- **修复记录（M2 第一刀，2026-10-06）**：
  - **落地**：连接记录新增**账号会话引用**（`ConnectionCredentialRecord.Session`），由 `SeatJoinCoordinator`（席位）与 `HubJoinFlow`（主持台）在签发时写入；撤销只留一个入口 `src/OpenClockTower.Server/AccountRevocationService.cs`——登出撤那一条会话 + 由它建立的连接，口令重置撤该账号全部会话 + 它们的连接，**同批**调 `ConnectionRegistry.RevokeSession` / `RevokeAccount`（凭据 + 席位 / 说书人路由一起撤，命令与私有推送同时停）。
  - **对"修法"的一处修正**：撤销粒度定为**会话**而不是账号（决策见 D-0030 选项 C）。审计条目当时写"按 `AccountId` 撤销"，那会把同账号在别台设备上的有效登录一起牵连——"我在手机上登出，笔记本上的牌局被踢了"不是修复，是新缺陷。
  - **先红后绿**：`tests/OpenClockTower.Integration.Tests/SessionRevocationHostTests.cs` 五条用例在改动前**全红**，红因一致——撤销后凭据闸仍然放行（`IsRejectedByConnectionGate` 为 false / 期望的 `HubException` 没抛出）；改动后五条全绿。边界与反方向另加 8 条单测（`ConnectionRegistryTests` 6 + `AccountSessionRegistryTests` 2）。
  - **全量门禁**（冻结版）：`dotnet build` + `dotnet test OpenClockTower.slnx` **1320 项全绿**（门禁 26 / 内核 501 / 规则 494 / 集成 299，较审计时 +13）· `dotnet format` 就地通过。
  - **本条仍未含**：客户端侧"登出后主动断开牌局连接 + 明确提示"（服务端已拒，**G-A2-7**）；会话**到期**不追溯已建立的连接（**G-A2-4** 的口径，D-0030 关键口径 5 已写明这是有意为之）。

#### G-A2-2 席位票据是明文落库、永不过期的第二套 bearer 凭据（合并原 G-A4-2 前半）｜**High**｜M2（凭据形态）+ M5（备份里不再有可直接用的凭据）
- **现状证据**：`src/OpenClockTower.Application/SeatTicket.cs` 自陈「票据明文（服务端持久化；重连时重新出示）」；`EfGameCatalog` 用 `JsonSerializer.Deserialize<SeatTicket[]>(row.SeatsJson, …)` 把它从 `Games.SeatsJson` 读出来；生成是 `$"seat-{number}-{Guid.NewGuid():N}"`，比较用 `StringComparison.Ordinal`——**全项目唯一非固定时间比较的凭据路径**，且无过期、无轮换。
- **运行时读数**（R3）：`sqlite3` 读出的表结构里 `Games.SeatsJson TEXT NOT NULL` 就是票据的存放处（架构上明文）。
- **影响**：拿到库或备份的人可以长期冒名入座（不受"8 小时会话过期"约束）；与"开源给陌生人部署"叠加后，备份外流 = 席位永久失守。
- **修法**：票据只存哈希（像账号会话那样），或改成短寿命 + 一次性消费；比较改固定时间。
- **怎么验证修好了**：库里不再有可用明文票据的读数 + 旧票据被拒的用例。

#### G-A2-3 账号会话表没有上界：`List<Entry>` 只增不减，登出路径不清扫｜**Medium**｜M2（口径）+ M4（与限速同批）
- **现状证据**：`src/OpenClockTower.Server/AccountSessionRegistry.cs` 的 `private readonly List<Entry> _sessions = [];`；`SweepExpired()` 只在 `Issue` 与 `TryResolve` 里调用，`Revoke` / `RevokeAllForAccount` **不清扫**；`TryResolve` 是 O(n) 逐条 `FixedTimeEquals`。
- **影响**：单一账号反复登录即可让表持续增长（无从认证的写入，因为 `Issue` 只在登录/注册成功后调用）；`TryResolve` 的线性扫描随表长变慢，配合零限速可放大。
- **修法**：撤销路径也清扫过期项；给"单账号并发会话数"和"全局会话表长度"各定一个上界（超限即撤最旧）。
- **怎么验证修好了**：表长度不随反复登录无界增长的用例（假时钟 + N 次登录）。

#### G-A2-4 会话过期对"正在进行的对局"没有交代：8 小时撞墙、没有"记住我"｜**Medium**｜M2（口径）+ M3（与 Cookie 方案一起定）+ M6（写进面向陌生人的文档）
- **现状证据**：`AccountSessionRegistry.Lifetime = TimeSpan.FromHours(8)`（`static readonly`，不可配、不滑动）；D-0029 关键口径 6 明确"记住我 / 滑动过期 / 跨设备共享登录态不在本决策内，属审计 A2 的待议项"。另一面：**房主账号的口令与恢复码双丢 = 这一桌永久失联**（登录名不可改、无删号、无运维转移入口）。
- **影响**：一局跨 8 小时（含复盘、重开）时会突然掉线；开桌账号丢钥匙后桌就成了孤岛。
- **修法**：明确"绝对 + 滑动"的取舍与"记住我"的产品口径；补一条**运维/客服侧的桌归属转移或删桌路径**（与 G-A4-7 的无界面入口同批）。
- **怎么验证修好了**：滑动过期或有"记住我"的用例 + 一条"桌归属可转移"的端到端路径。

#### G-A2-5 同账号并发登录无上限、无可见性、无"踢其它设备"；**说书人连接会被静默顶掉**｜**High**｜M2（并发登录策略）+ M4（审计日志记下每次顶线）
- **现状证据**：`AccountSessionRegistry.Issue` 无条件 `_sessions.Add(...)`（不撤旧、不计数、不告知）；主持台的连接绑定是"每桌每账号一条"，后来的连接会覆盖先前的绑定。
- **运行时读数**（R2，一次性探针）：同一账号开两条说书人连接 → **后进者可用（`GetStorytellerView` 受理），先进者立即失效**（下一次调用被 `HubException: 连接凭据无效` 拒），且**先进者事先没有收到任何提示**。
- **影响**：知道口令的人可以在主持人正在讲局时把他静默踢出主持台，主持人只会在下一次点击时发现"什么都不能做"；反过来，主持人换设备后旧设备也不会被告知。
- **修法**：定并发登录策略（允许几条、超限撤哪条）、顶线时给被顶的连接一条显式通知（前端提示"你在别处打开了主持台"），并把顶线事件写进账号审计日志。
- **怎么验证修好了**：两条说书人连接的用例——后进者可用、先进者**收到提示**且被拒。

#### G-A2-6 "改名"不撤销任何会话，注释与实现不一致｜**Low**｜M2（与 G-A2-1 同批）
- **现状证据**：`AccountSessionRegistry.RevokeAllForAccount` 的注释写着「口令重置 / 改名兜底」，但 `AccountHub.ChangeDisplayName` 只更新名字与推送，**不撤销任何会话**。
- **修法**：要么实现"改名撤会话"，要么改注释（推荐后者：改名不该踢人，但注释必须与实现一致）。
- **怎么验证修好了**：注释与实现一致（无测试可写，属规范漂移）。

#### G-A2-7 前端"撤销性"承诺缺单测：`web/src/services/accountSession.spec.ts` 不存在｜**Medium**｜M2（与 G-A2-1 的反方向用例同批）
- **现状证据**：`glob web/**/accountSession*` 只返回 `web/src/services/accountSession.ts`，没有对应的 `.spec.ts`（M1 判据里"旧凭据被塞回存储也必须被拒"这条只有服务端集成用例，前端那一半没有单测）。
- **修法**：补前端用例：会话失效时清空持久化、回登录卡、清位置。
- **怎么验证修好了**：`npm run test` 里出现该文件且断言覆盖"失效即清 + 回登录卡"。

#### G-A2-8 时钟只依赖未校准的系统墙钟，没有单调时钟兜底或回拨检测｜**Low**｜M5
- **现状证据**：`src/OpenClockTower.Server/SystemClock.cs` 直接用 `DateTimeOffset.UtcNow`；会话过期判定全押在它上面。
- **影响**：宿主时间被回拨会让已过期会话"复活"；单机单进程下影响有限，属运维口径问题。
- **修法**：部署文档里写明要求宿主开时间同步（`systemd-timesyncd` / `chrony`）；必要时代码里加"过期时刻只增不减"的兜底。
- **怎么验证修好了**：文档里有这条要求；假时钟回拨用例。

#### G-A2-9 `Resume` 每次刷新都用同一个 bearer 凭据换资料，且凭据无轮换｜**Low**（现状）→ M3 落地 Cookie 方案时**重估为 Medium**｜M3
- **现状证据**：`AccountHub.Resume` 是只读、幂等、不重发凭据——设计上是干净的；但明文凭据每次刷新都经浏览器脚本层，且**同一个凭据用满 8 小时**（不轮换），XSS 的窃取窗口等于整个会话寿命。
- **修法**：M3 上 HTTPS + CSP 后，重新评估 D-0029 选项 D（`HttpOnly` Cookie）；若要保留 bearer，考虑刷新即轮换凭据。
- **怎么验证修好了**：M3 的传输面读数 + 前端存储面读数（键名/内容清点）。

### A3 传输与部署

#### G-A3-1 全站明文 HTTP：没有 TLS、没有跳转、没有证书方案（合并原 G-A3-5 的明文链路清点）｜**Critical**｜M3
- **现状证据**：`tools/deploy/templates/clocktower.conf.template` 只有 `listen 80;`；`grep -rn add_header /etc/nginx/` 零命中；`Program.cs` 没有 `UseHttpsRedirection` / `UseHsts`；`tools/deploy-prepare.mjs` 不产任何证书步骤。
- **运行时读数**（R1/R3）：`https://` 握手直接失败（`curl: (35)`）；`ss -lntp` 只有 `:22` `:80` 与 `127.0.0.1:5080`；`/etc/letsencrypt` 不存在、`certbot` 未安装；少一个尾斜杠的请求回 `301` 到**明文**地址（不是 HTTPS 跳转）。
- **明文链路上传的东西（逐条清点）**：账号口令 · 账号会话凭据 · 席位票据 · 连接级凭据 · 恢复码（注册/重置时下发）· 玩家名与登录名 · 整局内容与全部事件流 · 席位注记 · 大厅桌列表 —— **9 项全部明文**（HTTP 与 `ws://` 上都是）。
- **影响**：链路上任何一个中间人（同 Wi-Fi、运营商、代理）都能拿到可直接冒充的身份材料，且口令是明文一次性暴露；这与根 `AGENTS.md`「明文链路上不许传口令、令牌或任何可用于冒充的身份材料」正面冲突。
- **修法**：上证书 + 自动续期（acme.sh / certbot），`listen 443 ssl`，80 端口只做跳转；`ASPNETCORE` 侧信任反代并只对内网监听（现状已是 `127.0.0.1`，保持）。
- **怎么验证修好了**：真机 `curl -I https://…` 返回 200 且带 HSTS；`curl -I http://…` 返回 301/308 到 https；一份"链路上还传什么明文"的清点归零。

#### G-A3-2 六个安全响应头全部缺席（应用与反代都没有）｜**High**｜M3
- **现状证据**：`Program.cs` 管线只有 `UseDefaultFiles` + `UseStaticFiles` + 三个映射，无任何头中间件；`grep -rn add_header /etc/nginx/` 零命中（模板里连注释掉的加固项都没有）。
- **运行时读数**（R1）：页面 / JS / CSS / `/healthz` / 301 / WS 握手**逐条实测**，`Strict-Transport-Security` · `Content-Security-Policy` · `X-Content-Type-Options` · `Referrer-Policy` · `Permissions-Policy` · `X-Frame-Options` **一个都没有**。
- **影响**：无 CSP ⇒ 一旦有注入点，`sessionStorage` 里的会话凭据可被脚本读走（D-0029 已登记的欠账，缓解正落在这里）；无 `X-Content-Type-Options` ⇒ MIME 嗅探；无 `Referrer-Policy` ⇒ 外链泄漏路径；无 `X-Frame-Options`/`frame-ancestors` ⇒ 可被嵌套（点击劫持）。`Server: nginx/1.26.3` 还顺带暴露版本。
- **修法**：反代统一加头（`add_header ... always`），CSP 先以 `default-src 'self'` 起步并处理前端内联样式；同时 `server_tokens off`。
- **怎么验证修好了**：真机 `curl -I` 逐头读数齐全；CSP 生效后页面功能不回归（装置全绿）。

#### G-A3-3 反代真实 IP 未被处理：nginx 传了，宿主没人读，日志里没有客户端 IP｜**High**｜M3
- **现状证据**：nginx 片段设了 `X-Real-IP` 与 `X-Forwarded-For`；但全仓检索 `UseForwardedHeaders` / `RemoteIpAddress` **零命中**（与 A5 的结论一致）。
- **影响**：**所有基于 IP 的限速、封禁、审计都不可能做**（G-A1-1 的前置条件）；出事时无法回答"是谁在打"。
- **修法**：`UseForwardedHeaders`（限定可信代理）+ 日志中间件记录真实 IP；口径写进部署文档（反代必须设这两个头）。
- **怎么验证修好了**：真机日志里能看到真实客户端 IP 的读数；限速按 IP 生效的用例。

#### G-A3-4 没有限流、没有超时口径、没有请求体上限（合并原 G-A5-9 / G-A5-11 的传输侧）｜**High**｜M3
- **现状证据**：`Program.cs` 全文无 `AddRateLimiter` / `ConfigureKestrel` / `RequestSizeLimit`；`AddSignalR()` 无 options（`MaximumReceiveMessageSize` 取默认 32 KB）；Kestrel `MaxRequestBodySize` 取默认 30 MB；nginx 无 `limit_req` / `limit_conn`，且 `proxy_read_timeout 3600s` 让慢连接能挂一小时。
- **影响**：慢连接占满（无 `limit_conn`）、`/negotiate` 可被 30 MB 级请求打、上限值随框架升级静默变化而项目无感知。
- **修法**：反代加 `limit_req` + `limit_conn`；应用显式写出 SignalR 与 Kestrel 的上限（而不是吃默认值）；把上限值写进部署文档与一条断言配置的门禁。
- **怎么验证修好了**：超限请求被拒的真机读数；配置文件里有显式值且有门禁守着。

#### G-A3-6 无 SRI、部署模板零加固项、缓存口径缺｜**Low**｜M3
- **现状证据**：`index.html` 只有 `crossorigin`，**没有 `integrity`**；静态资源响应无 `Cache-Control`（只有 `ETag`/`Last-Modified`）；模板里没有任何加固注释可循。
- **修法**：给打包产物接 SRI（构建期生成 `integrity`）；定静态资源缓存口径（带哈希的资源可以长缓存）。
- **怎么验证修好了**：产物 HTML 里 `integrity` 与 `Cache-Control` 各有一条读数。

### A4 授权与越权

**形态先说清（这部分是好的，别推翻）**：服务端鉴权是**单一凭据链 + 一处集中判定**——账号会话（只存 SHA-256、固定时间比较、8 小时绝对过期、可撤销）→ 席位票据（只定位、不授权）→ **连接级凭据**（`ConnectionRegistry`，唯一身份来源）。`GameHub` 41 个公开方法里 37 个第一步过 `HubActorResolver.Resolve` / `ResolveStoryteller`，失败即 `HubException`、根本不触达 Application；随后 `CommandGatePipeline.CheckIdentity` 按 `ActorKind` 逐命令族判定；桌边界靠连接的 `?gameId=` 与注册表按 `(GameId, SeatId)` 分区，说书人身份靠 `Games.CreatedByAccountId`。
**运行时读数**（R1，对部署实例）：用伪造凭据逐个调 `GetStorytellerView` / `GetReplay` / `SubmitResponse` / `StartNight` / `SetTableLock` / `ReleaseSeatBinding` / `ProposeSetup` / `JoinTable` / `JoinStorytellerWithAccount`，**每一个都返回一致的拒绝**（`HubException: 连接凭据无效：请先用票据加入（D-0012）` 或 `这一桌不存在` / `账号会话无效或已过期`）——未授权入口是关着的。

**逐方法矩阵单独一页**：`docs/security/authorization-matrix.md`（41 个 `GameHub` 方法 + 8 个 `AccountHub` 方法，逐个给"身份材料 / 拦截位置 / 允许身份 / 反方向用例 / 缺口"，并带审计追加的 4 条运行时读数）。**那一页就是 M2 的验收物**，M2 在它上面原地补用例。

#### G-A4-1 幂等回执没有归属：`(GameId, IdempotencyKey)` 是主键，回执不核对是谁的｜**原判 High，运行时推翻 → Low**｜M2
- **现状证据**：`ReceiptEntity` 主键只有 `(GameId, IdempotencyKey)`；`CommandGatePipeline` 命中回执即 `GateDecision.Duplicate(receipt)`；`GameSession.AnnotateIssuedTravellerSeat` 在重复投递时会把 `IssuedSeatTicket` 明文回填进回执。
- **运行时读数**（R2，一次性探针）：说书人先调 `JoinTraveller`（同键）→ `Kind=Accepted`、**有票（39 字符）**；同桌玩家拿**同一个幂等键**再调 → `Kind=Rejected`、`Code=identity.storyteller_only`、**无票**。原因在代码里也看得到：`CommandGatePipeline` **先** `CheckIdentity(envelope)`、**后**才看 `receipt`。
- **结论**：静态分析推断的"玩家可以偷走说书人的席位票据"**不成立**；残留的是设计缝隙——回执本身没有归属字段，今天靠"身份闸在前"这条**顺序**兜着，而这条顺序**没有任何用例锁住**（谁把回执短路挪到身份闸前面，票据就真的漏了）。
- **修法**：`ReceiptEntity` 加"签发者"字段并在回执命中时核对；补一条"身份闸先于回执短路"的会红用例。
- **怎么验证修好了**：上述用例存在且在顺序被改动时变红。

#### G-A4-2 锁桌**不拦**票据入座：`IsLocked` 只在自助入座那条路上判（合并原 G-A4-2 后半）｜**Medium**｜M2
- **现状证据**：`IsLocked` 只在 `JoinBySeatAsync`（自助）判；`JoinSeat` / `JoinSeatWithAccount` 走的 `JoinAsync` 完全不查锁。
- **运行时读数**（R2，一次性探针）：说书人 `SetTableLock(true)` 返回 `True` → 之后用**未消费的席位票据** `JoinSeat`：**受理、入座成功**（自助入座那条路另有既有用例守着被拒）。
- **影响**：说书人点"锁桌"以为关上了门，实际只关了自助那扇；持票（含泄露的旧票，见 G-A2-2）的人照样进。
- **修法**：锁桌语义统一到 `JoinAsync`（或明确"票据不受锁影响"并改文案与文档）。
- **怎么验证修好了**：锁桌后票据入座被拒的会红用例。

#### G-A4-3 连接声明的桌与凭据里的桌没有被核对｜**Medium**｜M2
- **现状证据**：`CredentialValidation.Game` 被解析出来却没人使用，`Actor` 也没有桌字段——桌边界完全靠"签发与解析同源"这一事实成立。
- **影响**：今天不可利用（探针也证实跨桌调用进不去），但这是一条**没有防线**的接缝：将来任何一处把凭据复用/转发，就会直接变成跨桌越权。
- **修法**：把桌标识并进身份（`Actor` 带 `GameId`），在 `HubGameScope` 解析处做一次显式比对。
- **怎么验证修好了**：构造"错桌凭据"的用例被拒。

#### G-A4-4 `?gameId=` 可枚举：未登录即可读到全服桌面与占席情况｜**Medium**｜M2
- **现状证据**：`AccountHub.ListTables(string? accountSession)` 允许 `null`（有意为之，大厅是公开门面），`LobbyTableDto` 含全部 `GameId` 与 `OccupiedSeatNumbers`（逐字段核对**不含票据与席位归属账号**）。
- **运行时读数**（R1）：`ListTables(null)` 在部署实例上成功返回（审计时桌上为空，`tables=0`）。
- **影响**：`GameId` 是进入某桌 Hub 的必需参数，公开枚举把它变成"可定向"；占席情况属社交信息，公开是否合适是产品判断。
- **修法**：若保留公开大厅，就把 `GameId` 换成不透明句柄（或只在登录后下发）；否则把大厅收进登录面。
- **怎么验证修好了**：匿名只能看到桌名与人数、拿不到可用于连接 Hub 的标识。

#### G-A4-5 授权面动作零限速｜**已合并进 G-A1-1**｜M4
- 注册 / 登录 / 开桌 / 入座 / 重复加入全部没有次数约束，证据与读数见 G-A1-1 与 G-A5-2 / G-A5-5 / G-A5-6。

#### G-A4-6 23 个说书人命令只有正面用例，没有"玩家调用被拒"的反方向用例｜**Medium**｜M2｜**已修（M2 第二刀，2026-10-06）**
- **现状证据**：矩阵逐行核对后，含 `PunishExecution` / `PitHagCasualty` / `ResolveDeferredDeath` / `ReportSeatState` / `RebuildRoom`（能直接杀人、改角色）在内的 23 个命令**没有一条会红的用例**锁住"玩家不能调"；默认分支今天是对的，但改坏不会有人发现。
- **修法**：表驱动地把 41 个方法 × 身份矩阵变成用例（玩家凭据遍历说书人命令 → 期望 `identity.storyteller_only`）。
- **怎么验证修好了**：把这 23 条补进测试，且**故意改坏一处**时能看到红。
- **修复记录（M2 第二刀，2026-10-06）**：
  - **落地**：新增 `tests/OpenClockTower.Integration.Tests/AuthorizationSurfaceHostTests.cs` —— 把本页矩阵写成**一张可执行的表**（`Matrix`：45 行，覆盖 `GameHub` 全部客户端可调方法），再用三种身份各扫一遍：**【反向·玩家】**（说书人命令必须被拒，拒绝码逐行写死）· **【反向·说书人】**（玩家命令必须被拒）· **【匿名】**（没加入过的连接出示伪造凭据，每个收凭据的方法都必须在前门被拒）。被允许的身份只要求"**没被**身份闸拒"——这样不必为每个方法构造"能成功"的局面，表不会随玩法腐烂。
  - **比"修法"多做的一处**：再加一条**覆盖门禁**（【覆盖】）——反射枚举 `GameHub` 的公开方法，与表双向比对。缺了它，"23 条补完"只是一次性动作：下次有人加第 46 个 Hub 方法，同样不会有人发现。凭据签发路径的 4 个 Join 方法（不收连接凭据）在表里显式标 `NotDriven` 并**必须写明由哪条既有用例覆盖**，否则同样红。
  - **先红证明（4 处，逐条实际改坏源码后复跑）**：放宽说书人闸（`PunishExecutionCommand` 放开 `Player`）→ **【反向·玩家】** 红，诊断为 `PunishExecution：期望 identity.storyteller_only，实际 legality.seat_unknown`；放宽玩家闸（`NominateCommand` 放开 `Storyteller`）→ **【反向·说书人】** 红（`Nominate：期望 identity.player_only，实际 phase.not_open_day`）；把 `SetTableLock` 的 `ResolveStorytellerActor` 删掉 → **玩家 + 匿名两条同时红**；给 `GameHub` 加一个不表态的方法 → **【覆盖】** 红并点名 `ProbeSurfaceGate`。
  - **顺带纠正**：审计原文记作"`GameHub` 41 个公开方法"，反射点数是 **45 个客户端可调方法 + 1 个框架回调 `OnDisconnectedAsync`**（其中 41 个走四道闸、4 个走 Hub 内显式说书人闸）。矩阵页 §0 已按点数更正。
  - **本条仍未含**：`AccountHub` 的 8 个方法不在该扫描面内（大厅 / 账号入口的身份面由 `AccountHostTests` / `LobbyHostTests` / `SessionRevocationHostTests` 覆盖）；"身份闸先于回执短路"这条**顺序**仍没有用例锁住（**G-A4-1**，Low）；越权只覆盖身份一维——跨桌 `gameId`（**G-A4-3** / **G-A4-4**）与锁桌语义（**G-A4-2**）另行。

#### G-A4-7 锁桌 / 移人服务端有、界面无，且**日志里没有操作者**（审计半边与 G-A5-10 合并）｜**Medium**｜M2
- **现状证据**：`web/src` 里 `SetTableLock` / `ReleaseSeatBinding` **零命中**（没有界面入口，但方法可被直接调用）；日志只写「桌元数据已更新：game=… 锁定=True」，**不写谁下的手**。
- **影响**：能力存在但不可见、不可审计；说书人只能靠开发者工具或第三方脚本用，出事查不到人。
- **修法**：决定是补界面还是删能力（不能两不靠）；任一选择都要把操作者写进日志（配合 G-A5-10）。
- **怎么验证修好了**：界面上有入口且有反方向用例（非说书人点了被拒）**或**方法已删除；日志里有操作者标识。

#### G-A4-8 `ChangeDisplayName` 缺反向用例｜**Low**｜M2
- **现状证据**：改名按 `accountId` 逐桌匹配席位名的逻辑目前正确，但没有用例；一旦有人改成"按席位匹配"就会静默改错**别人的**席位名而测试全绿。（显示名可以重名是 D-0021 的刻意取舍，**不是**待修项。）
- **修法**：补一条"A 改名不影响 B 的席位名"的用例。
- **怎么验证修好了**：该用例存在。

### A5 滥用与风控

**结论**：**现在一层风控都没有**——全仓 `AddRateLimiter` / `UseRateLimiter` / `AddCors` / `UseCors` / `Antiforgery` / `UseForwardedHeaders` / `MaximumReceiveMessageSize` / `MaxRequestBodySize` **零命中**，nginx 样例也没有 `limit_req` / `limit_conn`。有的是形状校验（长度、字符面、枚举白名单、唯一索引）与事后日志——那是"能定位"，不是"能拦"。

#### G-A5-2 注册零限制：匿名可无限批量注册，且一次注册固定烧两次慢哈希｜**Critical**｜M4
- **现状证据**：`AccountService.RegisterAsync` 无频率 / 总量 / IP / 邀请约束；唯一索引只防重名不防量；注册路径在落库**之前**就算完口令与恢复码两个 PBKDF2（撞名注册同样烧两次）；注册即登录，直接换成"可开桌"的门票。
- **运行时代价读数**：单次 PBKDF2-SHA256/210000/32B 在本机 OpenSSL 口径 median **22.3 ms**（单核约 45 次/秒）；部署实例上一次登录尝试往返中位 **55 ms**。批量注册就是把这两条成本乘上攻击者的并发度。
- **影响**：刷库 + 烧 CPU 两件事同时成立，而且**不需要任何凭据**。
- **修法**：注册限速与配额（IP + 全局），必要时加一次性邀请或关掉自助注册（部署开关）。
- **怎么验证修好了**：连续注册 N 次后被拒的会红用例；关掉自助注册的部署开关可用。

#### G-A5-5 开桌无配额、桌数无上限、不回收；心跳逐桌；`ListTables` 匿名可读且 N+1 查询｜**Critical**｜M4（配额）+ M5（回收与容量）
- **现状证据**：`TableCreationPolicy.CanCreate` 只问"登录了没"（`AllowPlayerTables` 默认 `true`，部署实例**没设这个开关**）；`GameRegistry` 注释「首版不做空闲桌回收」；`StepPacerHostedService` 每 200 ms **逐桌** `TickAsync`（桌数一多，心跳自己就是负载）；`AccountHub.ListTables` 匿名可读且每桌两次查询。
- **影响**：一次匿名注册就能反复开桌把内存与 CPU 摊薄（每桌都要装载事件流、起定时器），而"没人玩的桌"永不回收——这是把服务打垮的最短路径。
- **修法**：单账号桌数配额 + 全局桌数上限 + 空闲桌回收/归档；部署实例按需设 `GameServer__AllowPlayerTables=false` 收口到运维名单。
- **怎么验证修好了**：超配额开桌被拒的会红用例；空闲桌被回收的读数；`ListTables` 不再 N+1。

#### G-A5-6 单连接可无限重复加入，每次读全量事件流｜**High**｜M4
- **现状证据**：`SessionQueries.ReconnectBundleAsync` 固定 `ReadEventsAsync(gameId, afterSequence: 0, …)`——每次 `JoinSeat` / `JoinSeatWithAccount` / `JoinTable` 都是**全表扫 + 全量反序列化 + 逐条投影**，而这三个入口**没有调用次数限制**。
- **影响**：一条连接循环调用即可把服务端 CPU 拉满；对局越长，单次成本越高（与事件流增长叠加）。
- **修法**：给入座加频率上限；重连包的构建做缓存/增量（至少别每次 from 0）。
- **怎么验证修好了**：连续加入 N 次后被拒的会红用例 + 一次重连包耗时不随对局长度线性恶化的读数。

#### G-A5-7 连接条数无上限（Kestrel / SignalR / nginx 三处都没设）｜**High**｜M3（反代）+ M4
- **现状证据**：`Program.cs` 无 `ConfigureKestrel`（`MaxConcurrentConnections` 默认不限）；SignalR 默认不限；nginx 无 `limit_conn`，且 `proxy_read_timeout 3600s` 允许慢连接挂一小时。
- **影响**：慢速连接耗尽（slowloris 类）+ 匿名可开的 WS 连接，配合零限速即成廉价 DoS。
- **修法**：三处各定上限（至少反代 `limit_conn` 与 Kestrel 连接上限）。
- **怎么验证修好了**：超限连接被拒的真机读数。

#### G-A5-8 自由文本：长度只覆盖一半，**频率一个都没有**（票据 M4 的措辞需要更正）｜**High**｜M4
- **现状证据**：**有长度上限**的是登录名 24 / 玩家名 24 / 口令 8–128 / 桌名 24 / 席位注记 120 且每席 5 条 / 艺术家提问 200（都有 `TryNormalize` 与单测）；**完全没有长度与频率约束**的是 12 个命令里的 `note` / `reason` 与 `IdempotencyKey`——一路透传到落库与日志，`CommandGatePipeline` 对这些字段零判定。另有两条口径要更正：① 席位注记的"每席 5 条"**只挡新增**，`AnnotationCommandDispatch.Update` 对同文本更新也照样产事件 ⇒ 循环 Update 可写无上限事件行；② `tools/check-bounded-text.mjs` 与 `tools/lib/bounded-text.mjs` **不是文本长度门禁**——它守的是"装置里 Playwright 轮询/守卫式读取必须有界"（防 `innerText` 默认等 30 秒），项目里**没有**任何自由文本长度门禁脚本。
- **影响**：说书人（或被冒用的说书人连接）可以写任意大的文本进事件流与日志；日志行还会被换行注入（见 G-A5-10）。
- **修法**：给 `note` / `reason` / 幂等键定长度上限并在参数层拒绝；给写文本的命令加频率上限；补一条真正的字段长度门禁（脚本或测试）。
- **怎么验证修好了**：超长 `reason` 被拒的会红用例；注记 Update 也计入条数上限的用例；门禁在字段上限被删除时变红。

#### G-A5-10 审计日志：事件齐、**身份与来源缺**，且有客户端可控的无界字符串进日志（合并原 G-A4-7 的审计半边）｜**High**｜M4
- **现状证据**：登录失败只写 `connection={ConnectionId} code={Code}`（**无登录名、无 IP**）；注册 / 重置失败同样无登录名；锁桌 / 改名无操作者；`SeatJoinCoordinator.ReleaseBindingAsync` 既无 game 也无操作者；开桌被 `LobbyService` 与 `AccountHub` **重复记两遍**；全仓 `RemoteIpAddress` / `ForwardedHeaders` 零命中（与 G-A3-3 同源）。
- **秘密核对（这条是好的）**：口令、恢复码、席位票据、连接凭据**均未进日志**（逐处核过 13 处 `AccountHub` 日志与 `AccountService` / `ConnectionRegistry` / `HubActorResolver` / `HubJoinFlow` / `SeatJoinCoordinator`）；指纹是 `Convert.ToHexString(SHA256(v))[..12]` = 12 位 hex（48 bit），够定位、不能反推。
- **放大面**：客户端可控的无界字符串会进日志（`GameSession` 失败路径 `key={Key}`、`SessionCommit.ReplayAsync` 以 Information 级写 `key={Key}`、`GameCommandFactory.Reject` 的「未知的{label}：{raw}」）⇒ 换行注入伪造日志行 + 日志膨胀。
- **影响**："账号安全事件必须有审计日志"这条**只完成一半**：事后无法回答"是谁（哪个登录名 / 哪个 IP）在什么时候做了什么"，而日志本身又可以被灌爆。
- **修法**：日志带 `accountId` / 登录名 / 真实 IP（依赖 G-A3-3）；客户端字符串截断 + 控制字符清洗后再进日志；顶线、登出、改口令、开桌、锁桌统一成结构化事件。
- **怎么验证修好了**：一次真机读数——失败登录日志里能看到登录名与 IP；注入换行的输入在日志里是单行且被截断。

#### 其余 A5 差距的归属
- **G-A5-1（登录失败零限速，Critical）→ G-A1-1**；**G-A5-3（会话表无上限/线性扫描，High）→ G-A2-3**；**G-A5-4（恢复码零尝试限制，Low）→ G-A1-4**；**G-A5-9（超大载荷吃框架默认，Medium）→ G-A3-4**；**G-A5-11（无速率中间件、无反代限流，Medium）→ G-A1-1 / G-A3-4**。
- **已核对、无差距（这条要留着）**：CSRF / CSWSH 不构成身份冒用（全程显式 bearer，无 Cookie、无 ambient 凭据，跨站页面无法冒用身份）；一条连接结构上只能绑一桌一席；命令参数按枚举**名字**而非数字解析；`/healthz` 只回公开配置。

### A6 数据层

**结论**：结构演进撑得住的范围**恰好等于"加一个带默认值的列 / 删一个列"**，靠的是"每版记得往守卫里加一条"而不是机制；备份与删除这两块基本是空的。

#### G-A6-1 启动守卫只补列 / 删列，**索引与约束无人验证**｜**High**｜M5
- **现状证据**：`GameBootstrapHostedService` 的守卫走"表在不在 → 补列 / 删列"，**不检查索引与约束**；`LegacyDatabaseUpgradeTests` 自陈只比"列名 + 类型 + NOT NULL + 主键位"；仓库里没有 `Migrations/`、没有 `__EFMigrationsHistory`、没有 schema 版本号。
- **运行时读数**（R3）：`PRAGMA index_list` 显示 `IX_Users_UsernameKey`（唯一）与 `IX_SeatBindings_GameId_AccountId`（唯一）**当前都在**——"丢索引"尚未发生，但没有任何机制保证它不会发生。
- **影响**：这两条唯一索引是"一号一人 / 一席一人"的**唯一**执行者；改类型、加约束、加表、加"无默认值的 NOT NULL 列"全部**静默不生效**，直到运行期出错。
- **修法**：上正式 migration（见 G-A6-2），或至少把守卫扩成"索引 / 约束 / 列类型"三类都比对，并让 `LegacyDatabaseUpgradeTests` 覆盖索引。
- **怎么验证修好了**：手工删掉一条唯一索引后启动，守卫能报出来并自愈（或有迁移把它建回来）。

#### G-A6-2 上 Migrations 的过渡零方案｜**Medium**｜M5
- **现状证据**：`EnsureCreated` 生成的 `InitialCreate` 不能作用在已有库上；D-0021 的代价一节只写了"提示换新库"。
- **影响**：越晚迁移，已部署实例的过渡越贵（唯一办法是导出重建）。
- **修法**：先做一次"基线迁移"（把当前 schema 固化成第一版 migration，已有库打标为已应用），再往后正常迁移。
- **怎么验证修好了**：一份用真机 schema 造的老库能平滑迁到新版本（沿用 `LegacyDatabaseUpgradeTests` 的做法）。

#### G-A6-3 无单实例兜底，且有不可逆变更 ⇒ 不支持降级｜**Medium**｜M5
- **现状证据**：全仓 `Mutex` / `SingleInstance` / `lockfile` / `FileShare` 零命中；两实例同启时输的那个拿到 `duplicate column name`，被捕获后报成"请换新库"并打断启动；守卫里有 `DROP COLUMN StorytellerTicket` 这类不可逆动作。
- **影响**：误开两个实例会把启动错误伪装成"库坏了"；回滚（G-A7-5）没有退路。
- **修法**：库文件级单实例锁（启动时拿不到就明确报"另一个实例在跑"）；把不可逆变更单独列进发布清单。
- **怎么验证修好了**：同机起第二个实例时得到明确错误而不是"请换新库"。

#### G-A6-4 备份只有四行手工步骤：无脚本、无轮转、与库同盘、**从未演练恢复**｜**High**｜M5
- **现状证据**：`docs/operations/deploy.md` §6 只有 `stop / cp / start` 三步行；`tools/` 下没有任何备份或清理脚本。
- **运行时读数**（R3）：`<应用目录>/data` 下**没有** `backup-*` 文件；`crontab` 里没有任何本项目的备份任务；服务器上只存在一份**人工**复制的库快照（在应用目录之外，属于防误删快照，不是定期备份）。
- **影响**：按"没演练过的备份不算备份"这条口径，**当前没有可用的备份能力**——票据 M5 自己写的验收"备份能真恢复一次（演练读数）"未达成。
- **修法**：备份脚本（`VACUUM INTO` 或停服复制）+ 轮转 + 落在**不同磁盘/对象存储** + 一次真恢复演练；WAL 模式下必须写清能不能热备（见 G-A6-8）。
- **怎么验证修好了**：一次真机演练读数：从备份恢复出一个能起服、能读出一局历史的库。

#### G-A6-5 库层没有任何删除路径：账号注销、桌回收、事件流清理都没有落点｜**High**｜M5
- **现状证据**：`IAccountStore` / `IGameStore` / `IGameCatalog` 一个删除方法都没有；全仓 `HasForeignKey` / `HasOne` / `WithMany` / `OnDelete` 零命中（**无外键、无级联**）；`Events` / `Receipts` 只增不减；`GameRegistry` 注释「首版不做空闲桌回收」；**无任何体积估算**（规模装置只测耗时，不测字节）。
- **影响**：数据只进不出；账号注销（G-A1-6）与隐私说明（G-A8-4）都因此无解；桌只能靠运维手工五表联删，而且要停服。
- **修法**：先定义保留策略（桌多久算废弃、事件流留多久、注销删到什么程度），再落实现；补一条 `N 事件 ≈ X 字节` 的估算读数作为容量依据。
- **怎么验证修好了**：注销路径端到端可用 + 删桌不再需要停服 + 有体积估算。

#### G-A6-6 库文件权限 644：同机任何用户可读走全部口令哈希与整局事件流｜**Medium**｜M5
- **现状证据**：部署步骤用 `chmod -R u=rwX,go=rX <APP_DIR>`，把 `data/oct.db` 落成组/其他可读。
- **运行时读数**（R3）：`<应用目录>/data` 为 `root:root 755`，`oct.db` 为 **644**；`-shm`/`-wal` 同样可读。
- **影响**：同机其它用户（或任何以其它身份运行的进程）可读走口令哈希、恢复码哈希、席位票据与整局事件流。
- **修法**：库文件 `chmod 600` 且属主为服务运行用户；部署步骤里把 `data/` 单列成 `700`。
- **怎么验证修好了**：真机 `stat` 读数为 600/700，且服务仍能正常读写库。

#### G-A6-7 单元模板缺 `User=`（服务以 root 运行）｜**已合并进 G-A7-4**｜M5
- 证据与运行时读数（进程属主 root、`systemctl show -p User` 为空）见 G-A7-4。

#### G-A6-8 无 WAL / busy_timeout 口径：为什么"备份要停服"没有书面依据｜**Medium**｜M5
- **现状证据**：全仓库唯一的 `PRAGMA` 是只读的 `table_info(Games)`；`docs/` 里检索 `WAL` / `journal_mode` **零命中**——部署文档里的"先停服再复制"没有任何依据说明。
- **运行时读数**（R3）：`journal_mode = wal` · **`busy_timeout = 0`**（不等待，直接 `SQLITE_BUSY`）· `page_size = 4096` · `page_count = 25` · `integrity_check = ok`。
- **影响**：单进程下 WAL 是好事，但 `busy_timeout = 0` 意味着任何并发写入（第二个实例、运维 `sqlite3` 写操作）会立刻失败而不是排队；热备份的正确姿势（`VACUUM INTO` / `.backup`）也没写。
- **修法**：连接串里显式设 `busy_timeout`；部署文档写清 WAL 语义与推荐的热备份命令。
- **怎么验证修好了**：文档里有口径；一次热备演练成功。

### A7 运维与可观测（7 条，全部 Medium）

#### G-A7-1 没有 CI：三条 .NET 门禁 + 前端 gate 只在本机人肉跑｜**Medium**｜M5
- **现状证据**：仓库内无 `.github/`（`glob` 与 `git ls-files` 双向确认）；GitHub 内容 API 查 `.github/workflows` → `404`（平台侧也没有流水线）；`AGENTS.local.md` 自认"待办：建立 CI"。
- **影响**：门禁靠自觉；"冻结版本上的三条门禁全绿"这件事没有机器证据，别人 clone 也无法复现门禁口径。
- **修法**：一条流水线跑 `dotnet build/test/format` + `npm ci && npm run gate`（Linux runner 上顺带验证跨平台）。
- **怎么验证修好了**：一条绿的和一条**故意红**（例如临时把迭代数改小触发门禁）的流水线读数。

#### G-A7-2 `/healthz` 是纯常量端点，什么都不反映｜**Medium**｜M5（最小版可挂 M3）
- **现状证据**：`Program.cs` 的 `/healthz` 是 `() => Results.Ok(new { status = "ok", seatCount })`；库挂、事件流损坏、节拍器死掉都返回 200；没有 `/readyz` 之类的就绪探针。
- **影响**：外部监控与反代健康检查形同虚设（"200 但不可用"）。
- **修法**：加真检查（能否打开库 / 能否读到桌装载状态），区分 liveness 与 readiness。
- **怎么验证修好了**：把库文件改名/设只读后读数变红，恢复后变绿。

#### G-A7-3 日志只有 Console → journald：无轮转、无落盘、无上限、无 per-game 检索面｜**Medium**｜M5
- **现状证据**：`appsettings.json` 只配 `LogLevel`，没有文件日志 sink；`journalctl --disk-usage` ≈ 406 MB（含其它服务）；nginx 日志有 logrotate，应用日志没有自己的口径。
- **影响**：日志靠 journald 默认策略兜底；一次刷日志事件（见 G-A5-10 的日志膨胀）会挤占磁盘（根分区已 72%）。
- **修法**：定保留上限（journald `SystemMaxUse` 或独立文件 sink + 轮转）、按 `game` 维度可检索。
- **怎么验证修好了**：配置读数 + 一次真机日志检索演示（按 gameId 捞出整局）。

#### G-A7-4 重启语义对"正在进行的对局"没有文档化；单元模板缺 `User=`（合并原 G-A6-7）｜**Medium**｜M5
- **现状证据**：`tools/deploy/templates/clocktower.service.template` 有 `Restart=always` / `RestartSec=3`，但**没有 `User=`**；`docs/operations/deploy.md` §3 的手写示例里却有 `User=<运行用户>`——脚本产物与文档不一致。
- **运行时读数**（R3）：单元文件无 `User=`/`Group=`；`systemctl show -p User -p Group` 为空；服务进程属主 **root**；`<应用目录>/data` 为 `root:root 755`、`oct.db` **644**；`NRestarts=0`；journal 里能看到当天三次重启。
- **影响**：一个对公网服务的进程以 root 运行，任何 RCE 直接是 root；库文件 644 意味着同机任何用户可读走全部口令哈希与整局事件流；重启会清空全部内存态（会话、连接绑定），玩家看到的是"要重新登录、重新入座"，这条语义没有写进任何文档。
- **修法**：模板加专用运行用户（`User=` / `Group=`），安装步骤里 `chown` 应用目录 + `chmod 600` 库文件；部署文档写清"重启对局的影响"。
- **怎么验证修好了**：真机 `ps -o user=` 不再是 root、库文件权限 600 的读数；文档里有重启语义一节。

#### G-A7-5 回滚零步骤，而且旧版本回不去｜**Medium**｜M5
- **现状证据**：部署文档没有回滚一节；`GameBootstrapHostedService` 的启动守卫里有 `DROP COLUMN StorytellerTicket` 这类**不可逆**变更，`EnsureCreated` 也不建 `__EFMigrationsHistory`。
- **影响**：新版本一旦写坏数据形状，"退回上一个发布包"会撞 `NOT NULL` 直接起不来——回滚不是"换包重启"。
- **修法**：发布包保留上一版 + 库文件快照（与 G-A6-4 的备份策略同批）；把"哪些变更不可逆"写进发布清单。
- **怎么验证修好了**：一次真机回滚演练读数（旧包 + 旧库能起来）。

#### G-A7-6 磁盘水位零监控零告警｜**Medium**｜M5
- **现状证据**：仓库里没有任何监控/告警脚本（`tools/` 下只有装置与部署脚本）；真机根分区 **72%**（80 G 用 58 G）。
- **影响**：SQLite 库、journald、发布包都在同一分区；写满时表现为"服务莫名其妙起不来"。
- **修法**：一条最简水位告警（cron + 阈值 + 通知）与保留策略（发布包、日志、备份轮转）。
- **怎么验证修好了**：阈值触发一次的读数（可临时把阈值调低验证）。

#### G-A7-7 无耗时 / 错误率 / 连接数 / 桌数指标｜**Medium**｜M5
- **现状证据**：无任何 metrics 端点或导出；定位问题只能"改代码 → 部署 → 复现"。
- **影响**：G-A1-1 / G-A5-5 这类滥用只有事后日志（还没有 IP 与身份，见 G-A5-10），没有实时可见性。
- **修法**：最小可用指标（活跃连接数、桌数、登录失败计数、命令拒绝计数），先落日志聚合再谈时序库。
- **怎么验证修好了**：一次压测/滥用演练里能读到计数上升。

### A8 开源与合规就绪

**结论**：许可证、秘密管理、依赖清单三块**合格**；"给别人用"的那一整圈**几乎全空**。

#### G-A8-1 没有 `SECURITY.md`：开源仓库收到漏洞报告没有任何私下渠道｜**High**｜M6
- **现状证据**：仓库根无 `SECURITY.md`，也没有 `.github/`（连"用 issue 报漏洞"的兜底说明都没有），而 README 自称已在一台公网机器上跑起来。
- **影响**：陌生人发现漏洞后只能公开开 issue（等于公开 0day）；对"要放公网"的项目这是硬缺口。
- **修法**：`SECURITY.md`：报告渠道（邮箱或私密通报）、响应时限、支持范围（哪些版本）、以及"请勿公开披露"的口径。
- **怎么验证修好了**：仓库里可读；README 有指向它的链接。

#### G-A8-4 没有隐私说明，而实际在收集身份与整局数据，保留期限没有任何答案｜**High**｜M6（依赖 G-A6-5）
- **现状证据**：实际收集登录名、玩家名、口令哈希、恢复码哈希、席位绑定、整局事件流；仓库里没有任何隐私说明；数据保留期限与删除路径（G-A6-5）都不存在。
- **影响**：给别人部署时，部署者无法回答"你收集了什么、留多久"；配合 G-A1-6（不能注销）更明显。
- **修法**：`PRIVACY.md`（收集什么 / 用途 / 保留多久 / 怎么删）+ 注销路径。
- **怎么验证修好了**：文档可读且与实现一致（逐条对得上代码）。

#### G-A8-6 README 不是上手路径：陌生人跑不起来｜**High**｜M6
- **现状证据**：`dotnet run` 在 `README.md` 与 `docs/` 里**零命中**（只在 `web/AGENTS.md` §2，那份按口径是给代理看的约束文件，不算使用者文档）；没有 `npm install` 步骤、没有配置说明、没有截图；角色数在两处自相矛盾（30 / 25）。
- **影响**：MIT 开源 + "给别的圈子自己部署"的前提是自己跑得起来，现在做不到。
- **修法**：README 写成"从 clone 到浏览器里开一局"的路径（环境要求 → 构建 → 跑起来 → 配置项 → 常见问题），并真找一个陌生环境跑一遍。
- **怎么验证修好了**：一个干净环境从 clone 到跑通的清单被真跑一遍。

#### G-A8-2 无 `CONTRIBUTING.md`｜**Medium**｜M6
- **证据 / 影响 / 修法 / 验证**：无贡献指南；提交信息规范（`type(scope): summary`）与门禁口径只写在 `AGENTS.md`（面向代理的约束入口），外部贡献者读不到；补一份简短贡献指南并指向门禁命令；验证方式=文件可读且命令可复制执行。

#### G-A8-3 无 `CODE_OF_CONDUCT.md`｜**Low**｜M6
- **证据 / 影响 / 修法 / 验证**：无行为准则；公开协作时缺一把尺子；补一份（可用通用模板）；验证方式=文件可读。

#### G-A8-5 无 `CHANGELOG.md`、无任何 tag、版本号两处不一致｜**Medium**｜M6
- **证据 / 影响 / 修法 / 验证**：无变更日志、`git tag` 为空、版本号在 `web/package.json`（0.1.0）与 .NET 默认（1.0.0）之间不一致；部署者无法判断"我跑的是哪版"；补版本口径（单一来源）+ 打首个 tag + 变更日志起步；验证方式=`git tag` 有输出、版本号两处一致。

#### G-A8-7 无配置样例；`.gitignore` 未排除 `.env` / `appsettings.Development.json`；`appsettings.json` 残留已删除的配置项｜**Medium**｜M6
- **证据 / 影响 / 修法 / 验证**：没有 `.env.example` / `appsettings.example.json`，陌生人不知道要配什么；`.gitignore` 未排除 `.env` 与 `appsettings.Development.json`（本机秘密有被提交的风险）；`appsettings.json` 里还留着 D-0027 已删除的 `"GameId": "default"`（绑定器静默忽略，属陈旧配置）；补样例文件 + `.gitignore` 两条排除 + 清掉陈旧键；验证方式=样例存在、`git check-ignore` 命中、配置里没有已删除的键。

#### G-A8-8 `LICENSE` 与 `README` 声明"不包含百科正文内容"，而仓库里有约 300 KB 系统性转录，且无归属说明｜**Medium**｜M6
- **现状证据**：`LICENSE` 与 `README` 都写"不包含钟楼百科的正文内容"；而 `docs/standard/character-rules.md`（约 71 KB）、`character-examples.md`（约 23 KB）、`rulings.md`（约 206 KB）系统性转录百科正文；`docs/standard/sources.md` §4 自己写着"正文不进仓库…MIT 仓库不夹带"。
- **影响**：这是**声明与内容自相矛盾**（是否构成侵权需另行核实，审计不下这个结论）；对 MIT 开源项目，第三方素材的授权口径必须能自证。
- **修法**：二选一——把转录内容移出仓库（改为抓取脚本 + 本地缓存，仓库只留引用与口径），或改掉 LICENSE/README 的声明并补第三方素材的许可与归属说明。
- **怎么验证修好了**：声明与仓库内容一致，且 `sources.md` 的口径能被复现（脚本抓取可重跑）。

#### G-A8-9 前端产物里没有保留任何版权声明｜**Medium**｜M6
- **现状证据**：`web/dist/assets/*.js`（约 309 KB）里检索 `@license` / `copyright` / `/*!` **各 0 命中**；`vite.config.ts` 没有任何 `build` 配置——MIT 依赖要求"保留版权声明"的常见做法（`legalComments`）没有兑现；也没有 `NOTICE` / `THIRD-PARTY` 文件。
- **修法**：构建保留 legal comments 或生成第三方许可清单（`NOTICE` / `THIRD-PARTY.md`）。
- **怎么验证修好了**：产物里有许可声明，或仓库里有第三方许可清单且与依赖一致。

#### G-A8-10 仓库里**已经**存在机器绝对路径与私网地址，而门禁拦不下 Linux 路径｜**Medium**｜M6（顺带修门禁）
- **现状证据**：`git grep -c` 统计（不打印字面值）：`docs/acceptance/batches.md` 4 处、`docs/backlog/done/frontend-path-routing.md` 2 处、`docs/backlog/in-progress/multi-table-and-account-entry.md` 1 处命中"部署机绝对路径 / 私网地址"；`docs/standard/rulings.md` 与 `docs/backlog/done/character-art-hotlink.md` 另有一处私网地址。
- **门禁为什么没拦住**（实测）：`RepositoryGateTests` 的正则只匹配"盘符 + 分隔符"与"UNC 前缀"，用它去匹配 `/opt/...` 这类 Linux 绝对路径**不命中**——所以"绝不提交机器绝对路径"这条规则在 Linux 路径上**没有防线**。
- **影响**：违反根 `AGENTS.md` 的明文规则（上一轮已经因同类问题被需求方指为红线）；泄漏部署者目录结构与内网地址。
- **修法**：把这几处改成 `<APP_DIR>` 一类占位符；门禁正则扩到"Linux 绝对路径 + 私网地址"，并加一条会红的用例。
- **怎么验证修好了**：`git grep` 归零；门禁对样例字符串变红。

#### G-A8-11 同一个包两处版本号矛盾；`.gitattributes` 有指向不存在文件的条目｜**Low**｜M6
- **现状证据**：`Directory.Packages.props` 写 `SQLitePCLRaw.bundle_e_sqlite3` **3.0.5**，而 `OpenClockTower.Server.csproj` 的注释写"升级到 2.1.13"（生效的是 3.0.5）；`.gitattributes` 里 `pnpm-lock.yaml` / `packages.lock.json` 的 `-diff` 条目指向**不存在**的文件（实际入库的是 `web/package-lock.json`）。
- **修法**：注释与 `.gitattributes` 对齐现实。
- **怎么验证修好了**：两处读数一致。

#### 已核对、无差距（A6 / A8）
- **仓库零凭据泄漏**：口令片段、部署机 IP、`OCT_SSH_PASSWORD` 在全部已跟踪文件里 0 命中；`AGENTS.local.md` **从未被任何提交跟踪**；全历史无 `*.db` / `*.pfx` / `*.pem` / `.env` 入库。
- `LICENSE` MIT 正文完整、年份与著作权人齐；`git ls-files` 筛图片 / 字体 / 媒体 **0 命中**（D-0007"仓库永不出现官方图片"真被执行）；角色图确为**运行期热链**且有强制降级；前端无外部字体 / CDN。
- NuGet 集中管理 + npm 锁文件入库；**依赖漏洞扫描 Zero**：`dotnet list package --vulnerable --include-transitive` 9 个工程全部"没有易受攻击的包"，`npm audit`（含 dev）0 项。
- `EfGameStore.CommitAsync` 把事件 + 快照 + 回执放在**同一事务**里；内核侧自由文本（注记 120 且每席 5 条、提问 200、玩家名 24、登录名 24、口令 128、桌名 24）都有界且有单测。
- `LegacyDatabaseUpgradeTests` 用**抄自真机 `sqlite_master`** 的老库跑真宿主真开桌；`docs/operations/deploy.md` 与 `tools/deploy-prepare.mjs` 是达标的占位符部署样板（参数层就拦"前缀与产物不一致"）。

### A9 前端体验一致性

**结论**：四个面的登录态呈现**基本对齐**（M1 之后"刷新掉登录""首页谁都不认识我"已不存在）；残留集中在**边缘角色（未登录游客）**与**错误文案**。深链/刷新行为可预期（只读 GET 实测：带前缀无斜杠 → 301 补斜杠；`<前缀>/play` → 200 SPA 回退；`healthz` → `{"status":"ok","seatCount":7}`）。

#### G-A9-1 未登录游客用邀请码入座后，「账号」面板渲染成空壳｜**Medium**｜M2
- **现状证据**：门条件是 `!connected && accountProfile === null`，而邀请码路径允许 `accountProfile.value?.accountSession ?? null`（游客也能"已连接"）；`AccountPanel` 根节点 `v-if="profile !== null"` ⇒ 游客看到标题「账号」+ 副标题「改名、登出都在这」，内容却是空的。
- **影响**：这是"票据/邀请码"这条老路径与"账号是唯一身份证"（D-0027）之间的接缝没扫干净——用户看到一个说自己能改名登出、却什么都不给的面板。
- **修法**：账号面板在无 profile 时显示"这局你是以邀请码入座的，没有账号"的说明；或把游客路径明确标成待退场。
- **怎么验证修好了**：装置里补一条"游客入座后面板有说明、不是空壳"的断言。

#### G-A9-2 错误提示把协议码 / 枚举词直接渲染给用户（4 处）｜**Medium**｜M6
- **现状证据**：`pushDiagnostic(\`提交未成功：${String(kind)}\`)` → 用户看到「提交未成功：Rejected」；`normalizeOutcome` 把 `rejectionCode` 拼进 message（单测还锁着 `toContain('plan.contract_missing')`）；`failureText` 产出「账号操作未成功（invalid_credentials）：…」。
- **影响**：说人话这条没做到（D-0028 要求面向人的能力交付前自己走一遍）；用户拿到的是内部码。
- **修法**：code → 人类文案的映射表放 `display/`，未知码回退到通用话术并保留诊断详情可展开。
- **怎么验证修好了**：单测断言用户可见文案里不出现 `Rejected` / `_code` 形态的字符串。

#### G-A9-3 手机端：零判据 + 玩家面零断点 + 触摸目标偏小｜**High**｜M6
- **现状证据**：全前端只有 **4 个** `@media`（`ReplayPanel` / `ReplayCircle` / `GrimoireView` ×2 / `StorytellerPanel`），**`PlayerPanel` 与 `PlayerDayPanel` 一个都没有**；`global.css` 的 `button{padding:4px 10px}` 约 31 px 高、`HelpTip` 16×16 px；玩家大厅的 `.seat` 在 `PlayerPanel` 样式块里**没有任何规则**；两个装置都固定 `viewport: 1440×960`，唯一的移动端上下文是角色图热链探针（不判布局）；`vitest.config.ts` 是 `environment:'node'`（无 DOM，结构上做不了布局/键盘用例）。
- **影响**：这个平台的使用场景是"每人一台设备"，手机是主要入口之一；玩家面在窄屏上没有布局判据，**等于没人知道它在手机上什么样**。
- **修法**：先补判据（装置加一个 390×844 + `isMobile`/`hasTouch` 的窄屏档：`scrollWidth ≤ clientWidth + 1`、关键按钮高度 ≥ 44 px），再按红出来的问题改 CSS。
- **怎么验证修好了**：窄屏档在三个面上跑绿；触摸目标尺寸有断言。
- ⚠️ **诚实标注**：本条结论来自 CSS 与模板的**静态阅读**，本审计**没有**任何真机/真手机读数（见 §6）。

#### G-A9-4 无障碍：无 `aria-live`、tablist 半成品、无 `prefers-reduced-motion`、无 `autocomplete`｜**Medium**｜M6
- **现状证据**：状态推送更新没有 `aria-live` 播报；tablist 只做了一半（缺 `aria-controls`/`aria-labelledby` 的完整配对）；没有 `prefers-reduced-motion`（圆环与时间轴有动画）；登录表单没有 `autocomplete="username"` / `"current-password"`。
- **影响**：屏幕阅读器用户与键盘用户拿不到状态变化；密码管理器填不进去。
- **修法**：按上列四处逐个补齐，并把键盘 Tab 顺序写成一条断言。
- **怎么验证修好了**：Tab 顺序断言（登录卡 Tab 6 次应落在 username → password → login）+ `aria-live` 存在性断言。

#### G-A9-5 错误色三处不一致，`--danger` 变量未定义｜**Low**｜M6
- **证据 / 影响 / 修法 / 验证**：三处各写各的红色、`var(--danger)` 没有定义（回退成浏览器默认）；把颜色收进一处变量；验证=加一条样式变量存在性检查或人工走查。

#### G-A9-6 首页缺"正在恢复登录状态"态（与登录卡三态不一致）｜**Low**｜M6
- **证据 / 影响 / 修法 / 验证**：`Resume` 在途时首页先闪"未登录"，与登录卡的三态处理不一致；补一个恢复中态；验证=装置断言"恢复中不闪未登录"。

#### G-A9-7 回放位置有第二 / 第三个地址读写口，绕开 `display/routing.ts`｜**Low**｜M6
- **证据 / 影响 / 修法 / 验证**：回放位置仍挂在井号地址上，且有几处直接读写 `location.hash`（`routing.ts` 之外）；把地址读写收成一个出口；验证=门禁或单测断言 `location.hash` 只在一处出现。

#### G-A9-8 `sessionPersisted` 是死出口：降级提示不能跨刷新重播｜**Low**｜M6
- **证据 / 影响 / 修法 / 验证**：`browserSession` 暴露的 `persistent=false` 没有消费方，隐私模式下用户不会被再次告知；要么消费它、要么删掉这个出口；验证=有消费点或有测试证明不需要。

#### G-A9-9（原编号，已并入 G-A3-2）
- 无安全响应头 + 明文 HTTP 与 `G-A3-1` / `G-A3-2` 同源，**不重复计入**；其探针读数已并入那两条。

**已核对、无差距（A9）**：`sessionStorage` 单出口约定**没被绕过且有门禁**（`CredentialSecurityGateTests` 扫 `web/src` 全部 `.ts` 与 `.vue`）；前端 `console.*` 零命中；无 `v-html` / `innerHTML` / `eval`；**对比度全部过 WCAG AA**（最紧 4.97:1）；交互元素都是真 `<button>`；无焦点陷阱（不含模态）。覆盖度实测：前端 **21 spec / 62 describe / 256 it**。

### A10 代码与架构

**结论**：底子明显好于 A9——全仓 `TODO` / `FIXME` / `HACK` / `XXX` **只有 1 处命中**（还是审计票据在描述自己），`临时/暂时` 21 处逐条读过全是正当叙述；硬约束基本都落成了**会失败的门禁**。

#### G-A10-1 `tools/check-architecture.ps1` 不存在；"表达状态的 bool > 5"这条阈值没有任何门禁｜**Medium**｜M5
- **现状证据**：`tools/*.ps1` 里没有 `check-architecture.ps1`（用户级 `~/.dsh/AGENTS.md` 把它当作本项目的硬阈值来源，实际项目里没有这个脚本）；`bool > 5` 这条口径在门禁里检索不到。
- **影响**：一条写进规范硬约束的阈值靠自觉——而规范的效力来自"会失败的测试"（本项目自己的原则）。
- **修法**：要么把阈值实现成门禁（新测试），要么把这条口径从规范里删掉（不要留无人执行的红线）。
- **怎么验证修好了**：造一个超阈值类能看到红，或规范里不再出现该阈值。

#### G-A10-2 600 行门禁只看 `.cs`：前端 3 个文件超限（1029 / 1002 / 786）｜**Medium**｜M5
- **现状证据**：`SourceFileLengthGateTests` 只扫 `.cs`；`web/src` 里有 3 个文件超过 600 行（最大 1029）。
- **影响**：同一个"职责单一"的口径在前后端两套标准（前端没有门禁）。
- **修法**：把行数门禁扩到 `.vue` / `.ts`（阈值可单独定），或明确写清"前端不适用该阈值"的理由。
- **怎么验证修好了**：门禁覆盖前端文件（或规范里有明确豁免理由）。

#### G-A10-3 `StepMachine.cs` 正好 600 行，零余量（Top5 余量 ≤ 22 行）｜**Medium**｜M5
- **运行时读数**（R2 全量测试的推断 + 行数复核）：门禁口径是 `File.ReadAllLines().Length > 600` 才红；`StepMachine.cs` = **600**（100.0%）、`NightPlanBuilderTests.cs` = 599、`GameHub.cs` = 591、`GameCommandDispatcher.cs` = 583、`DayLedgerFolder.cs` = 578；全仓 **0 个超限**。
- **影响**：下一个碰 `StepMachine.cs` 的人只要加一行就会红——门禁会以"突然挡路"的方式出现，而不是提前给信号。
- **修法**：拆 `StepMachine.cs`（或把阈值与拆法写进票据）。
- **怎么验证修好了**：该文件行数有余量且测试仍绿。

#### G-A10-4 `ContractMirrorGateTests` 的 `MaxLines = 600` 是死常量；镜像文件 `game.ts` 已 684 行｜**Low**｜M5
- **证据 / 影响 / 修法 / 验证**：契约镜像门禁里写了一个从不参与判定的常量，而镜像文件已经超过它——门禁"看起来在管"其实没管；删掉死常量或让它真的生效；验证=改坏即红。

#### G-A10-5 `tools/lib/entrance.mjs` 直读库时**唯一**漏了 `readOnly: true`｜**Medium**｜M5
- **现状证据**：装置夹具里读 SQLite 的连接普遍带 `readOnly: true`，`tools/lib/entrance.mjs` 是唯一漏的一个。
- **影响**：装置在"读"的名义下拿了写权限，一次意外写就会污染真库（与 G-A10-6 同族）。
- **修法**：补上 `readOnly: true`；顺带把"装置只读库"作为一条约定写进验收文档。
- **怎么验证修好了**：该文件带 `readOnly: true`；装置仍全绿。

#### G-A10-6 真机验收装置在真库留数据，靠人工执行 SQL 清理（已实际漏过两次）｜**Medium**｜M5
- **现状证据**：真机装置在部署实例上创建桌与账号，收尾清理是人工 SQL；历史批次里出现过遗留（并已清理）。
- **影响**：一次忘记清理就留下夹具数据；更糟的是它证明"装置可以写生产库"这条边界没有机制。
- **修法**：装置自带清理（`try/finally` 或收尾脚本），并在收尾时打印"创建了什么 / 删了什么"；或让真机装置连测试库而不是生产库。
- **怎么验证修好了**：跑一次真机装置后库内计数不变（读数）。

#### G-A10-7 陈旧禁词 `storytellerTicket` 与写死的夹具账号名 `zt-account`｜**Low**｜M5
- **证据 / 影响 / 修法 / 验证**：D-0027 之后说书人票据概念已退场，禁词表与夹具里还留着旧名；清理并让门禁指向现实；验证=检索零命中。

#### G-A10-8 `AccountHub.ChangeDisplayName` 遍历**全部**桌并逐桌广播（O(桌数) 写放大）｜**Low**｜M5
- **现状证据**：改名时 `foreach (var gameId in _games.GameIds)` 逐桌 `Rename` + `PushSeatNamesChangedAsync`，与"这个账号在哪几桌"无关。
- **影响**：桌数一多，一次改名就是全服广播（与 G-A5-5 的桌数无上限叠加）。
- **修法**：按账号实际绑定的桌推送（席位绑定表里就有）。
- **怎么验证修好了**：改名只触及该账号所在桌的用例/读数。

#### G-A10-9 入口失效指针（窗口内已修）+ `web/AGENTS.md` 只剩 85 字节｜**Medium**｜M6
- **现状证据**：根 `AGENTS.md` 曾指向 `docs/backlog/todo/web-hardening-programme.md`（票据早已移到 `in-progress/`），**本次审计窗口内已修正**；`web/AGENTS.md` 当前 5035 / 5120 字节，余量 **85 字节**。
- **影响**：失效指针会让下一个会话照着不存在的路径找票据；余量 85 字节意味着**下一次改 `web/AGENTS.md` 必然先腾地方**，否则门禁红。
- **修法**：加一条"指令文件里的仓库相对路径必须存在"的门禁（把这次的偶发修复变成机制）；为 `web/AGENTS.md` 先做一次瘦身（把细节移进它链接的文档）。
- **怎么验证修好了**：门禁对样例失效路径变红；`web/AGENTS.md` 有余量。

#### G-A10-10 验收文档漂移（四处）｜**Medium**｜M6
- **现状证据**：① `docs/acceptance/AGENTS.md` 里"最近批次"仍写 E48 / E49（实际已到 E51）；② `docs/acceptance/devices.md` 写"账号装置 12 段"（实际 **14** 段）且漏列两个支持分段的装置；③ `devices.md` §1 的表格被一行空行隔断（第二十行不属于表格）；④ `web/AGENTS.md` 把 `npm run gate` 写成"上面三样串起来"（实际四样，含 lint）。
- **影响**：验收文档是下一个人照着跑的东西；漂移会让人跑错档位或漏段。
- **修法**：把这几处对齐现实；给"文档里的数字"找一个可复核的来源（例如装置自带 `--list-sections` 输出）。
- **怎么验证修好了**：文档数字与装置输出一致。

**已核对、无差距（A10）**：`docs/backlog/README.md` 指针表**零漂移**（72 链接 ↔ 72 票据文件，无缺失、无未登记、无重复）；`review/` 只有 `.gitkeep` 且索引章节为空，两边一致；`artifacts/` 与 `web/dist` 都被 gitignore（`git check-ignore -v` 命中）；控制面/数据面分离**没有反例**（`GameHub` 是纯翻译层、`ProjectionMapper` 是静态类、`NotificationDispatcher` 不持有状态、`ConnectionRegistry` 的状态有唯一所有者且在同一把锁内；`HubGameScope` 是唯一"单例里放可变状态"的形状且有架构理由 + `Forget()` 清理）；依赖是中央版本管理（版本只在 `Directory.Packages.props`），前端 2 个运行时依赖 + 14 个 devDeps 且有 `package-lock.json`。

---

## 3 严重度汇总与里程碑归属

> **修复进度（滚动更新）**：**G-A2-1 已修**（M2 第一刀，2026-10-06，见该条目的修复记录）；
> **G-A4-6 已修**（M2 第二刀，2026-10-06，见该条目的修复记录）。
> 本节条数是**审计当天**的基线读数，**不随修复回填**——"已修"逐条打在 §2 的条目上，
> 免得出现"基线数字随修复漂移、事后谁也说不清当初是多少"。

**活条目 78 条**（另有 8 条跨维度同源条目已合并进正式条目，原编号在条目内注明：G-A1-3 → G-A1-2；G-A3-5 → G-A3-1；G-A4-5 → G-A1-1；G-A5-1 → G-A1-1；G-A5-3 → G-A2-3；G-A5-4 → G-A1-4；G-A5-9 / G-A5-11 → G-A3-4；G-A6-7 → G-A7-4；G-A9-9 → G-A3-2）。

| 严重度 | 条数 | 判定口径 |
|---|---|---|
| **Critical** | **5** | 放公网即出事 |
| **High** | **18** | 会被刷 / 会丢数据 / 体验致命 |
| Medium | 39 | 该有但可排队 |
| Low | 16 | 打磨 |

**按里程碑归属**（每条差距的"归属里程碑"字段与此表一致；M5 在本项目里同时承担"工程底座"——CI、架构门禁、装置卫生，这是审计时按内容归的，票据 §2 的 M5 描述要跟着扩一句）：

| 里程碑 | 条数 | 拿走的 Critical / High |
|---|---|---|
| M2 授权与越权 | 18 | **Critical**：G-A2-1（撤销只到"下一次进门"）· **High**：G-A1-2（哈希参数）· G-A1-5（登录名口径）· G-A2-2（票据明文）· G-A2-5（并发登录顶线） |
| M3 传输与部署安全 | 7 | **Critical**：G-A3-1（全站明文 HTTP）· **High**：G-A3-2（零安全头）· G-A3-3（真实 IP）· G-A3-4（限流与上限）· G-A5-7（连接数无上限） |
| M4 滥用与风控 | 8 | **Critical**：G-A1-1（三个账号入口零限速）· G-A5-2（注册零限制）· G-A5-5（开桌无配额）· **High**：G-A5-6（重复加入读全量）· G-A5-8（自由文本）· G-A5-10（审计日志缺身份） |
| M5 数据层与运维（含工程底座） | 25 | **High**：G-A6-1（守卫不管索引约束）· G-A6-4（备份从未演练）· G-A6-5（无删除路径）· G-A1-6（无注销）· G-A1-8 |
| M6 开源与合规 + 上线前体验收口 | 20 | **High**：G-A8-1（无 SECURITY.md）· G-A8-4（无隐私说明）· G-A8-6（README 不是上手路径）· G-A9-3（手机端零判据） |

**按维度**：

| 维度 | 结论一句话 | 差距数 | C / H / M / L |
|---|---|---|---|
| A1 账号与口令 | 凭据链达标，但入口零限速、参数只有口径的 1/3、登录名口径松 | 8 | 1 / 2 / 4 / 1 |
| A2 会话与状态保持 | 会话本身干净，**撤销没覆盖已建立的连接** | 9 | 1 / 2 / 3 / 3 |
| A3 传输与部署 | **全站明文**、零安全头、无限流上限 | 5 | 1 / 3 / 0 / 1 |
| A4 授权与越权 | 鉴权形态好、未授权入口关得住；缺口在反方向用例与几处接缝 | 7 | 0 / 0 / 5 / 2 |
| A5 滥用与风控 | **一层风控都没有** | 6 | 2 / 4 / 0 / 0 |
| A6 数据层 | schema 演进只撑得住"加列/删列"，备份与删除是空的 | 7 | 0 / 3 / 4 / 0 |
| A7 运维与可观测 | 有人肉流程、没有工程底座 | 7 | 0 / 0 / 7 / 0 |
| A8 开源与合规 | 许可证与秘密管理合格，"给别人用"那一圈几乎全空 | 11 | 0 / 3 / 6 / 2 |
| A9 前端体验一致性 | 四面登录态对齐了；游客路径、错误文案、手机端欠账 | 8 | 0 / 1 / 3 / 4 |
| A10 代码与架构 | 欠账标记几乎为零、门禁基本落地；门禁覆盖面与文档漂移有缺口 | 10 | 0 / 0 / 7 / 3 |

**运行时证据读到的"全绿"面**（这些是已核对、无差距，别再重复查）：`dotnet test OpenClockTower.slnx` 全绿——NormativeGates **26** · Kernel.Tests **501** · Rules.Tests **494** · Integration.Tests **286**（合计 1307 项，0 失败）；`npm run gate` 全绿——typecheck + lint + vitest **21 文件 / 256 项** + 生产构建；NuGet（含传递依赖）**无已知漏洞**；`npm audit`（含 dev）**0 项**；未授权 Hub 调用**逐个被拒**（9 个方法抽样）；用户名存在性**无时序差**（55 vs 55.8 ms）；库 `integrity_check = ok`、两条唯一索引都在。

---

## 4 被运行时证据推翻 / 修正的线索

审计的价值一半在这里：纸面推断被读数改写的地方，逐条记下来，免得下一个人按旧结论施工。

| # | 原线索 | 读数结论 | 结果 |
|---|---|---|---|
| 1 | 幂等键可被同桌玩家复用，连席位票据一起拿走（静态判 **High**） | 玩家用同一个键调 `JoinTraveller` → `identity.storyteller_only`、**无票**；`CommandGatePipeline` 先 `CheckIdentity` 后看回执 | **推翻**，降为 Low（回执无归属仍是设计缝隙，且这条顺序没有用例锁住） |
| 2 | 锁桌可能不拦票据入座（静态判 High） | 锁桌返回 `True` 后，用未消费票据 `JoinSeat` **照样入座成功** | **证实**，定 Medium |
| 3 | M1 已把"撤销性"做完（D-0029 口径 4） | 登出 / 口令重置后，**已建立的连接继续可用**（旧凭据仍被受理） | **缺口暴露**，定为 **Critical**（G-A2-1）；**已于 M2 第一刀修复**（撤销打到已建立的连接上，批次 E53 / 决策 D-0030） |
| 4 | 用户名不存在与口令错误可能有时序差 | 8 + 8 次计时：中性 55 ms / 55.8 ms；`AccountService` 两条拒绝分支都跑同参数假哈希 | **无差距**（这条做得好，写进"已核对"） |
| 5 | 票据里"自由文本长度已有，频率待补" | `note` / `reason` / `IdempotencyKey` **连长度都没有**；且 `tools/check-bounded-text.mjs` **不是**文本长度门禁（它守的是装置读取超时） | **半错**，票据 §M4 措辞要改（G-A5-8） |
| 6 | 依赖有没有已知漏洞（本轮之前未查） | NuGet 9 个工程 + `npm audit` 全部零；且 `dotnet restore` 默认就带 NuGet 审计（首次跑失败正是因为它在下载漏洞库） | **无差距**（并留下一份可复跑的命令） |
| 7 | CI 可能只在本机跑 | 仓库内无 `.github/`；GitHub 内容 API 查 `.github/workflows` → `404` | **证实**（G-A7-1） |
| 8 | "手机端与 7 席满座未验证" | 7 席的**部署形态**已有真机读数（E49/E50/E51，含 25 项）；**7 名真人同局**仍未验证；**手机端仍未验证** | **一半更新**（措辞要写细） |
| 9 | 规范里的 `tools/check-architecture.ps1` 硬阈值 | 仓库里**没有这个脚本**；行数门禁实际在 `SourceFileLengthGateTests`，"bool > 5"没有任何门禁 | **修正**（G-A10-1：要么实现，要么从规范删掉） |
| 10 | `AccountHub` 是"28 KB 级的大类" | `AccountHub.cs` 是 **311 行 / 13,719 B**；28 KB 那个是 `GameHub.cs`（591 行） | **勘误**（不影响结论） |

---

## 5 本次没覆盖的部分（不许当"都查了"）

1. **未做渗透测试**：没有第三方视角的攻击面挖掘，本审计是自己人按清单自查。
2. **未做并发压测**：登录爆破的真实吞吐、连接数上限的实际表现、会话表线性退化的拐点、开桌刷量的内存曲线，**都是估算或代码推断，不是读数**。
3. **未做真机重启演练**：一局进行中 `systemctl restart` 后两端页面的真实表现（G-A7-4 的验收读数）没做。
4. **手机端零运行时判据**：窄屏、触摸目标、键盘 Tab 顺序、`aria-live` 播报、对比度的**运行时**判定一处都没有——A9 的相关结论来自 CSS 与模板的静态阅读（已在条目里标注）。
5. **未验证 7 名真人同局**（只有 7 席部署形态的读数）。
6. **未在部署机上直接测 60 万次迭代的耗时**（避免对生产登录入口加压）：只有 21 万次在部署实例上的往返中位 55 ms + 本机 OpenSSL 口径 22.3 ms，新参数取值需在测试环境实测。
7. **未实测框架上限**：>32 KB 的 Hub 消息、`/negotiate` 的大请求体、超长 `reason` 落库与日志行的真实形状，都没有发过。
8. **未做备份 / 恢复演练**：这是 M5 的验收项，审计只能证明"没有备份能力"。
9. **未核实百科转录的授权条款**：G-A8-8 是"声明与仓库内容自相矛盾"这一可复核事实，**不是侵权结论**。
10. **未逐类统计"表达状态的 bool > 5"**：口径本身没定义（先定义再数才有意义）。
11. ~~**未做 A4 的 41 × 身份表驱动用例**：只做了 9 个方法的抽样探针，其余靠逐行读码 + 既有用例核对。~~ **已补（2026-10-06，M2 第二刀 / G-A4-6）**：`GameHub` 的 45 个客户端可调方法 × 3 种身份（玩家 / 说书人 / 匿名）已表驱动；**仍缺**的是 `AccountHub` 的 8 个方法、跨维度组合（跨桌 `gameId`、凭据重放、锁桌 × 票据）与**并发**下的越权读数。
12. **未确认 `ActorKind.Host` 是否还有后台提交路径**（未跑起来验证）。
13. **文档交叉引用只系统核对了部分**：根 `AGENTS.md`、`backlog/README.md`、`acceptance/{AGENTS.md,devices.md,batches.md}`、`docs/AGENTS.md`、`docs/README.md`；`standard/`、`architecture/`、`decisions/` 的页内链接未系统核对。

---

## 6 关于"不用 JWT"的复核（D-0028 要求的重新论证）

**结论：维持原判断**——继续用**不透明随机令牌 + 服务端可撤销会话表**，不引入 JWT。理由在审计里被重新核对过一遍：

1. **JWT 的卖点是免状态，而本场景买的正是"状态"**：我们要的是登出 / 改口令 / 踢设备**当场生效**（D-0029 口径 4）。自包含令牌签发即不可撤，要么等过期，要么再维护一张吊销表——状态并没有省掉，只是从"会话表"挪到了"黑名单"。
2. **规模不构成理由**：单进程、一个圈子的几张桌，内存表毫无压力；换成 JWT 省下的那点查表开销，换不回多出来的密钥管理与轮换面。
3. **代价已经写在 D-0029**：进程重启全失效（重登即可，可接受）；单实例假设（首版明确不做多实例——**一旦要做，内存会话表必须重新设计**，这一条要在 M5 里显式记着）。
4. **"可撤销"曾只是半张空头承诺，两个前提里已补上一个**：撤销原先只覆盖"下一次进门"（G-A2-1）——**已由 M2 第一刀修掉**（撤销现在打到**已建立**的连接上，见 D-0030 与批次 E53）；剩下 **G-A2-3（会话表没有上界）** 仍待修，"不用 JWT 是因为要可撤销"这句话在它修掉之前仍不算完整。

复核不改决策文本（D-0028 已含该论证），只是把它从"断言"变成"复核过的断言 + 两个前提条件"
（截至 2026-10-06：前提 1 **已满足**，前提 2 **待修**）。

---

## 相关阅读

- **逐方法的授权矩阵（M2 的起点）**：`docs/security/authorization-matrix.md`
- 票据（第 2 步按里程碑改造）：`docs/backlog/in-progress/web-hardening-programme.md`
- 完成口径与"不用 JWT"的论证：`docs/decisions/active.md` D-0028
- 会话持久化的取舍与已知欠账：`docs/decisions/active.md` D-0029
- 账号三层身份与会话承载：`docs/decisions/active.md` D-0021 / D-0025 / D-0027
- 零信任模型（上一轮工作）：`docs/backlog/done/zero-trust-security-model.md`
- 部署形态与占位符说明：`docs/operations/deploy.md` · 批次与真机读数：`docs/acceptance/batches.md`
