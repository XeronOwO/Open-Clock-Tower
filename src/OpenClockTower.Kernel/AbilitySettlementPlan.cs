namespace OpenClockTower.Kernel;

/// <summary>
/// 一个行动槽位的结算安排：没有契约 / 等说书人再裁定一次 / 已经算出 / 判不了。
/// </summary>
/// <remarks>
/// 内核内部类型：<see cref="StepMachine"/> 只按它决定"推进、挂起还是拒绝"，
/// 结算本身的规则计算在 <see cref="AbilitySettlement"/> 与规则层的契约里。
/// </remarks>
internal sealed record AbilitySettlementPlan
{
    /// <summary>四种去向（嵌套枚举：本记录的辅助类型，不是独立契约）。</summary>
    internal enum PlanKind
    {
        /// <summary>槽位没有结算契约（空槽位 / 未登记角色）：不结算，照旧推进。</summary>
        NoContract,

        /// <summary>需要说书人再裁定一次（信息类能力）；裁定后才算得出事件。</summary>
        RequiresDecision,

        /// <summary>已经算出要产出的事件。</summary>
        Resolved,

        /// <summary>判不了（来源维度没观测齐）：整条命令必须被拒绝，不猜。</summary>
        Indeterminate,
    }

    /// <summary>本次结算的去向。</summary>
    internal required PlanKind Kind { get; init; }

    /// <summary>需要裁定时的提示契约。</summary>
    internal ChoicePrompt? DecisionPrompt { get; init; }

    /// <summary>已经算出的事件（结算结论 + 角色契约产出）。</summary>
    internal IReadOnlyList<GameEvent> Events { get; init; } = [];

    /// <summary>判不了时的说明。</summary>
    internal string? FailureNote { get; init; }

    /// <summary>没有结算契约。</summary>
    internal static AbilitySettlementPlan NoContract { get; } = new() { Kind = PlanKind.NoContract };

    /// <summary>挂起等说书人裁定。</summary>
    internal static AbilitySettlementPlan RequiresDecision(ChoicePrompt prompt) =>
        new() { Kind = PlanKind.RequiresDecision, DecisionPrompt = prompt };

    /// <summary>结算完成。</summary>
    internal static AbilitySettlementPlan Resolved(IReadOnlyList<GameEvent> events) =>
        new() { Kind = PlanKind.Resolved, Events = events };

    /// <summary>无法判定：把整条输入拒掉。</summary>
    internal static AbilitySettlementPlan Indeterminate(string note) =>
        new() { Kind = PlanKind.Indeterminate, FailureNote = note };
}
