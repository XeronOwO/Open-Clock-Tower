using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 结构比对的**决策表**（M5 / G-A6-1）：哪一种差异自愈、哪一种拦启动、哪一种只是记一行。
/// </summary>
/// <remarks>
/// 纯计算，不碰数据库——比对的输入是两个手工搭出来的 <see cref="DatabaseSchema"/>。
/// 真库上的行为（自愈真的把索引建回来了没有）在 <see cref="SchemaVersioningHostTests"/> 里判；
/// 这里判的是**分档本身**，尤其是"多一列到底算不算错"这种容易拍脑袋定的地方。
/// </remarks>
public sealed class SchemaComparerTests
{
    private static readonly DatabaseSchema.Column GameId =
        new("GameId", "TEXT", NotNull: true, HasDefault: false, PrimaryKeyOrdinal: 1);

    private static readonly DatabaseSchema.Column Name =
        new("Name", "TEXT", NotNull: true, HasDefault: false, PrimaryKeyOrdinal: 0);

    /// <summary>一条唯一索引（替身：真库里的那两条是"一号一人 / 一席一人"的执行者）。</summary>
    private static readonly DatabaseSchema.Index UniqueName =
        new("IX_Games_Name", ["Name"], IsUnique: true);

    /// <summary>同形 ⇒ 一处偏差都不报（否则启动日志会被假差异淹没，真问题就看不见了）。</summary>
    [Fact]
    public void Compare_OnIdenticalSchemas_ReportsNothing()
    {
        Assert.Empty(SchemaComparer.Compare(Table(GameId, Name), Table(GameId, Name)));
    }

    /// <summary>列序不参与：SQLite 按列名取值，补列顺序不影响任何语义。</summary>
    [Fact]
    public void Compare_IgnoresColumnOrder()
    {
        Assert.Empty(SchemaComparer.Compare(Table(GameId, Name), Table(Name, GameId)));
    }

    /// <summary>缺唯一索引 → **可自愈**，而且给出的语句必须真的能把它建回来（含 UNIQUE）。</summary>
    [Fact]
    public void Compare_MissingUniqueIndex_IsRepairableWithACreateStatement()
    {
        var deviation = Assert.Single(SchemaComparer.Compare(TableWithIndex(), Table(GameId, Name)));

        Assert.Equal(SchemaDeviationKind.MissingIndex, deviation.Kind);
        Assert.True(deviation.IsRepairable);
        Assert.False(deviation.IsFatal);
        Assert.Contains(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Games_Name\"",
            deviation.RepairSql,
            StringComparison.Ordinal);
        Assert.Contains("ON \"Games\" (\"Name\")", deviation.RepairSql, StringComparison.Ordinal);
    }

    /// <summary>同名索引但形状不对（这里：唯一性没了）→ 可自愈，且必须先删旧的再建。</summary>
    [Fact]
    public void Compare_IndexShapeMismatch_IsRepairedByDropAndRecreate()
    {
        var wrong = new DatabaseSchema([
            new DatabaseSchema.Table(
                "Games",
                [GameId, Name],
                [new DatabaseSchema.Index("IX_Games_Name", ["Name"], IsUnique: false)],
                ["GameId"]),
        ]);

        var deviation = Assert.Single(SchemaComparer.Compare(TableWithIndex(), wrong));

        Assert.Equal(SchemaDeviationKind.IndexShapeMismatch, deviation.Kind);
        Assert.True(deviation.IsRepairable);
        Assert.Contains("DROP INDEX IF EXISTS", deviation.RepairSql, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX", deviation.RepairSql, StringComparison.Ordinal);
    }

    /// <summary>缺列 → **致命且自愈不了**：凭空补一列会把数据悄悄丢在旧列里（改名就是这种形态）。</summary>
    [Fact]
    public void Compare_MissingColumn_IsFatalAndNotRepairable()
    {
        var deviation = Assert.Single(SchemaComparer.Compare(Table(GameId, Name), Table(GameId)));

        Assert.Equal(SchemaDeviationKind.MissingColumn, deviation.Kind);
        Assert.Equal("Games.Name", deviation.Target);
        Assert.False(deviation.IsRepairable);
        Assert.True(deviation.IsFatal);
    }

    /// <summary>声明类型不符 → 致命（SQLite 是动态类型，但亲和性会悄悄转换值）。</summary>
    [Fact]
    public void Compare_ColumnTypeMismatch_IsFatal()
    {
        var wrong = Table(GameId, Name with { DeclaredType = "INTEGER" });

        var deviation = Assert.Single(SchemaComparer.Compare(Table(GameId, Name), wrong));

        Assert.Equal(SchemaDeviationKind.ColumnTypeMismatch, deviation.Kind);
        Assert.True(deviation.IsFatal);
    }

    /// <summary>NOT NULL 不符（两个方向）→ 都是致命：一个允许写空，一个拒绝写空。</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Compare_NullabilityMismatch_IsFatal(bool expectedNotNull, bool actualNotNull)
    {
        var expected = Table(GameId, Name with { NotNull = expectedNotNull });
        var actual = Table(GameId, Name with { NotNull = actualNotNull });

        var deviation = Assert.Single(SchemaComparer.Compare(expected, actual));

        Assert.Equal(SchemaDeviationKind.ColumnNullabilityMismatch, deviation.Kind);
        Assert.True(deviation.IsFatal);
    }

    /// <summary>主键不符（这里是次序反了）→ 致命：主键决定行的身份，换一个身份就不是同一份数据了。</summary>
    [Fact]
    public void Compare_PrimaryKeyOrderMismatch_IsFatal()
    {
        var expected = new DatabaseSchema([new DatabaseSchema.Table("Events", [GameId, Name], [], ["GameId", "Name"])]);
        var actual = new DatabaseSchema([new DatabaseSchema.Table("Events", [GameId, Name], [], ["Name", "GameId"])]);

        var deviation = Assert.Single(SchemaComparer.Compare(expected, actual));

        Assert.Equal(SchemaDeviationKind.PrimaryKeyMismatch, deviation.Kind);
        Assert.True(deviation.IsFatal);
    }

    /// <summary>多一列（可为空）→ **不算错**：程序回滚到旧版时就是这副样子，EF 只按列名取值。</summary>
    [Fact]
    public void Compare_ExtraNullableColumn_IsOnlyNoted()
    {
        var retired = new DatabaseSchema.Column("Retired", "TEXT", NotNull: false, HasDefault: false, PrimaryKeyOrdinal: 0);

        var deviation = Assert.Single(SchemaComparer.Compare(Table(GameId, Name), Table(GameId, Name, retired)));

        Assert.Equal(SchemaDeviationKind.UnexpectedColumn, deviation.Kind);
        Assert.False(deviation.IsFatal);
    }

    /// <summary>
    /// 多一列而且它是 <c>NOT NULL</c> 且没有默认值 → **致命**。
    /// </summary>
    /// <remarks>
    /// 这条判据有真实来历（E49 真机读数）：说书人票据时代的 <c>Games.StorytellerTicket</c> 正是这个形态，
    /// 留着会让**开新桌的 INSERT 被 SQLite 当场拒掉**，而界面上只表现为"开桌失败"。
    /// "多一列不算错"那条宽容规则必须给它让路。
    /// </remarks>
    [Fact]
    public void Compare_ExtraNotNullColumnWithoutDefault_IsFatal()
    {
        var ticket = new DatabaseSchema.Column("StorytellerTicket", "TEXT", NotNull: true, HasDefault: false, PrimaryKeyOrdinal: 0);

        var deviation = Assert.Single(SchemaComparer.Compare(Table(GameId, Name), Table(GameId, Name, ticket)));

        Assert.Equal(SchemaDeviationKind.UnexpectedColumn, deviation.Kind);
        Assert.True(deviation.IsFatal);
        Assert.Contains("INSERT", deviation.Detail, StringComparison.Ordinal);
    }

    /// <summary>多出来的表只记一行：不属于本版的遗留物，删不删是人的决定，不是启动时该做的事。</summary>
    /// <remarks>
    /// 这张表的索引**不再逐个报**：表本身都已经不在模型里了，再报一遍它的索引只是噪音。
    /// </remarks>
    [Fact]
    public void Compare_ExtraTable_IsOnlyNoted()
    {
        var actual = new DatabaseSchema([
            .. Table(GameId, Name).Tables,
            new DatabaseSchema.Table(
                "Legacy",
                [GameId],
                [new DatabaseSchema.Index("IX_Legacy_GameId", ["GameId"], IsUnique: false)],
                ["GameId"]),
        ]);

        var deviation = Assert.Single(SchemaComparer.Compare(Table(GameId, Name), actual));

        Assert.Equal(SchemaDeviationKind.UnexpectedTable, deviation.Kind);
        Assert.False(deviation.IsFatal);
    }

    /// <summary>已在模型的表上多一条索引 → 同样只记一行。</summary>
    [Fact]
    public void Compare_ExtraIndexOnAKnownTable_IsOnlyNoted()
    {
        var actual = new DatabaseSchema([
            new DatabaseSchema.Table(
                "Games",
                [GameId, Name],
                [new DatabaseSchema.Index("IX_Games_GameId_Name", ["GameId", "Name"], IsUnique: false)],
                ["GameId"]),
        ]);

        var deviation = Assert.Single(SchemaComparer.Compare(Table(GameId, Name), actual));

        Assert.Equal(SchemaDeviationKind.UnexpectedIndex, deviation.Kind);
        Assert.False(deviation.IsFatal);
    }

    /// <summary>模型里有、库里整张表都没有 → 致命（迁移漏了建表）。</summary>
    [Fact]
    public void Compare_MissingTable_IsFatal()
    {
        var deviation = Assert.Single(SchemaComparer.Compare(Table(GameId, Name), new DatabaseSchema([])));

        Assert.Equal(SchemaDeviationKind.MissingTable, deviation.Kind);
        Assert.True(deviation.IsFatal);
    }

    private static DatabaseSchema Table(params DatabaseSchema.Column[] columns) =>
        new([new DatabaseSchema.Table("Games", columns, [], ["GameId"])]);

    private static DatabaseSchema TableWithIndex() =>
        new([new DatabaseSchema.Table("Games", [GameId, Name], [UniqueName], ["GameId"])]);
}
