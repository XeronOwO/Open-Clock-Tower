namespace OpenClockTower.Kernel;

/// <summary>
/// 流放流程的纯迁移：提议 / 钟盘收票 / 计票与死亡收口（票据 traveller-and-exile · D2）。
/// </summary>
/// <remarks>
/// <para>
/// 依据 <c>docs/standard/rulings.md</c> R-0044（三分流、分母与阈值、公开面、能力边界）与 R-0045
/// （旅行者死亡的后果面）；收票机制与提名共用 <see cref="BallotSweep"/>，节奏 / 中断沿用 R-0017。
/// </para>
/// <para>
/// **纯计算**（D-0008）：生死与角色事实从 <paramref name="context"/> 的账读，不猜；
/// 观测不齐就拒绝整条输入（D-0015）。
/// </para>
/// </remarks>
public static class ExileMachine
{
    /// <summary>流放致死的机器可读原因（写进 <see cref="SeatStateChangedEvent.Reason"/>）。</summary>
    public const string ExileDeathReason = "day.exile";

    /// <summary>
    /// 发起流放：任何在局玩家（含死者）随时可提；目标必须是在局旅行者；每名旅行者每个白天至多一次。
    /// </summary>
    public static DayOutcome Propose(DayState state, SettlementContext context, ProposeExileInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (state.OpenDay is not { } day)
        {
            return DayOutcome.Reject("day.not_open", "现在不是白天，或白天已经结束");
        }

        if (day.OpenExile is not null)
        {
            return DayOutcome.Reject(
                "day.exile_in_progress",
                "已经有一条流放没有结清：先把它收完、计票，再提出下一条（同日多条顺序进行）");
        }

        if (!context.Seats.Contains(input.Proposer))
        {
            return DayOutcome.Reject(
                "day.exile_proposer_not_in_game",
                $"席位 {input.Proposer.Value} 不在本局在局座次里：流放只能由在局玩家发起（含死者，R-0044 第 2 条）");
        }

        if (!context.Seats.Contains(input.Target))
        {
            return DayOutcome.Reject(
                "day.exile_target_not_in_game",
                $"席位 {input.Target.Value} 不在本局在局座次里：流放目标必须是在局旅行者");
        }

        var targetCharacter = context.State.Seat(input.Target)?.CharacterValue;
        if (targetCharacter is null)
        {
            return DayOutcome.Reject(
                "day.exile_target_character_unknown",
                $"席位 {input.Target.Value} 的角色还没有观测：无法判定他是不是旅行者（不猜）");
        }

        if (context.Characters is not { } characters)
        {
            return DayOutcome.Reject(
                "day.exile_character_facts_missing",
                "本批没有角色事实端口：无法判定目标是不是旅行者（不猜）");
        }

        if (!characters.IsTraveller(targetCharacter.Value))
        {
            return DayOutcome.Reject(
                "day.exile_target_not_traveller",
                $"席位 {input.Target.Value} 的角色 {targetCharacter.Value.Value} 不是旅行者："
                    + "流放只能针对旅行者（R-0044 第 2 条）");
        }

        if (day.HasExileProposed(input.Target))
        {
            return DayOutcome.Reject(
                "day.exile_already_proposed",
                $"席位 {input.Target.Value} 今天已经被提议过流放：每名旅行者每个白天只能被提议一次"
                    + "（成败都算，R-0044 第 3 条）");
        }

        return DayOutcome.Accepted(
        [
            new ExileProposedEvent
            {
                DayNumber = day.DayNumber,
                ExileIndex = day.Exiles.Count + 1,
                Proposer = input.Proposer,
                Target = input.Target,
            },
        ]);
    }

    /// <summary>说书人开始流放收票：进入倒计时，随后分针按席位升序逐席冻结结论（R-0044 第 10 条）。</summary>
    public static DayOutcome StartSweep(DayState state, SettlementContext context, StartExileSweepInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (state.OpenDay is not { } day)
        {
            return DayOutcome.Reject("day.not_open", "现在不是白天，或白天已经结束");
        }

        if (day.OpenExile is not { } exile)
        {
            return DayOutcome.Reject("day.no_open_exile", "现在没有开放表决的流放提议");
        }

        if (exile.Index != input.ExileIndex)
        {
            return DayOutcome.Reject(
                "day.exile_not_open",
                $"当前开放的是第 {exile.Index} 条流放，不是第 {input.ExileIndex} 条");
        }

        if (BallotSweep.CheckStart(
                exile.Sweep,
                context.Seats,
                $"第 {exile.Index} 条流放",
                input.CountdownMilliseconds,
                input.IntervalMilliseconds) is { } invalid)
        {
            return DayOutcome.Reject(invalid.Code, invalid.Note);
        }

        if (BallotSweep.CheckDialFree(day, BallotKind.Exile, exile.Index) is { } busy)
        {
            return DayOutcome.Reject(busy.Code, busy.Note);
        }

        return DayOutcome.Accepted(
        [
            new ExileSweepStartedEvent
            {
                DayNumber = day.DayNumber,
                ExileIndex = exile.Index,
                // 收票顺序 = 开始那一刻的在局座次（既是顺序也是阈值分母，R-0044 第 6 条）。
                Seats = BallotSweep.OrderSeats(context.Seats),
                CountdownMilliseconds = input.CountdownMilliseconds,
                IntervalMilliseconds = input.IntervalMilliseconds,
            },
        ]);
    }

    /// <summary>收流放第 N 席的票：严格时点冻结"已登记的举手状态"（先举也算、过时不候）。</summary>
    public static DayOutcome CollectSeatVote(DayState state, SettlementContext context, CollectExileSeatVoteInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (state.OpenDay is not { } day)
        {
            return DayOutcome.Reject("day.not_open", "现在不是白天，或白天已经结束");
        }

        if (day.OpenExile is not { } exile)
        {
            return DayOutcome.Reject("day.no_open_exile", "现在没有开放表决的流放提议");
        }

        if (exile.Index != input.ExileIndex)
        {
            return DayOutcome.Reject(
                "day.exile_not_open",
                $"当前开放的是第 {exile.Index} 条流放，不是第 {input.ExileIndex} 条");
        }

        if (BallotSweep.CheckCollect(exile.Sweep, input.Seat) is { } invalid)
        {
            return DayOutcome.Reject(invalid.Code, invalid.Note);
        }

        return DayOutcome.Accepted(
        [
            new ExileSeatVoteCollectedEvent
            {
                DayNumber = day.DayNumber,
                ExileIndex = exile.Index,
                Seat = input.Seat,
                Voted = exile.HandsRaised.Contains(input.Seat),
            },
        ]);
    }

    /// <summary>继续中断的流放收票：重新起倒计时，从下一未收席位接着收（R-0017 第 7 条）。</summary>
    public static DayOutcome ResumeSweep(DayState state, SettlementContext context, ResumeExileSweepInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (state.OpenDay is not { } day)
        {
            return DayOutcome.Reject("day.not_open", "现在不是白天，或白天已经结束");
        }

        if (day.OpenExile is not { } exile)
        {
            return DayOutcome.Reject("day.no_open_exile", "现在没有开放表决的流放提议");
        }

        if (exile.Index != input.ExileIndex)
        {
            return DayOutcome.Reject(
                "day.exile_not_open",
                $"当前开放的是第 {exile.Index} 条流放，不是第 {input.ExileIndex} 条");
        }

        if (BallotSweep.CheckResume(exile.Sweep) is { } invalid)
        {
            return DayOutcome.Reject(invalid.Code, invalid.Note);
        }

        return DayOutcome.Accepted(
        [
            new ExileSweepResumedEvent
            {
                DayNumber = day.DayNumber,
                ExileIndex = exile.Index,
            },
        ]);
    }

    /// <summary>
    /// 举手 / 放下：只在流放收票开始后、本席被收票之前有效（先举也算、过时不候）。
    /// 在局玩家（含死者）都可表决；死者不查也不耗「死后仅一次」投票标记（R-0044 第 1 / 4 条）。
    /// </summary>
    public static DayOutcome CastVote(DayState state, SettlementContext context, CastExileVoteInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (state.OpenDay is not { } day)
        {
            return DayOutcome.Reject("day.not_open", "现在不是白天，或白天已经结束");
        }

        if (day.OpenExile is not { } exile)
        {
            return DayOutcome.Reject("day.no_open_exile", "现在没有开放表决的流放提议");
        }

        if (exile.Index != input.ExileIndex)
        {
            return DayOutcome.Reject(
                "day.exile_not_open",
                $"当前开放的是第 {exile.Index} 条流放，不是第 {input.ExileIndex} 条");
        }

        if (exile.Sweep is not { } sweep)
        {
            return DayOutcome.Reject("day.sweep_not_started", "流放收票还没有开始：先由说书人点「开始」，再举手 / 放下");
        }

        if (sweep.Collected.Any(vote => vote.Seat == input.Voter))
        {
            return DayOutcome.Reject(
                "day.seat_collected",
                $"分针已经过了 {input.Voter.Value} 号席位：先举也算、过时不候");
        }

        if (!sweep.Seats.Contains(input.Voter))
        {
            return DayOutcome.Reject(
                "day.exile_voter_not_on_ballot",
                $"席位 {input.Voter.Value} 不在本次流放收票的名单里（名单 = 开始收票时的在局座次）");
        }

        if (!context.Seats.Contains(input.Voter))
        {
            return DayOutcome.Reject(
                "day.exile_voter_not_in_game",
                $"席位 {input.Voter.Value} 已经离场：离场者不参与流放表决（R-0044 第 6 条）");
        }

        return DayOutcome.Accepted(
        [
            new ExileVoteCastEvent
            {
                DayNumber = day.DayNumber,
                ExileIndex = exile.Index,
                Voter = input.Voter,
                Voted = input.Voted,
            },
        ]);
    }

    /// <summary>
    /// 计票：收票**全部完成**后才能计（R-0017 目标形态）；达线 = 赞成票 × 2 ≥ 开始收票时的在局人数
    /// （R-0044 第 5 / 6 条）；不与当日其他结果比较。达线且目标存活 → 即时死亡（真实死亡，R-0045）。
    /// </summary>
    public static DayOutcome CountVotes(DayState state, SettlementContext context, CountExileVotesInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (state.OpenDay is not { } day)
        {
            return DayOutcome.Reject("day.not_open", "现在不是白天，或白天已经结束");
        }

        if (day.OpenExile is not { } exile)
        {
            return DayOutcome.Reject("day.no_open_exile", "现在没有开放表决的流放提议");
        }

        if (exile.Index != input.ExileIndex)
        {
            return DayOutcome.Reject(
                "day.exile_not_open",
                $"当前开放的是第 {exile.Index} 条流放，不是第 {input.ExileIndex} 条");
        }

        if (exile.Sweep is not { } sweep)
        {
            return DayOutcome.Reject("day.sweep_not_started", "流放收票还没有开始：先由说书人点「开始」，不能直接计票");
        }

        if (!sweep.IsComplete)
        {
            return DayOutcome.Reject(
                "day.sweep_incomplete",
                "流放收票还没有走完：等分针走完一圈、每一席都有冻结结论后再计票");
        }

        var voters = exile.Ballot.OrderBy(seat => seat.Value).ToArray();
        var reached = voters.Length * 2 >= sweep.Seats.Count;

        var conclusion = reached ? ExileConclusion.Exiled : ExileConclusion.VotesInsufficient;
        SeatStateChangedEvent? death = null;

        if (reached)
        {
            if (!context.Seats.Contains(exile.Target))
            {
                // 目标在收票途中离场：死人复活没有意义，本次流放无法产生死亡。正常入口由
                // TravellerCommandDispatch 的「流放未结清不离场」闸拦住，这里只是防御性显式拒绝。
                return DayOutcome.Reject(
                    "day.exile_target_left",
                    $"席位 {exile.Target.Value} 已经离场：先把它请回局内，或撤回本次流放（不静默了结）");
            }

            var life = context.State.Seat(exile.Target)?.LifeValue;
            if (life is null)
            {
                return DayOutcome.Reject(
                    "day.exile_target_life_unknown",
                    $"席位 {exile.Target.Value} 的生死还没有观测：无法判定流放是否产生死亡（不猜）");
            }

            // 目标已死时只记结论、不重复记死亡（与 CloseDay 对已死者的处决口径一致）。
            if (life == LifeState.Alive)
            {
                // 统一死亡保护查询（R-0048）：受保护 → 目标存活、结论记「受保护」；
                // 待裁定 / 判定不了 → 显式拒绝——先把裁定 / 观测补齐，再重新计票（不猜、不静默死亡）。
                var protection = DeathProtectionQuery.Resolve(context, day, exile.Target, DeathProtectionCause.Exile);
                switch (protection.Outcome)
                {
                    case DeathProtectionOutcome.Protected:
                        conclusion = ExileConclusion.Protected;
                        break;
                    case DeathProtectionOutcome.NeedsRuling:
                        return DayOutcome.Reject("day.exile_protection_required", protection.Note);
                    case DeathProtectionOutcome.Indeterminate:
                        return DayOutcome.Reject(
                            "day.exile_protection_indeterminate",
                            $"{protection.Note}（先补观测，再重新计票；R-0048）");
                    default:
                        death = new SeatStateChangedEvent
                        {
                            Seat = exile.Target,
                            Life = LifeState.Dead,
                            Reason = ExileDeathReason,
                        };
                        break;
                }
            }
        }

        var events = new List<GameEvent>(capacity: 2)
        {
            new ExileVoteCountedEvent
            {
                DayNumber = day.DayNumber,
                ExileIndex = exile.Index,
                Voters = voters,
                Conclusion = conclusion,
            },
        };

        if (death is not null)
        {
            events.Add(death);
        }

        return DayOutcome.Accepted(events);
    }
}
