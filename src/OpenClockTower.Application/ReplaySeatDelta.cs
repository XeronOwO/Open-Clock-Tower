using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 一条复盘步骤引发的**席位事实增量**：只带本次观测到的维度，客户端按事件序号顺序合并出盘面。
/// </summary>
/// <remarks>
/// 形状与 <see cref="SeatStateChangedEvent"/> 一致（同源口径，D-0020）：未观测的维度为 null，
/// 不是默认值——六维度相互独立（架构 §2.1）。复盘不新增事实源：增量本身也是事件里的字段。
/// </remarks>
public sealed record ReplaySeatDelta
{
    /// <summary>发生变化的座位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>变化后的生死；null = 本次未观测。</summary>
    public LifeState? Life { get; init; }

    /// <summary>变化后的角色；null = 本次未观测。</summary>
    public CharacterId? Character { get; init; }

    /// <summary>本次变化**之前**该席位已知的角色；null = 之前未观测或本次未观测角色。</summary>
    public CharacterId? PreviousCharacter { get; init; }

    /// <summary>变化后的阵营；null = 本次未观测。</summary>
    public Alignment? Alignment { get; init; }

    /// <summary>变化后的醉酒状态；null = 本次未观测。</summary>
    public DrunkState? Drunk { get; init; }

    /// <summary>变化后的中毒状态；null = 本次未观测。</summary>
    public PoisonState? Poison { get; init; }

    /// <summary>变化原因（同事件字段）。</summary>
    public string? Reason { get; init; }

    /// <summary>导致变化的一方；null = 无特定归因。</summary>
    public SeatId? CausedBy { get; init; }
}
