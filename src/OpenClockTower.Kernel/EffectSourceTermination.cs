namespace OpenClockTower.Kernel;

/// <summary>
/// 来源失效传播（《重要细节》二-3 / 二-7，口径见 <c>docs/standard/rulings.md</c> R-0012 与 R-0021）：
/// 来源死亡 → 它施加的持续型效果与下达的疯狂要求**立即终止**；来源的角色已不是施加时的角色
/// （= 失去了原角色能力）→ 同样终止。醉酒 / 中毒**不终止**，只是暂时不生效。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="GameStateMachine"/> 拆出：状态账的迁移要同时回答"发生了什么"（六维度与两本账）
/// 与"谁的什么跟着失效"（本类），后者是一整套自足的判定，混在一起会把迁移类撑到架构上限。
/// </para>
/// <para>
/// "角色是不是变了"用**效果 / 要求自己记录的施加时角色**判定，而不是"上一次观测到的角色"：
/// 后者在来源角色从未被观测过时会静默漏判，让一条早就该终止的效果继续被算成生效。
/// </para>
/// </remarks>
internal static class EffectSourceTermination
{
    /// <summary>把一条席位状态变化带来的失效传播应用到状态账。</summary>
    internal static GameState Apply(GameState state, SeatStateChangedEvent changed)
    {
        var termination = BuildTermination(changed);
        if (termination is null)
        {
            return state;
        }

        var regained = state.PersistentEffects
            .Where(effect => LosesAbility(state, effect, changed) && effect.Window is not null)
            .ToArray();

        var effects = state.PersistentEffects
            .Select(effect => LosesAbility(state, effect, changed) ? effect.Terminate(termination) : effect)
            .ToArray();

        // 疯狂要求与持续型效果同源同命运：来源死亡 / 换角色 → 立即撤下（R-0021）。
        // 目标**自己**的死亡 / 换角不撤下要求——已死亡的目标仍可能因不够疯狂被处决（R-0021）。
        var requirementTermination = BuildRequirementTermination(changed);
        var seats = state.Seats
            .Select(entry => entry.Madnesses.Any(requirement =>
                    LosesRequirementAbility(state, requirement, changed))
                ? entry with
                {
                    Madnesses = [.. entry.Madnesses.Select(requirement =>
                        LosesRequirementAbility(state, requirement, changed)
                            ? requirement.Terminate(requirementTermination)
                            : requirement)],
                }
                : entry)
            .ToArray();

        var next = state with { PersistentEffects = effects, Seats = seats };

        // 来源死亡 / 换角把某条能力窗口收掉时，被重获（R-0054）或保留（R-0056）的能力同步失去。
        foreach (var window in regained)
        {
            next = AbilityWindowDependentTermination.Terminate(next, window, termination);
        }

        return next;
    }

    /// <summary>来源死亡一律终止；来源角色与效果记录的施加时角色不同也终止。已终止的不重复处理。</summary>
    /// <remarks>
    /// 死亡之所以不再一律终止：被亡骨魔杀死的爪牙**从未失去**能力（保留能力窗口，R-0056）——
    /// 百科《死后能力保留》· 2026-10-01 抓取 · 能力简介：「这类能力生效与否不关注玩家的生死状态」。
    /// 窗口判定不了时按"没有保留"处理（照常终止）：这一格是**不可逆**的写操作，宁可少保留、
    /// 不可凭一个未观测的窗口把该终止的效果留下（与 D-0015 的保守姿态同向）。
    /// </remarks>
    private static bool LosesAbility(GameState state, PersistentEffect effect, SeatStateChangedEvent changed) =>
        effect.Source == changed.Seat
        && !effect.IsTerminated
        && ((changed.Life == LifeState.Dead && state.RetainedAbilityOn(changed.Seat) != true)
            || (changed.Character is { } character && character != effect.SourceCharacter));

    /// <summary>疯狂要求的同款判定：来源死亡（且没有保留能力）或换角色即撤下。</summary>
    private static bool LosesRequirementAbility(
        GameState state,
        MadnessRequirement requirement,
        SeatStateChangedEvent changed) =>
        requirement.Source == changed.Seat
        && !requirement.IsTerminated
        && ((changed.Life == LifeState.Dead && state.RetainedAbilityOn(changed.Seat) != true)
            || (changed.Character is { } character && character != requirement.SourceCharacter));

    private static EffectTermination? BuildTermination(SeatStateChangedEvent changed)
    {
        if (changed.Life == LifeState.Dead)
        {
            return new EffectTermination
            {
                Kind = EffectTerminationKind.SourceDied,
                Reason = $"来源席位 {changed.Seat} 死亡，其持续型效果立即终止（{changed.Reason}）",
                CausedBy = changed.CausedBy,
            };
        }

        if (changed.Character is { } character)
        {
            return new EffectTermination
            {
                Kind = EffectTerminationKind.SourceLostAbility,
                Reason = $"来源席位 {changed.Seat} 的角色已变为 {character}，不再是施加该效果时的角色，"
                    + $"原角色能力不再存在，其持续型效果立即终止（{changed.Reason}）",
                CausedBy = changed.CausedBy,
            };
        }

        return null;
    }

    /// <summary>疯狂要求的撤下说明：与效果终止同一分类，措辞换成"要求"。</summary>
    private static EffectTermination BuildRequirementTermination(SeatStateChangedEvent changed) =>
        changed.Life == LifeState.Dead
            ? new EffectTermination
            {
                Kind = EffectTerminationKind.SourceDied,
                Reason = $"来源席位 {changed.Seat} 死亡，它下达的疯狂要求立即撤下（{changed.Reason}）",
                CausedBy = changed.CausedBy,
            }
            : new EffectTermination
            {
                Kind = EffectTerminationKind.SourceLostAbility,
                Reason = $"来源席位 {changed.Seat} 的角色已发生变化，原角色能力不再存在，"
                    + $"它下达的疯狂要求立即撤下（{changed.Reason}）",
                CausedBy = changed.CausedBy,
            };
}
