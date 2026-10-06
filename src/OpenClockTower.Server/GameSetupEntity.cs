namespace OpenClockTower.Server;

/// <summary>会话表行：席位票据、归属与大厅元数据，重启后仍然有效（D-0011 硬约束 2 的前提）。</summary>
/// <remarks>
/// <para>
/// 多桌（D-0024）之后本表每个在册的桌一行，主键仍是游戏标识。
/// <see cref="Name"/> / <see cref="IsLocked"/> / <see cref="CreatedByAccountId"/> 是后加的列：老库靠 v1 基线迁移
/// 补列（<c>SchemaMigrationCatalog</c>），不会因为升级而读不出数据。
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

    /// <summary>席位票据 JSON。</summary>
    public string SeatsJson { get; set; } = "[]";

    /// <summary>开桌账号（这一桌归谁）；升级前的老桌为 null = 没有房主。</summary>
    public int? CreatedByAccountId { get; set; }

    /// <summary>桌名（大厅标题；未命名为空串）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>是否锁定（不再接受新入座）。</summary>
    public bool IsLocked { get; set; }
}
