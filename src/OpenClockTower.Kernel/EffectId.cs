namespace OpenClockTower.Kernel;

/// <summary>
/// 效果标识：稳定且进事件流后永不变，用于追踪「是不是同一个效果」。
/// </summary>
/// <param name="Value">效果 slug（如 <c>snake-charmer-swap</c>）。</param>
public readonly record struct EffectId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => Value;
}
