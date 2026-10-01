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
}
