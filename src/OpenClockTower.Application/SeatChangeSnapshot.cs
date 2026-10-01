using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 说书人视角的一条状态变化记录：**谁**因为**什么原因**变成了什么样（上帝视角的信息面）。
/// </summary>
/// <remarks>
/// 由 <c>SeatStateChangedEvent</c> 折叠而来；完整的原因链与"最终计算结果"依赖结算引擎，
/// 见 `docs/backlog/todo/storyteller-step-insights.md`。
/// </remarks>
public sealed record SeatChangeSnapshot
{
    /// <summary>发生变化的座位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>变化后的生死；null = 未观测。</summary>
    public LifeState? Life { get; init; }

    /// <summary>变化后的角色；null = 未观测。</summary>
    public CharacterId? Character { get; init; }

    /// <summary>变化后的阵营；null = 未观测。</summary>
    public Alignment? Alignment { get; init; }

    /// <summary>变化后的醉酒状态；null = 未观测。</summary>
    public DrunkState? Drunk { get; init; }

    /// <summary>变化后的中毒状态；null = 未观测。</summary>
    public PoisonState? Poison { get; init; }

    /// <summary>变化原因。</summary>
    public required string Reason { get; init; }

    /// <summary>导致变化的一方。</summary>
    public SeatId? CausedBy { get; init; }

    /// <summary>
    /// 本次变化由哪条持续型效果导致（票据第 6 条的「维度 → 效果链接」）；
    /// null = 与效果无关（开局分配 / 说书人上报等）。说书人面板据此对**变化本身**追问
    /// 「这是哪条效果造成的」，不必再去状态账里比对。
    /// </summary>
    public EffectId? EffectId { get; init; }

    /// <summary>对应事件序号。</summary>
    public required long Sequence { get; init; }

    /// <summary>宿主记录的发生时刻。</summary>
    public required DateTimeOffset RecordedAt { get; init; }
}
