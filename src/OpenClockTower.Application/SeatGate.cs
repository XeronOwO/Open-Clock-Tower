using OpenClockTower.Kernel;
using static OpenClockTower.Application.GateRejections;

namespace OpenClockTower.Application;

/// <summary>
/// 席位合法性的共用原语：席位必须在本局席位名单里（会话信息；客户端声明不可信，D-0012）。
/// </summary>
/// <remarks>
/// 从 <see cref="CommandGatePipeline"/> 拆出（单文件 600 行门禁），同时收编钟盘收票门里的同款检查：
/// "席位存不存在"只在这里判断，各命令族的门只关心自己那条规则。
/// </remarks>
internal static class SeatGate
{
    /// <summary>席位属于本局？没有会话信息或席位不在名单里都给出显式拒绝。</summary>
    public static CommandRejection? CheckExists(SeatId seat, GameSetup? setup) =>
        setup is null
            ? Reject("legality.setup_missing", "本局还没有会话信息（席位名单）", "legality")
            : setup.Seats.Any(item => item.Seat == seat)
                ? null
                : Reject("legality.seat_unknown", $"席位 {seat.Value} 不在本局席位名单里", "legality");
}
