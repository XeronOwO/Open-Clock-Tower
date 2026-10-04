using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 说书人把一名旅行者移出本局（票据 `traveller-and-exile` D1；任意时刻）。
/// </summary>
/// <remarks>
/// 依据百科《旅行者》· 2026-10-04 抓取 · 旅行者运作方式：离开 = 在魔典中移除角色标记、
/// 在城镇广场移除生命标记；席位与票据保留（重连 / 复盘语义不动），但不再计入任何人数口径
/// （`rulings.md` R-0044 第 6 条）。
/// </remarks>
public sealed record RemoveTravellerCommand : GameCommand
{
    /// <summary>离场的旅行者席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>说书人给出的离场说明（审计 / 复盘追因；可空）。</summary>
    public string? Note { get; init; }
}
