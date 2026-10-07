namespace OpenClockTower.Server;

/// <summary>
/// SQLite 连接串口径：**库路径 → 连接串**只有这一处事实。
/// </summary>
/// <remarks>
/// 连接串不只是"怎么连"——它还是**连接池的分池键**：同样的库，连接串文本差一个字符就是两个池。
/// 于是任何"按库清池"（<c>SqliteConnection.ClearPool</c>）的调用方都必须与这里逐字一致，
/// 否则它清的是一个空池、真正的句柄还握着（Windows 上表现为文件删不掉）。
/// 单独抽出来，是为了让"宿主怎么连"与"谁按这个库清池"用的是同一个字符串，
/// 而不是两边各抄一份字面量——抄一遍就等于再埋一次"改了产品忘了改用例"。
/// </remarks>
public static class SqliteConnectionStrings
{
    /// <summary>服务宿主用的连接串（连接池复用）。</summary>
    /// <param name="databasePath">库文件路径（已解析成绝对路径）。</param>
    public static string ForPath(string databasePath) => $"Data Source={databasePath}";
}
