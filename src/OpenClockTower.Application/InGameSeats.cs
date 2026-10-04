using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 「在局座次」的统一构造（票据 `traveller-and-exile` D1）：会话席位名单 **减去** 内核离场账。
/// </summary>
/// <remarks>
/// <para>
/// 旅行者离场后席位票据与座位号仍然保留（重连 / 复盘语义不动），所以"谁还在局里"不能只看
/// <see cref="GameSetup.Seats"/>；离场事实记在 <see cref="GameState.DepartedSeats"/>。
/// 所有需要"本局座次"的调用点（结算上下文、胜负求值、投影、开夜 / 开白天建表）统一读这里，
/// 离场者不计入任何人数口径（`rulings.md` R-0044 第 6 条）。
/// </para>
/// <para>
/// 顺序按席位号升序（圆桌顺序）；会话信息还没读到时返回空表——宁可少给、不猜。
/// </para>
/// </remarks>
public static class InGameSeats
{
    /// <summary>从会话信息 + 当前账派生在局座次（席位号升序）。</summary>
    /// <param name="setup">会话信息（席位名单）；null = 还没读到，返回空表。</param>
    /// <param name="state">当前状态账（读离场账）。</param>
    public static IReadOnlyList<SeatId> Derive(GameSetup? setup, GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (setup is null)
        {
            return [];
        }

        var departed = state.DepartedSeats;
        return
        [
            .. setup.Seats
                .Select(ticket => ticket.Seat)
                .Where(seat => !departed.Contains(seat))
                .OrderBy(seat => seat.Value),
        ];
    }
}
