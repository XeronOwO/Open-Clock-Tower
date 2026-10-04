using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>阶段边界步骤（D-0020 步骤目录：<c>PhaseStarted</c> / <c>PhaseCompleted</c>）。</summary>
internal sealed class PhaseReplayPresenter : IReplayStepPresenter
{
    /// <inheritdoc />
    public IReadOnlyList<Type> HandledTypes => [typeof(PhaseStartedEvent), typeof(PhaseCompletedEvent)];

    /// <inheritdoc />
    public ReplayStep Present(ReplayStepContext context) => context.Stored.Event switch
    {
        PhaseStartedEvent started => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Phase,
            Phase = started.Plan.Phase,
            Summary = $"{ReplayText.Phase(started.Plan.Phase)}开始",
            Detail = $"计划 {started.Plan.Label}（{started.Plan.Slots.Count} 个槽位）"
                + (string.IsNullOrEmpty(started.Plan.Variant) ? string.Empty : $"；顺序口径 {started.Plan.Variant}"),
        },
        PhaseCompletedEvent completed => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Phase,
            Phase = context.Phase,
            Summary = $"阶段结束（{completed.PlanLabel}）",
        },
        _ => throw new InvalidOperationException(
            $"PhaseReplayPresenter 不认领事件 {context.Stored.Event.GetType().Name}"),
    };
}
