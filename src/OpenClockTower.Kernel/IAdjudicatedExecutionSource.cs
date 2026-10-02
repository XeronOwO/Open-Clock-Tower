namespace OpenClockTower.Kernel;

/// <summary>
/// 处罚处决依据契约（规则层实现）：回答"此刻以这个来源处罚这个席位是否成立、死亡怎么归因"。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="IEventTrigger"/> / <see cref="IAbilityPresence"/> 同族：内核声明它需要的外部能力，
/// 规则层实现（洗脑师看疯狂要求、畸形秀演员看自身角色与状态），依赖图保持无环。
/// 内核因此不必认识任何角色 slug（D-0008：规则数据属于 Rules）。
/// </para>
/// <para>
/// **纯函数**：只读账与座次，不写账、不产事件；后果事件由 <see cref="AdjudicatedExecutionMachine"/> 收口。
/// </para>
/// </remarks>
public interface IAdjudicatedExecutionSource
{
    /// <summary>本契约负责的处罚来源。</summary>
    MadnessPunishmentSource Source { get; }

    /// <summary>
    /// 判定以本来源处罚 <paramref name="seat"/> 是否成立。
    /// 返回 <see cref="AdjudicatedExecutionEligibility.Applicable"/> 为 null 表示事实没观测齐（不猜）。
    /// </summary>
    AdjudicatedExecutionEligibility Evaluate(GameState state, IReadOnlyList<SeatId> seats, SeatId seat);
}
