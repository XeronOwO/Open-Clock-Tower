using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 理发师能力的共同口径：角色 / 能力标识与文案（触发器与结算共用一份，避免两处漂移）。
/// </summary>
/// <remarks>
/// 来源（均为 2026-10-01 抓取）：百科《理发师》· 角色能力——「如果你死亡，在当晚恶魔可以选择两名玩家
/// （不能选择其他恶魔）交换角色」；· 提示标记「今晚理发」——放置条件「理发师死亡且此时未醉酒中毒」、
/// 移除时机「能力触发、恶魔执行交换（或放弃）后」；· 运作方式——唤醒一名恶魔玩家、
/// 恶魔摇头 = 不交换、不能选择另一名恶魔、可选自己与已死亡玩家；《死亡触发能力》· 能力简介——
/// 死亡时立即触发，涉及交互的效果等到夜晚。平台口径见 <c>docs/standard/rulings.md</c> R-0033。
/// </remarks>
internal static class BarberAbility
{
    /// <summary>理发师的角色标识。</summary>
    internal static readonly CharacterId Character = new("barber");

    /// <summary>「恶魔交换两名玩家的角色」的能力标识（操作请求来源与归因用）。</summary>
    internal static readonly AbilityId SwapAbility = new("barber.swap");

    /// <summary>
    /// 换角说明（写进 <see cref="SeatStateChangedEvent.Reason"/>）；
    /// 机器可读归因在 <c>CausedBy</c>（理发师席位）上。
    /// </summary>
    internal static string SwapReason(SeatId barber, SeatId demon) =>
        $"理发师死亡触发：{demon.Value} 号恶魔交换两名玩家的角色（阵营不变；理发师为 {barber.Value} 号；"
        + "百科《理发师》· 2026-10-01 抓取 · 角色能力）";
}
