using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 会话读侧的查询数据面：把（快照 + 按接收者投影的补齐事件）拼成重连包。
/// </summary>
/// <remarks>
/// 从 <see cref="GameSession"/> 拆出（单文件 600 行门禁）：会话持有锁与状态，
/// 这里只做"读全量事件 → 逐条按接收者白名单投影 → 拼快照"的纯读取（D-0010 / D-0012 §4.3）。
/// **调用方持锁**：重连包是"快照 + 补齐"的同一份事实，不加锁会在并发提交时把新序号与旧事件拼在一起。
/// </remarks>
internal static class SessionQueries
{
    /// <summary>取重连包：快照视图 + 从 <paramref name="afterSequence"/> 起对该席位可见的事件。</summary>
    internal static async Task<ReconnectBundle> ReconnectBundleAsync(
        IGameStore store,
        GameId gameId,
        StepMachineState? machine,
        GameState state,
        IReadOnlyList<SeatId> seats,
        long sequence,
        SessionTrackers trackers,
        IReadOnlyList<SeatDisplayName> seatNames,
        DateTimeOffset now,
        SeatId seat,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        var all = await store.ReadEventsAsync(gameId, afterSequence: 0, cancellationToken);
        var addressees = PlayerEventProjection.AddresseeLookup(all);

        var events = new List<PlayerEvent>();
        foreach (var stored in all)
        {
            if (stored.Sequence <= afterSequence)
            {
                continue;
            }

            var playerEvent = PlayerEventProjection.ForSeat(stored, seat, addressees);
            if (playerEvent is not null)
            {
                events.Add(playerEvent);
            }
        }

        return new ReconnectBundle
        {
            Sequence = sequence,
            View = GameProjection.ForSeat(
                machine,
                state,
                seats,
                sequence,
                now,
                trackers.VoteSweepStartedAt,
                seat,
                trackers,
                seatNames),
            EventsSince = events,
        };
    }
}
