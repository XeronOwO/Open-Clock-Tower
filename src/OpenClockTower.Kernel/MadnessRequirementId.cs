namespace OpenClockTower.Kernel;

/// <summary>
/// 疯狂要求的稳定标识：进事件流后永不变，用于「撤下的是哪一条要求」的认人。
/// </summary>
/// <param name="Value">要求 slug（如 <c>sv:night-2:cerenovus:madness</c>）。</param>
public readonly record struct MadnessRequirementId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => Value;
}
