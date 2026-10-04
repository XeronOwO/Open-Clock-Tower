namespace OpenClockTower.Kernel;

/// <summary>
/// 槽位激活与重绑的折叠：空槽位 → 行动槽位；行动槽位 → 换手重绑 / 「本夜无行动」重开。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="StepMachineFolder"/> 拆出（单文件 600 行门禁）：那边管「按事件推进步骤机」，
/// 这里只回答「一条 <see cref="SlotActivatedEvent"/> 如何改变计划」。只有**尚未进入**的槽位可以被处理
/// （过时不候，《隐性规则汇总》§6）；顺序损坏一律显式抛错（恢复必须失败，不许静默继续）。
/// </para>
/// <para>
/// 口径：激活 / 换手重绑见 <c>docs/standard/rulings.md</c> R-0030 第 6 条与 R-0032；
/// 「本夜无行动」格的重开（咖啡师「行动两次」让已用满「每局限一次」的角色重新可用）见 R-0052 第 3 条。
/// </para>
/// </remarks>
internal static class SlotActivationFolder
{
    /// <summary>
    /// 把一次槽位绑定折进计划：空槽位 → 激活成行动槽位；行动槽位 → 换手重绑（换行动者与提示），
    /// 或同一行动者的「本夜无行动」格重开（提示尚无合法选项时）。
    /// </summary>
    /// <remarks>
    /// 顺序损坏一律显式抛错：处理一个已经走过的槽位、下标越界、槽位标识对不上、
    /// 行动槽位重复绑定到同一行动者（提示已经开着选项）、或者绑定的不是角色槽位，都是事件流损坏。
    /// </remarks>
    internal static StepMachineState Apply(StepMachineState? state, SlotActivatedEvent activated)
    {
        var current = StepMachineFolder.Require(state, activated);
        if (activated.SlotIndex <= current.SlotIndex)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：槽位 {activated.SlotId.Value} 已经进入过（当前下标 {current.SlotIndex}），不能再激活");
        }

        if (activated.SlotIndex >= current.Plan.Slots.Count)
        {
            throw new InvalidOperationException($"事件流顺序损坏：激活下标 {activated.SlotIndex} 越界");
        }

        var slot = current.Plan.Slots[activated.SlotIndex];
        if (slot.Id != activated.SlotId)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：下标 {activated.SlotIndex} 是槽位 {slot.Id.Value}，不是 {activated.SlotId.Value}");
        }

        var slots = current.Plan.Slots.ToArray();
        slots[activated.SlotIndex] = slot.Kind switch
        {
            // 同一行动者、提示还没有合法选项：允许重开一次——计划期判成「本夜无行动」，
            // 更早的结算（咖啡师「行动两次」）让它重新可用（R-0052 第 3 条）。
            StepSlotKind.Action when slot.Actor == activated.Actor && slot.Prompt?.HasOptions != true
                => Rebind(slot, activated),

            StepSlotKind.Action when slot.Actor == activated.Actor => throw new InvalidOperationException(
                $"事件流顺序损坏：槽位 {activated.SlotId.Value} 的行动者没有变化，不能重复绑定"),

            // 换手重绑 / 激活：行动者与提示按新行动者重建，角色归属不变（R-0032）。
            // 行动者本人角色与槽位角色不同时（哲学家代行被获得角色的能力）构造代行槽位（R-0036）。
            StepSlotKind.Action or StepSlotKind.Empty => Rebind(slot, activated),

            _ => throw new InvalidOperationException(
                $"事件流顺序损坏：槽位 {activated.SlotId.Value} 不是角色槽位，不能被激活"),
        };

        return current with { Plan = current.Plan with { Slots = slots } };
    }

    /// <summary>
    /// 把一个角色槽位重新绑定给新行动者：行动者本人角色与槽位角色不同（哲学家代行被获得角色的能力，
    /// R-0036）时构造**代行槽位**，否则是普通的行动槽位（R-0032 的换手重绑 / 空槽位激活 / 重开）。
    /// </summary>
    private static StepSlot Rebind(StepSlot slot, SlotActivatedEvent activated) =>
        activated.ActorCharacter is { } actorCharacter
        && slot.Character is { } slotCharacter
        && actorCharacter != slotCharacter
            ? StepSlot.GrantedAction(
                slot.Id,
                activated.Actor,
                actorCharacter,
                activated.Prompt,
                activated.Dependencies,
                slotCharacter)
            : StepSlot.Action(
                slot.Id,
                activated.Actor,
                activated.Prompt,
                activated.Dependencies,
                slot.Character);
}
