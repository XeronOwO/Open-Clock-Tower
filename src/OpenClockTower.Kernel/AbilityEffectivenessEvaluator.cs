namespace OpenClockTower.Kernel;

/// <summary>
/// 能力生效判定：来源「存活 + 清醒 + 健康」→ 生效；中毒 / 醉酒 / 死亡 → 不生效（百科《重要细节》三-3）。
/// </summary>
/// <remarks>
/// <para>
/// 判定只读**来源自己的三个维度**；三个维度里任何一个还没被观测到，就返回 null——
/// 「无法判定」由调用方显式拒绝整条命令，绝不映射成默认值（D-0015 的同一原则）。
/// </para>
/// <para>
/// 死亡不是绝对的「能力不存在」：死亡但身上有生效中的「重获能力」窗口时，角色能力被重新获得，
/// 按「存活」继续判醉酒 / 中毒（百科《集骨者》· 2026-10-04 抓取 · 角色简介；R-0054）。
/// 因此判定取**状态账**，口径集中在 <see cref="GameState.AbilityPresentOn"/>。
/// </para>
/// <para>
/// 同时中毒且醉酒：两种状态并存、不相互抵消（《重要细节》三-3），能力不生效，
/// 且**两条原因并列**进账（R-0004 已闭合：不硬塞成单分类）。
/// </para>
/// </remarks>
public static class AbilityEffectivenessEvaluator
{
    /// <summary>
    /// 判定一次能力使用是否正常生效；来源维度未观测齐时返回 null（无法判定）。
    /// 死亡席位按状态账上的「重获能力」窗口放行（R-0054）。
    /// </summary>
    /// <param name="state">当前状态账（读重获窗口）。</param>
    /// <param name="actor">来源席位的账目。</param>
    public static AbilityOutcome? Evaluate(GameState state, SeatStateEntry actor)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.LifeValue is not { } life
            || actor.DrunkValue is not { } drunk
            || actor.PoisonValue is not { } poison)
        {
            return null;
        }

        if (life != LifeState.Alive)
        {
            switch (state.AbilityPresentOn(actor.Seat))
            {
                case true:
                    // 死亡但重获窗口生效：死亡本身不再是失效原因，继续按醉酒 / 中毒判定。
                    break;
                case null:
                    // 重获窗口判定不了：不猜（与来源维度未观测齐同款）。
                    return null;
                default:
                    return new AbilityOutcome
                    {
                        Effective = false,
                        Note = "来源已死亡：角色能力不复存在",
                    };
            }
        }

        return (poison == PoisonState.Poisoned, drunk == DrunkState.Drunk) switch
        {
            (true, true) => new AbilityOutcome
            {
                Effective = false,
                Malfunctions = [MalfunctionKind.Poisoned, MalfunctionKind.Drunk],
                Note = "同时中毒且醉酒：两种状态并存、不相互抵消，能力不生效（R-0004）",
            },
            (true, false) => new AbilityOutcome
            {
                Effective = false,
                Malfunctions = [MalfunctionKind.Poisoned],
                Note = "来源中毒：能力未生效",
            },
            (false, true) => new AbilityOutcome
            {
                Effective = false,
                Malfunctions = [MalfunctionKind.Drunk],
                Note = "来源醉酒：能力未生效",
            },
            _ => new AbilityOutcome { Effective = true },
        };
    }
}
