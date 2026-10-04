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
    /// <summary>已观测到的席位账目，按席位号升序；离场席位不在其中（见 <see cref="DepartedSeats"/>）。</summary>
    public IReadOnlyList<SeatStateEntry> Seats { get; init; } = [];

    /// <summary>
    /// 已离场的席位（按离场顺序）：席位与票据仍保留在会话信息里，但不再计入任何「人数」口径
    /// （流放分母、胜负、投票与收票顺序），也不进公开生死面（`rulings.md` R-0044 第 6 条）。
    /// </summary>
    public IReadOnlyList<SeatId> DepartedSeats { get; init; } = [];

    /// <summary>持续型效果（含已终止的），按施加顺序。</summary>
    public IReadOnlyList<PersistentEffect> PersistentEffects { get; init; } = [];

    /// <summary>即时型效果（已生效即不回滚），按发生顺序。</summary>
    public IReadOnlyList<InstantaneousEffect> InstantaneousEffects { get; init; } = [];

    /// <summary>能力使用账本：用过没有、生效过没有，两件事分开记（架构 §2.2）。</summary>
    public AbilityUseLedger AbilityUses { get; init; } = new();

    /// <summary>失效账本：每次「能力未正常生效」及原因分类（R-0004）。</summary>
    public MalfunctionLedger Malfunctions { get; init; } = new();

    /// <summary>还没有观测到任何东西的空账。</summary>
    public static GameState Empty { get; } = new();

    /// <summary>取某个席位的账目；该席位一个维度都没被观测过时返回 null。</summary>
    public SeatStateEntry? Seat(SeatId seat) => Seats.FirstOrDefault(entry => entry.Seat == seat);

    /// <summary>该席位是否已经离场（离场者不在席位账里，也不再计任何人数口径）。</summary>
    public bool HasDeparted(SeatId seat) => DepartedSeats.Contains(seat);

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
    /// 来源状态还没观测齐的效果不进结果——无法判定不等于生效，见 <see cref="IsOperative(PersistentEffect)"/>。
    /// </summary>
    public IReadOnlyList<PersistentEffect> OperativeEffectsOn(SeatId target) =>
        [.. LiveEffectsOn(target).Where(effect => IsOperative(effect) == true)];

    /// <summary>
    /// 某个席位上是否有一个**生效中**的指定窗口（咖啡师的效果 1 / 2，R-0047 / R-0052）。
    /// </summary>
    /// <returns>
    /// true = 至少一条窗口效果生效；false = 没有窗口，或窗口全部确定不生效；
    /// null = 有窗口，但生效与否判定不了（来源的生死 / 醉酒 / 中毒还没观测齐）——**不猜**。
    /// </returns>
    /// <remarks>
    /// 多条窗口并存（同一夜重复施加 / 不同来源）时，只要有一条判定为生效即为生效：窗口是
    /// 「能力存续」的表达，不因另一条窗口判定不了而被拖成"不知道"（R-0052 第 1 条）。
    /// </remarks>
    public bool? WindowOn(SeatId target, EffectWindowKind kind)
    {
        var unknown = false;
        foreach (var effect in LiveEffectsOn(target))
        {
            // 只认「纯窗口」效果：窗口与维度压制是两件事，本族自己不带 Dimension
            // （同时带两者的混合效果不是本模型的表达；把它排除在外也避免了生效判定的自指）。
            if (effect.Window != kind || effect.Dimension is not null)
            {
                continue;
            }

            switch (IsOperative(effect))
            {
                case true:
                    return true;
                case null:
                    unknown = true;
                    break;
                default:
                    break;
            }
        }

        return unknown ? null : false;
    }

    /// <summary>
    /// 一条持续型效果当前是否生效。
    /// 返回 null = 来源的生死 / 醉酒 / 中毒还没观测齐，**无法判定**——不做任何默认假设。
    /// </summary>
    /// <param name="effect">待判定的效果。</param>
    public bool? IsOperative(PersistentEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        // 目标免疫（咖啡师效果 1，R-0047 第 1 条）：窗口存续期间，目标身上**压制维度**的效果
        // 一律挂起——标记照记、暂不生效；窗口结束且效果仍在时按同一 EffectId 恢复（第 3 条）。
        // 排在来源判定之前：窗口判定不了时同样返回 null，不猜。
        if (effect.Dimension is not null)
        {
            switch (WindowOn(effect.Target, EffectWindowKind.AfflictionImmunity))
            {
                case true:
                    return false;
                case null:
                    return null;
                default:
                    break;
            }
        }

        // 与来源状态无关的效果（R-0031）：只看终止与否——来源维度没观测齐也不影响这条判定。
        if (effect.SourceStateIndependent)
        {
            return !effect.IsTerminated;
        }

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

    /// <summary>作用在某席位上的疯狂要求（含已撤下的），按写入顺序。</summary>
    public IReadOnlyList<MadnessRequirement> RequirementsOn(SeatId target) =>
        Seat(target)?.Madnesses ?? Array.Empty<MadnessRequirement>();

    /// <summary>作用在某席位上、尚未撤下的疯狂要求（R-0021 的存续窗口内）。</summary>
    public IReadOnlyList<MadnessRequirement> LiveRequirementsOn(SeatId target) =>
        [.. RequirementsOn(target).Where(requirement => !requirement.IsTerminated)];

    /// <summary>账上尚未撤下的全部疯狂要求（到期 / 来源失效时的收口对象）。</summary>
    public IReadOnlyList<MadnessRequirement> LiveRequirements =>
        [.. Seats.SelectMany(entry => entry.Madnesses).Where(requirement => !requirement.IsTerminated)];

    /// <summary>
    /// 一条疯狂要求当前是否生效：来源存活、未醉酒、未中毒（R-0012 的挂起口径）。
    /// 返回 null = 来源的生死 / 醉酒 / 中毒还没观测齐，**无法判定**——不做任何默认假设。
    /// </summary>
    /// <param name="requirement">待判定的要求。</param>
    public bool? IsOperative(MadnessRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        var source = Seat(requirement.Source);
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

        return !requirement.IsTerminated
            && life == LifeState.Alive
            && drunk == DrunkState.Sober
            && poison == PoisonState.Healthy;
    }
}
