namespace OpenClockTower.Kernel;

/// <summary>
/// 同源内核原语：**有人必须做一个选择**。
/// </summary>
/// <remarks>
/// <para>
/// 依据 D-0011 与 <c>docs/architecture/current.md</c> §2.7：说书人裁定点（<see cref="DecisionPoint"/>）
/// 与玩家操作请求（<see cref="OperationRequest"/>）是**同一个原语的两套投影**——
/// 受众与投递方式不同，而上下文、合法选项与「无合法选项时的行为」完全同源。
/// </para>
/// <para>
/// <see cref="OnNoOption"/> 必填：依据 <c>docs/standard/rulings.md</c> R-0009，
/// 无合法选项时禁止抛异常、禁止静默跳过，必须按声明的行为处理。
/// </para>
/// </remarks>
public sealed record ChoicePrompt
{
    /// <summary>为什么需要决定（涉及谁、哪个能力、哪一步）。</summary>
    public required string Context { get; init; }

    /// <summary>合法选项；由引擎算出并校验过。为空表示「无合法选项」。</summary>
    public required IReadOnlyList<DecisionOption> Options { get; init; }

    /// <summary>无合法选项时的行为；必填（R-0009）。</summary>
    public required NoOptionBehavior OnNoOption { get; init; }

    /// <summary>是否存在合法选项。</summary>
    public bool HasOptions => Options.Count > 0;

    /// <summary>
    /// 求当前去向：有选项 → 等对方选择；无选项 → 按 <see cref="OnNoOption"/> 走，**不抛异常**。
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
