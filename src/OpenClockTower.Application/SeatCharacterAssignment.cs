using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>开局分配的一项：某个席位由哪个角色扮演。</summary>
public sealed record SeatCharacterAssignment
{
    /// <summary>席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>角色。</summary>
    public required CharacterId Character { get; init; }
}
