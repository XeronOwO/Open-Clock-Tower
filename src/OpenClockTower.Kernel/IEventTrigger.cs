namespace OpenClockTower.Kernel;

/// <summary>
/// 事件触发型能力的契约：某个角色的能力在**特定事件发生时**产生后果。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="IStandingEffectSource"/> 同族、分工不同：常驻来源回答「此刻应当存在哪些持续型效果」，
/// 触发器回答「刚刚发生的这些事件，按规则还要产生什么」。首位消费者是女巫的「被诅咒者发起提名即死」——
/// 本项目第一条「夜晚选择 → 白天触发」的链路；后续的 OnDeath / 处决触发家族（心上人 / 理发师 / 呆瓜）
/// 按同一族实现。
/// </para>
/// <para>
/// 实现只在规则层（<c>OpenClockTower.Rules</c>）：它读账、算后果，**不写账**——产出的事件由
/// <see cref="EventTriggerReconciler"/> 收口后与业务事件**同一次原子提交**落库（D-0010 / D-0008）。
/// </para>
/// <para>
/// **幂等要求**：编排方保证每次只把「本轮新产生的事件」交给触发器，但级联过程中同一条件仍可能被
/// 二次求值——触发器必须自己确认后果在当前账上是否成立（例如目标此刻是否还活着），
/// 重复产出会污染事件流。
/// </para>
/// </remarks>
public interface IEventTrigger
{
    /// <summary>本触发器管理的能力（归因与诊断用）。</summary>
    AbilityId Ability { get; }

    /// <summary>按本轮新产生的事件求值；没有后果就返回空列表（不返回 null）。</summary>
    IReadOnlyList<GameEvent> Evaluate(EventTriggerContext context);
}
