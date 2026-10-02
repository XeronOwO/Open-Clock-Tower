using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 哲学家的「获得能力」在本夜的落格（R-0036）：能力在**谁的格上**执行。
/// </summary>
/// <remarks>
/// <para>
/// 建表期与结算期必须同源判定（"这一格属于谁"只有一处口径），因此收在这里。三条判据：
/// </para>
/// <list type="number">
/// <item><description>
/// 被获得角色在**本阶段**顺序表上是「角色行动」格——触发格（理发师）与不在表上的角色
/// 本夜没有可执行的行动；
/// </description></item>
/// <item><description>
/// 它此刻**没有存活持有者**（建表会给空槽位）：这一格交给获得者**代行**——
/// 当夜就能用上，首夜能力因此也照此落地（百科《哲学家》· 运作方式 8）；
/// </description></item>
/// <item><description>
/// 否则那一格仍归它的持有者（醉酒 → 能力不生效，照常被唤醒），获得者改在**自己的格**上代行
/// ——不把持有者从它自己的格里挤掉，避免"醉酒者没被唤醒"变成可观测信息（D-0013 §5 的姿态）。
/// </description></item>
/// </list>
/// </remarks>
internal static class PhilosopherBinding
{
    /// <summary>本夜的「获得能力」事实：获得者席位 + 被获得的角色；null = 还没获得（或它已终止）。</summary>
    internal static (SeatId Philosopher, CharacterId Granted)? Of(GameState state) =>
        PhilosopherAbility.FindGrant(state) is { GrantedCharacter: { } granted } grant
            ? (grant.Source, granted)
            : null;

    /// <summary>被获得角色的格今夜能否交给获得者代行：在表上是角色行动，且没有存活持有者。</summary>
    internal static bool GrantedSlotIsFree(
        GameState state,
        CharacterId granted,
        GamePhase phase,
        NightOrderVariant variant) =>
        HasActionOnPhase(granted, phase, variant) && !HasLivingHolder(state, granted);

    /// <summary>该角色在本阶段顺序表上是不是**角色行动**格（触发格不算行动）。</summary>
    internal static bool HasActionOnPhase(CharacterId character, GamePhase phase, NightOrderVariant variant) =>
        NightOrderTable.For(phase, variant)
            .Any(entry => entry.Character == character && entry.Kind == NightOrderEntryKind.CharacterAction);

    /// <summary>
    /// 该角色此刻有没有**存活**持有者；生死未观测时按"有"处理——不猜，
    /// 建表随后会用自己的口径（<c>plan.life_unobserved</c>）显式拒绝。
    /// </summary>
    private static bool HasLivingHolder(GameState state, CharacterId character) =>
        state.Seats.Any(entry =>
            entry.CharacterValue == character
            && entry.LifeValue != LifeState.Dead);
}
