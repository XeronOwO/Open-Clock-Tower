namespace OpenClockTower.Kernel;

/// <summary>
/// 裁定点：引擎算出「此处需要说书人决定」，给出合法选项与后果预览。
/// </summary>
/// <remarks>
/// <para>
/// 依据 D-0002（记录 + 校验 + 推演）与 <c>docs/architecture/current.md</c> §2.4：
/// 角色实现**禁止**自己选目标、自己摇随机数、自己决定真假信息——一律走裁定点。
/// </para>
/// <para>
/// <see cref="OnNoOption"/> 是**必填**的：R-0009 要求每个裁定点显式声明无合法选项时的行为。
/// </para>
/// </remarks>
public sealed record DecisionPoint
{
    /// <summary>稳定标识，进事件流后永不改变。</summary>
    public required DecisionPointId Id { get; init; }

    /// <summary>为什么需要决定（涉及谁、哪个能力、哪一步）。</summary>
    public required string Context { get; init; }

    /// <summary>合法选项；由引擎算出并校验过。为空表示「无合法选项」。</summary>
    public required IReadOnlyList<DecisionOption> Options { get; init; }

    /// <summary>无合法选项时的行为；必填（R-0009）。</summary>
    public required NoOptionBehavior OnNoOption { get; init; }

    /// <summary>是否存在合法选项。</summary>
    public bool HasOptions => Options.Count > 0;

    /// <summary>
    /// 求当前去向：有选项 → 等说书人；无选项 → 按 <see cref="OnNoOption"/> 走，**不抛异常**。
    /// 未知声明一律按阻塞处理：宁可报警，也不静默跳过（R-0009 禁止静默跳过）。
    /// </summary>
    public DecisionPointOutcome Evaluate() =>
        HasOptions
            ? DecisionPointOutcome.AwaitingChoice
            : OnNoOption switch
            {
                NoOptionBehavior.Skip => DecisionPointOutcome.Skipped,
                NoOptionBehavior.StorytellerDecides => DecisionPointOutcome.StorytellerDecides,
                NoOptionBehavior.BlockAndAlert => DecisionPointOutcome.BlockedAndAlerted,
                _ => DecisionPointOutcome.BlockedAndAlerted,
            };
}
