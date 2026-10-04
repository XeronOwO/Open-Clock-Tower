namespace OpenClockTower.Kernel;

/// <summary>
/// 重获能力窗口终止时的连带收口：被重获的能力**再次失去**——它在这段窗口里产生的持续型效果与
/// 疯狂要求立即一并终止（百科《重要细节》二-3：「角色能力产生的持续型效果也会终止」；
/// 口径见 <c>docs/standard/rulings.md</c> R-0054）。
/// </summary>
/// <remarks>
/// <para>
/// 只终止**非 <see cref="PersistentEffect.SourceStateIndependent"/>** 的效果：死亡触发型等
/// 既成事实类效果（心上人的醉酒）在重获窗口出现之前就已落账，能力再次失去不回溯它们（R-0039）。
/// 疯狂要求逐目标席位扫一遍（它们记在目标席位的账上）。
/// </para>
/// <para>
/// 挑选口径是「来源 = 重获目标」：死者在此之前的持续型效果都已在死亡那一刻终止（R-0012），
/// 还活着的必然是在窗口存续期间新产生的——不需要额外标记。
/// </para>
/// <para>
/// 从 <see cref="GameStateMachine"/> 拆出（单文件 600 行门禁）：那边只负责「何时调用」，
/// 这里只负责「终止哪些」。纯计算、无副作用（D-0008）。
/// </para>
/// </remarks>
internal static class RegainDependentTermination
{
    /// <summary>重获窗口终止 → 终止目标名下仍在的窗口期效果与疯狂要求。</summary>
    internal static GameState Terminate(
        GameState state,
        PersistentEffect regain,
        EffectTermination termination)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(regain);
        ArgumentNullException.ThrowIfNull(termination);

        var cause = new EffectTermination
        {
            Kind = termination.Kind,
            CausedBy = termination.CausedBy,
            Reason = $"重获能力窗口 {regain.Id} 终止（{termination.Reason}）：被重获的能力再次失去，"
                + "它在这段窗口里产生的持续型效果与疯狂要求一并终止"
                + "（百科《重要细节》二-3；rulings.md R-0054）",
        };

        var target = regain.Target;
        var effects = state.PersistentEffects
            .Select(effect => !effect.IsTerminated
                && effect.Source == target
                && !effect.SourceStateIndependent
                    ? effect.Terminate(cause)
                    : effect)
            .ToArray();

        var seats = state.Seats
            .Select(entry => entry.Madnesses.Any(requirement =>
                    !requirement.IsTerminated && requirement.Source == target)
                ? entry with
                {
                    Madnesses =
                    [
                        .. entry.Madnesses.Select(requirement =>
                            !requirement.IsTerminated && requirement.Source == target
                                ? requirement.Terminate(cause)
                                : requirement),
                    ],
                }
                : entry)
            .ToArray();

        return state with { PersistentEffects = effects, Seats = seats };
    }
}
