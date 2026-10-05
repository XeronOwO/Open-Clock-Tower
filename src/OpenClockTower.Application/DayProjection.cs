using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Application;

/// <summary>
/// 玩家视角的白天投影：把白天账折算成"公开事实 + 我能做什么"。
/// </summary>
/// <remarks>
/// 权限判定与服务端命令校验同源（<see cref="DayMachine"/> / <see cref="ExileMachine"/> /
/// <see cref="ExtraNominationMachine"/>）：这里给的是 UI 使能条件，真正的拒绝在服务端；
/// 观测不齐时一律给 false（保守），不允许前端自行推算（web/AGENTS §4）。
/// </remarks>
public static class DayProjection
{
    /// <summary>某个席位当前看到的白天信息；还没有开过白天时为 null。</summary>
    /// <param name="day">白天账。</param>
    /// <param name="state">状态账（判定生死与投票权）。</param>
    /// <param name="seats">本局**在局**座次（算可提名 / 可流放目标与「本席在不在局」用；读不到时给空表，宁可少给、不猜）。</param>
    /// <param name="seat">接收者席位。</param>
    /// <param name="board">公开生死面（`rulings.md` R-0022）：对外可见生死 + 本日公告；不含死因。</param>
    /// <param name="now">应用层当前时刻（算收票剩余时间；不驱动推进）。</param>
    /// <param name="voteSweepStartedAt">收票时间轴锚点；为空 = 未开始或已中断。</param>
    /// <param name="characters">角色事实端口（判定流放目标是不是旅行者）；缺失 = 不给流放候选（不猜）。</param>
    public static PlayerDay? ForSeat(
        DayState? day,
        GameState state,
        IReadOnlyList<SeatId> seats,
        SeatId seat,
        PublicLifeBoard board,
        DateTimeOffset now,
        DateTimeOffset? voteSweepStartedAt,
        IWinConditionFacts? characters = null)
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
        var inGame = seats.Contains(seat);
        var openNomination = facts.OpenNomination;
        var openExile = facts.OpenExile;

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

        // 流放提议（R-0044 第 2 / 3 条）：任何在局玩家（含死者）随时可提；同一天必须一条结清后再提下一条。
        var canProposeExile = facts.Status == DayStatus.Open
            && openExile is null
            && inGame;

        // 可流放目标 = 在局旅行者里今天还没被提议过的（角色未观测 / 端口缺失一律不列——不猜）。
        var exileCandidates = characters is null
            ? Array.Empty<SeatId>()
            : seats
                .Where(candidate => !facts.HasExileProposed(candidate))
                .Where(candidate => state.Seat(candidate)?.CharacterValue is { } character
                    && characters.IsTraveller(character))
                .OrderBy(candidate => candidate.Value)
                .ToArray();

        // 流放表决（R-0044 第 6 / 10 条）：名单 = 开始收票时的在局座次快照；含死者、不查也不耗投票标记。
        var exileSweep = openExile?.Sweep;
        var exileCollected = exileSweep?.Collected.FirstOrDefault(vote => vote.Seat == seat);
        var canVoteExile = facts.Status == DayStatus.Open
            && exileSweep is not null
            && exileCollected is null
            && inGame
            && exileSweep.Seats.Contains(seat);

        var exileVoted = exileCollected is { } frozenExile
            ? frozenExile.Voted
            : exileSweep is not null
                ? openExile!.HandsRaised.Contains(seat)
                : openExile is not null && openExile.Ballot.Contains(seat);

        // 额外提名窗口（R-0050）：窗口开着、本席是授予席位、没有开放提名，且本席此刻握有角色能力
        // （存活，或死亡但有生效中的重获窗口——《集骨者》范例；判定不了不给入口）。
        var extraNomination = facts.ExtraNomination;
        var canNominateExtra = facts.Status == DayStatus.Open
            && openNomination is null
            && extraNomination is { Status: ExtraNominationWindowStatus.Open }
            && extraNomination.Seat == seat
            && inGame
            && state.AbilityPresentOn(seat) == true;

        var extraNominationCandidates = canNominateExtra
            ? seats.OrderBy(candidate => candidate.Value).ToArray()
            : Array.Empty<SeatId>();

        // 杂耍艺人的公开猜测（R-0057-B）：只在这次持有的首个白天、且还没猜过时给入口。
        var canMakeJugglerGuesses = facts.Status == DayStatus.Open
            && inGame
            && CanMakeJugglerGuesses(day, facts, state, seat);

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
            ExileSweep = VoteSweepProjection.Build(openExile, now, voteSweepStartedAt),
            CanProposeExile = canProposeExile,
            ExileCandidates = exileCandidates,
            CanVoteExile = canVoteExile,
            ExileVoted = exileVoted,
            ExileSeatCollected = exileCollected is not null,
            CanNominateExtra = canNominateExtra,
            ExtraNominationCandidates = extraNominationCandidates,
            CanMakeJugglerGuesses = canMakeJugglerGuesses,
        };
    }

    /// <summary>
    /// 杂耍艺人此刻能不能公开猜测（R-0057-B）：本席持有杂耍艺人、当天就是**这次持有的首个白天**、
    /// 且这次持有还没猜过。判断口径与内核的 <c>JugglerGuessMachine</c> 同源（「首个白天」由规则层给）；
    /// 角色没观测到 / 判不了一律给 false（保守：宁可少给入口，不让前端自己推算）。
    /// </summary>
    private static bool CanMakeJugglerGuesses(DayState days, DayRecord openDay, GameState state, SeatId seat)
    {
        if (state.Seat(seat)?.CharacterValue is not { } character)
        {
            return false;
        }

        var source = RoleContracts.JugglerGuesses.FirstOrDefault(candidate => candidate.Character == character);
        if (source?.FirstHeldDay(state, seat) is not { } firstDay || firstDay != openDay.DayNumber)
        {
            return false;
        }

        return !days.Days.Any(record => record.JugglerGuesses.Any(guess =>
            guess.Seat == seat && guess.DayNumber >= firstDay));
    }
}
