namespace OpenClockTower.Kernel;

/// <summary>
/// 操作请求对某个座位的事实依赖：任一不满足，请求立即失去意义、自动作废。
/// </summary>
/// <remarks>
/// 依据票据第 5 条：目标玩家死亡、角色变更等上游变化必须自动作废并给出原因，不得静默丢弃。
/// 未声明的维度不约束（null = 不检查）。
/// </remarks>
public sealed record SeatDependency
{
    /// <summary>被依赖的座位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>要求该座位的生死；null 表示不约束。</summary>
    public LifeState? RequiredLife { get; init; }

    /// <summary>要求该座位的角色；null 表示不约束。</summary>
    public CharacterId? RequiredCharacter { get; init; }
}
