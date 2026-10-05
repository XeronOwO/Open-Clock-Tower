namespace OpenClockTower.Kernel;

/// <summary>
/// 近期活动账的折叠（R-0057-C）：把事件流里「真的变了什么」记进 <see cref="SeatActivityLedger"/>，
/// 并在开夜 / 黎明时推进两个窗口。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="GameStateMachine"/> 的迁移里拆出来，与账本同族：状态账回答"此刻是什么"，
/// 活动账回答"昨晚与今天发生了什么"，两者的判定规则并不相同——后者要求**变化前后都已知**
/// 才算一次变化（开局分配与首次观测是"知道"，不是"变化"），混在一处会长成一个谁也不认识的巨类。
/// </para>
/// <para>
/// 记录口径保守：宁可少记一条，也不记一条平台没看见的变化——候选事实库会把它当"真"讲给玩家听，
/// 假阳性等于平台在骗说书人（票据「要解决的问题」第 2 条）。
/// </para>
/// </remarks>
internal static class SeatActivityFolder
{
    /// <summary>
    /// 一条席位状态变化 → 活动账：死亡（存活 → 死亡）/ 角色变化 / 阵营变化，
    /// 各自独立记账（三者互不蕴含，架构「状态属于玩家，不属于角色」）。
    /// </summary>
    internal static SeatActivityLedger SeatChanged(
        SeatActivityLedger ledger,
        SeatStateChangedEvent changed,
        SeatStateEntry? previous)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(changed);

        if (changed.Life == LifeState.Dead && previous?.LifeValue == LifeState.Alive)
        {
            ledger = ledger.Record(SeatActivityKind.Death, changed.Seat, changed.Reason);
        }

        if (changed.Character is { } character
            && previous?.CharacterValue is { } previousCharacter
            && previousCharacter != character)
        {
            ledger = ledger.Record(SeatActivityKind.CharacterChange, changed.Seat, changed.Reason);
        }

        if (changed.Alignment is { } alignment
            && previous?.Alignment?.Value is { } previousAlignment
            && previousAlignment != alignment)
        {
            ledger = ledger.Record(SeatActivityKind.AlignmentChange, changed.Seat, changed.Reason);
        }

        return ledger;
    }

    /// <summary>处决事实（**处决 ≠ 死亡**，百科《处决》· 2026-10-01 抓取）：死亡另由状态变化记一条。</summary>
    internal static SeatActivityLedger Executed(SeatActivityLedger ledger, ExecutedEvent executed)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(executed);

        return ledger.Record(SeatActivityKind.Execution, executed.Seat, executed.Note);
    }

    /// <summary>
    /// 一次能力结算 → 活动账：**计入数学家的**失效（R-0004 的计数口径）记一条，其余分类不记。
    /// </summary>
    /// <remarks>
    /// 用与数学家同一个"算不算"的口径（<see cref="MalfunctionCounting.CountsForMathematician"/>），
    /// 是为了让「昨晚有玩家的能力未正常生效」这句话与数学家的数字同源——两处各判一次必然分叉。
    /// </remarks>
    internal static SeatActivityLedger AbilityResolved(SeatActivityLedger ledger, AbilityResolvedEvent resolved)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(resolved);

        var counted = resolved.Malfunctions
            .Where(kind => kind.CountsForMathematician())
            .ToArray();

        return counted.Length == 0
            ? ledger
            : ledger.Record(SeatActivityKind.Malfunction, resolved.Actor, resolved.Note);
    }

    /// <summary>开夜：推进"正在进行的夜晚"起点；白天计划不碰夜晚窗口。</summary>
    internal static SeatActivityLedger PhaseStarted(SeatActivityLedger ledger, PhaseStartedEvent started)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(started);

        return started.Plan.Phase is GamePhase.FirstNight or GamePhase.OtherNight
            ? ledger.StartNight()
            : ledger;
    }

    /// <summary>黎明：把刚结束的夜晚收成"最近一个已结束的夜晚"，并把白天窗口起点推到现在。</summary>
    internal static SeatActivityLedger Dawn(SeatActivityLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        return ledger.StartDay();
    }
}
