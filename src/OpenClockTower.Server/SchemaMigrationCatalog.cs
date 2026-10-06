namespace OpenClockTower.Server;

/// <summary>
/// 本版认识的**全部结构演进**（M5 / G-A6-2）：版本从 1 起连续，动作是守卫式的。
/// </summary>
/// <remarks>
/// <para>
/// 这里是库结构的**唯一出口**：建表 / 补列 / 建索引的语句只许写在 <see cref="BaselineStatements"/> 这一类里，
/// 别处（启动引导、实体、Hub）一条 DDL 都不许有——门禁 <c>SchemaVersioningGateTests</c> 扫源码守着这条。
/// 在此之前，DDL 散落在"启动守卫"里，每加一列就往那个 <c>foreach</c> 里补一行，
/// 索引与约束则**从来没有人问过**（审计 G-A6-1 的原话："丢索引尚未发生，但没有任何机制保证它不会发生"）。
/// </para>
/// <para>
/// <b>为什么第 1 条不是"打标为已应用"</b>：老库（<c>EnsureCreated</c> 时代建的、没有版本号）升上来时，
/// 直接把它标成"v1 已应用"是一种**声称**——声称它与新建的库同形，却没有任何东西证明过。
/// 这里改成真的跑一遍 v1：建表语句带 <c>IF NOT EXISTS</c>、补列语句先看列在不在，
/// 于是空库被建齐、老库被补齐，两条路径走同一段代码，跑完由 <see cref="SchemaGuard"/> 逐项核对。
/// </para>
/// <para>
/// <b>为什么不再用 <c>EnsureCreated</c></b>：它建完库不写版本号（于是每次启动都要"猜"库是什么形态），
/// 而且它建出来的形状随 EF 版本走——同一个程序在不同 EF 版本上会给新装用户建出不同的库。
/// 结构是我们要长期负责的东西，于是把它固化在这里的 SQL 里；
/// 与 EF 模型的一致性由 <see cref="SchemaComparer"/> 每次启动核对（对不上就拒绝启动）。
/// </para>
/// </remarks>
public static class SchemaMigrationCatalog
{
    /// <summary>说书人票据时代的列（D-0027 起不再映射；老库里那一列由 v1 清掉）。</summary>
    public const string RetiredTicketColumn = "StorytellerTicket";

    /// <summary>
    /// v1 · 基线：六张表的显式 DDL + 老库补列 + 清掉退场凭据列。
    /// </summary>
    /// <remarks>
    /// 版本号 1 的含义是"结构与 EF 模型的当前形态一致"，不是"跑过一次建表"：
    /// 与旧版升上来的库相比，它多做了 "Games 补三列 + 删 StorytellerTicket"，
    /// 与全新装的库相比它什么都不用补——两种情况跑完都得到同一个形状。
    /// <para>
    /// 声明位置在 <see cref="All"/> **之前**是必须的：静态字段按声明顺序初始化，
    /// 写在后面的话 <see cref="All"/> 初始化时它还是 null（实测：`CS8601 可能的 null 引用赋值`）。
    /// </para>
    /// </remarks>
    private static readonly SchemaMigration Baseline = new(
        Version: 1,
        Description: "基线：六张表的显式 DDL（Events / Games / Receipts / SeatBindings / Snapshots / Users）"
                     + " + 老库补列（Games.Name / IsLocked / CreatedByAccountId）+ 清掉退场凭据列 Games.StorytellerTicket",
        // 删列不可逆：跑过之后再换回旧程序，旧程序读不了这个库（部署文档 §9.3 的"哪些变更回不去"）。
        IsIrreversible: true,
        Apply: ApplyBaselineAsync);

    /// <summary>本版认识的迁移，**按版本升序**。</summary>
    public static IReadOnlyList<SchemaMigration> All { get; } = [Baseline];

    /// <summary>本版支持到哪一版（库的版本比它大 = 程序被回滚过，拒绝启动）。</summary>
    public static int LatestVersion => All[^1].Version;

    /// <summary>库里已应用到 <paramref name="appliedVersion"/> 时，还欠哪些迁移（按版本升序）。</summary>
    public static IReadOnlyList<SchemaMigration> Pending(int appliedVersion) =>
        [.. All.Where(migration => migration.Version > appliedVersion).OrderBy(migration => migration.Version)];

    /// <summary>v1 的动作：先建齐（空库），再补齐（老库），最后清掉退场列。</summary>
    private static async Task ApplyBaselineAsync(SchemaMigrationContext context, CancellationToken cancellationToken)
    {
        foreach (var statement in BaselineStatements)
        {
            await context.ExecuteAsync(statement, cancellationToken);
        }

        // 补列必须先看现状：SQLite 没有 `ADD COLUMN IF NOT EXISTS`，
        // 而重复加列会以 `duplicate column name` 直接失败（两实例同启时当年的症状正是它）。
        var schema = await context.ReadSchemaAsync(cancellationToken);
        foreach (var (column, statement) in LegacyGameColumns)
        {
            if (!schema.HasColumn("Games", column))
            {
                await context.ExecuteAsync(statement, cancellationToken);
            }
        }

        // 退场列（D-0027）：留着不只是"没用"——老库那一列是 **NOT NULL 且没有默认值**，
        // 于是**开新桌的 INSERT 会被它当场拒掉**（实测 `NOT NULL constraint failed: Games.StorytellerTicket`），
        // 表现成最难查的那种半截升级："原来那一桌读得出，新桌开不了"。
        // 删掉它还有第二个收益：把退场的凭据从磁盘上抹掉。
        if (schema.HasColumn("Games", RetiredTicketColumn))
        {
            await context.ExecuteAsync(DropRetiredTicketColumnSql, cancellationToken);
        }
    }

    /// <summary>
    /// 六张表的显式 DDL（形态与 EF 模型一致，逐个字段由 <see cref="SchemaComparer"/> 在启动时核对）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>CREATE TABLE IF NOT EXISTS</c> 一条都不带 <c>DEFAULT</c>：EF 的建库路径也不写默认值，
    /// 两边保持同一种写法，形状比对才只剩"真的不同"这一种差异。
    /// （老库里 <c>Name</c> / <c>IsLocked</c> 那两个默认值来自上一版的 <c>ADD COLUMN</c>——
    /// <c>NOT NULL</c> 在 SQLite 上必须带默认值才被接受；那是**补列**与**建表**两条路径的合法差异，
    /// 已在 <see cref="DatabaseSchema.Column.HasDefault"/> 上注明不参与比对。）
    /// </para>
    /// <para>
    /// 索引刻意显式建（<c>origin='c'</c>）：这两条唯一索引是"一号一人 / 一席一人"的**唯一**执行者，
    /// 建成约束自带的自动索引（<c>sqlite_autoindex_*</c>）就没有名字，丢没丢也没人说得清。
    /// </para>
    /// </remarks>
    private static readonly string[] BaselineStatements =
    [
        """
        CREATE TABLE IF NOT EXISTS "Events" (
            "GameId" TEXT NOT NULL,
            "Sequence" INTEGER NOT NULL,
            "Type" TEXT NOT NULL,
            "Payload" TEXT NOT NULL,
            "RecordedAt" TEXT NOT NULL,
            CONSTRAINT "PK_Events" PRIMARY KEY ("GameId", "Sequence")
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS "Games" (
            "GameId" TEXT NOT NULL CONSTRAINT "PK_Games" PRIMARY KEY,
            "SeatsJson" TEXT NOT NULL,
            "Name" TEXT NOT NULL,
            "IsLocked" INTEGER NOT NULL,
            "CreatedByAccountId" INTEGER NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS "Receipts" (
            "GameId" TEXT NOT NULL,
            "IdempotencyKey" TEXT NOT NULL,
            "FirstSequence" INTEGER NOT NULL,
            "LastSequence" INTEGER NOT NULL,
            CONSTRAINT "PK_Receipts" PRIMARY KEY ("GameId", "IdempotencyKey")
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS "SeatBindings" (
            "GameId" TEXT NOT NULL,
            "Seat" INTEGER NOT NULL,
            "AccountId" INTEGER NOT NULL,
            "BoundAt" TEXT NOT NULL,
            CONSTRAINT "PK_SeatBindings" PRIMARY KEY ("GameId", "Seat")
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS "Snapshots" (
            "GameId" TEXT NOT NULL CONSTRAINT "PK_Snapshots" PRIMARY KEY,
            "Sequence" INTEGER NOT NULL,
            "MachineJson" TEXT NULL,
            "RecordedAt" TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS "Users" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY AUTOINCREMENT,
            "UsernameKey" TEXT NOT NULL,
            "Username" TEXT NOT NULL,
            "DisplayName" TEXT NOT NULL,
            "PasswordHash" TEXT NOT NULL,
            "RecoveryCodeHash" TEXT NULL
        );
        """,
        """
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_SeatBindings_GameId_AccountId"
            ON "SeatBindings" ("GameId", "AccountId");
        """,
        """
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_UsernameKey" ON "Users" ("UsernameKey");
        """,
    ];

    /// <summary>
    /// 老库（<c>EnsureCreated</c> 时代，D-0024 / D-0027 前后陆续加的列）要补的三列。
    /// </summary>
    /// <remarks>
    /// 归属（D-0027）补成 <c>NULL</c> = 没有房主，谁都进不去它的主持台——
    /// 这是如实反映"升级前那一桌本来就没有开桌账号"，不做任何猜测性回填。
    /// </remarks>
    private static readonly (string Column, string Statement)[] LegacyGameColumns =
    [
        ("Name", "ALTER TABLE Games ADD COLUMN Name TEXT NOT NULL DEFAULT '';"),
        ("IsLocked", "ALTER TABLE Games ADD COLUMN IsLocked INTEGER NOT NULL DEFAULT 0;"),
        ("CreatedByAccountId", "ALTER TABLE Games ADD COLUMN CreatedByAccountId INTEGER NULL;"),
    ];

    /// <summary>清掉退场列的语句（SQLite 的原地 <c>DROP COLUMN</c>：不碰别列的数据）。</summary>
    private static readonly string DropRetiredTicketColumnSql =
        $"ALTER TABLE Games DROP COLUMN {RetiredTicketColumn};";
}
