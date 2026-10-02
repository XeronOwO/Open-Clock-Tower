namespace OpenClockTower.Kernel;

/// <summary>
/// 白天规则处理一条输入的结果：受理时给事件，拒绝时给机器可读的拒绝码。
/// </summary>
/// <remarks>
/// 与 <see cref="StepMachineOutcome"/> 分开：白天规则不该知道步骤机状态；
/// 由 <see cref="StepMachine"/> 把两者拼起来（拒绝码原样透传给上层做审计与测试断言）。
/// </remarks>
public sealed record DayOutcome
{
    /// <summary>产出的事件；被拒绝时为空。</summary>
    public required IReadOnlyList<GameEvent> Events { get; init; }

    /// <summary>拒绝码（如 <c>day.nominator_dead</c>）；受理时为 null。</summary>
    public string? RejectionCode { get; init; }

    /// <summary>拒绝说明（给人看的定位信息）。</summary>
    public string? RejectionNote { get; init; }

    /// <summary>是否被拒绝。</summary>
    public bool IsRejected => RejectionCode is not null;

    /// <summary>受理：产出事件。</summary>
    public static DayOutcome Accepted(IReadOnlyList<GameEvent> events) =>
        new() { Events = events };

    /// <summary>拒绝：状态不变、事件为空。</summary>
    public static DayOutcome Reject(string code, string note) =>
        new() { Events = [], RejectionCode = code, RejectionNote = note };
}
