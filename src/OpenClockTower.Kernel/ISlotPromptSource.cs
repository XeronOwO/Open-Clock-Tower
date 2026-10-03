namespace OpenClockTower.Kernel;

/// <summary>
/// 说书人裁定类提示的**实时重建**来源：入槽时按当前账重算提示上下文。
/// </summary>
/// <remarks>
/// <para>
/// 计划里的提示是建表期冻结的快照：玩家操作请求因此不会被中途改写（选项契约已定）；
/// 但「说书人裁定点」没有对玩家的承诺，且它的上下文可能引用**当夜更早槽位**的结果
/// （如数学家的失效窗口）——冻结快照会天然过期，必须入槽时重建。
/// </para>
/// <para>
/// 由规则层实现（按角色 slug 取行动契约），内核只定义请求形状；没有来源
/// （内核夹具 / 只推进不结算）时保持快照，行为不变。
/// </para>
/// </remarks>
public interface ISlotPromptSource
{
    /// <summary>按当前账重建该槽位的提示；角色没有契约时返回 null（调用方保持快照）。</summary>
    ChoicePrompt? Rebuild(SlotPromptRequest request);
}
