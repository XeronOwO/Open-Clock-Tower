namespace OpenClockTower.Kernel;

/// <summary>
/// 能力使用账本：追加式记录每次使用，回答两个**相互独立**的问题——用过没有、生效过没有。
/// </summary>
/// <remarks>
/// 「使用机会被浪费」的语义由此表达：<see cref="WasUsed"/> 为 true 的能力不得再次使用，
/// 哪怕它当初 <see cref="WasEffective"/> 为 false（醉酒/中毒期间使用即被浪费，恢复后同样不可再用）。
/// 依据：百科《重要细节》三-3。
/// </remarks>
public sealed record AbilityUseLedger
{
    /// <summary>全部使用记录，按发生顺序追加。</summary>
    public IReadOnlyList<AbilityUse> Entries { get; init; } = [];

    /// <summary>记录一次使用；<paramref name="effective"/> 表示这次使用是否正常生效。</summary>
    public AbilityUseLedger RecordUse(SeatId seat, AbilityId ability, bool effective) =>
        this with
        {
            Entries = [.. Entries, new AbilityUse { Seat = seat, Ability = ability, Effective = effective }],
        };

    /// <summary>该席位的该能力是否**被使用过**（无论是否生效）。</summary>
    public bool WasUsed(SeatId seat, AbilityId ability) =>
        Entries.Any(entry => entry.Seat == seat && entry.Ability == ability);

    /// <summary>该席位的该能力是否**至少正常生效过一次**。</summary>
    public bool WasEffective(SeatId seat, AbilityId ability) =>
        Entries.Any(entry => entry.Seat == seat && entry.Ability == ability && entry.Effective);
}
