using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 咖啡师窗口的收口触发器：**新的一夜开始**（下个黄昏）时，终止上一夜留下的全部咖啡师窗口。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《咖啡师》· 2026-10-04 抓取 · 提示标记——「移除时机：在黄昏时。在触发咖啡师下一晚的
/// 效果之前，将上一夜的所有咖啡师的标记都移除，包括『？』标记」；角色能力——「每个夜晚，
/// **直至下个黄昏**」。平台口径见 <c>docs/standard/rulings.md</c> R-0052 第 4 条。
/// </para>
/// <para>
/// 判定落在 <see cref="PhaseStartedEvent"/> 上（一条规则一个触发点）：只要本批开了新的夜晚阶段，
/// 就把账上仍存续的咖啡师窗口效果显式终止——**不管咖啡师本人此刻是什么状态**（标记移除是流程动作，
/// 不依赖当晚是否真的还能触发新效果；咖啡师死亡 / 离场时窗口已由来源失效路径提前终止）。
/// 幂等：已经终止的效果不在"仍存续"集合里，级联重复求值不会产出第二条。
/// </para>
/// <para>
/// 收口实现与集骨者窗口、开夜命令共用 <see cref="DuskExpiry"/>：开夜**建表之前**会先收口一次
/// （否则建表期读到的仍是上一夜的窗口），这里在结算管线里对同一批事件再求值也是幂等的。
/// </para>
/// </remarks>
internal sealed class BaristaWindowTrigger : IEventTrigger
{
    /// <inheritdoc />
    public AbilityId Ability => BaristaAbility.Ability;

    /// <inheritdoc />
    public IReadOnlyList<GameEvent> Evaluate(EventTriggerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return DuskExpiry.NightStarted(context.Events)
            ? DuskExpiry.Expire(context.State, BaristaAbility.IsWindowEffect)
            : [];
    }
}
