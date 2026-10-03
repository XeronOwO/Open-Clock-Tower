namespace OpenClockTower.Kernel;

/// <summary>
/// 艺术家的进行中提问：白天主动向说书人提出的一个是 / 否问题，等待回答。
/// </summary>
/// <remarks>
/// 口径见 <c>docs/standard/rulings.md</c> R-0040：是否提问由艺术家决定（玩家主动发起）、
/// 不是公开交流；回答（是 / 不是 / 我不知道）或「要求重问」结清后清空；
/// 问题全文只进说书人与本人投影（D-0012）。
/// </remarks>
public sealed record ArtistQuestion
{
    /// <summary>提问的艺术家席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>
    /// 提问时刻该席位的角色（来源检索键）：结清按它取契约——挂起期间角色被换走也不改变结清路径
    /// （与槽位结算按槽位 <c>Owner</c> 取契约同姿态）。
    /// </summary>
    public required CharacterId Character { get; init; }

    /// <summary>问题全文（自由文本）。</summary>
    public required string Question { get; init; }
}
