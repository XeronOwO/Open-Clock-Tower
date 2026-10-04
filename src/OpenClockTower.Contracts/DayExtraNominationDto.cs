namespace OpenClockTower.Contracts;

/// <summary>当天打开的额外提名窗口（屠夫；R-0050）。</summary>
/// <remarks>
/// 窗口只在当天首次处决事实产出后打开；窗口开着时，只有授予席位本人能发起一次额外提名
/// （不占当日提名次数、可提名当天已被提名过的玩家）。口径见 <c>docs/standard/rulings.md</c> R-0050。
/// </remarks>
public sealed record DayExtraNominationDto
{
    /// <summary>窗口授予的席位（屠夫）：只能由本人发起额外提名。</summary>
    public required int Seat { get; init; }

    /// <summary>Open（窗口开着）/ Used（已被用掉，不再受理第二次）。</summary>
    public required string Status { get; init; }
}
