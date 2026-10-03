using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 配额输入的构造：把服务端时钟（D-0013）翻译成一条系统命令——「到点了，推进当前槽位」。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="GameSession.TickAsync"/> 分工：会话管门与提交，这里只回答「这一拍有没有输入、
/// 输入长什么样」。缺起点信息（异常数据）时宁可不动，等说书人重建或强推。
/// </para>
/// <para>
/// **幂等键按「计划 + 槽位 + 本次进入」区分**（<see cref="SessionTrackers.SlotEntrySequence"/>）：
/// 触发格应答重进本格后要重新起算配额，只按「计划 + 槽位」做键会让第二次配额撞上收据、
/// 被当成重复命令回放，计划永久停在原地
/// （回归见 <c>BarberHostTests.BarberSwapAnsweredAfterQuotaElapsed_PlanStillAdvances</c>）。
/// </para>
/// </remarks>
internal static class SlotQuotaPacer
{
    /// <summary>
    /// 构造本次心跳的配额输入；不该推进时返回 null（接管模式 / 计划走完 / 本局已结束 /
    /// 配额不在跑 / 未到点 / 缺进入事件 / 白天窗口 / 没有当前槽位）。
    /// </summary>
    internal static CommandEnvelope? TryBuild(
        StepMachineState? machine,
        SessionTrackers trackers,
        DateTimeOffset now,
        TimeSpan slotQuota)
    {
        ArgumentNullException.ThrowIfNull(trackers);

        if (machine is null || machine.IsPlanCompleted || machine.Outcome is not null)
        {
            // 计划走完 / 本局已结束：节拍器没有可以推进的槽位（R-0024）。
            return null;
        }

        if (machine.Control != ControlMode.Automatic || machine.Quota != SlotQuotaState.Running)
        {
            // 接管模式不做任何自动推进（D-0014 能力 2）；配额已到点则等结清动作或被强推。
            return null;
        }

        if (trackers.SlotStartedAt is not { } startedAt
            || trackers.SlotEntrySequence is not { } entrySequence
            || now < startedAt + slotQuota)
        {
            // 缺起点信息（异常数据）时宁可不动；未到点同样不动。
            return null;
        }

        if (machine.CurrentSlot is not { } slot)
        {
            return null;
        }

        if (slot.Kind == StepSlotKind.DayWindow)
        {
            // 白天窗口不消耗配额：白天节奏由说书人掌握，没有节拍可送。
            return null;
        }

        return new CommandEnvelope
        {
            Command = new SlotQuotaElapsedCommand(),
            Actor = Actor.System,
            IdempotencyKey = $"slot-elapsed:{machine.Plan.Label}:{slot.Id}:{entrySequence}",
        };
    }
}
