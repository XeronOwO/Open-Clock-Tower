namespace OpenClockTower.Contracts;

/// <summary>净分布的一项：角色类型（Kernel 枚举名）→ 数量。</summary>
public sealed record SetupTypeCountDto
{
    /// <summary>角色类型枚举名（Townsfolk / Outsider / Minion / Demon）。</summary>
    public required string Type { get; init; }

    /// <summary>数量。</summary>
    public required int Count { get; init; }
}
