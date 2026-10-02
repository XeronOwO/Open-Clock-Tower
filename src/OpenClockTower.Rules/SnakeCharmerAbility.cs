using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 舞蛇人能力的共同口径：角色 / 能力标识与交换后永久中毒的效果标识。
/// </summary>
/// <remarks>
/// 来源：百科《舞蛇人》· 2026-10-01 抓取 · 角色能力——「每个夜晚，你要选择一名存活玩家：
/// 如果你选中了恶魔，你和他交换角色和阵营，然后他中毒」；· 提示标记「中毒」——放置时机
/// 「舞蛇人成功触发自己的能力，并在与恶魔交换角色标记之后」、移除时机
/// 「放置有此标记的角色死亡或离场时」；同页附注「让这个角色中毒的效果是来自于这个角色本身」。
/// </remarks>
internal static class SnakeCharmerAbility
{
    /// <summary>舞蛇人的角色标识。</summary>
    internal static readonly CharacterId Character = new("snake-charmer");

    /// <summary>夜间行动的能力标识（进两本账）。</summary>
    internal static readonly AbilityId ActionAbility = new("snake-charmer");

    /// <summary>交换后施加给原恶魔的永久中毒：与夜间行动分开记（同女巫的诅咒）。</summary>
    internal static readonly AbilityId PoisonAbility = new("snake-charmer.poison");

    /// <summary>换角说明（写进 <see cref="SeatStateChangedEvent.Reason"/>）；机器可读归因在 <c>CausedBy</c> 上。</summary>
    internal const string SwapReason =
        "舞蛇人与恶魔交换角色与阵营（百科《舞蛇人》· 2026-10-01 抓取 · 运作方式）";

    /// <summary>永久中毒效果的标识：计划 + 槽位唯一（<c>sv:night-2:snake-charmer:poison</c>），重放稳定。</summary>
    internal static EffectId PoisonEffectId(string planLabel, StepSlotId slotId) =>
        new($"{planLabel}:{slotId}:poison");
}
