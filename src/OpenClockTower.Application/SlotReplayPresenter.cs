using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 槽位步骤：激活（含换手重绑，R-0032）/ 阻塞 / 解除 / 可归因跳过（D-0020 步骤目录）。
/// </summary>
internal sealed class SlotReplayPresenter : IReplayStepPresenter
{
    /// <inheritdoc />
    public IReadOnlyList<Type> HandledTypes =>
    [
        typeof(SlotActivatedEvent),
        typeof(SlotInsertedEvent),
        typeof(SlotBlockedEvent),
        typeof(SlotUnblockedEvent),
        typeof(PromptSkippedEvent),
    ];

    /// <inheritdoc />
    public ReplayStep Present(ReplayStepContext context) => context.Stored.Event switch
    {
        SlotActivatedEvent activated => PresentActivated(context, activated),
        SlotInsertedEvent inserted => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Slot,
            Phase = context.Phase,
            Summary = $"{context.SeatText.Seat(inserted.Slot.Actor!)} 被唤醒"
                + "（获得的『首个夜晚』能力在顺序表上没有位置，追加结算）",
            Detail = inserted.Slot.Prompt?.Context,
        },
        SlotBlockedEvent blocked => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Slot,
            Phase = context.Phase,
            Summary = $"本步阻塞：{blocked.Reason}",
        },
        SlotUnblockedEvent unblocked => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Slot,
            Phase = context.Phase,
            Summary = $"阻塞解除：{unblocked.Reason}",
        },
        PromptSkippedEvent skipped => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Slot,
            Phase = context.Phase,
            Summary = "本步无合法选项，按声明跳过",
            Detail = skipped.Reason,
        },
        _ => throw new InvalidOperationException(
            $"SlotReplayPresenter 不认领事件 {context.Stored.Event.GetType().Name}"),
    };

    private static ReplayStep PresentActivated(ReplayStepContext context, SlotActivatedEvent activated)
    {
        var previousActor = SlotActorOf(context.MachineBefore, activated.SlotIndex);
        var isRebind = previousActor is { } old && old != activated.Actor;

        var markers = new List<ReplayMarker>();
        if (isRebind)
        {
            markers.Add(new ReplayMarker
            {
                Kind = "role-rebind",
                Seat = activated.Actor,
                From = previousActor,
                To = activated.Actor,
                Text = $"原行动者 {context.SeatText.Seat(previousActor)} → {context.SeatText.Seat(activated.Actor)}",
            });
        }

        return new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Slot,
            Phase = context.Phase,
            Summary = isRebind
                ? $"换手重绑：{context.SeatText.Seat(activated.Actor)} 接手尚未进入的槽位"
                : $"{context.SeatText.Seat(activated.Actor)} 被唤醒",
            Detail = activated.Prompt.Context,
            Markers = markers,
        };
    }

    /// <summary>折叠前该槽位的行动者；越界 / 未开局返回 null。</summary>
    private static SeatId? SlotActorOf(StepMachineState? machine, int slotIndex)
    {
        if (machine is null || slotIndex < 0 || slotIndex >= machine.Plan.Slots.Count)
        {
            return null;
        }

        return machine.Plan.Slots[slotIndex].Actor;
    }
}
