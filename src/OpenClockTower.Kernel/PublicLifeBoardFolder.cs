namespace OpenClockTower.Kernel;

/// <summary>
/// 公开生死面的确定性折叠：从事件流推演"玩家此刻已经被告知了什么"。
/// </summary>
/// <remarks>
/// <para>
/// 依据 <c>docs/standard/rulings.md</c> R-0022：夜晚（黄昏之后）的生死变化累积到黎明，
/// 在 <see cref="DayStartedEvent"/> 按"相对黄昏的净变化"一次性公告（死而复生者不公告、不留痕）；
/// 白天（黎明之后）的变化即时公告；首个黎明之前的观测只补折叠态、不公告（还没有"公告"这个时点）。
/// **黄昏边界**以 <see cref="DayClosedEvent"/>（白天关闭）为界：白天关闭到夜晚计划开始之间的变化
/// 按夜晚形态累积到下一个黎明（R-0022 第 2 条登记的黄昏边界，与 R-0020 第 6 条间隙窗口同族）。
/// </para>
/// <para>
/// 与其它 folder 同一姿态——纯计算、无 IO / 时间 / 随机（D-0008），重放得到同一结果；
/// 只产出公开面，不改变真实生死账（D-0015），也不替任何玩家推断死因。
/// </para>
/// </remarks>
public static class PublicLifeBoardFolder
{
    /// <summary>把一条事件折进公开生死面；无关事件原样返回。</summary>
    public static PublicLifeBoard Apply(PublicLifeBoard board, GameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(gameEvent);

        return gameEvent switch
        {
            SeatStateChangedEvent { Life: { } life } changed => ApplyLife(board, changed.Seat, life),

            // 旅行者离场：生命标记从城镇广场撤下（百科《旅行者》· 离开流程）——公开面不留幽灵席位。
            TravellerDepartedEvent departed => RemoveSeat(board, departed.Seat),

            DayClosedEvent => EnterNight(board),
            PhaseStartedEvent { Plan.Phase: GamePhase.FirstNight or GamePhase.OtherNight } => EnterNight(board),
            DayStartedEvent started => Dawn(board, started.DayNumber),
            _ => board,
        };
    }

    /// <summary>按顺序折叠一批事件。</summary>
    public static PublicLifeBoard ApplyAll(PublicLifeBoard board, IEnumerable<GameEvent> events)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(events);

        var next = board;
        foreach (var gameEvent in events)
        {
            next = Apply(next, gameEvent);
        }

        return next;
    }

    private static PublicLifeBoard ApplyLife(PublicLifeBoard board, SeatId seat, LifeState life)
    {
        if (board.InNight)
        {
            // 夜晚的变化只累积：黎明按"相对黄昏的净变化"统一公告（R-0022 第 2 条）。
            return board with { Pending = Upsert(board.Pending, seat, life) };
        }

        if (CurrentLife(board.Lives, seat) == life)
        {
            return board; // 同值重报：不上屏、不公告（幂等）。
        }

        var lives = Upsert(board.Lives, seat, life);
        if (board.DayNumber is null)
        {
            // 首个黎明之前：只补折叠态——此时玩家端还没有白天投影（Day = null），
            // 补观测不构成"对外可观察的变化"，因此不动公开版本号（否则会产出进不了任何连接的推送通知）。
            return board with { Lives = lives };
        }

        return board with
        {
            Lives = lives,
            Announcements = [.. board.Announcements, new PublicLifeEntry { Seat = seat, State = life }],
            PublicRevision = board.PublicRevision + 1,
        };
    }

    /// <summary>
    /// 旅行者离场：把该席位从公开生死面移除（生命标记从城镇广场撤下；百科《旅行者》· 离开流程）。
    /// 有变化才动公开版本号——没有它就没有"对外可观察的变化"。
    /// </summary>
    private static PublicLifeBoard RemoveSeat(PublicLifeBoard board, SeatId seat)
    {
        var present = board.Lives.Any(entry => entry.Seat == seat)
            || board.Pending.ContainsKey(seat)
            || board.AtDusk.ContainsKey(seat);
        if (!present)
        {
            return board;
        }

        return board with
        {
            Lives = [.. board.Lives.Where(entry => entry.Seat != seat)],
            Pending = board.Pending
                .Where(entry => entry.Key != seat)
                .ToDictionary(entry => entry.Key, entry => entry.Value),
            AtDusk = board.AtDusk
                .Where(entry => entry.Key != seat)
                .ToDictionary(entry => entry.Key, entry => entry.Value),
            PublicRevision = board.PublicRevision + 1,
        };
    }

    private static PublicLifeBoard EnterNight(PublicLifeBoard board)
    {
        if (board.InNight)
        {
            return board; // 黄昏与开夜相邻到来：只认第一次，保留同一份基准。
        }

        return board with
        {
            InNight = true,
            AtDusk = board.Lives.ToDictionary(entry => entry.Seat, entry => entry.State),
            Pending = new Dictionary<SeatId, LifeState>(),
        };
    }

    private static PublicLifeBoard Dawn(PublicLifeBoard board, int dayNumber)
    {
        var lives = board.Lives;
        var announcements = new List<PublicLifeEntry>();
        foreach (var (seat, life) in board.Pending)
        {
            if (CurrentLife(lives, seat) == life)
            {
                continue; // 与黄昏相同：夜里死而复生（或同值重报）不公告、不留痕。
            }

            lives = Upsert(lives, seat, life);
            if (board.AtDusk.TryGetValue(seat, out var atDusk) && atDusk != life)
            {
                // 有黄昏基准才能断言"相对黄昏发生了变化"；没有基准的首次观测只补牌面（不猜）。
                announcements.Add(new PublicLifeEntry { Seat = seat, State = life });
            }
        }

        // 黎明公告是同一批事实：按席位升序固定顺序，避免字典枚举顺序影响投影。
        announcements.Sort((left, right) => left.Seat.Value.CompareTo(right.Seat.Value));

        var changed = !Same(lives, board.Lives) || !Same(announcements, board.Announcements);
        return board with
        {
            Lives = lives,
            Announcements = announcements,
            Pending = new Dictionary<SeatId, LifeState>(),
            AtDusk = new Dictionary<SeatId, LifeState>(),
            InNight = false,
            DayNumber = dayNumber,
            PublicRevision = board.PublicRevision + (changed ? 1 : 0),
        };
    }

    private static LifeState? CurrentLife(IReadOnlyList<PublicLifeEntry> lives, SeatId seat) =>
        lives.FirstOrDefault(entry => entry.Seat == seat)?.State;

    private static bool Same(IReadOnlyList<PublicLifeEntry> left, IReadOnlyList<PublicLifeEntry> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<PublicLifeEntry> Upsert(
        IReadOnlyList<PublicLifeEntry> lives,
        SeatId seat,
        LifeState life) =>
        [.. lives
            .Where(entry => entry.Seat != seat)
            .Append(new PublicLifeEntry { Seat = seat, State = life })
            .OrderBy(entry => entry.Seat.Value)];

    private static IReadOnlyDictionary<SeatId, LifeState> Upsert(
        IReadOnlyDictionary<SeatId, LifeState> values,
        SeatId seat,
        LifeState life) =>
        new Dictionary<SeatId, LifeState>(values) { [seat] = life };
}
