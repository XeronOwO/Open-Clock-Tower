namespace OpenClockTower.Kernel;

/// <summary>
/// 步骤槽位标识：同一计划内稳定唯一，进事件流后永不改变。
/// </summary>
/// <param name="Value">稳定标识（英文 slug，如 <c>snake-charmer</c>）。</param>
public readonly record struct StepSlotId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => Value;
}
