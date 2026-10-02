namespace OpenClockTower.Kernel;

/// <summary>
/// 能力存续契约：某些角色的能力会在特定局势下**失去**，它产生的持续型效果必须随之立即解除。
/// </summary>
/// <remarks>
/// <para>
/// 首位消费者是女巫：百科《女巫》· 2026-10-01 抓取 · 角色简介——「只剩三名玩家存活时，
/// 女巫的诅咒立即解除」。能力一旦不在，账上遗留的「被诅咒」就是**假事实**：必须终止，
/// 而不是留着等它不生效。
/// </para>
/// <para>
/// 它只回答「还在不在」，不产事件：终止哪条效果由 <see cref="SettlementReconciler"/> 统一收口，
/// 与常驻效果共用同一个固定点、同一个写入方（D-0015）。判定不了时返回 null——**不猜**。
/// </para>
/// </remarks>
public interface IAbilityPresence
{
    /// <summary>本契约管理的能力。</summary>
    AbilityId Ability { get; }

    /// <summary>能力此刻是否仍在；null = 输入不全、判定不了（本次不解除任何效果）。</summary>
    bool? IsPresent(AbilityPresenceContext context);
}
