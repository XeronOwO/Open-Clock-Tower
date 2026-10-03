using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 理发师请求的选项值格式：一次原子选择 = 合法玩家对（<c>pair:1+3</c>）或不交换（<c>decline</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 玩家对的编码 / 解析委托给 <see cref="PlayerPairChoice"/>（与贤者的当晚展示共用一份）；
/// 这里只保留理发师特有的「不交换」（恶魔摇头，百科《理发师》· 2026-10-01 抓取 · 运作方式）。
/// </para>
/// <para>
/// 选项值要能进事件流、能被重放与校验，格式与解析收在一处，
/// 免得产出方与消费方各写一份——写歪了就是「合法性闸放行、结算时解析不出来」。
/// </para>
/// </remarks>
internal static class BarberChoice
{
    /// <summary>「不交换」的选项值（恶魔摇头）。</summary>
    internal const string Decline = "decline";

    /// <summary>把一对席位编码成选项值（升序规范化）。</summary>
    internal static string FormatPair(SeatId first, SeatId second) =>
        PlayerPairChoice.FormatPair(first, second);

    /// <summary>解析玩家对；不是合法形状（含两个相同席位）时返回 null。</summary>
    internal static (SeatId First, SeatId Second)? ParsePair(string? value) =>
        PlayerPairChoice.ParsePair(value);

    /// <summary>是否为「不交换」。</summary>
    internal static bool IsDecline(string? value) =>
        string.Equals(value, Decline, StringComparison.Ordinal);
}
