namespace OpenClockTower.Kernel;

/// <summary>
/// 「持续型效果窗口」分类：效果存续期间改变**目标**身上另一种机制的结算方式。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="PersistentEffect.Dimension"/> 的分工：那个声明「这条效果压制目标的哪一格维度」，
/// 这个声明「这条效果在目标身上开启哪种窗口」——两者互不顶替，本族效果自己**不**压制维度。
/// </para>
/// <para>
/// 首位消费者是旅行者咖啡师的两个效果（百科《咖啡师》· 2026-10-04 抓取）：
/// 效果 1「清醒且健康」= 目标免受醉酒 / 中毒影响（<see cref="AfflictionImmunity"/>）；
/// 效果 2「行动两次」= 目标的能力在窗口内可以生效两次（<see cref="SecondAction"/>）。
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0047（免疫窗口的账本语义）与
/// R-0052（两效果的平台收口）；<see cref="SecondAction"/> 与「获得能力」族的二次获得语义
/// 另见 R-0053（Open）。
/// </para>
/// </remarks>
public enum EffectWindowKind
{
    /// <summary>
    /// 清醒且健康：窗口存续期间，目标身上压制维度的持续型效果一律挂起——标记照记、暂不生效，
    /// 窗口结束且效果仍在时按**同一 EffectId** 恢复（不是重新施加）。R-0047 第 1–3 条。
    /// </summary>
    AfflictionImmunity,

    /// <summary>
    /// 行动两次：窗口存续期间，目标的能力可以再结算一次（夜晚槽位重入 / 「每局限一次」上限放宽到 2）。
    /// 口径见 R-0052。
    /// </summary>
    SecondAction,
}
