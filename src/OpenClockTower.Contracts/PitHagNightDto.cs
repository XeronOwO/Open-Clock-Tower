namespace OpenClockTower.Contracts;

/// <summary>
/// 麻脸巫婆之夜的死亡裁量窗口（<c>docs/standard/rulings.md</c> R-0030）：只说书人视图可见。
/// </summary>
/// <remarks>
/// 说书人据此裁定待定死亡、并在窗口内追加死亡；玩家投影里没有这个字段（D-0012 §4.3）。
/// </remarks>
public sealed record PitHagNightDto
{
    /// <summary>麻脸巫婆的席位（追加死亡的归因来源）。</summary>
    public required int Source { get; init; }

    /// <summary>窗口关闭点：最后一个能造成死亡的恶魔行动槽位下标。</summary>
    public required int ClosesAfterSlotIndex { get; init; }

    /// <summary>尚未裁定的待定死亡（裁定后从这里消失）。</summary>
    public required DeferredDeathDto[] Deferred { get; init; }
}
