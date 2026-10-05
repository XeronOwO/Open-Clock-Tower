namespace OpenClockTower.Rules;

/// <summary>两条候选的组合结论：受理与否 + 给说书人看的可读原因（R-0057-C 的 C1–C4）。</summary>
internal sealed record SavantCombinationVerdict
{
    /// <summary>受理吗。</summary>
    public required bool Allowed { get; init; }

    /// <summary>不受理的原因（受理时为 null）。</summary>
    public string? Reason { get; init; }
}
