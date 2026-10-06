namespace OpenClockTower.Server;

/// <summary>宿主配置：节奏配额 + 数据库位置 + 席位数 + 自助开桌开关 + 运维名单。</summary>
public sealed class GameServerOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "GameServer";

    /// <summary>
    /// 建议席位数：**只是给界面用的默认值**，不是"宿主自己开的那一桌"。
    /// </summary>
    /// <remarks>
    /// 默认桌已随 D-0027 退场：宿主不再创建任何桌，第一桌由人在界面上开出来。
    /// 这个值仍由 <c>/healthz</c> 下发，供开桌表单预填与面板渲染兜底。
    /// </remarks>
    public int SeatCount { get; set; } = 5;

    /// <summary>SQLite 数据库路径（相对内容根）。</summary>
    public string DatabasePath { get; set; } = "openclocktower.db";

    /// <summary>
    /// SQLite 连接口径（M5 / G-A6-8）：日志模式与同步级别写死在
    /// <see cref="SqliteConnectionPragmas"/>，这里只有可配的"等锁等多久"。
    /// </summary>
    public SqliteOptions Sqlite { get; set; } = new();

    /// <summary>每个槽位的最短配额（秒），默认 10（D-0013）。</summary>
    public double SlotQuotaSeconds { get; set; } = 10;

    /// <summary>节拍器心跳间隔（毫秒）。</summary>
    public int PacerIntervalMilliseconds { get; set; } = 200;

    /// <summary>
    /// **运维身份**的登录名清单（D-0026：运维身份，不是开桌前置）。
    /// </summary>
    /// <remarks>
    /// 刻意放在配置而不进库：它是部署者的名单，不是玩家数据；也避免为它改库结构。
    /// 语义只到这里——关桌 / 清场这类**部署级**动作将来由它授权。
    /// **它不是"说书人"**：说书人是这一局的主持人，由**开桌账号**认定（D-0027），任何登录玩家开一桌就得到它。
    /// 当前唯一用途是 <see cref="AllowPlayerTables"/> 关掉后的开桌兜底。**留空 = 没有运维身份**。
    /// </remarks>
    public string[] AdminUsernames { get; set; } = [];

    /// <summary>
    /// 是否放开**玩家自助开桌**（D-0026，默认放开）。
    /// </summary>
    /// <remarks>
    /// 默认放开：说书人是"玩这一局的角色"而不是系统权限，谁都能开一桌自己主持——小圈子自用就该是这样。
    /// 公开部署怕被刷桌时配 <c>GameServer__AllowPlayerTables=false</c> 收口，
    /// 此时退回"只有 <see cref="AdminUsernames"/> 里的运维身份能开"。
    /// </remarks>
    public bool AllowPlayerTables { get; set; } = true;

    /// <summary>
    /// 是否允许**自助注册**（M4 / G-A5-2，默认允许）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 公开部署被批量注册刷库时配 <c>GameServer__AllowSelfRegistration=false</c> 关掉：
    /// 注册入口给"本服当前不开放注册"的中性拒绝，**既有账号的登录不受影响**，
    /// 而且拒绝发生在慢哈希之前（一次注册要烧两次 PBKDF2）。
    /// </para>
    /// <para>
    /// **关掉 = 再也开不出新账号**：首版没有邀请码、也没有运维建号入口（策略对象会打告警说明这一点）。
    /// 它适合"人就这些、先把门焊死"的部署；要招新人就把它开回来。
    /// </para>
    /// </remarks>
    public bool AllowSelfRegistration { get; set; } = true;

    /// <summary>
    /// **可信反向代理**的地址或网段清单（M3 / G-A3-3）：<c>X-Forwarded-For</c> / <c>X-Forwarded-Proto</c>
    /// 只从这些来源采信。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 留空 = 只信回环，也就是本项目的标准形态（nginx 与宿主同机，见 <c>docs/operations/deploy.md</c>）。
    /// 反代在别的机器或容器里时，必须在这里写出它的地址（<c>10.0.0.5</c>）或网段（<c>172.18.0.0/16</c>），
    /// **不写就不认**——否则任何人都能靠伪造一个头把自己伪装成别人。
    /// </para>
    /// <para>
    /// 写错（拼错的 IP / 非法网段）会让宿主启动失败，这是有意的：真实 IP 一失效，
    /// 按 IP 的限速就退化成"所有请求同一个桶"，那种故障在运行期几乎看不出来。
    /// </para>
    /// </remarks>
    public string[] TrustedProxies { get; set; } = [];
}
