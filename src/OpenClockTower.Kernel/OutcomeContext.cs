namespace OpenClockTower.Kernel;

/// <summary>
/// 一次胜负求值的输入：当前账 + 本局席位名单 + 本批事件 + 角色事实端口（<see cref="OutcomeEvaluator"/>）。
/// </summary>
/// <remarks>
/// 全部是只读输入——求值是纯函数，不改账、不产事件（D-0008 / D-0015）。
/// <see cref="Day"/> 用于涡流的「今天有没有人被处决」（R-0026）。
/// </remarks>
public sealed record OutcomeContext
{
    /// <summary>当前状态账。</summary>
    public required GameState State { get; init; }

    /// <summary>本局全部席位（升序）——求值器据此判断"观测是否齐全"，不猜。</summary>
    public required IReadOnlyList<SeatId> Seats { get; init; }

    /// <summary>角色事实端口。</summary>
    public required IWinConditionFacts Characters { get; init; }

    /// <summary>白天账（没有白天时为 null）；涡流条件读它。</summary>
    public DayState? Day { get; init; }

    /// <summary>本批新事件（业务 + 派生）：处决事实 / 黄昏 / 呆瓜选择都从这里读。</summary>
    public IReadOnlyList<GameEvent> Events { get; init; } = [];
}
