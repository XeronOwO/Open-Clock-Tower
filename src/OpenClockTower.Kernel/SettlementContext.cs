namespace OpenClockTower.Kernel;

/// <summary>
/// 结算管线的一次输入：账 + 座次 + 规则层契约目录 + 常驻效果来源 + 事件触发器 + 能力存续契约。
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

    /// <summary>事件触发器（规则层实现）；没有就不产出任何触发后果。</summary>
    public IReadOnlyList<IEventTrigger> EventTriggers { get; init; } = [];

    /// <summary>能力存续契约（规则层实现）；没有就不解除任何「能力已失去」的效果。</summary>
    public IReadOnlyList<IAbilityPresence> AbilityPresences { get; init; } = [];

    /// <summary>处罚处决依据契约（规则层实现）；没有时任何处罚处决都会被显式拒绝。</summary>
    public IReadOnlyList<IAdjudicatedExecutionSource> AdjudicatedExecutions { get; init; } = [];

    /// <summary>
    /// 步骤机状态（可选）：给"需要读游戏流程状态"的触发器用（例如呆瓜选择的幂等判断，R-0027）。
    /// 触发管线只读它、不写它；派生事件折回步骤机由应用层完成。
    /// </summary>
    public StepMachineState? Machine { get; init; }

    /// <summary>
    /// 本批命令**之前**白天是否开着（R-0027 判断"白天死亡即时公告"的输入）：
    /// 白天开着 → 本批的死亡随提交即时公开；否则要等下一个黎明（R-0022 第 2 条）。
    /// </summary>
    public bool DayWasOpen { get; init; }

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
