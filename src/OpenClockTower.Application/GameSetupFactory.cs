using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 生成一局的席位名单。
/// </summary>
/// <remarks>
/// <para>
/// 这里**不签发任何凭据**（D-0038）：邀请码由 <c>SeatInvitationService</c> 在说书人**要**
/// 的时候才签发（只存哈希、带有效期），席位名单只是"这一桌有几个位子"。
/// 从前这里顺手给每个席位生成一枚明文票据并落进目录——那是"没人要也先造一批长期凭据"，
/// 审计 G-A2-2 的另一半。
/// </para>
/// <para>
/// **说书人不在这里**（D-0027）：主持权归属开桌账号，由调用方在 <see cref="GameSetup.CreatedByAccountId"/>
/// 上写明，不生成任何凭据。
/// </para>
/// </remarks>
public static class GameSetupFactory
{
    /// <summary>生成一局 <paramref name="seatCount"/> 名玩家的会话信息，归属 <paramref name="createdBy"/>。</summary>
    public static GameSetup Create(GameId gameId, int seatCount, AccountId createdBy)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seatCount, 1);

        return new GameSetup
        {
            GameId = gameId,
            Seats = [.. Enumerable.Range(1, seatCount).Select(number => new SeatId(number))],
            CreatedByAccountId = createdBy,
        };
    }

    /// <summary>
    /// 为一个新加入的旅行者追加席位：席位号 = 当前最大席位号 + 1
    /// （D6 配板口径：非旅行者占低号席、旅行者以「追加席位」进入 = 高号席）。
    /// </summary>
    /// <remarks>
    /// 只生成对象，不落库——持久化与失败补偿由 <see cref="GameSession"/> 在命令提交的同一把锁里编排。
    /// 追加席位**不等于**签发邀请码：说书人随后为这个席位签发（D-0038），那条路只写凭据表。
    /// </remarks>
    public static (GameSetup Setup, SeatId Seat) AppendSeat(GameSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        var number = setup.Seats.Count == 0 ? 1 : setup.Seats.Max(item => item.Value) + 1;
        var seat = new SeatId(number);

        return (setup with { Seats = [.. setup.Seats, seat] }, seat);
    }
}
