namespace OpenClockTower.Kernel;

/// <summary>当天一次处决的账目：被处决的席位与来源分类（R-0050 起每天至多两次）。</summary>
/// <remarks>
/// 与 <see cref="ExecutedEvent"/> 对应：处决 ≠ 死亡；死亡结果由配套的座位状态事件记录。
/// 常规处决每天一次；屠夫窗口用掉后可以发生第二次（R-0050 第 2 条）；处罚处决按 R-0020 占用当天上限并收口白天。
/// </remarks>
public sealed record DayExecution
{
    /// <summary>被处决的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>处决来源分类（常规 / 洗脑师处罚 / 畸形秀演员处罚）。</summary>
    public required ExecutionKind Kind { get; init; }
}
