namespace OpenClockTower.Server;

/// <summary>
/// 桌的**保留期限**（M5 / G-A5-5 容量半边 · G-A6-5）：没人玩的桌留多久。
/// </summary>
/// <remarks>
/// <para>
/// 这一组数字同时是**隐私说明的答案**（"数据留多久"，G-A8-4）与**容量的表达**（M4 的全局桌数上限
/// 之所以曾经是硬顶，只因为从前没人回收）。默认值取"够用且说得出口"的两档，见下。
/// </para>
/// <para>
/// **默认开着**是有意的：隐私说明里"留多久"若默认答案是"永远"，那这一条就是空话；
/// 而升级到本版的部署可能带着若干天没动的老桌，所以部署文档 §7 把
/// "升级后先 `db-report` 看桌数、`retire-tables` 看会被回收哪些桌"写成必做的一步。
/// </para>
/// <para>判定与动作在 <see cref="TableRetirementPolicy"/> / <see cref="TableRetirementService"/>；口径全文见 D-0036。</para>
/// </remarks>
public sealed class TableRetentionOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "GameServer:TableRetention";

    /// <summary>
    /// 是否启用空闲桌回收，默认启用。
    /// </summary>
    /// <remarks>
    /// 关掉之后**数据只进不出**：全局桌数上限重新变成不可自愈的硬顶，隐私说明里的保留期限也不成立。
    /// 允许关是为了"正在搬库 / 正在取证"这类场合有个闸，不是常规形态。
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// **从未开局**的桌留多久，默认 24 小时（按小时算）。
    /// </summary>
    /// <remarks>
    /// 空桌（没人坐下、没有任何事件）是纯粹的壳：好奇点一下开出来的那张桌占着全局额度与一个装载位。
    /// 一天足够"今天开、明天叫人"的用法，也足够刷桌的人自己撞上额度。
    /// **0 = 立即到期**（演练用：拿它验证回收链路真的会删）。
    /// </remarks>
    public int EmptyTableHours { get; set; } = 24;

    /// <summary>
    /// **开过局**的桌留多久，默认 90 天（按天算）。
    /// </summary>
    /// <remarks>
    /// 开过局的桌里是**整局事件流**——别人的对局记录。阈值给得宽：90 天没人动过的对局，
    /// 再回来接着打的概率远小于"库里一直留着它"的成本。**0 = 立即到期**（演练用）。
    /// </remarks>
    public int PlayedTableDays { get; set; } = 90;

    /// <summary>定时清扫的间隔（分钟），默认 60。</summary>
    /// <remarks>
    /// 清扫本身很便宜（三条聚合查询 + 一张桌一个事务），间隔取的是"多久之后回收"的粒度：
    /// 期限是小时 / 天级的，一小时一轮足够；启动时也会先扫一轮（停机期间到期的桌不必再等一轮）。
    /// </remarks>
    public int SweepIntervalMinutes { get; set; } = 60;
}
