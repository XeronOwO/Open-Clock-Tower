using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 「失去能力」提示标记（R-0040）：限次能力用尽后挂在角色标记旁。
/// </summary>
/// <remarks>
/// 由能力使用账本派生（不新增第二份事实，D-0010）；只说书人可见（D-0012 §4.3）。
/// </remarks>
public sealed record LostAbilityMarker
{
    /// <summary>标记所在的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>用尽的能力标识。</summary>
    public required AbilityId Ability { get; init; }

    /// <summary>呈现说明。</summary>
    public required string Note { get; init; }
}
