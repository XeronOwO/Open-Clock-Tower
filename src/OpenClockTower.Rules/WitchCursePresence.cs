using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 女巫诅咒的存续条件：只剩三名存活玩家（或女巫已不在场 / 已死亡）时，能力失去，诅咒立即解除。
/// </summary>
/// <remarks>
/// 来源：百科《女巫》· 2026-10-01 抓取 · 角色简介 2——「只剩三名玩家存活时，女巫的诅咒立即解除，
/// 女巫也无法再进行夜晚行动」；范例 4 给的正是"恶魔杀人后只剩三人 → 诅咒被移除"的场景。
/// 判定与提示、触发共用 <see cref="WitchAbility.InForce"/>：一份条件、三处一致。
/// <para>
/// **幂等且无副作用**：只读账、只回答"还在不在"；终止哪些效果由
/// <see cref="SettlementReconciler"/> 统一收口，且只挑未终止的那些（不会重复终止）。
/// </para>
/// </remarks>
internal sealed class WitchCursePresence : IAbilityPresence
{
    /// <inheritdoc />
    public AbilityId Ability => WitchAbility.CurseAbility;

    /// <inheritdoc />
    public bool? IsPresent(AbilityPresenceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return WitchAbility.InForce(context.State, context.Seats);
    }
}
