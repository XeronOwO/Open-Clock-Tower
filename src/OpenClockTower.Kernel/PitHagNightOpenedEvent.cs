namespace OpenClockTower.Kernel;

/// <summary>
/// 麻脸巫婆之夜的死亡裁量窗口开启：这一晚的死亡由说书人决定（R-0030 第 1 条）。
/// </summary>
/// <remarks>
/// 由麻脸巫婆的结算契约在「创造了恶魔」时产出——包括把恶魔变成另一种恶魔
/// （百科《麻脸巫婆》· 2026-10-01 抓取 · 运作方式：「如果麻脸巫婆将恶魔变成另一种恶魔，最好是让今晚无人死亡」）。
/// </remarks>
public sealed record PitHagNightOpenedEvent : GameEvent
{
    /// <summary>麻脸巫婆的席位。</summary>
    public required SeatId Source { get; init; }

    /// <summary>窗口关闭点：最后一个能造成死亡的恶魔行动槽位下标。</summary>
    public required int ClosesAfterSlotIndex { get; init; }

    /// <summary>追加死亡的归因能力标识。</summary>
    public required AbilityId CasualtyAbility { get; init; }
}
