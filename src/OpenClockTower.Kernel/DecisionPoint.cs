namespace OpenClockTower.Kernel;

/// <summary>
/// 裁定点：面向**说书人**的投影——引擎算出「此处需要说书人决定」，给出合法选项与后果预览。
/// </summary>
/// <remarks>
/// <para>
/// 依据 D-0002（记录 + 校验 + 推演）与 <c>docs/architecture/current.md</c> §2.4：
/// 角色实现**禁止**自己选目标、自己摇随机数、自己决定真假信息——一律走裁定点。
/// </para>
/// <para>
/// 它与 <see cref="OperationRequest"/> 同源于 <see cref="ChoicePrompt"/>：同一个原语，
/// 两套投影（受众不同、投递方式不同）。无合法选项时的行为由 <see cref="ChoicePrompt.OnNoOption"/>
/// 显式声明（R-0009），禁止抛异常、禁止静默跳过。
/// </para>
/// </remarks>
public sealed record DecisionPoint
{
    /// <summary>稳定标识，进事件流后永不改变。</summary>
    public required DecisionPointId Id { get; init; }

    /// <summary>同源原语：上下文、合法选项与无合法选项时的行为。</summary>
    public required ChoicePrompt Prompt { get; init; }

    /// <summary>是否存在合法选项。</summary>
    public bool HasOptions => Prompt.HasOptions;

    /// <summary>求当前去向：有选项 → 等说书人；无选项 → 按声明走，**不抛异常**（R-0009）。</summary>
    public DecisionPointOutcome Evaluate() => Prompt.Evaluate();
}
