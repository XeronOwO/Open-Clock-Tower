namespace OpenClockTower.Contracts;

/// <summary>
/// 「失去能力」提示标记（说书人视角，R-0040）：限次能力用尽后挂在角色标记旁。
/// </summary>
/// <remarks>由能力使用账本派生；玩家投影里没有这个数组（只有本人"已用尽"的窄字段）。</remarks>
public sealed record LostAbilityMarkerDto
{
    /// <summary>标记所在的席位。</summary>
    public required int Seat { get; init; }

    /// <summary>用尽的能力 slug。</summary>
    public required string Ability { get; init; }

    /// <summary>呈现说明。</summary>
    public required string Note { get; init; }
}
