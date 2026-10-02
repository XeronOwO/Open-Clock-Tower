namespace OpenClockTower.Kernel;

/// <summary>
/// 一次能力结算的上下文：引擎把「这一步、这个人、这个选择、这个生效结论」交给角色契约。
/// </summary>
/// <remarks>
/// <para>
/// 契约只做**规则计算**：读状态、产出事件，不接触时间 / 随机 / IO（D-0008）。
/// 随机与裁量一律走裁定点（D-0002）；<see cref="Decision"/> 就是说书人的裁定结果。
/// </para>
/// <para>
/// <see cref="Seats"/> 按座位号升序传入：它是圆桌顺序，也是「邻近 / 顺时针」这类规则计算
/// 的唯一依据（状态账只存值，不存座次）。
/// </para>
/// </remarks>
public sealed record AbilityResolutionContext
{
    /// <summary>发起结算的槽位。</summary>
    public required StepSlotId SlotId { get; init; }

    /// <summary>计划标识（稳定事件标识由它派生）。</summary>
    public required string PlanLabel { get; init; }

    /// <summary>所属阶段（首夜 / 其他夜晚）。</summary>
    public required GamePhase Phase { get; init; }

    /// <summary>实施能力的席位。</summary>
    public required SeatId Actor { get; init; }

    /// <summary>实施能力时的角色——建表时写进槽位的角色，不是"现在查账"（过时不候）。</summary>
    public required CharacterId ActorCharacter { get; init; }

    /// <summary>本局完整座次（按座位号升序 = 圆桌顺序）。</summary>
    public required IReadOnlyList<SeatId> Seats { get; init; }

    /// <summary>结算时刻的状态账。</summary>
    public required GameState State { get; init; }

    /// <summary>生效判定结论（存活 / 清醒 / 健康三选一的结果，R-0004）。</summary>
    public required AbilityOutcome Outcome { get; init; }

    /// <summary>玩家在操作请求里选的值；入口裁定点直接结算时为 null。</summary>
    public string? Choice { get; init; }

    /// <summary>说书人对裁定点的裁定原文；没有走过裁定点时为 null。</summary>
    public string? Decision { get; init; }

    /// <summary>
    /// 本次行动开始时，白天账里**已经开始的白天数**（夜晚 N 行动时为 N−1）。
    /// 需要"跨夜存续窗口"的能力用它算绝对到期日——洗脑师的疯狂要求在施加夜的次日白天与其后夜晚有效，
    /// 下一个黎明撤下（<c>docs/standard/rulings.md</c> R-0021）。
    /// </summary>
    public required int DaysStarted { get; init; }
}
