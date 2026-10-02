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

    /// <summary>
    /// 可选的第二维合法选项：为空 = 单维选择（现状）。两维时答案编码为
    /// <c>{第一维}|{第二维}</c>（如 <c>seat:3|clockmaker</c>），两维必须同时给全——
    /// 依据 <c>docs/standard/rulings.md</c> R-0021：洗脑师的一次行动是（玩家 × 善良角色）的原子选择，
    /// 拆成两次请求会引入"答了一半"的挂起状态，重放与作废语义都要跟着改。
    /// </summary>
    public IReadOnlyList<DecisionOption> SecondaryOptions { get; init; } = [];

    /// <summary>无合法选项时的行为；必填（R-0009）。</summary>
    public required NoOptionBehavior OnNoOption { get; init; }

    /// <summary>是否存在合法选项。</summary>
    public bool HasOptions => Options.Count > 0;

    /// <summary>是否需要两维选择。</summary>
    public bool HasSecondDimension => SecondaryOptions.Count > 0;

    /// <summary>
    /// 答案是否是本提示的合法选择：单维按值精确匹配；两维按 <c>|</c> 拆开逐维匹配，
    /// 缺一维 / 多一维 / 任一侧非法都算非法（R-0021：不可只完成一维）。
    /// </summary>
    /// <param name="value">玩家提交的答案值。</param>
    public bool IsLegalAnswer(string? value)
    {
        if (!TrySplitAnswer(value, out var primary, out var secondary))
        {
            return false;
        }

        if (!Options.Any(option => string.Equals(option.Value, primary, StringComparison.Ordinal)))
        {
            return false;
        }

        return HasSecondDimension
            ? SecondaryOptions.Any(option => string.Equals(option.Value, secondary, StringComparison.Ordinal))
            : secondary.Length == 0;
    }

    /// <summary>
    /// 按两维编码拆开答案：首个 <c>|</c> 之前是第一维、之后是第二维，两侧都必须非空；
    /// 不含 <c>|</c> 时整串即第一维、第二维为空串。**只拆形状、不判合法性**（合法性看
    /// <see cref="IsLegalAnswer"/>）——规则层解析答案时共用这一份拆分规则，避免两处各写一份。
    /// </summary>
    /// <param name="value">玩家提交的答案值。</param>
    /// <param name="primary">第一维取值。</param>
    /// <param name="secondary">第二维取值；单维答案时为空串。</param>
    public static bool TrySplitAnswer(string? value, out string primary, out string secondary)
    {
        primary = string.Empty;
        secondary = string.Empty;
        if (value is null)
        {
            return false;
        }

        var separator = value.IndexOf('|', StringComparison.Ordinal);
        if (separator < 0)
        {
            primary = value;
            return value.Length > 0;
        }

        if (separator == 0 || separator == value.Length - 1)
        {
            return false;
        }

        primary = value[..separator];
        secondary = value[(separator + 1)..];
        return true;
    }

    /// <summary>把两维选择编码成答案值（产生方与消费方共用同一条编码）。</summary>
    /// <param name="primary">第一维的值。</param>
    /// <param name="secondary">第二维的值。</param>
    public static string FormatAnswer(string primary, string secondary)
    {
        ArgumentException.ThrowIfNullOrEmpty(primary);
        ArgumentException.ThrowIfNullOrEmpty(secondary);
        return $"{primary}|{secondary}";
    }

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
