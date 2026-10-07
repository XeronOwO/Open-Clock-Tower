using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 旅行者这条线：能力不能把玩家变成旅行者，也不能把旅行者变成非旅行者（R-0060）。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《旅行者》· 2026-10-04 抓取 · 旅行者运作方式——「一般来说，旅行者角色不能在游戏过程中
/// 变成非旅行者角色，非旅行者角色也不能在游戏过程中变成旅行者角色。如果有玩家尝试用自己的能力执行
/// 这样的操作，那么**对他摇头示意让他们重新进行选择**」；百科《哪些是“可以但不建议”》· 2026-10-04 抓取 ·
/// 基础规则部分「旅行者的角色转换」（同页写明"你也可以允许"属于需开局前告知的家规，默认口径不采用）。
/// </para>
/// <para>
/// 收口位置与其它候选表一致：**不合法的对象不进候选**，而不是让玩家选完再被拒——
/// 与筑梦师的「选择除你及旅行者以外的一名玩家」同款（百科《筑梦师》· 2026-10-04 抓取 · 角色能力）。
/// 角色**未观测**时按"不是旅行者"处理（不猜，D-0015）：候选集合的完整性由夜间建表（每席角色已观测）
/// 保证，这里只做类型过滤，不额外抛错。
/// </para>
/// </remarks>
internal static class TravellerBoundary
{
    /// <summary>这一席此刻是不是旅行者；该席角色未观测 → false（不猜，D-0015）。</summary>
    internal static bool IsTravellerSeat(GameState state, SeatId seat) =>
        state.Seat(seat)?.CharacterValue is { } character && IsTraveller(character);

    /// <summary>这个角色是不是旅行者类型；不在册的角色 → false。</summary>
    internal static bool IsTraveller(CharacterId character) =>
        SectsAndVioletsRoster.TypeOf(character) == CharacterType.Traveller;

    /// <summary>两名玩家是不是"同一条线上"：同为旅行者或同为非旅行者（理发师交换角色的合法条件）。</summary>
    internal static bool IsSameSide(GameState state, SeatId first, SeatId second) =>
        IsTravellerSeat(state, first) == IsTravellerSeat(state, second);
}
