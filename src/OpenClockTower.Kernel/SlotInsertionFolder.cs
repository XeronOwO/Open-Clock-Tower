namespace OpenClockTower.Kernel;

/// <summary>
/// 槽位追加的折叠：把一个尚未进入的行动槽位插进计划（与 <see cref="SlotActivationFolder"/> 并列）。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="StepMachineFolder"/> 拆出（单文件 600 行门禁）：那边管「按事件推进步骤机」，
/// 这里只回答「一条 <see cref="SlotInsertedEvent"/> 如何改变计划」。
/// </para>
/// <para>
/// 顺序损坏一律显式抛错（恢复必须失败，不许静默继续）：插到已经走过 / 正在走的位置、下标越界、
/// 槽位标识与计划里已有的重复、插进来的不是角色行动格——四种都是事件流损坏。
/// </para>
/// </remarks>
internal static class SlotInsertionFolder
{
    /// <summary>把一次槽位追加折进计划。</summary>
    internal static StepMachineState Apply(StepMachineState? state, SlotInsertedEvent inserted)
    {
        var current = StepMachineFolder.Require(state, inserted);

        // 只允许未来位置：当前槽位与它之前的格子都已经走过（或正在走），回填会让历史与投影分叉。
        if (inserted.Index <= current.SlotIndex)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：追加下标 {inserted.Index} 不在当前槽位 {current.SlotIndex} 之后，"
                + "已经走过 / 正在走的位置不能回填槽位");
        }

        if (inserted.Index > current.Plan.Slots.Count)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：追加下标 {inserted.Index} 越界（计划共 {current.Plan.Slots.Count} 格）");
        }

        if (inserted.Slot.Kind != StepSlotKind.Action)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：追加的槽位 {inserted.Slot.Id.Value} 不是角色行动格"
                + $"（{inserted.Slot.Kind}），本事件只承载「唤醒某人结算能力」这一种追加");
        }

        foreach (var slot in current.Plan.Slots)
        {
            if (slot.Id == inserted.Slot.Id)
            {
                throw new InvalidOperationException(
                    $"事件流顺序损坏：槽位标识 {inserted.Slot.Id.Value} 已经在计划里（追加不得复用标识）");
            }
        }

        var slots = new List<StepSlot>(current.Plan.Slots);
        slots.Insert(inserted.Index, inserted.Slot);
        return current with { Plan = current.Plan with { Slots = slots } };
    }
}
