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

    /// <summary>
    /// 候选**事实编码**（信息类候选才有，如 <c>demon-seat-parity</c>）：同编码、不同取值的两条候选
    /// 在措辞上互为对照。null = 不适用。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ExclusionGroup"/> 配对使用：前者回答"是不是同一条事实"，后者回答
    /// "同一条事实的不同取值算不算互为反面"。两个字段都由**规则层**填，内核只搬运呈现元数据。
    /// </remarks>
    public string? Code { get; init; }

    /// <summary>
    /// 取值互斥组（信息类候选才有，如奇 / 偶两条事实共用一个组名）：同 <see cref="Code"/> 且组名非空的两条
    /// 候选**必然一真一假**——等于只给了一条信息，因此这一对不能同时入选（R-0057-C 第 3 条 C4）。
    /// null = 各取值彼此独立（如"3 号是邪恶"与"5 号是邪恶"）。
    /// </summary>
    /// <remarks>
    /// 服务端在提交时按它拒绝（权威判定）；说书人端据它在候选上**预先灰掉**那一条并写明原因，
    /// 省一次注定被拒的往返（前端不做规则判断，web/AGENTS.md §4）。
    /// </remarks>
    public string? ExclusionGroup { get; init; }

    /// <summary>候选徽章（信息类候选才有，如「高强度」）；按顺序显示，不进答案值。</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}
