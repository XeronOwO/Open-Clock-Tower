namespace OpenClockTower.Kernel;

/// <summary>
/// 一次命令提交前的**固定点对账**：让常驻效果与「由效果压制的维度」都跟上当前状态。
/// </summary>
/// <remarks>
/// <para>
/// 一轮的顺序是：常驻效果期望集 → 补缺 / 终止多余 → 折账 → <see cref="DimensionEffectReconciler"/>
/// 重算维度 → 折账；只要还有新事件就再来一轮。<see cref="MaxPasses"/> 是防呆：
/// 不收敛说明规则互相打架，显式抛错——恢复必须失败，不得静默继续（D-0014 能力 3）。
/// </para>
/// <para>
/// 产出的事件由调用方与业务事件**同一次原子提交**落库；重放只折事件、不重算，
/// 因此这里每轮都必须产出确定、可重放的事件（D-0008）。
/// </para>
/// <para>
/// **世代规则**：期望标识是"此刻应当存在的那条效果"的稳定键。已经终止的同源效果不再复用标识——
/// 来源复活或换回原角色之后重新生效，会得到新的一条效果（<c>id#2</c>、<c>id#3</c>…），
/// 与「终止不可逆」（R-0012）不冲突：复活的是能力，不是那条旧效果。
/// </para>
/// </remarks>
public static class SettlementReconciler
{
    /// <summary>允许的**有效**轮数；超出即判定规则互相冲突。</summary>
    private const int MaxPasses = 16;

    /// <summary>对账：先常驻效果（补 / 终止），再按仍生效的效果重算维度，直到没有新事件。</summary>
    public static SettlementReconciliation Reconcile(SettlementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var events = new List<GameEvent>();
        var diagnostics = new List<string>();
        var current = context.State;

        for (var pass = 0; pass <= MaxPasses; pass++)
        {
            var passEvents = new List<GameEvent>();
            foreach (var source in context.StandingEffects)
            {
                var assessment = source.Evaluate(new StandingEffectContext
                {
                    State = current,
                    Seats = context.Seats,
                });
                if (!assessment.IsConclusive)
                {
                    diagnostics.Add(
                        $"常驻效果 {source.Ability} 本次未重算：{assessment.Note ?? "输入不全"}");
                    continue;
                }

                passEvents.AddRange(PlanStanding(current, source, assessment.Expectations));
            }

            var afterStanding = ApplyAll(current, passEvents);
            var dimensionEvents = DimensionEffectReconciler.Reconcile(afterStanding);
            if (passEvents.Count == 0 && dimensionEvents.Count == 0)
            {
                return new SettlementReconciliation { Events = events, Diagnostics = diagnostics };
            }

            if (pass == MaxPasses)
            {
                break;
            }

            events.AddRange(passEvents);
            events.AddRange(dimensionEvents);
            current = ApplyAll(afterStanding, dimensionEvents);
        }

        throw new InvalidOperationException(
            $"结算对账在 {MaxPasses} 轮内没有收敛：常驻效果之间存在互相冲突的规则，"
            + "必须显式失败（D-0014 能力 3），不得静默停在半算出的状态");
    }

    /// <summary>
    /// 把一个常驻来源的期望集落地成事件：缺的补、多的终止。
    /// 终止不可逆（R-0012）：已经终止过的效果不复活，哪怕期望集里又有同一条标识。
    /// </summary>
    private static IReadOnlyList<GameEvent> PlanStanding(
        GameState state,
        IStandingEffectSource source,
        IReadOnlyList<StandingEffectExpectation> expectations)
    {
        var events = new List<GameEvent>();

        foreach (var expectation in expectations)
        {
            // 「同一条效果」按标识认，但已终止的不算活着：来源复活、或换走又换回原角色之后，
            // 角色能力重新生效，应当产生**新的一条效果**（新标识），而不是让旧效果复活
            // （R-0012：终止不可逆，但"再次获得同一能力"是另一回事）。
            var instances = state.PersistentEffects
                .Where(effect => IsInstanceOf(effect.Id, expectation.Id))
                .ToArray();

            if (instances.Any(instance => !instance.IsTerminated))
            {
                continue;
            }

            events.Add(new PersistentEffectAppliedEvent
            {
                Effect = new PersistentEffect
                {
                    Id = GenerationId(expectation.Id, instances.Length + 1),
                    Source = expectation.Source,
                    Ability = expectation.Ability,
                    Target = expectation.Target,
                    SourceCharacter = expectation.SourceCharacter,
                    Dimension = expectation.Dimension,
                },
            });
        }

        foreach (var effect in state.PersistentEffects)
        {
            if (effect.Ability != source.Ability || effect.IsTerminated)
            {
                continue;
            }

            if (expectations.Any(expectation => IsInstanceOf(effect.Id, expectation.Id)))
            {
                continue;
            }

            events.Add(new PersistentEffectTerminatedEvent
            {
                EffectId = effect.Id,
                Termination = new EffectTermination
                {
                    Kind = EffectTerminationKind.NoLongerApplies,
                    Reason = $"常驻效果重算：{source.Ability} 的条件不再满足，效果 {effect.Id} 终止",
                },
            });
        }

        return events;
    }

    private static GameState ApplyAll(GameState state, IReadOnlyList<GameEvent> events)
    {
        var next = state;
        foreach (var gameEvent in events)
        {
            next = GameStateMachine.Apply(next, gameEvent);
        }

        return next;
    }

    /// <summary>
    /// 这条效果标识是不是某个期望标识的实例：期望标识本身，或它的第 N 代（<c>id#2</c>、<c>id#3</c>…）。
    /// </summary>
    /// <remarks>
    /// 「终止后再次生效」（来源复活、或换走又换回原角色）产生的是**新的一条效果**，
    /// 旧的标识不再复用——终止不可逆（R-0012），但重新获得同一能力是另一回事。
    /// </remarks>
    private static bool IsInstanceOf(EffectId candidate, EffectId expectation) =>
        candidate == expectation
        || (candidate.Value.Length > expectation.Value.Length
            && candidate.Value.StartsWith(expectation.Value, StringComparison.Ordinal)
            && candidate.Value[expectation.Value.Length] == '#');

    /// <summary>第 N 代的期望标识：第 1 代就是期望标识本身。</summary>
    private static EffectId GenerationId(EffectId expectation, int generation) =>
        generation <= 1 ? expectation : new EffectId($"{expectation.Value}#{generation}");
}
