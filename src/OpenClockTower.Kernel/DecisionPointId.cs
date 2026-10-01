namespace OpenClockTower.Kernel;

/// <summary>
/// 裁定点标识：稳定标识，进事件流后**永不改变**。
/// </summary>
/// <remarks>
/// 依据 <c>docs/architecture/current.md</c> §2.4 与 D-0002：裁定点是一等输入契约，
/// 因此可回放、可撤销、可回归测试。
/// </remarks>
/// <param name="Value">稳定标识（英文 slug，如 <c>snake-charmer-night-1-target</c>）。</param>
public readonly record struct DecisionPointId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => Value;
}
