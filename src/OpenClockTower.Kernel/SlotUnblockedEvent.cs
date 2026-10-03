namespace OpenClockTower.Kernel;

/// <summary>
/// 阻塞报警被解除：只把 <see cref="StepMachineState.Block"/> 置空，**不动**槽位下标与配额。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SlotBlockedEvent"/> 的对偶。内核里三类挂起各有一条收口事件：挂起请求
/// （<see cref="OperationRequestVoidedEvent"/>）、等待说书人的裁定点
/// （<see cref="DecisionPointResolvedEvent"/>）、以及本条承载的阻塞报警（R-0009 BlockAndAlert）。
/// </para>
/// <para>
/// **不许用伪造的推进事件顺手清阻塞**：那会把槽位位置与最小配额一起改掉，投影与快照立刻分叉。
/// 结束批次（<c>SessionCommit.AppendGameEnding</c>）用本事件收口——结束后一切输入被拒（R-0024），
/// 阻塞报警再也无人能解，终局快照不该留一块点不动的死控件（D-0010：事件是唯一事实来源）。
/// </para>
/// </remarks>
public sealed record SlotUnblockedEvent : GameEvent
{
    /// <summary>被解除阻塞的槽位。</summary>
    public required StepSlotId SlotId { get; init; }

    /// <summary>解除原因（人类可读，进审计、说书人视图与日志）。</summary>
    public required string Reason { get; init; }
}
