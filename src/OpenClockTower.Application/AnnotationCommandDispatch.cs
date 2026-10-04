using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 说书人注记（D-0019）的命令产出：文本归一化、标识签发与改 / 删目标定位。
/// </summary>
/// <remarks>
/// 从 <see cref="GameCommandDispatcher"/> 拆出（单文件 600 行门禁）：注记**不改步骤机、不改状态账**，
/// 与开局分配 / 白天流程不是同一族——分派器只回答"这条命令走哪条域"，注记族整族搬到这里。
/// 文本归一化与合法性闸共用同一把尺子 <see cref="SeatAnnotationText.TryNormalize"/>（防止闸与内核两套口径）。
/// </remarks>
internal static class AnnotationCommandDispatch
{
    /// <summary>分派一条注记命令；步骤机状态原样透传（注记不参与任何阶段推进）。</summary>
    internal static CommandDispatchResult Dispatch(
        GameCommand command,
        StepMachineState? machine,
        SeatAnnotationLedger annotations) =>
        command switch
        {
            AddSeatAnnotationCommand add => Add(add, machine, annotations),
            UpdateSeatAnnotationCommand update => Update(update, machine, annotations),
            RemoveSeatAnnotationCommand remove => Remove(remove, machine, annotations),
            _ => CommandDispatchResult.Rejected(new CommandRejection
            {
                Code = "kernel.unsupported",
                Message = $"未支持的注记命令：{command.GetType().Name}",
                Gate = "kernel",
            }),
        };

    private static CommandDispatchResult Add(
        AddSeatAnnotationCommand command,
        StepMachineState? machine,
        SeatAnnotationLedger annotations)
    {
        if (!SeatAnnotationText.TryNormalize(command.Text, out var normalized, out _))
        {
            return TextRejected();
        }

        return new CommandDispatchResult(
            machine,
            [
                new SeatAnnotationAddedEvent
                {
                    Annotation = new SeatAnnotation(annotations.NextId, command.Seat, normalized),
                },
            ],
            null);
    }

    private static CommandDispatchResult Update(
        UpdateSeatAnnotationCommand command,
        StepMachineState? machine,
        SeatAnnotationLedger annotations)
    {
        if (annotations.Find(command.Id) is not { } existing)
        {
            return TargetMissing(command.Id);
        }

        if (!SeatAnnotationText.TryNormalize(command.Text, out var normalized, out _))
        {
            return TextRejected();
        }

        return new CommandDispatchResult(
            machine,
            [
                new SeatAnnotationUpdatedEvent
                {
                    Annotation = existing with { Text = normalized },
                },
            ],
            null);
    }

    private static CommandDispatchResult Remove(
        RemoveSeatAnnotationCommand command,
        StepMachineState? machine,
        SeatAnnotationLedger annotations)
    {
        if (annotations.Find(command.Id) is not { } existing)
        {
            return TargetMissing(command.Id);
        }

        return new CommandDispatchResult(
            machine,
            [new SeatAnnotationRemovedEvent { Annotation = existing }],
            null);
    }

    /// <summary>
    /// 文本不合规的兜底拒绝：合法性闸（<see cref="CommandGatePipeline"/>）本应先拦下，
    /// 这里保留一条显式失败，避免"闸门漏了"变成静默写入（防御性，不重复文案）。
    /// </summary>
    private static CommandDispatchResult TextRejected() =>
        CommandDispatchResult.Rejected(new CommandRejection
        {
            Code = "legality.annotation_invalid",
            Message = "注记文本不合规（空 / 超长 / 含控制字符）",
            Gate = "legality",
        });

    /// <summary>注记不存在（已被删除）的兜底拒绝：合法性闸本应先拦下。</summary>
    private static CommandDispatchResult TargetMissing(SeatAnnotationId id) =>
        CommandDispatchResult.Rejected(new CommandRejection
        {
            Code = "legality.annotation_unknown",
            Message = $"注记 {id} 不存在（可能已被删除）",
            Gate = "legality",
        });
}
