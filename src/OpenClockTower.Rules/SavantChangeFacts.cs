using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 候选事实库 C 组「昨晚与今天」：变化类事实（R-0057-C）。
/// </summary>
/// <remarks>
/// <para>
/// 读的是 <see cref="SeatActivityLedger"/> 的两个窗口：`last-night` = 最近一个**已结束**的夜晚阶段，
/// `today` = 最近一次黎明之后。窗口口径与"变化前后都已知才算变化"的记账口径都登记在 R-0057-C。
/// </para>
/// <para>
/// 还没有结束过任何夜晚时，「昨晚」类事实一律判不了（不进候选）——说不清就不说。
/// </para>
/// </remarks>
internal static class SavantChangeFacts
{
    /// <summary>分组名（说书人端分栏）。</summary>
    internal const string Group = "昨晚与今天";

    /// <summary>本组全部事实（顺序 = 候选顺序）。</summary>
    internal static IReadOnlyList<SavantFactDefinition> All { get; } =
    [
        new()
        {
            Code = "death-last-night",
            Group = Group,
            Evaluate = (world, _) => LastNightClosed(world)
                ? SavantFactEvaluation.Of(
                    world.State.Activity.AnyLastNight(SeatActivityKind.Death),
                    "昨晚有玩家死亡")
                : null,
        },
        new()
        {
            Code = "character-changed-last-night",
            Group = Group,
            Evaluate = (world, _) => LastNightClosed(world)
                ? SavantFactEvaluation.Of(
                    world.State.Activity.AnyLastNight(SeatActivityKind.CharacterChange),
                    "昨晚有玩家的角色发生变化")
                : null,
        },
        new()
        {
            Code = "alignment-changed-last-night",
            Group = Group,
            Evaluate = (world, _) => LastNightClosed(world)
                ? SavantFactEvaluation.Of(
                    world.State.Activity.AnyLastNight(SeatActivityKind.AlignmentChange),
                    "昨晚有玩家的阵营发生变化")
                : null,
        },
        new()
        {
            Code = "malfunction-last-night",
            Group = Group,
            Evaluate = (world, _) => LastNightClosed(world)
                ? SavantFactEvaluation.Of(
                    world.State.Activity.AnyLastNight(SeatActivityKind.Malfunction),
                    "昨晚有玩家的能力未正常生效")
                : null,
        },
        new()
        {
            Code = "death-today",
            Group = Group,
            Evaluate = (world, _) => SavantFactEvaluation.Of(
                world.State.Activity.AnyToday(SeatActivityKind.Death),
                "今天有玩家死亡"),
        },
        new()
        {
            Code = "character-changed-today",
            Group = Group,
            Evaluate = (world, _) => SavantFactEvaluation.Of(
                world.State.Activity.AnyToday(SeatActivityKind.CharacterChange),
                "今天有玩家的角色发生变化"),
        },
        new()
        {
            Code = "execution-today",
            Group = Group,
            Evaluate = (world, _) => SavantFactEvaluation.Of(
                world.State.Activity.AnyToday(SeatActivityKind.Execution),
                "今天有人被处决"),
        },
    ];

    /// <summary>最近一个夜晚已经结束了吗——「昨晚」这话说得出口的前提（R-0057-C）。</summary>
    private static bool LastNightClosed(SavantFactWorld world) => world.State.Activity.LastNightEnd is not null;
}
