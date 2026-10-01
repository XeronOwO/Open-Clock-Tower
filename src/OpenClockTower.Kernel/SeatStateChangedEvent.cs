namespace OpenClockTower.Kernel;

/// <summary>
/// 座位状态变化的事实记录：**谁**因为**什么原因**变成了什么样（说书人上帝视角的基础数据）。
/// </summary>
/// <remarks>
/// 这是审计与归因事件：它不改步骤机状态，但让"当前这一步的玩家为什么被中毒 / 解除中毒"
/// 这类问题在事件流里有据可查。观测维度可空：只记录本次真正观测到的维度。
/// </remarks>
public sealed record SeatStateChangedEvent : GameEvent
{
    /// <summary>发生变化的座位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>变化后的生死；null = 本次未观测。</summary>
    public LifeState? Life { get; init; }

    /// <summary>变化后的角色；null = 本次未观测。</summary>
    public CharacterId? Character { get; init; }

    /// <summary>变化后的阵营；null = 本次未观测。</summary>
    public Alignment? Alignment { get; init; }

    /// <summary>变化后的醉酒状态；null = 本次未观测。</summary>
    public DrunkState? Drunk { get; init; }

    /// <summary>变化后的中毒状态；null = 本次未观测。</summary>
    public PoisonState? Poison { get; init; }

    /// <summary>变化原因。</summary>
    public required string Reason { get; init; }

    /// <summary>导致变化的一方。</summary>
    public SeatId? CausedBy { get; init; }
}
