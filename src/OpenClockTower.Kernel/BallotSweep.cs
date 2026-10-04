namespace OpenClockTower.Kernel;

/// <summary>
/// 钟盘收票的共享内核：开始 / 收票 / 继续的校验，与票面 / 举手名单的折叠原语（提名与流放同尺）。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="DayMachine"/> 与 <see cref="ExileMachine"/> 的共同需求抽出：严格顺序（只接受下一待收席位）、
/// 冻结（票面 + 举手名单）、继续（重新起倒计时）与钟盘占用判定。两条流程各自持有
/// <see cref="VoteSweepState"/>、各自的事件类型——这里只共享「怎么收」，不共享「算什么」。
/// </para>
/// <para>
/// 收票的严格时点与中断口径见 <c>docs/standard/rulings.md</c> R-0017；流放的表决语义见 R-0044
/// 第 4 / 5 / 10 条；钟盘串行（同一时刻至多一条未收完的收票）见票据「D2 实施口径」。
/// </para>
/// </remarks>
internal static class BallotSweep
{
    /// <summary>一条收票校验的拒绝：机器可读码 + 给人看的说明。</summary>
    internal readonly record struct Rejection(string Code, string Note);

    /// <summary>
    /// 校验「开始一条新收票」：还没开始过、节奏参数合法、座次快照非空。
    /// </summary>
    /// <param name="existingSweep">这条选票已有的收票状态（null = 还没开始）。</param>
    /// <param name="seats">本局在局座次（开始收票那一刻的名单）。</param>
    /// <param name="ballotLabel">定位文本，如「第 2 项提名」「第 1 条流放」（只进提示文案）。</param>
    internal static Rejection? CheckStart(
        VoteSweepState? existingSweep,
        IReadOnlyList<SeatId> seats,
        string ballotLabel,
        int countdownMilliseconds,
        int intervalMilliseconds)
    {
        if (existingSweep is not null)
        {
            return new Rejection("day.sweep_started", $"{ballotLabel}的收票已经开始过了");
        }

        if (!VoteSweepLimits.IsCountdownValid(countdownMilliseconds))
        {
            return new Rejection(
                "day.sweep_countdown_invalid",
                $"倒计时必须在 {VoteSweepLimits.MinCountdownMilliseconds}–{VoteSweepLimits.MaxCountdownMilliseconds} 毫秒之间");
        }

        if (!VoteSweepLimits.IsIntervalValid(intervalMilliseconds))
        {
            return new Rejection(
                "day.sweep_interval_invalid",
                $"逐席间隔必须在 {VoteSweepLimits.MinIntervalMilliseconds}–{VoteSweepLimits.MaxIntervalMilliseconds} 毫秒之间");
        }

        if (seats.Count == 0)
        {
            return new Rejection("day.no_seats", "本局座次还没有观测：无法确定收票顺序（不猜）");
        }

        return null;
    }

    /// <summary>校验「收下一席」：已开始、未收完，且正好是下一待收席位（顺序由内核校验）。</summary>
    internal static Rejection? CheckCollect(VoteSweepState? sweep, SeatId seat)
    {
        if (sweep is null)
        {
            return new Rejection("day.sweep_not_started", "收票还没有开始：先由说书人点「开始」");
        }

        if (sweep.NextSeat is not { } nextSeat)
        {
            return new Rejection("day.sweep_complete", "收票已经全部完成，不能再收");
        }

        if (nextSeat != seat)
        {
            return new Rejection(
                "day.seat_out_of_order",
                $"下一待收的是 {nextSeat.Value} 号席位，不是 {seat.Value} 号（收票必须按席位升序走完一圈）");
        }

        return null;
    }

    /// <summary>校验「继续收票」：已开始且未收完（中断后可重新起倒计时，从下一未收席位接着收）。</summary>
    internal static Rejection? CheckResume(VoteSweepState? sweep)
    {
        if (sweep is null)
        {
            return new Rejection("day.sweep_not_started", "收票还没有开始：没有可以继续的收票");
        }

        if (sweep.IsComplete)
        {
            return new Rejection("day.sweep_complete", "收票已经全部完成，不能再继续");
        }

        return null;
    }

    /// <summary>
    /// 校验钟盘空闲（D2 实施口径）：另一条收票**未收完**时不能再开 / 继续这一条。
    /// 收票已收完但未计票的选票不占钟盘——计票可以延后。
    /// </summary>
    /// <param name="day">当前白天账。</param>
    /// <param name="kind">要开始 / 继续的选票族。</param>
    /// <param name="index">要开始 / 继续的序号（自己占着钟盘时放行）。</param>
    internal static Rejection? CheckDialFree(DayRecord day, BallotKind kind, int index)
    {
        var active = day.ActiveBallot;
        if (active is null || (active.Kind == kind && active.Index == index))
        {
            return null;
        }

        return new Rejection(
            "day.ballot_in_progress",
            $"钟盘上还有没走完的收票（{active.Describe()}）：先把它收完，再开下一条");
    }

    /// <summary>收票顺序：按席位号升序（开始事件的快照形状，进事件流后稳定）。</summary>
    internal static IReadOnlyList<SeatId> OrderSeats(IReadOnlyList<SeatId> seats) =>
        [.. seats.OrderBy(seat => seat.Value)];

    /// <summary>收票顺序必须严格升序（折叠层校验用；开始事件本应由内核保证）。</summary>
    internal static bool IsStrictlyAscending(IReadOnlyList<SeatId> seats)
    {
        for (var position = 1; position < seats.Count; position++)
        {
            if (seats[position].Value <= seats[position - 1].Value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 把某席位在名单里置入 / 移除（保持按席位号升序）：票面、举手名单共用同一把尺子，
    /// 重复置入是幂等的（不会产生第二条）。
    /// </summary>
    internal static IReadOnlyList<SeatId> SetSeat(IReadOnlyList<SeatId> seats, SeatId seat, bool present)
    {
        if (!present)
        {
            return [.. seats.Where(item => item != seat)];
        }

        if (seats.Contains(seat))
        {
            return seats;
        }

        var result = seats.ToList();
        result.Add(seat);
        result.Sort((left, right) => left.Value.CompareTo(right.Value));
        return result;
    }

    /// <summary>
    /// 冻结一条逐席结论：举起的手进票面（先举也算），放下的手从举手名单移除；
    /// 未举手时票面不变（该席本就不在票面里）。折叠层与两条流程共用。
    /// </summary>
    internal static (IReadOnlyList<SeatId> Ballot, IReadOnlyList<SeatId> Hands) Freeze(
        IReadOnlyList<SeatId> ballot,
        IReadOnlyList<SeatId> hands,
        SeatId seat,
        bool voted) =>
        (voted ? SetSeat(ballot, seat, present: true) : ballot, SetSeat(hands, seat, voted));
}
