namespace OpenClockTower.Contracts;

/// <summary>最近一次能力结算的结论（说书人上帝视角）：是否生效、为什么没生效。</summary>
public sealed record AbilityResolutionDto
{
    /// <summary>实施能力的席位。</summary>
    public required int Seat { get; init; }

    /// <summary>被结算的能力 slug。</summary>
    public required string Ability { get; init; }

    /// <summary>是否正常生效。</summary>
    public required bool Effective { get; init; }

    /// <summary>未正常生效时的原因分类（R-0004）。</summary>
    public string? Malfunction { get; init; }

    /// <summary>说明。</summary>
    public string? Note { get; init; }

    /// <summary>产生这条结论的事件序号。</summary>
    public required long Sequence { get; init; }
}
