using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>白天流程步骤：开始 / 提名 / 投票 / 计票 / 处决 / 结束（D-0020 步骤目录）。</summary>
internal sealed class DayReplayPresenter : IReplayStepPresenter
{
    /// <inheritdoc />
    public IReadOnlyList<Type> HandledTypes =>
    [
        typeof(DayStartedEvent),
        typeof(NominationMadeEvent),
        typeof(VoteCastEvent),
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
            Summary = $"{ReplayText.Seat(nomination.Nominator)} 提名 {ReplayText.Seat(nomination.Nominee)}",
            Detail = nomination.NominatorCharacter is { } character
                ? $"提名时提名者角色：{ReplayText.CharacterValue(character)}"
                : null,
        },
        VoteCastEvent vote => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"{ReplayText.Seat(vote.Voter)} {(vote.Voted ? "投出赞成票" : "撤回 / 取消赞成")}",
            Detail = vote.VoterCharacter is { } character
                ? $"投票时投票者角色：{ReplayText.CharacterValue(character)}"
                : null,
        },
        VoteCountedEvent counted => PresentCounted(context, counted),
        ExecutedEvent executed => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = executed.DayNumber is null ? ReplayStepKind.Trigger : ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"{ReplayText.Seat(executed.Seat)} 被处决（{ReplayText.Execution(executed.Kind)}）"
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
            details.Add($"赞成：{ReplayText.SeatList(counted.Voters)}");
        }

        if (counted.SpentVoteTokens.Count > 0)
        {
            details.Add($"用掉死亡玩家的一次性投票权：{ReplayText.SeatList(counted.SpentVoteTokens)}");
        }

        return new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Day,
            Phase = GamePhase.Day,
            Summary = $"第 {counted.DayNumber} 天计票（第 {counted.NominationIndex} 项提名）："
                + $"{counted.Voters.Count} 票"
                + (counted.AboutToBeExecuted is { } aboutToDie
                    ? $"，{ReplayText.Seat(aboutToDie)} 即将被处决"
                    : "，无人被处决"),
            Detail = details.Count == 0 ? null : string.Join("；", details),
        };
    }
}
