using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 四道闸管线（D-0012 §4.2）：身份 → 幂等 → 阶段 → 合法性，全过才进内核。
/// </summary>
/// <remarks>
/// <para>
/// **顺序说明**：幂等闸紧跟身份闸、放在阶段闸之前——重复投递必须返回首次结果，
/// 而阶段闸会看到"请求已了结"并把重复命令误判成非法；先重放回执才能满足
/// 「同一命令重复投递只生效一次、第二次返回同一结果」。其余顺序与 §4.2 一致。
/// </para>
/// <para>
/// 任何一闸不过：拒绝 + 记录 + 不改状态。拒绝日志由调用方（GameSession）统一写出，
/// 带完整上下文（谁、哪一闸、什么输入）。
/// </para>
/// </remarks>
public static class CommandGatePipeline
{
    /// <summary>按顺序跑四道闸。</summary>
    public static GateDecision Evaluate(
        CommandEnvelope envelope,
        StepMachineState? machine,
        CommandReceipt? receipt)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var identity = CheckIdentity(envelope);
        if (identity is not null)
        {
            return GateDecision.Reject(identity);
        }

        if (receipt is not null)
        {
            return GateDecision.Duplicate(receipt);
        }

        var phase = CheckPhase(envelope, machine);
        if (phase is not null)
        {
            return GateDecision.Reject(phase);
        }

        var legality = CheckLegality(envelope, machine);
        if (legality is not null)
        {
            return GateDecision.Reject(legality);
        }

        return GateDecision.Pass();
    }

    private static CommandRejection? CheckIdentity(CommandEnvelope envelope)
    {
        var actor = envelope.Actor;
        return envelope.Command switch
        {
            StartPhaseCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null,
            StartPhaseCommand => Reject(
                "identity.host_only",
                "只有宿主或说书人可以开启新阶段",
                "identity"),

            SubmitResponseCommand when actor.Kind == ActorKind.Player && actor.Seat is not null => null,
            SubmitResponseCommand => Reject(
                "identity.player_only",
                "只有玩家本人可以提交响应",
                "identity"),

            SlotQuotaElapsedCommand when actor.Kind == ActorKind.System => null,
            SlotQuotaElapsedCommand => Reject(
                "identity.system_only",
                "配额输入只能由系统节拍器发出",
                "identity"),

            RebuildRoomCommand when actor.Kind is ActorKind.Storyteller or ActorKind.Host => null,
            RebuildRoomCommand => Reject(
                "identity.storyteller_only",
                "只有说书人或宿主可以重建房间",
                "identity"),

            _ when actor.Kind == ActorKind.Storyteller => null,
            _ => Reject("identity.storyteller_only", "这条命令只有说书人可以发出", "identity"),
        };
    }

    private static CommandRejection? CheckPhase(CommandEnvelope envelope, StepMachineState? machine)
    {
        switch (envelope.Command)
        {
            case SubmitResponseCommand:
                if (machine is null)
                {
                    return Reject("phase.not_started", "本局还没有开始任何阶段", "phase");
                }

                var pending = machine.PendingRequest;
                if (pending is null)
                {
                    return Reject("phase.no_pending_request", "当前没有等待响应的请求", "phase");
                }

                if (pending.Status != OperationRequestStatus.Pending)
                {
                    return Reject("phase.request_resolved", "这条请求已经了结（已响应或已作废）", "phase");
                }

                if (envelope.Actor.Seat != pending.Addressee)
                {
                    return Reject(
                        "phase.not_your_request",
                        $"这条请求是给座位 {pending.Addressee} 的",
                        "phase");
                }

                return null;

            case SlotQuotaElapsedCommand:
                if (machine is null)
                {
                    return Reject("phase.not_started", "本局还没有开始任何阶段", "phase");
                }

                if (machine.IsPlanCompleted)
                {
                    return Reject("phase.phase_completed", "本阶段已走完", "phase");
                }

                return null;

            case RebuildRoomCommand:
                return null;

            case StartPhaseCommand:
                if (machine is not null && !machine.IsPlanCompleted)
                {
                    return Reject(
                        "phase.phase_running",
                        "当前阶段还没有走完；请先推进、强推或重建，不要静默丢弃挂起",
                        "phase");
                }

                return null;

            default:
                if (machine is null)
                {
                    return Reject("phase.not_started", "本局还没有开始任何阶段", "phase");
                }

                return null;
        }
    }

    private static CommandRejection? CheckLegality(CommandEnvelope envelope, StepMachineState? machine) =>
        envelope.Command switch
        {
            SubmitResponseCommand submit => CheckOption(machine, submit.RequestId, submit.OptionValue),
            ProxyFillCommand proxy => CheckOption(machine, proxy.RequestId, proxy.OptionValue),
            VoidRequestCommand voidRequest => Enum.IsDefined(voidRequest.Reason)
                ? null
                : Reject("legality.reason_invalid", $"未知作废原因：{voidRequest.Reason}", "legality"),
            _ => null,
        };

    private static CommandRejection? CheckOption(
        StepMachineState? machine,
        OperationRequestId requestId,
        string optionValue)
    {
        var pending = machine?.PendingRequest;
        if (pending is null || pending.Id != requestId)
        {
            return Reject("legality.request_not_current", $"当前挂起的不是 {requestId}", "legality");
        }

        return pending.Prompt.Options.Any(
            option => string.Equals(option.Value, optionValue, StringComparison.Ordinal))
            ? null
            : Reject("legality.option_not_legal", $"选项不在合法集合里：{optionValue}", "legality");
    }

    private static CommandRejection Reject(string code, string message, string gate) =>
        new() { Code = code, Message = message, Gate = gate };
}
