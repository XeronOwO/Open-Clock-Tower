namespace OpenClockTower.Kernel;

/// <summary>
/// 结算管线的一次输入：账 + 座次 + 规则层契约目录 + 常驻效果来源。
/// </summary>
/// <remarks>
/// <para>
/// 状态账与步骤机是同一条事件流上的两个派生视图（D-0015）；结算既要读账（生效判定、效果链接），
/// 也要读座次（圆桌关系），还要按角色取契约——这三样打包成上下文，由应用层每次命令现算。
/// </para>
/// <para>
/// <see cref="SettlementContext.Empty"/> 给内核夹具用：没有契约、没有账，只会推进、不结算。
/// </para>
/// </remarks>
public sealed record SettlementContext
{
    /// <summary>当前状态账。</summary>
    public required GameState State { get; init; }

    /// <summary>本局完整座次（按座位号升序 = 圆桌顺序）。</summary>
    public required IReadOnlyList<SeatId> Seats { get; init; }

    /// <summary>按角色取能力结算契约的目录。</summary>
    public required IAbilityResolutionCatalog Abilities { get; init; }

    /// <summary>常驻效果来源；没有就只做账内的维度重算。</summary>
    public IReadOnlyList<IStandingEffectSource> StandingEffects { get; init; } = [];

    /// <summary>没有规则层契约的上下文（内核夹具 / 只推进不结算）。</summary>
    public static SettlementContext Empty { get; } = new()
    {
        State = GameState.Empty,
        Seats = [],
        Abilities = EmptyCatalog.Instance,
    };

    /// <summary>换一份账，其余不变（折完事件后继续结算用）。</summary>
    public SettlementContext WithState(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return this with { State = state };
    }

    private sealed class EmptyCatalog : IAbilityResolutionCatalog
    {
        internal static readonly EmptyCatalog Instance = new();

        public IAbilityResolution? Find(CharacterId character) => null;
    }
}
