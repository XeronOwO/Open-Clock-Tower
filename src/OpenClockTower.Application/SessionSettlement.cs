using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 会话的结算管线：把一批业务事件的账跑固定点对账，产出要**同批落库**的派生事件。
/// </summary>
/// <remarks>
/// 从 <see cref="GameSession"/> 拆出：会话管状态与提交，这里只回答「折完账之后还要补什么事件」。
/// 规则计算全在内核（<see cref="SettlementReconciler"/>），应用层只做编排。
/// </remarks>
internal static class SessionSettlement
{
    /// <summary>把当前账打包成结算上下文：座次取服务端持有的席位名单（升序 = 圆桌顺序）。</summary>
    internal static SettlementContext BuildContext(
        GameSetup? setup,
        GameState state,
        IAbilityResolutionCatalog abilities,
        IReadOnlyList<IStandingEffectSource> standingEffects) =>
        new()
        {
            State = state,
            Seats = setup is null
                ? []
                : [.. setup.Seats.Select(ticket => ticket.Seat).OrderBy(seat => seat.Value)],
            Abilities = abilities,
            StandingEffects = standingEffects,
        };

    /// <summary>
    /// 对账：返回对账后的账、要与业务事件同批落库的派生事件，以及「这次没重算」的说明
    /// （说明由调用方记日志——内核不碰 IO）。
    /// </summary>
    internal static (
        GameState State,
        IReadOnlyList<GameEvent> Events,
        IReadOnlyList<string> Diagnostics) Reconcile(GameState folded, SettlementContext context)
    {
        var reconciliation = SettlementReconciler.Reconcile(context.WithState(folded));

        var state = folded;
        foreach (var derived in reconciliation.Events)
        {
            state = GameStateMachine.Apply(state, derived);
        }

        return (state, reconciliation.Events, reconciliation.Diagnostics);
    }
}
