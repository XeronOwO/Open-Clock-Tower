using System.Globalization;
using Microsoft.Extensions.Options;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 桌的**保留判定**（M5 / G-A5-5 容量半边 · G-A6-5）：给一张桌的活跃度读数，说出该留还是该回收。
/// </summary>
/// <remarks>
/// <para>
/// 判定与动作分开：本类**只看事实与阈值**，不碰库、不碰注册表、不碰连接。
/// 这样三个入口（定时清扫、开桌前自愈、运维维护命令）能共用同一套判定，
/// 而其中只有定时清扫那个进程里才有内存注册表——判定若能顺手读内存，维护命令就没法复用它了。
/// </para>
/// <para>
/// "有没有人在用"是**外部传进来的**（<c>occupied</c>）：守着内存连接表的那个进程知道答案，
/// 拿着单实例锁的维护进程则按定义知道"没有服务在跑"。传参而不是在这里查，两种调用方都成立。
/// </para>
/// </remarks>
public sealed class TableRetirementPolicy
{
    private readonly TableRetentionOptions _options;

    /// <summary>构造判定器。</summary>
    public TableRetirementPolicy(IOptions<TableRetentionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <summary>未开局桌的保留期（0 = 立即到期）。</summary>
    public TimeSpan EmptyTableThreshold => TimeSpan.FromHours(_options.EmptyTableHours);

    /// <summary>开过局桌的保留期（0 = 立即到期）。</summary>
    public TimeSpan PlayedTableThreshold => TimeSpan.FromDays(_options.PlayedTableDays);

    /// <summary>判定一张桌。</summary>
    /// <param name="activity">活跃度读数。</param>
    /// <param name="now">这一轮的"现在"（宿主时钟）。</param>
    /// <param name="occupied">这一桌当前是否有在线连接（席位或主持台）。</param>
    /// <remarks>
    /// 顺序有意义：**先看有没有人在用**，再看时间。反过来的话，一张正在对局、而最后一条事件
    /// 恰好很旧的桌会被判成"到期"——"有人在用"是比时长更硬的事实。
    /// </remarks>
    public TableRetirementOutcome Decide(TableActivity activity, DateTimeOffset now, bool occupied)
    {
        ArgumentNullException.ThrowIfNull(activity);

        if (occupied)
        {
            return new TableRetirementOutcome(
                activity.GameId,
                activity.Name,
                TableRetirementVerdict.InUse,
                "这一桌现在有在线连接（席位或主持台）——正在被使用，一律不动",
                null,
                null);
        }

        var lastActivity = activity.LastActivityAt;
        if (lastActivity is null)
        {
            return new TableRetirementOutcome(
                activity.GameId,
                activity.Name,
                TableRetirementVerdict.Undecidable,
                "没有任何可判定的时间依据（没有建桌时刻，也没有事件与席位绑定）——没有依据就不删",
                null,
                null);
        }

        // 时钟回拨（NTP 校时、手工改表）时 now 可能早于最后活跃：按"刚刚活跃"处理。
        // 宁可少回收一张，也不因为一次对时而删掉一整局。
        var idle = now > lastActivity.Value ? now - lastActivity.Value : TimeSpan.Zero;
        var threshold = activity.HasStarted ? PlayedTableThreshold : EmptyTableThreshold;
        var kind = activity.HasStarted ? "已开局" : "未开局";

        return idle >= threshold
            ? new TableRetirementOutcome(
                activity.GameId,
                activity.Name,
                TableRetirementVerdict.Retire,
                $"{kind}桌空闲 {Describe(idle)}，已到保留期 {Describe(threshold)}（{activity.EventCount} 条事件）",
                idle,
                null)
            : new TableRetirementOutcome(
                activity.GameId,
                activity.Name,
                TableRetirementVerdict.WithinRetention,
                $"{kind}桌空闲 {Describe(idle)}，未到保留期 {Describe(threshold)}",
                idle,
                null);
    }

    /// <summary>人话时长（报告里直接打印；阈值与空闲时长用同一把尺子写出来才好比）。</summary>
    private static string Describe(TimeSpan span) => span switch
    {
        { TotalDays: >= 1 } => $"{span.Days} 天 {span.Hours} 小时",
        { TotalHours: >= 1 } => $"{span.Hours} 小时 {span.Minutes} 分",
        { TotalMinutes: >= 1 } => $"{span.Minutes} 分 {span.Seconds} 秒",
        _ => $"{span.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} 秒",
    };
}
