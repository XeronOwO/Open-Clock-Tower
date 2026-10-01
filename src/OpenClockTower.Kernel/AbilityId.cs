namespace OpenClockTower.Kernel;

/// <summary>
/// 能力标识。首版用拥有该能力的角色 slug 承载（如 <c>snake-charmer</c>）。
/// </summary>
/// <remarks>
/// 「能力」与「角色」分开命名，是为「获得能力」类效果（如哲学家）留余地：
/// 使用与生效记录挂在能力上，而不是挂在角色上。术语见
/// <c>docs/standard/terminology.md</c> §7。
/// </remarks>
/// <param name="Value">能力 slug。</param>
public readonly record struct AbilityId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => Value;
}
