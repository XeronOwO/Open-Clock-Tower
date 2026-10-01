namespace OpenClockTower.Kernel;

/// <summary>
/// 「这次能力是否正常生效」的判定结论（失效账本的记录面，R-0004）。
/// </summary>
/// <remarks>
/// 判定只看来源自己的三个维度：存活、清醒、健康（百科《重要细节》三-3）。
/// 判定不出来的情形（维度未观测）由调用方显式拒绝，不用本类型表达——**不猜**（D-0015）。
/// </remarks>
public sealed record AbilityOutcome
{
    /// <summary>这次使用是否正常生效。</summary>
    public required bool Effective { get; init; }

    /// <summary>未正常生效时的原因分类；生效时为 null。</summary>
    public MalfunctionKind? Malfunction { get; init; }

    /// <summary>说明：分类无法表达的组合（如「同时中毒且醉酒」）写在这里，不许丢。</summary>
    public string? Note { get; init; }
}
