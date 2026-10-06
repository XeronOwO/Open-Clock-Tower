namespace OpenClockTower.Server;

/// <summary>
/// 一处结构偏差的**类别**（M5 / G-A6-1）：用来分辨"能自愈的"与"必须有人管的"。
/// </summary>
/// <remarks>
/// 分档的依据是**风险**，不是好看：索引是纯增量的元数据（丢了不影响数据，重建不需要动表），
/// 所以缺索引自愈；表与列的形状错了要么装不下数据、要么装错了数据，只能报出来让人决定。
/// </remarks>
public enum SchemaDeviationKind
{
    /// <summary>模型里有这张表，库里没有。</summary>
    MissingTable,

    /// <summary>模型里有这一列，库里没有。</summary>
    MissingColumn,

    /// <summary>模型里有这个索引，库里没有（**可自愈**）。</summary>
    MissingIndex,

    /// <summary>同名索引的列或唯一性与模型不符（**可自愈**：索引里没有数据，重建是安全的）。</summary>
    IndexShapeMismatch,

    /// <summary>列的声明类型与模型不符（SQLite 是动态类型，但声明类型决定类型亲和性）。</summary>
    ColumnTypeMismatch,

    /// <summary>列的 NOT NULL 与模型不符。</summary>
    ColumnNullabilityMismatch,

    /// <summary>主键的列或次序与模型不符。</summary>
    PrimaryKeyMismatch,

    /// <summary>库里的表模型里没有（上一版留下的、或被手工建出来的）——**不算错**，只记一行。</summary>
    UnexpectedTable,

    /// <summary>库里的列模型里没有——**不算错**：这是"程序回滚到旧版"时的正常形态。</summary>
    UnexpectedColumn,

    /// <summary>库里的索引模型里没有——同样不算错，只记一行。</summary>
    UnexpectedIndex,
}
