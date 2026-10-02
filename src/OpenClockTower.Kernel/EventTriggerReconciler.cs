namespace OpenClockTower.Kernel;

/// <summary>
/// 事件触发的**有界级联**：把「刚刚发生的事件」交给规则层触发器，产出后果事件，再把后果事件喂回去，
/// 直到没有新事件为止。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="SettlementReconciler"/> 的分工与顺序：这里只处理**事件驱动**的后果（女巫的提名即死、
/// 后续的 OnDeath 家族），之后才由结算对账把常驻效果 / 能力存续 / 维度重新对齐。顺序不能反——
/// 触发产出的死亡很可能正是「存活 ≤3 → 失去能力」这类存续条件的输入。
/// </para>
/// <para>
/// **前置条件**：<paramref name="folded"/> 必须已经折入 <paramref name="producedEvents"/>——
/// 触发器读的是「刚发生完这些事情之后的账」，由调用方保证（会话在派发后先折账再进管线）。
/// </para>
/// <para>
/// **有界**：级联轮数超限一律显式抛错（同 D-0014 能力 3 的姿态），不允许静默停在半算出的状态；
/// 触发是确定且可重放的：给定（账 + 本批事件），产出的后果事件固定（D-0008）。
/// </para>
/// </remarks>
public static class EventTriggerReconciler
{
    /// <summary>允许的级联轮数；超出即判定触发器之间互相引发、规则互相打架。</summary>
    private const int MaxPasses = 16;

    /// <summary>按本批新事件跑一轮有界级联。</summary>
    /// <exception cref="ArgumentNullException">入参为 null。</exception>
    /// <exception cref="InvalidOperationException">级联不收敛，或触发器返回了 null。</exception>
    public static EventTriggerReconciliation Reconcile(
        GameState folded,
        SettlementContext context,
        IReadOnlyList<GameEvent> producedEvents)
    {
        ArgumentNullException.ThrowIfNull(folded);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(producedEvents);

        var state = folded;
        var events = new List<GameEvent>();
        var diagnostics = new List<string>();
        var pending = producedEvents;

        for (var pass = 0; pending.Count > 0; pass++)
        {
            if (context.EventTriggers.Count == 0)
            {
                break;
            }

            if (pass >= MaxPasses)
            {
                throw new InvalidOperationException(
                    $"事件触发在 {MaxPasses} 轮内没有收敛：触发器之间存在互相引发，"
                    + "必须显式失败（D-0014 能力 3），不得静默停在半算出的状态");
            }

            var triggerContext = new EventTriggerContext
            {
                State = state,
                Seats = context.Seats,
                Events = pending,
            };

            var produced = new List<GameEvent>();
            foreach (var trigger in context.EventTriggers)
            {
                var consequences = trigger.Evaluate(triggerContext)
                    ?? throw new InvalidOperationException(
                        $"事件触发器 {trigger.Ability} 返回了 null：没有后果时必须返回空列表");
                if (consequences.Count == 0)
                {
                    continue;
                }

                diagnostics.Add(
                    $"事件触发 {trigger.Ability} 对 {pending.Count} 条新事件产出 {consequences.Count} 条后果"
                    + $"（第 {pass + 1} 轮）");
                produced.AddRange(consequences);
            }

            if (produced.Count == 0)
            {
                break;
            }

            foreach (var consequence in produced)
            {
                state = GameStateMachine.Apply(state, consequence);
            }

            events.AddRange(produced);
            pending = produced;
        }

        return new EventTriggerReconciliation
        {
            State = state,
            Events = events,
            Diagnostics = diagnostics,
        };
    }
}
