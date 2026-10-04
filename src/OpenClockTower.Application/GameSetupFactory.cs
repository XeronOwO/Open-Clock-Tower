using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 生成一局游戏的会话票据。
/// </summary>
/// <remarks>
/// 占位实现：票据只是随机字符串；连接绑定、私有凭据与负向测试属「零信任」票据。
/// </remarks>
public static class GameSetupFactory
{
    /// <summary>生成一局 <paramref name="seatCount"/> 名玩家的会话信息。</summary>
    public static GameSetup Create(GameId gameId, int seatCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seatCount, 1);

        var seats = Enumerable.Range(1, seatCount)
            .Select(number => new SeatTicket
            {
                Seat = new SeatId(number),
                Ticket = $"seat-{number}-{Guid.NewGuid():N}",
            })
            .ToArray();

        return new GameSetup
        {
            GameId = gameId,
            Seats = seats,
            StorytellerTicket = $"storyteller-{Guid.NewGuid():N}",
        };
    }

    /// <summary>
    /// 为一个新加入的旅行者追加席位并签发票据：席位号 = 当前最大席位号 + 1
    /// （D6 配板口径：非旅行者占低号席、旅行者以「追加席位」进入 = 高号席）。
    /// </summary>
    /// <remarks>
    /// 只生成对象，不落库——持久化与失败补偿由 <see cref="GameSession"/> 在命令提交的同一把锁里编排。
    /// </remarks>
    public static (GameSetup Setup, SeatTicket Ticket) AppendSeat(GameSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        var number = setup.Seats.Count == 0 ? 1 : setup.Seats.Max(item => item.Seat.Value) + 1;
        var ticket = new SeatTicket
        {
            Seat = new SeatId(number),
            Ticket = $"seat-{number}-{Guid.NewGuid():N}",
        };

        return (setup with { Seats = [.. setup.Seats, ticket] }, ticket);
    }
}
