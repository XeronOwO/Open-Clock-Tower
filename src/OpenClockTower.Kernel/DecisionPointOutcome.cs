namespace OpenClockTower.Kernel;

/// <summary>
/// 裁定点的当前去向：有合法选项 → 等说书人；没有 → 按声明的 <see cref="NoOptionBehavior"/> 走。
/// </summary>
public enum DecisionPointOutcome
{
    /// <summary>有合法选项，等待说书人裁定。</summary>
    AwaitingChoice,

    /// <summary>无合法选项且声明为跳过。</summary>
    Skipped,

    /// <summary>无合法选项且声明为说书人自由决定。</summary>
    StorytellerDecides,

    /// <summary>无合法选项且声明为阻塞报警。</summary>
    BlockedAndAlerted,
}
