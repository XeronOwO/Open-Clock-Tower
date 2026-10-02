using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 涡流的「镇民能力信息必假」标注（R-0028）：平台不生成信息内容，也不判定真假。
/// </summary>
/// <remarks>
/// 依据：百科《涡流》· 2026-10-01 抓取 · 角色简介 / 运作方式——「只要涡流存活，每当有镇民因为能力需要你
/// 提供信息，你就必须提供错误信息」「哪怕他们醉酒或中毒，信息也一定是错误的」。
/// 平台能做的是：把这类信息标成"可能为假"（只说书人可见）并在说明里写明"必须为假 + 涡流在场"。
/// 引擎级干扰计数（<c>MalfunctionKind.Vortox</c>、数学家口径）仍随 R-0004，不在这里。
/// </remarks>
internal static class VortoxInterference
{
    private static readonly CharacterId Vortox = new("vortox");

    /// <summary>涡流是否在场且存活（运作方式原文口径：只要涡流存活）。</summary>
    internal static bool IsActive(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.Seats.Any(entry =>
            entry.CharacterValue == Vortox && entry.LifeValue == LifeState.Alive);
    }

    /// <summary>涡流在场时给信息结果加的说书人说明；不在场返回 null。</summary>
    internal static string? NoteFor(GameState state, string? fallback)
    {
        ArgumentNullException.ThrowIfNull(state);

        return IsActive(state)
            ? "涡流在场：这条信息必须为假（百科《涡流》· 2026-10-01 抓取 · 运作方式）"
            : fallback;
    }
}
