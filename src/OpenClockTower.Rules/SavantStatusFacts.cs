using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 候选事实库 D 组「状态读数」：此刻谁不清醒、谁的能力已经用尽（R-0057-C）。
/// </summary>
/// <remarks>
/// 「场上有玩家中毒」这类**否定式断言**（说成假 = 保证全场没人中毒）要求所有在局席位的该维度都
/// 观测齐；只有"确实有一个"才只需一条已知记录就能说真（不猜，D-0015）。
/// </remarks>
internal static class SavantStatusFacts
{
    /// <summary>分组名（说书人端分栏）。</summary>
    internal const string Group = "状态读数";

    /// <summary>本组全部事实（顺序 = 候选顺序）。</summary>
    internal static IReadOnlyList<SavantFactDefinition> All { get; } =
    [
        new()
        {
            Code = "poisoned-present",
            Group = Group,
            Evaluate = (world, _) => AnyPoisoned(world) is { } poisoned
                ? SavantFactEvaluation.Of(poisoned, "场上有玩家中毒")
                : null,
        },
        new()
        {
            Code = "drunk-present",
            Group = Group,
            Evaluate = (world, _) => AnyDrunk(world) is { } drunk
                ? SavantFactEvaluation.Of(drunk, "场上有玩家醉酒")
                : null,
        },
        new()
        {
            Code = "demon-impaired",
            Group = Group,
            Evaluate = (world, _) => world.SingleDemon is not { } demon
                ? null
                : world.State.Seat(demon) is { DrunkValue: { } drunk, PoisonValue: { } poison }
                    ? SavantFactEvaluation.Of(
                        drunk == DrunkState.Drunk || poison == PoisonState.Poisoned,
                        "恶魔目前醉酒或中毒")
                    : null,
        },
        new()
        {
            Code = "limited-ability-used",
            Group = Group,
            Evaluate = (world, _) => SavantFactEvaluation.Of(
                OnceAbilities.Registry.Any(registered =>
                    world.State.AbilityUses.Entries.Any(use => use.Ability == registered.Ability)),
                "有角色已经用尽它的限次能力"),
        },
    ];

    /// <summary>场上有人中毒吗；全都没观测齐时返回 null（说不清就不说）。</summary>
    private static bool? AnyPoisoned(SavantFactWorld world) =>
        AnyTick(world, entry => entry.PoisonValue is { } poison && poison == PoisonState.Poisoned,
            static entry => entry.PoisonValue is not null);

    /// <summary>场上有人醉酒吗；全都没观测齐时返回 null（说不清就不说）。</summary>
    private static bool? AnyDrunk(SavantFactWorld world) =>
        AnyTick(world, entry => entry.DrunkValue is { } drunk && drunk == DrunkState.Drunk,
            static entry => entry.DrunkValue is not null);

    /// <summary>六维度里某一维的"有人吗 / 全都观测过了吗"通用判定。</summary>
    private static bool? AnyTick(
        SavantFactWorld world,
        Func<SeatStateEntry, bool> holds,
        Func<SeatStateEntry, bool> observed)
    {
        var entries = world.Circle
            .Select(seat => world.State.Seat(seat))
            .OfType<SeatStateEntry>()
            .ToList();

        if (entries.Any(holds))
        {
            return true;
        }

        return entries.Count == world.Circle.Count && entries.All(observed) ? false : null;
    }
}
