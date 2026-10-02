using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 夜间击杀的统一出口：窗口期外直接产生死亡事实，窗口期内改记**待定死亡**。
/// </summary>
/// <remarks>
/// <para>
/// 依据 <c>docs/standard/rulings.md</c> R-0030 第 2 条与百科《麻脸巫婆》· 2026-10-01 抓取 · 规则细节 1：
/// 「说书人能够自由决定是否让某名玩家死亡，或让被恶魔攻击的某名玩家存活」。
/// </para>
/// <para>
/// 所有会造成死亡的恶魔能力都必须走这里——分散写会让「麻脸巫婆之夜由说书人决定死亡」
/// 在不同恶魔身上表现不一致（改动要整族对齐）。
/// </para>
/// </remarks>
internal static class NightKill
{
    /// <summary>产出这次击杀的事件：窗口开启时是待定死亡，否则是即时型效果 + 死亡事实。</summary>
    /// <param name="context">结算上下文（提供施加者、槽位与窗口标志）。</param>
    /// <param name="target">被击杀的席位（调用方已经判定他还活着）。</param>
    /// <param name="ability">发起击杀的能力标识。</param>
    /// <param name="note">死亡原因（进状态变化事实与审计）。</param>
    internal static IReadOnlyList<GameEvent> Resolve(
        AbilityResolutionContext context,
        SeatId target,
        AbilityId ability,
        string note)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.PitHagNightActive)
        {
            return
            [
                new DeferredDeathRecordedEvent
                {
                    Target = target,
                    Source = context.Actor,
                    Ability = ability,
                    Note = $"{note}（麻脸巫婆之夜：死亡待说书人裁定）",
                },
            ];
        }

        var effectId = new EffectId($"{context.PlanLabel}:{context.SlotId}:kill");
        return
        [
            new InstantaneousEffectAppliedEvent
            {
                Effect = new InstantaneousEffect
                {
                    Id = effectId,
                    Source = context.Actor,
                    Ability = ability,
                    Target = target,
                },
            },
            new SeatStateChangedEvent
            {
                Seat = target,
                Life = LifeState.Dead,
                Reason = note,
                CausedBy = context.Actor,
                EffectId = effectId,
            },
        ];
    }
}
