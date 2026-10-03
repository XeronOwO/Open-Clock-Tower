using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>心上人能力的常量与文案（死亡触发：死亡时由说书人选择一名玩家持续醉酒）。</summary>
/// <remarks>
/// 来源：百科《心上人》· 2026-10-01 抓取 · 角色能力 / 角色简介 / 运作方式 / 提示标记；
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0039。
/// </remarks>
internal static class SweetheartAbility
{
    /// <summary>角色标识。</summary>
    internal static readonly CharacterId Character = new("sweetheart");

    /// <summary>死亡触发的能力标识（效果归因 / 裁定点幂等）。</summary>
    internal static readonly AbilityId DeathAbility = new("sweetheart");

    /// <summary>本局唯一的效果标识（来源 = 该心上人席位；效果持续到来源离场）。</summary>
    internal static EffectId DrunkEffectId(SeatId sweetheart) => new($"sweetheart:{sweetheart.Value}:drunk");

    /// <summary>触发型裁定点的稳定标识（死亡时派生一次，挂起 / 认领 / 重放都按它）。</summary>
    internal static DecisionPointId DecisionIdFor(SeatId sweetheart) => new($"sweetheart:{sweetheart.Value}");
}
