namespace OpenClockTower.Kernel;

/// <summary>
/// 麻脸巫婆之夜的死亡裁量窗口：创造了恶魔的那一晚，死亡由说书人决定。
/// </summary>
/// <remarks>
/// <para>
/// 依据：百科《麻脸巫婆》· 2026-10-01 抓取 · 规则细节 1——「麻脸巫婆造成死亡的时间范围为当前剧本中
/// 首个能够造成死亡的恶魔行动开始前，到最后一个能够造成死亡的恶魔行动结束后」；
/// 「只要麻脸巫婆创造了恶魔，即使在那之后麻脸巫婆离场、失去能力或死亡，当晚说书人仍然可以决定造成或阻止死亡」。
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0030。
/// </para>
/// <para>
/// 窗口**不随麻脸巫婆死亡而关闭**，只随「越过最后一个能造成死亡的恶魔行动槽位」关闭；
/// 关闭时仍未裁定的待定死亡按恶魔攻击的自然结果生效（R-0030 第 3 条，显式记录、不静默）。
/// </para>
/// </remarks>
public sealed record PitHagNight
{
    /// <summary>麻脸巫婆的席位（追加死亡的归因来源）。</summary>
    public required SeatId Source { get; init; }

    /// <summary>窗口关闭点：计划里最后一个「能造成死亡的恶魔行动」槽位下标。</summary>
    public required int ClosesAfterSlotIndex { get; init; }

    /// <summary>追加死亡的归因能力标识——由规则层给出，内核不硬编码任何角色 slug。</summary>
    public required AbilityId CasualtyAbility { get; init; }

    /// <summary>尚未裁定的待定死亡（裁定后从这里移除）。</summary>
    public required IReadOnlyList<DeferredDeath> Deferred { get; init; }
}
