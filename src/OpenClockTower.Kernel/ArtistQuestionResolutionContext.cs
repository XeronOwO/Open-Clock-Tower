namespace OpenClockTower.Kernel;

/// <summary>
/// 艺术家提问结清的输入：问题、说书人的裁决原文与当时的账（R-0040）。
/// </summary>
public sealed record ArtistQuestionResolutionContext
{
    /// <summary>进行中的问题。</summary>
    public required ArtistQuestion Question { get; init; }

    /// <summary>说书人的裁决原文（是 / 不是 / 我不知道 / 要求重问）。</summary>
    public required string Decision { get; init; }

    /// <summary>结清时刻的状态账（生效判定与信息内容都读它）。</summary>
    public required GameState State { get; init; }

    /// <summary>本局完整座次（按座位号升序 = 圆桌顺序）。</summary>
    public required IReadOnlyList<SeatId> Seats { get; init; }
}
