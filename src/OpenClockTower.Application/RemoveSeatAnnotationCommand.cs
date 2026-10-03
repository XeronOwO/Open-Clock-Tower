using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>说书人删一条注记（D-0019）；只说书人可发，删除写事件、不抹历史。</summary>
public sealed record RemoveSeatAnnotationCommand : GameCommand
{
    /// <summary>要删的注记标识（必须存在；不存在由合法性闸拒绝）。</summary>
    public required SeatAnnotationId Id { get; init; }
}
