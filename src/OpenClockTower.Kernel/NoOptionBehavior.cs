namespace OpenClockTower.Kernel;

/// <summary>
/// 裁定点**没有合法选项**时的行为。每个裁定点必须显式声明其中之一——禁止抛异常，禁止静默跳过。
/// </summary>
/// <remarks>
/// 依据 <c>docs/standard/rulings.md</c> R-0009：某些裁量点在极端局面下可能没有合法选项
/// （例如占卜师的干扰项要求「任意善良玩家」，而场上可能没有善良玩家）。逐点登记，未登记视为未完成。
/// </remarks>
public enum NoOptionBehavior
{
    /// <summary>跳过这一步，继续结算。</summary>
    Skip,

    /// <summary>由说书人自由决定。</summary>
    StorytellerDecides,

    /// <summary>视为阻塞并报警，等说书人处理。</summary>
    BlockAndAlert,
}
