namespace OpenClockTower.Kernel;

/// <summary>
/// 「这次能力是否正常生效」的判定结论（失效账本的记录面，R-0004）。
/// </summary>
/// <remarks>
/// <para>
/// 判定只看来源自己的三个维度：存活、清醒、健康（百科《重要细节》三-3）。
/// 判定不出来的情形（维度未观测）由调用方显式拒绝，不用本类型表达——**不猜**（D-0015）。
/// </para>
/// <para>
/// 一次结算可以同时命中多条失效路径（R-0004）：<see cref="Malfunctions"/> 是列表，不做单分类硬塞；
/// 涡流等「来源自身状态之外」的干扰由能力契约另行声明（<see cref="IAbilityResolution.InterferenceMalfunctions"/>）。
/// </para>
/// </remarks>
public sealed record AbilityOutcome
{
    /// <summary>这次使用是否正常生效。</summary>
    public required bool Effective { get; init; }

    /// <summary>因来源自身状态（中毒 / 醉酒）导致的失效分类；正常生效时为空。可并列多条。</summary>
    public IReadOnlyList<MalfunctionKind> Malfunctions { get; init; } = [];

    /// <summary>说明：分类表达不了的组合写在这里，不许丢。</summary>
    public string? Note { get; init; }
}
