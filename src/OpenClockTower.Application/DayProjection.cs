using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 玩家视角的白天投影：把白天账折算成"公开事实 + 我能做什么"。
/// </summary>
/// <remarks>
/// 权限判定与服务端命令校验同源（<see cref="DayMachine"/>）：这里给的是 UI 使能条件，
/// 真正的拒绝在服务端；观测不齐时一律给 false（保守），不允许前端自行推算（web/AGENTS §4）。
/// </remarks>
public static class DayProjection
{
    /// <summary>某个席位当前看到的白天信息；还没有开过白天时为 null。</summary>
    /// <param name="day">白天账。</param>
    /// <param name="state">状态账（判定生死与投票权）。</param>
    /// <param name="seats">本局完整座次（算可提名目标用；读不到时给空表，宁可少给、不猜）。</param>
    /// <param name="seat">接收者席位。</param>
    /// <param name="board">公开生死面（`rulings.md` R-0022）：对外可见生死 + 本日公告；不含死因。</param>
    /// <param name="now">应用层当前时刻（算收票剩余时间；不驱动推进）。</param>
    /// <param name="voteSweepStartedAt">收票时间轴锚点；为空 = 未开始或已中断。</param>
    public static PlayerDay? ForSeat(
        DayState? day,
        GameState state,
        IReadOnlyList<SeatId> seats,
        SeatId seat,
        PublicLifeBoard board,
        DateTimeOffset now,
        DateTimeOffset? voteSweepStartedAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(seats);
        ArgumentNullException.ThrowIfNull(board);

        if (day is null || day.Days.Count == 0)
        {
            return null;
        }

        var facts = day.Days[^1];
        var life = state.Seat(seat)?.LifeValue;
        var openNomination = facts.OpenNomination;

        var canNominate = facts.Status == DayStatus.Open
            && openNomination is null
            && life == LifeState.Alive
            && !facts.HasNominated(seat);

        // 举手窗口 = 开始收票之后、本席被收票之前（先举也算、过时不候）；本席收票后由服务端锁死。
        var sweep = openNomination?.Sweep;
        var collectedVote = sweep?.Collected.FirstOrDefault(vote => vote.Seat == seat);
        var canVote = facts.Status == DayStatus.Open
            && sweep is not null
            && collectedVote is null
            && life is not null
            && (life == LifeState.Alive || !day.HasSpentVoteToken(seat));

        // 本席已被收票：Voted 展示**冻结结论**；否则展示当前举手状态（旧形态回放退回票面口径）。
        var voted = collectedVote is { } frozen
            ? frozen.Voted
            : sweep is not null
                ? openNomination!.HandsRaised.Contains(seat)
                : openNomination is not null && openNomination.Ballot.Contains(seat);

        // 可提名目标 = 本局席位里"今天还没被提名过"的（死亡玩家可以被提名，百科《提名》）。
        var candidates = seats
            .Where(candidate => !facts.HasBeenNominated(candidate))
            .OrderBy(candidate => candidate.Value)
            .ToArray();

        return new PlayerDay
        {
            PublicView = facts,
            Lives = [.. board.Lives],
            Announcements = [.. board.Announcements],
            CanNominate = canNominate,
            CanVote = canVote,
            Voted = voted,
            SeatCollected = collectedVote is not null,
            VoteSweep = VoteSweepProjection.Build(openNomination, now, voteSweepStartedAt),
            NominationCandidates = candidates,
        };
    }
}
