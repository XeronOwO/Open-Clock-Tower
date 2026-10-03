using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>说书人改一条注记的文本（D-0019）；席位与标识不变，只说书人可发。</summary>
public sealed record UpdateSeatAnnotationCommand : GameCommand
{
    /// <summary>要改的注记标识（必须存在；不存在由合法性闸拒绝）。</summary>
    public required SeatAnnotationId Id { get; init; }

    /// <summary>新的自由文本原文（不可信；归一化与有界化在合法性闸与分派里做）。</summary>
    public required string Text { get; init; }
}
