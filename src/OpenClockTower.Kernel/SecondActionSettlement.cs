namespace OpenClockTower.Kernel;

/// <summary>
/// 「行动两次」窗口的第二次结算判定：这一格结算完了，要不要在同一槽位再走一遍。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="StepMachine"/> 的推进路径拆出（单文件 600 行门禁：步骤机管推进与挂起，
/// 这里只回答"要不要重进本格"）。依据：百科《咖啡师》· 2026-10-04 抓取 · 角色能力 / 角色简介 /
/// 运作方式——「一名玩家的能力可以生效两次……如果该玩家会在夜晚被唤醒，使用能力后入睡，
/// 那么就再次唤醒该玩家让他再使用一次角色能力」；平台口径见
/// <c>docs/standard/rulings.md</c> R-0052 第 2 / 3 条。
/// </para>
/// <para>
/// 判据四件（缺一不可）：① 窗口在本格**确实结算过之后**仍然生效（账取"折完本批事件"）；
/// ② 本遍是**首遍**（<see cref="StepMachineState.SlotPass"/> = 1）——重进过就不再重进；
/// ③ 本格**确实产出过能力结算**（<see cref="StepMachineState.SlotAbilityResolved"/>）——
/// 跳过 / 作废 / 阻塞没有能力可再结算一次；④ 契约声明支持二次结算，且「每局限一次」的能力
/// 总使用次数还不到 2。
/// </para>
/// </remarks>
internal static class SecondActionSettlement
{
    /// <summary>本格是否需要再结算一次（重进本格）。</summary>
    /// <param name="state">**折完本批事件之后**的步骤机状态（遍次与"已结算"标记是判据）。</param>
    /// <param name="context">结算上下文；账取它，再折本批事件得到"此刻的账"。</param>
    /// <param name="producedEvents">本批已产出的全部事件（窗口可能就在其中：咖啡师选择自己）。</param>
    internal static bool IsDue(
        StepMachineState state,
        SettlementContext context,
        IReadOnlyList<GameEvent> producedEvents)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(producedEvents);

        if (!state.SlotAbilityResolved || state.SlotPass != 1)
        {
            return false;
        }

        if (state.CurrentSlot is not { Kind: StepSlotKind.Action, Actor: { } actor, Owner: { } owner })
        {
            return false;
        }

        var ledger = Fold(context.State, producedEvents);
        if (ledger.WindowOn(actor, EffectWindowKind.SecondAction) is not true)
        {
            return false;
        }

        var ability = context.Abilities.Find(owner);
        if (ability is null || !ability.SupportsSecondAction)
        {
            return false;
        }

        // 「每局限一次」的能力：窗口把**总使用次数**的上限放宽到 2（已用过 → 还能再用一次；
        // 没用过 → 下个黄昏前可用两次），不是"想重进几次就几次"。
        return !ability.IsLimitedPerGame
            || ledger.AbilityUses.UseCount(actor, ability.Ability) < 2;
    }

    /// <summary>把一批事件折进账：窗口是否生效、能力用了几次，都要读**此刻**的账（D-0010）。</summary>
    internal static GameState Fold(GameState state, IReadOnlyList<GameEvent> producedEvents)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(producedEvents);

        var ledger = state;
        foreach (var produced in producedEvents)
        {
            ledger = GameStateMachine.Apply(ledger, produced);
        }

        return ledger;
    }
}
