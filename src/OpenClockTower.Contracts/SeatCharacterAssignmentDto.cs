namespace OpenClockTower.Contracts;

/// <summary>开局分配的一项：席位与角色（说书人 / 宿主命令的 wire 形态）。</summary>
public sealed record SeatCharacterAssignmentDto
{
    /// <summary>席位号。</summary>
    public required int Seat { get; init; }

    /// <summary>角色英文 slug；服务端会校验它是否在首版花名册里。</summary>
    public required string Character { get; init; }
}
