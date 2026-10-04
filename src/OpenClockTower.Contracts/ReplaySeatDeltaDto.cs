namespace OpenClockTower.Contracts;

/// <summary>
/// 一条复盘步骤引发的席位事实增量：只带本次观测到的维度（形状与 <c>SeatStateChangedEvent</c> 一致）。
/// </summary>
/// <remarks>未观测的维度为 null，不是默认值——客户端按事件序号顺序合并出盘面。</remarks>
public sealed record ReplaySeatDeltaDto
{
    /// <summary>席位号。</summary>
    public required int Seat { get; init; }

    /// <summary>变化后的生死（<c>LifeState</c> 枚举名）；null = 本次未观测。</summary>
    public string? Life { get; init; }

    /// <summary>变化后的角色 slug；null = 本次未观测。</summary>
    public string? Character { get; init; }

    /// <summary>本次变化之前的角色 slug；null = 之前未观测或本次未观测角色。</summary>
    public string? PreviousCharacter { get; init; }

    /// <summary>变化后的阵营（<c>Alignment</c> 枚举名）；null = 本次未观测。</summary>
    public string? Alignment { get; init; }

    /// <summary>变化后的醉酒状态（<c>DrunkState</c> 枚举名）；null = 本次未观测。</summary>
    public string? Drunk { get; init; }

    /// <summary>变化后的中毒状态（<c>PoisonState</c> 枚举名）；null = 本次未观测。</summary>
    public string? Poison { get; init; }

    /// <summary>变化原因（同事件字段）。</summary>
    public string? Reason { get; init; }

    /// <summary>导致变化的一方席位号；null = 无特定归因。</summary>
    public int? CausedBy { get; init; }
}
