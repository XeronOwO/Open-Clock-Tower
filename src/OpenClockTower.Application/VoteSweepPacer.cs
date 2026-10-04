using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 钟盘收票的控制面节拍器：按服务端时钟把「第 k 席到点」翻译成一条系统命令（R-0017 目标形态）。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="SlotQuotaPacer"/> 同一个挂点（<see cref="GameSession.TickAsync"/>）：时间只在应用层，
/// 内核只收显式输入（D-0008）。没到点、已收完、没有锚点（中断等待说书人继续）时都不产出输入。
/// </para>
/// <para>
/// 幂等键按「计划 + 提名 + 席位 + 这一次收票的锚点序号」区分：同一席的重试返回同一份回执，
/// 不会重复收票；继续收票会换锚点序号，后续席位各自有自己的键。
/// </para>
/// </remarks>
internal static class VoteSweepPacer
{
    /// <summary>构造本次心跳的收票输入；不该收时返回 null。</summary>
    internal static CommandEnvelope? TryBuild(
        StepMachineState? machine,
        SessionTrackers trackers,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trackers);

        if (machine is not { } current
            || current.Day?.OpenDay?.OpenNomination is not { Sweep: { } sweep } nomination
            || sweep.IsComplete
            || sweep.NextSeat is not { } nextSeat)
        {
            return null;
        }

        // 锚点为空 = 收票已中断（重启 / 重建后不追补）：等说书人点「继续收票」。
        if (trackers.VoteSweepStartedAt is not { } startedAt)
        {
            return null;
        }

        // 第 k 席（1 起）在 开始 + 倒计时 + k × 间隔 到点；已收 n 席 → 下一席是第 n+1 席。
        var nextNumber = sweep.Collected.Count + 1;
        var due = startedAt
            + TimeSpan.FromMilliseconds(sweep.CountdownMilliseconds + (nextNumber * (double)sweep.IntervalMilliseconds));
        if (now < due)
        {
            return null;
        }

        return new CommandEnvelope
        {
            Command = new CollectSeatVoteCommand
            {
                NominationIndex = nomination.Index,
                Seat = nextSeat,
            },
            Actor = Actor.System,
            IdempotencyKey = $"vote-seat:{current.Plan.Label}:{nomination.Index}:{nextSeat.Value}:"
                + $"{trackers.VoteSweepEntrySequence ?? 0}",
        };
    }
}
