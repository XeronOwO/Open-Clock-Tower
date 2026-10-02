using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>最近一次能力结算的结论摘要（说书人视图用；玩家投影里没有它）。</summary>
/// <remarks>
/// 说书人上帝视角要回答「这一步为什么是这样」：谁、用哪个能力、是否生效、为什么没生效。
/// 它是事件流的派生量（<c>AbilityResolvedEvent</c>），不是新的事实来源。
/// </remarks>
public sealed record AbilityResolutionSnapshot
{
    /// <summary>实施能力的席位。</summary>
    public required SeatId Actor { get; init; }

    /// <summary>被结算的能力。</summary>
    public required AbilityId Ability { get; init; }

    /// <summary>是否正常生效。</summary>
    public required bool Effective { get; init; }

    /// <summary>未正常生效 / 受干扰的原因分类（R-0004）；可并列多条，正常时为空。</summary>
    public IReadOnlyList<MalfunctionKind> Malfunctions { get; init; } = [];

    /// <summary>说明（分类表达不了的组合写在这里）。</summary>
    public string? Note { get; init; }

    /// <summary>产生这条结论的事件序号。</summary>
    public required long Sequence { get; init; }
}
