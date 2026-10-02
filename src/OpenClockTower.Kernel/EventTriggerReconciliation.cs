namespace OpenClockTower.Kernel;

/// <summary>
/// 一次事件触发级联的结果：折完触发的账 + 要落库的后果事件 + 判定不了的说明。
/// </summary>
/// <remarks>
/// 与 <see cref="SettlementReconciliation"/> 分开：那个是「账与常驻效果 / 维度对齐」的结果，
/// 这个是「事件后果」的结果，两者在应用层按顺序编排、同一次原子提交落库。
/// </remarks>
public sealed record EventTriggerReconciliation
{
    /// <summary>已折入 <see cref="Events"/> 的状态账。</summary>
    public required GameState State { get; init; }

    /// <summary>需要与业务事件同批落库的后果事件。</summary>
    public IReadOnlyList<GameEvent> Events { get; init; } = [];

    /// <summary>给应用层记日志的说明（内核不碰 IO，D-0008）。</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}
