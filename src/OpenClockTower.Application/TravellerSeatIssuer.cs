namespace OpenClockTower.Application;

/// <summary>
/// 旅行者加入的席位解析（票据 `traveller-and-exile` D1）：把"未指定席位"解析成服务端追加的新席位与票据。
/// </summary>
/// <remarks>
/// 只做纯解析（生成新席位对象），不落库——持久化、缓存刷新与提交失败补偿由 <see cref="GameSession"/>
/// 在同一把锁里编排。显式指定席位的加入不动会话信息（席位已在名单里），因此不需要签发。
/// </remarks>
internal static class TravellerSeatIssuer
{
    /// <summary>解析结果：解析后的命令、更新后的会话信息、追加之前的会话信息与签发的票据。</summary>
    internal sealed record Issue(
        JoinTravellerCommand Command,
        GameSetup Setup,
        GameSetup PreviousSetup,
        SeatTicket Ticket);

    /// <summary>把 <see cref="JoinTravellerCommand.Seat"/> 为空的加入解析成新席位；其余命令返回 null。</summary>
    internal static Issue? Resolve(GameSetup? setup, GameCommand command)
    {
        if (setup is null || command is not JoinTravellerCommand { Seat: null } join)
        {
            return null;
        }

        var (updated, ticket) = GameSetupFactory.AppendSeat(setup);
        return new Issue(join with { Seat = ticket.Seat }, updated, setup, ticket);
    }
}
