using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 咖啡师（旅行者）的标识与窗口查询：每晚由说书人二选一（「清醒且健康」或「行动两次」），
/// 两个效果都持续到下个黄昏。
/// </summary>
/// <remarks>
/// 来源：百科《咖啡师》· 2026-10-04 抓取 · 角色能力 / 角色简介 / 运作方式 / 提示标记 / 规则细节；
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0047（免疫窗口的账本语义）与
/// R-0052（两效果的平台收口：窗口分类、收口时点、「行动两次」的二次结算）。
/// </remarks>
internal static class BaristaAbility
{
    /// <summary>角色标识。</summary>
    internal static readonly CharacterId Character = new("barista");

    /// <summary>能力标识（能力使用账本 / 失效归因 / 窗口效果与收口匹配共用）。</summary>
    internal static readonly AbilityId Ability = new("barista");

    /// <summary>该席位此刻是否处于「清醒且健康」窗口；null = 窗口存在但生效与否判定不了（不猜）。</summary>
    internal static bool? ImmunityWindowOn(GameState state, SeatId seat) =>
        state.WindowOn(seat, EffectWindowKind.AfflictionImmunity);

    /// <summary>该席位此刻是否处于「行动两次」窗口；null = 判定不了（不猜）。</summary>
    internal static bool? SecondActionWindowOn(GameState state, SeatId seat) =>
        state.WindowOn(seat, EffectWindowKind.SecondAction);

    /// <summary>这条持续型效果是不是咖啡师的窗口（收口触发器按它挑选要终止的效果）。</summary>
    internal static bool IsWindowEffect(PersistentEffect effect) =>
        effect.Ability == Ability
        && effect.Window is EffectWindowKind.AfflictionImmunity or EffectWindowKind.SecondAction;
}
