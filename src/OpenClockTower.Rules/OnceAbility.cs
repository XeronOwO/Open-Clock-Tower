using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>一条限次能力（R-0040）：用尽后需要在魔典上放「失去能力」提示标记。</summary>
public sealed record OnceAbility
{
    /// <summary>能力标识（能力使用账本的键）。</summary>
    public required AbilityId Ability { get; init; }

    /// <summary>中文角色名（标记文案用）。</summary>
    public required string DisplayName { get; init; }
}
