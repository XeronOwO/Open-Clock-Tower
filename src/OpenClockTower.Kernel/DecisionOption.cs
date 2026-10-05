namespace OpenClockTower.Kernel;

/// <summary>
/// 裁定点的一个合法选项：说书人可以选什么，以及选它会发生什么。
/// </summary>
/// <remarks>
/// 依据 <c>docs/architecture/current.md</c> §2.4：选项由引擎算出并校验过；
/// 预览由内核**试算**得出，不改变真实状态。
/// </remarks>
public sealed record DecisionOption
{
    /// <summary>选项的稳定值（进事件流，如 <c>seat:3</c>）。</summary>
    public required string Value { get; init; }

    /// <summary>该选项的后果预览（人类可读，给说书人端；不改真实状态）。</summary>
    public required string Preview { get; init; }

    /// <summary>
    /// 该选项的**真值**（信息类候选才有）：说书人挑它当一条信息时，这句话此刻是对是错。
    /// null = 这个选项与真假无关（普通候选，如"选谁当目标"）。
    /// </summary>
    /// <remarks>
    /// 口径见 <c>docs/standard/rulings.md</c> R-0057-C：真值由规则层的候选事实库按状态账求值，
    /// **判不了的候选根本不进候选集合**（不猜，D-0015），因此这里要么有值、要么不适用。
    /// </remarks>
    public OptionTruth? Truth { get; init; }

    /// <summary>
    /// 候选所属分组（信息类候选才有，如「座位关系」）：说书人端按它分栏，不做一条大列表。
    /// null = 不分组。
    /// </summary>
    public string? Group { get; init; }

    /// <summary>候选徽章（信息类候选才有，如「高强度」）；按顺序显示，不进答案值。</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}
