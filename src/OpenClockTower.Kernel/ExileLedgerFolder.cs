namespace OpenClockTower.Kernel;

/// <summary>
/// 把流放事件折叠回 <see cref="DayState"/>——重放、重启恢复、重建的共同基础（票据 D2）。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="DayLedgerFolder"/> 同一姿态：**纯计算**、顺序损坏一律显式抛
/// <see cref="InvalidOperationException"/>，恢复必须失败、不得静默继续（D-0014 能力 3）；
/// **不重算计票结论**（阈值依赖收票开始时的在局人数快照，结论由 <see cref="ExileVoteCountedEvent"/>
/// 携带，D-0010）。
/// </para>
/// <para>
/// 依据 <c>docs/standard/rulings.md</c> R-0044 / R-0045 与票据「D2 实施口径」。
/// </para>
/// </remarks>
internal static class ExileLedgerFolder
{
    /// <summary>把一条流放事件折叠回白天账。</summary>
    internal static DayState Apply(DayState state, GameEvent gameEvent) => gameEvent switch
    {
        ExileProposedEvent proposed => ApplyProposed(state, proposed),
        ExileVoteCastEvent cast => ApplyVoteCast(state, cast),
        ExileSweepStartedEvent started => ApplySweepStarted(state, started),
        ExileSeatVoteCollectedEvent collected => ApplySeatVoteCollected(state, collected),
        ExileSweepResumedEvent resumed => ApplySweepResumed(state, resumed),
        ExileVoteCountedEvent counted => ApplyVoteCounted(state, counted),
        _ => throw new InvalidOperationException($"不是流放事件：{gameEvent.GetType().Name}"),
    };

    private static DayState ApplyProposed(DayState state, ExileProposedEvent proposed) =>
        DayLedgerEdit.UpdateOpenDay(state, proposed.DayNumber, day =>
        {
            if (day.OpenExile is not null)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 已有未结清的流放，又提议第 {proposed.ExileIndex} 条");
            }

            if (proposed.ExileIndex != day.Exiles.Count + 1)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：流放序号必须连续，已有 {day.Exiles.Count} 条，收到第 {proposed.ExileIndex} 条");
            }

            if (day.HasExileProposed(proposed.Target))
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：席位 {proposed.Target.Value} 今天已经被提议过流放，不能再次提议");
            }

            return day with
            {
                Exiles =
                [
                    .. day.Exiles,
                    new ExileRecord
                    {
                        Index = proposed.ExileIndex,
                        Proposer = proposed.Proposer,
                        Target = proposed.Target,
                        Status = ExileStatus.Voting,
                    },
                ],
            };
        });

    private static DayState ApplyVoteCast(DayState state, ExileVoteCastEvent cast) =>
        DayLedgerEdit.UpdateOpenDay(state, cast.DayNumber, day =>
        {
            var exile = DayLedgerEdit.FindOpenExile(day, cast.ExileIndex, "举手");

            if (exile.Sweep is not { } sweep)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {exile.Index} 条流放的收票还没有开始，却收到举手事件");
            }

            if (sweep.Collected.Any(vote => vote.Seat == cast.Voter))
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {exile.Index} 条流放里，"
                    + $"席位 {cast.Voter.Value} 已被收票，不能再改举手状态（过时不候）");
            }

            return DayLedgerEdit.ReplaceExile(day, exile with
            {
                HandsRaised = BallotSweep.SetSeat(exile.HandsRaised, cast.Voter, cast.Voted),
            });
        });

    private static DayState ApplySweepStarted(DayState state, ExileSweepStartedEvent started) =>
        DayLedgerEdit.UpdateOpenDay(state, started.DayNumber, day =>
        {
            var exile = DayLedgerEdit.FindOpenExile(day, started.ExileIndex, "开始收票");
            if (exile.Sweep is not null)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {exile.Index} 条流放的收票已经开始过");
            }

            if (started.Seats.Count == 0)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {exile.Index} 条流放的收票没有席位顺序");
            }

            if (!VoteSweepLimits.IsCountdownValid(started.CountdownMilliseconds)
                || !VoteSweepLimits.IsIntervalValid(started.IntervalMilliseconds))
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {exile.Index} 条流放的收票参数越界");
            }

            if (!BallotSweep.IsStrictlyAscending(started.Seats))
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {exile.Index} 条流放的收票席位必须严格升序");
            }

            // 钟盘串行（D2 实施口径）：另一条收票未收完时不允许再开——内核不会产出这种输入，
            // 出现即事件流损坏（不静默把两条收票同时挂上钟盘）。
            if (day.ActiveBallot is { } active)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 的钟盘上还有没走完的收票（{active.Describe()}），"
                    + $"却又开始第 {exile.Index} 条流放的收票");
            }

            return DayLedgerEdit.ReplaceExile(day, exile with
            {
                Ballot = [],
                HandsRaised = [],
                Sweep = new VoteSweepState
                {
                    Seats = started.Seats,
                    CountdownMilliseconds = started.CountdownMilliseconds,
                    IntervalMilliseconds = started.IntervalMilliseconds,
                },
            });
        });

    private static DayState ApplySeatVoteCollected(DayState state, ExileSeatVoteCollectedEvent collected) =>
        DayLedgerEdit.UpdateOpenDay(state, collected.DayNumber, day =>
        {
            var exile = DayLedgerEdit.FindOpenExile(day, collected.ExileIndex, "收票");
            if (exile.Sweep is not { } sweep)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {exile.Index} 条流放还没有开始收票，却收到收票事件");
            }

            if (sweep.IsComplete)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {exile.Index} 条流放的收票已经全部完成，却还有收票事件");
            }

            if (sweep.NextSeat != collected.Seat)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {exile.Index} 条流放下一待收的是 "
                    + $"{(sweep.NextSeat is { } next ? next.Value.ToString() : "无")} 号席位，"
                    + $"却收到 {collected.Seat.Value} 号");
            }

            var (ballot, hands) = BallotSweep.Freeze(
                exile.Ballot,
                exile.HandsRaised,
                collected.Seat,
                collected.Voted);

            return DayLedgerEdit.ReplaceExile(day, exile with
            {
                Ballot = ballot,
                HandsRaised = hands,
                Sweep = sweep with
                {
                    Collected =
                    [
                        .. sweep.Collected,
                        new CollectedSeatVote
                        {
                            Seat = collected.Seat,
                            Voted = collected.Voted,
                        },
                    ],
                },
            });
        });

    private static DayState ApplySweepResumed(DayState state, ExileSweepResumedEvent resumed) =>
        DayLedgerEdit.UpdateOpenDay(state, resumed.DayNumber, day =>
        {
            var exile = DayLedgerEdit.FindOpenExile(day, resumed.ExileIndex, "继续收票");
            if (exile.Sweep is not { IsComplete: false })
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {exile.Index} 条流放没有可继续的收票");
            }

            // 继续只重新起算控制面的时间轴（SessionTrackers 按本事件的记录时刻重锚）；
            // 内核状态不变——收票进度只由逐席事件推进（D-0010）。
            return day;
        });

    private static DayState ApplyVoteCounted(DayState state, ExileVoteCountedEvent counted) =>
        DayLedgerEdit.UpdateOpenDay(state, counted.DayNumber, day =>
        {
            var exile = DayLedgerEdit.FindOpenExile(day, counted.ExileIndex, "计票");
            if (exile.Sweep is not { } sweep)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {exile.Index} 条流放还没有开始收票，却收到计票事件");
            }

            if (!sweep.IsComplete)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {exile.Index} 条流放的收票还没走完"
                    + $"（下一席 {sweep.NextSeat?.Value} 号），却收到计票事件");
            }

            var voters = counted.Voters.OrderBy(seat => seat.Value).ToArray();

            // 计票名单必须与已折叠的票面一致：不一致说明事件流被改写 / 重排，恢复必须失败
            //（D-0010：票面本身就是事件折叠出来的事实）。
            if (!exile.Ballot.SequenceEqual(voters))
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {exile.Index} 条流放的计票名单与已折叠的票面不一致"
                    + $"（票面 {exile.Ballot.Count} 人，计票名单 {voters.Length} 人）");
            }

            if (voters.Any(seat => !sweep.Seats.Contains(seat)))
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {exile.Index} 条流放的计票名单里有人不在收票名册里");
            }

            if (counted.Conclusion == ExileConclusion.Exiled && voters.Length * 2 < sweep.Seats.Count)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {exile.Index} 条流放结论为「已流放」，"
                    + $"但 {voters.Length} 票没有达到 {sweep.Seats.Count} 席的过半线");
            }

            return DayLedgerEdit.ReplaceExile(day, exile with
            {
                Status = ExileStatus.Counted,
                Ballot = voters,
                Conclusion = counted.Conclusion,
            });
        });
}
