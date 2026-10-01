using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>玩家提交对当前操作请求的响应。</summary>
public sealed record SubmitResponseCommand : GameCommand
{
    /// <summary>被响应的请求。</summary>
    public required OperationRequestId RequestId { get; init; }

    /// <summary>被选中的选项值。</summary>
    public required string OptionValue { get; init; }
}
