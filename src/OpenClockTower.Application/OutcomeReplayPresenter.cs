using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>终局步骤：胜负结论（R-0024）。</summary>
internal sealed class OutcomeReplayPresenter : IReplayStepPresenter
{
    /// <inheritdoc />
    public IReadOnlyList<Type> HandledTypes => [typeof(GameEndedEvent)];

    /// <inheritdoc />
    public ReplayStep Present(ReplayStepContext context)
    {
        if (context.Stored.Event is not GameEndedEvent ended)
        {
            throw new InvalidOperationException(
                $"OutcomeReplayPresenter 不认领事件 {context.Stored.Event.GetType().Name}");
        }

        return new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Outcome,
            Phase = context.Phase,
            Summary = $"{ReplayText.Alignment(ended.Winner)}获胜（{ReplayText.Outcome(ended.Condition)}）",
            Detail = ended.Detail,
        };
    }
}
