using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 「在局座次」派生（票据 `traveller-and-exile` D1）：会话席位名单 **减去** 离场账，
/// 顺序按席位号升序；离场者不进任何人数口径（`rulings.md` R-0044 第 6 条）。
/// </summary>
public sealed class InGameSeatsTests
{
    /// <summary>离场席位被剔除，剩下的按席位号升序（与名单里的顺序无关）。</summary>
    [Fact]
    public void Derive_ExcludesDepartedSeats_AndSortsBySeatNumber()
    {
        var setup = Setup(3, 1, 2, 5);
        var state = GameState.Empty with { DepartedSeats = [new SeatId(2)] };

        var seats = InGameSeats.Derive(setup, state);

        Assert.Equal(new[] { new SeatId(1), new SeatId(3), new SeatId(5) }, seats);
    }

    /// <summary>没有离场账 = 全部席位都在局（开局缺省语义不变）。</summary>
    [Fact]
    public void Derive_WithoutDepartures_ReturnsAllSeatsAscending()
    {
        var seats = InGameSeats.Derive(Setup(2, 1, 4, 3), GameState.Empty);

        Assert.Equal(new[] { new SeatId(1), new SeatId(2), new SeatId(3), new SeatId(4) }, seats);
    }

    /// <summary>会话信息还没读到：返回空表——宁可少给、不猜。</summary>
    [Fact]
    public void Derive_WithoutSetup_ReturnsEmpty() =>
        Assert.Empty(InGameSeats.Derive(null, GameState.Empty with { DepartedSeats = [new SeatId(1)] }));

    private static GameSetup Setup(params int[] seatNumbers) => new()
    {
        GameId = new GameId("table-test"),
        Seats = [.. seatNumbers.Select(seat => new SeatTicket { Seat = new SeatId(seat), Ticket = $"seat-{seat}-test" })],
    };
}
