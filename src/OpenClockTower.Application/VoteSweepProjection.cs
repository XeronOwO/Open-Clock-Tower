using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 钟盘收票的呈现投影：把内核里的收票状态 + 控制面锚点折算成相位 / 当前席位 / 剩余毫秒。
/// </summary>
/// <remarks>
/// 时间只在应用层出现（D-0008）：这里用调用方给的 <c>now</c> 与 <c>startedAt</c> 做差，
/// 不读时钟；判定完全不看这些量。锚点为空 = 中断（服务端重启 / 重建后不追补），等待说书人继续。
/// </remarks>
public static class VoteSweepProjection
{
    /// <summary>折算一次提名的收票呈现；没有提名或没有开始收票时为 null。</summary>
    public static VoteSweepView? Build(NominationRecord? nomination, DateTimeOffset now, DateTimeOffset? startedAt)
    {
        if (nomination?.Sweep is not { } sweep)
        {
            return null;
        }

        var collected = sweep.Collected.Select(vote => vote.Seat).ToArray();
        if (sweep.IsComplete)
        {
            return new VoteSweepView
            {
                Phase = "AwaitingCount",
                CurrentSeat = null,
                Collected = collected,
                CountdownMilliseconds = sweep.CountdownMilliseconds,
                IntervalMilliseconds = sweep.IntervalMilliseconds,
                NextBeatMilliseconds = null,
            };
        }

        if (startedAt is not { } start)
        {
            return new VoteSweepView
            {
                Phase = "Interrupted",
                CurrentSeat = null,
                Collected = collected,
                CountdownMilliseconds = sweep.CountdownMilliseconds,
                IntervalMilliseconds = sweep.IntervalMilliseconds,
                NextBeatMilliseconds = null,
            };
        }

        // 负差（时钟回拨）按 0 处理：宁可停在倒计时起点，也不给越界的剩余时间。
        var elapsed = Math.Max(0, (now - start).TotalMilliseconds);
        if (elapsed < sweep.CountdownMilliseconds)
        {
            return new VoteSweepView
            {
                Phase = "Countdown",
                CurrentSeat = null,
                Collected = collected,
                CountdownMilliseconds = sweep.CountdownMilliseconds,
                IntervalMilliseconds = sweep.IntervalMilliseconds,
                NextBeatMilliseconds = (long)Math.Round(sweep.CountdownMilliseconds - elapsed),
            };
        }

        // 第 k 席（1 起）在 开始 + 倒计时 + k × 间隔 到点；已收 n 席 → 下一席是第 n+1 席。
        var nextNumber = sweep.Collected.Count + 1;
        var due = sweep.CountdownMilliseconds + (nextNumber * (double)sweep.IntervalMilliseconds);
        return new VoteSweepView
        {
            Phase = "Collecting",
            CurrentSeat = sweep.NextSeat?.Value,
            Collected = collected,
            CountdownMilliseconds = sweep.CountdownMilliseconds,
            IntervalMilliseconds = sweep.IntervalMilliseconds,
            NextBeatMilliseconds = (long)Math.Round(Math.Max(0, due - elapsed)),
        };
    }
}
