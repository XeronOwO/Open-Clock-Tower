using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 选择步骤：玩家操作请求（发出 / 作答 / 作废）、说书人裁定点（开出 / 结清）、艺术家提问（R-0040）。
/// </summary>
internal sealed class ChoiceReplayPresenter : IReplayStepPresenter
{
    /// <inheritdoc />
    public IReadOnlyList<Type> HandledTypes =>
    [
        typeof(OperationRequestIssuedEvent),
        typeof(OperationRequestAnsweredEvent),
        typeof(OperationRequestVoidedEvent),
        typeof(DecisionPointRaisedEvent),
        typeof(DecisionPointResolvedEvent),
        typeof(ArtistQuestionAskedEvent),
        typeof(ArtistQuestionClosedEvent),
        typeof(SavantQuestionAskedEvent),
        typeof(SavantQuestionClosedEvent),
    ];

    /// <inheritdoc />
    public ReplayStep Present(ReplayStepContext context) => context.Stored.Event switch
    {
        OperationRequestIssuedEvent issued => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Request,
            Phase = context.Phase,
            Summary = $"{context.SeatText.Seat(issued.Request.Addressee)} 被要求做出选择",
            Detail = issued.Request.Prompt.Context
                + (issued.Request.Prompt.HasSecondDimension
                    ? $"（两维选择：{issued.Request.Prompt.Options.Count} × "
                        + $"{issued.Request.Prompt.SecondaryOptions.Count} 个合法组合）"
                    : $"（{issued.Request.Prompt.Options.Count} 个合法选项）"),
        },
        OperationRequestAnsweredEvent answered => PresentAnswered(context, answered),
        OperationRequestVoidedEvent voided => PresentVoided(context, voided),
        DecisionPointRaisedEvent raised => PresentRaised(context, raised),
        DecisionPointResolvedEvent resolved => PresentResolved(context, resolved),
        ArtistQuestionAskedEvent asked => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Request,
            Phase = context.Phase,
            Summary = $"{context.SeatText.Seat(asked.Seat)} 向说书人提问",
            Detail = $"「{asked.Question}」；提问时角色：{ReplayText.Character(asked.Character)}",
        },
        ArtistQuestionClosedEvent closed => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Request,
            Phase = context.Phase,
            Summary = $"艺术家提问结清：{ReplayText.ArtistClosure(closed.Closure)}",
        },
        SavantQuestionAskedEvent savantAsked => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Request,
            Phase = context.Phase,
            Summary = $"{context.SeatText.Seat(savantAsked.Seat)} 向说书人要两条信息",
            Detail = $"私密请求（内容由说书人给，一真一假）；请求时角色：{ReplayText.Character(savantAsked.Character)}",
        },
        SavantQuestionClosedEvent savantClosed => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Request,
            Phase = context.Phase,
            Summary = $"博学者提问结清：{ReplayText.SavantClosure(savantClosed.Closure)}",
        },
        _ => throw new InvalidOperationException(
            $"ChoiceReplayPresenter 不认领事件 {context.Stored.Event.GetType().Name}"),
    };

    private static ReplayStep PresentAnswered(ReplayStepContext context, OperationRequestAnsweredEvent answered)
    {
        var request = PendingRequestOf(context.MachineBefore, answered.RequestId);
        var details = new List<string>();
        if (answered.Answer.Source == ResponseSource.StorytellerProxy)
        {
            details.Add("由说书人代填");
        }

        if (!string.IsNullOrEmpty(answered.Answer.Note))
        {
            details.Add(answered.Answer.Note);
        }

        return new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Request,
            Phase = context.Phase,
            Summary = $"{context.SeatText.Seat(request?.Addressee)} 完成选择："
                + ReplayText.Option(answered.Answer.OptionValue, context.SeatText),
            Detail = details.Count == 0 ? null : string.Join("；", details),
        };
    }

    private static ReplayStep PresentVoided(ReplayStepContext context, OperationRequestVoidedEvent voided)
    {
        var request = PendingRequestOf(context.MachineBefore, voided.RequestId);
        var details = new List<string>();
        if (!string.IsNullOrEmpty(voided.Void.Note))
        {
            details.Add(voided.Void.Note);
        }

        details.Add($"原因分类 {voided.Void.Reason}");

        return new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Request,
            Phase = context.Phase,
            Summary = $"{context.SeatText.Seat(request?.Addressee)} 的选择被作废",
            Detail = string.Join("；", details),
        };
    }

    private static ReplayStep PresentRaised(ReplayStepContext context, DecisionPointRaisedEvent raised)
    {
        var details = new List<string> { raised.DecisionPoint.Prompt.Context };

        if (raised.DecisionPoint.Prompt.HasSecondDimension)
        {
            details.Add(
                $"{raised.DecisionPoint.Prompt.Options.Count} × "
                + $"{raised.DecisionPoint.Prompt.SecondaryOptions.Count} 个合法组合");
        }
        else
        {
            details.Add($"{raised.DecisionPoint.Prompt.Options.Count} 个合法选项");
        }

        if (raised.AttributionSeat is { } attribution)
        {
            details.Add($"归属 {context.SeatText.Seat(attribution)}");
        }

        if (raised.TriggerAbility is { } trigger)
        {
            details.Add($"触发能力 {ReplayText.Ability(trigger)}");
        }

        return new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Decision,
            Phase = context.Phase,
            Summary = "说书人裁定点开出",
            Detail = string.Join("；", details),
        };
    }

    private static ReplayStep PresentResolved(ReplayStepContext context, DecisionPointResolvedEvent resolved)
    {
        var awaiting = context.MachineBefore?.AwaitingDecision;
        var match = awaiting is not null && awaiting.Id == resolved.DecisionPointId ? awaiting : null;

        var details = new List<string>();
        if (match is not null)
        {
            details.Add($"裁定点：{match.Prompt.Context}");
        }

        if (!string.IsNullOrEmpty(resolved.Note))
        {
            details.Add(resolved.Note);
        }

        return new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Decision,
            Phase = context.Phase,
            Summary = resolved.Decision is null
                ? "说书人裁定点以「未选择」了结"
                : $"说书人裁定：{ReplayText.Option(resolved.Decision, context.SeatText)}",
            Detail = details.Count == 0 ? null : string.Join("；", details),
        };
    }

    /// <summary>折叠前挂起的请求里找到指定 id 的那一条；不属于当前挂起返回 null。</summary>
    private static OperationRequest? PendingRequestOf(StepMachineState? machine, OperationRequestId requestId) =>
        machine?.PendingRequest is { } pending && pending.Id == requestId ? pending : null;
}
