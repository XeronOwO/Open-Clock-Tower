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

            // 流放族（D2）：折叠在独立文件里（提名 / 流放各自一册账，共用折叠原语）。
            ExileProposedEvent or ExileVoteCastEvent or ExileSweepStartedEvent
                or ExileSeatVoteCollectedEvent or ExileSweepResumedEvent or ExileVoteCountedEvent
                => ExileLedgerFolder.Apply(current, gameEvent),

            // 当天的死亡保护裁定（D3）：当天作用域、每席位一条（R-0048）。
            DayProtectionDecidedEvent protectionDecided => ApplyProtectionDecided(current, protectionDecided),

            // 屠夫窗口（D4 / R-0050）：首次处决后开窗、窗口内的额外提名；都折进当天账。
            ExtraNominationWindowOpenedEvent windowOpened => ApplyExtraNominationWindowOpened(current, windowOpened),
            ExtraNominationMadeEvent extraMade => ApplyExtraNominationMade(current, extraMade),

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
        return DayLedgerEdit.UpdateOpenDay(state, made.DayNumber, day =>
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
        return DayLedgerEdit.UpdateOpenDay(state, cast.DayNumber, day =>
        {
            var nomination = DayLedgerEdit.FindOpenNomination(day, cast.NominationIndex, "投票");

            // 旧形态（Sweep = null）：举手直接改实时票面；钟盘形态：只改"现在谁举着手"，
            // 票面要等逐席收票追加冻结结论（先举也算、过时不候，R-0017）。
            NominationRecord updated;
            if (nomination.Sweep is null)
            {
                updated = nomination with
                {
                    Ballot = BallotSweep.SetSeat(nomination.Ballot, cast.Voter, cast.Voted),
                };
            }
            else
            {
                if (nomination.Sweep.Collected.Any(vote => vote.Seat == cast.Voter))
                {
                    throw new InvalidOperationException(
                        $"事件流顺序损坏：白天 {day.DayNumber} 第 {nomination.Index} 项提名里，"
                        + $"席位 {cast.Voter.Value} 已被收票，不能再改举手状态（过时不候）");
                }

                updated = nomination with
                {
                    HandsRaised = BallotSweep.SetSeat(nomination.HandsRaised, cast.Voter, cast.Voted),
                };
            }

            // 动作表按发生顺序追加：撤回同样进表（它是一条"发生过"的事实，R-0037）。
            return DayLedgerEdit.ReplaceNomination(day, updated) with
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
        return DayLedgerEdit.UpdateOpenDay(state, started.DayNumber, day =>
        {
            var nomination = DayLedgerEdit.FindOpenNomination(day, started.NominationIndex, "开始收票");
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

            if (!BallotSweep.IsStrictlyAscending(started.Seats))
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {nomination.Index} 项提名的收票席位必须严格升序");
            }

            // 钟盘串行（D2 实施口径）：另一条收票未收完时不允许再开——内核不会产出这种输入，
            // 出现即事件流损坏（不静默把两条收票同时挂上钟盘）。
            if (day.ActiveBallot is { } active)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 的钟盘上还有没走完的收票（{active.Describe()}），"
                    + $"却又开始第 {nomination.Index} 项提名的收票");
            }

            // 开始收票 = 举手窗口重开：旧形态可能留下的实时票面不并入冻结结论（它不属于这一次收票），
            // 玩家在倒计时里重新举手；"今天谁举过手"仍完整留在 VoteAttempts 里（R-0037）。
            return DayLedgerEdit.ReplaceNomination(
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
        return DayLedgerEdit.UpdateOpenDay(state, collected.DayNumber, day =>
        {
            var nomination = DayLedgerEdit.FindOpenNomination(day, collected.NominationIndex, "收票");
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

            var (ballot, hands) = BallotSweep.Freeze(
                nomination.Ballot,
                nomination.HandsRaised,
                collected.Seat,
                collected.Voted);

            return DayLedgerEdit.ReplaceNomination(
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
        return DayLedgerEdit.UpdateOpenDay(state, resumed.DayNumber, day =>
        {
            var nomination = DayLedgerEdit.FindOpenNomination(day, resumed.NominationIndex, "继续收票");
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

        return DayLedgerEdit.UpdateOpenDay(state, counted.DayNumber, day =>
        {
            var nomination = DayLedgerEdit.FindOpenNomination(day, counted.NominationIndex, "计票");

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

            var updated = DayLedgerEdit.ReplaceNomination(
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

        return DayLedgerEdit.UpdateOpenDay(state, dayNumber, day =>
        {
            if (day.Executions.Count == 0)
            {
                // 当天首次处决：常规 / 处罚都允许；执行后「即将被处决」立即清空——D4 起白天可能保持 Open
                // 等屠夫窗口（R-0050），不清会把同一个人留到下一次 CloseDay。
                return day with
                {
                    Executions =
                    [
                        .. day.Executions,
                        new DayExecution { Seat = executed.Seat, Kind = executed.Kind },
                    ],
                    AboutToBeExecuted = null,
                };
            }

            if (day.ExtraNomination is not { Status: ExtraNominationWindowStatus.Used })
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 已经有 {day.Executions.Count} 次处决"
                    + $"（首次是 {day.Executions[0].Seat.Value} 号），额外提名窗口没有用掉，不能再产出处决事实"
                    + "（每个白天至多两次：常规一次 + 屠夫窗口一次；R-0050）");
            }

            if (day.Executions.Count >= 2 || executed.Kind != ExecutionKind.Day)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 的第二次处决必须是屠夫窗口用掉后的常规处决（R-0050）");
            }

            return day with
            {
                Executions =
                [
                    .. day.Executions,
                    new DayExecution { Seat = executed.Seat, Kind = executed.Kind },
                ],
                AboutToBeExecuted = null,
            };
        });
    }

    private static DayState ApplyDayClosed(DayState state, DayClosedEvent closed)
    {
        return DayLedgerEdit.UpdateOpenDay(state, closed.DayNumber, day => day with { Status = DayStatus.Closed });
    }

    /// <summary>折叠一条死亡保护裁定：受理条件与 <see cref="DayProtectionMachine"/> 同尺（恢复也必须失败）。</summary>
    private static DayState ApplyProtectionDecided(DayState state, DayProtectionDecidedEvent decided)
    {
        return DayLedgerEdit.UpdateOpenDay(state, decided.DayNumber, day =>
        {
            if (day.ProtectionDecisionFor(decided.Seat) is not null)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 席位 {decided.Seat.Value} 的死亡保护已经裁定过");
            }

            // 裁定只发生在「达线时」：流放必须是该席位、收票已收完、票面达线。
            // 事件流里出现别的形状 = 被改写 / 重排，恢复必须失败（D-0010 / D-0014 能力 3）。
            if (day.OpenExile is not { } exile || exile.Target != decided.Seat)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 没有针对席位 {decided.Seat.Value} 的未结清流放，"
                    + "却收到死亡保护裁定");
            }

            if (exile.Sweep is not { IsComplete: true } sweep)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {exile.Index} 条流放的收票还没走完，"
                    + "却收到死亡保护裁定");
            }

            if (exile.Ballot.Count * 2 < sweep.Seats.Count)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 第 {exile.Index} 条流放还没有达线，"
                    + "却收到死亡保护裁定");
            }

            return day with
            {
                ProtectionDecisions =
                [
                    .. day.ProtectionDecisions,
                    new DayProtectionDecision
                    {
                        Seat = decided.Seat,
                        Protected = decided.Protected,
                    },
                ],
            };
        });
    }

    /// <summary>折叠一次「额外提名窗口打开」：只有当天恰好一次处决、且从未开过窗口时合法（R-0050）。</summary>
    private static DayState ApplyExtraNominationWindowOpened(
        DayState state,
        ExtraNominationWindowOpenedEvent opened)
    {
        return DayLedgerEdit.UpdateOpenDay(state, opened.DayNumber, day =>
        {
            if (day.ExtraNomination is not null)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 的额外提名窗口已经打开过（每个白天至多一次，R-0050）");
            }

            if (day.Executions.Count != 1)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 在 {day.Executions.Count} 次处决之后打开了额外提名窗口"
                    + "（窗口只跟在当天首次处决之后，R-0050）");
            }

            if (day.AboutToBeExecuted is not null)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 还有「即将被处决」者，却打开了额外提名窗口（R-0050）");
            }

            return day with
            {
                ExtraNomination = new ExtraNominationWindow
                {
                    Seat = opened.Seat,
                    Status = ExtraNominationWindowStatus.Open,
                },
            };
        });
    }

    /// <summary>折叠一次额外提名：窗口必须开着、发起人必须是授予席位、序号连续（R-0050）。</summary>
    private static DayState ApplyExtraNominationMade(DayState state, ExtraNominationMadeEvent made)
    {
        return DayLedgerEdit.UpdateOpenDay(state, made.DayNumber, day =>
        {
            if (day.OpenNomination is not null)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 已有一项提名在投票，又发起第 {made.NominationIndex} 项");
            }

            if (day.ExtraNomination is not { Status: ExtraNominationWindowStatus.Open } window)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 的额外提名窗口没有开着，却收到额外提名（R-0050）");
            }

            if (made.Nominator != window.Seat)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：白天 {day.DayNumber} 的额外提名不是窗口授予席位 {window.Seat.Value} 发起的"
                    + "（R-0050）");
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
                        Kind = NominationKind.Extra,
                        Nominator = made.Nominator,
                        Nominee = made.Nominee,
                        NominatorCharacter = made.NominatorCharacter,
                        Status = NominationStatus.Voting,
                    },
                ],
                ExtraNomination = window with { Status = ExtraNominationWindowStatus.Used },
            };
        });
    }

}
