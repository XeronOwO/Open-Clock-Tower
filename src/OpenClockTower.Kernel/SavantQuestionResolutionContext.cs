namespace OpenClockTower.Kernel;

/// <summary>
/// 一条博学者提问的结清上下文：进行中的提问 + 说书人的裁定原文 + 当前账与座次。
/// </summary>
public sealed record SavantQuestionResolutionContext
{
    /// <summary>进行中的提问。</summary>
    public required SavantQuestion Question { get; init; }

    /// <summary>说书人的裁定原文（两条信息，用 <c>|</c> 分隔）。</summary>
    public required string? Decision { get; init; }

    /// <summary>裁定时刻的状态账。</summary>
    public required GameState State { get; init; }

    /// <summary>本局在局座次（按座位号升序）。</summary>
    public required IReadOnlyList<SeatId> Seats { get; init; }
}
