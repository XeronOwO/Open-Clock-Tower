namespace OpenClockTower.Kernel;

/// <summary>
/// 角色能力结算契约：把「玩家选完了 / 说书人裁完了」变成落地的事件。
/// </summary>
/// <remarks>
/// <para>
/// 与 <c>OpenClockTower.Rules.INightAction</c>（提示契约）成对：那个只声明「要让谁做什么选择」，
/// 这个负责「选择做完之后发生什么」。两者都由角色实现提供；建表时只把**角色 slug**写进槽位，
/// 结算时再按 slug 从注入的目录里取契约——计划要能随事件 JSON 往返，行为对象不进计划。
/// </para>
/// <para>
/// 硬约束（D-0002）：契约**不许**自己选目标、自己摇随机数、自己决定信息真假；
/// 需要裁量时返回裁定点提示（<see cref="BuildPostChoiceDecision"/>），由说书人拍板。
/// </para>
/// <para>
/// **契约义务**：<see cref="Resolve"/> 产出效果类事件前必须检查
/// <see cref="AbilityResolutionContext.Outcome"/>——中毒 / 醉酒 / 死亡时能力不生效，
/// 不得落任何效果（百科《重要细节》三-3）。信息类能力是例外：未生效时仍由说书人给出
/// （可能为假的）信息，平台只记录与提示、不判定真假。
/// </para>
/// </remarks>
public interface IAbilityResolution
{
    /// <summary>本契约对应的角色。</summary>
    CharacterId Character { get; }

    /// <summary>本契约对应的能力标识（进两本账）。</summary>
    AbilityId Ability { get; }

    /// <summary>
    /// 玩家已经作出选择之后，是否还需要说书人再裁定一次（信息类能力需要）。
    /// 返回 null = 直接按 <see cref="Resolve"/> 结算。
    /// </summary>
    ChoicePrompt? BuildPostChoiceDecision(AbilityResolutionContext context);

    /// <summary>产出本步的事件（效果 / 信息 / 状态变化）；结算结论本身由调用方记录。</summary>
    IReadOnlyList<GameEvent> Resolve(AbilityResolutionContext context);
}
