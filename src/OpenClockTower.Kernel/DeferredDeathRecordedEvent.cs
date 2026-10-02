namespace OpenClockTower.Kernel;

/// <summary>
/// 窗口期内的恶魔击杀被记为**待定死亡**（不直接致死），等说书人裁定（R-0030 第 2 条）。
/// </summary>
public sealed record DeferredDeathRecordedEvent : GameEvent
{
    /// <summary>被攻击的席位。</summary>
    public required SeatId Target { get; init; }

    /// <summary>发起击杀的恶魔席位。</summary>
    public required SeatId Source { get; init; }

    /// <summary>发起击杀的能力标识。</summary>
    public required AbilityId Ability { get; init; }

    /// <summary>发生位置与依据说明。</summary>
    public required string Note { get; init; }
}
