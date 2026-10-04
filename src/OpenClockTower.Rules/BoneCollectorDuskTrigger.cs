using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 集骨者重获窗口的收口触发器：**新的一夜开始**（下个黄昏）时，终止上一夜留下的全部
/// 「重获能力」窗口——被选中的玩家失去因集骨者而重新获得的能力。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《集骨者》· 2026-10-04 抓取 · 运作方式——「下一个黄昏，被选中的玩家会失去他的能力
/// ——移除『重获能力』提示标记」；· 提示标记——移除时机「在黄昏时，或集骨者死亡或离场时」。
/// 平台口径见 <c>docs/standard/rulings.md</c> R-0054 第 2 条。
/// </para>
/// <para>
/// 判定落在 <see cref="PhaseStartedEvent"/> 上（与咖啡师窗口同族，收口实现共用
/// <see cref="DuskExpiry"/>）：只要本批开了新的夜晚阶段，就把账上仍存续的重获窗口显式终止——
/// 不管集骨者此刻是什么状态。窗口终止的级联（被重获能力名下的效果一并终止）由
/// <see cref="GameStateMachine"/> 的折叠收口。集骨者死亡 / 离场时窗口由来源失效链路提前终止。
/// </para>
/// </remarks>
internal sealed class BoneCollectorDuskTrigger : IEventTrigger
{
    /// <inheritdoc />
    public AbilityId Ability => BoneCollectorAbility.RegainAbility;

    /// <inheritdoc />
    public IReadOnlyList<GameEvent> Evaluate(EventTriggerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return DuskExpiry.NightStarted(context.Events)
            ? DuskExpiry.Expire(context.State, BoneCollectorAbility.IsRegainEffect)
            : [];
    }
}
