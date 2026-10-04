namespace OpenClockTower.Kernel;

/// <summary>
/// 把白天事件折叠回 <see cref="DayState"/>——重放、重启恢复、重建的共同基础。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="GameStateMachine"/> 同一姿态：**纯计算**、状态是事件的折叠结果（D-0010），
/// 顺序损坏（重复开始白天、改不存在的提名、重复消耗票权）一律显式抛
/// <see cref="InvalidOperationException"/>，恢复必须失败、不得静默继续（D-0014 能力 3）。
/// </para>
/// <para>
/// 折叠**不重算计票结论**：阈值依赖计票那一刻的存活人数，折叠层只有事件流；
/// 结论由 <see cref="VoteCountedEvent"/> 携带（D-0010：事件是唯一事实来源）。
/// </para>
/// </remarks>
internal static class DayLedgerFolder
{
    /// <summary>把一条白天事件折叠回白天账。</summary>
    internal static DayState Apply(DayState? state, GameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);
        var current = state ?? DayState.Empty;

        return gameEvent switch
        {
            DayStartedEvent started => ApplyDayStarted(current, started),
            NominationMadeEvent made => ApplyNominationMade(current, made),
            VoteCastEvent cast => ApplyVoteCast(current, cast),
            VoteSweepStartedEvent sweepStarted => ApplyVoteSweepStarted(current, sweepStarted),
            SeatVoteCollectedEvent seatCollected => ApplySeatVoteCollected(current, seatCollected),
            VoteSweepResumedEvent sweepResumed => ApplyVoteSweepResumed(current, sweepResumed),
            VoteCountedEvent counted => ApplyVoteCounted(current, counted),
            ExecutedEvent executed => ApplyExecuted(current, executed),
            DayClosedEvent closed => ApplyDayClosed(current, closed),
            _ => throw new InvalidOperationException($"不是白天事件：{gameEvent.GetType().Name}"),
        };
    }

    private static DayState ApplyDayStarted(DayState state, DayStartedEvent started)
    {
        if (state.OpenDay is { } open)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：白天 {open.DayNumber} 还没有结束，又开始白天 {started.DayNumber}");
        }

        if (started.DayNumber != state.Days.Count + 1)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：白天序号必须连续，账里已有 {state.Days.Count} 天，收到第 {started.DayNumber} 天");
        }

        return state with
        {
            Days = [.. state.Days, new DayRecord { DayNumber = started.DayNumber, Status = DayStatus.Open }],
        };
    }

    private static DayState ApplyNominationMade(DayState state, NominationMadeEvent made)
    {
        return UpdateOpenDay(state, made.DayNumber, day =>
        {
            if (day.OpenNomination is not null)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 已有一项提名在投票，又发起第 {made.NominationIndex} 项");
            }

            if (made.NominationIndex != day.Nominations.Count + 1)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：提名序号必须连续，已有 {day.Nominations.Count} 项，收到第 {made.NominationIndex} 项");
            }

            return day with
            {
                Nominations =
                [
                    .. day.Nominations,
                    new NominationRecord
                    {
                        Index = made.NominationIndex,
                        Nominator = made.Nominator,
                        Nominee = made.Nominee,
                        NominatorCharacter = made.NominatorCharacter,
                        Status = NominationStatus.Voting,
                    },
                ],
            };
        });
    }

    private static DayState ApplyVoteCast(DayState state, VoteCastEvent cast)
    {
        return UpdateOpenDay(state, cast.DayNumber, day =>
        {
            var nomination = FindOpenNomination(day, cast.NominationIndex, "投票");

            // 旧形态（Sweep = null）：举手直接改实时票面；钟盘形态：只改"现在谁举着手"，
            // 票面要等逐席收票追加冻结结论（先举也算、过时不候，R-0017）。
            NominationRecord updated;
            if (nomination.Sweep is null)
            {
                var ballot = cast.Voted
                    ? InsertSeat(nomination.Ballot, cast.Voter)
                    : [.. nomination.Ballot.Where(seat => seat != cast.Voter)];
                updated = nomination with { Ballot = ballot };
            }
            else
            {
                if (nomination.Sweep.Collected.Any(vote => vote.Seat == cast.Voter))
                {
                    throw new InvalidOperationException(
                        $"事件流顺序损坏：白天 {day.DayNumber} 第 {nomination.Index} 项提名里，"
                        + $"席位 {cast.Voter.Value} 已被收票，不能再改举手状态（过时不候）");
                }

                var hands = cast.Voted
                    ? InsertSeat(nomination.HandsRaised, cast.Voter)
                    : [.. nomination.HandsRaised.Where(seat => seat != cast.Voter)];
                updated = nomination with { HandsRaised = hands };
            }

            // 动作表按发生顺序追加：撤回同样进表（它是一条"发生过"的事实，R-0037）。
            return ReplaceNomination(day, updated) with
            {
                VoteAttempts =
                [
                    .. day.VoteAttempts,
                    new DayVoteAttempt
                    {
                        NominationIndex = cast.NominationIndex,
                        Voter = cast.Voter,
                        VoterCharacter = cast.VoterCharacter,
                        Voted = cast.Voted,
                    },
                ],
            };
        });
    }

    private static DayState ApplyVoteSweepStarted(DayState state, VoteSweepStartedEvent started)
    {
        return UpdateOpenDay(state, started.DayNumber, day =>
        {
            var nomination = FindOpenNomination(day, started.NominationIndex, "开始收票");
            if (nomination.Sweep is not null)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {nomination.Index} 项提名的收票已经开始过");
            }

            if (started.Seats.Count == 0)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {nomination.Index} 项提名的收票没有席位顺序");
            }

            if (!VoteSweepLimits.IsCountdownValid(started.CountdownMilliseconds)
                || !VoteSweepLimits.IsIntervalValid(started.IntervalMilliseconds))
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {nomination.Index} 项提名的收票参数越界");
            }

            for (var index = 1; index < started.Seats.Count; index++)
            {
                if (started.Seats[index].Value <= started.Seats[index - 1].Value)
                {
                    throw new InvalidOperationException(
                        $"事件流顺序损坏：白天 {day.DayNumber} 第 {nomination.Index} 项提名的收票席位必须严格升序");
                }
            }

            // 开始收票 = 举手窗口重开：旧形态可能留下的实时票面不并入冻结结论（它不属于这一次收票），
            // 玩家在倒计时里重新举手；"今天谁举过手"仍完整留在 VoteAttempts 里（R-0037）。
            return ReplaceNomination(
                day,
                nomination with
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
    }

    private static DayState ApplySeatVoteCollected(DayState state, SeatVoteCollectedEvent collected)
    {
        return UpdateOpenDay(state, collected.DayNumber, day =>
        {
            var nomination = FindOpenNomination(day, collected.NominationIndex, "收票");
            if (nomination.Sweep is not { } sweep)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {nomination.Index} 项提名还没有开始收票，却收到收票事件");
            }

            if (sweep.IsComplete)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {nomination.Index} 项提名的收票已经全部完成，却还有收票事件");
            }

            if (sweep.NextSeat != collected.Seat)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {nomination.Index} 项提名下一待收的是 "
                    + $"{(sweep.NextSeat is { } next ? next.Value.ToString() : "无")} 号席位，"
                    + $"却收到 {collected.Seat.Value} 号");
            }

            var ballot = collected.Voted ? InsertSeat(nomination.Ballot, collected.Seat) : nomination.Ballot;
            var hands = collected.Voted
                ? InsertSeat(nomination.HandsRaised, collected.Seat)
                : [.. nomination.HandsRaised.Where(seat => seat != collected.Seat)];

            return ReplaceNomination(
                day,
                nomination with
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
                                VoterCharacter = collected.VoterCharacter,
                            },
                        ],
                    },
                });
        });
    }

    private static DayState ApplyVoteSweepResumed(DayState state, VoteSweepResumedEvent resumed)
    {
        return UpdateOpenDay(state, resumed.DayNumber, day =>
        {
            var nomination = FindOpenNomination(day, resumed.NominationIndex, "继续收票");
            if (nomination.Sweep is not { } sweep || sweep.IsComplete)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {nomination.Index} 项提名没有可继续的收票");
            }

            // 继续只重新起算控制面的时间轴（SessionTrackers 按本事件的记录时刻重锚）；
            // 内核状态不变——收票进度只由逐席事件推进（D-0010）。
            return day;
        });
    }

    private static DayState ApplyVoteCounted(DayState state, VoteCountedEvent counted)
    {
        foreach (var seat in counted.SpentVoteTokens)
        {
            if (!counted.Voters.Contains(seat))
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：席位 {seat.Value} 被记为消耗投票权，却不在投票者名单里");
            }

            if (state.HasSpentVoteToken(seat))
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：席位 {seat.Value} 的死后投票权被重复消耗");
            }
        }

        return UpdateOpenDay(state, counted.DayNumber, day =>
        {
            var nomination = FindOpenNomination(day, counted.NominationIndex, "计票");

            // 钟盘形态：收票没走完不能计票（R-0017 目标形态）；旧形态（Sweep = null）没有这条。
            if (nomination.Sweep is { IsComplete: false } incomplete)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {nomination.Index} 项提名的收票还没走完"
                    + $"（下一席 {incomplete.NextSeat?.Value} 号），却收到计票事件");
            }

            var voters = counted.Voters.OrderBy(seat => seat.Value).ToArray();

            // 计票名单必须与已折叠的票面一致：不一致说明事件流被改写 / 重排，恢复必须失败，
            // 不能"以事件里的名单为准"悄悄改掉票面（D-0010：票面本身就是事件折叠出来的事实）。
            if (!nomination.Ballot.SequenceEqual(voters))
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {nomination.Index} 项提名的计票名单与已折叠的票面不一致"
                    + $"（票面 {nomination.Ballot.Count} 人，计票名单 {voters.Length} 人）");
            }

            var updated = ReplaceNomination(
                day,
                nomination with
                {
                    Status = NominationStatus.Counted,
                    Ballot = voters,
                });

            if (counted.AboutToBeExecuted is { } candidate
                && !updated.Nominations.Any(item =>
                    item.Status == NominationStatus.Counted && item.Nominee == candidate))
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：计票事件把 {candidate.Value} 记为「即将被处决」，但他今天没有被计票的提名");
            }

            return updated with
            {
                AboutToBeExecuted = counted.AboutToBeExecuted,
            };
        }) with
        {
            SpentVoteTokens = [.. state.SpentVoteTokens, .. counted.SpentVoteTokens],
        };
    }

    private static DayState ApplyExecuted(DayState state, ExecutedEvent executed)
    {
        if (executed.DayNumber is not { } dayNumber)
        {
            // 夜晚的处罚处决：不占任何白天的上限，也不写白天账——事实留在事件流（R-0020）。
            return state;
        }

        return UpdateOpenDay(state, dayNumber, day =>
        {
            if (day.Executed is not null)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 已经处决过 {day.Executed.Value.Value}，不能再次处决");
            }

            return day with { Executed = executed.Seat, ExecutedKind = executed.Kind };
        });
    }

    private static DayState ApplyDayClosed(DayState state, DayClosedEvent closed)
    {
        return UpdateOpenDay(state, closed.DayNumber, day => day with { Status = DayStatus.Closed });
    }

    private static DayState UpdateOpenDay(DayState state, int dayNumber, Func<DayRecord, DayRecord> update)
    {
        var day = state.OpenDay;
        if (day is null || day.DayNumber != dayNumber)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：白天 {dayNumber} 不是当前进行中的白天（当前：{(day is null ? "无" : day.DayNumber)}）");
        }

        var days = state.Days.ToArray();
        days[^1] = update(day);
        return state with { Days = days };
    }

    private static NominationRecord FindOpenNomination(DayRecord day, int index, string action)
    {
        var nomination = day.Nominations.FirstOrDefault(item => item.Index == index);
        if (nomination is null)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：白天 {day.DayNumber} 没有第 {index} 项提名，却收到{action}事件");
        }

        if (nomination.Status != NominationStatus.Voting)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：白天 {day.DayNumber} 第 {index} 项提名已经计票，不能再次{action}");
        }

        return nomination;
    }

    private static DayRecord ReplaceNomination(DayRecord day, NominationRecord nomination)
    {
        var nominations = day.Nominations.ToArray();
        var position = Array.FindIndex(nominations, item => item.Index == nomination.Index);
        if (position < 0)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：白天 {day.DayNumber} 没有第 {nomination.Index} 项提名");
        }

        nominations[position] = nomination;
        return day with { Nominations = nominations };
    }

    /// <summary>把席位插进按席位号升序的票面（重复投票是幂等的，不产生第二条）。</summary>
    private static IReadOnlyList<SeatId> InsertSeat(IReadOnlyList<SeatId> ballot, SeatId seat)
    {
        if (ballot.Contains(seat))
        {
            return ballot;
        }

        var result = ballot.ToList();
        result.Add(seat);
        result.Sort((left, right) => left.Value.CompareTo(right.Value));
        return result;
    }
}
