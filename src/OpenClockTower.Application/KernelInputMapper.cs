using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 把上层的 <see cref="GameCommand"/> 翻译成内核输入。
/// </summary>
/// <remarks>
/// <para>
/// 这一层刻意只做**形状转换**，不含任何判定：四道闸在 <c>CommandGatePipeline</c>，
/// 领域判定在内核。翻译不认识某条命令时返回 null，由调用方按"未支持的命令"拒绝。
/// </para>
/// <para>
/// 说书人代填与玩家提交走同一个内核输入，只有 <see cref="ResponseSource"/> 不同——
/// "是谁做的这个决定"必须留在事件里（票据行 6）。
/// </para>
/// </remarks>
public static class KernelInputMapper
{
    /// <summary>翻译一条命令；未支持的命令返回 null。</summary>
    public static StepMachineInput? ToInput(GameCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command switch
        {
            SubmitResponseCommand submit => new SubmitResponseInput
            {
                RequestId = submit.RequestId,
                OptionValue = submit.OptionValue,
                Source = ResponseSource.Player,
            },
            ProxyFillCommand proxy => new SubmitResponseInput
            {
                RequestId = proxy.RequestId,
                OptionValue = proxy.OptionValue,
                Source = ResponseSource.StorytellerProxy,
                Note = proxy.Note,
            },
            VoidRequestCommand voidRequest => new VoidRequestInput
            {
                RequestId = voidRequest.RequestId,
                Reason = voidRequest.Reason,
                Note = voidRequest.Note,
            },
            ForceAdvanceCommand force => new ForceAdvanceInput { Reason = force.Reason },
            TakeOverCommand takeOver => new TakeOverInput { Reason = takeOver.Reason },
            ReleaseControlCommand release => new ReleaseControlInput { Reason = release.Reason },
            ResolveDecisionPointCommand resolve => new ResolveDecisionPointInput
            {
                DecisionPointId = resolve.DecisionPointId,
                Decision = resolve.Decision,
                Note = resolve.Note,
            },
            ApplySeatStateCommand seat => new SeatStateChangedInput
            {
                Seat = seat.Seat,
                Life = seat.Life,
                Character = seat.Character,
                Alignment = seat.Alignment,
                Drunk = seat.Drunk,
                Poison = seat.Poison,
                Reason = seat.Reason,
                CausedBy = seat.CausedBy,
            },
            SlotQuotaElapsedCommand => new SlotQuotaElapsedInput(),
            _ => null,
        };
    }
}
