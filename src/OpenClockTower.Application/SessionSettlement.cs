using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Application;

/// <summary>
/// 会话的结算管线：先把「事件 → 能力后果」落地，再把账跑固定点对账，产出要**同批落库**的派生事件。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="GameSession"/> 拆出：会话管状态与提交，这里只回答「折完账之后还要补什么事件」。
/// 规则计算全在内核（<see cref="EventTriggerReconciler"/> / <see cref="SettlementReconciler"/>），
/// 应用层只做编排。
/// </para>
/// <para>
/// **顺序不能反**：事件触发（女巫的「提名即死」）先落地，随后才做常驻效果 / 能力存续 / 维度对账——
/// 触发产出的死亡很可能正是「存活 ≤3 → 失去能力」这类存续条件的输入。
/// </para>
/// <para>
/// 事件触发 / 能力存续两族契约取自规则层目录 <see cref="RoleContracts"/>：它们是**规则数据**，
/// 与会话注入的契约目录（abilities / standingEffects）并存——后两者是会话持有的依赖，
/// 前者是规则层的静态目录，取法与 <c>GameCommandDispatcher</c> 取 <c>NightActions.Default</c> 同族。
/// </para>
/// </remarks>
internal static class SessionSettlement
{
    /// <summary>把当前账打包成结算上下文：座次取服务端持有的席位名单（升序 = 圆桌顺序）。</summary>
    /// <param name="setup">会话信息（席位名单）。</param>
    /// <param name="state">当前账。</param>
    /// <param name="abilities">能力结算契约目录。</param>
    /// <param name="standingEffects">常驻效果来源。</param>
    /// <param name="machine">
    /// 本批命令**之前**的步骤机状态：触发管线用它读白天是否开着（R-0027 的"即时公告"判据）
    /// 与请求归属；折完业务事件后由调用方换成批后的状态（<c>with { Machine = ... }</c>）。
    /// </param>
    internal static SettlementContext BuildContext(
        GameSetup? setup,
        GameState state,
        IAbilityResolutionCatalog abilities,
        IReadOnlyList<IStandingEffectSource> standingEffects,
        StepMachineState? machine = null) =>
        new()
        {
            State = state,
            Seats = setup is null
                ? []
                : [.. setup.Seats.Select(ticket => ticket.Seat).OrderBy(seat => seat.Value)],
            Abilities = abilities,
            StandingEffects = standingEffects,
            SlotPrompts = NightActions.Prompts,
            EventTriggers = RoleContracts.EventTriggers,
            AbilityPresences = RoleContracts.AbilityPresences,
            AdjudicatedExecutions = RoleContracts.AdjudicatedExecutions,
            ArtistQuestions = RoleContracts.ArtistQuestions,
            Machine = machine,
            DayWasOpen = machine?.Day?.OpenDay is not null,
        };

    /// <summary>
    /// 一次提交前的结算：事件触发（有界级联）→ 常驻效果 / 能力存续 / 维度对账。
    /// </summary>
    /// <param name="folded">已经折入 <paramref name="producedEvents"/> 的账。</param>
    /// <param name="context">结算上下文（座次与契约目录）。</param>
    /// <param name="producedEvents">本次命令产生的业务事件——触发器只按它们求值。</param>
    internal static (
        GameState State,
        IReadOnlyList<GameEvent> Events,
        IReadOnlyList<string> Diagnostics) Reconcile(
        GameState folded,
        SettlementContext context,
        IReadOnlyList<GameEvent> producedEvents)
    {
        var triggered = EventTriggerReconciler.Reconcile(folded, context, producedEvents);
        var reconciliation = SettlementReconciler.Reconcile(context.WithState(triggered.State));

        var state = triggered.State;
        foreach (var derived in reconciliation.Events)
        {
            state = GameStateMachine.Apply(state, derived);
        }

        return (
            state,
            [.. triggered.Events, .. reconciliation.Events],
            [.. triggered.Diagnostics, .. reconciliation.Diagnostics]);
    }

    /// <summary>
    /// 只做「账实一致的收尾」（常驻效果 / 能力存续 / 维度重算），**不跑事件触发**。
    /// </summary>
    /// <remarks>
    /// 用于本批已经判出胜负的场合：百科《处决》· 2026-10-01 抓取 · 一些相关效果的触发时机——
    /// 第 3 步（检查胜负）先于第 4 步（结算死亡触发能力），所以死亡触发能力不再结算；
    /// 但账务收尾仍要落地，否则终局的账与效果链会自相矛盾（来源已死的效果还压着目标的维度）。
    /// 收尾不产生任何规则后果：它只补效果 / 解除效果 / 按仍生效的效果重算维度。
    /// </remarks>
    internal static (
        GameState State,
        IReadOnlyList<GameEvent> Events,
        IReadOnlyList<string> Diagnostics) ReconcileHousekeeping(
        GameState folded,
        SettlementContext context)
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
