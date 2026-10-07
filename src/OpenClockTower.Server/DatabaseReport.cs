using System.Globalization;

namespace OpenClockTower.Server;

/// <summary>
/// 一次**只读体检**的读数（M5 / G-A6-4）：备份能不能用、库里有什么，都从这一个对象读。
/// </summary>
/// <remarks>
/// 维护命令（<c>db-report</c>）把它打给人看；备份命令（<c>backup</c>）用同一次体检验证刚写出来的备份。
/// 两处共用同一个读数，就不会出现"备份报成功、恢复时报坏"这种两头不一致的情况。
/// </remarks>
/// <param name="Path">被检查的文件。</param>
/// <param name="SizeBytes">文件字节数。</param>
/// <param name="Sha256">整文件 SHA-256（小写十六进制）——异地里核对"传过去的那份是不是这一份"。</param>
/// <param name="Integrity">SQLite <c>integrity_check</c> 的结论（<c>ok</c> 才是好）。</param>
/// <param name="JournalMode">日志模式（库文件里的持久属性）。</param>
/// <param name="SchemaVersion">库文件里的**结构版本**（<c>user_version</c>；0 = 迁移时代之前建的库）。</param>
/// <param name="SupportedSchemaVersion">本程序支持到哪一版（比 <paramref name="SchemaVersion"/> 小就是"库比程序新"）。</param>
/// <param name="PageSize">页大小（字节）。</param>
/// <param name="PageCount">页数。</param>
/// <param name="FreelistCount">空闲页数（不占内容、但占文件）。</param>
/// <param name="Tables">各表行数。</param>
/// <param name="Games">逐局读数。</param>
/// <param name="EventPayloadBytes">
/// 事件载荷的总字节数（M5 / G-A6-5：容量依据）。审计的原话是"库层没有任何体积估算"，
/// 于是"再放半年会不会撑爆磁盘"只能靠猜。量的是**载荷字节**（<c>LENGTH(CAST(Payload AS BLOB))</c>，
/// 不是字符数：中文载荷一个字三字节，按字符数量会低估三倍）；它不含页开销与索引，
/// 所以它是一局的**下界**——同一次读数里的库文件总字节与它的差额就是开销。
/// </param>
public sealed record DatabaseReport(
    string Path,
    long SizeBytes,
    string Sha256,
    string Integrity,
    string JournalMode,
    int SchemaVersion,
    int SupportedSchemaVersion,
    int PageSize,
    int PageCount,
    int FreelistCount,
    DatabaseReport.TableCounts Tables,
    IReadOnlyList<DatabaseReport.GameLine> Games,
    long EventPayloadBytes)
{
    /// <summary>各表的行数。</summary>
    public sealed record TableCounts(int Games, int Events, int Users, int Snapshots, int Receipts, int SeatBindings);

    /// <summary>一局的读数：桌名、席位数、事件条数、最后序号与事件载荷字节数。</summary>
    public sealed record GameLine(string GameId, string Name, int SeatCount, int Events, long LastSequence, long PayloadBytes);

    /// <summary>平均每条事件的载荷字节数（没有事件时为 0）。</summary>
    public double BytesPerEvent => Tables.Events == 0 ? 0 : (double)EventPayloadBytes / Tables.Events;

    /// <summary>把读数写成给人看的多行文本（维护命令与批次记录共用同一种表述）。</summary>
    public string Describe()
    {
        var heaviest = Games.OrderByDescending(game => game.PayloadBytes).FirstOrDefault();
        var lines = new List<string>
        {
            $"文件：{Path}（{SizeBytes.ToString("N0", CultureInfo.InvariantCulture)} 字节 · SHA-256 {Sha256}）",
            $"体检：完整性={Integrity} · 日志模式={JournalMode} · 页大小={PageSize} · 页数={PageCount} · 空闲页={FreelistCount}",
            $"结构：版本={SchemaVersion} · 本程序支持到={SupportedSchemaVersion}{SchemaVersionNote()}",
            $"内容：桌={Tables.Games} · 事件={Tables.Events} · 账号={Tables.Users} · 快照={Tables.Snapshots}"
            + $" · 回执={Tables.Receipts} · 席位绑定={Tables.SeatBindings}",
            VolumeLine(heaviest),
        };

        foreach (var game in Games)
        {
            lines.Add(
                $"某一局：标识={game.GameId} · 桌名={game.Name} · 席位={game.SeatCount}"
                + $" · 事件={game.Events} · 最后序号={game.LastSequence}"
                + $" · 载荷={game.PayloadBytes.ToString("N0", CultureInfo.InvariantCulture)} 字节");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// 体积那一行：总载荷、平均每条、最重的一局，以及载荷占库文件的比例。
    /// </summary>
    /// <remarks>
    /// 占比是这一行里最有用的数：它把"库里到底装的是什么"说清楚了——
    /// 占比很小意味着库文件被页开销与空闲页占着（<c>VACUUM</c> 能收），
    /// 占比很大意味着该考虑保留期了。
    /// </remarks>
    private string VolumeLine(GameLine? heaviest)
    {
        var payload = EventPayloadBytes.ToString("N0", CultureInfo.InvariantCulture);
        var share = SizeBytes == 0 ? "—" : $"{(double)EventPayloadBytes / SizeBytes:P1}";
        var line = $"体积：事件载荷={payload} 字节（占库 {share}）"
                   + $" · 平均每事件 {BytesPerEvent.ToString("0.#", CultureInfo.InvariantCulture)} 字节";
        return heaviest is null
            ? line
            : line + $" · 最重一局={heaviest.GameId}（{heaviest.Events} 条 / "
              + $"{heaviest.PayloadBytes.ToString("N0", CultureInfo.InvariantCulture)} 字节）";
    }

    /// <summary>
    /// 版本关系的一句话结论（只在不一致时出现）。
    /// </summary>
    /// <remarks>
    /// 两个方向都要说：**库比程序新** = 这个库被更新版的程序改过，换回旧程序之前必须先想清楚
    /// （迁移只进不退，部署文档 §9.3）；**库比程序旧** = 起一次服务就会自动补上，不用人管。
    /// 前者是回滚现场的唯一预警，所以它出现在体检读数里而不是只出现在启动日志里——
    /// 体检正是"动手之前先看一眼"的那个动作。
    /// </remarks>
    private string SchemaVersionNote() => SchemaVersion > SupportedSchemaVersion
        ? " ⚠ 比本程序新：迁移只进不退，回滚程序前先看部署文档 §9.3"
        : SchemaVersion < SupportedSchemaVersion
            ? " （启动一次即自动补齐迁移）"
            : string.Empty;
}
