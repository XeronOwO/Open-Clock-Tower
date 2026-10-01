using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 角色夜间行动契约：把「这一夜这个角色要给玩家什么选择」变成 <see cref="ChoicePrompt"/>。
/// </summary>
/// <remarks>
/// <para>
/// 契约只声明**提示**——上下文、合法选项、无合法选项时的行为（R-0009）。
/// 能力效果（状态变化、效果事件、信息下发）由结算引擎在后续票据里实现；
/// 本接口刻意不含"结算"。
/// </para>
/// <para>
/// 实现必须写清规则来源（百科页名 + 抓取日期 + 区域），依据本仓库第一原则，
/// 见 <c>AGENTS.md</c>「规则不许凭记忆写」。
/// </para>
/// </remarks>
public interface INightAction
{
    /// <summary>本契约对应的角色。</summary>
    CharacterId Character { get; }

    /// <summary>构造本步给行动者的选择契约。</summary>
    ChoicePrompt BuildPrompt(NightActionContext context);
}
