using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 女巫诅咒的事件触发：被诅咒者在下个白天发起提名时立即死亡；他的提名仍然生效。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《女巫》· 2026-10-01 抓取 · 角色简介 1——「那名玩家如果在下个白天提名了任何玩家，
/// 就会死亡。尽管如此，他的提名仍然生效」；· 运作方式——「下个白天，如果被标记为『被诅咒』的玩家
/// 发起了提名，立即宣布他死亡。（提名正常进行）」。触发是**自动的**（说书人只负责宣布），
/// 因此由引擎产出死亡事实，不是裁定点。
/// </para>
/// <para>
/// 两个时机都在这一个触发器里，因为它们是同一条诅咒的两种归宿：
/// 提名 → 咒杀；白天结束（黄昏）→ 撤下（提示标记的移除时机）。**只活一个白天**这条语义
/// 就落在黄昏这一步上，而不是靠给效果记天数。
/// </para>
/// <para>
/// 诅咒是否生效读的是**当前账**：来源醉酒 / 中毒时挂起 → 不致死（R-0012）；
/// 存活 ≤3 或女巫已死 → 能力已失去 → 不致死（此时由 <see cref="WitchCursePresence"/> 把效果解除）。
/// </para>
/// <para>
/// **幂等**：编排方每轮只喂新事件，但级联里同一条件会被重复求值——因此死过一次的席位不再产出第二条
/// 死亡事实（读当前账判存活），黄昏撤下也只挑未终止的效果。
/// </para>
/// <para>
/// **「只活一个白天」的挂点就是这里**：本条规则没有给效果记天数，而是把语义落在
/// <see cref="DayClosedEvent"/> 上（一条规则一个触发点）。因此本触发器**必须常驻注册**
/// （<see cref="RoleContracts"/>）——目录缺席时账上会留下跨白天的假事实，这是本实现的已知边界，
/// 见 <c>docs/backlog/done/witch-curse.md</c> 的残余。
/// </para>
/// </remarks>
internal sealed class WitchCurseTrigger : IEventTrigger
{
    /// <inheritdoc />
    public AbilityId Ability => WitchAbility.CurseAbility;

    /// <inheritdoc />
    public IReadOnlyList<GameEvent> Evaluate(EventTriggerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var events = new List<GameEvent>();
        var inForce = WitchAbility.InForce(context.State, context.Seats);

        // 额外提名也是提名（R-0050）：对「发起提名」类触发（女巫诅咒）一视同仁——屠夫本人在窗口里
        // 提名时，若身上带着生效的诅咒，同样立即死亡。
        foreach (var gameEvent in context.Events)
        {
            var nominator = gameEvent switch
            {
                NominationMadeEvent made => made.Nominator,
                ExtraNominationMadeEvent extra => extra.Nominator,
                _ => (SeatId?)null,
            };
            if (nominator is not { } seat)
            {
                continue;
            }

            if (inForce == false)
            {
                // 能力已失去：不产生后果（诅咒由存续契约在本次提交内解除）。
                continue;
            }

            if (FindOperativeCurse(context.State, seat) is not { } curse)
            {
                continue;
            }

            if (context.State.Seat(seat)?.LifeValue != LifeState.Alive)
            {
                // 已经死了（级联里的重复求值）：不再产出第二条死亡事实。
                continue;
            }

            events.Add(new SeatStateChangedEvent
            {
                Seat = seat,
                Life = LifeState.Dead,
                Reason = WitchAbility.CurseDeathReason,
                CausedBy = curse.Source,
                EffectId = curse.Id,
            });
        }

        if (context.Events.OfType<DayClosedEvent>().Any())
        {
            foreach (var curse in LiveCurses(context.State))
            {
                events.Add(new PersistentEffectTerminatedEvent
                {
                    EffectId = curse.Id,
                    Termination = new EffectTermination
                    {
                        Kind = EffectTerminationKind.NoLongerApplies,
                        Reason = "黄昏：诅咒只持续一个白天，按《女巫》提示标记的移除时机撤下",
                    },
                });
            }
        }

        return events;
    }

    /// <summary>作用在该席位上、当前确实生效的女巫诅咒；挂起 / 判不了 / 没有都返回 null。</summary>
    private static PersistentEffect? FindOperativeCurse(GameState state, SeatId target)
    {
        foreach (var effect in state.LiveEffectsOn(target))
        {
            if (effect.Ability == WitchAbility.CurseAbility && state.IsOperative(effect) == true)
            {
                return effect;
            }
        }

        return null;
    }

    /// <summary>账上尚未终止的女巫诅咒（黄昏撤下的对象）。</summary>
    private static IReadOnlyList<PersistentEffect> LiveCurses(GameState state) =>
        [.. state.PersistentEffects.Where(effect =>
            effect.Ability == WitchAbility.CurseAbility && !effect.IsTerminated)];
}
