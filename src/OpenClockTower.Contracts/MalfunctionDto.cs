namespace OpenClockTower.Contracts;

/// <summary>失效账本的一条记录（说书人视角）：哪条能力、因何未正常生效（R-0004）。</summary>
public sealed record MalfunctionDto
{
    /// <summary>发生异常的席位。</summary>
    public required int Seat { get; init; }

    /// <summary>未正常生效的能力 slug。</summary>
    public required string Ability { get; init; }

    /// <summary>原因分类（枚举名）。</summary>
    public required string Kind { get; init; }
}
