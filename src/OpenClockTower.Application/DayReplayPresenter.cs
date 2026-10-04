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
