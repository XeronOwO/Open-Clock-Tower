namespace OpenClockTower.Kernel;

/// <summary>
/// 一次结算对账的结果：要落库的派生事件 + 判定不了的说明。
/// </summary>
/// <remarks>
/// 内核不碰 IO（D-0008），所以"为什么这次没重算"只能作为数据交回应用层去记日志——
/// <see cref="Diagnostics"/> 就是那条通道，分支的可观测性不因为内核纯净而丢掉。
/// </remarks>
public sealed record SettlementReconciliation
{
    /// <summary>需要与业务事件同批落库的派生事件。</summary>
    public IReadOnlyList<GameEvent> Events { get; init; } = [];

    /// <summary>判定不了的说明（应用层记日志用）。</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}
