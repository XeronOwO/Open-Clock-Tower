namespace OpenClockTower.Kernel;

/// <summary>
/// 构造博学者裁定点提示的上下文：提问席位 + 那一刻的状态账与座次（R-0057-C）。
/// </summary>
/// <remarks>
/// <para>
/// 提示里的候选事实与真值是**构造提示那一刻账的快照**：它随裁定点事件进事件流、下发给说书人端。
/// 说书人提交时，规则层按**提交时刻**的账重新求值与核对——挂起期间局势变了（角色变化 / 有人死亡），
/// 以提交时刻为准（与"结算取当时账"同姿态，票据第 9 行）。
/// </para>
/// <para>
/// 与 <see cref="SavantQuestionResolutionContext"/> 的分工：那个是结清（说书人已提交），这个是开局提示。
/// </para>
/// </remarks>
public sealed record SavantPromptContext
{
    /// <summary>提问的博学者席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>构造提示时刻的状态账。</summary>
    public required GameState State { get; init; }

    /// <summary>本局在局座次（按座位号升序）。</summary>
    public required IReadOnlyList<SeatId> Seats { get; init; }
}
