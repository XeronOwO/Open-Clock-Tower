namespace OpenClockTower.Server;

/// <summary>会话表行：席位名单、归属与大厅元数据，重启后仍然有效（D-0011 硬约束 2 的前提）。</summary>
/// <remarks>
/// <para>
/// 多桌（D-0024）之后本表每个在册的桌一行，主键仍是游戏标识。
/// <see cref="Name"/> / <see cref="IsInviteOnly"/> / <see cref="CreatedByAccountId"/> 是后加的列：老库靠 v1 基线迁移
/// 补列（<c>SchemaMigrationCatalog</c>），不会因为升级而读不出数据。
/// </para>
/// <para>
/// <c>IsInviteOnly</c> 的原名是 <c>IsLocked</c>：v3 迁移把它**原地改名**（D-0037 的正名）。
/// 旧程序读不了改名后的库（它找的是 <c>IsLocked</c>），回滚方式见部署文档 §9.3。
/// </para>
/// <para>
/// <c>StorytellerTicket</c> 随 D-0027 退场：本类不再映射它，老库里的那一列由同一条迁移**删掉**
/// ——它是 <c>NOT NULL</c> 且没有默认值，留着会让开新桌的写入被 SQLite 拒掉（详见该迁移的说明）。
/// </para>
/// </remarks>
public sealed class GameSetupEntity
{
    /// <summary>游戏标识（主键）。</summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>席位名单 JSON（**只有席位号** `[1,2,3]`；D-0038：邀请码只存哈希，不在这里）。</summary>
    public string SeatsJson { get; set; } = "[]";

    /// <summary>开桌账号（这一桌归谁）；升级前的老桌为 null = 没有房主。</summary>
    public int? CreatedByAccountId { get; set; }

    /// <summary>桌名（大厅标题；未命名为空串）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>是否邀请制（不接受自助入座，要凭邀请码；D-0037）。原列名 <c>IsLocked</c>。</summary>
    public bool IsInviteOnly { get; set; }

    /// <summary>
    /// 建桌时刻（结构 v2 起；M5 / G-A6-5 用它算"这一桌空了多久"）。
    /// </summary>
    /// <remarks>
    /// 老库（v2 之前）没有这一列，由迁移**按首条事件回填**；从未开局的桌因此可能仍是 null——
    /// 那时"空了多久"没有依据，回收一律不碰它（见 <c>TableRetirementPolicy</c>）。
    /// 只有 <c>SaveAsync</c> 的**插入**路径会写它：更新桌名 / 锁定状态不该刷新"建桌时刻"，
    /// 否则一桌只要被改一次名就永远不会到期。
    /// </remarks>
    public DateTimeOffset? CreatedAt { get; set; }
}
