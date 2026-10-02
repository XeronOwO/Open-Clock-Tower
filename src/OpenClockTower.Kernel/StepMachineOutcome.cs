namespace OpenClockTower.Kernel;

/// <summary>
/// 步骤机处理一条输入的结果：新状态 + 产出的事件；被拒绝时状态原样返回。
/// </summary>
public sealed record StepMachineOutcome
{
    /// <summary>接受还是拒绝。</summary>
    public required StepMachineOutcomeKind Kind { get; init; }

    /// <summary>接受后的新状态；被拒绝时是输入前的状态。</summary>
    public required StepMachineState State { get; init; }

    /// <summary>产出的事件；被拒绝时为空。</summary>
    public required IReadOnlyList<GameEvent> Events { get; init; }

    /// <summary>被拒绝的原因；接受时为 null。</summary>
    public StepMachineRejectionReason? RejectionReason { get; init; }

    /// <summary>
    /// 机器可读的拒绝码（如 <c>day.nominator_dead</c>）：白天规则拒绝时给出；
    /// 其余拒绝为 null，由调用方按 <see cref="RejectionReason"/> 生成 <c>kernel.*</c> 码。
    /// </summary>
    public string? RejectionCode { get; init; }

    /// <summary>拒绝说明（给日志与说书人定位用）。</summary>
    public string? RejectionNote { get; init; }
}
