using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 夜尽收口：「直到下个黄昏」的窗口效果在**下一夜开始**时统一终止。
/// </summary>
/// <remarks>
/// <para>
/// 语义边界是「新夜晚阶段开始」：开夜命令在**建表之前**先按本收口把账折干净——否则建表期读到的
/// 还是上一夜的窗口（女裁缝「每局限一次」的第二次机会会被误判为仍可用，进而多开一格）。
/// 触发器（咖啡师 / 集骨者）与开夜命令共用这一份实现，避免"建表看得到、入槽看不到"的分叉；
/// 同一批里重复求值是幂等的：已终止的效果不再产出第二条。
/// </para>
/// <para>
/// 只看窗口效果本身：被终止效果名下的东西（重获能力的效果级联）由状态账折叠统一收口
/// （<see cref="GameStateMachine"/>），规则层不重复处理。
/// </para>
/// </remarks>
public static class DuskExpiry
{
    /// <summary>本批事件里有没有开启新的夜晚阶段。</summary>
    public static bool NightStarted(IReadOnlyList<GameEvent> events) =>
        events.Any(gameEvent =>
            gameEvent is PhaseStartedEvent { Plan.Phase: GamePhase.FirstNight or GamePhase.OtherNight });

    /// <summary>账上仍存续、且属于指定族（咖啡师 / 集骨者）的窗口效果的终止事件。</summary>
    public static IReadOnlyList<GameEvent> Expire(
        GameState state,
        Func<PersistentEffect, bool> belongsToFamily)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(belongsToFamily);

        return
        [
            .. state.PersistentEffects
                .Where(effect => !effect.IsTerminated && effect.Window is not null && belongsToFamily(effect))
                .Select(effect => (GameEvent)new PersistentEffectTerminatedEvent
                {
                    EffectId = effect.Id,
                    Termination = new EffectTermination
                    {
                        Kind = EffectTerminationKind.NoLongerApplies,
                        Reason = $"下个黄昏：窗口到期，移除上一夜留下的「{effect.Window}」标记"
                            + $"（{effect.Id}；rulings.md R-0052 / R-0054）",
                    },
                }),
        ];
    }

    /// <summary>账上仍存续的**全部**窗口效果（开夜前收口用：两族一起收）。</summary>
    public static IReadOnlyList<GameEvent> ExpireAll(GameState state) =>
        Expire(state, _ => true);
}
