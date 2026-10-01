namespace OpenClockTower.Kernel;

/// <summary>
/// 一次「能力未正常生效」的记录。这不是调试日志，是游戏机制——数学家要靠它给出数字。
/// </summary>
/// <remarks>
/// 依据 <c>docs/standard/rulings.md</c> R-0004。注意：数学家**不会得知**异常发生在哪名玩家身上
/// （百科《数学家》· 2026-10-01 抓取 · 角色简介 / 运作方式），因此 <see cref="Seat"/> 只用于
/// 服务端账本与说书人视图，向下投影时必须剥离。
/// </remarks>
public sealed record Malfunction
{
    /// <summary>发生异常的能力所属席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>未正常生效的能力。</summary>
    public required AbilityId Ability { get; init; }

    /// <summary>原因分类；口径未定时为 <see cref="MalfunctionKind.Open"/>。</summary>
    public required MalfunctionKind Kind { get; init; }
}
