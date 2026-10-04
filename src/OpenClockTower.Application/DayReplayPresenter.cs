using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>白天流程步骤：开始 / 提名 / 收票（开始 / 逐席 / 继续）/ 计票 / 处决 / 结束（D-0020 步骤目录）。</summary>
internal sealed class DayReplayPresenter : IReplayStepPresenter
{
    /// <inheritdoc />
    public IReadOnlyList<Type> HandledTypes =>
    [
        typeof(DayStartedEvent),
        typeof(NominationMadeEvent),
        typeof(VoteSweepStartedEvent),
        typeof(VoteCastEvent),
        typeof(SeatVoteCollectedEvent),
        typeof(VoteSweepResumedEvent),
        typeof(VoteCountedEvent),
        typeof(ExecutedEvent),
        typeof(DayClosedEvent),
        typeof(ExileProposedEvent),
        typeof(ExileVoteCastEvent),
        typeof(ExileSweepStartedEvent),
        typeof(ExileSeatVoteCollectedEvent),
        typeof(ExileSweepResumedEvent),
        typeof(ExileVoteCountedEvent),
        typeof(DayProtectionDecidedEvent),
        typeof(ExtraNominationWindowOpenedEvent),
        typeof(ExtraNominationMadeEvent),
    ];

    /// <inheritdoc />
    public ReplayStep Present(ReplayStepContext context) => context.Stored.Event switch
    {
        DayStartedEvent started => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"第 {started.DayNumber} 天开始",
        },
        NominationMadeEvent nomination => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"{context.SeatText.Seat(nomination.Nominator)} 提名 {context.SeatText.Seat(nomination.Nominee)}",
            Detail = nomination.NominatorCharacter is { } character
                ? $"提名时提名者角色：{ReplayText.CharacterValue(character)}"
                : null,
        },
        VoteSweepStartedEvent sweepStarted => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"第 {sweepStarted.DayNumber} 天第 {sweepStarted.NominationIndex} 项提名开始收票"
                + $"（倒计时 {sweepStarted.CountdownMilliseconds / 1000.0:0.#}s，间隔 {sweepStarted.IntervalMilliseconds / 1000.0:0.#}s）",
        },
        VoteCastEvent vote => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"{context.SeatText.Seat(vote.Voter)} {(vote.Voted ? "举起手（赞成）" : "放下手（撤回）")}",
            Detail = vote.VoterCharacter is { } character
                ? $"举手时投票者角色：{ReplayText.CharacterValue(character)}"
                : null,
        },
        SeatVoteCollectedEvent collected => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"第 {collected.DayNumber} 天第 {collected.NominationIndex} 项提名收票："
                + $"{context.SeatText.Seat(collected.Seat)} {(collected.Voted ? "举手赞成" : "未举手")}",
            Detail = collected.VoterCharacter is { } character
                ? $"收票时该席位角色：{ReplayText.CharacterValue(character)}"
                : null,
        },
        VoteSweepResumedEvent sweepResumed => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"第 {sweepResumed.DayNumber} 天第 {sweepResumed.NominationIndex} 项提名继续收票"
                + "（重新起倒计时，从下一未收席位接着收）",
        },
        VoteCountedEvent counted => PresentCounted(context, counted),
        ExecutedEvent executed => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = executed.DayNumber is null ? ReplayStepKind.Trigger : ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"{context.SeatText.Seat(executed.Seat)} 被处决（{ReplayText.Execution(executed.Kind)}）"
                + (executed.DayNumber is { } day ? $" · 第 {day} 天" : " · 夜晚形态"),
            Detail = executed.Note,
        },
        DayClosedEvent closed => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"第 {closed.DayNumber} 天结束",
        },

        // 流放（票据 traveller-and-exile · D2）：与提名同族但各自成步（流放不是提名 / 投票 / 处决，
        // R-0044 第 1 条）；圆盘在目标席位上画流放标记（D7）。
        ExileProposedEvent proposed => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"{context.SeatText.Seat(proposed.Proposer)} 提议流放 {context.SeatText.Seat(proposed.Target)}"
                + $"（第 {proposed.DayNumber} 天第 {proposed.ExileIndex} 条）",
            Markers =
            [
                new ReplayMarker
                {
                    Kind = "exile",
                    Seat = proposed.Target,
                    Text = $"由 {context.SeatText.Seat(proposed.Proposer)} 提议",
                },
            ],
        },
        ExileSweepStartedEvent exileSweepStarted => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"第 {exileSweepStarted.DayNumber} 天第 {exileSweepStarted.ExileIndex} 条流放开始收票"
                + $"（倒计时 {exileSweepStarted.CountdownMilliseconds / 1000.0:0.#}s，"
                + $"间隔 {exileSweepStarted.IntervalMilliseconds / 1000.0:0.#}s）",
        },
        ExileVoteCastEvent exileVote => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"{context.SeatText.Seat(exileVote.Voter)} "
                + $"{(exileVote.Voted ? "举起手（赞成流放）" : "放下手（撤回）")}",
        },
        ExileSeatVoteCollectedEvent exileCollected => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"第 {exileCollected.DayNumber} 天第 {exileCollected.ExileIndex} 条流放收票："
                + $"{context.SeatText.Seat(exileCollected.Seat)} {(exileCollected.Voted ? "举手赞成" : "未举手")}",
        },
        ExileSweepResumedEvent exileResumed => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"第 {exileResumed.DayNumber} 天第 {exileResumed.ExileIndex} 条流放继续收票"
                + "（重新起倒计时，从下一未收席位接着收）",
        },
        ExileVoteCountedEvent exileCounted => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"第 {exileCounted.DayNumber} 天第 {exileCounted.ExileIndex} 条流放计票："
                + $"{exileCounted.Voters.Count} 票，"
                + exileCounted.Conclusion switch
                {
                    ExileConclusion.Exiled => "流放成立（目标死亡）",
                    ExileConclusion.Protected => "达线但受死亡保护，目标存活",
                    _ => "未达线，目标存活",
                },
            Detail = exileCounted.Voters.Count == 0
                ? null
                : $"赞成：{context.SeatText.SeatList(exileCounted.Voters)}",
        },

        // 死亡保护裁定（D3 / R-0048）：说书人对某席位「今天的死亡保护」的裁定，进复盘。
        // 只有「受保护」才是圆盘上要记住的状态；「不受保护」不画标记（D7）。
        DayProtectionDecidedEvent protectionDecided => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"第 {protectionDecided.DayNumber} 天死亡保护裁定："
                + $"{context.SeatText.Seat(protectionDecided.Seat)} "
                + (protectionDecided.Protected ? "今天受保护（不因流放死亡）" : "不受保护"),
            Detail = protectionDecided.Note,
            Markers = protectionDecided.Protected
                ?
                [
                    new ReplayMarker
                    {
                        Kind = "protected",
                        Seat = protectionDecided.Seat,
                        Text = "今天受死亡保护",
                    },
                ]
                : [],
        },
        // 屠夫窗口（D4 / R-0050）：首次处决后开窗、窗口内由屠夫本人额外提名，各成一步。
        ExtraNominationWindowOpenedEvent windowOpened => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"第 {windowOpened.DayNumber} 天首次处决后打开额外提名窗口："
                + $"{context.SeatText.Seat(windowOpened.Seat)}（屠夫）可以再次发起提名",
            Markers =
            [
                new ReplayMarker
                {
                    Kind = "extra-nomination",
                    Seat = windowOpened.Seat,
                    Text = "额外提名窗口",
                },
            ],
        },
        ExtraNominationMadeEvent extraMade => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"{context.SeatText.Seat(extraMade.Nominator)} 额外提名 {context.SeatText.Seat(extraMade.Nominee)}"
                + $"（第 {extraMade.DayNumber} 天第 {extraMade.NominationIndex} 项，屠夫窗口）",
            Detail = extraMade.NominatorCharacter is { } character
                ? $"提名时提名者角色：{ReplayText.CharacterValue(character)}"
                : null,
        },
        _ => throw new InvalidOperationException(
            $"DayReplayPresenter 不认领事件 {context.Stored.Event.GetType().Name}"),
    };

    private static ReplayStep PresentCounted(ReplayStepContext context, VoteCountedEvent counted)
    {
        var details = new List<string>();
        if (counted.Voters.Count > 0)
        {
            details.Add($"赞成：{context.SeatText.SeatList(counted.Voters)}");
        }

        if (counted.SpentVoteTokens.Count > 0)
        {
            details.Add($"用掉死亡玩家的一次性投票权：{context.SeatText.SeatList(counted.SpentVoteTokens)}");
        }

        return new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"第 {counted.DayNumber} 天计票（第 {counted.NominationIndex} 项提名）："
                + $"{counted.Voters.Count} 票"
                + (counted.AboutToBeExecuted is { } aboutToDie
                    ? $"，{context.SeatText.Seat(aboutToDie)} 即将被处决"
                    : "，无人被处决"),
            Detail = details.Count == 0 ? null : string.Join("；", details),
        };
    }
}
