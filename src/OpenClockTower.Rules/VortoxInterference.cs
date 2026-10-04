using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 涡流的「镇民能力信息必假」约束（R-0028）：平台不生成信息内容，也不判定真假；
/// 但按 R-0004，这条约束要作为失效分类（<see cref="MalfunctionKind.Vortox"/>）进失效账本。
/// </summary>
/// <remarks>
/// 依据：百科《涡流》· 2026-10-01 抓取 · 角色简介 / 运作方式——「只要涡流存活，每当有镇民因为能力需要你
/// 提供信息，你就必须提供错误信息」「哪怕他们醉酒或中毒，信息也一定是错误的」；
/// 百科《获取信息》· 2026-10-01 抓取 · 错误信息的格式 3——「如果玩家的角色是镇民，则不论该玩家的能力
/// 为何种角色类型的能力，都会让该玩家的能力产生错误信息」。
/// </remarks>
internal static class VortoxInterference
{
    private static readonly CharacterId Vortox = new("vortox");

    /// <summary>
    /// 涡流是否在场且**能力仍在**（运作方式原文口径：只要涡流存活）——死亡但身处集骨者
    /// 「重获能力」窗口内的涡流按「仍握有能力」处理（R-0054）。
    /// </summary>
    internal static bool IsActive(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.Seats.Any(entry =>
            entry.CharacterValue == Vortox
            && entry.LifeValue is { } life
            && (life == LifeState.Alive
                || (life == LifeState.Dead && state.RegainedAbilityOn(entry.Seat) == true)));
    }

    /// <summary>
    /// 这个席位的信息能力此刻是否受涡流约束：涡流在场且该席位**本人**是镇民；
    /// 但处于咖啡师「清醒且健康」窗口内时不成立——「该玩家一定会获得正确信息，即使涡流在场」
    /// （百科《咖啡师》· 2026-10-04 抓取 · 角色简介；R-0047 第 4 条）。
    /// </summary>
    /// <remarks>
    /// 免疫窗口是否生效**判定不了**时不开覆盖（按涡流口径照常标「可能为假」）：不猜（D-0015 姿态）。
    /// </remarks>
    internal static bool IsActiveFor(GameState state, SeatId actor)
    {
        ArgumentNullException.ThrowIfNull(state);

        return IsActive(state)
            && BaristaAbility.ImmunityWindowOn(state, actor) is not true;
    }

    /// <summary>
    /// R-0004：本条信息能力是否因涡流留下失效记录——涡流存活、实施者**本人**的角色是镇民，
    /// 且实施者不在「清醒且健康」窗口内时返回 <see cref="MalfunctionKind.Vortox"/>，否则空列表。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="AbilityResolutionContext.ActorOwnCharacter"/> 而不是 <c>ActorCharacter</c>：
    /// 后者是结算契约的检索键（代行槽位上是"被获得的能力"），而《获取信息》第 3 条看的是**玩家本人的角色**。
    /// </remarks>
    internal static IReadOnlyList<MalfunctionKind> MalfunctionsFor(AbilityResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsActiveFor(context.State, context.Actor)
            || SectsAndVioletsRoster.TypeOf(context.ActorOwnCharacter) != CharacterType.Townsfolk)
        {
            return [];
        }

        return [MalfunctionKind.Vortox];
    }

    /// <summary>涡流在场时给信息结果加的说书人说明；不在场（或被咖啡师覆盖）返回兜底文案。</summary>
    internal static string? NoteFor(GameState state, SeatId actor, string? fallback)
    {
        ArgumentNullException.ThrowIfNull(state);

        return IsActiveFor(state, actor)
            ? "涡流在场：这条信息必须为假（百科《涡流》· 2026-10-01 抓取 · 运作方式）"
            : fallback;
    }
}
