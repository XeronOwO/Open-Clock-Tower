using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 洗脑师要求的到期触发器：下一个黎明（第 D 天开始）撤下施加夜早于本夜的要求。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《洗脑师》· 2026-10-01 抓取 · 提示标记——「移除时机：在下一个夜晚的黎明时，
/// 或洗脑师死亡或离场时」；· 提示标记 15——洗脑师第二夜行动转移标记时，
/// 「上一夜洗脑师选择的玩家仍然还处于疯狂状态」直到黎明。口径见
/// <c>docs/standard/rulings.md</c> R-0021：施加夜的次日白天与其后夜晚有效，下一个黎明撤下。
/// </para>
/// <para>
/// 到期判定落在 <see cref="DayStartedEvent"/> 上（一条规则一个触发点）：要求记录的是**绝对到期日**
/// （<see cref="MadnessRequirement.ExpiresAtDay"/>），因此这里不解析任何计划标签、也不依赖步骤机状态。
/// </para>
/// <para>
/// **幂等且无副作用**：只读账、产出撤下事件；已经被撤下的要求不再出现在账上的"未撤下"集合里，
/// 级联重复求值不会产出第二条（同 <see cref="WitchCurseTrigger"/> 的姿态）。
/// </para>
/// </remarks>
internal sealed class CerenovusRequirementTrigger : IEventTrigger
{
    /// <inheritdoc />
    public AbilityId Ability => CerenovusAbility.MadnessAbility;

    /// <inheritdoc />
    public IReadOnlyList<GameEvent> Evaluate(EventTriggerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 一批里可能有多条 DayStartedEvent（重建 / 补批）：按**最晚**的那天一次性到期，
        // 不取第一条就返回——否则后面的天会被静默漏掉，账上留着过期要求继续可罚。
        var latestDay = 0;
        var sawDayStarted = false;
        foreach (var started in context.Events.OfType<DayStartedEvent>())
        {
            sawDayStarted = true;
            latestDay = Math.Max(latestDay, started.DayNumber);
        }

        if (!sawDayStarted)
        {
            return [];
        }

        return
        [
            .. context.State.LiveRequirements
                .Where(requirement =>
                    requirement.Ability == CerenovusAbility.MadnessAbility
                    && requirement.ExpiresAtDay is { } expiresAt
                    && expiresAt <= latestDay)
                .Select(requirement => (GameEvent)new MadnessRequirementTerminatedEvent
                {
                    Id = requirement.Id,
                    Termination = new EffectTermination
                    {
                        Kind = EffectTerminationKind.NoLongerApplies,
                        Reason = $"黎明：洗脑师的要求只覆盖施加夜的次日白天与其后夜晚，"
                            + $"第 {requirement.ExpiresAtDay} 天开始即撤下（R-0021；百科《洗脑师》提示标记的移除时机）",
                    },
                }),
        ];
    }
}
