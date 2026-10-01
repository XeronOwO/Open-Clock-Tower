namespace OpenClockTower.Kernel;

/// <summary>
/// 状态账：这一局**当前已知**的六维度状态、身上的效果与它们的归因（D-0002 的记录面）。
/// </summary>
/// <remarks>
/// <para>
/// 它不推演、不臆造：内容全部来自事件流折叠（<see cref="GameStateMachine"/>）。
/// 现阶段写入方是说书人上报，结算引擎落地后由引擎产出同样的事件（D-0010：事件是唯一事实来源）。
/// </para>
/// <para>
/// 顺序确定性（D-0008）：席位与效果一律用**有序列表**承载，不依赖字典枚举顺序——
/// 座位按席位号升序，效果按发生顺序。
/// </para>
/// </remarks>
public sealed record GameState
{
    /// <summary>已观测到的席位账目，按席位号升序。</summary>
    public IReadOnlyList<SeatStateEntry> Seats { get; init; } = [];

    /// <summary>持续型效果（含已终止的），按施加顺序。</summary>
    public IReadOnlyList<PersistentEffect> PersistentEffects { get; init; } = [];

    /// <summary>即时型效果（已生效即不回滚），按发生顺序。</summary>
    public IReadOnlyList<InstantaneousEffect> InstantaneousEffects { get; init; } = [];

    /// <summary>还没有观测到任何东西的空账。</summary>
    public static GameState Empty { get; } = new();

    /// <summary>取某个席位的账目；该席位一个维度都没被观测过时返回 null。</summary>
    public SeatStateEntry? Seat(SeatId seat) => Seats.FirstOrDefault(entry => entry.Seat == seat);

    /// <summary>席位五维度齐全时的完整状态；有维度未观测时返回 null（不猜）。</summary>
    public SeatState? KnownStateOf(SeatId seat) => Seat(seat)?.ToSeatState();

    /// <summary>作用在某席位上的全部持续型效果（含已终止）。</summary>
    public IReadOnlyList<PersistentEffect> EffectsOn(SeatId target) =>
        [.. PersistentEffects.Where(effect => effect.Target == target)];

    /// <summary>某个席位施加出去的全部持续型效果（回答"它正在影响谁"）。</summary>
    public IReadOnlyList<PersistentEffect> EffectsSourcedBy(SeatId source) =>
        [.. PersistentEffects.Where(effect => effect.Source == source)];

    /// <summary>作用在某席位上、尚未终止的持续型效果。</summary>
    public IReadOnlyList<PersistentEffect> LiveEffectsOn(SeatId target) =>
        [.. PersistentEffects.Where(effect => effect.Target == target && !effect.IsTerminated)];

    /// <summary>作用在某席位上的即时型效果。</summary>
    public IReadOnlyList<InstantaneousEffect> InstantaneousEffectsOn(SeatId target) =>
        [.. InstantaneousEffects.Where(effect => effect.Target == target)];

    /// <summary>
    /// 作用在某席位上、当前**确实生效**的持续型效果。
    /// 来源状态还没观测齐的效果不进结果——无法判定不等于生效，见 <see cref="IsOperative"/>。
    /// </summary>
    public IReadOnlyList<PersistentEffect> OperativeEffectsOn(SeatId target) =>
        [.. LiveEffectsOn(target).Where(effect => IsOperative(effect) == true)];

    /// <summary>
    /// 一条持续型效果当前是否生效。
    /// 返回 null = 来源的生死 / 醉酒 / 中毒还没观测齐，**无法判定**——不做任何默认假设。
    /// </summary>
    /// <param name="effect">待判定的效果。</param>
    public bool? IsOperative(PersistentEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        var source = Seat(effect.Source);
        if (source is null)
        {
            return null;
        }

        if (source.LifeValue is not { } life
            || source.DrunkValue is not { } drunk
            || source.PoisonValue is not { } poison)
        {
            return null;
        }

        return effect.IsOperative(life, drunk, poison);
    }
}
