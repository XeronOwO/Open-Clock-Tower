namespace OpenClockTower.Kernel;

/// <summary>
/// 一次选择契约的**受众**：同一个内核原语的两套投影（D-0011 / 架构 §2.7）。
/// </summary>
/// <remarks>
/// <para>
/// 受众决定「谁有资格作答」，不改变合法性校验与选项内容：<see cref="Actor"/> 投影成操作请求
/// 发给行动者；<see cref="Storyteller"/> 投影成说书人裁定点（<see cref="StepSlotEntry"/> 的
/// 入槽分支据此选路）。选项对说书人同样是**结构化候选**：说书人从候选中点选，
/// 值原样进 <see cref="AbilityResolutionContext.Decision"/>。
/// </para>
/// <para>
/// 首位消费者是咖啡师：其能力由说书人（而不是行动者本人）选择目标与效果
/// （百科《咖啡师》· 2026-10-04 抓取 · 角色简介；平台口径见 <c>docs/standard/rulings.md</c> R-0052）。
/// </para>
/// </remarks>
public enum ChoiceAudience
{
    /// <summary>行动者本人（默认）：投影成操作请求。</summary>
    Actor,

    /// <summary>说书人：投影成裁定点；选项是给说书人的结构化候选。</summary>
    Storyteller,
}
