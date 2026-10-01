namespace OpenClockTower.Application;

/// <summary>说书人接管：暂停自动节拍，改为手动逐步驱动（D-0014）。</summary>
public sealed record TakeOverCommand : GameCommand
{
    /// <summary>接管原因。</summary>
    public required string Reason { get; init; }
}
