namespace OpenClockTower.Kernel;

/// <summary>
/// 一次「能力被使用」的记录。核心口径：**使用过 ≠ 生效过**。
/// </summary>
/// <remarks>
/// 依据：百科《重要细节》三-3——「如果他醉酒或中毒期间尝试使用自己的"每局游戏限一次"的能力，
/// 他无法在之后再次使用这项能力。使用机会会被浪费。」
/// </remarks>
public sealed record AbilityUse
{
    /// <summary>使用能力的玩家席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>被使用的能力。</summary>
    public required AbilityId Ability { get; init; }

    /// <summary>这次使用是否正常生效；醉酒/中毒期间使用一次性能力 → false（已浪费）。</summary>
    public required bool Effective { get; init; }
}
