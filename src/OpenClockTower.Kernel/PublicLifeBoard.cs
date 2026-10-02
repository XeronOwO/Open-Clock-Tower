namespace OpenClockTower.Kernel;

/// <summary>
/// 公开生死面：全体席位的对外可见生死 + 本日生死公告（城镇广场生命标记与黎明公告的等价物）。
/// </summary>
/// <remarks>
/// <para>
/// 依据 <c>docs/standard/rulings.md</c> R-0022：公开面只含"生死状态 + 生死变化"，不含死因、能力与账；
/// 夜晚变化累积到黎明、按"相对黄昏的净变化"公告；白天变化即时公告；未公告的变化对玩家不可见
/// （含本人——"已死亡但未得知死讯"是真实存在的状态）。
/// </para>
/// <para>
/// <see cref="Pending"/> / <see cref="AtDusk"/> / <see cref="InNight"/> / <see cref="DayNumber"/> 是折叠的中间态，
/// 只服务 <see cref="PublicLifeBoardFolder"/>，**不投影给玩家**；投影只取 <see cref="Lives"/> 与 <see cref="Announcements"/>。
/// 本面只描述"对外公开了什么"，不改变真实生死账（D-0015：真实生死仍由 <see cref="GameStateMachine"/> 折叠）。
/// </para>
/// </remarks>
public sealed record PublicLifeBoard
{
    /// <summary>空面：还没观测到任何生死，也没有任何公告。</summary>
    public static PublicLifeBoard Empty { get; } = new()
    {
        Lives = [],
        Announcements = [],
        Pending = new Dictionary<SeatId, LifeState>(),
        AtDusk = new Dictionary<SeatId, LifeState>(),
        InNight = false,
        DayNumber = null,
        PublicRevision = 0,
    };

    /// <summary>对外可见生死，按席位号升序；未观测到的席位不出现（不猜）。</summary>
    public required IReadOnlyList<PublicLifeEntry> Lives { get; init; }

    /// <summary>本日已公告的生死变化（黎明批次按席位升序 + 白天即时按发生顺序）。</summary>
    public required IReadOnlyList<PublicLifeEntry> Announcements { get; init; }

    /// <summary>夜间累积的最新生死（尚未公告）；不在其中 = 本夜没有变化。</summary>
    public required IReadOnlyDictionary<SeatId, LifeState> Pending { get; init; }

    /// <summary>本夜黄昏时的对外可见生死（"相对黄昏"的比较基准）。</summary>
    public required IReadOnlyDictionary<SeatId, LifeState> AtDusk { get; init; }

    /// <summary>当前是否处于夜晚（黄昏之后、黎明之前）。</summary>
    public required bool InNight { get; init; }

    /// <summary>当前白天序号；首个黎明之前为 null（此时变化只进牌面、不公告）。</summary>
    public required int? DayNumber { get; init; }

    /// <summary>
    /// 公开投影（<see cref="Lives"/> + <see cref="Announcements"/>）的版本号：每次**对外可观察**的变化 +1；
    /// 首个黎明前只补折叠态（还没有白天投影可下发），因此不动版本号。
    /// </summary>
    public required long PublicRevision { get; init; }
}
