using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>说书人代填当前挂起请求（玩家卡住时的对冲手段，事件标明代填来源）。</summary>
public sealed record ProxyFillCommand : GameCommand
{
    /// <summary>被代填的请求。</summary>
    public required OperationRequestId RequestId { get; init; }

    /// <summary>代填的选项值（仍必须在合法集合里）。</summary>
    public required string OptionValue { get; init; }

    /// <summary>代填缘由。</summary>
    public string? Note { get; init; }
}
