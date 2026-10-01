namespace OpenClockTower.Kernel;

/// <summary>对当前挂起请求的响应。</summary>
public sealed record SubmitResponseInput : StepMachineInput
{
    /// <summary>被响应的请求（必须是当前挂起的那个）。</summary>
    public required OperationRequestId RequestId { get; init; }

    /// <summary>被选中的选项值（必须来自合法集合）。</summary>
    public required string OptionValue { get; init; }

    /// <summary>响应来源：玩家本人或说书人代填。</summary>
    public required ResponseSource Source { get; init; }

    /// <summary>备注（代填缘由等）。</summary>
    public string? Note { get; init; }
}
