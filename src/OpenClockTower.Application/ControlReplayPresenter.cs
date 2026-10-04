using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>控制模式变化步骤：说书人接管 / 交还（D-0014；审计事实，复盘如实呈现）。</summary>
internal sealed class ControlReplayPresenter : IReplayStepPresenter
{
    /// <inheritdoc />
    public IReadOnlyList<Type> HandledTypes => [typeof(ControlModeChangedEvent)];

    /// <inheritdoc />
    public ReplayStep Present(ReplayStepContext context)
    {
        if (context.Stored.Event is not ControlModeChangedEvent changed)
        {
            throw new InvalidOperationException(
                $"ControlReplayPresenter 不认领事件 {context.Stored.Event.GetType().Name}");
        }

        return new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Control,
            Phase = context.Phase,
            Summary = changed.Mode == ControlMode.Automatic ? "说书人交还自动化控制" : "说书人接管控制",
            Detail = changed.Reason,
        };
    }
}
