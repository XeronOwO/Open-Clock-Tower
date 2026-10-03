namespace OpenClockTower.Kernel;

/// <summary>
/// 注记账的迁移：把事件折叠成 <see cref="SeatAnnotationLedger"/>（D-0019）。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="GameStateMachine"/> / <see cref="StepMachine"/> 同一套路数（D-0010）：事件是唯一事实来源，
/// 注记账只是同一事件流上的**独立派生视图**——自由文本不进状态账（D-0015）。
/// 纯计算（D-0008）：没有时间、随机、IO，也不依赖字典枚举顺序。
/// </para>
/// <para>
/// 只有三条注记事件会改这本账；其余事件原样返回——本账的作用域就此封闭，
/// 且未知事件类型仍会在状态账 / 步骤机的折叠里当场炸掉，不会被这里静默吞掉。
/// 顺序损坏（改 / 删不存在的注记、同一标识重复出现）显式抛错，绝不静默继续。
/// </para>
/// </remarks>
public static class SeatAnnotationMachine
{
    /// <summary>从零开始折叠整条事件流；空流得到空账。</summary>
    public static SeatAnnotationLedger Fold(IEnumerable<GameEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var ledger = SeatAnnotationLedger.Empty;
        foreach (var gameEvent in events)
        {
            ledger = Apply(ledger, gameEvent);
        }

        return ledger;
    }

    /// <summary>把一条事件折叠进注记账。</summary>
    /// <exception cref="InvalidOperationException">事件流顺序损坏时抛出。</exception>
    public static SeatAnnotationLedger Apply(SeatAnnotationLedger? ledger, GameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);
        var current = ledger ?? SeatAnnotationLedger.Empty;

        return gameEvent switch
        {
            SeatAnnotationAddedEvent added => current.Add(added.Annotation),
            SeatAnnotationUpdatedEvent updated => current.Update(updated.Annotation),
            SeatAnnotationRemovedEvent removed => current.Remove(removed.Annotation.Id),
            _ => current,
        };
    }
}
