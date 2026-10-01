namespace OpenClockTower.Contracts;

/// <summary>能力使用账本的一条记录（说书人视角）：用过没有、生效过没有。</summary>
public sealed record AbilityUseDto
{
    /// <summary>使用能力的席位。</summary>
    public required int Seat { get; init; }

    /// <summary>被使用的能力 slug。</summary>
    public required string Ability { get; init; }

    /// <summary>这次使用是否正常生效。</summary>
    public required bool Effective { get; init; }
}
