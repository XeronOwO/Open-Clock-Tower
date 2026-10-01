namespace OpenClockTower.Kernel;

/// <summary>
/// 状态账里的一条事实：某个维度**当前已知的值**，以及它是怎么来的。
/// </summary>
/// <remarks>
/// <para>
/// 说书人上帝视角要回答的是"这一步为什么是这样"，所以只记值不够——必须能指到原因：
/// 谁导致的、因为哪个能力 / 哪个效果 / 哪次裁定。值与其归因同时写入、同时被覆盖。
/// </para>
/// <para>
/// 这是**已观测事实**，不是推演结论（D-0002）：平台只记下报上来的值，
/// 不替上游补它没观测的维度。未观测的维度在账里保持为空，直到有人报上来。
/// </para>
/// </remarks>
/// <typeparam name="T">维度取值类型（如 <see cref="LifeState"/>）。</typeparam>
public sealed record StateFact<T>
{
    /// <summary>当前已知的值。</summary>
    public required T Value { get; init; }

    /// <summary>这条事实的来由（哪个能力 / 哪个效果 / 人工修正）。</summary>
    public required string Reason { get; init; }

    /// <summary>导致这条事实的席位；说书人直接修正等场景可为空。</summary>
    public SeatId? CausedBy { get; init; }

    /// <summary>
    /// 支撑这条事实的持续型效果；null = 没有效果链接（说书人上报 / 开局分配等）。
    /// 有了它，面板才能回答「这一格中毒是哪条效果造成的」，并在来源效果终止后提示
    /// 「效果已终止、维度还没解除」（票据第 6 条）。
    /// </summary>
    public EffectId? EffectId { get; init; }
}
