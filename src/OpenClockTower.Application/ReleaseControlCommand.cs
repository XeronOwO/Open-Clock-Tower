namespace OpenClockTower.Application;

/// <summary>说书人交还自动化：恢复节拍器按配额推进（D-0014）。</summary>
public sealed record ReleaseControlCommand : GameCommand
{
    /// <summary>交还原因。</summary>
    public required string Reason { get; init; }
}
