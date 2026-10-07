# 席位邀请码的凭据形态：只存哈希 · 有有效期 · 可轮换

- Status: **Done**（2026-10-08，批次 E63）
- Priority: **High**（审计 G-A2-2）
- Depends on: D-0037（入座必须登录 / 邀请制桌）· M5 第二刀（结构版本化与迁移，v4 的前置）

## 这一票是什么

审计 G-A2-2 的原话：**席位票据是明文落库、永不过期的第二套 bearer 凭据**。
邀请码当时就是席位票据本身——`seat-2-<GUID>` 明文写进 `Games.SeatsJson`、与席位同寿、不可作废，
比较还是全项目唯一一处非固定时间的 `string.Equals(…, Ordinal)`。拿到库或备份的人可以长期冒名入座，
不受"8 小时会话过期"的约束。

D-0037 只收窄了**用它的"人"**（必须登录、只出现在邀请制桌与旅行者路径），并把凭据形态明确留给下一条。

## 做了什么

1. **凭据与名单分家**：`GameSetup.Seats` 变成 `IReadOnlyList<SeatId>`（`SeatTicket` 类型退场），
   `Games.SeatsJson` 存 `[1,2,3]`；邀请码搬进自己的表 **`SeatInvitations`**（一席一行，
   主键 `(GameId, Seat)`），只存 **SHA-256 哈希 + 到期时刻**。
2. **签发与核验各只有一处**（`SeatInvitationService`）：256 位密码学随机（与账号会话同一套 `SecretToken`）、
   固定时间比较、逐条比完不提前退出、日志只写短指纹、**默认 24 小时**有效（可配，`0` = 立即过期）、
   **覆盖即轮换**（旧码当场失效）。
3. **签发入口只有一个**：说书人在主持台对某个席位点一下（`IssueSeatInvitation`）。
   席位面板新增"席位邀请码"一块（填席位号 → 签发 → 当场显示），旅行者加入成功后**自动补一次签发**。
   旅行者加入命令因此**不再签发凭据**（`CommandResultDto.IssuedSeatTicket` 删除）：
   命令回执会被重投回放，凭据不该走那条路。
4. **结构 v4（不可逆）**：建表 + 把老库席位列里的 `ticket` 抹掉——那一步就是"旧的明文邀请码全部作废"。
5. **不做一次性消费**（显式选择）：席位认领本身就是一次性的（绑定落库之后这一席归那个账号，先到先得），
   再叠"用过即焚"只会多一个失败模式。

## 证据

- 决策：`docs/decisions/active.md` **D-0038**（含选项取舍、10 条口径、代价）。
- 审计：`docs/security/web-hardening-audit.md` **G-A2-2** 的修复记录（本条**从 High 清零**）。
- 授权面：`docs/security/authorization-matrix.md` 的 `JoinByInviteCode` 行已改口径，
  新增 `IssueSeatInvitation` 行（本桌说书人；反方向由表驱动扫描覆盖）。
- 用例：`tests/OpenClockTower.Integration.Tests/SeatInvitationHostTests.cs` 5 条（库文件里没有明文 ·
  持码者进得来 · 轮换后旧码被拒 · 过期被拒 · 跨桌被拒）+
  `LegacyDatabaseUpgradeTests` 的老库 v4 迁移 1 条（明文被抹 · 旧码被拒 · 新码照进）。
- 装置：批次 **E63**（主装置取证档 310 项全过；`verify-table-access` 38 项含"库里没有明文"与"轮换"两条新读数；
  `verify-zero-trust` 取码改成让说书人签发）。
- 部署口径：`docs/operations/deploy.md` §5.1（有效期 / 轮换 / "签完必须当场转交"）与 §6.6（v4 不可逆、旧码失效）。

## 残余（本票未做，另行处理）

1. **装置侧的命名债**：`openTableAndHost` 返回的 `seatTickets`（以及各装置里的 `seatTicket`）
   现在装的是**签发出来的邀请码**——名字里的"票据"已经不存在了。改名要动 20 多个装置文件，
   且改完必须**逐台跑一遍**才敢说没破；本轮刻意没做（改名 + 全装置扫描 = 一个独立的小工作项）。
2. **`verify-replay-scale` 在本轮扫描里红**：`ReportSeatState` 撞上"写文本每窗口 120 次"的动作限速。
   与本票无关（没碰限速与那条命令），是装置的用量与阈值交互——需要装置侧放宽或分段。
3. 需要**已经在跑的宿主**的四台装置（`verify-transport-hardening` / `verify-abuse-guard` /
   `verify-retention-and-erasure` / `verify-live-open-table`）本轮未跑。
4. 被盗码者**先到先得**这条边界有意保留（见 D-0038 口径 4）。
