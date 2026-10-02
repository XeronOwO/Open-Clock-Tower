using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 规则层的角色契约总目录：把「事件触发器」「能力存续」「处罚处决依据」三族契约的注册点收在一处。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="NightActions"/> 并列：那边的检索键是角色 slug（建表取提示、结算取契约，各一次），
/// 这里的契约由会话的结算管线按**本轮事件 + 当前账**统一求值，因此是**列表**而不是按键检索。
/// </para>
/// <para>
/// 规则语义只在这里注册一次，应用层不写 switch —— 与 <c>GameCommandDispatcher</c> 取
/// <see cref="NightActions.Default"/> 同族。
/// </para>
/// </remarks>
public static class RoleContracts
{
    /// <summary>事件触发器：按「本轮新事件 + 当前账」求后果（女巫的提名即死、洗脑师的到期撤下）。</summary>
    public static IReadOnlyList<IEventTrigger> EventTriggers { get; } =
        [new WitchCurseTrigger(), new CerenovusRequirementTrigger()];

    /// <summary>能力存续契约：会在特定局势下失去的能力，失去时它名下的持续型效果立即解除。</summary>
    public static IReadOnlyList<IAbilityPresence> AbilityPresences { get; } =
        [new WitchCursePresence()];

    /// <summary>处罚处决依据契约：说书人主动处决是否成立、死亡怎么归因（R-0020）。</summary>
    public static IReadOnlyList<IAdjudicatedExecutionSource> AdjudicatedExecutions { get; } =
        [new CerenovusMadnessPunishment(), new MutantMadnessPunishment()];
}
