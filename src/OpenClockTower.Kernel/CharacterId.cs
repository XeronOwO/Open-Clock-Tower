namespace OpenClockTower.Kernel;

/// <summary>
/// 角色标识。用英文 slug 承载；中文名只出现在界面与文档里。
/// </summary>
/// <remarks>
/// 依据：<c>docs/standard/terminology.md</c> §1 选词规则——代码标识符一律用英文 slug，
/// 禁止拼音、禁止中文转写。中文名与 slug 的对照表在术语表里维护。
/// </remarks>
/// <param name="Value">角色的英文 slug，例如 <c>snake-charmer</c>。</param>
public readonly record struct CharacterId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => Value;
}
