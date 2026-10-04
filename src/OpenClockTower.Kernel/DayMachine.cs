namespace OpenClockTower.Kernel;

/// <summary>
/// 白天阶段的纯迁移：提名 / 钟盘收票 / 计票 / 结束白天。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="StepMachine"/> 的分工：这里只回答"这条输入产出什么事件、还是被拒绝"，
/// 槽位推进（白天计划的开始与结束）由步骤机负责；票面的折叠见 <see cref="DayLedgerFolder"/>。
/// </para>
/// <para>
/// **纯计算**（D-0008）：生命事实从 <paramref name="context"/> 的状态账读，不猜；
/// 观测不齐就拒绝整条输入（D-0015）。规则依据：百科《规则概要》三 /《提名》/《投票》/《处决》
/// · 2026-10-01 抓取；钟盘收票口径见 <c>docs/standard/rulings.md</c> R-0017（目标形态）。
/// </para>
/// </remarks>
public static class DayMachine
{
    /// <summary>处决致死的机器可读原因（写进 <see cref="SeatStateChangedEvent.Reason"/>）。</summary>
    public const string ExecutionDeathReason = "day.execution";

    /// <summary>发起提名：只有存活者可发起；同日每人只能发起一次、也只能被提名一次；同一时间只能一项。</summary>
    public static DayOutcome Nominate(DayState state, SettlementContext context, NominateInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (state.OpenDay is not { } day)
        {
            return DayOutcome.Reject("day.not_open", "现在不是白天，或白天已经结束");
        }

        if (day.OpenNomination is not null)
        {
            return DayOutcome.Reject(
                "day.nomination_in_progress",
                "已有一项提名在投票中：先计票（CountVotes）或强推兜底，不能同时提两名玩家");
        }

        if (!context.Seats.Contains(input.Nominator))
        {
            return DayOutcome.Reject("day.nominator_unknown", $"席位 {input.Nominator.Value} 不在本局座次里");
        }

        if (!context.Seats.Contains(input.Nominee))
        {
            return DayOutcome.Reject("day.nominee_unknown", $"席位 {input.Nominee.Value} 不在本局座次里");
        }

        var nominatorLife = context.State.Seat(input.Nominator)?.LifeValue;
        if (nominatorLife is null)
        {
            return DayOutcome.Reject(
                "day.nominator_life_unknown",
                $"席位 {input.Nominator.Value} 的生死还没有观测：无法判定他能不能发起提名（不猜）");
        }

        if (nominatorLife != LifeState.Alive)
        {
            return DayOutcome.Reject(
                "day.nominator_dead",
                $"席位 {input.Nominator.Value} 已经死亡：只有存活玩家可以发起提名（百科《提名》）");
        }

        if (day.HasNominated(input.Nominator))
        {
            return DayOutcome.Reject(
                "day.nominator_already_nominated",
                $"席位 {input.Nominator.Value} 今天已经发起过提名：每名玩家每天只能发起一次（百科《提名》）");
        }

        if (day.HasBeenNominated(input.Nominee))
        {
            return DayOutcome.Reject(
                "day.nominee_already_nominated",
                $"席位 {input.Nominee.Value} 今天已经被提名过：每名玩家每天只能被提名一次（百科《提名》）");
        }

        // 提名者此刻的角色**快照**随事件落账：城镇公告员要按"提名当时"判定爪牙提名（R-0037），
        // 之后换角（R-0032）不再改写这条事实。未观测就记 null，不猜（D-0015）。
        var nominatorCharacter = context.State.Seat(input.Nominator)?.CharacterValue;

        return DayOutcome.Accepted(
        [
            new NominationMadeEvent
            {
                DayNumber = day.DayNumber,
                NominationIndex = day.Nominations.Count + 1,
                Nominator = input.Nominator,
                Nominee = input.Nominee,
                NominatorCharacter = nominatorCharacter,
            },
        ]);
    }

    /// <summary>
    /// 开始钟盘收票：进入倒计时，之后由控制面逐席送 <see cref="CollectSeatVoteInput"/>（R-0017 目标形态）。
    /// </summary>
    public static DayOutcome StartVoteSweep(DayState state, SettlementContext context, StartVoteSweepInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (state.OpenDay is not { } day)
        {
            return DayOutcome.Reject("day.not_open", "现在不是白天，或白天已经结束");
        }

        var open = day.OpenNomination;
        if (open is null)
        {
            return DayOutcome.Reject("day.no_open_nomination", "现在没有开放投票的提名");
        }

        if (open.Index != input.NominationIndex)
        {
            return DayOutcome.Reject(
                "day.nomination_not_open",
                $"当前开放的是第 {open.Index} 项提名，不是第 {input.NominationIndex} 项");
        }

        if (open.Sweep is not null)
        {
            return DayOutcome.Reject("day.sweep_started", $"第 {open.Index} 项提名的收票已经开始过了");
        }

        if (!VoteSweepLimits.IsCountdownValid(input.CountdownMilliseconds))
        {
            return DayOutcome.Reject(
                "day.sweep_countdown_invalid",
                $"倒计时必须在 {VoteSweepLimits.MinCountdownMilliseconds}–{VoteSweepLimits.MaxCountdownMilliseconds} 毫秒之间");
        }

        if (!VoteSweepLimits.IsIntervalValid(input.IntervalMilliseconds))
        {
            return DayOutcome.Reject(
                "day.sweep_interval_invalid",
                $"逐席间隔必须在 {VoteSweepLimits.MinIntervalMilliseconds}–{VoteSweepLimits.MaxIntervalMilliseconds} 毫秒之间");
        }

        if (context.Seats.Count == 0)
        {
            return DayOutcome.Reject("day.no_seats", "本局座次还没有观测：无法确定收票顺序（不猜）");
        }

        return DayOutcome.Accepted(
        [
            new VoteSweepStartedEvent
            {
                DayNumber = day.DayNumber,
                NominationIndex = open.Index,
                Seats = [.. context.Seats.OrderBy(seat => seat.Value)],
                CountdownMilliseconds = input.CountdownMilliseconds,
                IntervalMilliseconds = input.IntervalMilliseconds,
            },
        ]);
    }

    /// <summary>
    /// 收第 N 席的票：严格时点冻结该席"已登记的举手状态"（先举也算、过时不候）；
    /// 只接受下一待收席位，顺序由内核校验（R-0017 目标形态）。
    /// </summary>
    public static DayOutcome CollectSeatVote(DayState state, SettlementContext context, CollectSeatVoteInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (state.OpenDay is not { } day)
        {
            return DayOutcome.Reject("day.not_open", "现在不是白天，或白天已经结束");
        }

        var open = day.OpenNomination;
        if (open is null)
        {
            return DayOutcome.Reject("day.no_open_nomination", "现在没有开放投票的提名");
        }

        if (open.Index != input.NominationIndex)
        {
            return DayOutcome.Reject(
                "day.nomination_not_open",
                $"当前开放的是第 {open.Index} 项提名，不是第 {input.NominationIndex} 项");
        }

        if (open.Sweep is not { } sweep)
        {
            return DayOutcome.Reject("day.sweep_not_started", "收票还没有开始：先由说书人点「开始」");
        }

        if (sweep.NextSeat is not { } nextSeat)
        {
            return DayOutcome.Reject("day.sweep_complete", "收票已经全部完成，不能再收");
        }

        if (nextSeat != input.Seat)
        {
            return DayOutcome.Reject(
                "day.seat_out_of_order",
                $"下一待收的是 {nextSeat.Value} 号席位，不是 {input.Seat.Value} 号（收票必须按席位升序走完一圈）");
        }

        // 举手状态是事件流折叠出来的事实（先举也算）；角色快照只用于回溯型能力（R-0037），未观测记 null。
        var voted = open.HandsRaised.Contains(input.Seat);
        var voterCharacter = context.State.Seat(input.Seat)?.CharacterValue;

        return DayOutcome.Accepted(
        [
            new SeatVoteCollectedEvent
            {
                DayNumber = day.DayNumber,
                NominationIndex = open.Index,
                Seat = input.Seat,
                Voted = voted,
                VoterCharacter = voterCharacter,
            },
        ]);
    }

    /// <summary>继续中断的收票：重新起倒计时，从下一未收席位接着收（R-0017 目标形态）。</summary>
    public static DayOutcome ResumeVoteSweep(DayState state, SettlementContext context, ResumeVoteSweepInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (state.OpenDay is not { } day)
        {
            return DayOutcome.Reject("day.not_open", "现在不是白天，或白天已经结束");
        }

        var open = day.OpenNomination;
        if (open is null)
        {
            return DayOutcome.Reject("day.no_open_nomination", "现在没有开放投票的提名");
        }

        if (open.Index != input.NominationIndex)
        {
            return DayOutcome.Reject(
                "day.nomination_not_open",
                $"当前开放的是第 {open.Index} 项提名，不是第 {input.NominationIndex} 项");
        }

        if (open.Sweep is not { } sweep)
        {
            return DayOutcome.Reject("day.sweep_not_started", "收票还没有开始：没有可以继续的收票");
        }

        if (sweep.IsComplete)
        {
            return DayOutcome.Reject("day.sweep_complete", "收票已经全部完成，不能再继续");
        }

        return DayOutcome.Accepted(
        [
            new VoteSweepResumedEvent
            {
                DayNumber = day.DayNumber,
                NominationIndex = open.Index,
            },
        ]);
    }

    /// <summary>
    /// 举手 / 放下：只在「开始收票之后、本席被收票之前」有效；本席收票后过时不候（R-0017 目标形态）。
    /// 存活玩家不限次数；死亡玩家消耗「死后仅一次」的投票权（计票时结算）。
    /// </summary>
    public static DayOutcome CastVote(DayState state, SettlementContext context, CastVoteInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (state.OpenDay is not { } day)
        {
            return DayOutcome.Reject("day.not_open", "现在不是白天，或白天已经结束");
        }

        var open = day.OpenNomination;
        if (open is null)
        {
            return DayOutcome.Reject("day.no_open_nomination", "现在没有开放投票的提名");
        }

        if (open.Index != input.NominationIndex)
        {
            return DayOutcome.Reject(
                "day.nomination_not_open",
                $"当前开放的是第 {open.Index} 项提名，不是第 {input.NominationIndex} 项");
        }

        if (open.Sweep is not { } sweep)
        {
            return DayOutcome.Reject("day.sweep_not_started", "收票还没有开始：先由说书人点「开始」，再举手 / 放下");
        }

        if (sweep.Collected.Any(vote => vote.Seat == input.Voter))
        {
            return DayOutcome.Reject(
                "day.seat_collected",
                $"分针已经过了 {input.Voter.Value} 号席位：先举也算、过时不候");
        }

        if (!context.Seats.Contains(input.Voter))
        {
            return DayOutcome.Reject("day.voter_unknown", $"席位 {input.Voter.Value} 不在本局座次里");
        }

        var voterLife = context.State.Seat(input.Voter)?.LifeValue;
        if (voterLife is null)
        {
            return DayOutcome.Reject(
                "day.voter_life_unknown",
                $"席位 {input.Voter.Value} 的生死还没有观测：无法判定他还有没有投票权（不猜）");
        }

        if (voterLife == LifeState.Dead && state.HasSpentVoteToken(input.Voter))
        {
            return DayOutcome.Reject(
                "day.vote_token_spent",
                $"席位 {input.Voter.Value} 死后的一次投票权已经用掉了（百科《投票》）");
        }

        // 投票者此刻的角色**快照**随事件落账：卖花女孩按"投票当时"判定恶魔是否参与（R-0037），
        // 之后换角不再改写这条事实（百科《卖花女孩》· 角色简介 4）。未观测就记 null，不猜。
        var voterCharacter = context.State.Seat(input.Voter)?.CharacterValue;

        return DayOutcome.Accepted(
        [
            new VoteCastEvent
            {
                DayNumber = day.DayNumber,
                NominationIndex = open.Index,
                Voter = input.Voter,
                Voted = input.Voted,
                VoterCharacter = voterCharacter,
            },
        ]);
    }

    /// <summary>
    /// 计票：收票**全部完成**后才能计（R-0017 目标形态）；票面 = 逐席冻结结论；
    /// 按「严格最多 + ≥ 存活人数一半 + ≥1 票」判定是否进入「即将被处决」；
    /// 平局或后来者超过会取消原有状态；计票后不再重判（百科《投票》）。
    /// </summary>
    public static DayOutcome CountVotes(DayState state, SettlementContext context, CountVotesInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (state.OpenDay is not { } day)
        {
            return DayOutcome.Reject("day.not_open", "现在不是白天，或白天已经结束");
        }

        var open = day.OpenNomination;
        if (open is null)
        {
            return DayOutcome.Reject("day.no_open_nomination", "现在没有开放投票的提名");
        }

        if (open.Index != input.NominationIndex)
        {
            return DayOutcome.Reject(
                "day.nomination_not_open",
                $"当前开放的是第 {open.Index} 项提名，不是第 {input.NominationIndex} 项");
        }

        if (open.Sweep is not { } sweep)
        {
            return DayOutcome.Reject("day.sweep_not_started", "收票还没有开始：先由说书人点「开始」，不能用旧口径直接计票");
        }

        if (!sweep.IsComplete)
        {
            return DayOutcome.Reject(
                "day.sweep_incomplete",
                "收票还没有走完：等分针走完一圈、每一席都有冻结结论后再计票");
        }

        var alive = 0;
        foreach (var seat in context.Seats)
        {
            var life = context.State.Seat(seat)?.LifeValue;
            if (life is null)
            {
                return DayOutcome.Reject(
                    "day.life_unobserved",
                    $"席位 {seat.Value} 的生死还没有观测：无法判定票数是否达到存活人数的一半（不猜）");
            }

            if (life == LifeState.Alive)
            {
                alive++;
            }
        }

        // 钟盘形态下票面就是逐席冻结结论（先举也算、过时不候）；按席位升序只是让事件形状稳定。
        var voters = open.Ballot.OrderBy(seat => seat.Value).ToArray();
        var votes = voters.Length;

        // 条件 1：今天所有已被提名者中最多（不得并列）——只跟**已计票**的提名比。
        var otherMax = day.Nominations
            .Where(nomination => nomination.Status == NominationStatus.Counted && nomination.Index != open.Index)
            .Select(nomination => nomination.Ballot.Count)
            .DefaultIfEmpty(0)
            .Max();

        // 条件 2：等于或超过存活玩家人数的一半；条件 3：至少 1 票。
        var qualifies = votes > otherMax && votes >= 1 && votes * 2 >= alive;

        var existing = day.AboutToBeExecuted;
        var existingVotes = existing is { } candidate
            ? day.Nominations
                .FirstOrDefault(nomination => nomination.Status == NominationStatus.Counted
                    && nomination.Nominee == candidate)?.Ballot.Count
            : null;

        SeatId? aboutToBeExecuted;
        if (qualifies)
        {
            aboutToBeExecuted = open.Nominee;
        }
        else if (existing is not null && votes >= (existingVotes ?? 0))
        {
            // 原有「即将被处决」者的票数不再是最多（并列或被人超过）→ 取消；本次也未达标 → 当前无人。
            aboutToBeExecuted = null;
        }
        else
        {
            aboutToBeExecuted = existing;
        }

        var spentVoteTokens = voters
            .Where(seat => context.State.Seat(seat)?.LifeValue == LifeState.Dead)
            .ToArray();

        return DayOutcome.Accepted(
        [
            new VoteCountedEvent
            {
                DayNumber = day.DayNumber,
                NominationIndex = open.Index,
                Voters = voters,
                SpentVoteTokens = spentVoteTokens,
                AboutToBeExecuted = aboutToBeExecuted,
            },
        ]);
    }

    /// <summary>
    /// 结束白天：处决当前「即将被处决」者（如果有），并关闭白天。
    /// 处决事件与死亡事件分开产出（处决 ≠ 死亡，百科《处决》）。
    /// </summary>
    public static DayOutcome CloseDay(DayState state, SettlementContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        if (state.OpenDay is not { } day)
        {
            return DayOutcome.Reject("day.not_open", "现在不是白天，或白天已经结束");
        }

        if (day.OpenNomination is not null)
        {
            return DayOutcome.Reject(
                "day.nomination_not_counted",
                "还有提名没有计票：先把票计完（或由说书人强推兜底），再结束白天");
        }

        var events = new List<GameEvent>(capacity: 3);
        if (day.AboutToBeExecuted is { } seat)
        {
            var life = context.State.Seat(seat)?.LifeValue;
            if (life is null)
            {
                return DayOutcome.Reject(
                    "day.executed_life_unknown",
                    $"席位 {seat.Value} 的生死还没有观测：无法判定处决是否产生死亡（不猜）");
            }

            events.Add(new ExecutedEvent
            {
                DayNumber = day.DayNumber,
                Seat = seat,
                Kind = ExecutionKind.Day,
            });

            // 本票没有免死角色：存活者被处决即死亡；已经死亡者只记录"被处决"，不重复记死亡。
            if (life == LifeState.Alive)
            {
                events.Add(new SeatStateChangedEvent
                {
                    Seat = seat,
                    Life = LifeState.Dead,
                    Reason = ExecutionDeathReason,
                });
            }
        }

        events.Add(new DayClosedEvent { DayNumber = day.DayNumber });
        return DayOutcome.Accepted(events);
    }
}
