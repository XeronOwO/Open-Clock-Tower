using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>说书人给某席加一条自由文本注记（D-0019）；只说书人可发。</summary>
public sealed record AddSeatAnnotationCommand : GameCommand
{
    /// <summary>挂在哪一席（必须在本局席位名单里）。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>自由文本原文（不可信；归一化与有界化在合法性闸与分派里做）。</summary>
    public required string Text { get; init; }
}
