namespace OpenClockTower.Kernel;

/// <summary>
/// 「此刻应当存在的一条持续型效果」：常驻效果对账的期望项。
/// </summary>
/// <remarks>
/// 它只描述**规则算出来应该是什么**，不描述现状；对账由 <see cref="SettlementReconciler"/> 完成——
/// 缺的补上、多的终止、维度跟着重算。<see cref="Id"/> 必须稳定（同样局面必得同一个标识），
/// 否则重放与撤销会对不上（D-0008）。
/// </remarks>
public sealed record StandingEffectExpectation
{
    /// <summary>效果标识（由来源 / 能力 / 目标派生，稳定）。</summary>
    public required EffectId Id { get; init; }

    /// <summary>产生这条效果的能力。</summary>
    public required AbilityId Ability { get; init; }

    /// <summary>施加者席位。</summary>
    public required SeatId Source { get; init; }

    /// <summary>施加时的来源角色（"换角色即失去能力"的判据，二-7）。</summary>
    public required CharacterId SourceCharacter { get; init; }

    /// <summary>作用对象席位。</summary>
    public required SeatId Target { get; init; }

    /// <summary>这条效果压制哪个维度；null = 不压制维度（如保护类效果）。</summary>
    public EffectDimension? Dimension { get; init; }
}
