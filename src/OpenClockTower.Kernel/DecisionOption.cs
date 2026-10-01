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
}
